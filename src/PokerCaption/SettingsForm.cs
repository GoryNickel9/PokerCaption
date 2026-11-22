using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokerCaption.Config;

namespace PokerCaption
{
    /// <summary>
    /// Editor generico del file INI (tutte le 450+ chiavi, schema Version=82).
    /// Utile finché la mappatura control→chiave non è ricostruita: permette di
    /// vedere e modificare qualsiasi valore senza perdere quelli sconosciuti.
    /// </summary>
    public sealed class SettingsForm : Form
    {
        private readonly string _path;
        private IniFile _ini;
        private readonly ListBox _sections;
        private readonly DataGridView _grid;
        private readonly TextBox _search;
        private readonly Label _status;
        private readonly Action<string> _onSaved;
        private readonly bool _standalone;

        public SettingsForm(string iniPath, IniFile liveIni = null, Action<string> onSaved = null)
        {
            _path = iniPath;
            _standalone = liveIni == null;
            _ini = liveIni ?? PokerCaptionConfig.Load(iniPath).Raw;
            _onSaved = onSaved;

            Text = UiLanguage.Text("Configuration — PokerCaption.ini");
            Icon = ProductBrand.Icon;
            Size = new Size(860, 620);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);

            var top = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6) };
            _search = new TextBox { Width = 260, PlaceholderText = UiLanguage.Text("Search key or section…") };
            var btnReload = new Button { Text = UiLanguage.Text("Reload"), AutoSize = true, Left = 280 };
            top.Controls.Add(_search);
            top.Controls.Add(btnReload);

            _sections = new ListBox { Dock = DockStyle.Left, Width = 220, IntegralHeight = false };
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = UiLanguage.Text("Key"), ReadOnly = true, FillWeight = 40 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = UiLanguage.Text("Value"), FillWeight = 60 });

            _status = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };

            Controls.Add(_grid);
            Controls.Add(_sections);
            Controls.Add(top);
            Controls.Add(_status);

            _sections.SelectedIndexChanged += (s, e) => ShowSection(_sections.SelectedItem as string);
            _search.TextChanged += (s, e) => ApplyFilter();
            btnReload.Click += (s, e) => Reload();

            _grid.CellEndEdit += (_, e) => SaveCell(e.RowIndex, e.ColumnIndex);
            LoadSections();
            _status.Text = _path;
        }

        private void LoadSections()
        {
            _sections.Items.Clear();
            foreach (var s in _ini.Sections) _sections.Items.Add(s.Name);
            if (_sections.Items.Count > 0) _sections.SelectedIndex = 0;
        }

        private void Reload()
        {
            if (_standalone) _ini = PokerCaptionConfig.Load(_path).Raw;
            LoadSections();
            _status.Text = _path + UiLanguage.Text(" (reloaded)");
        }

        private void ApplyFilter()
        {
            if (_search.Text.Length == 0) { ShowSection(_sections.SelectedItem as string); return; }
            var needle = _search.Text.Trim();
            _grid.Rows.Clear();
            foreach (var section in _ini.Sections)
                AddRows(section, needle);
        }

        private void ShowSection(string name)
        {
            _grid.Rows.Clear();
            if (name == null) return;
            var sec = _ini.Find(name);
            if (sec == null) return;
            AddRows(sec);
            _status.Text = $"{_path}   [{name}]  {string.Format(UiLanguage.Text("{0} keys"), sec.Entries.Count)}";
        }

        private void AddRows(IniFile.Section section, string filter = null)
        {
            for (int i = 0; i < section.Entries.Count; i++)
            {
                var (key, value) = section.Entries[i];
                if (filter != null && !key.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !section.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                int row = _grid.Rows.Add(filter == null ? key : $"{section.Name}.{key}", value);
                _grid.Rows[row].Tag = (section.Name, i);
            }
        }

        private void SaveCell(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || columnIndex != 1) return;
            var row = _grid.Rows[rowIndex];
            if (row.Tag is not ValueTuple<string, int> source) return;
            var (name, index) = source;
            var section = _ini.Find(name);
            if (section == null || index >= section.Entries.Count) return;
            var (key, previous) = section.Entries[index];
            string value = Convert.ToString(row.Cells[1].Value) ?? "";
            if (value == previous) return;

            try { _ini.SaveChanges(name, current => current.Entries[index] = new(key, value)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                row.Cells[1].Value = previous;
                _status.Text = ex.Message;
                return;
            }
            _onSaved?.Invoke(name);
            _status.Text = $"{_path}   {UiLanguage.Text("Saved")} ({DateTime.Now:HH:mm:ss})";
        }
    }
}
