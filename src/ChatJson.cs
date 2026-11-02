using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BikiniFriends
{
    /// <summary>Ollama 와 주고받는 JSON 만 다루는 아주 작은 파서(외부 참조 없이 EXE 하나로 유지하려고 직접 만들었다).</summary>
    internal static class Json
    {
        internal static string Quote(string s)
        {
            var b = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                        else b.Append(c);
                        break;
                }
            }
            return b.Append('"').ToString();
        }

        /// <summary>Dictionary&lt;string, object&gt; · List&lt;object&gt; · string · double · bool · null 로 돌려준다. 잘못된 JSON 이면 null.</summary>
        internal static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try { int i = 0; object v = Value(text, ref i); return v; }
            catch { return null; }
        }

        internal static Dictionary<string, object> Object(object o) { return o as Dictionary<string, object>; }

        internal static object Get(object o, string key)
        {
            var d = o as Dictionary<string, object>; object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        internal static string Str(object o, string key) { return Get(o, key) as string; }
        internal static bool Flag(object o, string key) { return Get(o, key) is bool && (bool)Get(o, key); }

        private static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException();
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++; Skip(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Skip(s, ref i); string k = Text(s, ref i); Skip(s, ref i);
                    if (s[i++] != ':') throw new FormatException();
                    d[k] = Value(s, ref i); Skip(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException();
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++; Skip(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i)); Skip(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException();
                }
            }
            if (c == '"') return Text(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double n;
            if (i == st || !double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out n)) throw new FormatException();
            return n;
        }

        private static string Text(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException();
            i++; var b = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') return b.ToString();
                if (c != '\\') { b.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': b.Append('\n'); break;
                    case 'r': b.Append('\r'); break;
                    case 't': b.Append('\t'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'u': b.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: b.Append(e); break;
                }
            }
        }
    }
}