using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using LayerUnpacker;
class PreferencesUiTests
{
    static object Field(MainForm f, string name) { return typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f); }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            RuntimeSetup.Initialize(); Application.EnableVisualStyles();
            string folder = Path.GetFullPath(args[1]); Directory.CreateDirectory(folder);
            string passwords = Path.Combine(folder, "passwords.dat");
            string[] names = { "depth", "nodes", "gigabytes", "fileCount", "minutes", "freeGb" };
            int[] values = { 47, 2345, 73, 234567, 83, 7 };
            using (var form = new MainForm(true, passwords))
            {
                if (args[0] == "write")
                {
                    int shown = 0;
                    var help = typeof(MainForm).GetMethod("ShowRestoreHelpOnce", BindingFlags.NonPublic | BindingFlags.Instance);
                    Action displayHelp = delegate { shown++; };
                    help.Invoke(form, new object[] { displayHelp });
                    help.Invoke(form, new object[] { displayHelp });
                    Check(shown == 1, "Restore help must appear only once");
                    for (int i = 0; i < names.Length; i++) ((NumericUpDown)Field(form, names[i])).Value = values[i];
                    ((TextBox)Field(form, "enginePath")).Text = @"E:\工具目录\7z.exe";
                    ((CheckBox)Field(form, "show")).Checked = true;
                    Check(((CheckBox)Field(form, "virusScan")).Checked, "Scanner must default on");
                    ((CheckBox)Field(form, "virusScan")).Checked = false;
                    ((ModeSwitch)Field(form, "mode")).VolumeMode = true;
                    string input = Path.Combine(folder, "输入样本.zip"); File.WriteAllText(input, "fixture");
                    typeof(MainForm).GetMethod("AddInputs", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { new[] { input } });
                    // No Close/FormClosing: every edit must have reached disk already.
                    Check(File.Exists(Path.Combine(folder, "preferences.json")), "Edits were not saved immediately");
                }
                else
                {
                    int shown = 0;
                    typeof(MainForm).GetMethod("ShowRestoreHelpOnce", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { (Action)delegate { shown++; } });
                    Check(shown == 0, "Restore help repeated after restart");
                    for (int i = 0; i < names.Length; i++) Check(((NumericUpDown)Field(form, names[i])).Value == values[i], names[i] + " not restored");
                    Check(((TextBox)Field(form, "enginePath")).Text == @"E:\工具目录\7z.exe", "Engine path lost");
                    Check(((CheckBox)Field(form, "show")).Checked, "Reveal preference lost");
                    Check(!((CheckBox)Field(form, "virusScan")).Checked, "Scanner preference lost");
                    Check(((ModeSwitch)Field(form, "mode")).VolumeMode, "Volume mode preference lost");
                    Check(((ListView)Field(form, "files")).Items.Count == 1, "Input queue lost");
                    Check(!(bool)Field(form, "running"), "Queue started automatically");
                    Check(!File.Exists(passwords), "Unexpected password write");
                }
            }
            Console.WriteLine("PASS " + args[0]); return 0;
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
    }
}
