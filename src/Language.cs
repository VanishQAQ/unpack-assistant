using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LayerUnpacker
{
    // Translate presentation only. Persisted states, filenames and passwords stay unchanged.
    public static class Language
    {
        public static string Current = "zh-CN";
        public static bool English { get { return Current == "en"; } }
        static readonly Dictionary<string, string> catalog = Load();
        static readonly string pattern = string.Join("|", catalog.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
        static readonly ConditionalWeakTable<Control, Caption> captions = new ConditionalWeakTable<Control, Caption>();
        sealed class Caption { public string Text; public bool Updating; }
        static Dictionary<string, string> Load()
        {
            using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("strings.en.json")))
                return new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
        }
        public static string T(string text)
        {
            if (!English || string.IsNullOrEmpty(text)) return text;
            string translated;
            if (catalog.TryGetValue(text, out translated)) return translated;
            // Protect original filenames/paths in diagnostic text and generated reports.
            return Regex.Replace(text, @"[A-Za-z]:\\[^\r\n<>""|]*|[^\s<>""/\\]+\.(?:json|html|md|exe|zip|7z|rar|mp4)(?:\.\d+)?|" + pattern,
                m => catalog.TryGetValue(m.Value, out translated) ? translated : m.Value);
        }
        public static void Apply(Control root)
        {
            // Editable values are user data, never translation targets.
            bool label = root is Label || root is ButtonBase || root is GroupBox || root is TabPage || root is Form;
            var box = root as TextBox;
            if (label || (box != null && box.ReadOnly))
            {
                Caption value;
                if (!captions.TryGetValue(root, out value))
                {
                    value = new Caption { Text = root.Text }; captions.Add(root, value);
                    root.TextChanged += delegate { if (!value.Updating) { value.Text = root.Text; Render(root, value); } };
                }
                Render(root, value);
            }
            var view = root as ListView;
            if (view != null) foreach (ColumnHeader column in view.Columns)
            {
                if (column.Tag == null) column.Tag = column.Text;
                column.Text = T((string)column.Tag);
            }
            foreach (Control child in root.Controls) Apply(child);
            root.Invalidate();
        }
        static void Render(Control control, Caption value)
        { value.Updating = true; try { control.Text = T(value.Text); } finally { value.Updating = false; } }
        public static bool Choose(PreferencesStore store)
        {
            var saved = store.Load();
            if (saved.Language == "zh-CN" || saved.Language == "en") { Current = saved.Language; return true; }
            using (var dialog = new Form { Text = "选择语言 / Choose language", ClientSize = new Size(410, 160), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
            {
                var label = new Label { Text = "请选择界面语言 / Choose your language", AutoSize = true, Location = new Point(24, 22) };
                var chinese = new Button { Text = "简体中文", Location = new Point(24, 78), Size = new Size(165, 42) };
                var english = new Button { Text = "English", Location = new Point(212, 78), Size = new Size(165, 42) };
                chinese.Click += delegate { Current = "zh-CN"; dialog.DialogResult = DialogResult.OK; };
                english.Click += delegate { Current = "en"; dialog.DialogResult = DialogResult.OK; };
                dialog.Controls.AddRange(new Control[] { label, chinese, english });
                if (dialog.ShowDialog() != DialogResult.OK) return false;
            }
            saved.Language = Current; saved.VirusScanner = "Defender"; store.Save(saved); return true;
        }
    }
}
