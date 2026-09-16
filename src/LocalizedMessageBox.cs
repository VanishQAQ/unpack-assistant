using System;
using System.Drawing;
using System.Windows.Forms;

namespace LayerUnpacker
{
    public static class LocalizedMessageBox
    {
        public static DialogResult Show(string text, string title, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        { return Show(null, text, title, buttons, icon, defaultButton); }
        public static DialogResult Show(IWin32Window owner, string text, string title, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        {
            if (!Language.English) return MessageBox.Show(owner, text, title, buttons, icon, defaultButton);
            using (var dialog = new Form { Text = Language.T(title), ClientSize = new Size(660, 350), MinimumSize = new Size(540, 260), StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = new Font("Segoe UI", 10) })
            {
                var content = new TextBox { Text = Language.T(text), Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = SystemColors.Control, Margin = new Padding(16) };
                var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) }; body.Controls.Add(content);
                var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(12), FlowDirection = FlowDirection.RightToLeft };
                bool yesNo = buttons == MessageBoxButtons.YesNo;
                var negative = new Button { Text = yesNo ? "No" : "Cancel", DialogResult = yesNo ? DialogResult.No : DialogResult.Cancel, Width = 105, Height = 32 };
                var positive = new Button { Text = yesNo ? "Yes" : "OK", DialogResult = yesNo ? DialogResult.Yes : DialogResult.OK, Width = 105, Height = 32 };
                if (buttons != MessageBoxButtons.OK) footer.Controls.Add(negative);
                footer.Controls.Add(positive); dialog.Controls.Add(body); dialog.Controls.Add(footer);
                dialog.AcceptButton = defaultButton == MessageBoxDefaultButton.Button2 ? negative : positive;
                dialog.CancelButton = buttons == MessageBoxButtons.OK ? positive : negative;
                return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            }
        }
    }
}
