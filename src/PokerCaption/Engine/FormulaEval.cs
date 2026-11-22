using System;
using System.Globalization;

namespace PokerCaption.Engine
{
    /// <summary>
    /// Contesto di valutazione: i valori correnti del tavolo (Fase 5).
    /// </summary>
    public sealed class FormulaContext
    {
        public double Pot;
        public double Stack;      // stack hero
        public double Bet;        // bet da chiamare
        public double Raise;      // last raise
        public double Bb;
        public double Sb;
        public double EffStack;   // effective stack
        public double Ante, Antes, HeroBet, Call;
        public double? Spot, BetBox;
        internal bool Strict;

        public double Get(string name)
        {
            double value = name.ToLowerInvariant() switch
            {
                "pot" => Pot,
                "stack" => Stack,
                "bet" => Bet,
                "raise" => Raise,
                "bb" => Bb,
                "sb" => Sb,
                "effstack" => Strict || EffStack > 0 ? EffStack : Math.Min(Stack, Pot),
                "ante" => Ante,
                "antes" => Antes,
                "hbet" => HeroBet,
                "call" => Call,
                "spot" => Spot ?? double.NaN,
                "betbox" => BetBox ?? double.NaN,
                _ => double.NaN,
            };
            if (!double.IsNaN(value)) return value;
            if (Strict) throw new FormatException($"Unknown or unavailable variable: {name}");
            return 0;
        }
    }

    /// <summary>
    /// Valutatore delle formule del betscript:
    /// numeri, pot/stack/bet/raise/bb/sb/effStack, + - * / ( ),
    /// moltiplicatore x0.75, suffisso bb, decimali @N, @H hide.
    /// Esempi originali: pot*2, pot*0.33, pot*1.5, 0.33*pot, pot*0.75bb@2
    /// </summary>
    public static class FormulaEval
    {
        public sealed class Result
        {
            public double Value;
            public int Decimals = 2;   // @N
            public bool Hide = false;  // @H (non scrivere il numero: solo azione)
            public string Error;
        }

        public static Result Evaluate(string formula, FormulaContext ctx)
        {
            var r = new Result();
            if (string.IsNullOrWhiteSpace(formula)) { r.Error = "formula vuota"; return r; }
            try
            {
                int pos = 0;
                // @H / @N possono stare in qualunque punto: li estraiamo prima
                if (formula.Length > 512) throw new FormatException("Formula is too long.");
                string expr = formula;
                int h = expr.IndexOf("@H", StringComparison.OrdinalIgnoreCase);
                if (h >= 0) { r.Hide = true; expr = expr.Remove(h, 2); }
                int at = expr.LastIndexOf('@');
                if (at >= 0 && at < expr.Length - 1 && char.IsDigit(expr[at + 1]))
                {
                    int p = at + 1;
                    while (p < expr.Length && char.IsDigit(expr[p])) p++;
                    if (!int.TryParse(expr.AsSpan(at + 1, p - at - 1), out r.Decimals) || r.Decimals > 15)
                        throw new FormatException("Use 0–15 decimal places.");
                    expr = expr.Remove(at, p - at);
                }

                double v = ParseExpr(expr, ref pos, ctx);
                if (pos < expr.Length)
                {
                    // suffisso bb dopo l'espressione completa (es. "2.5bb")
                    if (pos + 1 < expr.Length && (expr[pos] == 'b' || expr[pos] == 'B') &&
                        (expr[pos + 1] == 'b' || expr[pos + 1] == 'B'))
                    {
                        v *= ctx.Bb > 0 ? ctx.Bb : 1;
                        pos += 2;
                        SkipWhitespace(expr, ref pos);
                    }
                }
                if (pos < expr.Length) { r.Error = "carattere inatteso @" + pos + ": " + expr.Substring(pos); return r; }
                r.Value = v;
                return r;
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
                return r;
            }
        }

        // expr := term { (+|-) term }
        private static double ParseExpr(string s, ref int p, FormulaContext c)
        {
            double v = ParseTerm(s, ref p, c);
            while (p < s.Length && (s[p] == '+' || s[p] == '-'))
            {
                char op = s[p++];
                double t = ParseTerm(s, ref p, c);
                v = op == '+' ? v + t : v - t;
            }
            return v;
        }

        // term := factor { (*|/) factor } — include "x0.75" come moltiplicatore
        private static double ParseTerm(string s, ref int p, FormulaContext c)
        {
            double v = ParsePower(s, ref p, c);
            while (p < s.Length)
            {
                char ch = s[p];
                if (ch == '*' || ch == '/')
                {
                    p++;
                    double f = ParsePower(s, ref p, c);
                    if (ch == '/' && f == 0 && c.Strict) throw new FormatException("Division by zero.");
                    v = ch == '*' ? v * f : c.Strict || Math.Abs(f) >= 1e-12 ? v / f : 0;
                }
                else if (ch == 'x' || ch == 'X')
                {
                    // moltiplicatore esplicito "x0.75" applicato al valore corrente
                    p++;
                    double f = ParsePower(s, ref p, c);
                    v *= f;
                }
                else break;
            }
            return v;
        }

        private static double ParsePower(string s, ref int p, FormulaContext c)
        {
            SkipWhitespace(s, ref p);
            if (p < s.Length && (s[p] == '-' || s[p] == '+'))
            {
                char sign = s[p++];
                double operand = ParsePower(s, ref p, c);
                return sign == '-' ? -operand : operand;
            }
            double value = ParseFactor(s, ref p, c);
            SkipWhitespace(s, ref p);
            if (p == s.Length || s[p] != '^') return value;
            p++;
            return Math.Pow(value, ParsePower(s, ref p, c));
        }

        private static double ParseFactor(string s, ref int p, FormulaContext c)
        {
            SkipWhitespace(s, ref p);
            if (p >= s.Length) throw new FormatException("espressione incompleta");
            if (s[p] == '(')
            {
                p++;
                double v = ParseExpr(s, ref p, c);
                if (p >= s.Length || s[p] != ')') throw new FormatException("manca )");
                p++;
                return v;
            }
            if (c.Strict && (s[p] == 'x' || s[p] == 'X'))
            {
                p++;
                double factor = ParseFactor(s, ref p, c);
                double call = c.Get("call");
                return c.Get("hbet") + call + (c.Pot + call) * factor;
            }
            // numero con eventuale suffisso bb
            int start = p;
            while (p < s.Length && (char.IsDigit(s[p]) || s[p] == '.' || s[p] == ',')) p++;
            if (p > start)
            {
                string num = s.Substring(start, p - start).Replace(',', '.');
                double v = double.Parse(num, CultureInfo.InvariantCulture);
                // suffisso bb immediato sul numero
                if (p + 1 < s.Length && (s[p] == 'b' || s[p] == 'B') && (s[p + 1] == 'b' || s[p + 1] == 'B'))
                {
                    v *= c.Bb > 0 ? c.Bb : 1;
                    p += 2;
                }
                return v;
            }
            // identificatore (pot, stack, …); "potx0.5" → pot x 0.5
            start = p;
            while (p < s.Length && (char.IsLetter(s[p]) || s[p] == '_')) p++;
            if (p > start)
            {
                string id = s.Substring(start, p - start);
                // identificatore che finisce con "x" (es. "potx0.5"): il prefisso è
                // la variabile, la "x" è il moltiplicatore gestito in ParseTerm
                if (id.Length > 1 && (id.EndsWith("x") || id.EndsWith("X")))
                {
                    var prefix = id.Substring(0, id.Length - 1).ToLowerInvariant();
                    if (prefix == "pot" || prefix == "stack" || prefix == "bet" || prefix == "raise" ||
                        prefix == "bb" || prefix == "sb" || prefix == "effstack")
                    {
                        p = start + id.Length - 1;
                        return c.Get(prefix);
                    }
                }
                return c.Get(id);
            }
            throw new FormatException("token inatteso '" + s[p] + "' @" + p);
        }

        private static void SkipWhitespace(string s, ref int p)
        {
            while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
        }

        /// <summary>Formatta il valore secondo @N (o 2 decimali di default), senza zeri finali superflui.</summary>
        public static string Format(double v, int decimals)
        {
            var s = v.ToString("F" + Math.Max(0, decimals), CultureInfo.InvariantCulture);
            if (s.Contains("."))
            {
                s = s.TrimEnd('0').TrimEnd('.');
            }
            return s;
        }
    }
}
