using System;
using System.IO;
using System.Text;

namespace LayerUnpacker
{
    public sealed class OutputDirectoryStore
    {
        readonly string file;
        public OutputDirectoryStore(string location = null)
        {
            file = location ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LayerUnpacker", "output-directory.txt");
        }
        static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            if (!(value.StartsWith(@"\\", StringComparison.Ordinal) || (value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/')))) return null;
            if (value.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            try { return Path.GetFullPath(value); } catch (ArgumentException) { return null; } catch (NotSupportedException) { return null; }
        }
        public string Load()
        {
            if (!File.Exists(file)) return null;
            if (new FileInfo(file).Length > 128 * 1024) throw new IOException("输出目录配置无效。");
            return Normalize(File.ReadAllText(file, Encoding.UTF8));
        }
        public bool Save(string value)
        {
            string path = Normalize(value); if (path == null) return false;
            AtomicFile.WriteText(file, path);
            return true;
        }
    }
}
