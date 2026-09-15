using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LayerUnpacker;

static class ImeTests
{
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [STAThread] static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        using (var form = new Form { ShowInTaskbar = false, Opacity = 0 })
        using (var box = new ImePasswordBox { Top = 10 })
        using (var other = new TextBox { Top = 50 })
        {
            form.Controls.Add(box); form.Controls.Add(other);
            form.Shown += delegate { form.BeginInvoke(new Action(delegate
            {
                try
                {
                    box.Focus();
                    if (!box.Focused || box.UseSystemPasswordChar || box.ImeMode == ImeMode.Disable) throw new Exception("Focused field still disables IME");
                    box.Text = " 爱坤 ";
                    SendMessage(box.Handle, 0x010D, IntPtr.Zero, IntPtr.Zero);
                    if (!box.Composing) throw new Exception("IME composition not tracked");
                    SendMessage(box.Handle, 0x010E, IntPtr.Zero, IntPtr.Zero);
                    if (box.Composing) throw new Exception("IME composition did not end");
                    other.Focus();
                    if (!box.UseSystemPasswordChar || box.Text != " 爱坤 ") throw new Exception("Blur masking changed password");
                    box.Reveal = true;
                    if (box.UseSystemPasswordChar) throw new Exception("Reveal did not work");
                    box.Reveal = false; box.Focus();
                    if (box.UseSystemPasswordChar || box.ImeMode == ImeMode.Disable) throw new Exception("Refocus disables IME");
                    File.WriteAllText(args[0], "PASS: focused IME enabled; composition tracked; blur masked; Chinese text and spaces preserved; reveal and refocus passed.\r\nPhysical IME candidate selection still requires user confirmation.");
                }
                catch (Exception e) { File.WriteAllText(args[0], "FAIL: " + e); Environment.ExitCode = 1; }
                form.Close();
            })); };
            Application.Run(form);
        }
    }
}
