using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BikiniFriends
{
    /// <summary>포커스를 가져가지 않는 납작한 단추(메모 위 도구 단추가 본문의 선택을 풀지 않게).</summary>
    internal sealed class ChromeButton : Button
    {
        internal ChromeButton(string text, int width)
        {
            SetStyle(ControlStyles.Selectable, false); TabStop = false; Text = text; Width = width; Height = 24;
            FlatStyle = FlatStyle.Flat; UseVisualStyleBackColor = false; Margin = new Padding(0); Padding = new Padding(0); Cursor = Cursors.Hand;
            FlatAppearance.BorderSize = 0; Font = new Font("Segoe UI Symbol", 9.5f); TextAlign = ContentAlignment.MiddleCenter;
        }
    }

    /// <summary>
    /// 바탕화면의 메모 한 장(deskNote 의 NoteWindow). 쉬고 있을 때는 글과 종이뿐이고, 포인터를 올리거나 글을 쓰는 동안에만
    /// 위쪽에 + · ⋯ · ✕ 가, 아래쪽에 서식 막대가 나타난다. 제목 칸은 없다: 본문 첫 줄이 곧 제목.
    /// 창은 저장소를 직접 만지지 않고 NoteManager 에게 "이렇게 됐다" 고 알리기만 한다.
    /// </summary>
    internal sealed class NoteWindow : Form
    {
        private const int WM_NCHITTEST = 0x84, WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        private const int Edge = 6, Corner = 14;
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);

        private readonly NoteManager mgr;
        private readonly NoteEditor editor;
        private readonly NoteBox box;
        private readonly Panel strip, formatBar, editorHost;
        private readonly FlowLayoutPanel images;
        private readonly Label titleLabel, placeholder;
        private readonly ChromeButton newBtn, moreBtn, closeBtn;
        private readonly List<ChromeButton> formatButtons = new List<ChromeButton>();
        private readonly List<Button> removeButtons = new List<Button>();
        private readonly System.Windows.Forms.Timer chromeTimer;
        private NoteGeometry geometry;
        private string colorKey; private double opacityValue; private bool pinned, chromeShown, menuOpen, ready;

        internal Guid NoteId { get; private set; }
        internal string CurrentTitle { get { return NoteContent.DeriveTitle(editor.Text); } }
        internal string CurrentContent { get { return editor.Markdown; } }
        internal NoteGeometry Geometry { get { return geometry; } }
        internal string ColorKey { get { return colorKey; } }
        internal bool Pinned { get { return pinned; } }
        internal NoteBox Box { get { return box; } }
        internal NoteEditor Editor { get { return editor; } }
        /// <summary>글자를 짓는 중이면 저장을 미룬다: 서식을 읽으려면 선택을 옮겨야 하고, 그러면 조합이 끊긴다.</summary>
        internal bool IsComposing { get { return box.Composing; } }

        protected override bool ShowWithoutActivation { get { return false; } }

        internal NoteWindow(NoteManager mgr, Note note)
        {
            this.mgr = mgr; NoteId = note.Id; geometry = note.Geometry; colorKey = NoteColors.Normalize(note.ColorKey); opacityValue = note.Opacity; pinned = note.AlwaysOnTop;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; KeyPreview = false;
            MinimumSize = new Size(NoteGeometry.MinW, NoteGeometry.MinH); Padding = new Padding(Edge, 3, Edge, Edge); AllowDrop = true;
            Font = new Font("Malgun Gothic", 9f);
            try { var icon = mgr.AppIcon; if (icon != null) Icon = icon; } catch { }

            box = new NoteBox
            {
                BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, Multiline = true, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical, DetectUrls = false,
                AcceptsTab = false, HideSelection = false, ShortcutsEnabled = true, EnableAutoDragDrop = false, AllowDrop = true, Font = new Font("Malgun Gothic", 10.5f), RichTextShortcutsEnabled = false, MaxLength = 1000000,
            };
            editor = new NoteEditor(box);
            editorHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 2, 2, 0) }; editorHost.Controls.Add(box);
            placeholder = new Label { Text = "첫 줄이 제목이 됩니다", AutoSize = true, Left = 10, Top = 4, Cursor = Cursors.IBeam, Font = new Font("Malgun Gothic", 10.5f) };
            editorHost.Controls.Add(placeholder); placeholder.BringToFront();
            placeholder.Click += delegate { box.Focus(); };

            images = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Visible = false, Padding = new Padding(6, 0, 2, 4) };

            strip = new Panel { Dock = DockStyle.Top, Height = 26 };
            newBtn = new ChromeButton("＋", 28) { Dock = DockStyle.Left }; moreBtn = new ChromeButton("⋯", 28) { Dock = DockStyle.Right }; closeBtn = new ChromeButton("✕", 28) { Dock = DockStyle.Right };
            titleLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true, Font = new Font("Malgun Gothic", 8.5f), UseMnemonic = false };
            strip.Controls.Add(titleLabel); strip.Controls.Add(newBtn); strip.Controls.Add(moreBtn); strip.Controls.Add(closeBtn);
            foreach (Control c in new Control[] { strip, titleLabel }) c.MouseDown += DragStart;

            formatBar = new Panel { Dock = DockStyle.Bottom, Height = 28 };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 1, 0, 0) };
            formatBar.Controls.Add(bar);
            Action<string, int, FontStyle?, Action, string> addFormat = (text, w, style, click, tip) =>
            {
                var b = new ChromeButton(text, w) { Height = 26 }; if (style.HasValue) b.Font = new Font("Malgun Gothic", 10f, style.Value);
                b.Click += delegate { box.Focus(); click(); }; formatButtons.Add(b); bar.Controls.Add(b); tips.SetToolTip(b, tip);
            };
            addFormat("B", 30, FontStyle.Bold, () => Inline(InlineStyle.Bold), "굵게 (Ctrl+B)");
            addFormat("I", 30, FontStyle.Italic, () => Inline(InlineStyle.Italic), "기울임 (Ctrl+I)");
            addFormat("U", 30, FontStyle.Underline, () => Inline(InlineStyle.Underline), "밑줄 (Ctrl+U)");
            addFormat("S", 30, FontStyle.Strikeout, () => Inline(InlineStyle.Strikethrough), "취소선 (Ctrl+Shift+X)");
            addFormat("•", 30, null, () => LineCommand(LineStyle.Bullet), "글머리 목록 (Ctrl+Shift+L)");
            addFormat("☑", 30, null, () => LineCommand(LineStyle.Checklist), "체크리스트 (Ctrl+Shift+C)");
            addFormat("▣", 30, null, () => mgr.PickImage(this), "이미지 넣기");
            tips.SetToolTip(newBtn, "새 메모"); tips.SetToolTip(moreBtn, "메모 설정"); tips.SetToolTip(closeBtn, "닫기 (메모는 라이브러리에 그대로 있어요)");

            Controls.Add(editorHost); Controls.Add(formatBar); Controls.Add(images); Controls.Add(strip);

            newBtn.Click += delegate { mgr.CreateNote(geometry.Preset == NoteSizePreset.Custom ? NoteSizePreset.Medium : geometry.Preset, colorKey); };
            closeBtn.Click += delegate { Close(); };
            moreBtn.Click += delegate { ShowMoreMenu(); };

            box.CommandKey = OnCommandKey; box.PasteHandler = OnPaste;
            box.CompositionEnded = delegate { if (!editor.Busy && ready) { editor.Invalidate(); TextEdited(); } };
            box.TextChanged += delegate { if (editor.Busy) return; editor.Invalidate(); TextEdited(); };
            box.GotFocus += delegate { UpdateChrome(); }; box.LostFocus += delegate { if (!editor.Busy && ready) { editor.Invalidate(); TextEdited(); } UpdateChrome(); };
            box.DragEnter += OnDragEnter; box.DragDrop += OnDragDrop; DragEnter += OnDragEnter; DragDrop += OnDragDrop;
            LocationChanged += delegate { GeometryMoved(); }; SizeChanged += delegate { ApplyRegion(); GeometryMoved(); };
            Activated += delegate { mgr.NoteActivated(this); };
            FormClosed += delegate { chromeTimer.Stop(); chromeTimer.Dispose(); tips.Dispose(); mgr.WindowClosed(this); };

            editor.Load(note.Content, NoteColors.Ink);
            ApplyPaper(); ApplyGeometry(note.Geometry); ShowTitle(); TopMost = pinned; Opacity = Math.Max(Note.MinOpacity, opacityValue);
            chromeTimer = new System.Windows.Forms.Timer { Interval = 100 }; chromeTimer.Tick += delegate { UpdateChrome(); }; chromeTimer.Start();
            UpdateChrome(); ready = true;
        }

        private readonly ToolTip tips = new ToolTip();

        // ───────── 자리 · 모양 ─────────

        internal void ApplyGeometry(NoteGeometry g)
        {
            g = NotePlacement.ClampToVisibleArea(g, NotePlacement.CurrentMonitors()); geometry = g;
            bool was = ready; ready = false;
            try { Bounds = new Rectangle(g.X, g.Y, g.Width, g.Height); } finally { ready = was; }
        }

        internal void SetSizePreset(NoteSizePreset preset)
        {
            var s = NoteGeometry.SizeOf(preset); var g = geometry; g.Width = s.Width; g.Height = s.Height; g.Preset = preset;
            ApplyGeometry(g); mgr.GeometryChanged(this, geometry);
        }

        private void GeometryMoved()
        {
            if (!ready || WindowState != FormWindowState.Normal) return;
            string monitor = Screen.FromControl(this).DeviceName;
            geometry = new NoteGeometry { X = Left, Y = Top, MonitorKey = string.IsNullOrEmpty(monitor) ? geometry.MonitorKey : monitor, Width = Width, Height = Height, Preset = geometry.Preset }.WithSize(Width, Height);
            mgr.GeometryChanged(this, geometry); LayoutImages();
        }

        private void ApplyRegion()
        {
            if (Width < 10 || Height < 10) return;
            IntPtr h = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 16, 16);
            try { var old = Region; Region = Region.FromHrgn(h); if (old != null) old.Dispose(); } finally { DeleteObject(h); }
        }

        private void ApplyPaper()
        {
            Color bg = NoteColors.Background(colorKey), ink = NoteColors.Ink;
            Color quiet = Mix(ink, bg, 0.62), faint = Mix(ink, bg, 0.5);
            BackColor = bg; editorHost.BackColor = bg; strip.BackColor = bg; formatBar.BackColor = bg; images.BackColor = bg;
            foreach (Control c in formatBar.Controls) { c.BackColor = bg; foreach (Control d in c.Controls) d.BackColor = bg; }
            box.BackColor = bg; editor.SetInk(ink); titleLabel.BackColor = bg; titleLabel.ForeColor = faint; placeholder.BackColor = bg; placeholder.ForeColor = Mix(ink, bg, 0.45);
            foreach (var b in new[] { newBtn, moreBtn, closeBtn }.Concat(formatButtons)) StyleChrome(b, bg, quiet, ink);
            foreach (var r in removeButtons) { r.BackColor = bg; r.ForeColor = ink; }
            Invalidate();
        }

        private static void StyleChrome(ChromeButton b, Color bg, Color quiet, Color ink)
        {
            b.BackColor = bg; b.ForeColor = quiet; b.FlatAppearance.MouseOverBackColor = Mix(ink, bg, 0.12); b.FlatAppearance.MouseDownBackColor = Mix(ink, bg, 0.2);
        }

        private static Color Mix(Color a, Color b, double t)
        { return Color.FromArgb((int)(a.R * (1 - t) + b.R * t), (int)(a.G * (1 - t) + b.G * t), (int)(a.B * (1 - t) + b.B * t)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), 8)) using (var pen = new Pen(NoteColors.Border(colorKey), 1.5f)) e.Graphics.DrawPath(pen, path);
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath(); int d = radius * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        // ───────── 이동 · 크기 조절(테두리 없는 창) ─────────

        private void DragStart(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Native.ReleaseCapture(); Native.SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == 1 /* HTCLIENT */)
                {
                    var p = PointToClient(new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF)));
                    bool l = p.X < Edge, r = p.X >= Width - Edge, t = p.Y < Edge, b = p.Y >= Height - Edge;
                    bool cornerBR = p.X >= Width - Corner && p.Y >= Height - Corner;
                    if (cornerBR) m.Result = new IntPtr(HTBOTTOMRIGHT);
                    else if (t && l) m.Result = new IntPtr(HTTOPLEFT); else if (t && r) m.Result = new IntPtr(HTTOPRIGHT);
                    else if (b && l) m.Result = new IntPtr(HTBOTTOMLEFT); else if (b && r) m.Result = new IntPtr(HTBOTTOMRIGHT);
                    else if (l) m.Result = new IntPtr(HTLEFT); else if (r) m.Result = new IntPtr(HTRIGHT);
                    else if (t) m.Result = new IntPtr(HTTOP); else if (b) m.Result = new IntPtr(HTBOTTOM);
                }
                return;
            }
            base.WndProc(ref m);
        }

        // ───────── 쓰기 · 서식 ─────────

        private void TextEdited()
        {
            ShowTitle(); placeholder.Visible = box.TextLength == 0;
            mgr.TextChanged(this);
        }

        private void ShowTitle()
        {
            string title = CurrentTitle; titleLabel.Text = title; Text = title.Length > 0 ? title : "비키니 친구들 메모";
            placeholder.Visible = box.TextLength == 0;
        }

        /// <summary>글자 서식(굵게 등)만 바뀌어도 글은 안 바뀐 채로 저장해야 한다.</summary>
        private void Inline(InlineStyle style) { editor.ToggleInline(style); editor.Invalidate(); TextEdited(); }

        private void LineCommand(LineStyle style) { editor.Apply(MarkdownEditing.ApplyLineStyle(editor.State, style)); editor.Invalidate(); TextEdited(); }

        private void Command(Func<NoteTextState, NoteTextState> f) { editor.Apply(f(editor.State)); editor.Invalidate(); TextEdited(); }

        /// <summary>명령 키는 상자가 먼저 먹기 전에 여기서 가로챈다(RichTextBox 는 자기 Ctrl+B/I/U 를 따로 갖고 있어 두 번 처리될 수 있다).</summary>
        private bool OnCommandKey(Keys key)
        {
            Keys code = key & Keys.KeyCode, mods = key & Keys.Modifiers;
            bool ctrl = (mods & Keys.Control) != 0, shift = (mods & Keys.Shift) != 0, alt = (mods & Keys.Alt) != 0;
            if (alt) return false;
            if (code == Keys.Enter && !ctrl && !shift)
            {
                var next = MarkdownEditing.ContinueListOnEnter(editor.State); if (!next.HasValue) return false;
                editor.Apply(next.Value); editor.Invalidate(); TextEdited(); return true;
            }
            if (!ctrl) return false;
            if (code == Keys.Enter) { var t = MarkdownEditing.ToggleChecklistItemAtCaret(editor.State); if (t.HasValue) { editor.Apply(t.Value); editor.Invalidate(); TextEdited(); } return true; }
            if (!shift)
            {
                switch (code)
                {
                    case Keys.B: Inline(InlineStyle.Bold); return true;
                    case Keys.I: Inline(InlineStyle.Italic); return true;
                    case Keys.U: Inline(InlineStyle.Underline); return true;
                    case Keys.K: Command(s => MarkdownEditing.InsertLink(s)); return true;
                    case Keys.D1: LineCommand(LineStyle.Heading1); return true;
                    case Keys.D2: LineCommand(LineStyle.Heading2); return true;
                    case Keys.D3: LineCommand(LineStyle.Heading3); return true;
                    case Keys.E: case Keys.L: case Keys.R: case Keys.J: return true;   // 상자 자체의 줄 맞춤 단축키는 쓰지 않는다
                }
                return false;
            }
            switch (code)
            {
                case Keys.X: Inline(InlineStyle.Strikethrough); return true;
                case Keys.K: Command(MarkdownEditing.ToggleInlineCode); return true;
                case Keys.L: LineCommand(LineStyle.Bullet); return true;
                case Keys.C: LineCommand(LineStyle.Checklist); return true;
            }
            return false;
        }

        /// <summary>붙여넣기: 그림이면 첨부하고, 글이면 서식을 떼고 글자만 받는다(웹에서 복사한 글이 남의 글꼴을 달고 들어오지 않게).</summary>
        private bool OnPaste()
        {
            try
            {
                if (Clipboard.ContainsImage() && !Clipboard.ContainsText()) { mgr.PasteImage(this, Clipboard.GetImage()); return true; }
                if (Clipboard.ContainsFileDropList()) { var files = Clipboard.GetFileDropList(); var paths = new List<string>(); foreach (string f in files) paths.Add(f); if (paths.Count > 0) { mgr.Attach(this, paths); return true; } }
                if (Clipboard.ContainsText()) { InsertAtCaret(Clipboard.GetText()); return true; }
            }
            catch (Exception ex) { FeatureLog.Write(ex); }
            return true;
        }

        /// <summary>캐럿 자리에 글을 넣는다(실행 취소 가능). 바깥에서 온 글은 CRLF 라서 먼저 \n 으로 맞춘다.</summary>
        internal void InsertAtCaret(string text)
        {
            editor.Apply(editor.State.ReplacingSelection(NoteContent.NormalizeLineEndings(text))); editor.Invalidate(); TextEdited();
        }

        /// <summary>기록 복원처럼 이미 저장된 글로 본문을 통째로 바꾼다. 저장 이벤트는 내지 않는다(복원본이 또 기록되지 않게).</summary>
        internal void ReplaceContent(string content)
        {
            editor.Load(content, NoteColors.Ink); box.Select(box.TextLength, 0); ShowTitle();
        }

        internal void FocusEditor() { if (!Visible) Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); box.Focus(); }

        internal void ToggleAlwaysOnTop() { pinned = !pinned; TopMost = pinned; mgr.AppearanceChanged(this); }

        // ───────── 첨부(끌어다 놓기) ─────────

        private void OnDragEnter(object sender, DragEventArgs e) { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; }
        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null && paths.Length > 0) mgr.Attach(this, paths);
        }

        // ───────── 이미지 띠 ─────────

        internal void ShowImages(IEnumerable<NoteAttachment> list)
        {
            images.SuspendLayout(); foreach (Control c in images.Controls.OfType<Control>().ToList()) { images.Controls.Remove(c); c.Dispose(); }
            removeButtons.Clear();
            foreach (var a in list) images.Controls.Add(BuildCard(a));
            images.ResumeLayout(); LayoutImages();
        }

        internal void AddImage(NoteAttachment a) { images.Controls.Add(BuildCard(a)); LayoutImages(); }

        internal void RemoveImage(Guid attachmentId)
        {
            var card = images.Controls.OfType<Control>().FirstOrDefault(c => c.Tag is Guid && (Guid)c.Tag == attachmentId);
            if (card != null) { images.Controls.Remove(card); removeButtons.RemoveAll(b => b.Tag is Guid && (Guid)b.Tag == attachmentId); card.Dispose(); }
            LayoutImages();
        }

        private Control BuildCard(NoteAttachment a)
        {
            var card = new Panel { Tag = a.Id, Margin = new Padding(0, 0, 0, 6), BackColor = BackColor };
            Image img = null;
            try { using (var fs = File.OpenRead(a.Path)) using (var src = Image.FromStream(fs)) img = new Bitmap(src); }
            catch { img = null; }
            if (img == null)
            {
                var missing = new Label { Text = "⚠ " + a.DisplayName + " — 이미지를 찾을 수 없어요", AutoSize = false, Dock = DockStyle.Fill, ForeColor = Mix(NoteColors.Ink, BackColor, 0.3) };
                card.Controls.Add(missing); card.Height = 36;
            }
            else
            {
                var pic = new PictureBox { Image = img, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Cursor = Cursors.Hand };
                pic.Click += delegate { try { Process.Start(new ProcessStartInfo(a.Path) { UseShellExecute = true }); } catch (Exception ex) { FeatureLog.Write(ex); } };
                tips.SetToolTip(pic, a.DisplayName); card.Controls.Add(pic); card.Tag = a.Id; card.MinimumSize = new Size(10, 10);
                card.Height = Math.Min(220, Math.Max(40, img.Height)); card.Width = Math.Max(60, Math.Min(img.Width, 2000));
                card.Controls[0].Tag = img.Size;
            }
            var rm = new Button { Text = "✕", Width = 24, Height = 24, FlatStyle = FlatStyle.Flat, Visible = false, Tag = a.Id, Anchor = AnchorStyles.Top | AnchorStyles.Right, Font = new Font("Segoe UI Symbol", 8.5f), Cursor = Cursors.Hand, BackColor = BackColor, ForeColor = NoteColors.Ink };
            rm.SetBounds(card.Width - 28, 4, 24, 24); rm.FlatAppearance.BorderSize = 0; rm.UseVisualStyleBackColor = false;
            rm.Click += delegate { mgr.RemoveImage(this, a); };
            tips.SetToolTip(rm, "이 이미지 떼기"); card.Controls.Add(rm); rm.BringToFront(); removeButtons.Add(rm);
            return card;
        }

        /// <summary>이미지 띠는 창 높이의 절반을 넘지 않게(스크린샷 하나가 글을 밀어내지 못하게). 그림은 원래 해상도보다 키우지 않는다.</summary>
        private void LayoutImages()
        {
            if (images == null) return;
            int cards = images.Controls.Count; images.Visible = cards > 0; if (cards == 0) return;
            int width = Math.Max(40, images.ClientSize.Width - images.Padding.Horizontal - 2), total = 0;
            foreach (Control c in images.Controls)
            {
                var pic = c.Controls.OfType<PictureBox>().FirstOrDefault();
                if (pic != null && pic.Tag is Size)
                {
                    var s = (Size)pic.Tag; double scale = Math.Min(1.0, Math.Min(width / (double)s.Width, 220.0 / s.Height));
                    c.Size = new Size(Math.Max(40, (int)(s.Width * scale)), Math.Max(30, (int)(s.Height * scale)));
                }
                total += c.Height + 6;
            }
            images.Height = Math.Max(48, Math.Min(total + images.Padding.Vertical + 4, Math.Max(96, ClientSize.Height / 2)));
        }

        // ───────── 크롬(hover 로 나타나는 단추) ─────────

        private void UpdateChrome()
        {
            bool inside = Bounds.Contains(Cursor.Position) && Visible;
            bool show = inside || menuOpen || box.Focused || ContainsFocus;
            if (show == chromeShown && ready) return;
            chromeShown = show;
            foreach (Control c in new Control[] { newBtn, moreBtn, closeBtn, titleLabel }) c.Visible = show;
            foreach (var b in formatButtons) b.Visible = show;
            foreach (var r in removeButtons) r.Visible = show;
        }

        // ───────── ⋯ 메뉴 ─────────

        private void ShowMoreMenu()
        {
            var menu = new ContextMenuStrip { ShowImageMargin = true };
            var color = new ToolStripMenuItem("색상");
            foreach (string key in NoteColors.All)
            {
                string k = key; var item = new ToolStripMenuItem(NoteColors.Label(key)) { Checked = key == colorKey, Image = Swatch(NoteColors.Background(key), NoteColors.Border(key)) };
                item.Click += delegate { colorKey = k; ApplyPaper(); mgr.AppearanceChanged(this); };
                color.DropDownItems.Add(item);
            }
            menu.Items.Add(color);

            var opacity = new ToolStripMenuItem("불투명도");
            foreach (int pct in new[] { 100, 85, 70, 55, 40 })
            {
                int p = pct; var item = new ToolStripMenuItem(pct + "%") { Checked = Math.Abs(opacityValue * 100 - pct) < 8 };
                item.Click += delegate { opacityValue = p / 100.0; Opacity = Math.Max(Note.MinOpacity, opacityValue); mgr.AppearanceChanged(this); };
                opacity.DropDownItems.Add(item);
            }
            menu.Items.Add(opacity);

            var size = new ToolStripMenuItem("크기");
            foreach (var preset in new[] { NoteSizePreset.Small, NoteSizePreset.Medium, NoteSizePreset.Large })
            {
                var p = preset; var s = NoteGeometry.SizeOf(preset);
                var item = new ToolStripMenuItem((preset == NoteSizePreset.Small ? "작게" : preset == NoteSizePreset.Large ? "크게" : "보통") + "  " + s.Width + "×" + s.Height) { Checked = geometry.Preset == preset };
                item.Click += delegate { SetSizePreset(p); }; size.DropDownItems.Add(item);
            }
            menu.Items.Add(size);

            var remind = new ToolStripMenuItem("알림");
            remind.DropDownItems.Add("10분 뒤", null, delegate { mgr.AddReminder(NoteId, TimeSpan.FromMinutes(10)); });
            remind.DropDownItems.Add("1시간 뒤", null, delegate { mgr.AddReminder(NoteId, TimeSpan.FromHours(1)); });
            remind.DropDownItems.Add("내일 아침 9시", null, delegate { mgr.AddReminder(NoteId, TimeSpan.Zero); });
            remind.DropDownItems.Add(new ToolStripSeparator());
            remind.DropDownItems.Add("직접 정하기…", null, delegate { mgr.ShowReminderComposer(NoteId, this); });
            var next = NoteStore.Current.NextReminder(NoteId, DateTime.Now);
            if (next.HasValue) { remind.DropDownItems.Add(new ToolStripSeparator()); remind.DropDownItems.Add(new ToolStripMenuItem("다음 알림: " + ReminderLabel(next.Value)) { Enabled = false }); }
            menu.Items.Add(remind);

            menu.Items.Add(new ToolStripSeparator());
            var pin = new ToolStripMenuItem("항상 위에 두기") { Checked = pinned, CheckOnClick = false }; pin.Click += delegate { ToggleAlwaysOnTop(); }; menu.Items.Add(pin);
            menu.Items.Add("메모 라이브러리", null, delegate { mgr.ShowLibrary(); });
            menu.Items.Add("기록", null, delegate { mgr.ShowHistory(NoteId, this); });
            var delete = new ToolStripMenuItem("삭제") { ForeColor = Color.FromArgb(0xC0, 0x39, 0x2B) };
            delete.Click += delegate { ConfirmDelete(); };
            menu.Items.Add(delete);

            menu.Opened += delegate { menuOpen = true; };
            menu.Closed += delegate { menuOpen = false; UpdateChrome(); menu.BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(moreBtn, new Point(0, moreBtn.Height));
        }

        internal static string ReminderLabel(DateTime due)
        {
            string time = due.ToString("HH:mm"); DateTime now = DateTime.Now;
            if (due.Date == now.Date) return "오늘 " + time; if (due.Date == now.Date.AddDays(1)) return "내일 " + time;
            return due.ToString(due.Year == now.Year ? "M/d" : "yyyy/M/d") + " " + time;
        }

        private static Image Swatch(Color fill, Color border)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); using (var b = new SolidBrush(fill)) g.FillRectangle(b, 1, 1, 13, 13); using (var p = new Pen(border)) g.DrawRectangle(p, 1, 1, 13, 13); }
            return bmp;
        }

        /// <summary>삭제는 되돌릴 수 없다(휴지통이 없다). 그래서 한 번 더 묻는다.</summary>
        private void ConfirmDelete()
        {
            if (MessageBox.Show(this, "이 메모를 완전히 삭제할까요?\n기록과 첨부도 함께 사라지고 되돌릴 수 없어요.", "메모 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes) mgr.DeleteNotes(new[] { NoteId });
        }

        protected override void Dispose(bool disposing) { if (disposing && tips != null) { try { tips.Dispose(); } catch { } } base.Dispose(disposing); }
    }
}
