using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using LayerUnpacker;

static class DefenderTests
{
    sealed class Fake : IVirusScanner
    {
        public readonly List<string> Paths = new List<string>();
        public Func<string,VirusScanResult> Result;
        public VirusScanResult Scan(string path,int timeout,CancellationToken cancellation) { Paths.Add(path); return Result(path); }
    }
    static string root, archive;
    static int failed;
    static VirusScanResult Clean() { return new VirusScanResult{Status="未发现威胁",Detail="模拟无威胁"}; }
    static VirusScanResult Threat() { return new VirusScanResult{Status="发现威胁",Detail="模拟测试威胁，不含病毒文件"}; }
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    static void Test(string name,Action action) { try { action(); Console.WriteLine("PASS "+name); } catch(Exception e) { failed++; Console.WriteLine("FAIL "+name+": "+e.Message); } }
    static Runner New(Fake scanner) { var state=Runner.Create(archive,Path.Combine(root,"out"),BandizipEngine.Find(),new Limits()); state.VirusScanEnabled=true; return new Runner(state,new string[0],false){VirusScanner=scanner}; }
    static int Main(string[] args)
    {
        RuntimeSetup.Initialize(); root=Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        archive=Path.Combine(root,"clean.zip"); using(var stream=File.Create(archive)) using(var zip=new ZipArchive(stream,ZipArchiveMode.Create)) using(var w=new StreamWriter(zip.CreateEntry("clean.txt").Open())) w.Write("Harmless test content");
        Test("返回码2不误判为无威胁",()=>Check(!DefenderScanner.Interpret(2,"Scan failed").Clean,"Error allowed"));
        Test("威胁详情识别",()=>Check(DefenderScanner.Interpret(2,"Threat : Test:Win32/SyntheticFixture").Status=="发现威胁","Threat missed"));
        Test("返回0含威胁详情仍拦截",()=>Check(!DefenderScanner.Interpret(0,"Threat Name : SyntheticFixture").Clean,"Threat with zero allowed"));
        Test("扫描前后均调用并写报告",()=>{ var fake=new Fake{Result=p=>Clean()}; var r=New(fake); r.Run(); Check(r.State.Status=="成功" && fake.Paths.Count==2,"Missing pre/post scan"); Check(File.ReadAllText(Path.Combine(r.State.Home,"处理报告.md")).Contains("Microsoft Defender"),"Missing report"); });
        Test("解压前威胁默认停止且无提取",()=>{var fake=new Fake{Result=p=>Threat()};var r=New(fake);r.Run();Check(r.State.Status=="扫描暂停" && r.State.Nodes[0].Attempts==0,"Extracted blocked source");});
        Test("未知结果默认停止",()=>{var r=New(new Fake{Result=p=>new VirusScanResult{Status="扫描未完成",Detail="模拟不可用"}});r.Run();Check(r.State.Status=="扫描暂停","Unknown allowed");});
        Test("明确选择继续记入报告",()=>{var r=New(new Fake{Result=p=>Threat()});r.ConfirmScanRisk=s=>true;r.Run();Check(r.State.Status=="成功" && r.State.VirusScans.All(s=>s.Continued),"Confirmation lost");});
        Test("解压后拦截不导出，恢复不重复解压",()=>{
            var fake=new Fake{Result=p=>Directory.Exists(p)?Threat():Clean()};var r=New(fake);r.Run();int attempts=r.State.Nodes[0].Attempts;
            Check(r.State.Status=="扫描暂停" && r.State.Nodes[0].Exported.Count==0,"Blocked payload exported");
            var restored=new Runner(Disk.Load(Path.Combine(r.State.Home,"任务状态.json")),new string[0],true){VirusScanner=new Fake{Result=p=>Clean()}};restored.Run();
            Check(restored.State.Status=="成功" && restored.State.Nodes[0].Attempts==attempts,"Payload was re-extracted");
        });
        Test("关闭扫描不调用引擎",()=>{var fake=new Fake{Result=p=>{throw new Exception("Scanner called");}};var r=New(fake);r.State.VirusScanEnabled=false;r.Run();Check(r.State.Status=="成功" && fake.Paths.Count==0,"Disabled scanner called");});
        Test("扫描取消不提取",()=>{var r=New(new Fake{Result=p=>{throw new OperationCanceledException();}});r.Run();Check(r.State.Status=="已取消" && r.State.Nodes[0].Attempts==0,"Cancel failed");});
        Test("整组分卷逐卷扫描再检查产物",()=>{
            string data=Path.Combine(root,"data.bin"); var bytes=new byte[150000]; new Random(8).NextBytes(bytes); File.WriteAllBytes(data,bytes);
            string volume=Path.Combine(root,"volumes.7z"); var packed=BandizipEngine.Execute(BandizipEngine.Find(),new[]{"c","-fmt:7z","-v:64KB","-y",volume,data},30,CancellationToken.None,null);Check(packed.Code==0,"Fixture failed");
            var group=VolumeSet.Find(volume+".001"); var state=Runner.Create(group.Entry,Path.Combine(root,"out"),BandizipEngine.Find(),new Limits(),true);state.VirusScanEnabled=true;
            var fake=new Fake{Result=p=>Clean()};var r=new Runner(state,new string[0],false){VirusScanner=fake};r.Run();
            Check(state.Status=="成功" && group.Paths.All(p=>fake.Paths.Contains(p)) && fake.Paths.Count==group.Paths.Length+1,"Volume scan missed");
        });
        if(args.Length>1 && args[1]=="--live") Test("本机Defender实际调用",()=>{var result=new DefenderScanner().Scan(archive,30,CancellationToken.None);Console.WriteLine("实际结果："+result.Status+" / "+result.Detail);Check(result.Status=="未发现威胁" || result.Status=="扫描未完成","Unexpected live result");});
        Console.WriteLine("Failures: "+failed); return failed==0?0:1;
    }
}
