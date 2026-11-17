using System;
using System.Text.RegularExpressions;

namespace XianAuth
{
    public enum TriggerKind
    {
        Boot,
        Resume,
        Sleep,
        Wlan,
        Unlock,
        Interval,
        Manual,
        Logout
    }

    public enum BootKind
    {
        Unknown,
        Shutdown,
        Reboot,
        Hibernate
    }

    public enum ResumeKind
    {
        Unknown,
        Sleep,
        Hibernate
    }

    public sealed class PowerSignals
    {
        public BootKind Boot { get; set; }
        public ResumeKind Resume { get; set; }
    }

    public enum ConnectKind
    {
        Online,
        Captive,
        ProbeRedirect,
        Offline
    }

    public sealed class ConnectResult
    {
        public ConnectKind Kind { get; set; }
        public string RedirectUrl { get; set; }
        public string Detail { get; set; }
    }

    public static class ConnectProbe
    {
        public const string ProbeUrl = "http://www.msftconnecttest.com/connecttest.txt";
        public const string ExactBody = "Microsoft Connect Test";

        public static ConnectResult Interpret(int status, string body, string location, string error)
        {
            if (status == 200 && body == ExactBody)
                return new ConnectResult { Kind = ConnectKind.Online, Detail = "网络已连通" };

            if (IsRedirect(status) && !string.IsNullOrEmpty(location))
            {
                if (IsProbeHost(location))
                    return new ConnectResult { Kind = ConnectKind.ProbeRedirect, RedirectUrl = location, Detail = "探测地址发生同站重定向" };
                return new ConnectResult { Kind = ConnectKind.Captive, RedirectUrl = location, Detail = "捕获到门户重定向" };
            }

            string extracted = ExtractPortal(body);
            if (!string.IsNullOrEmpty(extracted))
                return new ConnectResult { Kind = ConnectKind.Captive, RedirectUrl = extracted, Detail = "从页面中发现门户地址" };

            if (!string.IsNullOrEmpty(error))
                return new ConnectResult { Kind = ConnectKind.Offline, Detail = error };
            return new ConnectResult { Kind = ConnectKind.Offline, Detail = "网络未连通" };
        }

        public static bool IsRedirect(int status)
        {
            return status == 301 || status == 302 || status == 303 || status == 307 || status == 308;
        }

        public static bool IsProbeHost(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                return false;
            return uri.Host.Equals("www.msftconnecttest.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("msftconnecttest.com", StringComparison.OrdinalIgnoreCase);
        }

        public static string ExtractPortal(string body)
        {
            if (string.IsNullOrEmpty(body))
                return null;
            var match = Regex.Match(body, @"https?://[^\s'""<>]+", RegexOptions.IgnoreCase);
            while (match.Success)
            {
                string url = match.Value;
                if (url.IndexOf("eportal", StringComparison.OrdinalIgnoreCase) >= 0
                    || url.IndexOf("srun", StringComparison.OrdinalIgnoreCase) >= 0
                    || url.IndexOf("InterFace.do", StringComparison.OrdinalIgnoreCase) >= 0)
                    return url;
                match = match.NextMatch();
            }
            return null;
        }
    }

    public static class PowerClassify
    {
        public static BootKind Boot(string text)
        {
            if (string.IsNullOrEmpty(text))
                return BootKind.Unknown;
            if (text.IndexOf("重新启动", StringComparison.Ordinal) >= 0
                || Regex.IsMatch(text, "(?i)shutdown type:\\s*restart"))
                return BootKind.Reboot;
            if (text.IndexOf("休眠", StringComparison.Ordinal) >= 0
                || Regex.IsMatch(text, "(?i)hibernate"))
                return BootKind.Hibernate;
            if (text.IndexOf("关闭电源", StringComparison.Ordinal) >= 0
                || text.IndexOf("关机", StringComparison.Ordinal) >= 0
                || Regex.IsMatch(text, "(?i)power off")
                || Regex.IsMatch(text, "(?i)shutdown"))
                return BootKind.Shutdown;
            return BootKind.Unknown;
        }

        public static ResumeKind Resume(int? targetState, int? effectiveState, int? reason)
        {
            if (reason == 6 || reason == 13)
                return ResumeKind.Hibernate;
            int? state = effectiveState ?? targetState;
            if (state == 4)
                return ResumeKind.Hibernate;
            if (state == 1 || state == 2 || state == 3)
                return ResumeKind.Sleep;
            return ResumeKind.Unknown;
        }
    }

    public static class TriggerPolicy
    {
        public static bool ShouldRun(TriggerKind trigger, AppConfig config, PowerSignals signals)
        {
            if (config == null)
                return false;
            if (signals == null)
                signals = new PowerSignals();

            switch (trigger)
            {
                case TriggerKind.Logout:
                case TriggerKind.Manual:
                    return true;
                case TriggerKind.Wlan:
                case TriggerKind.Unlock:
                    return config.AutoAuthEnabled;
                case TriggerKind.Interval:
                    return config.AutoAuthEnabled && config.IntervalEnabled;
                case TriggerKind.Sleep:
                    return config.AutoAuthEnabled && config.TriggerSleep;
                case TriggerKind.Boot:
                    if (!config.AutoAuthEnabled)
                        return false;
                    if (signals.Boot == BootKind.Reboot)
                        return config.TriggerReboot;
                    if (signals.Boot == BootKind.Shutdown)
                        return config.TriggerShutdown;
                    if (signals.Boot == BootKind.Hibernate)
                        return config.TriggerHibernate;
                    return config.TriggerShutdown;
                case TriggerKind.Resume:
                    if (!config.AutoAuthEnabled)
                        return false;
                    if (signals.Resume == ResumeKind.Sleep)
                        return config.TriggerSleep;
                    if (signals.Resume == ResumeKind.Hibernate)
                    {
                        bool fastStartup = signals.Boot == BootKind.Shutdown;
                        if (fastStartup && config.TriggerShutdown)
                            return true;
                        return config.TriggerHibernate;
                    }
                    return config.TriggerSleep || config.TriggerHibernate;
                default:
                    return false;
            }
        }
    }

    public static class RetryPlan
    {
        public const int MaxAttempts = 4;
        public const int BudgetMs = 150000;
        public static readonly int[] DelayBeforeAttemptMs = { 0, 15000, 35000, 70000 };

        public static bool CanStart(int attemptIndex, int elapsedMs)
        {
            if (attemptIndex < 0 || attemptIndex >= MaxAttempts)
                return false;
            if (attemptIndex == 0)
                return true;
            return elapsedMs + DelayBeforeAttemptMs[attemptIndex] <= BudgetMs;
        }
    }
}
