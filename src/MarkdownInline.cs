using System;
using System.Collections.Generic;
using System.Text;

namespace BikiniFriends
{
    [Flags] internal enum InlineStyle { None = 0, Bold = 1, Italic = 2, Underline = 4, Strikethrough = 8 }
    internal struct InlineRun { internal string Text; internal InlineStyle Style; internal InlineRun(string t, InlineStyle s) { Text = t; Style = s; } }
    internal struct RichSpan { internal int Start, Length; internal InlineStyle Style; internal RichSpan(int s, int l, InlineStyle st) { Start = s; Length = l; Style = st; } }

    /// <summary>
    /// 한 줄의 인라인 마크다운을 서식 구간으로, 또 그 반대로. 저장은 늘 마크다운이고 편집기는 **굵게** 의 별표를 숨기고 굵게 그린다.
    /// 되돌릴 때 글자 하나라도 달라지면(짝이 안 맞는 기호 등) 그 줄은 기호가 보이는 원문 그대로 둔다 — 글자가 사라지는 일은 없다.
    /// </summary>
    internal static class MarkdownInline
    {
        private static readonly InlineStyle[] NestingOrder = { InlineStyle.Bold, InlineStyle.Italic, InlineStyle.Underline, InlineStyle.Strikethrough };

        internal static List<InlineRun> Parse(string line)
        {
            if (line.Length == 0) return new List<InlineRun>();
            var runs = new List<InlineRun>(); Scan(line, InlineStyle.None, runs);
            var merged = Coalesce(runs);
            return string.Equals(Write(merged), line, StringComparison.Ordinal) ? merged : new List<InlineRun> { new InlineRun(line, InlineStyle.None) };
        }

        internal static string Write(IList<InlineRun> runs)
        {
            var text = new StringBuilder(); var open = new List<InlineStyle>();
            foreach (var run in runs)
            {
                if (run.Text.Length == 0) continue;
                while (open.Exists(s => (run.Style & s) != s)) { text.Append(Close(open[open.Count - 1])); open.RemoveAt(open.Count - 1); }
                foreach (var style in NestingOrder) if ((run.Style & style) == style && !open.Contains(style)) { text.Append(Open(style)); open.Add(style); }
                text.Append(run.Text);
            }
            for (int i = open.Count - 1; i >= 0; i--) text.Append(Close(open[i]));
            return text.ToString();
        }

        private struct Span { internal InlineStyle Style; internal int InnerStart, InnerEnd, End; }

        private static void Scan(string text, InlineStyle inherited, List<InlineRun> runs)
        {
            var plain = new StringBuilder(); int index = 0;
            while (index < text.Length)
            {
                Span span;
                if (!MatchAt(text, index, out span)) { plain.Append(text[index]); index++; continue; }
                Flush(plain, inherited, runs);
                Scan(text.Substring(span.InnerStart, span.InnerEnd - span.InnerStart), inherited | span.Style, runs);
                index = span.End;
            }
            Flush(plain, inherited, runs);
        }

        private static void Flush(StringBuilder plain, InlineStyle style, List<InlineRun> runs) { if (plain.Length == 0) return; runs.Add(new InlineRun(plain.ToString(), style)); plain.Clear(); }

        // ** 는 * 로 시작하므로 굵게를 먼저 본다(기울임으로 읽으면 굵게마다 별표 하나가 사라진다).
        private static bool MatchAt(string t, int i, out Span span)
        {
            return Delimited(t, i, "~~", "~~", InlineStyle.Strikethrough, out span) || Delimited(t, i, "**", "**", InlineStyle.Bold, out span)
                || Delimited(t, i, "<u>", "</u>", InlineStyle.Underline, out span) || Delimited(t, i, "*", "*", InlineStyle.Italic, out span);
        }

        private static bool Delimited(string t, int index, string open, string close, InlineStyle style, out Span span)
        {
            span = default(Span);
            if (string.CompareOrdinal(t, index, open, 0, open.Length) != 0) return false;
            int innerStart = index + open.Length; if (innerStart > t.Length) return false;
            int closeAt = t.IndexOf(close, innerStart, StringComparison.Ordinal);
            if (closeAt <= innerStart) return false;   // 빈 구간("****")은 서식이 아니라 글자 네 개
            span = new Span { Style = style, InnerStart = innerStart, InnerEnd = closeAt, End = closeAt + close.Length }; return true;
        }

        private static List<InlineRun> Coalesce(List<InlineRun> runs)
        {
            var merged = new List<InlineRun>();
            foreach (var r in runs)
            {
                if (r.Text.Length == 0) continue;
                if (merged.Count > 0 && merged[merged.Count - 1].Style == r.Style) merged[merged.Count - 1] = new InlineRun(merged[merged.Count - 1].Text + r.Text, r.Style);
                else merged.Add(r);
            }
            return merged;
        }

        private static string Open(InlineStyle s) { switch (s) { case InlineStyle.Bold: return "**"; case InlineStyle.Italic: return "*"; case InlineStyle.Underline: return "<u>"; default: return "~~"; } }
        private static string Close(InlineStyle s) { switch (s) { case InlineStyle.Bold: return "**"; case InlineStyle.Italic: return "*"; case InlineStyle.Underline: return "</u>"; default: return "~~"; } }
    }

    internal sealed class RichNoteText { internal string Text = ""; internal List<RichSpan> Spans = new List<RichSpan>(); }

    /// <summary>저장된 마크다운 ↔ 편집기에 보이는 글(+ 강조 구간).</summary>
    internal static class NoteRichText
    {
        internal static RichNoteText FromMarkdown(string markdown)
        {
            var result = new RichNoteText(); if (string.IsNullOrEmpty(markdown)) return result;
            var text = new StringBuilder(); string[] lines = NoteContent.NormalizeLineEndings(markdown).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) text.Append('\n');
                string line = lines[i], prefix = MarkdownEditing.LineMarkerPrefix(line);
                text.Append(prefix);
                foreach (var run in MarkdownInline.Parse(line.Substring(prefix.Length)))
                {
                    if (run.Style != InlineStyle.None) result.Spans.Add(new RichSpan(text.Length, run.Text.Length, run.Style));
                    text.Append(run.Text);
                }
            }
            result.Text = text.ToString(); return result;
        }

        internal static string ToMarkdown(string shown, IList<RichSpan> spans)
        {
            if (string.IsNullOrEmpty(shown)) return "";
            string text = NoteContent.NormalizeLineEndings(shown); var styles = StyleMap(text.Length, spans);
            var md = new StringBuilder(); int lineStart = 0;
            while (lineStart <= text.Length)
            {
                int nl = text.IndexOf('\n', lineStart), lineEnd = nl < 0 ? text.Length : nl;
                md.Append(MarkdownInline.Write(RunsIn(text, styles, lineStart, lineEnd)));
                if (nl < 0) break;
                md.Append('\n'); lineStart = nl + 1;
            }
            return md.ToString();
        }

        private static InlineStyle[] StyleMap(int length, IList<RichSpan> spans)
        {
            var styles = new InlineStyle[length];
            foreach (var s in spans)
            {
                int start = Math.Max(0, Math.Min(s.Start, length)), end = Math.Max(start, Math.Min(s.Start + s.Length, length));
                for (int i = start; i < end; i++) styles[i] |= s.Style;
            }
            return styles;
        }

        private static List<InlineRun> RunsIn(string text, InlineStyle[] styles, int start, int end)
        {
            var runs = new List<InlineRun>(); int runStart = start;
            for (int i = start; i < end; i++)
            {
                if (styles[i] == styles[runStart]) continue;
                runs.Add(new InlineRun(text.Substring(runStart, i - runStart), styles[runStart])); runStart = i;
            }
            if (runStart < end) runs.Add(new InlineRun(text.Substring(runStart, end - runStart), styles[runStart]));
            return runs;
        }
    }
}
