using System;
using System.IO;

namespace XianAuth
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
                AppPaths paths = AppPaths.ForBase();
                paths.Ensure();
                DailyLog.Paths = paths;
                using (SingleInstance single = SingleInstance.TryEnter("Global\\XianRenzheng.Core"))
                {
                    if (single == null)
                    {
                        DailyLog.Info("已有一次认证正在运行，本次退出。");
                        return 0;
                    }
                    if (Has(args, "--rollback"))
                    {
                        RollbackService.Rollback(paths, null);
                        DailyLog.Info("已按配置变更记录回滚。");
                        return 0;
                    }
                    return AuthEngine.Run(ParseTrigger(args)).ExitCode;
                }
            }
            catch (Exception ex)
            {
                try { DailyLog.Error(ex.Message); } catch (Exception) { }
                return 1;
            }
        }

        private static TriggerKind ParseTrigger(string[] args)
        {
            if (Has(args, "--logout"))
                return TriggerKind.Logout;
            string value = ArgValue(args, "--trigger");
            switch (value)
            {
                case "boot": return TriggerKind.Boot;
                case "logon": return TriggerKind.Logon;
                case "resume": return TriggerKind.Resume;
                case "sleep": return TriggerKind.Sleep;
                case "wlan": return TriggerKind.Wlan;
                case "unlock": return TriggerKind.Unlock;
                case "interval": return TriggerKind.Interval;
                case "logout": return TriggerKind.Logout;
                default: return TriggerKind.Manual;
            }
        }

        private static bool Has(string[] args, string name)
        {
            if (args == null)
                return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string ArgValue(string[] args, string name)
        {
            if (args == null)
                return "";
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1] ?? "";
            }
            return "";
        }
    }
}
