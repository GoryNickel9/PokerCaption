using System.Drawing;
using PokerCaption.Config;
using PokerCaption.Native;

namespace PokerCaption.Engine;

public sealed class TableManagerEngine
{
    private readonly Action<string> _log;
    private readonly Func<IntPtr, Rectangle, bool> _place;
    private readonly Dictionary<int, Rectangle> _geometryPreview = new();
    private readonly Dictionary<TableInfo, Placement> _placements = new();
    private long _nextSort;
    private const int MoveRetryMs = 1000;
    private volatile bool _enabled;

    private sealed class Placement
    {
        public int Home = -1;
        public int Active = -1;
        public bool OpenedActive;
        public Rectangle? Bounds;
        public long NextMove;
        public double LastBlind;
    }

    public string ConfigName { get; private set; } = "4x";
    public bool Enabled => _enabled;
    public TableManagerConfig Config { get; private set; }
    public string ConfigDir { get; set; }
    internal bool RenderedClient { get; set; }
    internal bool LayoutOnly => Enabled && RenderedClient;
    internal event Action LayoutModeChanged;

    public TableManagerEngine(Action<string> log) : this(log, null) { }

    internal TableManagerEngine(Action<string> log, Func<IntPtr, Rectangle, bool> place)
    {
        _log = log ?? (_ => { });
        _place = place;
    }

    internal void PreviewGeometry(int slot, TableManagerConfig.Slot geometry) =>
        _geometryPreview[slot] = new Rectangle(geometry.Left, geometry.Top, geometry.Width, geometry.EffectiveHeight);

    internal void ClearGeometryPreview() => _geometryPreview.Clear();

    internal void ClearGeometryPreview(int slot) => _geometryPreview.Remove(slot);

    public void Load(IniFile ini)
    {
        bool wasLayoutOnly = LayoutOnly;
        ConfigName = ini.Get("PokerCaption", "TableManagerConfig", "4x");
        _enabled = ini.GetBool("PokerCaption", "TableManagerOn", false);
        Config = null;
        ClearGeometryPreview();
        foreach (var table in _placements.Keys) ClearSlot(table);
        _placements.Clear();
        _nextSort = 0;
        try
        {
            if (!string.IsNullOrEmpty(ConfigDir) && File.Exists(TableManagerConfig.ProfilePath(ConfigDir, ConfigName)))
                Config = TableManagerConfig.Load(ConfigDir, ConfigName);
        }
        catch (Exception ex) { _log("TM config ERR: " + ex.Message); }
        if (wasLayoutOnly != LayoutOnly) LayoutModeChanged?.Invoke();
    }

    public void OnTick(IReadOnlyList<TableInfo> tables) => OnTick(tables, Environment.TickCount64);

    internal void OnTick(IReadOnlyList<TableInfo> tables, long now)
    {
        if (!Enabled || Config == null) return;

        var closedTables = _placements.Keys.Where(t => !tables.Contains(t)).ToArray();
        foreach (var closed in closedTables)
            _placements.Remove(closed);
        bool sortInputsChanged = closedTables.Length > 0;

        var candidates = tables;
        foreach (var table in tables)
        {
            if (!_placements.TryGetValue(table, out var state))
            {
                state = new Placement { OpenedActive = true, LastBlind = table.Bb };
                _placements.Add(table, state);
                sortInputsChanged = true;
            }
            else if (!state.LastBlind.Equals(table.Bb))
            {
                state.LastBlind = table.Bb;
                sortInputsChanged = true;
            }
        }

        if (sortInputsChanged)
            _nextSort = now + TableManagerConfig.AutoSortDelayMs;
        bool sort = _nextSort != 0 && now >= _nextSort;
        if (sort)
        {
            _nextSort = 0;
            candidates = candidates.OrderByDescending(table => table.Bb)
                .ThenBy(table => table.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var homeSlots = Config.InactiveSlots;
        var activeSlots = Config.ActiveSlots;
        var homes = new int[homeSlots.Count];
        var active = new int[activeSlots.Count];

        // Keep each table's home slot while it is active, so binding and return are stable.
        foreach (var table in candidates)
        {
            var state = _placements[table];
            if (sort && !table.Locked) state.Home = -1;
            if (state.Home >= 0) homes[state.Home]++;
            if (state.Active >= 0 && table.Locked) active[state.Active]++;
        }

        foreach (var table in candidates)
        {
            var state = _placements[table];
            if (table.Locked) continue;
            if (LayoutOnly) table.InActiveSlot = false;
            if (state.Home < 0)
                state.Home = Claim(homeSlots, homes, TableManagerConfig.AnyActiveSlot);
            if (LayoutOnly) continue;

            bool wantsActive = table.HeroAct ||
                state.OpenedActive ||
                (table.InActiveSlot && table.HeroInHand);
            if (table.HeroAct || table.HeroInHand || table.HeroFolded) state.OpenedActive = false;
            if (state.Active >= 0 && (!wantsActive || sort)) state.Active = -1;
            if (state.Active >= 0) active[state.Active]++;
            table.InActiveSlot = wantsActive;
        }

        // Existing active assignments reserve their slots before another table can claim one.
        foreach (var table in candidates)
        {
            var state = _placements[table];
            if (table.Locked || !table.InActiveSlot || state.Active >= 0) continue;
            var binding = state.Home < 0 ? TableManagerConfig.AnyActiveSlot : homeSlots[state.Home].Bind;
            if (binding != TableManagerConfig.NoBinding)
                state.Active = Claim(activeSlots, active, binding);
        }

        var occupancy = new Dictionary<Rectangle, int>();
        foreach (var table in candidates)
        {
            var state = _placements[table];
            if (!table.Locked)
            {
                table.InActiveSlot = state.Active >= 0;
                table.PlacedSlot = state.Active >= 0 ? state.Active : state.Home;
                var slots = state.Active >= 0 ? activeSlots : homeSlots;
                if (table.PlacedSlot < 0) state.Bounds = null;
                else
                {
                    var slot = slots[table.PlacedSlot];
                    if (state.Active >= 0 || !_geometryPreview.TryGetValue(table.PlacedSlot, out var bounds))
                        bounds = new Rectangle(slot.Left, slot.Top, slot.Width, slot.EffectiveHeight);
                    bool geometryChanged = state.Bounds != bounds;
                    if (now >= state.NextMove && (sort || geometryChanged ||
                        (_place == null && PositionChanged(table.Hwnd, bounds.Location))))
                    {
                        bool placed = _place?.Invoke(table.Hwnd, bounds) ?? PlaceWindow(table.Hwnd, bounds, geometryChanged || sort);
                        if (placed) state.Bounds = bounds;
                        state.NextMove = placed ? (_place == null ? now + 150 : now) : now + MoveRetryMs;
                    }
                }
            }
            if (state.Bounds is Rectangle rect)
                occupancy[rect] = occupancy.GetValueOrDefault(rect) + 1;
        }
        foreach (var table in candidates)
        {
            var bounds = _placements[table].Bounds;
            table.TablesInSlot = bounds is Rectangle rect ? occupancy[rect] : 1;
        }
    }

    private static int Claim(IReadOnlyList<TableManagerConfig.Slot> slots, int[] occupied, int binding)
    {
        if (binding == TableManagerConfig.AnyActiveSlot)
        {
            binding = -1;
            for (int i = 0; i < slots.Count; i++)
                if (occupied[i] == 0) { binding = i; break; }
            if (binding < 0)
                for (int i = 0; i < slots.Count; i++)
                    if (slots[i].Multislot) { binding = i; break; }
        }
        if (binding < 0 || binding >= slots.Count ||
            (occupied[binding] > 0 && !slots[binding].Multislot))
            return -1;
        occupied[binding]++;
        return binding;
    }

    private static void ClearSlot(TableInfo table)
    {
        table.InActiveSlot = false;
        table.PlacedSlot = -1;
        table.TablesInSlot = 1;
    }

    private static bool PositionChanged(IntPtr hwnd, Point expected) =>
        NativeMethods.GetWindowRect(hwnd, out var rect) && (rect.Left != expected.X || rect.Top != expected.Y);

    private static bool PlaceWindow(IntPtr hwnd, Rectangle bounds, bool resize)
    {
        if (NativeMethods.IsIconic(hwnd) || NativeMethods.IsZoomed(hwnd) ||
            NativeMethods.IsHungAppWindow(hwnd) || NativeMethods.IsInMoveSize(hwnd) ||
            !NativeMethods.GetWindowRect(hwnd, out var current))
            return false;

        const uint asyncMove = 0x4014; // SWP_ASYNCWINDOWPOS | SWP_NOZORDER | SWP_NOACTIVATE
        const uint moveOnly = 0x0001; // SWP_NOSIZE
        bool samePosition = current.Left == bounds.X && current.Top == bounds.Y;
        bool sameSize = current.Right - current.Left == bounds.Width && current.Bottom - current.Top == bounds.Height;
        return (samePosition && (!resize || sameSize)) || NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
            bounds.X, bounds.Y, bounds.Width, bounds.Height, resize && !sameSize ? asyncMove : asyncMove | moveOnly);
    }
}
