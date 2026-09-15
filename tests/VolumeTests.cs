using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using LayerUnpacker;

static class VolumeTests
{
    static string root, engine, fixture;
    static int failed;
    static readonly List<string> results = new List<string>();
    const string Key = "爱坤";
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Test(string title, Action action)
    { try { action(); results.Add("PASS " + title); } catch (Exception ex) { failed++; results.Add("FAIL " + title + ": " + ex.Message); } Console.WriteLine(results.Last()); }
    static void Pack(string name, string format, params string[] files)
    {
        var args = new[] { "c", "-fmt:" + format, "-v:64KB", "-p:" + Key, "-y", name }.Concat(files);
        using (var p = Process.Start(new ProcessStartInfo(engine, string.Join(" ", args.Select(BandizipEngine.Quote))) { WorkingDirectory = fixture, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
        { string output = p.StandardOutput.ReadToEnd(); p.WaitForExit(); Assert(p.ExitCode == 0, "Fixture creation failed: " + output); }
    }
    static Runner Run(string source, string[] keys)
    { var r = new Runner(Runner.Create(source, Path.Combine(root, "outputs"), engine, new Limits(), true), keys, false); r.Run(); return r; }
    static void Success(Runner runner)
    {
        Assert(runner.State.Status == "成功", runner.State.Status + ": " + string.Join(" / ", runner.State.Nodes.Select(n => n.Message)));
        string output = Disk.WalkFiles(Path.Combine(runner.State.Home, "结果")).Single(p => Path.GetFileName(p) == "payload.bin");
        Assert(Disk.Hash(output) == Disk.Hash(Path.Combine(fixture, "payload.bin")), "Output differs");
    }
    static int Main(string[] args)
    {
        RuntimeSetup.Initialize(); Console.OutputEncoding = Encoding.UTF8;
        root = Path.GetFullPath(args[0]); fixture = Path.Combine(root, "fixtures"); Directory.CreateDirectory(fixture); engine = BandizipEngine.Find();
        byte[] data = new byte[210000]; new Random(42).NextBytes(data); File.WriteAllBytes(Path.Combine(fixture, "payload.bin"), data);
        Pack("split.7z", "7z", "payload.bin"); Pack("split.zip", "zip", "payload.bin");
        string seven = Path.Combine(fixture, "split.7z.001"), zip = Path.Combine(fixture, "split.zip");
        Test("7z中文密码/任意卷识别/错误候选", () => { var group = VolumeSet.Find(Path.Combine(fixture, "split.7z.002")); Assert(group.Entry == seven && group.Paths.Length > 2, "Group mismatch"); Success(Run(group.Entry, new[] { "wrong", Key })); });
        Test("ZIP z01分卷/密码/完整清单", () => Success(Run(Path.Combine(fixture, "split.z02"), new[] { "wrong", Key })));
        Test("ZIP数字分卷", () => {
            string plain = Path.Combine(fixture, "plain.zip");
            using (var z = ZipFile.Open(plain, ZipArchiveMode.Create)) z.CreateEntryFromFile(Path.Combine(fixture, "payload.bin"), "payload.bin");
            byte[] bytes = File.ReadAllBytes(plain);
            for (int offset = 0, index = 1; offset < bytes.Length; offset += 65536, index++) File.WriteAllBytes(plain + "." + index.ToString("D3"), bytes.Skip(offset).Take(65536).ToArray());
            Success(Run(plain + ".002", new string[0]));
        });
        Test("缺少中间卷明确拒绝", () => {
            string path = Path.Combine(fixture, "split.7z.002"); byte[] bytes = File.ReadAllBytes(path); File.Delete(path);
            try { bool rejected = false; try { VolumeSet.Find(seven); } catch (IOException e) { rejected = e.Message.Contains("缺少"); } Assert(rejected, "Missing part accepted"); }
            finally { File.WriteAllBytes(path, bytes); }
        });
        Test("缺少末卷不得成功", () => {
            string path = VolumeSet.Find(seven).Paths.Last(); byte[] bytes = File.ReadAllBytes(path); File.Delete(path);
            try { Assert(Run(seven, new[] { Key }).State.Status != "成功", "Truncation accepted"); } finally { File.WriteAllBytes(path, bytes); }
        });
        Test("准备阶段缺卷原因写入报告", () => {
            var state = Runner.Create(seven, Path.Combine(root,"outputs"), engine, new Limits(), true);
            string part = VolumeSet.Find(seven).Paths[1]; byte[] bytes = File.ReadAllBytes(part); File.Delete(part);
            try {
                var run = new Runner(state,new[]{Key},false); run.Run();
                Assert(run.State.Status == "文件已变化", "Missing volume status lost");
                Assert(File.ReadAllText(Path.Combine(state.Home,"处理报告.md")).Contains("分卷不存在"), "Missing reason in report");
            } finally { File.WriteAllBytes(part,bytes); }
        });
        Test("补充密码与恢复任务", () => {
            var r = Run(seven, new[] { "wrong" }); Assert(r.State.Status == "等待密码", "Expected password wait");
            var restored = new Runner(Disk.Load(Path.Combine(r.State.Home, "任务状态.json")), new[] { Key }, true); restored.Run(); Success(restored);
        });
        Test("恢复前验证每一卷", () => {
            var r = Run(seven, new[] { "wrong" }); string path = VolumeSet.Find(seven).Paths[1]; byte[] bytes = File.ReadAllBytes(path), changed = (byte[])bytes.Clone(); changed[20] ^= 1; File.WriteAllBytes(path, changed);
            try { var restored = new Runner(Disk.Load(Path.Combine(r.State.Home, "任务状态.json")), new[] { Key }, true); restored.Run(); Assert(restored.State.Status == "文件已变化", "Changed secondary volume missed"); }
            finally { File.WriteAllBytes(path, bytes); }
        });
        Test("嵌套分卷分组/清理保留全部原卷", () => {
            Pack("nested.7z", "7z", "split.7z.*"); string source = Path.Combine(fixture, "nested.7z.001");
            var originals = VolumeSet.Find(source).Paths.ToDictionary(p => p, Disk.Hash);
            var r = Run(source, new[] { Key }); Success(r); Assert(r.State.Nodes.Count == 2, "Inner volumes created multiple nodes");
            var cleaned = StorageManager.Clean(Path.Combine(r.State.Home, "任务状态.json"), null, null); StorageManager.ValidateResults(cleaned);
            Assert(originals.All(p => File.Exists(p.Key) && Disk.Hash(p.Key) == p.Value), "Original volumes deleted");
        });
        Test("分卷ZIP路径穿越拦截", () => {
            string path = Path.Combine(fixture, "unsafe.zip"); using (var fs = File.Create(path)) using (var z = new ZipArchive(fs, ZipArchiveMode.Create)) using (var w = new StreamWriter(z.CreateEntry("../escaped.txt").Open())) w.Write("bad");
            byte[] bytes = File.ReadAllBytes(path); int half = bytes.Length / 2;
            File.WriteAllBytes(path + ".001", bytes.Take(half).ToArray()); File.WriteAllBytes(path + ".002", bytes.Skip(half).ToArray());
            Assert(Run(path + ".001", new[] { Key }).State.Status != "成功", "Unsafe archive accepted");
        });
        Test("普通ZIP外层自动递归分卷", () => {
            string outer = Path.Combine(fixture,"ordinary-outer.zip");
            using(var stream = File.Create(outer)) using(var zipFile = new ZipArchive(stream,ZipArchiveMode.Create))
                foreach(string part in VolumeSet.Find(seven).Paths) zipFile.CreateEntryFromFile(part,"nested/"+Path.GetFileName(part));
            var r = new Runner(Runner.Create(outer,Path.Combine(root,"outputs"),engine,new Limits(),false),new[]{Key},false);
            r.Run(); Success(r); Assert(r.State.Nodes.Count == 2 && r.State.Nodes[1].Volumes.Count == 4,"Inner volumes not grouped");
        });
        Test("旧普通模式分卷失败记录合并恢复", () => {
            string outer = Path.Combine(fixture,"ordinary-outer.zip");
            var r = new Runner(Runner.Create(outer,Path.Combine(root,"outputs"),engine,new Limits{MaxDepth=1},false),new[]{Key},false); r.Run();
            var child = r.State.Nodes[1]; var parts = VolumeSet.PathsFor(child); r.State.Nodes.Remove(child);
            foreach(string part in parts) {
                int id=r.State.Nodes.Count+1;
                r.State.Nodes.Add(new ArchiveNode { Id=id,Parent=1,Depth=2,Source=part,SourceHash=Disk.Hash(part),Status="失败",Format="分卷",Message="检测到分卷压缩包，请切换到分卷解压模式。",Payload=Path.Combine(r.State.Home,"工作区","节点"+id.ToString("D4"),"已验证"),Result=Path.Combine(r.State.Home,"结果","第02层_节点"+id.ToString("D4")) });
            }
            r.State.Limits.MaxDepth=30; int attempts=r.State.Nodes[0].Attempts; Disk.Save(r.State);
            var restored=new Runner(Disk.Load(Path.Combine(r.State.Home,"任务状态.json")),new[]{Key},true); restored.Run(); Success(restored);
            Assert(restored.State.Nodes.Count==2 && restored.State.Nodes[0].Attempts==attempts,"Outer repeated or duplicate inner nodes");
        });
        Test("RAR新旧命名识别与去重", () => {
            foreach (string name in new[] { "sample.part01.rar", "sample.part02.rar", "old.rar", "old.r00", "old.r01" }) File.WriteAllText(Path.Combine(fixture, name), "naming fixture");
            Assert(VolumeSet.Find(Path.Combine(fixture, "sample.part02.rar")).Paths.Length == 2, "New RAR names missed");
            Assert(VolumeSet.Find(Path.Combine(fixture, "old.r01")).Paths.Length == 3, "Old RAR names missed");
        });
        Test("伪装分卷不能跳过格式校验", () => {
            File.Copy(Path.Combine(fixture, "plain.zip.001"), Path.Combine(fixture, "disguised.7z.001"));
            File.Copy(Path.Combine(fixture, "plain.zip.002"), Path.Combine(fixture, "disguised.7z.002"));
            Assert(Run(Path.Combine(fixture, "disguised.7z.001"), new string[0]).State.Status == "失败", "Disguised ZIP bypassed verification");
        });
        if (args.Length > 1) Test("libarchive真实RAR分卷", () => {
            var r = Run(args[1], new string[0]);
            Assert(r.State.Status == "成功", r.State.Status + ": " + string.Join(" / ", r.State.Nodes.Select(n => n.Message)));
            Assert(Disk.WalkFiles(Path.Combine(r.State.Home, "结果")).Any(), "RAR output missing");
        });
        File.WriteAllLines(Path.Combine(root, "分卷测试结果.txt"), results.Concat(new[] { "Passed " + (results.Count - failed) + " / " + results.Count }), new UTF8Encoding(false));
        return failed == 0 ? 0 : 1;
    }
}
