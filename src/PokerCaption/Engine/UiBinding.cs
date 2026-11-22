using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokerCaption.Config;

namespace PokerCaption.Engine
{
    /// <summary>Maps desktop settings to their persisted keys.</summary>
    internal static class UiBinding
    {
        internal sealed record Entry(string Control, string Section, string Key, string Kind = "bool");

        internal static readonly IReadOnlyList<Entry> Settings =
        [
            new("cbCloseMyNews", "PokerCaption", "CloseMyNews"),
            new("cbCloseLogoutInfo", "PokerCaption", "CloseLogoutInfo"),
            new("cbStarsAccountInfo", "PokerCaption", "StarsAccountInfo"),
            new("CheckBox72", "PokerCaption", "BetscriptOn"),
            new("hudActive", "HUD", "HudActive"),
            new("lockGeneral", "HUD", "LockGeneral"),
            new("lockHud", "HUD", "LockHUD"),
            new("generalHudActive", "GenHUD", "active")
        ];

        /// <summary>Scrive i valori dell'INI nei controlli della form.</summary>
        public static int Apply(Control root, IniFile ini, IEnumerable<Entry> map)
        {
            int n = 0;
            foreach (var e in map)
            {
                var c = Find(root, e.Control);
                if (c == null) continue;
                var raw = ini.Get(e.Section, e.Key);
                if (raw == null) continue;
                switch (c)
                {
                    case CheckBox cb:
                        cb.Checked = raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case TextBox tb:
                        tb.Text = raw;
                        break;
                    case NumericUpDown nud:
                        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
                        {
                            var d = (decimal)dv;
                            if (d < nud.Minimum) nud.Minimum = d;
                            if (d > nud.Maximum) nud.Maximum = d;
                            nud.Value = d;
                        }
                        break;
                    case ComboBox cmb:
                        {
                            var i = cmb.Items.IndexOf(raw);
                            if (i >= 0) cmb.SelectedIndex = i;
                            break;
                        }
                    case TrackBar tr:
                        if (int.TryParse(raw, out var iv)) tr.Value = Math.Min(Math.Max(iv, tr.Minimum), tr.Maximum);
                        break;
                }
                n++;
            }
            return n;
        }

        /// <summary>Rilegge i controlli e li scrive nell'INI (senza salvare su disco).</summary>
        public static int Collect(Control root, IniFile ini, IEnumerable<Entry> map)
        {
            int n = 0;
            foreach (var e in map)
            {
                var c = Find(root, e.Control);
                if (c == null) continue;
                string v = e.Kind switch
                {
                    "bool" => (c as CheckBox)?.Checked == true ? "1" : "0",
                    "int" => Convert.ToString(c is TrackBar t2 ? t2.Value : 0, CultureInfo.InvariantCulture),
                    "double" => Convert.ToString(c is NumericUpDown n2 ? (double)n2.Value : 0, CultureInfo.InvariantCulture),
                    _ => c is TextBox t ? t.Text : (c as ComboBox)?.SelectedItem as string ?? ""
                };
                ini.Set(e.Section, e.Key, v);
                n++;
            }
            return n;
        }

        private static Control Find(Control root, string name)
        {
            foreach (Control c in root.Controls)
            {
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
                var r = Find(c, name);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>Stato checked di un controllo per nome (per i wiring live).</summary>
        public static bool IsActive(Control root, string name)
        {
            var c = Find(root, name) as CheckBox;
            return c?.Checked ?? false;
        }
    }
}
