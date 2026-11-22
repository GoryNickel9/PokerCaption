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
    private const int MoveRetryMs = 1000;
    private volatile bool _enabled;

    private sealed class Placement
    {
        public int Home = -1;
        public int Active = -1;
        public bool OpenedActive;
        public Rectangle? Bounds;
        public long NextMove;
        public bool Detached;
        public Rectangle? DragOrigin;
        public Rectangle? SettledBounds;
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

    internal bool IsMoving(TableInfo table) => Enabled && _placements.TryGetValue(table, out var state) &&
        (state.DragOrigin.HasValue || (state.Bounds.HasValue && state.SettledBounds == null &&
            Environment.TickCount64 < state.NextMove));

    public void Load(IniFile ini)
    {
        bool wasLayoutOnly = LayoutOnly;
        ConfigName = ini.Get("PokerCaption", "TableManagerConfig", "4x");
        _enabled = ini.GetBool("PokerCaption", "TableManagerOn", false);
        Config = null;
        ClearGeometryPreview();
        foreach (var table in _placements.Keys) ClearSlot(table);
        _placements.Clear();
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
        foreach (var table in tables)
        {
            if (!_placements.TryGetValue(table, out var state))
                _placements.Add(table, state = new Placement { OpenedActive = true });
            if (_place == null && !table.Locked) TrackDrop(table, state);
        }

        var homeSlots = Config.InactiveSlots;
        var activeSlots = Config.ActiveSlots;
        var homes = new int[homeSlots.Count];
        var active = new int[activeSlots.Count];

        // Keep each table's home slot while it is active so it can return to it.
        foreach (var table in tables)
        {
            var state = _placements[table];
            if (state.Home >= 0) homes[state.Home]++;
            if (state.Active >= 0 && (table.Locked || state.DragOrigin.HasValue)) active[state.Active]++;
        }

        foreach (var table in tables)
        {
            var state = _placements[table];
            if (table.Locked || state.Detached || state.DragOrigin.HasValue) continue;
            if (LayoutOnly) table.InActiveSlot = false;
            if (state.Home < 0)
                state.Home = Claim(homeSlots, homes);
            if (LayoutOnly) continue;

            bool wantsActive = table.HeroAct ||
                state.OpenedActive ||
                (table.InActiveSlot && table.HeroInHand);
            if (table.HeroAct || table.HeroInHand || table.HeroFolded) state.OpenedActive = false;
            if (state.Active >= 0 && !wantsActive) state.Active = -1;
            if (state.Active >= 0) active[state.Active]++;
            table.InActiveSlot = wantsActive;
        }

        // Existing active assignments reserve their slots before another table can claim one.
        foreach (var table in tables)
        {
            var state = _placements[table];
            if (table.Locked || state.Detached || state.DragOrigin.HasValue || !table.InActiveSlot || state.Active >= 0) continue;
            state.Active = Claim(activeSlots, active);
        }

        var occupancy = new Dictionary<Rectangle, int>();
        foreach (var table in tables)
        {
            var state = _placements[table];
            if (!table.Locked && !state.Detached && !state.DragOrigin.HasValue)
            {
                table.InActiveSlot = state.Active >= 0;
                table.PlacedSlot = state.Active >= 0 ? state.Active : state.Home;
                if (table.PlacedSlot < 0) state.Bounds = null;
                else
                {
                    var bounds = SlotBounds(state.Active >= 0, table.PlacedSlot);
                    bool geometryChanged = state.Bounds != bounds;
                    if (now >= state.NextMove && geometryChanged)
                    {
                        bool placed = _place?.Invoke(table.Hwnd, bounds) ?? PlaceWindow(table.Hwnd, bounds);
                        if (placed)
                        {
                            state.Bounds = bounds;
                            state.SettledBounds = null;
                        }
                        state.NextMove = placed ? (_place == null ? now + 150 : now) : now + MoveRetryMs;
                    }
                }
            }
            if (state.Bounds is Rectangle rect)
                occupancy[rect] = occupancy.GetValueOrDefault(rect) + 1;
        }
        foreach (var table in tables)
        {
            var bounds = _placements[table].Bounds;
            table.TablesInSlot = bounds is Rectangle rect ? occupancy[rect] : 1;
        }
    }

    private Rectangle SlotBounds(bool active, int index)
    {
        if (!active && _geometryPreview.TryGetValue(index, out var preview)) return preview;
        var slot = (active ? Config.ActiveSlots : Config.InactiveSlots)[index];
        return new Rectangle(slot.Left, slot.Top, slot.Width, slot.EffectiveHeight);
    }

    private static int Claim(IReadOnlyList<TableManagerConfig.Slot> slots, int[] occupied)
    {
        for (int i = 0; i < slots.Count; i++)
            if (occupied[i] == 0) { occupied[i]++; return i; }
        for (int i = 0; i < slots.Count; i++)
            if (slots[i].Multislot) { occupied[i]++; return i; }
        return -1;
    }

    private void TrackDrop(TableInfo table, Placement state)
    {
        if (NativeMethods.IsIconic(table.Hwnd) || NativeMethods.IsZoomed(table.Hwnd) ||
            !NativeMethods.GetWindowRect(table.Hwnd, out var rect)) return;
        var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (NativeMethods.IsInMoveSize(table.Hwnd))
        {
            state.DragOrigin ??= state.SettledBounds ?? state.Bounds ?? bounds;
            return;
        }
        var origin = state.DragOrigin ?? state.SettledBounds;
        state.DragOrigin = null;
        if (origin is Rectangle before && bounds.Location != before.Location)
        {
            DropTable(table, bounds, before);
            return;
        }
        if (state.Bounds == null || state.Bounds.Value.Location == bounds.Location)
            state.SettledBounds = bounds;
    }

    internal void DropTable(TableInfo table, Rectangle bounds, Rectangle origin)
    {
        var state = _placements[table];
        bool active = state.Active >= 0;
        int source = active ? state.Active : state.Home;
        var slots = active ? Config.ActiveSlots : Config.InactiveSlots;
        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        int target = Enumerable.Range(0, slots.Count)
            .Where(i => SlotBounds(active, i).Contains(center))
            .OrderByDescending(i =>
            {
                var overlap = Rectangle.Intersect(bounds, SlotBounds(active, i));
                return (long)overlap.Width * overlap.Height;
            }).FirstOrDefault(-1);
        if (target < 0)
        {
            state.Home = state.Active = -1;
            state.Detached = true;
            state.Bounds = null;
            state.SettledBounds = bounds;
            ClearSlot(table);
            return;
        }

        var occupant = _placements.FirstOrDefault(pair => pair.Key != table &&
            (active ? pair.Value.Active == target : pair.Value.Home == target));
        if (target != source && occupant.Key != null)
        {
            if (occupant.Key.Locked || occupant.Value.DragOrigin.HasValue) return;
            if (active) occupant.Value.Active = source;
            else occupant.Value.Home = source;
            occupant.Value.Bounds = null;
            occupant.Value.SettledBounds = null;
            if (source < 0)
            {
                occupant.Value.Detached = true;
                ClearSlot(occupant.Key);
                if (_place != null) _place(occupant.Key.Hwnd, origin);
                else PlaceWindow(occupant.Key.Hwnd, origin);
            }
            occupant.Value.NextMove = 0;
        }
        if (active) state.Active = target;
        else state.Home = target;
        state.Detached = false;
        state.Bounds = null;
        state.SettledBounds = null;
        state.NextMove = 0;
    }

    private static void ClearSlot(TableInfo table)
    {
        table.InActiveSlot = false;
        table.PlacedSlot = -1;
        table.TablesInSlot = 1;
    }

    private static bool PlaceWindow(IntPtr hwnd, Rectangle bounds)
    {
        if (NativeMethods.IsIconic(hwnd) || NativeMethods.IsZoomed(hwnd) ||
            NativeMethods.IsHungAppWindow(hwnd) || NativeMethods.IsInMoveSize(hwnd) ||
            !NativeMethods.GetWindowRect(hwnd, out var current))
            return false;

        const uint asyncMove = 0x4014; // SWP_ASYNCWINDOWPOS | SWP_NOZORDER | SWP_NOACTIVATE
        const uint moveOnly = 0x0001; // SWP_NOSIZE
        bool samePosition = current.Left == bounds.X && current.Top == bounds.Y;
        bool sameSize = current.Right - current.Left == bounds.Width && current.Bottom - current.Top == bounds.Height;
        return (samePosition && sameSize) || NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
            bounds.X, bounds.Y, bounds.Width, bounds.Height, sameSize ? asyncMove | moveOnly : asyncMove);
    }
}
