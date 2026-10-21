using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BikiniFriends
{
    /// <summary>정각·타이머 알림 카드. 직접 닫을 때까지 남고, 화면 가장자리 빛과 함께 보인다.</summary>
    internal sealed class HourlyNotice : Form
    {
        private readonly Label header, caption, clock, message;
        private readonly Button confirm, close;
        private readonly List<Font> ownedFonts = new List<Font>();
        private readonly float scale;
        private bool edgeRequested;
        private EdgeGlowController glow;
        private int glowTheme, glowWidth = 1, glowStrength = 1;
        internal bool GlowRunning { get { return glow != null && glow.Running; } }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= Native.WS_EX_NOACTIVATE; p.ClassStyle |= 0x20000; return p; }
        }

        internal HourlyNotice(Icon icon)
        {
            scale = Math.Max(1, Native.SystemDpiScale()); AutoScaleMode = AutoScaleMode.None;
            Text = "비키니친구들 · 정각 알림"; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true; TopMost = true; MaximizeBox = false; MinimizeBox = false; DoubleBuffered = true;
            BackColor = Color.FromArgb(255, 253, 249); ForeColor = Color.FromArgb(45, 48, 64); if (icon != null) Icon = icon;
            header = LabelAt("비키니친구들", 24, 21, 264, 22, 9, Color.FromArgb(110, 103, 115));
            caption = LabelAt("정각 알림", 24, 58, 290, 19, 9, Color.FromArgb(153, 117, 92));
            clock = LabelAt("00:00", 20, 77, 310, 72, 38, ForeColor); clock.Font = OwnFont("Segoe UI", 38, FontStyle.Regular);
            message = LabelAt("", 24, 155, 312, 46, 10, Color.FromArgb(91, 87, 101));
            close = new CardButton(true) { Text = "×", Name = "close", AccessibleName = "알림 닫기", Bounds = Rect(311, 15, 32, 32), Font = OwnFont("Segoe UI", 14, FontStyle.Regular) };
            close.Click += delegate { Close(); }; Controls.Add(close);
            confirm = new CardButton(false) { Text = "확인했어", Name = "confirm", Bounds = Rect(239, 218, 97, 38), Font = OwnFont("Malgun Gothic", 9.5f, FontStyle.Regular) };
            confirm.Click += delegate { Close(); }; Controls.Add(confirm); AcceptButton = confirm; CancelButton = close;
            ClientSize = new Size(Pixel(360), Pixel(277));
            header.MouseDown += DragCard; MouseDown += DragCard; caption.MouseDown += DragCard;
            SetMessage(App.HourlyText(DateTime.Now));
        }

        private int Pixel(float v) { return (int)Math.Round(v * scale); }
        private Rectangle Rect(float x, float y, float w, float h) { return new Rectangle(Pixel(x), Pixel(y), Pixel(w), Pixel(h)); }
        private Font OwnFont(string name, float size, FontStyle style) { var f = new Font(name, size * scale, style, GraphicsUnit.Point); ownedFonts.Add(f); return f; }
        private Label LabelAt(string text, float x, float y, float w, float h, float size, Color color)
        {
            var l = new Label { Text = text, Bounds = Rect(x, y, w, h), ForeColor = color, BackColor = BackColor, Font = OwnFont("Malgun Gothic", size, FontStyle.Regular) };
            Controls.Add(l); return l;
        }

        internal void SetMessage(string text)
        {
            bool timer = Text.Contains("타이머"); DateTime now = DateTime.Now;
            caption.Text = timer ? "타이머" : "정각 알림"; clock.Text = timer ? "시간이 됐어" : now.Hour.ToString("00") + ":00";
            if (timer) clock.Font = OwnFont("Malgun Gothic", 23, FontStyle.Regular);
            string friendly = (text ?? "").Replace("지금은 ", "").Trim();
            message.Text = friendly;
            int maximum = Math.Max(Pixel(46), Screen.FromPoint(Cursor.Position).WorkingArea.Height / 3);
            Size measured = TextRenderer.MeasureText(friendly, message.Font, new Size(Pixel(312), maximum), TextFormatFlags.WordBreak);
            message.Height = Math.Min(maximum, Math.Max(Pixel(46), measured.Height + Pixel(5)));
            message.AutoEllipsis = true; confirm.Top = message.Bottom + Pixel(17); ClientSize = new Size(Pixel(360), confirm.Bottom + Pixel(21));
            if (!Visible) PositionCard();
        }

        private void PositionCard()
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Min(Pixel(48), Math.Max(0, area.Height - Height)));
        }

        internal void EnableEdgeGlow(bool enable, int theme = 0, int width = 1, int strength = 1)
        {
            bool changed = glowTheme != theme || glowWidth != width || glowStrength != strength;
            glowTheme = theme; glowWidth = width; glowStrength = strength; edgeRequested = enable;
            if (!enable || changed) StopGlow();
            if (enable && Visible && glow == null) glow = new EdgeGlowController(this, glowTheme, glowWidth, glowStrength);
        }
        private void StopGlow() { if (glow != null) { glow.Dispose(); glow = null; } }
        protected override void OnShown(EventArgs e) { base.OnShown(e); if (edgeRequested && glow == null) glow = new EdgeGlowController(this, glowTheme, glowWidth, glowStrength); }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e); if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
            using (var path = Rounded(new RectangleF(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), 21 * Math.Max(1, scale)))
            { Region old = Region; Region = new Region(path); if (old != null) old.Dispose(); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(new RectangleF(.6f, .6f, ClientSize.Width - 1.2f, ClientSize.Height - 1.2f), 21 * scale))
            using (var pen = new Pen(Color.FromArgb(226, 219, 213), 1)) e.Graphics.DrawPath(pen, path);
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { StopGlow(); base.OnFormClosed(e); }
        protected override void Dispose(bool disposing)
        { StopGlow(); base.Dispose(disposing); if (disposing) { foreach (var f in ownedFonts) f.Dispose(); ownedFonts.Clear(); } }
        private void DragCard(object sender, MouseEventArgs e)
        { if (e.Button != MouseButtons.Left) return; Native.ReleaseCapture(); Native.SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); }

        internal static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }

        private sealed class CardButton : Button
        {
            private readonly bool icon; private bool hover;
            internal CardButton(bool icon) { this.icon = icon; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent == null ? Color.FromArgb(255, 253, 249) : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color bg = icon ? (hover ? Color.FromArgb(242, 236, 229) : Color.FromArgb(255, 253, 249)) : (hover ? Color.FromArgb(78, 81, 101) : Color.FromArgb(60, 63, 81));
                using (var path = Rounded(new RectangleF(0, 0, Width - 1, Height - 1), icon ? 8 : 12)) using (var brush = new SolidBrush(bg)) e.Graphics.FillPath(brush, path);
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, icon ? Color.FromArgb(108, 103, 114) : Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
            }
        }
    }

    /// <summary>알림창이 떠 있는 동안 화면 네 가장자리를 은은하게 빛내는 클릭 통과 창 모음.</summary>
    internal sealed class EdgeGlowController : IDisposable
    {
        private readonly Form owner;
        private readonly List<EdgeGlowWindow> windows = new List<EdgeGlowWindow>();
        private readonly System.Windows.Forms.Timer pulse = new System.Windows.Forms.Timer { Interval = 40 };
        private readonly int theme, width, strength;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private bool disposed;
        internal bool Running { get { return !disposed; } }

        internal EdgeGlowController(Form owner, int theme = 0, int width = 1, int strength = 1)
        {
            this.owner = owner; this.theme = theme; this.width = width; this.strength = strength;
            pulse.Tick += delegate
            {
                if (owner.IsDisposed) { Dispose(); return; }
                byte alpha = PulseAlpha(clock.Elapsed.TotalSeconds, strength);
                foreach (var w in windows) w.SetGlowAlpha(alpha);
            };
            Rebuild(); SystemEvents.DisplaySettingsChanged += DisplayChanged;
            pulse.Start();
        }

        private void DisplayChanged(object sender, EventArgs e)
        {
            if (disposed || owner.IsDisposed || !owner.IsHandleCreated) return;
            try { owner.BeginInvoke((Action)delegate { if (!disposed) Rebuild(); }); } catch { }
        }

        private void Rebuild()
        {
            foreach (var w in windows) w.Dispose(); windows.Clear();
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle r = screen.Bounds; int depth = Depth(r.Size, width);
                var parts = new[]
                {
                    new Rectangle(r.Left, r.Top, r.Width, depth), new Rectangle(r.Left, r.Bottom - depth, r.Width, depth),
                    new Rectangle(r.Left, r.Top + depth, depth, Math.Max(1, r.Height - depth * 2)), new Rectangle(r.Right - depth, r.Top + depth, depth, Math.Max(1, r.Height - depth * 2))
                };
                foreach (Rectangle part in parts)
                {
                    try { var w = new EdgeGlowWindow(part, r, depth, theme); windows.Add(w); w.Show(); w.SetGlowAlpha(PulseAlpha(0, strength)); }
                    catch (Exception error) { FeatureLog.Write(error); }
                }
            }
        }

        internal static int Depth(Size screen, int width)
        {
            double fraction = width <= 0 ? .08 : width == 1 ? .12 : .16;
            int shortest = Math.Min(screen.Width, screen.Height);
            return Math.Max(1, Math.Min(shortest / 3, Math.Min(360, Math.Max(12, (int)Math.Round(shortest * fraction)))));
        }

        internal static byte PulseAlpha(double seconds, int strength)
        {
            double middle = strength <= 0 ? 190 : strength == 1 ? 227.5 : 240;
            double amplitude = strength <= 0 ? 25 : strength == 1 ? 22.5 : 15;
            return (byte)Math.Round(middle + amplitude * Math.Sin(seconds * Math.PI * 2 / 3.4));
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true; pulse.Stop(); pulse.Dispose();
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            foreach (var w in windows) w.Dispose(); windows.Clear(); clock.Stop();
        }
    }

    internal sealed class EdgeGlowWindow : Form
    {
        private IntPtr dc, image, previous;
        private readonly Rectangle area;
        private int previousAlpha = -1;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST | Native.WS_EX_TRANSPARENT; return p; }
        }

        internal EdgeGlowWindow(Rectangle area, Rectangle monitor, int depth, int theme)
        {
            this.area = area; AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; Bounds = area;
            try
            {
                using (Bitmap bitmap = CreateGlowBitmap(area, monitor, depth, theme))
                {
                    byte[] bytes = LayerPixels.Premultiplied(bitmap); IntPtr screen = Native.GetDC(IntPtr.Zero);
                    try
                    {
                        dc = Native.CreateCompatibleDC(screen); if (dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                        var info = new Native.BitmapInfo { Size = 40, Width = bitmap.Width, Height = -bitmap.Height, Planes = 1, Bits = 32, ImageSize = (uint)bytes.Length };
                        IntPtr bits; image = Native.CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0);
                        if (image == IntPtr.Zero || bits == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                        Marshal.Copy(bytes, 0, bits, bytes.Length); previous = Native.SelectObject(dc, image);
                    }
                    finally { Native.ReleaseDC(IntPtr.Zero, screen); }
                }
            }
            catch { Dispose(); throw; }
        }

        internal static Bitmap CreateGlowBitmap(Rectangle part, Rectangle monitor, int depth, int theme)
        {
            var bitmap = new Bitmap(Math.Max(1, part.Width), Math.Max(1, part.Height), PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[data.Stride * bitmap.Height]; var palette = Palette(theme);
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int gx = part.Left - monitor.Left + x, gy = part.Top - monitor.Top + y;
                        int distance = Math.Min(Math.Min(gx, monitor.Width - 1 - gx), Math.Min(gy, monitor.Height - 1 - gy));
                        double t = Math.Max(0, Math.Min(1, distance / (double)Math.Max(1, depth - 1)));
                        int alpha = (int)Math.Round(255 * (.78 * Math.Pow(1 - t, 1.35) + .22 * Math.Exp(-distance / 3.0) * (1 - t)));
                        double position = .55 * gx / Math.Max(1.0, monitor.Width - 1) + .45 * gy / Math.Max(1.0, monitor.Height - 1);
                        double ci = Math.Max(0, Math.Min(palette.Length - 1, position * (palette.Length - 1)));
                        int index = Math.Min(palette.Length - 2, (int)ci); double mix = ci - index;
                        Color a = palette[index], b = palette[index + 1]; int o = y * data.Stride + x * 4;
                        bytes[o] = (byte)Math.Round(a.B + (b.B - a.B) * mix); bytes[o + 1] = (byte)Math.Round(a.G + (b.G - a.G) * mix);
                        bytes[o + 2] = (byte)Math.Round(a.R + (b.R - a.R) * mix); bytes[o + 3] = (byte)Math.Max(0, Math.Min(255, alpha));
                    }
                Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }

        private static Color[] Palette(int theme)
        {
            if (theme == 1) return new[] { Color.FromArgb(255, 186, 93), Color.FromArgb(255, 115, 137), Color.FromArgb(202, 111, 222), Color.FromArgb(133, 136, 249) };
            if (theme == 2) return new[] { Color.FromArgb(104, 240, 188), Color.FromArgb(107, 219, 249), Color.FromArgb(145, 154, 255), Color.FromArgb(224, 139, 249) };
            if (theme == 3) return new[] { Color.FromArgb(255, 117, 146), Color.FromArgb(255, 188, 98), Color.FromArgb(225, 230, 104), Color.FromArgb(106, 222, 157), Color.FromArgb(99, 197, 244), Color.FromArgb(170, 139, 248), Color.FromArgb(246, 138, 207) };
            return new[] { Color.FromArgb(255, 172, 117), Color.FromArgb(255, 143, 185), Color.FromArgb(181, 147, 255), Color.FromArgb(105, 205, 255) };
        }

        internal void SetGlowAlpha(byte alpha)
        {
            if (IsDisposed || dc == IntPtr.Zero || alpha == previousAlpha) return;
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            try
            {
                var src = new Native.POINT(0, 0); var dst = new Native.POINT(area.Left, area.Top); var size = new Native.SIZE(area.Width, area.Height);
                var blend = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
                if (!Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, dc, ref src, 0, ref blend, Native.ULW_ALPHA)) throw new Win32Exception(Marshal.GetLastWin32Error());
                previousAlpha = alpha;
            }
            catch (Exception error) { FeatureLog.Write(error); }
            finally { Native.ReleaseDC(IntPtr.Zero, screen); }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; }   // WM_NCHITTEST → HTTRANSPARENT
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; }    // WM_MOUSEACTIVATE → MA_NOACTIVATE
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (dc != IntPtr.Zero)
            {
                if (previous != IntPtr.Zero && previous != new IntPtr(-1)) Native.SelectObject(dc, previous);
                if (image != IntPtr.Zero) { Native.DeleteObject(image); image = IntPtr.Zero; }
                Native.DeleteDC(dc); dc = IntPtr.Zero;
            }
            base.Dispose(disposing);
        }
    }
}
