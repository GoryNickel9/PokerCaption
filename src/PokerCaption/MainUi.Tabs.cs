using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using PokerCaption.Config;
using PokerCaption.Engine;

namespace PokerCaption
{
    /// <summary>
    /// partial di MainUiController: tab Options (con il gruppo Dialogs unito dentro),
    /// Table manager e Dialogs.
    /// Le caption dei checkbox del DFM corrispondono alle funzioni del motore.
    /// Regola: ogni cambio scrive SUBITO il valore nell'INI in memoria;
    /// il salvataggio su disco avviene alla chiusura dell'app (OnFormClosing).
    ///  - Options → chiavi [PokerCaption] (+ backup/ripristino PokerCaption.ini_bak)
    ///  - Table manager → editor di layout con salvataggio esplicito
    ///  - Dialogs (gruppo dentro Options) → rigenera TUTTA la sezione [AutoDialog]
    ///    (Count + Caption_i/Delay_i) dalle checkbox "Close 'X'" e ricarica
    ///    Program.Service.AutoDialog
    /// </summary>
    public sealed partial class MainUiController
    {
        // evita rientranza: mentre i controlli vengono settati via codice
        // (apply iniziale / load backup) gli eventi di cambio devono restare inerti
        private bool _tabsSync;

        private static readonly string[] UnavailableOptions =
        {
            "CheckBox90", "CheckBox94", "CheckBox279", "Edit236", "UpDown165", "CheckBox549",
        };

        private sealed record DialogRule(CheckBox Box, string Caption, string LegacyKey);
        private readonly List<DialogRule> _dialogRules = new List<DialogRule>();

        private static readonly (string Control, string Key, bool Default)[] OptionsFlags =
        {
            ("CheckBox34", "MinimizeToTray", true),
            ("CheckBox344", "CaptionAutoClose", false),
            ("CheckBox173", "CloseTableHasBeenClosed", false),
            ("CheckBox401", "CloseTableHasConcluded", false),
            ("CheckBox504", "CloseTournamentIsOver", false),
            ("CheckBox283", "CloseTrnyLobby", false),
            ("CheckBox390", "WaitingListOk", false),
            ("CheckBox316", "CurSessInfo", false),
            ("CheckBox208", "KeepPlaying", false),
            ("CheckBox50", "InactivityTO", false),
        };

        public void WireTabs(Form f)
        {
            try { WireOptions(f); } catch (Exception ex) { Log("Options wiring ERR: " + ex.Message); }
            try { WireTableManager(f); } catch (Exception ex) { Log("TM wiring ERR: " + ex.Message); }
            try { WireDialogs(f); } catch (Exception ex) { Log("Dialogs wiring ERR: " + ex.Message); }
        }

        // =========================================================== OPTIONS
        private void WireOptions(Form f)
        {
            var page = (Control)TabPageByName(f, "Options") ?? f;

            foreach (var name in UnavailableOptions)
                if (Ctl(page, name) is Control unavailable)
                {
                    unavailable.Enabled = false;
                    unavailable.AccessibleDescription = "Unavailable in this build.";
                }

            // Applica le opzioni salvate prima di collegare gli eventi.
            SyncOptionsFromIni(page);

            foreach (var (name, key, _) in OptionsFlags)
            {
                if (Ctl(page, name) is not CheckBox cb) continue;
                cb.CheckedChanged += (s, e) =>
                {
                    if (_tabsSync) return;
                    Program.Ini.Set("PokerCaption", key, cb.Checked ? "1" : "0");
                    if (key == "MinimizeToTray") ApplyTrayOption(cb.Checked);
                    if (key == "CloseTrnyLobby") Program.Service?.SetCloseTournamentLobby(cb.Checked);
                    else if (key is not ("MinimizeToTray" or "CaptionAutoClose"))
                        Program.Service?.AutoDialog.Load(Program.Ini);
                    Log("Options: " + key + " = " + (cb.Checked ? "1" : "0"));
                };
            }

            f.Resize += (s, e) =>
            {
                if (f.WindowState == FormWindowState.Minimized && _tray?.Visible == true &&
                    Program.Ini.GetBool("PokerCaption", "MinimizeToTray", true))
                    HideToTray(f);
            };

        }

        private static bool IsCaptionClient(string path)
        {
            string name = Path.GetFileName(path);
            if (!name.StartsWith("Sisal", StringComparison.OrdinalIgnoreCase)) return true;
            return name.Equals("Sisal.exe", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("SisalGl.exe", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("SisalOpenGL.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static System.Windows.Forms.Timer WireCaptionAutoClose(Form form, Func<bool> hasClient)
        {
            var options = TabPageByName(form, "Options");
            if (options == null || Ctl(options, "CheckBox344") is not CheckBox enabled) return null;

            var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            bool clientSeen = false;
            int consecutiveMisses = 0;

            void ApplySelection()
            {
                timer.Stop();
                consecutiveMisses = 0;
                clientSeen = enabled.Checked && hasClient();
                if (enabled.Checked) timer.Start();
            }

            enabled.CheckedChanged += (_, _) => ApplySelection();
            timer.Tick += (_, _) =>
            {
                if (hasClient())
                {
                    clientSeen = true;
                    consecutiveMisses = 0;
                    return;
                }
                if (!clientSeen || ++consecutiveMisses < 2) return;
                timer.Stop();
                Log("CaptionAutoClose: ultimo client chiuso");
                form.Close();
            };
            form.Disposed += (_, _) => timer.Dispose();
            ApplySelection();
            return timer;
        }

        /// <summary>Controlli Options gestiti dal controller ← valori attuali dell'INI.</summary>
        private void SyncOptionsFromIni(Control page)
        {
            PokerCaptionConfig.RemoveRetiredOptions(Program.Ini);
            var syncing = _tabsSync;
            _tabsSync = true;
            try
            {
                foreach (var (name, key, fallback) in OptionsFlags)
                    SetChecked(page, name, Program.Ini.GetBool("PokerCaption", key, fallback));
            }
            finally { _tabsSync = syncing; }
        }

        // ============================================================ DIALOGS
        private void WireDialogs(Form f)
        {
            var page = (Control)TabPageByName(f, "Options") ?? f;
            var captionPattern = new Regex("^Close\\s+'(.+?)'", RegexOptions.Singleline);
            foreach (var box in DesktopWindow.Descendants(page).OfType<CheckBox>())
            {
                if (UnavailableOptions.Contains(box.Name)) continue;
                var match = captionPattern.Match(box.Text.Trim());
                if (!match.Success) continue;

                var legacy = box.Name switch
                {
                    "cbCloseMyNews" => "CloseMyNews",
                    "cbCloseLogoutInfo" => "CloseLogoutInfo",
                    "cbStarsAccountInfo" => "StarsAccountInfo",
                    "CheckBox41" => "TrnyRegShowLobbyClose",
                    _ => null,
                };
                var rule = new DialogRule(box, match.Groups[1].Value.Trim(), legacy);
                _dialogRules.Add(rule);
                box.CheckedChanged += (_, _) =>
                {
                    if (!_tabsSync) UpdateDialogRule(rule);
                };
            }

            Program.Service?.AutoDialog.SetOptionCaptions(_dialogRules.Select(rule => rule.Caption));
            SyncDialogsFromIni();
            Log("Dialogs: " + _dialogRules.Count + " checkbox collegate a [AutoDialog]");
        }

        private static List<(string Caption, int Delay)> ReadDialogRules()
        {
            var rules = new List<(string, int)>();
            for (int i = 0; ; i++)
            {
                var caption = Program.Ini.Get("AutoDialog", "Caption" + i);
                if (caption == null) return rules;
                rules.Add((caption, Program.Ini.GetInt("AutoDialog", "Delay" + i, 0)));
            }
        }

        private void SyncDialogsFromIni()
        {
            var saved = ReadDialogRules();
            bool migrated = saved.RemoveAll(entry => AutoDialogEngine.IsRemovedOptionCaption(entry.Caption)) > 0;
            Program.Ini.Find("PokerCaption")?.Entries.RemoveAll(entry =>
                entry.Key.Equals("FPafter", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("TableCapReached", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("CloseSeatIsReserved", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("CloseSeatIsOccupied", StringComparison.OrdinalIgnoreCase));
            var syncing = _tabsSync;
            _tabsSync = true;
            try
            {
                foreach (var rule in _dialogRules)
                {
                    var entry = saved.FirstOrDefault(x => string.Equals(x.Caption, rule.Caption, StringComparison.OrdinalIgnoreCase));
                    if (entry.Caption == null && rule.LegacyKey != null &&
                        Program.Ini.GetBool("PokerCaption", rule.LegacyKey, false))
                    {
                        entry = (rule.Caption, 0);
                        saved.Add(entry);
                        migrated = true;
                    }
                    rule.Box.Checked = entry.Caption != null;
                }
            }
            finally { _tabsSync = syncing; }
            if (migrated) WriteDialogRules(saved);
        }

        private static void UpdateDialogRule(DialogRule rule)
        {
            var rules = ReadDialogRules();
            int index = rules.FindIndex(x => string.Equals(x.Caption, rule.Caption, StringComparison.OrdinalIgnoreCase));
            if (rule.Box.Checked)
            {
                if (index < 0) rules.Add((rule.Caption, 0));
            }
            else
            {
                rules.RemoveAll(x => string.Equals(x.Caption, rule.Caption, StringComparison.OrdinalIgnoreCase));
            }

            if (rule.LegacyKey != null)
                Program.Ini.Set("PokerCaption", rule.LegacyKey, rule.Box.Checked ? "1" : "0");
            WriteDialogRules(rules);
        }

        private static void WriteDialogRules(List<(string Caption, int Delay)> rules)
        {
            var section = Program.Ini.GetOrAddSection("AutoDialog");
            section.Entries.RemoveAll(entry => Regex.IsMatch(entry.Key, "^(?:Caption|Delay)[0-9]+$", RegexOptions.IgnoreCase));
            for (int i = 0; i < rules.Count; i++)
            {
                section["Caption" + i] = rules[i].Caption;
                section["Delay" + i] = rules[i].Delay.ToString(CultureInfo.InvariantCulture);
            }
            section["Count"] = rules.Count.ToString(CultureInfo.InvariantCulture);
            Program.Service?.AutoDialog.Load(Program.Ini);
        }

        // ============================================================ helpers
        private static TabPage TabPageByName(Form f, string title) =>
            DesktopWindow.Descendants(f).OfType<TabPage>()
                .FirstOrDefault(p => string.Equals(p.Text.Trim(), title, StringComparison.OrdinalIgnoreCase));

        /// <summary>Controllo per nome (case-insensitive) in un sottoalbero.</summary>
        private static Control Ctl(Control root, string name) =>
            DesktopWindow.Descendants(root).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        private static void SetChecked(Control root, string name, bool v) { if (Ctl(root, name) is CheckBox cb) cb.Checked = v; }
        private static void SetText(Control root, string name, string v) { if (Ctl(root, name) is TextBox tb) tb.Text = v; }
        private static int IntText(TextBox tb, int def) =>
            tb != null && int.TryParse(tb.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
    }
}
