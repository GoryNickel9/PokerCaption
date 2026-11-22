using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PokerCaption.Config;
using PokerCaption.Native;

namespace PokerCaption.Engine
{
    /// <summary>
    /// Motore HUD: per ogni tavolo crea le finestre
    /// overlay dei pannelli [HUD] e della
    /// bet box in bb (bbBetboxX/Y, CustomBetBox, BetBoxFontSize/Bold, bbColor*,
    /// bbColorDiap*), gestisce RNG e refresh dei tag.
    /// </summary>
    public sealed class HudEngine : IDisposable
    {
        public bool Active = true;
        public double BbBetboxX = 635, BbBetboxY = 835;
        public bool CustomBetBox;
        public int BetBoxFontSize = 10;
        public bool BetBoxBold = true;
        public double BetBoxWidthFactor = 3.3;
        public int BbDecimals = 2;
        public bool TrimNick;
        public int TrimNickTo = 4;
        public int RngFrom, RngTo = 100, Rng2From, Rng2To = 100, RngTimerPeriod = 2;
        public bool RngNewHand, RngNewStreet, RngHeroAct, RngTimerEn;
        public bool FrameEnabled = true; // bbBetboxFrame

        public sealed class PanelDef
        {
            public int Index;                     // indice INI
            public string Format;
            public Color FontColor = Color.White; // FontColor_i (testo)
            public int FontSize = 10;             // FontSize_i
            public bool PlayerPanel;
            internal readonly Dictionary<int, PointF> SeatOffsets = new();
            public bool Visible = true;
        }

        public readonly List<PanelDef> Panels = new List<PanelDef>();
        /// <summary>'Lock HUD' ([HUD] LockHUD2): true = pannelli bloccati (no mouse).</summary>
        public bool LockPanels = true;

        private readonly Dictionary<IntPtr, List<HudTextPanel>> _perTable = new();
        private readonly Dictionary<IntPtr, Dictionary<(int seat, int panel), HudTextPanel>> _perPlayer = new();
        private readonly Dictionary<IntPtr, HudTextPanel> _bbBoxes = new();
        private readonly Action<string> _log;
        private const int PlayerPanelStartInset = 24;
        private IniFile _ini;
        private Random _rng = new Random();
        private int _handId, _streetId, _heroActId;

        public HudEngine(Action<string> log)
        {
            _log = log ?? (_ => { });
        }

        public void Load(IniFile ini)
        {
            _ini = ini;
            int previousTablePanelCount = Panels.Count(p => !p.PlayerPanel);
            Active = ini.GetBool("HUD", "HudActive", true);
            BbBetboxX = ini.GetDouble("HUD", "bbBetboxX", 635);
            BbBetboxY = ini.GetDouble("HUD", "bbBetboxY", 835);
            CustomBetBox = ini.GetBool("PokerCaption", "CustomBetBox", false);
            BetBoxFontSize = ini.GetInt("PokerCaption", "BetBoxFontSize", 10);
            BetBoxBold = ini.GetBool("PokerCaption", "BetBoxBold", true);
            BetBoxWidthFactor = ini.GetDouble("PokerCaption", "BetBoxWidthFactor", 3.3);
            BbDecimals = ini.GetInt("PokerCaption", "bb_Decimals", 2);
            TrimNick = ini.GetBool("HUD", "trimNick", false);
            TrimNickTo = ini.GetInt("HUD", "trimNickTo", 4);
            RngFrom = ini.GetInt("HUD", "rngFrom", 0);
            RngTo = ini.GetInt("HUD", "rngTo", 100);
            Rng2From = ini.GetInt("HUD", "rng2From", 0);
            Rng2To = ini.GetInt("HUD", "rng2To", 100);
            RngTimerPeriod = ini.GetInt("HUD", "RngTimerPeriod", 2);
            RngNewHand = ini.GetBool("PokerCaption", "RngNewHand", false);
            RngNewStreet = ini.GetBool("PokerCaption", "RngNewStreet", false);
            RngHeroAct = ini.GetBool("PokerCaption", "RngHeroAct", false);
            RngTimerEn = ini.GetBool("PokerCaption", "RngTimerEn", false);

            Panels.Clear();
            // 'Lock HUD' ([HUD] LockHUD2): al cambio ricrea i pannelli col giusto
            // click-through (bloccati = click-through, sbloccati = trascinabili)
            bool lockNow = ini.GetBool("HUD", "LockHUD", false);
            if (lockNow != LockPanels) DestroyAll();
            LockPanels = lockNow;
            var hud = ini.Find("HUD");
            if (hud != null)
            {
                // I pannelli sono indicizzati senza Count: si legge finché
                // la chiave esiste — i pannelli possono essere aggiunti/triminati dalla UI
                for (int i = 0; i < 64; i++)
                {
                    // Pannelli tabella in ordine di configurazione.
                    var f = ini.Get("HUD", "FormatStr" + i);
                    if (f == null) break;
                    Panels.Add(new PanelDef
                    {
                        Index = i,
                        Format = f,
                        FontColor = IniColors.FromInt(ini.GetLong("HUD", "FontColor" + i, 0xFFFFFF)),
                        PlayerPanel = false,
                        Visible = ini.GetBool("HUD", "FVisible" + i, true)
                    });
                }
                for (int i = 0; i < 64; i++)
                {
                    // pannelli giocatore (richiedono la mappa posti: live)
                    var pf = ini.Get("HUD", "plFormatStr" + i);
                    if (pf == null) break;
                    var playerPanel = new PanelDef
                    {
                        Index = i,
                        Format = pf,
                        FontColor = IniColors.FromInt(ini.GetLong("HUD", "plFontColor" + i, 0xFFFFFF)),
                        FontSize = ini.GetInt("HUD", "plFontSize" + i, 10),
                        PlayerPanel = true,
                        Visible = ini.GetBool("HUD", "plFVisible" + i, true)
                    };
                    for (int seat = 1; seat <= 10; seat++)
                    {
                        var offset = new PointF((float)ini.GetDouble("HUD", PlayerOffsetKey('X', seat, i), 0),
                            (float)ini.GetDouble("HUD", PlayerOffsetKey('Y', seat, i), 0));
                        if (!offset.IsEmpty) playerPanel.SeatOffsets[seat] = offset;
                    }
                    Panels.Add(playerPanel);
                }
            }

            // The next tick must rebuild the windows when definitions are added or removed.
            if (previousTablePanelCount != Panels.Count(p => !p.PlayerPanel))
                foreach (var hwnd in _perTable.Keys.ToList()) DestroyTablePanels(hwnd);
        }

        internal void FollowTables(IReadOnlyList<TableInfo> tables)
        {
            if (!Active) return;
            bool refresh = false;
            foreach (var table in tables)
            {
                if (!NativeMethods.GetWindowRect(table.Hwnd, out var rect)) continue;
                int dx = rect.Left - table.Rect.Left;
                int dy = rect.Top - table.Rect.Top;
                bool sizeChanged = rect.Right - table.Rect.Right != dx || rect.Bottom - table.Rect.Bottom != dy;
                if (dx == 0 && dy == 0 && !sizeChanged) continue;
                table.Rect = rect;
                if (sizeChanged) { refresh = true; continue; }
                if (_perTable.TryGetValue(table.Hwnd, out var panels))
                    foreach (var panel in panels) MoveWithTable(panel, dx, dy);
                if (_perPlayer.TryGetValue(table.Hwnd, out var players))
                    foreach (var panel in players.Values) MoveWithTable(panel, dx, dy);
                if (_bbBoxes.TryGetValue(table.Hwnd, out var box)) MoveWithTable(box, dx, dy);
            }
            if (refresh) OnTick(tables);
        }

        private static void MoveWithTable(HudTextPanel panel, int dx, int dy)
        {
            if (!panel.Dragging)
                panel.MoveTo(new Rectangle(panel.Left + dx, panel.Top + dy, panel.Width, panel.Height));
        }

        /// <summary>Refresh ciclico: crea/aggiorna/distrugge gli overlay per tavolo.</summary>
        public void OnTick(IReadOnlyList<TableInfo> tables)
        {
            if (!Active) { DestroyAll(); return; }

            foreach (var t in tables)
            {
                if (!NativeMethods.IsWindow(t.Hwnd)) continue;
                bool tableVisible = NativeMethods.IsWindowVisible(t.Hwnd) && !NativeMethods.IsIconic(t.Hwnd);
                if (!_perTable.TryGetValue(t.Hwnd, out var panels))
                {
                    panels = new List<HudTextPanel>();
                    foreach (var def in Panels)
                    {
                        if (def.PlayerPanel) continue;
                        var p = new HudTextPanel(clickThrough: true, tableOwner: t.Hwnd);
                        panels.Add(p);
                    }
                    _perTable[t.Hwnd] = panels;
                    _log($"HUD: creati {panels.Count} pannelli per tavolo '{t.TableName}'" +
                         (LockPanels ? " (bloccati)" : " (trascinabili)"));
                }

                var ctx = BuildContext(t, tables);

                int idx = 0;
                int y = t.Rect.Top + (int)((t.Rect.Bottom - t.Rect.Top) * 0.24);
                int center = t.Rect.Left + (t.Rect.Right - t.Rect.Left) / 2;
                foreach (var def in Panels.Where(def => !def.PlayerPanel))
                {
                    var panel = panels[idx++];
                    string text = def.Visible && tableVisible && HudTags.Visible(def.Format, ctx) ? HudTags.Resolve(def.Format, ctx) : "";
                    if (string.IsNullOrWhiteSpace(text)) { panel.Visible = false; continue; }
                    panel.SetRuns([(text, def.FontColor, Color.Empty, def.FontSize, true)], Padding.Empty);
                    panel.MoveTo(new Rectangle(center - panel.Width / 2, y, panel.Width, panel.Height));
                    panel.ShowOwned();
                    y += panel.Height + 2;
                }

                UpdatePlayerPanels(t, ctx, tableVisible);

                // L’ordine delle finestre nasconde automaticamente le aree coperte.
                if (tableVisible && CustomBetBox) UpdateBbBox(t, tables);
                else if (_bbBoxes.TryGetValue(t.Hwnd, out var hiddenBox)) hiddenBox.Visible = false;
            }

            // rimuovi overlay dei tavoli chiusi
            foreach (var dead in _perTable.Keys.Union(_perPlayer.Keys).Where(k => !NativeMethods.IsWindow(k)).ToList())
            {
                DestroyTable(dead);
            }
        }

        internal void UpdatePlayerPanels(TableInfo t, TagContext ctx, bool tableVisible)
        {
            if (!_perPlayer.TryGetValue(t.Hwnd, out var windows))
                _perPlayer[t.Hwnd] = windows = new();
            var used = new HashSet<(int seat, int panel)>();
            var origin = new NativeMethods.POINT();
            if (tableVisible && NativeMethods.ClientToScreen(t.Hwnd, ref origin) && NativeMethods.GetClientRect(t.Hwnd, out var client))
            {
                foreach (var (seat, anchor) in t.SeatBounds.Concat(t.ActionHistory.MissingAnchors(t.SeatBounds)))
                {
                    ctx.SeatNumber = seat;
                    int y = origin.Y + anchor.Bottom + 1;
                    foreach (var def in Panels)
                    {
                        if (!def.PlayerPanel || !def.Visible || !HudTags.Visible(def.Format, ctx)) continue;
                        if (!t.SeatBounds.ContainsKey(seat) && !HudTags.UsesActionHistory(def.Format)) continue;
                        var text = HudTags.Resolve(def.Format, ctx);
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        var key = (seat, def.Index);
                        used.Add(key);
                        if (!windows.TryGetValue(key, out var panel))
                        {
                            windows[key] = panel = new HudTextPanel(clickThrough: LockPanels, tableOwner: t.Hwnd);
                            int panelIndex = def.Index;
                            panel.DraggedTo += (x, y) =>
                            {
                                if (panel.Tag is Point defaultPosition)
                                    SavePlayerPanelPosition(t, seat, panelIndex, defaultPosition, new Point(x, y));
                            };
                        }
                        panel.SetRuns(new List<(string, Color, Color, int, bool)>
                        {
                            (text, def.FontColor, Color.Empty, def.FontSize, true)
                        }, Padding.Empty);
                        int left = Math.Clamp(anchor.Left + anchor.Width / 2 - PlayerPanelStartInset,
                            0, Math.Max(0, client.Right - PlayerPanelStartInset * 3));
                        int top = Math.Clamp(y - origin.Y, 0, Math.Max(0, client.Bottom - panel.Height));
                        var defaultPosition = new Point(origin.X + left, origin.Y + top);
                        panel.Tag = defaultPosition;
                        if (!panel.Dragging)
                        {
                            var position = PlayerPanelPosition(def, seat, defaultPosition, TableScale(t));
                            panel.MoveTo(new Rectangle(position, panel.Size));
                        }
                        panel.ShowOwned();
                        y += panel.Height + 1;
                    }
                }
                ctx.SeatNumber = 0;
            }
            foreach (var key in windows.Keys.Where(key => !used.Contains(key) && !windows[key].Dragging).ToArray())
            {
                windows[key].Dispose();
                windows.Remove(key);
            }
        }

        private static string PlayerOffsetKey(char axis, int seat, int index) => $"plDrag{axis}{seat}_{index}";

        internal static Point PlayerPanelPosition(PanelDef panel, int seat, Point origin, double scale)
        {
            PointF offset = panel.SeatOffsets.GetValueOrDefault(seat);
            return new Point(origin.X + (int)Math.Round(offset.X * scale), origin.Y + (int)Math.Round(offset.Y * scale));
        }

        internal void SavePlayerPanelPosition(TableInfo table, int seat, int index, Point origin, Point position)
        {
            var panel = Panels.FirstOrDefault(p => p.PlayerPanel && p.Index == index);
            if (panel == null) return;

            double scale = TableScale(table);
            var offset = new PointF((float)((position.X - origin.X) / scale), (float)((position.Y - origin.Y) / scale));
            panel.SeatOffsets[seat] = offset;
            try
            {
                _ini.Set("HUD", PlayerOffsetKey('X', seat, index), offset.X.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _ini.Set("HUD", PlayerOffsetKey('Y', seat, index), offset.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _ini.Save();
            }
            catch (Exception ex) { _log("HUD: save posizione giocatore ERR: " + ex.Message); }
        }

        private void UpdateBbBox(TableInfo t, IReadOnlyList<TableInfo> tables)
        {
            if (!_bbBoxes.TryGetValue(t.Hwnd, out var box))
            {
                box = new HudTextPanel(clickThrough: false, tableOwner: t.Hwnd) { BackColorOverride = Color.FromArgb(200, 20, 20, 20) };
                _bbBoxes[t.Hwnd] = box;
            }

            double amount = t.Bet;
            double bb = TableScanner.ToBb(amount, t, 1.0, 1.0, BbDecimals);
            // colore per soglie bbColorDiap0..3
            var color = Color.White;
            var d0 = Program.Ini?.GetLong("PokerCaption", "bbColorDiap0", 40) ?? 40;
            var c1 = IniColors.FromInt(Program.Ini?.GetLong("PokerCaption", "bbColor1", 8454143) ?? 8454143);
            if (bb >= d0) color = c1 == Color.Empty ? Color.Gold : c1;
            var runs = new List<(string, Color, Color, int, bool)>
            {
                (bb.ToString("0.##") + " bb", color, Color.Empty, BetBoxFontSize, BetBoxBold)
            };
            box.SetRuns(runs, new Padding(4, 2, 4, 2));
            var scale = TableScale(t);
            box.MoveTo(new Rectangle(
                t.Rect.Left + (int)(BbBetboxX * scale),
                t.Rect.Top + (int)(BbBetboxY * scale),
                (int)(box.ClientSize.Width * BetBoxWidthFactor), box.ClientSize.Height));
            box.ShowOwned();
        }

        private static double TableScale(TableInfo t)
        {
            int w = t.Rect.Right - t.Rect.Left;
            return w > 100 ? w / 792.0 : 1.0;
        }

        private TagContext BuildContext(TableInfo t, IReadOnlyList<TableInfo> tables)
        {
            // Aggiorna gli identificatori RNG ai cambi di mano, street o azione.
            if (RngNewHand && t != null && t.HandStamp != _handId) { _handId = t.HandStamp; }
            return new TagContext
            {
                Table = t,
                Tables = tables,
                Session = SessionStats.Current,
                Lobby = null,
                Rng = _rng,
                RngFrom = RngFrom, RngTo = RngTo, Rng2From = Rng2From, Rng2To = Rng2To,
                RngTimerPeriod = RngTimerPeriod,
                RngNewHand = RngNewHand, RngNewStreet = RngNewStreet, RngHeroAct = RngHeroAct, RngTimerEn = RngTimerEn,
                HandId = _handId, StreetId = _streetId, HeroActId = _heroActId,
                RngTimerValue = _rngTimerValue, RngTimerAt = _rngTimerAt
            };
        }

        private double _rngTimerValue;
        private DateTime _rngTimerAt = DateTime.MinValue;

        public void OnHandChanged(TableInfo t) { _handId++; }
        public void OnStreetChanged(TableInfo t) { _streetId++; }
        public void OnHeroAct(TableInfo t) { _heroActId++; }

        private void DestroyTablePanels(IntPtr hwnd)
        {
            if (_perTable.TryGetValue(hwnd, out var panels))
            {
                foreach (var p in panels) { p.Close(); p.Dispose(); }
                _perTable.Remove(hwnd);
            }
        }

        private void DestroyTable(IntPtr hwnd)
        {
            DestroyTablePanels(hwnd);
            if (_perPlayer.Remove(hwnd, out var playerPanels))
                foreach (var panel in playerPanels.Values) panel.Dispose();
            if (_bbBoxes.TryGetValue(hwnd, out var box))
            {
                box.Close(); box.Dispose();
                _bbBoxes.Remove(hwnd);
            }
        }

        public void DestroyAll()
        {
            foreach (var k in _perTable.Keys.Union(_perPlayer.Keys).Union(_bbBoxes.Keys).ToList()) DestroyTable(k);
        }


        public void Dispose()
        {
            DestroyAll();
        }
    }
}
