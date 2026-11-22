using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokerCaption.Config;
using PokerCaption.Engine;
using PokerCaption.Native;

namespace PokerCaption
{
    /// <summary>
    /// Controller della finestra principale.
    /// Dietro gira il motore (tavoli, betscript, HUD, wheel, TM, auto-dialog);
    /// qui vengono solo collegati: status bar, binding INI, tray, autotest.
    /// partial: il wiring per-tab sta in MainUi.Betscript.cs / MainUi.Tabs.cs
    /// </summary>
    public sealed partial class MainUiController : IDisposable
    {
        public Form Form { get; private set; }
        private Label _sessionStatus;
        private NotifyIcon _tray;
        private HotkeyEngine _wheel;         // solo wheel bet
        private HookInstaller _nativeHook;
        private System.Windows.Forms.Timer _statusTimer;
        private System.Windows.Forms.Timer _hudTimer;
        private bool _pingOk;
        public void Build(bool connectToClient = true)
        {
            var f = DesktopWindow.Create();
            Form = f;
            var ini = Program.Ini;
            _sessionStatus = (Label)Ctl(f, "sessionStatus");

            var map = UiBinding.Settings;
            int applied = UiBinding.Apply(f, ini, map);
            Log($"UI: {applied} settings loaded");

            // switch principali LIVE: il cambio ha effetto immediato sul motore
            void WireLive(string ctl, Action onChange)
            {
                var c = DesktopWindow.Descendants(f).FirstOrDefault(x => x.Name == ctl) as CheckBox;
                if (c != null) c.CheckedChanged += (s, e) => onChange();
            }
            WireLive("hudActive", () =>           // HUD tab 'Active' → [HUD] HudActive
            {
                Program.Ini.Set("HUD", "HudActive", UiBinding.IsActive(f, "hudActive") ? "1" : "0");
                Program.Service.Hud.Load(Program.Ini);
                Log("HUD active = " + UiBinding.IsActive(f, "hudActive"));
            });
            WireLive("generalHudActive", () =>      // General HUD 'Active' → [GenHUD] active
            {
                Program.Ini.Set("GenHUD", "active", UiBinding.IsActive(f, "generalHudActive") ? "1" : "0");
                Program.Service.GenHud.Load(Program.Ini);
                Log("GenHUD active = " + UiBinding.IsActive(f, "generalHudActive"));
            });
            WireLive("lockGeneral", () =>           // 'Lock General' → [HUD] LockGeneral: blocca il mouse sul General HUD
            {
                Program.Ini.Set("HUD", "LockGeneral", UiBinding.IsActive(f, "lockGeneral") ? "1" : "0");
                Program.Service.GenHud.Load(Program.Ini);   // rilegge LockGeneral per il drag
                Log("HUD lock general = " + UiBinding.IsActive(f, "lockGeneral"));
            });
            WireLive("lockHud", () =>               // 'Lock HUD' → [HUD] LockHUD: blocca i pannelli tavolo/giocatore
            {
                Program.Ini.Set("HUD", "LockHUD", UiBinding.IsActive(f, "lockHud") ? "1" : "0");
                Program.Service.Hud.Load(Program.Ini);      // al cambio ricrea i pannelli col giusto click-through
                Log("HUD lock = " + UiBinding.IsActive(f, "lockHud"));
            });
            var tabs = (TabControl)Ctl(f, "workspace");
            string pageName = ini.Get("PokerCaption", "ActivePage", "HUD");
            var selected = tabs.TabPages.Cast<TabPage>().FirstOrDefault(page => page.Text == pageName);
            if (selected != null) tabs.SelectedTab = selected;
            tabs.SelectedIndexChanged += (_, _) =>
            {
                if (tabs.SelectedTab is { } page) ini.Set("PokerCaption", "ActivePage", page.Text);
            };

            // tab HUD: gruppo 'HUD panels' (Table/Player panels + General HUD):
            // liste dai pannelli INI + pulsanti Add new/Delete/Clone (MainUi.HudPanels.cs)
            WireHudPanels(f);

            // tab Betscript + Options/Table manager/Dialogs: prima del blocco selftest,
            // che può chiudere il form a metà Build (il wiring su form chiuso è rumore)
            AttachBetscript(f);
            WireTabs(f);

            // Save options salva PokerCaption.ini e conserva il backup .ini_bak.
            if (Ctl(f, "saveOptions") is Button btnSaveOpts)
                btnSaveOpts.Click += (s, e) =>
                {
                    try { SaveBoundOptions(f); Log("Save options: PokerCaption.ini salvato su disco"); }
                    catch (Exception ex) { Log("Save options ERR: " + ex.Message); }
                };

            // '[actions] tag config' → dialog chiavi actTag* (Engine/ActionsTagConfigForm)
            if (Ctl(f, "actionsTagConfig") is Button btnActCfg)
                btnActCfg.Click += (s, e) =>
                {
                    try { using var dlg = new ActionsTagConfigForm(Program.Ini, Log); dlg.ShowDialog(Form); }
                    catch (Exception ex) { Log("actions tag config ERR: " + ex.Message); }
                };

            if (!connectToClient)
            {
                _sessionStatus.Text = "Offline preview";
                return;
            }

            // HUD refresh veloce (fix lag): i pannelli seguono il tavolo a 4 Hz
            _hudTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _hudTimer.Tick += (s, e) =>
            {
                try
                {
                    // tempo di sessione fluido: tra i tick di scansione (2s) il timer
                    // HUD continua ad avanzare i secondi con l'ultimo stato noto
                    SessionStats.Current.Tick(SessionStats.Current.InHandNow);
                    Program.Service?.Hud.OnTick(Program.Service.Tables);
                    Program.Service?.GenHud.SetTables(Program.Service.Tables);
                    Program.Service?.GenHud.OnTick();
                }
                catch { }
            };

            f.FormClosing += OnFormClosing;

            // status bar live (Memo4) ogni 2 s
            _statusTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _statusTimer.Tick += (s, e) => UpdateStatus();
            _statusTimer.Start();

            SetupTray();

            // autotest hook (richiede loop messaggi): --selftest
            if (Environment.GetCommandLineArgs().Contains("--selftest"))
                StartSelfTest();
            // motore: parte sempre
            StartEngine();

            // nickname pre-tavolo via UIA:
            // dump PowerShell del lobby, primo Pane con nome tipo-nick
            if (!Program.Service.Manager.LayoutOnly && string.IsNullOrEmpty(Program.HeroNick))
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var tmp = Path.Combine(Path.GetTempPath(), "sc_uia_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".txt");
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "powershell",
                            Arguments = $"-ExecutionPolicy Bypass -File \"{Path.Combine(Program.AppDir, "tools", "dump_uia.ps1")}\" -out \"{tmp}\"",
                            CreateNoWindow = true, UseShellExecute = false
                        };
                        var p = System.Diagnostics.Process.Start(psi);
                        if (!p.WaitForExit(15000)) { p.Kill(); return; }
                        var txt = File.ReadAllText(tmp);
                        File.Delete(tmp);
                        foreach (var m in System.Text.RegularExpressions.Regex.Matches(txt, @"ControlType\.Pane\] '(\w{3,20})'"))
                        {
                            var cand = ((System.Text.RegularExpressions.Match)m).Groups[1].Value;
                            if (cand.Length >= 4 && cand.ToLowerInvariant() != "sisal" &&
                                cand != "Deposit" && cand != "Withdraw" && cand != "Observe" && cand != "Play")
                            {
                                Program.HeroNick = cand;
                                Log("hero nick (UIA lobby): " + cand);
                                break;
                            }
                        }
                    }
                    catch (Exception ex) { Log("UIA nick ERR: " + ex.Message); }
                });
            }

            WireCaptionAutoClose(f, () => ClientLocator.FindClients().Any(c => IsCaptionClient(c.ExePath)));
        }

        // ---------- betscript list ----------
        /// <summary>Riempie ListBox4 con le regole [AdvancedBets] del merged INI.</summary>
        private static void FillBetscriptList(Form f, Config.IniFile ini)
        {
            if (ini == null) return;
            try
            {
                var lb = DesktopWindow.Descendants(f).OfType<ListBox>()
                    .FirstOrDefault(l => string.Equals(l.Name, "ListBox4", StringComparison.OrdinalIgnoreCase));
                if (lb == null) return;
                var bs = new BetScriptEngine();
                bs.Load(ini);
                foreach (var r in bs.Rules)
                    lb.Items.Add(BetscriptRuleText(r.Streets, r.Formula));
                if (bs.Rules.Count > 0) lb.SelectedIndex = 0;
            }
            catch (Exception ex) { Log("betscript list: " + ex.Message); }
        }

        private static void LayoutHud(Panel panel)
        {
            panel.SuspendLayout();
            var controls = new GroupBox
            {
                Text = "HUD controls",
                Bounds = new Rectangle(14, 12, 155, 94),
                TabStop = false,
            };
            panel.Controls.Add(controls);
            foreach (var (name, y) in new[] { ("hudActive", 22), ("lockHud", 45), ("lockGeneral", 68) })
            {
                var control = Ctl(panel, name);
                control.Parent = controls;
                control.Location = new Point(13, y);
            }
            controls.BringToFront();

            Ctl(panel, "GroupBox110").Location = new Point(184, 12);
            Ctl(panel, "actionsTagConfig").Location = new Point(433, 17);
            Ctl(panel, "GroupBox55").Location = new Point(433, 49);
            Ctl(panel, "GroupBox41").Location = new Point(14, 144);
            panel.ResumeLayout(true);
        }

        /// <summary>Regola [AdvancedBets] → riga visualizzata: "PREFLOP pot*2".</summary>
        private static string BetscriptRuleText(int mask, string formula)
        {
            var names = new List<string>();
            if ((mask & 1) != 0) names.Add("PREFLOP");
            if ((mask & 2) != 0) names.Add("FLOP");
            if ((mask & 4) != 0) names.Add("TURN");
            if ((mask & 8) != 0) names.Add("RIVER");
            if (names.Count == 0) names.Add("(nessuna street)");
            return string.Join(" ", names) + " " + (formula ?? "");
        }

        // ---------- motore ----------
        private void StartEngine()
        {
            try
            {
                Program.Service.Manager.LayoutModeChanged += RefreshClientIntegration;
                RefreshClientIntegration();
                Program.Service.Start();
                _hudTimer?.Start();
                _statusTimer?.Start();
                Log("engine AVVIATO");
                if (!Program.Service.Manager.LayoutOnly)
                    Log($"armato: auto-dialog={Program.Ini.GetInt("AutoDialog", "Count", 0)} regole · " +
                    $"TM={Program.Service.Manager.Enabled && Program.Service.Manager.Config != null} ({Program.Service.Manager.ConfigName}) · " +
                    $"betscript={BetscriptInfo()} · wheel step={Program.Ini.GetDouble("PokerCaption", "MouseWheelUp", 0.5)}bb");
            }
            catch (Exception ex) { Log("START ERR: " + ex.Message); }
        }

        private void RefreshClientIntegration()
        {
            Program.Shared.SetMainHwnd(Form.Handle);
            if (_nativeHook == null)
            {
                _nativeHook = new HookInstaller(PickNativeDll());
                if (!_nativeHook.Install(0))
                    Log("hook nativo: installazione globale fallita (err=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
            }
            if (Program.Service.Manager.LayoutOnly)
            {
                _wheel?.Dispose();
                _wheel = null;
                Log("TM: solo layout; HUD e scansione attivi, statistiche sui pixel visibili, PrintWindow, rotellina e dialoghi automatici sospesi; BetScript su scansione");
            }
            else if (_wheel == null)
            {
                _wheel = new HotkeyEngine(Program.Service.TableUnderCursor, Log);
                _wheel.Load(Program.Ini);
                if (!_wheel.Install()) Log("wheel hook install fallita");
            }
            Program.Service.RefreshClientWork();
        }

        private string BetscriptInfo()
        {
            var n = Program.Ini.GetInt("AdvancedBets", "Count", 0);
            return $"{n} regole";
        }

        private string PickNativeDll()
        {
            string baseDir = Path.Combine(Program.AppDir, "native");
            var arch = Environment.Is64BitProcess ? "x64" : "x86";
            var path = Path.Combine(baseDir, arch, "captionHook.dll");
            return File.Exists(path) ? path : "captionHook.dll";
        }

        // ---------- status bar ----------
        public void UpdateStatus()
        {
            if (_sessionStatus == null || Form == null) return;
            var tables = Program.Service?.Tables;
            string nickname = tables?.FirstOrDefault(table => !string.IsNullOrEmpty(table.HeroNick))?.HeroNick
                ?? Program.HeroNick;
            _sessionStatus.Text = nickname ?? "";
        }

        // ---------- tray ----------
        private void SetupTray()
        {
            _tray = new NotifyIcon
            {
                Icon = Form.Icon, Text = "PokerCaption",
                Visible = Program.Ini.GetBool("PokerCaption", "MinimizeToTray", true),
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Mostra", null, (s, e) => ShowFromTray());
            menu.Items.Add("Config INI…", null, (s, e) => new SettingsForm(Path.Combine(Program.AppDir, "PokerCaption.ini")).ShowDialog(Form));
            menu.Items.Add("Esci", null, (s, e) => Form.Close());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => ShowFromTray();
            Form.HandleCreated += (_, _) => ApplyTrayOption(Program.Ini.GetBool("PokerCaption", "MinimizeToTray", true));
            if (Form.IsHandleCreated) ApplyTrayOption(_tray.Visible);
        }

        private void ApplyTrayOption(bool enabled)
        {
            if (_tray == null || Form == null) return;
            if (Form.IsHandleCreated)
            {
                var window = Form.Handle;
                int owner = enabled ? NativeMethods.GetDesktopWindow().ToInt32() : 0;
                if (NativeMethods.GetWindowLongW(window, NativeMethods.GWL_HWNDPARENT) != owner)
                    NativeMethods.SetWindowLongW(window, NativeMethods.GWL_HWNDPARENT, owner);
            }
            _tray.Visible = enabled;
        }

        private void ShowFromTray()
        {
            Form.WindowState = FormWindowState.Normal;
            Form.Show();
            Form.Activate();
        }

        private void HideToTray(Form form)
        {
            form.Hide();
            if (_tray != null) _tray.Visible = true;
        }

        private static void SaveBoundOptions(Form form)
        {
            var map = UiBinding.Settings;
            if (form != null) UiBinding.Collect(form, Program.Ini, map);
            Program.Ini?.Save();
        }

        // ---------- ciclo di vita ----------
        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && _tableManagerPanel?.ConfirmDiscard() == false)
            {
                e.Cancel = true;
                return;
            }
            SaveBoundOptions(Form);
            try { SessionStats.Current.Save(Path.Combine(Program.AppDir, "global_stats")); } catch { }
            StopEngine();
            _tray?.Dispose();
        }

        private void StopEngine()
        {
            if (Program.Service != null)
                Program.Service.Manager.LayoutModeChanged -= RefreshClientIntegration;
            StopClientHooks();
            Program.Service?.Stop();
            _hudTimer?.Stop();
            Log("engine FERMATO");
        }

        private void StopClientHooks()
        {
            _nativeHook?.Dispose();
            _nativeHook = null;
            _wheel?.Dispose();
            _wheel = null;
        }

        // ---------- autotest hook ----------
        private void StartSelfTest()
        {
            try
            {
                File.WriteAllText(Path.Combine(Program.AppDir, "selftest.txt"), "START");
                HookSelfTest();
                // il ping è POSTED: serve il message loop per farlo processare
                // dall'hook; la conferma arriva dal contatore nativo hookReplies
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 1500)
                {
                    Application.DoEvents();
                    if (_nativeHook != null && _nativeHook.CallIntExport("hookReplies") > 0) { _pingOk = true; break; }
                    System.Threading.Thread.Sleep(20);
                }
                File.WriteAllText(Path.Combine(Program.AppDir, "selftest.txt"), _pingOk ? "PING_OK" : "PING_FAIL");
                Form.Close();
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(Program.AppDir, "selftest.txt"), "ERR " + ex);
                Form.Close();
            }
        }

        private void HookSelfTest()
        {
            _nativeHook?.Dispose();
            _nativeHook = new HookInstaller(PickNativeDll());
            var tid = NativeMethods.GetCurrentThreadId();
            if (!_nativeHook.Install((int)tid)) { Log("self-test: SetWindowsHookEx fallito, err=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error()); return; }
            Log("hook self-test installato sul thread " + tid);
            _pingOk = false;
            Program.Shared.SetMainHwnd(Form.Handle);
            System.Threading.Tasks.Task.Run(() => Program.Shared.SendCommand(SharedLayout.CMD_PING, 0)).Wait();
            Log("ping inviato via shared memory (attesa 0x402)");
        }

        // ---------- log ----------
        public static void Log(string line)
        {
            try
            {
                File.AppendAllText(Path.Combine(Program.AppDir, "main_log.txt"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line + "\r\n");
            }
            catch { }
        }

        public void Dispose()
        {
            StopEngine();
            _tray?.Dispose();
        }
    }
}
