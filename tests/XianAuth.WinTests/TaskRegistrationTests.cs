using System;
using System.IO;
using NUnit.Framework;

namespace XianAuth.WinTests
{
    public class TaskRegistrationTests
    {
        const string Folder = "\\XianRenzheng-Ci";

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
