using System;
using System.Collections.Generic;
using System.IO;

namespace XianAuth
{
    public sealed class AppPaths
    {
        public AppPaths(string root)
        {
            Root = root;
        }

        public string Root { get; private set; }
        public string Logs { get { return Path.Combine(Root, "logs"); } }
        public string Profiles { get { return Path.Combine(Root, "profiles"); } }
        public string Backups { get { return Path.Combine(Root, "backups"); } }
        public string ConfigFile { get { return Path.Combine(Root, "config.json"); } }
        public string CredentialFile { get { return Path.Combine(Root, "account.dat"); } }
        public string ChangeFile { get { return Path.Combine(Root, "配置变更记录.json"); } }
        public string StateFile { get { return Path.Combine(Root, "state.json"); } }

        public static AppPaths ForBase()
        {
            return new AppPaths(AppDomain.CurrentDomain.BaseDirectory);
        }

        public void Ensure()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Logs);
            Directory.CreateDirectory(Profiles);
            Directory.CreateDirectory(Backups);
        }
    }

    public sealed class AppConfig
    {
        int _intervalMinutes = 10;

        public int ConfigVersion { get; set; }
        public bool AutoAuthEnabled { get; set; }
        public bool TriggerShutdown { get; set; }
        public bool TriggerReboot { get; set; }
        public bool TriggerHibernate { get; set; }
        public bool TriggerSleep { get; set; }
        public bool VpnDirect { get; set; }
        public bool IntervalEnabled { get; set; }
        public bool AutoConnectWifi { get; set; }
        public int CredentialFailureLimit { get; set; }
        public string TargetSsid { get; set; }
        public string ActiveProfileId { get; set; }

        public int IntervalMinutes
        {
            get { return _intervalMinutes; }
            set { _intervalMinutes = value < 1 ? 1 : (value > 720 ? 720 : value); }
        }

        public AppConfig()
        {
            ConfigVersion = 1;
            TriggerShutdown = true;
            TriggerReboot = true;
            TriggerHibernate = true;
            TriggerSleep = true;
            AutoConnectWifi = true;
            CredentialFailureLimit = 2;
            IntervalMinutes = 10;
            TargetSsid = "";
            ActiveProfileId = "";
        }

        public static AppConfig CreateDefault()
        {
            return new AppConfig();
        }
    }

    public sealed class RuntimeState
    {
        public int ConsecutiveCredentialFailures { get; set; }
        public string CredentialStampUtc { get; set; }
        public string LastSummary { get; set; }
        public string LastRunUtc { get; set; }
    }

    public sealed class ChangeRecord
    {
        public string Id { get; set; }
        public string TimeUtc { get; set; }
        public string Kind { get; set; }
        public string Group { get; set; }
        public string Target { get; set; }
        public string BackupFile { get; set; }
    }

    public sealed class ChangeJournal
    {
        public ChangeJournal()
        {
            Records = new List<ChangeRecord>();
        }

        public int Version { get; set; }
        public List<ChangeRecord> Records { get; set; }

        public ChangeRecord Add(string kind, string group, string target, string backupFile)
        {
            var record = new ChangeRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                TimeUtc = DateTime.UtcNow.ToString("o"),
                Kind = kind,
                Group = group,
                Target = target,
                BackupFile = backupFile
            };
            Records.Add(record);
            return record;
        }

        public List<ChangeRecord> RollbackOrder(string groupOrNull)
        {
            var list = new List<ChangeRecord>();
            if (Records == null)
                return list;
            for (int i = Records.Count - 1; i >= 0; i--)
            {
                var record = Records[i];
                if (groupOrNull == null || string.Equals(record.Group, groupOrNull, StringComparison.Ordinal))
                    list.Add(record);
            }
            return list;
        }
    }

    public static class CredentialDocument
    {
        public static string ToJson(string account, string password)
        {
            return JsonDefaults.Serialize(new CredentialPlain
            {
                Account = account ?? "",
                Password = password ?? ""
            });
        }

        public static void Read(string json, out string account, out string password)
        {
            var plain = JsonDefaults.Deserialize<CredentialPlain>(json) ?? new CredentialPlain();
            account = plain.Account ?? "";
            password = plain.Password ?? "";
        }

        sealed class CredentialPlain
        {
            public string Account { get; set; }
            public string Password { get; set; }
        }
    }

    public static class CredentialEdit
    {
        public static void Resolve(string typedAccount, string typedPassword, string savedAccount, string savedPassword, out string account, out string password)
        {
            account = string.IsNullOrEmpty(typedAccount) ? (savedAccount ?? "") : typedAccount.Trim();
            password = string.IsNullOrEmpty(typedPassword) ? (savedPassword ?? "") : typedPassword;
        }
    }

    public static class CredentialLock
    {
        public static void SyncStamp(RuntimeState state, string stamp)
        {
            if (state == null)
                return;
            if (!string.Equals(state.CredentialStampUtc, stamp, StringComparison.Ordinal))
            {
                state.ConsecutiveCredentialFailures = 0;
                state.CredentialStampUtc = stamp;
            }
        }

        public static bool IsLocked(RuntimeState state, AppConfig config)
        {
            if (state == null || config == null)
                return false;
            if (config.CredentialFailureLimit <= 0)
                return false;
            return state.ConsecutiveCredentialFailures >= config.CredentialFailureLimit;
        }
    }
}
