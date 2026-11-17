using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace XianAuth
{
    public sealed class ScheduledTaskSpec
    {
        public string Name { get; set; }
        public string Xml { get; set; }
    }

    public static class TaskDefinitions
    {
        public const string Folder = "\\XianRenzheng";
        public const string BootName = "XianAuth-Boot";
        public const string ResumeName = "XianAuth-Resume";
        public const string SleepName = "XianAuth-Sleep";
        public const string WlanName = "XianAuth-Wlan";
        public const string UnlockName = "XianAuth-Unlock";
        public const string IntervalName = "XianAuth-Interval";

        public static readonly string[] AllNames = { BootName, ResumeName, SleepName, WlanName, UnlockName, IntervalName };

        public static List<ScheduledTaskSpec> Plan(AppConfig config, string exePath)
        {
            var list = new List<ScheduledTaskSpec>();
            if (config == null || !config.AutoAuthEnabled || string.IsNullOrEmpty(exePath))
                return list;
            if (config.TriggerShutdown || config.TriggerReboot)
            {
                list.Add(new ScheduledTaskSpec
                {
                    Name = BootName,
                    Xml = BuildXml(exePath, "--trigger boot", "闲·认证：开机", BootTrigger())
                });
            }
            if (config.TriggerSleep || config.TriggerHibernate)
            {
                string triggers = EventTrigger("System", "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and (EventID=1)]]")
                    + EventTrigger("System", "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=107)]]");
                list.Add(new ScheduledTaskSpec
                {
                    Name = ResumeName,
                    Xml = BuildXml(exePath, "--trigger resume", "闲·认证：睡眠或休眠恢复", triggers)
                });
            }
            if (config.TriggerSleep)
            {
                list.Add(new ScheduledTaskSpec
                {
                    Name = SleepName,
                    Xml = BuildXml(exePath, "--trigger sleep", "闲·认证：现代待机恢复",
                        EventTrigger("System", "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=507)]]"))
                });
            }
            list.Add(new ScheduledTaskSpec
            {
                Name = WlanName,
                Xml = BuildXml(exePath, "--trigger wlan", "闲·认证：无线网络已连接",
                    EventTrigger("Microsoft-Windows-WLAN-AutoConfig/Operational", "*[System[(EventID=8001)]]"))
            });
            list.Add(new ScheduledTaskSpec
            {
                Name = UnlockName,
                Xml = BuildXml(exePath, "--trigger unlock", "闲·认证：会话解锁",
                    "<SessionStateChangeTrigger><Enabled>true</Enabled><StateChange>SessionUnlock</StateChange></SessionStateChangeTrigger>")
            });
            if (config.IntervalEnabled)
            {
                int minutes = config.IntervalMinutes < 1 ? 1 : config.IntervalMinutes;
                string trigger = "<TimeTrigger><Repetition><Interval>PT" + minutes.ToString(CultureInfo.InvariantCulture)
                    + "M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition><StartBoundary>2026-01-01T00:00:00</StartBoundary><Enabled>true</Enabled></TimeTrigger>";
                list.Add(new ScheduledTaskSpec
                {
                    Name = IntervalName,
                    Xml = BuildXml(exePath, "--trigger interval", "闲·认证：间隔检测", trigger)
                });
            }
            return list;
        }

        public static string BootTrigger()
        {
            return "<BootTrigger><Enabled>true</Enabled><Delay>PT20S</Delay></BootTrigger>";
        }

        public static string LogonTrigger()
        {
            return "<LogonTrigger><Enabled>true</Enabled></LogonTrigger>";
        }

        public static List<ScheduledTaskSpec> SchemaProbes(string exePath)
        {
            return new List<ScheduledTaskSpec>
            {
                new ScheduledTaskSpec { Name = "Probe-Boot", Xml = BuildXml(exePath, "--trigger boot", "probe", BootTrigger()) },
                new ScheduledTaskSpec { Name = "Probe-Logon", Xml = BuildXml(exePath, "--trigger boot", "probe", LogonTrigger()) },
                new ScheduledTaskSpec { Name = "Probe-KernelPower107", Xml = BuildXml(exePath, "--trigger resume", "probe", EventTrigger("System", "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=107)]]")) },
                new ScheduledTaskSpec { Name = "Probe-PowerTroubleshooter1", Xml = BuildXml(exePath, "--trigger resume", "probe", EventTrigger("System", "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and (EventID=1)]]")) },
                new ScheduledTaskSpec { Name = "Probe-Wlan8001", Xml = BuildXml(exePath, "--trigger wlan", "probe", EventTrigger("Microsoft-Windows-WLAN-AutoConfig/Operational", "*[System[(EventID=8001)]]")) },
                new ScheduledTaskSpec { Name = "Probe-Unlock", Xml = BuildXml(exePath, "--trigger unlock", "probe", "<SessionStateChangeTrigger><Enabled>true</Enabled><StateChange>SessionUnlock</StateChange></SessionStateChangeTrigger>") },
                new ScheduledTaskSpec { Name = "Probe-Interval", Xml = BuildXml(exePath, "--trigger interval", "probe", "<TimeTrigger><Repetition><Interval>PT10M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition><StartBoundary>2026-01-01T00:00:00</StartBoundary><Enabled>true</Enabled></TimeTrigger>") }
            };
        }

        public static string EventTrigger(string log, string xpath)
        {
            string query = "<QueryList><Query Id=\"0\" Path=\"" + log + "\"><Select Path=\"" + log + "\">" + xpath + "</Select></Query></QueryList>";
            return "<EventTrigger><Enabled>true</Enabled><Subscription>" + XmlEscape(query) + "</Subscription></EventTrigger>";
        }

        public static string BuildXml(string exe, string arguments, string description, string triggersXml)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            sb.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            sb.Append("<RegistrationInfo><Description>");
            sb.Append(XmlEscape(description));
            sb.Append("</Description></RegistrationInfo>");
            sb.Append("<Triggers>");
            sb.Append(triggersXml);
            sb.Append("</Triggers>");
            sb.Append("<Principals><Principal id=\"Author\"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>");
            sb.Append("<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            sb.Append("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            sb.Append("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            sb.Append("<AllowHardTerminate>true</AllowHardTerminate>");
            sb.Append("<StartWhenAvailable>true</StartWhenAvailable>");
            sb.Append("<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
            sb.Append("<AllowStartOnDemand>true</AllowStartOnDemand>");
            sb.Append("<Enabled>true</Enabled><Hidden>true</Hidden>");
            sb.Append("<ExecutionTimeLimit>PT5M</ExecutionTimeLimit><Priority>7</Priority></Settings>");
            sb.Append("<Actions Context=\"Author\"><Exec><Command>");
            sb.Append(XmlEscape(exe));
            sb.Append("</Command><Arguments>");
            sb.Append(XmlEscape(arguments));
            sb.Append("</Arguments></Exec></Actions></Task>");
            return sb.ToString();
        }

        public static readonly string[] AllowedLogonTypes = {
            "S4U", "Password", "InteractiveToken", "InteractiveTokenOrPassword", "Group"
        };

        public static string Validate(string xml)
        {
            if (string.IsNullOrEmpty(xml))
                return "任务 XML 为空";
            if (xml.IndexOf("ServiceAccount", StringComparison.Ordinal) >= 0)
                return "LogonType ServiceAccount 不是任务计划 XML 的合法取值";
            int search = 0;
            while (true)
            {
                int at = xml.IndexOf("<LogonType>", search, StringComparison.Ordinal);
                if (at < 0)
                    break;
                int end = xml.IndexOf("</LogonType>", at, StringComparison.Ordinal);
                if (end < 0)
                    return "LogonType 没有结束标记";
                string value = xml.Substring(at + "<LogonType>".Length, end - (at + "<LogonType>".Length));
                bool allowed = false;
                for (int i = 0; i < AllowedLogonTypes.Length; i++)
                {
                    if (value == AllowedLogonTypes[i])
                        allowed = true;
                }
                if (!allowed)
                    return "不允许的 LogonType：" + value;
                search = end + 1;
            }
            if (xml.IndexOf("<UserId>S-1-5-18</UserId>", StringComparison.Ordinal) < 0)
                return "SYSTEM 任务缺少 UserId S-1-5-18";
            if (xml.IndexOf("<RunLevel>HighestAvailable</RunLevel>", StringComparison.Ordinal) < 0)
                return "缺少 RunLevel";
            return null;
        }

        public static string XmlEscape(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
