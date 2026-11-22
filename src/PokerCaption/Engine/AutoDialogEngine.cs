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
        private sealed record DialogAction(string Caption, int DelayMs, bool ClosesTable = false);
        private sealed record TableCloseRequest(uint ProcessId, long? LastPostTick, IntPtr ConfirmedPopup = default);
        private sealed record PendingClose(IntPtr Window, uint Owner, string Title, string WindowClass, bool IsDialog, DialogAction Action, long DueTick)
        {
            public string Body { get; init; }
            public IntPtr Button { get; init; }
        }

        private static readonly DialogAction TableClosed = new("Table closed", 0, ClosesTable: true);
        private static readonly DialogAction InsufficientChips = new("Insufficient chips", 0);
        private static readonly DialogAction StackUpdate = new("Stack update", 0);
        private const string InsufficientChipsText = "You requested more chips than you have available";
        private const string StackUpdateText = "Your stack will be updated when the current hand is finished";
        private const string ConcludedText = "Play at this table has concluded";
        private const string PokerStarsDialogButtonClass = "PokerStarsButtonClass";
        private const string FlutterDialogButtonClass = "FlutterOnlinePokerButtonClass";
        private const string NativeDialogClass = "#32770";
        private const string InactivityTitle = "Inactivity Timeout";
        private const uint WmGetText = 0x000D;
        private const uint WmMouseMove = 0x0200, WmLeftButtonDown = 0x0201, WmLeftButtonUp = 0x0202;
        private static readonly IntPtr ClickPoint = (IntPtr)0x000A000A;
        private const uint BlockIncomingSends = 0x0001, AbortIfHung = 0x0002, ErrorOnExit = 0x0020;
        private const uint OwnerWindow = 4;
        private const uint TextTimeoutMs = 40;
        private const int TextCapacity = 2048;
        private const int MaxDialogLabels = 16;
        private const int PollIntervalMs = 100;
        private const int ScanIntervalMs = 500;
        private const int TableCloseRetryMs = 3000;
        private const int LeaveConfirmationWindowMs = 10000;
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

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
            "You have been unregistered from", "Would you like to register into another Satellite?",
            "PokerStars Logout Information", "Reminder: Your current sesson has been active for X hours",
            "You have been successfully removed from all the waiting list",
            "You have been successfully removed from all the waiting lists",
            "You reached the cap of cash game tables", "You are playing at too many tables",
            "You are currently playing the maximum allowed Zoom tables at once",
            "Current Session Information", "Current Session",
        };

        internal static bool IsRemovedOptionCaption(string caption) =>
            RemovedOptionCaptions.Contains(Normalize(caption).TrimEnd('.', '!', '?'));
        internal static bool IsLegacyTableCloseCaption(string caption) =>
            SameTitle(Normalize(caption), "table is closed");

        private readonly List<DialogAction> _rules = new();
        private readonly List<PendingClose> _pending = new();
        private HashSet<IntPtr> _closureTables = new();
        private readonly Dictionary<IntPtr, TableCloseRequest> _tableClosures = new();
        private readonly object _state = new();
        private readonly Action<string> _log;
        private readonly Func<IntPtr, IntPtr> _lastActivePopup;
        private readonly Func<IntPtr, string> _windowClass;
        private readonly Func<IntPtr, SharedMemory.RenderedNotice> _readRenderedNotice;
        internal volatile bool CloseTableHasBeenClosed;
        private bool _stayConnected;
        private bool _dismissInsufficientChips;
        private bool _dismissStackUpdate;

        private DateTime _lastScan = DateTime.MinValue;
        private DateTime _lastPidRefresh = DateTime.MinValue;
        private HashSet<uint> _clientPids = new();
        private System.Windows.Forms.Timer _timer;
        private Task _poll;
        private int _runRevision, _configRevision;
        private bool _layoutOnly;

        public AutoDialogEngine(Action<string> log) : this(log, null) { }

        internal AutoDialogEngine(Action<string> log, Func<IntPtr, SharedMemory.RenderedNotice> readRenderedNotice)
            : this(log, GetLastActivePopup, TableScanner.GetClass, readRenderedNotice) { }

        private AutoDialogEngine(Action<string> log, Func<IntPtr, IntPtr> lastActivePopup, Func<IntPtr, string> windowClass,
            Func<IntPtr, SharedMemory.RenderedNotice> readRenderedNotice = null)
        {
            _log = log ?? (_ => { });
            _lastActivePopup = lastActivePopup;
            _windowClass = windowClass;
            _readRenderedNotice = readRenderedNotice;
        }

        internal void Start(bool layoutOnly = false)
        {
            Stop();
            lock (_state) _layoutOnly = layoutOnly;
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
                if (string.IsNullOrWhiteSpace(caption) || IsRemovedOptionCaption(caption) || IsLegacyTableCloseCaption(caption)) continue;
                int delay = Math.Max(0, ini.GetInt("AutoDialog", "Delay" + i, 0));
                _rules.Add(new DialogAction(caption.Trim(), delay));
            }
            CloseTableHasBeenClosed = ini.GetBool("PokerCaption", "CloseTableHasBeenClosed", false);
            if (!CloseTableHasBeenClosed) _tableClosures.Clear();
            _stayConnected = ini.GetBool("PokerCaption", "InactivityTO", false);
            _dismissInsufficientChips = ini.GetBool("PokerCaption", "DismissInsufficientChips", false);
            _dismissStackUpdate = ini.GetBool("PokerCaption", "DismissStackUpdate", false);
            _pending.RemoveAll(entry => entry.Button != IntPtr.Zero
                ? entry.Action == InsufficientChips ? !_dismissInsufficientChips
                    : entry.Action == StackUpdate ? !_dismissStackUpdate : !_stayConnected
                : entry.Action.ClosesTable ? !CloseTableHasBeenClosed : (entry.Body == null
                ? MatchDialog(entry.Title)
                : MatchBody(entry.Body, entry.IsDialog)) != entry.Action);
        }

        private DialogAction MatchDialog(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            return _rules.FirstOrDefault(rule => title.Contains(rule.Caption, StringComparison.OrdinalIgnoreCase));
        }

        private DialogAction MatchBody(string body, bool isDialog)
        {
            if (!isDialog || string.IsNullOrWhiteSpace(body)) return null;
            string text = Normalize(body);
            if (CloseTableHasBeenClosed && IsTableEndText(text)) return TableClosed;
            return null;
        }

        private static string Normalize(string text) => Whitespace.Replace(text.Trim(), " ");

        private static bool SameTitle(string title, string expected) =>
            string.Equals(title.Trim().TrimEnd('.', '!', '?'), expected.TrimEnd('.', '!', '?'), StringComparison.OrdinalIgnoreCase);

        private static bool IsTableEndText(string text) =>
            IsLegacyTableCloseCaption(text) || SameTitle(text, "This table has been closed") ||
            SameTitle(text, ConcludedText) || text.StartsWith(ConcludedText + " ", StringComparison.OrdinalIgnoreCase);

        private PendingClose ReadDialog(IntPtr window, uint owner, long tickCount)
        {
            string title = WindowTitle(window);
            string windowClass = _windowClass(window);
            bool isDialog = IsDialogWindow(window, owner, windowClass);
            string buttonText = null;
            bool checkSisalNotice, checkChips, checkStackUpdate;
            lock (_state)
            {
                var action = CloseTableHasBeenClosed && isDialog && IsTableEndText(Normalize(title))
                    ? TableClosed : _layoutOnly ? null : MatchDialog(title);
                if (action != null)
                    return new PendingClose(window, owner, title, windowClass, isDialog, action, tickCount + action.DelayMs);
                if (_stayConnected && windowClass == NativeDialogClass && title == InactivityTitle)
                    buttonText = "Stay connected";
                checkChips = _dismissInsufficientChips && windowClass == NativeDialogClass && title == "Sisal";
                checkStackUpdate = _dismissStackUpdate && windowClass == NativeDialogClass && title == "Sisal";
                checkSisalNotice = checkChips || checkStackUpdate;
                if (!isDialog || (!CloseTableHasBeenClosed && buttonText == null && !checkSisalNotice))
                    return null;
            }

            if (buttonText != null)
            {
                var button = FindDialogButton(window, owner, buttonText);
                return button == IntPtr.Zero ? null : new PendingClose(window, owner, title, windowClass, isDialog,
                    new DialogAction(title, 0), tickCount) { Button = button };
            }

            // This rule also checks the text of the dialog's child controls.
            PendingClose match = null;
            bool ambiguous = false;
            int labels = 0;
            NativeMethods.EnumChildWindows(window, (child, _) =>
            {
                string childClass = TableScanner.GetClass(child);
                if ((!checkSisalNotice && childClass != "Static") ||
                    !NativeMethods.IsWindowVisible(child) || !HasProcess(child, owner)) return true;
                if (++labels > MaxDialogLabels || !TryReadControlText(child, out var text))
                {
                    ambiguous = true;
                    return false;
                }
                DialogAction candidate;
                lock (_state)
                    candidate = checkChips && SameTitle(Normalize(text), InsufficientChipsText) ? InsufficientChips :
                        checkStackUpdate && SameTitle(Normalize(text), StackUpdateText) ? StackUpdate :
                        childClass == "Static" ? MatchBody(text, isDialog) : null;
                if (candidate == null) return true;
                if (match != null && match.Action != candidate)
                {
                    ambiguous = true;
                    return false;
                }
                match ??= new PendingClose(window, owner, title, windowClass, isDialog, candidate, tickCount + candidate.DelayMs)
                    { Body = text };
                return true;
            }, IntPtr.Zero);
            if (ambiguous) return null;
            if (!checkSisalNotice || match?.Action == TableClosed) return match;

            var ok = FindDialogButton(window, owner, "OK");
            if (ok == IntPtr.Zero) return null;
            if (match != null) return match with { Button = ok };
            var notice = _readRenderedNotice?.Invoke(window) switch
            {
                SharedMemory.RenderedNotice.InsufficientChips when checkChips => InsufficientChips,
                SharedMemory.RenderedNotice.StackUpdate when checkStackUpdate => StackUpdate,
                _ => null,
            };
            return notice == null ? null :
                new PendingClose(window, owner, title, windowClass, isDialog, notice, tickCount) { Button = ok };
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
                    if (entry.Action.ClosesTable) ObserveTableEnd(GetWindow(entry.Window, OwnerWindow), entry.Owner);
                    sent = entry.Button != IntPtr.Zero ? ClickDialogButton(entry.Button) :
                        NativeMethods.PostMessageW(entry.Window, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
                if (sent)
                    _log("auto-dialog: " + (entry.Button == IntPtr.Zero ? "richiesta chiusura" : "click") + " '" + entry.Action.Caption + "'");
            }
            FlushTableClosures(tickCount, run, config);
        }

        private static bool ClickDialogButton(IntPtr button) =>
            NativeMethods.PostMessageW(button, WmMouseMove, IntPtr.Zero, ClickPoint) &&
            NativeMethods.PostMessageW(button, WmLeftButtonDown, (IntPtr)1, ClickPoint) &&
            NativeMethods.PostMessageW(button, WmLeftButtonUp, IntPtr.Zero, ClickPoint);

        internal void ObserveTableEnd(IntPtr table)
        {
            if (NativeMethods.GetWindowThreadProcessId(table, out var processId) != 0)
                lock (_state) ObserveTableEnd(table, processId);
        }

        private void ObserveTableEnd(IntPtr table, uint processId)
        {
            if (CloseTableHasBeenClosed && _closureTables.Contains(table) && HasProcess(table, processId))
                _tableClosures.TryAdd(table, new TableCloseRequest(processId, null));
        }

        private void FlushTableClosures(long tickCount, int run, int config)
        {
            lock (_state)
            {
                if (run != _runRevision || config != _configRevision) return;
                if (!CloseTableHasBeenClosed) return;
                foreach (var (table, request) in _tableClosures.ToArray())
                {
                    if (!HasProcess(table, request.ProcessId))
                    {
                        _tableClosures.Remove(table);
                        continue;
                    }
                    var popup = _lastActivePopup(table);
                    if (popup != table && NativeMethods.IsWindowVisible(popup) && HasProcess(popup, request.ProcessId))
                    {
                        if (request.LastPostTick is long posted && tickCount - posted <= LeaveConfirmationWindowMs &&
                            popup != request.ConfirmedPopup && ConfirmTableLeave(table, popup, request.ProcessId))
                        {
                            _tableClosures[table] = request with { ConfirmedPopup = popup };
                            _log("auto-dialog: click 'Leave Now' dopo la richiesta di chiusura del tavolo concluso");
                        }
                        continue;
                    }
                    if (request.LastPostTick is long last && tickCount - last < TableCloseRetryMs) continue;
                    if (NativeMethods.PostMessageW(table, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                    {
                        _tableClosures[table] = request with { LastPostTick = tickCount, ConfirmedPopup = IntPtr.Zero };
                        _log("auto-dialog: richiesta chiusura del tavolo concluso");
                    }
                }
            }
        }

        private bool ConfirmTableLeave(IntPtr table, IntPtr popup, uint processId)
        {
            if (GetWindow(popup, OwnerWindow) != table || !IsDialogWindow(popup, processId, _windowClass(popup)))
                return false;
            string tableTitle = WindowTitle(table);
            string popupTitle = WindowTitle(popup);
            if (tableTitle.Length == 0 ||
                (popupTitle != tableTitle && popupTitle != "Table " + tableTitle)) return false;

            var leave = FindDialogButton(popup, processId, "Leave Now");
            return leave != IntPtr.Zero && FindDialogButton(popup, processId, "Keep Playing") != IntPtr.Zero &&
                ClickDialogButton(leave);
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

        private static string WindowTitle(IntPtr window)
        {
            var text = new StringBuilder(300);
            NativeMethods.GetWindowTextW(window, text, 300);
            return text.ToString();
        }
    }
}
