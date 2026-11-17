using System;
using System.Drawing;
using System.Windows.Forms;

namespace XianAuth
{
    internal static class Dialogs
    {
        public static bool Confirm(IWin32Window owner, string message, string okText, string cancelText)
        {
            using (var form = new PromptForm(message, okText, cancelText, 0))
                return form.ShowDialog(owner) == DialogResult.OK;
        }

        public static bool ConfirmLogout(IWin32Window owner)
        {
            using (var form = new PromptForm("将会断掉当前校园网的认证", "确认", "取消", 5))
                return form.ShowDialog(owner) == DialogResult.OK;
        }
    }

    internal sealed class PromptForm : Form
    {
        readonly Label _countdown;
        readonly Timer _timer;
        int _left;

        public PromptForm(string message, string okText, string cancelText, int seconds)
        {
            Text = "闲·认证";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(440, seconds > 0 ? 230 : 196);
            Font = UiFonts.Body(10f, FontStyle.Regular);
            BackColor = Palette.Background;

            var label = new Label
            {
                Text = message,
                AutoSize = false,
                Bounds = new Rectangle(24, 20, 392, 80),
                ForeColor = Palette.Text,
                BackColor = Palette.Background
            };
            Controls.Add(label);

            _countdown = new Label
            {
                AutoSize = false,
                Bounds = new Rectangle(24, 104, 392, 24),
                ForeColor = Palette.Muted,
                BackColor = Palette.Background,
                Visible = seconds > 0
            };
            Controls.Add(_countdown);

            var cancel = new FeedbackButton { Text = cancelText, Bounds = new Rectangle(200, seconds > 0 ? 156 : 124, 104, 44) };
            var ok = new FeedbackButton { Text = okText, Bounds = new Rectangle(312, cancel.Top, 104, 44) };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(cancel);
            Controls.Add(ok);
            CancelButton = cancel;
            AcceptButton = ok;

            _left = seconds;
            _timer = new Timer { Interval = 1000 };
            if (seconds > 0)
            {
                _countdown.Text = seconds + " 秒内未确认将自动取消";
                _timer.Tick += delegate
                {
                    _left--;
                    if (_left <= 0)
                    {
                        _timer.Stop();
                        DialogResult = DialogResult.Cancel;
                        Close();
                        return;
                    }
                    _countdown.Text = _left + " 秒内未确认将自动取消";
                };
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_left > 0)
                _timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
