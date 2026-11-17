using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace XianAuth
{
    public static class ClashRules
    {
        public static string Format(string ip)
        {
            return "IP-CIDR," + ip + "/32,DIRECT,no-resolve";
        }

        public static bool TryNormalizeIpv4(string text, out string ip)
        {
            ip = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var parts = text.Trim().Split('.');
            if (parts.Length != 4)
                return false;
            var bytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                int n;
                if (!int.TryParse(parts[i], out n) || n < 0 || n > 255)
                    return false;
                bytes[i] = (byte)n;
            }
            if (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19))
                return false;
            ip = bytes[0] + "." + bytes[1] + "." + bytes[2] + "." + bytes[3];
            return true;
        }

        public static bool IsFakeIp(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                return false;
            var parts = ip.Split('.');
            if (parts.Length != 4)
                return false;
            int a, b;
            if (!int.TryParse(parts[0], out a) || !int.TryParse(parts[1], out b))
                return false;
            return a == 198 && (b == 18 || b == 19);
        }
    }

    public static class RulesFileEditor
    {
        public static string AddPrepend(string yaml, string rule, out bool changed)
        {
            if (!string.IsNullOrEmpty(yaml) && yaml.IndexOf(rule, StringComparison.Ordinal) >= 0)
            {
                changed = false;
                return yaml;
            }
            if (string.IsNullOrWhiteSpace(yaml))
            {
                changed = true;
                return "prepend:\n  - " + rule + "\nappend: []\ndelete: []\n";
            }
            string nl = yaml.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var lines = Split(yaml);
            for (int i = 0; i < lines.Count; i++)
            {
                string trim = lines[i].Trim();
                if (trim == "prepend: []" || trim == "prepend:[]")
                {
                    lines[i] = "prepend:";
                    lines.Insert(i + 1, "  - " + rule);
                    changed = true;
                    return Join(lines, nl, yaml.EndsWith("\n", StringComparison.Ordinal) || yaml.EndsWith("\r\n", StringComparison.Ordinal));
                }
                if (trim == "prepend:")
                {
                    lines.Insert(i + 1, "  - " + rule);
                    changed = true;
                    return Join(lines, nl, true);
                }
            }
            lines.Insert(0, "  - " + rule);
            lines.Insert(0, "prepend:");
            changed = true;
            return Join(lines, nl, true);
        }

        static List<string> Split(string yaml)
        {
            string normalized = yaml.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = new List<string>(normalized.Split('\n'));
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            return lines;
        }

        static string Join(List<string> lines, string nl, bool trailing)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    sb.Append(nl);
                sb.Append(lines[i]);
            }
            if (trailing)
                sb.Append(nl);
            return sb.ToString();
        }
    }

    public sealed class VergeFileWrite
    {
        public string FileName { get; set; }
        public string Content { get; set; }
        public bool Created { get; set; }
        public bool Changed { get; set; }
    }

    public sealed class VergeEdit
    {
        public VergeEdit()
        {
            Files = new List<VergeFileWrite>();
        }

        public bool Ok { get; set; }
        public string Error { get; set; }
        public string ProfilesYaml { get; set; }
        public bool ProfilesChanged { get; set; }
        public List<VergeFileWrite> Files { get; set; }
    }

    public static class VergeProfilesEditor
    {
        public const string RulesUid = "xian-renzheng-rules";
        public const string RulesFile = "xian-renzheng-rules.yaml";

        public static VergeEdit Apply(string yaml, string portalIp, Func<string, string> readByFileName)
        {
            var edit = new VergeEdit();
            if (string.IsNullOrWhiteSpace(yaml))
            {
                edit.Error = "profiles.yaml 为空";
                return edit;
            }
            string rule = ClashRules.Format(portalIp);
            string nl = yaml.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            bool trailing = yaml.EndsWith("\n", StringComparison.Ordinal);
            var lines = Split(yaml);
            var targets = FindTargets(lines);
            if (targets.Count == 0)
            {
                edit.Error = "未找到可修改的 Clash Verge 订阅";
                return edit;
            }

            var drafted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int t = targets.Count - 1; t >= 0; t--)
            {
                int start = targets[t];
                int end = BlockEnd(lines, start);
                string rulesUid = Clean(ReadKey(lines, start, end, "rules"));
                bool missing = string.IsNullOrEmpty(rulesUid) || rulesUid == "null" || rulesUid == "~";
                string fileName;
                if (missing)
                {
                    if (!SetRules(lines, start, ref end, RulesUid))
                    {
                        edit.Error = "无法写入 rules 字段";
                        return edit;
                    }
                    EnsureRulesItem(lines);
                    fileName = RulesFile;
                }
                else
                {
                    int item = FindUidLine(lines, rulesUid);
                    if (item < 0)
                    {
                        edit.Error = "规则项不存在：" + rulesUid;
                        return edit;
                    }
                    fileName = Clean(ReadKey(lines, item, BlockEnd(lines, item), "file"));
                    if (string.IsNullOrEmpty(fileName))
                    {
                        edit.Error = "规则项没有 file 字段：" + rulesUid;
                        return edit;
                    }
                }

                if (!drafted.ContainsKey(fileName))
                {
                    string existing = readByFileName == null ? null : readByFileName(fileName);
                    bool fileCreated = string.IsNullOrEmpty(existing);
                    bool changed;
                    string updated = RulesFileEditor.AddPrepend(existing, rule, out changed);
                    drafted[fileName] = updated;
                    edit.Files.Add(new VergeFileWrite
                    {
                        FileName = fileName,
                        Content = updated,
                        Created = fileCreated,
                        Changed = changed || fileCreated
                    });
                }
            }

            string updatedYaml = Join(lines, nl, trailing);
            edit.Ok = true;
            edit.ProfilesYaml = updatedYaml;
            edit.ProfilesChanged = updatedYaml != yaml;
            return edit;
        }

        static List<int> FindTargets(List<string> lines)
        {
            var targets = new List<int>();
            var seen = new HashSet<int>();
            for (int i = 0; i < lines.Count; i++)
            {
                if (!IsItemStart(lines[i]) || !seen.Add(i))
                    continue;
                int end = BlockEnd(lines, i);
                string type = Clean(ReadKey(lines, i, end, "type"));
                if (type == "remote" || type == "local")
                    targets.Add(i);
            }
            return targets;
        }

        static void EnsureRulesItem(List<string> lines)
        {
            if (FindUidLine(lines, RulesUid) >= 0)
                return;
            int insertAt = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                if (Regex.IsMatch(lines[i], @"^items\s*:\s*$"))
                {
                    insertAt = lines.Count;
                    for (int j = i + 1; j < lines.Count; j++)
                    {
                        if (Regex.IsMatch(lines[j], @"^[A-Za-z_]"))
                        {
                            insertAt = j;
                            break;
                        }
                    }
                    break;
                }
            }
            var block = new[]
            {
                "- uid: " + RulesUid,
                "  type: rules",
                "  name: xian-renzheng-direct",
                "  file: " + RulesFile
            };
            if (insertAt > 0 && lines[insertAt - 1].Trim().Length != 0)
            {
                lines.Insert(insertAt, "");
                insertAt++;
            }
            for (int i = 0; i < block.Length; i++)
                lines.Insert(insertAt + i, block[i]);
        }

        static bool SetRules(List<string> lines, int start, ref int end, string uid)
        {
            var rules = new Regex(@"^(\s+)rules\s*:\s*.*$");
            for (int i = start; i < end && i < lines.Count; i++)
            {
                var match = rules.Match(lines[i]);
                if (!match.Success)
                    continue;
                lines[i] = match.Groups[1].Value + "rules: " + uid;
                return true;
            }
            for (int i = start; i < end && i < lines.Count; i++)
            {
                var option = Regex.Match(lines[i], @"^(\s+)option\s*:\s*$");
                if (option.Success)
                {
                    lines.Insert(i + 1, new string(' ', option.Groups[1].Value.Length + 2) + "rules: " + uid);
                    end++;
                    return true;
                }
                var optionNull = Regex.Match(lines[i], @"^(\s+)option\s*:\s*null\s*$");
                if (optionNull.Success)
                {
                    lines[i] = optionNull.Groups[1].Value + "option:";
                    lines.Insert(i + 1, new string(' ', optionNull.Groups[1].Value.Length + 2) + "rules: " + uid);
                    end++;
                    return true;
                }
            }
            lines.Insert(end, "  option:");
            lines.Insert(end + 1, "    rules: " + uid);
            end += 2;
            return true;
        }

        static bool IsItemStart(string line)
        {
            return Regex.IsMatch(line ?? "", @"^\s*-\s+uid\s*:");
        }

        static int FindUidLine(List<string> lines, string uid)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var match = Regex.Match(lines[i], @"^\s*-\s+uid\s*:\s*(.+)$");
                if (match.Success && Clean(match.Groups[1].Value) == uid)
                    return i;
            }
            return -1;
        }

        static int BlockEnd(List<string> lines, int start)
        {
            int indent = Leading(lines[start]);
            for (int i = start + 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;
                if (IsItemStart(lines[i]) && Leading(lines[i]) <= indent)
                    return i;
                if (Regex.IsMatch(lines[i], @"^[A-Za-z_]"))
                    return i;
            }
            return lines.Count;
        }

        static string ReadKey(List<string> lines, int start, int end, string key)
        {
            var re = new Regex(@"^\s+" + Regex.Escape(key) + @"\s*:\s*(.*)$");
            for (int i = start; i < end && i < lines.Count; i++)
            {
                var match = re.Match(lines[i]);
                if (match.Success)
                    return match.Groups[1].Value;
            }
            return null;
        }

        static string Clean(string value)
        {
            if (value == null)
                return null;
            value = value.Trim();
            int hash = value.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0)
                value = value.Substring(0, hash).Trim();
            return value.Trim().Trim('\'', '"');
        }

        static int Leading(string line)
        {
            int n = 0;
            while (n < line.Length && line[n] == ' ')
                n++;
            return n;
        }

        static List<string> Split(string yaml)
        {
            string normalized = yaml.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = new List<string>(normalized.Split('\n'));
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            return lines;
        }

        static string Join(List<string> lines, string nl, bool trailing)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    sb.Append(nl);
                sb.Append(lines[i]);
            }
            if (trailing)
                sb.Append(nl);
            return sb.ToString();
        }
    }

    public static class ClashConfigEditor
    {
        public static bool LooksLikeClash(string yaml)
        {
            if (string.IsNullOrEmpty(yaml))
                return false;
            return Regex.IsMatch(yaml, @"(?m)^(rules|proxies|proxy-groups|mixed-port|allow-lan)\s*:");
        }

        public static string PrependDirectRule(string yaml, string portalIp, out bool changed)
        {
            string rule = ClashRules.Format(portalIp);
            if (!string.IsNullOrEmpty(yaml) && yaml.IndexOf(rule, StringComparison.Ordinal) >= 0)
            {
                changed = false;
                return yaml;
            }
            if (yaml == null)
                yaml = "";
            string nl = yaml.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var lines = new List<string>(yaml.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'));
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Length == 0 || lines[i][0] == ' ' || lines[i][0] == '\t' || lines[i][0] == '#')
                    continue;
                string trim = lines[i].Trim();
                if (trim == "rules: []" || trim == "rules:[]")
                {
                    lines[i] = "rules:";
                    lines.Insert(i + 1, "  - " + rule);
                    changed = true;
                    return Join(lines, nl);
                }
                if (trim == "rules:" || trim.StartsWith("rules:", StringComparison.Ordinal))
                {
                    lines.Insert(i + 1, "  - " + rule);
                    changed = true;
                    return Join(lines, nl);
                }
            }
            if (lines.Count > 0 && lines[lines.Count - 1].Length != 0)
                lines.Add("");
            lines.Add("rules:");
            lines.Add("  - " + rule);
            changed = true;
            return Join(lines, nl);
        }

        static string Join(List<string> lines, string nl)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    sb.Append(nl);
                sb.Append(lines[i]);
            }
            sb.Append(nl);
            return sb.ToString();
        }
    }

    public static class FlClashConfigEditor
    {
        public static bool LooksLike(string json)
        {
            return !string.IsNullOrEmpty(json) && json.IndexOf("patchClashConfig", StringComparison.Ordinal) >= 0;
        }

        public static string AddPortalBypass(string json, string portalIp, out bool changed)
        {
            changed = false;
            if (!LooksLike(json))
                return json;
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Newtonsoft.Json.JsonException) { return json; }
            string cidr = portalIp + "/32";
            bool tun = EnsureArray(root, new[] { "patchClashConfig", "tun", "route-exclude-address" }, cidr);
            bool extra = EnsureArray(root, new[] { "networkProps", "bypassPrivateRouteAddress" }, cidr);
            changed = tun || extra;
            if (!changed)
                return json;
            return root.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        static bool EnsureArray(JObject root, string[] path, string value)
        {
            JObject current = root;
            for (int i = 0; i < path.Length - 1; i++)
            {
                JToken next = current[path[i]];
                JObject obj = next as JObject;
                if (obj == null)
                {
                    obj = new JObject();
                    current[path[i]] = obj;
                }
                current = obj;
            }
            string leaf = path[path.Length - 1];
            JArray array = current[leaf] as JArray;
            if (array == null)
            {
                array = new JArray();
                current[leaf] = array;
            }
            for (int i = 0; i < array.Count; i++)
            {
                if (string.Equals(array[i].ToString(), value, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            array.Add(value);
            return true;
        }
    }

    public static class SingBoxConfigEditor
    {
        public static bool LooksLike(string json)
        {
            if (string.IsNullOrEmpty(json))
                return false;
            return json.IndexOf("\"outbounds\"", StringComparison.Ordinal) >= 0
                && json.IndexOf("\"route\"", StringComparison.Ordinal) >= 0;
        }

        public static string AddDirectRule(string json, string portalIp, out bool changed)
        {
            changed = false;
            if (!LooksLike(json))
                return json;
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Newtonsoft.Json.JsonException) { return json; }
            JObject route = root["route"] as JObject;
            if (route == null)
            {
                route = new JObject();
                root["route"] = route;
            }
            JArray rules = route["rules"] as JArray;
            if (rules == null)
            {
                rules = new JArray();
                route["rules"] = rules;
            }
            string cidr = portalIp + "/32";
            for (int i = 0; i < rules.Count; i++)
            {
                JArray cidrs = rules[i]["ip_cidr"] as JArray;
                if (cidrs == null)
                    continue;
                for (int c = 0; c < cidrs.Count; c++)
                {
                    if (string.Equals(cidrs[c].ToString(), cidr, StringComparison.OrdinalIgnoreCase))
                        return json;
                }
            }
            rules.Insert(0, new JObject
            {
                ["ip_cidr"] = new JArray(cidr),
                ["outbound"] = "direct"
            });
            changed = true;
            return root.ToString(Newtonsoft.Json.Formatting.Indented);
        }
    }

    public static class RouteCommand
    {
        public static string AddArguments(string portalIp, string gateway, int interfaceIndex)
        {
            return "add " + portalIp + " mask 255.255.255.255 " + gateway + " metric 5 IF " + interfaceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string DeleteArguments(string portalIp)
        {
            return "delete " + portalIp;
        }
    }
}
