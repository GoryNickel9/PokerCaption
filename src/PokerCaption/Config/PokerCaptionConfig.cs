using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PokerCaption.Config
{
    /// <summary>
    /// Configurazione principale (sezione [PokerCaption] — schema Version=82).
    /// Accesso tipizzato alla configurazione e ai valori predefiniti.
    /// Le chiavi non elencate restano accessibili in forma grezza via Raw.
    /// </summary>
    public sealed class PokerCaptionConfig
    {
        public const int SchemaVersion = 82;
        public const string Section = "PokerCaption";

        private readonly IniFile _ini;
        public IniFile Raw => _ini;

        public PokerCaptionConfig(IniFile ini) { _ini = ini; }

        public static PokerCaptionConfig Load(string iniPath)
        {
            var f = IniFile.Load(iniPath);
            RemoveRetiredOptions(f);
            return new PokerCaptionConfig(f);
        }

        internal static void RemoveRetiredOptions(IniFile ini) =>
            ini.Find(Section)?.Entries.RemoveAll(entry =>
                entry.Key.Equals("StartMinimized", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("AutoTimebank", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("AutoHideChat", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("BetscriptOn", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("CloseCantFound", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("CNFafter", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("CurSessInfo", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("KeepPlaying", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("CloseTableHasConcluded", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("CloseTournamentIsOver", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("CloseTrnyLobby", StringComparison.OrdinalIgnoreCase));

        internal static void MigrateHudFormats(IniFile ini)
        {
            foreach (string section in new[] { "HUD", "GenHUD" })
            {
                var hud = ini.Find(section);
                if (hud == null) continue;
                for (int i = 0; i < hud.Entries.Count; i++)
                {
                    var entry = hud.Entries[i];
                    if (!Regex.IsMatch(entry.Key, @"^(?:pl)?FormatStr\d+$", RegexOptions.IgnoreCase)) continue;
                    string format = entry.Value.Replace("[timing]", "", StringComparison.OrdinalIgnoreCase);
                    if (section == "HUD" && !entry.Key.StartsWith("pl", StringComparison.OrdinalIgnoreCase))
                        format = Regex.Replace(format, @"\[(totalPot_bb|totalPot|toCall|seatStacks_bb|:|BB|SB|capt)\]",
                            match => match.Groups[1].Value.ToLowerInvariant() switch
                            {
                                "totalpot_bb" => "[pot_bb]",
                                "totalpot" => "[pot]",
                                _ => ""
                            }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    hud.Entries[i] = new KeyValuePair<string, string>(entry.Key, format);
                }
            }
        }

        public static PokerCaptionConfig CreateDefault(string iniPath)
        {
            var f = new IniFile(iniPath);
            var s = f.GetOrAddSection(Section);
            s["Version"] = SchemaVersion.ToString(CultureInfo.InvariantCulture);
            var cfg = new PokerCaptionConfig(f);
            cfg.SetDefaults();
            return cfg;
        }

        private void SetDefaults()
        {
            // Valori predefiniti dell'applicazione.
            D("TabIdx", 13); D("SngTabIdx", 0);
            D("MinimizeToTray", 1);
            D("BetscriptLog", 0); D("StartSpinDelay", 1000);
            D("TableManagerOn", 0); D("TableManagerConfig", "4x");
            D("MouseWheelUp", 0.5); D("MouseWheelDown", 0.5);
            D("Money2bb", 0); D("Money2bbTrny", 0);
            D("ScanDelay", 2);
            void D(string k, object v) => _ini.Set(Section, k, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        // ---- accesso tipizzato alle chiavi principali ----
        public int TabIdx { get => GI("TabIdx", 13); set => SI("TabIdx", value); }
        public bool MinimizeToTray { get => GB("MinimizeToTray", true); set => SB("MinimizeToTray", value); }
        public bool BetscriptLog { get => GB("BetscriptLog", false); set => SB("BetscriptLog", value); }
        public bool CaptionAutoClose { get => GB("CaptionAutoClose", false); }
        public int StartSpinDelay { get => GI("StartSpinDelay", 1000); set => SI("StartSpinDelay", value); }
        public string TableManagerConfigName { get => GS("TableManagerConfig", "4x"); set => SS("TableManagerConfig", value); }
        public int ScanDelay { get => GI("ScanDelay", 2); set => SI("ScanDelay", value); }
        public double MouseWheelUp { get => GD("MouseWheelUp", 0.5); set => SD("MouseWheelUp", value); }
        public double MouseWheelDown { get => GD("MouseWheelDown", 0.5); set => SD("MouseWheelDown", value); }
        public bool Money2bb { get => GB("Money2bb", false); set => SB("Money2bb", value); }
        public bool Money2bbTrny { get => GB("Money2bbTrny", false); set => SB("Money2bbTrny", value); }

        private string GS(string k, string d) => _ini.Get(Section, k, d);
        private int GI(string k, int d) => _ini.GetInt(Section, k, d);
        private double GD(string k, double d) => _ini.GetDouble(Section, k, d);
        private bool GB(string k, bool d) => _ini.GetBool(Section, k, d);
        private void SS(string k, string v) { _ini.Set(Section, k, v); }
        private void SI(string k, int v) { _ini.Set(Section, k, v); }
        private void SD(string k, double v) { _ini.Set(Section, k, v.ToString("R", CultureInfo.InvariantCulture)); }
        private void SB(string k, bool v) { _ini.Set(Section, k, v ? "1" : "0"); }

        public void Save() => _ini.Save();
    }

    /// <summary>
    /// Caricatore dei config del Table manager (TableManagerConfigs\&lt;name&gt;):
    /// sezioni [inact] / [act] con slot left/top/width
    /// </summary>
    public sealed class TableManagerConfig
    {
        public const int MaxSlots = 65;

        public sealed class Slot
        {
            public int Left, Top, Width = 792;
            internal readonly List<KeyValuePair<string, string>> Extra = new();
            public int EffectiveHeight => Width == 640 ? 461 :
                Math.Max(1, (int)Math.Round(Width * (546.0 / 792), MidpointRounding.AwayFromZero));
        }

        private IniFile _source;
        public string Name;
        public List<Slot> InactiveSlots = new List<Slot>();
        public List<Slot> ActiveSlots = new List<Slot>();

        public static string ProfilePath(string dir, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim() ||
                name.StartsWith('.') || name.EndsWith('.') ||
                name.EndsWith(".ini_bak", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Enter a valid layout name.");
            return Path.Combine(dir, name);
        }

        public static string[] ListProfiles(string dir) => Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir).Select(Path.GetFileName)
                .Where(name => !name.StartsWith('.') && !name.EndsWith(".ini_bak", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();

        public static void SeedProfiles(string dir)
        {
            if (Directory.Exists(dir)) return;
            Directory.CreateDirectory(dir);

            foreach (var (name, columns, count) in new[] { ("4x", 2, 4), ("3x2", 3, 6) })
            {
                var grid = new TableManagerConfig();
                for (int i = 0; i < count; i++) grid.InactiveSlots.Add(GridSlot(i, columns));
                grid.Save(dir, name, overwrite: false);
            }

            var stack = new TableManagerConfig();
            stack.InactiveSlots.Add(new Slot { Width = 640 });
            for (int i = 1; i <= 5; i++) stack.ActiveSlots.Add(GridSlot(i, 3));
            stack.Save(dir, "stack", overwrite: false);

            static Slot GridSlot(int index, int columns) =>
                new() { Left = index % columns * 640, Top = index / columns * 461, Width = 640 };
        }

        public static TableManagerConfig Load(string dir, string name)
        {
            var f = IniFile.Load(ProfilePath(dir, name));
            var cfg = new TableManagerConfig { Name = name, _source = f };

            LoadSlots(f, "inact", cfg.InactiveSlots);
            LoadSlots(f, "act", cfg.ActiveSlots);

            return cfg;
        }

        public void Save(string dir, string name, bool overwrite = true)
        {
            var path = ProfilePath(dir, name);
            var file = new IniFile(path);
            file.MergeFrom(_source);
            WriteSlots(file, "inact", InactiveSlots);
            WriteSlots(file, "act", ActiveSlots);
            file.Find("options")?.Entries.RemoveAll(entry =>
                entry.Key.Equals("autoSortTables", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("autoSortTime", StringComparison.OrdinalIgnoreCase));
            file.SaveAtomic(path, overwrite);
        }

        private static readonly string[] SlotFields = { "left", "top", "width" };

        private static bool SplitSlotKey(string key, out string field, out int index)
        {
            int suffix = key.Length;
            while (suffix > 0 && char.IsAsciiDigit(key[suffix - 1])) suffix--;
            field = key[..suffix];
            index = -1;
            return suffix > 0 && suffix < key.Length &&
                int.TryParse(key.AsSpan(suffix), NumberStyles.None, CultureInfo.InvariantCulture, out index);
        }

        private static void WriteSlots(IniFile file, string name, List<Slot> slots)
        {
            var section = file.GetOrAddSection(name);
            section.Entries.RemoveAll(entry => SplitSlotKey(entry.Key, out _, out _));
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var suffix = i.ToString(CultureInfo.InvariantCulture);
                int[] values = { slot.Left, slot.Top, slot.Width };
                for (int j = 0; j < SlotFields.Length; j++)
                    file.Set(name, SlotFields[j] + suffix, values[j]);
                foreach (var entry in slot.Extra) file.Set(name, entry.Key + suffix, entry.Value);
            }
        }

        private static void LoadSlots(IniFile f, string section, List<Slot> list)
        {
            for (int i = 0; i < MaxSlots; i++)
            {
                var suffix = i.ToString(CultureInfo.InvariantCulture);
                if (f.Get(section, "left" + suffix) == null) break;
                var slot = new Slot
                {
                    Left = f.GetInt(section, "left" + suffix),
                    Top = f.GetInt(section, "top" + suffix),
                    Width = Math.Max(1, f.GetInt(section, "width" + suffix, 792)),
                };
                foreach (var entry in f.Find(section).Entries)
                    if (SplitSlotKey(entry.Key, out var field, out var index) && index == i &&
                        !field.Equals("height", StringComparison.OrdinalIgnoreCase) &&
                        !field.Equals("bind", StringComparison.OrdinalIgnoreCase) &&
                        !field.Equals("multislot", StringComparison.OrdinalIgnoreCase) &&
                        !SlotFields.Contains(field, StringComparer.OrdinalIgnoreCase))
                        slot.Extra.Add(new(field, entry.Value));
                list.Add(slot);
            }
        }
    }
}
