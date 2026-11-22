using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using PokerCaption.Config;
using PokerCaption.Native;

namespace PokerCaption.Engine
{
    /// <summary>
    /// Servizio tavoli (Fasi 3–6, 8–9, 12): scansione ciclica, slot (max 42),
    /// heroAct via pixel (client renderizzati) o controlli (client classico),
    /// betscript, HUD + GenHUD, auto-dialog, chiusure tavoli e session stats.
    /// </summary>
    public sealed class TableService : IDisposable
    {
        public const int MaxTables = 42;

        private readonly List<TableInfo> _tables = new List<TableInfo>();
        private volatile TableInfo[] _pixelTables = Array.Empty<TableInfo>();
        private readonly SharedMemory _shared;
        private readonly Action<string> _log;
        private readonly SharedMemory.ActionHookMode? _diagnosticHookMode;
        private System.Windows.Forms.Timer _timer;
        private System.Windows.Forms.Timer _hudTimer;
        private long _nextHudScan;
        private long _nextLayoutRefresh;
        private bool _neverSitOut;
        private int? _neverSitOutMaximum;
        private readonly object _captureGate = new();
        private readonly ManualResetEventSlim _pixelWake = new(false);

        public BetScriptEngine Betscript { get; private set; }
        public TableManagerEngine Manager { get; private set; }
        public AutoDialogEngine AutoDialog { get; private set; }
        public HudEngine Hud { get; private set; }
        public GenHud GenHud { get; private set; }
        public PixelWatcher Pixels { get; private set; }

        public IReadOnlyList<TableInfo> Tables => _tables;
        public string ConfigDir { get => Manager.ConfigDir; set => Manager.ConfigDir = value; }

        public double CashBbMultiplier = 1.0, TrnyBbMultiplier = 1.0;
        public int ScanDelaySec = 2;
        public bool TextsPotInBb = true;   // [Sisal] PotInBb: il client mostra pot/stack in BB

        public TableService(SharedMemory shared, Action<string> log, SharedMemory.ActionHookMode? diagnosticHookMode = null)
        {
            _shared = shared;
            _log = log ?? (_ => { });
            _diagnosticHookMode = diagnosticHookMode;
            Betscript = new BetScriptEngine(_log);
            Manager = new TableManagerEngine(_log);
            AutoDialog = new AutoDialogEngine(_log, shared == null ? null : shared.ReadRenderedNotice);
            Hud = new HudEngine(_log);
            GenHud = new GenHud(_log);
            Pixels = new PixelWatcher();
        }

        public void Load(IniFile ini)
        {
            ScanDelaySec = Math.Max(1, ini.GetInt("PokerCaption", "ScanDelay", 2));
            CashBbMultiplier = ini.GetDouble("PokerCaption", "CashBbMultiplier", 1.0);
            TrnyBbMultiplier = ini.GetDouble("PokerCaption", "TrnyBbMultiplier", 1.0);
            TextsPotInBb = ini.GetBool("Sisal", "PotInBb", true);

            Betscript.Load(ini);
            Manager.RenderedClient = ClientProfile.Interaction == "coords";
            Manager.Load(ini);
            SetNeverSitOut(ini);
            AutoDialog.Load(ini);
            Hud.Load(ini);
            GenHud.Load(ini);
            Pixels.Load(ini);
        }


        /// <summary>Avvia il ciclo di scansione (timer UI) + thread pixel.</summary>
        public void Start()
        {
            Stop();
            _timer = new System.Windows.Forms.Timer { Interval = ScanDelaySec * 1000 };
            _timer.Tick += (s, e) => SafeTick();
            _timer.Start();
            _hudTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _hudTimer.Tick += (_, _) =>
            {
                try
                {
                    long now = Environment.TickCount64;
                    if (Manager.Enabled && now >= _nextLayoutRefresh)
                    {
                        _nextLayoutRefresh = now + 100;
                        try
                        {
                            // A capture that started before the toggle may still be in PrintWindow.
                            if (!Manager.LayoutOnly) Manager.OnTick(_tables);
                            else if (Monitor.TryEnter(_captureGate))
                            {
                                try { Manager.OnTick(_tables); }
                                finally { Monitor.Exit(_captureGate); }
                            }
                        }
                        catch (Exception ex) { _log("TM refresh ERR: " + ex.Message); }
                    }
                    Hud.FollowTables(_tables);
                    if (now >= _nextHudScan)
                    {
                        _nextHudScan = Environment.TickCount64 + (Hud.Active || Betscript.Rules.Count > 0 || _neverSitOut ? 200 : ScanDelaySec * 1000);
                        ScanHud();
                    }
                }
                catch (Exception ex) { _log("HUD refresh ERR: " + ex.Message); }
            };
            _hudTimer.Start();
            SafeTick();
            AutoDialog.Start(layoutOnly: Manager.LayoutOnly);

            // pixel-watch su thread dedicato (niente jank UI)
            _pixelRun = true;
            _pixelWake.Reset();
            _pixelThread = new Thread(PixelLoop) { IsBackground = true, Name = "PixelLoop" };
            _pixelThread.Start();

            _log($"engine avviato (scan {ScanDelaySec}s, tavoli={_tables.Count}, solo layout={Manager.LayoutOnly})");
        }

        internal void RefreshClientWork()
        {
            if (_timer == null) return;
            AutoDialog.Start(layoutOnly: Manager.LayoutOnly);
        }

        public void Stop()
        {
            AutoDialog.Stop();
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
            _hudTimer?.Dispose();
            _hudTimer = null;
            _pixelRun = false;
            _pixelWake.Set();
            try { _pixelThread?.Join(1500); } catch { }
            _pixelThread = null;
            foreach (var table in _tables)
                if (table.Slot >= 0) _shared.StopSeatActionCapture(table.Slot);
            Hud.DestroyAll();
            GenHud.Dispose();
        }

        private void SafeTick()
        {
            try { Tick(); } catch (Exception ex) { _log("engine tick ERR: " + ex.Message); }
        }

        private void Tick()
        {
            var live = TableScanner.ScanTables();

            for (int i = _tables.Count - 1; i >= 0; i--)
                if (!NativeMethods.IsWindow(_tables[i].Hwnd) || !live.Any(x => x.Hwnd == _tables[i].Hwnd))
                {
                    _shared.SetTableHwnd(_tables[i].Slot, IntPtr.Zero);
                    _log("tavolo chiuso: " + _tables[i].TableName);
                    _tables.RemoveAt(i);
                }
            foreach (var lt in live)
            {
                var t = _tables.FirstOrDefault(x => x.Hwnd == lt.Hwnd);
                if (t == null)
                {
                    // dedup finestre gemelle: il client Sisal crea due finestre GLFW
                    // per lo stesso tavolo ("X" e "Table X"). Se il nome normalizzato
                    // coincide con un tavolo già tracciato, ignora la seconda.
                    string Norm(string s) => System.Text.RegularExpressions.Regex.Replace(
                        s.Trim(), @"^Table\s+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var normNew = Norm(lt.Title);
                    bool duplicate = _tables.Any(x =>
                        Norm(x.Title).Equals(normNew, StringComparison.OrdinalIgnoreCase));
                    if (duplicate)
                    {
                        _log($"finestra gemella ignorata: '{lt.Title}'");
                        continue;
                    }

                    int slot = Enumerable.Range(0, MaxTables).FirstOrDefault(
                        candidate => _tables.All(existing => existing.Slot != candidate), -1);
                    if (slot < 0) continue;
                    lt.Slot = slot;
                    lt.LastHeroSeen = DateTime.Now;
                    lt.ClientThreadId = NativeMethods.GetWindowThreadProcessId(lt.Hwnd, out _);
                    _tables.Add(lt);
                    _shared.SetTableHwnd(slot, lt.Hwnd, _diagnosticHookMode ?? SharedMemory.ActionHookMode.Default);
                    _shared.EnableOptionButtonTrace(slot, false);
                    _shared.EnableBackDetection(slot, _neverSitOut);
                    if (lt.IsTourney && lt.TotalBuyin > 0) SessionStats.Current.TotalBuyin += lt.TotalBuyin;
                    _log($"nuovo tavolo slot {slot}: '{lt.TableName}' {lt.Sb}/{lt.Bb} {(lt.IsTourney ? "T" : "C")}");
                }
                else
                {
                    t.Title = lt.Title;
                    t.Sb = lt.Sb; t.Bb = lt.Bb; t.IsTourney = lt.IsTourney;
                    t.Ante = lt.Ante;
                    t.MaxSeats = lt.MaxSeats;
                    t.TableName = lt.TableName;
                    if (!string.Equals(t.HeroNick, lt.HeroNick, StringComparison.OrdinalIgnoreCase))
                    {
                        t.HeroSeat = t.ConfirmedHeroSeat = 0;
                        t.SitOutTracker = null;
                    }
                    t.HeroNick = lt.HeroNick;
                    NativeMethods.GetWindowRect(t.Hwnd, out t.Rect);
                }
            }

            _pixelTables = _tables.ToArray();
            AutoDialog.SetClosureTables(_tables.Select(table => table.Hwnd));

            foreach (var t in _tables)
            {
                if (t.Slot < 0) continue;
                // heroAct/hero-in-hand gestiti dal thread pixel (DetectHeroStatePixel)
                if (t.HeroAct) t.LastHeroSeen = DateTime.Now;
            }

            Hud.OnTick(_tables);
            GenHud.SetTables(_tables);
            GenHud.OnTick();

            // session stats (Fase 4): il tempo avanza mentre il hero è in mano —
            // dal deal alla fine della mano, fold incluso; quando il hero non riceve
            // carte (sitout/attesa) HeroInHand resta false → il tempo si ferma.
            // Lo stato resta visibile al timer HUD che mantiene il tempo fluido
            bool inHand = _tables.Any(t => t.HeroAct || (t.HeroInHand && !t.HeroSampleUnavailable));
            SessionStats.Current.InHandNow = inHand;
            SessionStats.Current.Tick(inHand);
        }

        private void ScanHud()
        {
            bool updated = false;
            bool detectTableEnd = Manager.RenderedClient && AutoDialog.CloseTableHasBeenClosed;
            foreach (var t in _tables)
            {
                if (t.Slot < 0) continue;
                if (t.PendingScanId != 0 && _shared.ReadTableScanId(t.Slot) == t.PendingScanId)
                {
                    ReadTableTexts(t);
                    updated = true;
                }
                t.BetScriptScanTurn = t.HeroAct ? t.BetScriptTurn : -1;
                _shared.EnableTableEndDetection(t.Slot, detectTableEnd);
                t.PendingScanId = _shared.RequestTableScan(t.ClientThreadId, t.Slot);
            }
            if (updated && Hud.Active) Hud.OnTick(_tables);
        }

        /// <summary>
        /// Interpreta i testi pubblicati dalla DLL. Il piatto è identificato dal nodo
        /// o dal prefisso "Pot:". Lo stack di Hero è riconosciuto nella regione configurata.
        /// </summary>
        private int _diagCycles;
        private void ReadTableTexts(TableInfo t)
        {
            var texts = _shared.ReadTableTexts(t.Slot);
            var (nodes, labels, cocosBound, scanStatus, viewApi) = _shared.ReadTableDiag(t.Slot);
            if (Manager.RenderedClient && scanStatus == SharedMemory.CocosScanStatus.Walked && _shared.ReadTableEnd(t.Slot))
                AutoDialog.ObserveTableEnd(t.Hwnd);
            if (++_diagCycles % 8 == 1)
            {
                string preview = string.Join(" | ", texts.Take(3).Select(x => (x.node.Length > 0 ? x.node + "=" : "") + x.text));
                _log($"slot testi '{t.TableName}': cocos={cocosBound} stato={scanStatus} viewApi=0x{viewApi:X} nodi={nodes} label={labels} letti={texts.Count}" +
                     (texts.Count > 0 ? "  [" + preview + "]" : ""));
                if (_diagnosticHookMode.HasValue)
                {
                    int mask = _shared.ReadActionHookMask(t.Slot);
                    _log($"diagnostic action hooks '{t.TableName}': mode={_diagnosticHookMode}, setVisible={(mask & 8) != 0}, textMask=0x{mask & 7:X}");
                    if (_neverSitOut)
                        _log($"diagnostic sit-out '{t.TableName}': {_shared.ReadOptionButtonTrace(t.Slot).Buttons}, back={_shared.ReadBackPosition(t.Slot)}, inHand={t.HeroInHand}");
                }
            }
            t.AmountToCall = null;
            t.DisplayedRaiseTo = null;
            t.SeatStacks.Clear();
            t.SeatBounds.Clear();
            t.SeatActions.Clear();
            t.Pot = 0;
            if (texts.Count == 0 && scanStatus != SharedMemory.CocosScanStatus.Walked) return;

            double bb = t.Bb > 0 ? t.Bb : 1.0;
            t.BetBoxInBb = Manager.RenderedClient && TextsPotInBb;
            double ToUnits(double v) => TextsPotInBb ? v * bb : v;
            bool heroStackSeen = false;
            var betScriptState = _shared.ReadBetScriptState(t.Slot);
            if (betScriptState.HeroSeat is >= 1 and <= 10)
                t.HeroSeat = t.ConfirmedHeroSeat = betScriptState.HeroSeat;
            NativeMethods.GetClientRect(t.Hwnd, out var client);

            foreach (var (rect, text, node, seat) in texts)
            {
                // pot: nodo *totalpot* o testo "Pot: X"
                var pot = System.Text.RegularExpressions.Regex.Match(text,
                    @"Pot\s*:\s*([\d.,]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                bool potNode = node.IndexOf("totalpot", StringComparison.OrdinalIgnoreCase) >= 0;
                if (pot.Success && TitleParser.ParseMoney(pot.Groups[1].Value) > 0)
                {
                    t.Pot = ToUnits(TitleParser.ParseMoney(pot.Groups[1].Value));
                }
                else if (potNode && TitleParser.ParseMoney(text) > 0)
                {
                    t.Pot = ToUnits(TitleParser.ParseMoney(text));
                }

                if (seat != 0 && node == "Status")
                {
                    var amount = System.Text.RegularExpressions.Regex.Match(text, @"^[\d.,]+");
                    if (amount.Success)
                    {
                        double stack = ToUnits(TitleParser.ParseMoney(amount.Value));
                        t.SeatStacks[seat] = stack;
                        t.SeatBounds[seat] = rect;
                        var hero = Pixels.HeroRect;
                        if (seat == t.ConfirmedHeroSeat || (t.ConfirmedHeroSeat == 0 && !Manager.RenderedClient &&
                            !string.IsNullOrEmpty(t.HeroNick) && client.Right > 0 && client.Bottom > 0 &&
                            hero.Contains((rect.Left + rect.Width / 2f) / client.Right,
                                (rect.Top + rect.Height / 2f) / client.Bottom)))
                        {
                            t.HeroSeat = seat;
                            t.Stack = stack;
                            heroStackSeen = stack > 0;
                        }
                    }
                }
                if (seat != 0 && node == "PlayerAction")
                {
                    t.SeatActions[seat] = text.Trim();
                    t.SeatBounds.TryAdd(seat, rect);
                }
                if (node == "Action")
                {
                    if (text.Equals("Check", StringComparison.OrdinalIgnoreCase)) t.AmountToCall ??= 0;
                    var call = System.Text.RegularExpressions.Regex.Match(text, @"^Call\s+([\d.,]+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (call.Success) t.AmountToCall = ToUnits(TitleParser.ParseMoney(call.Groups[1].Value));
                    var raiseTo = System.Text.RegularExpressions.Regex.Match(text, @"^Raise\s+To\s+([\d.,]+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (raiseTo.Success) t.DisplayedRaiseTo = ToUnits(TitleParser.ParseMoney(raiseTo.Groups[1].Value));
                }
            }
            if (scanStatus != SharedMemory.CocosScanStatus.Walked) return;
            if (Manager.RenderedClient)
                ObserveHeroAction(t, betScriptState.CanSetAmount && t.AmountToCall.HasValue && !string.IsNullOrEmpty(t.HeroNick));
            if (_neverSitOut && Manager.RenderedClient) ObserveSitOut(t);
            var actions = _shared.ReadSeatActionEvents(t.Slot, ref t.LastActionSequence, out bool droppedActions, out uint? snapshotTick);
            if (droppedActions) _log($"HUD: coda azioni incompleta su '{t.TableName}'");
            int street = t.ActionHistory.Observe(t.HandStamp, _shared.ReadBoardCards(t.Slot),
                t.Pot, t.SeatActions, t.SeatBounds, actions, snapshotTick);
            if (street > 0)
            {
                t.Street = street;
                ApplyScannedBetScript(t, heroStackSeen);
            }
        }

        internal void SetNeverSitOut(IniFile ini)
        {
            _neverSitOutMaximum = ini.GetBool("PokerCaption", "NeverSitOutLimit", false)
                ? Math.Clamp(ini.GetInt("PokerCaption", "NeverSitOutMaximum", 3), 1, 999)
                : null;
            bool enabled = ini.GetBool("PokerCaption", "NeverSitOut", false);
            if (_neverSitOut == enabled) return;
            _neverSitOut = enabled;
            foreach (var table in _tables)
            {
                table.SitOutTracker?.ResetObservation();
                _shared.EnableBackDetection(table.Slot, enabled);
            }
        }

        private void ObserveSitOut(TableInfo table)
        {
            var trace = _shared.ReadOptionButtonTrace(table.Slot);
            bool backVisible = trace.Buttons.HasFlag(SharedMemory.OptionButtonTrace.BackLabel);
            var position = _shared.ReadBackPosition(table.Slot);
            var tracker = table.SitOutTracker ??= new NeverSitOut();
            bool canResume = tracker.ShouldResume(backVisible, table.HeroAct || (table.HeroInHand && !table.HeroSampleUnavailable), DateTime.UtcNow,
                since => position.HasValue && NeverSitOut.HasRecentTimeout(table, since), _neverSitOutMaximum,
                (SitOutIntent)_shared.ReadSitOutIntent(table.Slot), table.HeroAct, NativeMethods.ReadLastInputTick());
            if (canResume && position is { } point && !Manager.IsMoving(table))
            {
                bool background = NativeMethods.GetForegroundWindow() != table.Hwnd;
                bool clicked = CoordsEngine.ClickSitOutButton(table.Hwnd, point, out bool foregroundRestored);
                if (!foregroundRestored)
                {
                    tracker.MarkResumed();
                    _log($"Never sit out: Windows non ha ripristinato il primo piano dopo il tentativo su '{table.TableName}'");
                    return;
                }
                if (!clicked) return;
                tracker.MarkResumed();
                string source = tracker.EarlySignal ? "transizione al sit-out" : "timeout nella cronologia";
                _log($"Never sit out: richiesta di rientro su '{table.TableName}' tramite {source}{(background ? " (secondo piano)" : "")}");
            }
        }

        internal string ApplyScannedBetScript(TableInfo table, bool heroStackSeen) =>
            heroStackSeen && table.AmountToCall.HasValue && table.BetScriptScanTurn == table.BetScriptTurn && !Manager.IsMoving(table)
                ? Betscript.Apply(table) : null;

        /// <summary>heroAct + hero-in-hand via pixel (client renderizzati).
        /// Gira su thread dedicato: niente jank UI. Isteresi su hero-in-hand
        /// (2 tick per accendersi, 3 per spegnersi) → niente doppio conteggio mani.</summary>
        private void DetectHeroStatePixel(TableInfo t)
        {
            // finestre transient (titolo non ancora impostato o non parsabile): skip
            if (string.IsNullOrWhiteSpace(t.Title) ||
                (string.IsNullOrEmpty(t.HeroNick) && string.IsNullOrEmpty(t.TableName)))
                return;

            var (accent, lime, heroInRaw) = Pixels.Measure(t);
            ObserveHeroSample(t, heroInRaw);
            if (accent < 0) return; // cattura fallita

            if (!Manager.RenderedClient)
                ObserveHeroAction(t, accent >= Pixels.MinAccentPixels || lime >= Pixels.MinButtonPixels);
        }

        internal void ObserveHeroAction(TableInfo t, bool canAct)
        {
            if (canAct)
            {
                if (!t.HeroAct)
                {
                    Interlocked.Increment(ref t.BetScriptTurn);
                    t.HeroAct = true;
                    t.HeroFolded = false;
                    Hud.OnHeroAct(t);
                    _log($"heroAct su '{t.TableName}'");
                }
                t.LastHeroSeen = DateTime.Now;
            }
            else if (t.HeroAct)
            {
                t.HeroAct = false;
                t.HeroFoldedAt = DateTime.Now;
            }
        }

        internal void ObserveHeroSample(TableInfo t, int pixels)
        {
            t.HeroSampleUnavailable = pixels < 0;
            if (t.HeroSampleUnavailable)
            {
                t.HeroInTrueTicks = t.HeroInFalseTicks = 0;
                return;
            }

            if (pixels >= Pixels.MinHeroPixels) { t.HeroInTrueTicks++; t.HeroInFalseTicks = 0; }
            else { t.HeroInFalseTicks++; t.HeroInTrueTicks = 0; }

            if (!t.HeroInHand && t.HeroInTrueTicks >= 2)
            {
                t.HeroInHand = true;
                Interlocked.Increment(ref t.HandStamp);
                long hand = SessionStats.Current.RecordHand();
                Hud.OnHandChanged(t);
                _log($"mano #{hand} su '{t.TableName}'");
            }
            if (t.HeroInHand && t.HeroInFalseTicks >= 3)
                t.HeroInHand = false;
        }

        /// <summary>Thread pixel: misura e aggiorna lo stato dei tavoli FUORI dalla UI.</summary>
        private volatile bool _pixelRun;
        private Thread _pixelThread;
        private void PixelLoop()
        {
            while (_pixelRun)
            {
                try
                {
                    foreach (var t in _pixelTables)
                    {
                        if (!NativeMethods.IsWindow(t.Hwnd)) continue;
                        lock (_captureGate)
                        {
                            if (Manager.LayoutOnly)
                                ObserveHeroSample(t, Pixels.MeasureVisibleHero(t));
                            else
                                DetectHeroStatePixel(t);
                        }
                    }
                }
                catch { }
                if (!_pixelRun || _pixelWake.Wait(700)) break;
            }
        }

        /// <summary>Tavolo sotto il cursore (per wheel bet).</summary>
        public IntPtr TableUnderCursor()
        {
            NativeMethods.GetCursorPos(out var pt);
            var h = NativeMethods.WindowFromPoint(pt);
            foreach (var t in _tables)
                if (t.Hwnd == h || NativeMethods.IsChild(t.Hwnd, h))
                    return t.Hwnd;
            return IntPtr.Zero;
        }

        public TableInfo Find(IntPtr hwnd) => _tables.FirstOrDefault(x => x.Hwnd == hwnd);
        public static TableInfo FindByHwnd(IntPtr hwnd) => _static?.Find(hwnd);

        private static TableService _static;
        public void MakeDefault() => _static = this;

        public void Dispose()
        {
            Stop();
            if (_static == this) _static = null;
        }
    }
}
