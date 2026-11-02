using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BikiniFriends
{
    internal enum LineStyle { None = 0, Bullet = 1, Checklist = 2, Heading1 = 3, Heading2 = 4, Heading3 = 5 }

    /// <summary>
    /// 마크다운 서식 명령(목록 · 체크리스트 · 제목 · 링크 · 코드)과 Enter 이어쓰기, Ctrl+Enter 체크박스.
    /// 메모는 마크다운 원문으로 저장되고, 줄 머리의 기호(- [ ] · # · 1.)는 계속 눈에 보이는 글자다 —
    /// 체크박스 토글과 제목 추출이 읽는 것이 바로 그 글자이기 때문.
    /// </summary>
    internal static class MarkdownEditing
    {
        private static readonly Regex LinePrefix = new Regex(@"^(?<indent>[ \t]*)(?<marker>(?:[-*+][ \t]+\[[ xX]\][ \t]+)|(?:[-*+][ \t]+)|(?:\d+[.)][ \t]+)|(?:\#{1,6}[ \t]+))?", RegexOptions.CultureInvariant);
        private static readonly Regex ChecklistLine = new Regex(@"^(?<indent>[ \t]*)[-*+][ \t]+\[(?<state>[ xX])\][ \t]*(?<text>.*)$", RegexOptions.CultureInvariant);

        internal static NoteTextState ToggleInlineCode(NoteTextState state) { return ToggleWrap(state, "`", "`"); }

        /// <summary>선택을 [글](주소) 로 감싼다. 주소를 모르면 캐럿이 괄호 안에 놓여 바로 붙여넣을 수 있다.</summary>
        internal static NoteTextState InsertLink(NoteTextState state, string url = null)
        {
            var c = state.Clamped(); string label = c.SelectionLength > 0 ? c.SelectedText : ""; string target = url ?? "";
            string replacement = "[" + label + "](" + target + ")";
            string text = c.Text.Remove(c.SelectionStart, c.SelectionLength).Insert(c.SelectionStart, replacement);
            return string.IsNullOrEmpty(target) ? new NoteTextState(text, c.SelectionStart + label.Length + 3, 0) : new NoteTextState(text, c.SelectionStart, replacement.Length);
        }

        /// <summary>
        /// 선택에 닿은 모든 줄에 줄 스타일을 켜거나 끈다. 전부 이미 그 스타일이면 끄고, 아니면 전부 켠다 —
        /// 줄마다 따로 뒤집으면 섞인 선택이 "다르게 섞인" 선택이 되어 명령이 안 먹은 것처럼 보인다.
        /// </summary>
        internal static NoteTextState ApplyLineStyle(NoteTextState state, LineStyle style)
        {
            var c = state.Clamped();
            int blockStart = LineStartAt(c.Text, c.SelectionStart), blockEnd = LineEndAt(c.Text, c.SelectionEnd);
            string block = c.Text.Substring(blockStart, blockEnd - blockStart); string[] lines = block.Split('\n');
            var written = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            bool already = written.Count > 0 && written.All(l => StyleOf(l) == style);
            var target = already ? LineStyle.None : style; bool blockIsBlank = written.Count == 0;
            var rebuilt = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) rebuilt.Append('\n');
                rebuilt.Append(string.IsNullOrWhiteSpace(lines[i]) && !blockIsBlank ? lines[i] : SetStyle(lines[i], target));
            }
            string replacement = rebuilt.ToString();
            string text = c.Text.Remove(blockStart, block.Length).Insert(blockStart, replacement);
            if (c.SelectionLength > 0) return new NoteTextState(text, blockStart, replacement.Length);
            string oldLine = lines[0], newLine = SetStyle(oldLine, target);
            int caret = Math.Max(blockStart, Math.Min(c.SelectionStart + (newLine.Length - oldLine.Length), text.Length));
            return new NoteTextState(text, caret, 0);
        }

        /// <summary>목록 안에서 Enter: 다음 항목을 이어 준다. 빈 항목에서 Enter 는 목록을 끝낸다. 목록 줄이 아니면 null(기본 동작).</summary>
        internal static NoteTextState? ContinueListOnEnter(NoteTextState state)
        {
            var c = state.Clamped();
            if (c.SelectionLength > 0) c = new NoteTextState(c.Text.Remove(c.SelectionStart, c.SelectionLength), c.SelectionStart, 0);
            int ls = LineStartAt(c.Text, c.SelectionStart), le = LineEndAt(c.Text, c.SelectionEnd);
            string line = c.Text.Substring(ls, le - ls);
            var m = LinePrefix.Match(line); string marker = m.Groups["marker"].Value;
            if (string.IsNullOrEmpty(marker) || marker.StartsWith("#", StringComparison.Ordinal)) return null;
            string indent = m.Groups["indent"].Value, body = line.Substring(m.Length);
            if (string.IsNullOrWhiteSpace(body))
            {
                string cleared = c.Text.Remove(ls, line.Length).Insert(ls, indent);
                return new NoteTextState(cleared, ls + indent.Length, 0);
            }
            string continuation = "\n" + indent + NextMarker(marker);
            string text = c.Text.Insert(c.SelectionStart, continuation);
            return new NoteTextState(text, c.SelectionStart + continuation.Length, 0);
        }

        /// <summary>Ctrl+Enter: 캐럿이 있는 체크리스트 항목을 체크/해제. 체크리스트 줄이 아니면 null.</summary>
        internal static NoteTextState? ToggleChecklistItemAtCaret(NoteTextState state)
        {
            var c = state.Clamped(); int ls = LineStartAt(c.Text, c.SelectionStart), le = LineEndAt(c.Text, c.SelectionStart);
            string line = c.Text.Substring(ls, le - ls); var m = ChecklistLine.Match(line);
            if (!m.Success) return null;
            bool done = !m.Groups["state"].Value.Equals("x", StringComparison.OrdinalIgnoreCase);
            string replacement = m.Groups["indent"].Value + "- [" + (done ? "x" : " ") + "] " + m.Groups["text"].Value;
            string text = c.Text.Remove(ls, line.Length).Insert(ls, replacement);
            return new NoteTextState(text, ls + Math.Min(c.SelectionStart - ls, replacement.Length), 0);
        }

        /// <summary>줄 머리의 들여쓰기 + 목록·체크리스트·제목 기호. 서식 그리기가 이 부분은 건드리지 않는다.</summary>
        internal static string LineMarkerPrefix(string line) { return line.Substring(0, LinePrefix.Match(line).Length); }

        internal static LineStyle StyleOf(string line)
        {
            string marker = LinePrefix.Match(line).Groups["marker"].Value;
            if (string.IsNullOrEmpty(marker)) return LineStyle.None;
            if (marker.IndexOf('[') >= 0) return LineStyle.Checklist;
            if (marker.StartsWith("#", StringComparison.Ordinal)) { int n = marker.TakeWhile(ch => ch == '#').Count(); return n == 1 ? LineStyle.Heading1 : n == 2 ? LineStyle.Heading2 : LineStyle.Heading3; }
            return LineStyle.Bullet;   // 번호 목록도 "목록 기호 지우기" 는 같은 동작이라 Bullet 으로 본다
        }

        private static string SetStyle(string line, LineStyle style)
        {
            var m = LinePrefix.Match(line); string indent = m.Groups["indent"].Value, body = line.Substring(m.Length);
            string marker;
            switch (style) { case LineStyle.Bullet: marker = "- "; break; case LineStyle.Checklist: marker = "- [ ] "; break; case LineStyle.Heading1: marker = "# "; break; case LineStyle.Heading2: marker = "## "; break; case LineStyle.Heading3: marker = "### "; break; default: marker = ""; break; }
            return indent + marker + body;
        }

        private static string NextMarker(string marker)
        {
            string t = marker.TrimEnd();
            if (t.EndsWith("]", StringComparison.Ordinal)) return "- [ ] ";   // 끝낸 항목 다음은 새 빈 항목
            if (char.IsDigit(t[0]))
            {
                string digits = new string(t.TakeWhile(char.IsDigit).ToArray()); char sep = t[digits.Length]; int n;
                return int.TryParse(digits, out n) ? (n + 1) + sep.ToString() + " " : t + " ";
            }
            return "- ";
        }

        private static NoteTextState ToggleWrap(NoteTextState state, string open, string close)
        {
            var c = state.Clamped(); string text = c.Text; int start = c.SelectionStart, length = c.SelectionLength;
            if (length >= open.Length + close.Length && string.CompareOrdinal(text, start, open, 0, open.Length) == 0 && string.CompareOrdinal(text, start + length - close.Length, close, 0, close.Length) == 0)
            {
                string inner = text.Substring(start + open.Length, length - open.Length - close.Length);
                return new NoteTextState(text.Remove(start, length).Insert(start, inner), start, inner.Length);
            }
            if (start >= open.Length && start + length + close.Length <= text.Length && string.CompareOrdinal(text, start - open.Length, open, 0, open.Length) == 0 && string.CompareOrdinal(text, start + length, close, 0, close.Length) == 0)
                return new NoteTextState(text.Remove(start + length, close.Length).Remove(start - open.Length, open.Length), start - open.Length, length);
            string wrapped = text.Insert(c.SelectionEnd, close).Insert(c.SelectionStart, open);
            return new NoteTextState(wrapped, c.SelectionStart + open.Length, c.SelectionLength);
        }

        private static int LineStartAt(string text, int index)
        { if (index <= 0 || text.Length == 0) return 0; int prev = text.LastIndexOf('\n', Math.Min(index, text.Length) - 1); return prev < 0 ? 0 : prev + 1; }
        private static int LineEndAt(string text, int index) { int next = text.IndexOf('\n', Math.Min(index, text.Length)); return next < 0 ? text.Length : next; }
    }

    internal struct TextReplacement { internal int Start, Length; internal string Insert; internal bool IsEmpty { get { return Length == 0 && Insert.Length == 0; } } }

    /// <summary>글 전체를 바꾸는 명령도 "바뀐 한 군데" 만 바꾸는 편집으로 줄인다 — 그래야 Ctrl+Z 와 다른 곳의 서식이 살아남는다.</summary>
    internal static class TextDiff
    {
        internal static TextReplacement Minimal(string current, string updated)
        {
            if (string.Equals(current, updated, StringComparison.Ordinal)) return new TextReplacement { Insert = "" };
            int maxPrefix = Math.Min(current.Length, updated.Length), prefix = 0;
            while (prefix < maxPrefix && current[prefix] == updated[prefix]) prefix++;
            int maxSuffix = Math.Min(current.Length - prefix, updated.Length - prefix), suffix = 0;
            while (suffix < maxSuffix && current[current.Length - 1 - suffix] == updated[updated.Length - 1 - suffix]) suffix++;
            return new TextReplacement { Start = prefix, Length = current.Length - prefix - suffix, Insert = updated.Substring(prefix, updated.Length - suffix - prefix) };
        }
    }
}
