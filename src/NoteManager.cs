using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BikiniFriends
{
    /// <summary>
    /// 바탕화면의 메모 창들을 거느리고 저장소와 맞춘다(deskNote 의 NoteWindowManager).
    /// 창이 바뀐 것을 저장으로 옮기는 일은 전부 여기를 지난다 — 그래서 "창을 닫으면 메모는 숨을 뿐 지워지지 않고,
    /// 삭제는 메뉴로만, 끌어서 옮기면 위치만 저장" 같은 몸짓의 뜻을 정하는 곳이 한 군데다.
    /// </summary>
    internal sealed class NoteManager
    {
        private readonly App app;
        private readonly Dictionary<Guid, NoteWindow> windows = new Dictionary<Guid, NoteWindow>();
        private readonly Dictionary<Guid, DateTime> pending = new Dictionary<Guid, DateTime>();
        private readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 100 };
        private readonly System.Windows.Forms.Timer reminderTimer = new System.Windows.Forms.Timer { Interval = 30000 };
        private NoteLibraryForm library; private NoteHistoryForm history; private ReminderForm reminderForm;
        private HotkeyWindow hotkeys;
        private NoteStore store;
        private int cascade, ticks; private bool suspending;

        internal static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(300);

        internal NoteManager(App app) { this.app = app; }
        internal NoteStore Store { get { return store; } }
        internal Icon AppIcon { get { return app.TrayIconImage; } }
        internal NoteWindow ActiveNote { get; private set; }
        internal int OpenWindowCount { get { return windows.Count; } }
        internal bool IsOpen(Guid id) { return windows.ContainsKey(id); }
        internal NoteWindow WindowOf(Guid id) { NoteWindow w; return windows.TryGetValue(id, out w) ? w : null; }

        // ───────── 새 메모의 기본 모습 ─────────
        internal string DefaultColor { get { return NoteColors.Normalize(store.GetSetting("note.defaultColor")); } set { store.SetSetting("note.defaultColor", NoteColors.Normalize(value)); } }
        internal NoteSizePreset DefaultSize
        {
            get { NoteSizePreset p; return Enum.TryParse(store.GetSetting("note.defaultSize"), true, out p) && (p == NoteSizePreset.Small || p == NoteSizePreset.Medium || p == NoteSizePreset.Large) ? p : NoteSizePreset.Medium; }
            set { store.SetSetting("note.defaultSize", (value == NoteSizePreset.Small || value == NoteSizePreset.Large ? value : NoteSizePreset.Medium).ToString()); }
        }

        // ───────── 시작 · 끝 ─────────

        /// <summary>
        /// 켤 때: 저장소를 열고, 도우미에 있던 옛 메모 · 할 일을 메모 한 장으로 옮기고, 열려 있던 메모들을 되살린 뒤,
        /// 아무것도 안 떴으면 마지막 메모(없으면 빈 메모)를 띄운다. 알림 확인과 단축키는 그 다음에 시작한다.
        /// </summary>
        internal void Start(bool restoreWindows = true)
        {
            store = new NoteStore(NoteStore.DefaultRoot); store.Load(); NoteStore.Current = store;
            foreach (string problem in store.LoadProblems) FeatureLog.Write(new Exception("메모 파일을 읽지 못했어요: " + problem));
            MigrateHelperData();
            tick.Tick += delegate { OnTick(); }; tick.Start();
            if (restoreWindows)
            {
                foreach (var note in store.OpenNotes()) Show(note);
                switch (StartupNotePolicy.Decide(windows.Count, store.Count))
                {
                    case StartupNoteAction.CreateFirstNote: CreateNote(); break;
                    case StartupNoteAction.OpenMostRecent: var recent = store.Query(null, null, null, NoteSort.UpdatedDescending, 1).FirstOrDefault(); if (recent != null) Focus(recent.Id); break;
                }
                reminderTimer.Tick += delegate { CheckReminders(); }; reminderTimer.Start(); CheckReminders();
                hotkeys = new HotkeyWindow(this);
            }
        }

        /// <summary>옛 도우미의 메모와 할 일(체크 목록)을 한 번만 메모로 옮긴다. 도우미는 없어졌지만 쓰던 글은 남아야 한다.</summary>
        private void MigrateHelperData()
        {
            if (store.GetSetting("migrated.helper") == "1") return;
            var d = FriendData.Current; string notes = (d.Notes ?? "").Trim();
            if (notes.Length > 0 || d.Todos.Count > 0)
            {
                var body = new System.Text.StringBuilder("# 도우미에서 가져온 메모\n\n");
                if (notes.Length > 0) body.Append(NoteContent.NormalizeLineEndings(notes)).Append("\n\n");
                foreach (var t in d.Todos) body.Append(t.Done ? "- [x] " : "- [ ] ").Append((t.Text ?? "").Replace('\n', ' ')).Append('\n');
                var size = NoteSizePreset.Large;
                store.Create(NotePlacement.PlaceNew(size, NotePlacement.Primary(), cascade++), NoteColors.Default, body.ToString().TrimEnd() + "\n");
                d.Notes = ""; d.Todos.Clear(); d.Save();
            }
            store.SetSetting("migrated.helper", "1");
        }

        /// <summary>끌 때: 열려 있던 메모는 열린 채로 둔다(다음에 켜면 그 자리에 다시 뜬다). 창이 닫히며 "닫음" 으로 기록되지 않게 suspending 으로 막는다.</summary>
        internal void Suspend()
        {
            if (store == null) return;
            suspending = true;
            try
            {
                tick.Stop(); reminderTimer.Stop(); if (hotkeys != null) { hotkeys.Dispose(); hotkeys = null; }
                foreach (var w in windows.Values.ToList()) SaveNow(w);
                pending.Clear();
                foreach (var w in windows.Values.ToList()) w.Close();
                windows.Clear(); store.PurgeBlanks(); store.FlushDirty();
                if (library != null && !library.IsDisposed) library.Close();
                if (history != null && !history.IsDisposed) history.Close();
                if (reminderForm != null && !reminderForm.IsDisposed) reminderForm.Close();
            }
            finally { suspending = false; }
        }

        private void OnTick()
        {
            if (pending.Count > 0)
            {
                var now = DateTime.UtcNow;
                foreach (var id in pending.Where(kv => now - kv.Value >= SaveDebounce).Select(kv => kv.Key).ToList())
                {
                    NoteWindow w; if (!windows.TryGetValue(id, out w)) { pending.Remove(id); continue; }
                    if (w.IsComposing) continue;   // 글자를 짓는 중에는 건드리지 않는다. 조합이 끝나면 다음 틱에 저장된다.
                    pending.Remove(id); SaveNow(w);
                }
            }
            if (++ticks % 5 == 0) store.FlushDirty();
        }

        // ───────── 창 열고 닫기 ─────────

        private NoteWindow Show(Note note)
        {
            NoteWindow existing; if (windows.TryGetValue(note.Id, out existing)) return existing;
            var w = new NoteWindow(this, note); windows[note.Id] = w;
            w.Show(); w.ShowImages(note.Attachments.Where(a => NoteStore.IsImage(a.DisplayName)));
            return w;
        }

        /// <summary>메모를 만들고 띄운다. 크기와 색을 말하지 않으면 사용자가 정해 둔 기본값(null 과 "보통" 은 다른 질문이다).</summary>
        internal Note CreateNote(NoteSizePreset? preset = null, string colorKey = null)
        {
            var p = preset ?? DefaultSize;
            var geometry = NotePlacement.PlaceNew(p, NotePlacement.Primary(), cascade++, NotePlacement.CurrentMonitors());
            var note = store.Create(geometry, colorKey ?? DefaultColor);
            var w = Show(note); w.FocusEditor(); return note;
        }

        /// <summary>떠 있으면 앞으로, 닫혀 있으면 다시 연다.</summary>
        internal void Focus(Guid id)
        {
            NoteWindow w; if (windows.TryGetValue(id, out w)) { w.FocusEditor(); return; }
            var note = store.Get(id); if (note == null) return;
            store.SetOpen(id, true); Show(note).FocusEditor();
        }

        internal void NoteActivated(NoteWindow w) { ActiveNote = w; }

        internal void WindowClosed(NoteWindow w)
        {
            if (ActiveNote == w) ActiveNote = null;
            windows.Remove(w.NoteId);
            if (suspending) return;
            if (pending.Remove(w.NoteId) || true) SaveNow(w);
            if (store.PurgeIfBlank(w.NoteId)) return;   // 빈 메모는 닫는 순간 사라진다(라이브러리에 남기지 않는다)
            store.SetOpen(w.NoteId, false); store.Policy.Forget(w.NoteId);   // 닫으면 숨을 뿐. 다음에 열고 고치면 곧바로 기록을 한 번 남긴다.
        }

        // ───────── 창 → 저장 ─────────

        internal void TextChanged(NoteWindow w) { pending[w.NoteId] = DateTime.UtcNow; }

        private void SaveNow(NoteWindow w)
        {
            try { if (store.Get(w.NoteId) != null) store.SaveContent(w.NoteId, w.CurrentContent); }
            catch (Exception ex) { FeatureLog.Write(ex); }   // 저장 실패가 메모 창을 죽이면 안 된다: 글은 화면에 있고 다음 입력이 다시 시도한다.
        }

        internal void GeometryChanged(NoteWindow w, NoteGeometry g) { store.SetGeometry(w.NoteId, g); }
        internal void AppearanceChanged(NoteWindow w) { store.SetAppearance(w.NoteId, w.ColorKey, OpacityOf(w), w.Pinned); }
        private double OpacityOf(NoteWindow w) { return Math.Max(Note.MinOpacity, w.Opacity); }

        /// <summary>기록 복원: 저장소에 먼저 쓰고(복원 전 글은 기록에 남는다), 화면의 창에도 반영한다.</summary>
        internal void RestoreContent(Guid id, string content)
        {
            store.SaveContent(id, content, RevisionReason.BulkReplace);
            NoteWindow w; if (windows.TryGetValue(id, out w)) { pending.Remove(id); w.ReplaceContent(content); }
        }

        /// <summary>삭제(되돌릴 수 없음). 호출하는 쪽이 이미 확인을 받았다.</summary>
        internal void DeleteNotes(IEnumerable<Guid> ids)
        {
            foreach (var id in ids.Distinct().ToList())
            {
                NoteWindow w;
                if (windows.TryGetValue(id, out w)) { windows.Remove(id); pending.Remove(id); suspending = true; try { w.Close(); } finally { suspending = false; } }
                store.Delete(id);
            }
        }

        // ───────── 첨부 ─────────

        internal void Attach(NoteWindow w, IEnumerable<string> paths)
        {
            var inserts = new List<string>();
            foreach (string path in paths)
            {
                try
                {
                    var a = store.AddFile(w.NoteId, path); if (a == null) continue;
                    if (NoteStore.IsImage(a.DisplayName)) w.AddImage(a); else inserts.Add(NoteStore.LinkMarkdown(a));
                }
                catch (Exception ex) { FeatureLog.Write(ex); }
            }
            if (inserts.Count > 0) w.InsertAtCaret(string.Join("\n", inserts.ToArray()) + "\n");
        }

        internal void PasteImage(NoteWindow w, Image image)
        {
            try
            {
                using (var ms = new MemoryStream()) { image.Save(ms, ImageFormat.Png); var a = store.Embed(w.NoteId, "pasted-image.png", ms.ToArray()); if (a != null) w.AddImage(a); }
            }
            catch (Exception ex) { FeatureLog.Write(ex); }
        }

        internal void PickImage(NoteWindow w)
        {
            using (var dlg = new OpenFileDialog { Title = "이미지 넣기", Filter = "이미지|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.tif;*.tiff|모든 파일|*.*", Multiselect = true })
                if (dlg.ShowDialog(w) == DialogResult.OK) Attach(w, dlg.FileNames);
        }

        internal void RemoveImage(NoteWindow w, NoteAttachment a) { store.RemoveAttachment(w.NoteId, a.Id); w.RemoveImage(a.Id); }

        // ───────── 알림 ─────────

        /// <summary>offset 이 0 이면 "내일 아침 9시"(기간이 아니라 시각인 유일한 항목).</summary>
        internal void AddReminder(Guid noteId, TimeSpan offset)
        {
            DateTime due = offset == TimeSpan.Zero ? DateTime.Now.Date.AddDays(1).AddHours(9) : DateTime.Now.Add(offset);
            store.AddReminder(noteId, due); app.Announce("알림을 맞췄어: " + NoteWindow.ReminderLabel(due));
        }

        /// <summary>이미 지난 시각도 버리거나 옮기지 않고 저장한다 — 바로 울리는 알림이 사용자의 날짜를 몰래 바꾸는 것보다 낫다.</summary>
        internal void AddReminderAt(Guid noteId, DateTime dueLocal, string rule)
        {
            store.AddReminder(noteId, dueLocal, rule); app.Announce("알림을 맞췄어: " + NoteWindow.ReminderLabel(dueLocal));
        }

        internal void ShowReminderComposer(Guid noteId, Form near)
        {
            if (reminderForm != null && !reminderForm.IsDisposed) reminderForm.Close();
            reminderForm = new ReminderForm((due, rule) => AddReminderAt(noteId, due, rule)); reminderForm.Show(near);
            reminderForm.Location = new Point(Math.Max(0, near.Left + near.Width / 2 - reminderForm.Width / 2), Math.Max(0, near.Top + 30));
        }

        internal void CheckReminders()
        {
            try
            {
                var now = DateTime.Now;
                foreach (var r in store.DueReminders(now))
                {
                    var note = store.Get(r.NoteId); if (note == null) continue;
                    ShowReminder(note);
                    var next = ReminderScheduler.NextDue(r, now);
                    if (next.HasValue) store.Reschedule(r.Id, next.Value); else store.MarkNotified(r.Id, now);
                }
            }
            catch (Exception ex) { FeatureLog.Write(ex); }
        }

        /// <summary>알림이 울리면 화면 가운데의 알림 비키니친구가 내용을 말하고, 그 메모를 바탕화면에 연다(deskNote 의 토스트를 눌러 메모를 여는 동작을 미리 해 준다).</summary>
        private void ShowReminder(Note note)
        {
            string title = note.Title.Length > 0 ? note.Title : "메모 알림";
            string flat = note.Content.Replace('\n', ' ').Trim();
            int at = note.Title.Length > 0 ? flat.IndexOf(note.Title, StringComparison.Ordinal) : -1;
            if (at >= 0 && at <= 4) flat = flat.Substring(at + note.Title.Length).Trim();   // 첫 줄이 곧 제목이라 말풍선에 두 번 나오지 않게
            string body = flat.Length <= 120 ? flat : flat.Substring(0, 120) + "…";
            app.ShowAlert(title + (body.Length > 0 && body != title ? "\n" + body : ""));
            Focus(note.Id);
        }

        // ───────── 창들 ─────────

        internal void ShowLibrary()
        {
            if (library != null && !library.IsDisposed) { if (library.WindowState == FormWindowState.Minimized) library.WindowState = FormWindowState.Normal; library.Activate(); return; }
            library = new NoteLibraryForm(this); library.Show();
        }

        internal void ShowHistory(Guid noteId, Form near)
        {
            if (history != null && !history.IsDisposed) history.Close();
            history = new NoteHistoryForm(this, noteId); history.Show(near);
        }

        // ───────── 단축키 ─────────

        internal void ToggleActivePin() { if (ActiveNote != null) ActiveNote.ToggleAlwaysOnTop(); }
        internal void RemindActive() { if (ActiveNote != null) AddReminder(ActiveNote.NoteId, TimeSpan.FromHours(1)); }

        /// <summary>어느 창에서든 먹는 단축키(Ctrl+Alt+N 새 메모 · Ctrl+Shift+F 라이브러리 · Ctrl+Shift+P 항상 위 · Ctrl+Shift+R 1시간 뒤 알림). 다른 앱이 먼저 잡았으면 조용히 건너뛴다.</summary>
        private sealed class HotkeyWindow : NativeWindow, IDisposable
        {
            [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
            [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
            private const int WM_HOTKEY = 0x0312; private const uint CTRL = 2, ALT = 1, SHIFT = 4, NOREPEAT = 0x4000;
            private readonly NoteManager mgr; internal readonly List<string> Failed = new List<string>();

            internal HotkeyWindow(NoteManager mgr)
            {
                this.mgr = mgr; CreateHandle(new CreateParams());
                Reg(1, CTRL | ALT, 'N', "Ctrl+Alt+N"); Reg(2, CTRL | SHIFT, 'F', "Ctrl+Shift+F"); Reg(3, CTRL | SHIFT, 'P', "Ctrl+Shift+P"); Reg(4, CTRL | SHIFT, 'R', "Ctrl+Shift+R");
            }
            private void Reg(int id, uint mods, char key, string name) { if (!RegisterHotKey(Handle, id, mods | NOREPEAT, key)) Failed.Add(name); }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    switch ((int)m.WParam) { case 1: mgr.CreateNote(); break; case 2: mgr.ShowLibrary(); break; case 3: mgr.ToggleActivePin(); break; case 4: mgr.RemindActive(); break; }
                    return;
                }
                base.WndProc(ref m);
            }

            public void Dispose() { for (int i = 1; i <= 4; i++) UnregisterHotKey(Handle, i); DestroyHandle(); }
        }
    }
}
