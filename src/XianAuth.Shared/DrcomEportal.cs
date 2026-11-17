using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace XianAuth
{
    public sealed class DrcomOptions
    {
        public string BaseUrl { get; set; }
        public string UserAccountPrefix { get; set; }
        public string Mac { get; set; }
        public string AcIp { get; set; }
        public string AcName { get; set; }
        public string JsVersion { get; set; }
        public string LoginMethod { get; set; }
    }

    public static class DrcomEportal
    {
        public static string BuildLoginUrl(DrcomOptions options, string account, string password, string ip, long unixMs)
        {
            var query = new StringBuilder();
            Add(query, "c", "Portal");
            Add(query, "a", "login");
            Add(query, "callback", "dr" + unixMs.ToString(CultureInfo.InvariantCulture));
            Add(query, "login_method", options.LoginMethod ?? "1");
            Add(query, "user_account", (options.UserAccountPrefix ?? ",0,") + (account ?? ""));
            Add(query, "user_password", password ?? "");
            Add(query, "wlan_user_ip", ip ?? "");
            Add(query, "wlan_user_mac", string.IsNullOrEmpty(options.Mac) ? "000000000000" : options.Mac);
            Add(query, "wlan_ac_ip", options.AcIp ?? "");
            Add(query, "wlan_ac_name", options.AcName ?? "");
            Add(query, "jsVersion", options.JsVersion ?? "3.0");
            Add(query, "_", unixMs.ToString(CultureInfo.InvariantCulture));
            return Combine(options.BaseUrl, query.ToString());
        }

        public static string BuildLogoutUrl(DrcomOptions options, string ip, long unixMs)
        {
            var query = new StringBuilder();
            Add(query, "c", "Portal");
            Add(query, "a", "logout");
            Add(query, "login_method", options.LoginMethod ?? "1");
            Add(query, "wlan_user_ip", ip ?? "");
            Add(query, "wlan_ac_name", options.AcName ?? "");
            Add(query, "jsVersion", options.JsVersion ?? "3.0");
            Add(query, "_", unixMs.ToString(CultureInfo.InvariantCulture));
            return Combine(options.BaseUrl, query.ToString());
        }

        public static bool IsForbiddenAdminUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            return url.IndexOf("c=main", StringComparison.OrdinalIgnoreCase) >= 0
                && url.IndexOf("a=login", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool LooksLike(string url)
        {
            if (string.IsNullOrEmpty(url) || IsForbiddenAdminUrl(url))
                return false;
            if (url.IndexOf("/eportal/portal/login", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (url.IndexOf("callback=dr", StringComparison.OrdinalIgnoreCase) >= 0
                && url.IndexOf("eportal", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (url.IndexOf("c=Portal", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("c=portal", StringComparison.OrdinalIgnoreCase) >= 0)
                return url.IndexOf("a=login", StringComparison.OrdinalIgnoreCase) >= 0
                    || url.IndexOf("a=logout", StringComparison.OrdinalIgnoreCase) >= 0
                    || url.IndexOf("eportal", StringComparison.OrdinalIgnoreCase) >= 0;
            return false;
        }

        public static PortalProfile ProfileFromUrl(string url, string ssid)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                return null;

            var query = ParseQuery(uri.Query);
            string ac = First(query, "wlanacname", "wlan_ac_name");
            string prefix = ",0,";
            string accountParam;
            if (query.TryGetValue("user_account", out accountParam))
            {
                var prefixed = Regex.Match(accountParam ?? "", @"^,[^,]+,");
                if (prefixed.Success)
                    prefix = prefixed.Value;
            }
            string portPart = uri.IsDefaultPort ? "" : ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
            string baseUrl = uri.Scheme + "://" + uri.Host + portPart + "/eportal/";
            var profile = new PortalProfile
            {
                Id = "drcom-" + uri.Host.Replace(".", "-"),
                Name = "Dr.COM ePortal",
                Kind = "drcom-eportal",
                Notes = "由重定向地址生成。登录使用 c=Portal&a=login，不使用 c=main&a=login。",
                Drcom = new DrcomOptions
                {
                    BaseUrl = baseUrl,
                    UserAccountPrefix = prefix,
                    Mac = "000000000000",
                    AcIp = "",
                    AcName = ac ?? "",
                    JsVersion = "3.0",
                    LoginMethod = "1"
                }
            };
            if (!string.IsNullOrEmpty(ssid))
                profile.Ssids.Add(ssid);
            profile.UrlContains.Add(uri.Host);
            if (!string.IsNullOrEmpty(ac))
                profile.UrlContains.Add(ac);
            return profile;
        }

        static string Combine(string baseUrl, string query)
        {
            string root = string.IsNullOrEmpty(baseUrl) ? "" : baseUrl;
            if (root.IndexOf('?') >= 0)
                return root + "&" + query;
            if (!root.EndsWith("/", StringComparison.Ordinal) && root.IndexOf("/eportal", StringComparison.OrdinalIgnoreCase) < 0)
                root += "/";
            return root + "?" + query;
        }

        static void Add(StringBuilder query, string key, string value)
        {
            if (query.Length > 0)
                query.Append('&');
            query.Append(key);
            query.Append('=');
            query.Append(Uri.EscapeDataString(value ?? ""));
        }

        public static Dictionary<string, string> ParseQuery(string query)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query))
                return map;
            string raw = query[0] == '?' ? query.Substring(1) : query;
            var parts = raw.Split('&');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                    continue;
                int eq = parts[i].IndexOf('=');
                string key = eq >= 0 ? parts[i].Substring(0, eq) : parts[i];
                string value = eq >= 0 ? parts[i].Substring(eq + 1) : "";
                key = Uri.UnescapeDataString(key.Replace("+", " "));
                value = Uri.UnescapeDataString(value.Replace("+", " "));
                map[key] = value;
            }
            return map;
        }

        static string First(Dictionary<string, string> map, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                string value;
                if (map.TryGetValue(keys[i], out value) && !string.IsNullOrEmpty(value))
                    return value;
            }
            return null;
        }
    }
}
