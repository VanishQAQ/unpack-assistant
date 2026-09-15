using System;
using System.IO;
using LayerUnpacker;

static class ConfigurationTests
{
    static int Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(args[0]);
            string file = Path.Combine(args[0], "atomic.txt");
            AtomicFile.WriteText(file, "original");
            using (var locked = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool failed = false;
                try { AtomicFile.WriteText(file, "replacement"); } catch (IOException) { failed = true; }
                if (!failed || File.ReadAllText(file) != "original") throw new Exception("Failed write damaged existing settings");
            }
            if (Directory.GetFiles(args[0], "*.tmp").Length != 0) throw new Exception("Temporary file leaked");
            AtomicFile.WriteText(file, "replacement");
            if (File.ReadAllText(file) != "replacement") throw new Exception("Replacement failed");
            var store = new OutputDirectoryStore(Path.Combine(args[0], "output.txt"));
            store.Save(Path.GetFullPath(args[0]));
            if (store.Save("relative") || store.Load() != Path.GetFullPath(args[0])) throw new Exception("Invalid directory overwrote preference");
            Console.WriteLine("PASS atomic replacement, failure preservation, temp cleanup and directory validation");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine(ex.Message); return 1; }
    }
}
