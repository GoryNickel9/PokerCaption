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
    ///  - Table panels:  Edit121 (FormatStr_i), CheckBox104 (FVisible_i), Edit88/91
    ///    (custom_x/y_i), SpeedButton33 (FrameColor_i), SpeedButton22 'Add tag',
    ///    SpeedButton48/93 su/giù
    ///  - Player panels: Edit120 (plFormatStr_i), CheckBox91 (plFVisible_i),
    ///    SpeedButton52 (plFrameColor_i), SpeedButton53 'Add tag', SpeedButton58/59 su/giù
    ///  - General HUD:   edGenHudFormatStr (formatStr_i), cbGenHudVisible (FVisible_i),
    ///    edGenHudBWidth (frameWidth_i), sbGenHudFontColor (FColor_i), sbGenHudBkColor
    ///    (FBkColor_i), cbGenHudBkInvisible (FInvisibleBack_i), sbGenHudFont (FSize_i),
    ///    rgGenHudAlign (FAlign_i), sbGenHudAddTag, sbGenHudMoveUp/Dn,
    ///    sbGenHudResetPos (left/top default), cbGenHudForceTop ([GenHUD] ForceTopmost)
    /// Il 'Add tag' apre il dialogo genHudAddTag e inserisce il
    /// tag al cursore. Ogni modifica: INI in memoria + salva su disco + ricarica
    /// il motore (come il pannello betscript).
    /// Restano inerti: combo
    /// OnClick/OnHover, gruppi 'Show panel' (condizioni), 'Bet formula', Background
    /// dei pannelli tavolo/giocatore, Fixed width/Group/Rounded, Position ↔.
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
                "[stack_bb] - Player stack in big blinds", "[stack] - Player stack",
                "[actions] - Player actions by street", "[timing] - Last action time in seconds"
            },
            "formatStr" => new[]
            {
                "[allHandsCount] - Hands played in the session", "[sessionTime] - Session time",
                "[handsPerHour] - Hands per hour", "[RNG] - Random number"
            },
            _ => new[]
            {
                "[totalPot_bb] - Pot in big blinds", "[totalPot] - Pot",
                "[toCall_bb] - Amount to call in big blinds", "[toCall] - Amount to call",
                "[seatStacks_bb] - Stack by seat in big blinds", "[%] - Call pot odds as a percentage",
                "[:] - Call pot odds as a ratio", "[BB] - Big blind", "[SB] - Small blind",
                "[capt] - Table name", "[RNG] - Random number"
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
            public string EdBorderWidth, EdX, EdY;         // EdX/EdY = posizione pannelli tavolo
            public string BtnBkColor, ChkBkInvisible, BtnFont, RgAlign, BtnResetPos, ChkForceTop;
            public string BkColorKey = "FBkColor";
            public Action ReloadEngine;   // risincronizza il motore dopo la scrittura
        }

        // [HUD] pannelli tabella: chiavi dell'engine + quelle del pannello proprietà
        // della risorsa.
        // Le chiavi delle condizioni "Show panel" (SOn*/SFor*/S_Caption) sono state
        // rimosse con il gruppo: i pannelli sono sempre visibili su qualsiasi variante.
        private static readonly Regex RxTablePanelKey = new Regex(
            "^(FormatStr|custom_x|custom_y|FrameColor|onClickParam|PanelClick|PanelHover|Group|Rounded|FVisible|FontName|FontColor|FontSize|FontBold|FontAlign|FontAlignV|FrameWidth|FontBackground|FontInvisibleBack|fixed_width_en|fixed_width|frame_width)(\\d+)$",
            RegexOptions.IgnoreCase);
        // [HUD] pannelli giocatore: tutte le chiavi pl*<i>
        private static readonly Regex RxPlayerPanelKey = new Regex("^(pl(?:[A-Za-z_]+|Drag[XY]\\d+_))(\\d+)$", RegexOptions.IgnoreCase);
        // [GenHUD] item: formatStr/frameWidth/FColor/FBkColor/FInvisibleBack/FAlign/FSize/FVisible
        private static readonly Regex RxGenHudKey = new Regex(
            "^(formatStr|frameWidth|FColor|FBkColor|FInvisibleBack|FAlign|FSize|FVisible)(\\d+)$",
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
                        EdX = "tablePanelsX", EdY = "tablePanelsY",
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
                        Title = "General HUD", Section = "GenHUD", Primary = "formatStr", VisibleKey = "FVisible",
                        IndexedKeys = RxGenHudKey, UseCount = true, ListName = "generalHudPanelsList",
                        BtnAdd = "generalHudAdd", BtnDelete = "generalHudDelete", BtnClone = null,
                        BtnUp = "generalHudMoveUp", BtnDown = "generalHudMoveDown",
                        EdFormat = "generalHudFormatStr", ChkVisible = "generalHudVisible", BtnAddTag = "generalHudAddTag",
                        BtnFontColor = "generalHudFontColor", ColorKey = "FColor", ColorKeyDefault = 0xFFFFFF,
                        EdBorderWidth = "generalHudBorderWidth", BtnBkColor = "generalHudBackColor",
                        ChkBkInvisible = "generalHudBackInvisible", BtnFont = "generalHudFont",
                        RgAlign = "generalHudAlign", BtnResetPos = "generalHudResetPosition", ChkForceTop = "generalHudForceTopmost",
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
                    if (Ctl(f, k.ChkVisible) is CheckBox chkVis)
                        chkVis.CheckedChanged += (s, e) => PanelVisibleChanged(f, k1, chkVis);
                    else Log("HUD panels: " + k.ChkVisible + " ('Visible') mancante");
                    WireColor(f, k1, k.BtnFontColor, k.ColorKey, k.ColorKeyDefault);
                    if (k.BtnAddTag != null)
                        WireBtn(f, k.BtnAddTag, () => InsertTag(f, k1), "'Add tag'");

                    // extra Table panels: posizione x/y del pannello sul tavolo
                    if (k.EdX != null) WireDouble(f, k1, k.EdX, "custom_x");
                    if (k.EdY != null) WireDouble(f, k1, k.EdY, "custom_y");
                    if (k.Primary == "FormatStr") WireTablePanelOptions(f, k);

                    // extra General HUD
                    if (k.EdBorderWidth != null) WireInt(f, k1, k.EdBorderWidth, "frameWidth", "Border width");
                    if (k.BtnBkColor != null) WireColor(f, k1, k.BtnBkColor, k.BkColorKey, 0x404040);
                    if (Ctl(f, k.ChkBkInvisible) is CheckBox chkInv)
                        chkInv.CheckedChanged += (s, e) => PanelFlagChanged(f, k1, "FInvisibleBack", chkInv.Checked, "sfondo invisibile");
                    if (Ctl(f, k.BtnFont) is Button btnFont)
                        btnFont.Click += (s, e) => PickPanelFont(f, k1);
                    if (k.RgAlign != null && Ctl(f, k.RgAlign) is Control rgAlign)
                        foreach (var rb in rgAlign.Controls.OfType<RadioButton>())
                            rb.CheckedChanged += (s, e) => { if (rb.Checked) PanelAlignChanged(f, k1, rgAlign); };
                    if (Ctl(f, k.BtnResetPos) is Button btnReset)
                        btnReset.Click += (s, e) => PanelResetPos(f, k1);
                    if (Ctl(f, k.ChkForceTop) is CheckBox chkForce)
                        chkForce.CheckedChanged += (s, e) =>
                        {
                            if (_panelsSync) return;
                            int i = SelectedIndex(f, k1);
                            if (i < 0) return;
                            Program.Ini.Set(k1.Section, "ForceTopmost", chkForce.Checked ? "1" : "0");
                            PersistPanelField(f, k1, i, updateRow: false);
                        };

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
                if (Ctl(f, k.ChkVisible) is CheckBox chkVis)
                    chkVis.Checked = i < 0 || Program.Ini.GetBool(k.Section, k.VisibleKey + i, true);
                if (k.EdBorderWidth != null && Ctl(f, k.EdBorderWidth) is TextBox edBw)
                    edBw.Text = i < 0 ? "0" : Program.Ini.GetInt(k.Section, "frameWidth" + i, 0).ToString(CultureInfo.InvariantCulture);
                if (k.EdX != null && Ctl(f, k.EdX) is TextBox edX)
                    edX.Text = i < 0 ? "0" : Program.Ini.GetDouble(k.Section, "custom_x" + i, 0).ToString("0.####", CultureInfo.InvariantCulture);
                if (k.EdY != null && Ctl(f, k.EdY) is TextBox edY)
                    edY.Text = i < 0 ? "0" : Program.Ini.GetDouble(k.Section, "custom_y" + i, 0).ToString("0.####", CultureInfo.InvariantCulture);
                if (k.ChkBkInvisible != null && Ctl(f, k.ChkBkInvisible) is CheckBox chkInv)
                    chkInv.Checked = i >= 0 && Program.Ini.GetBool(k.Section, "FInvisibleBack" + i, false);
                if (k.RgAlign != null && Ctl(f, k.RgAlign) is Control rg)
                    SetRadioIndex(rg, i < 0 ? 1 : Math.Max(0, Math.Min(2, Program.Ini.GetInt(k.Section, "FAlign" + i, 1))));
                if (k.Primary == "FormatStr") ReadTablePanelOptions(f, k, i);
            }
            finally { _panelsSync = false; }
        }

        private static readonly (string check, string key, bool fallback)[] TableFlags =
        {
            ("CheckBox93", "FontInvisibleBack", true), ("CheckBox538", "Rounded", false),
            ("CheckBox539", "fixed_width_en", false), ("CheckBox540", "Group", false)
        };

        private static readonly (string edit, string spin, string key, int min, int max)[] TableNumbers =
        {
            ("Edit175", "UpDown120", "FrameWidth", 0, 40), ("Edit140", "UpDown106", "frame_width", 0, 99),
            ("Edit139", "UpDown102", "fixed_width", 0, 999),
            ("tablePanelsX", "UpDown103", "custom_x", -999999, 999999),
            ("tablePanelsY", "UpDown104", "custom_y", -999999, 999999)
        };

        private static readonly (string control, string key, int fallback)[] TableAlignments =
        {
            ("RadioGroup5", "FontAlign", 1), ("RadioGroup17", "FontAlignV", 0)
        };

        private static readonly (string control, string key, string[] options)[] TableInteractions =
        {
            ("ComboBox34", "PanelClick", new[] { "None", "Copy to clipboard", "Reset session tags",
                "New [RNG] value", "Lock table in slot", "Close the table", "Minimize table" }),
            ("ComboBox23", "PanelHover", new[] { "None", "General HUD", "BB bet value" })
        };

        private void WireTablePanelOptions(Form f, PanelKind k)
        {
            WireColor(f, k, "SpeedButton23", "FontBackground", 0x303030);
            WireColor(f, k, "SpeedButton68", "FrameColor", 0xFFFFFF);
            WireBtn(f, "SpeedButton32", () => PickPanelFont(f, k), "Font");
            WireBtn(f, "SpeedButton126", () =>
            {
                if (Ctl(f, "Panel115") is Control position)
                {
                    position.Visible = !position.Visible;
                    if (position.Visible) position.BringToFront();
                }
            }, "Position");
            foreach (var (name, key, _) in TableFlags)
                if (Ctl(f, name) is CheckBox check)
                    check.CheckedChanged += (_, _) => PanelFlagChanged(f, k, key, check.Checked, key);
            foreach (var (editName, spinName, key, min, max) in TableNumbers)
            {
                if (key != "custom_x" && key != "custom_y") WireInt(f, k, editName, key, key);
                if (Ctl(f, editName) is not TextBox edit || Ctl(f, spinName) is not NumericUpDown spin) continue;
                spin.Minimum = min;
                spin.Maximum = max;
                spin.ValueChanged += (_, _) => { if (!_panelsSync) edit.Text = spin.Value.ToString(CultureInfo.InvariantCulture); };
                edit.TextChanged += (_, _) =>
                {
                    if (_panelsSync || !decimal.TryParse(edit.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return;
                    _panelsSync = true;
                    try { spin.Value = Math.Clamp(decimal.Truncate(value), min, max); }
                    finally { _panelsSync = false; }
                };
            }
            foreach (var (name, key, _) in TableAlignments)
                if (Ctl(f, name) is Control group)
                    foreach (var radio in group.Controls.OfType<RadioButton>())
                        radio.CheckedChanged += (_, _) =>
                        {
                            int i = SelectedIndex(f, k);
                            if (_panelsSync || !radio.Checked || i < 0) return;
                            SetPanelKey(k, key + i, RadioIndex(group).ToString(CultureInfo.InvariantCulture));
                            PersistPanelField(f, k, i, updateRow: false);
                        };
            foreach (var (name, key, options) in TableInteractions)
                if (Ctl(f, name) is ComboBox combo)
                {
                    combo.Items.Clear();
                    combo.Items.AddRange(options);
                    combo.SelectedIndexChanged += (_, _) =>
                    {
                        int i = SelectedIndex(f, k);
                        if (_panelsSync || i < 0) return;
                        SetPanelKey(k, key + i, combo.SelectedIndex > 0 ? combo.Text : "");
                        PersistPanelField(f, k, i, updateRow: false);
                    };
                }
        }

        private void ReadTablePanelOptions(Form f, PanelKind k, int i)
        {
            foreach (var (name, key, fallback) in TableFlags)
                if (Ctl(f, name) is CheckBox check)
                    check.Checked = i >= 0 && Program.Ini.GetBool(k.Section, key + i, fallback);
            foreach (var (editName, spinName, key, min, max) in TableNumbers)
            {
                double value = i < 0 ? 0 : Program.Ini.GetDouble(k.Section, key + i, 0);
                if (!double.IsFinite(value)) value = 0;
                if (Ctl(f, editName) is TextBox edit) edit.Text = value.ToString("0.####", CultureInfo.InvariantCulture);
                if (Ctl(f, spinName) is NumericUpDown spin) spin.Value = (decimal)Math.Clamp(Math.Truncate(value), min, max);
            }
            foreach (var (name, key, fallback) in TableAlignments)
                if (Ctl(f, name) is Control group)
                    SetRadioIndex(group, Math.Clamp(Program.Ini.GetInt(k.Section, key + i, fallback), 0, 2));
            foreach (var (name, key, _) in TableInteractions)
                if (Ctl(f, name) is ComboBox combo)
                    combo.SelectedIndex = Math.Max(0, combo.Items.IndexOf(Program.Ini.Get(k.Section, key + i) ?? ""));
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

        private void PanelFlagChanged(Form f, PanelKind k, string key, bool value, string what)
        {
            if (_panelsSync) return;
            try
            {
                int i = SelectedIndex(f, k);
                if (i < 0) return;
                SetPanelKey(k, key + i, value ? "1" : "0");
                PersistPanelField(f, k, i, updateRow: false);
                Log("HUD panels " + k.Title + ": " + what + " #" + (i + 1) + " = " + value);
            }
            catch (Exception ex) { Log("HUD panels flag ERR: " + ex.Message); }
        }

        private void PanelAlignChanged(Form f, PanelKind k, Control rg)
        {
            if (_panelsSync) return;
            try
            {
                int i = SelectedIndex(f, k);
                if (i < 0) return;
                SetPanelKey(k, "FAlign" + i, RadioIndex(rg).ToString(CultureInfo.InvariantCulture));
                PersistPanelField(f, k, i, updateRow: false);
                Log("HUD panels " + k.Title + ": align #" + (i + 1) + " = " + RadioIndex(rg));
            }
            catch (Exception ex) { Log("HUD panels align ERR: " + ex.Message); }
        }

        /// <summary>TextEdit numerico intero (es. Border width del General HUD).</summary>
        private void WireInt(Form f, PanelKind k, string ctlName, string key, string what)
        {
            if (Ctl(f, ctlName) is TextBox ed)
                ed.TextChanged += (s, e) =>
                {
                    if (_panelsSync) return;
                    int i = SelectedIndex(f, k);
                    if (i < 0) return;
                    if (!int.TryParse(ed.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return;
                    SetPanelKey(k, key + i, v.ToString(CultureInfo.InvariantCulture));
                    PersistPanelField(f, k, i, updateRow: false);
                };
            else Log("HUD panels: " + ctlName + " (" + what + ") mancante");
        }

        /// <summary>TextEdit numerico decimale (es. custom_x/custom_y dei pannelli tavolo).</summary>
        private void WireDouble(Form f, PanelKind k, string ctlName, string key)
        {
            if (Ctl(f, ctlName) is TextBox ed)
                ed.TextChanged += (s, e) =>
                {
                    if (_panelsSync) return;
                    int i = SelectedIndex(f, k);
                    if (i < 0) return;
                    if (!double.TryParse(ed.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return;
                    SetPanelKey(k, key + i, v.ToString("0.####", CultureInfo.InvariantCulture));
                    PersistPanelField(f, k, i, updateRow: false);
                };
            else Log("HUD panels: " + ctlName + " mancante");
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

        /// <summary>'Reset position' del General HUD: left/top ai default (4, 938).</summary>
        private void PanelResetPos(Form f, PanelKind k)
        {
            try
            {
                if (_panelsSync) return;
                int i = SelectedIndex(f, k);
                if (i < 0) return;
                Program.Ini.Set(k.Section, "left", "4");
                Program.Ini.Set(k.Section, "top", "938");
                PersistPanelField(f, k, i, updateRow: false);
                Log("HUD panels " + k.Title + ": posizione resettata (4, 938)");
            }
            catch (Exception ex) { Log("HUD panels resetpos ERR: " + ex.Message); }
        }

        // ---------- radio group (TRadioGroup del builder) ----------
        private static int RadioIndex(Control rg)
        {
            var rbs = rg.Controls.OfType<RadioButton>().OrderBy(r => r.Top).ThenBy(r => r.Left).ToList();
            for (int i = 0; i < rbs.Count; i++)
                if (rbs[i].Checked) return i;
            return 1;
        }

        private static void SetRadioIndex(Control rg, int idx)
        {
            var rbs = rg.Controls.OfType<RadioButton>().OrderBy(r => r.Top).ThenBy(r => r.Left).ToList();
            for (int i = 0; i < rbs.Count; i++)
                rbs[i].Checked = i == idx;
        }

        // ---------- 'Add tag' (dialogo genHudAddTag) ----------
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
                    sec["custom_x" + n] = "10";
                    sec["custom_y" + n] = "10";
                    sec["FrameColor" + n] = "0";
                    sec["FVisible" + n] = "1";
                    break;
                case "plFormatStr": // pannello giocatore [HUD]
                    sec["plFormatStr" + n] = "";
                    sec["plFVisible" + n] = "1";
                    sec["plFrameColor" + n] = "0";
                    break;
                default: // item [GenHUD]
                    sec["formatStr" + n] = "";
                    sec["frameWidth" + n] = "0";
                    sec["FColor" + n] = "16777215";   // bianco
                    sec["FBkColor" + n] = "6579300";  // grigio 0x404040
                    sec["FAlign" + n] = "1";
                    sec["FSize" + n] = "9";
                    sec["FVisible" + n] = "1";
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
