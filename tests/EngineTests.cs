using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using LayerUnpacker;

static class EngineTests
{
    static string root, seven, rar; static int passed, failed;
    static void Assert(bool result, string message) { if (!result) throw new Exception(message); }
    static void Test(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + " " + e); }
    }
    static string Pack(string engine, string name, string input, string password, bool volume, bool headers = true)
    {
        Console.WriteLine("CREATE " + name);
        string output = Path.Combine(root, name);
        if (name.EndsWith(".zip") && password != null)
        {
            var zipArgs = new List<string> { "c", "-fmt:zip", "-y", "-p:" + password };
            if (volume) zipArgs.Add("-v:64KB");
            zipArgs.Add(output); zipArgs.Add(input);
            var packed = BandizipEngine.Execute(BandizipEngine.Find(), zipArgs, 60, CancellationToken.None, null);
            Assert(packed.Code == 0, "ZIP fixture creation failed " + packed.Text);
            return output;
        }
        var args = new List<string> { "a" };
        if (engine == rar) { args.Add("-ep1"); args.Add("-scfr"); args.Add("-cfg-"); args.Add("-r"); if (password != null) args.Add((headers ? "-hp" : "-p") + password); }
        else { args.Add("-sccUTF-8"); if (password != null) { args.Add("-p" + password); if (name.EndsWith(".7z")) args.Add("-mhe=on"); } }
        if (volume) args.Add("-v64k"); args.Add(output); args.Add(input);
        var result = BandizipEngine.Execute(engine, args, 60, CancellationToken.None, null);
        Assert(result.Code == 0, "fixture failed " + result.Text);
        Console.WriteLine("CREATED " + name);
        return volume ? (engine == rar ? Path.ChangeExtension(output, ".part1.rar") : output + ".001") : output;
    }
    static Runner Run(string source, string engine, bool volume = false, string[] keys = null)
    {
        var runner = new Runner(Runner.Create(source, Path.Combine(root, "outputs"), engine, new Limits(), volume), keys ?? new[] { "wrong", "爱坤" }, false);
        runner.Run(); return runner;
    }
    static void Success(Runner runner, string hash)
    {
        Assert(runner.State.Status == "成功", runner.State.Status + " / " + string.Join(";", runner.State.Nodes.Select(n => n.Message)));
        Assert(Disk.WalkFiles(Path.Combine(runner.State.Home, "结果")).Any(p => Disk.Hash(p) == hash), "output differs");
    }
    static void Main(string[] args)
    {
        try { MainImpl(args); }
        catch (Exception e) { Console.WriteLine("HARNESS ERROR " + e.GetType().Name + ": " + e.Message); Environment.ExitCode = 1; }
    }
    static void MainImpl(string[] args)
    {
        RuntimeSetup.Initialize(); Console.OutputEncoding = Encoding.UTF8;
        root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        seven = Path.GetFullPath(args[1]); rar = Path.GetFullPath(args[2]);
        string fixture = Path.Combine(root, "中文 空格"); Directory.CreateDirectory(Path.Combine(fixture, "空目录"));
        string file = Path.Combine(fixture, "正文.txt"); File.WriteAllText(file, "中文解压验证", Encoding.UTF8); string hash = Disk.Hash(file);
        string data = Path.Combine(fixture, "数据.bin"); var bytes = new byte[160000]; new Random(77).NextBytes(bytes); File.WriteAllBytes(data, bytes);
        string z = Pack(seven, "中文.7z", fixture, "爱坤", false);
        string zip = Pack(seven, "中文.zip", fixture, "爱坤", false);
        string r = Pack(rar, "中文.rar", fixture, "爱坤", false);
        foreach (string engine in new[] { seven, rar })
        {
            string selected = engine;
            Test(Path.GetFileName(engine) + " 中文 RAR + wrong candidate", () => Success(Run(r, selected), hash));
            Test(Path.GetFileName(engine) + " no correct key", () => Assert(Run(r, selected, false, new[] { "wrong" }).State.Status == "等待密码", "wrong state"));
            Test(Path.GetFileName(engine) + " parser rejects traversal", () => {
                string text = selected == seven ? "Type = 7z\n----------\nPath = ..\\escape\nSize = 1\nAttributes = A\n" : "Details: RAR 5\n\n        Name: ..\\escape\n        Type: File\n        Size: 1\n";
                try { new ArchiveEngine(selected).ParseList(text, Path.Combine(root, "safe")); throw new Exception("unsafe path accepted"); } catch (StopException) { }
            });
        }
        Test("7-Zip encrypted 7z", () => Success(Run(z, seven), hash));
        Test("7-Zip encrypted ZIP", () => Success(Run(zip, seven), hash));
        Test("7-Zip plain ZIP", () => Success(Run(Pack(seven,"plain.zip",fixture,null,false), seven), hash));
        string nested = Pack(seven, "nested.7z", r, "爱坤", false);
        Test("7-Zip nested 7z -> RAR", () => { var run = Run(nested, seven); Success(run,hash); Assert(run.State.Nodes.Count == 2, "missing nested layer"); });
        Test("7-Zip disguised mp4", () => { string fake = Path.Combine(root,"disguised.mp4"); File.Copy(nested,fake); Success(Run(fake,seven),hash); });
        Test("WinRAR nested RAR", () => Success(Run(Pack(rar,"nested.rar",r,"爱坤",false),rar),hash));
        Test("WinRAR selection resolves console", () => Assert(new ArchiveEngine(Path.Combine(Path.GetDirectoryName(rar),"WinRAR.exe")).PathName == rar, "wrong resolved path"));
        Test("WinRAR rejects unsupported layer clearly", () => { var run = Run(z,rar); Assert(run.State.Status == "失败" && run.State.Nodes[0].Message.Contains("仅支持 RAR"),"missing guidance"); });
        Test("WinRAR switch engine and resume", () => {
            string mixed = Pack(rar,"mixed.rar",z,"爱坤",false); var run = Run(mixed,rar);
            Assert(run.State.Status != "成功", "unexpected RAR reader support");
            run.State.Engine = seven; var resumed = new Runner(run.State,new[]{"爱坤"},true); resumed.Run(); Success(resumed,hash);
        });
        string rv = Pack(rar,"volumes.rar",fixture,"爱坤",true);
        Test("WinRAR encrypted multi-volume", () => Success(Run(rv,rar,true),hash));
        Test("7-Zip encrypted RAR volumes", () => Success(Run(rv,seven,true),hash));
        Test("7-Zip encrypted 7z volumes", () => Success(Run(Pack(seven,"volumes.7z",fixture,"爱坤",true),seven,true),hash));
        Test("7-Zip encrypted ZIP volumes", () => Success(Run(Pack(seven,"volumes.zip",fixture,"爱坤",true),seven,true),hash));
        Test("WinRAR plain RAR", () => Success(Run(Pack(rar,"plain.rar",fixture,null,false),rar),hash));
        Test("WinRAR file encryption without header encryption", () => Success(Run(Pack(rar,"body-encrypted.rar",fixture,"爱坤",false,false),rar),hash));
        Test("WinRAR password whitespace and backslash", () => Success(Run(Pack(rar,"spaces.rar",fixture," 爱坤\\ ",false),rar,false,new[]{"wrong"," 爱坤\\ "}),hash));
        Test("7-Zip MP4 prefix followed by ZIP", () => {
            string fake = Path.Combine(root,"prefix.mp4");
            using(var stream = File.Create(fake)) { stream.Write(new byte[64],0,64); var archive = File.ReadAllBytes(zip); stream.Write(archive,0,archive.Length); }
            Success(Run(fake,seven),hash);
        });
        Test("7-Zip links rejected", () => {
            try { new ArchiveEngine(seven).ParseList("Type = Rar5\n----------\nPath = evil\nFolder = -\nSize = 1\nSymbolic Link = outside\n",root); throw new Exception("link accepted"); } catch(StopException){}
        });
        Test("WinRAR incomplete continuation rejected", () => {
            try { new ArchiveEngine(rar).ParseList("Details: RAR 5\n\n        Name: evil\n        Type: File\n        Size: 1\n       Ratio: -->\n",root); throw new Exception("partial accepted"); } catch(StopException){}
        });
        Console.WriteLine("RESULT " + passed + " passed, " + failed + " failed"); Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
