using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BikiniFriends
{
    internal static class NoteContent
    {
        internal const int MaxTitleLength = 60;

        /// <summary>저장하는 쪽은 \n 하나로 통일한다. 편집기가 주는 줄바꿈이 어떤 모양이든 같은 글이 되게.</summary>
        internal static string NormalizeLineEndings(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('\r') < 0) return text;
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        /// <summary>XML 에 쓸 수 없는 제어 문자를 뺀다.</summary>
        internal static string StripInvalidXml(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var b = new StringBuilder(text.Length);
            foreach (char c in text) if (c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c != 0xFFFE && c != 0xFFFF && !(c >= 0xD800 && c <= 0xDFFF)) || (c >= 0xD800 && c <= 0xDFFF)) b.Append(c);
            return b.ToString();
        }

        private static readonly Regex LineMarkers = new Regex(@"^[ \t]*(?:(?:\#{1,6}|>)[ \t]*|[-*+][ \t]+(?:\[[ xX]\][ \t]*)?|\d+[.)][ \t]+)*", RegexOptions.CultureInvariant);
        private static readonly Regex ImageRef = new Regex(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.CultureInvariant);
        private static readonly Regex LinkRef = new Regex(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant);
        private static readonly Regex HtmlTag = new Regex(@"</?[A-Za-z][^>]*>", RegexOptions.CultureInvariant);
        private static readonly Regex Emphasis = new Regex(@"\*\*|~~|[*`]", RegexOptions.CultureInvariant);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.CultureInvariant);

        /// <summary>
        /// 제목 칸은 없다: 본문에서 내용이 있는 첫 줄이 제목이다. "# 회의 준비" 는 "회의 준비" —
        /// 해시는 줄을 꾸미는 방법이지 말하는 내용이 아니라서 떼어 낸다.
        /// </summary>
        internal static string DeriveTitle(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "";
            foreach (string line in NormalizeLineEndings(content).Split('\n'))
            {
                string t = LineMarkers.Replace(line, "", 1);
                t = ImageRef.Replace(t, ""); t = LinkRef.Replace(t, "$1"); t = HtmlTag.Replace(t, ""); t = Emphasis.Replace(t, "");
                t = Spaces.Replace(t, " ").Trim();
                if (t.Length == 0) continue;
                return t.Length <= MaxTitleLength ? t : t.Substring(0, MaxTitleLength).TrimEnd();
            }
            return "";
        }
    }

    internal struct ChecklistItemLine { internal int LineIndex; internal string Text; internal bool IsDone; }

    /// <summary>체크리스트의 원본은 본문(마크다운)이다. 진행률 같은 건 거기서 읽어 낸 투영일 뿐.</summary>
    internal static class ChecklistParser
    {
        private static readonly Regex ItemLine = new Regex(@"^[ \t]*[-*+][ \t]+\[(?<state>[ xX])\][ \t]*(?<text>.*)$", RegexOptions.CultureInvariant);

        internal static List<ChecklistItemLine> Parse(string content)
        {
            var items = new List<ChecklistItemLine>();
            if (string.IsNullOrEmpty(content) || content.IndexOf('[') < 0) return items;
            string[] lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var m = ItemLine.Match(lines[i].TrimEnd('\r'));
                if (m.Success) items.Add(new ChecklistItemLine { LineIndex = i, Text = m.Groups["text"].Value.Trim(), IsDone = m.Groups["state"].Value.Equals("x", StringComparison.OrdinalIgnoreCase) });
            }
            return items;
        }
    }

    internal struct TagMention { internal string Name, NormalizedName; internal int Index; }

    /// <summary>본문에 #백엔드 라고 쓰면 그게 곧 태그. "# 제목" 처럼 해시 뒤가 공백이면 제목이지 태그가 아니다.</summary>
    internal static class TagParser
    {
        internal const int MaxLength = 50;
        private static readonly Regex Hashtag = new Regex(@"(?<![^\s(\[])#(?![\s#])(?<tag>[\p{L}\p{N}_\-/]{1,50})", RegexOptions.CultureInvariant);
        internal static string Normalize(string name) { return name.Trim().ToLowerInvariant(); }

        internal static List<TagMention> FindMentions(string content)
        {
            var list = new List<TagMention>();
            if (string.IsNullOrEmpty(content) || content.IndexOf('#') < 0) return list;
            foreach (Match m in Hashtag.Matches(content))
            {
                string name = m.Groups["tag"].Value;
                if (name.All(char.IsDigit)) continue;   // 숫자뿐이면 라벨이 아니라 번호
                list.Add(new TagMention { Name = name, NormalizedName = Normalize(name), Index = m.Groups["tag"].Index - 1 });
            }
            return list;
        }

        /// <summary>중복 없이, 처음 쓴 철자 그대로.</summary>
        internal static List<TagMention> Parse(string content)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal); var list = new List<TagMention>();
            foreach (var m in FindMentions(content)) if (seen.Add(m.NormalizedName)) list.Add(m);
            return list;
        }
    }

    /// <summary>편집기의 글과 선택 범위. 서식 명령은 이 값을 받아 새 값을 돌려주는 순수 함수라 화면 없이 점검된다.</summary>
    internal struct NoteTextState
    {
        internal string Text; internal int SelectionStart, SelectionLength;
        internal NoteTextState(string text, int start, int length) { Text = text ?? ""; SelectionStart = start; SelectionLength = length; }
        internal int SelectionEnd { get { return SelectionStart + SelectionLength; } }
        internal string SelectedText { get { return Text.Substring(SelectionStart, SelectionLength); } }
        internal NoteTextState Clamped()
        {
            int s = Math.Max(0, Math.Min(SelectionStart, Text.Length)); int l = Math.Max(0, Math.Min(SelectionLength, Text.Length - s));
            return new NoteTextState(Text, s, l);
        }
        internal NoteTextState ReplacingSelection(string text)
        {
            var c = Clamped();
            return new NoteTextState(c.Text.Remove(c.SelectionStart, c.SelectionLength).Insert(c.SelectionStart, text), c.SelectionStart + text.Length, 0);
        }
    }
}
