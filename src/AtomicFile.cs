using System;
using System.IO;
using System.Text;

namespace LayerUnpacker
{
    // Write and flush a sibling temporary file before replacing the previous settings.
    // If writing fails, the existing file stays intact.
    public static class AtomicFile
    {
        public static void WriteText(string path, string text)
        {
            if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == text) return;
            WriteBytes(path, new UTF8Encoding(false).GetBytes(text));
        }

        public static void WriteBytes(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
