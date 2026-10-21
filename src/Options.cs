using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace BikiniFriends
{
    /// <summary>알림·낮잠·정각 알림 설정 (비키니친구들_설정.ini).</summary>
    internal sealed class Settings
    {
        internal const string FileName = "비키니친구들_설정.ini";
        public bool LunchOn = true; public string LunchTime = "12:00"; public string LunchMsg = "점심 먹으러 가자!";
        public bool LeaveOn = true; public string LeaveTime = "18:00"; public string LeaveMsg = "퇴근이다! 오늘도 수고했어";
        public bool NapOn = true; public int NapMinutes = 10;
        public bool HourlyOn = true, HourlyWindow = true;
        public bool EdgeGlow = true; public int EdgeGlowTheme = 0, EdgeGlowWidth = 1, EdgeGlowStrength = 1;
        public bool QuietWeekdays; public int QuietStart = 9, QuietEnd = 18;
        public string Roster = "";   // 마지막에 불러 둔 친구들(캐릭터 번호, 쉼표 구분)

        internal static string FilePath { get { return Path.Combine(Paths.Dir, FileName); } }

        internal static Dictionary<string, string> ReadIni(string path)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string t = raw.Trim();
                if (t.Length == 0 || t[0] == ';' || t[0] == '#' || t[0] == '[') continue;
                int i = t.IndexOf('='); if (i > 0) d[t.Substring(0, i).Trim()] = t.Substring(i + 1).Trim();
            }
            return d;
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var d = ReadIni(FilePath); string v;
                if (d.TryGetValue("LunchOn", out v)) s.LunchOn = v != "0";
                if (d.TryGetValue("LunchTime", out v) && ParseTime(v).HasValue) s.LunchTime = v;
                if (d.TryGetValue("LunchMsg", out v)) s.LunchMsg = v;
                if (d.TryGetValue("LeaveOn", out v)) s.LeaveOn = v != "0";
                if (d.TryGetValue("LeaveTime", out v) && ParseTime(v).HasValue) s.LeaveTime = v;
                if (d.TryGetValue("LeaveMsg", out v)) s.LeaveMsg = v;
                if (d.TryGetValue("NapOn", out v)) s.NapOn = v != "0";
                int n;
                if (d.TryGetValue("NapMinutes", out v) && int.TryParse(v, out n)) s.NapMinutes = Math.Max(1, Math.Min(600, n));
                if (d.TryGetValue("HourlyOn", out v)) s.HourlyOn = v != "0";
                if (d.TryGetValue("HourlyWindow", out v)) s.HourlyWindow = v != "0";
                if (d.TryGetValue("EdgeGlow", out v)) s.EdgeGlow = v != "0";
                if (d.TryGetValue("EdgeGlowTheme", out v) && int.TryParse(v, out n)) s.EdgeGlowTheme = Math.Max(0, Math.Min(3, n));
                if (d.TryGetValue("EdgeGlowWidth", out v) && int.TryParse(v, out n)) s.EdgeGlowWidth = Math.Max(0, Math.Min(2, n));
                if (d.TryGetValue("EdgeGlowStrength", out v) && int.TryParse(v, out n)) s.EdgeGlowStrength = Math.Max(0, Math.Min(2, n));
                if (d.TryGetValue("QuietWeekdays", out v)) s.QuietWeekdays = v == "1";
                if (d.TryGetValue("QuietStart", out v) && int.TryParse(v, out n)) s.QuietStart = Math.Max(0, Math.Min(23, n));
                if (d.TryGetValue("QuietEnd", out v) && int.TryParse(v, out n)) s.QuietEnd = Math.Max(0, Math.Min(23, n));
                if (d.TryGetValue("Roster", out v)) s.Roster = v;
            }
            catch { }
            return s;
        }

        public bool Save()
        {
            var b = new StringBuilder();
            b.AppendLine("; 비키니친구들 설정 파일. 지우면 처음 설정으로 돌아가요.");
            b.AppendLine("[알림]");
            b.AppendLine("LunchOn=" + (LunchOn ? "1" : "0")); b.AppendLine("LunchTime=" + LunchTime); b.AppendLine("LunchMsg=" + OneLine(LunchMsg));
            b.AppendLine("LeaveOn=" + (LeaveOn ? "1" : "0")); b.AppendLine("LeaveTime=" + LeaveTime); b.AppendLine("LeaveMsg=" + OneLine(LeaveMsg));
            b.AppendLine("[낮잠]"); b.AppendLine("NapOn=" + (NapOn ? "1" : "0")); b.AppendLine("NapMinutes=" + NapMinutes);
            b.AppendLine("[정각]"); b.AppendLine("HourlyOn=" + (HourlyOn ? "1" : "0")); b.AppendLine("HourlyWindow=" + (HourlyWindow ? "1" : "0"));
            b.AppendLine("EdgeGlow=" + (EdgeGlow ? "1" : "0")); b.AppendLine("EdgeGlowTheme=" + EdgeGlowTheme);
            b.AppendLine("EdgeGlowWidth=" + EdgeGlowWidth); b.AppendLine("EdgeGlowStrength=" + EdgeGlowStrength);
            b.AppendLine("QuietWeekdays=" + (QuietWeekdays ? "1" : "0")); b.AppendLine("QuietStart=" + QuietStart); b.AppendLine("QuietEnd=" + QuietEnd);
            b.AppendLine("[친구]"); b.AppendLine("Roster=" + Roster);
            try { Directory.CreateDirectory(Paths.Dir); File.WriteAllText(FilePath, b.ToString(), new UTF8Encoding(true)); return true; }
            catch { return false; }
        }

        private static string OneLine(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); }

        public static TimeSpan? ParseTime(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            string[] p = v.Trim().Split(':'); int h, m;
            if (p.Length != 2 || !int.TryParse(p[0], out h) || !int.TryParse(p[1], out m) || h < 0 || h > 23 || m < 0 || m > 59) return null;
            return new TimeSpan(h, m, 0);
        }

        /// <summary>정각 알림을 지금 보여줘도 되는지 (평일 제외 시간대 반영).</summary>
        internal bool HourAllowed(DateTime now)
        {
            if (!HourlyOn) return false;
            if (!QuietWeekdays || now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday) return true;
            bool quiet = QuietStart <= QuietEnd ? (now.Hour >= QuietStart && now.Hour <= QuietEnd) : (now.Hour >= QuietStart || now.Hour <= QuietEnd);
            return !quiet;
        }
    }

    internal enum ClickReaction { Cycle, Random, Squash, Cry, Hop, None }

    /// <summary>움직임·반응·도우미 창 설정 (비키니친구들_움직임.ini).</summary>
    internal sealed class PlayOptions
    {
        internal bool Walls = true, Ceiling = true, Windows = true, Idle = true;
        internal bool Escape = true, Follow = false, Focus = false, WorkFocus = false, Fullscreen = false;
        internal bool HungerCry = true, Secrets = true, ShortRest = false, ShowAffinity = false;
        internal bool PositionFixed = false, MemoTopMost = false;
        internal double Speed = 1.0, FallSpeed = 1.2, SlideSpeed = 1.0, EscapeDelay = .12;
        internal int HungerMinutes = 60;
        internal int PetOpacityPercent = 100, MemoOpacityPercent = 100;
        internal ClickReaction ClickMode = ClickReaction.Cycle;
        internal int HelperBackgroundArgb = Color.FromArgb(255, 252, 249).ToArgb();
        internal int HelperForegroundArgb = Color.FromArgb(68, 50, 50).ToArgb();
        internal double FixedX = double.NaN, FixedY = double.NaN;

        internal static PlayOptions Current = new PlayOptions();
        internal const string Name = "비키니친구들_움직임.ini";
        internal static string FilePath { get { return Path.Combine(Paths.Dir, Name); } }
        internal byte PetAlpha { get { return (byte)Math.Round(Math.Max(20, Math.Min(100, PetOpacityPercent)) * 255.0 / 100); } }

        internal static bool IsFocus(DateTime now)
        {
            var o = Current;
            return o.Focus || (o.WorkFocus && now.DayOfWeek != DayOfWeek.Saturday && now.DayOfWeek != DayOfWeek.Sunday && now.Hour >= 9 && now.Hour < 18);
        }

        private static double ReadNumber(string text, double fallback, double min, double max)
        {
            double v;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || double.IsNaN(v) || double.IsInfinity(v)) return fallback;
            return Math.Max(min, Math.Min(max, v));
        }

        internal static void Load()
        {
            var o = Current = new PlayOptions();
            try
            {
                if (!File.Exists(FilePath)) return;
                var d = Settings.ReadIni(FilePath); string v; int n;
                Func<string, bool, bool> flag = (k, def) => d.TryGetValue(k, out v) ? v != "0" : def;
                o.Walls = flag("Walls", o.Walls); o.Ceiling = flag("Ceiling", o.Ceiling); o.Windows = flag("Windows", o.Windows); o.Idle = flag("Idle", o.Idle);
                o.Escape = flag("Escape", o.Escape); o.Follow = flag("Follow", o.Follow); o.Focus = flag("Focus", o.Focus); o.WorkFocus = flag("WorkFocus", o.WorkFocus);
                o.Fullscreen = flag("Fullscreen", o.Fullscreen); o.HungerCry = flag("HungerCry", o.HungerCry); o.Secrets = flag("Secrets", o.Secrets);
                o.ShortRest = flag("ShortRest", o.ShortRest); o.ShowAffinity = flag("ShowAffinity", o.ShowAffinity);
                o.PositionFixed = flag("PositionFixed", o.PositionFixed); o.MemoTopMost = flag("MemoTopMost", o.MemoTopMost);
                if (d.TryGetValue("Speed", out v)) o.Speed = ReadNumber(v, o.Speed, .5, 2);
                if (d.TryGetValue("FallSpeed", out v)) o.FallSpeed = ReadNumber(v, o.FallSpeed, .5, 3);
                if (d.TryGetValue("SlideSpeed", out v)) o.SlideSpeed = ReadNumber(v, o.SlideSpeed, .5, 3);
                if (d.TryGetValue("EscapeDelay", out v)) o.EscapeDelay = ReadNumber(v, o.EscapeDelay, .05, 1);
                if (d.TryGetValue("HungerMinutes", out v) && int.TryParse(v, out n)) o.HungerMinutes = Math.Max(0, Math.Min(360, n));
                if (d.TryGetValue("PetOpacityPercent", out v) && int.TryParse(v, out n)) o.PetOpacityPercent = Math.Max(20, Math.Min(100, n));
                if (d.TryGetValue("MemoOpacityPercent", out v) && int.TryParse(v, out n)) o.MemoOpacityPercent = Math.Max(20, Math.Min(100, n));
                ClickReaction mode;
                if (d.TryGetValue("ClickMode", out v) && Enum.TryParse(v, true, out mode) && Enum.IsDefined(typeof(ClickReaction), mode)) o.ClickMode = mode;
                foreach (string key in new[] { "HelperBackground", "HelperForeground" })
                {
                    int rgb;
                    if (d.TryGetValue(key, out v) && v.Length == 6 && int.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
                    {
                        int c = Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255).ToArgb();
                        if (key == "HelperBackground") o.HelperBackgroundArgb = c; else o.HelperForegroundArgb = c;
                    }
                }
                double x;
                if (d.TryGetValue("FixedX", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out x) && !double.IsNaN(x) && !double.IsInfinity(x)) o.FixedX = x;
                if (d.TryGetValue("FixedY", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out x) && !double.IsNaN(x) && !double.IsInfinity(x)) o.FixedY = x;
            }
            catch { }
        }

        internal bool Save()
        {
            Func<bool, string> b = x => x ? "1" : "0";
            Func<double, string> f = x => x.ToString("0.00", CultureInfo.InvariantCulture);
            var t = new StringBuilder("; 비키니친구들 움직임 설정\r\n");
            t.Append("Walls=" + b(Walls) + "\r\nCeiling=" + b(Ceiling) + "\r\nWindows=" + b(Windows) + "\r\nIdle=" + b(Idle) + "\r\n");
            t.Append("Escape=" + b(Escape) + "\r\nFollow=" + b(Follow) + "\r\nFocus=" + b(Focus) + "\r\nWorkFocus=" + b(WorkFocus) + "\r\nFullscreen=" + b(Fullscreen) + "\r\n");
            t.Append("HungerCry=" + b(HungerCry) + "\r\nSecrets=" + b(Secrets) + "\r\nShortRest=" + b(ShortRest) + "\r\nShowAffinity=" + b(ShowAffinity) + "\r\n");
            t.Append("PositionFixed=" + b(PositionFixed) + "\r\nMemoTopMost=" + b(MemoTopMost) + "\r\n");
            t.Append("Speed=" + f(Speed) + "\r\nFallSpeed=" + f(FallSpeed) + "\r\nSlideSpeed=" + f(SlideSpeed) + "\r\nClickMode=" + ClickMode + "\r\n");
            t.Append("EscapeDelay=" + f(EscapeDelay) + "\r\nHungerMinutes=" + HungerMinutes + "\r\n");
            t.Append("PetOpacityPercent=" + PetOpacityPercent + "\r\nMemoOpacityPercent=" + MemoOpacityPercent + "\r\n");
            t.Append("FixedX=" + FixedX.ToString("R", CultureInfo.InvariantCulture) + "\r\nFixedY=" + FixedY.ToString("R", CultureInfo.InvariantCulture) + "\r\n");
            t.Append("HelperBackground=" + (HelperBackgroundArgb & 0xffffff).ToString("X6", CultureInfo.InvariantCulture) + "\r\n");
            t.Append("HelperForeground=" + (HelperForegroundArgb & 0xffffff).ToString("X6", CultureInfo.InvariantCulture) + "\r\n");
            try { Directory.CreateDirectory(Paths.Dir); File.WriteAllText(FilePath, t.ToString(), new UTF8Encoding(true)); return true; }
            catch { return false; }
        }
    }

    internal sealed class TodoItem
    {
        internal string Text; internal bool Done;
        public override string ToString() { return Text; }
    }

    /// <summary>친구 기록: 친밀도·함께한 시간·포만감·메모·할 일·타이머·발견한 비밀 (비키니친구들_기록.xml).</summary>
    internal sealed class FriendData
    {
        internal const double SnackPortion = 25;
        internal static string Name = "비키니친구들_기록.xml";
        internal static FriendData Current = new FriendData();
        internal static string DataPath { get { return Path.Combine(Paths.Dir, Name); } }

        internal long Affinity;
        internal double TogetherSeconds, Satiety = 100;
        internal long CelebratedHundreds;
        internal HashSet<string> Discoveries = new HashSet<string>();
        internal List<TodoItem> Todos = new List<TodoItem>();
        internal string Notes = "";
        internal DateTime TimerUtc = DateTime.MinValue;
        internal string TimerText = "잠깐 쉬어가자!";
        private DateTime lastSave = DateTime.MinValue;

        internal bool CanSnack { get { return Satiety < 100; } }
        internal bool Hungry { get { return PlayOptions.Current.HungerCry && (PlayOptions.Current.HungerMinutes == 0 || Satiety <= 20); } }

        internal static double Number(XmlNode node, string name, double fallback)
        {
            double v; var a = node.Attributes[name];
            return a != null && double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v) ? v : fallback;
        }

        internal static void Load()
        {
            Current = new FriendData();
            var paths = new List<string>();
            if (File.Exists(DataPath)) paths.Add(DataPath);
            if (File.Exists(DataPath + ".bak")) paths.Add(DataPath + ".bak");
            foreach (string path in paths)
            {
                try
                {
                    var doc = new XmlDocument { XmlResolver = null };
                    using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) doc.Load(reader);
                    XmlElement root = doc.DocumentElement; if (root == null || root.Name != "friend") continue;
                    var d = new FriendData(); long l;
                    if (long.TryParse(root.GetAttribute("affinity"), NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) d.Affinity = Math.Max(0, l);
                    d.TogetherSeconds = Math.Max(0, Number(root, "togetherSeconds", 0));
                    d.Satiety = Math.Max(0, Math.Min(100, Number(root, "satiety", 100)));
                    d.CelebratedHundreds = d.Affinity / 100;
                    if (long.TryParse(root.GetAttribute("celebratedHundreds"), out l)) d.CelebratedHundreds = Math.Max(0, Math.Min(d.Affinity / 100, l));
                    var notes = root.SelectSingleNode("notes"); if (notes != null) d.Notes = notes.InnerText;
                    var timer = root.SelectSingleNode("timer");
                    if (timer != null)
                    {
                        var until = timer.Attributes["until"]; DateTime dt;
                        if (until != null && DateTime.TryParse(until.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt)) d.TimerUtc = dt.ToUniversalTime();
                        if (timer.InnerText.Length > 0) d.TimerText = timer.InnerText;
                    }
                    foreach (XmlNode t in root.SelectNodes("tasks/task"))
                        d.Todos.Add(new TodoItem { Text = t.InnerText, Done = t.Attributes["done"] != null && t.Attributes["done"].Value == "1" });
                    foreach (XmlNode egg in root.SelectNodes("discoveries/egg")) d.Discoveries.Add(egg.InnerText);
                    Current = d; return;
                }
                catch { }
            }
        }

        internal bool Save()
        {
            var doc = new XmlDocument(); var root = doc.CreateElement("friend"); doc.AppendChild(root);
            root.SetAttribute("schema", "1"); root.SetAttribute("affinity", Affinity.ToString(CultureInfo.InvariantCulture));
            root.SetAttribute("togetherSeconds", TogetherSeconds.ToString("R", CultureInfo.InvariantCulture));
            root.SetAttribute("satiety", Satiety.ToString("R", CultureInfo.InvariantCulture));
            root.SetAttribute("celebratedHundreds", CelebratedHundreds.ToString(CultureInfo.InvariantCulture));
            var notes = doc.CreateElement("notes"); notes.InnerText = Notes ?? ""; root.AppendChild(notes);
            var tasks = doc.CreateElement("tasks"); root.AppendChild(tasks);
            foreach (var t in Todos) { var e = doc.CreateElement("task"); e.InnerText = t.Text; e.SetAttribute("done", t.Done ? "1" : "0"); tasks.AppendChild(e); }
            var timer = doc.CreateElement("timer"); timer.InnerText = TimerText ?? ""; timer.SetAttribute("until", TimerUtc.ToString("o")); root.AppendChild(timer);
            var eggs = doc.CreateElement("discoveries"); root.AppendChild(eggs);
            foreach (string n in Discoveries.OrderBy(x => x)) { var e = doc.CreateElement("egg"); e.InnerText = n; eggs.AppendChild(e); }
            string tmp = null;
            try
            {
                Directory.CreateDirectory(Paths.Dir); string dest = DataPath;
                tmp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp"; doc.Save(tmp);
                if (File.Exists(dest))
                {
                    try { File.Replace(tmp, dest, dest + ".bak"); }
                    catch { File.Copy(dest, dest + ".bak", true); File.Copy(tmp, dest, true); File.Delete(tmp); }
                }
                else File.Move(tmp, dest);
                lastSave = DateTime.UtcNow; return true;
            }
            catch { try { if (tmp != null) File.Delete(tmp); } catch { } return false; }
        }

        internal void Like(int points)
        {
            if (points <= 0) return;
            Affinity = Affinity > long.MaxValue - points ? long.MaxValue : Affinity + points;
            if ((DateTime.UtcNow - lastSave).TotalSeconds > 10) Save();
        }

        /// <summary>실행 중인 시간만큼 함께한 시간·포만감·친밀도를 갱신 (1분마다 친밀도 +1).</summary>
        internal void AdvanceLife(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
            double oldMinutes = Math.Floor(TogetherSeconds / 60);
            TogetherSeconds += seconds;
            Satiety = Math.Max(0, Satiety - seconds * 80.0 / (Math.Max(1, PlayOptions.Current.HungerMinutes) * 60.0));
            int points = (int)Math.Min(int.MaxValue, Math.Floor(TogetherSeconds / 60) - oldMinutes);
            if (points > 0) Like(points);
        }

        internal bool TrySnack()
        {
            if (!CanSnack) return false;
            Satiety = Math.Min(100, Satiety + SnackPortion); Like(5); Save(); return true;
        }

        internal bool TakeMilestone()
        {
            if (Affinity / 100 <= CelebratedHundreds) return false;
            CelebratedHundreds++; Save(); return true;
        }

        internal void Discover(string name) { if (Discoveries.Add(name)) Save(); }

        internal static string FormatTogether(double seconds)
        {
            long total = Math.Max(0, (long)(seconds / 60));
            return (total / 60).ToString("N0") + "시간 " + (total % 60) + "분";
        }

        internal void PollTimer(App app)
        {
            if (TimerUtc == DateTime.MinValue || DateTime.UtcNow < TimerUtc) return;
            TimerUtc = DateTime.MinValue; Save();
            var notice = new HourlyNotice(app.TrayIconImage) { Text = "비키니친구들 · 타이머" };
            notice.SetMessage(string.IsNullOrWhiteSpace(TimerText) ? "설정한 시간이 됐어!" : TimerText); notice.Show();
            app.Announce(string.IsNullOrWhiteSpace(TimerText) ? "설정한 시간이 됐어!" : TimerText);
        }
    }
}
