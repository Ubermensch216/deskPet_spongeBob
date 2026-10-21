using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("비키니친구들")]
[assembly: System.Reflection.AssemblyProduct("비키니친구들")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace BikiniFriends
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 2 && args[0] == "--make-icon") { DevTools.MakeIcon(args[1]); return; }
            if (args.Length >= 2 && args[0] == "--render-test") { DevTools.RenderSheet(args[1], args.Length >= 3 ? float.Parse(args[2]) : Native.SystemDpiScale()); return; }
            if (args.Length >= 2 && args[0] == "--selftest") { Environment.Exit(DevTools.SelfTest(args[1])); return; }
            if (args.Length >= 2 && args[0] == "--ui-test") { Environment.Exit(DevTools.UiTest(args[1])); return; }
            bool created;
            using (var mutex = new Mutex(true, "BikiniFriends_DeskFriend_single_instance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("비키니 친구들이 이미 실행 중이야.\n작업표시줄 알림 영역의 친구 아이콘을 우클릭해서 ‘화면 가운데로 다시 부르기’를 눌러줘.", "비키니친구들", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { FeatureLog.Write(e.Exception); };
                try
                {
                    PlayOptions.Load(); FriendData.Load();
                    CharacterArt.Reset(Native.SystemDpiScale());
                    var app = new App();
                    Application.Run(app);
                }
                catch (Exception error)
                {
                    FeatureLog.Write(error);
                    MessageBox.Show("친구를 화면에 표시하지 못했어.\n\n" + error.Message + "\n\n기록: " + Path.Combine(Paths.Dir, "비키니친구들_오류기록.txt"), "비키니친구들", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                GC.KeepAlive(mutex);
            }
        }
    }

    /// <summary>화면 없이 돌려 보는 가짜 세계 (자체 점검·렌더링 확인용).</summary>
    internal sealed class FakeWorld : IWorld
    {
        public List<Rectangle> Areas { get; set; }
        public Random Rng { get; set; }
        public double Now { get; set; }
        public FakeWorld() { Areas = new List<Rectangle> { new Rectangle(0, 0, 1280, 720) }; Rng = new Random(7); }
    }

    internal static class DevTools
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void mouse_event(int flags, int dx, int dy, int data, UIntPtr extra);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, int flags, UIntPtr extra);

        /// <summary>비키니친구들 얼굴로 PNG 압축 .ico 를 만든다(빌드할 때 한 번 쓰임).</summary>
        internal static void MakeIcon(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            var pngs = new List<byte[]>();
            foreach (int n in sizes)
                using (Bitmap bmp = CharacterArt.IconBitmap(0, n))
                using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
            using (var fs = File.Create(path))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte b = sizes[i] >= 256 ? (byte)0 : (byte)sizes[i];
                    bw.Write(b); bw.Write(b); bw.Write((byte)0); bw.Write((byte)0);
                    bw.Write((ushort)1); bw.Write((ushort)32); bw.Write((uint)pngs[i].Length); bw.Write((uint)offset);
                    offset += pngs[i].Length;
                }
                foreach (byte[] p in pngs) bw.Write(p);
            }
        }

        /// <summary>캐릭터별·상태별 모습을 한 장의 시트 PNG 로 저장한다.</summary>
        internal static void RenderSheet(string dir, float scale)
        {
            Directory.CreateDirectory(dir);
            CharacterArt.Reset(scale);
            PlayOptions.Current = new PlayOptions();
            DesktopSurfaces.TestProvider = () => new List<SurfaceWindow>(); DesktopSurfaces.TestMouse = () => new Point(-9999, -9999);
            FriendData.Current = new FriendData();
            var states = new List<KeyValuePair<string, Action<Pet, FakeWorld>>>
            {
                K("걷기", (p, w) => { p.SetState(PetState.Walk, w); p.Update(w, .4, null, 0); }),
                K("서기", (p, w) => p.SetState(PetState.Pause, w)),
                K("앉기", (p, w) => p.Demonstrate(w, "sit")),
                K("낮잠", (p, w) => p.SetState(PetState.Sleep, w)),
                K("눌림", (p, w) => { p.StartSquish(w, .4, .24, PetState.Walk, false); w.Now += .2; p.Update(w, .2, null, 0); }),
                K("우는중", (p, w) => p.Demonstrate(w, "cry")),
                K("배고픔", (p, w) => p.Demonstrate(w, "weep")),
                K("폴짝", (p, w) => { p.Demonstrate(w, "hop"); p.Update(w, .25, null, 0); }),
                K("슬라이딩", (p, w) => p.Demonstrate(w, "slide")),
                K("어질어질", (p, w) => p.Demonstrate(w, "dizzy")),
                K("헬리콥터", (p, w) => p.Demonstrate(w, "fall")),
                K("벽타기", (p, w) => p.Demonstrate(w, "wall")),
                K("천장", (p, w) => p.Demonstrate(w, "ceiling")),
                K("먹는중", (p, w) => { FriendData.Current.Satiety = 40; p.Feed(w); }),
                K("꽃다발", (p, w) => p.StartFlower(w, 100)),
                K("말풍선", (p, w) => p.Cheer(w, "점심 먹으러 가자!", 8)),
                K("대표동작", (p, w) => { p.Demonstrate(w, "hobby"); w.Now += 1.6; p.Update(w, 1.6, null, 0); }),
                K("멍때리기", (p, w) => { p.Demonstrate(w, "daydream"); w.Now += 2.4; p.Update(w, 2.4, null, 0); }),
                K("기지개", (p, w) => { p.Demonstrate(w, "stretch"); w.Now += 1.2; p.Update(w, 1.2, null, 0); }),
                K("비밀조종사", (p, w) => p.Demonstrate(w, "fall")),
            };
            int cell = (int)Math.Round(150 * scale), cols = 8, rows = (int)Math.Ceiling(states.Count / (double)cols);
            for (int c = 0; c < CharacterArt.Count; c++)
            {
                using (var sheet = new Bitmap(cell * cols, cell * rows, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(sheet))
                using (var font = new Font("Malgun Gothic", 9f * scale))
                {
                    g.Clear(Color.FromArgb(206, 226, 214)); g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    for (int i = 0; i < states.Count; i++)
                    {
                        var w = new FakeWorld(); w.Areas[0] = new Rectangle(0, 0, 1280, 720);
                        var pet = new Pet(w, c, 640, 400) { Area = w.Areas[0] };
                        pet.SetState(PetState.Pause, w);
                        pet.X = 640; pet.Y = 400;
                        states[i].Value(pet, w);
                        FriendData.Current.Satiety = 100;
                        PointF origin; using (Bitmap b = Pet.Render(pet.GetLook(w.Now), out origin))
                        {
                            int cx = (i % cols) * cell, cy = (i / cols) * cell;
                            float f = Math.Min(1f, Math.Min((cell - 10f) / b.Width, (cell - 30f) / b.Height));
                            g.DrawImage(b, cx + (cell - b.Width * f) / 2, cy + cell - 18 * scale - b.Height * f, b.Width * f, b.Height * f);
                            g.DrawString(CharacterArt.Infos[c].Name + " · " + states[i].Key, font, Brushes.Black, cx + 4, cy + cell - 18 * scale);
                        }
                    }
                    sheet.Save(Path.Combine(dir, "sheet_" + CharacterArt.Infos[c].Key + ".png"), ImageFormat.Png);
                }
            }
            // 벽·천장 타기 한 주기(약 0.6초)를 10컷으로
            // 시간 흐름에 따른 연속 컷(캐릭터별 한 장): 걷기·벽 오르기/내리기·천장·낙하·점프·기지개·멍때리기·잠자기
            for (int c = 0; c < CharacterArt.Count; c++)
            {
                var rowDefs = new List<Tuple<string, Action<Pet, FakeWorld>, double, double>>
                {
                    Tuple.Create<string, Action<Pet, FakeWorld>, double, double>("걷기", (p, w) => { p.Y = 720; p.SetState(PetState.Walk, w); }, 0.55, 0),
                    Tuple.Create<string, Action<Pet, FakeWorld>, double, double>("벽 오르기", (p, w) => p.Demonstrate(w, "wall"), 0.55, 0),
                    Tuple.Create<string, Action<Pet, FakeWorld>, double, double>("벽 내려오기", (p, w) => p.Demonstrate(w, "walldown"), 0.55, 0),
                    Tuple.Create<string, Action<Pet, FakeWorld>, double, double>("천장", (p, w) => p.Demonstrate(w, "ceiling"), 0.55, 0),
                    Tuple.Create<string, Action<Pet, FakeWorld>, double, double>("낙하", (p, w) => p.Demonstrate(w, "fall"), 2.6, 0),
                    Tuple.Create<string, Action<Pet, FakeWorld>, double, double>("점프", (p, w) => { p.Y = 720; p.Demonstrate(w, "hop"); }, 0.7, 0),
                    Tuple.Create<string, Action<Pet, FakeWorld>, double, double>("기지개", (p, w) => { p.Y = 720; p.Demonstrate(w, "stretch"); }, 4.2, 0),
                    Tuple.Create<string, Action<Pet, FakeWorld>, double, double>("멍때리기", (p, w) => { p.Y = 720; p.Demonstrate(w, "daydream"); }, 3.6, 0),
                    Tuple.Create<string, Action<Pet, FakeWorld>, double, double>("잠자기", (p, w) => { p.Y = 720; p.Demonstrate(w, "sleep"); }, 3.6, 0),
                };
                int cw = (int)Math.Round(100 * scale), ch = (int)Math.Round(92 * scale), frameCols = 12;
                using (var strip = new Bitmap(cw * frameCols, ch * rowDefs.Count, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(strip))
                using (var font = new Font("Malgun Gothic", 8f * scale))
                {
                    g.Clear(Color.FromArgb(206, 226, 214)); g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    for (int r = 0; r < rowDefs.Count; r++)
                    {
                        var w = new FakeWorld { Rng = new Random(5) };
                        var pet = new Pet(w, c, 640, 720) { Area = w.Areas[0] };
                        pet.SetState(PetState.Pause, w); pet.X = 640; pet.Y = 720;
                        rowDefs[r].Item2(pet, w);
                        g.DrawString(rowDefs[r].Item1, font, Brushes.Black, 2, r * ch + 2);
                        double dt = rowDefs[r].Item3 / frameCols;
                        for (int i = 0; i < frameCols; i++)
                        {
                            for (double s2 = 0; s2 < dt - 1e-6; s2 += 0.02) { w.Now += 0.02; pet.Update(w, 0.02, null, 0); }
                            PointF origin; using (Bitmap b = Pet.Render(pet.GetLook(w.Now), out origin))
                            {
                                float f = Math.Min(1f, Math.Min((cw - 4f) / b.Width, (ch - 14f) / b.Height));
                                g.DrawImage(b, i * cw + (cw - b.Width * f) / 2, r * ch + ch - 2 - b.Height * f, b.Width * f, b.Height * f);
                            }
                        }
                    }
                    strip.Save(Path.Combine(dir, "motion_" + CharacterArt.Infos[c].Key + ".png"), ImageFormat.Png);
                }
            }            Console.WriteLine("saved " + dir);
        }

        /// <summary>실제 창들을 만들어 띄우고 그림으로 남긴다 (레이아웃·실행 오류 확인).</summary>
        internal static int UiTest(string dir)
        {
            Directory.CreateDirectory(dir);
            Paths.StorageOverride = Path.Combine(dir, "data");
            var log = new StringBuilder(); int failures = 0;
            Action<string, Action> step = (name, a) => { try { a(); log.AppendLine("OK   " + name); } catch (Exception e) { failures++; log.AppendLine("FAIL " + name + " " + e); } };
            Action<double> pump = secs => { var end = DateTime.UtcNow.AddSeconds(secs); while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(15); } };
            Action<Form, string> shot = (f, name) =>
            {
                f.Show(); pump(.4);
                using (var b = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height)); b.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png); }
            };
            Action<string> screen = name =>
            {
                Rectangle r = Screen.PrimaryScreen.Bounds;
                using (var b = new Bitmap(r.Width, r.Height)) using (var g = Graphics.FromImage(b)) { g.CopyFromScreen(r.Location, Point.Empty, r.Size); b.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png); }
            };
            PlayOptions.Load(); FriendData.Load(); CharacterArt.Reset(Native.SystemDpiScale());
            App app = null;
            step("앱 생성(펫 4마리)", () => { app = new App(true); app.Spawn(0); app.Spawn(1); app.Spawn(2); app.Spawn(3); pump(1); });
            step("알림 설정 창", () => { var f = new SettingsForm(app); shot(f, "ui_settings"); f.Close(); });
            step("움직임 설정 창", () => { var f = new PlaySettingsForm(app); shot(f, "ui_play"); f.Close(); });
            step("친구 기록 창", () => { var f = new FriendRecordForm(app); shot(f, "ui_record"); f.Close(); });
            step("도우미 창", () => { FriendData.Current.Todos.Add(new TodoItem { Text = "첫 할 일", Done = true }); FriendData.Current.Todos.Add(new TodoItem { Text = "둘째 할 일" }); var f = new FriendPanel(app); shot(f, "ui_helper"); f.Close(); });
            step("정각 카드 + 가장자리 빛", () =>
            {
                var n = new HourlyNotice(app.TrayIconImage); n.SetMessage(App.HourlyMessage(DateTime.Now, true, "13:00", "점심 먹으러 가자!", false, "18:00", "x"));
                n.EnableEdgeGlow(true, 1, 1, 1); n.Show(); pump(1.2); screen("ui_notice_glow"); if (!n.GlowRunning) throw new Exception("glow not running"); n.Close(); pump(.3);
            });
            step("펫 걷기 관찰", () => { pump(20); screen("ui_pets"); });
            step("실제 마우스: 클릭·드래그·던지기", () =>
            {
                PlayOptions.Current.Escape = false; PlayOptions.Current.Idle = false;   // 커서를 가까이 대도 도망가지 않게
                PetWindow target = app.LiveWindows.First(w => w.Pet.State == PetState.Walk || w.Pet.State == PetState.Pause);
                // 서로 겹치면 클릭이 다른 창으로 가므로: 대상은 화면 가운데, 나머지는 멀리 떼어 놓는다
                { int n = 0; Rectangle ar = target.Pet.Area; foreach (var o in app.LiveWindows) { if (o == target) continue; o.Pet.SupportWindow = IntPtr.Zero; o.Pet.Y = ar.Bottom; o.Pet.X = ar.Left + 180 + (n++) * 260; o.Pet.SetState(PetState.Pause, app); }
                  target.Pet.X = ar.Left + ar.Width / 2.0; }
                // 투명한 곳은 클릭이 통과하므로, 지금 그려진 그림에서 불투명한 점(가운데에 가장 가까운 곳)을 찾아 누른다.
                Func<Point> center = () =>
                {
                    PointF o; using (Bitmap b = Pet.Render(target.Pet.GetLook(app.Now), out o))
                    {
                        Point best = new Point(b.Width / 2, b.Height / 2); double bestD = double.MaxValue;
                        for (int y = 0; y < b.Height; y++) for (int x = 0; x < b.Width; x++)
                            if (b.GetPixel(x, y).A > 240) { double d = Math.Pow(x - b.Width / 2.0, 2) + Math.Pow(y - b.Height * .55, 2); if (d < bestD) { bestD = d; best = new Point(x, y); } }
                        return new Point(target.Left + best.X, target.Top + best.Y);
                    }
                };
                Action<int> mouse = flags => mouse_event(flags, 0, 0, 0, UIntPtr.Zero);
                Action<Point> go = p => { Cursor.Position = p; pump(.03); };
                // 클릭
                target.Pet.SupportWindow = IntPtr.Zero; target.Pet.Y = target.Pet.Area.Bottom; target.Pet.SetState(PetState.Pause, app); pump(.25);
                go(center()); pump(.1); mouse(2); pump(.05); mouse(4); pump(.1);
                if (target.Pet.State != PetState.Squish && target.Pet.State != PetState.TouchCry && target.Pet.State != PetState.Hop) throw new Exception("click did not react: " + target.Pet.State);
                pump(2.5);
                // 드래그 → 던지기
                target.Pet.SupportWindow = IntPtr.Zero; target.Pet.Y = target.Pet.Area.Bottom; target.Pet.SetState(PetState.Pause, app); pump(.25);
                Point start = center(); go(start); pump(.1); mouse(2); pump(.05);
                for (int i = 1; i <= 8; i++) { go(new Point(start.X + i * 14, start.Y - i * 5)); pump(.012); }
                if (target.Pet.State != PetState.Drag) throw new Exception("drag not started: " + target.Pet.State);
                mouse(4); pump(.05);
                if (target.Pet.State != PetState.Thrown && target.Pet.State != PetState.Falling) throw new Exception("not thrown: " + target.Pet.State);
                pump(1.2); screen("ui_thrown");
                // 우클릭 메뉴
                app.ShowPetMenu(target, new Point(Screen.PrimaryScreen.WorkingArea.Width / 2, 300)); pump(.6); screen("ui_menu");
                keybd_event(0x1B, 0, 0, UIntPtr.Zero); keybd_event(0x1B, 0, 2, UIntPtr.Zero); pump(.4);
            });
            step("종료", () => { app.Quit(); });
            log.AppendLine(failures == 0 ? "ALL OK" : failures + " FAILED");
            File.WriteAllText(Path.Combine(dir, "uitest_result.txt"), log.ToString(), new UTF8Encoding(true));
            return failures == 0 ? 0 : 1;
        }

        private static KeyValuePair<string, Action<Pet, FakeWorld>> K(string name, Action<Pet, FakeWorld> a)
        { return new KeyValuePair<string, Action<Pet, FakeWorld>>(name, a); }

        /// <summary>저장·친밀도 계산·상태 전이 등을 화면 없이 점검. 실패가 있으면 1 을 돌려준다.</summary>
        internal static int SelfTest(string dir)
        {
            Directory.CreateDirectory(dir);
            Paths.StorageOverride = Path.Combine(dir, "data");
            var log = new StringBuilder(); int failures = 0;
            Action<bool, string> check = (ok, name) => { log.AppendLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; };
            try
            {
                CharacterArt.Reset(1f);
                DesktopSurfaces.TestProvider = () => new List<SurfaceWindow>(); DesktopSurfaces.TestMouse = () => new Point(-9999, -9999);

                // 설정 저장·불러오기
                var s = new Settings { LunchTime = "11:30", LeaveMsg = "가자", NapMinutes = 7, EdgeGlowTheme = 3, QuietWeekdays = true, QuietStart = 22, QuietEnd = 6, Roster = "1,3" };
                check(s.Save(), "설정 저장");
                var s2 = Settings.Load();
                check(s2.LunchTime == "11:30" && s2.LeaveMsg == "가자" && s2.NapMinutes == 7 && s2.EdgeGlowTheme == 3 && s2.QuietStart == 22 && s2.Roster == "1,3", "설정 불러오기 일치");
                check(Settings.ParseTime("9:05") == new TimeSpan(9, 5, 0) && !Settings.ParseTime("25:00").HasValue && !Settings.ParseTime("abc").HasValue, "시간 해석");
                var quiet = new Settings { QuietWeekdays = true, QuietStart = 9, QuietEnd = 18 };
                check(!quiet.HourAllowed(new DateTime(2026, 10, 21, 10, 0, 0)) && quiet.HourAllowed(new DateTime(2026, 10, 24, 10, 0, 0)) && quiet.HourAllowed(new DateTime(2026, 10, 21, 20, 0, 0)), "평일 제외 시간대");
                check(App.HourlyMessage(new DateTime(2026, 10, 21, 12, 0, 0), true, "12:00", "점심!", true, "18:00", "퇴근!") == "지금은 오후 12시야!\n점심!", "정각 문구+점심 병합");
                check(App.HourlyText(new DateTime(2026, 10, 21, 0, 0, 0)) == "지금은 오전 12시야!", "정각 문구 0시");

                var o = new PlayOptions { Walls = false, Speed = 1.5, ClickMode = ClickReaction.Cry, HungerMinutes = 30, PetOpacityPercent = 70, FixedX = 123.5, FixedY = 456.25 };
                PlayOptions.Current = o; check(o.Save(), "움직임 설정 저장");
                PlayOptions.Load(); var l = PlayOptions.Current;
                check(!l.Walls && l.Speed == 1.5 && l.ClickMode == ClickReaction.Cry && l.HungerMinutes == 30 && l.PetOpacityPercent == 70 && l.FixedX == 123.5 && l.FixedY == 456.25, "움직임 설정 불러오기 일치");

                // 친구 기록
                PlayOptions.Current = new PlayOptions { HungerMinutes = 60 };
                var d = FriendData.Current = new FriendData();
                d.AdvanceLife(60);
                check(d.Affinity == 1 && d.TogetherSeconds == 60, "함께한 1분 = 친밀도 +1");
                check(Math.Abs(d.Satiety - (100 - 60 * 80.0 / 3600)) < 1e-6, "포만감 감소량");
                d.Satiety = 90; check(d.TrySnack() && d.Satiety == 100 && d.Affinity == 6, "간식: 포만감 +25(최대 100), 친밀도 +5");
                check(!d.TrySnack(), "배가 꽉 차면 간식 거절");
                d.Affinity = 250; d.CelebratedHundreds = 1; check(d.TakeMilestone() && d.CelebratedHundreds == 2 && !d.TakeMilestone(), "100 단위 꽃다발 한 번씩");
                d.Satiety = 10; check(d.Hungry, "포만감 20 이하 = 배고픔");
                PlayOptions.Current.HungerMinutes = 0; d.Satiety = 100; check(d.Hungry, "0분 = 항상 우는 표정");
                PlayOptions.Current.HungerMinutes = 60;
                d.Todos.Add(new TodoItem { Text = "첫 할 일", Done = true }); d.Todos.Add(new TodoItem { Text = "둘째", Done = false });
                d.Notes = "메모\n두 줄"; d.TimerText = "쉬자"; d.TimerUtc = DateTime.UtcNow.AddMinutes(5); d.Discover("dizzy");
                check(d.Save(), "기록 저장");
                FriendData.Load(); var r = FriendData.Current;
                check(r.Affinity == d.Affinity && r.Todos.Count == 2 && r.Todos[0].Done && !r.Todos[1].Done && r.Notes == "메모\n두 줄" && r.TimerText == "쉬자" && r.Discoveries.Contains("dizzy") && r.TimerUtc > DateTime.UtcNow, "기록 불러오기 일치(할 일·메모·타이머·비밀)");
                d.Save(); check(File.Exists(FriendData.DataPath + ".bak"), "이전 기록 .bak 유지");
                FriendData.Current = new FriendData();

                // 스프라이트
                for (int c = 0; c < CharacterArt.Count; c++)
                {
                    var a = CharacterArt.Get(c);
                    var probe = new Pet(new FakeWorld(), c, 640, 400);
                    PointF org; Pet.Look stand = probe.GetLook(0);
                    using (Bitmap bmp = Pet.Render(stand, out org))
                    {
                        Rectangle ink = CharacterArt.Bounds(bmp);
                        check(a.W > 30 && a.H > 30 && ink.Height > a.H * .8 && ink.Width > a.W * .4 && a.Ceiling.H == a.H, CharacterArt.Infos[c].Name + " 그림 생성");
                    }
                }

                // 상태 전이
                PlayOptions.Current = new PlayOptions { Escape = false, Idle = false, Windows = false };
                var w = new FakeWorld(); var pet = new Pet(w, 1, 640, 40);
                var all = new List<Pet> { pet }; var seen = new HashSet<PetState>();
                Action<double> run = secs => { for (double t = 0; t < secs; t += .03) { w.Now += .03; pet.Update(w, .03, all, 0); seen.Add(pet.State); } };
                run(.3); check(pet.State == PetState.Falling, "시작은 헬리콥터 낙하");
                run(25); check(pet.Y == w.Areas[0].Bottom && pet.State != PetState.Falling, "바닥에 착지");
                pet.Poke(w); check(pet.State == PetState.Squish, "첫 클릭 = 눌리기"); run(1);
                pet.Poke(w); check(pet.State == PetState.TouchCry, "둘째 클릭 = 달달 떨며 울기"); run(3);
                pet.Poke(w); check(pet.State == PetState.Hop, "셋째 클릭 = 폴짝"); run(1.5);
                FriendData.Current.Satiety = 50; pet.Feed(w); check(pet.State == PetState.Eating && FriendData.Current.Satiety == 75, "간식 먹기"); run(4);
                pet.BeginDrag(w, pet.X, pet.Y - 20); check(pet.State == PetState.Drag, "드래그 시작");
                for (int i = 0; i < 6; i++) { w.Now += .016; pet.DragTo(w, pet.X + pet.DragPx + 30, pet.Y + pet.DragPy - 4); }
                pet.EndDrag(w); check(pet.State == PetState.Thrown, "빠르게 놓으면 던지기"); run(.8); run(25);
                check(seen.Contains(PetState.Thrown) && pet.State != PetState.Thrown && pet.Y <= w.Areas[0].Bottom + .1, "던진 뒤 헬리콥터로 착지");
                pet.Demonstrate(w, "dizzy"); check(pet.State == PetState.Dizzy && FriendData.Current.Discoveries.Contains("dizzy"), "어질어질"); run(5);
                pet.SetState(PetState.Pause, w); pet.FallAsleep(w); check(pet.State == PetState.Sleep, "낮잠"); pet.WakeUp(w); check(pet.State == PetState.Waking, "깨어남");
                for (int i = 0; i < 3; i++) { pet.BeginDrag(w, pet.X, pet.Y - 20); for (int k = 0; k < 6; k++) { w.Now += .016; pet.DragTo(w, pet.X + pet.DragPx + 30, pet.Y + pet.DragPy - 4); } pet.EndDrag(w); run(.6); }
                check(seen.Contains(PetState.PilotRise) && FriendData.Current.Discoveries.Contains("pilot"), "비밀 조종사: 10초 안 3번 던지기");
                run(20); check(pet.State == PetState.Ceiling || seen.Contains(PetState.Ceiling), "조종사가 천장까지 올라감");

                // 오래 돌려서 벽·천장·쉬기가 모두 나오는지
                PlayOptions.Current = new PlayOptions { Escape = false, Idle = true };
                var w2 = new FakeWorld(); w2.Rng = new Random(3); var p2 = new Pet(w2, 2, 300, 40); var all2 = new List<Pet> { p2 }; var seen2 = new HashSet<PetState>();
                for (double t = 0; t < 600; t += .03) { w2.Now += .03; p2.Update(w2, .03, all2, 0); seen2.Add(p2.State); }
                check(seen2.Contains(PetState.Walk) && seen2.Contains(PetState.Pause) && seen2.Contains(PetState.Wall) && seen2.Contains(PetState.Ceiling) && seen2.Contains(PetState.Resting), "10분 시뮬레이션: 걷기·쉬기·벽·천장·앉기 모두 등장");
                check(p2.X >= -50 && p2.X <= 1330 && p2.Y >= -50 && p2.Y <= 770, "화면 밖으로 나가지 않음");

                // 위치 고정
                PlayOptions.Current = new PlayOptions { PositionFixed = true };
                var w3 = new FakeWorld(); var p3 = new Pet(w3, 0, 500, 300); var all3 = new List<Pet> { p3 };
                double px = p3.X, py = p3.Y; for (double t = 0; t < 20; t += .03) { w3.Now += .03; p3.Update(w3, .03, all3, 0); }
                check(Math.Abs(p3.X - px) < 1 && Math.Abs(p3.Y - py) < 1 || p3.Y <= w3.Areas[0].Bottom, "위치 고정 중에는 자동 이동 없음");

                // 두 마리 충돌 분리
                PlayOptions.Current = new PlayOptions { Escape = false, Idle = false, Windows = false };
                var w4 = new FakeWorld(); var a1 = new Pet(w4, 0, 500, 700) { Area = w4.Areas[0] }; var a2 = new Pet(w4, 1, 505, 700) { Area = w4.Areas[0] };
                a1.SetState(PetState.Pause, w4); a2.SetState(PetState.Pause, w4); a1.Y = a2.Y = 720;
                App.ResolveCompanionContacts(w4, new List<Pet> { a1, a2 }, null);
                check(a2.ContactBody(0).Left - a1.ContactBody(0).Right > -1 || Math.Abs(a1.X - a2.X) > a1.Art.W * .6, "겹친 친구는 서로 떨어짐");
            }
            catch (Exception e) { log.AppendLine("EXCEPTION " + e); failures++; }
            log.AppendLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            File.WriteAllText(Path.Combine(dir, "selftest_result.txt"), log.ToString(), new UTF8Encoding(true));
            Console.Write(log.ToString());
            return failures == 0 ? 0 : 1;
        }
    }
}
