using System;
using System.IO;
using NUnit.Framework;

namespace XianAuth.WinTests
{
    public class TaskRegistrationTests
    {
        const string Folder = "\\XianRenzheng-Ci";

        [Test]
        public void Plan_ContainsLogonAndUnlockWithoutDelay()
        {
            var config = new AppConfig { AutoAuthEnabled = true };
            var plan = TaskDefinitions.Plan(config, @"C:\Xian\XianAuthCore.exe");

            ScheduledTaskSpec boot = plan.Find(spec => spec.Name == TaskDefinitions.BootName);
            ScheduledTaskSpec logon = plan.Find(spec => spec.Name == TaskDefinitions.LogonName);
            ScheduledTaskSpec unlock = plan.Find(spec => spec.Name == TaskDefinitions.UnlockName);
            ScheduledTaskSpec wlan = plan.Find(spec => spec.Name == TaskDefinitions.WlanName);

            Assert.IsNotNull(boot, "缺少开机任务");
            Assert.IsNotNull(logon, "缺少登录任务");
            Assert.IsNotNull(unlock, "缺少解锁任务");
            Assert.IsNotNull(wlan, "缺少 WLAN 8001 任务");

            StringAssert.Contains("--trigger logon", logon.Xml);
            StringAssert.Contains("<LogonTrigger>", logon.Xml);
            Assert.IsFalse(logon.Xml.Contains("<Delay>"), "登录触发不应加延迟");

            StringAssert.Contains("SessionUnlock", unlock.Xml);
            Assert.IsFalse(unlock.Xml.Contains("<Delay>"), "解锁触发不应加延迟");

            StringAssert.Contains("EventID=8001", wlan.Xml);
            StringAssert.Contains("PT20S", boot.Xml);

            Assert.IsNull(TaskDefinitions.Validate(logon.Xml));
            Assert.IsNull(TaskDefinitions.Validate(unlock.Xml));
            Assert.IsNull(TaskDefinitions.Validate(wlan.Xml));
        }

        [Test]
        public void AllNames_CoversLogonSoItCanBeDeleted()
        {
            CollectionAssert.Contains(TaskDefinitions.AllNames, TaskDefinitions.LogonName);
            CollectionAssert.Contains(TaskDefinitions.AllNames, TaskDefinitions.UnlockName);
        }

        [Test]
        public void RegisterAndDelete_AllTriggerShapes()
        {
            string exe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var probes = TaskDefinitions.SchemaProbes(exe);
            ScheduledTaskService.DeleteNamedFolder(Folder);
            try
            {
                for (int i = 0; i < probes.Count; i++)
                {
                    string problem = TaskDefinitions.Validate(probes[i].Xml);
                    Assert.IsNull(problem, probes[i].Name + " " + problem);
                    ScheduledTaskService.RegisterRaw(Folder, probes[i].Name, probes[i].Xml);
                }
            }
            finally
            {
                ScheduledTaskService.DeleteNamedFolder(Folder);
            }
        }
    }
}
