using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using PokerCaption.Engine;

namespace PokerCaption.Native
{
    /// <summary>Import Win32 necessari (user32/kernel32). Solo x86/x64 Windows.</summary>
    public static class NativeMethods
    {
        // --- finestre ---
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumChildWindows(IntPtr hWnd, EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hWnd, StringBuilder name, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT pt);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] public static extern IntPtr SendMessageGetText(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] public static extern IntPtr SendMessageSetText(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);
        [DllImport("user32.dll")] public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern int GetWindowLongW(IntPtr hWnd, int index);
        [DllImport("user32.dll")] public static extern int SetWindowLongW(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();

        public const uint WM_CLOSE = 0x0010;
        public const uint WM_SYSCOMMAND = 0x0112;
        public const uint WM_APP = 0x8000;
        public const int GWL_STYLE = -16;
        public const int GWL_HWNDPARENT = -8;
        public const int GWL_EXSTYLE = -20;

        // --- hook ---
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr SetWindowsHookExW(int idHook, HookProc proc, IntPtr hMod, uint threadId);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        public const int WH_CALLWNDPROC = 4;
        public const int WH_GETMESSAGE = 3;
        public const int WH_MSGFILTER = -1;
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        // --- memoria condivisa ---
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateFileMappingW(IntPtr hFile, IntPtr attrs, uint protect, uint maxHigh, uint maxLow, string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr OpenFileMappingW(uint access, bool inherit, string name);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr MapViewOfFile(IntPtr hMap, uint access, uint offHigh, uint offLow, UIntPtr bytes);
        [DllImport("kernel32.dll")] public static extern bool UnmapViewOfFile(IntPtr baseAddress);
        [DllImport("kernel32.dll")] public static extern void CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] public static extern void CopyMemory(IntPtr dst, IntPtr src, UIntPtr count);

        public const uint PAGE_READWRITE = 0x04;
        public const uint FILE_MAP_ALL_ACCESS = 0x001F;

        // --- moduli / processi ---
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr LoadLibraryW(string fileName);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] public static extern IntPtr GetProcAddress(IntPtr module, string procName);
        [DllImport("kernel32.dll")] public static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("psapi.dll")] public static extern bool EnumProcesses(uint[] pids, uint cb, out uint used);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool QueryFullProcessImageNameW(IntPtr hProc, uint flags, StringBuilder name, ref uint size);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

        public const uint PROCESS_QUERY_INFORMATION = 0x0400;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint PROCESS_VM_READ = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }    }

    /// <summary>
    /// Identità dei binari e costanti condivise con la DLL nativa.
    /// </summary>
    public static class SharedLayout
    {
        public const string MapName = "Local\\PokerCaption_SM";
        public const ulong MapSize = 0x400000; // 4 MB

        // header
        public const ulong OFF_MAINHWND = 0x74;   // HWND finestra principale
        public const ulong OFF_BUSY     = 0x78;   // flag interlocked refresh
        public const ulong OFF_CMD      = 0x7C;   // comando per il dispatcher della DLL
        public const ulong OFF_IDX      = 0x88;   // indice tavolo del comando (0..41)
        public const ulong OFF_FLAG     = 0x8C;   // byte per-tavolo exe→DLL

        // Comandi del dispatcher
        public const int CMD_PING       = 0x74;   // risponde SendMessage(main, 0x402, 0x65, 0x42)
        public const int CMD_SCAN       = 0x75;   // scan tavolo idx
        public const int CMD_SCAN_A     = 0x77;
        public const int CMD_SCAN_B     = 0x78;
        public const int CMD_SCAN_C     = 0x79;

        // Messaggi privati consegnati al dispatcher dal hook WH_GETMESSAGE.
        public const uint MSG_DISPATCH  = 0x6742;
        public const uint MSG_TABLE_SCAN = 0x6743;

        // tabella tavoli
        public const ulong SLOT_BASE    = 0x80578;
        public const ulong SLOT_PITCH   = 0x2A00; // 107.520 byte per tavolo
        public const int   MAX_TABLES   = 42;     // 0..0x29
        public const ulong SLOT_HWND    = 0x0000; // HWND tavolo (dword 0)
        public const ulong SLOT_FLAGS   = 0x0158; // 0x806d0..: flag hotkey/azione
        public const ulong SLOT_ACTION  = 0x015C; // 0x806d4: azione presente
        public const ulong SLOT_STATE   = 0x26BC; // 0x82c34: enum street/stato
        public const ulong SLOT_SEATS   = 0x2668; // 0x99a*4: array posti (10 × 8 dword)

        // Layout scritto da native/captionHook.c: hwnd, rettangolo, lunghezza testo,
        // testo UTF-16, lunghezza nome nodo e nome UTF-16.
        public const ulong SLOT_TEXTCOUNT = 0x19F8;
        public const ulong SLOT_TEXTS     = 0x1A00;
        public const int   TEXT_ENTRY     = 160;
        public const int   MAX_TEXTS      = 18;
    }

    /// <summary>Accesso tipizzato alla memoria condivisa (creazione e slot tavoli).</summary>
    public sealed class SharedMemory : IDisposable
    {
        private int _scanId = Environment.TickCount;
        public enum ActionHookMode : byte { Default, SkipVisible, SkipAll, ProxyOnly = 4, WidgetOnly }

        public enum CocosScanStatus
        {
            Unreported, ModuleMissing, ExportsMissing, DirectorMissing, SceneMissing, Walked,
            ViewMissing, SceneTypeMismatch, ViewSceneMissing
        }

        private readonly IntPtr _map;
        private readonly IntPtr _view;
        private readonly unsafe byte* _base;

        public unsafe SharedMemory()
        {
            _map = NativeMethods.CreateFileMappingW(new IntPtr(-1), IntPtr.Zero,
                NativeMethods.PAGE_READWRITE,
                (uint)((SharedLayout.MapSize >> 32) & 0xFFFFFFFF),
                (uint)(SharedLayout.MapSize & 0xFFFFFFFF),
                SharedLayout.MapName);
            if (_map == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            _view = NativeMethods.MapViewOfFile(_map, NativeMethods.FILE_MAP_ALL_ACCESS, 0, 0, (UIntPtr)SharedLayout.MapSize);
            if (_view == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            _base = (byte*)_view;
        }

        public IntPtr View => _view;

        public unsafe void SetMainHwnd(IntPtr hwnd) => *(IntPtr*)(_base + SharedLayout.OFF_MAINHWND) = hwnd;

        /// <summary>Scrive comando+indice e posta il trigger al main. Ritorna true se il PostMessage è accettato.</summary>
        public unsafe bool SendCommand(int cmd, int tableIndex, byte flag = 0)
        {
            *(int*)(_base + SharedLayout.OFF_CMD) = cmd;
            *(int*)(_base + SharedLayout.OFF_IDX) = tableIndex;
            *(_base + SharedLayout.OFF_FLAG) = flag;
            var main = *(IntPtr*)(_base + SharedLayout.OFF_MAINHWND);
            // trigger del dispatcher della DLL via PostMessage + WH_GETMESSAGE
            if (main == IntPtr.Zero) return false;
            return NativeMethods.PostMessageW(main, SharedLayout.MSG_DISPATCH, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Comando + indice via PostThreadMessage al THREAD DEL CLIENT:
        /// l'hook WH_GETMESSAGE installato nel client lo vede e il dispatcher gira
        /// nel processo del client, da cui legge lo stato Cocos2d.</summary>
        public unsafe bool SendCommandToThread(uint threadId, int cmd, int tableIndex, byte flag = 0)
        {
            *(int*)(_base + SharedLayout.OFF_CMD) = cmd;
            *(int*)(_base + SharedLayout.OFF_IDX) = tableIndex;
            *(_base + SharedLayout.OFF_FLAG) = flag;
            if (threadId == 0) return false;
            return NativeMethods.PostThreadMessageW(threadId, SharedLayout.MSG_DISPATCH, IntPtr.Zero, IntPtr.Zero);
        }

        public int RequestTableScan(uint threadId, int slot)
        {
            if (threadId == 0 || slot < 0 || slot >= SharedLayout.MAX_TABLES) return 0;
            int request = System.Threading.Interlocked.Increment(ref _scanId);
            if (request == 0) request = System.Threading.Interlocked.Increment(ref _scanId);
            return NativeMethods.PostThreadMessageW(threadId, SharedLayout.MSG_TABLE_SCAN,
                (IntPtr)slot, (IntPtr)request) ? request : 0;
        }

        public unsafe int ReadTableScanId(int slot)
        {
            var p = (byte*)SlotAddress(_base, slot);
            return p == null ? 0 : System.Threading.Volatile.Read(ref *(int*)(p + 0x19DC));
        }

        public unsafe int ReadBoardCards(int slot)
        {
            var p = (byte*)SlotAddress(_base, slot);
            return p == null ? -1 : *(int*)(p + 0x19D8);
        }

        [Flags]
        internal enum OptionButtonTrace { None = 0, Walked = 1, BackNode = 2, BackLabel = 4, WaitingNode = 8, WaitingLabel = 16, LimitReached = 32 }

        internal unsafe (int Scan, OptionButtonTrace Buttons) ReadOptionButtonTrace(int slot)
        {
            var p = (byte*)SlotAddress(_base, slot);
            return p == null ? default : (*(int*)(p + 0x19EC), (OptionButtonTrace)(*(int*)(p + 0x19E0)));
        }

        internal void EnableOptionButtonTrace(int slot, bool enabled) => SetSlotFlag(slot, 0x19D4, enabled ? (byte)1 : (byte)0);

        internal unsafe int ReadActionHookMask(int slot)
        {
            var p = (byte*)SlotAddress(_base, slot);
            return p == null ? 0 : *(int*)(p + ActionHooksStatus);
        }

        private const int ActionHookModeOffset = 0x2654;
        private const int ActionHooksStatus = 0x2658;
        private const int ActionRingCapacity = 64;
        private const int ActionCaptureOffset = 0x27DC;
        private const int ActionTicks = 0x254C;
        private const int ActionTicksVersion = 0x264C;
        private const int ActionSnapshotTick = 0x2650;

        internal readonly record struct SeatActionEvent(
            int Seat, int Cards, int Code, uint? Tick = null, int Sequence = 0);

        internal unsafe List<SeatActionEvent> ReadSeatActionEvents(
            int slot, ref int last, out bool dropped, out uint? snapshotTick) =>
            ReadSeatActionEvents((byte*)SlotAddress(_base, slot), ref last, out dropped, out snapshotTick);

        internal static unsafe List<SeatActionEvent> ReadSeatActionEvents(
            byte* p, ref int last, out bool dropped, out uint? snapshotTick)
        {
            dropped = false;
            snapshotTick = null;
            if (p == null || *(int*)(p + ActionCaptureOffset) != 1) return null;
            int head = *(int*)(p + 0x27D4);
            if (last > head) last = 0;
            bool timed = *(int*)(p + ActionTicksVersion) == 1;
            if (timed) snapshotTick = *(uint*)(p + ActionSnapshotTick);
            var events = new List<SeatActionEvent>();
            if (head - last > ActionRingCapacity)
            {
                dropped = true;
                last = head - ActionRingCapacity;
            }
            while (last < head)
            {
                int sequence = ++last;
                int index = (sequence - 1) % ActionRingCapacity;
                var entry = p + 0x27E0 + index * 8;
                if (System.Threading.Volatile.Read(ref *(int*)entry) != sequence) { dropped = true; continue; }
                int packed = *(int*)(entry + 4);
                uint? tick = timed ? *(uint*)(p + ActionTicks + index * sizeof(uint)) : null;
                if (System.Threading.Volatile.Read(ref *(int*)entry) != sequence) { dropped = true; continue; }
                events.Add(new SeatActionEvent(packed & 0xFF, (packed >> 8) & 0xFF, packed >> 16, tick, sequence));
            }
            return events;
        }

        public static unsafe IntPtr SlotAddress(byte* b, int idx)
        {
            if (idx < 0 || idx >= SharedLayout.MAX_TABLES) return IntPtr.Zero;
            return (IntPtr)(b + SharedLayout.SLOT_BASE + (ulong)idx * SharedLayout.SLOT_PITCH);
        }

        /// <summary>Scrive l'HWND del tavolo nello slot (dword 0).</summary>
        public unsafe void SetTableHwnd(int idx, IntPtr hwnd, ActionHookMode hookMode = ActionHookMode.Default)
        {
            var p = (byte*)SlotAddress(_base, idx);
            if (p == default) return;
            System.Threading.Volatile.Write(ref *(int*)(p + 0x19DC), 0);
            if (*(IntPtr*)p != hwnd)
            {
                *(int*)(p + 0x19D4) = 0;
                *(int*)(p + 0x19E0) = 0;
                *(int*)(p + 0x27D4) = 0;
                *(int*)(p + 0x27D8) = 0;
                *(int*)(p + ActionCaptureOffset) = 0;
                *(int*)(p + ActionTicksVersion) = 0;
                *(int*)(p + ActionHooksStatus) = 0;
            }
            p[ActionHookModeOffset] = (byte)hookMode;
            *(IntPtr*)p = hwnd;
        }

        internal void StopSeatActionCapture(int slot) => SetSlotFlag(slot, ActionCaptureOffset, 0);

        public unsafe IntPtr GetTableHwnd(int idx)
        {
            var p = (byte*)SlotAddress(_base, idx);
            if (p == default) return IntPtr.Zero;
            return *(IntPtr*)p;
        }

        public unsafe void SetSlotFlag(int idx, ulong offset, byte value)
        {
            var p = (byte*)SlotAddress(_base, idx);
            if (p == default) return;
            p[offset] = value;
        }

        /// <summary>Diagnostica: (nodi visitati, testi raccolti) scritti dalla DLL allo scan.</summary>
        public unsafe (int nodes, int texts, int cocosBound, CocosScanStatus status, int viewApi) ReadTableDiag(int idx)
        {
            var p = (byte*)SlotAddress(_base, idx);
            if (p == default) return default;
            return (*(int*)(p + 0x19F0), *(int*)(p + 0x19F4), *(int*)(p + 0x19E8),
                (CocosScanStatus)(*(int*)(p + 0x19EC)), *(int*)(p + 0x19E4));
        }

        /// <summary>Legge i testi delle finestre figlie scritti dalla DLL allo scan
        /// (slot+0x19F8 count, entry 160 B a slot+0x1A00: hwnd,left,top,w,h,textLen,
        /// text[40], nameLen, name[24]). Coordinata top-left relativa al tavolo.</summary>
        public unsafe List<(Rectangle rect, string text, string node, int seat)> ReadTableTexts(int idx)
        {
            var result = new List<(Rectangle, string, string, int)>();
            var p = (byte*)SlotAddress(_base, idx);
            if (p == default) return result;
            int count = *(int*)(p + SharedLayout.SLOT_TEXTCOUNT);
            if (count < 0 || count > SharedLayout.MAX_TEXTS) return result;
            for (int i = 0; i < count; i++)
            {
                var e = p + SharedLayout.SLOT_TEXTS + (ulong)(i * SharedLayout.TEXT_ENTRY);
                int left = *(int*)(e + 4), top = *(int*)(e + 8);
                int w = *(int*)(e + 12), h = *(int*)(e + 16), len = *(int*)(e + 20);
                if (len <= 0) continue;
                len = Math.Min(len, 40);
                var chars = new char[len];
                for (int c = 0; c < len; c++)
                    chars[c] = (char)*((ushort*)(e + 24) + c);
                int nlen = *(int*)(e + 24 + 40 * 2);
                nlen = Math.Min(Math.Max(nlen, 0), 24);
                var nchars = new char[nlen];
                for (int c = 0; c < nlen; c++)
                    nchars[c] = (char)*((ushort*)(e + 28 + 40 * 2) + c);
                int seat = *(int*)(e + 156);
                result.Add((new Rectangle(left, top, w, h), new string(chars), new string(nchars),
                    seat is >= 1 and <= 10 ? seat : 0));
            }
            return result;
        }

        public void Dispose()
        {
            if (_view != IntPtr.Zero) NativeMethods.UnmapViewOfFile(_view);
            if (_map != IntPtr.Zero) NativeMethods.CloseHandle(_map);
        }
    }

    /// <summary>
    /// Caricamento e installazione degli hook Win32.
    /// LoadLibraryW("captionHook.dll") → GetProcAddress("hookProc") →
    /// SetWindowsHookExW(WH_CALLWNDPROC=4, proc, hMod, threadId).
    /// </summary>
    public sealed class HookInstaller : IDisposable
    {
        private IntPtr _module;
        private IntPtr _hookWnd;    // WH_CALLWNDPROC
        private IntPtr _hookMsg;    // WH_GETMESSAGE
        private readonly string _dllPath;

        public HookInstaller(string dllPath) { _dllPath = dllPath; }

        /// <summary>
        /// Installa gli hook Win32 per messaggi inviati e accodati.
        /// WH_CALLWNDPROC (monitoraggio messaggi inviati) e WH_GETMESSAGE
        /// (inclusi i comandi del dispatcher).
        /// </summary>
        public bool Install(int threadId = 0, bool messagesOnly = false)
        {
            if (_hookWnd != IntPtr.Zero || _hookMsg != IntPtr.Zero) return true;
            if (messagesOnly && threadId == 0) throw new ArgumentOutOfRangeException(nameof(threadId));
            _module = NativeMethods.LoadLibraryW(_dllPath);
            if (_module == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                    "LoadLibraryW fallita per " + _dllPath);

            var proc = NativeMethods.GetProcAddress(_module, "hookProc");
            if (proc == IntPtr.Zero)
                throw new EntryPointNotFoundException("export hookProc");
            var procMsg = NativeMethods.GetProcAddress(_module, "hookGetMsgProc");
            if (procMsg == IntPtr.Zero)
                throw new EntryPointNotFoundException("export hookGetMsgProc");

            if (!messagesOnly)
                _hookWnd = NativeMethods.SetWindowsHookExW(NativeMethods.WH_CALLWNDPROC,
                    (NativeMethods.HookProc)Marshal.GetDelegateForFunctionPointer(proc, typeof(NativeMethods.HookProc)),
                    _module, (uint)threadId);
            _hookMsg = NativeMethods.SetWindowsHookExW(NativeMethods.WH_GETMESSAGE,
                (NativeMethods.HookProc)Marshal.GetDelegateForFunctionPointer(procMsg, typeof(NativeMethods.HookProc)),
                _module, (uint)threadId);
            return (messagesOnly || _hookWnd != IntPtr.Zero) && _hookMsg != IntPtr.Zero;
        }

        /// <summary>HMODULE caricato (per GetProcAddress di export aggiuntivi).</summary>
        public IntPtr Module => _module;

        /// <summary>Chiama un export int(void) della DLL (diagnostica).</summary>
        public int CallIntExport(string name)
        {
            var addr = NativeMethods.GetProcAddress(_module, name);
            if (addr == IntPtr.Zero) throw new EntryPointNotFoundException(name);
            var fn = (FuncInt)Marshal.GetDelegateForFunctionPointer(addr, typeof(FuncInt));
            return fn();
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int FuncInt();

        public void Dispose()
        {
            if (_hookWnd != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_hookWnd); _hookWnd = IntPtr.Zero; }
            if (_hookMsg != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_hookMsg); _hookMsg = IntPtr.Zero; }
            if (_module != IntPtr.Zero) { NativeMethods.FreeLibrary(_module); _module = IntPtr.Zero; }
        }
    }

    /// <summary>
    /// Localizzazione dei client PokerStars: POKERSTARS.EXE e
    /// POKERSTARSONBETFAIR.EXE, più varianti whitelabel e fallback via finestre.
    /// Usa PROCESS_QUERY_LIMITED_INFORMATION per funzionare anche quando il
    /// client è elevato e l'app no.
    /// </summary>
    public static class ClientLocator
    {
        public static readonly string[] ClientProcessNames =
        {
            "POKERSTARS.EXE", "POKERSTARSONBETFAIR.EXE"
        };

        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public sealed class Client
        {
            public uint ProcessId;
            public string ExePath;
        }

        /// <summary>True se la classe finestra appartiene al client (profilo corrente o prefisso PokerStars).</summary>
        private static bool IsClientWindowClass(string cls)
        {
            if (string.IsNullOrEmpty(cls)) return false;
            if (cls == ClientProfile.TableClass || cls == ClientProfile.FrameClass ||
                cls == ClientProfile.NoteEditorClass || cls == ClientProfile.ListClass) return true;
            return cls.StartsWith("PokerStars", StringComparison.OrdinalIgnoreCase);
        }

        public static System.Collections.Generic.List<Client> FindClients()
        {
            var result = new System.Collections.Generic.List<Client>();
            var seen = new System.Collections.Generic.HashSet<uint>();
            var pids = new uint[4096];
            NativeMethods.EnumProcesses(pids, (uint)(pids.Length * sizeof(uint)), out var used);
            int count = (int)(used / sizeof(uint));
            var sb = new StringBuilder(1024);
            for (int i = 0; i < count; i++)
            {
                // limitato: funziona anche verso processi elevati
                IntPtr h = NativeMethods.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pids[i]);
                if (h == IntPtr.Zero) continue;
                try
                {
                    uint size = 1024;
                    sb.Clear();
                    if (NativeMethods.QueryFullProcessImageNameW(h, 0, sb, ref size))
                    {
                        var name = System.IO.Path.GetFileName(sb.ToString());
                        if (ClientProfile.IsClientProcess(name) && seen.Add(pids[i]))
                            result.Add(new Client { ProcessId = pids[i], ExePath = sb.ToString() });
                    }
                }
                finally { NativeMethods.CloseHandle(h); }
            }

            // fallback: pidi dalle finestre del client (cattura skin con exe sconosciuto)
            NativeMethods.EnumWindows((h, l) =>
            {
                if (!NativeMethods.IsWindowVisible(h)) return true;
                var cls = PokerWindows.GetClassName(h);
                if (!IsClientWindowClass(cls)) return true;
                NativeMethods.GetWindowThreadProcessId(h, out var pid);
                if (pid != 0 && seen.Add(pid))
                    result.Add(new Client { ProcessId = pid, ExePath = "(via finestra " + cls + ")" });
                return true;
            }, IntPtr.Zero);

            return result;
        }
    }

    /// <summary>
    /// Enumerazione delle finestre PokerStars per class name
    /// </summary>
    public static class PokerWindows
    {
        public const string TableFrameClass = "PokerStarsTableFrameClass";
        public const string TablePanelClass = "PokerStarsTablePanelWnd";
        public const string ButtonClass = "PokerStarsButtonClass";
        public const string SliderClass = "PokerStarsSliderClass";
        public const string SliderEditorClass = "PokerStarsSliderEditorClass";
        public const string FrameClass = "PokerStarsFrameClass";
        public const string ListClass = "PokerStarsListClass";

        public static string GetClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(64);
            return NativeMethods.GetClassNameW(hwnd, sb, 64) > 0 ? sb.ToString() : null;
        }

        public static string GetWindowText(IntPtr hwnd)
        {
            var sb = new StringBuilder(512);
            return NativeMethods.GetWindowTextW(hwnd, sb, 512) > 0 ? sb.ToString() : null;
        }

        public static bool IsTable(IntPtr hwnd) => GetClassName(hwnd) == TableFrameClass;

        /// <summary>Trova tutte le finestre tavolo visibili (top-level).</summary>
        public static System.Collections.Generic.List<IntPtr> FindTables()
        {
            var tables = new System.Collections.Generic.List<IntPtr>();
            NativeMethods.EnumWindows((h, l) =>
            {
                if (NativeMethods.IsWindowVisible(h) && IsTable(h)) tables.Add(h);
                return true;
            }, IntPtr.Zero);
            return tables;
        }
    }
}
