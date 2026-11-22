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
            var page = TabPageByName(form, "Options");
            const string section = "PokerCaption";
            Program.Ini = new IniFile(null);
            try
            {
                controller.WireOptions(form);
                controller.WireDialogs(form);

                foreach (var name in UnavailableOptions)
                    check("Options unavailable " + name, Ctl(page, name) is { Enabled: false }, "");

                var retiredOptions = new[] { ("CheckBox13", "AutoTimebank"), ("cbAutoHideChat", "AutoHideChat"), ("CheckBox14", "StartMinimized") };
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
                    bool initial = box.Checked;
                    box.Checked = !initial;
                    bool writesToggle = Program.Ini.GetBool(section, key, initial) == !initial;
                    box.Checked = initial;
                    check("Options flag " + key, box.Enabled && initial == fallback && writesToggle &&
                        Program.Ini.GetBool(section, key, !initial) == initial, "");
                }

                check("Options dialog rule count", controller._dialogRules.Count == 22,
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
                Program.Ini.Set("AutoDialog", "Caption3", "My News");
                Program.Ini.Set("AutoDialog", "Delay3", 17);
                controller.SyncDialogsFromIni();
                check("Options removed rules are purged and surviving delay is preserved",
                    removed.All(entry => Program.Ini.Get(section, entry.Item2) == null) &&
                    Program.Ini.GetInt("AutoDialog", "Count", -1) == 1 &&
                    Program.Ini.Get("AutoDialog", "Caption0") == "My News" &&
                    Program.Ini.GetInt("AutoDialog", "Delay0", -1) == 17 &&
                    Program.Ini.Get("AutoDialog", "Caption1") == null, "");
                if (Ctl(page, "cbCloseMyNews") is CheckBox myNews) myNews.Checked = false;

                if (Ctl(page, "CheckBox82") is CheckBox delayed && Ctl(page, "Edit39") is TextBox field &&
                    Ctl(page, "UpDown38") is NumericUpDown spinner)
                {
                    delayed.Checked = true;
                    field.Text = "3";
                    check("Options delay text to spinner and milliseconds", spinner.Value == 3 &&
                        Program.Ini.GetInt(section, "CNFafter") == 3 &&
                        Program.Ini.GetInt("AutoDialog", "Delay0") == 3000, "");
                    spinner.Value = 0;
                    check("Options delay spinner accepts zero", field.Text == "0" &&
                        Program.Ini.GetInt("AutoDialog", "Delay0", -1) == 0, "");
                    delayed.Checked = false;
                    Program.Ini.Set(section, "CloseCantFound", 1);
                    Program.Ini.Set(section, "CNFafter", 2);
                    controller.SyncDialogsFromIni();
                    check("Options delay migrates original checkbox and seconds", delayed.Checked && spinner.Value == 2 &&
                        Program.Ini.GetInt("AutoDialog", "Delay0", -1) == 2000, "");
                    field.Text = "25";
                    check("Options VCL spinner clamps typed seconds", spinner.Minimum == 0 &&
                        spinner.Maximum == 20 && spinner.Value == 20 && field.Text == "25" &&
                        Program.Ini.GetInt("AutoDialog", "Delay0", -1) == 20000, "");
                    field.Text = "abc";
                    check("Options VCL spinner retains its position on invalid text", spinner.Value == 20 &&
                        Program.Ini.GetInt("AutoDialog", "Delay0", -1) == 20000, "");
                    spinner.Value = 19;
                    check("Options VCL spinner writes seconds after manual input", field.Text == "19" &&
                        Program.Ini.GetInt("AutoDialog", "Delay0", -1) == 19000, "");
                    field.Text = "-2";
                    check("Options VCL spinner clamps negative text", spinner.Value == 0 &&
                        Program.Ini.GetInt("AutoDialog", "Delay0", -1) == 0, "");
                    string group = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator;
                    field.Text = "1" + group + "2";
                    bool grouped = spinner.Value == 12;
                    field.Text = "-";
                    check("Options VCL spinner accepts grouped digits and bare minus", grouped && spinner.Value == 0, "");
                }
                else check("Options delay controls", false, "missing checkbox, field or spinner");

                check("Options Find a Player removed", Ctl(page, "CheckBox57") == null &&
                    Ctl(page, "Edit23") == null && Ctl(page, "UpDown45") == null, "");
                Program.Ini.Set(section, "TrnyRegShowLobbyClose", 1);
                controller.SyncDialogsFromIni();
                var showLobby = Ctl(page, "CheckBox41") as CheckBox;
                bool restored = showLobby is { Enabled: true, Checked: true };
                if (restored) showLobby.Checked = false;
                check("Options Show Lobby restores and clears legacy setting", restored &&
                    !Program.Ini.GetBool(section, "TrnyRegShowLobbyClose", true), "");
                Engine.AutoDialogEngine.CheckOptionsClosures(controller._dialogRules.Select(rule => rule.Caption), check);
                CheckTrayAndSaveOptions(form, page, check);
            }
            finally { Program.Ini = previousIni; }
        }

        private static void CheckTrayAndSaveOptions(Form form, TabPage page, Action<string, bool, string> check)
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
                Program.Ini.Set("PokerCaption", "MinimizeToTray", 1);
                SaveBoundOptions(form);
                bool firstSave = File.Exists(path) && !File.Exists(backup);
                if (Ctl(page, "CheckBox34") is CheckBox minimize) minimize.Checked = false;
                SaveBoundOptions(form);
                bool firstBackup = !IniFile.Load(path).GetBool("PokerCaption", "MinimizeToTray", true) &&
                    IniFile.Load(backup).GetBool("PokerCaption", "MinimizeToTray", false);
                Program.Ini.Set("PokerCaption", "MinimizeToTray", 1);
                SaveBoundOptions(form);
                check("Options Save options creates and replaces with previous backup", firstSave && firstBackup &&
                    IniFile.Load(path).GetBool("PokerCaption", "MinimizeToTray", false) &&
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
