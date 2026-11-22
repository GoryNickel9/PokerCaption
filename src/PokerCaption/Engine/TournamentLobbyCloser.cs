using System.Text.RegularExpressions;
using PokerCaption.Native;

namespace PokerCaption.Engine;

internal sealed class TournamentLobbyCloser
{
    private static readonly Regex TableTitle = new(
        @"(?:\ATable '(?<id>[0-9]+) [0-9]+'|[-–]\s*Tournament (?<id>[0-9]+) Table [0-9]+\b)",
        RegexOptions.CultureInvariant);
    private static readonly Regex LobbyTitle = new(@"\ATournament (?<id>[0-9]+) Lobby\b", RegexOptions.CultureInvariant);

    private readonly Dictionary<IntPtr, (uint Process, string Id)> _tables = new();
    private Dictionary<(uint Process, string Id), IntPtr> _lobbies = new();
    private readonly Action<string> _log;

    internal bool Enabled { get; private set; }

    internal TournamentLobbyCloser(Action<string> log) => _log = log;

    internal void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        Reset();
    }

    internal void Reset()
    {
        _tables.Clear();
        _lobbies.Clear();
    }

    internal void Observe(IReadOnlyList<TableInfo> tables)
    {
        if (!Enabled) return;

        var closed = new HashSet<(uint Process, string Id)>();
        foreach (var (handle, key) in _tables.ToArray())
        {
            if (NativeMethods.GetWindowThreadProcessId(handle, out uint process) != 0 && process == key.Process)
                continue;
            _tables.Remove(handle);
            closed.Add(key);
        }

        foreach (var table in tables)
        {
            var match = table.IsTourney && table.Title != null ? TableTitle.Match(table.Title) : Match.Empty;
            if (!match.Success || NativeMethods.GetWindowThreadProcessId(table.Hwnd, out uint process) == 0)
                _tables.Remove(table.Hwnd);
            else
                _tables[table.Hwnd] = (process, match.Groups["id"].Value);
        }

        var open = _tables.Values.ToHashSet();
        var currentLobbies = FindLobbies(open.Concat(closed).ToHashSet());
        foreach (var key in closed)
        {
            if (open.Contains(key) || !_lobbies.TryGetValue(key, out var lobby) || lobby == IntPtr.Zero ||
                !currentLobbies.TryGetValue(key, out var current) || current != lobby ||
                !NativeMethods.IsWindowVisible(lobby) || NativeMethods.GetWindowThreadProcessId(lobby, out uint process) == 0 ||
                process != key.Process) continue;

            if (NativeMethods.PostMessageW(lobby, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                _log?.Invoke("tournament lobby: requested close for " + key.Id);
        }
        _lobbies = currentLobbies;
    }

    private static Dictionary<(uint Process, string Id), IntPtr> FindLobbies(HashSet<(uint Process, string Id)> relevant)
    {
        var result = new Dictionary<(uint Process, string Id), IntPtr>();
        if (relevant.Count == 0) return result;
        var processes = relevant.Select(key => key.Process).ToHashSet();
        NativeMethods.EnumWindows((window, _) =>
        {
            if (NativeMethods.GetWindowThreadProcessId(window, out uint process) == 0 || !processes.Contains(process)) return true;
            string title = PokerWindows.GetWindowText(window);
            if (string.IsNullOrEmpty(title)) return true;
            var match = LobbyTitle.Match(title);
            if (!match.Success) return true;
            var key = (process, match.Groups["id"].Value);
            if (relevant.Contains(key) && !result.TryAdd(key, window)) result[key] = IntPtr.Zero;
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
