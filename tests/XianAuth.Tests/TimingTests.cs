using System;
using NUnit.Framework;

namespace XianAuth.Tests
{
    public class TimingTests
    {
        static readonly DateTimeOffset Success = new DateTimeOffset(2026, 11, 17, 12, 0, 30, TimeSpan.Zero);

        [Test]
        public void ComputesBootAndDesktopSeconds()
        {
            var boot = Success.AddSeconds(-90);
            var desktop = Success.AddSeconds(-12.5);
            AuthTiming timing = TimingCalculator.Compute(TriggerKind.Boot, boot, desktop, Success.AddSeconds(-30), Success);

            Assert.AreEqual(90.0, timing.BootToAuthSeconds);
            Assert.AreEqual(12.5, timing.DesktopToAuthSeconds);
            Assert.AreEqual("Boot", timing.Trigger);
            Assert.IsNotNull(timing.AuthSuccessUtc);
        }

        [Test]
        public void MissingEventTimesDoNotThrow()
        {
            AuthTiming timing = TimingCalculator.Compute(TriggerKind.Unlock, null, null, null, Success);

            Assert.IsNull(timing.BootToAuthSeconds);
            Assert.IsNull(timing.DesktopToAuthSeconds);
            Assert.IsNull(timing.SystemBootUtc);
            Assert.IsNull(timing.DesktopReadyUtc);
            Assert.IsNull(timing.WifiConnectedUtc);
            Assert.IsNotNull(timing.AuthSuccessUtc);
        }

        [Test]
        public void FutureOrStaleTimesAreDropped()
        {
            Assert.IsNull(TimingCalculator.SecondsBetween(Success.AddSeconds(5), Success));
            Assert.IsNull(TimingCalculator.SecondsBetween(Success.AddDays(-2), Success));
            Assert.AreEqual(0.0, TimingCalculator.SecondsBetween(Success, Success));
        }

        [Test]
        public void DescribePrefersDesktopTime()
        {
            var timing = new AuthTiming { BootToAuthSeconds = 95, DesktopToAuthSeconds = 7.4 };
            Assert.AreEqual("上次认证：进桌面后 7.4 秒完成", TimingCalculator.Describe(timing));
        }

        [Test]
        public void DescribeFallsBackToBootTime()
        {
            var timing = new AuthTiming { BootToAuthSeconds = 42 };
            Assert.AreEqual("上次认证：开机后 42 秒完成", TimingCalculator.Describe(timing));
        }

        [Test]
        public void DescribeWithoutTimingIsEmpty()
        {
            Assert.AreEqual("", TimingCalculator.Describe(null));
            Assert.AreEqual("", TimingCalculator.Describe(new AuthTiming()));
        }

        [Test]
        public void DeadlineDefaultsAndClamps()
        {
            Assert.AreEqual(15, new AppConfig().AuthDeadlineSeconds);
            Assert.AreEqual(5, new AppConfig { AuthDeadlineSeconds = 1 }.AuthDeadlineSeconds);
            Assert.AreEqual(120, new AppConfig { AuthDeadlineSeconds = 999 }.AuthDeadlineSeconds);
            Assert.AreEqual(30, new AppConfig { AuthDeadlineSeconds = 30 }.AuthDeadlineSeconds);
        }

        [Test]
        public void DesktopSecondsRejectsStaleLogonEvent()
        {
            // 间隔检测每 10 分钟触发一次，Winlogon 7001 仍是当天早上登录的时间。
            // 这类「几小时前」的起点必须丢掉，不能算出进桌面后 21600 秒。
            Assert.IsNull(TimingCalculator.SecondsBetween(Success.AddHours(-6), Success, TimingCalculator.MaxDesktopSeconds));
            Assert.IsNull(TimingCalculator.SecondsBetween(Success.AddSeconds(-1801), Success, TimingCalculator.MaxDesktopSeconds));
            Assert.AreEqual(1800.0, TimingCalculator.SecondsBetween(Success.AddSeconds(-1800), Success, TimingCalculator.MaxDesktopSeconds));
        }

        [Test]
        public void ComputeDropsStaleDesktopButKeepsBoot()
        {
            var timing = TimingCalculator.Compute(TriggerKind.Interval,
                Success.AddSeconds(-400), Success.AddHours(-6), null, Success);

            Assert.AreEqual(400.0, timing.BootToAuthSeconds);
            Assert.IsNull(timing.DesktopToAuthSeconds);
            Assert.AreEqual("上次认证：开机后 400 秒完成", TimingCalculator.Describe(timing));
        }

        [Test]
        public void AlreadyOnlineIsNotCountedAsAuthentication()
        {
            // 本来就是通的说明这一轮没提交凭据，不应覆盖上一次真实认证的耗时。
            Assert.IsTrue(AuthOutcome.AlreadyOnline("网络已连通").IsOnline);
            Assert.AreEqual(AuthStatus.Success, AuthOutcome.Success("认证成功").Status);
            Assert.AreNotEqual(AuthStatus.Success, AuthOutcome.AlreadyOnline("网络已连通").Status);
        }
    }
}
