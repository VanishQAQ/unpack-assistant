using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace LayerUnpacker
{
    // Personal edition opens its own UI. Process exit is NOT a scan verdict.
    public sealed class HuorongScanner : IVirusScanner
    {
        public static string Find()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\HipsDaemon"))
                {
                    string command = key == null ? "" : Convert.ToString(key.GetValue("ImagePath"));
                    string executable = command.StartsWith("\"") ? command.Split('"')[1] : command.Split(' ')[0];
                    string candidate = Path.Combine(Path.GetDirectoryName(executable) ?? "", "HipsMain.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch (Exception) { }
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                string candidate = Path.Combine(Environment.GetFolderPath(folder), "Huorong", "Sysdiag", "bin", "HipsMain.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return "";
        }

        public VirusScanResult Scan(string path, int timeout, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string exe = Find();
            if (exe.Length == 0) return Unknown("未找到已安装的火绒个人版。请安装后重试，或选择其他扫描器。");
            if (!File.Exists(path) && !Directory.Exists(path)) return Unknown("待扫描文件不存在，可能已移动或被隔离。");
            try
            {
                // Paths cannot contain quotes. Trim the trailing separator to preserve quoting.
                string target = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                using (Process process = Process.Start(new ProcessStartInfo(exe, "-s \"" + target + "\"") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) })) { }
                return new VirusScanResult { Status = "等待人工确认", Detail = "已请求火绒扫描此目标。请切换到火绒，确认目标正确并等待扫描结束，再返回此处。\r\n拆包助手无法自动读取本次扫描结果；如未启动扫描、已取消或结果不明，请选择否。\r\n火绒按自身设置处理威胁，可能隔离文件；拆包助手不会更改火绒设置。" };
            }
            catch (Exception e) { return Unknown("无法启动火绒扫描（" + e.GetType().Name + "）。"); }
        }
        static VirusScanResult Unknown(string detail) { return new VirusScanResult { Status = "扫描未完成", Detail = detail }; }
    }
}
