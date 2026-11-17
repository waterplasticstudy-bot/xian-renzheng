using System.Text.RegularExpressions;

namespace XianAuth
{
    public static class SecretRedactor
    {
        static readonly Regex Sensitive = new Regex(
            @"(?i)(user_password|user_account|username|userid|password|passwd|pwd|chksum|info)=([^&\s""]+)",
            RegexOptions.Compiled);

        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            return Sensitive.Replace(text, "$1=***");
        }
    }
}
