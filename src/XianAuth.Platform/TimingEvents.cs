using System;
using System.Diagnostics.Eventing.Reader;

namespace XianAuth
{
    /// <summary>
    /// 从 Windows 事件日志读取本轮认证相关的时间点。
    /// 任何一项读不到都只返回 null，绝不抛异常，也不影响认证本身。
    /// </summary>
    public sealed class TimingMarks
    {
        public DateTimeOffset? SystemBoot { get; set; }
        public DateTimeOffset? DesktopReady { get; set; }
        public DateTimeOffset? WifiConnected { get; set; }
    }

    public static class TimingEvents
    {
        const string SystemLog = "System";
        const string WlanLog = "Microsoft-Windows-WLAN-AutoConfig/Operational";

        public static TimingMarks Read(TriggerKind trigger)
        {
            var marks = new TimingMarks();
            try
            {
                marks.SystemBoot = ReadStartOrWake(trigger);
            }
            catch (Exception ex)
            {
                DailyLog.Error("读取启动时间失败：" + ex.Message);
            }
            try
            {
                marks.DesktopReady = ReadDesktopReady();
            }
            catch (Exception ex)
            {
                DailyLog.Error("读取进入桌面时间失败：" + ex.Message);
            }
            try
            {
                marks.WifiConnected = Latest(WlanLog, "*[System[(EventID=8001)]]");
            }
            catch (Exception ex)
            {
                DailyLog.Error("读取无线连接时间失败：" + ex.Message);
            }
            return marks;
        }

        /// <summary>
        /// 冷启动看 Kernel-General 12，唤醒看 Power-Troubleshooter 1。
        /// 两个事件都可能存在（快速启动），取较晚的一个作为本轮起点。
        /// </summary>
        static DateTimeOffset? ReadStartOrWake(TriggerKind trigger)
        {
            DateTimeOffset? boot = Latest(SystemLog, "*[System[Provider[@Name='Microsoft-Windows-Kernel-General'] and (EventID=12)]]");
            DateTimeOffset? wake = Latest(SystemLog, "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and (EventID=1)]]");
            if (trigger == TriggerKind.Boot || trigger == TriggerKind.Logon)
                return boot ?? wake;
            return Later(boot, wake) ?? boot ?? wake;
        }

        /// <summary>
        /// 进入桌面：优先 Winlogon 7001，读不到时退回本程序「登录」「解锁」任务的最近运行时间。
        /// </summary>
        static DateTimeOffset? ReadDesktopReady()
        {
            DateTimeOffset? winlogon = Latest(SystemLog, "*[System[Provider[@Name='Microsoft-Windows-Winlogon'] and (EventID=7001)]]");
            DateTimeOffset? unlockTask = LatestTaskRun(TaskDefinitions.UnlockName);
            DateTimeOffset? logonTask = LatestTaskRun(TaskDefinitions.LogonName);
            DateTimeOffset? task = Later(unlockTask, logonTask);
            return Later(winlogon, task) ?? winlogon ?? task;
        }

        static DateTimeOffset? Latest(string log, string xpath)
        {
            try
            {
                var query = new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true };
                using (var reader = new EventLogReader(query))
                using (EventRecord record = reader.ReadEvent())
                {
                    if (record == null || !record.TimeCreated.HasValue)
                        return null;
                    DateTime value = record.TimeCreated.Value;
                    if (value.Kind == DateTimeKind.Unspecified)
                        value = DateTime.SpecifyKind(value, DateTimeKind.Local);
                    return new DateTimeOffset(value).ToUniversalTime();
                }
            }
            catch (Exception)
            {
                // 日志被关闭、事件不存在、权限不足都视为「读不到」。
                return null;
            }
        }

        static DateTimeOffset? LatestTaskRun(string taskName)
        {
            try
            {
                Type type = Type.GetTypeFromProgID("Schedule.Service");
                if (type == null)
                    return null;
                dynamic service = Activator.CreateInstance(type);
                service.Connect();
                dynamic folder = service.GetFolder(TaskDefinitions.Folder);
                dynamic task = folder.GetTask(taskName);
                DateTime last = (DateTime)task.LastRunTime;
                if (last.Year < 2000)
                    return null;
                return new DateTimeOffset(DateTime.SpecifyKind(last, DateTimeKind.Local)).ToUniversalTime();
            }
            catch (Exception)
            {
                return null;
            }
        }

        static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b)
        {
            if (!a.HasValue)
                return b;
            if (!b.HasValue)
                return a;
            return a.Value >= b.Value ? a : b;
        }
    }
}
