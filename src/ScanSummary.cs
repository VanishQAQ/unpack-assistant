using System;
using System.Collections.Generic;
using System.Linq;

namespace LayerUnpacker
{
    public static class ScanSummary
    {
        static List<VirusScanRecord> Records(JobState state, int node)
        { return (state.VirusScans ?? new List<VirusScanRecord>()).Where(r => r.Node == node).ToList(); }
        public static string Suffix(JobState state, int node)
        {
            var all = Records(state, node);
            if (all.Count == 0) return state.VirusScanEnabled ? " · " + Language.T("尚未扫描") : "";
            // Never hide a threat or a bypassed failed scan behind a later clean result.
            if (all.Any(r => r.Status == "发现威胁")) return " · " + Language.T("有威胁");
            var failed = all.LastOrDefault(r => r.Status != "未发现威胁");
            if (failed != null) return " · " + Language.T("扫描未成功") + "：" + Language.T(Reason(failed.Detail));
            bool postScan = all.Any(r => r.Phase == "本层解压后");
            return " · " + Language.T(postScan ? "未发现威胁" : "解压前未发现威胁");
        }
        public static string Reason(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail)) return "结果未知";
            string line = detail.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return line.Length > 100 ? line.Substring(0, 100) + "…" : line;
        }
        public static string Details(JobState state, int node)
        { return string.Concat(Records(state, node).Select(r => "\r\n" + Language.T(r.Phase) + " · " + Language.T(r.Status) + "\r\n" + Language.T(r.Detail))); }
    }
}
