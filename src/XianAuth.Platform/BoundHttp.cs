using System;
using System.IO;
using System.Net;
using System.Text;

namespace XianAuth
{
    public sealed class HttpRequestSpec
    {
        public string Method { get; set; }
        public string Url { get; set; }
        public string Body { get; set; }
        public string ContentType { get; set; }
        public int TimeoutMs { get; set; }
    }

    public sealed class HttpResult
    {
        public int Status { get; set; }
        public string Body { get; set; }
        public string Location { get; set; }
        public string Error { get; set; }

        public static HttpResult Fail(string error)
        {
            return new HttpResult { Status = 0, Body = "", Error = error ?? "网络请求失败" };
        }
    }

    public static class BoundHttp
    {
        public static HttpResult Send(HttpRequestSpec spec, IPAddress localAddress)
        {
            if (spec == null || string.IsNullOrEmpty(spec.Url))
                return HttpResult.Fail("请求地址为空");
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(spec.Url);
                request.Method = string.IsNullOrEmpty(spec.Method) ? "GET" : spec.Method;
                request.AllowAutoRedirect = false;
                request.Timeout = spec.TimeoutMs <= 0 ? 8000 : spec.TimeoutMs;
                request.ReadWriteTimeout = request.Timeout;
                request.KeepAlive = false;
                request.Proxy = new WebProxy();
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                request.Accept = "*/*";
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                if (localAddress != null)
                {
                    IPAddress bind = localAddress;
                    request.ServicePoint.BindIPEndPointDelegate = delegate { return new IPEndPoint(bind, 0); };
                }
                if (!string.IsNullOrEmpty(spec.Body))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(spec.Body);
                    request.ContentType = string.IsNullOrEmpty(spec.ContentType)
                        ? "application/x-www-form-urlencoded; charset=UTF-8"
                        : spec.ContentType;
                    request.ContentLength = bytes.Length;
                    using (Stream stream = request.GetRequestStream())
                        stream.Write(bytes, 0, bytes.Length);
                }
                try
                {
                    using (var response = (HttpWebResponse)request.GetResponse())
                        return Read(response);
                }
                catch (WebException ex)
                {
                    var response = ex.Response as HttpWebResponse;
                    if (response != null)
                    {
                        using (response)
                            return Read(response);
                    }
                    return HttpResult.Fail(ex.Message);
                }
            }
            catch (Exception ex)
            {
                return HttpResult.Fail(ex.Message);
            }
        }

        public static ConnectResult CheckOnline(IPAddress localAddress)
        {
            return CheckOnline(localAddress, 6000);
        }

        public static ConnectResult CheckOnline(IPAddress localAddress, int timeoutMs)
        {
            if (timeoutMs <= 0)
                timeoutMs = 6000;
            HttpResult first = Send(new HttpRequestSpec { Method = "GET", Url = ConnectProbe.ProbeUrl, TimeoutMs = timeoutMs }, localAddress);
            ConnectResult interpreted = ConnectProbe.Interpret(first.Status, first.Body, first.Location, first.Error);
            if (interpreted.Kind != ConnectKind.ProbeRedirect || string.IsNullOrEmpty(interpreted.RedirectUrl))
                return interpreted;
            HttpResult second = Send(new HttpRequestSpec { Method = "GET", Url = interpreted.RedirectUrl, TimeoutMs = timeoutMs }, localAddress);
            return ConnectProbe.Interpret(second.Status, second.Body, null, second.Error);
        }

        static HttpResult Read(HttpWebResponse response)
        {
            string body = "";
            using (Stream stream = response.GetResponseStream())
            {
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                        body = reader.ReadToEnd();
                }
            }
            if (body.Length > 200000)
                body = body.Substring(0, 200000);
            return new HttpResult
            {
                Status = (int)response.StatusCode,
                Body = body,
                Location = response.Headers["Location"]
            };
        }
    }
}
