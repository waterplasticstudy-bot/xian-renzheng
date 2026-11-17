using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XianAuth
{
    public sealed class SrunOptions
    {
        public string BaseUrl { get; set; }
        public string AcId { get; set; }
    }

    public sealed class SrunLoginParts
    {
        public string Info { get; set; }
        public string Hmd5 { get; set; }
        public string Chksum { get; set; }
        public string InfoJson { get; set; }
    }

    // 深澜 srun 门户的挑战值、xEncode、HMAC-MD5 与 chksum。
    // 算法按门户页面公开的登录脚本重新实现，并用固定向量锁定结果。
    public static class SrunPortal
    {
        const string Alphabet = "LVoJPiCN2R8G90yg+hmFHuacZ1OWMnrsSTXkYpUq/3dlbfKwv6xztjI7DeBE45QA";
        const string Standard = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        public static bool LooksLike(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            return url.IndexOf("srun_portal", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("get_challenge", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("/srun/", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("srun_portal_pc", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static SrunLoginParts Build(string username, string password, string ip, string acid, string token)
        {
            string json = BuildInfoJson(username, password, ip, acid);
            byte[] encoded = XEncode(json, token ?? "");
            string info = "{SRBX1}" + ToCustomBase64(encoded);
            string hmd5 = HmacMd5Hex(token ?? "", password ?? "");
            string chk = (token ?? "") + string.Join(token ?? "", new[]
            {
                username ?? "",
                hmd5,
                acid ?? "",
                ip ?? "",
                "200",
                "1",
                info
            });
            return new SrunLoginParts
            {
                InfoJson = json,
                Info = info,
                Hmd5 = hmd5,
                Chksum = Sha1Hex(chk)
            };
        }

        public static string BuildChallengeUrl(string baseUrl, string username, string ip, long unixMs)
        {
            string callback = "dr" + unixMs.ToString(CultureInfo.InvariantCulture);
            return TrimBase(baseUrl) + "/cgi-bin/get_challenge?callback=" + Uri.EscapeDataString(callback)
                + "&username=" + Uri.EscapeDataString(username ?? "")
                + "&ip=" + Uri.EscapeDataString(ip ?? "")
                + "&_=" + unixMs.ToString(CultureInfo.InvariantCulture);
        }

        public static string BuildLoginUrl(string baseUrl, string username, string password, string ip, string acid, string token, long unixMs)
        {
            var parts = Build(username, password, ip, acid, token);
            string callback = "dr" + unixMs.ToString(CultureInfo.InvariantCulture);
            return TrimBase(baseUrl) + "/cgi-bin/srun_portal?callback=" + Uri.EscapeDataString(callback)
                + "&action=login"
                + "&username=" + Uri.EscapeDataString(username ?? "")
                + "&password=" + Uri.EscapeDataString("{MD5}" + parts.Hmd5)
                + "&ac_id=" + Uri.EscapeDataString(acid ?? "")
                + "&ip=" + Uri.EscapeDataString(ip ?? "")
                + "&chksum=" + Uri.EscapeDataString(parts.Chksum)
                + "&info=" + Uri.EscapeDataString(parts.Info)
                + "&n=200&type=1&os=Windows&name=XianAuth&double_stack=0"
                + "&_=" + unixMs.ToString(CultureInfo.InvariantCulture);
        }

        public static string BuildLogoutUrl(string baseUrl, string username, string ip, string acid, long unixMs)
        {
            return TrimBase(baseUrl) + "/cgi-bin/srun_portal?action=logout"
                + "&username=" + Uri.EscapeDataString(username ?? "")
                + "&ac_id=" + Uri.EscapeDataString(acid ?? "")
                + "&ip=" + Uri.EscapeDataString(ip ?? "")
                + "&_=" + unixMs.ToString(CultureInfo.InvariantCulture);
        }

        public static AuthOutcome Interpret(string body)
        {
            Newtonsoft.Json.Linq.JObject json;
            if (!JsonpParser.TryParse(body, out json))
                return AuthOutcome.Transient("深澜门户返回无法解析");
            string error = json["error"] == null ? "" : json["error"].ToString();
            string message = json["error_msg"] == null || json["error_msg"].Type == Newtonsoft.Json.Linq.JTokenType.Null
                ? error
                : json["error_msg"].ToString();
            var ecode = json["ecode"];
            bool ok = error.Equals("ok", StringComparison.OrdinalIgnoreCase)
                || (ecode != null && ecode.Type == Newtonsoft.Json.Linq.JTokenType.Integer && (long)ecode == 0L);
            if (ok)
                return AuthOutcome.Success(string.IsNullOrEmpty(message) || message == "ok" ? "认证成功" : message);
            if (message.IndexOf("already online", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("已在线", StringComparison.Ordinal) >= 0
                || message.IndexOf("已经在线", StringComparison.Ordinal) >= 0)
                return AuthOutcome.AlreadyOnline(message);
            if (JsonpParser.IsCredentialMessage(message))
                return AuthOutcome.CredentialRejected(message);
            return AuthOutcome.Rejected(string.IsNullOrEmpty(message) ? "深澜认证被拒绝" : message);
        }

        public static string BuildInfoJson(string username, string password, string ip, string acid)
        {
            return "{\"username\":\"" + EscapeJson(username) + "\",\"password\":\"" + EscapeJson(password)
                + "\",\"ip\":\"" + EscapeJson(ip) + "\",\"acid\":\"" + EscapeJson(acid) + "\",\"enc_ver\":\"srun_bx1\"}";
        }

        public static byte[] XEncode(string msg, string key)
        {
            if (string.IsNullOrEmpty(msg))
                return new byte[0];
            var v = SEncode(msg, true);
            var k = SEncode(key ?? "", false);
            while (k.Count < 4)
                k.Add(0);
            int n = v.Count - 1;
            uint z = v[n];
            uint y = v[0];
            uint d = 0;
            int q = 6 + 52 / (n + 1);
            const uint delta = 0x9E3779B9;
            while (q-- > 0)
            {
                d = unchecked(d + delta);
                uint e = (d >> 2) & 3;
                int p;
                for (p = 0; p < n; p++)
                {
                    y = v[p + 1];
                    uint m = (z >> 5) ^ (y << 2);
                    m = unchecked(m + (((y >> 3) ^ (z << 4)) ^ (d ^ y)));
                    m = unchecked(m + (k[(p & 3) ^ (int)e] ^ z));
                    z = v[p] = unchecked(v[p] + m);
                }
                y = v[0];
                uint m2 = (z >> 5) ^ (y << 2);
                m2 = unchecked(m2 + (((y >> 3) ^ (z << 4)) ^ (d ^ y)));
                m2 = unchecked(m2 + (k[(p & 3) ^ (int)e] ^ z));
                z = v[n] = unchecked(v[n] + m2);
            }
            return LEncode(v);
        }

        static System.Collections.Generic.List<uint> SEncode(string msg, bool appendLength)
        {
            var list = new System.Collections.Generic.List<uint>();
            int length = msg.Length;
            for (int i = 0; i < length; i += 4)
            {
                uint value = Ordat(msg, i)
                    | (Ordat(msg, i + 1) << 8)
                    | (Ordat(msg, i + 2) << 16)
                    | (Ordat(msg, i + 3) << 24);
                list.Add(value);
            }
            if (appendLength)
                list.Add((uint)length);
            return list;
        }

        static uint Ordat(string msg, int index)
        {
            if (index < 0 || index >= msg.Length)
                return 0;
            return msg[index];
        }

        static byte[] LEncode(System.Collections.Generic.List<uint> words)
        {
            var bytes = new byte[words.Count * 4];
            for (int i = 0; i < words.Count; i++)
            {
                uint value = words[i];
                bytes[i * 4] = (byte)(value & 255);
                bytes[i * 4 + 1] = (byte)((value >> 8) & 255);
                bytes[i * 4 + 2] = (byte)((value >> 16) & 255);
                bytes[i * 4 + 3] = (byte)((value >> 24) & 255);
            }
            return bytes;
        }

        static string ToCustomBase64(byte[] data)
        {
            string b64 = Convert.ToBase64String(data);
            var sb = new StringBuilder(b64.Length);
            for (int i = 0; i < b64.Length; i++)
            {
                int index = Standard.IndexOf(b64[i]);
                sb.Append(index >= 0 ? Alphabet[index] : b64[i]);
            }
            return sb.ToString();
        }

        static string HmacMd5Hex(string key, string message)
        {
            using (var hmac = new HMACMD5(Encoding.UTF8.GetBytes(key ?? "")))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message ?? ""));
                return ToHex(hash);
            }
        }

        static string Sha1Hex(string text)
        {
            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                return ToHex(hash);
            }
        }

        static string ToHex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++)
                sb.Append(data[i].ToString("x2"));
            return sb.ToString();
        }

        static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' || c == '"')
                    sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string TrimBase(string baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl))
                return "";
            return baseUrl.TrimEnd('/');
        }
    }
}
