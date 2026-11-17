using System.Collections.Generic;
using NUnit.Framework;

namespace XianAuth.Tests
{
    public class ConnectProbeTests
    {
        [Test]
        public void ExactBody_IsOnline()
        {
            var result = ConnectProbe.Interpret(200, "Microsoft Connect Test", null, null);
            Assert.AreEqual(ConnectKind.Online, result.Kind);
        }

        [Test]
        public void TrailingNewline_IsNotOnline()
        {
            var result = ConnectProbe.Interpret(200, "Microsoft Connect Test\n", null, null);
            Assert.AreNotEqual(ConnectKind.Online, result.Kind);
        }

        [Test]
        public void RedirectToPortal_IsCaptive()
        {
            var result = ConnectProbe.Interpret(302, "", "http://portal.example.edu/eportal/?c=Portal&a=login&wlanuserip=10.0.0.1", null);
            Assert.AreEqual(ConnectKind.Captive, result.Kind);
            StringAssert.Contains("portal.example.edu", result.RedirectUrl);
        }

        [Test]
        public void RedirectToSameProbe_IsNotCaptive()
        {
            var result = ConnectProbe.Interpret(302, "", "https://www.msftconnecttest.com/connecttest.txt", null);
            Assert.AreEqual(ConnectKind.ProbeRedirect, result.Kind);
        }

        [Test]
        public void HtmlBody_ExtractsEportal()
        {
            var result = ConnectProbe.Interpret(200, "<script>location.href='http://1.2.3.4/eportal/index.jsp?w=1'</script>", null, null);
            Assert.AreEqual(ConnectKind.Captive, result.Kind);
            StringAssert.Contains("index.jsp", result.RedirectUrl);
        }
    }

    public class TriggerPolicyTests
    {
        static AppConfig Config()
        {
            return new AppConfig
            {
                AutoAuthEnabled = true,
                TriggerShutdown = false,
                TriggerReboot = false,
                TriggerHibernate = false,
                TriggerSleep = false
            };
        }

        [Test]
        public void WlanAndUnlock_RunWheneverAutoIsOn()
        {
            var config = Config();
            Assert.IsTrue(TriggerPolicy.ShouldRun(TriggerKind.Wlan, config, new PowerSignals()));
            Assert.IsTrue(TriggerPolicy.ShouldRun(TriggerKind.Unlock, config, new PowerSignals()));
            config.AutoAuthEnabled = false;
            Assert.IsFalse(TriggerPolicy.ShouldRun(TriggerKind.Wlan, config, new PowerSignals()));
        }

        [Test]
        public void Boot_RespectsShutdownAndRebootSeparately()
        {
            var config = Config();
            config.TriggerShutdown = true;
            Assert.IsTrue(TriggerPolicy.ShouldRun(TriggerKind.Boot, config, new PowerSignals { Boot = BootKind.Shutdown }));
            Assert.IsFalse(TriggerPolicy.ShouldRun(TriggerKind.Boot, config, new PowerSignals { Boot = BootKind.Reboot }));
            config.TriggerShutdown = false;
            config.TriggerReboot = true;
            Assert.IsTrue(TriggerPolicy.ShouldRun(TriggerKind.Boot, config, new PowerSignals { Boot = BootKind.Reboot }));
        }

        [Test]
        public void Resume_DistinguishesSleepHibernateAndFastStartup()
        {
            var config = Config();
            config.TriggerSleep = true;
            Assert.IsTrue(TriggerPolicy.ShouldRun(TriggerKind.Resume, config, new PowerSignals { Resume = ResumeKind.Sleep }));
            Assert.IsFalse(TriggerPolicy.ShouldRun(TriggerKind.Resume, config, new PowerSignals { Resume = ResumeKind.Hibernate, Boot = BootKind.Hibernate }));
            config.TriggerSleep = false;
            config.TriggerShutdown = true;
            Assert.IsTrue(TriggerPolicy.ShouldRun(TriggerKind.Resume, config, new PowerSignals { Resume = ResumeKind.Hibernate, Boot = BootKind.Shutdown }));
            config.TriggerShutdown = false;
            config.TriggerHibernate = true;
            Assert.IsTrue(TriggerPolicy.ShouldRun(TriggerKind.Resume, config, new PowerSignals { Resume = ResumeKind.Hibernate, Boot = BootKind.Hibernate }));
        }

        [Test]
        public void Interval_RequiresTheCheckbox()
        {
            var config = Config();
            Assert.IsFalse(TriggerPolicy.ShouldRun(TriggerKind.Interval, config, new PowerSignals()));
            config.IntervalEnabled = true;
            Assert.IsTrue(TriggerPolicy.ShouldRun(TriggerKind.Interval, config, new PowerSignals()));
        }

        [Test]
        public void PowerText_PrefersRestartOverTheWordShutdown()
        {
            Assert.AreEqual(BootKind.Reboot, PowerClassify.Boot("关机类型: 重新启动"));
            Assert.AreEqual(BootKind.Shutdown, PowerClassify.Boot("关机类型: 关机"));
            Assert.AreEqual(BootKind.Hibernate, PowerClassify.Boot("关机类型: 休眠"));
            Assert.AreEqual(ResumeKind.Hibernate, PowerClassify.Resume(4, 4, 0));
            Assert.AreEqual(ResumeKind.Sleep, PowerClassify.Resume(3, 3, 0));
            Assert.AreEqual(ResumeKind.Hibernate, PowerClassify.Resume(3, 3, 6));
        }
    }

    public class RetryAndSelectorTests
    {
        [Test]
        public void Budget_AllowsFourAttemptsAndStopsPast150Seconds()
        {
            Assert.IsTrue(RetryPlan.CanStart(0, 0));
            Assert.IsTrue(RetryPlan.CanStart(1, 0));
            Assert.IsTrue(RetryPlan.CanStart(3, 0));
            Assert.IsFalse(RetryPlan.CanStart(3, 100000));
            Assert.IsFalse(RetryPlan.CanStart(4, 0));
            Assert.AreEqual(150000, RetryPlan.DelayBeforeAttemptMs[1] + RetryPlan.DelayBeforeAttemptMs[2] + RetryPlan.DelayBeforeAttemptMs[3] + 30000);
        }

        [Test]
        public void SavedProfile_IsSelectedBySsid()
        {
            var profile = new PortalProfile
            {
                Id = "example-drcom",
                Kind = "drcom-eportal",
                Ssids = new List<string> { "MyCampus-WiFi" },
                Drcom = new DrcomOptions { UserAccountPrefix = ",b,", BaseUrl = "http://portal.example.edu/eportal/" }
            };
            var selected = PortalSelector.Select(new List<PortalProfile> { profile }, "example-drcom", "MyCampus-WiFi", null);
            Assert.AreEqual("example-drcom", selected.Id);
            Assert.AreEqual(",b,", selected.Drcom.UserAccountPrefix);
        }

        [Test]
        public void DrcomRedirect_KeepsHostAndAccountPrefix()
        {
            var plain = PortalSelector.Synthesize("MyCampus-WiFi", "http://portal.example.edu/eportal/?c=Portal&a=login&wlan_ac_name=example-ac");
            Assert.AreEqual("drcom-eportal", plain.Kind);
            Assert.AreEqual(",0,", plain.Drcom.UserAccountPrefix);
            Assert.AreEqual("example-ac", plain.Drcom.AcName);
            Assert.AreEqual("http://portal.example.edu/eportal/", plain.Drcom.BaseUrl);

            var prefixed = PortalSelector.Synthesize("MyCampus-WiFi", "http://portal.example.edu/eportal/?c=Portal&a=login&user_account=%2Cb%2Cstudent&wlan_ac_name=example-ac");
            Assert.AreEqual(",b,", prefixed.Drcom.UserAccountPrefix);
        }

        [Test]
        public void SrunAndRuijie_AreFingerprinted()
        {
            var srun = PortalSelector.Synthesize("MyCampus-WiFi", "http://portal.example.edu/srun_portal_pc.php?ac_id=3");
            Assert.AreEqual("srun", srun.Kind);
            Assert.AreEqual("3", srun.Srun.AcId);
            var ruijie = PortalSelector.Synthesize("MyCampus-WiFi", "http://portal.example.edu/eportal/index.jsp?wlanuserip=10.0.0.1");
            Assert.AreEqual("ruijie", ruijie.Kind);
        }

        [Test]
        public void CredentialLock_ResetsWhenStampChanges()
        {
            var state = new RuntimeState { ConsecutiveCredentialFailures = 5, CredentialStampUtc = "old" };
            var config = new AppConfig();
            Assert.IsTrue(CredentialLock.IsLocked(state, config));
            CredentialLock.SyncStamp(state, "new");
            Assert.AreEqual(0, state.ConsecutiveCredentialFailures);
            Assert.IsFalse(CredentialLock.IsLocked(state, config));
        }
    }

    public class ClashAndTaskTests
    {
        const string Profiles = "# Profiles Config for Clash Verge\ncurrent: abc\nitems:\n- uid: Merge\n  type: merge\n  file: Merge.yaml\n- uid: Script\n  type: script\n  file: Script.js\n- uid: abc\n  type: remote\n  name: campus\n  file: abc.yaml\n  option:\n    rules: null\n";

        [Test]
        public void VergeRules_UseExactDirectCidr_AndStayIdempotent()
        {
            var reads = new Dictionary<string, string>();
            VergeEdit first = VergeProfilesEditor.Apply(Profiles, "10.0.0.1", delegate(string name)
            {
                string value;
                return reads.TryGetValue(name, out value) ? value : null;
            });
            Assert.IsTrue(first.Ok, first.Error);
            StringAssert.Contains("# Profiles Config for Clash Verge", first.ProfilesYaml);
            StringAssert.Contains("rules: xian-renzheng-rules", first.ProfilesYaml);
            StringAssert.DoesNotContain("type: merge\n  file: Merge.yaml\n  option:", first.ProfilesYaml);
            Assert.AreEqual(1, first.Files.Count);
            Assert.AreEqual("xian-renzheng-rules.yaml", first.Files[0].FileName);
            Assert.AreEqual("prepend:\n  - IP-CIDR,10.0.0.1/32,DIRECT,no-resolve\nappend: []\ndelete: []\n", first.Files[0].Content);
            reads[first.Files[0].FileName] = first.Files[0].Content;

            VergeEdit second = VergeProfilesEditor.Apply(first.ProfilesYaml, "10.0.0.1", delegate(string name)
            {
                string value;
                return reads.TryGetValue(name, out value) ? value : null;
            });
            Assert.IsTrue(second.Ok, second.Error);
            Assert.IsFalse(second.ProfilesChanged);
            Assert.IsFalse(second.Files[0].Changed);
        }

        [Test]
        public void ClashConfig_PrependsRuleOnce()
        {
            const string yaml = "mixed-port: 7890\nrules:\n  - MATCH,PROXY\n";
            bool changed;
            string updated = ClashConfigEditor.PrependDirectRule(yaml, "10.0.0.1", out changed);
            Assert.IsTrue(changed);
            StringAssert.StartsWith("mixed-port: 7890\nrules:\n  - IP-CIDR,10.0.0.1/32,DIRECT,no-resolve\n", updated);
            bool again;
            ClashConfigEditor.PrependDirectRule(updated, "10.0.0.1", out again);
            Assert.IsFalse(again);
        }

        [Test]
        public void FlClash_AddsRouteExcludeWithoutTogglingTun()
        {
            const string json = "{\"patchClashConfig\":{\"mixed-port\":7890,\"tun\":{\"device\":\"Bettbox\",\"route-exclude-address\":[\"10.0.0.0/8\"]}},\"networkProps\":{\"bypassPrivateRouteAddress\":[]}}";
            bool changed;
            string updated = FlClashConfigEditor.AddPortalBypass(json, "10.0.0.1", out changed);
            Assert.IsTrue(changed);
            StringAssert.Contains("10.0.0.1/32", updated);
            StringAssert.Contains("10.0.0.0/8", updated);
            StringAssert.DoesNotContain("enable", updated);
            bool again;
            FlClashConfigEditor.AddPortalBypass(updated, "10.0.0.1", out again);
            Assert.IsFalse(again);
        }

        [Test]
        public void SingBox_PrependsDirectCidrOnce()
        {
            const string json = "{\"outbounds\":[{\"type\":\"direct\",\"tag\":\"direct\"}],\"route\":{\"rules\":[]}}";
            bool changed;
            string updated = SingBoxConfigEditor.AddDirectRule(json, "10.0.0.1", out changed);
            Assert.IsTrue(changed);
            StringAssert.Contains("10.0.0.1/32", updated);
            bool again;
            SingBoxConfigEditor.AddDirectRule(updated, "10.0.0.1", out again);
            Assert.IsFalse(again);
        }

        [Test]
        public void RouteCommand_TargetsWifiInterface()
        {
            Assert.AreEqual("add 10.0.0.1 mask 255.255.255.255 10.0.0.254 metric 5 IF 12", RouteCommand.AddArguments("10.0.0.1", "10.0.0.254", 12));
            Assert.AreEqual("delete 10.0.0.1", RouteCommand.DeleteArguments("10.0.0.1"));
        }

        [Test]
        public void FakeIp_IsRejected()
        {
            string ip;
            Assert.IsFalse(ClashRules.TryNormalizeIpv4("198.18.0.8", out ip));
            Assert.IsTrue(ClashRules.TryNormalizeIpv4("10.0.0.1", out ip));
            Assert.AreEqual("10.0.0.1", ip);
        }

        [Test]
        public void Tasks_RegisterBootWlanUnlockAndIntervalOnlyWhenEnabled()
        {
            var config = AppConfig.CreateDefault();
            Assert.AreEqual(0, TaskDefinitions.Plan(config, "C:\\XianAuthCore.exe").Count);
            config.AutoAuthEnabled = true;
            config.IntervalEnabled = true;
            config.IntervalMinutes = 10;
            var plan = TaskDefinitions.Plan(config, "C:\\XianAuthCore.exe");
            string all = "";
            for (int i = 0; i < plan.Count; i++)
                all += plan[i].Name + "\n" + plan[i].Xml + "\n";
            StringAssert.Contains("XianAuth-Boot", all);
            StringAssert.Contains("XianAuth-Wlan", all);
            StringAssert.Contains("XianAuth-Unlock", all);
            StringAssert.Contains("XianAuth-Interval", all);
            StringAssert.Contains("IgnoreNew", all);
            StringAssert.Contains("S-1-5-18", all);
            StringAssert.DoesNotContain("LogonType", all);
            StringAssert.DoesNotContain("ServiceAccount", all);
            Assert.IsNull(TaskDefinitions.Validate(plan[0].Xml));
            StringAssert.Contains("EventID=8001", all);
            StringAssert.Contains("SessionUnlock", all);
            StringAssert.Contains("PT10M", all);
            StringAssert.Contains("PT5M", all);
            StringAssert.Contains("--trigger boot", all);
            StringAssert.Contains("Kernel-Power", all);
            StringAssert.Contains("Power-Troubleshooter", all);
        }

        [Test]
        public void Journal_RollsBackNewestFirst()
        {
            var journal = new ChangeJournal();
            journal.Add("file", "vpn", "a", "1");
            journal.Add("task", "task", "b", null);
            journal.Add("shortcut", "shortcut", "c", null);
            var order = journal.RollbackOrder(null);
            Assert.AreEqual("c", order[0].Target);
            Assert.AreEqual("b", order[1].Target);
            Assert.AreEqual("a", order[2].Target);
            var vpn = journal.RollbackOrder("vpn");
            Assert.AreEqual(1, vpn.Count);
            Assert.AreEqual("a", vpn[0].Target);
        }

        [Test]
        public void SchemaProbes_OmitInvalidLogonType()
        {
            var probes = TaskDefinitions.SchemaProbes("C:\\Windows\\System32\\cmd.exe");
            Assert.AreEqual(7, probes.Count);
            for (int i = 0; i < probes.Count; i++)
            {
                Assert.IsNull(TaskDefinitions.Validate(probes[i].Xml), probes[i].Name);
                StringAssert.Contains("S-1-5-18", probes[i].Xml);
            }
            string joined = "";
            for (int i = 0; i < probes.Count; i++)
                joined += probes[i].Xml;
            StringAssert.Contains("BootTrigger", joined);
            StringAssert.Contains("LogonTrigger", joined);
            StringAssert.Contains("EventID=107", joined);
            StringAssert.Contains("Power-Troubleshooter", joined);
            StringAssert.Contains("EventID=8001", joined);
            StringAssert.Contains("SessionUnlock", joined);
            StringAssert.Contains("PT10M", joined);
        }

        [Test]
        public void ServiceAccountLogonType_IsRejected()
        {
            string bad = TaskDefinitions.BuildXml("C:\\app.exe", "--trigger boot", "x", TaskDefinitions.BootTrigger())
                .Replace("<RunLevel>", "<LogonType>ServiceAccount</LogonType><RunLevel>");
            StringAssert.Contains("ServiceAccount", TaskDefinitions.Validate(bad));
        }

        [Test]
        public void EmptyPassword_KeepsStoredPassword()
        {
            string account;
            string password;
            CredentialEdit.Resolve("student", "", "student", "stored-secret", out account, out password);
            Assert.AreEqual("student", account);
            Assert.AreEqual("stored-secret", password);
            CredentialEdit.Resolve("student", "new-secret", "student", "stored-secret", out account, out password);
            Assert.AreEqual("new-secret", password);
        }

        [Test]
        public void Redactor_HidesAccountAndPassword()
        {
            string raw = "http://portal.example.edu/eportal/?user_account=%2Cb%2Cstudent&user_password=s3cret&wlan_user_ip=10.0.0.1";
            string redacted = SecretRedactor.Redact(raw);
            StringAssert.Contains("user_password=***", redacted);
            StringAssert.Contains("user_account=***", redacted);
            StringAssert.DoesNotContain("s3cret", redacted);
            StringAssert.DoesNotContain("student", redacted);
            StringAssert.Contains("wlan_user_ip=10.0.0.1", redacted);
        }

        [Test]
        public void CredentialDocument_RoundTrip()
        {
            string json = CredentialDocument.ToJson("stu", "p@ss");
            string account;
            string password;
            CredentialDocument.Read(json, out account, out password);
            Assert.AreEqual("stu", account);
            Assert.AreEqual("p@ss", password);
        }
    }
}
