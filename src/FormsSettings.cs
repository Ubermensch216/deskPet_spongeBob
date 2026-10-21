using System;
using System.Drawing;
using System.Windows.Forms;

namespace BikiniFriends
{
    internal static class FriendUI
    {
        internal static readonly Color Paper = Color.FromArgb(255, 252, 249), Ink = Color.FromArgb(68, 50, 50);
        internal static void Setup(Form f, Icon icon)
        {
            f.Font = new Font("Malgun Gothic", 9f); f.BackColor = Paper;
            f.StartPosition = FormStartPosition.CenterScreen; f.AutoScaleMode = AutoScaleMode.Dpi;
            f.MinimizeBox = false; f.MaximizeBox = false; f.FormBorderStyle = FormBorderStyle.FixedDialog;
            if (icon != null) f.Icon = icon;
        }
        internal static Button Button(string text, EventHandler click)
        { var b = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 3, 6, 3), Margin = new Padding(4) }; b.Click += click; return b; }
        internal static Label Label(string text)
        { return new Label { Text = text, AutoSize = true, ForeColor = Ink, Margin = new Padding(4, 8, 4, 8) }; }
    }

    /// <summary>알림 설정: 점심·퇴근·낮잠·정각 알림·가장자리 빛·자동 시작.</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly App app;
        private readonly CheckBox cLunch, cLeave, cNap, cHourly, cStartup, cPopup, cGlow, cQuiet;
        private readonly TextBox tLunch, tLunchMsg, tLeave, tLeaveMsg;
        private readonly NumericUpDown nNap, nStart, nEnd;
        private readonly ComboBox glowTheme, glowWidth, glowStrength;

        public SettingsForm(App app)
        {
            this.app = app; Settings s = app.Settings;
            FriendUI.Setup(this, app.TrayIconImage);
            Text = "비키니친구들 · 자동 시작 / 알림 설정"; ShowInTaskbar = true; TopMost = true; BackColor = SystemColors.Control;
            Padding = new Padding(14);
            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
            Controls.Add(panel);

            Func<string, bool, CheckBox> check = (t, v) => { var c = new CheckBox { Text = t, Checked = v, AutoSize = true, Margin = new Padding(3) }; return c; };
            Func<string, Label> hint = t => new Label { Text = t, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(24, 0, 3, 6) };

            cHourly = check("정각 알림 (매시간 현재 시각 알려주기)", s.HourlyOn); panel.Controls.Add(cHourly);
            panel.Controls.Add(hint("표시 방식과 평일 제외 시간은 아래에서 선택하세요."));
            cStartup = check("Windows 로그인 시 자동으로 켜기", StartupOption.IsEnabled()); panel.Controls.Add(cStartup);
            cPopup = check("별도 정각 알림창 사용 (직접 닫을 때까지 유지)", s.HourlyWindow); panel.Controls.Add(cPopup);
            cGlow = check("알림창을 닫을 때까지 화면 가장자리 빛 유지", s.EdgeGlow); panel.Controls.Add(cGlow);
            var colorRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(24, 2, 0, 0) };
            colorRow.Controls.Add(Hint("빛 색상"));
            glowTheme = Combo(new[] { "피치 · 핑크 · 보라 · 하늘", "노을", "오로라", "무지개" }, s.EdgeGlowTheme, 200); colorRow.Controls.Add(glowTheme); panel.Controls.Add(colorRow);
            var glowRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(24, 2, 0, 0) };
            glowRow.Controls.Add(Hint("퍼지는 폭")); glowWidth = Combo(new[] { "보통", "넓게", "아주 넓게" }, s.EdgeGlowWidth, 90); glowRow.Controls.Add(glowWidth);
            glowRow.Controls.Add(Hint("진하기")); glowStrength = Combo(new[] { "은은하게", "선명하게", "강하게" }, s.EdgeGlowStrength, 90); glowRow.Controls.Add(glowStrength);
            panel.Controls.Add(glowRow);
            cQuiet = check("평일(월~금) 아래 시간에는 정각 알림 끄기", s.QuietWeekdays); panel.Controls.Add(cQuiet);
            var quietRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(24, 2, 0, 0) };
            nStart = Hour(s.QuietStart); nEnd = Hour(s.QuietEnd);
            quietRow.Controls.Add(nStart); quietRow.Controls.Add(Hint("시 ~")); quietRow.Controls.Add(nEnd); quietRow.Controls.Add(Hint("시 (양 끝 정각 포함)")); panel.Controls.Add(quietRow);
            cQuiet.CheckedChanged += delegate { nStart.Enabled = nEnd.Enabled = cQuiet.Checked; };
            nStart.Enabled = nEnd.Enabled = cQuiet.Checked;
            var previewRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            var tip = Hint("9~18 설정: 평일 09~18시 제외, 주말은 알림"); tip.Margin = new Padding(24, 9, 14, 0);
            previewRow.Controls.Add(tip);
            previewRow.Controls.Add(new Button { Text = "정각창 미리보기", AutoSize = true }.With(b => b.Click += PreviewClick));
            panel.Controls.Add(previewRow);

            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
            cLunch = check("점심 알림", s.LunchOn); tLunch = new TextBox { Text = s.LunchTime, Width = 60 }; tLunchMsg = new TextBox { Text = s.LunchMsg, Width = 250 };
            grid.Controls.Add(cLunch, 0, 0); grid.Controls.Add(Label2("시간"), 1, 0); grid.Controls.Add(tLunch, 2, 0);
            grid.Controls.Add(Label2(""), 0, 1); grid.Controls.Add(Label2("문구"), 1, 1); grid.Controls.Add(tLunchMsg, 2, 1);
            cLeave = check("퇴근 알림", s.LeaveOn); cLeave.Margin = new Padding(3, 12, 3, 3);
            tLeave = new TextBox { Text = s.LeaveTime, Width = 60, Margin = new Padding(3, 12, 3, 3) }; tLeaveMsg = new TextBox { Text = s.LeaveMsg, Width = 250 };
            var l3 = Label2("시간"); l3.Margin = new Padding(3, 15, 3, 3);
            grid.Controls.Add(cLeave, 0, 2); grid.Controls.Add(l3, 1, 2); grid.Controls.Add(tLeave, 2, 2);
            grid.Controls.Add(Label2(""), 0, 3); grid.Controls.Add(Label2("문구"), 1, 3); grid.Controls.Add(tLeaveMsg, 2, 3);
            cNap = check("낮잠", s.NapOn); cNap.Margin = new Padding(3, 12, 3, 3);
            var napRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 9, 0, 0), WrapContents = false };
            nNap = new NumericUpDown { Minimum = 1, Maximum = 600, Value = Math.Max(1, Math.Min(600, s.NapMinutes)), Width = 60 };
            napRow.Controls.Add(nNap); napRow.Controls.Add(new Label { Text = "분 동안 입력이 없으면 잠들어요", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
            grid.Controls.Add(cNap, 0, 4); grid.Controls.Add(Label2(""), 1, 4); grid.Controls.Add(napRow, 2, 4);
            var note = new Label { Text = "시간은 12:00처럼 시:분으로 적어 주세요.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 10, 3, 3) };
            grid.Controls.Add(note, 0, 5); grid.SetColumnSpan(note, 3);
            panel.Controls.Add(grid);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
            var bLunch = new Button { Text = "점심 미리보기", AutoSize = true }; bLunch.Click += delegate { app.PreviewLunch(tLunchMsg.Text); };
            var bLeave = new Button { Text = "퇴근 미리보기", AutoSize = true }; bLeave.Click += delegate { app.PreviewLeave(tLeaveMsg.Text); };
            var bSave = new Button { Text = "저장", AutoSize = true, Margin = new Padding(18, 3, 3, 3) }; bSave.Click += delegate { DoSave(); };
            var bCancel = new Button { Text = "취소", AutoSize = true }; bCancel.Click += delegate { Close(); };
            buttons.Controls.AddRange(new Control[] { bLunch, bLeave, bSave, bCancel });
            panel.Controls.Add(buttons);
            AcceptButton = bSave; CancelButton = bCancel;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; MinimumSize = new Size(560, 0);
        }

        private static Label Label2(string text) { return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) }; }
        private static Label Hint(string text) { return new Label { Text = text, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 3, 0) }; }
        private static NumericUpDown Hour(int v) { return new NumericUpDown { Minimum = 0, Maximum = 23, Value = Math.Max(0, Math.Min(23, v)), Width = 58 }; }
        private static ComboBox Combo(string[] items, int selected, int width)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Margin = new Padding(3) };
            c.Items.AddRange(items); c.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, selected)); return c;
        }

        private void PreviewClick(object sender, EventArgs e)
        {
            var notice = new HourlyNotice(app.TrayIconImage);
            notice.SetMessage(App.HourlyText(DateTime.Now) + "\n(미리보기)");
            notice.EnableEdgeGlow(cGlow.Checked, glowTheme.SelectedIndex, glowWidth.SelectedIndex, glowStrength.SelectedIndex);
            notice.Show();
        }

        private void Warn(string m) { MessageBox.Show(this, m, "비키니친구들", MessageBoxButtons.OK, MessageBoxIcon.Asterisk); }
        private static string Norm(string v) { TimeSpan t = Settings.ParseTime(v).Value; return string.Format("{0:00}:{1:00}", t.Hours, t.Minutes); }

        private void DoSave()
        {
            if (!Settings.ParseTime(tLunch.Text).HasValue) { Warn("점심 시간을 12:00처럼 적어 주세요."); tLunch.Focus(); return; }
            if (!Settings.ParseTime(tLeave.Text).HasValue) { Warn("퇴근 시간을 18:00처럼 적어 주세요."); tLeave.Focus(); return; }
            Settings s = app.Settings;
            try { StartupOption.SetEnabled(cStartup.Checked); }
            catch (Exception ex) { Warn("자동 시작 설정을 저장하지 못했어요.\n" + ex.Message); return; }
            s.HourlyOn = cHourly.Checked; s.HourlyWindow = cPopup.Checked; s.EdgeGlow = cGlow.Checked;
            s.EdgeGlowTheme = glowTheme.SelectedIndex; s.EdgeGlowWidth = glowWidth.SelectedIndex; s.EdgeGlowStrength = glowStrength.SelectedIndex;
            s.QuietWeekdays = cQuiet.Checked; s.QuietStart = (int)nStart.Value; s.QuietEnd = (int)nEnd.Value;
            s.LunchOn = cLunch.Checked; s.LunchTime = Norm(tLunch.Text); s.LunchMsg = tLunchMsg.Text.Trim();
            s.LeaveOn = cLeave.Checked; s.LeaveTime = Norm(tLeave.Text); s.LeaveMsg = tLeaveMsg.Text.Trim();
            s.NapOn = cNap.Checked; s.NapMinutes = (int)nNap.Value;
            if (s.LunchMsg.Length == 0) s.LunchMsg = "점심 먹으러 가자!";
            if (s.LeaveMsg.Length == 0) s.LeaveMsg = "퇴근이다! 오늘도 수고했어";
            app.OnSettingsChanged();
            if (!s.Save()) Warn("설정 파일을 저장하지 못했어요. 이번에 켜 있는 동안만 적용돼요.");
            Close();
        }
    }

    internal static class ControlExtensions
    {
        internal static T With<T>(this T control, Action<T> action) { action(control); return control; }
    }

    /// <summary>움직임·반응 설정.</summary>
    internal sealed class PlaySettingsForm : Form
    {
        internal PlaySettingsForm(App app)
        {
            FriendUI.Setup(this, app.TrayIconImage); Text = "비키니친구들 · 움직임과 반응"; ClientSize = new Size(480, 640);
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(18, 12, 18, 12) };
            Controls.Add(panel);
            panel.Controls.Add(FriendUI.Label("친구가 어떻게 놀지 골라줘"));
            var o = PlayOptions.Current;
            Func<string, bool, CheckBox> choice = (text, value) => { var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(4, 3, 4, 3) }; panel.Controls.Add(c); return c; };
            var walls = choice("화면 양옆 벽 타기", o.Walls);
            var ceiling = choice("화면 천장 타기", o.Ceiling);
            var windows = choice("열린 창 옆을 올라가고 창 위에서 걷기 · 쉬기", o.Windows);
            var idle = choice("혼자 앉아서 쉬기", o.Idle);
            var shortRest = choice("앉아있는 시간 줄이기 (6~11초)", o.ShortRest);
            var escape = choice("커서가 가까이 오면 슬라이딩으로 도망가기", o.Escape);
            var follow = choice("커서를 따라오기", o.Follow);
            escape.CheckedChanged += delegate { if (escape.Checked) follow.Checked = false; };
            follow.CheckedChanged += delegate { if (follow.Checked) escape.Checked = false; };
            var focus = choice("얌전 모드: 바닥에만 머물고 도망가지 않기", o.Focus);
            var work = choice("평일 09:00~18:00에는 자동으로 얌전 모드", o.WorkFocus);
            var full = choice("전체화면 영상 · 게임을 볼 때 자동으로 숨기기", o.Fullscreen);
            var affinity = choice("친구 옆에 친밀도 숫자 표시", o.ShowAffinity);
            var pinned = choice("친구 위치 고정 (마우스로 옮길 수 있어요)", o.PositionFixed);
            var opacityRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            opacityRow.Controls.Add(FriendUI.Label("캐릭터 진하기"));
            var opacity = new NumericUpDown { Minimum = 20, Maximum = 100, Increment = 5, Value = o.PetOpacityPercent, Width = 72, Margin = new Padding(4) };
            opacityRow.Controls.Add(opacity); opacityRow.Controls.Add(FriendUI.Label("%  ·  낮을수록 투명해요")); panel.Controls.Add(opacityRow);
            panel.Controls.Add(FriendUI.Label("속도 조절  ·  숫자가 클수록 빨라져요"));
            Func<string, double, decimal, NumericUpDown> speedRow = (label, value, max) =>
            {
                var r = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 1, 0, 1) };
                var caption = FriendUI.Label(label); caption.AutoSize = false; caption.Width = 206; caption.Height = 23; caption.Margin = new Padding(4, 5, 4, 0); r.Controls.Add(caption);
                var n = new NumericUpDown { Minimum = .5m, Maximum = max, DecimalPlaces = 1, Increment = .1m, Value = (decimal)Math.Max(.5, Math.Min((double)max, value)), Width = 82, Margin = new Padding(4) };
                r.Controls.Add(n); var unit = FriendUI.Label("배"); unit.Margin = new Padding(4, 5, 4, 0); r.Controls.Add(unit); panel.Controls.Add(r); return n;
            };
            var speed = speedRow("걷기 · 기어오르기", o.Speed, 2m);
            var fallSpeed = speedRow("헬리콥터 낙하", o.FallSpeed, 3m);
            var slideSpeed = speedRow("슬라이딩", o.SlideSpeed, 3m);
            var delayRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            delayRow.Controls.Add(FriendUI.Label("슬라이딩 반응 대기"));
            var escapeDelay = new NumericUpDown { Minimum = .05m, Maximum = 1m, DecimalPlaces = 2, Increment = .05m, Value = (decimal)Math.Max(.05, Math.Min(1, o.EscapeDelay)), Width = 80, Margin = new Padding(4) };
            delayRow.Controls.Add(escapeDelay); delayRow.Controls.Add(FriendUI.Label("초")); panel.Controls.Add(delayRow);
            panel.Controls.Add(FriendUI.Label("클릭했을 때 반응"));
            var clickMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 412, Margin = new Padding(4) };
            clickMode.Items.AddRange(new object[] { "눌리기 · 달달 떨며 울기 · 폴짝 뛰기", "무작위 반응 (눌리기 · 울기 · 뛰기)", "눌리기만", "달달 떨며 울기만", "폴짝 뛰기만", "클릭 반응 끄기" });
            clickMode.SelectedIndex = (int)o.ClickMode; panel.Controls.Add(clickMode);
            panel.Controls.Add(FriendUI.Label("살짝 놓으면 헬리콥터, 휙 던지면 날아간 뒤 내려와요.\n저장한 속도와 클릭 반응은 다음 실행에도 유지돼요."));
            var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(18, 3, 18, 3), WrapContents = false };
            var save = FriendUI.Button("저장", delegate
            {
                o.Walls = walls.Checked; o.Ceiling = ceiling.Checked; o.Windows = windows.Checked; o.Idle = idle.Checked; o.ShortRest = shortRest.Checked;
                o.Escape = escape.Checked; o.Follow = follow.Checked; o.Focus = focus.Checked; o.WorkFocus = work.Checked; o.Fullscreen = full.Checked;
                o.ShowAffinity = affinity.Checked; o.PositionFixed = pinned.Checked; o.PetOpacityPercent = (int)opacity.Value;
                o.Speed = (double)speed.Value; o.FallSpeed = (double)fallSpeed.Value; o.SlideSpeed = (double)slideSpeed.Value;
                o.ClickMode = (ClickReaction)clickMode.SelectedIndex; o.EscapeDelay = (double)escapeDelay.Value;
                app.ApplyCharacterSettings();
                if (!o.Save()) MessageBox.Show(this, "설정을 파일에 저장하지 못했어. 지금 실행 중에는 적용돼.", "비키니친구들");
                Close();
            });
            row.Controls.Add(save);
            var cancel = FriendUI.Button("취소", delegate { Close(); }); row.Controls.Add(cancel);
            Controls.Add(row); AcceptButton = save; CancelButton = cancel;
        }
    }
}
