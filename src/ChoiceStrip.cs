using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BikiniFriends
{
    /// <summary>
    /// 그림 카드 여러 장 중 하나를 고르는 가로 선택줄(콤보박스 대신). 카드마다 그림(painter)과 글자가 들어간다.
    /// 설정창의 빛 색상 · 퍼지는 폭 · 진하기처럼 "보고 고르는" 값에 쓴다.
    /// </summary>
    internal sealed class ChoiceStrip : Control
    {
        internal delegate void IconPainter(Graphics g, Rectangle iconRect, int index);

        private const int Gap = 6, PadTop = 7, LabelH = 18, PadBottom = 4;
        private readonly string[] labels; private readonly IconPainter painter; private readonly Size iconSize;
        private int selected, hot = -1;
        internal event EventHandler SelectedIndexChanged;

        /// <param name="cellWidth">카드 한 장의 너비(96dpi 기준 픽셀)</param>
        /// <param name="iconSize">카드 안 그림 영역 크기(96dpi 기준)</param>
        internal ChoiceStrip(string[] labels, int selectedIndex, int cellWidth, Size iconSize, IconPainter painter)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            this.labels = labels; this.painter = painter; this.iconSize = iconSize;
            selected = Math.Max(0, Math.Min(labels.Length - 1, selectedIndex));
            TabStop = true; Cursor = Cursors.Hand; Margin = new Padding(3);
            Size = new Size(labels.Length * cellWidth + (labels.Length - 1) * Gap, PadTop + iconSize.Height + 3 + LabelH + PadBottom);
        }

        internal int SelectedIndex
        {
            get { return selected; }
            set
            {
                value = Math.Max(0, Math.Min(labels.Length - 1, value));
                if (value == selected) return;
                selected = value; Invalidate();
                var h = SelectedIndexChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }

        private float UiScale { get { return DeviceDpi / 96f; } }

        private Rectangle Cell(int i)
        {
            int gap = (int)Math.Round(Gap * UiScale), n = labels.Length;
            int w = (Width - gap * (n - 1)) / n;
            return new Rectangle(i * (w + gap), 0, w, Height - 1);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < labels.Length; i++) if (Cell(i).Contains(p)) return i;
            return -1;
        }

        internal static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = Math.Max(2, radius * 2); var p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(BackColor);
            float s = UiScale; int radius = (int)Math.Round(8 * s);
            using (var font = new Font(Font.FontFamily, 8.5f))
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    Rectangle c = Cell(i); bool sel = i == selected, isHot = i == hot && Enabled;
                    using (var path = RoundRect(c, radius))
                    {
                        using (var fill = new SolidBrush(sel ? Color.FromArgb(232, 240, 255) : Color.White)) g.FillPath(fill, path);
                        using (var pen = new Pen(sel ? FriendUI.Blue : isHot ? Color.FromArgb(120, 160, 230) : Color.FromArgb(200, 212, 235), sel ? 2f : 1f)) g.DrawPath(pen, path);
                    }
                    int iw = (int)Math.Round(iconSize.Width * s), ih = (int)Math.Round(iconSize.Height * s);
                    painter(g, new Rectangle(c.Left + (c.Width - iw) / 2, c.Top + (int)Math.Round(PadTop * s), iw, ih), i);
                    var lr = new Rectangle(c.Left + 2, c.Top + (int)Math.Round((PadTop + 3) * s) + ih, c.Width - 4, (int)Math.Round(LabelH * s));
                    TextRenderer.DrawText(g, labels[i], font, lr, sel ? FriendUI.BlueDark : FriendUI.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    if (sel) DrawBadge(g, c, s);
                    if (sel && Focused && ShowFocusCues) { var fr = Rectangle.Inflate(c, -3, -3); ControlPaint.DrawFocusRectangle(g, fr); }
                }
            }
            if (!Enabled) using (var veil = new SolidBrush(Color.FromArgb(150, BackColor))) g.FillRectangle(veil, ClientRectangle);
        }

        // 선택된 카드 오른쪽 위의 파란 체크 배지
        private static void DrawBadge(Graphics g, Rectangle c, float s)
        {
            int d = (int)Math.Round(14 * s); var r = new Rectangle(c.Right - d - (int)Math.Round(4 * s), c.Top + (int)Math.Round(4 * s), d, d);
            using (var br = new SolidBrush(FriendUI.Blue)) g.FillEllipse(br, r);
            using (var pen = new Pen(Color.White, Math.Max(1.5f, 1.6f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(r.Left + d * .27f, r.Top + d * .53f), new PointF(r.Left + d * .44f, r.Top + d * .70f), new PointF(r.Left + d * .74f, r.Top + d * .32f) });
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = HitTest(e.Location); if (h != hot) { hot = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hot != -1) { hot = -1; Invalidate(); } }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            Focus(); int h = HitTest(e.Location); if (h >= 0) SelectedIndex = h;
        }
        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode) { case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: return true; }
            return base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up) { SelectedIndex = selected - 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Down) { SelectedIndex = selected + 1; e.Handled = true; }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    }
}