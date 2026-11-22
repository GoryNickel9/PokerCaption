using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using PokerCaption.Config;
using SeatActionEvent = PokerCaption.Native.SharedMemory.SeatActionEvent;

namespace PokerCaption.Engine;

internal sealed class SeatActionHistory
{
    private readonly Dictionary<int, List<(int street, string key, string symbol, uint? duration)>> _history = new();
    private readonly Dictionary<int, string> _lastVisible = new();
    private readonly Dictionary<int, Rectangle> _anchors = new();
    private readonly List<SeatActionEvent> _pendingPreflop = new();
    private uint? _timingTick;
    private int _timingStreet = -1, _timingSequence;
    private int _street = -1, _hand;
    private double _lastPot;
    private bool _emptyPot, _hasPreflopLimp;
    private static readonly (string key, string symbol)[] ActionSymbols =
    {
        ("", ""), ("Fold", "F"), ("Check", "X"), ("Call", "C"), ("Bet", "B"),
        ("Raise", "R"), ("Limp", "OL"), ("AllIn", "AI"), ("Postdead", "P")
    };

    private static int Street(int cards) => cards switch { 0 => 0, 3 => 1, 4 => 2, 5 => 3, _ => -1 };

    private void ResetHand()
    {
        _history.Clear();
        _lastVisible.Clear();
        _anchors.Clear();
        _hasPreflopLimp = false;
    }

    private void Append(int seat, int street, int code, uint? tick = null, int sequence = 0)
    {
        if (street != _timingStreet || (sequence > 0 && _timingSequence > 0 && sequence != _timingSequence + 1))
            _timingTick = null;
        if (sequence > 0) _timingSequence = sequence;
        _timingStreet = street;
        if (seat < 1 || seat > 10 || code <= 0 || code >= ActionSymbols.Length) return;
        if (!_history.TryGetValue(seat, out var actions)) _history[seat] = actions = new();
        var (key, symbol) = ActionSymbols[code];
        if (code == 1 && actions.Any(action => action.key == key)) return;
        if (code is 2 or 4 && actions.Any(action => action.street == street && action.key == key)) return;
        if (code == 6 && street == 0)
        {
            if (actions.Any(action => action.street == 0 && action.key is "1stimp" or "Limp")) return;
            if (!_hasPreflopLimp) (key, symbol) = ("1stimp", "L");
            _hasPreflopLimp = true;
        }
        uint? duration = null;
        if (tick is uint current)
        {
            if (code == 8) duration = 0;
            else if (_timingTick is uint previous)
            {
                uint elapsed = unchecked(current - previous);
                // Reject a timestamp older than the snapshot anchor while allowing clock rollover.
                if (elapsed <= int.MaxValue) duration = elapsed;
            }
        }
        _timingTick = tick;
        actions.Add((street, key, symbol, duration));
    }

    public int Observe(int hand, int cards, double pot, IReadOnlyDictionary<int, string> visible,
        IReadOnlyDictionary<int, Rectangle> anchors = null,
        IReadOnlyList<SeatActionEvent> events = null, uint? snapshotTick = null)
    {
        int street = Street(cards);
        bool newHand = street == 0 && pot > 0 &&
            (_emptyPot || ((_street > 0 || hand != _hand) && pot < _lastPot));
        if (events == null || (street == 0 && (pot == 0 || hand != _hand))) _timingTick = null;
        if (events != null)
        {
            foreach (var action in events)
            {
                int eventStreet = Street(action.Cards);
                if (eventStreet == 0 && (newHand || _street > 0 || _emptyPot))
                {
                    _pendingPreflop.Add(action);
                    continue;
                }
                if (eventStreet < 0 || eventStreet < _street) continue;
                Append(action.Seat, eventStreet, action.Code, action.Tick, action.Sequence);
                _street = eventStreet;
            }
        }

        if (street == 0 && pot == 0)
        {
            _emptyPot = true;
            return -1;
        }
        if (newHand)
        {
            ResetHand();
            _timingStreet = 0;
            _timingTick = _pendingPreflop.Count == 0 ? snapshotTick : null;
            foreach (var action in _pendingPreflop)
                Append(action.Seat, 0, action.Code, action.Tick, action.Sequence);
            _pendingPreflop.Clear();
        }
        else if (street < 0 || street < _street) return -1;
        else if (street > 0)
        {
            // Reconnecting can temporarily blank the table in the middle of a street.
            _pendingPreflop.Clear();
            if (_street >= 0 && street > _street)
            {
                _timingStreet = street;
                _timingTick = snapshotTick;
            }
        }
        if (street == 0) _hand = hand;
        _emptyPot = false;
        _lastPot = pot;
        _street = street;
        if (events == null)
        {
            foreach (var (seat, label) in visible)
            {
                if (_lastVisible.TryGetValue(seat, out var prior) && prior == label) continue;
                int code = label.Split(' ', 2)[0].ToLowerInvariant() switch
                {
                    "fold" => 1, "check" => 2, "call" => 3, "bet" => 4, "raise" => 5,
                    "limp" => 6, "all-in" or "all" => 7, "post" => 8, _ => 0
                };
                Append(seat, street, code);
            }
            _lastVisible.Clear();
            foreach (var (seat, label) in visible) _lastVisible[seat] = label;
        }
        if (anchors != null)
            foreach (var (seat, bounds) in anchors)
                if (_history.ContainsKey(seat)) _anchors[seat] = bounds;
        return 1 << street;
    }

    public IEnumerable<KeyValuePair<int, Rectangle>> MissingAnchors(IReadOnlyDictionary<int, Rectangle> current) =>
        _anchors.Where(anchor => !current.ContainsKey(anchor.Key));

    private static string Seconds(uint milliseconds) =>
        (milliseconds / 1000d).ToString("0.#", CultureInfo.InvariantCulture);

    public string Format(int seat, IniFile ini)
    {
        if (!_history.TryGetValue(seat, out var actions)) return "";
        string divider = ini?.Get("HUD", "actionsSymStreetdivider") ?? "|";
        bool showTimings = ini?.GetBool("HUD", "actionsAddTimings", false) == true;
        var streets = actions.GroupBy(action => action.street)
            .Select(street => string.Concat(street.Select(action =>
            {
                string symbol = ini?.Get("HUD", "actionsSym" + action.key) ?? action.symbol;
                return showTimings && symbol.Length > 0 && action.duration is uint duration
                    ? $"{symbol} {Seconds(duration)}s" : symbol;
            })));
        return string.Join(divider, streets.Where(street => street.Length > 0));
    }
}
