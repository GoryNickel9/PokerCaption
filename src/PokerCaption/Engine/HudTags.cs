using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PokerCaption.Engine
{
    /// <summary>Sorgente opzionale dei tag TL_* (stub: la feature Lobby è rimossa).</summary>
    public sealed class LobbyTagsSource
    {
        public string GetTag(string tag) => "-";
    }

    /// <summary>
    /// Contesto di valutazione dei tag HUD.
    /// </summary>
    public sealed class TagContext
    {
        public TableInfo Table;             // tavolo del pannello (null per GenHUD)
        public int SeatNumber;
        public IReadOnlyList<TableInfo> Tables;
        public SessionStats Session;
        public LobbyTagsSource Lobby;       // opzionale (feature Lobby rimossa)
        public Random Rng;
        public int RngFrom = 0, RngTo = 100, Rng2From = 0, Rng2To = 100, RngTimerPeriod = 2;
        public bool RngNewHand, RngNewStreet, RngHeroAct, RngTimerEn;
        public int HandId;                  // cambia a ogni hand (per [RNG])
        public int StreetId;
        public int RngHeroActId;                // cambia a ogni street
        public int HeroActId;
        public double RngTimerValue;
        public DateTime RngTimerAt = DateTime.MinValue;
    }

    /// <summary>
    /// Motore dei tag: risolve `[nome]` nelle format string.
    /// Tag supportati: BB SB BB100 totalPot(_bb) pot pot_bb
    /// SPR effSPR flopEffSPR effStack(_bb) nplayers playersInHand
    /// handsCount allHandsCount allTrnys sessionTime handsPerHour
    /// capt tableIdx RNG guarantee startingChips minStartStack
    /// avStartStack blindsTimer timeToAct timeFromAct maxTimeFromAct
    /// TM2_tableLocked TM2_tablesInSlot
    /// Condizioni (visibilità): [preflop] [postflop] [heroInHand] [heroAct] [!heroAct].
    /// </summary>
    public static class HudTags
    {
        internal static bool UsesActionHistory(string format) =>
            format.Contains("[actions]", StringComparison.OrdinalIgnoreCase) ||
            format.Contains("[action]", StringComparison.OrdinalIgnoreCase) ||
            format.Contains("[timing]", StringComparison.OrdinalIgnoreCase);
        public static string Resolve(string format, TagContext c)
        {
            if (string.IsNullOrEmpty(format)) return "";
            // Escape INI: "\s" = spazio (es. "Session\stime" -> "Session time")
            format = format.Replace("\\s", " ");
            var sb = new StringBuilder();
            int i = 0;
            while (i < format.Length)
            {
                int open = format.IndexOf('[', i);
                if (open < 0) { sb.Append(format.Substring(i)); break; }
                sb.Append(format.Substring(i, open - i));
                int close = format.IndexOf(']', open);
                if (close < 0) { sb.Append(format.Substring(open)); break; }
                var tag = format.Substring(open + 1, close - open - 1);
                sb.Append(EvalTag(tag, c, format, open, close, out int skipTo));
                // le condizioni possono nascondere il resto del pannello
                i = skipTo >= 0 ? skipTo : close + 1;
            }
            return sb.ToString();
        }

        /// <summary>True se le condizioni di visibilità nel formatString sono soddisfatte.</summary>
        public static bool Visible(string format, TagContext c)
        {
            if (format == null) return true;
            if (format.Contains("[preflop]") && c.Table != null && c.Table.Street != 1) return false;
            if (format.Contains("[postflop]") && c.Table != null && c.Table.Street == 1) return false;
            if (format.Contains("[heroInHand]") && !(c.Table?.HeroAct == true || c.Table?.HeroFolded == true && false)) return c.Table != null && (c.Table.HeroAct || c.Table.HeroFolded);
            if (format.Contains("[heroAct]") && c.Table?.HeroAct != true) return false;
            if (format.Contains("[!heroAct]") && c.Table?.HeroAct == true) return false;
            return true;
        }

        private static string EvalTag(string tag, TagContext c, string full, int open, int close, out int skipTo)
        {
            skipTo = -1;
            var t = TableInfoFor(c);
            var s = SessionStats.Current;
            var lower = tag.ToLowerInvariant();

            // tag con suffisso (_bb): valore in bb
            bool inBb = lower.EndsWith("_bb");
            var baseTag = inBb ? lower.Substring(0, lower.Length - 3) : lower;
            string BbOf(double v)
            {
                if (t == null || t.Bb <= 0) return "0";
                double bb = v / t.Bb;
                return bb.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (c.SeatNumber > 0 && baseTag == "stack")
                return t != null && t.SeatStacks.TryGetValue(c.SeatNumber, out double stack)
                    ? (inBb ? BbOf(stack) : stack.ToString("0.##")) : "";
            if (baseTag is "actions" or "action") return t?.ActionHistory.Format(c.SeatNumber, Program.Ini) ?? "";
            if (lower == "timing") return t?.ActionHistory.LastTiming(c.SeatNumber) ?? "";

            // varianti _bb
            if (inBb)
            {
                double v = baseTag switch
                {
                    "pot" => t?.Pot ?? 0,
                    "totalpot" => t?.Pot ?? 0,
                    "effstack" => t?.Stack ?? 0,
                    "stack" => t?.Stack ?? 0,
                    "bet" => t?.Bet ?? 0,
                    "raise" => t?.Bet ?? 0,
                    _ => double.NaN
                };
                if (!double.IsNaN(v)) return BbOf(v);
            }

            switch (baseTag)
            {
                // --- visibilità/condizioni ---
                case "preflop":
                case "postflop":
                case "heroinhand":
                case "heroact":
                case "!heroact":
                    skipTo = full.Length; // il resto è gestito da Visible()
                    return "";

                // --- tavolo ---
                case "bb": return t?.Bb.ToString("0.###") ?? "0";
                case "sb": return t?.Sb.ToString("0.###") ?? "0";
                case "bb100": return t != null ? (t.Bb * 100).ToString("0.##") : "0";
                case "totalpot":
                case "pot": return t != null ? t.Pot.ToString("0.##") : "0";
                case "pot_bb": case "totalpot_bb": return t != null ? BbOf(t.Pot) : "0";
                case "tocall":
                    return t?.AmountToCall is double call ? (inBb ? BbOf(call) : call.ToString("0.##")) : "-";
                case "seatstacks":
                    return t != null && t.SeatStacks.Count > 0
                        ? string.Join(" | ", t.SeatStacks.OrderBy(x => x.Key).Select(x =>
                            $"{x.Key}: {(inBb ? BbOf(x.Value) + " BB" : x.Value.ToString("0.##"))}"))
                        : "-";
                case "spr": return t != null && t.Pot > 0 ? (t.Stack / t.Pot).ToString("0.##") : "-";
                // effSPR: SPR sullo stack effettivo (stack al netto della call già in pot)
                case "effspr": return t != null && t.Pot > 0 ? (Math.Max(t.Stack, 0.01) / t.Pot).ToString("0.##") : "-";
                case "flopeffspr": return t != null && t.Pot > 0 ? (Math.Max(t.Stack, 0.01) / t.Pot).ToString("0.##") : "-";
                case "effstack": return t?.Stack.ToString("0.##") ?? "0";
                case "effstack_bb": return t != null ? BbOf(t.Stack) : "0";
                case "nplayers": return t?.MaxSeats.ToString() ?? "0";
                case "playersinhand": return t?.PlayersInHand.ToString() ?? "0";
                case "capt": return t?.TableName ?? "";
                case "tableidx": return t != null ? t.Slot.ToString() : "0";
                case "tableidxconst": return t != null ? (t.Slot + 1).ToString() : "0";
                case "startingchips": return t?.StartingChips.ToString("0.##") ?? "0";
                case "minstartstack": return t?.MinStartStack.ToString("0.##") ?? "0";
                case "avstartstack": return t?.AvStartStack.ToString("0.##") ?? "0";
                case "guarantee": return t?.Guarantee.ToString("0.##") ?? "0";
                case "blindstimer": return t?.BlindsTimerSec.ToString() ?? "0";
                case "timetoact": return t?.TimeToActSec.ToString() ?? "0";
                case "timefromact": return t?.TimeFromActSec.ToString("0") ?? "0";
                case "maxtimefromact": return "40";
                case "tm2_tablelocked": return t?.Locked == true ? "1" : "0";
                case "tm2_tablesinslot": return t?.TablesInSlot.ToString() ?? "1";
                case "sizing": return t?.Bet.ToString("0.##") ?? "0";

                // pot odds (definizioni originali, strings dell'exe):
                //  [:]  - pot odds for call (rapporto "x:1")
                //  [%]  - pot odds for call in %  = call / (pot + call) * 100
                //  [p%] - pot odds for push in %  = push / (pot + push) * 100
                case ":":
                    return t?.AmountToCall is double ratioCall && ratioCall > 0 && t.Pot > 0
                        ? (t.Pot / ratioCall).ToString("0.##") + ":1" : "-";
                case "%":
                    return t?.AmountToCall is double oddsCall && oddsCall > 0 && t.Pot > 0
                        ? (oddsCall / (t.Pot + oddsCall) * 100).ToString("0.#") : "-";
                case "p%":
                    return t != null && t.Stack > 0
                        ? (t.Stack / (t.Pot + t.Stack) * 100).ToString("0.#") : "-";

                // --- sessione/globale ---
                case "sessiontime": return SessionStats.FormatTime(s.SessionTime);
                // Il tag [allHandsCount] mostra le mani della sessione.
                // (il cumulativo resta nel file global_stats)
                case "handscount": return s.SessionHands.ToString();
                case "allhandscount": return s.SessionHands.ToString();
                case "allhandscounttotal": return s.AllHands.ToString();
                case "alltrnys": return s.AllTrnys.ToString();
                case "handshour": case "handsperhour": return s.HandsPerHour.ToString("0.#");

                // --- Generatore di valori casuali ---
                case "rng":
                    {
                        double v;
                        if (c.RngHeroAct && t != null) v = c.RngHeroActId % Math.Max(1, (c.RngTo - c.RngFrom + 1)) + c.RngFrom;
                        else v = c.Rng.Next(Math.Min(c.RngFrom, c.RngTo), Math.Max(c.RngFrom, c.RngTo) + 1);
                        return v.ToString("0.#");
                    }
                case "rng2":
                    return c.Rng.Next(Math.Min(c.Rng2From, c.Rng2To), Math.Max(c.Rng2From, c.Rng2To) + 1).ToString("0.#");
                case "blinds_timer": case "rngtimer":
                    {
                        if (!c.RngTimerEn) return "0";
                        if ((DateTime.UtcNow - c.RngTimerAt).TotalSeconds > c.RngTimerPeriod)
                        {
                            c.RngTimerAt = DateTime.UtcNow;
                            c.RngTimerValue = c.Rng.Next(Math.Min(c.RngFrom, c.RngTo), Math.Max(c.RngFrom, c.RngTo) + 1);
                        }
                        return c.RngTimerValue.ToString("0.#");
                    }

                // --- lobby TL_* ---
                default:
                    if (lower.StartsWith("tl_"))
                        return c.Lobby?.GetTag(tag) ?? "-";
                    return "[" + tag + "]"; // tag sconosciuto: lascialo visibile (diagnostica)
            }
        }

        private static TableInfo TableInfoFor(TagContext c) => c.Table;
    }
}
