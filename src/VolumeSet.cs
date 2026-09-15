using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LayerUnpacker
{
    public sealed class VolumeSet
    {
        public string Entry, Kind;
        public string[] Paths;

        public static VolumeSet Find(string source)
        {
            source = Path.GetFullPath(source);
            string directory = Path.GetDirectoryName(source), name = Path.GetFileName(source);
            string pattern, kind, entry = null;
            int first = 1;
            Match match = Regex.Match(name, @"^(.*\.(7z|zip))\.([0-9]{3,})$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                pattern = "^" + Regex.Escape(match.Groups[1].Value) + @"\.([0-9]{3,})$";
                kind = match.Groups[2].Value.Equals("zip", StringComparison.OrdinalIgnoreCase) ? "ZIP" : "7z";
            }
            else if ((match = Regex.Match(name, @"^(.*)\.part([0-9]+)\.rar$", RegexOptions.IgnoreCase)).Success)
            { pattern = "^" + Regex.Escape(match.Groups[1].Value) + @"\.part([0-9]+)\.rar$"; kind = "RAR"; }
            else if ((match = Regex.Match(name, @"^(.*)\.(zip|z[0-9]{2,})$", RegexOptions.IgnoreCase)).Success)
            {
                string stem = match.Groups[1].Value;
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !Directory.EnumerateFiles(directory).Any(p => Regex.IsMatch(Path.GetFileName(p), "^" + Regex.Escape(stem) + @"\.z[0-9]{2,}$", RegexOptions.IgnoreCase))) return null;
                pattern = "^" + Regex.Escape(stem) + @"\.z([0-9]{2,})$";
                kind = "ZIP分卷"; entry = Path.Combine(directory, stem + ".zip");
            }
            else if ((match = Regex.Match(name, @"^(.*)\.(rar|r[0-9]{2,})$", RegexOptions.IgnoreCase)).Success)
            {
                string stem = match.Groups[1].Value;
                if (name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) && !File.Exists(Path.Combine(directory, stem + ".r00"))) return null;
                pattern = "^" + Regex.Escape(stem) + @"\.r([0-9]{2,})$";
                kind = "RAR"; entry = Path.Combine(directory, stem + ".rar"); first = 0;
            }
            else return null;

            var numbered = new SortedDictionary<int, string>();
            foreach (string path in Directory.EnumerateFiles(directory))
            {
                var part = Regex.Match(Path.GetFileName(path), pattern, RegexOptions.IgnoreCase);
                if (!part.Success) continue;
                int number;
                if (!int.TryParse(part.Groups[1].Value, out number) || number > 10000 || numbered.ContainsKey(number))
                    throw new IOException("分卷编号重复或超出支持范围。");
                numbered.Add(number, path);
            }
            if (numbered.Count == 0) throw new IOException("没有找到同组分卷，请把所有分卷放在同一目录。");
            int expected = first;
            foreach (int number in numbered.Keys)
                if (number != expected++) throw new IOException("分卷不完整：缺少编号 " + (expected - 1) + " 的分卷。");
            var paths = numbered.Values.ToList();
            if (entry != null)
            {
                if (!File.Exists(entry)) throw new IOException("分卷不完整：缺少 " + Path.GetFileName(entry) + "。");
                if (kind == "ZIP分卷") paths.Add(entry); else paths.Insert(0, entry);
            }
            else entry = paths[0];
            foreach (string path in paths) { Disk.NoLinks(path); if (new FileInfo(path).Length == 0) throw new IOException("存在空分卷：" + Path.GetFileName(path)); }
            return new VolumeSet { Entry = entry, Paths = paths.ToArray(), Kind = kind };
        }

        public List<FileRecord> Records()
        { return Paths.Select(p => new FileRecord { Name = Path.GetFileName(p), Size = new FileInfo(p).Length }).ToList(); }

        public static string[] PathsFor(ArchiveNode node)
        { return node.Volumes.Select(v => Disk.Under(Path.GetDirectoryName(node.Source), v.Name)).ToArray(); }

        public static bool Contains(ArchiveNode node, string path)
        { return node.Source == path || (node.Volumes != null && node.Volumes.Count > 0 && PathsFor(node).Contains(path, StringComparer.OrdinalIgnoreCase)); }
        public static string[] Inputs(JobState state)
        {
            var root = state.Nodes[0];
            return root.Volumes != null && root.Volumes.Count > 0 ? PathsFor(root) : new[] { state.Input };
        }
    }

    // A read-only view over volume files. No joined archive is written to disk.
    public sealed class VolumeStream : Stream
    {
        readonly string[] paths;
        public readonly long[] Starts;
        readonly long length;
        long position;
        FileStream current;
        int currentIndex = -1;
        public VolumeStream(string[] files)
        {
            paths = files; Starts = new long[files.Length];
            for (int i = 0; i < files.Length; i++) { Disk.NoLinks(files[i]); Starts[i] = length; length = checked(length + new FileInfo(files[i]).Length); }
        }
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return length; } }
        public override long Position { get { return position; } set { if (value < 0) throw new IOException("负的分卷偏移。"); position = value; } }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (count > 0 && position < length)
            {
                int index = Array.BinarySearch(Starts, position);
                if (index < 0) index = ~index - 1;
                if (index != currentIndex) { if (current != null) current.Dispose(); current = File.OpenRead(paths[index]); currentIndex = index; }
                current.Position = position - Starts[index];
                int read = current.Read(buffer, offset, (int)Math.Min(count, current.Length - current.Position));
                if (read == 0) throw new EndOfStreamException("读取分卷时文件发生变化。");
                position += read; offset += read; count -= read; total += read;
            }
            return total;
        }
        public override long Seek(long offset, SeekOrigin origin) { Position = checked(offset + (origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? position : length)); return position; }
        public override void Flush() { }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        protected override void Dispose(bool disposing) { if (disposing && current != null) current.Dispose(); base.Dispose(disposing); }
    }
}
