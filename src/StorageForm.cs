using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LayerUnpacker
{
    public sealed class StorageForm : Form
    {
        readonly ComboBox tasks = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ListView results = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
        readonly Label usage = new Label { Dock = DockStyle.Fill, AutoSize = true };
        readonly Button clean = new Button { Text = "清理中间文件", AutoSize = true };
        readonly List<string> paths = new List<string>();
        readonly List<string> protectedInputs;
        JobState state;
        StorageUsage measured;
        bool busy;
        public bool IsBusy { get { return busy; } }
        public StorageForm(IEnumerable<JobState> states, IEnumerable<string> inputs)
        {
            Text = "结果管理 · 占用与清理"; Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(870, 480); MinimumSize = new Size(760, 440); StartPosition = FormStartPosition.CenterParent;
            protectedInputs = inputs.ToList();
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4 };
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); Controls.Add(grid);
            grid.Controls.Add(tasks, 0, 0); grid.Controls.Add(results, 0, 1); grid.Controls.Add(usage, 0, 2);
            results.Columns.Add("层级 / 状态", 140); results.Columns.Add("来源", 235); results.Columns.Add("保留文件", 90); results.Columns.Add("结果目录", 290);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; grid.Controls.Add(buttons, 0, 3);
            AddButton(buttons, "载入已有任务", delegate {
                if (busy) return;
                using (var dialog = new OpenFileDialog { Filter = Language.T("任务状态|任务状态.json") })
                    if (dialog.ShowDialog(this) == DialogResult.OK) { try { AddState(Disk.Load(dialog.FileName)); tasks.SelectedIndex = paths.IndexOf(dialog.FileName); } catch (Exception ex) { Error(ex); } }
            });
            AddButton(buttons, "打开选中结果", delegate { if (state != null) Open(results.SelectedItems.Count > 0 ? (string)results.SelectedItems[0].Tag : StorageManager.PrimaryResult(state)); });
            AddButton(buttons, "打开任务目录", delegate { if (state != null) Open(state.Home); });
            AddButton(buttons, "重新统计", async delegate { if (!busy) await RefreshUsage(); });
            buttons.Controls.Add(clean); clean.Click += Cleanup;
            tasks.SelectedIndexChanged += async delegate { await RefreshUsage(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; usage.Text = "正在校验或清理，请等待完成后关闭。"; } };
            foreach (var s in states) AddState(s);
            Shown += delegate { if (paths.Count > 0) tasks.SelectedIndex = 0; else { clean.Enabled = false; usage.Text = "载入任务目录中的“任务状态.json”，即可查看占用和各层结果。"; } };
        }
        public void RefreshLanguage()
        {
            Language.Apply(this);
            if (state != null && results.Items.Count == state.Nodes.Count)
                for (int i = 0; i < state.Nodes.Count; i++)
                    results.Items[i].Text = Language.T("第 ") + state.Nodes[i].Depth + Language.T(" 层 · ") + Language.T(state.Nodes[i].Status);
        }
        void AddState(JobState s)
        {
            string path = Path.Combine(s.Home, "任务状态.json");
            if (paths.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
            paths.Add(path); protectedInputs.AddRange(VolumeSet.Inputs(s)); tasks.Items.Add(Path.GetFileName(s.Home));
        }
        void AddButton(Control panel, string title, EventHandler handler) { var b = new Button { Text = title, AutoSize = true }; b.Click += handler; panel.Controls.Add(b); }
        void Error(Exception ex) { LocalizedMessageBox.Show(this, ex is IOException ? ex.Message : "操作失败，请检查任务文件和目录权限。", "结果管理", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        void Open(string path) { try { if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", BandizipEngine.Quote(path)) { UseShellExecute = false }); } catch (Exception ex) { Error(ex); } }
        async Task RefreshUsage()
        {
            if (busy || tasks.SelectedIndex < 0) return;
            busy = true; tasks.Enabled = false; clean.Enabled = false; usage.Text = "正在统计实际文件大小……";
            try
            {
                state = Disk.Load(paths[tasks.SelectedIndex]);
                measured = await Task.Run(() => StorageManager.Measure(state));
                results.Items.Clear();
                foreach (var node in state.Nodes)
                {
                    var item = new ListViewItem(Language.T("第 ") + node.Depth + Language.T(" 层 · ") + Language.T(node.Status)) { Tag = node.Result };
                    item.SubItems.Add(Path.GetFileName(node.Source)); item.SubItems.Add(node.Exported.Count.ToString()); item.SubItems.Add(node.Result); results.Items.Add(item);
                }
                usage.Text = "最终结果：" + StorageManager.Size(measured.Results) + "（" + measured.ResultFiles + " 个文件）    中间文件：" + StorageManager.Size(measured.Work)
                    + "\r\n可清理：" + StorageManager.Size(measured.Reclaimable) + "    保留的中间内容：" + StorageManager.Size(Math.Max(0, measured.Work - measured.Reclaimable))
                    + "\r\n原始输入：" + StorageManager.Size(measured.Input) + "（单独统计） · 仅全部成功的任务可清理，来源不明的残留会保留。"
                    + (state.CleanupPending ? "\r\n上次清理未完成，可以重试。" : "\r\n按文件大小统计；清理前会校验最终结果和中间文件。");
                clean.Enabled = state.Status == "成功" && state.Nodes.All(n => n.Status == "完成") && (measured.Reclaimable > 0 || state.CleanupPending);
            }
            catch (Exception ex) { state = null; usage.Text = "无法统计此任务。"; Error(ex); }
            finally { busy = false; tasks.Enabled = true; }
        }
        async void Cleanup(object sender, EventArgs e)
        {
            if (busy || state == null || measured == null) return;
            if (LocalizedMessageBox.Show(this, "确认最终结果已满足需要后，可永久删除约 " + StorageManager.Size(measured.Reclaimable) + " 的已登记中间文件。\r\n\r\n原始输入、最终结果和报告会保留。之后重新解压需从原始输入新建任务。\r\n\r\n现在校验并清理？", "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            busy = true; tasks.Enabled = false; clean.Enabled = false;
            try
            {
                string path = paths[tasks.SelectedIndex];
                state = await Task.Run(() => StorageManager.Clean(path, protectedInputs, message => BeginInvoke(new Action(() => usage.Text = message))));
                StorageManager.WriteIndex(state);
                LocalizedMessageBox.Show(this, "清理完成，累计释放 " + StorageManager.Size(state.ReleasedBytes) + "。", "结果管理");
            }
            catch (Exception ex) { Error(ex); }
            finally { busy = false; tasks.Enabled = true; }
            await RefreshUsage();
        }
    }
}
