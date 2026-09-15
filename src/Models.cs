using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace LayerUnpacker
{
    public sealed class Limits
    {
        public int MaxDepth = 30;
        public int MaxNodes = 1000;
        public int MaxFiles = 100000;
        public long MaxBytes = 20L * 1024 * 1024 * 1024;
        public long MinFreeBytes = 1024L * 1024 * 1024;
        public int TimeoutSeconds = 1800;
    }
    public sealed class FileRecord
    {
        public string Name;
        public long Size;
        public string Hash;
    }
    public sealed class ArchiveNode
    {
        public int Id;
        public int Parent;
        public int Depth;
        public string Source;
        public string SourceHash;
        public string Format;
        public string Status = "排队";
        public string Message = "等待处理";
        public string Payload;
        public string Result;
        public List<FileRecord> Files = new List<FileRecord>();
        public List<string> Directories = new List<string>();
        public List<string> Exported = new List<string>();
        public int Attempts;
        public List<FileRecord> GarbageFiles = new List<FileRecord>();
        public List<FileRecord> Volumes = new List<FileRecord>();
        public string VolumeKind;
    }
    public sealed class JobState
    {
        public int Schema = 1;
        public string Id;
        public string Home;
        public string Input;
        public string Engine;
        public string EngineVersion;
        public string Created;
        public string Status = "排队";
        public bool VolumeMode;
        public long WrittenBytes;
        public long WrittenFiles;
        public bool IntermediateCleaned;
        public bool CleanupPending;
        public long ReleasedBytes;
        public string CleanedAt;
        public List<FileRecord> CleanupPlan = new List<FileRecord>();
        public Limits Limits = new Limits();
        public List<ArchiveNode> Nodes = new List<ArchiveNode>();
    }
    public sealed class StopException : Exception
    {
        public readonly string State;
        public StopException(string state, string message) : base(message) { State = state; }
    }
    public static class Disk
    {
        public static string Hash(string path)
        {
            using (var file = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "");
        }
        public static string SafeName(string name)
        {
            foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            name = name.Trim().TrimEnd('.');
            if (name.Length > 48) name = name.Substring(0, 48);
            return string.IsNullOrEmpty(name) ? "归档" : name;
        }
        public static string Under(string root, string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
                throw new StopException("失败", "归档含有不安全的路径。");
            string[] parts = relative.Replace('/', '\\').TrimEnd('\\').Split('\\');
            foreach (string part in parts)
            {
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(" ") || part.EndsWith(".") ||
                    part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase))
                    throw new StopException("失败", "归档含有不安全或 Windows 不支持的文件名。");
            }
            string fullRoot = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            string full = Path.GetFullPath(Path.Combine(fullRoot, string.Join("\\", parts)));
            if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new StopException("失败", "归档路径超出输出目录。");
            if (full.Length >= 32000) throw new StopException("受限", "路径过长，请将输出目录改为较短的路径。");
            return full;
        }
        public static void NoLinks(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new StopException("失败", "不支持符号链接、目录联接或重解析点。");
                current = Path.GetDirectoryName(current);
            }
        }
        public static IEnumerable<string> WalkFiles(string root)
        {
            var queue = new Stack<string>(); queue.Push(root);
            while (queue.Count > 0)
            {
                string dir = queue.Pop(); NoLinks(dir);
                foreach (string path in Directory.GetFiles(dir)) { NoLinks(path); yield return path; }
                foreach (string path in Directory.GetDirectories(dir)) { NoLinks(path); queue.Push(path); }
            }
        }
        public static string Relative(string root, string path)
        {
            string prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("路径不属于任务目录。");
            return full.Substring(prefix.Length);
        }
        public static void Save(JobState state)
        {
            NoLinks(state.Home);
            string path = Path.Combine(state.Home, "任务状态.json");
            string json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(state);
            string temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static JobState Load(string path)
        {
            NoLinks(path);
            if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new IOException("任务状态文件过大。");
            var state = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<JobState>(File.ReadAllText(path, Encoding.UTF8));
            if (state == null || state.Schema != 1 || state.Nodes == null || state.Nodes.Count == 0 || state.Limits == null)
                throw new IOException("任务状态格式无效。");
            string home = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.Equals(home, state.Home, StringComparison.OrdinalIgnoreCase)) throw new IOException("任务目录已移动，请从原文件重新创建任务。");
            if (state.Limits.MaxDepth < 1 || state.Limits.MaxNodes < 1 || state.Limits.MaxFiles < 1 || state.Limits.MaxBytes < 1 || state.Limits.TimeoutSeconds < 1 || state.Limits.MinFreeBytes < 0)
                throw new IOException("任务限制无效。");
            var ids = new HashSet<int>();
            foreach (var n in state.Nodes)
            {
                if (n.Id < 1 || !ids.Add(n.Id) || n.Depth < 1 || n.Files == null || n.Exported == null || n.Directories == null) throw new IOException("任务节点无效。");
                string expectedPayload = Path.Combine(home, "工作区", "节点" + n.Id.ToString("D4"), "已验证");
                string expectedResult = Path.Combine(home, "结果", "第" + n.Depth.ToString("D2") + "层_节点" + n.Id.ToString("D4"));
                if (n.Payload != expectedPayload || n.Result != expectedResult) throw new IOException("任务节点路径无效。");
                NoLinks(n.Payload); NoLinks(n.Result);
                if (n.Volumes != null) foreach (var volume in n.Volumes)
                {
                    if (volume.Name != Path.GetFileName(volume.Name)) throw new IOException("分卷记录路径无效。");
                    Under(Path.GetDirectoryName(n.Source), volume.Name);
                }
                foreach (var f in n.Files) Under(n.Payload, f.Name);
                foreach (string d in n.Directories) Under(n.Payload, d);
            }
            foreach (var n in state.Nodes)
            {
                var parent = state.Nodes.FirstOrDefault(x => x.Id == n.Parent);
                if (n.Parent == 0)
                { if (n.Id != 1 || n.Depth != 1 || n.Source != state.Input) throw new IOException("根节点无效。"); }
                else if (parent == null || parent.Id >= n.Id || n.Depth != parent.Depth + 1 || !n.Source.StartsWith(parent.Payload + "\\", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("父子节点关系无效。");
            }
            return state;
        }
    }
    public static class Detector
    {
        static readonly HashSet<string> Excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".docx", ".xlsx", ".pptx", ".docm", ".xlsm", ".pptm", ".odt", ".ods", ".odp", ".apk", ".aab", ".jar", ".epub", ".vsix", ".nupkg", ".appx", ".msix", ".exe", ".dll", ".bundle", ".unity3d", ".sav" };
        public static string Detect(string path, bool root)
        {
            string ext = Path.GetExtension(path);
            if (Excluded.Contains(ext)) return "文档或应用容器";
            if (Regex.IsMatch(path, @"\.(?:z\d{2}|r\d{2}|\d{3})$|\.part\d+\.rar$", RegexOptions.IgnoreCase)) return "分卷";
            using (var fs = File.OpenRead(path))
            {
                byte[] head = new byte[8]; int read = fs.Read(head, 0, head.Length);
                if (read >= 6 && head[0] == 0x37 && head[1] == 0x7a && head[2] == 0xbc && head[3] == 0xaf && head[4] == 0x27 && head[5] == 0x1c) return "7z";
                if (read >= 7 && head[0] == 0x52 && head[1] == 0x61 && head[2] == 0x72 && head[3] == 0x21 && head[4] == 0x1a && head[5] == 0x07) return "RAR";
                if (read >= 4 && head[0] == 0x50 && head[1] == 0x4b && ((head[2] == 3 && head[3] == 4) || (head[2] == 5 && head[3] == 6))) return "ZIP";
                // A video prefix can precede ZIP data. Validate the end-of-central-directory comment length.
                int length = (int)Math.Min(fs.Length, 65557); byte[] tail = new byte[length];
                fs.Position = fs.Length - length; int offset = 0;
                while (offset < length) { int count = fs.Read(tail, offset, length - offset); if (count == 0) break; offset += count; }
                for (int i = length - 22; i >= 0; i--)
                    if (tail[i] == 0x50 && tail[i + 1] == 0x4b && tail[i + 2] == 5 && tail[i + 3] == 6 && i + 22 + BitConverter.ToUInt16(tail, i + 20) == length) return "ZIP（前缀伪装）";
            }
            if (new[] { ".zip", ".7z", ".rar", ".7" }.Contains(ext.ToLowerInvariant())) return "待探测";
            return root ? "待探测" : "普通文件";
        }
        public static bool IsArchive(string kind) { return kind != "普通文件" && kind != "文档或应用容器"; }
    }
}
