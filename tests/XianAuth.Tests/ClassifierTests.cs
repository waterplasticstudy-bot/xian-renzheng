using System.Collections.Generic;
using NUnit.Framework;

namespace XianAuth.Tests
{
    public class ClassifierTests
    {
        static ClassifyHints Hints()
        {
            return new ClassifyHints { Account = "20230001", Password = "s3cret-pass", Ip = "10.1.2.3", Mac = "AABBCCDDEEFF" };
        }

        [Test]
        public void SingleGet_ReplacesAccountPasswordIpAndDoesNotKeepSecret()
        {
            var requests = new List<CapturedRequest>
            {
                new CapturedRequest
                {
                    Method = "GET",
                    Url = "http://10.0.0.1/login?user_account=%2Cb%2C20230001&user_password=s3cret-pass&wlan_user_ip=10.1.2.3&wlan_user_mac=000000000000&callback=dr1700000000123&_=1700000000123"
                }
            };
            var result = RequestClassifier.Classify(requests, Hints(), "CAMPUS");
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual("GET", result.ChineseKind);
            Assert.AreEqual("single-get", result.TemplateKind);
            string url = result.Profile.Steps[0].Url;
            StringAssert.Contains("user_password={password}", url);
            StringAssert.Contains("%2Cb%2C{account}", url);
            StringAssert.Contains("wlan_user_ip={ip}", url);
            StringAssert.Contains("wlan_user_mac={mac_zero}", url);
            StringAssert.Contains("callback=dr{timestamp}", url);
            StringAssert.DoesNotContain("s3cret-pass", url);
            StringAssert.DoesNotContain("s3cret-pass", JsonDefaults.Serialize(result.Profile));
        }

        [Test]
        public void SinglePost_IsClassified()
        {
            var requests = new List<CapturedRequest>
            {
                new CapturedRequest
                {
                    Method = "POST",
                    Url = "http://portal.example/login",
                    ContentType = "application/x-www-form-urlencoded",
                    Body = "username=20230001&password=s3cret-pass",
                    ResponseBody = "{\"result\":\"success\"}"
                }
            };
            var result = RequestClassifier.Classify(requests, Hints(), "CAMPUS");
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual("单POST", result.ChineseKind);
            StringAssert.Contains("password={password}", result.Profile.Steps[0].Body);
            StringAssert.DoesNotContain("s3cret-pass", result.Profile.Steps[0].Body);
            CollectionAssert.Contains(result.Profile.SuccessContains, "\"result\":\"success\"");
        }

        [Test]
        public void DoublePost_ExtractsTokenFromFirstResponse()
        {
            var requests = new List<CapturedRequest>
            {
                new CapturedRequest { Method = "GET", Url = "http://portal.example/app.js" },
                new CapturedRequest
                {
                    Method = "POST",
                    Url = "http://portal.example/challenge",
                    Body = "user=20230001",
                    ResponseBody = "{\"challenge\":\"toktoktok1\"}"
                },
                new CapturedRequest
                {
                    Method = "POST",
                    Url = "http://portal.example/login",
                    ContentType = "application/x-www-form-urlencoded",
                    Body = "username=20230001&password=s3cret-pass&token=toktoktok1",
                    ResponseBody = "登录成功"
                }
            };
            var result = RequestClassifier.Classify(requests, Hints(), "CAMPUS");
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual("双POST", result.ChineseKind);
            Assert.AreEqual(2, result.Profile.Steps.Count);
            StringAssert.Contains("{challenge}", result.Profile.Steps[1].Body);
            StringAssert.Contains("{password}", result.Profile.Steps[1].Body);
            StringAssert.DoesNotContain("toktoktok1", result.Profile.Steps[1].Body);
            StringAssert.DoesNotContain("s3cret-pass", JsonDefaults.Serialize(result.Profile));
            Assert.AreEqual("challenge", result.Profile.Steps[0].Extract[0].Name);
        }

        [Test]
        public void Put_IsClassified()
        {
            var requests = new List<CapturedRequest>
            {
                new CapturedRequest
                {
                    Method = "PUT",
                    Url = "http://portal.example/session",
                    ContentType = "application/json",
                    Body = "{\"user\":\"20230001\",\"password\":\"s3cret-pass\"}",
                    ResponseBody = "{\"result\":1}"
                }
            };
            var result = RequestClassifier.Classify(requests, Hints(), null);
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual("PUT", result.ChineseKind);
            StringAssert.DoesNotContain("s3cret-pass", result.Profile.Steps[0].Body);
        }

        [Test]
        public void HarImport_DropsCookieAndClassifiesPost()
        {
            const string har = "{\"log\":{\"entries\":[{\"request\":{\"method\":\"POST\",\"url\":\"http://portal.example/login\",\"headers\":[{\"name\":\"Cookie\",\"value\":\"session=SUPERSECRET\"},{\"name\":\"Content-Type\",\"value\":\"application/x-www-form-urlencoded\"}],\"cookies\":[{\"name\":\"session\",\"value\":\"SUPERSECRET\"}],\"postData\":{\"mimeType\":\"application/x-www-form-urlencoded\",\"text\":\"user=20230001&password=s3cret-pass\"}},\"response\":{\"status\":200,\"cookies\":[{\"name\":\"sid\",\"value\":\"SUPERSECRET\"}],\"content\":{\"text\":\"认证成功\"}}}]}}";
            var requests = HarImport.Read(har);
            Assert.AreEqual(1, requests.Count);
            var result = RequestClassifier.Classify(requests, Hints(), "CAMPUS");
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual("单POST", result.ChineseKind);
            string saved = JsonDefaults.Serialize(result.Profile);
            StringAssert.DoesNotContain("SUPERSECRET", saved);
            StringAssert.DoesNotContain("s3cret-pass", saved);
        }

        [Test]
        public void PastedAdminUrl_IsRejected()
        {
            var result = RequestClassifier.FromPastedUrl("http://portal.example.edu/eportal/?c=main&a=login", Hints(), "MyCampus-WiFi");
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("后台", result.Error);
        }

        [Test]
        public void StaticAsset_IsIgnored()
        {
            var requests = new List<CapturedRequest>
            {
                new CapturedRequest { Method = "GET", Url = "http://portal.example/logo.png?password=s3cret-pass" }
            };
            var result = RequestClassifier.Classify(requests, Hints(), null);
            Assert.IsFalse(result.Ok);
        }
    }
}
