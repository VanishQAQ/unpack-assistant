using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace LayerUnpacker
{
    public sealed class Preferences
    {
        public string Engine;
        public int Depth = 30, Nodes = 1000, Gigabytes = 20, Files = 100000, Minutes = 30, FreeGb = 1;
        public bool ShowPasswords;
        public bool VolumeMode;
        public bool RestoreHelpShown;
        public bool VirusScanEnabled = true;
        public string VirusScanner = "Defender";
        public string Language;
        public string[] Inputs = new string[0];
    }
    public sealed class PreferencesStore
    {
        readonly string file;
        public PreferencesStore(string location = null)
        { file = location ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LayerUnpacker", "preferences.json"); }
        public Preferences Load()
        {
            if (!File.Exists(file)) return new Preferences();
            if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new IOException("设置文件过大。");
            return new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(file, Encoding.UTF8)) ?? new Preferences();
        }
        public void Save(Preferences value)
        {
            string json = new JavaScriptSerializer().Serialize(value);
            AtomicFile.WriteText(file, json);
        }
    }
}
