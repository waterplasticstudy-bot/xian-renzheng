using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace XianAuth
{
    public sealed class CapturedRequest
    {
        public string Method { get; set; }
        public string Url { get; set; }
        public string ContentType { get; set; }
        public string Body { get; set; }
        public string ResponseBody { get; set; }
        public int Status { get; set; }
    }

    public sealed class ClassifyHints
    {
        public string Account { get; set; }
        public string Password { get; set; }
        public string Ip { get; set; }
        public string Mac { get; set; }
    }

    public sealed class ClassificationResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
        public string TemplateKind { get; set; }
        public string ChineseKind { get; set; }
        public string SourceUrl { get; set; }
        public PortalProfile Profile { get; set; }
    }

    public static class CookieGuard
    {
        public static bool IsCookieHeader(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            return name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class RequestClassifier
    {
        static readonly Regex PasswordName = new Regex(@"^(user_password|password|passwd|pwd|pass|userpwd|user_pwd)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex IpName = new Regex(@"^(ip|userip|user_ip|wlan_user_ip|wlanuserip|client_ip|wlanuserip)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex MacName = new Regex(@"^(mac|wlan_user_mac|usermac|user_mac|wlanusermac)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex TimeName = new Regex(@"^(_|t|v|timestamp|time)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly string[] StaticExt = { ".js", ".css", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".svg", ".woff", ".woff2", ".ttf", ".map", ".bmp", ".webp" };

        public static ClassificationResult Classify(IList<CapturedRequest> requests, ClassifyHints hints, string ssid)
        {
            if (hints == null)
                hints = new ClassifyHints();
            if (string.IsNullOrEmpty(hints.Password))
                return Fail("请先填写密码，以便把口令替换成占位符，配置文件不会保存明文密码。");

            var interesting = new List<CapturedRequest>();
            if (requests != null)
            {
                for (int i = 0; i < requests.Count; i++)
                {
                    var req = requests[i];
                    if (req == null || string.IsNullOrEmpty(req.Url) || IsStatic(req.Url))
                        continue;
                    interesting.Add(req);
                }
            }
            if (interesting.Count == 0)
                return Fail("没有捕获到认证请求。");

            int credIndex = -1;
            for (int i = interesting.Count - 1; i >= 0; i--)
            {
                if (CarriesPassword(interesting[i], hints))
                {
                    credIndex = i;
                    break;
                }
            }
            if (credIndex < 0)
                return Fail("没有发现携带密码的认证请求。可以改用导入 HAR 或粘贴登录网址。");

            int challengeIndex = FindChallenge(interesting, credIndex);
            var steps = new List<TemplateStep>();
            if (challengeIndex >= 0)
                steps.Add(ToStep(interesting[challengeIndex], hints, false));
            var credStep = ToStep(interesting[credIndex], hints, true);
            if (challengeIndex >= 0)
                AttachExtract(interesting[challengeIndex], credStep, steps[0]);
            steps.Add(credStep);

            string method = (interesting[credIndex].Method ?? "GET").ToUpperInvariant();
            string kind;
            if (challengeIndex >= 0 && method == "POST")
                kind = "double-post";
            else if (method == "POST")
                kind = "single-post";
            else if (method == "PUT")
                kind = "single-put";
            else if (method == "GET")
                kind = "single-get";
            else
                return Fail("暂不支持的请求方法：" + method);

            if (ContainsSecret(steps, hints.Password))
                return Fail("生成的配置仍包含密码，已拒绝保存。");

            var profile = new PortalProfile
            {
                Id = "captured-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
                Name = "已识别门户（" + TemplateText.ChineseKind(kind) + "）",
                Kind = "template",
                TemplateKind = kind,
                Steps = steps,
                SuccessContains = SuccessMarkers(interesting[credIndex].ResponseBody)
            };
            if (!string.IsNullOrEmpty(ssid))
                profile.Ssids.Add(ssid);
            Uri uri;
            if (Uri.TryCreate(interesting[credIndex].Url, UriKind.Absolute, out uri))
                profile.UrlContains.Add(uri.Host);
            return new ClassificationResult
            {
                Ok = true,
                TemplateKind = kind,
                ChineseKind = TemplateText.ChineseKind(kind),
                SourceUrl = interesting[credIndex].Url,
                Profile = profile
            };
        }

        public static ClassificationResult FromPastedUrl(string url, ClassifyHints hints, string ssid)
        {
            if (string.IsNullOrWhiteSpace(url))
                return Fail("网址为空。");
            url = url.Trim();
            if (DrcomEportal.IsForbiddenAdminUrl(url))
                return Fail("这是门户后台地址（c=main&a=login），不是上网认证，已忽略。");
            var req = new CapturedRequest { Method = "GET", Url = url, Body = "" };
            return Classify(new List<CapturedRequest> { req }, hints, ssid);
        }

        static bool ContainsSecret(List<TemplateStep> steps, string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 4)
                return false;
            for (int i = 0; i < steps.Count; i++)
            {
                if ((steps[i].Url ?? "").IndexOf(password, StringComparison.Ordinal) >= 0)
                    return true;
                if ((steps[i].Body ?? "").IndexOf(password, StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        static int FindChallenge(List<CapturedRequest> requests, int credIndex)
        {
            var cred = requests[credIndex];
            string credText = (cred.Url ?? "") + "\n" + (cred.Body ?? "");
            for (int i = credIndex - 1; i >= 0 && i >= credIndex - 6; i--)
            {
                string response = requests[i].ResponseBody;
                if (string.IsNullOrEmpty(response))
                    continue;
                if (SharesToken(response, credText, requests[i].Body))
                    return i;
            }
            return -1;
        }

        static bool SharesToken(string response, string credText, string challengeBody)
        {
            JObject json;
            if (!JsonpParser.TryParse(response, out json))
                return false;
            foreach (var prop in json.Properties())
            {
                if (prop.Value == null || prop.Value.Type != JTokenType.String)
                    continue;
                string value = (string)prop.Value;
                if (value == null || value.Length < 8)
                    continue;
                if (challengeBody != null && challengeBody.IndexOf(value, StringComparison.Ordinal) >= 0)
                    continue;
                if (credText.IndexOf(value, StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        static void AttachExtract(CapturedRequest challenge, TemplateStep credStep, TemplateStep challengeStep)
        {
            JObject json;
            if (!JsonpParser.TryParse(challenge.ResponseBody, out json))
                return;
            string credText = (credStep.Url ?? "") + "\n" + (credStep.Body ?? "");
            foreach (var prop in json.Properties())
            {
                if (prop.Value == null || prop.Value.Type != JTokenType.String)
                    continue;
                string value = (string)prop.Value;
                if (value == null || value.Length < 8)
                    continue;
                string placeholder = "{" + SafeName(prop.Name) + "}";
                if (credText.IndexOf(value, StringComparison.Ordinal) < 0 && credText.IndexOf(placeholder, StringComparison.Ordinal) < 0)
                    continue;
                credStep.Url = ReplaceOnce(credStep.Url, value, placeholder);
                credStep.Body = ReplaceOnce(credStep.Body, value, placeholder);
                challengeStep.Extract.Add(new ExtractRule
                {
                    Name = SafeName(prop.Name),
                    Pattern = "\"" + Regex.Escape(prop.Name) + "\"\\s*:\\s*\"([^\"]+)\""
                });
                credText = (credStep.Url ?? "") + "\n" + (credStep.Body ?? "");
            }
        }

        static string ReplaceOnce(string text, string value, string placeholder)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
                return text;
            int index = text.IndexOf(value, StringComparison.Ordinal);
            if (index < 0)
                return text;
            return text.Substring(0, index) + placeholder + text.Substring(index + value.Length);
        }

        static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "token";
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')
                    sb.Append(c);
            }
            return sb.Length == 0 ? "token" : sb.ToString();
        }

        static TemplateStep ToStep(CapturedRequest req, ClassifyHints hints, bool credentialStep)
        {
            string url = req.Url ?? "";
            string body = req.Body ?? "";
            url = TemplatizeUrl(url, hints);
            body = TemplatizeBody(body, req.ContentType, hints);
            if (credentialStep)
            {
                url = ForcePasswordParams(url, true);
                body = ForcePasswordParams(body, false);
            }
            return new TemplateStep
            {
                Method = string.IsNullOrEmpty(req.Method) ? "GET" : req.Method.ToUpperInvariant(),
                Url = url,
                Body = string.IsNullOrEmpty(body) ? null : body,
                ContentType = req.ContentType
            };
        }

        static string TemplatizeUrl(string url, ClassifyHints hints)
        {
            int q = url.IndexOf('?');
            if (q < 0)
                return url;
            string path = url.Substring(0, q);
            string query = TemplatizeForm(url.Substring(q + 1), hints);
            return path + "?" + query;
        }

        static string TemplatizeBody(string body, string contentType, ClassifyHints hints)
        {
            if (string.IsNullOrEmpty(body))
                return body;
            if (contentType != null && contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
                return TemplatizeJson(body, hints);
            if (body.IndexOf('=') >= 0 && body.IndexOf('{') < 0)
                return TemplatizeForm(body, hints);
            if (!string.IsNullOrEmpty(hints.Password) && body.IndexOf(hints.Password, StringComparison.Ordinal) >= 0)
                return body.Replace(hints.Password, "{password}");
            return body;
        }

        static string TemplatizeJson(string body, ClassifyHints hints)
        {
            try
            {
                var token = JToken.Parse(body);
                Walk(token, hints);
                return token.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return body;
            }
        }

        static void Walk(JToken token, ClassifyHints hints)
        {
            var obj = token as JObject;
            if (obj != null)
            {
                foreach (var prop in obj.Properties())
                {
                    var value = prop.Value as JValue;
                    if (value != null && value.Type == JTokenType.String)
                        prop.Value = new JValue(ReplaceValue(prop.Name, (string)value, hints));
                    else
                        Walk(prop.Value, hints);
                }
                return;
            }
            var array = token as JArray;
            if (array != null)
            {
                for (int i = 0; i < array.Count; i++)
                    Walk(array[i], hints);
            }
        }

        static string TemplatizeForm(string form, ClassifyHints hints)
        {
            var parts = form.Split('&');
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    sb.Append('&');
                string part = parts[i];
                int eq = part.IndexOf('=');
                string rawKey = eq >= 0 ? part.Substring(0, eq) : part;
                string rawValue = eq >= 0 ? part.Substring(eq + 1) : "";
                string key = Uri.UnescapeDataString(rawKey.Replace("+", " "));
                string value = Uri.UnescapeDataString(rawValue.Replace("+", " "));
                string replaced = ReplaceValue(key, value, hints);
                if (key == "callback" && Regex.IsMatch(replaced, @"^dr\d{10,}$"))
                    replaced = "dr{timestamp}";
                sb.Append(rawKey);
                if (eq >= 0)
                {
                    sb.Append('=');
                    sb.Append(EncodeReplaced(replaced));
                }
            }
            return sb.ToString();
        }

        static string EncodeReplaced(string value)
        {
            if (value == "{account}" || value == "{password}" || value == "{ip}" || value == "{mac}"
                || value == "{mac_zero}" || value == "{timestamp}" || value == "{timestamp_sec}"
                || value == "dr{timestamp}")
                return value;
            if (value != null && value.IndexOf("{account}", StringComparison.Ordinal) >= 0)
            {
                string encoded = Uri.EscapeDataString(value);
                return encoded.Replace("%7Baccount%7D", "{account}").Replace("%7Bpassword%7D", "{password}");
            }
            return Uri.EscapeDataString(value ?? "");
        }

        static string ReplaceValue(string name, string value, ClassifyHints hints)
        {
            if (value == null)
                value = "";
            if (PasswordName.IsMatch(name ?? ""))
                return "{password}";
            if (!string.IsNullOrEmpty(hints.Password) && hints.Password.Length >= 4 && value == hints.Password)
                return "{password}";
            if (!string.IsNullOrEmpty(hints.Account))
            {
                if (value == hints.Account)
                    return "{account}";
                var prefixed = Regex.Match(value, @"^,[^,]+,(.+)$");
                if (prefixed.Success && prefixed.Groups[1].Value == hints.Account)
                    return value.Substring(0, value.Length - hints.Account.Length) + "{account}";
                if (value.StartsWith(hints.Account + "@", StringComparison.Ordinal))
                    return "{account}" + value.Substring(hints.Account.Length);
            }
            if (IpName.IsMatch(name ?? "") && Regex.IsMatch(value, @"^\d{1,3}(\.\d{1,3}){3}$"))
                return "{ip}";
            if (MacName.IsMatch(name ?? "") && Regex.IsMatch(value, @"^[0-9A-Fa-f]{12}$"))
                return value == "000000000000" ? "{mac_zero}" : "{mac}";
            if (TimeName.IsMatch(name ?? ""))
            {
                if (Regex.IsMatch(value, @"^\d{13}$"))
                    return "{timestamp}";
                if (Regex.IsMatch(value, @"^\d{10}$"))
                    return "{timestamp_sec}";
            }
            if (name == "callback" && Regex.IsMatch(value, @"^dr\d{13}$"))
                return "dr{timestamp}";
            return value;
        }

        static string ForcePasswordParams(string text, bool isUrl)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            return Regex.Replace(text, @"(?i)(user_password|password|passwd|pwd|pass)=([^&]*)", "$1={password}");
        }

        static bool CarriesPassword(CapturedRequest req, ClassifyHints hints)
        {
            string blob = (req.Url ?? "") + "\n" + (req.Body ?? "");
            if (!string.IsNullOrEmpty(hints.Password) && blob.IndexOf(hints.Password, StringComparison.Ordinal) >= 0)
                return true;
            return Regex.IsMatch(blob, @"(?i)(user_password|password|passwd|pwd|pass)=");
        }

        static bool IsStatic(string url)
        {
            string path = url.Split('?')[0].ToLowerInvariant();
            for (int i = 0; i < StaticExt.Length; i++)
            {
                if (path.EndsWith(StaticExt[i], StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        static List<string> SuccessMarkers(string response)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(response))
                return found;
            string[] markers =
            {
                "\"result\":1",
                "\"result\": 1",
                "\"result\":\"1\"",
                "\"result\":\"success\"",
                "\"result\":\"ok\"",
                "\"ecode\":0",
                "认证成功",
                "登录成功",
                "Portal协议认证成功"
            };
            for (int i = 0; i < markers.Length; i++)
            {
                if (response.IndexOf(markers[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    found.Add(markers[i]);
            }
            return found;
        }

        static ClassificationResult Fail(string message)
        {
            return new ClassificationResult { Ok = false, Error = message };
        }
    }

    public static class HarImport
    {
        public static List<CapturedRequest> Read(string json)
        {
            var list = new List<CapturedRequest>();
            if (string.IsNullOrWhiteSpace(json))
                return list;
            var root = JObject.Parse(json);
            var entries = root["log"] == null ? null : root["log"]["entries"] as JArray;
            if (entries == null)
                return list;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i] as JObject;
                if (entry == null)
                    continue;
                var request = entry["request"] as JObject;
                if (request == null)
                    continue;
                var captured = new CapturedRequest
                {
                    Method = (string)request["method"] ?? "GET",
                    Url = (string)request["url"] ?? ""
                };
                var headers = request["headers"] as JArray;
                if (headers != null)
                {
                    for (int h = 0; h < headers.Count; h++)
                    {
                        var header = headers[h] as JObject;
                        if (header == null)
                            continue;
                        string name = (string)header["name"];
                        if (CookieGuard.IsCookieHeader(name))
                            continue;
                        if (name != null && name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            captured.ContentType = (string)header["value"];
                    }
                }
                var post = request["postData"] as JObject;
                if (post != null)
                {
                    captured.Body = (string)post["text"];
                    if (captured.ContentType == null)
                        captured.ContentType = (string)post["mimeType"];
                }
                var response = entry["response"] as JObject;
                if (response != null)
                {
                    captured.Status = response["status"] == null ? 0 : response["status"].Value<int>();
                    var content = response["content"] as JObject;
                    if (content != null)
                    {
                        string text = (string)content["text"];
                        if (text != null && text.Length > 4096)
                            text = text.Substring(0, 4096);
                        captured.ResponseBody = text;
                    }
                }
                list.Add(captured);
            }
            return list;
        }
    }
}
