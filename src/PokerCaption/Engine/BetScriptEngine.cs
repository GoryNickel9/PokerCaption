using System;
using System.Collections.Generic;
using PokerCaption.Config;

namespace PokerCaption.Engine
{
    /// <summary>
    /// Motore betscript: valuta le regole [AdvancedBets]
    /// sulla street corrente e scrive la bet box. Formato reale della config:
    ///   [AdvancedBets] Count=N, Streets<i>=bitmask (1 preflop, 2 flop, 4 turn, 8 river),
    ///   Formula<i>=pot*2 / pot*0.33 / 0.33*pot …
    /// </summary>
    public sealed partial class BetScriptEngine
    {
        public sealed class Rule
        {
            public int Streets;
            public string Formula;
            public double RoundPercent;
            public double PushPercent = 100;
            internal Rule Copy() => (Rule)MemberwiseClone();
        }

        public readonly List<Rule> Rules = new List<Rule>();
        public bool Log;
        public int SpinDelayMs = 1000;

        private readonly Action<string> _log;

        public BetScriptEngine(Action<string> log = null) { _log = log ?? (_ => { }); }

        public void Load(IniFile ini)
        {
            Rules.Clear();
            int count = ini.GetInt(RuleSection, "Count", 0);
            for (int i = 0; i < count; i++)
                Rules.Add(ReadRule(ini, i));
            Log = ini.GetBool("PokerCaption", "BetscriptLog", false);
            SpinDelayMs = Math.Max(0, ini.GetInt("PokerCaption", "StartSpinDelay", 1000));
        }

        /// <summary>Restituisce la prima formula valida per la street corrente.</summary>
        public string FormulaFor(int streetMask)
        {
            foreach (var r in Rules)
                if ((r.Streets & streetMask) != 0 && !string.IsNullOrWhiteSpace(r.Formula))
                    return r.Formula;
            return null;
        }

        /// <summary>
        /// Precompila l'importo una volta per turno di azione.
        /// </summary>
        public string Apply(TableInfo t)
        {
            if (t == null || !t.HeroAct) return null;
            int turn = t.BetScriptTurn;
            if (t.LastPrefilledTurn == turn || t.Pot <= 0 || t.Stack <= 0) return null;
            bool coords = t.Editor == IntPtr.Zero;
            if (coords && (ClientProfile.Interaction != "coords" || Native.NativeMethods.IsInMoveSize(t.Hwnd))) return null;

            string currentText = coords ? null : TableScanner.GetText(t.Editor, 64);
            double? betBox = !string.IsNullOrWhiteSpace(currentText) &&
                TryReadAmount(currentText, 0, out double current) ? current : null;
            var result = EvaluateScenario(ScenarioForTable(t, betBox));
            if (result.Error != null)
            {
                if (Log) _log($"bet[{t.TableName}] {result.Error}");
                return null;
            }
            if (result.Amount == null || !t.HeroAct || t.BetScriptTurn != turn) return null;

            string text = FormatForBetBox(result, t, coords);
            if (coords)
            {
                if (!ClientProfile.Points.TryGetValue("BetBox", out var box)) return null;
                box = BetBoxPoint(box);
                if (!IsBetBoxPoint(box) ||
                    (!CoordsEngine.TypeInto(t.Hwnd, box.X, box.Y, text, foregroundOnly: true) &&
                     !CoordsEngine.PostAmount(t.Hwnd, box.X, box.Y, text)))
                {
                    if (Log) _log($"bet[{t.TableName}] bet box unavailable; amount '{text}' was not sent");
                    return null;
                }
            }
            else if (currentText != text && !Input.SetBetBox(t.Editor, text)) return null;

            t.LastPrefilledTurn = turn;
            t.BetBoxText = text;
            if (Log) _log($"bet[{t.TableName}] street={t.Street} rule={result.RuleIndex + 1} -> '{text}'");
            return text;
        }

        internal static string FormatForBetBox(Preview result, TableInfo table, bool coords) =>
            FormulaEval.Format(AmountForBetBox(result.Amount.Value, table) /
                (coords && table.BetBoxInBb ? table.Bb : 1), result.Decimals);

        internal static double AmountForBetBox(double amount, TableInfo table)
        {
            // If the formula cannot even cover the call, use the legal raise shown by the client.
            if (table.AmountToCall is double call && call > 0 && amount <= call &&
                table.DisplayedRaiseTo is double raiseTo && raiseTo > call)
                return raiseTo;
            return amount;
        }

        // The previous stock point overlaps the slider at the standard slot size.
        internal static (float X, float Y) BetBoxPoint((float X, float Y) point) =>
            point == (0.730f, 0.865f) ? (0.697f, 0.872f) : point;

        internal static Scenario ScenarioForTable(TableInfo t, double? betBox)
        {
            bool noPostflopBet = t.Street != 1 && t.AmountToCall == 0;
            bool onlyBlinds = t.Street == 1 && t.Ante == 0 && t.AmountToCall is double call &&
                call >= 0 && call <= t.Bb && Math.Abs(t.Pot - t.Bb - t.Sb) < 1e-8;
            double? bet = noPostflopBet ? 0 : onlyBlinds ? t.Bb : null;
            double? contributed = bet - t.AmountToCall;

            double? effectiveStack = null;
            bool headsUp = t.MaxSeats == 2 && t.SeatStacks.Count == 2 && t.AmountToCall != null;
            if (headsUp && noPostflopBet) effectiveStack = Math.Min(t.Stack, t.SeatStacks.Values.Min());
            double? antes = t.Ante == 0 ? 0 : headsUp ? t.Ante * 2 : null;
            return new Scenario(t.Street, t.Bb, t.Sb, t.Ante, antes, t.Pot, bet, bet, t.Stack,
                effectiveStack, Contributed: contributed, StreetStartPot: noPostflopBet ? t.Pot : null,
                BetBox: betBox, AmountToCall: t.AmountToCall);
        }

        internal static bool IsBetBoxPoint((float X, float Y) point) =>
            point.X > 0 && point.X < 1 && point.Y > 0 && point.Y < 1 &&
            new[] { "Raise", "Call", "Check", "Fold" }.All(key =>
                ClientProfile.Points.TryGetValue(key, out var action) &&
                (Math.Abs(action.X - point.X) >= 0.05 || Math.Abs(action.Y - point.Y) >= 0.05));

    }
}
