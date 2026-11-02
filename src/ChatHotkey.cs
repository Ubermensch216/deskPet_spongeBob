using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BikiniFriends
{
    /// <summary>말 걸기 단축키 규칙: 보조키(Ctrl · Alt · Shift) 2개 이상 + 글쇠 1개, 모두 합쳐 3개 이상. 값은 RegisterHotKey 의 modifier / 가상 키 코드.</summary>
    internal static class ChatHotkey
    {
        internal const uint Alt = 1, Ctrl = 2, Shift = 4;
        internal const uint DefaultMods = Ctrl | Alt, DefaultKey = (uint)'B';
        internal const int MinKeys = 3;

        // 같이 쓰는 다른 전역 단축키(메모). 겹치면 둘 중 하나가 등록되지 않는다.
        private static readonly KeyValuePair<uint, uint>[] Taken =
        {
            new KeyValuePair<uint, uint>(Ctrl | Alt, (uint)'N'), new KeyValuePair<uint, uint>(Ctrl | Shift, (uint)'F'),
            new KeyValuePair<uint, uint>(Ctrl | Shift, (uint)'P'), new KeyValuePair<uint, uint>(Ctrl | Shift, (uint)'R')
        };

        internal static int ModCount(uint mods) { return ((mods & Ctrl) != 0 ? 1 : 0) + ((mods & Alt) != 0 ? 1 : 0) + ((mods & Shift) != 0 ? 1 : 0); }

        internal static bool IsModifierKey(Keys k) { return k == Keys.ControlKey || k == Keys.Menu || k == Keys.ShiftKey || k == Keys.LControlKey || k == Keys.RControlKey || k == Keys.LMenu || k == Keys.RMenu || k == Keys.LShiftKey || k == Keys.RShiftKey; }

        /// <summary>쓸 수 있는 조합인가. 안 되면 이유를 reason 에 돌려준다.</summary>
        internal static bool Valid(uint mods, uint key, out string reason)
        {
            reason = null;
            if (key == 0 || ModCount(mods) + 1 < MinKeys) { reason = "키를 " + MinKeys + "개 이상 눌러야 해요 (예: Ctrl + Alt + B)"; return false; }
            Keys k = (Keys)key;
            if (k == Keys.Escape || k == Keys.Back || k == Keys.Tab || k == Keys.LWin || k == Keys.RWin)
            { reason = "이 키는 단축키로 쓸 수 없어요"; return false; }
            if (Taken.Any(t => t.Key == mods && t.Value == key)) { reason = "메모 단축키와 겹쳐요. 다른 조합을 눌러 주세요"; return false; }
            return true;
        }

        internal static string KeyName(uint vk)
        {
            Keys k = (Keys)vk;
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num" + (k - Keys.NumPad0);
            switch (k)
            {
                case Keys.Oemtilde: return "`"; case Keys.OemMinus: return "-"; case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "["; case Keys.OemCloseBrackets: return "]"; case Keys.OemPipe: return "\\";
                case Keys.OemSemicolon: return ";"; case Keys.OemQuotes: return "'"; case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return "."; case Keys.OemQuestion: return "/"; case Keys.Return: return "Enter";
                case Keys.Next: return "PageDown"; case Keys.Prior: return "PageUp"; case Keys.Capital: return "CapsLock";
            }
            return k.ToString();
        }

        internal static IEnumerable<string> ModNames(uint mods)
        {
            if ((mods & Ctrl) != 0) yield return "Ctrl";
            if ((mods & Alt) != 0) yield return "Alt";
            if ((mods & Shift) != 0) yield return "Shift";
        }

        /// <summary>"Ctrl + Alt + B" 처럼 사람이 읽는 글. sep 으로 구분 기호를 고른다(메뉴는 "+", 설정은 " + ").</summary>
        internal static string Text(uint mods, uint key, string sep = " + ")
        {
            return string.Join(sep, ModNames(mods).Concat(new[] { KeyName(key) }).ToArray());
        }
    }

    /// <summary>
    /// 단축키를 누른 키로 정하는 입력란. 클릭하면 비워지고, 키를 하나씩 누를 때마다 "Ctrl + " 처럼 + 를 붙여 다음 키를 기다린다.
    /// 보조키 두 개 이상 + 글쇠를 누르면 확정된다(Esc: 취소 · Backspace: 방금 누른 키 취소).
    /// </summary>
    internal sealed class HotkeyBox : TextBox
    {
        private uint mods, key, pending;
        private readonly List<uint> order = new List<uint>();
        private bool capturing;

        internal uint Mods { get { return mods; } }
        internal uint Key { get { return key; } }
        /// <summary>안내·거절 이유를 알릴 때(없으면 null).</summary>
        internal event Action<string> Hint;
        internal event Action Changed;

        internal HotkeyBox(uint m, uint k)
        {
            ReadOnly = true; ShortcutsEnabled = false; Cursor = Cursors.Hand; BackColor = SystemColors.Window; TabStop = true; Width = 190; Margin = new Padding(3);
            SetValue(m, k);
        }

        internal void SetValue(uint m, uint k)
        {
            mods = m; key = k; capturing = false; pending = 0; order.Clear();
            ForeColor = SystemColors.WindowText; Text = ChatHotkey.Text(m, k);
        }

        private void Begin()
        {
            if (capturing) return;
            capturing = true; pending = 0; order.Clear(); UpdateDisplay(); Notify("키를 하나씩 누르세요. 보조키(Ctrl · Alt · Shift) 2개 이상 + 글쇠 1개");
        }

        private void Cancel()
        {
            capturing = false; pending = 0; order.Clear();
            ForeColor = SystemColors.WindowText; Text = ChatHotkey.Text(mods, key);
            Notify(null);
        }

        private void Pop()
        {
            if (!capturing) return;
            if (order.Count > 0)
            {
                uint last = order[order.Count - 1]; order.RemoveAt(order.Count - 1);
                pending &= ~last;
                UpdateDisplay();
                Notify("키를 하나씩 누르세요");
            }
            else Cancel();
        }

        private void Commit(uint vkey)
        {
            string why;
            if (!ChatHotkey.Valid(pending, vkey, out why)) { Notify(why); return; }
            mods = pending; key = vkey; capturing = false; pending = 0; order.Clear();
            ForeColor = SystemColors.WindowText; Text = ChatHotkey.Text(mods, key);
            Notify(null);
            var h = Changed; if (h != null) h();
        }

        private void UpdateDisplay()
        {
            ForeColor = Color.DimGray;
            var parts = new List<string>();
            foreach (uint m in order)
            {
                if (m == ChatHotkey.Ctrl) parts.Add("Ctrl");
                else if (m == ChatHotkey.Alt) parts.Add("Alt");
                else if (m == ChatHotkey.Shift) parts.Add("Shift");
            }
            Text = parts.Count == 0 ? "키를 누르세요..." : string.Join(" + ", parts.ToArray()) + " + ";
        }

        private void Notify(string text) { var h = Hint; if (h != null) h(text); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) Begin();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!capturing) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Begin(); e.Handled = true; e.SuppressKeyPress = true; return; } base.OnKeyDown(e); return; }
            e.Handled = true; e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape) { Cancel(); return; }
            if (e.KeyCode == Keys.Back) { Pop(); return; }
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.LControlKey || e.KeyCode == Keys.RControlKey)
            { if ((pending & ChatHotkey.Ctrl) == 0) { pending |= ChatHotkey.Ctrl; order.Add(ChatHotkey.Ctrl); UpdateDisplay(); } return; }
            if (e.KeyCode == Keys.Menu || e.KeyCode == Keys.LMenu || e.KeyCode == Keys.RMenu)
            { if ((pending & ChatHotkey.Alt) == 0) { pending |= ChatHotkey.Alt; order.Add(ChatHotkey.Alt); UpdateDisplay(); } return; }
            if (e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LShiftKey || e.KeyCode == Keys.RShiftKey)
            { if ((pending & ChatHotkey.Shift) == 0) { pending |= ChatHotkey.Shift; order.Add(ChatHotkey.Shift); UpdateDisplay(); } return; }
            Commit((uint)e.KeyCode);
        }

        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); if (capturing) Cancel(); }
    }
}