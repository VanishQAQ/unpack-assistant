using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using LayerUnpacker;
static class DefenderUiTests
{
    [STAThread] static int Main(string[] args)
    {
        try
        {
            RuntimeSetup.Initialize(); Application.EnableVisualStyles(); var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            using(var form=new MainForm(false))
            {
                form.ShowInTaskbar=false;form.Opacity=0;form.Show();Application.DoEvents();
                typeof(MainForm).GetMethod("ShowSettings",flags).Invoke(form,new object[]{null,EventArgs.Empty});
                var settings=form.Controls.OfType<SettingsForm>().Single();var navigation=(ListBox)typeof(SettingsForm).GetField("navigation",flags).GetValue(settings);
                navigation.SelectedIndex=5;Application.DoEvents();
                var toggle=(CheckBox)typeof(MainForm).GetField("virusScan",flags).GetValue(form);
                if(!toggle.Visible || !toggle.Checked || settings.TopLevel) throw new Exception("Scanner page not usable or not enabled by default");
                using(var bitmap=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)){form.DrawToBitmap(bitmap,form.ClientRectangle);bitmap.Save(Path.GetFullPath(args[0]));}
                settings.Close();Application.DoEvents();form.Close();
            }
            Console.WriteLine("PASS embedded scanner settings, default enabled and return");return 0;
        }
        catch(Exception e){Console.WriteLine(e);return 1;}
    }
}
