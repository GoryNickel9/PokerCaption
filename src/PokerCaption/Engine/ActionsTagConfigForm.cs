using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PokerCaption.Config;

namespace PokerCaption.Engine
{
    /// <summary>
    /// Dialog '[actions] tag config'.
    ///  - griglia Action symbols: Action | Symbol | Text color | Background
    ///    (doppio click su una cella colore → ColorDialog; click destro → 'default')
    ///  - Timings: Add timings / Color + Set color / Use Tag color range of [timing] tag
    /// Persistenza su [HUD] con chiavi semantiche actions*:
    /// actionsSym&lt;Action&gt;, actionsTextColor&lt;Action&gt;, actionsBackColor&lt;Action&gt;,
    /// actionsAddTimings, actionsTimingsColorOn/Color/UseRange.
    /// </summary>
    public sealed class ActionsTagConfigForm : Form
    {
        private readonly IniFile _ini;
        private readonly Action<string> _log;

        // Righe della griglia: azione e simbolo predefinito.
        private static readonly (string action, string symbol)[] Rows =
        {
            ("Fold", "F"), ("Check", "X"), ("1st limp", "L"), ("Overlimp", "OL"),
            ("Call", "C"), ("Overcall", "O"), ("Call 3+ bet", "C"), ("Bet", "B"),
            ("Donk", "D"), ("Open raise", "oR"), ("Raise", "R"), ("3+ bet", "#b"),
            ("Post dead", "P"), ("Street divider", "|"), ("Isolate", "")
        };

        private DataGridView _grid;
        private CheckBox _addTimings, _timingsColorOn, _timingsUseRange;
        private readonly Dictionary<string, Color> _textColors = new();
        private readonly Dictionary<string, Color> _backColors = new();

        public ActionsTagConfigForm(IniFile ini, Action<string> log)
        {
            _ini = ini; _log = log ?? (_ => { });
            Text = "[actions] tag config";
            Icon = ProductBrand.Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(690, 380);
            Font = new Font("Tahoma", 8.25f);

            BuildActionSymbolsGrid();
            BuildTimings();
            BuildHints();

            var ok = new Button { Text = "Ok", Location = new Point(10, ClientSize.Height - 32), Size = new Size(75, 26) };
            var cancel = new Button { Text = "Cancel", Location = new Point(95, ClientSize.Height - 32), Size = new Size(75, 26) };
            var apply = new Button { Text = "Apply", Location = new Point(180, ClientSize.Height - 32), Size = new Size(75, 26) };
            ok.Click += (s, e) => { Save(); DialogResult = DialogResult.OK; };
            apply.Click += (s, e) => Save();
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.Add(ok); Controls.Add(cancel); Controls.Add(apply);
            AcceptButton = ok; CancelButton = cancel;

            LoadValues();
        }

        // ---------- griglia Action symbols ----------
        private void BuildActionSymbolsGrid()
        {
            var group = new GroupBox { Text = "Action symbols", Location = new Point(8, 8), Size = new Size(330, 330) };
            _grid = new DataGridView
            {
                Location = new Point(8, 16),
                Size = new Size(314, 300),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
            };
            _grid.Columns.Add("action", "Action");
            _grid.Columns.Add("symbol", "Symbol");
            _grid.Columns.Add("textColor", "Text color");
            _grid.Columns.Add("backColor", "Background");
            _grid.Columns["action"].ReadOnly = true;
            _grid.Columns["action"].FillWeight = 30;
            _grid.Columns["symbol"].FillWeight = 18;
            _grid.Columns["textColor"].FillWeight = 26;
            _grid.Columns["backColor"].FillWeight = 26;

            foreach (var (action, symbol) in Rows)
                _grid.Rows.Add(action, symbol, "default", "default");

            // doppio click su una cella colore → ColorDialog; click destro → 'default'
            _grid.CellMouseClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var col = _grid.Columns[e.ColumnIndex].Name;
                if (col != "textColor" && col != "backColor") return;
                if (e.Button == MouseButtons.Right)
                {
                    _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = "default";
                    if (col == "textColor") _textColors.Remove(ActionKey(e.RowIndex));
                    else _backColors.Remove(ActionKey(e.RowIndex));
                    return;
                }
                using var dlg = new ColorDialog { FullOpen = true };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = dlg.Color.Name;
                if (col == "textColor") _textColors[ActionKey(e.RowIndex)] = dlg.Color;
                else _backColors[ActionKey(e.RowIndex)] = dlg.Color;
            };

            group.Controls.Add(_grid);
            Controls.Add(group);
        }

        private static string ActionKey(int rowIndex) => Rows[rowIndex].action switch
        {
            "1st limp" => "1stimp",
            "Overlimp" => "Limp",
            var action => action.Replace(" ", "").Replace("+", "")
        };

        // ---------- Timings ----------
        private void BuildTimings()
        {
            var g = new GroupBox { Text = "Timings", Location = new Point(346, 8), Size = new Size(336, 86) };
            _addTimings = new CheckBox { Text = "Add timings", Location = new Point(10, 16), AutoSize = true };
            _timingsColorOn = new CheckBox { Text = "Color", Location = new Point(26, 38), AutoSize = true };
            var timingsSetColor = new Button { Text = "Set color", Location = new Point(86, 36), Size = new Size(70, 22) };
            _timingsUseRange = new CheckBox { Text = "Use Tag color range of [timing] tag", Location = new Point(42, 60), AutoSize = true };
            timingsSetColor.Click += (s, e) =>
            {
                using var dialog = new ColorDialog { FullOpen = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) _timingsColor = dialog.Color;
            };
            g.Controls.AddRange(new Control[] { _addTimings, _timingsColorOn, timingsSetColor, _timingsUseRange });
            Controls.Add(g);
        }

        private Color? _timingsColor;

        // ---------- hint blu ----------
        private void BuildHints()
        {
            var hint1 = new Label
            {
                Text = "use # here as a bet number (#b displays as 3b, 4b, etc.)",
                Location = new Point(346, 108), AutoSize = true, ForeColor = Color.RoyalBlue
            };
            var hint2 = new Label
            {
                Text = "use [=/] to display each street at a new line",
                Location = new Point(346, 128), AutoSize = true, ForeColor = Color.RoyalBlue
            };
            Controls.Add(hint1); Controls.Add(hint2);
        }

        // ---------- load/save ----------
        private void LoadValues()
        {
            for (int i = 0; i < Rows.Length; i++)
            {
                var key = ActionKey(i);
                var sym = _ini.Get("HUD", "actionsSym" + key);
                if (sym != null) _grid.Rows[i].Cells["symbol"].Value = sym;
                var tc = _ini.Get("HUD", "actionsTextColor" + key);
                if (!string.IsNullOrEmpty(tc))
                {
                    _grid.Rows[i].Cells["textColor"].Value = tc;
                    try { _textColors[key] = Color.FromArgb((int)new ColorConverter().ConvertFromString(tc)); } catch { }
                }
                var bc = _ini.Get("HUD", "actionsBackColor" + key);
                if (!string.IsNullOrEmpty(bc))
                {
                    _grid.Rows[i].Cells["backColor"].Value = bc;
                    try { _backColors[key] = Color.FromArgb((int)new ColorConverter().ConvertFromString(bc)); } catch { }
                }
            }
            _addTimings.Checked = _ini.GetBool("HUD", "actionsAddTimings", false);
            _timingsColorOn.Checked = _ini.GetBool("HUD", "actionsTimingsColorOn", false);
            _timingsUseRange.Checked = _ini.GetBool("HUD", "actionsTimingsUseRange", false);
            if (_ini.Get("HUD", "actionsTimingsColor") != null)
                _timingsColor = IniColors.FromInt(_ini.GetLong("HUD", "actionsTimingsColor", 0xFFFFFF));
        }

        private void Save()
        {
            if (!_grid.EndEdit()) return;
            for (int i = 0; i < _grid.Rows.Count && i < Rows.Length; i++)
            {
                var key = ActionKey(i);
                _ini.Set("HUD", "actionsSym" + key, Convert.ToString(_grid.Rows[i].Cells["symbol"].Value) ?? "");
                var tc = Convert.ToString(_grid.Rows[i].Cells["textColor"].Value);
                _ini.Set("HUD", "actionsTextColor" + key, tc == "default" ? "" : tc ?? "");
                var bc = Convert.ToString(_grid.Rows[i].Cells["backColor"].Value);
                _ini.Set("HUD", "actionsBackColor" + key, bc == "default" ? "" : bc ?? "");
            }
            void SetBool(string k, bool v) => _ini.Set("HUD", k, v ? "1" : "0");
            SetBool("actionsAddTimings", _addTimings.Checked);
            SetBool("actionsTimingsColorOn", _timingsColorOn.Checked);
            SetBool("actionsTimingsUseRange", _timingsUseRange.Checked);
            if (_timingsColor is Color color)
                _ini.Set("HUD", "actionsTimingsColor", (color.R | (color.G << 8) | (color.B << 16)).ToString(CultureInfo.InvariantCulture));

            _ini.Save();
            _log("actions tag config: simboli/timings salvati ([HUD] actions*)");
        }
    }
}
