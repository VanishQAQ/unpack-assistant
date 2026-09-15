using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace LayerUnpacker
{
    public sealed class StorageUsage
    {
        public long Results, Work, Reclaimable, Input;
        public int ResultFiles;
    }
    public static class StorageManager
    {
        public static FileStream Lease(string home)
        {
            Disk.NoLinks(home);
            return new FileStream(Path.Combine(home, ".task.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        public static string Size(long bytes)
        {
            if (bytes >= 1073741824) return (bytes / 1073741824.0).ToString("N2") + " GiB";
            if (bytes >= 1048576) return (bytes / 1048576.0).ToString("N2") + " MiB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("N1") + " KiB";
            return bytes + " B";
        }
        static List<FileRecord> Candidates(JobState state)
        {
            if (state.CleanupPending) return state.CleanupPlan ?? new List<FileRecord>();
            if (state.IntermediateCleaned) return new List<FileRecord>();
            var records = new List<FileRecord>();
            foreach (var node in state.Nodes)
            {
                records.AddRange(node.Files.Select(f => new FileRecord { Name = Disk.Relative(state.Home, Disk.Under(node.Payload, f.Name)), Hash = f.Hash, Size = f.Size }));
                if (node.GarbageFiles != null) records.AddRange(node.GarbageFiles);
            }
            return records.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        }
        static string Target(JobState state, FileRecord file)
        {
            string path = Disk.Under(state.Home, file.Name);
            string work = Path.GetFullPath(Path.Combine(state.Home, "工作区")) + "\\";
            if (!path.StartsWith(work, StringComparison.OrdinalIgnoreCase) || string.Equals(path, Path.GetFullPath(state.Input), StringComparison.OrdinalIgnoreCase))
                throw new IOException("清理目标不属于可删除的中间文件。");
            Disk.NoLinks(path); return path;
        }
        public static StorageUsage Measure(JobState state)
        {
            var usage = new StorageUsage();
            string result = Path.Combine(state.Home, "结果"), work = Path.Combine(state.Home, "工作区");
            foreach (string input in VolumeSet.Inputs(state)) if (File.Exists(input)) usage.Input += new FileInfo(input).Length;
            if (Directory.Exists(result)) foreach (string f in Disk.WalkFiles(result)) { usage.Results += new FileInfo(f).Length; usage.ResultFiles++; }
            if (Directory.Exists(work)) foreach (string f in Disk.WalkFiles(work)) usage.Work += new FileInfo(f).Length;
            if (state.Status == "成功" && state.Nodes.All(n => n.Status == "完成"))
                foreach (var f in Candidates(state)) { string p = Target(state, f); if (File.Exists(p)) usage.Reclaimable += new FileInfo(p).Length; }
            return usage;
        }
        public static void ValidateResults(JobState state)
        {
            foreach (var node in state.Nodes)
            {
                foreach (string name in node.Exported)
                {
                    var file = node.Files.Single(f => f.Name == name); string path = Disk.Under(node.Result, name); Disk.NoLinks(path);
                    if (!File.Exists(path) || new FileInfo(path).Length != file.Size || Disk.Hash(path) != file.Hash)
                        throw new IOException("最终文件缺失或已修改，已停止清理，请先确认结果。");
                }
                foreach (string dir in node.Directories)
                { string path = Disk.Under(node.Result, dir); Disk.NoLinks(path); if (!Directory.Exists(path)) throw new IOException("最终目录缺失，已停止清理。"); }
                // Every original entry must either have been exported or have a completed child archive.
                foreach (var file in node.Files)
                    if (!node.Exported.Contains(file.Name) && !state.Nodes.Any(child => child.Parent == node.Id && child.Status == "完成" && VolumeSet.Contains(child, Disk.Under(node.Payload, file.Name))))
                        throw new IOException("存在未导出的文件，已停止清理。");
            }
        }
        public static JobState Clean(string statePath, IEnumerable<string> protectedInputs, Action<string> progress)
        {
            var initial = Disk.Load(statePath);
            using (Lease(initial.Home))
            {
                var state = Disk.Load(statePath);
                if (state.Status != "成功" || state.Nodes.Any(n => n.Status != "完成")) throw new IOException("只有全部成功的任务才能清理；未完成分支会保留。");
                if (progress != null) progress("正在校验最终文件……");
                ValidateResults(state);
                var plan = Candidates(state);
                var ownInputs = VolumeSet.Inputs(state);
                var protectedSet = new HashSet<string>((protectedInputs ?? new string[0]).Concat(ownInputs).Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
                foreach (var file in plan)
                {
                    string target = Target(state, file);
                    if (protectedSet.Contains(target)) throw new IOException("中间文件仍被其他已载入任务作为输入使用，已停止清理。");
                    if (File.Exists(target) && (new FileInfo(target).Length != file.Size || Disk.Hash(target) != file.Hash))
                        throw new IOException("中间文件已被修改，已停止清理并保留文件。");
                }
                // Persist the cleanup intent before deleting. An interrupted cleanup can be retried.
                state.CleanupPlan = plan; state.CleanupPending = true; state.IntermediateCleaned = true; Disk.Save(state);
                if (progress != null) progress("校验通过，正在清理已登记的中间文件……");
                try
                {
                    foreach (var file in plan)
                    {
                        string target = Target(state, file); if (!File.Exists(target)) continue;
                        if (protectedSet.Contains(target) || new FileInfo(target).Length != file.Size || Disk.Hash(target) != file.Hash) throw new IOException("清理过程中检测到文件变化，已停止。");
                        File.Delete(target); state.ReleasedBytes += file.Size;
                    }
                    // Non-recursive removal only: unregistered files and folders are never deleted.
                    var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string work = Path.Combine(state.Home, "工作区");
                    foreach (var file in plan)
                    {
                        string dir = Path.GetDirectoryName(Target(state, file));
                        while (dir != null && (dir == work || dir.StartsWith(work + "\\", StringComparison.OrdinalIgnoreCase))) { dirs.Add(dir); dir = Path.GetDirectoryName(dir); }
                    }
                    foreach (string dir in dirs.OrderByDescending(d => d.Length))
                    { Disk.NoLinks(dir); if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir, false); }
                    state.CleanupPending = false; state.CleanupPlan.Clear(); state.CleanedAt = DateTime.Now.ToString("s");
                }
                finally { Disk.Save(state); }
                return state;
            }
        }
        public static string PrimaryResult(JobState state)
        {
            var leaves = state.Nodes.Where(n => n.Status == "完成" && !state.Nodes.Any(c => c.Parent == n.Id)).ToArray();
            string path = leaves.Length == 1 ? leaves[0].Result : Path.Combine(state.Home, "结果");
            if (!Directory.Exists(path)) return Path.Combine(state.Home, "结果");
            while (!Directory.GetFiles(path).Any())
            { var dirs = Directory.GetDirectories(path); if (dirs.Length != 1) break; Disk.NoLinks(dirs[0]); path = dirs[0]; }
            return path;
        }
        public static void WriteIndex(JobState state)
        {
            var sb = new StringBuilder("<!doctype html><meta charset='utf-8'><title>解压结果</title><style>body{font:16px system-ui;max-width:1000px;margin:40px auto;padding:20px;color:#243448}a{color:#2467cf}li{margin:18px 0}small{color:#667}</style>");
            sb.Append("<h1>解压结果</h1><p>").Append(WebUtility.HtmlEncode(Path.GetFileName(state.Input))).Append(" · ").Append(WebUtility.HtmlEncode(state.Status)).Append("</p><ul>");
            foreach (var node in state.Nodes.Where(n => n.Exported.Count > 0 || n.Directories.Count > 0))
            {
                string relative = string.Join("/", Disk.Relative(state.Home, node.Result).Split('\\').Select(Uri.EscapeDataString));
                sb.Append("<li><a href='").Append(relative).Append("/'>第 ").Append(node.Depth).Append(" 层 · ").Append(WebUtility.HtmlEncode(Path.GetFileName(node.Source))).Append("</a> — ").Append(node.Exported.Count).Append(" 个普通文件 <small>").Append(WebUtility.HtmlEncode(node.Status)).Append("</small></li>");
            }
            sb.Append("</ul><p>原文件未改动。各层内容单独存放，不合并或覆盖同名文件。</p>");
            if (state.IntermediateCleaned) sb.Append("<p>中间文件已清理或正在清理。结果和报告保留；需要重新解压时请从原始文件创建任务。</p>");
            File.WriteAllText(Path.Combine(state.Home, "结果导航.html"), sb.ToString(), new UTF8Encoding(false));
        }
    }
}
