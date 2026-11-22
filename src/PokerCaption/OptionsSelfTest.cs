using System.Windows.Forms;
using PokerCaption.Config;
using PokerCaption.Native;

namespace PokerCaption
{
    public sealed partial class MainUiController
    {
        internal static void CheckOptions(Form form, Action<string, bool, string> check)
        {
            var previousIni = Program.Ini;
            var controller = new MainUiController();
            var page = WorkspacePageByName(form, "Options");
            const string section = "PokerCaption";
            string optionsPath = Path.Combine(Path.GetTempPath(), $"poker-caption-options-{Guid.NewGuid():N}.ini");
            Program.Ini = new IniFile(optionsPath);
            try
            {
                controller.WireOptions(form);
                controller.WireDialogs(form);

                check("Options exposes one Never sit out control for cash and tournament tables",
                    Ctl(page, "neverSitOut") is CheckBox { Text: "Never sit out", Enabled: true } &&
                    Ctl(page, "CheckBox94") == null, "");

                var retiredOptions = new[] { ("CheckBox13", "AutoTimebank"), ("cbAutoHideChat", "AutoHideChat"), ("CheckBox14", "StartMinimized"),
                    ("CheckBox82", "CloseCantFound"), ("UpDown38", "CNFafter"),
                    ("CheckBox401", "CloseTableHasConcluded"), ("CheckBox504", "CloseTournamentIsOver"), ("CheckBox283", "CloseTrnyLobby") };
                foreach (var (control, key) in retiredOptions)
                {
                    check("Options removed control " + control, Ctl(page, control) == null, "");
                    Program.Ini.Set(section, key, 1);
                }
                controller.SyncOptionsFromIni(page);
                check("Options retired settings are removed from old configuration",
                    retiredOptions.All(entry => Program.Ini.Get(section, entry.Item2) == null), "");

                foreach (var (name, key, fallback) in OptionsFlags)
                {
                    if (Ctl(page, name) is not CheckBox box)
                    {
                        check("Options flag " + key, false, "missing checkbox");
                        continue;
                    }
                    if (key == "NeverSitOutLimit") continue;
                    bool initial = box.Checked;
                    box.Checked = !initial;
                    bool writesToggle = Program.Ini.GetBool(section, key, initial) == !initial;
                    box.Checked = initial;
                    check("Options flag " + key, box.Enabled && initial == fallback && writesToggle &&
                        Program.Ini.GetBool(section, key, !initial) == initial, "");
                }

                var never = (CheckBox)Ctl(page, "neverSitOut");
                var limit = (CheckBox)Ctl(page, "neverSitOutLimit");
                var maximum = (NumericUpDown)Ctl(page, "neverSitOutMaximum");
                bool initiallyOff = !limit.Enabled && !maximum.Enabled;
                never.Checked = true;
                bool available = limit.Enabled && !maximum.Enabled;
                limit.Checked = true;
                maximum.Value = 7;
                var disk = IniFile.Load(optionsPath);
                check("Options saves the optional per-table sit-out maximum immediately",
                    initiallyOff && available && maximum.Enabled &&
                    disk.GetBool(section, "NeverSitOutLimit", false) && disk.GetInt(section, "NeverSitOutMaximum") == 7, "");

                check("Options dialog rule count", controller._dialogRules.Count == 1,
                    controller._dialogRules.Count.ToString());
                foreach (var rule in controller._dialogRules)
                {
                    bool initiallyDisabled = !rule.Box.Checked;
                    rule.Box.Checked = true;
                    bool writesRule = Program.Ini.Get("AutoDialog", "Caption0") == rule.Caption;
                    rule.Box.Checked = false;
                    check("Options dialog " + rule.Box.Name, rule.Box.Enabled && initiallyDisabled && writesRule &&
                        Program.Ini.GetInt("AutoDialog", "Count", -1) == 0, rule.Caption);
                }

                var removed = new[]
                {
                    ("CheckBox545", "TableCapReached", "table cap reached"),
                    ("CheckBox310", "CloseSeatIsReserved", "This seat is reserved for players on the waiting list"),
                    ("CheckBox311", "CloseSeatIsOccupied", "This seat is occupied"),
                };
                for (int i = 0; i < removed.Length; i++)
                {
                    var (control, key, caption) = removed[i];
                    check("Options removed control " + control, Ctl(page, control) == null, "");
                    Program.Ini.Set("AutoDialog", "Caption" + i, caption);
                    Program.Ini.Set("AutoDialog", "Delay" + i, i + 1);
                    Program.Ini.Set(section, key, 1);
                }
                Program.Ini.Set("AutoDialog", "Caption3", "Player cannot be found.");
                Program.Ini.Set("AutoDialog", "Delay3", 1000);
                Program.Ini.Set("AutoDialog", "Caption4", "Find Seat (Cash Games)");
                Program.Ini.Set("AutoDialog", "Caption5", "Seat Me / All seats have been taken");
                Program.Ini.Set("AutoDialog", "Caption6", "My News");
                Program.Ini.Set("AutoDialog", "Caption7", "PokerStars Logout Information");
                Program.Ini.Set("AutoDialog", "Caption8", "You have been unregistered from");
                Program.Ini.Set("AutoDialog", "Caption9", "table is closed");
                Program.Ini.Set("AutoDialog", "Delay9", 17);
                controller.SyncDialogsFromIni();
                check("Options removed rules are purged and surviving delay is preserved",
                    removed.All(entry => Program.Ini.Get(section, entry.Item2) == null) &&
                    Program.Ini.GetInt("AutoDialog", "Count", -1) == 1 &&
                    Program.Ini.Get("AutoDialog", "Caption0") == "table is closed" &&
                    Program.Ini.GetInt("AutoDialog", "Delay0", -1) == 17 &&
                    Program.Ini.Get("AutoDialog", "Caption1") == null, "");
                if (Ctl(page, "CheckBox327") is CheckBox closedTable) closedTable.Checked = false;

                var columns = Ctl(page, "dialogColumns") as TableLayoutPanel;
                check("Options dialog rules span two populated columns", columns is { ColumnCount: 2 } &&
                    columns.Controls.Count == 2 && columns.Controls.Cast<Control>().All(column => column.Controls.Count > 0) &&
                    columns.Controls.Cast<Control>().Sum(column => column.Controls.OfType<CheckBox>().Count()) == 4, "");

                check("Options Find a Player removed", Ctl(page, "CheckBox57") == null &&
                    Ctl(page, "Edit23") == null && Ctl(page, "UpDown45") == null, "");
                string[] retiredControls = ["cbCloseMyNews", "cbStarsAccountInfo", "CheckBox532", "CheckBox472",
                    "CheckBox477", "CheckBox508", "CheckBox390", "CheckBox549", "CheckBox452", "CheckBox89",
                    "CheckBox41", "CheckBox63", "CheckBox372", "CheckBox170", "cbCloseLogoutInfo", "CheckBox421",
                    "CheckBox506", "CheckBox500", "CheckBox313", "CheckBox312"];
                string[] retiredKeys = ["WaitingListOk", "CloseMyNews", "StarsAccountInfo", "TrnyRegShowLobbyClose", "CloseLogoutInfo"];
                foreach (string key in retiredKeys) Program.Ini.Set(section, key, 1);
                controller.SyncDialogsFromIni();
                check("Options retired dialog controls and legacy flags are removed",
                    retiredControls.All(name => Ctl(page, name) == null) &&
                    retiredKeys.All(key => Program.Ini.Get(section, key) == null), "");
                Engine.AutoDialogEngine.CheckOptionsClosures(controller._dialogRules.Select(rule => rule.Caption), check);
                CheckTrayAndAutosave(page, check);
            }
            finally
            {
                Program.Ini = previousIni;
                File.Delete(optionsPath);
                File.Delete(Path.ChangeExtension(optionsPath, ".ini_bak"));
            }
        }

        private static void CheckTrayAndAutosave(Control page, Action<string, bool, string> check)
        {
            using var window = new Form
            {
                ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(-2000, -2000),
            };
            using var tray = new NotifyIcon();
            var startup = new MainUiController { Form = window, _tray = tray };
            window.Show();
            startup.ApplyTrayOption(true);
            int actualOwner = NativeMethods.GetWindowLongW(window.Handle, NativeMethods.GWL_HWNDPARENT);
            bool trayEnabled = tray.Visible && actualOwner == NativeMethods.GetDesktopWindow().ToInt32();
            startup.ApplyTrayOption(false);
            check("Options tray owns window only while enabled", trayEnabled && !tray.Visible &&
                NativeMethods.GetWindowLongW(window.Handle, NativeMethods.GWL_HWNDPARENT) == 0,
                $"enabledOwner=0x{actualOwner:x}");

            string path = Path.Combine(Path.GetTempPath(), $"poker-caption-options-{Guid.NewGuid():N}.ini");
            string backup = Path.ChangeExtension(path, ".ini_bak");
            try
            {
                Program.Ini = new IniFile(path);
                var minimize = (CheckBox)Ctl(page, "CheckBox34");
                minimize.Checked = false;
                bool firstSave = File.Exists(path) && !File.Exists(backup) &&
                    !IniFile.Load(path).GetBool("PokerCaption", "MinimizeToTray", true);
                minimize.Checked = true;
                check("Options autosaves immediately and preserves the previous configuration as a backup",
                    firstSave && IniFile.Load(path).GetBool("PokerCaption", "MinimizeToTray", false) &&
                    !IniFile.Load(backup).GetBool("PokerCaption", "MinimizeToTray", true), "");
            }
            finally
            {
                File.Delete(path);
                File.Delete(backup);
            }
        }

    }
}
