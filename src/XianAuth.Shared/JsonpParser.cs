using System;
using Newtonsoft.Json.Linq;

namespace XianAuth
{
    public static class RetCode
    {
        // 只接受 JSON 字符串 "2" 或整数 2。禁止前缀/Contains 判断，
        // 因此 "20"、"200"、20、200 都不是“已经在线”。
        public static bool IsExactlyTwo(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return false;
            if (token.Type == JTokenType.String)
                return (string)token == "2";
            if (token.Type == JTokenType.Integer)
                return token.Value<long>() == 2L;
            return false;
        }

        public static bool IsExactlyOne(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return false;
            if (token.Type == JTokenType.String)
                return (string)token == "1";
            if (token.Type == JTokenType.Integer)
                return token.Value<long>() == 1L;
            return false;
        }
    }

    public static class JsonpParser
    {
        public static bool TryParse(string body, out JObject json)
        {
            json = null;
            if (string.IsNullOrWhiteSpace(body))
                return false;
            int start = body.IndexOf('{');
            if (start < 0)
                return false;
            int end = FindMatchingBrace(body, start);
            if (end < 0)
                return false;
            try
            {
                json = JObject.Parse(body.Substring(start, end - start + 1));
                return true;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return false;
            }
        }

        public static AuthOutcome InterpretDrcom(string body)
        {
            JObject json;
            if (!TryParse(body, out json))
                return AuthOutcome.Transient("门户返回无法解析");

            var result = json["result"];
            string message = MessageOf(json);
            if (IsSuccessResult(result))
                return AuthOutcome.Success(string.IsNullOrEmpty(message) ? "认证成功" : message);

            if (IsZeroResult(result) && RetCode.IsExactlyTwo(json["ret_code"]))
                return AuthOutcome.AlreadyOnline(string.IsNullOrEmpty(message) ? "已经在线" : message);

            if (IsCredentialMessage(message))
                return AuthOutcome.CredentialRejected(string.IsNullOrEmpty(message) ? "账号或密码被拒绝" : message);

            if (string.IsNullOrEmpty(message))
                message = "认证被拒绝";
            return AuthOutcome.Rejected(message);
        }

        public static bool IsSuccessResult(JToken result)
        {
            if (result == null || result.Type == JTokenType.Null)
                return false;
            if (result.Type == JTokenType.Integer)
                return result.Value<long>() == 1L;
            if (result.Type == JTokenType.String)
            {
                var text = ((string)result ?? "").Trim();
                return text == "1"
                    || text.Equals("success", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("ok", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public static bool IsZeroResult(JToken result)
        {
            if (result == null || result.Type == JTokenType.Null)
                return false;
            if (result.Type == JTokenType.Integer)
                return result.Value<long>() == 0L;
            if (result.Type == JTokenType.String)
                return (string)result == "0";
            return false;
        }

        public static bool IsCredentialMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return false;
            if (message.IndexOf("密码", StringComparison.Ordinal) >= 0)
                return true;
            if (message.IndexOf("账号不存在", StringComparison.Ordinal) >= 0)
                return true;
            if (message.IndexOf("用户不存在", StringComparison.Ordinal) >= 0)
                return true;
            if (message.IndexOf("账号错误", StringComparison.Ordinal) >= 0)
                return true;
            if (message.IndexOf("用户名", StringComparison.Ordinal) >= 0
                && (message.IndexOf("错", StringComparison.Ordinal) >= 0 || message.IndexOf("不存在", StringComparison.Ordinal) >= 0))
                return true;
            if (message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        static string MessageOf(JObject json)
        {
            var token = json["msg"] ?? json["message"] ?? json["error_msg"] ?? json["error"];
            return token == null || token.Type == JTokenType.Null ? "" : token.ToString();
        }

        static int FindMatchingBrace(string text, int start)
        {
            int depth = 0;
            bool inString = false;
            bool escape = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escape)
                        escape = false;
                    else if (c == '\\')
                        escape = true;
                    else if (c == '"')
                        inString = false;
                    continue;
                }
                if (c == '"')
                {
                    inString = true;
                    continue;
                }
                if (c == '{')
                    depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            return -1;
        }
    }
}
