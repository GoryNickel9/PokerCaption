using System.Drawing;
using PokerCaption.Config;
using PokerCaption.Native;

namespace PokerCaption.Engine;

public sealed class TableManagerEngine
{
    private readonly Action<string> _log;
    private readonly Func<IntPtr, Rectangle, bool> _place;
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
    internal bool PreserveWindowSize { get; set; }
    internal bool LayoutOnly => Enabled && PreserveWindowSize;
    internal event Action LayoutModeChanged;

    public TableManagerEngine(Action<string> log) : this(log, null) { }

    internal TableManagerEngine(Action<string> log, Func<IntPtr, Rectangle, bool> place)
    {
        _log = log ?? (_ => { });
        _place = place ?? PlaceWindow;
    }

    public void Load(IniFile ini)
    {
        bool wasLayoutOnly = LayoutOnly;
        ConfigName = ini.Get("PokerCaption", "TableManagerConfig", "4x");
        _enabled = ini.GetBool("PokerCaption", "TableManagerOn", false);
        Config = null;
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

        var candidates = new List<TableInfo>(tables.Count);
        foreach (var table in tables)
        {
            if (!_placements.TryGetValue(table, out var state))
            {
                state = new Placement { OpenedActive = Config.ActivateOpened, LastBlind = table.Bb };
                _placements.Add(table, state);
                sortInputsChanged = true;
            }
            else if (!state.LastBlind.Equals(table.Bb))
            {
                state.LastBlind = table.Bb;
                sortInputsChanged = true;
            }
            if (LayoutOnly || table.Locked || !Config.WithHeroOnly || table.HeroAct || table.HeroInHand || table.HandStamp > 0)
                candidates.Add(table);
            else
            {
                state.Home = state.Active = -1;
                state.Bounds = null;
                ClearSlot(table);
            }
        }

        if (Config.AutoSortTables && sortInputsChanged)
            _nextSort = now + Config.AutoSortTime;
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
            if (Config.DeactivateAtFold && table.HeroFolded && !table.HeroAct) wantsActive = false;
            if (table.HeroAct || table.HeroInHand || table.HeroFolded) state.OpenedActive = false;
            if (state.Active >= 0 && (!wantsActive || sort)) state.Active = -1;
            if (state.Active >= 0) active[state.Active]++;
            table.InActiveSlot = wantsActive;
        }

        // Existing active assignments reserve their slots before another table can claim one.
        foreach (var table in candidates)
        {
            var state = _placements[table];
            if (table.Locked || !table.InActiveSlot || state.Active >= 0 || Config.FreeDesktop) continue;
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
                bool free = table.InActiveSlot && Config.FreeDesktop;
                table.InActiveSlot = state.Active >= 0 || free;
                table.PlacedSlot = free ? -1 : state.Active >= 0 ? state.Active : state.Home;
                var slots = state.Active >= 0 ? activeSlots : homeSlots;
                if (table.PlacedSlot < 0) state.Bounds = null;
                else
                {
                    var slot = slots[table.PlacedSlot];
                    var bounds = new Rectangle(slot.Left, slot.Top, slot.Width, slot.EffectiveHeight);
                    if ((sort || state.Bounds != bounds) && now >= state.NextMove)
                    {
                        bool placed = _place(table.Hwnd, bounds);
                        if (placed) state.Bounds = bounds;
                        if (PreserveWindowSize || !placed) state.NextMove = now + MoveRetryMs;
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

    private bool PlaceWindow(IntPtr hwnd, Rectangle bounds)
    {
        if (NativeMethods.IsIconic(hwnd) || NativeMethods.IsZoomed(hwnd) ||
            NativeMethods.IsHungAppWindow(hwnd) || !NativeMethods.GetWindowRect(hwnd, out var current))
            return false;

        const uint asyncMove = 0x4014; // SWP_ASYNCWINDOWPOS | SWP_NOZORDER | SWP_NOACTIVATE
        const uint moveOnly = 0x0001; // SWP_NOSIZE
        if (PreserveWindowSize)
        {
            if (current.Left == bounds.X && current.Top == bounds.Y) return true;
            bool queued = NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, 0, 0, asyncMove | moveOnly);
            if (queued) _log($"TM: richiesto spostamento at {bounds.X},{bounds.Y} (dimensioni invariate)");
            return queued;
        }

        return Rectangle.FromLTRB(current.Left, current.Top, current.Right, current.Bottom) == bounds ||
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, asyncMove);
    }
}
