using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using LayerUnpacker;

static class ModeUiTests
{
    [STAThread] static int Main(string[] args)
    {
        try
        {
            RuntimeSetup.Initialize(); Application.EnableVisualStyles();
            string fixture = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])), "tests", ".generated-ui", "queue-fixture");
            Directory.CreateDirectory(fixture);
            string settings = Path.Combine(fixture, "passwords.dat");
            using (var form = new MainForm(true, settings))
            {
                form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var mode = (ModeSwitch)typeof(MainForm).GetField("mode", flags).GetValue(form);
                typeof(ModeSwitch).GetMethod("OnMouseDown", flags).Invoke(mode, new object[] { new MouseEventArgs(MouseButtons.Left, 1, mode.Width - 20, 20, 0) });
                typeof(ModeSwitch).GetMethod("OnMouseUp", flags).Invoke(mode, new object[] { new MouseEventArgs(MouseButtons.Left, 1, mode.Width - 20, 20, 0) });
                var watch = Stopwatch.StartNew(); bool intermediate = false;
                while (watch.ElapsedMilliseconds < 350)
                {
                    Application.DoEvents(); Thread.Sleep(5);
                    float position = (float)typeof(ModeSwitch).GetField("position", flags).GetValue(mode);
                    intermediate |= position > 0 && position < 1;
                }
                if (!mode.VolumeMode || !intermediate || (float)typeof(ModeSwitch).GetField("position", flags).GetValue(mode) != 1) throw new Exception("Animated switch failed");
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(args[0]); }
                typeof(MainForm).GetMethod("SetBusy", flags).Invoke(form, new object[] { true });
                if (mode.Enabled) throw new Exception("Mode can change during extraction");
                typeof(MainForm).GetMethod("SetBusy", flags).Invoke(form, new object[] { false });
                typeof(ModeSwitch).GetMethod("OnKeyDown", flags).Invoke(mode, new object[] { new KeyEventArgs(Keys.Left) });
                if (mode.VolumeMode) throw new Exception("Keyboard selection failed");
                string[] paths = new string[3];
                for (int i = 0; i < paths.Length; i++) { paths[i] = Path.Combine(fixture, i + ".zip"); File.WriteAllText(paths[i], "queue fixture"); }
                typeof(MainForm).GetMethod("AddInputs", flags).Invoke(form, new object[] { paths });
                var list = (ListView)typeof(MainForm).GetField("files", flags).GetValue(form);
                list.Items[0].Selected = true; list.Items[1].Selected = true;
                ((Button)typeof(MainForm).GetField("remove", flags).GetValue(form)).PerformClick();
                if (list.Items.Count != 1 || new PreferencesStore(Path.Combine(fixture, "preferences.json")).Load().Inputs.Length != 1) throw new Exception("Multi-remove was not saved");
                ((Button)typeof(MainForm).GetField("clear", flags).GetValue(form)).PerformClick();
                if (list.Items.Count != 0 || new PreferencesStore(Path.Combine(fixture, "preferences.json")).Load().Inputs.Length != 0) throw new Exception("Clear was not saved");
                foreach (string path in paths) if (!File.Exists(path)) throw new Exception("Queue removal deleted original file");
                form.Close();
            }
            Console.WriteLine("PASS animation, mode lock, multiple removal, clear, saved queue and original file preservation"); return 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
    }
}
