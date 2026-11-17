using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace XianAuth
{
    public static class DpapiCredentialStore
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("xian-renzheng-account-v1");

        public static void Save(AppPaths paths, string account, string password)
        {
            paths.Ensure();
            byte[] plain = Encoding.UTF8.GetBytes(CredentialDocument.ToJson(account, password));
            try
            {
                byte[] cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.LocalMachine);
                string temp = paths.CredentialFile + ".tmp";
                File.WriteAllBytes(temp, cipher);
                ProtectAcl(temp);
                if (File.Exists(paths.CredentialFile))
                    File.Delete(paths.CredentialFile);
                File.Move(temp, paths.CredentialFile);
                ProtectAcl(paths.CredentialFile);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        public static bool TryLoad(AppPaths paths, out string account, out string password)
        {
            account = "";
            password = "";
            if (!File.Exists(paths.CredentialFile))
                return false;
            byte[] cipher = File.ReadAllBytes(paths.CredentialFile);
            byte[] plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.LocalMachine);
            try
            {
                CredentialDocument.Read(Encoding.UTF8.GetString(plain), out account, out password);
                return true;
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        public static string Stamp(AppPaths paths)
        {
            if (!File.Exists(paths.CredentialFile))
                return "";
            return File.GetLastWriteTimeUtc(paths.CredentialFile).Ticks.ToString();
        }

        static void ProtectAcl(string path)
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
    }
}
