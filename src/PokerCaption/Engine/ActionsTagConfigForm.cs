using System;
using System.Drawing;
using System.Windows.Forms;
using PokerCaption.Config;

namespace PokerCaption.Engine
{
    /// <summary>
    /// Dialog '[actions] tag config'.
    /// Persiste i simboli delle azioni e la visualizzazione dei tempi in [HUD].
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
        private CheckBox _addTimings;

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

            var close = new Button { Text = "Close", Location = new Point(10, ClientSize.Height - 32), Size = new Size(90, 26) };
            close.Click += (_, _) => Close();
            Controls.Add(close);
            CancelButton = close;
            FormClosing += (_, e) => e.Cancel = !_grid.EndEdit();

            LoadValues();
            _grid.CellValueChanged += (_, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != _grid.Columns["symbol"].Index) return;
                var cell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                SaveValue("actionsSym" + ActionKey(e.RowIndex), Convert.ToString(cell.Value) ?? "", () =>
                    cell.Value = _ini.Get("HUD", "actionsSym" + ActionKey(e.RowIndex), Rows[e.RowIndex].symbol));
            };
            _addTimings.CheckedChanged += (_, _) => SaveValue("actionsAddTimings", _addTimings.Checked ? "1" : "0",
                () => _addTimings.Checked = _ini.GetBool("HUD", "actionsAddTimings", false));
            Text = UiLanguage.Text(Text);
            UiLanguage.Apply(this);
            _grid.Columns["action"].HeaderText = UiLanguage.Text("Action");
            _grid.Columns["symbol"].HeaderText = UiLanguage.Text("Symbol");
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
            _grid.Columns["action"].ReadOnly = true;
            _grid.Columns["action"].FillWeight = 30;
            _grid.Columns["symbol"].FillWeight = 18;

            foreach (var (action, symbol) in Rows)
                _grid.Rows.Add(UiLanguage.Text(action), symbol);

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
            var g = new GroupBox { Text = "Timings", Location = new Point(346, 8), Size = new Size(336, 42) };
            _addTimings = new CheckBox { Text = "Add timings", Location = new Point(10, 16), AutoSize = true };
            g.Controls.Add(_addTimings);
            Controls.Add(g);
        }

        // ---------- load/save ----------
        private void LoadValues()
        {
            for (int i = 0; i < Rows.Length; i++)
            {
                var key = ActionKey(i);
                var sym = _ini.Get("HUD", "actionsSym" + key);
                if (sym != null) _grid.Rows[i].Cells["symbol"].Value = sym;
            }
            _addTimings.Checked = _ini.GetBool("HUD", "actionsAddTimings", false);
        }

        private bool _restoring;

        private void SaveValue(string key, string value, Action restore)
        {
            if (_restoring) return;
            try { _ini.SaveChanges("HUD", section => section[key] = value); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _restoring = true;
                try { restore(); }
                finally { _restoring = false; }
                _log(ex.Message);
                MessageBox.Show(this, ex.Message, "PokerCaption", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
