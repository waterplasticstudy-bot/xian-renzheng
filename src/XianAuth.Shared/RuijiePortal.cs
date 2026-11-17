using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace XianAuth
{
    public sealed class RuijieOptions
    {
        public string BaseUrl { get; set; }
        public string Service { get; set; }
    }

    // 锐捷 ePortal：pageInfo 取出公钥后，对密码做无填充 RSA，再 POST method=login。
    // 按公开的登录字段重新实现，不复制既有客户端源码。
    public static class RuijiePortal
    {
        public static bool LooksLike(string url)
        {
            if (string.IsNullOrEmpty(url) || DrcomEportal.LooksLike(url))
                return false;
            return url.IndexOf("InterFace.do", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("eportal/index.jsp", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("eportal/gologout", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("eportal/redirectortosuccess", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string EncryptPasswordHex(string password, string exponentHex, string modulusHex)
        {
            BigInteger exp = ParseHex(exponentHex);
            BigInteger mod = ParseHex(modulusHex);
            byte[] data = Encoding.UTF8.GetBytes(password ?? "");
            var little = new byte[data.Length + 1];
            for (int i = 0; i < data.Length; i++)
                little[i] = data[data.Length - 1 - i];
            BigInteger input = new BigInteger(little);
            BigInteger crypt = BigInteger.ModPow(input, exp, mod);
            int length = (BitLength(mod) + 7) / 8;
            byte[] cbytes = crypt.ToByteArray();
            if (cbytes.Length > 0 && cbytes[cbytes.Length - 1] == 0)
                Array.Resize(ref cbytes, cbytes.Length - 1);
            var big = new byte[length];
            int copy = Math.Min(cbytes.Length, length);
            for (int i = 0; i < copy; i++)
                big[length - 1 - i] = cbytes[i];
            var sb = new StringBuilder(big.Length * 2);
            for (int i = 0; i < big.Length; i++)
                sb.Append(big[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static string PageInfoUrl(string baseUrl)
        {
            return SrunPortal.TrimBase(baseUrl) + "/eportal/InterFace.do?method=pageInfo";
        }

        public static string LoginUrl(string baseUrl)
        {
            return SrunPortal.TrimBase(baseUrl) + "/eportal/InterFace.do?method=login";
        }

        public static string LogoutUrl(string baseUrl)
        {
            return SrunPortal.TrimBase(baseUrl) + "/eportal/InterFace.do?method=logout";
        }

        public static string EncodeQueryString(string rawQuery)
        {
            if (string.IsNullOrEmpty(rawQuery))
                return "";
            if (rawQuery[0] == '?')
                rawQuery = rawQuery.Substring(1);
            return Uri.EscapeDataString(rawQuery);
        }

        static BigInteger ParseHex(string hex)
        {
            hex = (hex ?? "").Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2);
            if (hex.Length == 0)
                return BigInteger.Zero;
            if ((hex.Length % 2) == 1)
                hex = "0" + hex;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            var little = new byte[bytes.Length + 1];
            for (int i = 0; i < bytes.Length; i++)
                little[i] = bytes[bytes.Length - 1 - i];
            return new BigInteger(little);
        }

        static int BitLength(BigInteger value)
        {
            byte[] bytes = value.ToByteArray();
            int i = bytes.Length - 1;
            if (i >= 0 && bytes[i] == 0)
                i--;
            if (i < 0)
                return 0;
            int bits = i * 8;
            byte b = bytes[i];
            while (b != 0)
            {
                bits++;
                b >>= 1;
            }
            return bits;
        }
    }
}
