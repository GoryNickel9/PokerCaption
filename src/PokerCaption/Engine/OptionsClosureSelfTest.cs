using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PokerCaption.Config;
using PokerCaption.Native;

namespace PokerCaption.Engine;

public sealed partial class AutoDialogEngine
{
    internal static void CheckOptionsClosures(Action<string, bool, string> check)
    {
        var activePopups = new Dictionary<IntPtr, IntPtr>();
        var windowClasses = new Dictionary<IntPtr, string>();
        var renderedNotices = new Dictionary<IntPtr, SharedMemory.RenderedNotice>();
        var engine = new AutoDialogEngine(null, table => activePopups.GetValueOrDefault(table, table),
            window => windowClasses.TryGetValue(window, out var name) ? name : TableScanner.GetClass(window),
            window => renderedNotices.GetValueOrDefault(window));
        ClosureTestChild.RegisterFixtureClass(PokerStarsDialogButtonClass);
        ClosureTestChild.RegisterFixtureClass(FlutterDialogButtonClass);
        engine._clientPids.Add((uint)Environment.ProcessId);
        using var owner = new ClosureTestForm("Options test owner");
        owner.Show();
        owner.Enabled = false;

        void Flush(int milliseconds = 0) => engine.FlushPending(milliseconds, engine._runRevision, engine._configRevision);

        bool Exercise(string title, string childText, string childClass = "Static", bool modal = true,
            int delayMs = 0, bool expectClose = true, bool recognized = true, string otherButton = null,
            string windowClass = null, bool visibleChild = true, string otherLabel = null, string otherButtonClass = null,
            Action<ClosureTestChild> beforeDeadline = null, bool expectClick = false,
            SharedMemory.RenderedNotice renderedNotice = SharedMemory.RenderedNotice.None)
        {
            using var window = new ClosureTestForm(title);
            if (modal) window.Show(owner);
            else window.Show();
            using var child = new ClosureTestChild(window, childClass, childText, visibleChild);
            using var additionalButton = new ClosureTestChild(window, otherButtonClass ?? childClass, otherButton);
            using var additionalLabel = new ClosureTestChild(window, "Static", otherLabel);
            var handle = window.Handle;
            renderedNotices[handle] = renderedNotice;
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
            renderedNotices.Remove(handle);
            var expectedMessages = !expectClick ? Array.Empty<(int, IntPtr, IntPtr)>() :
                new[] { ((int)WmMouseMove, IntPtr.Zero, ClickPoint), ((int)WmLeftButtonDown, (IntPtr)1, ClickPoint),
                    ((int)WmLeftButtonUp, IntPtr.Zero, ClickPoint) };
            if (remainedOpen && window.Visible != expectClose && window.CloseMessages == (expectClose ? 1 : 0) &&
                child.MouseMessages.SequenceEqual(expectedMessages) && additionalButton.MouseMessages.Count == 0) return true;
            check($"Options WinForms fixture {title}", false,
                $"modal={modal} visible={window.Visible} expectClose={expectClose} WM_CLOSE={window.CloseMessages} button messages={child.MouseMessages.Count}");
            return false;
        }

        var closureConfig = new IniFile(null);
        closureConfig.Set("PokerCaption", "CloseTableHasBeenClosed", 1);
        closureConfig.Set("PokerCaption", "InactivityTO", 1);
        closureConfig.Set("PokerCaption", "DismissInsufficientChips", 1);
        closureConfig.Set("PokerCaption", "DismissStackUpdate", 1);
        engine.Load(closureConfig);
        engine._layoutOnly = true;
        check("Options unified rule closes supported dialogs in layout mode",
            Exercise("table is closed", null) &&
            Exercise("Other dialog", "This table has been closed") &&
            Exercise("Other dialog", ConcludedText + " and the table will close in 6 sec") &&
            Exercise("table is closed", null, modal: false, expectClose: false) &&
            Exercise("Other dialog", "This table has been closed by another player", expectClose: false), "");

        using (var table = new ClosureTestForm("Options test table"))
        using (var other = new ClosureTestForm("Options other table"))
        using (var popup = new ClosureTestForm("table is closed"))
        {
            table.Show();
            other.Show();
            table.Enabled = false;
            popup.Show(table);
            windowClasses.Add(popup.Handle, NativeDialogClass);
            activePopups.Add(table.Handle, popup.Handle);
            engine.SetClosureTables([table.Handle, other.Handle]);
            engine.ScanWindow(popup.Handle, 0, engine._runRevision, engine._configRevision);
            Flush();
            Application.DoEvents();
            bool popupClosedFirst = !popup.Visible && popup.CloseMessages == 1 && table.CloseMessages == 0;
            activePopups.Remove(table.Handle);
            table.Enabled = true;
            Flush(100);
            Application.DoEvents();
            check("Options closes popup before its tracked table", popupClosedFirst &&
                !table.Visible && table.CloseMessages == 1 && other.Visible, "");
        }

        using (var table = new ClosureTestForm("Rendered table"))
        using (var other = new ClosureTestForm("Other table"))
        {
            table.Show();
            other.Show();
            engine.SetClosureTables([table.Handle]);
            engine.ObserveTableEnd(table.Handle);
            engine.ObserveTableEnd(other.Handle);
            closureConfig.Set("PokerCaption", "CloseTableHasBeenClosed", 0);
            engine.Load(closureConfig);
            Flush();
            Application.DoEvents();
            bool cancelled = table.CloseMessages == 0;
            closureConfig.Set("PokerCaption", "CloseTableHasBeenClosed", 1);
            engine.Load(closureConfig);
            table.IgnoreClose = true;
            engine.ObserveTableEnd(table.Handle);
            Flush();
            Application.DoEvents();
            engine.ObserveTableEnd(table.Handle);
            Flush(2999);
            Application.DoEvents();
            bool throttled = table.CloseMessages == 1;
            table.IgnoreClose = false;
            Flush(3000);
            Application.DoEvents();
            check("Options rendered table signal closes only its tracked window in layout mode",
                cancelled && throttled && table.CloseMessages == 2 && !table.Visible && other.Visible, "");
        }

        using (var table = new ClosureTestForm("Closing table"))
        using (var popup = new ClosureTestForm("Table Closing table"))
        {
            table.Show();
            table.IgnoreClose = true;
            popup.Show(table);
            var popupHandle = popup.Handle;
            windowClasses[popupHandle] = NativeDialogClass;
            activePopups[table.Handle] = popupHandle;
            using var leave = new ClosureTestChild(popup, FlutterDialogButtonClass, "Leave Now");
            using var stay = new ClosureTestChild(popup, FlutterDialogButtonClass, "Keep Playing");
            engine.SetClosureTables([table.Handle]);
            engine.ObserveTableEnd(table.Handle);
            Flush();
            Application.DoEvents();
            bool ignoredExistingPopup = table.CloseMessages == 0 && leave.MouseMessages.Count == 0;

            popup.Hide();
            Flush(100);
            Application.DoEvents();
            popup.Text = "Different table";
            popup.Show(table);
            Flush(200);
            Application.DoEvents();
            bool ignoredWrongTitle = leave.MouseMessages.Count == 0;
            popup.Text = "Table Closing table";
            Flush(300);
            Application.DoEvents();
            Flush(400);
            Application.DoEvents();
            check("Options confirms a matching Leave Now prompt only after its own table close",
                ignoredExistingPopup && ignoredWrongTitle && table.CloseMessages == 1 &&
                leave.MouseMessages.Select(message => message.Message)
                    .SequenceEqual([(int)WmMouseMove, (int)WmLeftButtonDown, (int)WmLeftButtonUp]) &&
                stay.MouseMessages.Count == 0, "");
            activePopups.Remove(table.Handle);
            windowClasses.Remove(popupHandle);
        }

        check("Options dismisses only the insufficient-chips notice with a unique OK button",
            Exercise("Sisal", "OK", FlutterDialogButtonClass, expectClose: false, expectClick: true,
                otherLabel: "You requested more chips than you\nhave available") &&
            Exercise("Sisal", "OK", FlutterDialogButtonClass, expectClose: false,
                otherLabel: "You requested more chips") &&
            Exercise("Sisal", "Cancel", FlutterDialogButtonClass, expectClose: false,
                otherLabel: InsufficientChipsText) &&
            Exercise("Sisal", "OK", FlutterDialogButtonClass, expectClose: false,
                otherLabel: InsufficientChipsText, otherButton: "OK", otherButtonClass: PokerStarsDialogButtonClass) &&
            Exercise("Unrelated popup", "OK", FlutterDialogButtonClass, expectClose: false,
                otherLabel: InsufficientChipsText), "");
        check("Options identifies and separately enables the two rendered Sisal notices",
            Exercise("Sisal", "OK", FlutterDialogButtonClass, expectClose: false, expectClick: true,
                renderedNotice: SharedMemory.RenderedNotice.StackUpdate) &&
            Exercise("Sisal", "OK", FlutterDialogButtonClass, expectClose: false, expectClick: true,
                renderedNotice: SharedMemory.RenderedNotice.InsufficientChips) &&
            Exercise("Sisal", "OK", FlutterDialogButtonClass, expectClose: false) &&
            Exercise("Sisal", "OK", FlutterDialogButtonClass, expectClose: false,
                renderedNotice: SharedMemory.RenderedNotice.StackUpdate, beforeDeadline: _ =>
                {
                    closureConfig.Set("PokerCaption", "DismissStackUpdate", 0);
                    engine.Load(closureConfig);
                }) &&
            Exercise("Sisal", "OK", FlutterDialogButtonClass, expectClose: false, expectClick: true,
                renderedNotice: SharedMemory.RenderedNotice.InsufficientChips), "");
        check("Options layout mode keeps inactivity available",
            Exercise(InactivityTitle, "Stay connected", PokerStarsDialogButtonClass, expectClose: false, expectClick: true,
                otherButton: "Cancel") &&
            Exercise(InactivityTitle, "Stay connected", FlutterDialogButtonClass, expectClose: false, expectClick: true) &&
            Exercise(InactivityTitle, "Stay Connected", FlutterDialogButtonClass, expectClose: false) &&
            Exercise(InactivityTitle, "Stay connected", FlutterDialogButtonClass, expectClose: false, recognized: false) &&
            Exercise(InactivityTitle, "Stay connected", FlutterDialogButtonClass, expectClose: false,
                otherButton: "Stay connected", otherButtonClass: PokerStarsDialogButtonClass) &&
            Exercise(InactivityTitle, "Stay connected", FlutterDialogButtonClass, expectClose: false,
                beforeDeadline: _ => engine.Load(new IniFile(null))), "");
        engine._layoutOnly = false;

        var oldConfig = new IniFile(null);
        oldConfig.Set("AutoDialog", "Caption0", "TABLE CAP REACHED");
        oldConfig.Set("AutoDialog", "Caption1", "This seat is reserved for players on the waiting list");
        oldConfig.Set("AutoDialog", "Caption2", "This seat is occupied.");
        oldConfig.Set("AutoDialog", "Caption3", "My News");
        oldConfig.Set("AutoDialog", "Caption4", "table is closed");
        oldConfig.Set("AutoDialog", "Caption5", "Options unrelated custom dialog");
        engine.Load(oldConfig);
        check("Options retired rules are inert and custom rules survive",
            Exercise("Table Cap Reached", null, expectClose: false) &&
            Exercise("This seat is reserved for players on the waiting list", null, expectClose: false) &&
            Exercise("This seat is occupied.", null, expectClose: false) &&
            Exercise("My News", null, expectClose: false) &&
            Exercise("table is closed", null, expectClose: false) &&
            Exercise("Options unrelated custom dialog", null), "");

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
