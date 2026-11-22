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
            check("TM preset stack and grid", stack.InactiveSlots is [{ Height: 461 }] &&
                stack.ActiveSlots.Count == 5 && grid.InactiveSlots.Count == 6 &&
                grid.InactiveSlots[0].EffectiveHeight == 461, "");

            var original = TableManagerConfig.ProfilePath(dir, "test");
            File.WriteAllText(original, "[inact]\nleft0=-200\ntop0=20\nwidth0=640\nheight0=455\nbind0=1\nmultislot0=1\nextra0=yes\n" +
                "[act]\nleft0=900\nwidth0=900\n[options]\nfuture=preserve\n");
            var config = TableManagerConfig.Load(dir, "test");
            config.Save(dir, "test");
            var reloaded = TableManagerConfig.Load(dir, "test");
            var raw = IniFile.Load(original);
            check("TM round-trip preserves negative coordinates, height and metadata and drops retired slot settings",
                reloaded.InactiveSlots is [{ Left: -200, Height: 455 }] &&
                raw.Get("inact", "extra0") == "yes" && raw.Get("inact", "bind0") == null && raw.Get("inact", "multislot0") == null &&
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
            var opened = new TableInfo { Hwnd = (IntPtr)9 };
            manager.OnTick(new[] { opened }, 900);
            check("TM offers an active slot immediately when a new table opens", opened.InActiveSlot &&
                opened.PlacedSlot == 0 && positions[opened.Hwnd].X == 900, "");
            manager.Load(ini);
            moves = 0;
            var first = new TableInfo { Hwnd = (IntPtr)1, HeroAct = true };
            var second = new TableInfo { Hwnd = (IntPtr)2, HeroAct = true };
            var third = new TableInfo { Hwnd = (IntPtr)3 };
            TableInfo[] tables = { first, second, third };
            manager.OnTick(tables, 1_000);
            var home = new Rectangle(-200, 20, 640, 455);
            check("TM reserves the home of the active table and leaves overflow unplaced",
                first.InActiveSlot && first.PlacedSlot == 0 && positions[first.Hwnd].X == 900 &&
                !second.InActiveSlot && second.PlacedSlot < 0 && !positions.ContainsKey(second.Hwnd) &&
                third.PlacedSlot < 0 && !positions.ContainsKey(third.Hwnd), "");

            first.HeroAct = false;
            first.Locked = true;
            manager.OnTick(tables, 1_010);
            check("TM locked table reserves its active slot and avoids redundant moves",
                first.InActiveSlot && !second.InActiveSlot && moves == 1, "");

            first.Locked = false;
            manager.OnTick(tables, 1_020);
            check("TM frees active slot and returns table to its previous home",
                !first.InActiveSlot && positions[first.Hwnd] == home && second.InActiveSlot &&
                positions[second.Hwnd].X == 900 && moves == 3, "");

            using var form = new Form { ClientSize = new Size(790, 550), ShowInTaskbar = false };
            var panel = new TableManagerPanel(manager, ini);
            form.Controls.Add(panel);
            form.Show();
            var controls = DesktopWindow.Descendants(panel).ToArray();
            var profile = controls.OfType<ComboBox>().Single(c => c.AccessibleName == "Layout");
            NumericUpDown GeometryField(string name) => controls.OfType<NumericUpDown>().Single(c => c.AccessibleName == name);
            var enable = controls.OfType<CheckBox>().Single(c => c.Text == "Enable Table Manager");
            var slots = controls.OfType<ListBox>().Single();
            void Click(string caption) => controls.OfType<Button>().Single(c => c.Text == caption).PerformClick();

            check("TM exposes a single home slot list without the retired options or capture button",
                slots.Items.Count == 1 && !controls.OfType<ComboBox>().Any(c => c.AccessibleName is "Slot type" or "Active slot binding") &&
                !controls.Any(c => c.Text is "Layout options" or "Use open table" or "Allow multiple tables in this slot"), "");
            GeometryField("Left").Value = 950;
            GeometryField("Top").Value = 30;
            GeometryField("Width").Value = 850;
            GeometryField("Height").Value = 620;
            manager.OnTick(tables, 1_030);
            check("TM previews existing slot geometry without saving or changing its assignment",
                positions[first.Hwnd] == new Rectangle(950, 30, 850, 620) && first.PlacedSlot == 0 &&
                positions[second.Hwnd].X == 900 && TableManagerConfig.Load(dir, "test").InactiveSlots[0].Left == -200, "");
            Click("Save and activate");
            check("TM saves edited home geometry and preserves configured active slots",
                manager.Config.InactiveSlots[0] is { Left: 950 } &&
                manager.Config.ActiveSlots.Select(slot => slot.Left).SequenceEqual(new[] { 900 }), "");

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
            using var testWindow = new Form { StartPosition = FormStartPosition.Manual, ShowInTaskbar = false,
                Bounds = new Rectangle(area.Left + 25, area.Top + 25, 540, 360) };
            testWindow.Show();
            Application.DoEvents();
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
            native.OnTick(new[] { new TableInfo { Hwnd = testWindow.Handle } });
            Application.DoEvents();
            check("TM moves and sizes a local WinForms window through Win32",
                NativeMethods.GetWindowRect(testWindow.Handle, out var rect) &&
                Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) == expected,
                $"actual={testWindow.Bounds}, expected={expected}, state={testWindow.WindowState}");

            var shortSlot = new Rectangle(expected.Location, new Size(400, 200));
            using (var proportionalWindow = new Form
            {
                StartPosition = FormStartPosition.Manual, ShowInTaskbar = false,
                Bounds = new Rectangle(expected.Location, new Size(480, 360))
            })
            {
                proportionalWindow.Resize += (_, _) =>
                {
                    int proportionalHeight = proportionalWindow.Width * 3 / 4;
                    if (proportionalWindow.Height != proportionalHeight) proportionalWindow.Height = proportionalHeight;
                };
                proportionalWindow.Show();
                Application.DoEvents();
                var layout = new TableManagerEngine(null) { ConfigDir = dir, RenderedClient = true };
                layout.Load(ini);
                layout.PreviewGeometry(0, new TableManagerConfig.Slot
                {
                    Left = shortSlot.Left, Top = shortSlot.Top, Width = shortSlot.Width, Height = shortSlot.Height
                });
                var proportionalTable = new TableInfo { Hwnd = proportionalWindow.Handle };
                layout.OnTick([proportionalTable], 1000);
                Application.DoEvents();
                int firstHeight = proportionalWindow.Height;
                layout.OnTick([proportionalTable], 1200);
                Application.DoEvents();
                layout.OnTick([proportionalTable], 1400);
                check("TM fits an aspect-constrained window after reading the actual height",
                    firstHeight == 300 && proportionalWindow.Width == 266 && proportionalWindow.Height <= shortSlot.Height &&
                    proportionalTable.PlacedSlot == 0,
                    $"first height={firstHeight}, final size={proportionalWindow.Size}");
            }
            check("TM scales width only when the client enforces a height larger than the explicit slot",
                TableManagerEngine.FitWithinHeight(shortSlot, expected) ==
                    new Rectangle(expected.Location, new Size(266, 200)) &&
                TableManagerEngine.FitWithinHeight(expected, expected) == null &&
                TableManagerEngine.FitWithinHeight(shortSlot, new Rectangle(0, 0, 401, 300)) == null, "");

            int heightOnly = expected.Height + 30;
            var direct = new TableInfo { Hwnd = testWindow.Handle };
            native.Load(ini);
            native.OnTick([direct], 250);
            native.PreviewGeometry(0, new TableManagerConfig.Slot
            {
                Left = expected.Left, Top = expected.Top, Width = expected.Width, Height = heightOnly
            });
            native.OnTick([direct], 400);
            Application.DoEvents();
            check("TM applies a Height-only preview without saving or changing width",
                testWindow.Bounds == new Rectangle(expected.Location, new Size(expected.Width, heightOnly)) &&
                TableManagerConfig.Load(dir, "native").InactiveSlots[0].Height == expected.Height, "");

            windowConfig.InactiveSlots[0].Left += 70;
            windowConfig.InactiveSlots[0].Width += 90;
            windowConfig.ActiveSlots.Add(new TableManagerConfig.Slot { Left = expected.X + 150 });
            windowConfig.Save(dir, "native");
            native.RenderedClient = true;
            native.Load(ini);
            int resizes = 0;
            testWindow.Resize += (_, _) => resizes++;
            var rendered = new TableInfo { Hwnd = testWindow.Handle, HeroAct = true };
            native.OnTick(new[] { rendered }, 1000);
            Application.DoEvents();
            var moved = new Rectangle(expected.X + 70, expected.Y, expected.Width + 90, expected.Height);
            check("TM rendered-client mode sizes the home window without activating an action slot",
                NativeMethods.GetWindowRect(testWindow.Handle, out rect) &&
                Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) == moved && resizes > 0 &&
                !rendered.InActiveSlot && native.LayoutOnly, $"actual={testWindow.Bounds}, expected={moved}, resizes={resizes}");
            native.OnTick(new[] { rendered }, 1150);
            var freePosition = new Point(moved.Right + 50, moved.Top + 20);
            testWindow.Location = freePosition;
            native.OnTick(new[] { rendered }, 1250);
            Application.DoEvents();
            check("TM keeps a window moved outside all slots where the user left it",
                testWindow.Location == freePosition && rendered.PlacedSlot == -1, "");

            ini.Set("PokerCaption", "TableManagerConfig", "4x");
            manager.RenderedClient = true;
            manager.Load(ini);
            TableInfo[] four = Enumerable.Range(1, 4).Select(i => new TableInfo { Hwnd = (IntPtr)i, Bb = i }).ToArray();
            manager.OnTick(four, 2000);
            var firstSlot = positions[four[0].Hwnd];
            var fourthSlot = positions[four[3].Hwnd];
            manager.DropTable(four[0], fourthSlot, firstSlot);
            manager.OnTick(four, 2100);
            check("TM swaps the windows in the first and fourth slots",
                four[0].PlacedSlot == 3 && positions[four[0].Hwnd] == fourthSlot &&
                four[3].PlacedSlot == 0 && positions[four[3].Hwnd] == firstSlot &&
                four[1].PlacedSlot == 1 && four[2].PlacedSlot == 2, "");

            var added = new TableInfo { Hwnd = (IntPtr)5, Bb = 100 };
            manager.OnTick([.. four, added], 2200);
            check("TM leaves a fifth table untouched while four slots are occupied",
                added.PlacedSlot < 0 && !positions.ContainsKey(added.Hwnd), "");
            four[3].Bb = 99;
            manager.OnTick([four[0], four[2], four[3], added], 3000);
            manager.OnTick([four[0], four[2], four[3], added], 6000);
            check("TM opens a new table in the vacant slot without sorting the others",
                added.PlacedSlot == 1 && four[0].PlacedSlot == 3 && four[2].PlacedSlot == 2 && four[3].PlacedSlot == 0, "");
        }
        finally { work.Delete(recursive: true); }
    }
}
