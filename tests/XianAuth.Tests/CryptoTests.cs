using NUnit.Framework;

namespace XianAuth.Tests
{
    public class SrunCryptoTests
    {
        [Test]
        public void Build_MatchesPublishedVector()
        {
            var parts = SrunPortal.Build("20230001", "p@ssw0rd", "10.1.2.3", "1", "a1b2c3d4e5");
            Assert.AreEqual("{\"username\":\"20230001\",\"password\":\"p@ssw0rd\",\"ip\":\"10.1.2.3\",\"acid\":\"1\",\"enc_ver\":\"srun_bx1\"}", parts.InfoJson);
            Assert.AreEqual("{SRBX1}QD4jEcUxPcIPq5Xvqru5uigGjEMdynF54oqByUrP6sc9k5iXa5Qp7LrATOHhTrMit6ADX/OAycf3M1tKl64NM7iSE7MB23ybuI64+04Jz9cQPSdS7GtKSEDF/PnbcKxkSGRMH+==", parts.Info);
            Assert.AreEqual("0c3505f11e66368f6d304bbdfcb131c0", parts.Hmd5);
            Assert.AreEqual("b1893e796c8d8b77ac12e2ea4cdc0bcae6a6b927", parts.Chksum);
        }

        [Test]
        public void AlreadyOnlineMessage_IsSuccessClass()
        {
            var outcome = SrunPortal.Interpret("jcb({\"error\":\"already_online\",\"error_msg\":\"You are already online.\"})");
            Assert.AreEqual(AuthStatus.AlreadyOnline, outcome.Status);
        }
    }

    public class RuijieCryptoTests
    {
        [Test]
        public void RawRsa_MatchesPythonVector()
        {
            string hex = RuijiePortal.EncryptPasswordHex("secret", "10001", "b1e0c0ffee1234567890abcd");
            Assert.AreEqual("ad048f637475c6114fc6df93", hex);
        }
    }
}
