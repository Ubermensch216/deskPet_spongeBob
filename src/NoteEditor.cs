using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace BikiniFriends
{
    /// <summary>
    /// 메모 본문 상자. RichTextBox 에 세 가지를 더한다: 붙여넣기를 글자만 받게 가로채기(서식 · 이미지는 위에서 처리),
    /// 서식 명령 키를 상자가 먼저 먹지 못하게 가로채기, 실행 취소가 되는 편집과 글자 서식 읽기·쓰기.
    /// </summary>
    internal sealed class NoteBox : RichTextBox
    {
        private const int WM_IME_STARTCOMPOSITION = 0x010D, WM_IME_ENDCOMPOSITION = 0x010E, WM_KILLFOCUS = 0x0008;
        private const int WM_PASTE = 0x0302, WM_SETREDRAW = 0x000B, EM_REPLACESEL = 0x00C2, EM_SETSEL = 0x00B1, EM_GETCHARFORMAT = 0x043A, EM_SETCHARFORMAT = 0x0444, EM_EMPTYUNDOBUFFER = 0x00CD;
        private const int SCF_SELECTION = 1, SCF_ALL = 4;
        internal const int CFM_BOLD = 1, CFM_ITALIC = 2, CFM_UNDERLINE = 4, CFM_STRIKEOUT = 8, CFM_COLOR = 0x40000000;
        internal const int CFE_BOLD = 1, CFE_ITALIC = 2, CFE_UNDERLINE = 4, CFE_STRIKEOUT = 8;
        internal const int FourStyles = CFM_BOLD | CFM_ITALIC | CFM_UNDERLINE | CFM_STRIKEOUT;
        private static readonly Regex LoneLf = new Regex("\r?\n");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CHARFORMAT2
        {
            public int cbSize; public int dwMask; public int dwEffects; public int yHeight; public int yOffset; public int crTextColor;
            public byte bCharSet; public byte bPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szFaceName;
            public short wWeight; public short sSpacing; public int crBackColor; public int lcid; public int dwReserved; public short sStyle; public short wKerning;
            public byte bUnderlineType; public byte bAnimation; public byte bRevAuthor; public byte bUnderlineColor;
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageFmt(IntPtr hwnd, int msg, IntPtr wparam, ref CHARFORMAT2 lparam);

        /// <summary>붙여넣기 요청. true 를 돌려주면 처리한 것(상자는 아무것도 하지 않는다).</summary>
        internal Func<bool> PasteHandler;
        /// <summary>명령 키(Enter · Ctrl+B …). true 면 처리한 것.</summary>
        internal Func<Keys, bool> CommandKey;

        /// <summary>한글 같은 IME 가 아직 글자를 짓고 있는 중(밑줄 친 마지막 글자). 이때 선택을 건드리면 조합이 끊겨 캐럿이 글자 사이로 밀려난다.</summary>
        internal bool Composing { get; private set; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_PASTE && PasteHandler != null && PasteHandler()) return;
            if (m.Msg == WM_IME_STARTCOMPOSITION) Composing = true;
            base.WndProc(ref m);
            if (m.Msg == WM_IME_ENDCOMPOSITION || m.Msg == WM_KILLFOCUS)
            {
                bool was = Composing; Composing = false;
                if (was && CompositionEnded != null) CompositionEnded();
            }
        }

        /// <summary>조합이 끝나 마지막 글자가 확정됐다. 조합 중에는 글이 바뀌어도 알림이 오지 않을 수 있어 읽어 둔 값을 새로 받아야 한다.</summary>
        internal Action CompositionEnded;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (CommandKey != null && CommandKey(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        internal void SetRedraw(bool on)
        {
            SendMessageW(Handle, WM_SETREDRAW, new IntPtr(on ? 1 : 0), IntPtr.Zero);
            if (on) Invalidate();
        }

        /// <summary>start 부터 length 글자를 text 로 바꾼다. 상자의 실행 취소 기록에 남는다(Text 를 통째로 바꾸면 기록이 사라진다).</summary>
        internal void ReplaceRange(int start, int length, string text)
        {
            Select(start, length);
            IntPtr p = Marshal.StringToHGlobalUni(LoneLf.Replace(text ?? "", "\r"));
            try { SendMessageW(Handle, EM_REPLACESEL, new IntPtr(1), p); } finally { Marshal.FreeHGlobal(p); }
        }

        internal void ClearUndoStack() { ClearUndo(); SendMessageW(Handle, EM_EMPTYUNDOBUFFER, IntPtr.Zero, IntPtr.Zero); }

        /// <summary>선택(또는 입력 위치)의 굵게·기울임·밑줄·취소선. mask 는 상자가 말해 주는 "구간 전체가 같은 값인" 항목.</summary>
        internal void GetEffects(out int mask, out int effects)
        {
            var cf = new CHARFORMAT2 { cbSize = Marshal.SizeOf(typeof(CHARFORMAT2)), szFaceName = "" };
            SendMessageFmt(Handle, EM_GETCHARFORMAT, new IntPtr(SCF_SELECTION), ref cf);
            mask = cf.dwMask & FourStyles; effects = cf.dwEffects & FourStyles;
        }

        internal void SetEffects(int bit, bool on, bool all = false)
        {
            var cf = new CHARFORMAT2 { cbSize = Marshal.SizeOf(typeof(CHARFORMAT2)), dwMask = bit, dwEffects = on ? bit : 0, szFaceName = "" };
            SendMessageFmt(Handle, EM_SETCHARFORMAT, new IntPtr(all ? SCF_ALL : SCF_SELECTION), ref cf);
        }

        /// <summary>모든 글자에 먹색을 칠한다. 메모 색이 바뀌면 상자 색만이 아니라 글자마다 박힌 색도 바꿔야 한다.</summary>
        internal void PaintInk(Color ink)
        {
            var cf = new CHARFORMAT2 { cbSize = Marshal.SizeOf(typeof(CHARFORMAT2)), dwMask = CFM_COLOR, crTextColor = ColorTranslator.ToWin32(ink), szFaceName = "" };
            SendMessageFmt(Handle, EM_SETCHARFORMAT, new IntPtr(SCF_ALL), ref cf);
            SendMessageFmt(Handle, EM_SETCHARFORMAT, new IntPtr(SCF_SELECTION), ref cf);
        }
    }

    /// <summary>
    /// 메모 본문 편집기. 저장은 마크다운이고 화면은 강조를 그린다: ~~취소선~~ 은 취소선으로 보이고 물결표는 숨는다.
    /// 마크다운 ↔ 화면 변환은 이 클래스만 안다. 위쪽(서식 명령 · 저장 · 기록)은 전부 "보이는 글 + 선택" 만으로 일한다.
    /// </summary>
    internal sealed class NoteEditor
    {
        private readonly NoteBox box;
        private string shown, markdown;
        /// <summary>코드가 글이나 선택을 바꾸는 동안 이벤트를 무시하게 하는 표시.</summary>
        internal bool Busy;

        internal NoteEditor(NoteBox box) { this.box = box; }

        internal NoteBox Box { get { return box; } }
        internal string Text { get { if (shown == null) shown = NoteContent.NormalizeLineEndings(box.Text); return shown; } }
        internal string Markdown { get { if (markdown == null) markdown = NoteRichText.ToMarkdown(Text, ReadSpans()); return markdown; } }
        internal int SelectionStart { get { return Math.Max(0, box.SelectionStart); } }
        internal int SelectionLength { get { return Math.Max(0, box.SelectionLength); } }
        internal NoteTextState State { get { return new NoteTextState(Text, SelectionStart, SelectionLength).Clamped(); } }

        /// <summary>글이 바뀌어 읽어 둔 값이 낡았다고 알린다(매 글자마다 서식 구간을 훑지 않으려고 캐시한다).</summary>
        internal void Invalidate() { shown = null; markdown = null; }

        /// <summary>저장된 마크다운으로 본문 전체를 바꾸고 강조를 그린다. 불러오는 것은 사용자의 편집이 아니므로 실행 취소 기록은 비운다.</summary>
        internal void Load(string md, Color ink)
        {
            bool was = Busy; Busy = true;
            try
            {
                var rich = NoteRichText.FromMarkdown(md);
                box.SetRedraw(false);
                box.Text = rich.Text;
                foreach (var s in rich.Spans.Where(x => x.Length > 0))
                {
                    box.Select(s.Start, s.Length);
                    SetStyle(s.Style);
                }
                box.PaintInk(ink); box.Select(0, 0);
                box.ClearUndoStack();
            }
            finally { box.SetRedraw(true); Busy = was; Invalidate(); }
        }

        private void SetStyle(InlineStyle style)
        {
            box.SetEffects(NoteBox.CFE_BOLD, (style & InlineStyle.Bold) != 0);
            box.SetEffects(NoteBox.CFE_ITALIC, (style & InlineStyle.Italic) != 0);
            box.SetEffects(NoteBox.CFE_UNDERLINE, (style & InlineStyle.Underline) != 0);
            box.SetEffects(NoteBox.CFE_STRIKEOUT, (style & InlineStyle.Strikethrough) != 0);
        }

        internal void Select(int start, int length) { bool was = Busy; Busy = true; try { box.Select(start, length); } finally { Busy = was; } }

        /// <summary>바뀐 한 군데만 바꿔 넣는다. 통째로 바꾸면 실행 취소 기록과 다른 곳의 강조가 사라진다.</summary>
        internal void Apply(NoteTextState next)
        {
            bool was = Busy; Busy = true;
            try
            {
                var edit = TextDiff.Minimal(Text, next.Text);
                if (!edit.IsEmpty) box.ReplaceRange(edit.Start, edit.Length, edit.Insert);
                box.Select(next.SelectionStart, next.SelectionLength);
            }
            finally { Busy = was; Invalidate(); }
        }

        /// <summary>선택(없으면 이어서 쓸 글)의 강조 하나를 켜거나 끈다. 섞인 선택은 "아직 안 한 것" 으로 읽어 첫 눌림에 전부 켠다.</summary>
        internal void ToggleInline(InlineStyle style)
        {
            int bit = style == InlineStyle.Bold ? NoteBox.CFE_BOLD : style == InlineStyle.Italic ? NoteBox.CFE_ITALIC : style == InlineStyle.Underline ? NoteBox.CFE_UNDERLINE : NoteBox.CFE_STRIKEOUT;
            int mask, effects; box.GetEffects(out mask, out effects);
            bool currentlyOn = (mask & bit) != 0 && (effects & bit) != 0;
            box.SetEffects(bit, !currentlyOn);
            Invalidate();
        }

        internal void SetInk(Color ink) { box.ForeColor = ink; box.PaintInk(ink); }

        // 구간을 반으로 쪼개 가며 "전부 같은 서식" 인 덩어리를 찾는다. 균일한 메모는 한 번에 끝난다.
        private List<RichSpan> ReadSpans()
        {
            var spans = new List<RichSpan>(); int length = Text.Length; if (length == 0) return spans;
            bool was = Busy; Busy = true; int s0 = box.SelectionStart, l0 = box.SelectionLength;
            try { box.SetRedraw(false); Collect(0, length, spans); }
            finally { box.Select(s0, l0); box.SetRedraw(true); Busy = was; }
            return spans;
        }

        private void Collect(int start, int end, List<RichSpan> spans)
        {
            if (end <= start) return;
            box.Select(start, end - start);
            int mask, effects; box.GetEffects(out mask, out effects);
            if ((mask & NoteBox.FourStyles) == NoteBox.FourStyles || end - start == 1)
            {
                var style = InlineStyle.None;
                if ((mask & effects & NoteBox.CFE_BOLD) != 0) style |= InlineStyle.Bold;
                if ((mask & effects & NoteBox.CFE_ITALIC) != 0) style |= InlineStyle.Italic;
                if ((mask & effects & NoteBox.CFE_UNDERLINE) != 0) style |= InlineStyle.Underline;
                if ((mask & effects & NoteBox.CFE_STRIKEOUT) != 0) style |= InlineStyle.Strikethrough;
                if (style != InlineStyle.None) spans.Add(new RichSpan(start, end - start, style));
                return;
            }
            int mid = start + (end - start) / 2; Collect(start, mid, spans); Collect(mid, end, spans);
        }
    }
}
