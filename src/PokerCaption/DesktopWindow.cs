using System.Drawing;

namespace PokerCaption;

internal static class DesktopWindow
{
    internal static readonly Color Canvas = Color.FromArgb(241, 243, 250);
    internal static readonly Color Ink = Color.FromArgb(38, 46, 72);
    internal static readonly Color Border = Color.FromArgb(208, 214, 232);
    private static readonly Color Rail = Color.FromArgb(35, 39, 65);
    internal static readonly Color Accent = Color.FromArgb(81, 68, 153);
    private static readonly Color Selected = Color.FromArgb(73, 66, 118);

    internal static Form Create()
    {
        var form = new Form
        {
            Text = "PokerCaption", Icon = ProductBrand.Icon, Font = new Font("Segoe UI", 10),
            BackColor = Canvas, ForeColor = Ink, StartPosition = FormStartPosition.CenterScreen,
            AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(1180, 745), MinimumSize = new Size(1000, 620)
        };
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.Controls.Add(shell);

        var nav = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Rail, Padding = new Padding(16, 22, 16, 18),
            Margin = Padding.Empty, ColumnCount = 1, RowCount = 3
        };
        nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var logo = Heading("PokerCaption", 17, Color.White);
        nav.Controls.Add(logo, 0, 0);
        var links = Stack();
        links.Dock = DockStyle.Fill;
        links.WrapContents = false;
        nav.Controls.Add(links, 0, 1);
        var save = Button("saveOptions", "Save settings", primary: true);
        save.Dock = DockStyle.Fill;
        save.Margin = Padding.Empty;
        nav.Controls.Add(save, 0, 2);
        shell.Controls.Add(nav, 0, 0);

        var work = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(22, 16, 22, 12),
            ColumnCount = 1, RowCount = 3, BackColor = Canvas
        };
        work.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        work.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        work.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        var title = Heading("HUD", 21, Ink);
        work.Controls.Add(title, 0, 0);
        var pages = new WorkspaceTabs { Name = "workspace", Dock = DockStyle.Fill, Margin = Padding.Empty, TabStop = false };
        work.Controls.Add(pages, 0, 1);
        var status = new Label
        {
            Name = "sessionStatus", Text = "Waiting for client", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = Padding.Empty
        };
        work.Controls.Add(status, 0, 2);
        shell.Controls.Add(work, 1, 0);

        var pagesAndLabels = new (TabPage page, string label)[]
        {
            (HudPage(), "HUD"),
            (new TabPage("Table manager") { BackColor = Canvas }, "Table layout"),
            (BetsPage(), "BetScript"),
            (OptionsPage(), "Options")
        };
        var navButtons = new List<Button>();
        foreach (var (page, label) in pagesAndLabels)
        {
            pages.TabPages.Add(page);
            var link = Button("", label);
            link.AutoSize = false;
            link.Size = new Size(157, 40);
            link.TextAlign = ContentAlignment.MiddleLeft;
            link.Padding = new Padding(10, 0, 0, 0);
            link.BackColor = Rail;
            link.ForeColor = Color.White;
            link.FlatAppearance.BorderSize = 0;
            link.FlatAppearance.MouseOverBackColor = Selected;
            link.Click += (_, _) => pages.SelectedTab = page;
            navButtons.Add(link);
            links.Controls.Add(link);
        }
        void SyncNavigation()
        {
            int current = Math.Max(0, pages.SelectedIndex);
            title.Text = pagesAndLabels[current].label;
            for (int i = 0; i < navButtons.Count; i++)
                navButtons[i].BackColor = i == current ? Selected : Rail;
        }
        pages.SelectedIndexChanged += (_, _) => SyncNavigation();
        SyncNavigation();
        return form;
    }

    private static TabPage HudPage()
    {
        var page = new TabPage("HUD") { BackColor = Canvas };
        var layout = PageLayout();
        page.Controls.Add(layout);
        var toolbar = Row(Check("hudActive", "Table HUD", true), Check("lockHud", "Lock HUD"),
            Check("generalHudActive", "General HUD", true), Check("lockGeneral", "Lock General HUD"),
            Button("actionsTagConfig", "Action symbols"));
        layout.Controls.Add(toolbar, 0, 0);
        var families = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        layout.Controls.Add(families, 0, 1);
        families.TabPages.Add(HudFamily("Table panels", "tablePanels", isTable: true));
        families.TabPages.Add(HudFamily("Player panels", "playerPanels"));
        families.TabPages.Add(HudFamily("General HUD", "generalHud", isGeneral: true));
        return page;
    }

    private static TabPage HudFamily(string title, string prefix, bool isTable = false, bool isGeneral = false)
    {
        var page = new TabPage(title) { BackColor = Canvas, Padding = new Padding(8) };
        var split = Columns(270);
        page.Controls.Add(split);
        var listSide = PageLayout();
        listSide.Padding = new Padding(12);
        listSide.BackColor = Color.White;
        listSide.Controls.Add(Row(Button(prefix + "Add", "Add", primary: true),
            Button(prefix + "Delete", "Delete")), 0, 0);
        var list = new ListBox { Name = isGeneral ? "generalHudPanelsList" : prefix + "List", Dock = DockStyle.Fill, IntegralHeight = false,
            AccessibleName = title, Margin = new Padding(0, 8, 0, 8) };
        listSide.Controls.Add(list, 0, 1);
        var order = Row(Button(prefix + "MoveUp", "Up"), Button(prefix + "MoveDown", "Down"));
        if (!isGeneral) order.Controls.Add(Button(prefix + "Clone", "Duplicate"));
        listSide.RowCount = 3;
        listSide.RowStyles.Insert(2, new RowStyle(SizeType.AutoSize));
        listSide.Controls.Add(order, 0, 2);
        split.Controls.Add(listSide, 0, 0);

        var inspector = Editor(out var fields);
        split.Controls.Add(inspector, 1, 0);
        var format = Field(prefix + "FormatStr", "Panel format", 345);
        format.Multiline = true;
        format.Height = 63;
        fields.Controls.Add(Labeled("Format", format));
        var formatActions = Row(Button(prefix + "AddTag", "Insert tag"));
        if (!isGeneral) formatActions.Controls.Add(Check(prefix + "Visible", "Visible", true));
        formatActions.Controls.Add(Button(prefix + "FontColor", "Text colour"));
        fields.Controls.Add(formatActions);
        if (isTable)
        {
            fields.Controls.Add(Row(Button("SpeedButton32", "Font"), Button("SpeedButton23", "Background"),
                Button("SpeedButton68", "Frame colour"), Button("SpeedButton126", "Position")));
            var position = Stack();
            position.Name = "Panel115";
            position.Controls.Add(Row(Pair("X", "tablePanelsX", "UpDown103"), Pair("Y", "tablePanelsY", "UpDown104")));
            fields.Controls.Add(position);
            fields.Controls.Add(Row(Check("CheckBox93", "Transparent background", true), Check("CheckBox538", "Rounded"),
                Check("CheckBox540", "Group with previous")));
            fields.Controls.Add(Row(Pair("Padding", "Edit175", "UpDown120"), Pair("Frame", "Edit140", "UpDown106")));
            fields.Controls.Add(Row(Check("CheckBox539", "Fixed width"), Pair("Pixels", "Edit139", "UpDown102")));
            fields.Controls.Add(Row(Alignment("RadioGroup5", 1, "Horizontal", "Left", "Centre", "Right"),
                Alignment("RadioGroup17", 0, "Vertical", "Top", "Middle", "Bottom")));
            fields.Controls.Add(Row(Labeled("On click", Choice("ComboBox34")), Labeled("On hover", Choice("ComboBox23"))));
        }
        else if (isGeneral)
        {
            fields.Controls.Add(Row(Button("generalHudFont", "Font"), Button("generalHudBackColor", "Background")));
        }
        return page;
    }

    private static TabPage BetsPage()
    {
        var page = new TabPage("Betscript") { BackColor = Canvas };
        var layout = PageLayout();
        page.Controls.Add(layout);
        layout.Controls.Add(Row(Check("CheckBox72", "Enable BetScript", true), new Label
        {
            Name = "BetscriptLayoutNotice", Text = "Paused while Table Manager is enabled on Sisal.",
            AutoSize = true, Visible = false, Margin = new Padding(10, 6, 0, 0)
        }), 0, 0);
        var split = Columns(282);
        split.Margin = new Padding(0, 12, 0, 0);
        layout.Controls.Add(split, 0, 1);

        var rules = PageLayout();
        rules.BackColor = Color.White;
        rules.Padding = new Padding(12);
        rules.Controls.Add(Row(Button("SpeedButton9", "Add rule", true), Button("SpeedButton13", "Duplicate"),
            Button("SpeedButton10", "Delete")), 0, 0);
        rules.Controls.Add(new ListBox
        {
            Name = "ListBox4", AccessibleName = "Rules in priority order", Dock = DockStyle.Fill,
            IntegralHeight = false, HorizontalScrollbar = true, Margin = new Padding(0, 8, 0, 8)
        }, 0, 1);
        rules.RowCount = 3;
        rules.RowStyles.Insert(2, new RowStyle(SizeType.AutoSize));
        rules.Controls.Add(Row(Button("SpeedButton11", "Move up"), Button("SpeedButton12", "Move down")), 0, 2);
        split.Controls.Add(rules, 0, 0);

        var editor = Editor(out var items);
        editor.Name = "GroupBox9";
        split.Controls.Add(editor, 1, 0);
        items.Controls.Add(Heading("Rule settings", 13, Ink));
        var help = Button("BetFormulaHelp", "?");
        help.AccessibleName = "BetScript formula help";
        items.Controls.Add(Row(Labeled("Formula", Field("Edit16", "Bet formula", 260)), help));
        var streets = new CheckedListBox
        {
            Name = "CheckListBox2", AccessibleName = "Streets for this rule", Size = new Size(136, 94),
            CheckOnClick = true, IntegralHeight = false
        };
        streets.Items.AddRange(new object[] { "Preflop", "Flop", "Turn", "River" });
        items.Controls.Add(Labeled("Streets", streets));
        items.Controls.Add(Row(Labeled("Round to (% of BB)", Field("Edit27", "Round to percent of big blind", 85)),
            Labeled("Shove above (% of stack)", Field("Edit28", "Shove above percent of stack", 85))));
        return page;
    }

    private static TabPage OptionsPage()
    {
        var page = new TabPage("Options") { BackColor = Canvas };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        page.Controls.Add(scroll);
        var contents = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(2, 0, 12, 12)
        };
        scroll.Controls.Add(contents);
        contents.Controls.Add(Section("Application", Check("CheckBox34", "Minimize to tray", true),
            Check("CheckBox344", "Close PokerCaption after the last client closes")));
        var max = Check("CheckBox279", "Maximum");
        var maximum = Field("Edit236", "Maximum", 50);
        maximum.Text = "3";
        contents.Controls.Add(Section("Table preferences",
            Check("CheckBox90", "Never sit out on cash tables"), Row(max, maximum,
                Spin("UpDown165", "Maximum", 1, 999, 3)), Check("CheckBox94", "Never sit out on tournament tables")));
        contents.Controls.Add(Section("Table closure", Check("CheckBox173", "Auto close 'This table has been closed' tables"),
            Check("CheckBox401", "Auto close 'Play at this table has concluded' tables"),
            Check("CheckBox504", "Auto close 'Tournament is over' tables"),
            Check("CheckBox283", "Close tournament lobby when its last table closes"),
            Check("CheckBox549", "Auto close table in waiting list")));

        var dialogItems = DialogRules.Select(rule => Check(rule.name, $"Close '{rule.caption}'")).ToList();
        foreach (var (name, caption) in new[]
        {
            ("CheckBox390", "Confirm Waiting List"), ("CheckBox316", "Close Current Session Information"),
            ("CheckBox208", "Keep playing in Current Session"), ("CheckBox50", "Stay connected on inactivity timeout")
        }) dialogItems.Add(Check(name, caption));

        var columns = new TableLayoutPanel
        {
            Name = "dialogColumns", Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        int splitAt = (dialogItems.Count + 1) / 2;
        for (int i = 0; i < 2; i++)
        {
            var column = Stack();
            column.Dock = DockStyle.Top;
            column.Padding = new Padding(0, 0, 8, 0);
            column.Controls.AddRange((i == 0 ? dialogItems.Take(splitAt) : dialogItems.Skip(splitAt)).ToArray());
            columns.Controls.Add(column, i, 0);
        }
        var dialogs = new GroupBox
        {
            Text = "Dialogs", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top, BackColor = Color.White, ForeColor = Ink, Padding = new Padding(12, 7, 12, 8)
        };
        dialogs.Controls.Add(columns);
        contents.Controls.Add(dialogs);
        scroll.ClientSizeChanged += (_, _) => FitDialogColumns();
        FitDialogColumns();
        return page;

        void FitDialogColumns()
        {
            int maxWidth = Math.Max(220, (scroll.ClientSize.Width - 46) / 2 - 20);
            foreach (var box in dialogItems)
            {
                int checkWidth = box.GetPreferredSize(Size.Empty).Width -
                    TextRenderer.MeasureText(box.Text, box.Font).Width;
                int textHeight = TextRenderer.MeasureText(box.Text, box.Font,
                    new Size(maxWidth - Math.Max(checkWidth, 20), int.MaxValue), TextFormatFlags.WordBreak).Height;
                box.AutoSize = false;
                box.Size = new Size(maxWidth, Math.Max(box.GetPreferredSize(Size.Empty).Height, textHeight + 4));
            }
        }
    }

    private static readonly (string name, string caption)[] DialogRules =
    [
        ("cbCloseMyNews", "My News"), ("cbCloseLogoutInfo", "PokerStars Logout Information"),
        ("cbStarsAccountInfo", "Stars Account Information"), ("CheckBox532", "Chest Announcement"),
        ("CheckBox472", "Active Session Reminder"), ("CheckBox421", "Reminder: Your current sesson has been active for X hours"),
        ("CheckBox477", "Feedback for PokerStars"), ("CheckBox508", "Responsible Gaming"),
        ("CheckBox506", "You have been successfully removed from all the waiting lists"),
        ("CheckBox500", "You reached the cap of cash game tables"),
        ("CheckBox313", "You are playing at too many tables"),
        ("CheckBox312", "You are currently playing the maximum allowed Zoom tables at once"),
        ("CheckBox452", "Your auto-rebuy was restricted to"), ("CheckBox327", "table is closed"),
        ("CheckBox89", "You did not act in time, this seat is no longer reserved for you"),
        ("CheckBox372", "You have been unregistered from"),
        ("CheckBox41", "Tournament Registration / Show Lobby"),
        ("CheckBox63", "Tournament Registration / Welcome to the tournament"),
        ("CheckBox170", "Would you like to register into another Satellite?")
    ];

    internal static Form CreateTagPicker()
    {
        var form = new Form
        {
            Text = "Insert HUD tag", StartPosition = FormStartPosition.CenterParent, Icon = ProductBrand.Icon,
            Font = new Font("Segoe UI", 10), ClientSize = new Size(540, 420), MinimumSize = new Size(400, 300),
            MinimizeBox = false, ShowInTaskbar = false, BackColor = Canvas, ForeColor = Ink
        };
        var table = PageLayout();
        table.Padding = new Padding(16);
        table.RowCount = 3;
        table.RowStyles.Insert(2, new RowStyle(SizeType.AutoSize));
        form.Controls.Add(table);
        table.Controls.Add(Labeled("Filter tags", Field("Edit1", "Filter HUD tags", 335)), 0, 0);
        table.Controls.Add(new ListBox { Name = "ListBox1", AccessibleName = "HUD tags",
            Dock = DockStyle.Fill, IntegralHeight = false, Margin = new Padding(0, 8, 0, 8) }, 0, 1);
        var insert = Button("Button1", "Insert", true);
        var cancel = Button("Button2", "Cancel");
        insert.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        table.Controls.Add(Row(insert, cancel), 0, 2);
        form.AcceptButton = insert;
        form.CancelButton = cancel;
        return form;
    }

    internal static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control control in root.Controls)
        {
            yield return control;
            foreach (var child in Descendants(control)) yield return child;
        }
    }

    private static TableLayoutPanel PageLayout()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return panel;
    }

    private static TableLayoutPanel Columns(int left)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, left));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return panel;
    }

    private static Panel Editor(out FlowLayoutPanel fields)
    {
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White,
            Padding = new Padding(14), Margin = new Padding(10, 0, 0, 0) };
        fields = Stack();
        fields.Dock = DockStyle.Top;
        panel.Controls.Add(fields);
        return panel;
    }

    private static FlowLayoutPanel Stack() => new()
    {
        FlowDirection = FlowDirection.TopDown, AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty
    };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static GroupBox Section(string title, params Control[] controls)
    {
        var section = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top, BackColor = Color.White, ForeColor = Ink, Padding = new Padding(12, 7, 12, 8) };
        var body = Stack();
        body.Dock = DockStyle.Top;
        body.Controls.AddRange(controls);
        section.Controls.Add(body);
        return section;
    }

    private static Control Labeled(string title, Control input)
    {
        var stack = Stack();
        stack.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(3, 2, 3, 2) });
        stack.Controls.Add(input);
        return stack;
    }

    private static Control Pair(string label, string edit, string spin) =>
        Labeled(label, Row(Field(edit, label, 64), Spin(spin, label, -999999, 999999)));

    private static Control Alignment(string name, int selected, string label, params string[] choices)
    {
        var group = Row();
        group.Name = name;
        for (int i = 0; i < choices.Length; i++)
            group.Controls.Add(new RadioButton { Text = choices[i], Checked = i == selected, AutoSize = true });
        return Labeled(label, group);
    }

    private static TextBox Field(string name, string label, int width) =>
        new() { Name = name, AccessibleName = label, Width = width, BorderStyle = BorderStyle.FixedSingle };

    private static ComboBox Choice(string name) =>
        new() { Name = name, Width = 176, DropDownStyle = ComboBoxStyle.DropDownList };

    private static NumericUpDown Spin(string name, string label, int min, int max, int value = 0) =>
        new() { Name = name, AccessibleName = label, Minimum = min, Maximum = max, Width = 22,
            ReadOnly = true, TabStop = false, Value = Math.Clamp(value, min, max) };

    private static CheckBox Check(string name, string label, bool value = false) =>
        new() { Name = name, Text = label, Checked = value, AutoSize = true, Margin = new Padding(4, 6, 8, 5) };

    private static Label Heading(string title, int size, Color color)
    {
        var font = new Font("Segoe UI", size, FontStyle.Bold);
        var label = new Label { Text = title, AutoSize = true, Font = font, ForeColor = color, Margin = Padding.Empty };
        label.Disposed += (_, _) => font.Dispose();
        return label;
    }

    private static Button Button(string name, string label, bool primary = false)
    {
        var button = new Button
        {
            Name = name, Text = label, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(48, 32), Padding = new Padding(4, 0, 4, 0),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Color.White,
            ForeColor = primary ? Color.White : Ink
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        return button;
    }

    private sealed class WorkspaceTabs : TabControl
    {
        internal WorkspaceTabs()
        {
            Appearance = TabAppearance.FlatButtons;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(0, 1);
        }

        protected override void WndProc(ref Message message)
        {
            const int adjustTabRectangle = 0x1328;
            if (message.Msg == adjustTabRectangle) { message.Result = (IntPtr)1; return; }
            base.WndProc(ref message);
        }
    }
}
