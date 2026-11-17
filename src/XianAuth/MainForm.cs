using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace XianAuth
{
    public sealed class MainForm : Form
    {
        readonly AppPaths _paths = AppPaths.ForBase();
        readonly Banner _banner = new Banner();
        readonly ToggleSwitch _auto = new ToggleSwitch();
        readonly TextBox _account = new TextBox();
        readonly TextBox _password = new TextBox();
        readonly CheckBox _shutdown = new CheckBox();
        readonly CheckBox _reboot = new CheckBox();
        readonly CheckBox _hibernate = new CheckBox();
        readonly CheckBox _sleep = new CheckBox();
        readonly CheckBox _vpn = new CheckBox();
        readonly CheckBox _interval = new CheckBox();
        readonly NumericUpDown _minutes = new NumericUpDown();
        readonly FeedbackButton _mode = new FeedbackButton();
        readonly FlowLayoutPanel _modePanel = new FlowLayoutPanel();
        readonly FeedbackButton _restore = new FeedbackButton();
        readonly FeedbackButton _logout = new FeedbackButton();
        readonly FeedbackButton _identify = new FeedbackButton();
        readonly FeedbackButton _verify = new FeedbackButton();
        readonly FeedbackButton _save = new FeedbackButton();
        readonly Label _portal = new Label();
        readonly Label _vpnInfo = new Label();
        AppConfig _config;
        bool _busy;
        bool _loading;
        bool _showingPasswordCue;
        const string PasswordCue = "已保存（不显示），留空则不修改";
        bool _modeOpen = true;
        bool _hasStoredPassword;

        public MainForm()
        {
            _paths.Ensure();
            DailyLog.Paths = _paths;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = "闲·认证";
            Font = UiFonts.Body(9.5f, FontStyle.Regular);
            BackColor = Palette.Background;
            ClientSize = new Size(520, 860);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TrySetIcon();

            var header = new HeaderPanel();
            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 8, 20, 8), BackColor = Palette.Background };
            Controls.Add(body);
            Controls.Add(_banner);
            Controls.Add(header);
            BuildBody(body);
            LoadConfigToUi();
            Shown += delegate { RefreshVpnInfo(); };
        }

        void BuildBody(Panel body)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                Width = 470,
                BackColor = Palette.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.Controls.Add(layout);

            var switchRow = new Panel { Height = 44, Width = 460, BackColor = Palette.Background };
            _auto.Location = new Point(0, 6);
            var autoLabel = new Label
            {
                Text = "开启自动认证",
                AutoSize = false,
                Bounds = new Rectangle(64, 8, 200, 24),
                ForeColor = Palette.Text,
                Font = UiFonts.Body(11f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Palette.Background
            };
            switchRow.Controls.Add(_auto);
            switchRow.Controls.Add(autoLabel);
            _auto.Toggled += delegate { ToggleAuto(); };
            AddRow(layout, switchRow);

            AddRow(layout, FieldLabel("账号："));
            StyleBox(_account);
            AddRow(layout, _account);
            AddRow(layout, FieldLabel("密码："));
            StyleBox(_password);
            _password.UseSystemPasswordChar = true;
            _password.Enter += delegate { HidePasswordCue(); };
            _password.Leave += delegate { UpdatePasswordCue(); };
            AddRow(layout, _password);

            _portal.AutoSize = false;
            _portal.Height = 40;
            _portal.ForeColor = Palette.Muted;
            AddRow(layout, _portal);

            _mode.Text = "模式选择";
            _mode.Chevron = Chevron.Up;
            _mode.Dock = DockStyle.Top;
            _mode.Height = 48;
            _mode.Click += delegate { ToggleMode(layout); };
            AddRow(layout, _mode);

            _modePanel.AutoSize = true;
            _modePanel.WrapContents = true;
            _modePanel.FlowDirection = FlowDirection.LeftToRight;
            _modePanel.Padding = new Padding(0, 2, 0, 0);
            _modePanel.BackColor = Palette.Background;
            _shutdown.Text = "关机";
            _reboot.Text = "重启";
            _hibernate.Text = "休眠";
            _sleep.Text = "睡眠";
            CheckBox[] modes = { _shutdown, _reboot, _hibernate, _sleep };
            for (int i = 0; i < modes.Length; i++)
            {
                modes[i].AutoSize = true;
                modes[i].Margin = new Padding(8, 6, 16, 6);
                modes[i].ForeColor = Palette.Text;
                modes[i].BackColor = Palette.Background;
                modes[i].CheckedChanged += ModeChanged;
                _modePanel.Controls.Add(modes[i]);
            }
            AddRow(layout, _modePanel);

            _vpn.Text = "VPN选择";
            _vpn.AutoSize = true;
            _vpn.ForeColor = Palette.Text;
            _vpn.BackColor = Palette.Background;
            _vpn.CheckedChanged += VpnChanged;
            AddRow(layout, _vpn);
            _vpnInfo.AutoSize = false;
            _vpnInfo.Height = 36;
            _vpnInfo.ForeColor = Palette.Muted;
            _vpnInfo.Text = "正在识别 VPN 客户端…";
            AddRow(layout, _vpnInfo);

            var intervalRow = new Panel { Height = 32, Width = 460, BackColor = Palette.Background };
            _interval.Text = "间隔检测";
            _interval.AutoSize = true;
            _interval.Location = new Point(0, 4);
            _interval.ForeColor = Palette.Text;
            _interval.BackColor = Palette.Background;
            _interval.CheckedChanged += IntervalChanged;
            _minutes.Minimum = 1;
            _minutes.Maximum = 720;
            _minutes.Value = 10;
            _minutes.Location = new Point(110, 2);
            _minutes.Width = 70;
            _minutes.BackColor = Palette.Input;
            _minutes.ForeColor = Palette.Text;
            _minutes.ValueChanged += IntervalChanged;
            var minuteLabel = new Label { Text = "分钟", AutoSize = true, Location = new Point(188, 6), ForeColor = Palette.Text, BackColor = Palette.Background };
            intervalRow.Controls.Add(_interval);
            intervalRow.Controls.Add(_minutes);
            intervalRow.Controls.Add(minuteLabel);
            AddRow(layout, intervalRow);

            var split = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Height = 56,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                BackColor = Palette.Background
            };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _restore.Text = "恢复默认配置";
            _logout.Text = "注销";
            _restore.Dock = DockStyle.Fill;
            _logout.Dock = DockStyle.Fill;
            _restore.Margin = new Padding(0, 0, 8, 0);
            _logout.Margin = new Padding(8, 0, 0, 0);
            _restore.Click += delegate { Restore(); };
            _logout.Click += delegate { Logout(); };
            split.Controls.Add(_restore, 0, 0);
            split.Controls.Add(_logout, 1, 0);
            AddRow(layout, split);

            _identify.Text = "识别待连接网络";
            _identify.Height = 48;
            _identify.Click += delegate { Identify(); };
            AddRow(layout, _identify);

            _verify.Text = "立即验证";
            _verify.Height = 48;
            _verify.Click += delegate { VerifyNow(); };
            AddRow(layout, _verify);

            _save.Text = "保存账号";
            _save.Height = 48;
            _save.Click += delegate { SaveAccount(); };
            AddRow(layout, _save);
        }

        static void AddRow(TableLayoutPanel layout, Control control)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Margin = new Padding(0, 6, 0, 6);
            control.Dock = DockStyle.Top;
            if (!(control is TextBox) && !(control is NumericUpDown))
                control.BackColor = Palette.Background;
            layout.Controls.Add(control, 0, row);
        }

        static Label FieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Height = 22,
                ForeColor = Palette.Text,
                BackColor = Palette.Background,
                TextAlign = ContentAlignment.BottomLeft
            };
        }

        static void StyleBox(TextBox box)
        {
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Height = 28;
            box.BackColor = Palette.Input;
            box.ForeColor = Palette.Text;
        }

        void ToggleMode(TableLayoutPanel layout)
        {
            _modeOpen = !_modeOpen;
            RefreshModeHeader();
            layout.PerformLayout();
        }

        void RefreshModeHeader()
        {
            _modePanel.Visible = _modeOpen;
            _mode.Chevron = _modeOpen ? Chevron.Up : Chevron.Down;
            if (_modeOpen)
            {
                _mode.Text = "模式选择";
                return;
            }
            var parts = new System.Collections.Generic.List<string>();
            if (_shutdown.Checked) parts.Add("关机");
            if (_reboot.Checked) parts.Add("重启");
            if (_hibernate.Checked) parts.Add("休眠");
            if (_sleep.Checked) parts.Add("睡眠");
            _mode.Text = parts.Count == 0 ? "模式选择" : "模式选择  " + string.Join("、", parts.ToArray());
        }

        void LoadConfigToUi()
        {
            _loading = true;
            _config = Storage.LoadConfig(_paths);
            _auto.On = _config.AutoAuthEnabled;
            _shutdown.Checked = _config.TriggerShutdown;
            _reboot.Checked = _config.TriggerReboot;
            _hibernate.Checked = _config.TriggerHibernate;
            _sleep.Checked = _config.TriggerSleep;
            _vpn.Checked = _config.VpnDirect;
            _interval.Checked = _config.IntervalEnabled;
            decimal minutes = _config.IntervalMinutes;
            if (minutes < _minutes.Minimum) minutes = _minutes.Minimum;
            if (minutes > _minutes.Maximum) minutes = _minutes.Maximum;
            _minutes.Value = minutes;
            string savedAccount;
            string savedPassword;
            _hasStoredPassword = false;
            try
            {
                if (DpapiCredentialStore.TryLoad(_paths, out savedAccount, out savedPassword))
                {
                    _account.Text = savedAccount;
                    _hasStoredPassword = !string.IsNullOrEmpty(savedPassword);
                }
            }
            catch (Exception ex)
            {
                _banner.ShowErr("无法读取已保存的账号：" + ex.Message);
            }
            _password.Text = "";
            var state = Storage.LoadState(_paths);
            UpdatePortalLabel();
            if (!string.IsNullOrEmpty(state.LastSummary))
                _banner.ShowMuted(state.LastSummary);
            else
                _banner.ShowMuted(_hasStoredPassword ? "密码已加密保存在本机，留空表示不修改。" : "就绪");
            RefreshModeHeader();
            UpdatePasswordCue();
            _loading = false;
        }

        void UpdatePasswordCue()
        {
            if (!_hasStoredPassword)
                return;
            if (_password.Focused)
                return;
            if (_password.TextLength > 0 && !_showingPasswordCue)
                return;
            _showingPasswordCue = true;
            _password.UseSystemPasswordChar = false;
            _password.ForeColor = Palette.Muted;
            _password.Text = PasswordCue;
        }

        void HidePasswordCue()
        {
            if (!_showingPasswordCue)
                return;
            _showingPasswordCue = false;
            _password.ForeColor = Palette.Text;
            _password.Text = "";
            _password.UseSystemPasswordChar = true;
        }

        void UpdatePortalLabel()
        {
            _portal.Text = "目标网络：" + (_config.TargetSsid ?? "") + "\r\n当前门户：" + (_config.ActiveProfileId ?? "");
        }

        void ToggleAuto()
        {
            if (_busy || _loading)
                return;
            if (!_config.AutoAuthEnabled)
            {
                if (!Dialogs.Confirm(this, "开启后将默认一直开启，手动关闭可停止使用", "确定", "取消"))
                {
                    _auto.On = false;
                    return;
                }
                string account;
                string password;
                if (!ResolveCredentials(out account, out password))
                {
                    _auto.On = false;
                    _banner.ShowErr("请先填写账号和密码。");
                    return;
                }
                RunBusy(null, "开启自动认证", "处理中…", delegate
                {
                    PersistCredentials(account, password);
                    try
                    {
                        ReadUi(true);
                        SyncTasks();
                        var journal = Storage.LoadJournal(_paths);
                        ShortcutService.Ensure(Application.ExecutablePath, journal);
                        Storage.SaveJournal(_paths, journal);
                    }
                    catch (Exception ex)
                    {
                        _config.AutoAuthEnabled = false;
                        Storage.SaveConfig(_paths, _config);
                        try { ScheduledTaskService.DeleteAll(); } catch (Exception) { }
                        throw new InvalidOperationException("账号已保存在本机，但计划任务注册失败，自动认证未开启。" + ex.Message, ex);
                    }
                    return "已开启自动认证，计划任务已注册。";
                }, delegate { _auto.On = _config.AutoAuthEnabled; });
            }
            else
            {
                RunBusy(null, "开启自动认证", "处理中…", delegate
                {
                    ReadUi(false);
                    ScheduledTaskService.DeleteAll();
                    var journal = Storage.LoadJournal(_paths);
                    if (journal.Records != null)
                        journal.Records.RemoveAll(record => record.Group == "task");
                    Storage.SaveJournal(_paths, journal);
                    return "已关闭自动认证，计划任务已移除。";
                }, delegate { _auto.On = _config.AutoAuthEnabled; });
            }
        }

        void ModeChanged(object sender, EventArgs e)
        {
            if (_loading || _busy)
                return;
            RefreshModeHeader();
            ReadUi(_config.AutoAuthEnabled);
            if (_config.AutoAuthEnabled)
            {
                RunBusy(_mode, _mode.Text, "处理中…", delegate
                {
                    SyncTasks();
                    return "触发方式已更新。";
                }, null);
            }
            else
            {
                _banner.ShowOk("已保存，开启自动认证后生效。");
            }
        }

        void IntervalChanged(object sender, EventArgs e)
        {
            if (_loading || _busy)
                return;
            ReadUi(_config.AutoAuthEnabled);
            if (_config.AutoAuthEnabled)
            {
                RunBusy(_save, "保存账号", "处理中…", delegate
                {
                    SyncTasks();
                    return _config.IntervalEnabled
                        ? "已按每 " + _config.IntervalMinutes + " 分钟注册间隔检测。"
                        : "已关闭间隔检测。";
                }, null);
            }
        }

        void VpnChanged(object sender, EventArgs e)
        {
            if (_loading || _busy)
                return;
            if (_vpn.Checked)
            {
                if (!Dialogs.Confirm(this, "开启后，将更改此电脑的相关VPN配置，保障网络认证走直连", "确定", "取消"))
                {
                    _loading = true;
                    _vpn.Checked = false;
                    _loading = false;
                    return;
                }
                RunBusy(_save, "保存账号", "处理中…", delegate
                {
                    string ip = ResolvePortalIp();
                    var journal = Storage.LoadJournal(_paths);
                    ClashApplyResult result = ClashRuleService.Apply(_paths, journal, ip);
                    Storage.SaveJournal(_paths, journal);
                    if (!result.Ok)
                        throw new InvalidOperationException(result.Message);
                    _config.VpnDirect = true;
                    Storage.SaveConfig(_paths, _config);
                    return result.Message;
                }, delegate
                {
                    RefreshVpnInfo();
                    if (!_config.VpnDirect)
                    {
                        _loading = true;
                        _vpn.Checked = false;
                        _loading = false;
                    }
                });
            }
            else
            {
                RunBusy(_save, "保存账号", "处理中…", delegate
                {
                    RollbackService.Rollback(_paths, "vpn");
                    _config.VpnDirect = false;
                    Storage.SaveConfig(_paths, _config);
                    return "已撤销本程序写入的 VPN 直连规则。";
                }, delegate { RefreshVpnInfo(); });
            }
        }

        void RefreshVpnInfo()
        {
            _vpnInfo.Text = VpnScanner.Describe();
        }

        void VerifyNow()
        {
            if (_busy)
                return;
            string account;
            string password;
            if (!ResolveCredentials(out account, out password))
            {
                _banner.ShowErr("失败：请先填写账号和密码。");
                return;
            }
            _busy = true;
            _verify.SetBusy("验证中…");
            _banner.ShowMuted("正在检测连通性…");
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += delegate(object sender, System.ComponentModel.DoWorkEventArgs args)
            {
                using (SingleInstance single = SingleInstance.TryEnter("Global\\XianRenzheng.Core"))
                {
                    if (single == null)
                        throw new InvalidOperationException("已有一次认证正在运行。");
                    args.Result = AuthEngine.RunImmediate(account, password, delegate(string text)
                    {
                        try
                        {
                            BeginInvoke(new Action(delegate { _banner.ShowMuted(text); }));
                        }
                        catch (Exception)
                        {
                        }
                    });
                }
            };
            worker.RunWorkerCompleted += delegate(object sender, System.ComponentModel.RunWorkerCompletedEventArgs args)
            {
                _busy = false;
                _verify.ClearBusy("立即验证");
                if (args.Error != null)
                {
                    _banner.ShowErr("失败：" + args.Error.Message);
                    return;
                }
                string text = FormatVerify(args.Result as AuthRunResult);
                if (text.StartsWith("失败", StringComparison.Ordinal))
                    _banner.ShowErr(text);
                else
                    _banner.ShowOk(text);
            };
            worker.RunWorkerAsync();
        }

        static string FormatVerify(AuthRunResult result)
        {
            if (result == null)
                return "失败：没有返回结果";
            if (result.Status == AuthStatus.AlreadyOnline)
                return "已在线";
            if (result.Status == AuthStatus.Success && (result.Message == "网络已连通" || result.Message.IndexOf("已在线", StringComparison.Ordinal) >= 0))
                return "已在线";
            if (result.Status == AuthStatus.Success)
                return "认证成功";
            return "失败：" + result.Message;
        }

        void SaveAccount()
        {
            if (_busy)
                return;
            string account;
            string password;
            if (!ResolveCredentials(out account, out password))
            {
                _banner.ShowErr("请先填写账号和密码。");
                return;
            }
            RunBusy(_save, "保存账号", "处理中…", delegate
            {
                PersistCredentials(account, password);
                return "账号已加密保存在本机。未改动计划任务。";
            }, delegate { UpdatePasswordCue(); });
        }

        void Restore()
        {
            if (_busy)
                return;
            if (!Dialogs.Confirm(this, "此程序修改的设置将回到初始状态", "确定", "取消"))
                return;
            RunBusy(_restore, "恢复默认配置", "处理中…", delegate
            {
                RollbackService.Rollback(_paths, null);
                ScheduledTaskService.DeleteAll();
                _config = AppConfig.CreateDefault();
                Storage.SaveConfig(_paths, _config);
                return "已恢复本程序修改过的设置。账号仍保留在本机。";
            }, delegate { LoadConfigToUi(); });
        }

        void Logout()
        {
            if (_busy)
                return;
            if (!Dialogs.ConfirmLogout(this))
            {
                _banner.ShowMuted("已取消注销。");
                return;
            }
            RunBusy(_logout, "注销", "处理中…", delegate
            {
                AuthRunResult result;
                using (SingleInstance single = SingleInstance.TryEnter("Global\\XianRenzheng.Core"))
                {
                    if (single == null)
                        throw new InvalidOperationException("已有一次认证正在运行。");
                    result = AuthEngine.Run(TriggerKind.Logout);
                }
                if (result.ExitCode != 0 && result.Status != AuthStatus.Success && result.Status != AuthStatus.AlreadyOnline)
                    throw new InvalidOperationException(result.Message);
                return string.IsNullOrEmpty(result.Message) ? "已发送注销。" : result.Message;
            }, null);
        }

        void Identify()
        {
            if (_busy)
                return;
            if (!Dialogs.Confirm(this, "请打开网页认证页面并开启F12开发者模式，本程序会自动读取相关网页内容，一切数据仅存在本机，cookie不会读取", "确定", "取消"))
                return;
            string account;
            string password;
            if (!ResolveCredentials(out account, out password))
            {
                _banner.ShowErr("请先填写账号和密码，这样配置里只会留下占位符。");
                return;
            }
            using (var form = new CaptureForm(account, password))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.Result == null || !form.Result.Ok)
                {
                    if (form.Result != null && !string.IsNullOrEmpty(form.Result.Error))
                        _banner.ShowErr(form.Result.Error);
                    return;
                }
                RunBusy(_identify, "识别待连接网络", "处理中…", delegate
                {
                    return SaveRecognized(form.Result, string.IsNullOrEmpty(form.Ssid) ? _config.TargetSsid : form.Ssid);
                }, delegate { UpdatePortalLabel(); });
            }
        }

        string SaveRecognized(ClassificationResult result, string ssid)
        {
            PortalProfile finger = PortalSelector.Synthesize(ssid, result.SourceUrl);
            if (finger != null && finger.Kind != "template")
            {
                if (!string.IsNullOrEmpty(ssid) && finger.Ssids.Count == 0)
                    finger.Ssids.Add(ssid);
                Storage.SaveProfile(_paths, finger);
                _config.ActiveProfileId = finger.Id;
                if (!string.IsNullOrEmpty(ssid))
                    _config.TargetSsid = ssid;
                Storage.SaveConfig(_paths, _config);
                return "识别到 " + finger.Name + "，已保存门户配置。";
            }
            if (!string.IsNullOrEmpty(ssid) && (result.Profile.Ssids == null || result.Profile.Ssids.Count == 0))
                result.Profile.Ssids.Add(ssid);
            Storage.SaveProfile(_paths, result.Profile);
            _config.ActiveProfileId = result.Profile.Id;
            if (!string.IsNullOrEmpty(ssid))
                _config.TargetSsid = ssid;
            Storage.SaveConfig(_paths, _config);
            return "已识别为 " + result.ChineseKind + "，配置已保存在本机。";
        }

        void RunBusy(FeedbackButton button, string idleText, string busyText, Func<string> work, Action after)
        {
            if (_busy)
                return;
            _busy = true;
            if (button != null)
                button.SetBusy(busyText);
            UseWaitCursor = true;
            try
            {
                string message = work();
                _banner.ShowOk(message);
            }
            catch (Exception ex)
            {
                DailyLog.Error(ex.Message);
                _banner.ShowErr(ex.Message);
            }
            finally
            {
                UseWaitCursor = false;
                if (button != null)
                    button.ClearBusy(idleText);
                _busy = false;
                if (after != null)
                    after();
            }
        }

        bool ResolveCredentials(out string account, out string password)
        {
            string savedAccount = "";
            string savedPassword = "";
            try
            {
                DpapiCredentialStore.TryLoad(_paths, out savedAccount, out savedPassword);
            }
            catch (Exception)
            {
            }
            string typedPassword = _showingPasswordCue ? "" : _password.Text;
            CredentialEdit.Resolve(_account.Text, typedPassword, savedAccount, savedPassword, out account, out password);
            return !string.IsNullOrEmpty(account) && !string.IsNullOrEmpty(password);
        }

        void PersistCredentials(string account, string password)
        {
            DpapiCredentialStore.Save(_paths, account, password);
            _hasStoredPassword = true;
            _showingPasswordCue = false;
            _password.Text = "";
            UpdatePasswordCue();
            var state = Storage.LoadState(_paths);
            state.CredentialStampUtc = DpapiCredentialStore.Stamp(_paths);
            state.ConsecutiveCredentialFailures = 0;
            state.LastSummary = "账号已加密保存";
            Storage.SaveState(_paths, state);
        }

        void ReadUi(bool auto)
        {
            _config.AutoAuthEnabled = auto;
            _config.TriggerShutdown = _shutdown.Checked;
            _config.TriggerReboot = _reboot.Checked;
            _config.TriggerHibernate = _hibernate.Checked;
            _config.TriggerSleep = _sleep.Checked;
            _config.IntervalEnabled = _interval.Checked;
            _config.IntervalMinutes = (int)_minutes.Value;
            _auto.On = auto;
            Storage.SaveConfig(_paths, _config);
        }

        void SyncTasks()
        {
            string core = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XianAuthCore.exe");
            if (!File.Exists(core))
                throw new FileNotFoundException("找不到 XianAuthCore.exe，无法注册计划任务。");
            var journal = Storage.LoadJournal(_paths);
            ScheduledTaskService.Sync(_config, core, journal);
            Storage.SaveJournal(_paths, journal);
        }

        string ResolvePortalIp()
        {
            PortalProfile active = null;
            var profiles = Storage.LoadProfiles(_paths);
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i].Id == _config.ActiveProfileId)
                    active = profiles[i];
            }
            if (active == null)
                throw new InvalidOperationException("请先识别门户，或在本机保存门户配置。");
            string ip;
            if (PortalEndpoints.TryGetIpv4(active, out ip))
                return ip;
            string host = HostOf(active);
            if (!string.IsNullOrEmpty(host))
            {
                try
                {
                    IPAddress[] addresses = Dns.GetHostAddresses(host);
                    for (int i = 0; i < addresses.Length; i++)
                    {
                        if (addresses[i].AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                            continue;
                        if (ClashRules.TryNormalizeIpv4(addresses[i].ToString(), out ip))
                            return ip;
                    }
                }
                catch (Exception)
                {
                }
            }
            throw new InvalidOperationException("解析到的地址不可用，或位于 Clash Fake-IP 段。未修改分流规则。");
        }

        static string HostOf(PortalProfile profile)
        {
            string raw = null;
            if (profile.Drcom != null) raw = profile.Drcom.BaseUrl;
            else if (profile.Srun != null) raw = profile.Srun.BaseUrl;
            else if (profile.Ruijie != null) raw = profile.Ruijie.BaseUrl;
            else if (profile.Steps != null && profile.Steps.Count > 0) raw = profile.Steps[0].Url;
            Uri uri;
            if (!string.IsNullOrEmpty(raw) && Uri.TryCreate(raw, UriKind.Absolute, out uri))
                return uri.Host;
            return "";
        }

        void TrySetIcon()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "xian-auth.ico");
                if (File.Exists(path))
                    Icon = new Icon(path);
                else
                    Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
            }
        }
    }
}
