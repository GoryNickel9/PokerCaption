using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using PokerCaption.Config;
using PokerCaption.Engine;
using PokerCaption.Native;

namespace PokerCaption
{
    internal static class Program
    {
        public static TableService Service;       // motore tavoli (Fasi 3–6, 8–12)
        public static IniFile Ini;                // config condivisa
        public static string AppDir;
        public static SharedMemory Shared;        // memoria condivisa con la DLL
        public static string HeroNick;            // nick via UIA lobby (pre-tavolo)

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] is "--hud-preview" or "--hud-preview-test")
                return HudPreview.Run(verify: args[0] == "--hud-preview-test");

            AppDir = AppContext.BaseDirectory;
            File.WriteAllText(Path.Combine(AppDir, "main_log.txt"),
                "start " + string.Join(" ", args) + " bit=" + (Environment.Is64BitProcess ? 64 : 32) + "\r\n");

            SharedMemory.ActionHookMode? diagnosticHookMode = args switch
            {
                ["--diagnostic-set-visible=on"] => SharedMemory.ActionHookMode.Default,
                ["--diagnostic-set-visible=off"] => SharedMemory.ActionHookMode.SkipVisible,
                ["--diagnostic-action-hooks=off"] => SharedMemory.ActionHookMode.SkipAll,
                ["--diagnostic-text-hook=proxy"] => SharedMemory.ActionHookMode.ProxyOnly,
                ["--diagnostic-text-hook=widget"] => SharedMemory.ActionHookMode.WidgetOnly,
                _ => null
            };
            if (!diagnosticHookMode.HasValue && args.Any(arg =>
                arg.StartsWith("--diagnostic-set-visible", StringComparison.Ordinal) ||
                arg.StartsWith("--diagnostic-action-hooks", StringComparison.Ordinal) ||
                arg.StartsWith("--diagnostic-text-hook", StringComparison.Ordinal)))
            {
                MainUiController.Log("Use exactly one diagnostic argument: --diagnostic-set-visible=on|off, --diagnostic-action-hooks=off, or --diagnostic-text-hook=proxy|widget");
                return 2;
            }

            // autotest motori (senza UI): scrive selftest2.txt
            if (args.Length >= 1 && args[0] == "--selftest2")
                return SelfTest2();

            // ricognizione client: dump finestre/classi dei processi skin
            if (args.Length >= 1 && args[0] == "--scan-client")
                return ScanClient();

            // cattura screenshot dei tavoli rilevati (calibrazione coordinate)
            if (args.Length >= 1 && args[0] == "--snap")
                return SnapTables();

            // nota: dump UIA si fa con tools\dump_uia.ps1 (UIA nativo solo in PowerShell)

            // profilo client (skin) per i tool sopra e per l'app
            LoadClientProfile();

            if (args.Length == 2 && args[0] == "--trace-option-buttons")
                return OptionButtonProbe.Run(args[1], AppDir);

            // editor INI standalone (debug)
            if (args.Length >= 1 && args[0] == "--config")
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new SettingsForm(Path.Combine(AppDir, "PokerCaption.ini")));
                return 0;
            }

            if (args is ["--ui-preview"])
                return PreviewWorkspace();

            ApplicationConfiguration.Initialize();

            // istanza singola (Fase 0)
            bool createdNew;
            using (var mutex = new Mutex(true, "Local\\PokerCaption_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("PokerCaption è già in esecuzione.", "PokerCaption",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 1;
                }

                string iniPath = Path.Combine(AppDir, "PokerCaption.ini");
                if (!File.Exists(iniPath))
                    PokerCaptionConfig.CreateDefault(iniPath).Save();

                Ini = PokerCaptionConfig.Load(iniPath).Raw;

                // profilo client (skin) + seeding sezioni config reali ([HUD], [GenHUD], …):
                // SOLO le sezioni mancanti — le modifiche utente non vengono riscritte
                LoadClientProfile();
                Ini.SeedFrom(ClientProfileSource());
                PokerCaptionConfig.MigrateHudFormats(Ini);

                // session stats cumulative (global_stats, [GStats])
                SessionStats.Current.Load(Path.Combine(AppDir, "global_stats"));

                try
                {
                    using (Shared = new SharedMemory())
                    {
                        Service = new TableService(Shared, MainUiController.Log, diagnosticHookMode);
                        Service.MakeDefault();
                        Service.ConfigDir = Path.Combine(AppDir, "TableManagerConfigs");
                        TableManagerConfig.SeedProfiles(Service.ConfigDir);
                        Service.Load(Ini);

                        var ui = new MainUiController();
                        ui.Build();

                        Application.Run(ui.Form);
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText(Path.Combine(AppDir, "main_log.txt"), "EX " + ex + "\r\n");
                    return 4;
                }
            }
            return 0;
        }

        private static int PreviewWorkspace()
        {
            ApplicationConfiguration.Initialize();
            Ini = new IniFile(null);
            Ini.SeedFrom(ClientProfileSource());
            PokerCaptionConfig.MigrateHudFormats(Ini);
            using var service = new TableService(null, MainUiController.Log);
            Service = service;
            using var controller = new MainUiController();
            service.ConfigDir = Path.Combine(AppDir, "TableManagerConfigs");
            service.Load(Ini);
            controller.Build(connectToClient: false);
            Application.Run(controller.Form);
            return 0;
        }

        private static void LoadClientProfile()
        {
            ClientProfile.Load(ClientProfileSource());
            var iniPath = Path.Combine(AppDir, "PokerCaption.ini");
            if (File.Exists(iniPath)) ClientProfile.Load(IniFile.Load(iniPath));
            var overridePath = Path.Combine(AppDir, "client_profile.ini");
            if (File.Exists(overridePath)) ClientProfile.Load(IniFile.Load(overridePath));
        }

        private static IniFile ClientProfileSource()
        {
            var defaults = new IniFile(null);
            defaults.Set("Client", "Interaction", "coords");
            defaults.Set("Client", "ProcessNames", "Sisal.exe;POKERSTARS.EXE;POKERSTARSONBETFAIR.EXE");
            defaults.Set("Client", "RenderTableClasses", "GLFW30");
            defaults.Set("Client", "FrameClass", "ItaFlutterOnlinePokerLobbyWindowClass");
            defaults.Set("Client", "ListClass", "FlutterOnlinePokerListWndClass");
            defaults.Set("Client", "ToolTipClass", "FlutterOnlinePokerToolTipClass");
            defaults.Set("HUD", "HudActive", "1");
            defaults.Set("HUD", "FormatStr0", "Pot [pot_bb] BB");
            defaults.Set("HUD", "plFormatStr0", "[actions]");
            defaults.Set("GenHUD", "active", "1");
            defaults.Set("GenHUD", "Count", 2);
            defaults.Set("GenHUD", "formatStr0", "Time [sessionTime]");
            defaults.Set("GenHUD", "formatStr1", "Hands [allHandsCount]");
            defaults.Set("GenHUD", "top", 95);
            defaults.Set("AdvancedBets", "Count", 0);

            var profilePath = Path.Combine(AppDir, "client_profile.ini");
            if (!File.Exists(profilePath)) return defaults;
            var profile = IniFile.Load(profilePath);
            profile.SeedFrom(defaults);
            return profile;
        }

        private static int SnapTables()
        {
            var outDir = Path.Combine(AppDir, "tablesnap");
            Directory.CreateDirectory(outDir);
            var tables = TableScanner.ScanTables();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"tavoli rilevati: {tables.Count} (interazione={ClientProfile.Interaction}, classe tavolo={ClientProfile.TableClass})");
            int n = 0;
            foreach (var t in tables)
            {
                using var bmp = CoordsEngine.Capture(t.Hwnd);
                if (bmp == null) { sb.AppendLine($"0x{t.Hwnd.ToInt64():X} '{t.Title}' — cattura fallita"); continue; }
                var safe = string.Concat(System.Linq.Enumerable.Where(t.Title, c => char.IsLetterOrDigit(c) || c == '_')).Truncate(40);
                if (string.IsNullOrEmpty(safe)) safe = $"table{n}";
                var path = Path.Combine(outDir, $"snap_{DateTime.Now:HHmmss}_{safe}.png");
                CoordsEngine.SavePng(bmp, path);
                sb.AppendLine($"0x{t.Hwnd.ToInt64():X} [{t.MaxSeats}max {t.Sb}/{t.Bb}] '{t.Title}' -> {path} ({bmp.Width}x{bmp.Height})");
                n++;
            }
            var outPath = Path.Combine(AppDir, "snap_result.txt");
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine(sb.ToString());
            return n == 0 ? 1 : 0;
        }

        private static int ScanClient()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== finestre dei processi client (sisal/pokerstars/betfair) ===");
            sb.AppendLine("formato: hwnd | classe | titolo | pid | exe");
            sb.AppendLine();

            var byPid = new Dictionary<uint, List<(IntPtr hwnd, string cls, string title)>>();
            var exeByPid = new Dictionary<uint, string>();

            NativeMethods.EnumWindows((h, l) =>
            {
                NativeMethods.GetWindowThreadProcessId(h, out var pid);
                if (pid == 0) return true;
                var clsSb = new System.Text.StringBuilder(128);
                NativeMethods.GetClassNameW(h, clsSb, 128);
                var titleSb = new System.Text.StringBuilder(300);
                NativeMethods.GetWindowTextW(h, titleSb, 300);
                if (!byPid.TryGetValue(pid, out var list)) { list = new List<(IntPtr, string, string)>(); byPid[pid] = list; }
                list.Add((h, clsSb.ToString(), titleSb.ToString()));
                if (!exeByPid.ContainsKey(pid))
                {
                    var hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                    if (hProc != IntPtr.Zero)
                    {
                        var psb = new System.Text.StringBuilder(1024);
                        uint size = 1024;
                        if (NativeMethods.QueryFullProcessImageNameW(hProc, 0, psb, ref size))
                            exeByPid[pid] = psb.ToString();
                        NativeMethods.CloseHandle(hProc);
                    }
                    else exeByPid[pid] = "(accesso negato)";
                }
                return true;
            }, IntPtr.Zero);

            var hints = new[] { "sisal", "pokerstars", "betfair", "poker" };
            var matched = byPid.Where(kv =>
            {
                var exe = exeByPid.TryGetValue(kv.Key, out var e) ? e.ToLowerInvariant() : "";
                return hints.Any(h => exe.Contains(h));
            }).ToList();
            if (matched.Count == 0)
            {
                sb.AppendLine("(nessun processo con nome sisal/pokerstars/betfair trovato)");
                sb.AppendLine("=== tutti i processi con finestre visibili (primi 40) ===");
                matched = byPid.Where(kv => kv.Value.Any(w => w.cls.StartsWith("#32770") || w.cls.ToLowerInvariant().Contains("poker") || w.cls.ToLowerInvariant().Contains("stars")))
                    .Take(40).ToList();
                if (matched.Count == 0) matched = byPid.Take(40).ToList();
            }

            foreach (var kv in matched)
            {
                sb.AppendLine($"PID {kv.Key}  exe={exeByPid.GetValueOrDefault(kv.Key, "?")}");
                foreach (var w in kv.Value.Where(w => w.title.Length > 0 || !w.cls.StartsWith("Windows")))
                    sb.AppendLine($"  0x{w.hwnd.ToInt64():X}  [{w.cls}]  '{w.title}'");
                sb.AppendLine();
            }

            var outPath = Path.Combine(AppDir, "scan_client.txt");
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine("scritto " + outPath);
            return 0;
        }

        private static int SelfTest2()
        {
            var sb = new System.Text.StringBuilder();
            void Check(string name, bool ok, string info = "")
                => sb.AppendLine((ok ? "OK   " : "FAIL ") + name + (info.Length > 0 ? "  " + info : ""));

            // --- formula evaluator (Fase 5) ---
            var ctx = new FormulaContext { Pot = 10, Stack = 50, Bet = 2, Raise = 4, Bb = 1, Sb = 0.5 };
            var r = FormulaEval.Evaluate("pot*2", ctx);
            Check("formula pot*2", Math.Abs(r.Value - 20) < 1e-9, "= " + FormulaEval.Format(r.Value, r.Decimals));
            r = FormulaEval.Evaluate("pot*0.33", ctx);
            Check("formula pot*0.33", Math.Abs(r.Value - 3.3) < 1e-9);
            r = FormulaEval.Evaluate("0.33*pot", ctx);
            Check("formula 0.33*pot", Math.Abs(r.Value - 3.3) < 1e-9);
            r = FormulaEval.Evaluate("(pot-bet)*0.5", ctx);
            Check("formula (pot-bet)*0.5", Math.Abs(r.Value - 4) < 1e-9);
            r = FormulaEval.Evaluate("2.5bb", ctx);
            Check("formula 2.5bb", Math.Abs(r.Value - 2.5) < 1e-9);
            r = FormulaEval.Evaluate("pot*0.75bb@2", ctx);
            Check("formula pot*0.75bb@2", Math.Abs(r.Value - 7.5) < 1e-9 && r.Decimals == 2);
            r = FormulaEval.Evaluate("stack@H", ctx);
            Check("formula stack@H", Math.Abs(r.Value - 50) < 1e-9 && r.Hide);
            r = FormulaEval.Evaluate("potx0.5", ctx);
            Check("formula potx0.5", Math.Abs(r.Value - 5) < 1e-9);
            r = FormulaEval.Evaluate("pot+", ctx);
            Check("formula errata rifiutata", r.Error != null);

            // --- parsing titoli (Fasi 3-4) ---
            var t = new TableInfo();
            t.Title = "Table 'Almeria II' 6-max No Limit Hold'em $0.01/$0.02 - $0.02 ante";
            TitleParser.Apply(t);
            Check("titolo cash", t.TableName == "Almeria II" && t.MaxSeats == 6 && Math.Abs(t.Bb - 0.02) < 1e-9 && t.GameType == "NLHE");
            t = new TableInfo();
            t.Title = "Table '184825012244 21' 9-max Seat 3 is the button, Level III (100/200)";
            TitleParser.Apply(t);
            Check("titolo torneo", t.IsTourney && t.MaxSeats == 9 && Math.Abs(t.Bb - 200) < 1e-9 && t.Sb == 100);
            t = new TableInfo();
            t.Title = "Menkab V - No Limit 6+ Hold'em button blind€250 ante€250 Play Money - Logged In as SamplePlayer";
            TitleParser.Apply(t);
            Check("titolo sisal", t.TableName == "Menkab V" && t.Bb == 250 && t.Ante == 250 && t.HeroNick == "SamplePlayer" && t.ShortDeck && t.IsPlayMoney);
            t = new TableInfo { Title = "Hercules VII - Limit Hold'em 500/1,000 Play Money - Logged In as SamplePlayer" };
            TitleParser.Apply(t);
            Check("titolo Sisal Limit Hold'em", t is
                { TableName: "Hercules VII", GameType: "FLHE", Sb: 500, Bb: 1000, IsPlayMoney: true, IsTourney: false });

            // --- regole [AdvancedBets] ---
            var tmp = Path.Combine(Path.GetTempPath(), "sc_adv.ini");
            File.WriteAllText(tmp, "[AdvancedBets]\r\nCount=2\r\nStreets0=1\r\nFormula0=pot*2\r\nStreets1=8\r\nFormula1=pot*1.5\r\n");
            var bs = new BetScriptEngine();
            bs.Load(IniFile.Load(tmp));
            Check("regole caricate", bs.Rules.Count == 2);
            Check("formula preflop", bs.FormulaFor(1) == "pot*2");
            Check("formula river", bs.FormulaFor(8) == "pot*1.5");
            Check("formula flop assente", bs.FormulaFor(2) == null);
            BetScriptSelfTest.Run(Check);
            BetScriptSelfTest.CheckPrefill(Check);
            var sitOutTable = new TableInfo { TableName = "Local cash", HeroNick = "Hero" };
            const string cashHistory = "Sisal Hand #1:\nTable 'Local cash' 6-max\nHero has timed out\n*** SUMMARY ***\n";
            const string voluntaryHistory = "Sisal Hand #2:\nTable 'Local cash' 6-max\nHero: folds\nOther has timed out\n*** SUMMARY ***\n";
            Check("Never sit out distinguishes the local player's timeout from a voluntary pause or another player's timeout",
                NeverSitOut.LatestCompletedHandTimedOut(cashHistory, sitOutTable) == true &&
                NeverSitOut.LatestCompletedHandTimedOut(cashHistory + voluntaryHistory, sitOutTable) == false &&
                NeverSitOut.LatestCompletedHandTimedOut(cashHistory, new TableInfo { TableName = "Different", HeroNick = "Hero" }) == null);
            Check("Never sit out recognizes tournament and table numbers in hand history",
                NeverSitOut.LatestCompletedHandTimedOut(cashHistory.Replace("Local cash", "321 4"),
                    new TableInfo { Title = "Daily - Tournament 321 Table 4", HeroNick = "Hero" }) == true);
            var sitOutTracker = new NeverSitOut();
            var now = DateTime.UtcNow;
            bool skippedExisting = !sitOutTracker.ShouldResume(true, false, now, _ => true);
            sitOutTracker.ShouldResume(false, true, now.AddSeconds(1), _ => false);
            bool skippedVoluntary = !sitOutTracker.ShouldResume(true, false, now.AddSeconds(2), _ => false);
            sitOutTracker.ShouldResume(false, true, now.AddSeconds(3), _ => false);
            bool timeoutAccepted = sitOutTracker.ShouldResume(true, false, now.AddSeconds(4), since => since == now.AddSeconds(3));
            sitOutTracker.MarkResumed();
            Check("Never sit out requires an observed turn and a recent timeout and attempts once per sit-out",
                skippedExisting && skippedVoluntary && timeoutAccepted &&
                !sitOutTracker.ShouldResume(true, false, now.AddSeconds(7), _ => true));
            var earlyTracker = new NeverSitOut();
            var voluntaryTracker = new NeverSitOut();
            var changedInputTracker = new NeverSitOut();
            foreach (var tracker in new[] { earlyTracker, voluntaryTracker, changedInputTracker })
                tracker.ShouldResume(false, true, now, _ => false, intent: SitOutIntent.HandClear,
                    heroDecision: true, inputTick: 42);
            voluntaryTracker.ShouldResume(false, true, now.AddMilliseconds(100), _ => false,
                intent: SitOutIntent.HandSelected, heroDecision: true, inputTick: 43);
            Check("Never sit out accepts a quick transition with no user input and preserves voluntary sit-outs",
                earlyTracker.ShouldResume(true, false, now.AddMilliseconds(200), _ => false, inputTick: 42) &&
                earlyTracker.EarlySignal &&
                !voluntaryTracker.ShouldResume(true, false, now.AddMilliseconds(200), _ => true, inputTick: 43) &&
                !changedInputTracker.ShouldResume(true, false, now.AddMilliseconds(200), _ => false, inputTick: 43));

            var otherSitOutTracker = new NeverSitOut();
            sitOutTracker.ResetObservation();
            sitOutTracker.ShouldResume(false, true, now.AddSeconds(8), _ => false);
            otherSitOutTracker.ShouldResume(false, true, now.AddSeconds(8), _ => false);
            Check("Never sit out enforces a separate maximum for each table and preserves attempts across reconfiguration",
                !sitOutTracker.ShouldResume(true, false, now.AddSeconds(9), _ => true, maximum: 1) &&
                otherSitOutTracker.ShouldResume(true, false, now.AddSeconds(9), _ => true, maximum: 1) &&
                sitOutTracker.ShouldResume(true, false, now.AddSeconds(10), _ => true, maximum: 2) &&
                sitOutTracker.Attempts == 1 && otherSitOutTracker.Attempts == 0);

            TableManagerSelfTest.Run(Check);

            // --- binding map (Fase B) ---
            var map = UiBinding.Settings;
            Check("desktop settings bind to unique controls", map.Count == 4 && map.Select(item => item.Control).Distinct().Count() == map.Count);

            // --- tag HUD (Fase 7) ---
            var tc = new TagContext
            {
                Table = new TableInfo { Pot = 12, Stack = 50, Bet = 3, AmountToCall = 2, Bb = 1, Sb = 0.5, Street = 1, HeroAct = true },
                Tables = new List<TableInfo>(),
                Session = SessionStats.Current,
                Rng = new Random(1)
            };
            var rv = HudTags.Resolve("[effSPR]", tc);
            Check("tag [effSPR]", rv == "4.17", "= " + rv); // 50/12: SPR sullo stack effettivo
            var tp = HudTags.Resolve("[pot_bb]", new TagContext { Table = new TableInfo { Pot = 10, Bb = 2 } });
            Check("tag [pot_bb]", tp == "5", "= " + tp);
            var legacyHud = new IniFile(null);
            const string retired = "[totalPot_bb][totalPot][toCall][seatStacks_bb][:][BB][SB][capt]";
            legacyHud.Set("HUD", "FormatStr0", retired + "[toCall_bb][%]");
            legacyHud.Set("HUD", "plFormatStr0", "[actions][TIMING]");
            legacyHud.Set("GenHUD", "formatStr0", "[sessionTime][timing]");
            legacyHud.Set("HUD", "actionsAddTimings", 1);
            PokerCaptionConfig.MigrateHudFormats(legacyHud);
            Check("Retired HUD tags no longer resolve and saved table formats migrate", HudTags.Resolve(retired, tc) == retired &&
                HudTags.Resolve("[timing]", tc) == "[timing]" && !HudTags.UsesActionHistory("[timing]") &&
                legacyHud.Get("HUD", "FormatStr0") == "[pot_bb][pot][toCall_bb][%]" &&
                legacyHud.Get("HUD", "plFormatStr0") == "[actions]" &&
                legacyHud.Get("GenHUD", "formatStr0") == "[sessionTime]" &&
                legacyHud.GetBool("HUD", "actionsAddTimings", false));
            Check("tag condizione heroAct", HudTags.Visible("[heroAct]", tc) && !HudTags.Visible("[heroAct]", new TagContext { Table = new TableInfo() }));
            Check("tag sconosciuto passthrough", HudTags.Resolve("[tagInesistente]", tc) == "[tagInesistente]");
            Check("tag [sessionTime]", HudTags.Resolve("[sessionTime]", new TagContext { Session = SessionStats.Current }).Contains(":"));
            var savedSession = SessionStats.Current;
            SessionStats.Current = new SessionStats();
            try
            {
                using var tableService = new TableService(null, null);
                var table = new TableInfo();
                int dealt = tableService.Pixels.MinHeroPixels;
                tableService.ObserveHeroSample(table, dealt);
                Check("General HUD waits for two visible hero samples", SessionStats.Current.SessionHands == 0);
                tableService.ObserveHeroSample(table, dealt);
                Check("General HUD counts a hand from visible hero samples",
                    table.HeroInHand && SessionStats.Current.SessionHands == 1 && SessionStats.Current.AllHands == 1);
                tableService.ObserveHeroSample(table, -1);
                Check("General HUD marks an obscured sample without ending the hand", table.HeroSampleUnavailable && table.HeroInHand);
                tableService.ObserveHeroSample(table, dealt);
                Check("General HUD avoids counting the same hand after uncovering the table",
                    !table.HeroSampleUnavailable && SessionStats.Current.SessionHands == 1);
                for (int i = 0; i < 3; i++) tableService.ObserveHeroSample(table, 0);
                Check("General HUD clears the hand after three visible negative samples", !table.HeroInHand);

                var stats = SessionStats.Current;
                stats.RunActiveSeconds = 30;
                stats.Tick(inHand: false);
                Check("Hands per hour stays unchanged when the clock advances without a hand", stats.HandsPerHour == 0);
                tableService.ObserveHeroSample(table, dealt);
                tableService.ObserveHeroSample(table, dealt);
                Check("Hands per hour updates on the next hand even before 72 seconds", stats.SessionHands == 2 && stats.HandsPerHour == 240);
                stats.RunActiveSeconds = 120;
                stats.Tick(inHand: false);
                Check("Hands per hour keeps the last recorded rate between hands", stats.HandsPerHour == 240);
                stats.ResetSessionHands();
                Check("Resetting session hands clears the cached rate and preserves the cumulative hands", stats.HandsPerHour == 0 && stats.AllHands == 2);
            }
            finally { SessionStats.Current = savedSession; }
            // pot odds (definizioni originali): call=2, pot=12, stack=50
            Check("tag [%] pot odds call", HudTags.Resolve("[%]", tc) == "14.3", "= " + HudTags.Resolve("[%]", tc));      // 2/14*100
            Check("tag [toCall_bb] independent of raise size", HudTags.Resolve("[toCall_bb]", tc) == "2");
            var noCall = new TagContext { Table = new TableInfo { Pot = 12, Bet = 3, Bb = 1 } };
            Check("missing call keeps raise size out of odds", HudTags.Resolve("[toCall_bb] [%]", noCall) == "- -");
            tc.Table.SeatStacks[3] = 41.5;
            tc.Table.SeatActions[3] = "Call";
            var player = new TagContext { Table = tc.Table, SeatNumber = 3 };
            var observed = tc.Table.ActionHistory;
            var missing = new Dictionary<int, string>();
            var anchors = new Dictionary<int, Rectangle> { [3] = new Rectangle(10, 20, 100, 20) };
            observed.Observe(1, 0, 4, missing);
            foreach (int cards in new[] { 3, 4, 5 })
            {
                tc.Table.SeatActions[3] = "Bet";
                observed.Observe(1, cards, 10 + cards, tc.Table.SeatActions, anchors);
                observed.Observe(1, cards, 10 + cards, tc.Table.SeatActions, anchors);
                observed.Observe(1, cards, 10 + cards, missing);
            }
            observed.Observe(2, 4, 15, missing);
            observed.Observe(2, 0, 0, missing);
            Check("Player HUD keeps B|B|B and its seat anchor through showdown",
                HudTags.Resolve("[stack_bb] [actions] [action]", player) == "41.5 B|B|B B|B|B" &&
                observed.MissingAnchors(new Dictionary<int, Rectangle>()).Single().Value == anchors[3]);
            var symbols = new IniFile("");
            symbols.Set("HUD", "actionsSymBet", "BET");
            symbols.Set("HUD", "actionsSymStreetdivider", "/");
            Check("Player HUD uses configured action symbols and divider", observed.Format(3, symbols) == "BET/BET/BET");
            observed.Observe(2, 0, 4, missing);
            Check("Player HUD clears history and anchors at next deal",
                observed.Format(3, symbols) == "" && !observed.MissingAnchors(new Dictionary<int, Rectangle>()).Any());
            static SharedMemory.SeatActionEvent[] UntimedActions(params (int seat, int cards, int code)[] actions) =>
                actions.Select(action => new SharedMemory.SeatActionEvent(action.seat, action.cards, action.code)).ToArray();
            var buffered = new SeatActionHistory();
            buffered.Observe(1, 5, 24, missing, anchors,
                UntimedActions((3, 0, 3), (3, 3, 2), (3, 3, 3), (3, 4, 4), (3, 5, 4), (5, 5, 1)));
            buffered.Observe(1, 0, 0, missing);
            Check("Player HUD drains rapid native events in order and retains showdown history",
                buffered.Format(3, null) == "C|XC|B|B" && buffered.Format(5, null) == "F");
            buffered.Observe(1, 5, 24, missing, anchors, UntimedActions((5, 5, 1), (3, 5, 4)));
            Check("Player HUD ignores duplicate fold and bet updates on the same street",
                buffered.Format(3, null) == "C|XC|B|B" && buffered.Format(5, null) == "F");
            buffered.Observe(1, 0, 0, missing, anchors, UntimedActions((3, 0, 5)));
            buffered.Observe(1, 5, 24, missing, anchors, Array.Empty<SharedMemory.SeatActionEvent>());
            Check("Player HUD keeps history when the table briefly blanks mid-hand",
                buffered.Format(3, null) == "C|XC|B|B");
            buffered.Observe(1, 0, 0, missing, anchors, UntimedActions((3, 0, 8)));
            buffered.Observe(2, 0, 5, missing, anchors, UntimedActions((3, 0, 5)));
            Check("Player HUD keeps first queued action of the new hand without carrying over the old one",
                buffered.Format(3, null) == "PR" && buffered.Format(5, null) == "");
            var limps = new SeatActionHistory();
            limps.Observe(1, 0, 5, missing, events: UntimedActions((3, 0, 6), (3, 0, 6), (5, 0, 6)));
            Check("Player HUD distinguishes the first limp from overlimps", limps.Format(3, null) == "L" && limps.Format(5, null) == "OL");
            var limpSymbols = new IniFile("");
            limpSymbols.Set("HUD", "actionsSym1stimp", "FIRST");
            limpSymbols.Set("HUD", "actionsSymLimp", "OVER");
            limps.Observe(1, 0, 10, missing, events: UntimedActions((3, 0, 5), (5, 0, 3)));
            Check("Player HUD joins configured action symbols without spaces",
                limps.Format(3, limpSymbols) == "FIRSTR" && limps.Format(5, limpSymbols) == "OVERC");
            limps.Observe(2, 0, 3, missing, events: UntimedActions((5, 0, 6)));
            Check("Player HUD resets limp order for the next hand",
                limps.Format(5, null) == "L" && limps.Format(3, null) == "");
            using (var actionsDialog = new ActionsTagConfigForm(limpSymbols, null))
            {
                var sections = actionsDialog.Controls.OfType<GroupBox>().ToArray();
                var grid = sections.Single(section => section.Text == "Action symbols").Controls.OfType<DataGridView>().Single();
                Check("Actions settings show Overlimp with its saved symbol and omit Sizings",
                    grid.Rows.Cast<DataGridViewRow>().Any(row => Equals(row.Cells["action"].Value, "Overlimp") &&
                        Equals(row.Cells["symbol"].Value, "OVER")) &&
                    sections.All(section => section.Text != "Sizings"));
                Check("Actions settings have no standalone checkbox", !actionsDialog.Controls.OfType<CheckBox>().Any());
            }
            unsafe
            {
                Span<byte> slot = stackalloc byte[(int)SharedLayout.SLOT_PITCH];
                slot.Clear();
                fixed (byte* p = slot)
                {
                    *(int*)(p + 0x27DC) = 1;
                    *(int*)(p + 0x27D4) = 2;
                    *(int*)(p + 0x27E0) = 1;
                    *(int*)(p + 0x27E4) = (3 << 16) | 3;
                    *(int*)(p + 0x27E8) = 2;
                    *(int*)(p + 0x27EC) = (5 << 16) | 5;
                    *(uint*)(p + 0x254C) = uint.MaxValue;
                    *(uint*)(p + 0x2550) = 0;
                    *(int*)(p + 0x264C) = 1;
                    *(uint*)(p + 0x2650) = 12;
                    int last = 0;
                    var timedBatch = SharedMemory.ReadSeatActionEvents(p, ref last, out bool dropped, out uint? snapshot);
                    Check("Player HUD reads action timestamps and snapshot from the native ring",
                        !dropped && last == 2 && snapshot == 12 && timedBatch.SequenceEqual(
                            new SharedMemory.SeatActionEvent[] { new(3, 0, 3, uint.MaxValue, 1), new(5, 0, 5, 0, 2) }));

                    *(int*)(p + 0x264C) = 0;
                    last = 0;
                    var legacyBatch = SharedMemory.ReadSeatActionEvents(p, ref last, out dropped, out snapshot);
                    Check("Player HUD legacy native ring keeps actions without timestamps",
                        !dropped && snapshot is null && legacyBatch.SequenceEqual(
                            new SharedMemory.SeatActionEvent[] { new(3, 0, 3, null, 1), new(5, 0, 5, null, 2) }));
                }
            }
            var timedTable = new TableInfo();
            var timed = timedTable.ActionHistory;
            var timingIni = new IniFile("");
            timingIni.Set("HUD", "actionsAddTimings", "1");
            var combined = new SeatActionHistory();
            combined.Observe(1, 0, 2, missing, events: [], snapshotTick: 900);
            combined.Observe(1, 3, 4, missing, events: [], snapshotTick: 1000);
            var visibleActions = new Dictionary<int, string> { [3] = "Call", [4] = "Raise" };
            combined.Observe(1, 3, 8, visibleActions, events: [new(3, 3, 3, 1180, 1)]);
            Check("HUD fills missing actions from visible labels without duplicating captured events",
                combined.Format(3, timingIni) == "C 0.2s" && combined.Format(4, timingIni) == "R");
            combined.Observe(1, 3, 8, visibleActions, events: [new(4, 3, 5, 1260, 2)]);
            Check("HUD adds the captured timing to an existing visible action", combined.Format(4, timingIni) == "R 0.1s");
            timed.Observe(1, 0, 5, missing, events: new SharedMemory.SeatActionEvent[]
            {
                new(3, 0, 8, 1200, 1), new(3, 0, 3, 2440, 2), new(5, 0, 5, 2750, 3)
            });
            Check("Player HUD times forced posts and actions from the same batch",
                timed.Format(3, timingIni) == "P 0sC 1.2s" && timed.Format(5, timingIni) == "R 0.3s" &&
                timed.Format(3, null) == "PC");
            timedTable.HeroSeat = 3;
            timedTable.SeatStacks[3] = 42;
            var previousIni = Ini;
            try
            {
                Ini = timingIni;
                Check("Player HUD shows action durations only for opponents with Add timings enabled",
                    HudTags.Resolve("[stack] [actions]", new TagContext { Table = timedTable, SeatNumber = 3 }) == "42 " &&
                    HudTags.Resolve("[actions]", new TagContext { Table = timedTable, SeatNumber = 5 }) == "R 0.3s");
            }
            finally { Ini = previousIni; }
            timed.Observe(1, 3, 15, missing, events: Array.Empty<SharedMemory.SeatActionEvent>(), snapshotTick: 5000);
            timed.Observe(1, 3, 20, missing, events: new SharedMemory.SeatActionEvent[]
            {
                new(3, 3, 2, 5520, 4), new(5, 3, 4, 6200, 6), new(3, 3, 1, 6630, 7)
            });
            Check("Player HUD times street resets and recovers after a missing event",
                timed.Format(3, timingIni) == "P 0sC 1.2s|X 0.5sF 0.4s" &&
                timed.Format(5, timingIni) == "R 0.3s|B");
            timed.Observe(2, 0, 0, missing, events: new SharedMemory.SeatActionEvent[] { new(5, 0, 8, 7000, 8) });
            timed.Observe(2, 0, 5, missing, events: new SharedMemory.SeatActionEvent[] { new(3, 0, 3, 7550, 9) });
            Check("Player HUD preserves timing for buffered actions of a new hand",
                timed.Format(5, timingIni) == "P 0s" && timed.Format(3, timingIni) == "C 0.6s");
            timed.Observe(2, 3, 10, missing, events: new SharedMemory.SeatActionEvent[] { new(3, 3, 4, 8100, 10) });
            Check("Player HUD omits duration if the new street was first seen with an action",
                timed.Format(3, timingIni) == "C 0.6s|B");
            player.SeatNumber = 2;
            Check("Player HUD omits missing seat values", HudTags.Resolve("[stack_bb][actions]", player) == "");
            Check("Add tag player catalog is seat-specific",
                MainUiController.AvailableHudTags("plFormatStr").Select(s => s.Split(' ')[0])
                    .SequenceEqual(new[] { "[actions]" }));
            Check("tag [p%] pot odds push", HudTags.Resolve("[p%]", tc) == "80.6", "= " + HudTags.Resolve("[p%]", tc));   // 50/62*100
            Check("HUD tag catalog removes General HUD RNG and keeps the supported table options",
                !MainUiController.AvailableHudTags("formatStr").Any(tag => tag.StartsWith("[RNG]", StringComparison.Ordinal)) &&
                MainUiController.AvailableHudTags("FormatStr").Select(tag => tag.Split(' ')[0])
                    .SequenceEqual(["[toCall_bb]", "[%]", "[RNG]"]));

            // --- pannelli HUD (MainUi.HudPanels): scrittura UI ↔ lettura motori ---
            var tmpPanels = Path.Combine(Path.GetTempPath(), "sc_panels.ini");
            File.WriteAllText(tmpPanels,
                "[HUD]\r\nFormatStr0=[effSPR]\r\nFormatStr1=Odds:[%]\r\n" +
                "plFormatStr0=[actions]\r\nplFVisible0=1\r\n" +
                "[GenHUD]\r\nCount=4\r\nformatStr0=Session\\stime\r\nformatStr1=[sessionTime]\r\nformatStr2=Hands\r\nformatStr3=[allHandsCount]\r\nactive=1\r\n");
            var pin = IniFile.Load(tmpPanels);

            // Add new panel (General HUD): coda + Count
            var gsec = pin.GetOrAddSection("GenHUD");
            int gn = pin.GetInt("GenHUD", "Count", 0);
            gsec["formatStr" + gn] = "";
            gsec["FColor" + gn] = "16777215"; gsec["FBkColor" + gn] = "4210752";
            gsec["FSize" + gn] = "9";
            gsec["Count"] = (gn + 1).ToString(CultureInfo.InvariantCulture);
            using var gen = new GenHud(_ => { }); gen.Load(pin);
            Check("GenHUD add -> engine items", gen.Items.Count == 5, gen.Items.Count + " item");

            // Delete panel (General HUD, indice 2) con reindicizzazione: Hands esce, [allHandsCount] sale a 2
            int rIdx = 2;
            gsec.Entries.RemoveAll(e => System.Text.RegularExpressions.Regex.IsMatch(e.Key, "^(formatStr|FColor|FBkColor|FSize)" + rIdx + "$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            foreach (var kv in gsec.Entries.ToList())
            {
                var m = System.Text.RegularExpressions.Regex.Match(kv.Key, "^(formatStr|FColor|FBkColor|FSize)(\\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success && int.Parse(m.Groups[2].Value) > rIdx)
                    gsec[m.Groups[1].Value + (int.Parse(m.Groups[2].Value) - 1).ToString()] = kv.Value;
            }
            gsec["Count"] = "3";
            gen.Load(pin);
            Check("GenHUD delete + reindex", gen.Items.Count == 3 && gen.Items[2].Format == "[allHandsCount]",
                gen.Items.Count + " item, #3='" + (gen.Items.Count > 2 ? gen.Items[2].Format : "?") + "'");
            gsec["FVisible0"] = "0";
            gsec["FInvisibleBack0"] = "1";
            gsec["FBkColor0"] = "-1";
            gen.Load(pin);
            gen.OnTick();
            var generalWindow = Application.OpenForms.OfType<GenHudWindow>().SingleOrDefault();
            Check("General HUD renders every item with an opaque background despite legacy options",
                generalWindow?.Boxes.Count == 3 && generalWindow.Boxes[0].Back == Color.FromArgb(64, 64, 64));
            gen.Dispose();

            // Add new panel (Table panels): FormatStr2 senza Count → il motore lo vede
            var hsec = pin.GetOrAddSection("HUD");
            hsec["FormatStr2"] = "";
            var hudE = new HudEngine(_ => { }); hudE.Load(pin);
            Check("HUD add pannello tabella", hudE.Panels.Count(p => !p.PlayerPanel) == 3 && hudE.Panels.Count(p => p.PlayerPanel) == 1,
                hudE.Panels.Count + " pannelli");
            Check("HUD pannello vuoto Visible()", HudTags.Visible("", new TagContext()));
            var playerPanel = hudE.Panels.Single(p => p.PlayerPanel);
            var anchor = new Point(300, 200);
            var moved = new Point(340, 180);
            var doubleWidth = new TableInfo { Rect = new NativeMethods.RECT { Right = 1584 } };
            hudE.SavePlayerPanelPosition(doubleWidth, 3, playerPanel.Index, anchor, moved);
            hudE.Load(IniFile.Load(tmpPanels));
            playerPanel = hudE.Panels.Single(p => p.PlayerPanel);
            Check("Player HUD restores a saved drag position at the same scale",
                HudEngine.PlayerPanelPosition(playerPanel, 3, anchor, 2) == moved);
            Check("Player HUD drag offsets scale and affect only the dragged seat",
                HudEngine.PlayerPanelPosition(playerPanel, 3, anchor, 1) == new Point(320, 190) &&
                HudEngine.PlayerPanelPosition(playerPanel, 4, anchor, 2) == anchor);
            using (var tableWindow = new Form { ClientSize = new Size(400, 240) })
            using (var dragHud = new HudEngine(null))
            {
                pin.Set("HUD", "plFormatStr0", "Player drag test");
                for (int i = 0; i < 3; i++) pin.Set("HUD", "FVisible" + i, "0");
                dragHud.Load(pin);
                tableWindow.Show();
                Application.DoEvents();
                var testTable = new TableInfo { Hwnd = tableWindow.Handle };
                NativeMethods.GetWindowRect(testTable.Hwnd, out testTable.Rect);
                testTable.SeatBounds[2] = new Rectangle(60, 75, 100, 16);
                void RefreshPlayerPanels() => dragHud.UpdatePlayerPanels(testTable, new TagContext { Table = testTable }, tableVisible: true);
                RefreshPlayerPanels();
                var overlay = Application.OpenForms.OfType<HudTextPanel>().SingleOrDefault(p => p.Visible);
                Check("Player HUD creates an interactive overlay when unlocked", overlay is { Draggable: true });
                using (var coveringWindow = new Form
                {
                    StartPosition = FormStartPosition.Manual,
                    Bounds = new Rectangle(tableWindow.Left + 120, tableWindow.Top + 45, 320, 190)
                })
                {
                    coveringWindow.Show();
                    Application.DoEvents();
                    dragHud.OnTick([testTable]);
                    Check("Player HUD stays visible and owned by its table when the table centre is covered",
                        overlay is { Visible: true, TopMost: false } &&
                        NativeMethods.GetWindowLongW(overlay.Handle, NativeMethods.GWL_HWNDPARENT) == tableWindow.Handle.ToInt32(),
                        $"visible={overlay?.Visible}, topmost={overlay?.TopMost}, owner={NativeMethods.GetWindowLongW(overlay.Handle, NativeMethods.GWL_HWNDPARENT):x}, table={tableWindow.Handle:x}");
                }
                if (overlay != null)
                {
                    var start = overlay.Location;
                    NativeMethods.SendMessageW(overlay.Handle, 0x0201, (IntPtr)1, (IntPtr)0x00020002); // WM_LBUTTONDOWN
                    NativeMethods.SendMessageW(overlay.Handle, 0x0200, (IntPtr)1, (IntPtr)0x00110016); // WM_MOUSEMOVE
                    RefreshPlayerPanels();
                    bool stayedUnderMouse = overlay.Location == new Point(start.X + 20, start.Y + 15);
                    NativeMethods.SendMessageW(overlay.Handle, 0x0202, IntPtr.Zero, (IntPtr)0x00020002); // WM_LBUTTONUP
                    RefreshPlayerPanels();
                    Check("Player HUD keeps drag coordinates through refresh and drop",
                        stayedUnderMouse && overlay.Location == new Point(start.X + 20, start.Y + 15));
                }
                pin.Set("HUD", "LockHUD", "1");
                dragHud.Load(pin);
                RefreshPlayerPanels();
                Check("Player HUD disables drag when Lock HUD is checked",
                    Application.OpenForms.OfType<HudTextPanel>().SingleOrDefault(p => p.Visible) is { Draggable: false });
                var lockedPanel = Application.OpenForms.OfType<HudTextPanel>().SingleOrDefault(p => p.Visible);
                var lockedPosition = lockedPanel?.Location;
                int shortWidth = lockedPanel?.Width ?? 0;
                dragHud.Panels.Single(p => p.PlayerPanel).Format = "C 1.6sC 1.5s|X 6.2s";
                RefreshPlayerPanels();
                Check("Player HUD growing text keeps its locked position", lockedPanel != null && lockedPanel.Width > shortWidth &&
                    lockedPanel.Location == lockedPosition);
            }

            // --- SeedFrom: il profilo non deve riscrivere le sezioni esistenti ---
            var seedBase = new IniFile(Path.Combine(Path.GetTempPath(), "sc_seed.ini"));
            seedBase.Set("HUD", "HudActive", "0");
            seedBase.Set("HUD", "Opacity", "80");
            seedBase.SeedFrom(ClientProfileSource());
            Check("SeedFrom non riscrive [HUD]", seedBase.Get("HUD", "HudActive") == "0" && seedBase.Get("HUD", "Opacity") == "80");
            Check("SeedFrom semina sezioni assenti", seedBase.Get("GenHUD", "Count") != null && seedBase.Get("AdvancedBets", "Count") != null);

            var iniBeforeStartupTest = Ini;
            string uiTestPath = Path.Combine(Path.GetTempPath(), $"poker-caption-ui-{Guid.NewGuid():N}.ini");
            try
            {
                Ini = new IniFile(uiTestPath);
                Ini.Set("PokerCaption", "ActivePage", "Table manager");
                using var startupUi = new MainUiController();
                startupUi.Build(connectToClient: false);
                var form = startupUi.Form;
                Check("PokerCaption opens on HUD even when a legacy startup page is saved",
                    DesktopWindow.GetWorkspace(form).SelectedPage?.Name == "HUD");
                Check("Desktop has no manual Save settings button",
                    form.Controls.Find("saveOptions", true).Length == 0);
                var selector = (ComboBox)form.Controls.Find("uiLanguage", true).Single();
                selector.SelectedIndex = 1;
                bool italian = form.Controls.Find("workspaceNavOptions", true).Single().Text == "Opzioni" &&
                    IniFile.Load(uiTestPath).Get("PokerCaption", "Language") == "it" &&
                    UiLanguage.Text("Formula guide body").StartsWith("OPERATORI", StringComparison.Ordinal);
                var rule = (CheckBox)form.Controls.Find("CheckBox173", true).Single();
                rule.Checked = true;
                bool savedRule = rule.Text == "Chiudi automaticamente i tavoli conclusi" &&
                    IniFile.Load(uiTestPath).GetBool("PokerCaption", "CloseTableHasBeenClosed", false);
                selector.SelectedIndex = 0;
                Check("Language switches the desktop live and the closure toggle keeps its saved value",
                    italian && savedRule && form.Controls.Find("workspaceNavOptions", true).Single().Text == "Options" &&
                    IniFile.Load(uiTestPath).Get("PokerCaption", "Language") == "en");
                var hudToggle = (CheckBox)form.Controls.Find("hudActive", true).Single();
                hudToggle.Checked = false;
                using var autosaveSymbolsDialog = new ActionsTagConfigForm(Ini, null);
                DesktopWindow.Descendants(autosaveSymbolsDialog).OfType<CheckBox>().Single().Checked = true;
                DesktopWindow.Descendants(autosaveSymbolsDialog).OfType<DataGridView>().Single().Rows[0].Cells["symbol"].Value = "FOLD";
                var saved = IniFile.Load(uiTestPath);
                Check("HUD toggles and action symbols autosave without closing either window",
                    !saved.GetBool("HUD", "HudActive", true) && saved.GetBool("HUD", "actionsAddTimings", false) &&
                    saved.Get("HUD", "actionsSymFold") == "FOLD");
            }
            finally
            {
                Ini = iniBeforeStartupTest;
                UiLanguage.Use("en");
                File.Delete(uiTestPath);
                File.Delete(Path.ChangeExtension(uiTestPath, ".ini_bak"));
            }

            var styles = new IniFile(null);
            styles.Set("HUD", "FormatStr0", "Test");
            styles.Set("HUD", "FontSize0", 17);
            styles.Set("HUD", "BackColor0", 0x030201);
            styles.Set("HUD", "plFormatStr0", "Test");
            styles.Set("HUD", "plFontSize0", 14);
            styles.Set("HUD", "plBackColor0", 0x060504);
            using (var styledHud = new HudEngine(null))
            {
                styledHud.Load(styles);
                Check("Table and player HUD load independent font sizes and backgrounds",
                    styledHud.Panels.Single(p => !p.PlayerPanel) is { FontSize: 17 } tableStyle &&
                    tableStyle.Background == Color.FromArgb(1, 2, 3) &&
                    styledHud.Panels.Single(p => p.PlayerPanel) is { FontSize: 14 } playerStyle &&
                    playerStyle.Background == Color.FromArgb(4, 5, 6));
            }

            using (var ui = DesktopWindow.Create())
            {
                var pages = DesktopWindow.GetWorkspace(ui);
                Check("Desktop shell exposes four workspaces", ui.Text == "PokerCaption" && pages.Controls.Count == 4);
                pages.SelectPage(pages.Controls["Table manager"]);
                ui.Opacity = 0;
                ui.ShowInTaskbar = false;
                ui.Show();
                Application.DoEvents();
                Button Navigation(string name) => (Button)ui.Controls.Find("workspaceNav" + name, true).Single();
                bool IsWorkspace(string name, string heading, string controlName) =>
                    pages.SelectedPage.Name == name &&
                    pages.Controls.Cast<Control>().Where(c => c.Visible).SequenceEqual([pages.SelectedPage]) &&
                    pages.SelectedPage.Controls.Find(controlName, true).Single().Visible &&
                    ui.Controls.Find("workspaceHeading", true).Single().Text == heading &&
                    Navigation(name).BackColor != Navigation(name == "HUD" ? "Options" : "HUD").BackColor;
                var layoutPanel = new Panel { Name = "layoutTestContent", Dock = DockStyle.Fill };
                pages.Controls["Table manager"].Controls.Add(layoutPanel);
                bool restoredLayout = IsWorkspace("Table manager", "Table Layout", layoutPanel.Name);
                // WinForms can change its child order independently of sidebar order.
                pages.Controls.SetChildIndex(pages.Controls["Options"], 0);
                Navigation("HUD").PerformClick();
                bool hud = IsWorkspace("HUD", "HUD", "hudActive");
                Navigation("Table manager").PerformClick();
                bool layout = IsWorkspace("Table manager", "Table Layout", layoutPanel.Name);
                Navigation("Betscript").PerformClick();
                bool bets = IsWorkspace("Betscript", "BetScript", "ListBox4");
                Navigation("Options").PerformClick();
                Check("Desktop startup and sidebar keep the heading, highlight and page in sync after z-order changes",
                    restoredLayout && hud && layout && bets && IsWorkspace("Options", "Options", "neverSitOut"));
                Check("General HUD editor hides the retired controls",
                    new[] { "generalHudAlign", "generalHudResetPosition", "generalHudVisible", "generalHudBackInvisible",
                        "generalHudBorderWidth" }.All(name => ui.Controls.Find(name, true).Length == 0));
                Check("HUD editor exposes all three panel families",
                    new[] { "tablePanelsList", "playerPanelsList", "generalHudPanelsList" }
                        .All(name => ui.Controls.Find(name, true).Single() is ListBox));
                var tablePanels = DesktopWindow.Descendants(ui).OfType<TabPage>().Single(page => page.Text == "Table panels");
                Check("Table and player editors have Font and Background and remove the ordering controls",
                    new[] { "tablePanels", "playerPanels" }.All(prefix =>
                        new[] { "Font", "BackColor" }.All(suffix => ui.Controls.Find(prefix + suffix, true).Single() is Button) &&
                        new[] { "MoveUp", "MoveDown", "Clone" }.All(suffix => ui.Controls.Find(prefix + suffix, true).Length == 0)) &&
                    !DesktopWindow.Descendants(tablePanels).Any(c => c.Text is "Frame colour" or "Position" or
                        "Transparent background" or "Rounded" or "Group with previous" or "Padding" or
                        "Frame" or "Fixed width" or "Pixels" or "Horizontal" or "Vertical" or "On click" or "On hover"));
                Check("Desktop surface contains all persisted settings",
                    map.All(item => ui.Controls.Find(item.Control, true).Single() is CheckBox));
                BetScriptSelfTest.CheckEditor(ui, Check);
                MainUiController.CheckOptions(ui, Check);
            }
            using (var picker = DesktopWindow.CreateTagPicker())
                Check("HUD tag picker has filter, list and keyboard buttons",
                    picker.Controls.Find("Edit1", true).Single() is TextBox &&
                    picker.Controls.Find("ListBox1", true).Single() is ListBox &&
                    picker.AcceptButton != null && picker.CancelButton != null);
            using (var actions = new ActionsTagConfigForm(new IniFile(null), null))
            {
                var controls = DesktopWindow.Descendants(actions).ToArray();
                var grid = controls.OfType<DataGridView>().Single();
                Check("Action symbols dialog keeps symbol editing and only the timing toggle", grid.Rows.Count > 0 &&
                    grid.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText).SequenceEqual(["Action", "Symbol"]) &&
                    controls.OfType<CheckBox>().Select(box => box.Text).SequenceEqual(["Add timings"]));
            }
            using (var autoClose = DesktopWindow.Create())
            {
                ((CheckBox)autoClose.Controls.Find("CheckBox344", true).Single()).Checked = true;
                bool clientOpen = false;
                int polls = 0;
                using var timer = MainUiController.WireCaptionAutoClose(autoClose, () => { polls++; return clientOpen; });
                void WaitForPoll(int count)
                {
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    while (polls < count && wait.ElapsedMilliseconds < 1_200)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                }
                WaitForPoll(2);
                bool waitedForFirst = polls >= 2 && !autoClose.IsDisposed;
                clientOpen = true;
                WaitForPoll(3);
                clientOpen = false;
                WaitForPoll(4);
                Check("Auto-close waits to see a client and closes on the first poll without one",
                    waitedForFirst && polls == 4 && autoClose.IsDisposed);
            }

            File.WriteAllText(Path.Combine(AppDir, "selftest2.txt"), sb.ToString());
            Console.Write(sb.ToString());
            return sb.ToString().Contains("FAIL") ? 1 : 0;
        }

        private static string Truncate(this string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);

    }
}
