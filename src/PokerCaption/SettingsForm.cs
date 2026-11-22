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

        public SettingsForm(string iniPath)
        {
            _path = iniPath;
            _ini = PokerCaptionConfig.Load(iniPath).Raw;

            Text = "Configurazione — PokerCaption.ini";
            Icon = ProductBrand.Icon;
            Size = new Size(860, 620);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);

            var top = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6) };
            _search = new TextBox { Width = 260, PlaceholderText = "cerca chiave o sezione…" };
            var btnReload = new Button { Text = "Ricarica", AutoSize = true, Left = 280 };
            var btnSave = new Button { Text = "Salva", AutoSize = true, Left = 370 };
            top.Controls.Add(_search);
            top.Controls.Add(btnReload);
            top.Controls.Add(btnSave);

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
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Chiave", ReadOnly = true, FillWeight = 40 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Valore", FillWeight = 60 });

            _status = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };

            Controls.Add(_grid);
            Controls.Add(_sections);
            Controls.Add(top);
            Controls.Add(_status);

            _sections.SelectedIndexChanged += (s, e) => ShowSection(_sections.SelectedItem as string);
            _search.TextChanged += (s, e) => ApplyFilter();
            btnReload.Click += (s, e) => Reload();
            btnSave.Click += (s, e) => SaveAll();

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
            _ini = PokerCaptionConfig.Load(_path).Raw;
            LoadSections();
            _status.Text = _path + "  (ricaricato)";
        }

        private void ApplyFilter()
        {
            if (_search.Text.Length == 0) { ShowSection(_sections.SelectedItem as string); return; }
            var needle = _search.Text.Trim();
            _grid.Rows.Clear();
            foreach (var sec in _ini.Sections)
                foreach (var kv in sec.Entries)
                    if (kv.Key.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        sec.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                        _grid.Rows.Add($"{sec.Name}.{kv.Key}", kv.Value);
        }

        private void ShowSection(string name)
        {
            _grid.Rows.Clear();
            if (name == null) return;
            var sec = _ini.Find(name);
            if (sec == null) return;
            foreach (var kv in sec.Entries) _grid.Rows.Add(kv.Key, kv.Value);
            _status.Text = $"{_path}   [{name}]  {sec.Entries.Count} chiavi";
        }

        private void SaveAll()
        {
            // riporta nella struttura INI solo le modifiche visibili
            if (_search.Text.Length == 0)
            {
                var secName = _sections.SelectedItem as string;
                var sec = secName == null ? null : _ini.Find(secName);
                if (sec != null)
                {
                    for (int i = 0; i < _grid.Rows.Count && i < sec.Entries.Count; i++)
                    {
                        var v = Convert.ToString(_grid.Rows[i].Cells[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                        sec.Entries[i] = new KeyValuePair<string, string>(sec.Entries[i].Key, v ?? "");
                    }
                }
            }
            else
            {
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    var full = Convert.ToString(row.Cells[0].Value);
                    var val = Convert.ToString(row.Cells[1].Value) ?? "";
                    int dot = full.IndexOf('.');
                    if (dot <= 0) continue;
                    _ini.Set(full.Substring(0, dot), full.Substring(dot + 1), val);
                }
            }
            _ini.Save();
            _status.Text = $"{_path}   salvato (backup .ini_bak) alle {DateTime.Now:HH:mm:ss}";
        }
    }
}
