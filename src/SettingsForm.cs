using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LayerUnpacker
{
    public sealed class SettingsForm : Form
    {
        readonly ListBox navigation = new ListBox();
        readonly Panel content = new Panel { Dock = DockStyle.Fill };
        readonly Label heading = new Label { Dock = DockStyle.Top, Height = 48 };
        readonly Control[] pages;
        readonly Control log, advanced;
        readonly StorageForm storage;
        public bool IsBusy { get { return storage != null && storage.IsBusy; } }
        public SettingsForm(JobState[] states, string[] inputs, string output, Control logView, Control advancedView, bool running, Action export)
        {
            Text = "设置 · 拆包助手"; Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(244, 247, 251); ForeColor = Color.FromArgb(27, 43, 65);
            ClientSize = new Size(1120, 620); MinimumSize = new Size(1060, 560); StartPosition = FormStartPosition.CenterParent;
            log = logView; advanced = advancedView;
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(grid);
            var bar = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(18, 12, 18, 6) };
            var back = new Button { Text = "← 返回", Width = 96, Dock = DockStyle.Left, FlatStyle = FlatStyle.Flat, BackColor = Color.White };
            back.Click += delegate { Close(); }; bar.Controls.Add(back);
            bar.Controls.Add(new Label { Text = "设置", AutoSize = true, Font = new Font(Font.FontFamily, 16, FontStyle.Bold), Location = new Point(138, 14) }); Controls.Add(bar);
            navigation.Dock = DockStyle.Fill; navigation.BorderStyle = BorderStyle.None; navigation.BackColor = BackColor;
            navigation.IntegralHeight = false; navigation.ItemHeight = 46; navigation.DrawMode = DrawMode.OwnerDrawFixed;
            navigation.Items.AddRange(new object[] { "打开结果", "导出报告", "结果管理", "运行记录", "高级选项" });
            navigation.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var brush = new SolidBrush(selected ? Color.FromArgb(225, 235, 253) : BackColor)) e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(e.Graphics, navigation.Items[e.Index].ToString(), Font, new Rectangle(e.Bounds.X + 14, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height), selected ? Color.FromArgb(39, 103, 224) : ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            grid.Controls.Add(navigation, 0, 0);
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 12, 12), BackColor = Color.White };
            heading.Font = new Font(Font.FontFamily, 17, FontStyle.Bold); right.Controls.Add(content); right.Controls.Add(heading); grid.Controls.Add(right, 1, 0);
            pages = new Control[5];
            var results = new Panel();
            var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
            var paths = states.SelectMany(s => s.Nodes.Where(n => n.Status == "完成").Select(n => n.Result)).Distinct().ToArray();
            if (paths.Length == 0) paths = new[] { output };
            list.Items.AddRange(paths); if (list.Items.Count > 0) list.SelectedIndex = 0;
            var open = new Button { Text = "打开选中目录", Dock = DockStyle.Bottom, Height = 36 };
            open.Click += delegate {
                string path = list.SelectedItem as string;
                if (path == null || !Directory.Exists(path)) { MessageBox.Show(this, "该目录尚未创建。", "打开结果"); return; }
                try { Process.Start(new ProcessStartInfo("explorer.exe", BandizipEngine.Quote(path)) { UseShellExecute = false }); }
                catch { MessageBox.Show(this, "无法打开目录。", "打开结果"); }
            };
            results.Controls.Add(list); results.Controls.Add(open); pages[0] = results;
            var report = new Panel();
            var preview = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Color.White };
            preview.Text = running ? "任务运行中，完成后重新打开设置即可预览报告。" : string.Join(Environment.NewLine + Environment.NewLine, states.Select(s => {
                try { string path = Path.Combine(s.Home, "处理报告.md"); return File.Exists(path) ? File.ReadAllText(path) : "报告尚未生成。"; }
                catch { return "报告暂时无法读取。"; }
            }));
            if (preview.Text.Length == 0) preview.Text = "暂无任务报告，完成解压或恢复任务后可查看。";
            var save = new Button { Text = "导出报告…", Dock = DockStyle.Bottom, Height = 36, Enabled = !running && states.Length > 0 };
            save.Click += delegate { export(); }; report.Controls.Add(preview); report.Controls.Add(save); pages[1] = report;
            if (!running)
            {
                storage = new StorageForm(states, inputs) { TopLevel = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.Fill, MinimumSize = Size.Empty };
                pages[2] = storage;
            }
            else pages[2] = new Label { Text = "任务运行中，请完成后重新打开设置管理结果。", AutoSize = false };
            pages[3] = log; pages[4] = advanced; advanced.Enabled = !running;
            foreach (var page in pages) { page.Dock = DockStyle.Fill; content.Controls.Add(page); page.Visible = false; }
            navigation.SelectedIndexChanged += delegate {
                for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == navigation.SelectedIndex;
                heading.Text = navigation.SelectedItem.ToString(); pages[navigation.SelectedIndex].BringToFront();
            };
            navigation.SelectedIndex = 0;
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (storage != null && storage.IsBusy) { e.Cancel = true; MessageBox.Show(this, "正在统计或清理，请完成后关闭设置。", "设置"); } };
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { content.Controls.Remove(log); content.Controls.Remove(advanced); advanced.Enabled = true; }
            base.Dispose(disposing);
        }
    }
}
