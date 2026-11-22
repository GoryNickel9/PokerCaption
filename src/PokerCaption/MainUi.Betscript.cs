using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using PokerCaption.Engine;

namespace PokerCaption
{
    public sealed partial class MainUiController
    {
        // evita rientranza: mentre il pannello proprietà viene riempito dalla
        // selezione, gli eventi TextChanged/ItemCheck devono restare inerti
        private bool _betsPanelUpdating;

        internal void AttachBetscript(Form f)
        {
            FillBetscriptList(f, Program.Ini);
            try
            {
                var list = DesktopWindow.Descendants(f).OfType<ListBox>()
                    .FirstOrDefault(l => string.Equals(l.Name, "ListBox4", StringComparison.OrdinalIgnoreCase));
                var btnAdd = Ctl(f, "SpeedButton9") as Button;      // 'Add new bet'
                var btnClone = Ctl(f, "SpeedButton13") as Button;   // 'Clone bet'
                var btnDelete = Ctl(f, "SpeedButton10") as Button;  // 'Delete'

                if (list == null) { Log("betscript: ListBox4 mancante, wiring tab annullato"); return; }

                if (btnAdd != null) btnAdd.Click += (s, e) => BetAdd(f);
                else Log("betscript: SpeedButton9 (Add new bet) mancante");
                if (btnClone != null) btnClone.Click += (s, e) => BetClone(f);
                else Log("betscript: SpeedButton13 (Clone bet) mancante");
                if (btnDelete != null) btnDelete.Click += (s, e) => BetDelete(f);
                else Log("betscript: SpeedButton10 (Delete) mancante");

                // selezione nella lista → pannello proprietà; doppio click → nuova regola
                list.SelectedIndexChanged += (s, e) => BetSelectedChanged(f);
                list.DoubleClick += (s, e) => BetAdd(f);

                var editFormula = Ctl(f, "Edit16") as TextBox;            // Formula
                if (editFormula != null) editFormula.TextChanged += (s, e) => BetFormulaEdited(f);
                else Log("betscript: Edit16 (Formula) mancante");

                var streets = Ctl(f, "CheckListBox2") as CheckedListBox;  // Preflop/Flop/Turn/River
                if (streets != null) streets.ItemCheck += (s, e) => BetStreetChanged(f, streets, e);
                else Log("betscript: CheckListBox2 (Street) mancante");

                Ctl(f, "BetFormulaHelp").Click += (_, _) => ShowBetFormulaHelp(f);

                BetSelectedChanged(f); // allinea il pannello alla selezione corrente
            }
            catch (Exception ex) { Log("betscript wiring ERR: " + ex.Message); }
        }

        // ---------- pannello proprietà ----------
        private void BetSelectedChanged(Form f)
        {
            if (_betsPanelUpdating) return;
            try
            {
                var list = Ctl(f, "ListBox4") as ListBox;
                if (list == null) return;
                int i = list.SelectedIndex;
                var panel = Ctl(f, "GroupBox9");
                if (i < 0)
                {
                    if (panel != null) panel.Visible = false;
                    return;
                }
                var rules = LoadBetscriptRules();
                if (i >= rules.Count) return;

                _betsPanelUpdating = true;
                try
                {
                    if (panel != null) panel.Visible = true;
                    var edit = Ctl(f, "Edit16") as TextBox;
                    if (edit != null) edit.Text = rules[i].Formula ?? "";
                    var streets = Ctl(f, "CheckListBox2") as CheckedListBox;
                    if (streets != null)
                        for (int k = 0; k < streets.Items.Count; k++)
                            streets.SetItemChecked(k, (rules[i].Streets & (1 << k)) != 0);
                }
                finally { _betsPanelUpdating = false; }
            }
            catch (Exception ex) { Log("betscript sel ERR: " + ex.Message); }
        }

        private void BetFormulaEdited(Form f)
        {
            if (_betsPanelUpdating) return;
            try
            {
                var list = Ctl(f, "ListBox4") as ListBox;
                var edit = Ctl(f, "Edit16") as TextBox;
                if (list == null || edit == null) return;
                int i = list.SelectedIndex;
                if (i < 0) return;
                var rules = LoadBetscriptRules();
                if (i >= rules.Count) return;
                rules[i].Formula = edit.Text;
                SaveBetscriptIni(rules);
                list.Items[i] = BetscriptRuleText(rules[i].Streets, rules[i].Formula);
            }
            catch (Exception ex) { Log("betscript formula ERR: " + ex.Message); }
        }

        private void BetStreetChanged(Form f, CheckedListBox clb, ItemCheckEventArgs e)
        {
            if (_betsPanelUpdating) return;
            try
            {
                var list = Ctl(f, "ListBox4") as ListBox;
                if (list == null || clb == null) return;
                int i = list.SelectedIndex;
                if (i < 0) return;
                var rules = LoadBetscriptRules();
                if (i >= rules.Count) return;
                int mask = 0;
                for (int k = 0; k < clb.Items.Count; k++)
                {
                    bool on = k == e.Index ? e.NewValue == CheckState.Checked : clb.GetItemChecked(k);
                    if (on) mask |= 1 << k;
                }
                rules[i].Streets = mask;
                SaveBetscriptIni(rules);
                list.Items[i] = BetscriptRuleText(mask, rules[i].Formula);
                Log("betscript: regola " + (i + 1) + " streets -> " + mask);
            }
            catch (Exception ex) { Log("betscript streets ERR: " + ex.Message); }
        }

        // ---------- CRUD ----------
        private void BetAdd(Form f)
        {
            try
            {
                var rules = LoadBetscriptRules();
                rules.Add(new BetScriptEngine.Rule { Streets = 1, Formula = "pot*1" });
                SaveBetscriptIni(rules);
                RefreshBetList(f, rules);
                var list = Ctl(f, "ListBox4") as ListBox;
                if (list != null && rules.Count > 0) list.SelectedIndex = rules.Count - 1;
                Log("betscript: nuova regola #" + rules.Count + " (PREFLOP pot*1)");
            }
            catch (Exception ex) { Log("betscript add ERR: " + ex.Message); }
        }

        private void BetClone(Form f)
        {
            try
            {
                var list = Ctl(f, "ListBox4") as ListBox;
                if (list == null) return;
                var rules = LoadBetscriptRules();
                int i = list.SelectedIndex;
                if (i < 0 || i >= rules.Count) { Log("betscript: clone senza regola selezionata"); return; }
                rules.Add(rules[i].Copy());
                SaveBetscriptIni(rules);
                RefreshBetList(f, rules);
                list.SelectedIndex = rules.Count - 1;
                Log("betscript: clonata regola " + (i + 1) + " -> #" + rules.Count);
            }
            catch (Exception ex) { Log("betscript clone ERR: " + ex.Message); }
        }

        private void BetDelete(Form f)
        {
            try
            {
                var list = Ctl(f, "ListBox4") as ListBox;
                if (list == null) return;
                var rules = LoadBetscriptRules();
                int i = list.SelectedIndex;
                if (i < 0 || i >= rules.Count) { Log("betscript: delete senza regola selezionata"); return; }
                string desc = BetscriptRuleText(rules[i].Streets, rules[i].Formula);
                rules.RemoveAt(i);
                SaveBetscriptIni(rules);
                RefreshBetList(f, rules);
                list.SelectedIndex = Math.Min(i, rules.Count - 1); // -1 se vuota: deseleziona
                Log("betscript: eliminata regola " + (i + 1) + " (" + desc + ")");
            }
            catch (Exception ex) { Log("betscript delete ERR: " + ex.Message); }
        }

        // ---------- lista e persistenza ----------
        /// <summary>Riscrive ListBox4 dalle regole col formato "PREFLOP pot*2", mantenendo la selezione.</summary>
        private static void RefreshBetList(Form f, List<BetScriptEngine.Rule> rules)
        {
            try
            {
                var lb = Ctl(f, "ListBox4") as ListBox;
                if (lb == null) return;
                int sel = lb.SelectedIndex;
                lb.BeginUpdate();
                try
                {
                    lb.Items.Clear();
                    if (rules != null)
                        foreach (var r in rules)
                            lb.Items.Add(BetscriptRuleText(r.Streets, r.Formula));
                }
                finally { lb.EndUpdate(); }
                if (rules != null && rules.Count > 0)
                    lb.SelectedIndex = Math.Max(0, Math.Min(sel, rules.Count - 1));
            }
            catch (Exception ex) { Log("betscript refresh ERR: " + ex.Message); }
        }

        /// <summary>Legge le regole [AdvancedBets] direttamente dall'INI.</summary>
        private static List<BetScriptEngine.Rule> LoadBetscriptRules()
        {
            var bs = new BetScriptEngine();
            if (Program.Ini != null) bs.Load(Program.Ini);
            return bs.Rules;
        }

        /// <summary>
        /// Riscrive DIRETTAMENTE le chiavi [AdvancedBets] (Count, Streets_i, Formula_i,
        /// reindicizzati da 0), salva su PokerCaption.ini e risincronizza il motore.
        /// </summary>
        private static void SaveBetscriptIni(List<BetScriptEngine.Rule> rules)
        {
            if (Program.Ini == null) return;
            Program.Ini.SaveChanges("AdvancedBets", _ => BetScriptEngine.WriteRules(Program.Ini, rules));
            try { Program.Service?.Betscript?.Load(Program.Ini); }
            catch (Exception ex) { Log("betscript sync ERR: " + ex.Message); }
        }

        /// <summary>Controllo per nome (case-insensitive) nella form.</summary>
        private static Control Ctl(Form f, string name) =>
            DesktopWindow.Descendants(f).FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
