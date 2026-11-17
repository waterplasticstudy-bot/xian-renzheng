using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace XianAuth
{
    internal static class Palette
    {
        public static readonly Color Background = Color.FromArgb(18, 8, 32);
        public static readonly Color Magenta = Color.FromArgb(255, 45, 149);
        public static readonly Color Violet = Color.FromArgb(124, 58, 237);
        public static readonly Color Cyan = Color.FromArgb(34, 211, 238);
        public static readonly Color HeaderLeft = Magenta;
        public static readonly Color HeaderRight = Cyan;
        public static readonly Color Accent = Violet;
        public static readonly Color AccentPressed = Color.FromArgb(88, 28, 196);
        public static readonly Color AccentHover = Color.FromArgb(168, 85, 247);
        public static readonly Color Text = Color.FromArgb(255, 248, 252);
        public static readonly Color Muted = Color.FromArgb(214, 190, 230);
        public static readonly Color Success = Color.FromArgb(52, 232, 158);
        public static readonly Color Danger = Color.FromArgb(255, 92, 138);
        public static readonly Color Line = Color.FromArgb(92, 48, 128);
        public static readonly Color Card = Color.FromArgb(42, 18, 68);
        public static readonly Color Input = Color.FromArgb(36, 14, 58);
    }

    internal static class UiFonts
    {
        public static Font Body(float size, FontStyle style)
        {
            string[] names = { "Microsoft YaHei UI", "Microsoft YaHei", "SimSun" };
            for (int i = 0; i < names.Length; i++)
            {
                try { return new Font(names[i], size, style, GraphicsUnit.Point); }
                catch (ArgumentException) { }
            }
            return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
        }
    }

    internal static class PaintUtil
    {
        public static GraphicsPath Round(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class HeaderPanel : Panel
    {
        readonly Font _title;
        readonly Font _sub;
        readonly Image _icon;

        public HeaderPanel()
        {
            Height = 96;
            Dock = DockStyle.Top;
            _title = UiFonts.Body(20f, FontStyle.Bold);
            _sub = UiFonts.Body(9.5f, FontStyle.Regular);
            _icon = LoadIcon();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        static Image LoadIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("XianAuth.icon.png"))
            {
                if (stream == null)
                    return null;
                using (Image image = Image.FromStream(stream))
                    return new Bitmap(image);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle area = ClientRectangle;
            if (area.Width < 1 || area.Height < 1)
                return;
            using (var brush = new LinearGradientBrush(area, Palette.Magenta, Palette.Cyan, LinearGradientMode.Horizontal))
            {
                var blend = new ColorBlend
                {
                    Colors = new[] { Palette.Magenta, Palette.Violet, Palette.Cyan },
                    Positions = new[] { 0f, 0.55f, 1f }
                };
                brush.InterpolationColors = blend;
                e.Graphics.FillRectangle(brush, area);
            }
            if (_icon != null)
                e.Graphics.DrawImage(_icon, new Rectangle(18, 18, 60, 60));
            TextRenderer.DrawText(e.Graphics, "闲·认证", _title, new Rectangle(92, 20, Width - 110, 36), Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "校园网自动认证", _sub, new Rectangle(94, 54, Width - 110, 24), Color.FromArgb(230, 244, 244),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _title.Dispose();
                _sub.Dispose();
                if (_icon != null)
                    _icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class Banner : Panel
    {
        string _text = "就绪";
        Color _color = Palette.Muted;

        public Banner()
        {
            Height = 78;
            Dock = DockStyle.Bottom;
            BackColor = Palette.Background;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void ShowMuted(string text) { Set(text, Palette.Muted); }
        public void ShowOk(string text) { Set(text, Palette.Success); }
        public void ShowErr(string text) { Set(text, Palette.Danger); }

        void Set(string text, Color color)
        {
            _text = text ?? "";
            _color = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Palette.Background);
            Rectangle pill = new Rectangle(16, 12, Math.Max(1, Width - 32), Math.Max(1, Height - 24));
            using (GraphicsPath glow = PaintUtil.Round(Rectangle.Inflate(pill, 2, 2), pill.Height / 2))
            using (var glowBrush = new SolidBrush(Color.FromArgb(70, _color)))
                e.Graphics.FillPath(glowBrush, glow);
            using (GraphicsPath path = PaintUtil.Round(pill, pill.Height / 2))
            using (var brush = new SolidBrush(Palette.Card))
            using (var pen = new Pen(_color, 2f))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
                TextRenderer.DrawText(e.Graphics, _text, Font, Rectangle.Inflate(pill, -16, -4), _color,
                    TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter);
            }
        }
    }

    internal sealed class FeedbackButton : Button
    {
        bool _down;
        bool _hover;
        bool _busy;
        float _lift;
        Timer _anim;
        Chevron _chevron;

        public Chevron Chevron
        {
            get { return _chevron; }
            set { _chevron = value; Invalidate(); }
        }

        public FeedbackButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Height = 48;
            BackColor = Palette.Background;
            ForeColor = Color.White;
            _anim = new Timer { Interval = 16 };
            _anim.Tick += delegate { StepLift(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _anim != null)
                _anim.Dispose();
            base.Dispose(disposing);
        }

        public void SetBusy(string text)
        {
            _busy = true;
            Text = text;
            Enabled = false;
            Invalidate();
        }

        public void ClearBusy(string text)
        {
            _busy = false;
            Text = text;
            Enabled = true;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _down = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _down = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            _anim.Start();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _down = false;
            _anim.Start();
            base.OnMouseLeave(e);
        }

        void StepLift()
        {
            float target = _hover && Enabled ? 1f : 0f;
            _lift += (target - _lift) * 0.35f;
            if (Math.Abs(target - _lift) < 0.02f)
            {
                _lift = target;
                _anim.Stop();
            }
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.Clear(Palette.Background);
            int rise = _down ? 0 : (int)Math.Round(3 * _lift);
            int pad = 6;
            Rectangle pill = new Rectangle(pad, pad + 3 - rise, Math.Max(8, Width - (pad * 2)), Math.Max(8, Height - (pad * 2) - 4));
            if (!Enabled || _busy)
            {
                using (GraphicsPath idle = PaintUtil.Round(pill, pill.Height / 2))
                using (var brush = new SolidBrush(Color.FromArgb(90, 70, 110)))
                    e.Graphics.FillPath(brush, idle);
            }
            else
            {
                int layers = 3;
                for (int i = layers; i >= 1; i--)
                {
                    Rectangle glow = Rectangle.Inflate(pill, i, i);
                    int alpha = (int)((16 + 22 * _lift) * (layers - i + 1));
                    if (alpha > 120)
                        alpha = 120;
                    using (GraphicsPath glowPath = PaintUtil.Round(glow, glow.Height / 2))
                    using (var glowBrush = new SolidBrush(Color.FromArgb(alpha, 255, 70, 190)))
                        e.Graphics.FillPath(glowBrush, glowPath);
                }
                using (GraphicsPath path = PaintUtil.Round(pill, pill.Height / 2))
                using (var brush = new LinearGradientBrush(pill, Palette.Magenta, Palette.Cyan, LinearGradientMode.Horizontal))
                {
                    var blend = new ColorBlend
                    {
                        Colors = new[] { _down ? Palette.AccentPressed : Palette.Magenta, Palette.Violet, Palette.Cyan },
                        Positions = new[] { 0f, 0.5f, 1f }
                    };
                    brush.InterpolationColors = blend;
                    e.Graphics.FillPath(brush, path);
                }
            }
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            Rectangle text = new Rectangle(pill.X + 8, pill.Y, Math.Max(1, pill.Width - 28), pill.Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, text, Color.White, flags);
            if (_chevron != Chevron.None)
                DrawChevron(e.Graphics, Color.White, pill);
        }

        void DrawChevron(Graphics graphics, Color color, Rectangle pill)
        {
            int cx = pill.Right - 16;
            int cy = pill.Y + (pill.Height / 2);
            Point[] points = _chevron == Chevron.Up
                ? new[] { new Point(cx - 5, cy + 2), new Point(cx + 5, cy + 2), new Point(cx, cy - 3) }
                : new[] { new Point(cx - 5, cy - 2), new Point(cx + 5, cy - 2), new Point(cx, cy + 3) };
            using (var brush = new SolidBrush(color))
                graphics.FillPolygon(brush, points);
        }
    }

    internal enum Chevron
    {
        None,
        Up,
        Down
    }

    internal sealed class ToggleSwitch : Control
    {
        bool _down;
        bool _on;

        public ToggleSwitch()
        {
            Width = 58;
            Height = 32;
            BackColor = Palette.Background;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public bool On
        {
            get { return _on; }
            set { _on = value; Invalidate(); }
        }

        public event EventHandler Toggled;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _down = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool inside = ClientRectangle.Contains(e.Location);
            _down = false;
            Invalidate();
            if (inside && e.Button == MouseButtons.Left && Toggled != null)
                Toggled(this, EventArgs.Empty);
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Palette.Background);
            Rectangle bounds = new Rectangle(2, 4, Width - 5, Height - 8);
            using (GraphicsPath path = PaintUtil.Round(bounds, bounds.Height / 2))
            {
                if (_on)
                {
                    using (var brush = new LinearGradientBrush(bounds, Palette.Magenta, Palette.Cyan, LinearGradientMode.Horizontal))
                        e.Graphics.FillPath(brush, path);
                }
                else
                {
                    using (var brush = new SolidBrush(Color.FromArgb(72, 48, 96)))
                        e.Graphics.FillPath(brush, path);
                }
            }
            int knob = _on ? Width - 26 : 4;
            if (_down)
                knob += _on ? -1 : 1;
            using (var glow = new SolidBrush(Color.FromArgb(_on ? 90 : 30, 255, 255, 255)))
                e.Graphics.FillEllipse(glow, knob - 2, 3, 26, 26);
            using (var brush = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(brush, knob, 5, 22, 22);
        }
    }
}
