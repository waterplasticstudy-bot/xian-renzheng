using System;
using System.Globalization;

namespace XianAuth
{
    /// <summary>
    /// 一次认证的耗时记录。所有时间点都以 UTC ISO 8601 字符串保存，
    /// 便于 state.json 直接阅读，也不受本机时区变化影响。
    /// </summary>
    public sealed class AuthTiming
    {
        public string Trigger { get; set; }
        public string SystemBootUtc { get; set; }
        public string DesktopReadyUtc { get; set; }
        public string WifiConnectedUtc { get; set; }
        public string AuthSuccessUtc { get; set; }
        public double? BootToAuthSeconds { get; set; }
        public double? DesktopToAuthSeconds { get; set; }
        public string Note { get; set; }
    }

    /// <summary>
    /// 耗时计算只做纯计算，方便在没有 Windows 事件日志的环境里跑单元测试。
    /// </summary>
    public static class TimingCalculator
    {
        public const double MaxReasonableSeconds = 86400;

        public static AuthTiming Compute(
            TriggerKind trigger,
            DateTimeOffset? systemBoot,
            DateTimeOffset? desktopReady,
            DateTimeOffset? wifiConnected,
            DateTimeOffset authSuccess)
        {
            var timing = new AuthTiming
            {
                Trigger = trigger.ToString(),
                AuthSuccessUtc = Iso(authSuccess)
            };
            if (systemBoot.HasValue)
                timing.SystemBootUtc = Iso(systemBoot.Value);
            if (desktopReady.HasValue)
                timing.DesktopReadyUtc = Iso(desktopReady.Value);
            if (wifiConnected.HasValue)
                timing.WifiConnectedUtc = Iso(wifiConnected.Value);

            timing.BootToAuthSeconds = SecondsBetween(systemBoot, authSuccess);
            timing.DesktopToAuthSeconds = SecondsBetween(desktopReady, authSuccess);
            return timing;
        }

        public static double? SecondsBetween(DateTimeOffset? from, DateTimeOffset to)
        {
            if (!from.HasValue)
                return null;
            double seconds = (to - from.Value).TotalSeconds;
            // 事件时间比认证时间还晚，或跨了一天以上，说明读到的不是本轮事件。
            if (seconds < 0 || seconds > MaxReasonableSeconds)
                return null;
            return Math.Round(seconds, 1);
        }

        /// <summary>
        /// 状态栏文案。优先展示「进桌面到认证成功」，读不到桌面时间时退回开机时间。
        /// </summary>
        public static string Describe(AuthTiming timing)
        {
            if (timing == null)
                return "";
            if (timing.DesktopToAuthSeconds.HasValue)
                return "上次认证：进桌面后 " + Format(timing.DesktopToAuthSeconds.Value) + " 秒完成";
            if (timing.BootToAuthSeconds.HasValue)
                return "上次认证：开机后 " + Format(timing.BootToAuthSeconds.Value) + " 秒完成";
            return "";
        }

        public static string Format(double seconds)
        {
            return seconds.ToString("0.#", CultureInfo.InvariantCulture);
        }

        static string Iso(DateTimeOffset value)
        {
            return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }
    }
}
