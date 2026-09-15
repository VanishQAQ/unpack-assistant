using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace LayerUnpacker
{
    public sealed class PasswordStore
    {
        readonly string path;
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LayerUnpacker.Passwords.v1");
        public PasswordStore(string file = null)
        {
            path = file ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LayerUnpacker", "passwords.dat");
        }
        public List<string> Load()
        {
            if (!File.Exists(path)) return new List<string>();
            if (new FileInfo(path).Length > 1024 * 1024) throw new IOException("密码配置文件过大。");
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var values = new JavaScriptSerializer().Deserialize<List<string>>(Encoding.UTF8.GetString(plain));
                if (values == null || values.Any(p => p == null || p.IndexOf('"') >= 0 || p.IndexOf('\0') >= 0)) throw new IOException("密码配置无效。");
                return values.Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            }
            finally { Array.Clear(plain, 0, plain.Length); }
        }
        public void Save(IEnumerable<string> candidates)
        {
            byte[] plain = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(candidates.ToArray()));
            byte[] encrypted;
            try { encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
            finally { Array.Clear(plain, 0, plain.Length); }
            if (encrypted.Length > 1024 * 1024) throw new IOException("密码配置文件过大。");
            AtomicFile.WriteBytes(path, encrypted);
        }
    }
}
