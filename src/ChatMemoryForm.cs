using System;
using System.Drawing;
using System.Windows.Forms;

namespace BikiniFriends
{
    /// <summary>대화에서 기억해 둔 이야기를 보고 지우는 창. 기억은 전부 이 PC 의 파일(비키니친구들_기억.txt)에만 있다.</summary>
    internal sealed class ChatMemoryForm : Form
    {
        private readonly ListBox list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
        private readonly Label empty = new Label { Dock = DockStyle.Top, Height = 40, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };

        internal ChatMemoryForm(Icon icon)
        {
            FriendUI.Setup(this, icon);
            Text = "비키니 친구들이 기억하는 이야기"; ClientSize = new Size(460, 360); MinimumSize = new Size(380, 280); FormBorderStyle = FormBorderStyle.Sizable;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 8, 8, 4) };
            bar.Controls.Add(FriendUI.Button("닫기", delegate { Close(); }));
            bar.Controls.Add(FriendUI.Button("모두 잊게 하기", delegate
            {
                if (MessageBox.Show(this, "기억한 이야기를 모두 지울까요?", "비키니친구들", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { ChatMemory.Clear(); Fill(); }
            }));
            bar.Controls.Add(FriendUI.Button("선택한 이야기 잊게 하기", delegate { if (list.SelectedIndex >= 0) { ChatMemory.RemoveAt(list.SelectedIndex); Fill(); } }));
            var note = new Label { Dock = DockStyle.Top, Height = 44, ForeColor = Color.DimGray, Padding = new Padding(8, 6, 8, 0), Text = "대화가 끝나면 알게 된 사실만 한두 줄로 적어 둬요. 이 PC 안(비키니친구들_기억.txt)에만 저장되고, 언제든 지울 수 있어요." };
            Controls.Add(list); Controls.Add(empty); Controls.Add(note); Controls.Add(bar);
            list.BringToFront();
            Fill();
        }

        private void Fill()
        {
            ChatMemory.Reload();
            list.Items.Clear();
            foreach (string f in ChatMemory.Facts) list.Items.Add(f);
            empty.Text = ChatMemory.Facts.Count == 0 ? "아직 기억하고 있는 이야기가 없어요." : "";
            empty.Visible = ChatMemory.Facts.Count == 0;
        }
    }
}