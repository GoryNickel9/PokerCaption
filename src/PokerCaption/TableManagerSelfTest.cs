using System.Drawing;
using System.Windows.Forms;
using PokerCaption.Config;
using PokerCaption.Engine;
using PokerCaption.Native;

namespace PokerCaption;

internal static class TableManagerSelfTest
{
    public static void Run(Action<string, bool, string> check)
    {
        var work = Directory.CreateTempSubdirectory("poker-caption-tm");
        try
        {
            var dir = Path.Combine(work.FullName, "TableManagerConfigs");
            var ini = new IniFile(Path.Combine(work.FullName, "PokerCaption.ini"));
            TableManagerConfig.SeedProfiles(dir);
            var stack = TableManagerConfig.Load(dir, "stack");
            var grid = TableManagerConfig.Load(dir, "3x2");
            check("TM preset stack and grid", stack.InactiveSlots is [{ Bind: -2, Multislot: true, Height: 461 }] &&
                stack.ActiveSlots.Count == 5 && grid.InactiveSlots.Count == 6 &&
                grid.InactiveSlots[0].EffectiveHeight == 461, "");

            var original = TableManagerConfig.ProfilePath(dir, "test");
            File.WriteAllText(original, "[inact]\nleft0=-200\ntop0=20\nwidth0=640\nheight0=455\nbind0=1\nmultislot0=1\nextra0=yes\n" +
                "[act]\nleft0=10\nwidth0=800\nleft1=900\nwidth1=900\n[options]\nfuture=preserve\n");
            var config = TableManagerConfig.Load(dir, "test");
            config.Save(dir, "test");
            var reloaded = TableManagerConfig.Load(dir, "test");
            var raw = IniFile.Load(original);
            check("TM round-trip preserves negative coordinates, height, binding and metadata",
                reloaded.InactiveSlots is [{ Left: -200, Height: 455, Bind: 1, Multislot: true }] &&
                raw.Get("inact", "extra0") == "yes" &&
                raw.Get("options", "future") == "preserve", "");

            ini.Set("PokerCaption", "TableManagerConfig", "test");
            ini.Set("PokerCaption", "TableManagerOn", 1);
            var positions = new Dictionary<IntPtr, Rectangle>();
            int moves = 0;
            var manager = new TableManagerEngine(null, (handle, rect) => { moves++; positions[handle] = rect; return true; })
            {
                ConfigDir = dir
            };
            manager.Load(ini);
            var first = new TableInfo { Hwnd = (IntPtr)1, HeroAct = true };
            var second = new TableInfo { Hwnd = (IntPtr)2, HeroAct = true };
            var third = new TableInfo { Hwnd = (IntPtr)3 };
            TableInfo[] tables = { first, second, third };
            manager.OnTick(tables, 1_000);
            var home = new Rectangle(-200, 20, 640, 455);
            check("TM binding selects the requested active slot and stacks overflow at home",
                first.InActiveSlot && first.PlacedSlot == 1 && positions[first.Hwnd].X == 900 &&
                !second.InActiveSlot && positions[second.Hwnd] == home && positions[third.Hwnd] == home &&
                second.TablesInSlot == 2 && third.TablesInSlot == 2, "");

            first.HeroAct = false;
            first.Locked = true;
            manager.OnTick(tables, 1_010);
            check("TM locked table reserves its active slot and avoids redundant moves",
                first.InActiveSlot && !second.InActiveSlot && moves == 3, "");

            first.Locked = false;
            manager.OnTick(tables, 1_020);
            check("TM frees active slot and returns table to its previous home",
                !first.InActiveSlot && positions[first.Hwnd] == home && second.InActiveSlot &&
                positions[second.Hwnd].X == 900 && moves == 5, "");

            using var form = new Form { ClientSize = new Size(790, 550), ShowInTaskbar = false };
            var panel = new TableManagerPanel(manager, ini, () => tables);
            form.Controls.Add(panel);
            form.Show();
            var controls = DesktopWindow.Descendants(panel).ToArray();
            var profile = controls.OfType<ComboBox>().Single(c => c.AccessibleName == "Layout");
            NumericUpDown GeometryField(string name) => controls.OfType<NumericUpDown>().Single(c => c.AccessibleName == name);
            var enable = controls.OfType<CheckBox>().Single(c => c.Text == "Enable Table Manager");
            var slots = controls.OfType<ListBox>().Single();
            void Click(string caption) => controls.OfType<Button>().Single(c => c.Text == caption).PerformClick();

            check("TM exposes a single home slot list and four layout options",
                slots.Items.Count == 1 && !controls.OfType<ComboBox>().Any(c => c.AccessibleName == "Slot type") &&
                controls.OfType<GroupBox>().Single(c => c.Text == "Layout options").Controls.OfType<CheckBox>().Count() == 4, "");
            GeometryField("Left").Value = 950;
            GeometryField("Top").Value = 30;
            GeometryField("Width").Value = 850;
            GeometryField("Height").Value = 620;
            manager.OnTick(tables, 1_030);
            check("TM previews existing slot geometry without saving or changing its assignment",
                positions[first.Hwnd] == new Rectangle(950, 30, 850, 620) && first.PlacedSlot == 0 &&
                positions[second.Hwnd].X == 900 && TableManagerConfig.Load(dir, "test").InactiveSlots[0].Left == -200, "");
            Click("Save and activate");
            check("TM saves edited home geometry and preserves configured active slots and bindings",
                manager.Config.InactiveSlots[0] is { Left: 950, Bind: 1 } &&
                manager.Config.ActiveSlots.Select(slot => slot.Left).SequenceEqual(new[] { 10, 900 }), "");

            Click("Add slot");
            check("TM edit stays local before saving", slots.Items.Count == 2 && manager.Config.InactiveSlots.Count == 1, "");
            Click("Save and activate");
            Click("Remove");
            Click("Save and activate");
            check("TM screen adds/removes slots and updates the profile",
                manager.Config.InactiveSlots.Count == 1 && !IniFile.Load(original).Find("inact").TryGet("left1", out _), "");

            profile.SelectedItem = "4x";
            check("TM selecting a profile leaves the current layout in use", manager.ConfigName == "test", "");
            Click("Save and activate");
            enable.Checked = false;
            var saved = IniFile.Load(Path.Combine(work.FullName, "PokerCaption.ini"));
            check("TM screen persists profile selection and enable flag",
                manager.ConfigName == "4x" && !manager.Enabled && saved.Get("PokerCaption", "TableManagerConfig") == "4x" &&
                saved.Get("PokerCaption", "TableManagerOn") == "0", "");

            var windowConfig = new TableManagerConfig();
            var area = Screen.FromControl(form).WorkingArea;
            var expected = new Rectangle(area.Left + 40, area.Top + 60, 400, 300);
            windowConfig.InactiveSlots.Add(new TableManagerConfig.Slot
            {
                Left = expected.Left, Top = expected.Top, Width = expected.Width, Height = expected.Height
            });
            windowConfig.Save(dir, "native");
            ini.Set("PokerCaption", "TableManagerConfig", "native");
            ini.Set("PokerCaption", "TableManagerOn", 1);
            var native = new TableManagerEngine(null) { ConfigDir = dir };
            native.Load(ini);
            native.OnTick(new[] { new TableInfo { Hwnd = form.Handle } });
            check("TM moves and sizes a local WinForms window through Win32",
                NativeMethods.GetWindowRect(form.Handle, out var rect) &&
                Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) == expected, "");

            windowConfig.InactiveSlots[0].Left += 70;
            windowConfig.InactiveSlots[0].Width += 90;
            windowConfig.InactiveSlots[0].Bind = 0;
            windowConfig.ActiveSlots.Add(new TableManagerConfig.Slot { Left = expected.X + 150 });
            windowConfig.WithHeroOnly = true;
            windowConfig.Save(dir, "native");
            native.RenderedClient = true;
            native.Load(ini);
            int resizes = 0;
            form.Resize += (_, _) => resizes++;
            var rendered = new TableInfo { Hwnd = form.Handle, HeroAct = true };
            native.OnTick(new[] { rendered }, 1000);
            var moved = new Rectangle(expected.X + 70, expected.Y, expected.Width + 90, expected.Height);
            check("TM rendered-client mode sizes the home window without activating an action slot",
                NativeMethods.GetWindowRect(form.Handle, out rect) &&
                Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) == moved && resizes > 0 &&
                !rendered.InActiveSlot && native.LayoutOnly, "");
            form.Location = new Point(moved.X + 25, moved.Y + 20);
            int resizesBeforeReturn = resizes;
            native.OnTick(new[] { rendered }, 1250);
            check("TM returns a manually moved window to its assigned slot without resizing again",
                form.Bounds == moved && resizes == resizesBeforeReturn, "");

            File.WriteAllText(TableManagerConfig.ProfilePath(dir, "sorting"),
                "[inact]\nleft0=100\nleft1=200\n[options]\nautoSortTables=0\nautoSortTime=100\n");
            ini.Set("PokerCaption", "TableManagerConfig", "sorting");
            manager.Load(ini);
            var lower = new TableInfo { Hwnd = (IntPtr)1, Bb = 5, Title = "A" };
            var higher = new TableInfo { Hwnd = (IntPtr)2, Bb = 10, Title = "B" };
            TableInfo[] unsorted = { lower, higher };
            manager.OnTick(unsorted, 1_000);
            manager.OnTick(unsorted, 1_499);
            bool waited = positions[lower.Hwnd].X == 100;
            manager.OnTick(unsorted, 1_500);
            bool reordered = positions[higher.Hwnd].X == 100;
            int settledMoves = moves;
            manager.OnTick(unsorted, 5_000);
            bool stable = moves == settledMoves;
            lower.Bb = 10;
            manager.OnTick(unsorted, 5_010);
            manager.OnTick(unsorted, 5_510);
            check("TM always sorts by blinds then title after 500 ms despite legacy settings",
                waited && reordered && stable && positions[lower.Hwnd].X == 100, "");
        }
        finally { work.Delete(recursive: true); }
    }
}
