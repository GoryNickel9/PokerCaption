using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PokerCaption.Config
{
    /// <summary>
    /// Parser/scrivitore INI con ordine delle sezioni e delle chiavi preservato,
    /// compatibile con il formato PokerCaption (Version=82).
    /// All'uscita o su richiesta produce il backup .ini_bak.
    /// </summary>
    public sealed class IniFile
    {
        private readonly string _path;
        // sezione -> lista di coppie chiave/valore (ordine preservato)
        private readonly List<Section> _sections = new List<Section>();

        public sealed class Section
        {
            public string Name;
            public List<KeyValuePair<string, string>> Entries = new List<KeyValuePair<string, string>>();
            public string this[string key]
            {
                get => Entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
                set
                {
                    var i = Entries.FindIndex(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
                    if (i >= 0) Entries[i] = new KeyValuePair<string, string>(Entries[i].Key, value);
                    else Entries.Add(new KeyValuePair<string, string>(key, value));
                }
            }
            public bool TryGet(string key, out string value)
            {
                var e = Entries.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                value = e.Value;
                return e.Key != null;
            }
        }

        public IniFile(string path) { _path = path; }

        public static IniFile Load(string path)
        {
            var f = new IniFile(path);
            if (!File.Exists(path)) return f;
            Section cur = null;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.TrimEnd('\r');
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith(";") || t.StartsWith("#")) continue;
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    cur = f.GetOrAddSection(t.Substring(1, t.Length - 2));
                    continue;
                }
                int eq = t.IndexOf('=');
                if (eq <= 0 || cur == null) continue;
                var k = t.Substring(0, eq).Trim();
                var v = t.Substring(eq + 1);
                cur.Entries.Add(new KeyValuePair<string, string>(k, v)); // preserva duplicati e ordine
            }
            return f;
        }

        public Section GetOrAddSection(string name)
        {
            var s = _sections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (s == null) { s = new Section { Name = name }; _sections.Add(s); }
            return s;
        }

        public Section Find(string name) =>
            _sections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Tutte le sezioni, nell'ordine del file.</summary>
        public IReadOnlyList<Section> Sections => _sections;

        public string Get(string section, string key, string def = null)
        {
            var s = Find(section);
            if (s == null) return def;
            return s.TryGet(key, out var v) && v != null ? v : def;
        }

        public int GetInt(string section, string key, int def = 0) =>
            int.TryParse(Get(section, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : def;

        public double GetDouble(string section, string key, double def = 0) =>
            double.TryParse(Get(section, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;

        public long GetLong(string section, string key, long def = 0) =>
            long.TryParse(Get(section, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : def;

        public bool GetBool(string section, string key, bool def = false)
        {
            var v = Get(section, key);
            if (v == null) return def;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public void Set(string section, string key, string value)
        {
            GetOrAddSection(section)[key] = value;
        }

        public void Set(string section, string key, int value) => Set(section, key, value.ToString(CultureInfo.InvariantCulture));

        public void Set(string section, string key, long value) => Set(section, key, value.ToString(CultureInfo.InvariantCulture));

        /// <summary>Copia tutte le sezioni di un altro INI in questo (sovrascrive).</summary>
        public void MergeFrom(IniFile other)
        {
            if (other == null) return;
            foreach (var s in other.Sections)
                foreach (var kv in s.Entries)
                    Set(s.Name, kv.Key, kv.Value);
        }

        /// <summary>Copia dal profilo SOLO le sezioni assenti in questo INI (seeding del
        /// primo avvio). Le sezioni già presenti non vengono toccate: le modifiche
        /// dell'utente (Active, Lock, pannelli, posizioni) sopravvivono ai riavvii —
        /// col vecchio MergeFrom il profilo riscriveva [HUD]/[GenHUD] a ogni start.</summary>
        public void SeedFrom(IniFile other)
        {
            if (other == null) return;
            foreach (var s in other.Sections)
            {
                if (Find(s.Name) != null) continue;
                var dst = GetOrAddSection(s.Name);
                foreach (var kv in s.Entries)
                    dst.Entries.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));
            }
        }

        internal void SaveChanges(string name, Action<Section> update)
        {
            var section = GetOrAddSection(name);
            var previous = section.Entries.ToArray();
            try
            {
                update(section);
                Save();
            }
            catch
            {
                section.Entries.Clear();
                section.Entries.AddRange(previous);
                throw;
            }
        }

        /// <summary>Salva con backup .ini_bak.</summary>
        public void Save()
        {
            var path = Path.GetFullPath(_path);
            var dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);
            var temp = Path.Combine(dir, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Write(temp);
                if (File.Exists(path)) File.Replace(temp, path, Path.ChangeExtension(path, ".ini_bak"));
                else File.Move(temp, path);
            }
            finally { File.Delete(temp); }
        }

        internal void SaveAtomic(string path, bool overwrite)
        {
            var dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);
            var temp = Path.Combine(dir, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Write(temp);
                File.Move(temp, path, overwrite);
            }
            finally { File.Delete(temp); }
        }

        private void Write(string path)
        {
            var sb = new StringBuilder();
            foreach (var s in _sections)
            {
                sb.Append('[').Append(s.Name).Append("]\r\n");
                foreach (var e in s.Entries)
                    sb.Append(e.Key).Append('=').Append(e.Value).Append("\r\n");
                sb.Append("\r\n");
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
    }
}
