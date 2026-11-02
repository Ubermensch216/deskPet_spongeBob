using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BikiniFriends
{
    /// <summary>AI 대화 설정 (비키니친구들_대화.ini). 로컬 Ollama 와만 이야기하는 것이 기본이다.</summary>
    internal sealed class ChatOptions
    {
        internal bool Enabled = true;
        internal string Endpoint = "http://localhost:11434";
        internal string Model = "gemma4:e2b";
        internal string KeepAlive = "30m";
        internal int Mode = 0;                // 대화 모드(ChatMode.All 의 번호): 친구·공감·설명·장난·간결
        internal bool Hotkey = true;          // 단축키로 말 걸기
        internal uint HotkeyMods = ChatHotkey.DefaultMods, HotkeyKey = ChatHotkey.DefaultKey;   // 기본 Ctrl+Alt+B
        internal bool Sound = false;          // 말할 때 뽁뽁 소리
        internal bool Memory = true;          // 대화에서 알게 된 사실을 기억하기
        internal bool Chips = true;           // 말 끝에 답장 추천 칩 보여 주기
        internal bool Diary = true;           // 대화가 끝나면 친구가 일기 한 줄 쓰기
        internal bool Proactive = false;      // 친구가 먼저 말 걸기
        internal int ProactiveMinutes = 40;   // 먼저 말 거는 평균 간격(분)

        /// <summary>말 걸기 단축키 글(메뉴 표시용 "Ctrl+Alt+B").</summary>
        internal string HotkeyText { get { return ChatHotkey.Text(HotkeyMods, HotkeyKey, "+"); } }

        internal static ChatOptions Current = new ChatOptions();
        internal const string Name = "비키니친구들_대화.ini";
        internal static string FilePath { get { return Path.Combine(Paths.Dir, Name); } }

        internal static void Load()
        {
            var o = Current = new ChatOptions();
            try
            {
                if (!File.Exists(FilePath)) return;
                var d = Settings.ReadIni(FilePath); string v; int n;
                Func<string, bool, bool> flag = (k, def) => d.TryGetValue(k, out v) ? v != "0" : def;
                o.Enabled = flag("Enabled", o.Enabled); o.Hotkey = flag("Hotkey", o.Hotkey); o.Sound = flag("Sound", o.Sound);
                o.Memory = flag("Memory", o.Memory); o.Chips = flag("Chips", o.Chips); o.Diary = flag("Diary", o.Diary); o.Proactive = flag("Proactive", o.Proactive);
                uint hm, hk; string why;
                if (d.TryGetValue("HotkeyMods", out v) && uint.TryParse(v, out hm) && d.TryGetValue("HotkeyKey", out v) && uint.TryParse(v, out hk) && ChatHotkey.Valid(hm, hk, out why)) { o.HotkeyMods = hm; o.HotkeyKey = hk; }
                if (d.TryGetValue("Mode", out v) && int.TryParse(v, out n)) o.Mode = n >= 0 && n < ChatMode.All.Length ? n : 0;
                if (d.TryGetValue("Endpoint", out v) && v.Length > 0) o.Endpoint = v.TrimEnd('/');
                if (d.TryGetValue("Model", out v) && v.Length > 0) o.Model = v;
                if (d.TryGetValue("KeepAlive", out v) && v.Length > 0) o.KeepAlive = v;
                if (d.TryGetValue("ProactiveMinutes", out v) && int.TryParse(v, out n)) o.ProactiveMinutes = Math.Max(5, Math.Min(480, n));
            }
            catch { }
        }

        internal bool Save()
        {
            Func<bool, string> b = x => x ? "1" : "0";
            var t = new StringBuilder("; 비키니친구들 AI 대화 설정 (로컬 Ollama)\r\n");
            t.Append("Enabled=" + b(Enabled) + "\r\nEndpoint=" + Endpoint + "\r\nModel=" + Model + "\r\nKeepAlive=" + KeepAlive + "\r\n");
            t.Append("Mode=" + Mode + "\r\nHotkey=" + b(Hotkey) + "\r\nHotkeyMods=" + HotkeyMods + "\r\nHotkeyKey=" + HotkeyKey + "\r\nSound=" + b(Sound) + "\r\nMemory=" + b(Memory) + "\r\nChips=" + b(Chips) + "\r\nDiary=" + b(Diary) + "\r\n");
            t.Append("Proactive=" + b(Proactive) + "\r\nProactiveMinutes=" + ProactiveMinutes + "\r\n");
            try { Directory.CreateDirectory(Paths.Dir); File.WriteAllText(FilePath, t.ToString(), new UTF8Encoding(true)); return true; }
            catch { return false; }
        }
    }

    /// <summary>대화에서 알게 된 사용자 이야기(비키니친구들_기억.txt, 한 줄에 하나). 사용자가 보고 지울 수 있다.</summary>
    internal static class ChatMemory
    {
        internal const int MaxFacts = 30;
        internal const string Name = "비키니친구들_기억.txt";
        private static List<string> facts;
        internal static string FilePath { get { return Path.Combine(Paths.Dir, Name); } }

        internal static List<string> Facts
        {
            get
            {
                if (facts != null) return facts;
                facts = new List<string>();
                try { if (File.Exists(FilePath)) facts.AddRange(File.ReadAllLines(FilePath, Encoding.UTF8).Select(l => l.Trim()).Where(l => l.Length > 0)); }
                catch { }
                return facts;
            }
        }

        internal static void Reload() { facts = null; }

        internal static bool Save()
        {
            try { Directory.CreateDirectory(Paths.Dir); File.WriteAllLines(FilePath, Facts.ToArray(), new UTF8Encoding(true)); return true; }
            catch { return false; }
        }

        /// <summary>같은 이야기가 이미 있으면 넣지 않는다. 가득 차면 가장 오래된 것부터 잊는다.</summary>
        internal static int Add(IEnumerable<string> newFacts)
        {
            int added = 0;
            foreach (string raw in newFacts)
            {
                string f = (raw ?? "").Trim().TrimStart('-', '*', '·', ' ').Trim();
                if (f.Length < 3 || f.Length > 80 || f == "없음") continue;
                if (Facts.Any(x => x == f || x.Contains(f) || f.Contains(x))) continue;
                Facts.Add(f); added++;
            }
            while (Facts.Count > MaxFacts) Facts.RemoveAt(0);
            if (added > 0) Save();
            return added;
        }

        internal static void RemoveAt(int index) { if (index >= 0 && index < Facts.Count) { Facts.RemoveAt(index); Save(); } }
        internal static void Clear() { Facts.Clear(); Save(); }
    }

    /// <summary>친구의 일기장(비키니친구들_일기.txt, 한 줄에 "날짜 시각 [탭] 문장"). 대화가 끝날 때 한 줄 쓰고, 사용자가 보고 지울 수 있다.</summary>
    internal static class ChatDiary
    {
        internal struct Entry { internal DateTime When; internal string Text; }

        internal const int MaxEntries = 200;
        internal const string Name = "비키니친구들_일기.txt";
        private static List<Entry> entries;
        internal static string FilePath { get { return Path.Combine(Paths.Dir, Name); } }

        /// <summary>오래된 것부터.</summary>
        internal static List<Entry> Entries
        {
            get
            {
                if (entries != null) return entries;
                entries = new List<Entry>();
                try
                {
                    if (File.Exists(FilePath))
                        foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                        {
                            int tab = line.IndexOf('\t'); DateTime when;
                            if (tab > 0 && DateTime.TryParseExact(line.Substring(0, tab), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out when) && line.Length > tab + 1)
                                entries.Add(new Entry { When = when, Text = line.Substring(tab + 1).Trim() });
                        }
                }
                catch { }
                return entries;
            }
        }

        internal static void Reload() { entries = null; }

        internal static bool Save()
        {
            try
            {
                Directory.CreateDirectory(Paths.Dir);
                File.WriteAllLines(FilePath, Entries.Select(e => e.When.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "\t" + e.Text).ToArray(), new UTF8Encoding(true));
                return true;
            }
            catch { return false; }
        }

        /// <summary>한 줄 일기를 덧붙인다. 너무 짧거나 길거나 같은 날 똑같은 문장이면 넣지 않는다.</summary>
        internal static bool Add(DateTime when, string text)
        {
            text = (text ?? "").Replace('\t', ' ').Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length < 6 || text.Length > 140) return false;
            if (Entries.Any(e => e.When.Date == when.Date && e.Text == text)) return false;
            Entries.Add(new Entry { When = when, Text = text });
            while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
            Save();
            return true;
        }

        internal static void RemoveAt(int index) { if (index >= 0 && index < Entries.Count) { Entries.RemoveAt(index); Save(); } }
        internal static void Clear() { Entries.Clear(); Save(); }
    }
}