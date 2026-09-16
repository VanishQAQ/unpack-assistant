using System;
using System.IO;
using System.Threading;
using LayerUnpacker;
static class HuorongTests
{
    static int Main(string[] args)
    {
        try
        {
            var store = new PreferencesStore(Path.Combine(args[1], "preferences.json"));
            if (args[0] == "save") store.Save(new Preferences { VirusScanner = "Huorong", VirusScanEnabled = true });
            else if (args[0] == "load")
            {
                if (store.Load().VirusScanner != "Huorong" || !store.Load().VirusScanEnabled) throw new Exception("Preference not restored");
                if (new VirusScanResult { Status = "等待人工确认" }.Clean) throw new Exception("Manual result marked clean");
                if (new HuorongScanner().Scan(Path.Combine(args[1], "missing"), 1, CancellationToken.None).Clean) throw new Exception("Missing path allowed");
            }
            else if (args[0] == "scan")
            {
                string target = Path.Combine(args[1], "无害扫描样本.txt"); File.WriteAllText(target, "Harmless Huorong integration check.");
                var result = new HuorongScanner().Scan(target, 30, CancellationToken.None);
                if (result.Status != "等待人工确认" || result.Clean) throw new Exception(result.Detail);
                Console.WriteLine("SCAN_TARGET " + target);
            }
            Console.WriteLine("PASS " + args[0]); return 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
    }
}
