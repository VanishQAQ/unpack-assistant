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
    public sealed class MainForm : Form
    {
        readonly Color ink = Color.FromArgb(27, 43, 65), blue = Color.FromArgb(39, 103, 224);
        readonly List<string> inputs = new List<string>();
        readonly List<string> candidates = new List<string>();
        readonly List<Runner> jobs = new List<Runner>();
        readonly ListView files = new ListView();
        readonly TreeView tree = new TreeView();
        readonly ImePasswordBox password = new ImePasswordBox();
        readonly TextBox output = new TextBox(), enginePath = new TextBox();
        readonly ListBox passwordList = new ListBox();
        readonly TextBox log = new TextBox();
        readonly Panel advancedHost = new Panel { Dock = DockStyle.Fill };
        readonly Label status = new Label(), note = new Label();
        readonly NumericUpDown depth = Number(30, 1, 100), nodes = Number(1000, 1, 10000), gigabytes = Number(20, 1, 2048), fileCount = Number(100000, 1, 1000000), minutes = Number(30, 1, 1440), freeGb = Number(1, 0, 1024);
        readonly Button start, pause, resume, cancel, add, remove, restore, clear;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        bool running, closing;
        Runner current;
        SettingsForm settingsPage;
        readonly PasswordStore passwordStore;
        readonly OutputDirectoryStore outputStore;
        readonly PreferencesStore preferencesStore;
        bool preferencesReady;
        bool restoreHelpShown;
        readonly CheckBox show = new CheckBox();
        readonly ModeSwitch mode = new ModeSwitch();
        readonly System.Windows.Forms.Timer outputSaveTimer = new System.Windows.Forms.Timer { Interval = 600 };
        public MainForm(bool persistPasswords = true, string passwordFile = null)
        {
            passwordStore = persistPasswords ? new PasswordStore(passwordFile) : null;
            outputStore = persistPasswords ? new OutputDirectoryStore(passwordFile == null ? null : Path.Combine(Path.GetDirectoryName(passwordFile), "output-directory.txt")) : null;
            preferencesStore = persistPasswords ? new PreferencesStore(passwordFile == null ? null : Path.Combine(Path.GetDirectoryName(passwordFile), "preferences.json")) : null;
            Text = "拆包助手 · 自动多层解压工具 0.3.3 预览版";
            Font = new Font("Microsoft YaHei UI", 9F); ForeColor = ink;
            BackColor = Color.FromArgb(244, 247, 251); StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1100, 780); MinimumSize = new Size(940, 720); AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 77));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 265));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            Controls.Add(layout);
            var header = new Panel { Dock = DockStyle.Fill };
            mode.Location = new Point(215, 7); header.Controls.Add(mode);
            // Hide the entry while retaining its state and implementation.
            mode.Visible = false; mode.TabStop = false;
            mode.ValueChanged += delegate { SavePreferences(); };
            header.Controls.Add(new Label { Text = "拆包助手", Font = new Font(Font.FontFamily, 25, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0), ForeColor = ink });
            var tag = new Label { Text = "本地处理  /  0.3.3 预览版", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, ForeColor = blue };
            header.Controls.Add(tag); header.Resize += delegate { tag.Location = new Point(header.Width - tag.PreferredWidth - 4, 16); }; layout.Controls.Add(header, 0, 0);
            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); layout.Controls.Add(top, 0, 1);
            var left = Card("01  待处理文件"); var right = Card("02  候选密码"); left.Margin = new Padding(0, 0, 12, 0); right.Margin = Padding.Empty;
            top.Controls.Add(left, 0, 0); top.Controls.Add(right, 1, 0);
            var inputGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            inputGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); inputGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.Controls.Add(inputGrid);
            var fileButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            add = Button("添加文件", delegate { using (var dialog = new OpenFileDialog { Multiselect = true, Filter = "所有文件|*.*" }) if (dialog.ShowDialog(this) == DialogResult.OK) AddInputs(dialog.FileNames); });
            remove = Button("移除选中", delegate { if (running) return; foreach (ListViewItem item in files.SelectedItems) inputs.Remove((string)item.Tag); RefreshFiles(); });
            clear = Button("全部移除", delegate { if (running) return; inputs.Clear(); RefreshFiles(); });
            restore = Button("恢复任务", Restore);
            fileButtons.Controls.AddRange(new Control[] { add, remove, clear, restore }); inputGrid.Controls.Add(fileButtons, 0, 0);
            files.MultiSelect = true; files.Dock = DockStyle.Fill; files.View = View.Details; files.FullRowSelect = true; files.HideSelection = false; files.BorderStyle = BorderStyle.None;
            files.Columns.Add("文件（Ctrl/Shift 多选，支持拖放）", 370); files.Columns.Add("大小", 85); files.AllowDrop = true;
            files.DragEnter += delegate(object s, DragEventArgs e) { if (!running && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            files.DragDrop += delegate(object s, DragEventArgs e) { AddInputs((string[])e.Data.GetData(DataFormats.FileDrop)); }; inputGrid.Controls.Add(files, 0, 1);
            var pwGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            pwGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); pwGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 39)); pwGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); pwGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); right.Controls.Add(pwGrid);
            password.Dock = DockStyle.Fill; password.UseSystemPasswordChar = true; password.Margin = new Padding(3, 2, 3, 2);
            password.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter && !password.Composing) { AddPassword(); e.SuppressKeyPress = true; } }; pwGrid.Controls.Add(password, 0, 0);
            var pwButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            pwButtons.Controls.Add(Button("添加", delegate { AddPassword(); }, 60));
            pwButtons.Controls.Add(Button("粘贴多行", delegate { if (Clipboard.ContainsText()) { AddCandidates(Clipboard.GetText().Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)); } }, 87));
            pwButtons.Controls.Add(Button("移除", delegate { if (passwordList.SelectedIndex >= 0) { candidates.RemoveAt(passwordList.SelectedIndex); RefreshPasswords(); SavePasswords(); } }, 60));
            pwGrid.Controls.Add(pwButtons, 0, 1);
            passwordList.Dock = DockStyle.Fill; passwordList.BorderStyle = BorderStyle.None; passwordList.IntegralHeight = false; pwGrid.Controls.Add(passwordList, 0, 2);
            show.Text = "始终显示（输入时自动显示）"; show.Dock = DockStyle.Fill; show.AutoSize = true;
            show.CheckedChanged += delegate { password.Reveal = show.Checked; passwordList.Tag = show.Checked; RefreshPasswords(); }; pwGrid.Controls.Add(show, 0, 3);
            var pathGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(0, 14, 0, 4) };
            pathGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82)); pathGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            pathGrid.Controls.Add(new Label { Text = "输出目录", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            output.Text = @"E:\NO GAME NO LIFE"; output.Dock = DockStyle.Fill;
            pathGrid.Controls.Add(output, 1, 0); pathGrid.Controls.Add(Button("选择目录", delegate { using (var dialog = new FolderBrowserDialog { Description = "选择输出目录" }) if (dialog.ShowDialog(this) == DialogResult.OK) output.Text = dialog.SelectedPath; }), 2, 0);
            note.Text = "自动识别普通包与分卷 · 同组分卷放在同一目录，可全部添加"; note.Dock = DockStyle.Fill; note.ForeColor = Color.FromArgb(100, 114, 134); note.TextAlign = ContentAlignment.MiddleLeft;
            pathGrid.Controls.Add(note, 1, 1); layout.Controls.Add(pathGrid, 0, 2);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var taskTab = new TabPage("任务进度"); tabs.TabPages.Add(taskTab);
            tree.Dock = DockStyle.Fill; tree.BorderStyle = BorderStyle.None; tree.ItemHeight = 28; tree.ShowNodeToolTips = true; taskTab.Controls.Add(tree);
            log.Dock = DockStyle.Fill; log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.BackColor = Color.White; log.BorderStyle = BorderStyle.None; 
            var settings = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 4, RowCount = 5, AutoScroll = true };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            settings.Controls.Add(new Label { Text = "解压引擎", AutoSize = true }, 0, 0); enginePath.Text = ArchiveEngine.Find(); enginePath.Dock = DockStyle.Fill;
            settings.Controls.Add(enginePath, 1, 0); settings.SetColumnSpan(enginePath, 2);
            settings.Controls.Add(Button("选择引擎", delegate { using (var dialog = new OpenFileDialog { Filter = "支持的引擎|bz.exe;7z.exe;WinRAR.exe;Rar.exe|7-Zip|7z.exe|WinRAR（仅 RAR）|WinRAR.exe;Rar.exe|Bandizip|bz.exe" }) if (dialog.ShowDialog(this) == DialogResult.OK) enginePath.Text = dialog.FileName; }), 3, 0);
            Setting(settings, "最大层数", depth, 0, 1); Setting(settings, "最多归档数", nodes, 2, 1);
            Setting(settings, "累计写入上限 / GB", gigabytes, 0, 2); Setting(settings, "累计文件数上限", fileCount, 2, 2);
            Setting(settings, "单次超时 / 分钟", minutes, 0, 3); Setting(settings, "最低剩余空间 / GB", freeGb, 2, 3);
            var preview = new Label { Text = "Bandizip / 7-Zip：ZIP、7z、RAR；WinRAR：仅 RAR。引擎路径自动保存。\r\n预览版：请处理可信归档。恶意链接防护尚未完成验收；资源限制为轮询软限制。\r\n分卷模式支持常见 7z / ZIP / RAR 分卷；Office、APK 等容器默认完整保留。已保存密码仅供当前 Windows 用户恢复。", Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.FromArgb(110, 92, 68), Padding = new Padding(0, 8, 0, 0) };
            settings.Controls.Add(preview, 0, 4); settings.SetColumnSpan(preview, 4); advancedHost.Controls.Add(settings); layout.Controls.Add(tabs, 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), WrapContents = false };
            start = Button("开始解压", Start, 112); start.BackColor = blue; start.ForeColor = Color.White; start.FlatAppearance.BorderSize = 0;
            pause = Button("暂停", delegate { if (current != null) { current.PauseRequested = true; pause.Enabled = false; resume.Enabled = true; Append("将在当前解压完成后的安全节点暂停。"); } }, 75);
            resume = Button("继续", Resume, 75); cancel = Button("取消", delegate { if (current != null) { current.Cancel(); Append("正在取消当前解压任务……"); } }, 75);
            var settingsButton = Button("设置", ShowSettings, 102);
            Disposed += delegate { log.Dispose(); advancedHost.Dispose(); };
            actions.Controls.AddRange(new Control[] { start, pause, resume, cancel, settingsButton }); layout.Controls.Add(actions, 0, 4);
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.Text = "就绪 · 添加文件即可开始"; layout.Controls.Add(status, 0, 5);
            timer.Interval = 650; timer.Tick += delegate { if (running) RefreshTree(); }; timer.Start();
            FormClosing += OnClosing; SetBusy(false);
            if (outputStore != null)
            {
                try { output.Text = outputStore.Load() ?? output.Text; }
                catch { Append("输出目录配置读取失败，暂时使用默认目录。"); }
                output.TextChanged += delegate { outputSaveTimer.Stop(); outputSaveTimer.Start(); };
                output.Leave += delegate { SaveOutputDirectory(); };
                outputSaveTimer.Tick += delegate { SaveOutputDirectory(); };
            }
            if (passwordStore != null)
            {
                try { candidates.AddRange(passwordStore.Load()); RefreshPasswords(); if (candidates.Count > 0) Append("已恢复 " + candidates.Count + " 个候选密码。"); }
                catch { note.Text = "已保存密码读取失败，请重新添加；原配置尚未修改。"; Append(note.Text); }
            }
            if (preferencesStore != null)
            {
                try
                {
                    var saved = preferencesStore.Load();
                    if (saved.Engine != null) enginePath.Text = saved.Engine;
                    SetNumber(depth, saved.Depth); SetNumber(nodes, saved.Nodes); SetNumber(gigabytes, saved.Gigabytes);
                    SetNumber(fileCount, saved.Files); SetNumber(minutes, saved.Minutes); SetNumber(freeGb, saved.FreeGb);
                    show.Checked = saved.ShowPasswords;
                    mode.VolumeMode = saved.VolumeMode;
                    restoreHelpShown = saved.RestoreHelpShown;
                    foreach (string path in saved.Inputs ?? new string[0])
                        if (!string.IsNullOrWhiteSpace(path) && !inputs.Contains(path, StringComparer.OrdinalIgnoreCase)) inputs.Add(path);
                    RefreshFiles();
                }
                catch { Append("设置读取失败，暂时使用默认值；原配置尚未修改。"); }
                preferencesReady = true;
                foreach (var control in new[] { depth, nodes, gigabytes, fileCount, minutes, freeGb }) control.ValueChanged += delegate { SavePreferences(); };
                enginePath.TextChanged += delegate { SavePreferences(); };
                show.CheckedChanged += delegate { SavePreferences(); };
            }
        }
        static void SetNumber(NumericUpDown control, int value) { control.Value = Math.Max(control.Minimum, Math.Min(control.Maximum, value)); }
        void SavePreferences()
        {
            if (!preferencesReady || preferencesStore == null) return;
            try { preferencesStore.Save(new Preferences { Engine = enginePath.Text, Depth = (int)depth.Value, Nodes = (int)nodes.Value, Gigabytes = (int)gigabytes.Value, Files = (int)fileCount.Value, Minutes = (int)minutes.Value, FreeGb = (int)freeGb.Value, ShowPasswords = show.Checked, VolumeMode = mode.VolumeMode, RestoreHelpShown = restoreHelpShown, Inputs = inputs.ToArray() }); }
            catch { Append("设置保存失败，请检查本地配置目录权限；当前更改仍可使用。"); }
        }
        static NumericUpDown Number(decimal value, decimal min, decimal max) { return new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 110, ThousandsSeparator = true }; }
        static void Setting(TableLayoutPanel panel, string caption, Control control, int col, int row)
        { panel.Controls.Add(new Label { Text = caption, AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, col, row); panel.Controls.Add(control, col + 1, row); }
        GroupBox Card(string title) { return new GroupBox { Text = title, Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12, 9, 12, 10), ForeColor = ink }; }
        Button Button(string title, EventHandler handler, int width = 96)
        {
            var b = new Button { Text = title, Width = width, Height = 31, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(212, 221, 234); b.Click += handler; return b;
        }
        void AddInputs(IEnumerable<string> paths)
        {
            if (running) return;
            foreach (string path in paths) if (File.Exists(path) && !inputs.Contains(path, StringComparer.OrdinalIgnoreCase)) inputs.Add(Path.GetFullPath(path));
            RefreshFiles();
        }
        void RefreshFiles()
        {
            SavePreferences();
            files.Items.Clear(); foreach (string path in inputs)
            {
                var item = new ListViewItem(Path.GetFileName(path)) { Tag = path, ToolTipText = path };
                item.SubItems.Add(File.Exists(path) ? (new FileInfo(path).Length / 1048576.0).ToString("N1") + " MB" : "文件缺失"); files.Items.Add(item);
            }
        }
        void AddPassword() { if (password.Text.Length > 0) { AddCandidates(new[] { password.Text }); password.Clear(); } }
        void AddCandidates(IEnumerable<string> values)
        {
            int before = candidates.Count;
            foreach (string value in values)
            {
                if (value.IndexOf('"') >= 0 || value.IndexOf('\0') >= 0) { Append("当前命令行接口不支持包含双引号或空字符的密码；该条未添加。"); continue; }
                if (value.Length > 0 && !candidates.Contains(value)) candidates.Add(value);
            }
            RefreshPasswords();
            if (before != candidates.Count) SavePasswords();
        }
        void SavePasswords()
        {
            if (passwordStore == null) return;
            try { passwordStore.Save(candidates); }
            catch { note.Text = "密码保存失败：本次仍可使用，但下次启动可能无法恢复。"; Append(note.Text); }
        }
        void SaveOutputDirectory()
        {
            outputSaveTimer.Stop();
            if (outputStore == null) return;
            try { outputStore.Save(output.Text); }
            catch { Append("输出目录保存失败，请检查本地配置目录权限。"); }
        }
        void RefreshPasswords()
        {
            passwordList.Items.Clear(); bool show = passwordList.Tag is bool && (bool)passwordList.Tag;
            for (int i = 0; i < candidates.Count; i++) passwordList.Items.Add(show ? candidates[i] : "候选 " + (i + 1) + "    ••••••••");
        }
        void Append(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<string>(Append), text); } catch (InvalidOperationException) { } return; }
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
            if (log.TextLength > 80000) log.Text = log.Text.Substring(log.TextLength - 60000);
            status.Text = text;
        }
        Limits GetLimits() { return new Limits { MaxDepth = (int)depth.Value, MaxNodes = (int)nodes.Value, MaxBytes = (long)gigabytes.Value * 1024 * 1024 * 1024, MaxFiles = (int)fileCount.Value, TimeoutSeconds = (int)minutes.Value * 60, MinFreeBytes = (long)freeGb.Value * 1024 * 1024 * 1024 }; }
        void SetBusy(bool value)
        {
            running = value; start.Enabled = !value; add.Enabled = !value; remove.Enabled = !value; clear.Enabled = !value; restore.Enabled = !value;
            pause.Enabled = value; cancel.Enabled = value; resume.Enabled = !value && jobs.Any(j => j.State.Status == "等待密码" || j.State.Status == "已取消");
            output.Enabled = !value; enginePath.Enabled = !value; mode.Enabled = !value;
        }
        async void Start(object sender, EventArgs e)
        {
            if (running) return;
            try
            {
                AddPassword(); if (inputs.Count == 0) { Append("请先添加一个或多个压缩文件。"); return; }
                new ArchiveEngine(enginePath.Text); Disk.NoLinks(output.Text); Directory.CreateDirectory(output.Text);
                var sources = inputs.Select(input => {
                    var group = VolumeSet.Find(input);
                    return group == null ? input : group.Entry;
                }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                jobs.Clear();
                foreach (string input in sources)
                {
                    bool isVolume = VolumeSet.Find(input) != null;
                    var runner = new Runner(Runner.Create(input, output.Text, enginePath.Text, GetLimits(), isVolume), candidates, false); runner.Changed += Append; jobs.Add(runner);
                }
                await RunJobs();
            }
            catch (Exception ex) { Append(ex is StopException || ex is IOException ? ex.Message : "无法创建任务，请检查输入文件、输出目录和解压引擎路径。"); SetBusy(false); }
        }
        async Task RunJobs()
        {
            SetBusy(true);
            try
            {
                foreach (var job in jobs)
                {
                    if (job.State.Status == "成功" || job.State.Status == "失败" || job.State.Status == "文件已变化" || job.State.Status == "部分完成") continue;
                    current = job; job.AddPasswords(candidates); job.PauseRequested = false;
                    await Task.Run(new Action(job.Run));
                    if (job.State.Status == "已取消") break;
                }
            }
            finally
            {
                current = null; SetBusy(false); RefreshTree();
                if (closing) { timer.Stop(); Close(); }
            }
        }
        async void Resume(object sender, EventArgs e)
        {
            AddPassword();
            if (running)
            {
                if (current != null) { current.AddPasswords(candidates); current.PauseRequested = false; pause.Enabled = true; resume.Enabled = false; }
                return;
            }
            try { await RunJobs(); } catch { Append("恢复失败，请检查任务文件和目录权限。"); SetBusy(false); }
        }
        void Restore(object sender, EventArgs e)
        {
            ShowRestoreHelpOnce(delegate {
                MessageBox.Show(this,
                    "恢复任务可以继续处理上次未完成的解压，例如关闭软件、取消解压或缺少密码之后。\r\n\r\n" +
                    "1. 在接下来的窗口中，打开上次的输出文件夹，选择“任务状态.json”。\r\n" +
                    "2. 如需密码，先添加候选密码，再点击“继续”。\r\n\r\n" +
                    "程序会检查原文件和已完成的内容，复用已完成的层；中途被打断的那一层通常需要重新解压。\r\n\r\n" +
                    "请保留原压缩包和完整的任务文件夹。解压新文件时直接使用“添加文件”即可。\r\n\r\n" +
                    "此说明仅在首次点击“恢复任务”时显示。",
                    "恢复任务 · 使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            using (var dialog = new OpenFileDialog { Filter = "任务状态|任务状态.json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var state = Disk.Load(dialog.FileName); state.Engine = enginePath.Text;
                    mode.VolumeMode = state.VolumeMode;
                    var runner = new Runner(state, candidates, true); runner.Changed += Append; jobs.Clear(); jobs.Add(runner);
                    // Explicit continue verifies the persisted fingerprints before any processing.
                    state.Status = "排队";
                    RefreshTree(); resume.Enabled = true; Append("已载入任务。添加密码后点击继续，将先验证已有文件。");
                }
                catch (Exception ex) { Append(ex is IOException ? ex.Message : "无法读取任务状态。"); }
            }
        }
        void ShowRestoreHelpOnce(Action showHelp)
        {
            if (restoreHelpShown) return;
            showHelp();
            restoreHelpShown = true;
            SavePreferences();
        }
        void RefreshTree()
        {
            if (jobs.Count == 0) return;
            string selected = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as string;
            tree.BeginUpdate(); tree.Nodes.Clear();
            foreach (Runner job in jobs)
            {
                var root = new TreeNode(Path.GetFileName(job.State.Input) + "    [" + job.State.Status + "]") { Tag = job.State.Home, ToolTipText = job.State.Home };
                tree.Nodes.Add(root); var lookup = new Dictionary<int, TreeNode>();
                if (selected == job.State.Home) tree.SelectedNode = root;
                lock (job.Sync) foreach (ArchiveNode node in job.State.Nodes)
                {
                    var item = new TreeNode("第 " + node.Depth + " 层 · " + Path.GetFileName(node.Source) + "    " + node.Status) { ToolTipText = node.Message, Tag = node.Result };
                    item.ForeColor = node.Status == "完成" ? Color.FromArgb(21, 125, 89) : node.Status == "等待密码" || node.Status == "失败" || node.Status == "受限" ? Color.FromArgb(165, 86, 25) : ink;
                    TreeNode parent; if (lookup.TryGetValue(node.Parent, out parent)) parent.Nodes.Add(item); else root.Nodes.Add(item); lookup[node.Id] = item;
                    if (selected == node.Result) tree.SelectedNode = item;
                }
            }
            tree.ExpandAll(); tree.EndUpdate();
            if (!running) status.Text = string.Join("  ·  ", jobs.GroupBy(j => j.State.Status).Select(g => g.Key + " " + g.Count() + " 个任务"));
        }
        void ShowSettings(object sender, EventArgs e)
        {
            if (settingsPage != null) return;
            bool openedWhileRunning = running;
            var mainPage = Controls[0];
            settingsPage = new SettingsForm(jobs.Select(j => j.State).ToArray(), inputs.Concat(jobs.SelectMany(j => VolumeSet.Inputs(j.State))).ToArray(), output.Text, log, advancedHost, running, () => ExportReport(this, EventArgs.Empty)) { TopLevel = false, FormBorderStyle = FormBorderStyle.None, MinimumSize = Size.Empty, Dock = DockStyle.Fill };
            settingsPage.FormClosed += delegate {
                var closed = settingsPage; settingsPage = null; Controls.Remove(closed); closed.Dispose(); mainPage.Visible = true;
                if (!openedWhileRunning && !running) RefreshManagedJobs();
            };
            Controls.Add(settingsPage); mainPage.Visible = false; settingsPage.Show(); settingsPage.BringToFront();
        }
        void RefreshManagedJobs()
        {
            for (int i = 0; i < jobs.Count; i++)
            {
                try { var restored = new Runner(Disk.Load(Path.Combine(jobs[i].State.Home, "任务状态.json")), candidates, true); restored.Changed += Append; jobs[i] = restored; restored.WriteReport(); }
                catch { Append("部分任务状态无法重新读取，请通过恢复任务载入。"); }
            }
            SetBusy(false); RefreshTree();
        }
        void ExportReport(object sender, EventArgs e)
        {
            if (running) { Append("请等待任务停止后导出报告。"); return; }
            if (jobs.Count == 0) return;
            using (var dialog = new SaveFileDialog { Filter = "Markdown 报告|*.md", FileName = "解压处理报告.md" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var text = new System.Text.StringBuilder(); foreach (Runner job in jobs) { job.WriteReport(); text.AppendLine(File.ReadAllText(Path.Combine(job.State.Home, "处理报告.md"))); }
                    File.WriteAllText(dialog.FileName, text.ToString(), new System.Text.UTF8Encoding(false)); Append("处理报告已导出。");
                }
                catch { Append("报告写入失败，请检查目录权限。"); }
            }
        }
        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (settingsPage != null && settingsPage.IsBusy) { e.Cancel = true; MessageBox.Show(this, "正在统计或清理，请完成后关闭。", "拆包助手"); return; }
            SaveOutputDirectory();
            SavePreferences();
            AddPassword();
            if (running) { e.Cancel = true; closing = true; if (current != null) current.Cancel(); Append("正在停止解压并保存任务……"); }
            else timer.Stop();
        }
        public void RenderPreview(string path)
        {
            CreateControl(); Show(); Application.DoEvents(); timer.Stop();
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(path); }
            Close();
        }
        public void Smoke(string source, string destination, string report)
        {
            ShowInTaskbar = false; Opacity = 0;
            AddInputs(new[] { source }); output.Text = destination;
            AddCandidates((Environment.GetEnvironmentVariable("LAYER_UNPACKER_TEST_KEYS") ?? "").Split('\n'));
            var watch = Stopwatch.StartNew(); bool begun = false;
            using (var watcher = new System.Windows.Forms.Timer { Interval = 250 })
            {
                Shown += delegate { BeginInvoke(new Action(delegate { begun = true; Start(this, EventArgs.Empty); })); watcher.Start(); };
                watcher.Tick += delegate
                {
                    if (!begun || (running && watch.Elapsed.TotalSeconds < 60)) return;
                    watcher.Stop(); timer.Stop();
                    bool ok = !running && jobs.Count == 1 && jobs[0].State.Status == "成功";
                    File.WriteAllText(report, ok ? "PASS: 界面添加输入、候选密码、开始解压、后台完成与任务树更新。\r\n" + jobs[0].State.Home : "FAIL: " + status.Text);
                    BeginInvoke(new Action(Close));
                };
                Application.Run(this);
            }
        }
    }
    static class Program
    {
        [STAThread] static void Main(string[] args)
        {
            RuntimeSetup.Initialize();
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 4 && args[0] == "--ui-smoke")
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                try { using (var form = new MainForm(false)) form.Smoke(args[1], args[2], args[3]); }
                catch (Exception ex) { File.WriteAllText(args[3], "FAIL: " + ex.ToString()); }
                return;
            }
            if (args.Length == 2 && args[0] == "--render")
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                try { using (var form = new MainForm(false)) { form.ShowInTaskbar = false; form.Opacity = 0; form.RenderPreview(args[1]); } }
                catch (Exception ex) { File.WriteAllText(args[1] + ".error.txt", ex.ToString()); }
                return;
            }
            Application.ThreadException += delegate { MessageBox.Show("界面操作失败，请检查目录权限后重试。", "拆包助手"); };
            try { Application.Run(new MainForm()); }
            catch (Exception) { MessageBox.Show("程序启动失败。请确认系统已启用 .NET Framework 4.8。", "拆包助手", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
