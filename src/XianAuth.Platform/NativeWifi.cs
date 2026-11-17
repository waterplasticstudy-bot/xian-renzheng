using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace XianAuth
{
    public sealed class WifiSnapshot
    {
        public string Ssid { get; set; }
        public Guid InterfaceId { get; set; }
        public IPAddress IPv4 { get; set; }
        public IPAddress Gateway { get; set; }
        public int InterfaceIndex { get; set; }
        public string Mac { get; set; }
    }

    public static class NativeWifi
    {
        const int CurrentConnectionOpcode = 7;
        const int ConnectedState = 1;

        public static List<WifiSnapshot> QueryAll()
        {
            var result = new List<WifiSnapshot>();
            IntPtr handle;
            uint negotiated;
            uint status = WlanOpenHandle(2, IntPtr.Zero, out negotiated, out handle);
            if (status != 0)
                throw new InvalidOperationException("无法打开无线接口，错误码 " + status);
            try
            {
                IntPtr list;
                status = WlanEnumInterfaces(handle, IntPtr.Zero, out list);
                if (status != 0)
                    throw new InvalidOperationException("无法枚举无线接口，错误码 " + status);
                try
                {
                    int count = Marshal.ReadInt32(list);
                    const int infoSize = 532;
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr info = IntPtr.Add(list, 8 + (i * infoSize));
                        int state = Marshal.ReadInt32(info, 528);
                        if (state != ConnectedState)
                            continue;
                        var id = (Guid)Marshal.PtrToStructure(info, typeof(Guid));
                        string ssid = ReadConnectedSsid(handle, id);
                        if (ssid == null)
                            continue;
                        var snap = new WifiSnapshot { Ssid = ssid, InterfaceId = id };
                        FillAddress(snap);
                        result.Add(snap);
                    }
                }
                finally
                {
                    WlanFreeMemory(list);
                }
            }
            finally
            {
                WlanCloseHandle(handle, IntPtr.Zero);
            }
            return result;
        }

        public static bool TryConnect(string ssid)
        {
            if (string.IsNullOrEmpty(ssid))
                return false;
            IntPtr handle;
            uint negotiated;
            if (WlanOpenHandle(2, IntPtr.Zero, out negotiated, out handle) != 0)
                return false;
            try
            {
                IntPtr list;
                if (WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0)
                    return false;
                try
                {
                    int count = Marshal.ReadInt32(list);
                    const int infoSize = 532;
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr info = IntPtr.Add(list, 8 + (i * infoSize));
                        var id = (Guid)Marshal.PtrToStructure(info, typeof(Guid));
                        if (ConnectInterface(handle, id, ssid))
                            return true;
                    }
                }
                finally
                {
                    WlanFreeMemory(list);
                }
            }
            finally
            {
                WlanCloseHandle(handle, IntPtr.Zero);
            }
            return false;
        }

        public static IPAddress WaitForIpv4(Guid interfaceId, int timeoutMs)
        {
            int start = Environment.TickCount;
            while (unchecked(Environment.TickCount - start) < timeoutMs)
            {
                List<WifiSnapshot> all = QueryAll();
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].InterfaceId == interfaceId && all[i].IPv4 != null)
                        return all[i].IPv4;
                }
                System.Threading.Thread.Sleep(500);
            }
            return null;
        }

        static bool ConnectInterface(IntPtr handle, Guid id, string ssid)
        {
            IntPtr profiles;
            Guid iface = id;
            if (WlanGetProfileList(handle, ref iface, IntPtr.Zero, out profiles) != 0)
                return false;
            try
            {
                int count = Marshal.ReadInt32(profiles);
                const int stride = 516;
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = IntPtr.Add(profiles, 8 + (i * stride));
                    string profileName = Marshal.PtrToStringUni(item);
                    if (string.IsNullOrEmpty(profileName))
                        continue;
                    if (!ProfileMatches(handle, iface, profileName, ssid) && !string.Equals(profileName, ssid, StringComparison.Ordinal))
                        continue;
                    var parameters = new WlanConnectionParameters
                    {
                        Mode = 0,
                        ProfileName = profileName,
                        Ssid = IntPtr.Zero,
                        BssidList = IntPtr.Zero,
                        BssType = 1,
                        Flags = 0
                    };
                    uint result = WlanConnect(handle, ref iface, ref parameters, IntPtr.Zero);
                    if (result == 0)
                    {
                        DailyLog.Info("已请求连接无线网络配置：" + profileName);
                        return true;
                    }
                }
            }
            finally
            {
                WlanFreeMemory(profiles);
            }
            return false;
        }

        static bool ProfileMatches(IntPtr handle, Guid iface, string profileName, string ssid)
        {
            IntPtr xmlPtr;
            Guid id = iface;
            if (WlanGetProfile(handle, ref id, profileName, IntPtr.Zero, out xmlPtr, IntPtr.Zero, IntPtr.Zero) != 0 || xmlPtr == IntPtr.Zero)
                return false;
            try
            {
                string xml = Marshal.PtrToStringUni(xmlPtr) ?? "";
                var name = Regex.Match(xml, @"<SSID>[\s\S]*?<name>([^<]*)</name>", RegexOptions.IgnoreCase);
                if (name.Success && string.Equals(name.Groups[1].Value, ssid, StringComparison.Ordinal))
                    return true;
                var hex = Regex.Match(xml, @"<SSID>[\s\S]*?<hex>([0-9A-Fa-f]+)</hex>", RegexOptions.IgnoreCase);
                if (!hex.Success)
                    return false;
                return string.Equals(DecodeHexSsid(hex.Groups[1].Value), ssid, StringComparison.Ordinal);
            }
            finally
            {
                WlanFreeMemory(xmlPtr);
            }
        }

        static string DecodeHexSsid(string hex)
        {
            if (hex.Length % 2 != 0)
                return "";
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return Encoding.UTF8.GetString(bytes);
        }

        static string ReadConnectedSsid(IntPtr handle, Guid id)
        {
            uint size;
            IntPtr data;
            Guid iface = id;
            uint status = WlanQueryInterface(handle, ref iface, CurrentConnectionOpcode, IntPtr.Zero, out size, out data, IntPtr.Zero);
            if (status != 0 || data == IntPtr.Zero)
                return null;
            try
            {
                int length = Marshal.ReadInt32(data, 520);
                if (length <= 0 || length > 32)
                    return "";
                var bytes = new byte[length];
                Marshal.Copy(IntPtr.Add(data, 524), bytes, 0, length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                WlanFreeMemory(data);
            }
        }

        static void FillAddress(WifiSnapshot snap)
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                Guid id;
                if (!Guid.TryParse(interfaces[i].Id, out id) || id != snap.InterfaceId)
                    continue;
                byte[] mac = interfaces[i].GetPhysicalAddress().GetAddressBytes();
                snap.Mac = BitConverter.ToString(mac).Replace("-", "");
                IPInterfaceProperties properties = interfaces[i].GetIPProperties();
                try { snap.InterfaceIndex = properties.GetIPv4Properties().Index; }
                catch (NetworkInformationException) { }
                foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
                {
                    if (gateway.Address != null && gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        snap.Gateway = gateway.Address;
                        break;
                    }
                }
                foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                {
                    if (address.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        continue;
                    byte[] bytes = address.Address.GetAddressBytes();
                    if (bytes[0] == 169 && bytes[1] == 254)
                        continue;
                    snap.IPv4 = address.Address;
                    return;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WlanConnectionParameters
        {
            public uint Mode;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string ProfileName;
            public IntPtr Ssid;
            public IntPtr BssidList;
            public uint BssType;
            public uint Flags;
        }

        [DllImport("wlanapi.dll")]
        static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);

        [DllImport("wlanapi.dll")]
        static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);

        [DllImport("wlanapi.dll")]
        static extern void WlanFreeMemory(IntPtr memory);

        [DllImport("wlanapi.dll")]
        static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);

        [DllImport("wlanapi.dll")]
        static extern uint WlanQueryInterface(IntPtr handle, ref Guid iface, int opcode, IntPtr reserved, out uint size, out IntPtr data, IntPtr opcodeType);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        static extern uint WlanGetProfileList(IntPtr handle, ref Guid iface, IntPtr reserved, out IntPtr list);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        static extern uint WlanGetProfile(IntPtr handle, ref Guid iface, string profileName, IntPtr reserved, out IntPtr xml, IntPtr flags, IntPtr access);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        static extern uint WlanConnect(IntPtr handle, ref Guid iface, ref WlanConnectionParameters parameters, IntPtr reserved);
    }
}
