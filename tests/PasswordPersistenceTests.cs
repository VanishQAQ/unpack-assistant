using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using LayerUnpacker;

static class PasswordPersistenceTests
{
    static readonly string[] Keys = { "sample-password-one", " 爱坤 ", "abc\\def" };
    [STAThread] static void Main(string[] args)
    {
        try
        {
            string path = Path.GetFullPath(args[1]);
            Application.EnableVisualStyles();
            using (var form = new MainForm(true, path))
            {
                var list = (List<string>)typeof(MainForm).GetField("candidates", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                if (args[0] == "write")
                    typeof(MainForm).GetMethod("AddCandidates", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { Keys.Concat(Keys) });
                else if (args[0] == "read")
                {
                    if (!list.SequenceEqual(Keys)) throw new Exception("Restart did not restore exact ordered candidates");
                    string data = Encoding.UTF8.GetString(File.ReadAllBytes(path));
                    if (Keys.Any(data.Contains)) throw new Exception("Plaintext stored");
                    list.RemoveAt(1);
                    typeof(MainForm).GetMethod("SavePasswords", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
                }
                else if (args[0] == "removed")
                {
                    if (!list.SequenceEqual(new[] { Keys[0], Keys[2] })) throw new Exception("Removal did not persist");
                    list.Clear(); typeof(MainForm).GetMethod("SavePasswords", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
                }
                else if (args[0] == "empty")
                { if (list.Count != 0) throw new Exception("Empty list did not persist"); }
            }
            if (args[0] == "empty")
            {
                byte[] damaged = File.ReadAllBytes(path); damaged[damaged.Length / 2] ^= 1; File.WriteAllBytes(path, damaged);
                bool rejected = false;
                try { new PasswordStore(path).Load(); } catch (System.Security.Cryptography.CryptographicException) { rejected = true; }
                if (!rejected) throw new Exception("Damaged ciphertext accepted");
            }
            Console.WriteLine("PASS " + args[0]);
        }
        catch (Exception e) { Console.WriteLine("FAIL " + e.GetType().Name + ": " + e.Message); Environment.ExitCode = 1; }
    }
}
