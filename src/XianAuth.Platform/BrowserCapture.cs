using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace XianAuth
{
    public sealed class BrowserCapture : IDisposable
    {
        readonly object _gate = new object();
        readonly List<CapturedRequest> _requests = new List<CapturedRequest>();
        readonly Dictionary<string, CapturedRequest> _byId = new Dictionary<string, CapturedRequest>();
        CancellationTokenSource _cancel;
        Process _process;
        string _profileDir;
        Task _loop;
        int _nextId = 1;
        int _bodies;

        public string Status { get; private set; }

        public IList<CapturedRequest> Snapshot()
        {
            lock (_gate)
                return new List<CapturedRequest>(_requests);
        }

        public void Start(string url)
        {
            string browser = FindBrowser();
            if (browser == null)
            {
                Status = "未找到 Edge 或 Chrome，请改用导入 HAR 或粘贴网址。";
                return;
            }
            int port = FreePort();
            _profileDir = Path.Combine(Path.GetTempPath(), "xian-renzheng-cdp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_profileDir);
            string args = "--remote-debugging-port=" + port
                + " --remote-allow-origins=* --user-data-dir=\"" + _profileDir + "\""
                + " --no-first-run --no-default-browser-check --disable-sync --new-window \"" + (url ?? "about:blank") + "\"";
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = browser,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            _cancel = new CancellationTokenSource();
            Status = "浏览器已打开，请完成一次门户登录。";
            int localPort = port;
            _loop = Task.Run(() => Loop(localPort, _cancel.Token));
        }

        public void Stop()
        {
            if (_cancel != null)
            {
                try { _cancel.Cancel(); } catch (ObjectDisposedException) { }
            }
            if (_loop != null)
            {
                try { _loop.Wait(2000); } catch (AggregateException) { }
            }
            KillProcess();
            try
            {
                if (!string.IsNullOrEmpty(_profileDir) && Directory.Exists(_profileDir))
                    Directory.Delete(_profileDir, true);
            }
            catch (IOException)
            {
            }
        }

        public void Dispose()
        {
            Stop();
        }

        void Loop(int port, CancellationToken token)
        {
            try
            {
                string ws = WaitForPage(port, token);
                if (ws == null)
                {
                    Status = "没有连上浏览器调试端口，仍可导入 HAR 或粘贴网址。";
                    return;
                }
                using (var socket = new ClientWebSocket())
                {
                    socket.ConnectAsync(new Uri(ws), token).GetAwaiter().GetResult();
                    Send(socket, "{\"id\":" + NextId() + ",\"method\":\"Network.enable\",\"params\":{}}", token);
                    var buffer = new byte[16384];
                    while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                    {
                        string message = Receive(socket, buffer, token);
                        if (message == null)
                            break;
                        Handle(socket, message, token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Status = "读取浏览器请求失败：" + ex.Message;
            }
        }

        void Handle(ClientWebSocket socket, string message, CancellationToken token)
        {
            JObject json;
            try { json = JObject.Parse(message); }
            catch (Newtonsoft.Json.JsonException) { return; }

            if (json["id"] != null && json["result"] != null)
            {
                int commandId = json["id"].Value<int>();
                string requestId;
                if (_pending.TryGetValue(commandId, out requestId))
                {
                    _pending.Remove(commandId);
                    string body = (string)json["result"]["body"];
                    bool base64 = json["result"]["base64Encoded"] != null && json["result"]["base64Encoded"].Value<bool>();
                    if (!base64 && body != null)
                    {
                        if (body.Length > 4096)
                            body = body.Substring(0, 4096);
                        lock (_gate)
                        {
                            CapturedRequest captured;
                            if (_byId.TryGetValue(requestId, out captured))
                                captured.ResponseBody = body;
                        }
                    }
                }
                return;
            }

            string method = (string)json["method"];
            JObject param = json["params"] as JObject;
            if (param == null)
                return;
            if (method == "Network.requestWillBeSent")
            {
                string type = (string)param["type"];
                if (type == "Image" || type == "Stylesheet" || type == "Font" || type == "Media")
                    return;
                JObject request = param["request"] as JObject;
                if (request == null)
                    return;
                string url = (string)request["url"] ?? "";
                if (IsSkippable(url))
                    return;
                var captured = new CapturedRequest
                {
                    Method = (string)request["method"] ?? "GET",
                    Url = url,
                    Body = (string)request["postData"]
                };
                JObject headers = request["headers"] as JObject;
                if (headers != null)
                {
                    foreach (var prop in headers.Properties())
                    {
                        if (CookieGuard.IsCookieHeader(prop.Name))
                            continue;
                        if (prop.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            captured.ContentType = prop.Value.ToString();
                    }
                }
                string requestId = (string)param["requestId"];
                lock (_gate)
                {
                    if (_requests.Count >= 250)
                        return;
                    _requests.Add(captured);
                    if (!string.IsNullOrEmpty(requestId))
                        _byId[requestId] = captured;
                }
            }
            else if (method == "Network.loadingFinished")
            {
                string requestId = (string)param["requestId"];
                lock (_gate)
                {
                    CapturedRequest ignored;
                    if (requestId == null || !_byId.TryGetValue(requestId, out ignored))
                        return;
                    if (_bodies > 40)
                        return;
                    _bodies++;
                }
                int id = NextId();
                _pending[id] = requestId;
                Send(socket, "{\"id\":" + id + ",\"method\":\"Network.getResponseBody\",\"params\":{\"requestId\":\"" + Escape(requestId) + "\"}}", token);
            }
        }

        readonly Dictionary<int, string> _pending = new Dictionary<int, string>();

        void Send(ClientWebSocket socket, string json, CancellationToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).GetAwaiter().GetResult();
        }

        static string Receive(ClientWebSocket socket, byte[] buffer, CancellationToken token)
        {
            using (var stream = new MemoryStream())
            {
                while (true)
                {
                    WebSocketReceiveResult result = socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).GetAwaiter().GetResult();
                    if (result.MessageType == WebSocketMessageType.Close)
                        return null;
                    stream.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                        break;
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        string WaitForPage(int port, CancellationToken token)
        {
            string endpoint = "http://127.0.0.1:" + port + "/json/list";
            for (int i = 0; i < 40 && !token.IsCancellationRequested; i++)
            {
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(endpoint);
                    request.Timeout = 1000;
                    request.Proxy = new WebProxy();
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        var array = JArray.Parse(reader.ReadToEnd());
                        for (int n = 0; n < array.Count; n++)
                        {
                            if ((string)array[n]["type"] == "page" && array[n]["webSocketDebuggerUrl"] != null)
                                return (string)array[n]["webSocketDebuggerUrl"];
                        }
                    }
                }
                catch (WebException)
                {
                }
                catch (Newtonsoft.Json.JsonException)
                {
                }
                Thread.Sleep(500);
            }
            return null;
        }

        int NextId()
        {
            return Interlocked.Increment(ref _nextId);
        }

        void KillProcess()
        {
            try
            {
                if (_process == null || _process.HasExited)
                    return;
                using (Process killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = "/PID " + _process.Id + " /T /F",
                    CreateNoWindow = true,
                    UseShellExecute = false
                }))
                {
                    if (killer != null)
                        killer.WaitForExit(4000);
                }
            }
            catch (Exception)
            {
            }
        }

        static bool IsSkippable(string url)
        {
            if (string.IsNullOrEmpty(url))
                return true;
            string path = url.Split('?')[0].ToLowerInvariant();
            return path.EndsWith(".png", StringComparison.Ordinal)
                || path.EndsWith(".jpg", StringComparison.Ordinal)
                || path.EndsWith(".css", StringComparison.Ordinal)
                || path.EndsWith(".woff", StringComparison.Ordinal)
                || path.EndsWith(".ico", StringComparison.Ordinal);
        }

        static string FindBrowser()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] candidates =
            {
                Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(pf86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(pf86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe")
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                    return candidates[i];
            }
            return null;
        }

        static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        static string Escape(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
