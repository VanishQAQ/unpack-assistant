using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using LayerUnpacker;

static class EngineUiTests
{
    [STAThread] static int Main(string[] args)
    {
        try
        {
            RuntimeSetup.Initialize(); Application.EnableVisualStyles();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            using (var form = new MainForm(false))
            {
                form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                typeof(MainForm).GetMethod("ShowSettings", flags).Invoke(form, new object[] { null, EventArgs.Empty });
                Application.DoEvents();
                var settings = form.Controls.OfType<SettingsForm>().Single();
                ((ListBox)typeof(SettingsForm).GetField("navigation", flags).GetValue(settings)).SelectedIndex = 4;
                Application.DoEvents();
                if (settings.TopLevel || !settings.Visible) throw new Exception("settings must stay in main window");
                var path = (TextBox)typeof(MainForm).GetField("enginePath",flags).GetValue(form);
                path.Text = @"C:\Program Files\7-Zip\7z.exe";
                if (!path.Visible || path.Width < 250) throw new Exception("engine selector not usable");
                using (var bitmap = new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,form.ClientRectangle); bitmap.Save(Path.GetFullPath(args[0])); }
                settings.Close(); Application.DoEvents();
                if (!path.Text.EndsWith("7z.exe")) throw new Exception("return discarded selection");
                form.Close();
            }
            Console.WriteLine("PASS embedded engine settings, navigation and render"); return 0;
        }
        catch(Exception e) { Console.WriteLine(e); return 1; }
    }
}
