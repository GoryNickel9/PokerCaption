using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using PokerCaption.Config;
using PokerCaption.Engine;

namespace PokerCaption
{
    /// <summary>
    /// partial di MainUiController: gruppo "HUD panels" della tab HUD
    /// (PageControl2: Table panels / Player panels / General HUD).
    /// Liste + CRUD (vedi sotto) e pannello proprietà a destra delle liste,
    /// cablato sui controlli del DFM di Form1 (nomi originali):
    ///  - Table panels: formato, visibilità, colore del testo e ordine.
    ///  - Player panels: formato, visibilità, colore del testo e ordine.
    ///  - General HUD: formato, font, colore del testo e sfondo.
    /// Il 'Add tag' apre il dialogo genHudAddTag e inserisce il
    /// tag al cursore. Ogni modifica: INI in memoria + salva su disco + ricarica
    /// il motore (come il pannello betscript).
    /// </summary>
    public sealed partial class MainUiController
    {
        // evita rientranza: mentre il pannello proprietà viene riempito dalla
        // selezione, gli eventi TextChanged/CheckedChanged devono restare inerti
        private bool _panelsSync;

        // dialog 'Add tag' (genHudAddTag), costruito una sola volta
        private Form _addTagDlg;
        private ListBox _addTagList;
        private TextBox _addTagFilter;
        private List<string> _addTagAll = new List<string>();

        internal static string[] AvailableHudTags(string primary) => primary switch
        {
            "plFormatStr" => new[]
            {
                "[actions] - Player actions by street", "[timing] - Last action time in seconds"
            },
            "formatStr" => new[]
            {
                "[allHandsCount] - Hands played in the session", "[sessionTime] - Session time",
                "[handsPerHour] - Hands per hour"
            },
            _ => new[]
            {
                "[toCall_bb] - Amount to call in big blinds", "[%] - Call pot odds as a percentage",
                "[RNG] - Random number"
            }
        };

        /// <summary>Descrittore di una famiglia di pannelli (sezione INI + chiavi indicizzate + controlli).</summary>
        private sealed class PanelKind
        {
            public string Title;          // per il log
            public string Section;        // sezione INI
            public string Primary;        // chiave primaria <Primary><i> (la format string)
            public string VisibleKey;     // chiave visibilità per item
            public Regex IndexedKeys;     // chiavi <prefisso><i> della famiglia (gruppo 1=prefisso, 2=indice)
            public bool UseCount;         // [GenHUD] usa Count=N
            public string ListName;       // ListBox
            public string BtnAdd, BtnDelete, BtnClone, BtnUp, BtnDown;
            // pannello proprietà
            public string EdFormat, ChkVisible, BtnAddTag, BtnFontColor;
            public string ColorKey;                        // chiave scritta da BtnFontColor
            public long ColorKeyDefault = 0xFFFFFF;        // default se la chiave manca
            // General HUD only
            public string BtnBkColor, BtnFont;
            public string BkColorKey = "FBkColor";
            public Action ReloadEngine;   // risincronizza il motore dopo la scrittura
        }

        // [HUD] pannelli tabella: chiavi dell'engine + quelle del pannello proprietà
        // della risorsa.
        // Le chiavi delle condizioni "Show panel" (SOn*/SFor*/S_Caption) sono state
        // rimosse con il gruppo: i pannelli sono sempre visibili su qualsiasi variante.
        private static readonly Regex RxTablePanelKey = new Regex(
            "^(FormatStr|FVisible|FontColor)(\\d+)$",
            RegexOptions.IgnoreCase);
        // [HUD] pannelli giocatore: tutte le chiavi pl*<i>
        private static readonly Regex RxPlayerPanelKey = new Regex("^(pl(?:[A-Za-z_]+|Drag[XY]\\d+_))(\\d+)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxGenHudKey = new Regex(
            "^(formatStr|FColor|FBkColor|FSize)(\\d+)$",
            RegexOptions.IgnoreCase);

        private void WireHudPanels(Form f)
        {
            try
            {
                var kinds = new[]
                {
                    new PanelKind
                    {
                        Title = "Table panels", Section = "HUD", Primary = "FormatStr", VisibleKey = "FVisible",
                        IndexedKeys = RxTablePanelKey, UseCount = false, ListName = "tablePanelsList",
                        BtnAdd = "tablePanelsAdd", BtnDelete = "tablePanelsDelete", BtnClone = "tablePanelsClone",
                        BtnUp = "tablePanelsMoveUp", BtnDown = "tablePanelsMoveDown",
                        EdFormat = "tablePanelsFormatStr", ChkVisible = "tablePanelsVisible", BtnAddTag = "tablePanelsAddTag",
                        BtnFontColor = "tablePanelsFontColor", ColorKey = "FontColor", ColorKeyDefault = 0xFFFFFF,
                        ReloadEngine = () => Program.Service?.Hud?.Load(Program.Ini)
                    },
                    new PanelKind
                    {
                        Title = "Player panels", Section = "HUD", Primary = "plFormatStr", VisibleKey = "plFVisible",
                        IndexedKeys = RxPlayerPanelKey, UseCount = false, ListName = "playerPanelsList",
                        BtnAdd = "playerPanelsAdd", BtnDelete = "playerPanelsDelete", BtnClone = "playerPanelsClone",
                        BtnUp = "playerPanelsMoveUp", BtnDown = "playerPanelsMoveDown",
                        EdFormat = "playerPanelsFormatStr", ChkVisible = "playerPanelsVisible", BtnAddTag = "playerPanelsAddTag",
                        BtnFontColor = "playerPanelsFontColor", ColorKey = "plFontColor", ColorKeyDefault = 0xFFFFFF,
                        ReloadEngine = () => Program.Service?.Hud?.Load(Program.Ini)
                    },
                    new PanelKind
                    {
                        Title = "General HUD", Section = "GenHUD", Primary = "formatStr",
                        IndexedKeys = RxGenHudKey, UseCount = true, ListName = "generalHudPanelsList",
                        BtnAdd = "generalHudAdd", BtnDelete = "generalHudDelete", BtnClone = null,
                        BtnUp = "generalHudMoveUp", BtnDown = "generalHudMoveDown",
                        EdFormat = "generalHudFormatStr", BtnAddTag = "generalHudAddTag",
                        BtnFontColor = "generalHudFontColor", ColorKey = "FColor", ColorKeyDefault = 0xFFFFFF,
                        BtnBkColor = "generalHudBackColor", BtnFont = "generalHudFont",
                        ReloadEngine = () => Program.Service?.GenHud?.Load(Program.Ini)
                    }
                };

                foreach (var k in kinds)
                {
                    var k1 = k;

                    // CRUD
                    WireBtn(f, k.BtnAdd, () => PanelAdd(f, k1), "'Add new panel'");
                    WireBtn(f, k.BtnDelete, () => PanelDelete(f, k1), "'Delete panel'");
                    if (k.BtnClone != null)
                        WireBtn(f, k.BtnClone, () => PanelClone(f, k1), "'Clone panel'");
                    WireBtn(f, k.BtnUp, () => PanelMove(f, k1, -1), "su");
                    WireBtn(f, k.BtnDown, () => PanelMove(f, k1, +1), "giù");

                    // selezione nella lista → pannello proprietà
                    var list = PanelListBox(f, k);
                    if (list != null) list.SelectedIndexChanged += (s, e) => PanelSelected(f, k1);
                    else Log("HUD panels: " + k.ListName + " mancante");

                    // pannello proprietà
                    if (Ctl(f, k.EdFormat) is TextBox edFormat)
                        edFormat.TextChanged += (s, e) => PanelFormatChanged(f, k1, edFormat);
                    else Log("HUD panels: " + k.EdFormat + " (Format string) mancante");
                    if (k.ChkVisible != null && Ctl(f, k.ChkVisible) is CheckBox chkVis)
                        chkVis.CheckedChanged += (s, e) => PanelVisibleChanged(f, k1, chkVis);
                    WireColor(f, k1, k.BtnFontColor, k.ColorKey, k.ColorKeyDefault);
                    if (k.BtnAddTag != null)
                        WireBtn(f, k.BtnAddTag, () => InsertTag(f, k1), "'Add tag'");


                    // extra General HUD
                    if (k.BtnBkColor != null) WireColor(f, k1, k.BtnBkColor, k.BkColorKey, 0x404040);
                    if (Ctl(f, k.BtnFont) is Button btnFont)
                        btnFont.Click += (s, e) => PickPanelFont(f, k1);

                    FillPanelList(f, k); // riempie e scatena PanelSelected → pannello proprietà
                }
                Log("HUD panels: liste + CRUD + pannello proprietà collegati (format/visible/colori/x-y/border/align/add tag/su-giù)");
            }
            catch (Exception ex) { Log("HUD panels wiring ERR: " + ex.Message); }
        }

        /// <summary>Collega un pulsante con log se il controllo manca (nomi della risorsa).</summary>
        private void WireBtn(Form f, string name, Action onClick, string what)
        {
            if (Ctl(f, name) is Button b) b.Click += (s, e) => { try { onClick(); } catch (Exception ex) { Log("HUD panels " + what + " ERR: " + ex.Message); } };
            else Log("HUD panels: " + name + " (" + what + ") mancante");
        }

        // ---------- lista ----------
        /// <summary>Righe della lista = format string dei pannelli presenti nell'INI.</summary>
        private static string PanelDisplayText(string format) => (format ?? "").Replace("\\s", " ");

        private static ListBox PanelListBox(Form f, PanelKind k) => Ctl(f, k.ListName) as ListBox;

        private static int SelectedIndex(Form f, PanelKind k) => PanelListBox(f, k)?.SelectedIndex ?? -1;

        /// <summary>Numero di pannelli presenti: chiavi primarie consecutive ([HUD]) o Count ([GenHUD]).</summary>
        private static int PanelCount(PanelKind k)
        {
            if (Program.Ini == null) return 0;
            if (k.UseCount) return Math.Max(0, Program.Ini.GetInt(k.Section, "Count", 0));
            int n = 0;
            while (n < 64 && Program.Ini.Get(k.Section, k.Primary + n) != null) n++;
            return n;
        }

        /// <summary>Riempie la lista dai pannelli dell'INI e riallinea il pannello proprietà.</summary>
        private void FillPanelList(Form f, PanelKind k)
        {
            try
            {
                var lb = PanelListBox(f, k);
                if (lb == null || Program.Ini == null) return;
                int sel = lb.SelectedIndex;
                _panelsSync = true;
                try
                {
                    lb.BeginUpdate();
                    try
                    {
                        lb.Items.Clear();
                        int n = PanelCount(k);
                        for (int i = 0; i < n; i++)
                            lb.Items.Add(PanelDisplayText(Program.Ini.Get(k.Section, k.Primary + i)));
                    }
                    finally { lb.EndUpdate(); }
                    if (lb.Items.Count > 0)
                        lb.SelectedIndex = Math.Max(0, Math.Min(sel, lb.Items.Count - 1));
                    else lb.SelectedIndex = -1;
                }
                finally { _panelsSync = false; }
                PanelSelected(f, k); // il guard qui sopra ha soppresso l'evento: riempi a mano
            }
            catch (Exception ex) { Log("HUD panels fill ERR: " + ex.Message); }
        }

        // ---------- selezione → pannello proprietà ----------
        private void PanelSelected(Form f, PanelKind k)
        {
            if (_panelsSync) return;
            int i = SelectedIndex(f, k);
            _panelsSync = true;
            try
            {
                if (Ctl(f, k.EdFormat) is TextBox ed)
                    ed.Text = i >= 0 ? (Program.Ini.Get(k.Section, k.Primary + i) ?? "") : "";
                if (k.ChkVisible != null && Ctl(f, k.ChkVisible) is CheckBox chkVis)
                    chkVis.Checked = i < 0 || Program.Ini.GetBool(k.Section, k.VisibleKey + i, true);
            }
            finally { _panelsSync = false; }
        }

        // ---------- persistenza dal pannello proprietà ----------
        private static void SetPanelKey(PanelKind k, string key, string value) =>
            Program.Ini.GetOrAddSection(k.Section)[key] = value;

        /// <summary>Salva l'INI, ricarica il motore e (per la format string) aggiorna la riga della lista.</summary>
        private void PersistPanelField(Form f, PanelKind k, int i, bool updateRow)
        {
            Program.Ini.Save();
            try { k.ReloadEngine(); }
            catch (Exception ex) { Log("HUD panels reload ERR: " + ex.Message); }
            if (updateRow)
            {
                var lb = PanelListBox(f, k);
                if (lb != null && i >= 0 && i < lb.Items.Count)
                {
                    _panelsSync = true;
                    try { lb.Items[i] = PanelDisplayText(Program.Ini.Get(k.Section, k.Primary + i)); }
                    finally { _panelsSync = false; }
                }
            }
        }

        private void PanelFormatChanged(Form f, PanelKind k, TextBox ed)
        {
            if (_panelsSync) return;
            try
            {
                int i = SelectedIndex(f, k);
                if (i < 0) return;
                SetPanelKey(k, k.Primary + i, ed.Text);
                PersistPanelField(f, k, i, updateRow: true);
            }
            catch (Exception ex) { Log("HUD panels format ERR: " + ex.Message); }
        }

        private void PanelVisibleChanged(Form f, PanelKind k, CheckBox chk)
        {
            if (_panelsSync) return;
            try
            {
                int i = SelectedIndex(f, k);
                if (i < 0) return;
                SetPanelKey(k, k.VisibleKey + i, chk.Checked ? "1" : "0");
                PersistPanelField(f, k, i, updateRow: false);
                Log("HUD panels " + k.Title + ": visible #" + (i + 1) + " = " + chk.Checked);
            }
            catch (Exception ex) { Log("HUD panels visible ERR: " + ex.Message); }
        }

        /// <summary>Pulsante colore → ColorDialog → chiave <key><i> (BGR).</summary>
        private void WireColor(Form f, PanelKind k, string btnName, string key, long def)
        {
            if (btnName == null) return;
            if (Ctl(f, btnName) is Button btn)
                btn.Click += (s, e) =>
                {
                    try
                    {
                        if (_panelsSync) return;
                        int i = SelectedIndex(f, k);
                        if (i < 0) return;
                        var cur = IniColors.FromInt(Program.Ini.GetLong(k.Section, key + i, def));
                        using var dlg = new ColorDialog { FullOpen = true, Color = cur == Color.Empty ? Color.White : cur };
                        if (dlg.ShowDialog(Form) != DialogResult.OK) return;
                        SetPanelKey(k, key + i, ColorToBgr(dlg.Color).ToString(CultureInfo.InvariantCulture));
                        PersistPanelField(f, k, i, updateRow: false);
                        Log("HUD panels " + k.Title + ": " + key + " #" + (i + 1) + " = " + dlg.Color.Name);
                    }
                    catch (Exception ex) { Log("HUD panels color ERR: " + ex.Message); }
                };
            else Log("HUD panels: " + btnName + " (colore) mancante");
        }

        private static int ColorToBgr(Color c) => c.R | (c.G << 8) | (c.B << 16);

        /// <summary>'Font' del General HUD: il motore onora solo la dimensione (FSize_i).</summary>
        private void PickPanelFont(Form f, PanelKind k)
        {
            try
            {
                if (_panelsSync) return;
                int i = SelectedIndex(f, k);
                if (i < 0) return;
                bool tablePanel = k.Primary == "FormatStr";
                var sizeKey = tablePanel ? "FontSize" : "FSize";
                using var dlg = new FontDialog { Font = new Font(Program.Ini.Get(k.Section, "FontName" + i) ?? "Tahoma",
                    Math.Max(1, Program.Ini.GetInt(k.Section, sizeKey + i, 9)),
                    tablePanel && Program.Ini.GetBool(k.Section, "FontBold" + i, true) ? FontStyle.Bold : FontStyle.Regular) };
                if (dlg.ShowDialog(Form) != DialogResult.OK) return;
                SetPanelKey(k, sizeKey + i, ((int)Math.Round(dlg.Font.Size)).ToString(CultureInfo.InvariantCulture));
                if (tablePanel)
                {
                    SetPanelKey(k, "FontName" + i, dlg.Font.Name);
                    SetPanelKey(k, "FontBold" + i, dlg.Font.Bold ? "1" : "0");
                }
                PersistPanelField(f, k, i, updateRow: false);
                Log("HUD panels " + k.Title + ": font size #" + (i + 1) + " = " + (int)Math.Round(dlg.Font.Size));
            }
            catch (Exception ex) { Log("HUD panels font ERR: " + ex.Message); }
        }

        private void InsertTag(Form f, PanelKind k)
        {
            try
            {
                if (_panelsSync) return;
                int i = SelectedIndex(f, k);
                if (i < 0) return;
                var ed = Ctl(f, k.EdFormat) as TextBox;
                if (ed == null) return;
                if (_addTagDlg == null || _addTagDlg.IsDisposed) BuildAddTagDialog();
                _addTagAll = AvailableHudTags(k.Primary).ToList();
                if (_addTagFilter != null) _addTagFilter.Clear();
                RefreshAddTagList();
                if (_addTagDlg.ShowDialog(Form) != DialogResult.OK || _addTagList.SelectedItem is not string selItem) return;
                var m = Regex.Match(selItem, "^(\\[[^\\]]+\\])");
                if (!m.Success) return;
                string tag = m.Groups[1].Value;
                int pos = ed.SelectionStart;
                _panelsSync = true;
                try
                {
                    ed.Text = ed.Text.Insert(pos, tag);
                    ed.SelectionStart = pos + tag.Length;
                }
                finally { _panelsSync = false; }
                SetPanelKey(k, k.Primary + i, ed.Text);
                PersistPanelField(f, k, i, updateRow: true);
                Log("HUD panels " + k.Title + ": aggiunto tag " + tag + " al pannello #" + (i + 1));
            }
            catch (Exception ex) { Log("HUD panels addtag ERR: " + ex.Message); }
        }

        private void BuildAddTagDialog()
        {
            var dialog = DesktopWindow.CreateTagPicker();
            _addTagList = (ListBox)Ctl(dialog, "ListBox1");
            _addTagFilter = (TextBox)Ctl(dialog, "Edit1");
            _addTagFilter.TextChanged += (_, _) => RefreshAddTagList();
            _addTagList.DoubleClick += (_, _) =>
            {
                if (_addTagList.SelectedIndex >= 0) dialog.DialogResult = DialogResult.OK;
            };
            _addTagDlg = dialog;
        }

        private void RefreshAddTagList()
        {
            string query = _addTagFilter?.Text.Trim() ?? "";
            _addTagList.BeginUpdate();
            try
            {
                _addTagList.Items.Clear();
                foreach (var entry in _addTagAll.Where(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)))
                    _addTagList.Items.Add(entry);
            }
            finally { _addTagList.EndUpdate(); }
        }

        // ---------- CRUD ----------
        private void PanelAdd(Form f, PanelKind k)
        {
            try
            {
                if (Program.Ini == null) return;
                var sec = Program.Ini.GetOrAddSection(k.Section);
                int n = PanelCount(k);
                WritePanelDefaults(k, sec, n);
                if (k.UseCount) sec["Count"] = (n + 1).ToString(CultureInfo.InvariantCulture);
                CommitPanelChange(f, k, "aggiunto pannello #" + (n + 1), n);
            }
            catch (Exception ex) { Log("HUD panels add ERR: " + ex.Message); }
        }

        private void PanelDelete(Form f, PanelKind k)
        {
            try
            {
                if (Program.Ini == null) return;
                var lb = PanelListBox(f, k);
                if (lb == null) return;
                int i = lb.SelectedIndex;
                int n = PanelCount(k);
                if (i < 0 || i >= n) { Log("HUD panels: " + k.Title + " delete senza pannello selezionato"); return; }
                var sec = Program.Ini.GetOrAddSection(k.Section);
                RemovePanelIndex(sec, k.IndexedKeys, i);
                ReindexPanelKeys(sec, k.IndexedKeys, i);
                if (k.UseCount) sec["Count"] = (n - 1).ToString(CultureInfo.InvariantCulture);
                CommitPanelChange(f, k, "eliminato pannello #" + (i + 1) + " ('" + PanelDisplayText(lb.Items[i] as string) + "')",
                    Math.Min(i, Math.Max(0, n - 2)));
            }
            catch (Exception ex) { Log("HUD panels delete ERR: " + ex.Message); }
        }

        private void PanelClone(Form f, PanelKind k)
        {
            try
            {
                if (Program.Ini == null) return;
                var lb = PanelListBox(f, k);
                if (lb == null) return;
                int i = lb.SelectedIndex;
                int n = PanelCount(k);
                if (i < 0 || i >= n) { Log("HUD panels: " + k.Title + " clone senza pannello selezionato"); return; }
                var sec = Program.Ini.GetOrAddSection(k.Section);
                // copia tutte le chiavi <prefisso><i> sulla nuova coda <prefisso><n>
                foreach (var e in sec.Entries.ToList())
                {
                    var m = k.IndexedKeys.Match(e.Key);
                    if (m.Success && int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) == i)
                        sec.Entries.Add(new KeyValuePair<string, string>(m.Groups[1].Value + n, e.Value));
                }
                if (k.UseCount) sec["Count"] = (n + 1).ToString(CultureInfo.InvariantCulture);
                CommitPanelChange(f, k, "clonato pannello #" + (i + 1) + " -> #" + (n + 1), n);
            }
            catch (Exception ex) { Log("HUD panels clone ERR: " + ex.Message); }
        }

        /// <summary>Su/giù: scambia l'item selezionato col vicino (swap di tutte le chiavi indicizzate).</summary>
        private void PanelMove(Form f, PanelKind k, int delta)
        {
            try
            {
                if (Program.Ini == null) return;
                int i = SelectedIndex(f, k), j = i + delta;
                int n = PanelCount(k);
                if (i < 0 || i >= n || j < 0 || j >= n) return;
                var sec = Program.Ini.GetOrAddSection(k.Section);
                SwapPanelIndex(sec, k.IndexedKeys, i, j);
                CommitPanelChange(f, k, "spostato pannello #" + (i + 1) + (delta < 0 ? " su" : " giù"), j);
            }
            catch (Exception ex) { Log("HUD panels move ERR: " + ex.Message); }
        }

        // ---------- persistenza CRUD ----------
        /// <summary>Default della nuova voce, come i pannelli dell'INI di riferimento.</summary>
        private static void WritePanelDefaults(PanelKind k, IniFile.Section sec, int n)
        {
            switch (k.Primary)
            {
                case "FormatStr": // pannello tabella [HUD]
                    sec["FormatStr" + n] = "";
                    sec["FVisible" + n] = "1";
                    break;
                case "plFormatStr": // pannello giocatore [HUD]
                    sec["plFormatStr" + n] = "";
                    sec["plFVisible" + n] = "1";
                    break;
                default: // item [GenHUD]
                    sec["formatStr" + n] = "";
                    sec["FColor" + n] = "16777215";   // bianco
                    sec["FBkColor" + n] = "4210752";  // grigio 0x404040
                    sec["FSize" + n] = "9";
                    break;
            }
        }

        /// <summary>Salva l'INI, ricarica il motore e riallinea la lista alla selezione.</summary>
        private void CommitPanelChange(Form f, PanelKind k, string what, int select)
        {
            Program.Ini.Save();
            try { k.ReloadEngine(); }
            catch (Exception ex) { Log("HUD panels reload ERR: " + ex.Message); }
            FillPanelList(f, k);
            var lb = PanelListBox(f, k);
            if (lb != null && lb.Items.Count > 0)
                lb.SelectedIndex = Math.Max(0, Math.Min(select, lb.Items.Count - 1));
            Log("HUD panels " + k.Title + ": " + what);
        }

        /// <summary>Tutte le chiavi <prefisso><idx> della famiglia.</summary>
        private static void RemovePanelIndex(IniFile.Section sec, Regex rx, int idx)
            => sec.Entries.RemoveAll(e =>
            {
                var m = rx.Match(e.Key);
                return m.Success && int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) == idx;
            });

        /// <summary>Chiavi con indice > removed: scende di 1.</summary>
        private static void ReindexPanelKeys(IniFile.Section sec, Regex rx, int removedIdx)
        {
            var idxs = sec.Entries
                .Select(e => rx.Match(e.Key))
                .Where(m => m.Success && int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) > removedIdx)
                .Select(m => int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))
                .Distinct().OrderBy(v => v).ToList();
            foreach (var idx in idxs)
                for (int e2 = 0; e2 < sec.Entries.Count; e2++)
                {
                    var m = rx.Match(sec.Entries[e2].Key);
                    if (m.Success && int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) == idx)
                        sec.Entries[e2] = new KeyValuePair<string, string>(m.Groups[1].Value + (idx - 1), sec.Entries[e2].Value);
                }
        }

        /// <summary>Scambia gli item a e b: per ogni prefisso presente copia il valore sull'altro
        /// indice e rimuove le chiavi rimaste senza controparte (swap "spazio" delle chiavi).</summary>
        private static void SwapPanelIndex(IniFile.Section sec, Regex rx, int a, int b)
        {
            var pa = PanelKeysOf(sec, rx, a);
            var pb = PanelKeysOf(sec, rx, b);
            foreach (var kv in pa) sec[kv.Key + b] = kv.Value;
            foreach (var kv in pb) sec[kv.Key + a] = kv.Value;
            foreach (var kv in pa.Where(kv => !pb.ContainsKey(kv.Key)).ToList())
                sec.Entries.RemoveAll(e => string.Equals(e.Key, kv.Key + a, StringComparison.OrdinalIgnoreCase));
            foreach (var kv in pb.Where(kv => !pa.ContainsKey(kv.Key)).ToList())
                sec.Entries.RemoveAll(e => string.Equals(e.Key, kv.Key + b, StringComparison.OrdinalIgnoreCase));
        }

        private static Dictionary<string, string> PanelKeysOf(IniFile.Section sec, Regex rx, int idx)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in sec.Entries)
            {
                var m = rx.Match(e.Key);
                if (m.Success && int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) == idx)
                    d[m.Groups[1].Value] = e.Value;
            }
            return d;
        }
    }
}
