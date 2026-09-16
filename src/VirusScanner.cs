using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.ServiceProcess;

namespace LayerUnpacker
{
    public sealed class VirusScanResult
    {
        public string Status, Detail;
        public bool Clean { get { return Status == "未发现威胁"; } }
    }
    public sealed class VirusScanRecord
    {
        public int Node;
        public string Time, Phase, Target, Status, Detail;
        public string Engine;
        public bool Continued;
    }
    public interface IVirusScanner
    {
        VirusScanResult Scan(string path, int timeout, CancellationToken cancellation);
    }
    sealed class VirusScanStopped : Exception { }

    public sealed class DefenderScanner : IVirusScanner
    {
        public static string Find()
        {
            string platform = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows Defender", "Platform");
            try
            {
                if (Directory.Exists(platform))
                    foreach (string dir in Directory.GetDirectories(platform).OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase))
                    { string exe = Path.Combine(dir, "MpCmdRun.exe"); if (File.Exists(exe)) return exe; }
            }
            catch (UnauthorizedAccessException) { }
            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
            return File.Exists(fallback) ? fallback : "";
        }
        public VirusScanResult Scan(string path, int timeout, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                using (var service = new ServiceController("WinDefend"))
                    if (service.Status != ServiceControllerStatus.Running) return Unknown("Defender 服务未运行，可能已被其他杀毒软件接管或停用。");
            }
            catch (InvalidOperationException) { return Unknown("未找到 Microsoft Defender。请在 Windows 安全中心检查杀毒服务。"); }
            catch (System.ComponentModel.Win32Exception) { }
            string exe = Find();
            if (exe.Length == 0) return Unknown("未找到 Microsoft Defender。请在 Windows 安全中心检查杀毒服务。");
            if (!File.Exists(path) && !Directory.Exists(path)) return Unknown("待扫描文件不存在，可能已被移动或隔离。");
            try
            {
                // This documented per-scan mode reports detections before remediation.
                // It changes no Defender settings; real-time protection remains independent.
                var result = BandizipEngine.Execute(exe, new[] { "-Scan", "-ScanType", "3", "-File", Path.GetFullPath(path), "-DisableRemediation" }, timeout, cancellation, null, Encoding.UTF8);
                return Interpret(result.Code, result.Text);
            }
            catch (OperationCanceledException) { throw; }
            catch (StopException) { return Unknown("扫描超时或输出超出限制，本次扫描未完成。系统后台扫描可能仍在结束中。"); }
            catch (Exception e) { return Unknown("无法完成 Defender 扫描（" + e.GetType().Name + "）。请检查 Windows 安全中心、服务状态和权限。"); }
        }
        public static VirusScanResult Interpret(int code, string output)
        {
            string detail = Regex.Replace(output ?? "", @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "").Trim();
            if (detail.Length > 6000) detail = detail.Substring(0, 6000) + "\r\n（输出已截断）";
            if (detail.IndexOf("Product/Feature disabled", StringComparison.OrdinalIgnoreCase) >= 0) return Unknown("Defender 防病毒功能已停用。");
            // Exit code 2 also means scan errors, so it alone never proves malware.
            bool threat = Regex.IsMatch(detail, @"(?im)^\s*(Threat\s*(Name)?|威胁名称|威胁)\s*[:：]\s*\S+");
            if (code == 0 && !threat) return new VirusScanResult { Status = "未发现威胁", Detail = "Microsoft Defender 本次扫描未报告威胁；加密内容须解开后再扫描。" };
            return new VirusScanResult { Status = threat ? "发现威胁" : "扫描未完成", Detail = "Defender 返回代码：" + code + "\r\n" + detail + (threat ? "" : "\r\n结果未知，请检查 Windows 安全中心中的杀毒服务和权限。") };
        }
        static VirusScanResult Unknown(string message) { return new VirusScanResult { Status = "扫描未完成", Detail = message }; }
    }
}
