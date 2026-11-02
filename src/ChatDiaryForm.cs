using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace BikiniFriends
{
    /// <summary>비키니 친구들의 일기장. 대화가 끝날 때 친구가 한 줄씩 적어 둔 일기를 날짜별로 보여 주고, 지울 수 있다(비키니친구들_일기.txt).</summary>
    internal sealed class ChatDiaryForm : Form
    {
        private static readonly Color Paper = Color.FromArgb(255, 251, 235), Rule = Color.FromArgb(232, 222, 190), Ink = Color.FromArgb(60, 48, 40), Date = Color.FromArgb(54, 96, 190);
        private readonly ListBox list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawVariable, BackColor = Paper };
        private readonly Label empty = new Label { Dock = DockStyle.Top, Height = 44, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
        private readonly Font dateFont = new Font("Malgun Gothic", 9f, FontStyle.Bold), textFont = new Font("Malgun Gothic", 10.5f);

        internal ChatDiaryForm(Icon icon)
        {
            FriendUI.Setup(this, icon);
            Text = "비키니 친구들의 일기장"; ClientSize = new Size(480, 420); MinimumSize = new Size(380, 300); FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 8, 8, 4) };
            bar.Controls.Add(FriendUI.Button("닫기", delegate { Close(); }));
            bar.Controls.Add(FriendUI.Button("일기 모두 지우기", delegate
            {
                if (MessageBox.Show(this, "비키니 친구들의 일기를 모두 지울까요?", "비키니친구들", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { ChatDiary.Clear(); Fill(); }
            }));
            bar.Controls.Add(FriendUI.Button("선택한 일기 지우기", delegate { DeleteSelected(); }));
            var note = new Label { Dock = DockStyle.Top, Height = 44, ForeColor = Color.DimGray, Padding = new Padding(8, 6, 8, 0), Text = "대화가 끝나면 친구가 오늘 있었던 일을 한 줄씩 적어 둬요. 이 PC 안(비키니친구들_일기.txt)에만 저장되고, 언제든 지울 수 있어요." };
            Controls.Add(list); Controls.Add(empty); Controls.Add(note); Controls.Add(bar);
            list.BringToFront();
            list.MeasureItem += OnMeasure; list.DrawItem += OnDraw;
            list.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) DeleteSelected(); };
            list.Resize += delegate { list.Invalidate(); };
            Fill();
        }

        protected override void Dispose(bool disposing) { if (disposing) { dateFont.Dispose(); textFont.Dispose(); } base.Dispose(disposing); }

        /// <summary>목록은 최근 일기가 위. 항목의 번호는 저장된 순서(오래된 것부터)로 되돌릴 수 있게 따로 들고 있는다.</summary>
        private void Fill()
        {
            ChatDiary.Reload();
            list.Items.Clear();
            var all = ChatDiary.Entries;
            for (int i = all.Count - 1; i >= 0; i--) list.Items.Add(new Row { Index = i, Entry = all[i] });
            empty.Text = all.Count == 0 ? "아직 쓴 일기가 없어요. 친구와 이야기를 나누면 한 줄씩 적어 둬요." : "";
            empty.Visible = all.Count == 0;
        }

        private void DeleteSelected()
        {
            var row = list.SelectedItem as Row;
            if (row == null) return;
            int at = list.SelectedIndex;
            ChatDiary.RemoveAt(row.Index); Fill();
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(at, list.Items.Count - 1);
        }

        private sealed class Row
        {
            internal int Index; internal ChatDiary.Entry Entry;
            public override string ToString() { return Entry.Text; }
        }

        private static string DateLabel(DateTime d)
        {
            string[] day = { "일", "월", "화", "수", "목", "금", "토" };
            return d.ToString("yyyy년 M월 d일", CultureInfo.InvariantCulture) + " (" + day[(int)d.DayOfWeek] + ")  " + d.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private int TextWidth() { return Math.Max(80, list.ClientSize.Width - 28); }

        private void OnMeasure(object sender, MeasureItemEventArgs e)
        {
            var row = (Row)list.Items[e.Index];
            Size t = TextRenderer.MeasureText(row.Entry.Text, textFont, new Size(TextWidth(), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            e.ItemHeight = 12 + dateFont.Height + 4 + t.Height + 12;
        }

        private void OnDraw(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var row = (Row)list.Items[e.Index]; bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(sel ? Color.FromArgb(255, 240, 190) : Paper)) e.Graphics.FillRectangle(back, e.Bounds);
            using (var pen = new Pen(Rule)) e.Graphics.DrawLine(pen, e.Bounds.Left + 12, e.Bounds.Bottom - 1, e.Bounds.Right - 12, e.Bounds.Bottom - 1);
            var dateRect = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + 10, e.Bounds.Width - 28, dateFont.Height + 2);
            TextRenderer.DrawText(e.Graphics, DateLabel(row.Entry.When), dateFont, dateRect, Date, TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            var textRect = new Rectangle(e.Bounds.Left + 14, dateRect.Bottom + 4, TextWidth(), e.Bounds.Bottom - dateRect.Bottom - 8);
            TextRenderer.DrawText(e.Graphics, row.Entry.Text, textFont, textRect, Ink, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
}