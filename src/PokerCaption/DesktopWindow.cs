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
            Margin = Padding.Empty, ColumnCount = 1, RowCount = 2
        };
        nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var logo = Heading("PokerCaption", 17, Color.White);
        nav.Controls.Add(logo, 0, 0);
        var links = Stack();
        links.Dock = DockStyle.Fill;
        links.WrapContents = false;
        nav.Controls.Add(links, 0, 1);
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
        title.Name = "workspaceHeading";
        work.Controls.Add(title, 0, 0);
        var pages = new WorkspacePages { Name = "workspace", Dock = DockStyle.Fill, Margin = Padding.Empty };
        work.Controls.Add(pages, 0, 1);
        var status = new Label
        {
            Name = "sessionStatus", Text = "Waiting for client", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = Padding.Empty
        };
        work.Controls.Add(status, 0, 2);
        shell.Controls.Add(work, 1, 0);

        var pagesAndLabels = new (Panel page, string label)[]
        {
            (HudPage(), "HUD"),
            (BetsPage(), "BetScript"),
            (WorkspacePage("Table manager"), "Table Layout"),
            (OptionsPage(), "Options")
        };
        var navButtons = new List<Button>();
        for (int i = 0; i < pagesAndLabels.Length; i++)
        {
            var (page, label) = pagesAndLabels[i];
            page.TabIndex = i;
            pages.Controls.Add(page);
            var link = Button("workspaceNav" + page.Name, label);
            link.AutoSize = false;
            link.Size = new Size(157, 40);
            link.TextAlign = ContentAlignment.MiddleLeft;
            link.Padding = new Padding(10, 0, 0, 0);
            link.BackColor = Rail;
            link.ForeColor = Color.White;
            link.FlatAppearance.BorderSize = 0;
            link.FlatAppearance.MouseOverBackColor = Selected;
            link.Click += (_, _) => pages.SelectPage(page);
            navButtons.Add(link);
            links.Controls.Add(link);
        }
        void SyncNavigation()
        {
            for (int i = 0; i < navButtons.Count; i++)
            {
                bool selected = pagesAndLabels[i].page == pages.SelectedPage;
                navButtons[i].BackColor = selected ? Selected : Rail;
                if (selected) title.Text = UiLanguage.Text(pagesAndLabels[i].label);
            }
        }
        pages.PageChanged += (_, _) => SyncNavigation();
        pages.SelectPage(pagesAndLabels[0].page);
        return form;
    }

    private static Panel HudPage()
    {
        var page = WorkspacePage("HUD");
        var layout = PageLayout();
        page.Controls.Add(layout);
        var toolbar = Row(Check("hudActive", "Table HUD", true), Check("lockHud", "Lock HUD"),
            Check("generalHudActive", "General HUD", true), Check("lockGeneral", "Lock General HUD"),
            Button("actionsTagConfig", "Action symbols"));
        layout.Controls.Add(toolbar, 0, 0);
        var families = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        layout.Controls.Add(families, 0, 1);
        families.TabPages.Add(HudFamily("Table panels", "tablePanels"));
        families.TabPages.Add(HudFamily("Player panels", "playerPanels"));
        families.TabPages.Add(HudFamily("General HUD", "generalHud", isGeneral: true));
        return page;
    }

    private static TabPage HudFamily(string title, string prefix, bool isGeneral = false)
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
        if (isGeneral)
        {
            listSide.RowCount = 3;
            listSide.RowStyles.Insert(2, new RowStyle(SizeType.AutoSize));
            listSide.Controls.Add(Row(Button(prefix + "MoveUp", "Up"), Button(prefix + "MoveDown", "Down")), 0, 2);
        }
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
        fields.Controls.Add(Row(Button(prefix + "Font", "Font"), Button(prefix + "BackColor", "Background")));
        return page;
    }

    private static Panel BetsPage()
    {
        var page = WorkspacePage("Betscript");
        var split = Columns(282);
        page.Controls.Add(split);

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
        return page;
    }

    private static Panel OptionsPage()
    {
        var page = WorkspacePage("Options");
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        page.Controls.Add(scroll);
        var contents = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(2, 0, 12, 12)
        };
        scroll.Controls.Add(contents);
        var language = new ComboBox
        {
            Name = "uiLanguage", AccessibleName = "Language", DropDownStyle = ComboBoxStyle.DropDownList, Width = 145
        };
        language.Items.AddRange(["English", "Italiano"]);
        contents.Controls.Add(Section("Application", Labeled("Language", language), Check("CheckBox34", "Minimize to tray", true),
            Check("CheckBox344", "Close PokerCaption after the client closes")));
        contents.Controls.Add(Section("Table preferences", Check("neverSitOut", "Never sit out"),
            Row(Check("neverSitOutLimit", "Maximum"), Spin("neverSitOutMaximum", "Maximum", 1, 999, 3))));
        contents.Controls.Add(Section("Table closure", Check("CheckBox173", "Automatically close finished tables")));
        contents.Controls.Add(Section("Dialogs",
            Check("CheckBox50", "Stay connected on inactivity timeout"),
            Check("dismissInsufficientChips", "Close the insufficient chips notice"),
            Check("dismissStackUpdate", "Close the stack update notice")));
        return page;
    }

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

    private static TextBox Field(string name, string label, int width) =>
        new() { Name = name, AccessibleName = label, Width = width, BorderStyle = BorderStyle.FixedSingle };



    private static NumericUpDown Spin(string name, string label, int min, int max, int value = 0) =>
        new() { Name = name, AccessibleName = label, Minimum = min, Maximum = max, Width = 66,
            TextAlign = HorizontalAlignment.Right, Value = Math.Clamp(value, min, max) };

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

    internal sealed class WorkspacePages : Panel
    {
        internal Control SelectedPage { get; private set; }
        internal event EventHandler PageChanged;

        internal void SelectPage(Control page)
        {
            bool changed = page != SelectedPage;
            SuspendLayout();
            try
            {
                SelectedPage = page;
                foreach (Control candidate in Controls)
                    candidate.Visible = candidate == page;
                page.BringToFront();
            }
            finally { ResumeLayout(); }
            if (changed) PageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static Panel WorkspacePage(string name) =>
        new() { Name = name, Dock = DockStyle.Fill, BackColor = Canvas, Visible = false };

    internal static WorkspacePages GetWorkspace(Form form) =>
        (WorkspacePages)form.Controls.Find("workspace", true).Single();
}
