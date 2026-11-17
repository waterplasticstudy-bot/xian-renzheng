using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace XianAuth
{
    public static class ScheduledTaskService
    {
        public static void Sync(AppConfig config, string exePath, ChangeJournal journal)
        {
            DeleteAll();
            if (journal.Records != null)
                journal.Records.RemoveAll(record => record.Group == "task");
            if (config == null || !config.AutoAuthEnabled)
                return;
            List<ScheduledTaskSpec> plan = TaskDefinitions.Plan(config, exePath);
            for (int i = 0; i < plan.Count; i++)
            {
                Register(plan[i]);
                journal.Add("task", "task", TaskDefinitions.Folder + "\\" + plan[i].Name, null);
            }
        }

        public static void DeleteAll()
        {
            dynamic folder = OpenFolder(false);
            if (folder == null)
                return;
            for (int i = 0; i < TaskDefinitions.AllNames.Length; i++)
            {
                try { folder.DeleteTask(TaskDefinitions.AllNames[i], 0); }
                catch (Exception) { }
            }
        }

        public static void DeleteOne(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return;
            string name = fullName;
            int slash = name.LastIndexOf('\\');
            if (slash >= 0)
                name = name.Substring(slash + 1);
            dynamic folder = OpenFolder(false);
            if (folder == null)
                return;
            try { folder.DeleteTask(name, 0); }
            catch (Exception) { }
        }

        public static void RegisterRaw(string folderPath, string name, string xml)
        {
            string problem = TaskDefinitions.Validate(xml);
            if (problem != null)
                throw new InvalidOperationException("计划任务 XML 无效：" + problem);
            dynamic folder = OpenNamedFolder(folderPath, true);
            // TASK_CREATE_OR_UPDATE = 6, TASK_LOGON_SERVICE_ACCOUNT = 5.
            // The XML must not contain LogonType ServiceAccount; SYSTEM is S-1-5-18.
            folder.RegisterTask(name, xml, 6, "SYSTEM", null, 5, null);
        }

        public static void DeleteNamedFolder(string folderPath)
        {
            Type type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null)
                return;
            dynamic service = Activator.CreateInstance(type);
            service.Connect();
            string name = folderPath ?? "";
            if (name.StartsWith("\\", StringComparison.Ordinal))
                name = name.Substring(1);
            try
            {
                dynamic root = service.GetFolder("\\");
                root.DeleteFolder(name, 0);
            }
            catch (Exception)
            {
            }
        }

        static void Register(ScheduledTaskSpec spec)
        {
            try
            {
                RegisterRaw(TaskDefinitions.Folder, spec.Name, spec.Xml);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("计划任务注册失败：" + ex.Message, ex);
            }
        }

        static dynamic OpenFolder(bool create)
        {
            return OpenNamedFolder(TaskDefinitions.Folder, create);
        }

        static dynamic OpenNamedFolder(string folderPath, bool create)
        {
            Type type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null)
                throw new InvalidOperationException("无法创建任务计划服务");
            dynamic service = Activator.CreateInstance(type);
            service.Connect();
            try
            {
                return service.GetFolder(folderPath);
            }
            catch (Exception)
            {
                if (!create)
                    return null;
                string name = folderPath ?? "";
                if (name.StartsWith("\\", StringComparison.Ordinal))
                    name = name.Substring(1);
                dynamic root = service.GetFolder("\\");
                return root.CreateFolder(name, null);
            }
        }
    }

    public static class PowerEvents
    {
        public static PowerSignals Read()
        {
            var signals = new PowerSignals();
            try
            {
                signals.Boot = PowerClassify.Boot(ReadLatest("System", "*[System[Provider[@Name='User32'] and (EventID=1074)]]"));
                string resumeXml = ReadLatest("System", "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=42)]]");
                signals.Resume = PowerClassify.Resume(NamedInt(resumeXml, "TargetState"), NamedInt(resumeXml, "EffectiveState"), NamedInt(resumeXml, "Reason"));
            }
            catch (Exception ex)
            {
                DailyLog.Error("读取电源事件失败：" + ex.Message);
            }
            return signals;
        }

        static string ReadLatest(string log, string xpath)
        {
            var query = new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true };
            using (var reader = new EventLogReader(query))
            using (EventRecord record = reader.ReadEvent())
            {
                if (record == null)
                    return "";
                string xml = record.ToXml() ?? "";
                try
                {
                    string description = record.FormatDescription();
                    if (!string.IsNullOrEmpty(description))
                        xml = xml + "\n" + description;
                }
                catch (EventLogException)
                {
                }
                return xml;
            }
        }

        static int? NamedInt(string xml, string name)
        {
            if (string.IsNullOrEmpty(xml))
                return null;
            var match = Regex.Match(xml, "<Data Name=\"" + Regex.Escape(name) + "\">\\s*(-?\\d+)\\s*</Data>");
            if (!match.Success)
                return null;
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
    }

    public static class ShortcutService
    {
        public static void Ensure(string guiExe, ChangeJournal journal)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop))
                return;
            string link = Path.Combine(desktop, "闲·认证.lnk");
            if (File.Exists(link))
                return;
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null)
                throw new InvalidOperationException("无法创建桌面快捷方式");
            dynamic shell = Activator.CreateInstance(type);
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = guiExe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(guiExe);
            shortcut.IconLocation = guiExe + ",0";
            shortcut.Description = "闲·认证";
            shortcut.Save();
            journal.Add("shortcut", "shortcut", link, null);
        }
    }

    public static class RollbackService
    {
        public static void Rollback(AppPaths paths, string groupOrNull)
        {
            ChangeJournal journal = Storage.LoadJournal(paths);
            List<ChangeRecord> order = journal.RollbackOrder(groupOrNull);
            for (int i = 0; i < order.Count; i++)
            {
                ChangeRecord record = order[i];
                try
                {
                    Undo(paths, record);
                }
                catch (Exception ex)
                {
                    DailyLog.Error("回滚失败 " + record.Target + "：" + ex.Message);
                }
                journal.Records.Remove(record);
            }
            Storage.SaveJournal(paths, journal);
        }

        static void Undo(AppPaths paths, ChangeRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.Kind))
                return;
            if (record.Kind == "task")
            {
                ScheduledTaskService.DeleteOne(record.Target);
                return;
            }
            if (record.Kind == "shortcut" || record.Kind == "created-file")
            {
                if (!string.IsNullOrEmpty(record.Target) && File.Exists(record.Target))
                    File.Delete(record.Target);
                return;
            }
            if (record.Kind == "route")
            {
                PortalRoute.Delete(record.Target);
                return;
            }
            if (record.Kind == "file")
            {
                string backup = Path.Combine(paths.Backups, record.BackupFile ?? "");
                if (File.Exists(backup) && !string.IsNullOrEmpty(record.Target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(record.Target));
                    File.Copy(backup, record.Target, true);
                }
            }
        }
    }

    public sealed class ClashApplyResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
    }

    public static class ClashRuleService
    {
        public static ClashApplyResult Apply(AppPaths paths, ChangeJournal journal, string portalIp)
        {
            string normalized;
            if (!ClashRules.TryNormalizeIpv4(portalIp, out normalized))
                return new ClashApplyResult { Ok = false, Message = "门户地址不是可用的 IPv4，未修改分流规则。" };

            int changes = 0;
            var notes = new List<string>();
            foreach (string home in VergeHomes())
            {
                string profilesPath = Path.Combine(home, "profiles.yaml");
                if (!File.Exists(profilesPath))
                    continue;
                string yaml = File.ReadAllText(profilesPath, Encoding.UTF8);
                string profilesDir = Path.Combine(home, "profiles");
                VergeEdit edit = VergeProfilesEditor.Apply(yaml, normalized, delegate(string fileName)
                {
                    string path = Path.Combine(profilesDir, fileName);
                    return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
                });
                if (!edit.Ok)
                {
                    notes.Add(home + "：" + edit.Error);
                    continue;
                }
                if (edit.ProfilesChanged)
                {
                    Backup(paths, journal, profilesPath);
                    File.WriteAllText(profilesPath, edit.ProfilesYaml, new UTF8Encoding(false));
                    changes++;
                }
                Directory.CreateDirectory(profilesDir);
                for (int i = 0; i < edit.Files.Count; i++)
                {
                    VergeFileWrite file = edit.Files[i];
                    if (!file.Changed)
                        continue;
                    string target = Path.Combine(profilesDir, file.FileName);
                    if (file.Created || !File.Exists(target))
                        journal.Add("created-file", "vpn", target, null);
                    else
                        Backup(paths, journal, target);
                    File.WriteAllText(target, file.Content, new UTF8Encoding(false));
                    changes++;
                }
                notes.Add("已写入 Clash Verge Rev 前置规则 " + ClashRules.Format(normalized));
            }

            changes += EditClashFolder(paths, journal, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "clash"), normalized, notes, "Clash for Windows");
            string mihomo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "mihomo", "config.yaml");
            if (File.Exists(mihomo))
                changes += EditOne(paths, journal, mihomo, normalized, notes, "mihomo");
            changes += FlClashFamily.Apply(paths, journal, normalized, notes);
            changes += SingBoxFamily.Apply(paths, journal, normalized, notes);

            bool routed;
            string routeNote = PortalRoute.TryAdd(paths, journal, normalized, out routed);
            var head = new List<string> { VpnScanner.Describe() };
            if (!string.IsNullOrEmpty(routeNote))
                head.Add(routeNote);
            head.AddRange(notes);
            if (changes == 0 && !routed && notes.Count == 0)
                head.Add("没有写入持久规则。认证请求仍会绑定 Wi-Fi 地址。");
            return new ClashApplyResult
            {
                Ok = changes > 0 || routed,
                Message = string.Join("；", head.ToArray())
            };
        }

        static int EditClashFolder(AppPaths paths, ChangeJournal journal, string folder, string ip, List<string> notes, string label)
        {
            if (!Directory.Exists(folder))
                return 0;
            int changes = 0;
            string main = Path.Combine(folder, "config.yaml");
            if (File.Exists(main))
                changes += EditOne(paths, journal, main, ip, notes, label);
            string profiles = Path.Combine(folder, "profiles");
            if (Directory.Exists(profiles))
            {
                string[] files = Directory.GetFiles(profiles, "*.y*ml");
                for (int i = 0; i < files.Length; i++)
                    changes += EditOne(paths, journal, files[i], ip, notes, label);
            }
            return changes;
        }

        static int EditOne(AppPaths paths, ChangeJournal journal, string file, string ip, List<string> notes, string label)
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > 2 * 1024 * 1024)
                return 0;
            string yaml = File.ReadAllText(file, Encoding.UTF8);
            if (!ClashConfigEditor.LooksLikeClash(yaml))
                return 0;
            bool changed;
            string updated = ClashConfigEditor.PrependDirectRule(yaml, ip, out changed);
            if (!changed)
                return 0;
            Backup(paths, journal, file);
            File.WriteAllText(file, updated, new UTF8Encoding(false));
            notes.Add("已在 " + label + " 配置中加入直连规则");
            return 1;
        }

        static void Backup(AppPaths paths, ChangeJournal journal, string target)
        {
            paths.Ensure();
            string name = Guid.NewGuid().ToString("N") + ".bak";
            File.Copy(target, Path.Combine(paths.Backups, name), true);
            journal.Add("file", "vpn", target, name);
        }

        static IEnumerable<string> VergeHomes()
        {
            string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[]
            {
                Path.Combine(app, "io.github.clash-verge-rev.clash-verge-rev"),
                Path.Combine(app, "clash-verge"),
                Path.Combine(user, ".config", "clash-verge"),
                Path.Combine(user, ".config", "clash-verge-rev")
            };
        }
    }
}
