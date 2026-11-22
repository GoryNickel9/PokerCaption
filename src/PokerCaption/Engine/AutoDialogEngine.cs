using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using PokerCaption.Config;
using PokerCaption.Native;

namespace PokerCaption.Engine
{
    /// <summary>Gestisce le azioni sui dialoghi e le chiusure dei tavoli dei client riconosciuti.</summary>
    public sealed partial class AutoDialogEngine
    {
        [Flags]
        private enum TableCloseReason { None = 0, Closed = 1, Concluded = 2, TournamentEnded = 4 }

        private sealed record DialogAction(string Caption, int DelayMs, TableCloseReason TableReason = TableCloseReason.None);
        private sealed record TableCloseRequest(uint ProcessId, TableCloseReason Reasons, long? LastPostTick);
        private sealed record PendingClose(IntPtr Window, uint Owner, string Title, string WindowClass, bool IsDialog, DialogAction Action, long DueTick)
        {
            public string Body { get; init; }
            public IntPtr Button { get; init; }
        }

        private static readonly DialogAction TableClosed = new("This table has been closed", 0, TableCloseReason.Closed);
        private static readonly DialogAction TableConcluded = new("Play at this table has concluded", 0, TableCloseReason.Concluded);
        private static readonly DialogAction TournamentEnded = new("Tournament is over", 0, TableCloseReason.TournamentEnded);
        private const string ReminderCaption = "Reminder: Your current sesson has been active for X hours";
        private const string PokerStarsDialogButtonClass = "PokerStarsButtonClass";
        private const string FlutterDialogButtonClass = "FlutterOnlinePokerButtonClass";
        private const string NativeDialogClass = "#32770";
        private const uint WmGetText = 0x000D;
        private const uint WmMouseMove = 0x0200, WmLeftButtonDown = 0x0201, WmLeftButtonUp = 0x0202;
        private static readonly IntPtr ClickPoint = (IntPtr)0x000A000A;
        private const uint BlockIncomingSends = 0x0001, AbortIfHung = 0x0002, ErrorOnExit = 0x0020;
        private const uint OwnerWindow = 4;
        private const uint TextTimeoutMs = 40;
        private const int TextCapacity = 2048;
        private const int MaxDialogLabels = 16;
        private const int RenderedDialogTextLength = 199;
        private const int PollIntervalMs = 100;
        private const int ScanIntervalMs = 500;
        private const int TableCloseRetryMs = 3000;
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);
        private const string LogoutCaption = "PokerStars Logout Information";

        // Old [AutoDialog] rows otherwise fall back to generic title matching.
        private static readonly HashSet<string> RemovedOptionCaptions = new(StringComparer.OrdinalIgnoreCase)
        {
            "Find a Player", "Table Cap Reached", "This seat is reserved for players on the waiting list", "This seat is occupied",
            "Player cannot be found",
            "Find Seat (Cash Games)", "Seat Me / All seats have been taken", "Seat Me",
            "My News", "Stars Account Information", "Chest Announcement", "Active Session Reminder",
            "Feedback for PokerStars", "Responsible Gaming", "Confirm Waiting List", "Auto close table in waiting list",
            "Your auto-rebuy was restricted to", "You did not act in time, this seat is no longer reserved for you",
            "Tournament Registration / Show Lobby", "Tournament Registration / Welcome to the tournament",
        };
        private static readonly (string Key, string Title, string Button)[] ButtonOptions =
        {
            ("CurSessInfo", "Current Session Information", "Close"),
            ("KeepPlaying", "Current Session", "Keep Playing"),
            ("InactivityTO", "Inactivity Timeout", "Stay connected"),
        };

        internal static bool IsRemovedOptionCaption(string caption) =>
            RemovedOptionCaptions.Contains(Normalize(caption).TrimEnd('.', '!', '?'));

        private readonly List<DialogAction> _rules = new();
        private readonly Dictionary<string, string> _dialogButtons = new(StringComparer.Ordinal);
        private readonly List<PendingClose> _pending = new();
        private HashSet<IntPtr> _closureTables = new();
        private readonly Dictionary<IntPtr, TableCloseRequest> _tableClosures = new();
        private readonly Dictionary<string, string> _optionCaptions = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _state = new();
        private readonly Action<string> _log;
        private readonly Func<IntPtr, IntPtr> _lastActivePopup;
        private readonly Func<IntPtr, string> _windowClass;
        public bool CloseTableHasBeenClosed, CloseTableHasConcluded, CloseTournamentIsOver;

        private DateTime _lastScan = DateTime.MinValue;
        private DateTime _lastPidRefresh = DateTime.MinValue;
        private HashSet<uint> _clientPids = new();
        private System.Windows.Forms.Timer _timer;
        private Task _poll;
        private int _runRevision, _configRevision;

        public AutoDialogEngine(Action<string> log) : this(log, GetLastActivePopup, TableScanner.GetClass) { }

        private AutoDialogEngine(Action<string> log, Func<IntPtr, IntPtr> lastActivePopup, Func<IntPtr, string> windowClass)
        {
            _log = log ?? (_ => { });
            _lastActivePopup = lastActivePopup;
            _windowClass = windowClass;
        }

        internal void Start()
        {
            Stop();
            _timer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
            _timer.Tick += (_, _) =>
            {
                if (_poll is { IsCompleted: false }) return;
                int run = _runRevision;
                _poll = Task.Run(() =>
                {
                    try { Poll(run); }
                    catch (Exception ex) { _log("auto-dialog: " + ex.Message); }
                });
            };
            _timer.Start();
        }

        internal void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            lock (_state)
            {
                _runRevision++;
                _pending.Clear();
                _tableClosures.Clear();
            }
        }

        internal void SetClosureTables(IEnumerable<IntPtr> handles)
        {
            lock (_state)
            {
                _closureTables = handles.ToHashSet();
                foreach (var table in _tableClosures.Keys.Where(handle => !_closureTables.Contains(handle)).ToArray())
                    _tableClosures.Remove(table);
            }
        }

        internal void SetOptionCaptions(IEnumerable<string> captions)
        {
            lock (_state)
            {
                _configRevision++;
                _optionCaptions.Clear();
                foreach (string caption in captions) _optionCaptions[caption] = caption;
            }
        }

        public void Load(IniFile ini)
        {
            lock (_state)
            {
                _configRevision++;
                LoadRules(ini);
            }
        }

        private void LoadRules(IniFile ini)
        {
            _rules.Clear();
            for (int i = 0; i <= 64; i++)
            {
                var caption = ini.Get("AutoDialog", "Caption" + i);
                if (caption == null) break;
                if (string.IsNullOrWhiteSpace(caption) || IsRemovedOptionCaption(caption)) continue;
                int delay = Math.Max(0, ini.GetInt("AutoDialog", "Delay" + i, 0));
                _rules.Add(new DialogAction(caption.Trim(), delay));
            }
            CloseTableHasBeenClosed = ini.GetBool("PokerCaption", "CloseTableHasBeenClosed", false);
            CloseTableHasConcluded = ini.GetBool("PokerCaption", "CloseTableHasConcluded", false);
            CloseTournamentIsOver = ini.GetBool("PokerCaption", "CloseTournamentIsOver", false);
            _dialogButtons.Clear();
            foreach (var (key, title, button) in ButtonOptions)
                if (ini.GetBool("PokerCaption", key, false)) _dialogButtons.Add(title, button);
            _pending.RemoveAll(entry => entry.Button != IntPtr.Zero
                ? !_dialogButtons.ContainsKey(entry.Title)
                : entry.Action.TableReason == TableCloseReason.None && (entry.Body == null
                ? MatchDialog(entry.Title, entry.WindowClass, entry.IsDialog)
                : MatchBody(entry.Title, entry.Body, entry.WindowClass, entry.IsDialog,
                    combinedBody: UsesCombinedBody(entry.Action.Caption))) != entry.Action);
        }

        private DialogAction MatchDialog(string title, string windowClass, bool isDialog)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;

            return _rules.FirstOrDefault(rule => MatchesCaption(title, rule.Caption, windowClass, isDialog));
        }

        private bool MatchesCaption(string title, string caption, string windowClass, bool isDialog)
        {
            if (!_optionCaptions.TryGetValue(caption, out var optionCaption))
                return title.Contains(caption, StringComparison.OrdinalIgnoreCase);
            if (optionCaption == ReminderCaption) return false;
            if (optionCaption == LogoutCaption)
                return windowClass == NativeDialogClass && title.Equals(LogoutCaption, StringComparison.Ordinal);
            return isDialog && MatchesOptionText(Normalize(title), optionCaption);
        }

        private static bool MatchesOptionText(string text, string optionCaption) =>
            SameTitle(text, optionCaption) ||
            (optionCaption == "You have been unregistered from" && StartsWithPhrase(text, optionCaption));

        private DialogAction MatchBody(string title, string body, string windowClass, bool isDialog,
            bool tableMessage = false, bool combinedBody = false)
        {
            if (!isDialog || string.IsNullOrWhiteSpace(body)) return null;
            string text = Normalize(body);
            if (tableMessage)
            {
                if (body == TableClosed.Caption) return TableClosed;
                if (body.StartsWith(TableConcluded.Caption, StringComparison.Ordinal)) return TableConcluded;
                if (body.StartsWith(TournamentEnded.Caption, StringComparison.Ordinal)) return TournamentEnded;
            }

            return _rules.FirstOrDefault(rule =>
                _optionCaptions.TryGetValue(rule.Caption, out var caption) &&
                caption != LogoutCaption && UsesCombinedBody(caption) == combinedBody &&
                MatchesDialogBody(title, body, text, windowClass, caption));
        }

        private static bool UsesCombinedBody(string caption) => caption == ReminderCaption;

        private static bool MatchesDialogBody(string title, string body, string normalized, string windowClass, string caption) => caption switch
        {
            ReminderCaption => windowClass == NativeDialogClass && title.Length == 0 &&
                (body.StartsWith("Reminder:Yourcurrentsession", StringComparison.Ordinal) ||
                 body.StartsWith("Reminder: Your current session", StringComparison.Ordinal)),
            _ => MatchesOptionText(normalized, caption),
        };

        private static bool StartsWithPhrase(string text, string phrase) =>
            text.StartsWith(phrase, StringComparison.OrdinalIgnoreCase) &&
            (text.Length == phrase.Length || " .!?:,(".Contains(text[phrase.Length]));

        private static string Normalize(string text) => Whitespace.Replace(text.Trim(), " ");

        private static bool SameTitle(string title, string expected) =>
            string.Equals(title.Trim().TrimEnd('.', '!', '?'), expected.TrimEnd('.', '!', '?'), StringComparison.OrdinalIgnoreCase);

        private PendingClose ReadDialog(IntPtr window, uint owner, long tickCount)
        {
            string title = WindowTitle(window);
            string windowClass = _windowClass(window);
            bool isDialog = IsDialogWindow(window, owner, windowClass);
            string buttonText = null;
            bool tableMessage;
            bool collectCombinedBody;
            lock (_state)
            {
                tableMessage = isDialog && _closureTables.Contains(GetWindow(window, OwnerWindow));
                var action = MatchDialog(title, windowClass, isDialog);
                if (action != null)
                    return new PendingClose(window, owner, title, windowClass, isDialog, action, tickCount + action.DelayMs);
                if (windowClass == NativeDialogClass) _dialogButtons.TryGetValue(title, out buttonText);
                if (!isDialog || (_rules.Count == 0 && !tableMessage && buttonText == null)) return null;
                collectCombinedBody = windowClass == NativeDialogClass && (title.Length == 0 || title == "Find a Player") &&
                    _rules.Any(rule => UsesCombinedBody(rule.Caption));
            }

            if (buttonText != null)
            {
                var button = FindDialogButton(window, owner, buttonText);
                return button == IntPtr.Zero ? null : new PendingClose(window, owner, title, windowClass, isDialog,
                    new DialogAction(title, 0), tickCount) { Button = button };
            }

            // This rule also checks the text of the dialog's child controls.
            PendingClose match = null;
            int labels = 0;
            var combinedBody = collectCombinedBody ? new StringBuilder(RenderedDialogTextLength) : null;
            bool completeEnumeration = true;
            NativeMethods.EnumChildWindows(window, (child, _) =>
            {
                string childClass = TableScanner.GetClass(child);
                if (childClass != "Static" || !NativeMethods.IsWindowVisible(child)) return true;
                if (++labels > MaxDialogLabels || !TryReadControlText(child, out var text))
                {
                    match = null;
                    completeEnumeration = false;
                    return false;
                }
                if (combinedBody != null)
                    combinedBody.Append(text, 0, Math.Min(text.Length, RenderedDialogTextLength - combinedBody.Length));
                DialogAction candidate;
                lock (_state) candidate = MatchBody(title, text, windowClass, isDialog, tableMessage);
                if (candidate == null) return true;
                if (match != null && match.Action != candidate)
                {
                    match = null;
                    completeEnumeration = false;
                    return false;
                }
                match ??= new PendingClose(window, owner, title, windowClass, isDialog, candidate, tickCount + candidate.DelayMs)
                    { Body = text };
                return true;
            }, IntPtr.Zero);
            if (match == null && completeEnumeration && combinedBody is { Length: > 0 })
            {
                string body = combinedBody.ToString();
                DialogAction candidate;
                lock (_state) candidate = MatchBody(title, body, windowClass, isDialog, combinedBody: true);
                if (candidate != null)
                    match = new PendingClose(window, owner, title, windowClass, isDialog, candidate, tickCount + candidate.DelayMs)
                    { Body = body };
            }
            return match;
        }

        private static IntPtr FindDialogButton(IntPtr window, uint owner, string text)
        {
            var match = IntPtr.Zero;
            foreach (var buttonClass in new[] { PokerStarsDialogButtonClass, FlutterDialogButtonClass })
            {
                for (var button = FindWindowExW(window, IntPtr.Zero, buttonClass, null); button != IntPtr.Zero;
                    button = FindWindowExW(window, button, buttonClass, null))
                {
                    if (!HasProcess(button, owner) || !TryReadControlText(button, out var caption)) return IntPtr.Zero;
                    if (caption != text) continue;
                    if (match != IntPtr.Zero) return IntPtr.Zero;
                    match = button;
                }
            }
            return match != IntPtr.Zero && NativeMethods.IsWindowVisible(match) && IsWindowEnabled(match) ? match : IntPtr.Zero;
        }

        private static bool IsDialogWindow(IntPtr window, uint processId, string windowClass)
        {
            if (windowClass == NativeDialogClass) return true;
            var owner = GetWindow(window, OwnerWindow);
            return owner != IntPtr.Zero && !IsWindowEnabled(owner) &&
                NativeMethods.GetWindowThreadProcessId(owner, out uint ownerPid) != 0 && ownerPid == processId;
        }

        public void OnTick() => Poll(_runRevision);

        private void Poll(int run)
        {
            int config;
            lock (_state)
            {
                if (run != _runRevision) return;
                config = _configRevision;
            }
            var now = DateTime.UtcNow;
            long tickCount = Environment.TickCount64;
            if ((now - _lastPidRefresh).TotalSeconds > 5)
            {
                _lastPidRefresh = now;
                _clientPids = new HashSet<uint>(ClientLocator.FindClients().Select(c => c.ProcessId));
            }

            if ((now - _lastScan).TotalMilliseconds >= ScanIntervalMs)
            {
                _lastScan = now;
                NativeMethods.EnumWindows((window, _) => ScanWindow(window, tickCount, run, config), IntPtr.Zero);
            }

            FlushPending(Environment.TickCount64, run, config);
        }

        private bool ScanWindow(IntPtr window, long tickCount, int run, int config)
        {
            if (!NativeMethods.IsWindowVisible(window)) return true;
            NativeMethods.GetWindowThreadProcessId(window, out var owner);
            if (!_clientPids.Contains(owner)) return true;
            lock (_state)
            {
                if (run != _runRevision || config != _configRevision) return false;
                if (_pending.Exists(p => p.Window == window)) return true;
            }

            var entry = ReadDialog(window, owner, tickCount);
            lock (_state)
            {
                if (run != _runRevision || config != _configRevision) return false;
                if (entry != null) _pending.Add(entry);
            }
            return true;
        }

        private void FlushPending(long tickCount, int run, int config)
        {
            PendingClose[] due;
            lock (_state)
            {
                if (run != _runRevision || config != _configRevision) return;
                due = _pending.Where(entry => tickCount >= entry.DueTick).Reverse().ToArray();
            }
            foreach (var entry in due)
            {
                PendingClose current = null;
                if (NativeMethods.IsWindowVisible(entry.Window) &&
                    NativeMethods.GetWindowThreadProcessId(entry.Window, out var owner) != 0 &&
                    owner == entry.Owner && _clientPids.Contains(owner))
                    current = ReadDialog(entry.Window, owner, tickCount);

                bool matches = current != null && current.Title == entry.Title && current.WindowClass == entry.WindowClass && current.Body == entry.Body &&
                    current.Action == entry.Action && current.Button == entry.Button;
                bool sent;
                lock (_state)
                {
                    if (run != _runRevision || config != _configRevision) return;
                    if (!_pending.Remove(entry) || !matches) continue;
                    if (entry.Action.TableReason != TableCloseReason.None)
                    {
                        RememberTableClosure(entry);
                        continue;
                    }
                    sent = entry.Button != IntPtr.Zero ? ClickDialogButton(entry) :
                        NativeMethods.PostMessageW(entry.Window, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
                if (sent)
                    _log("auto-dialog: " + (entry.Button == IntPtr.Zero ? "richiesta chiusura" : "click") + " '" + entry.Action.Caption + "'");
            }
            FlushTableClosures(tickCount, run, config);
        }

        private static bool ClickDialogButton(PendingClose entry)
        {
            if (entry.Title == "Inactivity Timeout")
                return NativeMethods.PostMessageW(entry.Button, WmMouseMove, IntPtr.Zero, ClickPoint) &&
                    NativeMethods.PostMessageW(entry.Button, WmLeftButtonDown, (IntPtr)1, ClickPoint) &&
                    NativeMethods.PostMessageW(entry.Button, WmLeftButtonUp, IntPtr.Zero, ClickPoint);

            bool down = SendMessageTimeoutW(entry.Button, WmLeftButtonDown, (IntPtr)1, ClickPoint,
                BlockIncomingSends | AbortIfHung | ErrorOnExit, TextTimeoutMs, out _) != IntPtr.Zero;
            bool up = SendMessageTimeoutW(entry.Button, WmLeftButtonUp, IntPtr.Zero, ClickPoint,
                BlockIncomingSends | AbortIfHung | ErrorOnExit, TextTimeoutMs, out _) != IntPtr.Zero;
            return down && up;
        }

        private void RememberTableClosure(PendingClose entry)
        {
            var table = GetWindow(entry.Window, OwnerWindow);
            if (!_closureTables.Contains(table) || !HasProcess(table, entry.Owner)) return;
            if (_tableClosures.TryGetValue(table, out var previous) && previous.ProcessId == entry.Owner)
                _tableClosures[table] = previous with { Reasons = previous.Reasons | entry.Action.TableReason };
            else
                _tableClosures[table] = new TableCloseRequest(entry.Owner, entry.Action.TableReason, null);
        }

        private void FlushTableClosures(long tickCount, int run, int config)
        {
            lock (_state)
            {
                if (run != _runRevision || config != _configRevision) return;
                var enabled = (CloseTableHasBeenClosed ? TableCloseReason.Closed : TableCloseReason.None) |
                    (CloseTableHasConcluded ? TableCloseReason.Concluded : TableCloseReason.None) |
                    (CloseTournamentIsOver ? TableCloseReason.TournamentEnded : TableCloseReason.None);
                foreach (var (table, request) in _tableClosures.ToArray())
                {
                    if (!HasProcess(table, request.ProcessId))
                    {
                        _tableClosures.Remove(table);
                        continue;
                    }
                    if ((request.Reasons & enabled) == 0 ||
                        (request.LastPostTick is long last && tickCount - last < TableCloseRetryMs)) continue;
                    var popup = _lastActivePopup(table);
                    if (popup != table && HasProcess(popup, request.ProcessId)) continue;
                    if (NativeMethods.PostMessageW(table, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                        _tableClosures[table] = request with { LastPostTick = tickCount };
                }
            }
        }

        private static bool HasProcess(IntPtr window, uint processId) => window != IntPtr.Zero &&
            NativeMethods.GetWindowThreadProcessId(window, out uint actual) != 0 && actual == processId;

        private static bool TryReadControlText(IntPtr window, out string text)
        {
            var buffer = new StringBuilder(TextCapacity);
            bool read = SendMessageTimeoutW(window, WmGetText, (IntPtr)buffer.Capacity, buffer,
                BlockIncomingSends | AbortIfHung | ErrorOnExit, TextTimeoutMs, out var length) != IntPtr.Zero;
            text = read && length.ToUInt64() < (ulong)(buffer.Capacity - 1) ? buffer.ToString() : "";
            return read;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetLastActivePopup(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string windowClass, string title);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam,
            StringBuilder text, uint flags, uint timeout, out UIntPtr result);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam,
            IntPtr lParam, uint flags, uint timeout, out UIntPtr result);

        private static string WindowTitle(IntPtr window)
        {
            var text = new StringBuilder(300);
            NativeMethods.GetWindowTextW(window, text, 300);
            return text.ToString();
        }
    }
}
