using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace XianAuth
{
    public static class AuthEngine
    {
        public static AuthRunResult Run(TriggerKind trigger)
        {
            return Execute(trigger, null, false, null, null);
        }

        public static AuthRunResult RunImmediate(string account, string password, Action<string> progress)
        {
            return Execute(TriggerKind.Manual, progress, true, account, password);
        }

        static void Report(Action<string> progress, string text)
        {
            if (progress != null)
                progress(text);
        }

        static AuthRunResult Execute(TriggerKind trigger, Action<string> progress, bool singlePass, string givenAccount, string givenPassword)
        {
            AppPaths paths = AppPaths.ForBase();
            paths.Ensure();
            DailyLog.Paths = paths;
            AppConfig config = Storage.LoadConfig(paths);
            RuntimeState state = Storage.LoadState(paths);
            PowerSignals signals = trigger == TriggerKind.Boot || trigger == TriggerKind.Resume
                ? PowerEvents.Read()
                : new PowerSignals();

            // 时间点在进程一开始就读取，避免认证过程把「进桌面」的相对时间拉长。
            // 注销、手动「立即验证」和间隔检测都没有「从开机/进桌面算起」的起点，
            // 不记耗时，也省掉事件日志查询的开销。
            TimingMarks marks = HasTimingOrigin(trigger) ? TimingEvents.Read(trigger) : null;

            if (trigger != TriggerKind.Logout && !TriggerPolicy.ShouldRun(trigger, config, signals))
            {
                string skip = "本次触发未启用，已跳过。";
                DailyLog.Info(skip + " 触发=" + trigger + " 开机=" + signals.Boot + " 恢复=" + signals.Resume);
                state.LastSummary = skip;
                Storage.SaveState(paths, state);
                return AuthRunResult.Skipped(skip);
            }

            string account = givenAccount;
            string password = givenPassword;
            bool hasCredential = !string.IsNullOrEmpty(account) && !string.IsNullOrEmpty(password);
            if (!hasCredential)
            {
                try
                {
                    hasCredential = DpapiCredentialStore.TryLoad(paths, out account, out password);
                }
                catch (Exception ex)
                {
                    DailyLog.Error("读取加密账号失败：" + ex.Message);
                    return new AuthRunResult(AuthStatus.Rejected, "无法解密已保存的账号");
                }
            }
            if (!hasCredential || string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
            {
                const string missing = "未找到已保存的账号或密码";
                DailyLog.Info(missing);
                state.LastSummary = missing;
                Storage.SaveState(paths, state);
                return new AuthRunResult(AuthStatus.Rejected, missing);
            }

            CredentialLock.SyncStamp(state, DpapiCredentialStore.Stamp(paths));
            if (!singlePass && trigger != TriggerKind.Logout && CredentialLock.IsLocked(state, config))
            {
                const string locked = "已连续多次认证失败，已停止自动认证以避免账号锁定。请核对密码后重新保存。";
                DailyLog.Info(locked);
                state.LastSummary = locked;
                Storage.SaveState(paths, state);
                return new AuthRunResult(AuthStatus.Locked, locked);
            }

            List<PortalProfile> profiles = Storage.LoadProfiles(paths);
            AuthOutcome last = AuthOutcome.Transient("尚未开始");
            bool connectTried = false;
            bool timedOut = false;
            int deadlineSeconds = config.AuthDeadlineSeconds;

            if (singlePass || trigger == TriggerKind.Logout)
            {
                int started = Environment.TickCount;
                int attempts = singlePass ? 1 : (trigger == TriggerKind.Logout ? 2 : RetryPlan.MaxAttempts);
                int[] delays = singlePass ? new[] { 0 } : (trigger == TriggerKind.Logout ? new[] { 0, 3000 } : RetryPlan.DelayBeforeAttemptMs);
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    int elapsed = unchecked(Environment.TickCount - started);
                    if (attempt > 0)
                    {
                        if (trigger != TriggerKind.Logout && !RetryPlan.CanStart(attempt, elapsed))
                            break;
                        System.Threading.Thread.Sleep(delays[attempt]);
                    }
                    Report(progress, "正在检测连通性…");
                    last = Attempt(trigger, config, profiles, account, password, progress, ref connectTried,
                        trigger == TriggerKind.Logout ? 4000 : 8000, 8000);
                    DailyLog.Info("第 " + (attempt + 1).ToString(CultureInfo.InvariantCulture) + " 次：" + last.Status + " " + SecretRedactor.Redact(last.Message));
                    if (!last.ShouldRetry)
                        break;
                }
            }
            else
            {
                // 自动触发按「N 秒内完成认证」执行：先等 Wi-Fi 拿到 IPv4（最多时限的一半），
                // 然后每 1–2 秒做一次「检测 + 认证」，成功、账号被拒或超时就退出。
                int budgetMs = deadlineSeconds * 1000;
                int started = Environment.TickCount;
                WaitForWifi(config, Math.Max(1000, budgetMs / 2), ref connectTried);
                for (int attempt = 0; ; attempt++)
                {
                    int elapsed = unchecked(Environment.TickCount - started);
                    int remaining = budgetMs - elapsed;
                    if (remaining <= 0)
                    {
                        timedOut = true;
                        break;
                    }
                    Report(progress, "正在检测连通性…");
                    int httpTimeout = Math.Max(1500, Math.Min(8000, remaining));
                    last = Attempt(trigger, config, profiles, account, password, progress, ref connectTried,
                        Math.Min(1500, remaining), httpTimeout);
                    DailyLog.Info("第 " + (attempt + 1).ToString(CultureInfo.InvariantCulture) + " 次：" + last.Status + " " + SecretRedactor.Redact(last.Message));
                    if (!last.ShouldRetry)
                        break;
                    elapsed = unchecked(Environment.TickCount - started);
                    remaining = budgetMs - elapsed;
                    if (remaining <= 0)
                    {
                        timedOut = true;
                        break;
                    }
                    // 间隔在 1–2 秒之间交替，既及时又不会把门户打得太频繁。
                    int wait = Math.Min(1000 + ((attempt % 2) * 1000), remaining);
                    System.Threading.Thread.Sleep(wait);
                }
                if (timedOut && !last.IsOnline)
                {
                    string message = deadlineSeconds.ToString(CultureInfo.InvariantCulture) + " 秒内未完成认证";
                    DailyLog.Info(message);
                    last = AuthOutcome.Transient(message);
                }
            }

            if (last.Status == AuthStatus.CredentialRejected)
                state.ConsecutiveCredentialFailures++;
            else if (last.IsOnline)
                state.ConsecutiveCredentialFailures = 0;

            // 只有真正提交了凭据并认证成功才记耗时。本来就是通的（AlreadyOnline）
            // 说明这一轮没发生认证，记下来只会把上一次真实的耗时冲掉。
            if (last.Status == AuthStatus.Success && marks != null)
            {
                AuthTiming timing = TimingCalculator.Compute(trigger, marks.SystemBoot, marks.DesktopReady, marks.WifiConnected, DateTime.UtcNow);
                state.LastTiming = timing;
                LogTiming(timing);
            }

            state.LastSummary = last.Message;
            Storage.SaveState(paths, state);
            return AuthRunResult.From(last);
        }

        /// <summary>
        /// 哪些触发有「从开机或进桌面算起」的起点。注销、手动验证、间隔检测都没有，
        /// 记录它们只会算出无意义的秒数。
        /// </summary>
        static bool HasTimingOrigin(TriggerKind trigger)
        {
            switch (trigger)
            {
                case TriggerKind.Boot:
                case TriggerKind.Logon:
                case TriggerKind.Resume:
                case TriggerKind.Sleep:
                case TriggerKind.Wlan:
                case TriggerKind.Unlock:
                    return true;
                default:
                    return false;
            }
        }

        static void LogTiming(AuthTiming timing)
        {
            if (timing == null)
                return;
            DailyLog.Info("耗时统计：系统启动=" + Display(timing.SystemBootUtc)
                + "，进入桌面=" + Display(timing.DesktopReadyUtc)
                + "，Wi-Fi 连接=" + Display(timing.WifiConnectedUtc)
                + "，认证成功=" + Display(timing.AuthSuccessUtc)
                + "，开机到认证=" + Seconds(timing.BootToAuthSeconds)
                + "，进桌面到认证=" + Seconds(timing.DesktopToAuthSeconds));
        }

        static string Display(string iso)
        {
            DateTimeOffset value;
            if (string.IsNullOrEmpty(iso) || !DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out value))
                return "未读到";
            return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        static string Seconds(double? value)
        {
            return value.HasValue ? TimingCalculator.Format(value.Value) + " 秒" : "未读到";
        }

        /// <summary>
        /// 等目标 SSID 连上并拿到 IPv4，最多等 budgetMs。期间允许自动连接一次。
        /// </summary>
        static void WaitForWifi(AppConfig config, int budgetMs, ref bool connectTried)
        {
            if (budgetMs <= 0 || config == null || string.IsNullOrEmpty(config.TargetSsid))
                return;
            int started = Environment.TickCount;
            while (true)
            {
                try
                {
                    WifiSnapshot wifi = Match(NativeWifi.QueryAll(), config.TargetSsid);
                    if (wifi != null && string.Equals(wifi.Ssid, config.TargetSsid, StringComparison.Ordinal) && wifi.IPv4 != null)
                        return;
                    if (config.AutoConnectWifi && !connectTried)
                    {
                        connectTried = true;
                        NativeWifi.TryConnect(config.TargetSsid);
                    }
                }
                catch (Exception)
                {
                    // 读无线接口失败不阻断流程，交给后面的 Attempt 记录。
                }
                int elapsed = unchecked(Environment.TickCount - started);
                if (elapsed >= budgetMs)
                    return;
                System.Threading.Thread.Sleep(Math.Min(500, budgetMs - elapsed));
            }
        }

        static AuthOutcome Attempt(TriggerKind trigger, AppConfig config, List<PortalProfile> profiles, string account, string password, Action<string> progress, ref bool connectTried, int ipv4WaitMs, int httpTimeoutMs)
        {
            List<WifiSnapshot> all;
            try
            {
                all = NativeWifi.QueryAll();
            }
            catch (Exception ex)
            {
                return AuthOutcome.Transient("读取无线网络失败：" + ex.Message);
            }

            WifiSnapshot wifi = Match(all, config.TargetSsid);
            if (wifi == null || !string.Equals(wifi.Ssid, config.TargetSsid, StringComparison.Ordinal))
            {
                if (config.AutoConnectWifi && !connectTried)
                {
                    connectTried = true;
                    NativeWifi.TryConnect(config.TargetSsid);
                    System.Threading.Thread.Sleep(2500);
                    try { all = NativeWifi.QueryAll(); } catch (Exception) { }
                    wifi = Match(all, config.TargetSsid);
                }
                if (wifi == null || !string.Equals(wifi.Ssid, config.TargetSsid, StringComparison.Ordinal))
                {
                    string current = wifi == null ? "未连接" : wifi.Ssid;
                    return AuthOutcome.NotTarget("当前网络不是目标网络 " + config.TargetSsid + "（现在是 " + current + "），未提交凭据");
                }
            }

            IPAddress ip = wifi.IPv4 ?? NativeWifi.WaitForIpv4(wifi.InterfaceId, trigger == TriggerKind.Logout ? 4000 : ipv4WaitMs);
            if (ip == null)
                return AuthOutcome.Transient("无线网卡尚未获得 IPv4");

            if (trigger == TriggerKind.Logout)
                return Logout(profiles, config, account, ip, wifi.Mac);

            Report(progress, "正在检测连通性…");
            ConnectResult probe = BoundHttp.CheckOnline(ip, httpTimeoutMs);
            if (probe.Kind == ConnectKind.Online)
                return AuthOutcome.AlreadyOnline("网络已连通");

            Report(progress, "正在认证…");

            PortalProfile profile = PortalSelector.Select(profiles, config.ActiveProfileId, wifi.Ssid, probe.RedirectUrl);
            if (profile == null)
            {
                if (string.IsNullOrEmpty(probe.RedirectUrl))
                    return AuthOutcome.Transient("尚未发现门户重定向");
                return AuthOutcome.Rejected("未能识别当前门户，未提交凭据");
            }
            DailyLog.Info("使用门户配置 " + profile.Id + "，绑定地址 " + ip);
            return Login(profile, account, password, ip, wifi.Mac, probe.RedirectUrl, httpTimeoutMs);
        }

        static WifiSnapshot Match(List<WifiSnapshot> all, string ssid)
        {
            if (all == null)
                return null;
            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].Ssid, ssid, StringComparison.Ordinal))
                    return all[i];
            }
            return all.Count > 0 ? all[0] : null;
        }

        static AuthOutcome Login(PortalProfile profile, string account, string password, IPAddress ip, string mac, string redirectUrl, int httpTimeoutMs)
        {
            if (profile.Kind == "drcom-eportal")
                return DrcomLogin(profile, account, password, ip, httpTimeoutMs);
            if (profile.Kind == "srun")
                return SrunLogin(profile, account, password, ip, redirectUrl, httpTimeoutMs);
            if (profile.Kind == "ruijie")
                return RuijieLogin(profile, account, password, ip, redirectUrl, httpTimeoutMs);
            return TemplateLogin(profile, account, password, ip, mac, httpTimeoutMs);
        }

        static AuthOutcome Logout(List<PortalProfile> profiles, AppConfig config, string account, IPAddress ip, string mac)
        {
            PortalProfile profile = PortalSelector.Select(profiles, config.ActiveProfileId, config.TargetSsid, null);
            if (profile == null)
                return AuthOutcome.Rejected("没有可用的门户配置，请先识别待连接网络");
            if (profile.Kind == "drcom-eportal" && profile.Drcom != null)
            {
                long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string url = DrcomEportal.BuildLogoutUrl(profile.Drcom, ip.ToString(), ts);
                DailyLog.Info("Dr.COM 注销 " + SecretRedactor.Redact(url));
                HttpResult http = BoundHttp.Send(new HttpRequestSpec { Method = "GET", Url = url, TimeoutMs = 8000 }, ip);
                if (http.Status == 0)
                    return AuthOutcome.Transient(http.Error);
                return JsonpParser.InterpretDrcom(http.Body);
            }
            if (profile.Kind == "srun" && profile.Srun != null)
            {
                string url = SrunPortal.BuildLogoutUrl(profile.Srun.BaseUrl, account, ip.ToString(), profile.Srun.AcId ?? "1", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                HttpResult http = BoundHttp.Send(new HttpRequestSpec { Method = "GET", Url = url, TimeoutMs = 8000 }, ip);
                if (http.Status == 0)
                    return AuthOutcome.Transient(http.Error);
                return http.Status >= 200 && http.Status < 400
                    ? AuthOutcome.Success("已请求注销")
                    : AuthOutcome.Rejected("注销失败");
            }
            if (profile.Kind == "ruijie" && profile.Ruijie != null)
            {
                HttpResult http = BoundHttp.Send(new HttpRequestSpec
                {
                    Method = "POST",
                    Url = RuijiePortal.LogoutUrl(profile.Ruijie.BaseUrl),
                    Body = "userIndex=",
                    TimeoutMs = 8000
                }, ip);
                if (http.Status == 0)
                    return AuthOutcome.Transient(http.Error);
                return InterpretRuijie(http.Body);
            }
            if (!string.IsNullOrEmpty(profile.LogoutUrl))
            {
                var vars = Vars(account, "", ip, mac);
                string url = TemplateText.Apply(profile.LogoutUrl, vars);
                HttpResult http = BoundHttp.Send(new HttpRequestSpec
                {
                    Method = string.IsNullOrEmpty(profile.LogoutMethod) ? "GET" : profile.LogoutMethod,
                    Url = url,
                    Body = TemplateText.Apply(profile.LogoutBody, vars),
                    ContentType = profile.LogoutContentType,
                    TimeoutMs = 8000
                }, ip);
                if (http.Status == 0)
                    return AuthOutcome.Transient(http.Error);
                return http.Status >= 200 && http.Status < 400
                    ? AuthOutcome.Success("已请求注销")
                    : AuthOutcome.Rejected("注销失败");
            }
            return AuthOutcome.Rejected("当前门户配置没有注销地址");
        }

        static AuthOutcome DrcomLogin(PortalProfile profile, string account, string password, IPAddress ip, int httpTimeoutMs)
        {
            if (profile.Drcom == null)
                return AuthOutcome.Rejected("Dr.COM 配置不完整");
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string url = DrcomEportal.BuildLoginUrl(profile.Drcom, account, password, ip.ToString(), ts);
            if (DrcomEportal.IsForbiddenAdminUrl(url))
                return AuthOutcome.Rejected("拒绝访问带验证码的门户后台");
            DailyLog.Info("Dr.COM 登录 " + SecretRedactor.Redact(url));
            HttpResult http = BoundHttp.Send(new HttpRequestSpec { Method = "GET", Url = url, TimeoutMs = httpTimeoutMs }, ip);
            if (http.Status == 0)
                return AuthOutcome.Transient(http.Error);
            return JsonpParser.InterpretDrcom(http.Body);
        }

        static AuthOutcome SrunLogin(PortalProfile profile, string account, string password, IPAddress ip, string redirectUrl, int httpTimeoutMs)
        {
            if (profile.Srun == null || string.IsNullOrEmpty(profile.Srun.BaseUrl))
                return AuthOutcome.Rejected("深澜配置不完整");
            string acid = string.IsNullOrEmpty(profile.Srun.AcId) ? "1" : profile.Srun.AcId;
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string challengeUrl = SrunPortal.BuildChallengeUrl(profile.Srun.BaseUrl, account, ip.ToString(), ts);
            HttpResult challenge = BoundHttp.Send(new HttpRequestSpec { Method = "GET", Url = challengeUrl, TimeoutMs = httpTimeoutMs }, ip);
            if (challenge.Status == 0)
                return AuthOutcome.Transient(challenge.Error);
            JObject json;
            if (!JsonpParser.TryParse(challenge.Body, out json) || json["challenge"] == null)
                return AuthOutcome.Transient("未获得深澜挑战值");
            string token = json["challenge"].ToString();
            string loginUrl = SrunPortal.BuildLoginUrl(profile.Srun.BaseUrl, account, password, ip.ToString(), acid, token, ts);
            DailyLog.Info("深澜登录 " + SecretRedactor.Redact(loginUrl));
            HttpResult login = BoundHttp.Send(new HttpRequestSpec { Method = "GET", Url = loginUrl, TimeoutMs = httpTimeoutMs }, ip);
            if (login.Status == 0)
                return AuthOutcome.Transient(login.Error);
            return SrunPortal.Interpret(login.Body);
        }

        static AuthOutcome RuijieLogin(PortalProfile profile, string account, string password, IPAddress ip, string redirectUrl, int httpTimeoutMs)
        {
            if (profile.Ruijie == null || string.IsNullOrEmpty(profile.Ruijie.BaseUrl))
                return AuthOutcome.Rejected("锐捷配置不完整");
            string query = "";
            Uri redir;
            if (!string.IsNullOrEmpty(redirectUrl) && Uri.TryCreate(redirectUrl, UriKind.Absolute, out redir))
                query = redir.Query;
            HttpResult page = BoundHttp.Send(new HttpRequestSpec
            {
                Method = "POST",
                Url = RuijiePortal.PageInfoUrl(profile.Ruijie.BaseUrl),
                Body = "queryString=" + RuijiePortal.EncodeQueryString(query),
                TimeoutMs = httpTimeoutMs
            }, ip);
            if (page.Status == 0)
                return AuthOutcome.Transient(page.Error);
            JObject info;
            if (!JsonpParser.TryParse(page.Body, out info))
                return AuthOutcome.Transient("锐捷页面信息无法解析");
            string exponent = (string)info["publicKeyExponent"];
            string modulus = (string)info["publicKeyModulus"];
            if (string.IsNullOrEmpty(exponent) || string.IsNullOrEmpty(modulus))
                return AuthOutcome.Rejected("未获得锐捷公钥");
            string cipher = RuijiePortal.EncryptPasswordHex(password, exponent, modulus);
            string body = "userId=" + Uri.EscapeDataString(account)
                + "&password=" + Uri.EscapeDataString(cipher)
                + "&service=" + Uri.EscapeDataString(profile.Ruijie.Service ?? "")
                + "&queryString=" + RuijiePortal.EncodeQueryString(query)
                + "&operatorPwd=&operatorUserId=&validcode=&passwordEncrypt=true";
            DailyLog.Info("锐捷登录（口令已加密，日志不记录密码）");
            HttpResult login = BoundHttp.Send(new HttpRequestSpec
            {
                Method = "POST",
                Url = RuijiePortal.LoginUrl(profile.Ruijie.BaseUrl),
                Body = body,
                TimeoutMs = httpTimeoutMs
            }, ip);
            if (login.Status == 0)
                return AuthOutcome.Transient(login.Error);
            return InterpretRuijie(login.Body);
        }

        static AuthOutcome InterpretRuijie(string body)
        {
            JObject json;
            if (!JsonpParser.TryParse(body, out json))
                return AuthOutcome.Transient("锐捷返回无法解析");
            string result = json["result"] == null ? "" : json["result"].ToString();
            string message = json["message"] == null ? "" : json["message"].ToString();
            if (result.Equals("success", StringComparison.OrdinalIgnoreCase))
                return AuthOutcome.Success(string.IsNullOrEmpty(message) ? "认证成功" : message);
            if (JsonpParser.IsCredentialMessage(message))
                return AuthOutcome.CredentialRejected(message);
            return AuthOutcome.Rejected(string.IsNullOrEmpty(message) ? "锐捷认证被拒绝" : message);
        }

        static AuthOutcome TemplateLogin(PortalProfile profile, string account, string password, IPAddress ip, string mac, int httpTimeoutMs)
        {
            if (profile.Steps == null || profile.Steps.Count == 0)
                return AuthOutcome.Rejected("门户模板没有请求步骤");
            var vars = Vars(account, password, ip, mac);
            HttpResult last = null;
            for (int i = 0; i < profile.Steps.Count; i++)
            {
                TemplateStep step = profile.Steps[i];
                string url = TemplateText.Apply(step.Url, vars);
                string body = TemplateText.Apply(step.Body, vars);
                DailyLog.Info((step.Method ?? "GET") + " " + SecretRedactor.Redact(url));
                last = BoundHttp.Send(new HttpRequestSpec
                {
                    Method = step.Method,
                    Url = url,
                    Body = body,
                    ContentType = step.ContentType,
                    TimeoutMs = httpTimeoutMs
                }, ip);
                if (last.Status == 0)
                    return AuthOutcome.Transient(last.Error);
                if (step.Extract == null)
                    continue;
                for (int e = 0; e < step.Extract.Count; e++)
                {
                    ExtractRule rule = step.Extract[e];
                    if (rule == null || string.IsNullOrEmpty(rule.Pattern))
                        continue;
                    Match match = Regex.Match(last.Body ?? "", rule.Pattern);
                    if (match.Success && match.Groups.Count > 1)
                        vars[rule.Name] = match.Groups[1].Value;
                }
            }
            return InterpretTemplate(last, profile);
        }

        static AuthOutcome InterpretTemplate(HttpResult last, PortalProfile profile)
        {
            if (last == null)
                return AuthOutcome.Transient("没有收到门户响应");
            string body = last.Body ?? "";
            if (profile.SuccessContains != null)
            {
                for (int i = 0; i < profile.SuccessContains.Count; i++)
                {
                    string marker = profile.SuccessContains[i];
                    if (!string.IsNullOrEmpty(marker) && body.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                        return AuthOutcome.Success("认证成功");
                }
            }
            if (JsonpParser.IsCredentialMessage(body))
                return AuthOutcome.CredentialRejected("账号或密码被拒绝");
            if (last.Status >= 500)
                return AuthOutcome.Transient("门户暂时不可用");
            return AuthOutcome.Rejected("门户未返回成功标记");
        }

        static Dictionary<string, string> Vars(string account, string password, IPAddress ip, string mac)
        {
            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new Dictionary<string, string>
            {
                { "account", account ?? "" },
                { "password", password ?? "" },
                { "ip", ip == null ? "" : ip.ToString() },
                { "mac", mac ?? "" },
                { "mac_zero", "000000000000" },
                { "timestamp", ms.ToString(CultureInfo.InvariantCulture) },
                { "timestamp_sec", (ms / 1000).ToString(CultureInfo.InvariantCulture) }
            };
        }
    }
}
