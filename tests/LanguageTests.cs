using System;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using LayerUnpacker;
static class LanguageTests
{
    static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static T Field<T>(object obj, string name) { return (T)obj.GetType().GetField(name, Flags).GetValue(obj); }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Captions(Control c)
    {
        if (c is Label || c is ButtonBase || c is GroupBox || c is TabPage || (c is TextBox && ((TextBox)c).ReadOnly))
            Check(!Regex.IsMatch(c.Text.Replace("任务状态.json", "task-state.json"), @"[\u4e00-\u9fff]"), "Untranslated caption: " + c.Text);
        foreach (Control child in c.Controls) Captions(child);
    }
    static void Screenshot(Control form, string file)
    { using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(file); } }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            RuntimeSetup.Initialize(); Application.EnableVisualStyles(); Directory.CreateDirectory(args[1]);
            var store = new PreferencesStore(Path.Combine(args[1], "preferences.json"));
            if (args[0] == "first")
            {
                Check(store.Load().Language == null, "Fresh user should choose a language");
                using (var timer = new Timer { Interval = 100 })
                {
                    timer.Tick += delegate { foreach (Form f in Application.OpenForms) { var english = f.Controls.OfType<Button>().FirstOrDefault(b => b.Text == "English"); if (english != null) { timer.Stop(); english.PerformClick(); break; } } };
                    timer.Start(); Check(Language.Choose(store), "First choice cancelled");
                }
                Check(store.Load().Language == "en", "First choice not saved");
                Console.WriteLine("PASS first launch language selection and save"); return 0;
            }
            if (args[0] == "restore")
            {
                Check(Language.Choose(store) && Language.English, "Saved language not restored");
                Console.WriteLine("PASS second process restores language without prompt"); return 0;
            }
            Language.Current = "en";
            Check(Language.T(@"完成 E:\完成\结果.zip") == @"Completed E:\完成\结果.zip", "Path changed by translation");
            var state = new JobState { VirusScanEnabled = true };
            Check(ScanSummary.Suffix(state, 1).Contains("Not yet scanned"), "Missing unscanned state");
            state.VirusScans.Add(new VirusScanRecord { Node = 1, Phase = "解压前", Status = "未发现威胁" });
            Check(ScanSummary.Suffix(state, 1).Contains("before extraction"), "Pre-scan incorrectly implies full scan");
            state.VirusScans.Add(new VirusScanRecord { Node = 1, Phase = "本层解压后", Status = "扫描未完成", Detail = "Defender 防病毒功能已停用。", Continued = true });
            Check(ScanSummary.Suffix(state, 1).Contains("disabled"), "Failure reason missing");
            state.VirusScans.Add(new VirusScanRecord { Node = 1, Phase = "本层解压后", Status = "未发现威胁" });
            Check(ScanSummary.Suffix(state, 1).Contains("unsuccessful"), "A bypassed failure was hidden");
            state.VirusScans.Add(new VirusScanRecord { Node = 1, Status = "发现威胁" });
            Check(ScanSummary.Suffix(state, 1).Contains("Threat detected"), "Threat hidden");
            var clean = new JobState { VirusScanEnabled = true };
            clean.VirusScans.Add(new VirusScanRecord { Node = 1, Phase = "本层解压后", Status = "未发现威胁" });
            Check(ScanSummary.Suffix(clean, 1) == " · No threats found", "Clean label missing");
            Language.Current = "zh-CN";
            using (var form = new MainForm(true, Path.Combine(args[1], "passwords.dat")))
            {
                form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                typeof(MainForm).GetMethod("ShowSettings", Flags).Invoke(form, new object[] { null, EventArgs.Empty });
                var settings = Field<SettingsForm>(form, "settingsPage");
                var navigation = Field<ListBox>(settings, "navigation");
                Check(navigation.Items.Count == 7, "Language settings page missing");
                Field<ComboBox>(form, "languageChoice").SelectedIndex = 1; Application.DoEvents();
                Check(store.Load().Language == "en" && store.Load().VirusScanner == "Defender", "UI choice not saved or old scanner retained");
                for (int i = 0; i < navigation.Items.Count; i++)
                {
                    navigation.SelectedIndex = i; Application.DoEvents(); Captions(settings);
                    if (i >= 4) Screenshot(form, Path.Combine(args[1], "settings-en-" + i + ".png"));
                }
                Check(!Field<Panel>(form, "antivirusHost").Controls.OfType<ComboBox>().Any(), "Scanner selector still present");
                settings.Close(); Application.DoEvents(); Captions(form); Screenshot(form, Path.Combine(args[1], "main-en.png"));
                string source = Path.Combine(args[1], "test-source.txt"); File.WriteAllText(source, "Harmless UI fixture");
                var job = new Runner(Runner.Create(source, Path.Combine(args[1], "output"), ArchiveEngine.Find(), new Limits()), new string[0], false);
                job.State.Status = "成功"; job.State.Nodes[0].Status = "完成"; job.State.VirusScanEnabled = true;
                job.State.VirusScans.Add(new VirusScanRecord { Node = job.State.Nodes[0].Id, Phase = "本层解压后", Status = "未发现威胁", Detail = "Microsoft Defender 本次扫描未报告威胁；加密内容须解开后再扫描。" });
                Field<List<Runner>>(form, "jobs").Add(job);
                typeof(MainForm).GetMethod("RefreshTree", Flags).Invoke(form, null);
                Check(Field<TreeView>(form, "tree").Nodes[0].Nodes[0].Text.Contains("Completed · No threats found"), "Scan result not rendered after completion");
                Screenshot(form, Path.Combine(args[1], "progress-en.png"));
                Field<ComboBox>(form, "languageChoice").SelectedIndex = 0; Application.DoEvents();
                Check(Field<Button>(form, "start").Text == "开始解压", "Chinese not restored");
                Check(store.Load().Language == "zh-CN", "Chinese choice not saved");
                Check(Field<TreeView>(form, "tree").Nodes[0].Nodes[0].Text.Contains("完成 · 未发现威胁"), "Chinese scan summary missing");
                Screenshot(form, Path.Combine(args[1], "progress-zh.png"));
                form.Close();
            }
            Console.WriteLine("PASS live bilingual UI, seven pages, persistence, preserved paths and scan status precedence"); return 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
    }
}
