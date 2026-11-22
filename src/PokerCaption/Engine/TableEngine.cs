using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using PokerCaption.Native;

namespace PokerCaption.Engine
{
    /// <summary>Stato e dati HUD di un tavolo.</summary>
    public sealed class TableInfo
    {
        public IntPtr Hwnd;
        public int Slot = -1;
        public int PendingScanId;
        public string Title = "";
        public string TableName = "";
        public bool IsTourney;
        public string GameType = "";          // "NLHE", "PLO", …
        public int MaxSeats;
        public double Sb, Bb;
        public string Currency = "$";
        public double Pot, Bet, Stack;
        public double? AmountToCall;
        public readonly Dictionary<int, double> SeatStacks = new();
        public readonly Dictionary<int, System.Drawing.Rectangle> SeatBounds = new();
        public readonly Dictionary<int, string> SeatActions = new();
        internal readonly SeatActionHistory ActionHistory = new();
        internal int LastActionSequence;
        public int Street;                    // 1 preflop, 2 flop, 4 turn, 8 river (bitmask come AdvancedBets)
        public volatile bool HeroAct;
        internal int BetScriptTurn;
        internal int BetScriptScanTurn = -1;
        internal int LastPrefilledTurn = -1;
        public bool HeroFolded;
        public volatile bool HeroInHand;      // pixel: hero distribuito in mano (client renderizzati)
        internal volatile bool HeroSampleUnavailable;
        public string BetBoxText = "";

        // --- Sisal / client renderizzato (dal titolo finestra) ---
        public string HeroNick = "";          // "Logged In as <nick>"
        public double Ante;
        public bool IsPlayMoney;
        public bool ShortDeck;                // variante "6+"

        // dati di stato estesi (Fase 4/7): riempiti dal watcher live
        public int PlayersInHand;
        public double StartingChips, MinStartStack, AvStartStack;
        public double Guarantee, TotalBuyin, CurrentBuyin;
        public int BlindsTimerSec, TimeToActSec, TimeFromActSec;
        public double OddsPct;
        public bool Locked;                   // TM2 lock tavolo in slot
        public int TablesInSlot = 1;
        public int Sitout;                    // 0 no, 1 sitout next hand, 2 next BB…
        public bool InActiveSlot;             // posizionato in slot attivo (TM)
        public int HandStamp;                 // incrementato dal watcher a ogni hand
        public int PlacedSlot = -1;           // slot TM assegnato
        public uint ClientThreadId;           // thread del client che ospita il tavolo (trigger scan in-process)
        public DateTime OpenedAt = DateTime.Now;
        public DateTime HeroFoldedAt;
        public bool HadActButtons;            // il tavolo ha mostrato pulsanti azione
        public DateTime LastHeroSeen;
        public DateTime LastHandCountTime;    // anti-doppio conteggio mani (gap ≥ 20s)
        public int HeroInTrueTicks;           // isteresi pixel hero-in-hand
        public int HeroInFalseTicks;

        // controlli figli (classi PokerStars)
        public readonly List<ChildCtl> Buttons = new List<ChildCtl>();
        public IntPtr Slider, Editor, FoldBtn, CheckBtn, CallBtn, BetBtn, RaiseBtn;

        public NativeMethods.RECT Rect;
        public DateTime LastSeen = DateTime.Now;

        public override string ToString() => $"0x{Hwnd.ToInt64():X} '{TableName}' {Sb}/{Bb}";
    }

    public sealed class ChildCtl
    {
        public IntPtr Hwnd;
        public string Class = "";
        public NativeMethods.RECT Rect;
        public bool Visible;
    }

    /// <summary>
    /// Parsing dei titoli dei tavoli PokerStars (Fase 3/4).
    /// Cash:   Table 'Nome II' 6-max No Limit Hold'em $0.01/$0.02 - ...
    /// Torneo: Table '123456 11' 9-max Seat 2 is the button, Level II (25/50) ...
    /// </summary>
    public static class TitleParser
    {
        public static void Apply(TableInfo t)
        {
            if (t == null || string.IsNullOrEmpty(t.Title)) return;
            var s = t.Title;

            var m = System.Text.RegularExpressions.Regex.Match(s, @"Table '([^']+)'");
            if (m.Success) t.TableName = m.Groups[1].Value;

            // --- profilo Sisal (client renderizzato) ---
            // titolo: "<nome> - No Limit 6+ Hold'em button blind€250 ante€250 Play Money - Logged In as <nick>"
            m = System.Text.RegularExpressions.Regex.Match(s, @"Logged In as (\S+)");
            if (m.Success) t.HeroNick = m.Groups[1].Value;
            t.IsPlayMoney = s.IndexOf("Play Money", StringComparison.OrdinalIgnoreCase) >= 0;
            t.ShortDeck = s.Contains("6+ ");
            m = System.Text.RegularExpressions.Regex.Match(s, @"blind\s*[$€£]?([\d.,]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) { t.Sb = 0; t.Bb = ParseMoney(m.Groups[1].Value); t.Currency = "€"; }
            m = System.Text.RegularExpressions.Regex.Match(s, @"ante\s*[$€£]?([\d.,]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) t.Ante = ParseMoney(m.Groups[1].Value);
            m = System.Text.RegularExpressions.Regex.Match(s, @"^([^–\-]+?)\s*[–\-]+\s*(No Limit|Pot Limit|Fixed Limit)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) t.TableName = m.Groups[1].Value.Trim();
            // via i codici admin/participation che alcune skin mettono nel titolo
            if (!string.IsNullOrEmpty(t.TableName))
                t.TableName = System.Text.RegularExpressions.Regex.Replace(t.TableName, @"\[[^\]]*\]", "").Trim();

            m = System.Text.RegularExpressions.Regex.Match(s, @"(\d+)-max");
            if (m.Success) t.MaxSeats = int.Parse(m.Groups[1].Value);

            // cash: $0.01/$0.02 o €1.00/€2.00
            m = System.Text.RegularExpressions.Regex.Match(s, @"([$€£])\s*([\d.,]+)\s*/\s*[$€£]?\s*([\d.,]+)");
            if (m.Success)
            {
                t.IsTourney = false;
                t.Currency = m.Groups[1].Value;
                t.Sb = ParseMoney(m.Groups[2].Value);
                t.Bb = ParseMoney(m.Groups[3].Value);
            }
            else
            {
                // torneo: Level III (100/200) oppure (25/50)
                m = System.Text.RegularExpressions.Regex.Match(s, @"\(([\d.,]+)\s*/\s*([\d.,]+)\)");
                if (m.Success)
                {
                    t.IsTourney = true;
                    t.Currency = "";
                    t.Sb = ParseMoney(m.Groups[1].Value);
                    t.Bb = ParseMoney(m.Groups[2].Value);
                }
            }

            m = System.Text.RegularExpressions.Regex.Match(s,
                @"^(.+?)\s+[-–]\s*[$€£]?([\d.,]+)\s*/\s*[$€£]?([\d.,]+)(?:\s+ante\s*[$€£]?([\d.,]+))?\s+[-–]\s*Tournament\s+\d+\s+Table\s+\d+\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success)
            {
                t.TableName = m.Groups[1].Value.Trim();
                t.IsTourney = true;
                t.Currency = "";
                t.Sb = ParseMoney(m.Groups[2].Value);
                t.Bb = ParseMoney(m.Groups[3].Value);
                if (m.Groups[4].Success) t.Ante = ParseMoney(m.Groups[4].Value);
            }

            if (s.IndexOf("No Limit", StringComparison.OrdinalIgnoreCase) >= 0) t.GameType = "NL";
            else if (s.IndexOf("Pot Limit", StringComparison.OrdinalIgnoreCase) >= 0) t.GameType = "PL";
            else if (s.IndexOf("Fixed Limit", StringComparison.OrdinalIgnoreCase) >= 0) t.GameType = "FL";
            if (s.IndexOf("Hold'em", StringComparison.OrdinalIgnoreCase) >= 0) t.GameType += "HE";
            else if (s.IndexOf("Omaha", StringComparison.OrdinalIgnoreCase) >= 0) t.GameType += "O";
        }

        public static double ParseMoney(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            s = s.Trim().Replace(",", "");
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }
    }

    /// <summary>
    /// Scanner delle finestre tavolo e dei controlli figli (Fasi 3–4).
    /// Classi dal ClientProfile (PokerStars di default; skin configurabili in [Client]).
    /// </summary>
    public static class TableScanner
    {
        public static string TableClass => ClientProfile.TableClass;
        public static string ButtonClass => ClientProfile.ButtonClass;
        public static string SliderClass => ClientProfile.SliderClass;
        public static string EditorClass => ClientProfile.EditorClass;
        public static string SelectorClass => ClientProfile.SelectorClass;
        public static string FrameClass => ClientProfile.FrameClass;
        public static string ListClass => ClientProfile.ListClass;

        /// <summary>Titolo plausibile di un tavolo poker (fallback per skin con classi sconosciute).</summary>
        private static readonly System.Text.RegularExpressions.Regex TableTitleRx =
            new System.Text.RegularExpressions.Regex(
                @"(Table\s*'|\d+-max|Level\s+[IVX]+|\bHold'em\b|\bOmaha\b|\bNo Limit\b|\bPot Limit\b)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Trova i tavoli visibili e popola i campi base di ogni TableInfo.</summary>
        public static List<TableInfo> ScanTables()
        {
            var tables = new List<TableInfo>();
            var clientPids = new HashSet<uint>(
                System.Linq.Enumerable.Select(ClientLocator.FindClients(), c => c.ProcessId));
            NativeMethods.EnumWindows((h, l) =>
            {
                if (!NativeMethods.IsWindowVisible(h)) return true;
                var cls = GetClass(h);
                NativeMethods.GetWindowThreadProcessId(h, out var pidOwner);
                var tsb = new StringBuilder(400);
                NativeMethods.GetWindowTextW(h, tsb, 400);
                var windowTitle = tsb.ToString();

                bool isTable = cls == TableClass;
                if (!isTable)
                {
                    // skin con classi rinominate: suffisso noto OPPURE titolo da tavolo
                    if (cls != null && cls.EndsWith("TableFrameClass", StringComparison.OrdinalIgnoreCase))
                        isTable = true;
                    else if (windowTitle.Trim().Length > 8 && TableTitleRx.IsMatch(windowTitle) && clientPids.Contains(pidOwner))
                        isTable = true;
                }
                // client renderizzato (Sisal): finestre GLFW dei soli processi client,
                // escluso il main window (Client.MainTitles) e le finestre transient
                // senza titolo (durante la creazione del tavolo)
                if (!isTable && ClientProfile.Interaction == "coords")
                {
                    if (Array.IndexOf(ClientProfile.RenderTableClasses, cls) >= 0 &&
                        clientPids.Contains(pidOwner) &&
                        windowTitle.Trim().Length > 0 &&
                        Array.IndexOf(ClientProfile.MainTitles, windowTitle) < 0)
                        isTable = true;
                }
                if (!isTable) return true;
                if (windowTitle.Trim().Length == 0) return true; // transient: niente tavolo fantasma
                var t = new TableInfo { Hwnd = h };
                t.Title = windowTitle;
                TitleParser.Apply(t);
                if (ClientProfile.Interaction == "coords" && Array.IndexOf(ClientProfile.RenderTableClasses, cls) >= 0 &&
                    (t.TableName.Length == 0 || (t.GameType.Length == 0 && !t.IsTourney))) return true;
                NativeMethods.GetWindowRect(h, out t.Rect);
                tables.Add(t);
                return true;
            }, IntPtr.Zero);
            return tables;
        }

        /// <summary>Enumera i controlli figli e prova a identificarne i ruoli.</summary>
        public static void ScanChildren(TableInfo t)
        {
            t.Buttons.Clear();
            t.Slider = t.Editor = IntPtr.Zero;
            t.FoldBtn = t.CheckBtn = t.CallBtn = t.BetBtn = t.RaiseBtn = IntPtr.Zero;

            NativeMethods.EnumChildWindows(t.Hwnd, (h, l) =>
            {
                var cls = GetClass(h);
                NativeMethods.GetWindowRect(h, out var r);
                bool vis = NativeMethods.IsWindowVisible(h);
                if (cls == ButtonClass) t.Buttons.Add(new ChildCtl { Hwnd = h, Class = cls, Rect = r, Visible = vis });
                else if (cls == SliderClass && vis && t.Slider == IntPtr.Zero) t.Slider = h;
                else if (cls == EditorClass && vis && t.Editor == IntPtr.Zero) t.Editor = h;
                else if (cls == FrameClass && vis) { /* frame contenuti: ignorati per ora */ }
                return true;
            }, IntPtr.Zero);

            ClassifyButtons(t);
        }

        /// <summary>
        /// Identificazione per geometria (client 792×546):
        /// i pulsanti azione stanno nella fascia bassa, disposti orizzontalmente.
        /// Fold = più a sinistra; Bet/Raise = più a destra; Check/Call in mezzo.
        /// Il timebank sta in alto (riga sotto il titolo).
        /// </summary>
        private static void ClassifyButtons(TableInfo t)
        {
            var w = t.Rect.Right - t.Rect.Left;
            var h = t.Rect.Bottom - t.Rect.Top;
            if (w <= 0) return;

            // pulsanti azione: fascia bassa (y > 75% altezza), larghezza ragionevole
            var act = new List<ChildCtl>();
            foreach (var b in t.Buttons)
            {
                int bw = b.Rect.Right - b.Rect.Left, bh = b.Rect.Bottom - b.Rect.Top;
                int relY = b.Rect.Top - t.Rect.Top;
                if (!b.Visible || bw < 30 || bh < 15) continue;
                if (relY > h * 0.70) act.Add(b);
            }
            act.Sort((a, b) => a.Rect.Left - b.Rect.Left);
            if (act.Count >= 3)
            {
                t.FoldBtn = act[0].Hwnd;
                t.CheckBtn = act[1].Hwnd;   // Check o Call
                t.CallBtn = t.CheckBtn;
                t.BetBtn = act[act.Count - 1].Hwnd;  // Bet o Raise
                t.RaiseBtn = t.BetBtn;
            }
            else if (act.Count == 2)
            {
                t.FoldBtn = act[0].Hwnd;
                t.CheckBtn = act[1].Hwnd;
                t.CallBtn = t.CheckBtn;
            }
        }

        /// <summary>Legge il testo della bet box (WM_GETTEXT).</summary>
        public static string GetText(IntPtr hwnd, int max = 128)
        {
            if (hwnd == IntPtr.Zero) return "";
            var sb = new StringBuilder(max);
            NativeMethods.SendMessageGetText(hwnd, 0x000D /*WM_GETTEXT*/, (IntPtr)max, sb);
            return sb.ToString();
        }

        public static string GetClass(IntPtr h)
        {
            var sb = new StringBuilder(64);
            return NativeMethods.GetClassNameW(h, sb, 64) > 0 ? sb.ToString() : "";
        }

        /// <summary>Conversione valuta → bb (Fase 4).</summary>
        public static double ToBb(double amount, TableInfo t, double cashMult = 1.0, double trnyMult = 1.0, int decimals = 2)
        {
            if (t == null || t.Bb <= 0) return 0;
            var mult = t.IsTourney ? trnyMult : cashMult;
            return Math.Round(amount / t.Bb * mult, decimals);
        }

        public static double FromBb(double bb, TableInfo t)
        {
            if (t == null || t.Bb <= 0) return 0;
            return bb * t.Bb;
        }
    }
}
