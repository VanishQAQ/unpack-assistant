using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace LayerUnpacker
{
    // Dispatch by executable, retaining the existing saved Engine path format.
    public sealed class ArchiveEngine
    {
        public readonly string PathName;
        readonly string kind;
        public string Name { get { return kind == "bz" ? "Bandizip" : kind == "7z" ? "7-Zip" : "WinRAR (RAR)"; } }

        public ArchiveEngine(string path)
        {
            string file = Path.GetFileName(path ?? "").ToLowerInvariant();
            if (file == "winrar.exe") { path = Path.Combine(Path.GetDirectoryName(path), "Rar.exe"); file = "rar.exe"; }
            if (!File.Exists(path) || !(file == "bz.exe" || file == "7z.exe" || file == "rar.exe"))
                throw new IOException("请选择 bz.exe、7z.exe 或 WinRAR.exe（同目录须有 Rar.exe）。");
            PathName = Path.GetFullPath(path); kind = file == "bz.exe" ? "bz" : file == "7z.exe" ? "7z" : "rar";
        }

        public static string Find()
        {
            string bz = BandizipEngine.Find(); if (bz.Length > 0) return bz;
            foreach (string name in new[] { "7-Zip\\7z.exe", "WinRAR\\WinRAR.exe" })
                foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
                { string path = Path.Combine(root, name); if (File.Exists(path)) return path; }
            foreach (string part in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                foreach (string name in new[] { "7z.exe", "Rar.exe" })
                    try { string path = Path.Combine(part, name); if (File.Exists(path)) return Path.GetFullPath(path); } catch (ArgumentException) { }
            return "";
        }

        public void CheckFormat(string format)
        {
            if (kind == "rar" && !format.StartsWith("RAR", StringComparison.OrdinalIgnoreCase))
                throw new StopException("引擎不支持", "WinRAR 的 Rar.exe 控制台仅支持 RAR。此层是 " + format + "，请在设置→高级选项改选 7-Zip 或 Bandizip，然后恢复任务。");
        }

        public EngineResult Run(string command, string source, string password, string output, int timeout, CancellationToken cancel, Action monitor)
        {
            if (kind == "bz") return new BandizipEngine(PathName).Run(command, source, password, output, timeout, cancel, monitor);
            if (kind == "7z")
            {
                var args = new List<string> { command, "-sccUTF-8", "-y", "-bd" };
                if (command == "l") args.Add("-slt");
                if (!string.IsNullOrEmpty(password)) args.Add("-p" + password);
                if (output != null) { args.Add("-aoa"); args.Add("-o" + output); }
                args.Add("--"); args.Add(Path.GetFullPath(source));
                return BandizipEngine.Execute(PathName, args, timeout, cancel, monitor);
            }
            // RAR lt describes one physical volume. Read all volumes before extraction;
            // continuation records are joined by the strict parser below.
            var group = command == "l" ? VolumeSet.Find(source) : null;
            var sources = group == null ? new[] { source } : group.Paths;
            var text = new StringBuilder(); var watch = Stopwatch.StartNew();
            foreach (string item in sources)
            {
                var args = new List<string> { command == "l" ? "lt" : "x", "-cfg-", "-c-", "-y", "-scfr", string.IsNullOrEmpty(password) ? "-p-" : "-p" + password };
                args.Add("--"); args.Add(Path.GetFullPath(item));
                if (output != null) args.Add(output.TrimEnd('\\') + "\\");
                int remaining = timeout - (int)watch.Elapsed.TotalSeconds;
                if (remaining <= 0) throw new StopException("受限", "本次操作超过时间限制。");
                var result = BandizipEngine.Execute(PathName, args, remaining, cancel, monitor);
                if (result.Code != 0) return result;
                if (text.Length + result.Text.Length > 16 * 1024 * 1024) throw new StopException("受限", "归档目录超过限制。");
                text.AppendLine(result.Text);
            }
            return new EngineResult { Code = 0, Text = text.ToString() };
        }

        public List<Entry> ParseList(string text, string destination)
        {
            if (kind == "bz") return BandizipEngine.ParseList(text, destination);
            var entries = kind == "7z" ? ParseSevenZip(text) : ParseRar(text);
            Validate(entries, destination); return entries;
        }

        static StopException Invalid() { return new StopException("失败", "引擎目录格式无法可靠验证，或包含不支持的链接/特殊条目，已停止解压。"); }
        static long Size(string value)
        {
            long size;
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size < 0) throw Invalid();
            return size;
        }
        static string Required(Dictionary<string, string> record, string key)
        { string value; if (!record.TryGetValue(key, out value)) throw Invalid(); return value; }

        static List<Entry> ParseSevenZip(string text)
        {
            var entries = new List<Entry>(); var record = new Dictionary<string, string>();
            bool inside = false, format = false;
            Action flush = delegate
            {
                if (record.Count == 0) return;
                string name = Required(record, "Path"), folder;
                if (!record.TryGetValue("Folder", out folder)) folder = Required(record, "Attributes").StartsWith("D", StringComparison.Ordinal) ? "+" : "-";
                if (folder != "+" && folder != "-") throw Invalid();
                foreach (var pair in record)
                    if (((pair.Key.IndexOf("Link", StringComparison.OrdinalIgnoreCase) >= 0 || pair.Key == "Alternate Stream") && pair.Value != "" && pair.Value != "-") ||
                        (pair.Key == "Attributes" && System.Text.RegularExpressions.Regex.IsMatch(pair.Value, @"(^|\s)[lbcps][rwx-]{9}"))) throw Invalid();
                entries.Add(new Entry { Name = name, Size = Size(Required(record, "Size")), Directory = folder == "+" }); record.Clear();
            };
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (!inside)
                {
                    if (line == "Type = 7z" || line == "Type = zip" || line == "Type = Rar" || line == "Type = Rar5") format = true;
                    if (line == "----------") { if (!format) throw Invalid(); inside = true; }
                    continue;
                }
                if (line.Length == 0) { flush(); continue; }
                int separator = line.IndexOf(" = ", StringComparison.Ordinal);
                if (separator <= 0 || record.ContainsKey(line.Substring(0, separator))) throw Invalid();
                record.Add(line.Substring(0, separator), line.Substring(separator + 3));
            }
            flush(); if (!inside) throw Invalid(); return entries;
        }

        static List<Entry> ParseRar(string text)
        {
            var entries = new List<Entry>(); var record = new Dictionary<string, string>();
            Entry pending = null; bool archive = false;
            Action flush = delegate
            {
                if (record.Count == 0) return;
                string name = Required(record, "Name"), type = Required(record, "Type");
                if (type != "File" && type != "Directory") throw Invalid();
                foreach (string key in record.Keys) if (key.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Link", StringComparison.OrdinalIgnoreCase) >= 0) throw Invalid();
                var entry = new Entry { Name = name, Directory = type == "Directory", Size = type == "Directory" ? 0 : Size(Required(record, "Size")) };
                string ratio; record.TryGetValue("Ratio", out ratio);
                bool before = ratio == "<--" || ratio == "<->", after = ratio == "-->" || ratio == "<->";
                if (before)
                {
                    if (pending == null || pending.Name != entry.Name || pending.Size != entry.Size || entry.Directory) throw Invalid();
                }
                else { if (pending != null) throw Invalid(); entries.Add(entry); }
                pending = after ? entry : null; record.Clear();
            };
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("Details: RAR ", StringComparison.Ordinal)) { archive = true; continue; }
                if (line.Length == 0) { flush(); continue; }
                if (line.StartsWith("        Name: ", StringComparison.Ordinal))
                { flush(); record.Add("Name", line.Substring(14)); continue; }
                if (record.Count == 0) continue;
                int separator = line.IndexOf(": ", StringComparison.Ordinal);
                if (separator < 0) throw Invalid();
                string key = line.Substring(0, separator).Trim();
                if (record.ContainsKey(key)) throw Invalid(); record.Add(key, line.Substring(separator + 2));
            }
            flush(); if (!archive || pending != null) throw Invalid(); return entries;
        }

        static void Validate(List<Entry> entries, string destination)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                string safe = Disk.Under(destination, entry.Name);
                if (!names.Add(safe)) throw Invalid();
                if (!entry.Directory) files.Add(safe);
            }
            foreach (var entry in entries)
                for (string parent = Path.GetDirectoryName(Disk.Under(destination, entry.Name)); parent != null; parent = Path.GetDirectoryName(parent))
                    if (files.Contains(parent)) throw Invalid();
        }
    }
}
