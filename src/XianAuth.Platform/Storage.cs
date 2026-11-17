using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XianAuth
{
    public static class Storage
    {
        public static AppConfig LoadConfig(AppPaths paths)
        {
            try
            {
                if (!File.Exists(paths.ConfigFile))
                    return AppConfig.CreateDefault();
                var config = JsonDefaults.Deserialize<AppConfig>(File.ReadAllText(paths.ConfigFile, Encoding.UTF8));
                return config ?? AppConfig.CreateDefault();
            }
            catch (Exception ex)
            {
                DailyLog.Error("读取配置失败，已使用默认配置：" + ex.Message);
                return AppConfig.CreateDefault();
            }
        }

        public static void SaveConfig(AppPaths paths, AppConfig config)
        {
            paths.Ensure();
            File.WriteAllText(paths.ConfigFile, JsonDefaults.Serialize(config), new UTF8Encoding(false));
        }

        public static RuntimeState LoadState(AppPaths paths)
        {
            try
            {
                if (!File.Exists(paths.StateFile))
                    return new RuntimeState();
                return JsonDefaults.Deserialize<RuntimeState>(File.ReadAllText(paths.StateFile, Encoding.UTF8)) ?? new RuntimeState();
            }
            catch (Exception ex)
            {
                DailyLog.Error("读取状态失败：" + ex.Message);
                return new RuntimeState();
            }
        }

        public static void SaveState(AppPaths paths, RuntimeState state)
        {
            paths.Ensure();
            if (state == null)
                state = new RuntimeState();
            state.LastRunUtc = DateTime.UtcNow.ToString("o");
            File.WriteAllText(paths.StateFile, JsonDefaults.Serialize(state), new UTF8Encoding(false));
        }

        public static ChangeJournal LoadJournal(AppPaths paths)
        {
            try
            {
                if (!File.Exists(paths.ChangeFile))
                    return new ChangeJournal { Version = 1 };
                var journal = JsonDefaults.Deserialize<ChangeJournal>(File.ReadAllText(paths.ChangeFile, Encoding.UTF8));
                if (journal == null)
                    return new ChangeJournal { Version = 1 };
                if (journal.Records == null)
                    journal.Records = new List<ChangeRecord>();
                return journal;
            }
            catch (Exception ex)
            {
                DailyLog.Error("读取配置变更记录失败：" + ex.Message);
                return new ChangeJournal { Version = 1 };
            }
        }

        public static void SaveJournal(AppPaths paths, ChangeJournal journal)
        {
            paths.Ensure();
            if (journal == null)
                journal = new ChangeJournal { Version = 1 };
            journal.Version = 1;
            File.WriteAllText(paths.ChangeFile, JsonDefaults.Serialize(journal), new UTF8Encoding(false));
        }

        public static List<PortalProfile> LoadProfiles(AppPaths paths)
        {
            paths.Ensure();
            var list = new List<PortalProfile>();
            string[] files = Directory.GetFiles(paths.Profiles, "*.json");
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    var profile = JsonDefaults.Deserialize<PortalProfile>(File.ReadAllText(files[i], Encoding.UTF8));
                    if (profile != null && !string.IsNullOrEmpty(profile.Id))
                        list.Add(profile);
                }
                catch (Exception ex)
                {
                    DailyLog.Error("无法读取门户配置：" + ex.Message);
                }
            }
            return list;
        }

        public static void SaveProfile(AppPaths paths, PortalProfile profile)
        {
            paths.Ensure();
            string name = string.IsNullOrEmpty(profile.Id) ? "captured" : profile.Id;
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');
            File.WriteAllText(Path.Combine(paths.Profiles, name + ".json"), JsonDefaults.Serialize(profile), new UTF8Encoding(false));
        }
    }
}
