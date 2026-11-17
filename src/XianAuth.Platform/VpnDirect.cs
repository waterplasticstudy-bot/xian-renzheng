using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Text;

namespace XianAuth
{
    public static class VpnScanner
    {
        public static string Describe()
        {
            try
            {
                List<VpnHit> hits = Scan();
                if (hits.Count == 0)
                    return "未识别到常见的 VPN 客户端";
                var parts = new List<string>();
                for (int i = 0; i < hits.Count; i++)
                {
                    VpnHit hit = hits[i];
                    string state = !hit.Running ? "已安装" : (hit.TunUp ? "TUN 开" : "TUN 关");
                    parts.Add(hit.Name + "（" + state + "）");
                }
                return "已识别：" + string.Join("、", parts.ToArray());
            }
            catch (Exception ex)
            {
                return "识别 VPN 客户端时出错：" + ex.Message;
            }
        }

        static List<VpnHit> Scan()
        {
            var found = new Dictionary<string, VpnHit>(StringComparer.OrdinalIgnoreCase);
            NoteProcesses(found);
            NoteServices(found);
            NoteInstalls(found);
            NoteTunnels(found);
            string[] order = { "Bettbox", "FlClash", "YueLink", "Clash Verge Rev", "mihomo", "sing-box" };
            var list = new List<VpnHit>();
            for (int i = 0; i < order.Length; i++)
            {
                VpnHit hit;
                if (found.TryGetValue(order[i], out hit))
                {
                    list.Add(hit);
                    found.Remove(order[i]);
                }
            }
            foreach (KeyValuePair<string, VpnHit> pair in found)
                list.Add(pair.Value);
            return list;
        }

        static void Mark(Dictionary<string, VpnHit> found, string name, bool running)
        {
            if (string.IsNullOrEmpty(name))
                return;
            VpnHit hit;
            if (!found.TryGetValue(name, out hit))
            {
                hit = new VpnHit { Name = name };
                found[name] = hit;
            }
            if (running)
                hit.Running = true;
        }

        static void NoteProcesses(Dictionary<string, VpnHit> found)
        {
            Process[] processes;
            try { processes = Process.GetProcesses(); }
            catch (Exception) { return; }
            for (int i = 0; i < processes.Length; i++)
            {
                string name;
                try { name = processes[i].ProcessName; }
                catch (Exception) { continue; }
                string client = ClientFromProcess(name);
                if (client != null)
                    Mark(found, client, true);
            }
        }

        static void NoteServices(Dictionary<string, VpnHit> found)
        {
            try
            {
                ServiceController[] services = ServiceController.GetServices();
                for (int i = 0; i < services.Length; i++)
                {
                    string client = ClientFromService(services[i].ServiceName);
                    if (client == null)
                        continue;
                    bool running = false;
                    try { running = services[i].Status == ServiceControllerStatus.Running; }
                    catch (Exception) { }
                    Mark(found, client, running);
                }
            }
            catch (Exception)
            {
            }
        }

        static void NoteInstalls(Dictionary<string, VpnHit> found)
        {
            string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            MarkIfDir(found, Path.Combine(app, "com.appshub", "Bettbox"), "Bettbox");
            MarkIfDir(found, Path.Combine(app, "com.follow", "clash"), "FlClash");
            MarkIfDir(found, Path.Combine(app, "io.github.clash-verge-rev.clash-verge-rev"), "Clash Verge Rev");
            if (!Directory.Exists(app))
                return;
            string[] parents = Directory.GetDirectories(app);
            for (int i = 0; i < parents.Length; i++)
            {
                ConsiderDir(found, parents[i]);
                try
                {
                    string[] children = Directory.GetDirectories(parents[i]);
                    for (int c = 0; c < children.Length; c++)
                        ConsiderDir(found, children[c]);
                }
                catch (Exception)
                {
                }
            }
        }

        static void ConsiderDir(Dictionary<string, VpnHit> found, string dir)
        {
            string client = ClientFromFolder(Path.GetFileName(dir));
            if (client == null)
                client = ClientFromFolder(Path.GetFileName(Path.GetDirectoryName(dir)));
            if (client == null)
                return;
            if (File.Exists(Path.Combine(dir, "config.json")) || Directory.Exists(Path.Combine(dir, "profiles")))
                Mark(found, client, false);
        }

        static void MarkIfDir(Dictionary<string, VpnHit> found, string dir, string name)
        {
            if (Directory.Exists(dir))
                Mark(found, name, false);
        }

        static void NoteTunnels(Dictionary<string, VpnHit> found)
        {
            NetworkInterface[] interfaces;
            try { interfaces = NetworkInterface.GetAllNetworkInterfaces(); }
            catch (Exception) { return; }
            for (int i = 0; i < interfaces.Length; i++)
            {
                if (interfaces[i].OperationalStatus != OperationalStatus.Up)
                    continue;
                string desc = interfaces[i].Description ?? "";
                string name = interfaces[i].Name ?? "";
                bool meta = Contains(desc, "Meta Tunnel");
                bool wintun = Contains(desc, "Wintun");
                bool sing = Contains(desc, "sing-tun");
                if (meta || wintun)
                {
                    ForceTun(found, "Bettbox");
                    ForceTun(found, "FlClash");
                    ForceTun(found, "YueLink");
                    ForceTun(found, "mihomo");
                    ForceTun(found, "Clash Verge Rev");
                }
                if (sing)
                    ForceTun(found, "sing-box");
                if (Contains(name, "Bettbox"))
                    ForceTun(found, "Bettbox");
                if (Contains(name, "FlClash"))
                    ForceTun(found, "FlClash");
                if (Contains(name, "YueLink"))
                    ForceTun(found, "YueLink");
            }
        }

        static void ForceTun(Dictionary<string, VpnHit> found, string client)
        {
            VpnHit hit;
            if (found.TryGetValue(client, out hit) && hit.Running)
                hit.TunUp = true;
        }

        static string ClientFromProcess(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (EqualsName(name, "Bettbox") || EqualsName(name, "BettboxCore"))
                return "Bettbox";
            if (EqualsName(name, "FlClash") || EqualsName(name, "FlClashCore"))
                return "FlClash";
            if (EqualsName(name, "YueLink") || EqualsName(name, "YueLinkCore"))
                return "YueLink";
            if (EqualsName(name, "clash-verge") || EqualsName(name, "verge-mihomo") || EqualsName(name, "Clash Verge"))
                return "Clash Verge Rev";
            if (EqualsName(name, "mihomo") || EqualsName(name, "clash-meta"))
                return "mihomo";
            if (EqualsName(name, "sing-box"))
                return "sing-box";
            return null;
        }

        static string ClientFromService(string name)
        {
            if (EqualsName(name, "BettboxHelperService"))
                return "Bettbox";
            if (EqualsName(name, "FlClashHelperService"))
                return "FlClash";
            if (EqualsName(name, "clash-verge-service"))
                return "Clash Verge Rev";
            return null;
        }

        static string ClientFromFolder(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (Contains(name, "bettbox"))
                return "Bettbox";
            if (Contains(name, "yuelink"))
                return "YueLink";
            if (Contains(name, "flclash") || string.Equals(name, "clash", StringComparison.OrdinalIgnoreCase))
                return "FlClash";
            if (Contains(name, "clash-verge") || Contains(name, "verge-rev"))
                return "Clash Verge Rev";
            if (Contains(name, "sing-box") || Contains(name, "singbox"))
                return "sing-box";
            if (Contains(name, "mihomo") || Contains(name, "clash-meta"))
                return "mihomo";
            return null;
        }

        static bool EqualsName(string value, string expected)
        {
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }

        static bool Contains(string value, string part)
        {
            return value != null && value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        sealed class VpnHit
        {
            public string Name;
            public bool Running;
            public bool TunUp;
        }
    }

    public static class PortalRoute
    {
        public static string TryAdd(AppPaths paths, ChangeJournal journal, string portalIp, out bool added)
        {
            added = false;
            if (JournalHas(journal, portalIp))
            {
                added = true;
                return "门户主机路由已存在";
            }
            WifiSnapshot wifi;
            try
            {
                wifi = PickWifi();
            }
            catch (Exception ex)
            {
                return "未能添加主机路由（读取无线网卡失败：" + ex.Message + "）";
            }
            if (wifi == null || wifi.Gateway == null || wifi.InterfaceIndex <= 0)
                return "未能添加主机路由（无线网卡没有网关或接口序号）。认证请求仍会绑定 Wi-Fi 地址。";
            string args = RouteCommand.AddArguments(portalIp, wifi.Gateway.ToString(), wifi.InterfaceIndex);
            string output;
            int code = RunRoute(args, out output);
            bool exists = output.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0
                || output.IndexOf("已存在", StringComparison.Ordinal) >= 0;
            if (code != 0 && !exists)
                return "添加主机路由失败（route.exe 返回 " + code.ToString(System.Globalization.CultureInfo.InvariantCulture) + "）";
            if (code == 0)
                journal.Add("route", "vpn", portalIp, null);
            added = true;
            return code == 0
                ? "已添加门户主机路由（不持久，恢复默认配置时删除）"
                : "门户主机路由已在系统中";
        }

        public static void Delete(string portalIp)
        {
            if (string.IsNullOrEmpty(portalIp))
                return;
            string ignored;
            RunRoute(RouteCommand.DeleteArguments(portalIp), out ignored);
        }

        static bool JournalHas(ChangeJournal journal, string portalIp)
        {
            if (journal == null || journal.Records == null)
                return false;
            for (int i = 0; i < journal.Records.Count; i++)
            {
                ChangeRecord record = journal.Records[i];
                if (record.Kind == "route" && string.Equals(record.Target, portalIp, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static WifiSnapshot PickWifi()
        {
            List<WifiSnapshot> all = NativeWifi.QueryAll();
            WifiSnapshot best = null;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].IPv4 == null)
                    continue;
                best = all[i];
                if (all[i].Gateway != null && all[i].InterfaceIndex > 0)
                    return all[i];
            }
            return best;
        }

        static int RunRoute(string arguments, out string output)
        {
            output = "";
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "route.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process process = Process.Start(start))
                {
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit(8000);
                    output = (stdout ?? "") + (stderr ?? "");
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }
    }

    public static class FlClashFamily
    {
        public static int Apply(AppPaths paths, ChangeJournal journal, string portalIp, List<string> notes)
        {
            int changes = 0;
            List<string> homes = Homes();
            for (int i = 0; i < homes.Count; i++)
            {
                string file = Path.Combine(homes[i], "config.json");
                if (!File.Exists(file))
                    continue;
                var info = new FileInfo(file);
                if (info.Length > 8 * 1024 * 1024)
                    continue;
                string json = File.ReadAllText(file, Encoding.UTF8);
                bool changed;
                string updated = FlClashConfigEditor.AddPortalBypass(json, portalIp, out changed);
                if (!changed)
                    continue;
                Backup(paths, journal, file);
                File.WriteAllText(file, updated, new UTF8Encoding(false));
                notes.Add("已在 " + Path.GetFileName(homes[i]) + " 的路由排除中加入门户地址");
                changes++;
            }
            return changes;
        }

        static List<string> Homes()
        {
            var list = new List<string>();
            string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Add(list, Path.Combine(app, "com.appshub", "Bettbox"));
            Add(list, Path.Combine(app, "com.follow", "clash"));
            if (!Directory.Exists(app))
                return list;
            string[] parents = Directory.GetDirectories(app);
            for (int i = 0; i < parents.Length; i++)
            {
                Consider(list, parents[i]);
                try
                {
                    string[] children = Directory.GetDirectories(parents[i]);
                    for (int c = 0; c < children.Length; c++)
                        Consider(list, children[c]);
                }
                catch (Exception)
                {
                }
            }
            return list;
        }

        static void Consider(List<string> list, string dir)
        {
            string name = Path.GetFileName(dir) ?? "";
            string parent = Path.GetFileName(Path.GetDirectoryName(dir)) ?? "";
            if (!Hints(name) && !Hints(parent))
                return;
            if (File.Exists(Path.Combine(dir, "config.json")))
                Add(list, dir);
        }

        static bool Hints(string name)
        {
            return Contains(name, "bettbox") || Contains(name, "flclash") || Contains(name, "yuelink")
                || Contains(name, "clash") || Contains(name, "mihomo") || Contains(name, "sing-box")
                || Contains(name, "singbox");
        }

        static bool Contains(string value, string part)
        {
            return value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void Add(List<string> list, string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], path, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            list.Add(path);
        }

        static void Backup(AppPaths paths, ChangeJournal journal, string target)
        {
            paths.Ensure();
            string name = Guid.NewGuid().ToString("N") + ".bak";
            File.Copy(target, Path.Combine(paths.Backups, name), true);
            journal.Add("file", "vpn", target, name);
        }
    }

    public static class SingBoxFamily
    {
        public static int Apply(AppPaths paths, ChangeJournal journal, string portalIp, List<string> notes)
        {
            int changes = 0;
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] files =
            {
                Path.Combine(user, ".config", "sing-box", "config.json"),
                Path.Combine(app, "sing-box", "config.json")
            };
            for (int i = 0; i < files.Length; i++)
            {
                if (!File.Exists(files[i]))
                    continue;
                var info = new FileInfo(files[i]);
                if (info.Length > 8 * 1024 * 1024)
                    continue;
                string json = File.ReadAllText(files[i], Encoding.UTF8);
                bool changed;
                string updated = SingBoxConfigEditor.AddDirectRule(json, portalIp, out changed);
                if (!changed)
                    continue;
                paths.Ensure();
                string name = Guid.NewGuid().ToString("N") + ".bak";
                File.Copy(files[i], Path.Combine(paths.Backups, name), true);
                journal.Add("file", "vpn", files[i], name);
                File.WriteAllText(files[i], updated, new UTF8Encoding(false));
                notes.Add("已在 sing-box 配置中加入直连规则");
                changes++;
            }
            return changes;
        }
    }
}
