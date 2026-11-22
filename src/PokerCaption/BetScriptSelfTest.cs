using System.Windows.Forms;
using PokerCaption.Config;
using PokerCaption.Engine;

namespace PokerCaption
{
    internal static class BetScriptSelfTest
    {
        internal static void Run(Action<string, bool, string> check)
        {
            string path = TestPath();
            try
            {
                var ini = new IniFile(path);
                const string section = "AdvancedBets";
                foreach (var (key, value) in new (string, string)[]
                {
                    ("Count", "3"), ("Streets0", "3"), ("Formula0", "1.5bb"),
                    ("bbSizeL0", "25"), ("bbSize0", "100"), ("Ante0", "25"),
                    ("BetSizeL0", "0"), ("BetSize0", "50"), ("Players0", "6"),
                    ("RoundBet0", "100"), ("PushIfBet0", "40"), ("Positions0", "0"), ("Comment0", "old"),
                    ("PushIfBetUsesEffStack", "1"), ("PotSize0", "0"),
                    ("Streets1", "3"), ("Formula1", ""), ("Streets2", "3"), ("Formula2", "pot / 2"),
                })
                    ini.Set(section, key, value);
                var engine = new BetScriptEngine();
                engine.Load(ini);
                var scenario = new BetScriptEngine.Scenario(1, 1, 0.5, 25, 150, 5, 1, 1, 10, 4);
                var result = engine.EvaluateScenario(scenario);
                check("BetScript uses street priority and ignores removed INI settings",
                    result.RuleIndex == 0 && result.Amount == 2 &&
                    engine.EvaluateScenario(scenario with { Street = 4 }).RuleIndex == -1, result.Text ?? "");
                engine.Rules[0].PushPercent = 15;
                check("BetScript rounded value triggers full shove using hero stack",
                    engine.EvaluateScenario(scenario with { Contributed = 1 }).Amount == 11, "");
                check("BetScript skips a shove if hero contribution is missing",
                    engine.EvaluateScenario(scenario with { Contributed = null }).RuleIndex == 2, "");
                engine.Rules[0].PushPercent = 20;
                check("BetScript strict push threshold and minimum bet",
                    engine.EvaluateScenario(scenario with { MinimumBet = 3 }).Amount == 3, "");
                engine.Rules[0].Formula = "unknown + 1";
                check("BetScript invalid formula falls through", engine.EvaluateScenario(scenario).RuleIndex == 2, "");
                engine.Rules[0].Formula = "1 2";
                check("BetScript keeps distinct formula tokens separated", engine.EvaluateScenario(scenario).RuleIndex == 2, "");
                engine.Rules[0].Formula = "pot + 1bb - antes + 2^2";
                engine.Rules[0].RoundPercent = 0;
                engine.Rules[0].PushPercent = 100;
                check("BetScript documented formula", engine.EvaluateScenario(scenario with { Antes = 8 }).Amount == 2, "");
                engine.Rules[0].Formula = "x(1/2) + 1bb";
                check("BetScript pot-size prefix", engine.EvaluateScenario(scenario with { Contributed = 0.5, EffectiveStack = 10 }).Amount == 4.75, "");

                var copy = engine.Rules[0].Copy();
                engine.Rules[0].Formula = "changed";
                BetScriptEngine.WriteRules(ini, [engine.Rules[2], copy]);
                ini.Save();
                var loaded = IniFile.Load(path);
                engine.Load(loaded);
                check("BetScript INI round trip, clone and reindex",
                    engine.Rules.Count == 2 && engine.Rules[1].Formula == "x(1/2) + 1bb" &&
                    loaded.Get(section, "bbSizeL0") == null && loaded.Get(section, "PushIfBetUsesEffStack") == null &&
                    loaded.Get(section, "Formula2") == null && loaded.Get(section, "Positions0") == null &&
                    loaded.Get(section, "Comment0") == null, "");
            }
            finally { File.Delete(path); }
        }

        internal static void CheckEditor(Form form, Action<string, bool, string> check)
        {
            string path = TestPath();
            var original = Program.Ini;
            try
            {
                Program.Ini = new IniFile(path);
                BetScriptEngine.WriteRules(Program.Ini,
                    [new() { Streets = 1, Formula = "pot*2" }, new() { Streets = 2, Formula = "pot*0.33" }]);
                new MainUiController().AttachBetscript(form);

                var panel = form.Controls.Find("GroupBox9", true).Single();
                check("BetScript editor removes conditions and effective stack toggle",
                    !panel.Controls.OfType<ComboBox>().Any() &&
                    form.Controls.Find("CheckBox554", true).Length == 0, "");
                var list = (ListBox)form.Controls.Find("ListBox4", true).Single();
                list.SelectedIndex = 1;
                check("BetScript editor selects formula and default sizing",
                    panel.Controls.Find("Edit16", true).Single().Text == "pot*0.33" &&
                    panel.Controls.Find("Edit27", true).Single().Text == "0" &&
                    panel.Controls.Find("Edit28", true).Single().Text == "100", "");
                Control container = panel;
                while (container is not TabPage) container = container.Parent;
                var page = (TabPage)container;
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.Show();
                ((TabControl)page.Parent).SelectedTab = page;
                check("BetScript editor has no scenario preview",
                    form.Controls.Find("BetscriptPreview", true).Length == 0, "");
                var rules = list.Parent;
                check("BetScript rule list and settings fit in separate columns",
                    rules.Right < panel.Left && panel.Right <= panel.Parent.ClientRectangle.Right &&
                    new[] { rules, panel }.All(section => section.Controls.Cast<Control>().All(
                        control => section.ClientRectangle.Contains(control.Bounds))), "");

                var round = (TextBox)panel.Controls.Find("Edit27", true).Single();
                round.Text = "25";
                check("BetScript editor saves rounding",
                    form.ValidateChildren() && IniFile.Load(path).Get("AdvancedBets", "RoundBet1") == "25", "");
                list.SelectedIndex = 0;
                bool firstUnrounded = round.Text == "0";
                list.SelectedIndex = 1;
                check("BetScript editor restores rounding per rule", firstUnrounded && round.Text == "25", "");

                var help = (Button)panel.Controls.Find("BetFormulaHelp", true).Single();
                bool guideShown = false;
                form.BeginInvoke(() =>
                {
                    var dialog = Application.OpenForms["BetFormulaGuide"];
                    if (dialog == null) return;
                    try
                    {
                        var guide = (TextBox)dialog.Controls.Find("FormulaGuideText", true).Single();
                        guideShown = guide.ReadOnly && guide.Text.Contains("x1") && guide.Text.Contains("effStack");
                    }
                    finally { dialog.Close(); }
                });
                help.PerformClick();
                check("BetScript formula guide opens from the question mark", help.Text == "?" && guideShown, "");
            }
            finally
            {
                Program.Ini = original;
                File.Delete(path);
                File.Delete(Path.ChangeExtension(path, ".ini_bak"));
            }
        }

        internal static void CheckPrefill(Action<string, bool, string> check)
        {
            var rule = new BetScriptEngine.Rule { Streets = 2, Formula = "pot / 2", RoundPercent = 100, PushPercent = 50 };
            var engine = new BetScriptEngine { Enabled = true };
            engine.Rules.Add(rule);
            using var editor = new TextBox { Text = "1" };
            var table = new TableInfo
            {
                Editor = editor.Handle, HeroAct = true, Street = 2, Bb = 1, Sb = 0.5, Pot = 5,
                Stack = 5, AmountToCall = 0, MaxSeats = 2,
            };
            table.SeatStacks.Add(1, 5);
            table.SeatStacks.Add(2, 7);

            var preview = engine.EvaluateScenario(BetScriptEngine.ScenarioForTable(table, 1));
            check("BetScript prefill matches preview after rounding and shove",
                preview.Text == "5" && engine.Apply(table) == preview.Text && editor.Text == preview.Text, editor.Text);
            editor.Text = "4";
            check("BetScript prefill preserves manual edits on the same turn",
                engine.Apply(table) == null && editor.Text == "4", editor.Text);
            table.BetScriptTurn++;
            table.AmountToCall = 1;
            table.MaxSeats = 6;
            rule.Formula = "raise * 3";
            check("BetScript leaves bet box unchanged when a formula value is unavailable",
                engine.Apply(table) == null && editor.Text == "4", editor.Text);
            rule.Formula = "2bb";
            check("BetScript prefills without bet or dealt player data when the formula does not need them",
                engine.Apply(table) == "2" && editor.Text == "2", editor.Text);
            table.BetScriptTurn++;
            rule.Formula = "pot@H";
            check("BetScript hide leaves existing text in place",
                engine.Apply(table) == null && editor.Text == "2", editor.Text);
            check("BetScript rejects calibrated action points as the bet box",
                !BetScriptEngine.IsBetBoxPoint(ClientProfile.Points["Raise"]) &&
                !BetScriptEngine.IsBetBoxPoint(ClientProfile.Points["Call"]), "");
        }

        private static string TestPath() => Path.Combine(Path.GetTempPath(), "poker-caption-bets-" + Guid.NewGuid() + ".ini");
    }
}
