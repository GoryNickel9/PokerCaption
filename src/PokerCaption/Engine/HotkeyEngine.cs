using System;
using System.Runtime.InteropServices;
using PokerCaption.Config;
using PokerCaption.Native;

namespace PokerCaption.Engine
{
    /// <summary>
    /// Wheel bet (Fase 6): rotella sul tavolo = +/− MouseWheelUp/Down (bb) o
    /// % pot postflop (MouseWheelPrPotPostflop/Val), arrotondamento MouseWheelRound.
    /// Solo mouse: niente hook tastiera, niente mappature.
    /// Su client renderizzati (coords) il valore viene riscritto alle coordinate
    /// della bet box (SendInput); su client classico via WM_SETTEXT.
    /// </summary>
    public sealed class HotkeyEngine : IDisposable
    {
        private IntPtr _msHook;
        private readonly NativeMethods.HookProc _msProc; //tieni vivo il delegato
        private readonly Func<IntPtr> _getTableUnderCursor;
        private readonly Action<string> _log;
        private readonly object _hookState = new();
        private SynchronizationContext _ui;
        private uint _hookThreadId;
        private volatile bool _disposed;
        private IniFile _ini;

        public double WheelUp = 0.5, WheelDown = 0.5;
        public bool WheelPrPotPostflop;
        public double WheelPrPotVal = 5;
        public bool WheelInDollars;
        public int WheelRound;

        public HotkeyEngine(Func<IntPtr> tableUnderCursor, Action<string> log)
        {
            _getTableUnderCursor = tableUnderCursor;
            _log = log ?? (_ => { });
            _msProc = MsProc;
        }

        public void Load(IniFile ini)
        {
            _ini = ini;
            WheelUp = ini.GetDouble("PokerCaption", "MouseWheelUp", 0.5);
            WheelDown = ini.GetDouble("PokerCaption", "MouseWheelDown", 0.5);
            WheelPrPotPostflop = ini.GetBool("PokerCaption", "MouseWheelPrPotPostflop", false);
            WheelPrPotVal = ini.GetDouble("PokerCaption", "MouseWheelPrPotVal", 5);
            WheelInDollars = ini.GetBool("PokerCaption", "WheelInDollars", false);
            WheelRound = ini.GetInt("PokerCaption", "MouseWheelRound", 0);
        }

        public bool Install()
        {
            _ui = SynchronizationContext.Current ?? new System.Windows.Forms.WindowsFormsSynchronizationContext();
            var installed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            new Thread(() => RunHook(installed)) { IsBackground = true, Name = "PokerCaption mouse hook" }.Start();
            if (installed.Task.Wait(1000)) return installed.Task.Result;
            Dispose();
            return false;
        }

        private void RunHook(TaskCompletionSource<bool> installed)
        {
            // Create the thread's message queue before Dispose can post WM_QUIT.
            PeekMessageW(out _, IntPtr.Zero, 0, 0, 0);
            lock (_hookState)
            {
                if (!_disposed)
                {
                    _hookThreadId = NativeMethods.GetCurrentThreadId();
                    _msHook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_MOUSE_LL, _msProc, IntPtr.Zero, 0);
                }
                installed.SetResult(_msHook != IntPtr.Zero);
            }
            if (_msHook == IntPtr.Zero) return;
            try { while (GetMessageW(out _, IntPtr.Zero, 0, 0) > 0) { } }
            finally
            {
                NativeMethods.UnhookWindowsHookEx(_msHook);
                lock (_hookState)
                {
                    _msHook = IntPtr.Zero;
                    _hookThreadId = 0;
                }
            }
        }

        // ---- mouse: solo wheel bet ----
        private IntPtr MsProc(int code, IntPtr wp, IntPtr lp)
        {
            try
            {
                if (!_disposed && code >= 0 && (uint)wp.ToInt64() == 0x020A) // WM_MOUSEWHEEL
                {
                    var mw = Marshal.PtrToStructure<MsllHookstruct>(lp);
                    int delta = (short)((mw.mouseData >> 16) & 0xFFFF);
                    if (delta != 0 && mw.flags == 0 && (delta > 0 ? WheelUp : WheelDown) != 0)
                    {
                        var hit = NativeMethods.WindowFromPoint(mw.pt);
                        _ui.Post(_ =>
                        {
                            if (_disposed || hit == IntPtr.Zero) return;
                            try
                            {
                                var table = _getTableUnderCursor();
                                if (table == hit || NativeMethods.IsChild(table, hit))
                                    HandleWheel(table, delta > 0);
                            }
                            catch (Exception ex) { _log("wheel bet: " + ex.Message); }
                        }, null);
                    }
                }
            }
            catch { }
            return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wp, lp);
        }

        /// <summary>Wheel bet: aggiorna la bet box del tavolo sotto il cursore (Fase 6).</summary>
        private void HandleWheel(IntPtr table, bool up)
        {
            var t = TableService.FindByHwnd(table);
            if (t == null) return;
            bool coords = ClientProfile.Interaction == "coords";
            (float X, float Y) betBox = default;
            bool hasBetBox = ClientProfile.Points.TryGetValue("BetBox", out betBox);
            if (!coords && t.Editor == IntPtr.Zero) return;
            if (coords && !hasBetBox) return;

            double step = up ? WheelUp : -WheelDown;
            if (step == 0) return;

            // % pot postflop se configurato
            bool usePotPct = WheelPrPotPostflop && t.Street != 1;
            double current = coords
                ? TitleParser.ParseMoney(t.BetBoxText ?? "")
                : TitleParser.ParseMoney(TableScanner.GetText(t.Editor, 64));
            double bb = t.Bb > 0 ? t.Bb : 1;

            double target;
            if (usePotPct)
            {
                target = t.Pot * WheelPrPotVal / 100.0;
                if (!up) target = current - t.Pot * WheelPrPotVal / 100.0;
            }
            else if (WheelInDollars)
            {
                target = current + step;
            }
            else
            {
                double curBb = current / bb;
                target = (curBb + step) * bb;
            }

            if (WheelRound > 0)
            {
                double r = WheelRound == 1 ? 1 : (WheelRound == 2 ? 0.5 : 0.1);
                target = Math.Round(target / r) * r;
            }
            if (target < 0) target = 0;
            var txt = target.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            if (coords)
            {
                if (!CoordsEngine.TypeInto(t.Hwnd, betBox.X, betBox.Y, txt)) return;
                t.BetBoxText = txt;
                var v = TitleParser.ParseMoney(txt);
                if (v > 0) t.Bet = v;
            }
            else Input.SetBetBox(t.Editor, txt);
        }

        public void Dispose()
        {
            lock (_hookState)
            {
                _disposed = true;
                if (_hookThreadId != 0)
                    NativeMethods.PostThreadMessageW(_hookThreadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
            }
        }

        [DllImport("user32.dll")]
        private static extern int GetMessageW(out Message msg, IntPtr window, uint first, uint last);
        [DllImport("user32.dll")]
        private static extern bool PeekMessageW(out Message msg, IntPtr window, uint first, uint last, uint remove);

        [StructLayout(LayoutKind.Sequential)]
        private struct MsllHookstruct { public NativeMethods.POINT pt; public uint mouseData, flags, time; public UIntPtr extraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct Message
        {
            public IntPtr Window;
            public uint Id;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public NativeMethods.POINT Point;
            public uint Private;
        }
    }
}
