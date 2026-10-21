using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BikiniFriends
{
    /// <summary>친구 기록 창: 친밀도·포만감·간식 주기·생활 설정·발견한 비밀.</summary>
    internal sealed class FriendRecordForm : Form
    {
        private Label lifeSummary, lifeDetails, discoverySummary;
        private Button snackButton;
        private readonly System.Windows.Forms.Timer recordTimer;

        internal FriendRecordForm(App app)
        {
            FriendUI.Setup(this, app.TrayIconImage); Name = "friendRecords"; Text = "비키니친구들 · 친구 기록";
            ClientSize = new Size(500, 560); MinimizeBox = true; ShowInTaskbar = true;
            var panel = new FlowLayoutPanel { Name = "lifeView", Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16, 12, 12, 12) };
            Controls.Add(panel);
            lifeSummary = FriendUI.Label(""); lifeSummary.MaximumSize = new Size(430, 0); lifeSummary.Font = new Font(Font.FontFamily, 13, FontStyle.Bold); panel.Controls.Add(lifeSummary);
            lifeDetails = FriendUI.Label(""); panel.Controls.Add(lifeDetails);
            snackButton = FriendUI.Button("간식 주기", delegate { var pet = app.LiveWindows.Select(w => w.Pet).FirstOrDefault(); if (pet != null) pet.Feed(app); UpdateLifeView(); });
            panel.Controls.Add(snackButton);
            panel.Controls.Add(FriendUI.Label("생활 설정"));
            var options = PlayOptions.Current;
            panel.Controls.Add(FriendUI.Label("간식은 포만감 +25씩, 최대 100까지 채워줘요."));
            panel.Controls.Add(FriendUI.Label("함께한 시간 1분마다 친밀도도 1씩 쌓여요."));
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var caption = FriendUI.Label("간식 없이 배고파지기까지"); caption.AutoSize = false; caption.Size = new Size(224, 26); row.Controls.Add(caption);
            var hunger = new NumericUpDown { Name = "hungerMinutes", Minimum = 0, Maximum = 360, Value = options.HungerMinutes, Width = 72, Margin = new Padding(4, 5, 4, 0) };
            row.Controls.Add(hunger); row.Controls.Add(FriendUI.Label("분")); panel.Controls.Add(row);
            panel.Controls.Add(FriendUI.Label("0분으로 설정하면 항상 우는 표정을 볼 수 있어요."));
            var cry = new CheckBox { Name = "hungerCry", AutoSize = true, Text = "배고프면 계속 우는 표정", Checked = options.HungerCry, Margin = new Padding(4, 6, 4, 4) }; panel.Controls.Add(cry);
            var secrets = new CheckBox { AutoSize = true, Text = "숨겨진 반응 사용", Checked = options.Secrets, Margin = new Padding(4, 6, 4, 4) }; panel.Controls.Add(secrets);
            var save = FriendUI.Button("생활 설정 저장", delegate
            {
                options.HungerMinutes = (int)hunger.Value; options.HungerCry = cry.Checked; options.Secrets = secrets.Checked;
                if (!options.Save()) MessageBox.Show(this, "생활 설정을 저장하지 못했어.", "비키니친구들");
                UpdateLifeView();
            });
            save.Name = "saveLife"; panel.Controls.Add(save);
            discoverySummary = FriendUI.Label(""); discoverySummary.MaximumSize = new Size(430, 0); panel.Controls.Add(discoverySummary);
            var hints = FriendUI.Label("어질어질: 친구 머리 위에서 마우스를 한 바퀴 빙글빙글.\n몰래 군무: 친구 둘 이상일 때 커서를 떨어뜨리고 4초.\n비밀 조종사: 같은 친구를 10초 안에 세 번 휙 던지기.\n우클릭 → 동작 바로 보기에서도 확인할 수 있어요.");
            hints.MaximumSize = new Size(430, 0); panel.Controls.Add(hints);
            UpdateLifeView();
            recordTimer = new System.Windows.Forms.Timer { Interval = 500 }; recordTimer.Tick += delegate { UpdateLifeView(); }; recordTimer.Start();
            FormClosed += delegate { recordTimer.Stop(); recordTimer.Dispose(); };
        }

        private void UpdateLifeView()
        {
            if (lifeSummary == null) return; var d = FriendData.Current;
            lifeSummary.Text = "♥ 친밀도 " + d.Affinity.ToString("N0") + "   ·   함께 " + FriendData.FormatTogether(d.TogetherSeconds);
            lifeDetails.Text = "포만감 " + (int)Math.Floor(d.Satiety) + " / 100\n다음 꽃다발: 친밀도 " + ((d.Affinity / 100 + 1) * 100).ToString("N0")
                + "\n" + (d.CanSnack ? "지금 간식을 줄 수 있어." : "지금은 배가 꽉 찼어");
            snackButton.Enabled = d.CanSnack;
            var names = new[] { new[] { "dance", "몰래 하는 군무" }, new[] { "dizzy", "어질어질" }, new[] { "pilot", "비밀 조종사" } };
            discoverySummary.Text = "발견한 비밀 " + d.Discoveries.Count + " / 3\n" + string.Join("  ·  ", names.Where(p => d.Discoveries.Contains(p[0])).Select(p => p[1]).ToArray());
        }
    }

    /// <summary>체크하면 회색 취소선이 남는 할 일 목록.</summary>
    internal sealed class TodoCheckList : CheckedListBox
    {
        private Font completedFont;
        protected override void OnFontChanged(EventArgs e)
        { if (completedFont != null) { completedFont.Dispose(); completedFont = null; } base.OnFontChanged(e); }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            TodoItem task = Items[e.Index] as TodoItem;
            bool done = task != null && task.Done;
            e.DrawBackground();
            int glyph = Math.Max(13, Font.Height - 1), inset = 3;
            var check = new Rectangle(e.Bounds.Left + inset, e.Bounds.Top + (e.Bounds.Height - glyph) / 2, glyph, glyph);
            ControlPaint.DrawCheckBox(e.Graphics, check, done ? ButtonState.Checked : ButtonState.Normal);
            if (completedFont == null) completedFont = new Font(Font, Font.Style | FontStyle.Strikeout);
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color muted = Color.FromArgb((ForeColor.R * 2 + BackColor.R) / 3, (ForeColor.G * 2 + BackColor.G) / 3, (ForeColor.B * 2 + BackColor.B) / 3);
            Color color = selected ? SystemColors.HighlightText : done ? muted : ForeColor;
            var text = Rectangle.FromLTRB(check.Right + 6, e.Bounds.Top, e.Bounds.Right - 3, e.Bounds.Bottom);
            TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), done ? completedFont : Font, text, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }
        protected override void Dispose(bool disposing)
        { if (disposing && completedFont != null) { completedFont.Dispose(); completedFont = null; } base.Dispose(disposing); }
    }

    /// <summary>도우미 창: 할 일 / 메모 / 타이머 / 도우미 설정.</summary>
    internal sealed class FriendPanel : Form
    {
        private readonly CheckedListBox tasks;
        private readonly TextBox notes;
        private readonly Label remaining;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Label taskHeader;
        private bool populating, notesDirty;
        private DateTime notesChanged;

        internal FriendPanel(App app)
        {
            FriendUI.Setup(this, app.TrayIconImage); Text = "비키니친구들 · 오늘의 작은 도우미"; ClientSize = new Size(550, 540);
            MinimizeBox = true; ShowInTaskbar = true;
            var tabs = new TabControl { Name = "friendTabs", Dock = DockStyle.Fill, Padding = new Point(12, 7) }; Controls.Add(tabs);

            var todo = new TabPage("할 일") { BackColor = BackColor, Padding = new Padding(12) }; tabs.TabPages.Add(todo);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); todo.Controls.Add(layout);
            taskHeader = FriendUI.Label(""); layout.Controls.Add(taskHeader, 0, 0);
            tasks = new TodoCheckList { Name = "todoList", Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle, DrawMode = DrawMode.OwnerDrawFixed };
            layout.Controls.Add(tasks, 0, 1);
            var entryRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            var entry = new TextBox { Width = 230, Margin = new Padding(3, 9, 3, 3), MaxLength = 200 };
            entryRow.Controls.Add(entry); entryRow.Controls.Add(FriendUI.Button("추가", delegate { AddTask(entry); }));
            entryRow.Controls.Add(FriendUI.Button("삭제", delegate { int i = tasks.SelectedIndex; if (i >= 0) { FriendData.Current.Todos.RemoveAt(i); RefreshTasks(); SaveOrWarn(); } }));
            entry.KeyDown += delegate (object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { AddTask(entry); e.SuppressKeyPress = true; } };
            layout.Controls.Add(entryRow, 0, 2);
            RefreshTasks();
            tasks.ItemCheck += delegate (object sender, ItemCheckEventArgs e)
            {
                if (populating || e.Index >= FriendData.Current.Todos.Count) return;
                FriendData.Current.Todos[e.Index].Done = e.NewValue == CheckState.Checked;
                if (e.NewValue == CheckState.Checked) { FriendData.Current.Like(2); app.Announce("하나 끝냈다! 수고했어"); }
                SaveOrWarn(); tasks.Invalidate(); UpdateTaskSummary();
            };

            var memo = new TabPage("메모") { BackColor = BackColor, Padding = new Padding(12) }; tabs.TabPages.Add(memo);
            notes = new TextBox { Name = "memoText", Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Text = FriendData.Current.Notes, MaxLength = 100000 };
            memo.Controls.Add(notes); notes.TextChanged += delegate { FriendData.Current.Notes = notes.Text; notesDirty = true; notesChanged = DateTime.UtcNow; };

            var time = new TabPage("타이머") { BackColor = BackColor, Padding = new Padding(15) }; tabs.TabPages.Add(time);
            var clock = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false }; time.Controls.Add(clock);
            clock.Controls.Add(FriendUI.Label("집중 시간이나 잠깐 쉬는 시간을 정해줘."));
            var duration = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var minutes = new NumericUpDown { Name = "timerMinutes", Minimum = 1, Maximum = 240, Value = 25, Width = 80, Margin = new Padding(4) };
            duration.Controls.Add(minutes); duration.Controls.Add(FriendUI.Label("분 뒤에 알려주기")); clock.Controls.Add(duration);
            var message = new TextBox { Name = "timerMessage", Width = 400, Text = FriendData.Current.TimerText, MaxLength = 200, Margin = new Padding(4, 8, 4, 8) }; clock.Controls.Add(message);
            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var startTimer = FriendUI.Button("시작", delegate
            { FriendData.Current.TimerText = message.Text; FriendData.Current.TimerUtc = DateTime.UtcNow.AddMinutes((double)minutes.Value); SaveOrWarn(); UpdateTimer(); });
            startTimer.Name = "startTimer"; actions.Controls.Add(startTimer);
            actions.Controls.Add(FriendUI.Button("타이머 취소", delegate { FriendData.Current.TimerUtc = DateTime.MinValue; SaveOrWarn(); UpdateTimer(); })); clock.Controls.Add(actions);
            remaining = FriendUI.Label(""); remaining.Font = new Font(Font.FontFamily, 20, FontStyle.Bold); clock.Controls.Add(remaining);
            clock.Controls.Add(FriendUI.Label("시간이 되면 직접 닫는 알림창이 떠요.\n도우미 창을 닫아도 타이머는 계속 돌아가요."));

            AddHelperSettingsTab(tabs); ApplyHelperColors();
            timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += delegate { UpdateTimer(); UpdateTaskSummary(); if (notesDirty && (DateTime.UtcNow - notesChanged).TotalSeconds > 1) { notesDirty = false; SaveOrWarn(); } };
            timer.Start();
            FormClosed += delegate { timer.Stop(); timer.Dispose(); FriendData.Current.Notes = notes.Text; FriendData.Current.Save(); };
            UpdateTimer();
        }

        private void AddTask(TextBox entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Text)) return;
            FriendData.Current.Todos.Add(new TodoItem { Text = entry.Text.Trim() }); entry.Clear(); RefreshTasks(); SaveOrWarn(); entry.Focus();
        }
        private void RefreshTasks()
        { populating = true; tasks.Items.Clear(); foreach (var t in FriendData.Current.Todos) tasks.Items.Add(t, t.Done); populating = false; UpdateTaskSummary(); }
        private void UpdateTaskSummary()
        {
            if (taskHeader == null) return;
            int done = FriendData.Current.Todos.Count(t => t.Done);
            taskHeader.Text = "하나씩 끝내자!   진행 " + (FriendData.Current.Todos.Count - done) + "  ·  완료 " + done;
        }
        private void UpdateTimer()
        {
            var d = FriendData.Current.TimerUtc;
            if (d == DateTime.MinValue) remaining.Text = "타이머 대기 중";
            else { int s = Math.Max(0, (int)Math.Ceiling((d - DateTime.UtcNow).TotalSeconds)); remaining.Text = string.Format("{0:00}:{1:00} 남았어", s / 60, s % 60); }
        }
        private void SaveOrWarn()
        { if (!FriendData.Current.Save()) MessageBox.Show(this, "파일에 저장하지 못했어. 저장 위치의 쓰기 권한을 확인해 줘.", "비키니친구들"); }

        // ───── 도우미 설정 탭 ─────
        private void AddHelperSettingsTab(TabControl tabs)
        {
            TopMost = PlayOptions.Current.MemoTopMost; Opacity = PlayOptions.Current.MemoOpacityPercent / 100.0;
            var tab = new TabPage("도우미 설정") { Name = "helperSettings", Padding = new Padding(18, 12, 12, 12) }; tabs.TabPages.Add(tab);
            var panel = new FlowLayoutPanel { Name = "helperSettingsPanel", Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true }; tab.Controls.Add(panel);
            var heading = FriendUI.Label("도우미 창 설정"); heading.Font = new Font(Font.FontFamily, 12, FontStyle.Bold); panel.Controls.Add(heading);
            var pin = new CheckBox { Name = "memoTopMost", Text = "맨 앞 고정", Checked = TopMost, AutoSize = true, Margin = new Padding(4, 8, 4, 8) };
            pin.CheckedChanged += delegate { TopMost = pin.Checked; PlayOptions.Current.MemoTopMost = pin.Checked; SaveWindowOptions(); }; panel.Controls.Add(pin);
            var opacityRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            opacityRow.Controls.Add(FriendUI.Label("창 진하기"));
            var opacity = new NumericUpDown { Name = "memoOpacity", Minimum = 20, Maximum = 100, Increment = 5, Value = PlayOptions.Current.MemoOpacityPercent, Width = 72, Margin = new Padding(4, 5, 4, 4) };
            opacity.ValueChanged += delegate { Opacity = (double)opacity.Value / 100; PlayOptions.Current.MemoOpacityPercent = (int)opacity.Value; SaveWindowOptions(); };
            opacityRow.Controls.Add(opacity); opacityRow.Controls.Add(FriendUI.Label("%  ·  낮을수록 투명해요")); panel.Controls.Add(opacityRow);
            var minimize = FriendUI.Button("이 창 최소화", delegate { WindowState = FormWindowState.Minimized; }); minimize.Name = "memoMinimize"; panel.Controls.Add(minimize);
            var colors = FriendUI.Label("배경색 · 글자색"); colors.Font = new Font(Font.FontFamily, 11, FontStyle.Bold); panel.Controls.Add(colors);
            var choices = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            choices.Controls.Add(FriendUI.Button("배경색 선택...", delegate { PickHelperColor(true); }));
            choices.Controls.Add(FriendUI.Button("글자색 선택...", delegate { PickHelperColor(false); })); panel.Controls.Add(choices);
            panel.Controls.Add(FriendUI.Button("기본 색상으로", delegate { SetHelperColors(FriendUI.Paper, FriendUI.Ink); }));
            var preview = FriendUI.Label("이런 배경색과 글자색으로 보여요."); preview.Font = new Font(Font.FontFamily, 12, FontStyle.Bold); preview.MaximumSize = new Size(460, 0); panel.Controls.Add(preview);
            var hint = FriendUI.Label("할 일 · 메모 · 타이머에 함께 적용돼요.\n변경한 설정과 색상은 자동으로 저장돼요."); hint.MaximumSize = new Size(460, 0); panel.Controls.Add(hint);
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.DrawItem += delegate (object sender, DrawItemEventArgs e)
            {
                using (var brush = new SolidBrush(BackColor)) e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                if (e.Index == tabs.SelectedIndex) using (var pen = new Pen(ForeColor, 2)) e.Graphics.DrawLine(pen, e.Bounds.Left + 5, e.Bounds.Bottom - 2, e.Bounds.Right - 5, e.Bounds.Bottom - 2);
            };
        }

        private void PickHelperColor(bool background)
        {
            using (var chooser = new ColorDialog { FullOpen = true, Color = background ? BackColor : ForeColor })
                if (chooser.ShowDialog(this) == DialogResult.OK) SetHelperColors(background ? chooser.Color : BackColor, background ? ForeColor : chooser.Color);
        }

        internal void SetHelperColors(Color background, Color foreground)
        {
            PlayOptions.Current.HelperBackgroundArgb = Color.FromArgb(background.R, background.G, background.B).ToArgb();
            PlayOptions.Current.HelperForegroundArgb = Color.FromArgb(foreground.R, foreground.G, foreground.B).ToArgb();
            ApplyHelperColors(); SaveWindowOptions();
        }

        private void ApplyHelperColors()
        {
            Color bg = Color.FromArgb(PlayOptions.Current.HelperBackgroundArgb), fg = Color.FromArgb(PlayOptions.Current.HelperForegroundArgb);
            SuspendLayout(); ApplyColors(this, bg, fg); ResumeLayout(); Invalidate(true);
        }

        private static void ApplyColors(Control control, Color bg, Color fg)
        {
            control.BackColor = bg; control.ForeColor = fg;
            var button = control as Button; if (button != null) button.UseVisualStyleBackColor = false;
            foreach (Control child in control.Controls) ApplyColors(child, bg, fg);
        }

        private void SaveWindowOptions()
        { if (!PlayOptions.Current.Save()) MessageBox.Show(this, "도우미 설정을 저장하지 못했어. 지금 실행 중에는 적용돼.", "비키니친구들"); }
    }
}
