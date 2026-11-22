using System.Drawing;
using System.Windows.Forms;
using PokerCaption.Config;
using PokerCaption.Engine;

namespace PokerCaption;

public sealed partial class MainUiController
{
    private TableManagerPanel _tableManagerPanel;

    private void WireTableManager(Form form)
    {
        var service = Program.Service;
        if (service != null)
            _tableManagerPanel = AttachTableManager(form, service.Manager, Program.Ini);
    }

    internal static TableManagerPanel AttachTableManager(Form form, TableManagerEngine manager,
        IniFile ini)
    {
        var page = WorkspacePageByName(form, "Table manager");
        if (page == null) return null;
        foreach (var control in page.Controls.Cast<Control>().ToArray()) control.Dispose();
        var panel = new TableManagerPanel(manager, ini);
        page.Controls.Add(panel);
        return panel;
    }
}

internal sealed class TableManagerPanel : Panel
{
    private static readonly Color Canvas = DesktopWindow.Canvas;
    private static readonly Color Ink = DesktopWindow.Ink;
    private static readonly Color Border = DesktopWindow.Border;
    private readonly TableManagerEngine _manager;
    private readonly IniFile _ini;
    private readonly ComboBox _profiles = Choice("Layout");
    private readonly CheckBox _enabled = new() { Text = "Enable Table Manager", AutoSize = true };
    private readonly ListBox _slots = new() { AccessibleName = "Slots", IntegralHeight = false };
    private readonly NumericUpDown _left = Number(int.MinValue, int.MaxValue);
    private readonly NumericUpDown _top = Number(int.MinValue, int.MaxValue);
    private readonly NumericUpDown _width = Number(1, int.MaxValue);
    private readonly NumericUpDown _height = Number(-1, int.MaxValue);
    private readonly Label _status = new() { AutoSize = true };
    private readonly Button _duplicate;
    private readonly Button _delete;
    private readonly Button _apply;
    private readonly Button _add;
    private readonly Button _remove;
    private readonly Button _up;
    private readonly Button _down;
    private readonly GroupBox _geometry = new() { Text = "Slot geometry", Name = "SlotGeometry" };
    private TableManagerConfig _draft;
    private TableManagerConfig.Slot[] _savedHomes;
    private bool _loading;
    private bool _dirty;

    public TableManagerPanel(TableManagerEngine manager, IniFile ini)
    {
        _manager = manager;
        _ini = ini;
        Dock = DockStyle.Fill;
        BackColor = Canvas;
        ForeColor = Ink;
        AutoScroll = true;
        AutoScrollMinSize = new Size(769, 535);

        Put(this, _enabled, 20, 17);
        LabelAt(this, "Layout", 20, 51);
        Put(this, _profiles, 20, 70, 238);
        ButtonAt(this, "New", 270, 68, 62, () => Create(false));
        _duplicate = ButtonAt(this, "Duplicate", 340, 68, 85, () => Create(true));
        _delete = ButtonAt(this, "Delete", 433, 68, 65, DeleteProfile);

        var slotPanel = new GroupBox { Text = "Slots" };
        Put(this, slotPanel, 20, 116, 475, 365);
        _add = ButtonAt(slotPanel, "Add slot", 14, 24, 78, AddSlot);
        Put(slotPanel, _slots, 14, 63, 244, 241);
        _up = ButtonAt(slotPanel, "Up", 14, 316, 74, () => Reorder(-1));
        _down = ButtonAt(slotPanel, "Down", 94, 316, 87, () => Reorder(1));
        _remove = ButtonAt(slotPanel, "Remove", 187, 316, 71, RemoveSlot);

        Put(slotPanel, _geometry, 269, 60, 190, 151);
        Field(_geometry, "Left", _left, 13, 22, 75);
        Field(_geometry, "Top", _top, 99, 22, 75);
        Field(_geometry, "Width", _width, 13, 75, 75);
        Field(_geometry, "Height", _height, 99, 75, 75);
        LabelAt(_geometry, "Use −1 for automatic height", 13, 121);

        if (manager.RenderedClient)
            LabelAt(this, "Sisal uses home slots.\r\nWheel and automatic dialogs pause\r\nwhile Table Manager is enabled.", 513, 122);

        _apply = ButtonAt(this, "Save and activate", 20, 495, 132, Apply);
        _apply.BackColor = DesktopWindow.Accent;
        _apply.ForeColor = Color.White;
        Put(this, _status, 165, 502);

        _enabled.CheckedChanged += (_, _) => Run(() =>
        {
            if (!_loading) ChangeEnabled(_enabled.Checked);
        });
        _profiles.SelectedIndexChanged += (_, _) => Run(SelectProfile);
        _slots.SelectedIndexChanged += (_, _) => { if (!_loading) DisplaySlot(); };
        foreach (var control in new[] { _left, _top, _width, _height })
            control.ValueChanged += (_, _) => UpdateSlot();
        Run(Reload);
    }

    public void Reload()
    {
        ReloadProfiles(_manager.ConfigName);
        DisplayEnabled();
    }

    private void ReloadProfiles(string preferred)
    {
        var names = TableManagerConfig.ListProfiles(_manager.ConfigDir);
        _loading = true;
        try
        {
            _profiles.Items.Clear();
            _profiles.Items.AddRange(names);
            var match = Array.FindIndex(names, name => string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase));
            _profiles.SelectedIndex = match < 0 && names.Length > 0 ? 0 : match;
        }
        finally { _loading = false; }
        ReadDraft();
    }

    private void DisplayEnabled()
    {
        _loading = true;
        _enabled.Checked = _manager.Enabled && _manager.Config != null;
        _enabled.Enabled = _manager.Config != null;
        _loading = false;
    }

    private void SelectProfile()
    {
        if (_loading) return;
        if (ConfirmDiscard()) ReadDraft();
        else
        {
            _loading = true;
            _profiles.SelectedItem = _draft?.Name;
            _loading = false;
        }
    }

    private void ReadDraft()
    {
        _manager.ClearGeometryPreview();
        _draft = _profiles.SelectedItem is string name
            ? TableManagerConfig.Load(_manager.ConfigDir, name) : null;
        RememberSavedSlots();
        _dirty = false;
        _duplicate.Enabled = _delete.Enabled = _apply.Enabled = _draft != null;
        RefreshSlots(0);
        UpdateStatus();
    }

    private List<TableManagerConfig.Slot> Slots => _draft?.InactiveSlots;

    private void RefreshSlots(int selection)
    {
        _loading = true;
        _slots.Items.Clear();
        if (Slots is { } slots)
            _slots.Items.AddRange(slots.Select((slot, index) => SlotLabel(slot, index)).ToArray());
        _slots.SelectedIndex = _slots.Items.Count == 0 ? -1 : Math.Clamp(selection, 0, _slots.Items.Count - 1);
        _loading = false;
        DisplaySlot();
    }

    private void DisplaySlot()
    {
        int i = _slots.SelectedIndex;
        _add.Enabled = Slots is { Count: < TableManagerConfig.MaxSlots };
        _geometry.Enabled = _remove.Enabled = i >= 0;
        _up.Enabled = i > 0;
        _down.Enabled = i >= 0 && i < Slots.Count - 1;
        if (i < 0) return;

        var slot = Slots[i];
        _loading = true;
        _left.Value = slot.Left;
        _top.Value = slot.Top;
        _width.Value = slot.Width;
        _height.Value = Math.Max(-1, slot.Height);
        _loading = false;
    }

    private void UpdateSlot()
    {
        int i = _slots.SelectedIndex;
        if (_loading || i < 0) return;
        var slot = Slots[i];
        slot.Left = (int)_left.Value;
        slot.Top = (int)_top.Value;
        slot.Width = (int)_width.Value;
        slot.Height = (int)_height.Value;
        _slots.Items[i] = SlotLabel(slot, i);
        if (IsCurrentLayout())
        {
            int savedIndex = Array.IndexOf(_savedHomes, slot);
            if (savedIndex >= 0) _manager.PreviewGeometry(savedIndex, slot);
        }
        MarkDirty();
    }

    private void AddSlot()
    {
        if (Slots == null || Slots.Count >= TableManagerConfig.MaxSlots) return;
        var workArea = Screen.FromControl(this).WorkingArea;
        Slots.Add(new TableManagerConfig.Slot
        {
            Left = workArea.Left, Top = workArea.Top, Width = 640, Height = 461
        });
        RefreshSlots(Slots.Count - 1);
        MarkDirty();
    }

    private void RemoveSlot()
    {
        int i = _slots.SelectedIndex;
        if (i < 0) return;
        if (IsCurrentLayout())
        {
            int savedIndex = Array.IndexOf(_savedHomes, Slots[i]);
            if (savedIndex >= 0) _manager.ClearGeometryPreview(savedIndex);
        }
        Slots.RemoveAt(i);
        RefreshSlots(i);
        MarkDirty();
    }

    private void Reorder(int delta)
    {
        int i = _slots.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= Slots.Count) return;
        (Slots[i], Slots[j]) = (Slots[j], Slots[i]);
        RefreshSlots(j);
        MarkDirty();
    }

    private void Create(bool duplicate)
    {
        if (!duplicate && !ConfirmDiscard()) return;
        using var dialog = Dialog(duplicate ? "Duplicate layout" : "New layout", 320, 114);
        var input = new TextBox { AccessibleName = "Layout name", Text = duplicate ? _draft.Name + " copy" : "" };
        Field(dialog, "Layout name", input, 12, 10, 296);
        DialogButtons(dialog, "Create");
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        var name = input.Text.Trim();
        (duplicate ? _draft : new TableManagerConfig()).Save(_manager.ConfigDir, name, overwrite: false);
        ReloadProfiles(name);
    }

    private void DeleteProfile()
    {
        if (_draft == null || MessageBox.Show(FindForm(), $"Delete layout “{_draft.Name}”?",
            "Delete layout", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
            != DialogResult.Yes) return;

        var name = _draft.Name;
        if (string.Equals(name, _manager.ConfigName, StringComparison.OrdinalIgnoreCase)) ChangeEnabled(false);
        File.Delete(TableManagerConfig.ProfilePath(_manager.ConfigDir, name));
        _manager.Load(_ini);
        Reload();
    }

    private void Apply()
    {
        if (_draft == null) return;
        if (_draft.InactiveSlots.Count == 0 && _draft.ActiveSlots.Count == 0)
            throw new InvalidOperationException("Add at least one slot before activating the layout.");
        _draft.Save(_manager.ConfigDir, _draft.Name);
        UpdateSetting("TableManagerConfig", _draft.Name);
        RememberSavedSlots();
        _dirty = false;
        DisplayEnabled();
        UpdateStatus();
    }

    private void ChangeEnabled(bool enabled)
    {
        try { UpdateSetting("TableManagerOn", enabled ? "1" : "0"); }
        finally { DisplayEnabled(); }
        UpdateStatus();
    }

    private bool IsCurrentLayout() => _manager.Enabled &&
        string.Equals(_draft?.Name, _manager.ConfigName, StringComparison.OrdinalIgnoreCase);

    private void RememberSavedSlots() => _savedHomes = _draft?.InactiveSlots.ToArray() ?? [];

    private void UpdateSetting(string key, string value)
    {
        const string section = "PokerCaption";
        var previous = _ini.Get(section, key);
        _ini.Set(section, key, value);
        try { _ini.Save(); }
        catch
        {
            if (previous != null) _ini.Set(section, key, previous);
            else _ini.Find(section).Entries.RemoveAll(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            throw;
        }
        _manager.Load(_ini);
    }

    internal bool ConfirmDiscard() => !_dirty || MessageBox.Show(FindForm(), "Discard unsaved layout changes?",
        "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
        == DialogResult.Yes;

    private void MarkDirty()
    {
        _dirty = true;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var current = _manager.Config == null ? "No layout selected for use" : $"Current layout: {_manager.ConfigName}";
        if (!_manager.Enabled) current += " (manager off)";
        _status.Text = _dirty ? $"{current}   |   Unsaved changes" : current;
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _status.Text = ex.Message;
        }
    }

    private static string SlotLabel(TableManagerConfig.Slot slot, int i) =>
        $"{i + 1}.  {slot.Left}, {slot.Top}   ·   {slot.Width} × {slot.EffectiveHeight}";

    private static ComboBox Choice(string name, params string[] options)
    {
        var control = new ComboBox { AccessibleName = name, DropDownStyle = ComboBoxStyle.DropDownList };
        control.Items.AddRange(options);
        return control;
    }

    private static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max };

    private static void Put(Control parent, Control control, int x, int y, int width = 0, int height = 0)
    {
        control.Location = new Point(x, y);
        if (width > 0) control.Width = width;
        if (height > 0) control.Height = height;
        parent.Controls.Add(control);
    }

    private static void LabelAt(Control parent, string text, int x, int y) =>
        Put(parent, new Label { Text = text, AutoSize = true }, x, y);

    private static void Field(Control parent, string label, Control field, int x, int y, int width)
    {
        LabelAt(parent, label, x, y);
        field.AccessibleName = label;
        Put(parent, field, x, y + 19, width);
    }

    private Button ButtonAt(Control parent, string title, int x, int y, int width, Action action)
    {
        var button = new Button { Text = title, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Ink };
        button.FlatAppearance.BorderColor = Border;
        button.Click += (_, _) => Run(action);
        Put(parent, button, x, y, width, 27);
        return button;
    }

    private static Form Dialog(string title, int width, int height) => new()
    {
        Text = title, ClientSize = new Size(width, height), StartPosition = FormStartPosition.CenterParent,
        FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
        ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.Dpi
    };

    private static void DialogButtons(Form dialog, string accept)
    {
        var ok = new Button { Text = accept, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        Put(dialog, ok, dialog.ClientSize.Width - 194, dialog.ClientSize.Height - 38, 88, 26);
        Put(dialog, cancel, dialog.ClientSize.Width - 100, dialog.ClientSize.Height - 38, 88, 26);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
    }
}
