using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BikiniFriends
{
    /// <summary>
    /// 친구에게 말을 거는 입력칸. 채팅창이 아니라 친구 발치에 놓이는 얇은 알약 하나다.
    /// 그림(안티앨리어싱된 알약·그림자·보낸 말의 잔상)은 투명한 레이어드 창이 그리고,
    /// 글자 입력(한글 IME 포함)은 그 위에 얹은 테두리 없는 창의 TextBox 가 맡는다.
    /// </summary>
    internal sealed class ChatInput : IDisposable
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(IntPtr h);
        [DllImport("imm32.dll")] private static extern bool ImmNotifyIME(IntPtr himc, int action, int index, int value);
        [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(IntPtr h, IntPtr himc);

        internal static readonly Color Paper = Color.FromArgb(255, 252, 247), Ink = Color.FromArgb(30, 30, 30), Blue = Color.FromArgb(54, 96, 190);

        private readonly float s = CharacterArt.Scale;
        private readonly Frame frame;
        private readonly Editor edit;
        private readonly int pillW, pillH, margin, echoH, chipRow;
        private string echoText; private double echoStart = -100; private bool echoShown;
        private string[] chips; private double chipsStart; private int chipHover = -1;
        private RectangleF[] chipRects = new RectangleF[0];
        private readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 40 };
        private int frameX = int.MinValue, frameY = int.MinValue;
        private bool disposed;

        internal event Action<string> Submitted;
        internal event Action Cancelled;
        internal event Action<int> Typing;
        internal event Action ActivityChanged;

        internal bool Focused { get { return edit.Focused || edit.ContainsFocus; } }
        internal string Text { get { return edit.Box.Text; } set { edit.Box.Text = value; } }
        internal Rectangle Bounds { get { return new Rectangle(frameX, frameY, frame.Width, frame.Height); } }
        internal string[] Chips { get { return chips; } }
        internal void SetCue(string text) { edit.SetCue(text); }

        /// <summary>화면 좌표가 알약이나 답장 칩 위인가(투명한 여백은 아님). 밖을 눌러 대화를 끝낼지 가르는 데 쓴다.</summary>
        internal bool Contains(Point screen)
        {
            if (disposed) return false;
            var pill = new Rectangle(frameX + margin, frameY + margin + chipRow + echoH, pillW, pillH);
            pill.Inflate((int)(3 * s), (int)(3 * s));
            if (pill.Contains(screen)) return true;
            foreach (RectangleF r in chipRects)
                if (!r.IsEmpty && new RectangleF(frameX + r.X, frameY + r.Y, r.Width, r.Height).Contains(screen)) return true;
            return false;
        }

        internal ChatInput()
        {
            pillW = (int)Math.Round(272 * s); pillH = (int)Math.Round(38 * s); margin = (int)Math.Round(8 * s); echoH = (int)Math.Round(34 * s); chipRow = (int)Math.Round(34 * s);
            frame = new Frame(); frame.Size = new Size(pillW + margin * 2, chipRow + echoH + pillH + margin * 2);
            edit = new Editor(s);
            edit.Box.TextChanged += delegate { if (edit.Box.TextLength > 0) chips = null; if (Typing != null) Typing(edit.Box.TextLength); Redraw(); };
            edit.Chip += i => ChooseChip(i);
            frame.MouseMove += delegate (object o, MouseEventArgs e)
            {
                int h = HitChip(e.Location);
                if (h != chipHover) { chipHover = h; frame.Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Redraw(); }
            };
            frame.MouseLeave += delegate { if (chipHover != -1) { chipHover = -1; frame.Cursor = Cursors.Default; Redraw(); } };
            frame.MouseDown += delegate (object o, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                int h = HitChip(e.Location);
                if (h >= 0) ChooseChip(h + 1); else Focus();
            };
            edit.Submit += delegate { string t = edit.Box.Text.Trim(); if (t.Length > 0 && Submitted != null) Submitted(t); };
            edit.Cancel += delegate { if (Cancelled != null) Cancelled(); };
            edit.GotLostFocus += delegate { Redraw(); if (ActivityChanged != null) ActivityChanged(); };
            anim.Tick += delegate { if (echoText != null || echoShown || (chips != null && Environment.TickCount / 1000.0 - chipsStart < .6)) Redraw(); };
        }

        // ───────── 답장 추천 칩 ─────────
        /// <summary>알약 위에 눌러 보낼 수 있는 짧은 말 몇 개를 띄운다. 글자를 치기 시작하면 사라진다.</summary>
        internal void ShowChips(string[] items)
        {
            if (items == null || items.Length == 0 || edit.Box.TextLength > 0) return;
            chips = items; chipsStart = Environment.TickCount / 1000.0; chipHover = -1;
            Redraw();
        }

        internal void ClearChips() { if (chips == null) return; chips = null; chipHover = -1; chipRects = new RectangleF[0]; frame.Cursor = Cursors.Default; Redraw(); }

        /// <summary>1부터 센 칩 번호(마우스 클릭·Ctrl+1~3 공통). 보냈으면 true.</summary>
        internal bool ChooseChip(int number)
        {
            if (chips == null || number < 1 || number > chips.Length || chipRects.Length < number || chipRects[number - 1].IsEmpty) return false;
            string t = chips[number - 1]; ClearChips();
            if (Submitted != null) Submitted(t);
            Focus();
            return true;
        }

        private int HitChip(Point p)
        {
            for (int i = 0; i < chipRects.Length; i++) if (!chipRects[i].IsEmpty && chipRects[i].Contains(p)) return i;
            return -1;
        }

        /// <summary>화면에 띄우고 글자를 칠 수 있게 포커스를 가져온다.</summary>
        internal void Show(int x, int y)
        {
            Place(x, y);
            frame.Show();
            edit.Show(frame);
            Redraw();
            Focus();
            anim.Start();
        }

        internal void Focus()
        {
            try
            {
                edit.Activate();
                Native.SetForegroundWindow(edit.Handle);
                edit.Box.Focus();
            }
            catch { }
        }

        /// <summary>왼쪽 위가 아니라 "알약의 왼쪽 아래" 기준 좌표.</summary>
        internal Point Anchor(int pillLeft, int pillBottom) { return new Point(pillLeft - margin, pillBottom + margin - frame.Height); }
        internal int PillWidth { get { return pillW; } }
        internal int PillHeight { get { return pillH; } }

        internal void Place(int x, int y)
        {
            if (x == frameX && y == frameY) return;
            frameX = x; frameY = y;
            if (frame.IsHandleCreated && frame.Visible) Native.SetWindowPos(frame.Handle, IntPtr.Zero, x, y, 0, 0, 533);
            else { frame.Location = new Point(x, y); }
            int editH = edit.Box.Height;
            int ex = x + margin + (int)(36 * s), ey = y + margin + chipRow + echoH + (pillH - editH) / 2;
            edit.Location = new Point(ex, ey);
            edit.Size = new Size(pillW - (int)(36 * s) - (int)(34 * s), editH);
        }

        /// <summary>보낸 말을 알약 위에 잠깐 띄웠다가 흐려지게 한다.</summary>
        internal void Echo(string text)
        {
            echoText = text; echoStart = Environment.TickCount / 1000.0; echoShown = true;
            edit.Box.Clear();
            Redraw();
        }

        private void Redraw()
        {
            if (disposed || !frame.IsHandleCreated) return;
            double age = Environment.TickCount / 1000.0 - echoStart;
            float echoAlpha = echoText == null ? 0 : age < 3.2 ? 1f : (float)Math.Max(0, 1 - (age - 3.2) / .9);
            if (echoAlpha <= 0) { echoText = null; echoShown = false; }
            using (Bitmap bmp = Render(echoAlpha)) frame.Present(bmp, frameX, frameY);
        }

        private Bitmap Render(float echoAlpha)
        {
            var bmp = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var pill = new RectangleF(margin, margin + chipRow + echoH, pillW, pillH);
                // 그림자
                using (var sh = RoundPath(new RectangleF(pill.Left + 1, pill.Top + 3 * s, pill.Width, pill.Height), pill.Height / 2))
                using (var br = new SolidBrush(Color.FromArgb(46, 20, 30, 60))) g.FillPath(br, sh);
                bool focus = edit.ContainsFocus || edit.Focused;
                using (var path = RoundPath(pill, pill.Height / 2))
                {
                    using (var fill = new SolidBrush(Paper)) g.FillPath(fill, path);
                    using (var pen = new Pen(focus ? Blue : Ink, 2f * s) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
                }
                // 왼쪽: AI 반짝이
                float cx = pill.Left + 19 * s, cy = pill.Top + pill.Height / 2;
                var orb = new RectangleF(cx - 10 * s, cy - 10 * s, 20 * s, 20 * s);
                using (var fill = new LinearGradientBrush(orb, Color.FromArgb(70, 140, 255), Color.FromArgb(150, 90, 255), 45f)) g.FillEllipse(fill, orb);
                DrawStar(g, cx, cy, 6.5f * s, Color.White);
                DrawStar(g, cx + 6 * s, cy - 6 * s, 2.6f * s, Color.FromArgb(255, 226, 70));
                // 오른쪽: 엔터 안내(글자가 있을 때 진하게)
                bool has = edit.Box.TextLength > 0;
                using (var f = new Font("Malgun Gothic", 12f * s, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var br = new SolidBrush(has ? Blue : Color.FromArgb(190, 190, 200)))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("↵", f, br, new RectangleF(pill.Right - 34 * s, pill.Top, 28 * s, pill.Height), fmt);
                // 보낸 말의 잔상
                if (echoText != null && echoAlpha > 0)
                {
                    using (var f = new Font("Malgun Gothic", 11.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var fmt = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip })
                    {
                        float maxW = pillW - 40 * s;
                        SizeF sz = g.MeasureString(echoText, f, PointF.Empty, StringFormat.GenericTypographic);
                        float w = Math.Min(maxW, sz.Width) + 22 * s, h = 24 * s;
                        float lift = chips != null ? 32 * s : 0;
                        var r = new RectangleF(pill.Right - w, pill.Top - h - 5 * s - lift - (1 - echoAlpha) * 6 * s, w, h);
                        int a = (int)(255 * echoAlpha);
                        using (var path = RoundPath(r, h / 2))
                        {
                            using (var fill = new SolidBrush(Color.FromArgb((int)(240 * echoAlpha), 232, 238, 255))) g.FillPath(fill, path);
                            using (var pen = new Pen(Color.FromArgb((int)(200 * echoAlpha), 120, 140, 210), 1.4f * s)) g.DrawPath(pen, path);
                        }
                        using (var br = new SolidBrush(Color.FromArgb(a, 50, 62, 120)))
                            g.DrawString(echoText, f, br, new RectangleF(r.Left + 11 * s, r.Top + (h - sz.Height) / 2, w - 22 * s, h), fmt);
                    }
                }
                DrawChips(g, pill);
            }
            return bmp;
        }

        private void DrawChips(Graphics g, RectangleF pill)
        {
            if (chips == null) { chipRects = new RectangleF[0]; return; }
            var rects = new RectangleF[chips.Length];
            float h = 26 * s, gap = 6 * s, x = pill.Left + 4 * s, y = pill.Top - h - 6 * s, now = (float)(Environment.TickCount / 1000.0 - chipsStart);
            using (var f = new Font("Malgun Gothic", 11.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            {
                var fit = new System.Collections.Generic.List<string>(); float used = x;
                foreach (string c in chips)
                {
                    float cw = g.MeasureString(c, f, PointF.Empty, StringFormat.GenericTypographic).Width + 22 * s;
                    if (used + cw > pill.Right - 2 * s) continue;
                    fit.Add(c); used += cw + gap;
                }
                if (fit.Count != chips.Length) { chips = fit.ToArray(); rects = new RectangleF[chips.Length]; }
                for (int i = 0; i < chips.Length; i++)
                {
                    SizeF sz = g.MeasureString(chips[i], f, PointF.Empty, StringFormat.GenericTypographic);
                    float w = sz.Width + 22 * s;
                    if (x + w > pill.Right - 2 * s) continue;
                    var r = new RectangleF(x, y, w, h); rects[i] = r; x += w + gap;
                    float t = Math.Max(0, Math.Min(1, (now - i * .07f) / .22f)), a = t * t * (3 - 2 * t);
                    if (a <= 0) continue;
                    var shown = new RectangleF(r.X, r.Y + (1 - a) * 7 * s, r.Width, r.Height);
                    bool hot = i == chipHover;
                    using (var path = RoundPath(shown, h / 2))
                    {
                        using (var sh = RoundPath(new RectangleF(shown.X, shown.Y + 2 * s, shown.Width, shown.Height), h / 2))
                        using (var br = new SolidBrush(Color.FromArgb((int)(40 * a), 20, 30, 60))) g.FillPath(br, sh);
                        using (var fill = new SolidBrush(hot ? Color.FromArgb((int)(255 * a), 214, 226, 255) : Color.FromArgb((int)(245 * a), 244, 247, 255))) g.FillPath(fill, path);
                        using (var pen = new Pen(Color.FromArgb((int)(255 * a), hot ? Blue : Color.FromArgb(120, 140, 210)), (hot ? 1.8f : 1.4f) * s)) g.DrawPath(pen, path);
                    }
                    using (var br = new SolidBrush(Color.FromArgb((int)(255 * a), hot ? Color.FromArgb(24, 50, 130) : Color.FromArgb(50, 62, 120))))
                        g.DrawString(chips[i], f, br, shown, fmt);
                }
            }
            chipRects = rects;
        }

        private static void DrawStar(Graphics g, float cx, float cy, float r, Color color)
        {
            var pts = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4 - Math.PI / 2; float rr = i % 2 == 0 ? r : r * .3f;
                pts[i] = new PointF(cx + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr);
            }
            using (var br = new SolidBrush(color)) g.FillPolygon(br, pts);
        }

        private static GraphicsPath RoundPath(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        internal void KeepOnTop()
        {
            if (disposed) return;
            if (frame.IsHandleCreated && frame.Visible) Native.SetWindowPos(frame.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 19);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; anim.Stop(); anim.Dispose();
            try { edit.Close(); edit.Dispose(); } catch { }
            try { frame.Close(); frame.Dispose(); } catch { }
        }

        // ───────── 그림을 그리는 투명 창 ─────────
        private sealed class Frame : Form
        {
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { var p = base.CreateParams; p.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST; return p; }
            }
            internal Frame()
            {
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
                Text = "비키니 친구들 · 말 걸기"; Size = new Size(1, 1); Location = new Point(-32000, -32000);
            }
            protected override void OnPaintBackground(PaintEventArgs e) { }

            internal void Present(Bitmap bitmap, int x, int y)
            {
                if (!IsHandleCreated) return;
                try { LayerPixels.Present(Handle, bitmap, x, y, 255); }
                catch (Exception e) { FeatureLog.Write(e); }
            }
        }

        // ───────── 글자를 받는 창(알약 안쪽) ─────────
        private sealed class Editor : Form
        {
            internal readonly TextBox Box;
            internal event Action Submit, Cancel, GotLostFocus;
            internal event Action<int> Chip;
            private string cue = "비키니 친구들에게 말 걸기…";
            internal void SetCue(string text) { cue = text; if (Box.IsHandleCreated) try { SendMessage(Box.Handle, 0x1501, new IntPtr(1), cue); } catch { } }

            protected override bool ShowWithoutActivation { get { return false; } }
            protected override CreateParams CreateParams
            {
                get { var p = base.CreateParams; p.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST; return p; }
            }

            internal Editor(float s)
            {
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
                BackColor = Paper; Text = "비키니 친구들에게 말 걸기";
                Box = new TextBox
                {
                    BorderStyle = BorderStyle.None, BackColor = Paper, ForeColor = Color.FromArgb(40, 40, 40), MaxLength = 200, Dock = DockStyle.Fill,
                    Font = new Font("Malgun Gothic", 14f * s, FontStyle.Regular, GraphicsUnit.Pixel), ImeMode = ImeMode.Hangul
                };
                Controls.Add(Box);
                Size = new Size(200, Box.PreferredHeight);
                Box.Height = Box.PreferredHeight;
                Box.KeyDown += OnKey;
                Box.GotFocus += delegate { if (GotLostFocus != null) GotLostFocus(); };
                Box.LostFocus += delegate { if (GotLostFocus != null) GotLostFocus(); };
                Box.HandleCreated += delegate { try { SendMessage(Box.Handle, 0x1501, new IntPtr(1), cue); } catch { } };
            }

            private void OnKey(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; e.Handled = true; if (Cancel != null) Cancel(); return; }
                if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D3) { e.SuppressKeyPress = true; e.Handled = true; if (Chip != null) Chip(e.KeyCode - Keys.D0); return; }
                if (e.KeyCode != Keys.Enter || e.Shift) return;
                e.SuppressKeyPress = true; e.Handled = true;
                try { IntPtr himc = ImmGetContext(Box.Handle); if (himc != IntPtr.Zero) { ImmNotifyIME(himc, 0x15, 1, 0); ImmReleaseContext(Box.Handle, himc); } } catch { }
                BeginInvoke(new Action(delegate { if (Submit != null) Submit(); }));
            }
        }
    }
}