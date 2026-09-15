using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using LayerUnpacker;

static class VolumeQueueUiTests
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(MainForm form, string name) { return typeof(MainForm).GetField(name,Flags).GetValue(form); }
    static void Check(bool ok, string message) { if(!ok) throw new Exception(message); }
    static void Run(string fixture, string output, string[] inputs, bool oldMode, int count)
    {
        using(var form = new MainForm(false))
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show();
            ((TextBox)Field(form,"output")).Text = output;
            ((ModeSwitch)Field(form,"mode")).VolumeMode = oldMode;
            ((List<string>)Field(form,"candidates")).Add("爱坤");
            typeof(MainForm).GetMethod("AddInputs",Flags).Invoke(form,new object[]{inputs});
            typeof(MainForm).GetMethod("Start",Flags).Invoke(form,new object[]{null,EventArgs.Empty});
            var watch = Stopwatch.StartNew();
            do { Application.DoEvents(); Thread.Sleep(20); if(watch.Elapsed.TotalSeconds > 90) throw new Exception("UI timeout"); } while((bool)Field(form,"running"));
            var jobs = (List<Runner>)Field(form,"jobs");
            Check(jobs.Count == count,"wrong task count: " + jobs.Count + " " + ((TextBox)Field(form,"log")).Text);
            foreach(var job in jobs)
            {
                Check(job.State.Status == "成功", "failed: " + string.Join(";",job.State.Nodes.Select(n=>n.Message)));
                string actual = Disk.WalkFiles(Path.Combine(job.State.Home,"结果")).Single(p=>Path.GetFileName(p)=="payload.bin");
                Check(Disk.Hash(actual)==Disk.Hash(Path.Combine(fixture,"payload.bin")),"output hash differs");
            }
            form.Close();
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            RuntimeSetup.Initialize(); Application.EnableVisualStyles();
            string fixture = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
            var seven = VolumeSet.Find(Path.Combine(fixture,"split.7z.001")).Paths;
            var zip = VolumeSet.Find(Path.Combine(fixture,"split.zip")).Paths;
            Run(fixture,output,seven.Reverse().ToArray(),false,1); Console.WriteLine("PASS 普通模式全选所有7z分卷，只生成一个任务并解压");
            Run(fixture,output,zip.Reverse().ToArray(),false,1); Console.WriteLine("PASS 普通模式全选ZIP分卷，只生成一个任务并解压");
            Run(fixture,output,new[]{seven[1]},false,1); Console.WriteLine("PASS 仅添加中间卷，自动寻找同组文件");
            // plain.zip has numeric split siblings; copy it to an independent name.
            string ordinary = Path.Combine(output,"ordinary.zip"); Directory.CreateDirectory(output); File.Copy(Path.Combine(fixture,"plain.zip"),ordinary,true);
            Run(fixture,output,seven.Concat(zip).Concat(new[]{ordinary}).ToArray(),true,3); Console.WriteLine("PASS 旧分卷模式下混合两组分卷与普通包");
            if(args.Length > 2) { var rar = VolumeSet.Find(Path.GetFullPath(args[2])); Run(Path.GetDirectoryName(rar.Entry),output,rar.Paths.Reverse().ToArray(),false,1); Console.WriteLine("PASS 全选全部RAR分卷，只生成一个任务并解压"); }
            return 0;
        }
        catch(Exception e) { Console.WriteLine(e); return 1; }
    }
}
