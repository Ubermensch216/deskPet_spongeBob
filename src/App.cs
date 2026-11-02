using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BikiniFriends
{
    /// <summary>실행 중 입력(키보드·마우스 클릭)이 있었는지 감시 — 정각 말풍선이 입력이 있을 때까지 남게 한다.</summary>
    internal static class ActivityInput
    {
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        private static readonly bool[] pressed = new bool[256];
        private static int generation;
        private static bool active;
        private static bool Read(int k) { try { return GetAsyncKeyState(k) < 0; } catch { return false; } }
        internal static int Start() { active = true; for (int i = 1; i < 255; i++) pressed[i] = Read(i); return generation; }
        internal static void Stop() { active = false; }
        internal static int Version()
        {
            if (!active) return generation;
            for (int i = 1; i < 255; i++) { bool p = Read(i); if (p && !pressed[i]) generation++; pressed[i] = p; }
            return generation;
        }
    }

    /// <summary>Windows 로그인 시 자동 시작 등록(HKCU Run).</summary>
    internal static class StartupOption
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run", ValueName = "BikiniFriends";
        internal static string Command() { return "\"" + Application.ExecutablePath + "\""; }
        internal static bool IsEnabled()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(KeyPath, false)) return k != null && k.GetValue(ValueName) is string; }
            catch { return false; }
        }
        internal static void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (k == null) throw new System.IO.IOException("자동 시작 등록 위치를 열 수 없어요.");
                    k.SetValue(ValueName, Command(), RegistryValueKind.String);
                }
                return;
            }
            using (var k = Registry.CurrentUser.OpenSubKey(KeyPath, true)) { if (k != null) k.DeleteValue(ValueName, false); }
        }
        internal static void RebindIfEnabled()
        {
            try
            {
                string cur; using (var k = Registry.CurrentUser.OpenSubKey(KeyPath, false)) cur = k != null ? k.GetValue(ValueName) as string : null;
                if (cur != null && !string.Equals(cur, Command(), StringComparison.OrdinalIgnoreCase)) SetEnabled(true);
            }
            catch { }
        }
    }

    internal sealed partial class App : ApplicationContext, IWorld
    {
        public const int MaxPets = 16;
        public const string Title = "비키니 친구들";

        private readonly List<PetWindow> windows = new List<PetWindow>();
        private readonly Random rng = new Random();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly System.Windows.Forms.Timer timer;
        private List<Rectangle> areas = new List<Rectangle>();
        private double lastTick, lastSlow, lastTop;
        private bool hidden, paused, locked, napping, autoHidden;
        private NotifyIcon tray;
        private SettingsForm settingsForm;
        internal NoteManager Notes;
        private double surfaceRefreshAt, forceVisibleUntil = 12;
        public Settings Settings;
        public Icon TrayIconImage;
        private DateTime lunchDone = DateTime.MinValue, leaveGatherDone = DateTime.MinValue, leaveDone = DateTime.MinValue;
        private double previewLeaveAt = -1; private string previewLeaveMsg;
        private DateTime hourlyLast;
        private string hourlyPendingText; private double hourlyPendingExpiry; private int hourlyActivityVersion;
        private HourlyNotice hourlyNotice;
        private AlertPetWindow alert;
        private PetWindow alertHidden;
        private double companionClock = -1, companionSavedAt, danceAwayAt, nextSecretDance;
        private bool danceAwaySeen;

        public List<Rectangle> Areas { get { return areas; } }
        public Random Rng { get { return rng; } }
        public double Now { get { return clock.Elapsed.TotalSeconds; } }
        internal List<PetWindow> LiveWindows { get { return windows; } }
        internal bool Paused { get { return paused; } }
        internal bool AllHidden { get { return hidden || autoHidden; } }
        internal ChatController Chat;

        public App(bool testMode = false)
        {
            Settings = Settings.Load();
            Chat = new ChatController(this);
            RefreshAreas();
            int[] roster = ParseRoster(Settings.Roster);
            TrayIconImage = CharacterArt.MakeIcon(roster.Length > 0 ? roster[0] : rng.Next(CharacterArt.Count));
            if (!testMode) BuildTray();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            timer = new System.Windows.Forms.Timer { Interval = 30 };
            timer.Tick += OnTick;
            lastTick = Now;
            timer.Start();
            loadingRoster = true;
            if (roster.Length == 0) Spawn(); else foreach (int c in roster.Take(MaxPets)) Spawn(c);
            loadingRoster = false; if (!testMode) SaveRoster();
            if (!testMode) StartupOption.RebindIfEnabled();
        }

        private static int[] ParseRoster(string text)
        {
            var list = new List<int>();
            foreach (string t in (text ?? "").Split(',')) { int v; if (int.TryParse(t.Trim(), out v) && v >= 0 && v < CharacterArt.Count) list.Add(v); }
            return list.ToArray();
        }

        private void SaveRoster()
        {
            Settings.Roster = string.Join(",", windows.Select(w => w.Pet.Art.Id.ToString()).ToArray());
            Settings.Save();
        }

        // ───────── 화면 영역·세션 ─────────
        private void RefreshAreas()
        {
            var list = new List<Rectangle>();
            try { foreach (Screen s in Screen.AllScreens) { var wa = s.WorkingArea; if (wa.Width > 0 && wa.Height > 0) list.Add(wa); } } catch { }
            if (list.Count == 0) list.Add(new Rectangle(0, 0, 1280, 720));
            areas = list;
            foreach (PetWindow window in windows)
            {
                Pet pet = window.Pet;
                if (!areas.Contains(pet.Area)) pet.Area = Pet.FindArea(this, pet.X, Math.Min(pet.Y, pet.Area.Bottom));
                if (pet.X < pet.Area.Left) pet.X = pet.Area.Left + 20;
                if (pet.X > pet.Area.Right) pet.X = pet.Area.Right - 20;
                if (pet.Y > pet.Area.Bottom && pet.State != PetState.Drag) pet.Y = pet.Area.Bottom;
            }
        }

        private void OnDisplayChanged(object sender, EventArgs e)
        { RefreshAreas(); if (PlayOptions.Current.PositionFixed) ApplyCharacterSettings(); }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect || e.Reason == SessionSwitchReason.RemoteDisconnect) locked = true;
            else if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect || e.Reason == SessionSwitchReason.RemoteConnect) locked = false;
        }

        // ───────── 친구 부르기·보내기·숨기기 ─────────
        public void Spawn(int character = -1)
        {
            if (windows.Count >= MaxPets) return;
            if (character < 0) character = rng.Next(CharacterArt.Count);
            Rectangle area;
            try { area = Screen.PrimaryScreen.WorkingArea; } catch { area = areas[0]; }
            CharacterArt art = CharacterArt.Get(character);
            double w = art.W;
            double x = area.Left + w + rng.NextDouble() * Math.Max(1, area.Width - w * 2);
            double y = area.Top + art.H * .6;
            var pet = new Pet(this, character, x, y) { Area = area };
            if (PlayOptions.Current.PositionFixed && windows.Count == 0) pet.RestorePinned(this); else pet.SyncPinned(this);
            pet.Hidden = hidden || autoHidden;
            var window = new PetWindow(this, pet);
            windows.Add(window);
            if (!hidden && !autoHidden) { window.Show(); window.Present(Now); }
            if (!loadingRoster) SaveRoster();
        }
        private bool loadingRoster;

        public void Remove(PetWindow w)
        {
            windows.Remove(w);
            try { w.Close(); w.Dispose(); } catch { }
            SaveRoster();
        }

        public void BringToFront(PetWindow w) { w.KeepOnTop(); }

        public void ToggleHidden()
        {
            hidden = !hidden;
            foreach (PetWindow window in windows)
            {
                window.Pet.Hidden = hidden || autoHidden;
                if (hidden || autoHidden) { window.Hide(); continue; }
                window.Show(); window.Invalidate2(); window.Present(Now); window.KeepOnTop();
            }
            if (!hidden && windows.Count == 0) Spawn();
            lastTick = Now;
        }

        /// <summary>화면 가운데 위에서 다시 헬리콥터로 내려오게 한다 (숨은 친구 구조).</summary>
        internal void RecoverVisible()
        {
            hidden = false; autoHidden = false; forceVisibleUntil = Now + 15;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            if (windows.Count == 0) Spawn();
            int index = 0;
            foreach (var w in windows)
            {
                w.Pet.ReleaseFeatures(this); w.Pet.Area = area; w.Pet.Hidden = false;
                w.Pet.X = area.Left + area.Width / 2.0 + (index++ - (windows.Count - 1) / 2.0) * w.Pet.Art.W;
                w.Pet.Y = area.Top + w.Pet.HeliH + 10; w.Pet.Vy = 0; w.Pet.SetState(PetState.Falling, this);
                w.Pet.SyncPinned(this);
                w.Invalidate2(); w.Show(); w.Present(Now); w.KeepOnTop();
            }
            RememberPinnedPosition(); lastTick = Now;
        }

        // ───────── 메뉴 ─────────
        private ContextMenuStrip BuildMenu(PetWindow w)
        {
            Pet pet = w != null ? w.Pet : null;
            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("말 걸기  (AI 대화)", null, delegate { BeginInvokeSafe(delegate { StartChat(pet); }); }) { Font = new Font(menu.Font, FontStyle.Bold), Enabled = ChatOptions.Current.Enabled, ShortcutKeyDisplayString = ChatOptions.Current.Hotkey ? ChatOptions.Current.HotkeyText : null });
            menu.Items.Add(ModeMenu());
            menu.Items.Add("비키니 친구들의 일기장...", null, delegate { BeginInvokeSafe(ShowDiary); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("♥ 친밀도 " + FriendData.Current.Affinity.ToString("N0") + "  ·  함께 " + FriendData.FormatTogether(FriendData.Current.TogetherSeconds)) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("간식 주기  ·  포만감 " + (int)Math.Floor(FriendData.Current.Satiety) + "/100", null, delegate { FeedFirst(pet); }));
            menu.Items.Add(DemoMenu(pet));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(FriendsMenu(w));
            menu.Items.Add(QuickMenu());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(NewNoteItem());
            menu.Items.Add("설정...", null, delegate { OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, delegate { Quit(); });
            return menu;
        }

        private ToolStripMenuItem DemoMenu(Pet pet)
        {
            var demo = new ToolStripMenuItem("동작 바로 보기");
            string hobbyName = pet != null ? pet.Art.Info.Hobby : "대표 동작";
            var items = new[] {
                new[] { "앉아서 쉬기", "sit" },
                new[] { "★ 대표 동작: " + hobbyName, "hobby" },
                new[] { "잠자기", "sleep" },
                new[] { "멍때리기", "daydream" },
                new[] { "기지개", "stretch" },
                new[] { "클릭할 때 달달 떨며 우는 모습", "cry" },
                new[] { "배고플 때 우는 모습", "weep" },
                new[] { "폴짝 뛰기", "hop" },
                new[] { "슬라이딩", "slide" },
                new[] { "어질어질", "dizzy" },
                new[] { "헬리콥터 낙하", "fall" },
                new[] { "벽 타기", "wall" },
                new[] { "천장 타기", "ceiling" },
                new[] { "창 타기", "window" }
            };
            if (pet != null && (pet.Art.Id == 0 || pet.Art.Id == 3))
                items = items.Concat(new[] { new[] { "★ 대표 동작 두 번째: " + (pet.Art.Id == 0 ? "비눗방울 불기" : "올가미 돌리기"), "hobby2" } }).ToArray();
            foreach (var pair in items)
            {
                string key = pair[1];
                demo.DropDownItems.Add(pair[0], null, delegate {
                    Pet p = pet ?? windows.Select(a => a.Pet).FirstOrDefault();
                    if (p != null) p.Demonstrate(this, key);
                });
            }
            demo.DropDownItems.Add("몰래 하는 군무", null, delegate { DemonstrateSecretDance(pet); });
            return demo;
        }

        private ToolStripMenuItem FriendsMenu(PetWindow w)
        {
            var friends = new ToolStripMenuItem("친구");
            friends.DropDownItems.Add(SpawnMenu("한 마리 더 부르기"));
            if (w != null) friends.DropDownItems.Add("이 친구 보내기", null, delegate { Remove(w); });
            friends.DropDownItems.Add(hidden ? "모두 보이기" : "모두 숨기기", null, delegate { ToggleHidden(); });
            friends.DropDownItems.Add("화면 가운데로 다시 부르기", null, delegate { RecoverVisible(); });
            return friends;
        }

        private ToolStripMenuItem QuickMenu()
        {
            var quick = new ToolStripMenuItem("빠른 설정");
            var o = PlayOptions.Current;
            Func<string, Func<bool>, Action<bool>, ToolStripMenuItem> toggle = (text, get, set) =>
            {
                var item = new ToolStripMenuItem(text) { Checked = get(), CheckOnClick = false };
                item.Click += delegate { set(!get()); item.Checked = get(); };
                return item;
            };
            quick.DropDownItems.Add(toggle("친구 위치 고정 (자동 이동 멈추기)", () => o.PositionFixed, v => SetPositionFixed(v)));
            quick.DropDownItems.Add(toggle("얌전 모드 (바닥에서만)", () => o.Focus, v => { o.Focus = v; o.Save(); }));
            quick.DropDownItems.Add(toggle("친밀도 숫자 표시", () => o.ShowAffinity, v => { o.ShowAffinity = v; o.Save(); ApplyCharacterSettings(); }));
            return quick;
        }

        private ToolStripMenuItem SpawnMenu(string text)
        {
            var item = new ToolStripMenuItem(text) { Enabled = windows.Count < MaxPets };
            item.DropDownItems.Add("무작위", null, delegate { SpawnFromUi(-1); });
            for (int i = 0; i < CharacterArt.Count; i++)
            { int id = i; item.DropDownItems.Add(CharacterArt.Infos[i].Name, null, delegate { SpawnFromUi(id); }); }
            return item;
        }

        private void BuildTray()
        {
            var menu = BuildMenu(null);
            menu.Opening += delegate
            {
                var fresh = BuildMenu(null);
                menu.Items.Clear();
                foreach (ToolStripItem i in fresh.Items.Cast<ToolStripItem>().ToList()) menu.Items.Add(i);
            };
            tray = new NotifyIcon { Icon = TrayIconImage, Text = "비키니 친구들", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ToggleHidden(); };
        }

        public void ShowPetMenu(PetWindow w, Point at)
        {
            var menu = BuildMenu(w);
            menu.Closed += delegate { BeginInvokeSafe(delegate { menu.Dispose(); }); };
            try { Native.SetForegroundWindow(w.Handle); } catch { }
            menu.Show(at);
        }

        private void BeginInvokeSafe(Action a)
        {
            var t = new System.Windows.Forms.Timer { Interval = 50 };
            t.Tick += delegate { t.Stop(); t.Dispose(); try { a(); } catch { } };
            t.Start();
        }

        // ───────── AI 대화 ─────────
        /// <summary>친구에게 말 걸기. pet 이 없으면(트레이 메뉴) 보이는 첫 친구와.</summary>
        internal void StartChat(Pet pet)
        {
            if (hidden) ToggleHidden();
            Pet p = pet ?? (Chat.Pet != null ? Chat.Pet : windows.Select(w => w.Pet).FirstOrDefault(x => !x.Hidden) ?? windows.Select(w => w.Pet).FirstOrDefault());
            if (p != null) Chat.Begin(p);
        }

        internal void StartChatFromHotkey() { StartChat(null); }

        /// <summary>대화 모드 ▶ 친구 · 공감 · 설명 · 장난 · 간결 (지금 모드에 체크).</summary>
        private ToolStripMenuItem ModeMenu()
        {
            var root = new ToolStripMenuItem("대화 모드  ·  " + ChatMode.Current.Name) { Enabled = ChatOptions.Current.Enabled };
            for (int i = 0; i < ChatMode.All.Length; i++)
            {
                int index = i; var mode = ChatMode.All[i];
                root.DropDownItems.Add(new ToolStripMenuItem(mode.Name + "  —  " + mode.Desc, null, delegate { SetChatMode(index); }) { Checked = index == ChatOptions.Current.Mode });
            }
            return root;
        }

        /// <summary>대화 모드를 바꾸고 저장한다. 말풍선으로 바뀐 걸 알려 준다(바꾼 다음 말부터 적용).</summary>
        internal void SetChatMode(int index)
        {
            var o = ChatOptions.Current; o.Mode = index; o.Save(); Chat.ModeChanged();
            Pet p = Chat.Pet ?? windows.Select(w => w.Pet).FirstOrDefault(x => !x.Hidden);
            if (p != null) { p.Bubble = ChatMode.At(index).Name + " 모드로 얘기할게!"; p.BubbleUntil = Now + 3.5; }
        }

        private ChatDiaryForm diaryForm;
        /// <summary>친구들이 대화 끝에 한 줄씩 적어 둔 일기장 창(하나만 연다).</summary>
        internal void ShowDiary()
        {
            if (diaryForm != null && !diaryForm.IsDisposed) { diaryForm.Show(); diaryForm.Activate(); return; }
            diaryForm = new ChatDiaryForm(TrayIconImage); diaryForm.FormClosed += delegate { diaryForm = null; }; diaryForm.Show();
        }

        // ───────── 설정창에서 쓰는 동작 ─────────
        internal void SpawnFromUi(int character) { if (hidden) ToggleHidden(); Spawn(character); }
        internal void RemoveByIndex(int index) { if (index >= 0 && index < windows.Count) Remove(windows[index]); }
        internal void FeedFirst(Pet pet = null) { Pet p = pet ?? windows.Select(a => a.Pet).FirstOrDefault(); if (p != null) p.Feed(this); }
        /// <summary>메뉴에는 "새 메모" 한 줄만 둔다(깊이를 줄이려고). 라이브러리는 메모 위젯의 ⋯ 메뉴와 Ctrl+Shift+F 로 연다.</summary>
        private ToolStripMenuItem NewNoteItem()
        {
            return new ToolStripMenuItem("새 메모", null, delegate { if (Notes != null) Notes.CreateNote(); }) { ShortcutKeyDisplayString = "Ctrl+Alt+N" };
        }

        internal void ResetAllSettings()
        {
            PlayOptions.Current = new PlayOptions(); PlayOptions.Current.Save();
            ChatOptions.Current = new ChatOptions(); ChatOptions.Current.Save(); Chat.ApplyOptions();
            string roster = Settings.Roster; Settings = new Settings { Roster = roster }; Settings.Save();
            try { StartupOption.SetEnabled(false); } catch { }
            OnSettingsChanged(); ApplyCharacterSettings();
        }

        // ───────── 창 열기 ─────────
        internal void OpenSettings() { OpenSettingsPage(-1); }
        internal void OpenSettingsPage(int page)
        {
            if (settingsForm != null && !settingsForm.IsDisposed) { if (page >= 0) settingsForm.SelectPage(page); settingsForm.Activate(); return; }
            settingsForm = new SettingsForm(this, page); settingsForm.FormClosed += delegate { settingsForm = null; };
            settingsForm.Show(); settingsForm.Activate();
        }
        internal void ShowPlaySettings() { OpenSettingsPage(1); }
        internal void ShowFriendRecords() { OpenSettingsPage(3); }
        internal void ShowFriendPanel() { OpenSettingsPage(3); }
        internal void CloseFeatureWindows()
        {
            FriendData.Current.Save();
            if (diaryForm != null && !diaryForm.IsDisposed) diaryForm.Close();
            if (settingsForm != null) settingsForm.Close();
        }

        internal void Announce(string text)
        { foreach (var w in windows) { w.Pet.Bubble = text; w.Pet.BubbleUntil = Now + 5; } }

        internal void ApplyCharacterSettings()
        {
            foreach (var window in windows) { window.Pet.SyncPinned(this); window.Invalidate2(); window.Present(Now); }
            RememberPinnedPosition();
        }
        internal void SetPositionFixed(bool enabled)
        { PlayOptions.Current.PositionFixed = enabled; ApplyCharacterSettings(); PlayOptions.Current.Save(); }
        internal void RememberPinnedPosition()
        {
            if (!PlayOptions.Current.PositionFixed || windows.Count == 0) return;
            PlayOptions.Current.FixedX = windows[0].Pet.X; PlayOptions.Current.FixedY = windows[0].Pet.Y; PlayOptions.Current.Save();
        }

        // ───────── 알림 ─────────
        public void OnSettingsChanged()
        {
            ClearBubbleHourly();
            DateTime now = DateTime.Now;
            TimeSpan? lunch = Settings.ParseTime(Settings.LunchTime);
            if (lunch.HasValue && now.TimeOfDay > lunch.Value.Add(TimeSpan.FromSeconds(60))) lunchDone = now.Date;
            else if (lunchDone == now.Date) lunchDone = DateTime.MinValue;
            TimeSpan? leave = Settings.ParseTime(Settings.LeaveTime);
            if (leave.HasValue && now.TimeOfDay > leave.Value.Add(TimeSpan.FromSeconds(60))) { leaveDone = now.Date; leaveGatherDone = now.Date; }
            else if (leaveDone == now.Date) { leaveDone = DateTime.MinValue; leaveGatherDone = DateTime.MinValue; }
        }

        public void PreviewLunch(string msg)
        {
            if (hidden) ToggleHidden();
            if (windows.Count == 0) Spawn();
            NoticeAll(!string.IsNullOrEmpty(msg) ? msg : Settings.LunchMsg);
        }

        public void PreviewLeave(string msg)
        {
            if (hidden) ToggleHidden();
            if (windows.Count == 0) Spawn();
            previewLeaveAt = Now + 10; previewLeaveMsg = !string.IsNullOrEmpty(msg) ? msg : Settings.LeaveMsg;
            StartGather(previewLeaveAt);
        }

        private void StartGather(double deadline)
        {
            foreach (var group in windows.Select(w => w.Pet).Where(p => p.CanReact).GroupBy(p => p.Area))
            {
                var list = group.OrderBy(p => p.X).ToList();
                double gap = list.Max(p => p.Art.W) * .95, center = group.Key.Left + group.Key.Width / 2.0;
                for (int i = 0; i < list.Count; i++) list[i].Gather(this, center + (i - (list.Count - 1) / 2.0) * gap, deadline);
            }
        }

        private void CheerAll(string msg) { foreach (var w in windows) w.Pet.Cheer(this, msg, 8); }

        /// <summary>점심·퇴근 같은 알림: 큰 친구로 보여 주거나(기본) 친구들의 말풍선으로 알린다.</summary>
        private void NoticeAll(string msg)
        {
            if (Settings.AlertPet && !string.IsNullOrWhiteSpace(msg)) ShowAlert(msg); else CheerAll(msg);
        }

        private void CheckAlarms(double now)
        {
            if (previewLeaveAt > 0 && now >= previewLeaveAt) { previewLeaveAt = -1; NoticeAll(previewLeaveMsg); }
            DateTime dt = DateTime.Now; TimeSpan tod = dt.TimeOfDay; DateTime date = dt.Date;
            if (Settings.LunchOn && lunchDone != date)
            {
                TimeSpan? t = Settings.ParseTime(Settings.LunchTime);
                if (t.HasValue && tod >= t.Value && tod < t.Value + TimeSpan.FromSeconds(60)) { lunchDone = date; napping = false; NoticeAll(string.IsNullOrWhiteSpace(Settings.LunchMsg) ? "점심시간이야!" : Settings.LunchMsg); }
            }
            if (Settings.LeaveOn)
            {
                TimeSpan? t = Settings.ParseTime(Settings.LeaveTime);
                if (t.HasValue)
                {
                    TimeSpan gather = t.Value - TimeSpan.FromSeconds(10);
                    if (leaveGatherDone != date && tod >= gather && tod < t.Value)
                    { leaveGatherDone = date; napping = false; StartGather(now + (t.Value - tod).TotalSeconds); }
                    if (leaveDone != date && tod >= t.Value && tod < t.Value + TimeSpan.FromSeconds(60))
                    { leaveDone = date; leaveGatherDone = date; napping = false; NoticeAll(string.IsNullOrWhiteSpace(Settings.LeaveMsg) ? "퇴근 시간이야!" : Settings.LeaveMsg); }
                }
            }
            if (OptionsHourlyDue(dt))
            {
                napping = false;
                BeginHourly(HourlyMessage(dt, Settings.LunchOn, Settings.LunchTime, Settings.LunchMsg, Settings.LeaveOn, Settings.LeaveTime, Settings.LeaveMsg));
            }
        }

        public static string HourlyText(DateTime now)
        {
            int h = now.Hour % 12; if (h == 0) h = 12;
            return "지금은 " + (now.Hour >= 12 ? "오후 " : "오전 ") + h + "시야!";
        }

        public static bool AlarmAtHour(DateTime now, bool enabled, string time)
        { TimeSpan r; return enabled && TimeSpan.TryParse(time, out r) && r.Ticks == now.Hour * 36000000000L; }

        public static string HourlyMessage(DateTime now, bool lunchOn, string lunchTime, string lunchMsg, bool leaveOn, string leaveTime, string leaveMsg)
        {
            string text = HourlyText(now);
            if (AlarmAtHour(now, lunchOn, lunchTime) && !string.IsNullOrEmpty(lunchMsg)) text += "\n" + lunchMsg;
            if (AlarmAtHour(now, leaveOn, leaveTime) && !string.IsNullOrEmpty(leaveMsg)) text += "\n" + leaveMsg;
            return text;
        }

        internal bool OptionsHourlyDue(DateTime now)
        {
            if (now.Minute != 0) return false;
            DateTime h = now.AddTicks(-(now.Ticks % 36000000000L));
            if (hourlyLast == h) return false;
            hourlyLast = h;
            return Settings.HourAllowed(now);
        }

        internal void BeginHourly(string message)
        { if (Settings.HourlyWindow) ShowHourWindow(message); else BeginBubbleHourly(message); }

        internal void ShowHourWindow(string message)
        {
            ClearBubbleHourly();
            if (Settings.AlertPet) { ShowAlert(message); return; }
            if (hourlyNotice == null || hourlyNotice.IsDisposed)
            {
                hourlyNotice = new HourlyNotice(TrayIconImage);
                hourlyNotice.SetMessage(message);
                hourlyNotice.EnableEdgeGlow(Settings.EdgeGlow, Settings.EdgeGlowTheme, Settings.EdgeGlowWidth, Settings.EdgeGlowStrength);
                hourlyNotice.Show();
            }
            else
            {
                hourlyNotice.SetMessage(message);
                hourlyNotice.EnableEdgeGlow(Settings.EdgeGlow, Settings.EdgeGlowTheme, Settings.EdgeGlowWidth, Settings.EdgeGlowStrength);
                hourlyNotice.Show(); hourlyNotice.BringToFront();
            }
        }

        /// <summary>
        /// 알림(정각·점심·퇴근·메모)을 알리는 모습: 친구가 2배 크기로 화면 가운데에 나와 실루엣에 네온 빛을 두르고,
        /// 큰 글씨 말풍선으로 내용을 말한다. 눌러서 닫을 때까지 남는다. 이미 떠 있으면 말풍선에 이어 붙인다.
        /// </summary>
        internal AlertPetWindow ShowAlert(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return alert;
            bool fresh = alert == null || alert.IsDisposed;
            if (fresh)
            {
                alert = new AlertPetWindow(this, windows.Count > 0 ? windows[0].Pet.Art.Id : 0);
                alertHidden = windows.FirstOrDefault();
                alert.FormClosed += OnAlertClosed;
            }
            alert.AddMessage(message);
            if (fresh)
            {
                if (alertHidden != null) { alertHidden.Suppressed = true; alertHidden.Hide(); }
                alert.Begin();
            }
            return alert;
        }

        private void OnAlertClosed(object sender, FormClosedEventArgs e)
        {
            if (ReferenceEquals(sender, alert)) alert = null;
            PetWindow back = alertHidden; alertHidden = null;
            if (back == null) return;
            back.Suppressed = false;
            if (windows.Contains(back) && !hidden && !autoHidden) { back.Show(); back.Invalidate2(); back.Present(Now); back.KeepOnTop(); }
        }

        internal void BeginBubbleHourly(string message)
        {
            hourlyPendingText = message; hourlyPendingExpiry = double.PositiveInfinity; hourlyActivityVersion = ActivityInput.Start();
            CheerAll(message); MaintainBubbleHourly(Now);
        }

        internal void MaintainBubbleHourly(double now)
        {
            if (hourlyPendingText == null) return;
            if (!Settings.HourlyOn) { ClearBubbleHourly(); return; }
            if (now >= hourlyPendingExpiry) { ClearBubbleHourly(); return; }
            if (double.IsPositiveInfinity(hourlyPendingExpiry))
            {
                DateTime n = DateTime.Now; DateTime h = n.AddTicks(-(n.Ticks % 36000000000L));
                if (hourlyLast != DateTime.MinValue && hourlyLast != h)
                {
                    hourlyLast = h;
                    BeginBubbleHourly(HourlyMessage(n, Settings.LunchOn, Settings.LunchTime, Settings.LunchMsg, Settings.LeaveOn, Settings.LeaveTime, Settings.LeaveMsg));
                    return;
                }
                if (ActivityInput.Version() != hourlyActivityVersion) { hourlyPendingExpiry = now + 8; ActivityInput.Stop(); }
            }
            foreach (var w in windows) w.Pet.hourlyBubble = hourlyPendingText;
        }

        internal void ClearBubbleHourly()
        {
            ActivityInput.Stop();
            foreach (var w in windows) { w.Pet.hourlyBubble = null; if (w.Pet.Bubble == hourlyPendingText) w.Pet.Bubble = null; }
            hourlyPendingText = null; hourlyPendingExpiry = 0;
        }

        internal void MaintainHourly(double now)
        {
            if (Settings.HourlyWindow) return;
            if (!Settings.HourAllowed(DateTime.Now)) ClearBubbleHourly(); else MaintainBubbleHourly(now);
        }

        internal void ClearHourly()
        {
            ClearBubbleHourly();
            if (hourlyNotice != null && !hourlyNotice.IsDisposed) hourlyNotice.Close();
            hourlyNotice = null;
            if (alert != null && !alert.IsDisposed) alert.Close();
            alert = null;
        }

        // ───────── 틱 ─────────
        private void OnTick(object sender, EventArgs e)
        {
            double now = Now, dt = now - lastTick; lastTick = now;
            if (dt > .1) dt = .1; if (dt < 0) dt = 0;
            if (now - lastSlow >= 1) { lastSlow = now; SlowTick(now); }
            CompanionTick(now, !paused);
            try { Chat.Tick(now); } catch (Exception error) { FeatureLog.Write(error); }
            if (!paused) FriendData.Current.PollTimer(this);
            if (paused || hidden) return;
            CheckAlarms(now);
            MaintainHourly(now);
            if (!FeatureTick(now)) return;
            var all = windows.Select(w => w.Pet).ToList();
            double mouseX = DesktopSurfaces.Mouse.X;
            var old = all.ToDictionary(p => p, p => new PointF((float)p.X, (float)p.Y));
            foreach (PetWindow w in windows.ToArray())
            {
                try { w.Pet.Update(this, dt, all, mouseX); } catch (Exception error) { FeatureLog.Write(error); }
            }
            ResolveCompanionContacts(this, all, old);
            CheckSecretDance(now, all);
            foreach (PetWindow w in windows.ToArray()) { try { w.Present(now); } catch (Exception error) { FeatureLog.Write(error); } }
            if (now - lastTop < 2) return;
            lastTop = now;
            foreach (var w in windows) w.KeepOnTop();
        }

        private bool FeatureTick(double now)
        {
            if (now - surfaceRefreshAt > .35) { surfaceRefreshAt = now; DesktopSurfaces.Refresh(); }
            if (hidden || paused) return false;
            bool hide = now >= forceVisibleUntil && PlayOptions.Current.Fullscreen && DesktopSurfaces.FullscreenActive();
            if (hide != autoHidden)
            {
                autoHidden = hide;
                foreach (var w in windows)
                {
                    w.Pet.Hidden = hide;
                    if (hide) w.Hide(); else { w.Show(); w.Invalidate2(); w.Present(now); w.KeepOnTop(); }
                }
            }
            return !autoHidden;
        }

        private void SlowTick(double now)
        {
            bool saver = Native.ScreenSaverRunning(), was = paused;
            paused = locked || saver;
            if (was && !paused) lastTick = Now;
            RefreshAreas();
            if (paused || hidden) return;
            double idle = Native.IdleSeconds();
            if (Settings.NapOn && idle >= Settings.NapMinutes * 60.0)
            { napping = true; foreach (var w in windows) w.Pet.FallAsleep(this); }
            else if (idle < 2 && napping)
            { napping = false; foreach (var w in windows) w.Pet.WakeUp(this); }
        }

        // ───────── 함께한 시간·꽃다발·군무·충돌 ─────────
        private void CompanionTick(double now, bool active)
        {
            double dt = companionClock < 0 ? 0 : now - companionClock; companionClock = now;
            if (active && dt > 0 && dt < 2) FriendData.Current.AdvanceLife(dt);
            if (now - companionSavedAt >= 30) { companionSavedAt = now; FriendData.Current.Save(); }
            if (!active || hidden || autoHidden) return;
            var pet = windows.Select(w => w.Pet).FirstOrDefault(p => !p.Hidden && p.IsCompanionFloorPose);
            if (pet != null && FriendData.Current.TakeMilestone()) pet.StartFlower(this, FriendData.Current.CelebratedHundreds * 100);
        }

        private void CheckSecretDance(double now, List<Pet> all)
        {
            Point mouse = DesktopSurfaces.Mouse;
            var eligible = all.Where(p => p.CanDance(now)).ToList();
            if (!PlayOptions.Current.Secrets || PlayOptions.IsFocus(DateTime.Now) || eligible.Count < 2) { danceAwaySeen = false; return; }
            if (eligible.Any(p => Math.Abs(mouse.X - p.X) < p.Art.W * 1.6 && Math.Abs(mouse.Y - (p.Y - p.Art.H * .5)) < p.Art.H * 1.5)) { danceAwaySeen = false; return; }
            if (!danceAwaySeen) { danceAwayAt = now; danceAwaySeen = true; }
            if (now < nextSecretDance || now - danceAwayAt < 4) return;
            foreach (var group in eligible.GroupBy(p => new { p.Area, p.SupportWindow }))
            {
                var pets = group.OrderBy(p => p.X).ToList();
                for (int i = 0; i + 1 < pets.Count; i++)
                {
                    var dancers = pets.Skip(i).TakeWhile(p => p.X - pets[i].X <= p.Art.W * 8).Where(p => Math.Abs(p.Y - pets[i].Y) < p.Art.H * .4).ToList();
                    if (dancers.Count < 2) continue;
                    BeginSecretDance(dancers, now); return;
                }
            }
        }

        private void BeginSecretDance(List<Pet> dancers, double now)
        {
            foreach (var p in dancers) p.StartSecretDance(this, now);
            nextSecretDance = now + 45; danceAwaySeen = false; FriendData.Current.Discover("dance");
        }

        internal void DemonstrateSecretDance(Pet selected)
        {
            if (selected == null) selected = windows.Select(w => w.Pet).FirstOrDefault(p => !p.Hidden);
            if (selected == null) return;
            var dancers = windows.Select(w => w.Pet).Where(p => p.CanDance(Now) && p.Area == selected.Area && p.SupportWindow == selected.SupportWindow).ToList();
            if (dancers.Count < 2) { selected.Bubble = "친구 둘이 바닥에 모이면 춤출 수 있어 ♪"; selected.BubbleUntil = Now + 4; return; }
            BeginSecretDance(dancers, Now);
        }

        internal static void ResolveCompanionContacts(IWorld world, List<Pet> all, Dictionary<Pet, PointF> oldPositions)
        {
            if (PlayOptions.Current.PositionFixed) return;
            var live = all.Where(p => !p.Hidden).ToArray();
            float S = CharacterArt.Scale;
            for (int pass = 0; pass < Math.Max(8, live.Length * 3); pass++)
            {
                bool moved = false;
                for (int i = 0; i < live.Length; i++)
                    for (int j = i + 1; j < live.Length; j++)
                    {
                        Pet a = live[i], b = live[j]; RectangleF ra = a.ContactBody(world.Now), rb = b.ContactBody(world.Now);
                        bool touching = ra.IntersectsWith(rb);
                        PointF oa, ob;
                        double hh = Math.Min(a.Art.H, b.Art.H) * .25;
                        bool crossed = pass == 0 && oldPositions != null && oldPositions.TryGetValue(a, out oa) && oldPositions.TryGetValue(b, out ob)
                            && Math.Abs(a.Y - b.Y) < hh && Math.Abs(oa.Y - ob.Y) < hh && (oa.X - ob.X) * (a.X - b.X) < 0;
                        if (!touching && !crossed) continue;
                        float overlapY = Math.Min(ra.Bottom, rb.Bottom) - Math.Max(ra.Top, rb.Top);
                        bool wallA = a.State == PetState.Wall || a.State == PetState.WindowClimb, wallB = b.State == PetState.Wall || b.State == PetState.WindowClimb;
                        bool vertical = !crossed && ((wallA && wallB) || (wallA && b.State == PetState.Drag) || (wallB && a.State == PetState.Drag));
                        int sign = a.X < b.X ? -1 : 1;
                        if (crossed && oldPositions[a].X != oldPositions[b].X) sign = oldPositions[a].X < oldPositions[b].X ? -1 : 1;
                        if (vertical)
                        {
                            double distance = overlapY + 3 * S; sign = a.Y < b.Y ? -1 : 1;
                            if (a.State != PetState.Drag) a.Y += sign * distance * (b.State == PetState.Drag ? 1 : .5);
                            if (b.State != PetState.Drag) b.Y -= sign * distance * (a.State == PetState.Drag ? 1 : .5);
                            a.ClampWallContact(); b.ClampWallContact();
                        }
                        else
                        {
                            double needed = (ra.Width + rb.Width) / 2.0 + 3 * S, mid = (a.X + b.X) * .5;
                            if (a.State == PetState.Drag || wallA) b.X = a.X - sign * needed;
                            else if (b.State == PetState.Drag || wallB) a.X = b.X + sign * needed;
                            else { a.X = mid + sign * needed / 2; b.X = mid - sign * needed / 2; }
                            a.ClampContact(); b.ClampContact();
                            // 한쪽이 화면 끝에 막혀 못 밀려났으면 남은 간격을 반대쪽이 마저 물러난다(끝에 낀 채 계속 엉키는 것 방지)
                            Pet left = sign < 0 ? a : b, right = sign < 0 ? b : a;
                            double shortfall = needed - (right.X - left.X);
                            if (shortfall > .5)
                            {
                                if (right.CanBeShoved) { right.X += shortfall; right.ClampContact(); shortfall = needed - (right.X - left.X); }
                                if (shortfall > .5 && left.CanBeShoved) { left.X -= shortfall; left.ClampContact(); }
                            }
                        }
                        if (pass == 0) { if (a.State != PetState.Drag) a.Bump(world, sign); if (b.State != PetState.Drag) b.Bump(world, -sign); }
                        moved = true;
                    }
                if (!moved) break;
            }
        }

        public void Quit()
        {
            Chat.Dispose();
            if (Notes != null) Notes.Suspend();
            CloseFeatureWindows(); ClearHourly(); ActivityInput.Stop();
            SaveRoster();
            timer.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; SystemEvents.SessionSwitch -= OnSessionSwitch;
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            foreach (PetWindow w in windows.ToArray()) { try { w.Close(); w.Dispose(); } catch { } }
            windows.Clear();
            ExitThread();
        }
    }
}
