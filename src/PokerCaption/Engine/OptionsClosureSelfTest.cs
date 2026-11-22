using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PokerCaption.Config;

namespace PokerCaption.Engine;

public sealed partial class AutoDialogEngine
{
    internal static void CheckOptionsClosures(IEnumerable<string> optionCaptions, Action<string, bool, string> check)
    {
        var captions = optionCaptions.ToArray();
        var activePopups = new Dictionary<IntPtr, IntPtr>();
        var windowClasses = new Dictionary<IntPtr, string>();
        var engine = new AutoDialogEngine(null, table => activePopups.GetValueOrDefault(table, table),
            window => windowClasses.TryGetValue(window, out var name) ? name : TableScanner.GetClass(window));
        ClosureTestChild.RegisterFixtureClass(PokerStarsDialogButtonClass);
        ClosureTestChild.RegisterFixtureClass(FlutterDialogButtonClass);
        engine._clientPids.Add((uint)Environment.ProcessId);
        engine.SetOptionCaptions(captions);
        using var owner = new ClosureTestForm("Options test owner");
        owner.Show();
        owner.Enabled = false;

        void Flush(int milliseconds = 0) => engine.FlushPending(milliseconds, engine._runRevision, engine._configRevision);

        bool Exercise(string title, string childText, string childClass = "Static", bool modal = true,
            int delayMs = 0, bool expectClose = true, bool recognized = true, string otherButton = null,
            string windowClass = null, bool visibleChild = true, string otherLabel = null, string otherButtonClass = null,
            Action<ClosureTestChild> beforeDeadline = null, bool expectClick = false)
        {
            using var window = new ClosureTestForm(title);
            if (modal) window.Show(owner);
            else window.Show();
            using var child = new ClosureTestChild(window, childClass, childText, visibleChild);
            using var additionalButton = new ClosureTestChild(window, otherButtonClass ?? childClass, otherButton);
            using var additionalLabel = new ClosureTestChild(window, "Static", otherLabel);
            var handle = window.Handle;
            if (windowClass != null || modal) windowClasses.Add(handle, windowClass ?? NativeDialogClass);
            if (!recognized) engine._clientPids.Clear();
            engine.ScanWindow(handle, 0, engine._runRevision, engine._configRevision);
            bool remainedOpen = true;
            if (delayMs > 0)
            {
                Flush(delayMs - 1);
                Application.DoEvents();
                remainedOpen = window.Visible;
            }
            beforeDeadline?.Invoke(child);
            Flush(delayMs);
            Application.DoEvents();
            if (!recognized) engine._clientPids.Add((uint)Environment.ProcessId);
            windowClasses.Remove(handle);
            var expectedMessages = !expectClick ? Array.Empty<(int, IntPtr, IntPtr)>() : title == "Inactivity Timeout"
                ? new[] { ((int)WmMouseMove, IntPtr.Zero, ClickPoint), ((int)WmLeftButtonDown, (IntPtr)1, ClickPoint),
                    ((int)WmLeftButtonUp, IntPtr.Zero, ClickPoint) }
                : new[] { ((int)WmLeftButtonDown, (IntPtr)1, ClickPoint), ((int)WmLeftButtonUp, IntPtr.Zero, ClickPoint) };
            if (remainedOpen && window.Visible != expectClose && window.CloseMessages == (expectClose ? 1 : 0) &&
                child.MouseMessages.SequenceEqual(expectedMessages) && additionalButton.MouseMessages.Count == 0) return true;
            check($"Options WinForms fixture {title}", false,
                $"modal={modal} visible={window.Visible} expectClose={expectClose} WM_CLOSE={window.CloseMessages} button messages={child.MouseMessages.Count}");
            return false;
        }

        foreach (string caption in captions)
        {
            var config = new IniFile(null);
            config.Set("AutoDialog", "Caption0", caption);
            const int delay = 0;
            config.Set("AutoDialog", "Delay0", delay);
            engine.Load(config);
            check("Options WinForms dialog " + caption,
                Exercise(caption, null, delayMs: delay) &&
                Exercise("Other dialog", caption, delayMs: delay) &&
                Exercise(caption, null, modal: false, delayMs: delay, expectClose: false), "");
        }

        foreach (var (key, caption) in new[]
        {
            ("CloseTableHasBeenClosed", TableClosed.Caption),
            ("CloseTableHasConcluded", TableConcluded.Caption),
            ("CloseTournamentIsOver", TournamentEnded.Caption),
        })
        {
            using var table = new ClosureTestForm("Options test table");
            using var other = new ClosureTestForm("Options other table");
            using var popup = new ClosureTestForm("Options table message");
            table.Show();
            other.Show();
            table.Enabled = false;
            popup.Show(table);
            activePopups.Add(table.Handle, popup.Handle);
            using var label = new ClosureTestChild(popup, "Static", caption.ToLowerInvariant());
            var config = new IniFile(null);
            engine.Load(config);
            engine.SetClosureTables(new[] { table.Handle, other.Handle });
            popup.Text = caption;
            engine.ScanWindow(popup.Handle, 0, engine._runRevision, engine._configRevision);
            Flush();
            bool rejectedTitleAndWrongCase = !engine._tableClosures.ContainsKey(table.Handle);
            popup.Text = "Options table message";
            string extendedCaption = caption + " (fixture suffix)";
            label.SetText(extendedCaption);
            engine.ScanWindow(popup.Handle, 0, engine._runRevision, engine._configRevision);
            Flush();
            bool fullOrPrefix = engine._tableClosures.ContainsKey(table.Handle) == (caption != TableClosed.Caption);
            label.SetText(caption);
            engine.ScanWindow(popup.Handle, 0, engine._runRevision, engine._configRevision);
            Flush();
            config.Set("PokerCaption", key, 1);
            engine.Load(config);
            Flush();
            Application.DoEvents();
            bool waited = table.Visible && table.CloseMessages == 0 && popup.Visible && popup.CloseMessages == 0;
            popup.Close();
            activePopups.Remove(table.Handle);
            table.Enabled = true;
            config.Set("PokerCaption", key, 0);
            engine.Load(config);
            Flush();
            Application.DoEvents();
            bool disabled = table.Visible && table.CloseMessages == 0;
            config.Set("PokerCaption", key, 1);
            engine.Load(config);
            table.IgnoreClose = true;
            Flush();
            Application.DoEvents();
            Flush(2999);
            Application.DoEvents();
            bool throttled = table.Visible && table.CloseMessages == 1;
            table.IgnoreClose = false;
            Flush(3000);
            Application.DoEvents();
            check("Options WinForms closes tracked table " + key,
                waited && disabled && throttled && !table.Visible && table.CloseMessages == 2 && other.Visible &&
                Exercise(caption, null, expectClose: false),
                $"waited={waited} disabled={disabled} throttled={throttled} tableWM_CLOSE={table.CloseMessages}");
            check("Options WinForms table signal uses original text comparison " + key,
                rejectedTitleAndWrongCase && fullOrPrefix, "");
        }
        engine.SetClosureTables(Array.Empty<IntPtr>());

        var oldConfig = new IniFile(null);
        oldConfig.Set("AutoDialog", "Caption0", "TABLE CAP REACHED");
        oldConfig.Set("AutoDialog", "Caption1", "This seat is reserved for players on the waiting list");
        oldConfig.Set("AutoDialog", "Caption2", "This seat is occupied.");
        oldConfig.Set("AutoDialog", "Caption3", "My News");
        oldConfig.Set("AutoDialog", "Caption4", "table is closed");
        engine.Load(oldConfig);
        check("Options WinForms legacy table cap rule is inert", Exercise("Table Cap Reached", null, expectClose: false), "");
        check("Options WinForms legacy seat reserved rule is inert",
            Exercise("This seat is reserved for players on the waiting list", null, expectClose: false), "");
        check("Options WinForms legacy seat occupied rule is inert",
            Exercise("This seat is occupied.", null, expectClose: false), "");
        check("Options WinForms retired news rule is inert", Exercise("My News", null, expectClose: false), "");
        check("Options WinForms legacy filtering preserves other rules", Exercise("table is closed", null), "");

        foreach (var (key, title, buttonText) in ButtonOptions)
        {
            var config = new IniFile(null);
            config.Set("PokerCaption", key, 1);
            engine.Load(config);
            check("Options WinForms clicks the exact dialog button " + key,
                Exercise(title, buttonText, PokerStarsDialogButtonClass, expectClose: false, expectClick: true,
                    otherButton: "Cancel") &&
                Exercise(title, buttonText, FlutterDialogButtonClass, expectClose: false, expectClick: true), "");
            check("Options WinForms rejects unsafe dialog targets " + key,
                Exercise(title.ToLowerInvariant(), buttonText, PokerStarsDialogButtonClass, expectClose: false) &&
                Exercise(title, buttonText.ToLowerInvariant(), PokerStarsDialogButtonClass, expectClose: false) &&
                Exercise(title, buttonText, "Button", expectClose: false) &&
                Exercise(title, buttonText, PokerStarsDialogButtonClass, expectClose: false, visibleChild: false) &&
                Exercise(title, buttonText, PokerStarsDialogButtonClass, expectClose: false, recognized: false) &&
                Exercise(title, buttonText, PokerStarsDialogButtonClass, expectClose: false,
                    otherButton: buttonText, otherButtonClass: FlutterDialogButtonClass) &&
                Exercise(title, buttonText, PokerStarsDialogButtonClass, expectClose: false,
                    beforeDeadline: child => child.SetText("Different button")), "");
            check("Options WinForms cancels dialog click when the flag is turned off " + key,
                Exercise(title, buttonText, PokerStarsDialogButtonClass, expectClose: false,
                    beforeDeadline: _ => engine.Load(new IniFile(null))) &&
                Exercise(title, buttonText, FlutterDialogButtonClass, expectClose: false), "");
        }

        var waitingListConfig = new IniFile(null);
        waitingListConfig.Set("PokerCaption", "CloseTableWaitingList", 1);
        engine.Load(waitingListConfig);
        using (var table = new ClosureTestForm("Waiting list table"))
        {
            table.Show();
            using var button = new ClosureTestChild(table, PokerStarsDialogButtonClass, "Unjoin Waiting List");
            engine.SetClosureTables(new[] { table.Handle });
            engine.ScanWindow(table.Handle, 0, engine._runRevision, engine._configRevision);
            Flush();
            Application.DoEvents();
            check("Options WinForms waiting-list label alone leaves the tracked table open",
                table.Visible && table.CloseMessages == 0 && button.MouseMessages.Count == 0, "");
            engine.SetClosureTables(Array.Empty<IntPtr>());
        }

        CheckTournamentLobbyClosures(check);
    }

    private static void CheckTournamentLobbyClosures(Action<string, bool, string> check)
    {
        var closer = new TournamentLobbyCloser(null);
        closer.SetEnabled(true);
        using var lobby = new ClosureTestForm("Tournament 123456 Lobby");
        using var unrelated = new ClosureTestForm("Tournament 123457 Lobby");
        using var untitled = new ClosureTestForm("");
        using var first = new ClosureTestForm("Table '123456 1' 9-max Level III (100/200)");
        using var second = new ClosureTestForm("Menkab - 100/200 - Tournament 123456 Table 2");
        lobby.Show();
        unrelated.Show();
        untitled.Show();
        first.Show();
        second.Show();
        TableInfo Table(ClosureTestForm form)
        {
            var table = new TableInfo { Hwnd = form.Handle, Title = form.Text };
            TitleParser.Apply(table);
            return table;
        }

        closer.Observe(new[] { Table(first), Table(second) });
        first.Close();
        second.Hide();
        closer.Observe(Array.Empty<TableInfo>());
        Application.DoEvents();
        check("Options WinForms shared tournament lobby stays open after first table", lobby.Visible &&
            lobby.CloseMessages == 0 && second.IsHandleCreated, "");
        second.Close();
        closer.Observe(Array.Empty<TableInfo>());
        Application.DoEvents();
        check("Options WinForms closes only the lobby of the final table", !lobby.Visible && lobby.CloseMessages == 1 &&
            unrelated.Visible && unrelated.CloseMessages == 0 && untitled.Visible && untitled.CloseMessages == 0, "");

        using var candidate = new ClosureTestForm("Tournament 654321 Lobby");
        using var duplicate = new ClosureTestForm("Tournament 654321 Lobby - duplicate");
        using var last = new ClosureTestForm("Table '654321 1' 9-max Level I (10/20)");
        candidate.Show();
        duplicate.Show();
        last.Show();
        closer.Observe(new[] { Table(last) });
        duplicate.Close();
        last.Close();
        closer.Observe(Array.Empty<TableInfo>());
        Application.DoEvents();
        check("Options WinForms ambiguous tournament lobby stays open", candidate.Visible && candidate.CloseMessages == 0, "");

        using var disabledTable = new ClosureTestForm("Table '654321 2' 9-max Level I (10/20)");
        disabledTable.Show();
        closer.Observe(new[] { Table(disabledTable) });
        closer.SetEnabled(false);
        disabledTable.Close();
        closer.Observe(Array.Empty<TableInfo>());
        Application.DoEvents();
        check("Options WinForms disabled tournament lobby rule leaves window open", candidate.Visible && candidate.CloseMessages == 0, "");
    }

    private sealed class ClosureTestForm : Form
    {
        internal int CloseMessages { get; private set; }
        internal bool IgnoreClose { get; set; }
        protected override bool ShowWithoutActivation => true;

        internal ClosureTestForm(string title)
        {
            Text = title;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-2000, -2000);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0010)
            {
                CloseMessages++;
                if (IgnoreClose) return;
            }
            base.WndProc(ref message);
        }
    }

    private sealed class ClosureTestChild : NativeWindow, IDisposable
    {
        private readonly IntPtr _handle;
        internal List<(int Message, IntPtr Keys, IntPtr Point)> MouseMessages { get; } = new();

        internal ClosureTestChild(Form parent, string className, string text, bool visible = true)
        {
            if (text == null) return;
            const uint wsChild = 0x40000000, wsVisible = 0x10000000;
            _handle = CreateWindowExW(0, className, text, wsChild | (visible ? wsVisible : 0), 0, 0, 160, 24,
                parent.Handle, (IntPtr)100, GetModuleHandleW(null), IntPtr.Zero);
            if (_handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            AssignHandle(_handle);
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero) return;
            ReleaseHandle();
            DestroyWindow(_handle);
        }

        protected override void WndProc(ref Message message)
        {
            const int bmClick = 0x00F5;
            if ((uint)message.Msg is WmMouseMove or WmLeftButtonDown or WmLeftButtonUp || message.Msg == bmClick)
                MouseMessages.Add((message.Msg, message.WParam, message.LParam));
            base.WndProc(ref message);
        }

        internal void SetText(string text)
        {
            if (!SetWindowTextW(_handle, text)) throw new System.ComponentModel.Win32Exception();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool SetWindowTextW(IntPtr window, string text);

        internal static void RegisterFixtureClass(string className)
        {
            var descriptor = new WindowClass
            {
                Procedure = GetProcAddress(GetModuleHandleW("user32.dll"), "DefWindowProcW"),
                Instance = GetModuleHandleW(null),
                Name = className,
            };
            if (RegisterClassW(ref descriptor) == 0)
                throw new System.ComponentModel.Win32Exception();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            internal uint Style;
            internal IntPtr Procedure;
            internal int ClassExtra, WindowExtra;
            internal IntPtr Instance, Icon, Cursor, Background, MenuName;
            internal string Name;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern ushort RegisterClassW(ref WindowClass windowClass);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr GetModuleHandleW(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procedure);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(uint extendedStyle, string className, string text, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr id, IntPtr instance, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr window);
    }
}
