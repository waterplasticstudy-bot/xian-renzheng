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
    }
}
