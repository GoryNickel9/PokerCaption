using System.Globalization;
using System.Text.RegularExpressions;
using PokerCaption.Config;
using PokerCaption.Engine;

namespace PokerCaption;

internal static class HudPreview
{
    private sealed record Seat(string Name, decimal Stack, string Actions);
    private sealed record Table(string Name, decimal Pot, decimal EffectiveStack, decimal ToCall, decimal RaiseTo, Seat[] Seats);

    public static int Run(bool verify)
    {
        ApplicationConfiguration.Initialize();
        var tableA = new Table("Tavolo A", 7m, 48m, 0m, 2m, new Seat[]
        {
            new("Player A", 330.6m, ""), new("Player B", 134.7m, "C|B"), null,
            new("Player C", 154.5m, ""), new("Hero", 48m, ""), null
        });
        var tableB = new Table("Tavolo B", 11m, 26m, 3m, 9m, new Seat[]
        {
            null, new("Player D", 80m, "B|B|B"), null,
            null, new("Hero", 26m, ""), null
        });

        var ini = new IniFile(string.Empty);
        ini.Set("HUD", "plFormatStr0", "[nick]  [stack_bb] BB");
        ini.Set("HUD", "plFormatStr1", "Actions: [actions]");
        ini.Set("HUD", "plFormatStr2", "Hidden panel");
        ini.Set("HUD", "plFVisible2", "0");
        using var hud = new HudEngine(_ => { });
        hud.Load(ini);

        using var window = new Form
        {
            Text = "PokerCaption — HUD di prova offline",
            ClientSize = new System.Drawing.Size(720, 300),
            StartPosition = FormStartPosition.CenterScreen
        };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildTable(tableA, hud.Panels));
        tabs.TabPages.Add(BuildTable(tableB, hud.Panels));
        window.Controls.Add(tabs);

        if (!verify)
        {
            Application.Run(window);
            return 0;
        }

        window.Show();
        Application.DoEvents();
        const string summaryA = "Piatto 7 BB   ·   Check   ·   Raise To 2 BB   ·   Pot odds —   ·   SPR 6.86";
        const string summaryB = "Piatto 11 BB   ·   Call 3 BB   ·   Raise To 9 BB   ·   Pot odds 21.43%   ·   SPR 2.36";
        bool firstTable = VerifyTable(tabs, 0, new[] { 1, 2, 0, 1, 1, 0 }, summaryA,
            "Player B  134.7 BB", "Actions: C|B", "Hero  48 BB");
        bool secondTable = VerifyTable(tabs, 1, new[] { 0, 2, 0, 0, 1, 0 }, summaryB,
            "Player D  80 BB", "Actions: B|B|B", "Hero  26 BB");
        bool backToFirstTable = VerifyTable(tabs, 0, new[] { 1, 2, 0, 1, 1, 0 }, summaryA,
            "Player B  134.7 BB", "Actions: C|B", "Hero  48 BB");
        return firstTable && secondTable && backToFirstTable ? 0 : 1;
    }

    private static TabPage BuildTable(Table snapshot, IReadOnlyList<HudEngine.PanelDef> panels)
    {
        var page = new TabPage(snapshot.Name);
        var table = new TableLayoutPanel { Name = "seats", Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        for (int i = 0; i < table.ColumnCount; i++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / table.ColumnCount));
        for (int i = 0; i < table.RowCount; i++)
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / table.RowCount));
        page.Controls.Add(table);

        for (int i = 0; i < snapshot.Seats.Length; i++)
        {
            var seat = snapshot.Seats[i];
            var group = new GroupBox { Text = $"Posto {i + 1}", Dock = DockStyle.Fill };
            var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
            group.Controls.Add(content);
            table.Controls.Add(group, i % table.ColumnCount, i / table.ColumnCount);
            if (seat == null)
            {
                content.Controls.Add(new Label { Text = "Vuoto", AutoSize = true });
                continue;
            }

            foreach (var panel in panels.Where(p => p.PlayerPanel && p.Visible))
            {
                var text = ResolvePlayer(panel.Format, seat);
                if (text.Length > 0)
                    content.Controls.Add(new Label
                    {
                        Name = "playerPanel", Text = text, AutoSize = true,
                        ForeColor = panel.FontColor, BackColor = System.Drawing.Color.FromArgb(35, 43, 55)
                    });
            }
        }

        var action = snapshot.ToCall > 0 ? $"Call {Number(snapshot.ToCall)} BB" : "Check";
        var odds = CallOdds(snapshot.Pot, snapshot.ToCall);
        var spr = Number(snapshot.EffectiveStack / snapshot.Pot);
        page.Controls.Add(new Label
        {
            Name = "tableSummary",
            Text = $"Piatto {Number(snapshot.Pot)} BB   ·   {action}   ·   Raise To {Number(snapshot.RaiseTo)} BB   ·   Pot odds {odds}   ·   SPR {spr}",
            Dock = DockStyle.Bottom,
            Height = 30,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        });
        return page;
    }

    private static bool VerifyTable(TabControl tabs, int index, int[] counts, string summary,
        string opponent, string action, string hero)
    {
        tabs.SelectedIndex = index;
        Application.DoEvents();
        var page = tabs.TabPages[index];
        var table = (TableLayoutPanel)page.Controls["seats"];
        var header = (Label)page.Controls["tableSummary"];
        var groups = table.Controls.OfType<GroupBox>().ToArray();
        var labelsBySeat = groups.Select(group => group.Controls[0].Controls.OfType<Label>()
            .Where(label => label.Name == "playerPanel" && label.Visible).Select(label => label.Text).ToArray()).ToArray();
        bool correct = header.Visible && header.Text == summary &&
            labelsBySeat.Select(labels => labels.Length).SequenceEqual(counts) &&
            labelsBySeat[1].SequenceEqual(new[] { opponent, action }) &&
            labelsBySeat[4].SequenceEqual(new[] { hero });
        Console.WriteLine($"{page.Text}: {(correct ? "OK" : "FAIL")} | {header.Text} | {string.Join(", ", labelsBySeat[1])} | {string.Join(", ", labelsBySeat[4])}");
        return correct;
    }

    private static string ResolvePlayer(string format, Seat seat)
    {
        bool hasTags = false;
        bool hasValue = false;
        string text = Regex.Replace(format, @"\[([^\[\]]+)\]", match =>
        {
            hasTags = true;
            var value = match.Groups[1].Value.ToLowerInvariant() switch
            {
                "nick" => seat.Name,
                "stack_bb" => Number(seat.Stack),
                "actions" => seat.Actions,
                _ => ""
            };
            hasValue |= value.Length > 0;
            return value;
        });
        return hasTags && !hasValue ? "" : text.Trim();
    }

    private static string CallOdds(decimal pot, decimal toCall) =>
        toCall > 0 ? Number(100m * toCall / (pot + toCall)) + "%" : "—";

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
