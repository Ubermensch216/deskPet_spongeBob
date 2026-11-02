using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BikiniFriends
{
    /// <summary>
    /// 메모·알림 점검 (개발용). --selftest 에 규칙·저장소 점검이 합쳐지고,
    /// --note-test(실제 메모 창) · --alert-test(알림 친구) · --note-shots(그림)를 따로 돌릴 수 있다.
    /// </summary>
    internal static partial class DevTools
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        /// <summary>창 메시지를 처리하며 secs 초 기다린다.</summary>
        private static void Pump(double secs)
        {
            var end = DateTime.UtcNow.AddSeconds(secs);
            while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(15); }
        }

        /// <summary>주 화면 전체를 PNG 로 저장한다.</summary>
        private static void SaveScreen(string path)
        {
            Rectangle r = Screen.PrimaryScreen.Bounds;
            try
            {
                using (var b = new Bitmap(r.Width, r.Height))
                using (var g = Graphics.FromImage(b))
                { g.CopyFromScreen(r.Location, Point.Empty, r.Size); b.Save(path, ImageFormat.Png); }
            }
            catch
            {
                try
                {
                    using (var b = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height)))
                    {
                        using (var g = Graphics.FromImage(b)) g.Clear(Color.Black);
                        b.Save(path, ImageFormat.Png);
                    }
                }
                catch { }
            }
        }

        /// <summary>--selftest 가 부르는 메모·알림 점검 묶음.</summary>
        internal static void NoteSelfTests(Action<bool, string> check, string dir)
        {
            AlertPetTests(check);
            NoteRuleTests(check);
            NoteStoreTests(check, dir);
        }

        /// <summary>알림 친구: 2배 크기 · 네온 빛 · 큰 말풍선.</summary>
        private static void AlertPetTests(Action<bool, string> check)
        {
            CharacterArt normalArt = CharacterArt.Get(0), alertArt = CharacterArt.Zoomed(0, AlertPetWindow.Zoom);
            check(AlertPetWindow.Zoom == 2f && Math.Abs(alertArt.H - normalArt.H * 2) <= 1 && Math.Abs(alertArt.W - normalArt.W * 2) <= 1 && alertArt.Unit == normalArt.Unit * 2, "알림 친구는 걷는 친구의 2배 크기");
            check(NeonGlow.ColorAt(0) != NeonGlow.ColorAt(3) && NeonGlow.ColorAt(0).A == 255 && NeonGlow.ColorAt(0) == NeonGlow.ColorAt(7.5), "네온 색이 시간에 따라 돌고 한 바퀴 뒤 제자리");
            var aw = new FakeWorld(); var alertPet = new Pet(aw, alertArt, 640, 360); alertPet.StartAlert(aw); alertPet.Bubble = "회의 시작 10분 전이야!";
            Pet.Look plainLook = alertPet.GetLook(0), neonLook = plainLook;
            using (var bigFont = new Font("Malgun Gothic", 34f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                plainLook.BubbleFont = neonLook.BubbleFont = bigFont; plainLook.BubbleWidth = neonLook.BubbleWidth = 600;
                neonLook.Neon = Color.FromArgb(255, 30, 200); neonLook.NeonPulse = 1;
                PointF plainOrigin, no, smallOrigin; Pet.Look smallLook = alertPet.GetLook(0);
                using (Bitmap plainBmp = Pet.Render(plainLook, out plainOrigin)) using (Bitmap neonBmp = Pet.Render(neonLook, out no)) using (Bitmap smallBmp = Pet.Render(smallLook, out smallOrigin))
                {
                    check(neonBmp.Width > plainBmp.Width && neonBmp.Height > plainBmp.Height, "네온 빛이 번질 여백만큼 그림이 커짐");
                    int below = (int)no.Y + 14, cx = (int)no.X;
                    check(below < neonBmp.Height && neonBmp.GetPixel(cx, below).A > 20, "발밑까지 네온 빛이 번짐(실루엣 둘레)");
                    check(plainBmp.Height - smallBmp.Height > 15, "알림 말풍선 글씨가 기본 말풍선보다 크게 그려짐");
                }
            }
        }

        /// <summary>메모 본문 규칙: 제목·태그·체크리스트·마크다운 편집·인라인 서식·반복 알림·배치·기록 정책.</summary>
        private static void NoteRuleTests(Action<bool, string> check)
        {
            check(NoteContent.DeriveTitle("# 회의 준비\n본문") == "회의 준비" && NoteContent.DeriveTitle("\n\n- [ ] **김대리** 확인") == "김대리 확인" && NoteContent.DeriveTitle("   ") == "" && NoteContent.DeriveTitle(new string('가', 100)).Length == 60, "제목: 내용 있는 첫 줄에서 마크업을 뗀다");
            check(NoteContent.NormalizeLineEndings("a\r\nb\rc") == "a\nb\nc", "줄바꿈을 \\n 으로 통일");
            var tagList = TagParser.Parse("#백엔드 일 # 제목 #123 #Backend #backend (#v1)");
            check(tagList.Count == 3 && tagList[0].Name == "백엔드" && tagList[1].Name == "Backend" && tagList[2].Name == "v1", "태그: 본문의 #단어, 제목(# )·숫자 제외, 대소문자 같은 태그는 처음 철자");
            var cl = ChecklistParser.Parse("- [ ] a\n- [x] b\n본문\n  * [X] c"); check(cl.Count == 3 && !cl[0].IsDone && cl[1].IsDone && cl[2].IsDone && cl[2].LineIndex == 3, "체크리스트 읽기");
            Func<string, int, string> enter = (t, c) => { var r = MarkdownEditing.ContinueListOnEnter(new NoteTextState(t, c, 0)); return r.HasValue ? r.Value.Text : null; };
            check(enter("- 사과", 4) == "- 사과\n- ", "Enter: 불릿 이어쓰기");
            check(enter("1. 가\n2. 나", 9) == "1. 가\n2. 나\n3. ", "Enter: 번호 +1");
            check(enter("- [x] 끝", 7) == "- [x] 끝\n- [ ] ", "Enter: 끝낸 항목 다음은 새 빈 체크박스");
            check(enter("  - 들여씀", 7) == "  - 들여씀\n  - ", "Enter: 들여쓰기 유지");
            check(enter("- ", 2) == "" && enter("- [ ] ", 6) == "", "Enter: 빈 항목에서 목록 끝내기");
            check(enter("그냥 글", 3) == null && enter("# 제목", 4) == null, "Enter: 목록이 아니면 기본 동작");
            var tg = MarkdownEditing.ToggleChecklistItemAtCaret(new NoteTextState("- [ ] 할일", 3, 0));
            check(tg.HasValue && tg.Value.Text == "- [x] 할일" && tg.Value.SelectionStart == 3 && MarkdownEditing.ToggleChecklistItemAtCaret(new NoteTextState("글", 0, 0)) == null, "Ctrl+Enter: 체크박스 토글(캐럿 유지)");
            check(MarkdownEditing.ApplyLineStyle(new NoteTextState("가\n나", 0, 3), LineStyle.Bullet).Text == "- 가\n- 나" && MarkdownEditing.ApplyLineStyle(new NoteTextState("- 가\n- 나", 0, 7), LineStyle.Bullet).Text == "가\n나", "줄 스타일: 전부 켜고, 이미 전부 켜졌으면 끈다");
            check(MarkdownEditing.ApplyLineStyle(new NoteTextState("", 0, 0), LineStyle.Checklist).Text == "- [ ] " && MarkdownEditing.ApplyLineStyle(new NoteTextState("글", 1, 0), LineStyle.Heading2).Text == "## 글", "줄 스타일: 빈 줄에도 붙고 제목 단계");
            var link = MarkdownEditing.InsertLink(new NoteTextState("글", 0, 1)); check(link.Text == "[글]()" && link.SelectionStart == 4, "링크: 캐럿이 괄호 안");
            check(MarkdownEditing.ToggleInlineCode(new NoteTextState("글", 0, 1)).Text == "`글`" && MarkdownEditing.ToggleInlineCode(new NoteTextState("`글`", 1, 1)).Text == "글", "코드: 감싸기 ↔ 풀기");
            var td = TextDiff.Minimal("abcde", "abXde"); check(td.Start == 2 && td.Length == 1 && td.Insert == "X" && TextDiff.Minimal("a", "a").IsEmpty, "최소 편집");
            // 인라인 서식 왕복
            foreach (string line in new[] { "**굵게** 와 *기울임* ~~취소~~ <u>밑줄</u>", "일반 글", "깨진 **기호", "2 * 3 = 6", "**~~굵은 취소~~**" })
                check(MarkdownInline.Write(MarkdownInline.Parse(line)) == line, "서식 왕복(글자 손실 없음): " + line);
            var rich = NoteRichText.FromMarkdown("- [ ] **a** b\n둘째");
            check(rich.Text == "- [ ] a b\n둘째" && rich.Spans.Count == 1 && rich.Spans[0].Start == 6 && rich.Spans[0].Length == 1 && NoteRichText.ToMarkdown(rich.Text, rich.Spans) == "- [ ] **a** b\n둘째", "마크다운 ↔ 화면 글 + 강조 구간");
            // 반복 알림 · 배치 · 기록 정책
            var monthly = new Recurrence(RecurrenceFrequency.Monthly); var jan31 = new DateTime(2026, 1, 31, 9, 0, 0);
            check(monthly.NextAfter(jan31, jan31.AddHours(1)) == new DateTime(2026, 2, 28, 9, 0, 0) && monthly.NextAfter(jan31, new DateTime(2026, 2, 28, 10, 0, 0)) == new DateTime(2026, 3, 31, 9, 0, 0), "월간 반복은 처음 날짜 기준(28일로 밀리지 않음)");
            check(new Recurrence(RecurrenceFrequency.Daily).NextAfter(new DateTime(2026, 10, 1, 9, 0, 0), new DateTime(2026, 10, 4, 12, 0, 0)) == new DateTime(2026, 10, 5, 9, 0, 0), "며칠 비워도 매일 알림은 9시 그대로, 한 번만");
            var rule = new Recurrence(RecurrenceFrequency.Weekly, 2).ToRule(); var back = Recurrence.Parse(rule);
            check(rule == "FREQ=WEEKLY;INTERVAL=2" && back.HasValue && back.Value.Frequency == RecurrenceFrequency.Weekly && back.Value.Interval == 2 && Recurrence.Parse("FREQ=HOURLY") == null && Recurrence.Parse(null) == null, "RRULE 쓰기·읽기");
            var rd = new NoteReminder { DueLocal = new DateTime(2026, 10, 1, 9, 0, 0) }; var rd2 = new NoteReminder { DueLocal = new DateTime(2026, 10, 9, 9, 0, 0) }; var rd3 = new NoteReminder { DueLocal = new DateTime(2026, 10, 1, 9, 0, 0), NotifiedLocal = new DateTime(2026, 10, 1, 9, 1, 0) };
            check(ReminderScheduler.SelectDue(new[] { rd, rd2, rd3 }, new DateTime(2026, 10, 2, 0, 0, 0)).Count == 1, "울릴 알림: 시각이 됐고 아직 안 울린 것만");
            var areaA = new WorkArea { DeviceName = "A", Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
            var lost = NotePlacement.ClampToVisibleArea(NoteGeometry.ForPreset(NoteSizePreset.Medium, 4000, 3000, "B"), new List<WorkArea> { areaA });
            check(lost.X <= 1920 - NotePlacement.MinimumVisibleEdge && lost.Y <= 1080 - NotePlacement.MinimumVisibleEdge && lost.MonitorKey == "A", "없어진 모니터의 메모는 손 닿는 곳으로");
            var leftArea = new WorkArea { DeviceName = "L", Left = -1280, Top = 0, Right = 0, Bottom = 1024 };
            var onLeft = NotePlacement.ClampToVisibleArea(NoteGeometry.ForPreset(NoteSizePreset.Small, -1000, 100, "L"), new List<WorkArea> { areaA, leftArea });
            check(onLeft.X == -1000 && onLeft.MonitorKey == "L", "기본 모니터 왼쪽(음수 좌표)의 메모는 그대로");
            var placed = NotePlacement.PlaceNew(NoteSizePreset.Medium, areaA, 0); var placed2 = NotePlacement.PlaceNew(NoteSizePreset.Medium, areaA, 1);
            check(placed.X == (1920 - 320) / 2 && placed2.X == placed.X + 28 && placed2.Y == placed.Y + 28, "새 메모: 가운데, 연달아 28px 씩 비껴서");
            check(NoteGeometry.ForPreset(NoteSizePreset.Medium, 0, 0, "").WithSize(320, 260).Preset == NoteSizePreset.Medium && NoteGeometry.ForPreset(NoteSizePreset.Medium, 0, 0, "").WithSize(333, 260).Preset == NoteSizePreset.Custom, "크기: 규격이면 그 규격, 아니면 Custom");
            check(StartupNotePolicy.Decide(2, 5) == StartupNoteAction.Nothing && StartupNotePolicy.Decide(0, 5) == StartupNoteAction.OpenMostRecent && StartupNotePolicy.Decide(0, 0) == StartupNoteAction.CreateFirstNote, "시작: 있으면 그대로, 없으면 마지막 메모, 아예 없으면 빈 메모");
            var rp2 = new RevisionPolicy(); var gid = Guid.NewGuid(); var nowT = new DateTime(2026, 10, 25, 9, 0, 0);
            check(!rp2.ShouldSnapshot(gid, "", nowT, RevisionReason.Edit) && rp2.ShouldSnapshot(gid, "글", nowT, RevisionReason.Edit), "기록 정책: 빈 글은 안 남기고, 처음엔 남긴다");
            rp2.MarkTaken(gid, nowT); check(!rp2.ShouldSnapshot(gid, "글", nowT.AddMinutes(2), RevisionReason.Edit) && rp2.ShouldSnapshot(gid, "글", nowT.AddMinutes(6), RevisionReason.Edit) && rp2.ShouldSnapshot(gid, "글", nowT.AddSeconds(1), RevisionReason.BulkReplace), "기록 정책: 일반 입력은 5분 간격, 통째 교체는 늘");
        }

        /// <summary>메모 저장소: 왕복·검색·삭제·깨진 파일·빈 메모.</summary>
        private static void NoteStoreTests(Action<bool, string> check, string dir)
        {
            string storeRoot = Path.Combine(dir, "notestore"); if (Directory.Exists(storeRoot)) Directory.Delete(storeRoot, true);
            var st = new NoteStore(storeRoot); st.Load();
            var n1 = st.Create(NoteGeometry.ForPreset(NoteSizePreset.Large, 10, 20, "A"), NoteColors.Blue, "# 첫 메모\n- [ ] 일 #업무\n😀 이모지 & <특수> \"따옴표\"");
            check(n1.Title == "첫 메모" && n1.ChecklistTotal == 1 && n1.Tags.Count == 1 && n1.Geometry.Width == 420, "새 메모: 제목·체크리스트·태그를 본문에서 읽는다");
            st.SaveContent(n1.Id, "# 첫 메모\n- [x] 일 #업무\n둘째 줄\n"); check(st.Revisions(n1.Id).Count == 1 && st.Get(n1.Id).ChecklistDone == 1, "저장: 바뀐 글이면 덮어쓰기 전 본문을 기록에");
            st.SaveContent(n1.Id, "# 첫 메모\n바뀜"); check(st.Revisions(n1.Id).Count == 1, "저장: 5분 안의 연속 입력은 기록을 또 만들지 않음");
            st.SaveContent(n1.Id, "복원한 글", RevisionReason.BulkReplace); check(st.Revisions(n1.Id).Count == 2 && st.Revisions(n1.Id)[0].Content.StartsWith("# 첫 메모\n바뀜"), "저장: 통째 교체는 직전 글을 늘 기록(새 것이 앞)");
            st.SaveContent(n1.Id, "# 첫 메모\n- [ ] 일 #업무\n😀 이모지 & <특수> \"따옴표\"\r\n끝", RevisionReason.BulkReplace);
            var n2 = st.Create(NoteGeometry.ForPreset(NoteSizePreset.Small, 0, 0, ""), NoteColors.Pink, "장보기 #집안일\n사과"); var nb = st.CreateNotebook("업무"); st.SetNotebook(n1.Id, nb.Id);
            st.SetGeometry(n1.Id, NoteGeometry.ForPreset(NoteSizePreset.Medium, 55, 66, "A").WithSize(401, 301)); st.SetAppearance(n1.Id, NoteColors.Green, 0.5, true); st.SetOpen(n2.Id, false); st.FlushDirty();
            var due = st.AddReminder(n1.Id, new DateTime(2026, 11, 1, 9, 0, 0), new Recurrence(RecurrenceFrequency.Weekly).ToRule()); st.SetSetting("note.defaultColor", "teal");
            var att = st.Embed(n1.Id, "그림.png", new byte[] { 1, 2, 3, 4 }); check(att != null && File.Exists(att.Path) && st.Get(n1.Id).Attachments.Count == 1, "첨부: 붙여넣은 이미지는 메모 폴더로 복사");
            var st2 = new NoteStore(storeRoot); st2.Load(); var r1 = st2.Get(n1.Id);
            check(st2.Count == 2 && r1 != null && r1.Content == "# 첫 메모\n- [ ] 일 #업무\n😀 이모지 & <특수> \"따옴표\"\n끝" && r1.NotebookId == nb.Id && r1.ColorKey == NoteColors.Green && Math.Abs(r1.Opacity - .5) < 1e-9 && r1.AlwaysOnTop && r1.Geometry.X == 55 && r1.Geometry.Width == 401 && r1.Geometry.Preset == NoteSizePreset.Custom && r1.Attachments.Count == 1, "저장소 다시 불러오기: 글(이모지·특수문자·줄바꿈)·노트북·모양·위치·첨부 일치");
            check(!st2.Get(n2.Id).IsOpen && st2.GetSetting("note.defaultColor") == "teal" && st2.Notebooks.Count == 1 && st2.RemindersOf(n1.Id).Count == 1 && st2.RemindersOf(n1.Id)[0].DueLocal == new DateTime(2026, 11, 1, 9, 0, 0), "저장소 다시 불러오기: 닫음 표시·기본 설정·노트북·알림");
            check(st2.Query("#업무", null, null).Count == 1 && st2.Query("이모지", null, null).Count == 1 && st2.Query("사과", null, null).Count == 1 && st2.Query("사과", nb.Id, null).Count == 0 && st2.Query(null, nb.Id, null).Count == 1 && st2.Query(null, null, "집안일").Count == 1 && st2.Query("없는말", null, null).Count == 0, "검색·노트북·태그 걸러 보기");
            check(st2.TagUsage().Count == 2 && st2.TagUsage()[0].Value == 1, "태그 목록과 개수");
            check(st2.DueReminders(new DateTime(2026, 10, 31)).Count == 0 && st2.DueReminders(new DateTime(2026, 11, 2)).Count == 1, "알림이 시각이 되면 목록에");
            st2.DeleteNotebook(nb.Id); check(st2.Get(n1.Id) != null && st2.Get(n1.Id).NotebookId == null, "노트북을 지워도 메모는 남는다");
            string attDir = Path.GetDirectoryName(att.Path); st2.Delete(n1.Id);
            check(st2.Count == 1 && !File.Exists(Path.Combine(storeRoot, "notes", n1.Id.ToString("N") + ".xml")) && !Directory.Exists(attDir) && st2.RemindersOf(n1.Id).Count == 0 && st2.Revisions(n1.Id).Count == 0, "삭제: 본문·기록·복사해 둔 이미지·알림이 함께 사라진다");
            File.WriteAllText(Path.Combine(storeRoot, "notes", Guid.NewGuid().ToString("N") + ".xml"), "<note id=\"깨짐"); var st3 = new NoteStore(storeRoot); st3.Load();
            check(st3.Count == 1 && st3.LoadProblems.Count == 1, "깨진 메모 파일 하나가 나머지 메모를 막지 않는다");
            // 빈 메모는 저장하지 않는다
            string blankRoot = Path.Combine(dir, "blankstore"); if (Directory.Exists(blankRoot)) Directory.Delete(blankRoot, true);
            var sb = new NoteStore(blankRoot); sb.Load(); var geo0 = NoteGeometry.ForPreset(NoteSizePreset.Medium, 0, 0, "");
            var bn = sb.Create(geo0, NoteColors.Yellow); string bpath = Path.Combine(blankRoot, "notes", bn.Id.ToString("N") + ".xml");
            check(bn.IsBlank && !File.Exists(bpath) && sb.Count == 0 && sb.Query(null, null, null).Count == 0 && sb.OpenNotes().Count == 0, "빈 메모: 파일로 남기지 않고 라이브러리에도 안 보인다");
            sb.SaveContent(bn.Id, "   \n "); check(!File.Exists(bpath) && sb.Count == 0, "빈 메모: 공백뿐인 글도 빈 메모");
            sb.SaveContent(bn.Id, "글"); check(File.Exists(bpath) && sb.Count == 1 && sb.Query(null, null, null).Count == 1, "빈 메모에 글을 쓰면 그때부터 저장되고 보인다");
            sb.SaveContent(bn.Id, ""); check(!File.Exists(bpath) && sb.Count == 0 && sb.Query(null, null, null).Count == 0, "글을 다 지우면 저장에서 빠진다");
            check(sb.PurgeIfBlank(bn.Id) && sb.Get(bn.Id) == null && !sb.PurgeIfBlank(Guid.NewGuid()), "닫을 때 빈 메모는 통째로 치운다");
            var bi = sb.Create(geo0, NoteColors.Yellow); sb.Embed(bi.Id, "a.png", new byte[] { 1, 2 });
            check(!bi.IsBlank && File.Exists(Path.Combine(blankRoot, "notes", bi.Id.ToString("N") + ".xml")) && sb.Count == 1 && !sb.PurgeIfBlank(bi.Id), "이미지만 있는 메모는 빈 메모가 아니다");
            var leftId = Guid.NewGuid(); string leftPath = Path.Combine(blankRoot, "notes", leftId.ToString("N") + ".xml");
            File.WriteAllText(leftPath, "<note id=\"" + leftId + "\" color=\"yellow\"><content></content></note>");
            var sb2 = new NoteStore(blankRoot); sb2.Load(); check(sb2.Count == 1 && !File.Exists(leftPath), "이전에 남은 빈 메모 파일은 불러올 때 치운다");
            var bz = sb2.Create(geo0, NoteColors.Yellow); sb2.PurgeBlanks(); check(sb2.Get(bz.Id) == null && sb2.Count == 1, "끌 때 메모리에 남은 빈 메모를 치운다(내용 있는 메모는 그대로)");
            FriendData.Current = new FriendData();
        }

        /// <summary>창을 띄우고 그 창만 PNG 로 저장한다(바탕화면은 찍히지 않음). 메모 창은 투명 영역 때문에 PrintWindow 로 찍는다.</summary>
        private static void CaptureForm(Form f, string path)
        {
            f.Show(); Pump(.4);
            if (f is NoteWindow)
            {
                Rectangle r = f.Bounds;
                using (var b = new Bitmap(r.Width, r.Height))
                using (var g = Graphics.FromImage(b)) { IntPtr hdc = g.GetHdc(); PrintWindow(f.Handle, hdc, 2); g.ReleaseHdc(hdc); b.Save(path, ImageFormat.Png); }
                return;
            }
            using (var b = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height)); b.Save(path, ImageFormat.Png); }
        }

        /// <summary>알림 친구를 실제 화면에 띄워 전체 화면(alert_screen.png)과 알림 친구 그림(alert_render.png)을 저장한다(마우스는 건드리지 않음).</summary>
        internal static int AlertTest(string dir, string message)
        {
            Directory.CreateDirectory(dir);
            Paths.StorageOverride = Path.Combine(dir, "data");
            PlayOptions.Load(); FriendData.Load(); CharacterArt.Reset(Native.SystemDpiScale());
            var app = new App(true); app.Spawn(0);
            Pump(1.5);
            var alert = app.ShowAlert(message ?? "회의 시작 10분 전이야!\n자료 챙겨서 회의실로 가자");
            Pump(1.5);
            SaveScreen(Path.Combine(dir, "alert_screen.png"));
            var drawClock = System.Diagnostics.Stopwatch.StartNew(); for (int i = 0; i < 10; i++) alert.Draw(2 + i * .05); double drawMs = drawClock.Elapsed.TotalMilliseconds / 10;
            PointF origin; using (Bitmap shot = alert.Snapshot(1.2, out origin)) shot.Save(Path.Combine(dir, "alert_render.png"), ImageFormat.Png);
            File.WriteAllText(Path.Combine(dir, "alert_info.txt"), "placed=" + alert.Placed + " origin=" + origin + " area=" + alert.Area + " presented=" + alert.HasPresented + " drawMs=" + drawMs.ToString("0.0"));
            alert.Close(); Pump(.3);
            return alert.HasPresented ? 0 : 1;
        }

        /// <summary>메모 위젯·알림 친구를 실제 창으로 띄워 확인한다 (--note-test). 마우스는 건드리지 않는다.</summary>
        internal static int NoteTest(string dir)
        {
            Directory.CreateDirectory(dir);
            Paths.StorageOverride = Path.Combine(dir, "data");
            var log = new StringBuilder(); int failures = 0;
            Action<string, Action> step = (name, a) => { try { a(); log.AppendLine("OK   " + name); } catch (Exception e) { failures++; log.AppendLine("FAIL " + name + " " + e); } };
            Action<double> pump = Pump;
            Action<Form, string> shot = (f, name) => CaptureForm(f, Path.Combine(dir, name + ".png"));
            Action<string> screen = name => SaveScreen(Path.Combine(dir, name + ".png"));
            PlayOptions.Load(); FriendData.Load(); CharacterArt.Reset(Native.SystemDpiScale());
            App app = null;
            step("앱 생성(펫 3마리)", () => { app = new App(true); app.Spawn(0); app.Spawn(1); app.Spawn(2); pump(1); });
            step("트레이 메뉴", () => { var m = typeof(App).GetMethod("BuildMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); using (var menu = (ContextMenuStrip)m.Invoke(app, new object[] { null })) { var newNote = menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Text == "새 메모"); if (newNote == null || newNote.HasDropDownItems) throw new Exception("최상위 메뉴에 새 메모 한 줄만 있어야 해요"); if (menu.Items.OfType<ToolStripItem>().Any(i => i.Text == "메모" || i.Text == "메모 라이브러리")) throw new Exception("메모 하위 메뉴·메모 라이브러리가 최상위에 남아 있어요"); } });
            step("메모 위젯: 쓰기 · 이어쓰기 · 서식 · 저장 · 다시 열기", () =>
            {
                var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var mgr = new NoteManager(app); app.Notes = mgr; mgr.Start(false);
                var note = mgr.CreateNote(NoteSizePreset.Medium, NoteColors.Yellow); var w = mgr.WindowOf(note.Id); pump(.4);
                if (w == null) throw new Exception("메모 창이 안 떴어요");
                Func<Keys, bool> press = k => (bool)typeof(NoteWindow).GetMethod("OnCommandKey", bf).Invoke(w, new object[] { k });
                w.Box.Focus(); w.InsertAtCaret("# 회의 준비\n- 사과");
                if (!press(Keys.Enter) || w.Editor.Text != "# 회의 준비\n- 사과\n- ") throw new Exception("Enter 이어쓰기: [" + w.Editor.Text.Replace("\n", "\\n") + "]");
                w.InsertAtCaret("배"); press(Keys.Control | Keys.Shift | Keys.C); press(Keys.Control | Keys.Enter);
                if (w.Editor.Text != "# 회의 준비\n- 사과\n- [x] 배") throw new Exception("체크리스트 + Ctrl+Enter: [" + w.Editor.Text.Replace("\n", "\\n") + "]");
                if (!w.Box.CanUndo) throw new Exception("편집이 실행 취소 기록에 없음");
                int apple = w.Editor.Text.IndexOf("사과"); w.Editor.Select(apple, 2); press(Keys.Control | Keys.B);
                int meet = w.Editor.Text.IndexOf("회의"); w.Editor.Select(meet, 2); press(Keys.Control | Keys.Shift | Keys.X);
                w.Editor.Select(w.Editor.Text.Length, 0); w.Editor.Invalidate();
                string expected = "# ~~회의~~ 준비\n- **사과**\n- [x] 배";
                if (w.CurrentContent != expected) throw new Exception("서식을 마크다운으로: [" + w.CurrentContent.Replace("\n", "\\n") + "]");
                if (w.CurrentTitle != "회의 준비") throw new Exception("제목: " + w.CurrentTitle);
                w.TopMost = true; w.FocusEditor(); pump(.4); shot(w, "ui_note_text"); w.TopMost = false;
                using (var bmp = new Bitmap(300, 180)) using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.SteelBlue); g.FillEllipse(Brushes.Gold, 100, 40, 100, 100); mgr.PasteImage(w, bmp); }
                mgr.TextChanged(w); pump(.7);
                if (mgr.Store.Get(note.Id).Content != expected) throw new Exception("자동 저장 안 됨: [" + mgr.Store.Get(note.Id).Content.Replace("\n", "\\n") + "]");
                w.TopMost = true; w.FocusEditor(); pump(.6); File.WriteAllText(Path.Combine(dir, "note_bounds.txt"), w.Bounds + " visible=" + w.Visible + " screen=" + Screen.PrimaryScreen.Bounds + " region=" + (w.Region != null)); shot(w, "ui_note"); w.TopMost = false;
                w.Box.Focus(); w.InsertAtCaret("\n#업무 둘째 줄"); mgr.TextChanged(w); pump(.7);
                var second = mgr.CreateNote(NoteSizePreset.Small, NoteColors.Pink); mgr.WindowOf(second.Id).InsertAtCaret("장보기\n- [ ] 우유\n- [x] 빵"); mgr.TextChanged(mgr.WindowOf(second.Id)); pump(.7);
                var nb = mgr.Store.CreateNotebook("업무"); mgr.Store.SetNotebook(note.Id, nb.Id);
                // 알림: 이미 지난 시각은 바로 울리고(노트 창이 열려 있는 채) 한 번만
                mgr.Store.AddReminder(note.Id, DateTime.Now.AddMinutes(-1)); mgr.CheckReminders();
                var reminderAlert = Application.OpenForms.OfType<AlertPetWindow>().ToList();
                if (reminderAlert.Count != 1 || reminderAlert[0].Message.Length == 0) throw new Exception("메모 알림이 알림 친구로 안 떴어요");
                foreach (var f in reminderAlert) f.Close();
                if (mgr.Store.DueReminders(DateTime.Now).Count != 0) throw new Exception("알림이 한 번 울리고 끝나야 해요");
                mgr.ShowLibrary(); pump(.5); var lib = Application.OpenForms.OfType<NoteLibraryForm>().First(); shot(lib, "ui_library"); lib.Close();
                var hist = new NoteHistoryForm(mgr, note.Id); shot(hist, "ui_history"); hist.Close();
                var rem = new ReminderForm((d, r) => { }); shot(rem, "ui_reminder"); rem.Close();
                // 끄고 다시 켜면 열려 있던 메모가 그 모습 그대로 돌아온다
                mgr.Suspend(); if (mgr.OpenWindowCount != 0) throw new Exception("끄면 창이 다 닫혀야 해요");
                var mgr2 = new NoteManager(app); app.Notes = mgr2; mgr2.Start(true); pump(.4);
                if (mgr2.OpenWindowCount != 2) throw new Exception("다시 켰을 때 열려 있던 메모 수: " + mgr2.OpenWindowCount);
                var again = mgr2.WindowOf(note.Id); string reloaded = again.CurrentContent;
                if (!reloaded.StartsWith("# ~~회의~~ 준비\n- **사과**\n- [x] 배")) throw new Exception("다시 연 메모: [" + reloaded.Replace("\n", "\\n") + "]");
                if (again.Box.Text.IndexOf("**") >= 0 || again.Box.Text.IndexOf("~~") >= 0) throw new Exception("강조 기호가 숨지 않았어요");
                var blankNote = mgr2.CreateNote(); var blankWin = mgr2.WindowOf(blankNote.Id); pump(.2);
                if (mgr2.Store.Query(null, null, null).Any(n => n.Id == blankNote.Id) || mgr2.Store.Count != 2) throw new Exception("빈 메모가 라이브러리에 보여요");
                blankWin.Close(); pump(.2); if (mgr2.Store.Get(blankNote.Id) != null) throw new Exception("빈 메모는 닫을 때 사라져야 해요");
                var typed = mgr2.CreateNote(); var typedWin = mgr2.WindowOf(typed.Id); typedWin.Box.Focus(); typedWin.InsertAtCaret("잠깐 쓴 글"); mgr2.TextChanged(typedWin); pump(.7); typedWin.InsertAtCaret(""); typedWin.Editor.Select(0, typedWin.Editor.Text.Length); typedWin.InsertAtCaret(""); mgr2.TextChanged(typedWin); pump(.7);
                typedWin.Close(); pump(.2); if (mgr2.Store.Get(typed.Id) != null) throw new Exception("글을 다 지운 메모도 닫을 때 사라져야 해요");
                mgr2.DeleteNotes(new[] { second.Id }); if (mgr2.Store.Get(second.Id) != null || mgr2.OpenWindowCount != 1) throw new Exception("삭제");
            });
            step("알림 친구: 2배 · 화면 가운데 · 네온 · 큰 말풍선", () =>
            {
                var n = app.ShowAlert(App.HourlyMessage(DateTime.Now, true, "13:00", "점심 먹으러 가자!", false, "18:00", "x"));
                pump(1.6); screen("ui_alert");
                if (!n.HasPresented) throw new Exception("알림 친구를 창에 못 올렸어요");
                if (app.LiveWindows.Count(w => w.Visible) != app.LiveWindows.Count - 1) throw new Exception("알림 동안 걷던 친구 한 마리만 숨어야 해요");
                PointF origin; using (Bitmap shot = n.Snapshot(1, out origin))
                {
                    shot.Save(Path.Combine(dir, "alert_render.png"), ImageFormat.Png);
                    Rectangle area = n.Area; double footX = n.Placed.X + origin.X;
                    if (Math.Abs(footX - (area.Left + area.Width / 2.0)) > 3) throw new Exception("친구가 화면 가운데에 안 서 있어요: " + footX + " / " + (area.Left + area.Width / 2.0));
                }
                n.Close(); pump(.3);
                if (app.LiveWindows.Count(w => w.Visible) != app.LiveWindows.Count) throw new Exception("알림을 닫으면 숨었던 친구가 돌아와야 해요");
            });
            step("종료", () => { app.Quit(); });
            log.AppendLine(failures == 0 ? "ALL OK" : failures + " FAILED");
            File.WriteAllText(Path.Combine(dir, "notetest_result.txt"), log.ToString(), new UTF8Encoding(true));
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// README 용 그림 만들기 (--shots). 바탕화면은 찍지 않고 창·그림만 따로 저장한다.
        /// 저장 위치를 폴더 안으로 돌려 두므로 실제 메모·설정에는 손대지 않는다.
        /// 만드는 파일: alert.png · note.png · library.png · settings.png · menu.png
        /// </summary>
        internal static int NoteShots(string dir)
        {
            Directory.CreateDirectory(dir);
            Paths.StorageOverride = Path.Combine(dir, "data");
            PlayOptions.Load(); FriendData.Load(); CharacterArt.Reset(Native.SystemDpiScale());
            FriendData.Current.Affinity = 128; FriendData.Current.TogetherSeconds = 5 * 3600 + 20 * 60; FriendData.Current.Satiety = 72;

            DesktopSurfaces.TestProvider = () => new System.Collections.Generic.List<SurfaceWindow>();

            var app = new App(true); app.Spawn(0); Pump(1);
            ShotAlert(app, Path.Combine(dir, "alert.png"));
            ShotNotes(app, dir);
            using (var f = new SettingsForm(app, 1)) CaptureForm(f, Path.Combine(dir, "settings.png"));
            ShotMenu(app, Path.Combine(dir, "menu.png"));
            app.Quit();
            return 0;
        }

        /// <summary>알림 친구(2배 크기 · 네온 빛 · 큰 말풍선)를 어두운 바탕 위에 올려 저장한다.</summary>
        private static void ShotAlert(App app, string path)
        {
            AlertPetWindow alert = app.ShowAlert("점심시간이야!\n오늘은 뭐 먹을까?");
            Pump(1.5);
            PointF origin;
            using (Bitmap shot = alert.Snapshot(1.2, out origin))
            using (var b = new Bitmap(shot.Width, shot.Height))
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.FromArgb(24, 30, 54)); g.DrawImage(shot, 0, 0);
                b.Save(path, ImageFormat.Png);
            }
            alert.Close(); Pump(.3);
        }

        /// <summary>메모 위젯과 라이브러리.</summary>
        private static void ShotNotes(App app, string dir)
        {
            var mgr = new NoteManager(app); app.Notes = mgr; mgr.Start(false);
            Note main = mgr.CreateNote(NoteSizePreset.Medium, NoteColors.Yellow);
            Note second = mgr.CreateNote(NoteSizePreset.Small, NoteColors.Pink);
            Note third = mgr.CreateNote(NoteSizePreset.Small, NoteColors.Blue);
            Write(mgr, main, "# 오늘 할 일\n- [x] 스폰지밥 만나기\n- [ ] **게살버거** 먹기\n- [ ] 뚱이랑 놀기 #일상");
            Write(mgr, second, "장보기 #집안일\n- [x] 해파리 젤리\n- [ ] 빵\n- [ ] 미역");
            Write(mgr, third, "# 아이디어\n새로운 버거 레시피 만들기 #요리");
            var notebook = mgr.Store.CreateNotebook("업무"); mgr.Store.SetNotebook(main.Id, notebook.Id);
            mgr.Store.AddReminder(main.Id, DateTime.Now.AddHours(1));
            // 껐다 켜면 서식 기호(** 등)가 숨고 실제 서식으로 보인다.
            mgr.Suspend();
            var shown = new NoteManager(app); app.Notes = shown; shown.Start(true); Pump(.4);
            NoteWindow w = shown.WindowOf(main.Id);
            w.TopMost = true; w.FocusEditor(); CaptureForm(w, Path.Combine(dir, "note.png")); w.TopMost = false;
            shown.ShowLibrary(); Pump(.5);
            CaptureForm(Application.OpenForms.OfType<NoteLibraryForm>().First(), Path.Combine(dir, "library.png"));
            foreach (var f in Application.OpenForms.OfType<NoteLibraryForm>().ToList()) f.Close();
            shown.Suspend();
        }

        private static void Write(NoteManager mgr, Note note, string text)
        {
            NoteWindow w = mgr.WindowOf(note.Id);
            w.Box.Focus(); w.InsertAtCaret(text); mgr.TextChanged(w); Pump(.7);
        }

        /// <summary>우클릭 메뉴.</summary>
        private static void ShotMenu(App app, string path)
        {
            MethodInfo build = typeof(App).GetMethod("BuildMenu", BindingFlags.NonPublic | BindingFlags.Instance);
            using (var menu = (ContextMenuStrip)build.Invoke(app, new object[] { null }))
            {
                menu.Show(new Point(40, 40)); Pump(.5);
                using (var b = new Bitmap(menu.Width, menu.Height))
                {
                    menu.DrawToBitmap(b, new Rectangle(0, 0, menu.Width, menu.Height));
                    b.Save(path, ImageFormat.Png);
                }
                menu.Close();
            }
        }
    }
}
