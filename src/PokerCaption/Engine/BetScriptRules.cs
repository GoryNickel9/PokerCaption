using System.Globalization;
using System.Text.RegularExpressions;
using PokerCaption.Config;

namespace PokerCaption.Engine
{
    public sealed partial class BetScriptEngine
    {
        private const string RuleSection = "AdvancedBets";

        public sealed record Scenario(int Street, double Bb, double Sb, double Ante, double? Antes,
            double Pot, double? Bet, double? Raise, double Stack, double? EffectiveStack,
            double? Contributed = 0, double MinimumBet = 0,
            double? StreetStartPot = null, double? BetBox = null, double? AmountToCall = null);

        public sealed record Preview(int RuleIndex, double? Amount, string Text, string Error);

        /// <summary>Evaluate an explicit scenario using the first matching rule, in list order.</summary>
        public Preview EvaluateScenario(Scenario s)
        {
            double? max = s.Stack + s.Contributed;
            if (s.Street is not (1 or 2 or 4 or 8) || s.Bb <= 0 ||
                new double?[] { s.Bb, s.Sb, s.Ante, s.Antes, s.Pot, s.Bet, s.Raise, s.Stack,
                        s.EffectiveStack, s.Contributed, s.MinimumBet,
                        s.StreetStartPot, s.BetBox, s.AmountToCall, max }
                    .Any(n => n is double value && (!double.IsFinite(value) || value < 0)) ||
                s.MinimumBet > (max ?? s.Stack) || s.EffectiveStack > max)
                return new(-1, null, null, "Check the street and chip amounts.");
            double? call = s.AmountToCall ?? (s.Bet - s.Contributed);

            Preview invalidRule = null;
            for (int i = 0; i < Rules.Count; i++)
            {
                var rule = Rules[i];
                if ((rule.Streets & s.Street) == 0 || string.IsNullOrWhiteSpace(rule.Formula))
                    continue;

                var result = FormulaEval.Evaluate(rule.Formula, new FormulaContext
                {
                    Pot = s.Pot, Bet = s.Bet ?? double.NaN, Raise = s.Raise ?? double.NaN, Stack = s.Stack, Bb = s.Bb,
                    Sb = s.Sb, Ante = s.Ante, Antes = s.Antes ?? double.NaN, EffStack = s.EffectiveStack ?? double.NaN,
                    HeroBet = s.Contributed ?? double.NaN, Call = Math.Max(0, call ?? double.NaN),
                    Spot = s.StreetStartPot, BetBox = s.BetBox, Strict = true,
                });
                if (result.Error != null || !double.IsFinite(result.Value) || result.Value < 0)
                {
                    invalidRule ??= new(i, null, null, result.Error ??
                        "The formula must return a finite, non-negative amount.");
                    continue;
                }
                if (result.Hide) return new(i, null, "", null);

                double amount = result.Value;
                double step = rule.RoundPercent > 0 ? Math.Max(0.01, s.Bb * (rule.RoundPercent / 100)) : 0;
                if (step > 0 && double.IsFinite(amount / step))
                    amount = Math.Round(amount / step, MidpointRounding.AwayFromZero) * step;

                double pushRatio = rule.PushPercent / 100;
                if (max is double totalStack)
                {
                    if (amount > totalStack * pushRatio) amount = totalStack;
                }
                else if (amount > s.Stack * pushRatio || amount > s.Stack)
                {
                    // The remaining stack is a lower bound for the total stack.
                    continue;
                }
                amount = Math.Clamp(amount, s.MinimumBet, max ?? s.Stack);
                return new(i, amount, FormulaEval.Format(amount, result.Decimals), null);
            }
            return invalidRule ?? new(-1, null, null, null);
        }

        internal static bool TryReadAmount(string text, double fallback, out double value)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                value = fallback;
                return true;
            }
            return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value >= 0;
        }

        private static Rule ReadRule(IniFile ini, int index)
        {
            return new Rule
            {
                Streets = ini.GetInt(RuleSection, "Streets" + index),
                Formula = ini.Get(RuleSection, "Formula" + index, ""),
                RoundPercent = ReadPercent("RoundBet", 0),
                PushPercent = ReadPercent("PushIfBet", 100),
            };

            double ReadPercent(string key, double fallback)
            {
                double value = ini.GetDouble(RuleSection, key + index, fallback);
                return double.IsFinite(value) && value >= 0 ? value : fallback;
            }
        }

        internal static void WriteRules(IniFile ini, IReadOnlyList<Rule> rules)
        {
            var section = ini.GetOrAddSection(RuleSection);
            var indexed = new Regex(@"^(Streets|Formula|RoundBet|PushIfBet|Comment|Positions|GameType|Cash|Tourney|Zoom|Spin|Psko|Ko|(bbSize|Ante|PotSize|BetSize|StackSize|Players)L?)\d+$",
                RegexOptions.IgnoreCase);
            section.Entries.RemoveAll(e => indexed.IsMatch(e.Key) ||
                string.Equals(e.Key, "PushIfBetUsesEffStack", StringComparison.OrdinalIgnoreCase));
            ini.Set(RuleSection, "Count", rules.Count);
            for (int i = 0; i < rules.Count; i++)
            {
                int index = i;
                var rule = rules[i];
                ini.Set(RuleSection, "Streets" + i, rule.Streets);
                ini.Set(RuleSection, "Formula" + i, rule.Formula ?? "");
                Write("RoundBet", rule.RoundPercent == 0 ? null : rule.RoundPercent);
                Write("PushIfBet", rule.PushPercent == 100 ? null : rule.PushPercent);

                void Write(string key, double? value)
                {
                    if (value is double n)
                        ini.Set(RuleSection, key + index, n.ToString(CultureInfo.InvariantCulture));
                }
            }
        }
    }
}
