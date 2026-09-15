using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace LayerUnpacker
{
    public sealed class Runner
    {
        public readonly JobState State;
        public readonly object Sync = new object();
        public event Action<string> Changed;
        public event Action<Exception> Diagnostic;
        public volatile bool PauseRequested;
        public CancellationTokenSource Cancellation = new CancellationTokenSource();
        readonly ArchiveEngine engine;
        readonly List<string> passwords = new List<string>();
        readonly Dictionary<int, HashSet<string>> attempted = new Dictionary<int, HashSet<string>>();
        string recent;
        bool resumed;
        public Runner(JobState state, IEnumerable<string> candidates, bool restored)
        {
            State = state; engine = new ArchiveEngine(state.Engine);
            State.EngineVersion = FileVersionInfo.GetVersionInfo(engine.PathName).FileVersion;
            AddPasswords(candidates); resumed = restored;
        }
        public static JobState Create(string source, string output, string engine, Limits limits, bool volumeMode = false)
        {
            VolumeSet group = volumeMode ? VolumeSet.Find(source) : null;
            if (volumeMode && group == null) throw new IOException("未识别到分卷组。支持 .7z.001、.zip.001、.z01/.zip 和 RAR 分卷。");
            if (group != null) source = group.Entry;
            source = Path.GetFullPath(source); Disk.NoLinks(source); Disk.NoLinks(output);
            if (!File.Exists(source)) throw new IOException("输入文件不存在。");
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string home = Path.Combine(Path.GetFullPath(output), Disk.SafeName(Path.GetFileName(source)) + "_" + id);
            Directory.CreateDirectory(home);
            var state = new JobState { Id = id, Home = home, Input = source, Engine = engine,
                EngineVersion = FileVersionInfo.GetVersionInfo(engine).FileVersion, Limits = limits, Created = DateTime.Now.ToString("s") };
            state.VolumeMode = volumeMode;
            state.Nodes.Add(NewNode(state, source, 0, 1, 1, group)); Disk.Save(state); return state;
        }
        static ArchiveNode NewNode(JobState state, string source, int parent, int depth, int id, VolumeSet group = null)
        {
            return new ArchiveNode { Id = id, Parent = parent, Depth = depth, Source = source,
                Volumes = group == null ? new List<FileRecord>() : group.Records(), VolumeKind = group == null ? null : group.Kind,
                Payload = Path.Combine(state.Home, "工作区", "节点" + id.ToString("D4"), "已验证"),
                Result = Path.Combine(state.Home, "结果", "第" + depth.ToString("D2") + "层_节点" + id.ToString("D4")) };
        }
        public void AddPasswords(IEnumerable<string> candidates)
        {
            lock (passwords) foreach (string p in candidates)
            {
                if (p != null && (p.IndexOf('"') >= 0 || p.IndexOf('\0') >= 0)) throw new StopException("失败", "当前命令行接口不支持包含双引号或空字符的密码。");
                if (!string.IsNullOrEmpty(p) && !passwords.Contains(p)) passwords.Add(p);
            }
        }
        public void Cancel() { Cancellation.Cancel(); PauseRequested = false; }
        void Notify(string text)
        {
            // Engine output/comments and process arguments are deliberately never logged.
            Action<string> handler = Changed; if (handler != null) handler(text);
        }
        void Save() { Disk.Save(State); }
        void Set(ArchiveNode node, string status, string message)
        {
            node.Status = status; node.Message = message; Save(); Notify("第 " + node.Depth + " 层 · " + message);
        }
        void Checkpoint(ArchiveNode node)
        {
            Cancellation.Token.ThrowIfCancellationRequested();
            if (!PauseRequested) return;
            string oldStatus = node.Status, oldMessage = node.Message;
            Set(node, "已暂停", "已在安全节点暂停，可继续或取消。");
            try { while (PauseRequested) { Cancellation.Token.ThrowIfCancellationRequested(); Thread.Sleep(100); } }
            finally { Set(node, oldStatus, oldMessage); }
        }
        void Budget()
        {
            Cancellation.Token.ThrowIfCancellationRequested();
            if (State.WrittenBytes > State.Limits.MaxBytes || State.WrittenFiles > State.Limits.MaxFiles)
                throw new StopException("受限", "累计写入量或文件数达到限制，已停止当前分支。");
            if (new DriveInfo(Path.GetPathRoot(State.Home)).AvailableFreeSpace < State.Limits.MinFreeBytes)
                throw new StopException("受限", "磁盘剩余空间低于设置的最低值。");
        }
        Action Meter(string directory)
        {
            var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            return delegate
            {
                if (Directory.Exists(directory)) foreach (string file in Disk.WalkFiles(directory))
                {
                    long now;
                    try { now = new FileInfo(file).Length; } catch (FileNotFoundException) { continue; }
                    long old;
                    if (!sizes.TryGetValue(file, out old)) { State.WrittenFiles++; old = 0; }
                    if (now > old) { State.WrittenBytes += now - old; sizes[file] = now; } else if (!sizes.ContainsKey(file)) sizes[file] = old;
                }
                Budget();
            };
        }
        List<string> Candidates(ArchiveNode node)
        {
            var list = new List<string> { "" };
            lock (passwords)
            {
                if (!string.IsNullOrEmpty(recent)) list.Add(recent);
                list.AddRange(passwords);
            }
            HashSet<string> used;
            if (!attempted.TryGetValue(node.Id, out used)) { used = new HashSet<string>(StringComparer.Ordinal); attempted[node.Id] = used; }
            return list.Distinct(StringComparer.Ordinal).Where(p => !used.Contains(p)).ToList();
        }
        bool Repeated(ArchiveNode node)
        {
            int parent = node.Parent;
            while (parent != 0)
            {
                ArchiveNode ancestor = State.Nodes.Single(n => n.Id == parent);
                if (ancestor.SourceHash == node.SourceHash) return true;
                parent = ancestor.Parent;
            }
            return false;
        }
        void ValidatePayload(ArchiveNode node)
        {
            if (!Directory.Exists(node.Payload)) throw new StopException("文件已变化", "已验证工作文件缺失，请从原始归档创建新任务。");
            string[] actual = Disk.WalkFiles(node.Payload).ToArray();
            if (actual.Length != node.Files.Count) throw new StopException("文件已变化", "工作目录内容已变化，请从原始归档创建新任务。");
            foreach (var file in node.Files)
            {
                Cancellation.Token.ThrowIfCancellationRequested();
                string path = Disk.Under(node.Payload, file.Name);
                if (!File.Exists(path) || new FileInfo(path).Length != file.Size || Disk.Hash(path) != file.Hash)
                    throw new StopException("文件已变化", "工作文件校验失败，请从原始归档创建新任务。");
            }
            foreach (string name in node.Exported)
            {
                FileRecord file = node.Files.Single(f => f.Name == name); string path = Disk.Under(node.Result, name);
                Disk.NoLinks(path);
                if (!File.Exists(path) || new FileInfo(path).Length != file.Size || Disk.Hash(path) != file.Hash)
                    throw new StopException("文件已变化", "最终输出已变化，请保留现有文件并创建新任务。");
            }
        }
        public void Run()
        {
            using (StorageManager.Lease(State.Home)) RunCore();
        }
        void RunCore()
        {
            if (State.IntermediateCleaned)
            {
                try { StorageManager.ValidateResults(State); State.Status = "成功"; Notify("结果已验证；中间文件已清理，无需再次解压。"); }
                catch { State.Status = "文件已变化"; Notify("最终文件缺失或已变化，请从原始文件创建新任务。"); }
                Save(); WriteReport(); return;
            }
            if (Cancellation.IsCancellationRequested) { Cancellation.Dispose(); Cancellation = new CancellationTokenSource(); }
            State.Status = "进行中"; Save();
            try
            {
                foreach (var node in State.Nodes.Where(n => n.Volumes != null && n.Volumes.Count > 0)) ValidateVolumes(node);
                if (resumed)
                {
                    Notify("正在核对已有任务和文件指纹……");
                    foreach (ArchiveNode node in State.Nodes.ToArray())
                    {
                        Cancellation.Token.ThrowIfCancellationRequested();
                        if (node.SourceHash != null && (!File.Exists(node.Source) || Disk.Hash(node.Source) != node.SourceHash))
                            throw new StopException("文件已变化", "归档内容已变化，请从原始文件创建新任务。");
                        if (node.Status == "完成" || node.Status == "待扫描") ValidatePayload(node);
                        else if (node.Status == "失败" && node.Message != null && node.Message.StartsWith("文件路径过长", StringComparison.Ordinal))
                        { node.Status = "排队"; node.Message = "已启用内置长路径支持，将重新处理本层。"; }
                        else if (node.Status != "失败" && node.Status != "受限" && node.Status != "文件已变化") node.Status = "排队";
                    }
                    UpgradeLegacyVolumeNodes();
                    resumed = false;
                }
                // New nodes are appended while scanning; independent branches keep progressing.
                for (int index = 0; index < State.Nodes.Count; index++)
                {
                    ArchiveNode node = State.Nodes[index];
                    if (node.Status == "完成" || node.Status == "失败" || node.Status == "受限" || node.Status == "文件已变化") continue;
                    if (node.Status == "等待密码" && Candidates(node).Count == 0) continue;
                    try
                    {
                        Checkpoint(node); Budget();
                        if (node.Status != "待扫描") Extract(node);
                        if (node.Status == "待扫描") Scan(node);
                    }
                    catch (StopException e) { Set(node, e.State, e.Message); }
                    catch (OperationCanceledException) { Set(node, node.Status == "待扫描" ? "待扫描" : "已取消", "已取消，可点击继续重新处理当前节点。"); throw; }
                    catch (Exception e)
                    {
                        if (Diagnostic != null) Diagnostic(e);
                        Set(node, "失败", e is UnauthorizedAccessException ? "没有目录读写权限，请换一个输出目录。" : e is PathTooLongException ? "文件路径过长，请缩短输出目录或启用系统长路径支持。" : "文件或引擎操作失败，请检查文件、磁盘和引擎路径（" + e.GetType().Name + "）。");
                    }
                }
                bool complete = State.Nodes.All(n => n.Status == "完成");
                bool pending = State.Nodes.Any(n => n.Status == "等待密码");
                State.Status = complete ? "成功" : pending ? "等待密码" : State.Nodes.Any(n => n.Status == "完成") ? "部分完成" : "失败";
            }
            catch (OperationCanceledException) { State.Status = "已取消"; }
            catch (StopException e) { State.Status = e.State; RecordPreparationFailure(e.Message); Notify(e.Message); }
            catch (Exception e)
            {
                if (Diagnostic != null) Diagnostic(e);
                State.Status = "失败";
                string message = e is FileNotFoundException || e is DirectoryNotFoundException ? "输入文件或分卷不存在，请检查原文件是否已移动或删除。" :
                    e is UnauthorizedAccessException ? "无法读取分卷或任务目录：访问被拒绝，请检查文件权限或占用。" :
                    "任务准备或文件访问失败（" + e.GetType().Name + "），请检查分卷、磁盘和目录权限。";
                RecordPreparationFailure(message); Notify(message);
            }
            finally { Save(); WriteReport(); Notify("任务状态：" + State.Status); }
        }
        void RecordPreparationFailure(string message)
        {
            // Persist the reason even when failure precedes the first Extract call.
            foreach (var node in State.Nodes.Where(n => n.Status == "排队")) node.Message = message;
        }
        void UpgradeLegacyVolumeNodes()
        {
            // Old ordinary-mode scans created one failed node per physical volume.
            // Only merge untouched failures; never discard extracted data or descendants.
            foreach (var node in State.Nodes.ToArray())
            {
                if (!State.Nodes.Contains(node) || node.Parent == 0 || node.Status != "失败" || node.Format != "分卷") continue;
                var group = VolumeSet.Find(node.Source);
                if (group == null) continue;
                var siblings = State.Nodes.Where(n => n.Parent == node.Parent && group.Paths.Contains(n.Source, StringComparer.OrdinalIgnoreCase)).ToArray();
                if (siblings.Any(n => n.Status != "失败" || n.Format != "分卷" || n.Attempts != 0 || n.Files.Count != 0 || n.Exported.Count != 0 || n.GarbageFiles.Count != 0 || State.Nodes.Any(c => c.Parent == n.Id) || (Directory.Exists(n.Payload) && Disk.WalkFiles(n.Payload).Any()))) continue;
                var keep = siblings.FirstOrDefault(n => string.Equals(n.Source, group.Entry, StringComparison.OrdinalIgnoreCase));
                if (keep == null) continue;
                keep.Volumes = group.Records(); keep.VolumeKind = group.Kind;
                keep.Format = group.Kind; keep.Status = "排队"; keep.Message = "已将旧分卷节点合并，继续处理本层。";
                foreach (var other in siblings) if (other != keep) State.Nodes.Remove(other);
                Notify("已合并第 " + keep.Depth + " 层的 " + group.Paths.Length + " 个分卷，将复用已完成的外层。");
            }
            Save();
        }
        void ValidateVolumes(ArchiveNode node)
        {
            foreach (string path in VolumeSet.PathsFor(node))
                if (!File.Exists(path)) throw new StopException("文件已变化", "分卷不存在：" + Path.GetFileName(path) + "。请检查原文件是否已移动或删除。");
            var group = VolumeSet.Find(node.Source);
            if (group == null || group.Kind != node.VolumeKind || !group.Paths.SequenceEqual(VolumeSet.PathsFor(node), StringComparer.OrdinalIgnoreCase))
                throw new StopException("文件已变化", "分卷组发生变化或缺卷，请补齐后从原始分卷新建任务。");
            // A renamed ZIP must not bypass ZIP metadata checks by posing as 7z/RAR.
            using (var first = File.OpenRead(group.Paths[0]))
            {
                var header = new byte[6]; int read = first.Read(header, 0, header.Length);
                bool matches = read == 6 && (group.Kind == "7z"
                    ? header.SequenceEqual(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c })
                    : group.Kind == "RAR" ? header.SequenceEqual(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07 })
                    : header[0] == 0x50 && header[1] == 0x4b);
                if (!matches) throw new StopException("失败", "首卷内容与分卷类型不符，请检查文件格式或是否选错分卷组。");
            }
            foreach (var volume in node.Volumes)
            {
                Cancellation.Token.ThrowIfCancellationRequested();
                Notify("正在校验分卷 " + (node.Volumes.IndexOf(volume) + 1) + "/" + node.Volumes.Count + "：" + volume.Name);
                string path = Disk.Under(Path.GetDirectoryName(node.Source), volume.Name);
                string hash = Disk.Hash(path);
                if (new FileInfo(path).Length != volume.Size || (volume.Hash != null && volume.Hash != hash)) throw new StopException("文件已变化", "分卷内容已变化：" + volume.Name);
                volume.Hash = hash;
            }
            Save();
        }
        void Extract(ArchiveNode node)
        {
            if (node.Depth > State.Limits.MaxDepth) throw new StopException("受限", "已达到最大嵌套层数。");
            Disk.NoLinks(node.Source);
            Set(node, "识别中", "正在识别文件内容并计算指纹。");
            string fingerprint = Disk.Hash(node.Source);
            if (node.SourceHash != null && node.SourceHash != fingerprint) throw new StopException("文件已变化", "归档已变化，请创建新任务。");
            bool volumes = node.Volumes != null && node.Volumes.Count > 0;
            if (volumes) ValidateVolumes(node);
            node.SourceHash = fingerprint; node.Format = volumes ? node.VolumeKind : Detector.Detect(node.Source, node.Parent == 0);
            if (node.Format == "分卷") throw new StopException("失败", "检测到分卷压缩包，请切换到分卷解压模式。");
            if (!Detector.IsArchive(node.Format)) throw new StopException("失败", "该文件是普通文件或默认保留的文档、应用容器。");
            if (node.Format.StartsWith("ZIP", StringComparison.Ordinal))
            {
                if (volumes) using (var stream = new VolumeStream(VolumeSet.PathsFor(node))) ZipGuard.Check(stream, node.VolumeKind == "ZIP分卷" ? stream.Starts : null);
                else ZipGuard.Check(node.Source);
            }
            if (Repeated(node)) throw new StopException("受限", "检测到与祖先归档相同的内容，已停止重复展开。");
            engine.CheckFormat(node.Format);
            bool crc = false;
            foreach (string candidate in Candidates(node))
            {
                Checkpoint(node); Budget();
                Set(node, "尝试密码", "正在验证候选密码（第 " + (node.Attempts + 1) + " 次尝试）。");
                EngineResult listed = engine.Run("l", node.Source, candidate, null, State.Limits.TimeoutSeconds, Cancellation.Token, Budget);
                if (listed.Code != 0)
                {
                    attempted[node.Id].Add(candidate); node.Attempts++;
                    if (listed.PasswordError) continue;
                    throw new StopException("失败", "无法读取归档，可能损坏、格式不支持或缺少分卷（引擎代码 " + listed.Code + "）。");
                }
                string attempt = Path.Combine(Path.GetDirectoryName(node.Payload), "尝试-" + Guid.NewGuid().ToString("N"));
                List<Entry> entries = engine.ParseList(listed.Text, attempt);
                // Validate destination paths too before writing any extracted or final file.
                foreach (var entry in entries) Disk.Under(node.Result, entry.Name);
                if (entries.Sum(e => e.Size) > State.Limits.MaxBytes - State.WrittenBytes || entries.Count(e => !e.Directory) > State.Limits.MaxFiles - State.WrittenFiles)
                    throw new StopException("受限", "归档声明的解压大小或文件数超过剩余限制。");
                Disk.NoLinks(attempt); Directory.CreateDirectory(attempt); Action meter = Meter(attempt);
                Set(node, "解压中", "正在解压并校验第 " + node.Depth + " 层。");
                EngineResult extracted;
                try { extracted = engine.Run("x", node.Source, candidate, attempt, State.Limits.TimeoutSeconds, Cancellation.Token, meter); }
                finally { try { meter(); } catch (OperationCanceledException) { } }
                attempted[node.Id].Add(candidate); node.Attempts++;
                if (extracted.Code != 0)
                {
                    // Only completed failed attempts are inventoried. Cancelled/unrecorded remnants stay protected.
                    try
                    {
                        if (node.GarbageFiles == null) node.GarbageFiles = new List<FileRecord>();
                        foreach (string path in Disk.WalkFiles(attempt)) node.GarbageFiles.Add(new FileRecord { Name = Disk.Relative(State.Home, path), Size = new FileInfo(path).Length, Hash = Disk.Hash(path) });
                        Save();
                    }
                    catch { Notify("部分失败尝试未能登记，清理时将保留这些文件。"); }
                    if (extracted.PasswordError || extracted.CrcError) { crc |= extracted.CrcError; continue; }
                    throw new StopException("失败", "解压失败，可能是文件损坏、权限不足或格式不支持（引擎代码 " + extracted.Code + "）。");
                }
                if (volumes) ValidateVolumes(node);
                Set(node, "校验中", "正在核对完整输出清单和文件指纹。");
                var expected = entries.Where(e => !e.Directory).ToDictionary(e => Disk.Under(attempt, e.Name), StringComparer.OrdinalIgnoreCase);
                string[] files = Disk.WalkFiles(attempt).ToArray();
                if (files.Length != expected.Count) throw new StopException("失败", "解压后的文件数量与归档目录不一致。");
                var manifest = new List<FileRecord>();
                foreach (string path in files)
                {
                    Cancellation.Token.ThrowIfCancellationRequested(); Entry entry;
                    if (!expected.TryGetValue(path, out entry) || new FileInfo(path).Length != entry.Size) throw new StopException("失败", "解压后的文件路径或大小不一致。");
                    manifest.Add(new FileRecord { Name = Disk.Relative(attempt, path), Size = entry.Size, Hash = Disk.Hash(path) });
                }
                // A crash after the directory rename may leave an uncommitted payload; preserve it for inspection.
                if (Directory.Exists(node.Payload)) Directory.Move(node.Payload, node.Payload + "-未提交-" + Guid.NewGuid().ToString("N"));
                Directory.Move(attempt, node.Payload); node.Files = manifest;
                node.Directories = Directory.GetDirectories(node.Payload, "*", SearchOption.AllDirectories).Select(p => Disk.Relative(node.Payload, p)).ToList();
                recent = candidate; Set(node, "待扫描", "本层解压成功，准备查找下一层。"); return;
            }
            Set(node, "等待密码", crc ? "所有候选未成功；存在 CRC/数据错误，也可能是文件损坏。可补充密码后继续。" : "现有候选密码均未成功，请补充密码后继续。");
        }
        void CopyFile(ArchiveNode node, FileRecord file)
        {
            if (node.Exported.Contains(file.Name)) return;
            string from = Disk.Under(node.Payload, file.Name), to = Disk.Under(node.Result, file.Name);
            Disk.NoLinks(to); Directory.CreateDirectory(Path.GetDirectoryName(to));
            // A previous interrupted copy is never treated as a completed export.
            string temp = Path.Combine(Path.GetDirectoryName(node.Payload), "导出-" + Guid.NewGuid().ToString("N") + ".partial");
            State.WrittenFiles++; Budget();
            using (var input = File.OpenRead(from))
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[1024 * 1024]; int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                { State.WrittenBytes += read; Budget(); output.Write(buffer, 0, read); }
            }
            if (Disk.Hash(temp) != file.Hash) throw new StopException("失败", "复制后的文件校验不一致。");
            if (File.Exists(to))
            {
                if (Disk.Hash(to) != file.Hash) throw new StopException("文件已变化", "输出目录出现同名且内容不同的文件，请创建新任务。");
                File.Delete(temp);
            }
            else File.Move(temp, to);
            node.Exported.Add(file.Name);
        }
        void Scan(ArchiveNode node)
        {
            Checkpoint(node); Set(node, "待扫描", "正在保留普通文件并识别内层归档。");
            Directory.CreateDirectory(node.Result);
            foreach (string relative in node.Directories)
            {
                string path = Disk.Under(node.Result, relative); Disk.NoLinks(path); Directory.CreateDirectory(path);
            }
            foreach (FileRecord file in node.Files)
            {
                Checkpoint(node); Budget();
                string source = Disk.Under(node.Payload, file.Name);
                if (State.Nodes.Any(n => n.Parent == node.Id && VolumeSet.Contains(n, source))) continue;
                VolumeSet group = VolumeSet.Find(source);
                string kind = group == null ? Detector.Detect(source, false) : group.Kind;
                if (group != null) source = group.Entry;
                if (Detector.IsArchive(kind))
                {
                    if (!State.Nodes.Any(n => n.Parent == node.Id && n.Source == source))
                    {
                        if (State.Nodes.Count >= State.Limits.MaxNodes) throw new StopException("受限", "已达到归档节点数量上限，剩余文件保留在工作区。");
                        lock (Sync) State.Nodes.Add(NewNode(State, source, node.Id, node.Depth + 1, State.Nodes.Max(n => n.Id) + 1, group));
                        Save(); Notify("发现第 " + (node.Depth + 1) + " 层归档。");
                    }
                }
                else CopyFile(node, file);
            }
            Set(node, "完成", "本层完成；已导出 " + node.Exported.Count + " 个普通文件。");
        }
        public void WriteReport()
        {
            StorageManager.WriteIndex(State);
            var sb = new StringBuilder();
            sb.AppendLine("# 自动多层解压工具 · 处理报告").AppendLine();
            sb.AppendLine("状态：" + State.Status).AppendLine();
            sb.AppendLine("模式：" + (State.VolumeMode ? "分卷解压" : "普通解压")).AppendLine();
            foreach (var node in State.Nodes.Where(n => n.Volumes != null && n.Volumes.Count > 0))
            { sb.AppendLine("第 " + node.Depth + " 层分卷：" + node.Volumes.Count + " 卷"); foreach (var volume in node.Volumes) sb.AppendLine("- " + volume.Name); sb.AppendLine(); }
            if (State.IntermediateCleaned) sb.AppendLine(State.CleanupPending ? "中间文件清理未完成，可在结果管理中重试。" : "已清理登记的中间文件；最终结果保留。需要重新解压时请从原始输入新建任务。").AppendLine();
            sb.AppendLine("结果导航：" + Path.Combine(State.Home, "结果导航.html")).AppendLine();
            sb.AppendLine("创建时间：" + State.Created).AppendLine();
            sb.AppendLine("引擎：" + engine.Name + " " + State.EngineVersion).AppendLine();
            sb.AppendLine("输入：" + State.Input).AppendLine();
            sb.AppendLine("累计观测写入：" + State.WrittenBytes + " 字节 / " + State.WrittenFiles + " 个文件（包含中间层、失败尝试和结果复制）。").AppendLine();
            sb.AppendLine("普通文件位于结果目录，各层分别存放；成功的中间归档不复制到结果。失败归档及尝试产物保留在工作区，可通过任务状态继续。").AppendLine();
            foreach (ArchiveNode node in State.Nodes)
            {
                sb.AppendLine("## 节点 " + node.Id + " / 第 " + node.Depth + " 层 / " + node.Status).AppendLine();
                sb.AppendLine("父节点：" + node.Parent + "；格式：" + node.Format).AppendLine();
                sb.AppendLine("源文件：" + node.Source).AppendLine();
                sb.AppendLine("SHA-256：" + node.SourceHash).AppendLine();
                sb.AppendLine("输出：" + node.Result).AppendLine();
                sb.AppendLine(node.Message).AppendLine();
            }
            sb.AppendLine("本版本为本地预览版。资源限制采用轮询，非操作系统硬配额；文本目录接口无法完整证明恶意链接安全，尚未通过生产安全验收。桌面端候选密码使用 Windows 当前用户加密另行保存，不写入任务文件和报告；调用解压引擎时密码对本机有权限读取进程命令行的程序可见。");
            File.WriteAllText(Path.Combine(State.Home, "处理报告.md"), sb.ToString(), new UTF8Encoding(false));
        }
    }
}
