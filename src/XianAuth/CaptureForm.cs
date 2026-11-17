using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace XianAuth
{
    internal sealed class CaptureForm : Form
    {
        readonly string _account;
        readonly string _password;
        readonly BrowserCapture _capture = new BrowserCapture();
        readonly Label _status;
        readonly FeedbackButton _done;
        readonly FeedbackButton _har;
        readonly FeedbackButton _paste;
        readonly FeedbackButton _cancel;
        bool _busy;

        public ClassificationResult Result { get; private set; }
        public string Ssid { get; private set; }

        public CaptureForm(string account, string password)
        {
            _account = account;
            _password = password;
            Text = "识别待连接网络";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            ClientSize = new Size(480, 280);
            Font = UiFonts.Body(10f, FontStyle.Regular);
            BackColor = Palette.Background;

            var hint = new Label
            {
                Text = "请在打开的浏览器中完成一次门户登录，然后点击“完成识别”。本程序不读取 cookie。若已经在线，请先注销再登录一次。",
                Bounds = new Rectangle(20, 16, 440, 64),
                ForeColor = Palette.Text,
                BackColor = Palette.Background
            };
            _status = new Label
            {
                Text = "正在打开浏览器…",
                Bounds = new Rectangle(20, 86, 440, 40),
                ForeColor = Palette.Muted,
                BackColor = Palette.Background
            };
            _done = new FeedbackButton { Text = "完成识别", Bounds = new Rectangle(16, 196, 108, 48) };
            _har = new FeedbackButton { Text = "导入 HAR", Bounds = new Rectangle(130, 196, 108, 48) };
            _paste = new FeedbackButton { Text = "粘贴网址", Bounds = new Rectangle(244, 196, 108, 48) };
            _cancel = new FeedbackButton { Text = "取消", Bounds = new Rectangle(358, 196, 104, 48) };
            _done.Click += delegate { Finish(_capture.Snapshot()); };
            _har.Click += delegate { ImportHar(); };
            _paste.Click += delegate { PasteUrl(); };
            _cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(hint);
            Controls.Add(_status);
            Controls.Add(_done);
            Controls.Add(_har);
            Controls.Add(_paste);
            Controls.Add(_cancel);
            Ssid = ReadSsid();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                _capture.Start("http://www.msftconnecttest.com/connecttest.txt");
                _status.Text = string.IsNullOrEmpty(_capture.Status) ? "浏览器已打开。" : _capture.Status;
            }
            catch (Exception ex)
            {
                _status.Text = "浏览器未能启动：" + ex.Message;
            }
        }

        void Finish(IList<CapturedRequest> requests)
        {
            if (_busy)
                return;
            _busy = true;
            _done.SetBusy("处理中…");
            try
            {
                Result = RequestClassifier.Classify(requests, new ClassifyHints { Account = _account, Password = _password }, Ssid);
                if (Result != null && Result.Ok)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }
                _status.Text = Result == null ? "没有结果" : Result.Error;
                _status.ForeColor = Palette.Danger;
            }
            finally
            {
                _done.ClearBusy("完成识别");
                _busy = false;
            }
        }

        void ImportHar()
        {
            if (_busy)
                return;
            using (var dialog = new OpenFileDialog { Filter = "HAR 文件|*.har|所有文件|*.*", Title = "导入 HAR" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                _busy = true;
                _har.SetBusy("处理中…");
                try
                {
                    string json = File.ReadAllText(dialog.FileName);
                    Result = RequestClassifier.Classify(HarImport.Read(json), new ClassifyHints { Account = _account, Password = _password }, Ssid);
                    if (Result != null && Result.Ok)
                    {
                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }
                    _status.Text = Result == null ? "导入失败" : Result.Error;
                    _status.ForeColor = Palette.Danger;
                }
                catch (Exception ex)
                {
                    _status.Text = "无法读取 HAR：" + ex.Message;
                    _status.ForeColor = Palette.Danger;
                }
                finally
                {
                    _har.ClearBusy("导入 HAR");
                    _busy = false;
                }
            }
        }

        void PasteUrl()
        {
            if (_busy)
                return;
            string url = "";
            using (var form = new Form())
            {
                form.Text = "粘贴认证网址";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.ClientSize = new Size(460, 150);
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.Font = Font;
                var box = new TextBox { Bounds = new Rectangle(16, 16, 428, 24) };
                var ok = new FeedbackButton { Text = "确定", Bounds = new Rectangle(250, 90, 90, 36) };
                var cancel = new FeedbackButton { Text = "取消", Bounds = new Rectangle(350, 90, 90, 36) };
                ok.Click += delegate { form.DialogResult = DialogResult.OK; };
                cancel.Click += delegate { form.DialogResult = DialogResult.Cancel; };
                form.Controls.Add(box);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                if (form.ShowDialog(this) != DialogResult.OK)
                    return;
                url = box.Text;
            }
            _busy = true;
            _paste.SetBusy("处理中…");
            try
            {
                Result = RequestClassifier.FromPastedUrl(url, new ClassifyHints { Account = _account, Password = _password }, Ssid);
                if (Result != null && Result.Ok)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }
                _status.Text = Result == null ? "无法识别网址" : Result.Error;
                _status.ForeColor = Palette.Danger;
            }
            finally
            {
                _paste.ClearBusy("粘贴网址");
                _busy = false;
            }
        }

        static string ReadSsid()
        {
            try
            {
                var all = NativeWifi.QueryAll();
                if (all.Count > 0)
                    return all[0].Ssid;
            }
            catch (Exception)
            {
            }
            return "";
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _capture.Dispose();
            base.OnFormClosed(e);
        }
    }
}
