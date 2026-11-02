using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace BikiniFriends
{
    /// <summary>한 줄 입력 창(노트북 이름 등).</summary>
    internal sealed class PromptForm : Form
    {
        private readonly TextBox box;
        internal string Value { get { return box.Text.Trim(); } }

        internal PromptForm(string title, string caption, string initial)
        {
            FriendUI.Setup(this, null); Text = title; ClientSize = new Size(360, 120); ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            Controls.Add(new Label { Text = caption, AutoSize = true, Left = 14, Top = 14, ForeColor = FriendUI.Ink });
            box = new TextBox { Left = 14, Top = 38, Width = 330, MaxLength = 60, Text = initial ?? "" }; Controls.Add(box);
            var ok = new Button { Text = "확인", DialogResult = DialogResult.OK, Left = 184, Top = 76, Width = 76, Height = 28 };
            var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Left = 268, Top = 76, Width = 76, Height = 28 };
            Controls.Add(ok); Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel;
            Shown += delegate { box.Focus(); box.SelectAll(); };
        }
    }

    /// <summary>목록 한 줄: 체크 칸 + 색 막대 + 제목 + 미리보기 + (수정 시각 · 체크리스트 · 태그 · 알림). 체크 칸을 누르면 선택, 나머지를 누르면 그 메모가 바탕화면에 뜬다.</summary>
    internal sealed class NoteListBox : ListBox
    {
        internal readonly HashSet<Guid> Checked = new HashSet<Guid>();
        internal event Action<Note> NoteClicked; internal event Action CheckedChanged;

        internal NoteListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 68; IntegralHeight = false; BorderStyle = BorderStyle.FixedSingle; DoubleBuffered = true; BackColor = Color.White;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            e.DrawBackground(); if (e.Index < 0 || e.Index >= Items.Count) return;
            var n = Items[e.Index] as Note; if (n == null) return;
            var g = e.Graphics; var b = e.Bounds; bool hot = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(hot ? Color.FromArgb(232, 240, 252) : BackColor)) g.FillRectangle(back, b);
            ControlPaint.DrawCheckBox(g, new Rectangle(b.Left + 8, b.Top + 8, 15, 15), Checked.Contains(n.Id) ? ButtonState.Checked : ButtonState.Normal);
            using (var bar = new SolidBrush(NoteColors.Swatch(n.ColorKey))) g.FillRectangle(bar, b.Left + 34, b.Top + 8, 8, 44);
            int x = b.Left + 52, w = b.Width - x - 8;
            using (var bold = new Font(Font, FontStyle.Bold)) TextRenderer.DrawText(g, TitleOf(n), bold, new Rectangle(x, b.Top + 5, w, 18), Color.FromArgb(40, 36, 30), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, PreviewOf(n), Font, new Rectangle(x, b.Top + 23, w, 18), Color.FromArgb(105, 100, 92), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            using (var small = new Font(Font.FontFamily, 8f)) TextRenderer.DrawText(g, Meta(n), small, new Rectangle(x, b.Top + 44, w, 16), Color.FromArgb(130, 125, 118), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            using (var line = new Pen(Color.FromArgb(234, 230, 222))) g.DrawLine(line, b.Left, b.Bottom - 1, b.Right, b.Bottom - 1);
        }

        internal static string TitleOf(Note n) { if (n.Title.Length > 0) return n.Title; return "빈 메모"; }
        internal static string PreviewOf(Note n)
        {
            string p = NoteRichText.FromMarkdown(n.Content.Length > 200 ? n.Content.Substring(0, 200) : n.Content).Text;
            p = p.Replace('\n', ' ').Trim(); return p.Length == 0 ? "" : p;
        }

        internal static string Meta(Note n)
        {
            var parts = new List<string> { Relative(n.UpdatedUtc) };
            if (n.ChecklistTotal > 0) parts.Add("☑ " + n.ChecklistDone + "/" + n.ChecklistTotal);
            if (n.Tags.Count > 0) parts.Add(string.Join(" ", n.Tags.Select(t => "#" + t).ToArray()));
            var next = NoteStore.Current == null ? null : NoteStore.Current.NextReminder(n.Id, DateTime.Now);
            if (next.HasValue) parts.Add("🔔 " + NoteWindow.ReminderLabel(next.Value));
            return string.Join("  ·  ", parts.ToArray());
        }

        internal static string Relative(DateTime utc)
        {
            var span = DateTime.UtcNow - utc;
            if (span.TotalMinutes < 1) return "방금"; if (span.TotalHours < 1) return (int)span.TotalMinutes + "분 전";
            if (span.TotalDays < 1) return (int)span.TotalHours + "시간 전"; if (span.TotalDays < 7) return (int)span.TotalDays + "일 전";
            return utc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); int i = IndexFromPoint(e.Location); if (i < 0 || e.Button != MouseButtons.Left) return;
            var n = Items[i] as Note; if (n == null) return;
            if (e.X < 32)
            {
                if (!Checked.Add(n.Id)) Checked.Remove(n.Id);
                Invalidate(GetItemRectangle(i)); var h = CheckedChanged; if (h != null) h();
            }
            else { var h = NoteClicked; if (h != null) h(n); }
        }
    }

    /// <summary>
    /// 메모 라이브러리(deskNote 의 Notes Explorer). 노트북과 태그로 좁혀 보고, 검색하고, 메모를 골라 옮기거나 지운다.
    /// 행을 누르면 그 메모가 바탕화면에 뜬다. 다른 창에서 메모를 고치면 목록이 따라온다(체크해 둔 동안에는 멈춘다).
    /// </summary>
    internal sealed class NoteLibraryForm : Form
    {
        private sealed class Choice { internal Guid? Id; internal string Key, Label; public override string ToString() { return Label; } }

        private readonly NoteManager mgr;
        private readonly ListBox notebookList, tagList;
        private readonly TextBox search;
        private readonly NoteListBox results;
        private readonly Label selectionText, status, searchHint;
        private readonly Button selectAll, clearAll, move, delete, renameNb, deleteNb;
        private readonly System.Windows.Forms.Timer searchTimer = new System.Windows.Forms.Timer { Interval = 150 }, refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        private Guid? notebookFilter; private string tagFilter; private bool suppress;

        internal NoteLibraryForm(NoteManager mgr)
        {
            this.mgr = mgr; FriendUI.Setup(this, mgr.AppIcon); Text = "비키니 친구들 · 메모 라이브러리"; ClientSize = new Size(920, 620); MinimumSize = new Size(700, 440);
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true; ShowInTaskbar = true; StartPosition = FormStartPosition.CenterScreen;

            var left = new Panel { Dock = DockStyle.Left, Width = 256, Padding = new Padding(12, 14, 6, 10) };
            var nbLabel = new Label { Text = "노트북", Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.BottomLeft, ForeColor = FriendUI.Ink };
            var nbHead = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false };
            var newNb = Small("＋ 새로"); renameNb = Small("이름"); deleteNb = Small("삭제"); nbHead.Controls.AddRange(new Control[] { newNb, renameNb, deleteNb });
            notebookList = new ListBox { Dock = DockStyle.Top, Height = 220, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
            var tagHead = new Label { Text = "태그", Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.BottomLeft, ForeColor = FriendUI.Ink };
            tagList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
            left.Controls.Add(tagList); left.Controls.Add(tagHead); left.Controls.Add(notebookList); left.Controls.Add(nbHead); left.Controls.Add(nbLabel);

            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 14, 14, 10) };
            var top = new Panel { Dock = DockStyle.Top, Height = 34 };
            search = new TextBox { Dock = DockStyle.Fill }; var refresh = new Button { Text = "⟳", Dock = DockStyle.Right, Width = 36 };
            searchHint = new Label { Text = "메모 검색", AutoSize = true, ForeColor = Color.Gray, BackColor = Color.White, Left = 6, Top = 4, Cursor = Cursors.IBeam }; searchHint.Click += delegate { search.Focus(); };
            top.Controls.Add(search); top.Controls.Add(refresh); search.Controls.Add(searchHint);
            var tools = new Panel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(0, 4, 0, 4) };
            selectionText = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
            var toolButtons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
            selectAll = Small("모두 선택"); clearAll = Small("선택 해제"); move = Small("노트북으로 이동 ▾"); delete = Small("선택 삭제"); toolButtons.Controls.AddRange(new Control[] { selectAll, clearAll, move, delete });
            tools.Controls.Add(selectionText); tools.Controls.Add(toolButtons);
            status = new Label { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
            results = new NoteListBox { Dock = DockStyle.Fill };
            right.Controls.Add(results); right.Controls.Add(status); right.Controls.Add(tools); right.Controls.Add(top);
            Controls.Add(right); Controls.Add(left); right.BringToFront();

            search.TextChanged += delegate { searchHint.Visible = search.TextLength == 0; searchTimer.Stop(); searchTimer.Start(); };
            searchTimer.Tick += delegate { searchTimer.Stop(); RefreshResults(); };
            refreshTimer.Tick += delegate { refreshTimer.Stop(); RefreshAll(); };
            refresh.Click += delegate { RefreshAll(); };
            notebookList.SelectedIndexChanged += delegate { if (suppress) return; var c = notebookList.SelectedItem as Choice; notebookFilter = c == null ? null : c.Id; UpdateNotebookButtons(); RefreshResults(); };
            tagList.SelectedIndexChanged += delegate { if (suppress) return; var c = tagList.SelectedItem as Choice; tagFilter = c == null ? null : c.Key; RefreshResults(); };
            results.NoteClicked += n => mgr.Focus(n.Id);
            results.CheckedChanged += UpdateSelection;
            selectAll.Click += delegate { foreach (var n in results.Items.OfType<Note>()) results.Checked.Add(n.Id); results.Invalidate(); UpdateSelection(); };
            clearAll.Click += delegate { results.Checked.Clear(); results.Invalidate(); UpdateSelection(); };
            delete.Click += delegate { DeleteChecked(); };
            move.Click += delegate { ShowMoveMenu(); };
            newNb.Click += delegate { NewNotebook(); }; renameNb.Click += delegate { RenameNotebook(); }; deleteNb.Click += delegate { DeleteNotebook(); };
            Action changed = () => { if (!IsDisposed && results.Checked.Count == 0) { refreshTimer.Stop(); refreshTimer.Start(); } };
            mgr.Store.Changed += changed;
            FormClosed += delegate { mgr.Store.Changed -= changed; searchTimer.Dispose(); refreshTimer.Dispose(); };
            ApplyHelperStyle(); RefreshAll();
        }

        private static Button Small(string text) { return new Button { Text = text, AutoSize = true, Height = 26, Margin = new Padding(3, 0, 3, 0), Padding = new Padding(4, 0, 4, 0) }; }
        private void ApplyHelperStyle() { BackColor = FriendUI.Paper; ForeColor = FriendUI.Ink; }

        internal void RefreshAll() { RefreshFilters(); RefreshResults(); }

        private void RefreshFilters()
        {
            suppress = true;
            var nbs = new List<Choice> { new Choice { Label = "전체" } }; nbs.AddRange(mgr.Store.Notebooks.Select(n => new Choice { Id = n.Id, Label = n.Name }));
            notebookList.DataSource = null; notebookList.Items.Clear(); foreach (var c in nbs) notebookList.Items.Add(c);
            int ni = nbs.FindIndex(c => c.Id == notebookFilter); if (ni < 0) { notebookFilter = null; ni = 0; } notebookList.SelectedIndex = ni;
            var tags = new List<Choice> { new Choice { Label = "전체" } }; tags.AddRange(mgr.Store.TagUsage().Select(t => new Choice { Key = TagParser.Normalize(t.Key), Label = "#" + t.Key + "  (" + t.Value + ")" }));
            tagList.Items.Clear(); foreach (var c in tags) tagList.Items.Add(c);
            int ti = tags.FindIndex(c => c.Key == tagFilter); if (ti < 0) { tagFilter = null; ti = 0; } tagList.SelectedIndex = ti;
            suppress = false; UpdateNotebookButtons();
        }

        private void RefreshResults()
        {
            var rows = mgr.Store.Query(search.Text, notebookFilter, tagFilter);
            results.BeginUpdate(); results.Items.Clear(); foreach (var n in rows) results.Items.Add(n); results.EndUpdate();
            results.Checked.IntersectWith(rows.Select(n => n.Id));
            status.Text = rows.Count > 0 ? rows.Count + "개" : string.IsNullOrWhiteSpace(search.Text) ? "메모가 없어요" : "‘" + search.Text.Trim() + "’ 에 해당하는 메모가 없어요";
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            int c = results.Checked.Count;
            selectionText.Text = c == 0 ? "메모를 고르세요" : c + "개 선택됨";
            selectAll.Enabled = results.Items.Count > 0 && c < results.Items.Count; clearAll.Enabled = c > 0; move.Enabled = c > 0; delete.Enabled = c > 0;
        }

        private void UpdateNotebookButtons() { bool sel = notebookFilter.HasValue; renameNb.Enabled = sel; deleteNb.Enabled = sel; }

        private void NewNotebook()
        {
            using (var dlg = new PromptForm("새 노트북", "노트북 이름", "")) if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Value.Length > 0) { var nb = mgr.Store.CreateNotebook(dlg.Value); notebookFilter = nb.Id; RefreshAll(); }
        }

        private void RenameNotebook()
        {
            if (!notebookFilter.HasValue) return; var c = notebookList.SelectedItem as Choice; if (c == null) return;
            using (var dlg = new PromptForm("노트북 이름 변경", "노트북 이름", c.Label)) if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Value.Length > 0) { mgr.Store.RenameNotebook(notebookFilter.Value, dlg.Value); RefreshAll(); }
        }

        /// <summary>노트북을 지워도 안의 메모는 남는다(노트북에서만 빠진다).</summary>
        private void DeleteNotebook()
        {
            if (!notebookFilter.HasValue) return; var c = notebookList.SelectedItem as Choice; if (c == null) return;
            if (MessageBox.Show(this, "노트북 ‘" + c.Label + "’ 을(를) 삭제할까요?\n안에 있는 메모는 지워지지 않고 노트북에서만 빠져요.", "노트북 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            mgr.Store.DeleteNotebook(notebookFilter.Value); notebookFilter = null; RefreshAll();
        }

        private void ShowMoveMenu()
        {
            if (results.Checked.Count == 0) { status.Text = "옮길 메모를 고르세요"; return; }
            var menu = new ContextMenuStrip(); menu.Items.Add("노트북 없음", null, delegate { MoveChecked(null); });
            var nbs = mgr.Store.Notebooks; if (nbs.Count > 0) menu.Items.Add(new ToolStripSeparator());
            foreach (var nb in nbs) { Guid id = nb.Id; menu.Items.Add(nb.Name, null, delegate { MoveChecked(id); }); }
            menu.Closed += delegate { menu.BeginInvoke(new Action(menu.Dispose)); }; menu.Show(move, new Point(0, move.Height));
        }

        private void MoveChecked(Guid? notebook)
        {
            int count = results.Checked.Count; foreach (var id in results.Checked.ToList()) mgr.Store.SetNotebook(id, notebook);
            results.Checked.Clear(); RefreshAll(); status.Text = "메모 " + count + "개를 옮겼어요";
        }

        /// <summary>삭제는 되돌릴 수 없다(휴지통 없음) — 기록과 첨부까지 그 자리에서 사라지므로 한 번 더 묻는다.</summary>
        private void DeleteChecked()
        {
            int count = results.Checked.Count; if (count == 0) { status.Text = "삭제할 메모를 고르세요"; return; }
            if (MessageBox.Show(this, "선택한 메모 " + count + "개를 완전히 삭제할까요?\n기록과 첨부도 함께 사라지고 되돌릴 수 없어요.", "메모 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            mgr.DeleteNotes(results.Checked.ToList()); results.Checked.Clear(); RefreshAll(); status.Text = "메모 " + count + "개를 삭제했어요";
        }
    }

    /// <summary>
    /// 메모 기록. 왼쪽에서 예전 모습을 고르고 오른쪽에서 읽어 본 뒤 복원한다. 복원도 일반 저장이라 복원 직전의 글을 기록에 남기므로
    /// 잘못 복원한 것도 되돌릴 수 있다.
    /// </summary>
    internal sealed class NoteHistoryForm : Form
    {
        private readonly NoteManager mgr; private readonly Guid noteId;
        private readonly ListBox list; private readonly TextBox body; private readonly Button restore; private readonly Label status;
        private readonly List<NoteRevision> revisions;

        internal NoteHistoryForm(NoteManager mgr, Guid noteId)
        {
            this.mgr = mgr; this.noteId = noteId; FriendUI.Setup(this, mgr.AppIcon); Text = "비키니 친구들 · 메모 기록"; ClientSize = new Size(740, 500); MinimumSize = new Size(560, 360);
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 50 };
            restore = new Button { Text = "이 모습으로 복원", Width = 140, Height = 32, Enabled = false, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            var close = new Button { Text = "닫기", Width = 90, Height = 32, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            status = new Label { Left = 14, Top = 8, Height = 34, Width = 300, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
            bottom.Controls.AddRange(new Control[] { status, restore, close });
            bottom.Resize += delegate { close.Left = bottom.Width - 14 - close.Width; restore.Left = close.Left - 8 - restore.Width; restore.Top = close.Top = 9; status.Width = Math.Max(0, restore.Left - 8 - status.Left); };
            CancelButton = close; close.Click += delegate { Close(); };
            var head = new Label { Dock = DockStyle.Top, Height = 44, Padding = new Padding(14, 8, 0, 0), Text = "덮어쓰기 전의 글이에요. 복원해도 지금 글은 기록에 남아요.", ForeColor = Color.DimGray };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12, 0, 12, 0) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 8, 0) };
            body = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
            table.Controls.Add(list, 0, 0); table.Controls.Add(body, 1, 0);
            Controls.Add(table); Controls.Add(head); Controls.Add(bottom); table.BringToFront();
            revisions = mgr.Store.Revisions(noteId);
            foreach (var r in revisions) list.Items.Add(r.CreatedUtc.ToLocalTime().ToString("M/d HH:mm", CultureInfo.InvariantCulture) + "   " + r.Content.Length.ToString("N0") + "자");
            status.Text = revisions.Count == 0 ? "아직 남은 기록이 없어요" : "기록 " + revisions.Count + "개";
            list.SelectedIndexChanged += delegate { int i = list.SelectedIndex; restore.Enabled = i >= 0 && i < revisions.Count; body.Text = restore.Enabled ? revisions[i].Content.Replace("\n", "\r\n") : ""; body.Select(0, 0); };
            restore.Click += delegate
            {
                int i = list.SelectedIndex; if (i < 0 || i >= revisions.Count) return;
                if (mgr.Store.Get(noteId) == null) { status.Text = "메모가 삭제되어 복원할 수 없어요"; return; }
                mgr.RestoreContent(noteId, revisions[i].Content); Close();
            };
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
    }

    /// <summary>직접 정하는 알림: 날짜 · 시각 · 반복. 만들기 전에 어떻게 읽었는지 한 줄로 보여 준다.</summary>
    internal sealed class ReminderForm : Form
    {
        private readonly DateTimePicker date, time; private readonly ComboBox repeat; private readonly NumericUpDown interval; private readonly Label summary, unit;
        private readonly Action<DateTime, string> create;

        internal ReminderForm(Action<DateTime, string> create)
        {
            this.create = create; FriendUI.Setup(this, null); Text = "비키니 친구들 · 알림 만들기"; ClientSize = new Size(380, 230); ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            Controls.Add(new Label { Text = "언제 알려 줄까?", AutoSize = true, Left = 14, Top = 14, ForeColor = FriendUI.Ink });
            var first = DateTime.Now.AddHours(1); first = new DateTime(first.Year, first.Month, first.Day, first.Hour, first.Minute, 0);
            date = new DateTimePicker { Left = 14, Top = 40, Width = 150, Format = DateTimePickerFormat.Short, Value = first };
            time = new DateTimePicker { Left = 172, Top = 40, Width = 96, Format = DateTimePickerFormat.Custom, CustomFormat = "tt hh:mm", ShowUpDown = true, Value = first };
            Controls.Add(date); Controls.Add(time);
            Controls.Add(new Label { Text = "반복", AutoSize = true, Left = 14, Top = 82, ForeColor = FriendUI.Ink });
            repeat = new ComboBox { Left = 14, Top = 104, Width = 110, DropDownStyle = ComboBoxStyle.DropDownList }; repeat.Items.AddRange(new object[] { "한 번만", "매일", "매주", "매달", "매년" }); repeat.SelectedIndex = 0;
            interval = new NumericUpDown { Left = 134, Top = 104, Width = 58, Minimum = 1, Maximum = 366, Value = 1, Enabled = false }; unit = new Label { Left = 198, Top = 108, AutoSize = true, Text = "", ForeColor = FriendUI.Ink };
            Controls.Add(repeat); Controls.Add(interval); Controls.Add(unit);
            summary = new Label { Left = 14, Top = 144, Width = 350, Height = 38, ForeColor = Color.DimGray }; Controls.Add(summary);
            var ok = new Button { Text = "만들기", Left = 200, Top = 188, Width = 80, Height = 28 }; var cancel = new Button { Text = "취소", Left = 288, Top = 188, Width = 76, Height = 28, DialogResult = DialogResult.Cancel };
            Controls.Add(ok); Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel;
            EventHandler update = delegate { interval.Enabled = repeat.SelectedIndex > 0; unit.Text = repeat.SelectedIndex == 1 ? "일마다" : repeat.SelectedIndex == 2 ? "주마다" : repeat.SelectedIndex == 3 ? "개월마다" : repeat.SelectedIndex == 4 ? "년마다" : ""; UpdateSummary(); };
            date.ValueChanged += update; time.ValueChanged += update; repeat.SelectedIndexChanged += update; interval.ValueChanged += update;
            ok.Click += delegate { create(Due, RuleText); Close(); };
            update(null, EventArgs.Empty);
        }

        private DateTime Due { get { return new DateTime(date.Value.Year, date.Value.Month, date.Value.Day, time.Value.Hour, time.Value.Minute, 0); } }
        private string RuleText
        {
            get
            {
                if (repeat.SelectedIndex <= 0) return null;
                var f = repeat.SelectedIndex == 1 ? RecurrenceFrequency.Daily : repeat.SelectedIndex == 2 ? RecurrenceFrequency.Weekly : repeat.SelectedIndex == 3 ? RecurrenceFrequency.Monthly : RecurrenceFrequency.Yearly;
                return new Recurrence(f, (int)interval.Value).ToRule();
            }
        }

        private void UpdateSummary()
        {
            var rec = Recurrence.Parse(RuleText);
            string text = Due.ToString("yyyy-MM-dd (ddd) tt hh:mm", new CultureInfo("ko-KR")) + (rec.HasValue ? "  ·  " + rec.Value.Describe() : "  ·  한 번만");
            if (Due <= DateTime.Now) text += "\n이미 지난 시각이라 만들면 바로 알려요.";
            summary.Text = text;
        }
    }
}
