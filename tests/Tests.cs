using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LayerUnpacker;

static class Tests
{
    static string root, bz; static int passed; static List<string> results = new List<string>();
    const string A = "test-key-alpha", B = "test-key-beta";
    static void Assert(bool value, string detail) { if (!value) throw new Exception(detail); }
    static void Test(string name, Action body)
    {
        try { body(); passed++; results.Add("PASS " + name); Console.WriteLine("PASS " + name); }
        catch (Exception e) { results.Add("FAIL " + name + " : " + e.Message); Console.WriteLine("FAIL " + name + " : " + e); }
    }
    static string Dir(string name) { string path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
    static string Text(string dir, string name, string text) { string path = Path.Combine(dir, name); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text, new UTF8Encoding(false)); return path; }
    static string Pack(string dir, string name, string password, params string[] names)
    {
        var args = new List<string> { "c", "-fmt:" + (name.EndsWith(".zip") ? "zip" : "7z"), "-y" };
        if (password != null) args.Add("-p:" + password);
        args.Add(name); args.AddRange(names);
        using (var p = Process.Start(new ProcessStartInfo(bz, string.Join(" ", args.Select(BandizipEngine.Quote))) { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
        { string text = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(); Assert(p.ExitCode == 0, "Fixture creation failed: " + text); }
        return Path.Combine(dir, name);
    }
    static Runner Run(string file, string[] passwords, Limits limits = null)
    {
        var r = new Runner(Runner.Create(file, Dir("outputs"), bz, limits ?? new Limits()), passwords, false); r.Run(); return r;
    }
    static void Success(Runner r) { Assert(r.State.Status == "成功", "Expected success: " + r.State.Status + " / " + string.Join(";", r.State.Nodes.Select(n => n.Message))); }
    static string[] Outputs(Runner r) { string p = Path.Combine(r.State.Home, "结果"); return Directory.Exists(p) ? Disk.WalkFiles(p).ToArray() : new string[0]; }
    static void Zip(string file, params string[] names)
    {
        using (var fs = File.Create(file)) using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        foreach (string name in names) { var entry = zip.CreateEntry(name); using (var w = new StreamWriter(entry.Open())) w.Write("fixture content"); }
    }
    static void Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8; Console.OutputEncoding = new UTF8Encoding(false);
        try { MainImpl(args); }
        catch (Exception e) { Console.WriteLine("TEST ERROR: " + e.Message); Environment.ExitCode = 1; }
    }
    static void MainImpl(string[] args)
    {
        if (args.Length > 0 && (args[0] == "l" || args[0] == "x"))
        {
            if (args[0] == "l") Console.WriteLine("Archive format: Zip/Zipx\n-------------------\n2026-09-07 00:00:00 A___            1            1 sleep.txt\n-------------------\n2026-09-07 00:00:00            1            1 1 files, 0 folders");
            else { string dest = args.First(a => a.StartsWith("-o:")).Substring(3); Directory.CreateDirectory(dest); File.WriteAllText(Path.Combine(dest, "sleep.txt"), "x"); Thread.Sleep(60000); }
            return;
        }
        root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root); bz = BandizipEngine.Find();
        if (args.Length > 1 && args[1] == "--inspect")
        {
            foreach (string file in Disk.WalkFiles(args[2]))
            {
                try { long size = new FileInfo(file).Length; Disk.Hash(file); }
                catch (Exception e) { Console.WriteLine(file + "\n" + e.ToString()); Environment.ExitCode = 1; return; }
            }
            Console.WriteLine("ALL FILES READABLE"); return;
        }
        if (args.Length > 1 && args[1] == "--sample")
        {
            string[] keys = new[] { Console.ReadLine(), Console.ReadLine() };
            var run = new Runner(Runner.Create(args[2], Dir("sample-output"), bz, new Limits()), keys, false);
            run.Changed += Console.WriteLine; run.Diagnostic += delegate(Exception e) { Console.WriteLine(e.ToString()); }; run.Run(); Success(run);
            Assert(run.State.Nodes.Count == 3, "Expected exactly 3 archive layers");
            Assert(Outputs(run).Length == 512, "Expected 510 game files plus 2 sidecar files");
            Console.WriteLine("SAMPLE PASS: " + run.State.Home); return;
        }
        string d = Dir("fixtures");
        Text(d, "内容/中文 空格.txt", "this is the final payload 中文");
        string leaf = Pack(d, "inner.7z", B, "内容"); File.Copy(leaf, Path.Combine(d, "hidden.bin"));
        Text(d, "说明.txt", "sidecar survives"); string middle = Pack(d, "middle.7z", A, "hidden.bin", "说明.txt");
        File.Copy(middle, Path.Combine(d, "pretend.mp4")); string top = Pack(d, "outer.zip", A, "pretend.mp4");
        Runner three = null;
        Test("T01-T03 三层/错误候选/MP4与BIN后缀", delegate
        {
            string before = Disk.Hash(top); three = Run(top, new[] { "incorrect", A, B }); Success(three);
            Assert(three.State.Nodes.Count == 3 && Outputs(three).Length == 2, "Unexpected recursive output");
            Assert(Disk.Hash(top) == before, "Input modified");
            Assert(Outputs(three).Any(p => File.ReadAllText(p).Contains("final payload")), "Missing content");
        });
        Test("T04 无密码归档", delegate { string plain = Pack(d, "plain.zip", null, "说明.txt"); var r = Run(plain, new string[0]); Success(r); });
        Test("T05-T06 多分支与补充密码", delegate
        {
            string both = Pack(d, "both.zip", null, "inner.7z", "middle.7z", "说明.txt"); var r = Run(both, new[] { A });
            Assert(r.State.Status == "等待密码", "Expected waiting"); Assert(Outputs(r).Any(p => Path.GetFileName(p) == "说明.txt"), "Independent files lost");
            int doneAttempts = r.State.Nodes[0].Attempts; r.AddPasswords(new[] { B }); r.Run(); Success(r);
            Assert(r.State.Nodes[0].Attempts == doneAttempts, "Completed root repeated");
            Assert(Outputs(r).Count(p => File.ReadAllText(p).Contains("final payload")) == 2, "Duplicate branch content skipped");
        });
        Test("T07 中文/首尾空格/反斜杠密码", delegate
        {
            string key = " 测试专用密码B2026 x\\ "; string zip = Pack(d, "unicode.7z", key, "内容"); var r = Run(zip, new[] { key.Trim(), key }); Success(r);
        });
        Test("T07 引号密码明确拒绝", delegate { bool rejected = false; try { Run(leaf, new[] { "a\"b" }); } catch (StopException) { rejected = true; } Assert(rejected, "Quote password should be rejected"); });
        Test("T08 截断包不得成功", delegate
        {
            string damaged = Path.Combine(d, "damaged.7z"); byte[] bytes = File.ReadAllBytes(leaf); File.WriteAllBytes(damaged, bytes.Take(bytes.Length / 2).ToArray());
            Assert(Run(damaged, new[] { B }).State.Status != "成功", "Corruption incorrectly accepted");
        });
        Test("T09 同名输入隔离", delegate { var one = Run(leaf, new[] { B }); var two = Run(leaf, new[] { B }); Success(one); Success(two); Assert(one.State.Home != two.State.Home, "Output collision"); });
        Test("T10 Office容器保留", delegate
        {
            string doc = Path.Combine(d, "document.docx"); Zip(doc, "word/document.xml"); string outer = Pack(d, "office.zip", null, "document.docx"); var r = Run(outer, new string[0]); Success(r);
            Assert(r.State.Nodes.Count == 1 && Disk.Hash(Outputs(r).Single()) == Disk.Hash(doc), "Office container unpacked");
        });
        Test("ZIP格式SAV游戏存档完整保留", delegate
        {
            string save = Path.Combine(d, "ManualSave.sav"); Zip(save, "state.json");
            string outer = Pack(d, "save-container.zip", null, "ManualSave.sav");
            var r = Run(outer, new string[0]); Success(r);
            Assert(r.State.Nodes.Count == 1 && Disk.Hash(Outputs(r).Single()) == Disk.Hash(save), "Save container was unpacked");
        });
        Test("T11 路径穿越/绝对路径/ADS/保留名", delegate
        {
            foreach (string name in new[] { "../outside.txt", "C:/outside.txt", "file.txt:stream", "CON", "trailing. ", "folder/../outside.txt" })
            {
                string bad = Path.Combine(d, "evil" + Guid.NewGuid().ToString("N") + ".zip"); Zip(bad, name); var r = Run(bad, new string[0]); Assert(r.State.Status != "成功" && Outputs(r).Length == 0, "Unsafe path accepted: " + name);
            }
            Assert(!File.Exists(Path.Combine(root, "outside.txt")), "Path escaped");
        });
        Test("T11 ZIP符号链接属性", delegate
        {
            string bad = Path.Combine(d, "link.zip"); Zip(bad, "link"); byte[] data = File.ReadAllBytes(bad);
            for (int i = 0; i < data.Length - 46; i++) if (BitConverter.ToUInt32(data, i) == 0x02014b50) { byte[] attr = BitConverter.GetBytes(0xA1FF0000u); Array.Copy(attr, 0, data, i + 38, 4); break; }
            File.WriteAllBytes(bad, data); var r = Run(bad, new string[0]); Assert(r.State.Status == "失败", "Symlink accepted");
        });
        Test("T12 深度/文件数/字节/节点上限", delegate
        {
            Assert(Run(top, new[] { A, B }, new Limits { MaxDepth = 1 }).State.Status != "成功", "Depth cap ignored");
            Assert(Run(middle, new[] { A }, new Limits { MaxFiles = 1 }).State.Status != "成功", "File cap ignored");
            Assert(Run(leaf, new[] { B }, new Limits { MaxBytes = 1 }).State.Status != "成功", "Byte cap ignored");
            Assert(Run(top, new[] { A, B }, new Limits { MaxNodes = 1 }).State.Status != "成功", "Node cap ignored");
        });
        Test("T13 状态恢复/完成节点复用", delegate
        {
            var r = Run(top, new[] { A }); Assert(r.State.Status == "等待密码", "Expected password wait");
            string stateFile = Path.Combine(r.State.Home, "任务状态.json"); var state = Disk.Load(stateFile); int attempts = state.Nodes[0].Attempts;
            var restored = new Runner(state, new[] { A, B }, true); restored.Run(); Success(restored); Assert(state.Nodes[0].Attempts == attempts, "Completed node repeated on restart");
            var finished = new Runner(Disk.Load(stateFile), new[] { A, B }, true); finished.Run(); Success(finished);
        });
        Test("T13 恢复前校验输出变化", delegate
        {
            var r = Run(leaf, new[] { B }); Success(r); File.AppendAllText(Outputs(r).Single(), "changed");
            var restored = new Runner(Disk.Load(Path.Combine(r.State.Home, "任务状态.json")), new[] { B }, true); restored.Run(); Assert(restored.State.Status == "文件已变化", "Tampering missed");
        });
        Test("T14 加密目录与不加密目录均实际验证", delegate
        {
            var r = Run(top, new[] { "wrong" }); Assert(r.State.Status == "等待密码", "Listing counted as success");
            var encrypted = Run(leaf, new[] { "wrong" }); Assert(encrypted.State.Status == "等待密码", "Header encryption ignored");
        });
        Test("T15 分卷明确停止", delegate { string part = Path.Combine(d, "sample.7z.001"); File.Copy(leaf, part); Assert(Run(part, new[] { B }).State.Status == "失败", "Multipart accepted"); });
        Test("T16 状态与报告不记录密码", delegate
        {
            string state = File.ReadAllText(Path.Combine(three.State.Home, "任务状态.json")); string report = File.ReadAllText(Path.Combine(three.State.Home, "处理报告.md"));
            foreach (string key in new[] { A, B, "incorrect" }) Assert(!state.Contains(key) && !report.Contains(key), "Password leaked");
        });
        Test("MP4前缀ZIP尾部识别", delegate
        {
            string plain = Path.Combine(d, "plain.zip"); string prefixed = Path.Combine(d, "video.mp4");
            File.WriteAllBytes(prefixed, Encoding.ASCII.GetBytes("fake MP4 prefix").Concat(File.ReadAllBytes(plain)).ToArray());
            Assert(Detector.Detect(prefixed, false).StartsWith("ZIP"), "ZIP tail missed"); var r = Run(prefixed, new string[0]); Success(r);
        });
        Test("暂停/继续/取消后重试", delegate
        {
            var r = new Runner(Runner.Create(top, Dir("outputs"), bz, new Limits()), new[] { A, B }, false); r.PauseRequested = true;
            var task = Task.Run(new Action(r.Run)); Assert(SpinWait.SpinUntil(() => r.State.Nodes[0].Status == "已暂停", 5000), "Pause missed");
            r.Cancel(); Assert(task.Wait(5000), "Cancel did not stop pause"); Assert(r.State.Status == "已取消", "Cancellation lost");
            r.Run(); Success(r);
        });
        Test("运行中取消/引擎超时", delegate
        {
            string fakeDir = Dir("fake-engine"); string fake = Path.Combine(fakeDir, "bz.exe"); File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location, fake);
            string source = Path.Combine(d, "sleep.zip"); Zip(source, "sleep.txt");
            var cancelled = new Runner(Runner.Create(source, Dir("outputs"), fake, new Limits()), new string[0], false);
            var task = Task.Run(new Action(cancelled.Run));
            Assert(SpinWait.SpinUntil(() => Directory.Exists(Path.Combine(cancelled.State.Home, "工作区")) && Directory.GetFiles(Path.Combine(cancelled.State.Home, "工作区"), "sleep.txt", SearchOption.AllDirectories).Length > 0, 5000), "Fake extraction never started");
            cancelled.Cancel(); Assert(task.Wait(5000), "Extraction cancellation hung"); Assert(cancelled.State.Status == "已取消" && Outputs(cancelled).Length == 0, "Partial output committed");
            var timed = new Runner(Runner.Create(source, Dir("outputs"), fake, new Limits { TimeoutSeconds = 1 }), new string[0], false);
            var watch = Stopwatch.StartNew(); timed.Run(); Assert(watch.Elapsed.TotalSeconds < 8 && timed.State.Nodes[0].Status == "受限", "Timeout not enforced");
        });
        Test("扫描阶段取消后继续", delegate
        {
            var r = new Runner(Runner.Create(leaf, Dir("outputs"), bz, new Limits()), new[] { B }, false); bool once = false;
            r.Changed += delegate(string msg) { if (!once && msg.Contains("正在保留普通文件")) { once = true; r.Cancel(); } };
            r.Run(); Assert(r.State.Status == "已取消", "Scan cancellation missed"); int attempts = r.State.Nodes[0].Attempts;
            r.Run(); Success(r); Assert(r.State.Nodes[0].Attempts == attempts, "Scan restart re-extracted archive");
            Assert(!Outputs(r).Any(p => p.Contains("partial-")), "Partial export leaked");
        });
        Test("清理成功任务保留原件/结果/未知残留并可重新载入", delegate
        {
            var r = Run(top, new[] { A, B }); Success(r);
            string original = Disk.Hash(top), statePath = Path.Combine(r.State.Home, "任务状态.json");
            var hashes = Outputs(r).ToDictionary(p => p, Disk.Hash);
            string unknown = Path.Combine(r.State.Home, "工作区", "用户补充.txt"); File.WriteAllText(unknown, "keep");
            string garbage = Path.Combine(r.State.Home, "工作区", "失败尝试.txt"); File.WriteAllText(garbage, "partial");
            r.State.Nodes[0].GarbageFiles.Add(new FileRecord { Name = Disk.Relative(r.State.Home, garbage), Size = new FileInfo(garbage).Length, Hash = Disk.Hash(garbage) }); Disk.Save(r.State);
            long reclaim = StorageManager.Measure(r.State).Reclaimable;
            var clean = StorageManager.Clean(statePath, new[] { top }, null);
            Assert(clean.IntermediateCleaned && !clean.CleanupPending && clean.ReleasedBytes == reclaim && reclaim > 0, "Cleanup accounting failed");
            Assert(File.Exists(unknown) && !File.Exists(garbage) && Disk.Hash(top) == original && hashes.All(p => Disk.Hash(p.Key) == p.Value), "Protected data changed");
            Assert(StorageManager.Measure(clean).Reclaimable == 0, "Cleanup not idempotent");
            var restored = new Runner(Disk.Load(statePath), new[] { A, B }, true); restored.Run(); Success(restored);
            Assert(File.Exists(Path.Combine(clean.Home, "结果导航.html")), "Index missing");
        });
        Test("未完成任务拒绝清理", delegate
        {
            var r = Run(top, new[] { A }); string path = Path.Combine(r.State.Home, "任务状态.json");
            long before = StorageManager.Measure(r.State).Work; bool rejected = false;
            try { StorageManager.Clean(path, null, null); } catch (IOException) { rejected = true; }
            Assert(rejected && StorageManager.Measure(r.State).Work == before, "Incomplete branch deleted");
        });
        Test("最终结果变化时拒绝清理", delegate
        {
            var r = Run(leaf, new[] { B }); File.AppendAllText(Outputs(r).Single(), "changed"); long before = StorageManager.Measure(r.State).Work; bool rejected = false;
            try { StorageManager.Clean(Path.Combine(r.State.Home, "任务状态.json"), null, null); } catch (IOException) { rejected = true; }
            Assert(rejected && StorageManager.Measure(r.State).Work == before, "Changed result lost backup");
        });
        Test("其他输入/占用锁/越界计划保护", delegate
        {
            var r = Run(leaf, new[] { B }); string path = Path.Combine(r.State.Home, "任务状态.json");
            string payload = Disk.Under(r.State.Nodes[0].Payload, r.State.Nodes[0].Files[0].Name); bool rejected = false;
            try { StorageManager.Clean(path, new[] { payload }, null); } catch (IOException) { rejected = true; }
            Assert(rejected && File.Exists(payload), "Another input deleted"); rejected = false;
            using (StorageManager.Lease(r.State.Home)) { try { StorageManager.Clean(path, null, null); } catch (IOException) { rejected = true; } }
            Assert(rejected, "Concurrent operation allowed");
            r.State.CleanupPending = true; r.State.CleanupPlan.Add(new FileRecord { Name = "结果\\unexpected.txt" }); Disk.Save(r.State); rejected = false;
            try { StorageManager.Clean(path, null, null); } catch (IOException) { rejected = true; }
            Assert(rejected && File.Exists(payload), "Invalid plan accepted");
        });
        Test("中断清理可重试", delegate
        {
            var r = Run(top, new[] { A, B });
            r.State.CleanupPlan = r.State.Nodes.SelectMany(n => n.Files.Select(f => new FileRecord { Name = Disk.Relative(r.State.Home, Disk.Under(n.Payload, f.Name)), Size = f.Size, Hash = f.Hash })).ToList();
            r.State.CleanupPending = true; r.State.IntermediateCleaned = true;
            string target = Disk.Under(r.State.Home, r.State.CleanupPlan[0].Name);
            Assert(target.StartsWith(Path.Combine(r.State.Home, "工作区") + "\\"), "Fixture escaped"); File.Delete(target); Disk.Save(r.State);
            var clean = StorageManager.Clean(Path.Combine(r.State.Home, "任务状态.json"), null, null);
            Assert(!clean.CleanupPending && StorageManager.Measure(clean).Work == 0, "Retry left registered data"); StorageManager.ValidateResults(clean);
        });
        File.WriteAllLines(Path.Combine(root, "测试结果.txt"), results.Concat(new[] { "Passed " + passed + " / " + results.Count }), new UTF8Encoding(false));
        Console.WriteLine("Passed " + passed + " / " + results.Count); Environment.ExitCode = passed == results.Count ? 0 : 1;
    }
}
