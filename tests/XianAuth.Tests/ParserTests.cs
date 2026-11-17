using NUnit.Framework;

namespace XianAuth.Tests
{
    public class JsonpParserTests
    {
        [Test]
        public void ResultOne_IsSuccess()
        {
            var outcome = JsonpParser.InterpretDrcom("dr1700000000000({\"result\":1,\"msg\":\"Portal协议认证成功！\"});");
            Assert.AreEqual(AuthStatus.Success, outcome.Status);
            Assert.AreEqual("Portal协议认证成功！", outcome.Message);
        }

        [Test]
        public void StringRetCodeTwo_IsAlreadyOnline()
        {
            var outcome = JsonpParser.InterpretDrcom("dr1({\"result\":0,\"msg\":\"已经在线\",\"ret_code\":\"2\"})");
            Assert.AreEqual(AuthStatus.AlreadyOnline, outcome.Status);
        }

        [Test]
        public void IntegerRetCodeTwo_IsAlreadyOnline()
        {
            var outcome = JsonpParser.InterpretDrcom("dr1({\"result\":\"0\",\"ret_code\":2})");
            Assert.AreEqual(AuthStatus.AlreadyOnline, outcome.Status);
        }

        [TestCase("\"20\"")]
        [TestCase("\"200\"")]
        [TestCase("20")]
        [TestCase("200")]
        [TestCase("\"02\"")]
        [TestCase("\"2 \"")]
        public void NearMissRetCode_IsNotAlreadyOnline(string literal)
        {
            var outcome = JsonpParser.InterpretDrcom("dr1({\"result\":0,\"msg\":\"失败\",\"ret_code\":" + literal + "})");
            Assert.AreNotEqual(AuthStatus.AlreadyOnline, outcome.Status);
            Assert.AreNotEqual(AuthStatus.Success, outcome.Status);
        }

        [Test]
        public void PasswordError_IsCredentialRejection_AndNotPrefixOfTwenty()
        {
            var outcome = JsonpParser.InterpretDrcom("dr1({\"result\":0,\"msg\":\"密码错误\",\"ret_code\":1})");
            Assert.AreEqual(AuthStatus.CredentialRejected, outcome.Status);
            Assert.IsFalse(outcome.ShouldRetry);
        }

        [Test]
        public void AcFailure_IsRejectedWithoutCredentialLockoutClass()
        {
            var outcome = JsonpParser.InterpretDrcom("dr1({\"result\":0,\"msg\":\"AC认证失败\"})");
            Assert.AreEqual(AuthStatus.Rejected, outcome.Status);
        }

        [Test]
        public void MalformedBody_IsTransient()
        {
            var outcome = JsonpParser.InterpretDrcom("not-json");
            Assert.AreEqual(AuthStatus.Transient, outcome.Status);
        }

        [Test]
        public void BraceInsideString_DoesNotTruncateJson()
        {
            var outcome = JsonpParser.InterpretDrcom("cb({\"result\":1,\"msg\":\"ok } still\"});");
            Assert.AreEqual(AuthStatus.Success, outcome.Status);
            Assert.AreEqual("ok } still", outcome.Message);
        }
    }

    public class DrcomUrlTests
    {
        static DrcomOptions Example()
        {
            return new DrcomOptions
            {
                BaseUrl = "http://portal.example.edu/eportal/",
                UserAccountPrefix = ",b,",
                Mac = "000000000000",
                AcIp = "",
                AcName = "example-ac",
                JsVersion = "3.0",
                LoginMethod = "1"
            };
        }

        [Test]
        public void LoginUrl_UsesProfileParameters_AndNeverUsesAdminPortal()
        {
            var url = DrcomEportal.BuildLoginUrl(Example(), "user@test", "p@ss w", "10.0.0.1", 1700000000000L);
            StringAssert.Contains("http://portal.example.edu/eportal/?c=Portal&a=login", url);
            StringAssert.Contains("callback=dr1700000000000", url);
            StringAssert.Contains("user_account=%2Cb%2Cuser%40test", url);
            StringAssert.Contains("user_password=p%40ss%20w", url);
            StringAssert.Contains("wlan_user_ip=10.0.0.1", url);
            StringAssert.Contains("wlan_user_mac=000000000000", url);
            StringAssert.Contains("wlan_ac_ip=", url);
            StringAssert.Contains("wlan_ac_name=example-ac", url);
            StringAssert.Contains("jsVersion=3.0", url);
            StringAssert.Contains("_=1700000000000", url);
            StringAssert.DoesNotContain("c=main", url);
            Assert.IsFalse(DrcomEportal.IsForbiddenAdminUrl(url));
        }

        [Test]
        public void LogoutUrl_OmitsPassword()
        {
            var url = DrcomEportal.BuildLogoutUrl(Example(), "10.0.0.1", 42);
            StringAssert.Contains("a=logout", url);
            StringAssert.DoesNotContain("user_password", url);
            StringAssert.DoesNotContain("c=main", url);
        }

        [Test]
        public void Redactor_HidesPasswordQuery()
        {
            string raw = DrcomEportal.BuildLoginUrl(Example(), "user", "s3cret", "10.0.0.1", 1);
            string redacted = SecretRedactor.Redact(raw);
            StringAssert.Contains("user_password=***", redacted);
            StringAssert.DoesNotContain("s3cret", redacted);
        }

        [Test]
        public void AdminLogin_IsRejectedByLooksLike()
        {
            const string admin = "http://portal.example.edu/eportal/?c=main&a=login";
            Assert.IsTrue(DrcomEportal.IsForbiddenAdminUrl(admin));
            Assert.IsFalse(DrcomEportal.LooksLike(admin));
        }
    }
}
