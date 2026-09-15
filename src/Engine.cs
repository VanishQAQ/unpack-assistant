using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace LayerUnpacker
{
    public sealed class EngineResult
    {
        public int Code;
        public string Text;
        public bool PasswordError { get { return Code == 11 || Regex.IsMatch(Text, "incorrect password|invalid password|wrong password|password is (?:incorrect|invalid)|password required|enter.*password|密码", RegexOptions.IgnoreCase); } }
        public bool CrcError { get { return Regex.IsMatch(Text, "CRC|checksum|data error|file is broken|decompression failed|invalid compressed", RegexOptions.IgnoreCase); } }
    }
    public sealed class Entry
    {
        public string Name;
        public long Size;
        public bool Directory;
    }
    // A job object closes every attached child process if the app terminates.
    sealed class ChildJob : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct BASIC
        { public long A, B; public uint Flags; public UIntPtr Min, Max; public uint Processes; public UIntPtr Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] struct IO { public ulong A, B, C, D, E, F; }
        [StructLayout(LayoutKind.Sequential)] struct EXT { public BASIC Basic; public IO Io; public UIntPtr Pmem, Jmem, PeakP, PeakJ; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attr, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int kind, IntPtr data, uint length);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        IntPtr handle;
        public ChildJob(Process process)
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            var limits = new EXT(); limits.Basic.Flags = 0x2000;
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(EXT)));
            try
            {
                Marshal.StructureToPtr(limits, ptr, false);
                if (handle == IntPtr.Zero || !SetInformationJobObject(handle, 9, ptr, (uint)Marshal.SizeOf(typeof(EXT))) || !AssignProcessToJobObject(handle, process.Handle))
                { Dispose(); throw new IOException("无法建立解压子进程控制，请关闭其他沙箱后重试。"); }
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    }
    public sealed class BandizipEngine
    {
        public readonly string PathName;
        public BandizipEngine(string path)
        {
            if (!File.Exists(path) || !string.Equals(Path.GetFileName(path), "bz.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("请选择 Bandizip 安装目录中的 bz.exe。");
            PathName = Path.GetFullPath(path);
        }
        public static string Find()
        {
            foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
            {
                string path = Path.Combine(root, "Bandizip", "bz.exe"); if (File.Exists(path)) return path;
            }
            foreach (string part in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            { try { string p = Path.Combine(part, "bz.exe"); if (File.Exists(p)) return p; } catch { } }
            return "";
        }
        public static string Quote(string value)
        {
            // Windows CommandLineToArgvW / CRT quoting; never interpreted by a shell.
            var sb = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { sb.Append('\\', slashes * 2 + 1); sb.Append(c); slashes = 0; continue; }
                sb.Append('\\', slashes); slashes = 0; sb.Append(c);
            }
            sb.Append('\\', slashes * 2); sb.Append('"'); return sb.ToString();
        }
        public EngineResult Run(string command, string source, string password, string output, int timeout, CancellationToken cancel, Action monitor)
        {
            var args = new List<string> { command, "-consolemode:utf8", "-y", "-p:" + (password ?? "") };
            if (output != null) { args.Add("-r"); args.Add("-o:" + output); }
            args.Add(System.IO.Path.GetFullPath(source));
            return Execute(PathName, args, timeout, cancel, monitor);
        }
        internal static EngineResult Execute(string path, IEnumerable<string> args, int timeout, CancellationToken cancel, Action monitor)
        {
            var start = new ProcessStartInfo(path, string.Join(" ", args.Select(Quote)))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = System.IO.Path.GetDirectoryName(path) };
            using (var process = new Process { StartInfo = start })
            {
                var text = new StringBuilder(); object gate = new object(); bool tooMuch = false;
                DataReceivedEventHandler handler = delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (gate) { if (text.Length + e.Data.Length > 16 * 1024 * 1024) tooMuch = true; else text.AppendLine(e.Data); }
                };
                process.OutputDataReceived += handler; process.ErrorDataReceived += handler;
                cancel.ThrowIfCancellationRequested(); process.Start();
                try
                {
                    using (var job = new ChildJob(process))
                    {
                        process.StandardInput.Close(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                        var watch = Stopwatch.StartNew();
                        while (!process.WaitForExit(200))
                        {
                            cancel.ThrowIfCancellationRequested();
                            if (watch.Elapsed.TotalSeconds > timeout) throw new StopException("受限", "本次操作超过时间限制。");
                            lock (gate) if (tooMuch) throw new StopException("受限", "归档目录或引擎输出超过限制。");
                            if (monitor != null) monitor();
                        }
                        process.WaitForExit(); cancel.ThrowIfCancellationRequested();
                        if (monitor != null) monitor();
                        lock (gate)
                        {
                            if (tooMuch) throw new StopException("受限", "归档目录或引擎输出超过限制。");
                            return new EngineResult { Code = process.ExitCode, Text = text.ToString() };
                        }
                    }
                }
                finally { try { if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); } } catch { } }
            }
        }
        public static List<Entry> ParseList(string text, string destination)
        {
            var entries = new List<Entry>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool inside = false, ended = false; int advertised = -1; string kind = "";
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("Archive format: ")) kind = line.Substring(16).Trim();
                if (line.StartsWith("-------------------")) { if (inside) { inside = false; ended = true; } else if (!ended) inside = true; continue; }
                if (!inside)
                {
                    if (ended && advertised < 0)
                    {
                        Match summary = Regex.Match(line, @"\s(\d+) files?, (\d+) folders?\s*$");
                        if (summary.Success) advertised = checked(int.Parse(summary.Groups[1].Value) + int.Parse(summary.Groups[2].Value));
                    }
                    continue;
                }
                Match m = Regex.Match(line, @"^\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\s+([A-Z_]{4})\s+(\d+)\s+\d+ (.+)$");
                if (!m.Success) throw new StopException("失败", "无法可靠读取归档目录，已停止解压。");
                string name = m.Groups[3].Value; string safe = Disk.Under(destination, name);
                if (!names.Add(safe)) throw new StopException("失败", "归档含有重复或大小写冲突的路径。");
                entries.Add(new Entry { Name = name, Size = long.Parse(m.Groups[2].Value), Directory = m.Groups[1].Value[0] == 'D' });
            }
            if (!ended || advertised != entries.Count) throw new StopException("失败", "归档条目数量无法验证，已停止解压。");
            if (!(kind.StartsWith("Zip", StringComparison.OrdinalIgnoreCase) || kind == "7z" || kind == "7z(split)" || kind.StartsWith("RAR", StringComparison.OrdinalIgnoreCase)))
                throw new StopException("失败", "首版仅支持 ZIP、7z 和 RAR 归档。");
            var filePaths = new HashSet<string>(entries.Where(e => !e.Directory).Select(e => e.Name.Replace('/', '\\').TrimEnd('\\')), StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                string name = entry.Name.Replace('/', '\\').TrimEnd('\\');
                for (int i = name.IndexOf('\\'); i >= 0; i = name.IndexOf('\\', i + 1))
                    if (filePaths.Contains(name.Substring(0, i))) throw new StopException("失败", "归档文件与目录路径发生冲突。");
            }
            return entries;
        }
    }
}
