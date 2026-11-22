using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PokerCaption.Native;

namespace PokerCaption.Engine
{
    /// <summary>
    /// Interazione a coordinate per i client renderizzati (Sisal: GLFW/OpenGL,
    /// senza controlli Win32 figli): click e wheel con SendInput, digitazione,
    /// cattura screenshot della finestra per calibrazione/pixel-watch.
    /// Le coordinate sono normalizzate (0..1) rispetto alla finestra, così la
    /// calibrazione regola a qualunque dimensione del tavolo.
    /// </summary>
    public static class CoordsEngine
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_WHEEL = 0x0800;

        public static bool Click(IntPtr hwnd, double nx, double ny)
        {
            if (!TryPoint(hwnd, nx, ny, out var point) || !NativeMethods.GetCursorPos(out var original) ||
                !SetCursorPos(point.X, point.Y)) return false;
            try
            {
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(30);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                return true;
            }
            finally { RestoreCursor(point, original); }
        }

        public static void Wheel(IntPtr hwnd, double nx, double ny, int delta)
        {
            if (!TryPoint(hwnd, nx, ny, out var point) || !NativeMethods.GetCursorPos(out var original) ||
                !SetCursorPos(point.X, point.Y)) return;
            try { mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta), UIntPtr.Zero); }
            finally { RestoreCursor(point, original); }
        }

        /// <summary>Sostituisce il testo nel campo selezionato.</summary>
        public static bool TypeInto(IntPtr hwnd, double nx, double ny, string text, bool foregroundOnly = false)
        {
            if (foregroundOnly ? !FocusBetBox(hwnd, nx, ny) : !Click(hwnd, nx, ny)) return false;
            Thread.Sleep(50);
            if (NativeMethods.GetForegroundWindow() != hwnd || IsHungAppWindow(hwnd) || NativeMethods.IsInMoveSize(hwnd) ||
                (foregroundOnly && (!NativeMethods.GetCursorPos(out var cursor) || !OwnsPoint(hwnd, cursor))))
                return false;
            const ushort endScan = 0x4F, backspaceScan = 0x0E;
            const uint extendedKey = 1, keyUp = 2, unicode = 4, scanCode = 8;
            const int maximumAmountLength = 16;
            // The rendered client uses scan codes for editing keys and Unicode events for digits.
            var inputs = new List<INPUT>(2 + maximumAmountLength * 2 + text.Length * 2)
            { Keyboard(endScan, scanCode | extendedKey), Keyboard(endScan, scanCode | extendedKey | keyUp) };
            for (int i = 0; i < maximumAmountLength; i++)
            {
                inputs.Add(Keyboard(backspaceScan, scanCode));
                inputs.Add(Keyboard(backspaceScan, scanCode | keyUp));
            }
            foreach (char ch in text)
            {
                inputs.Add(Keyboard(scan: ch, flags: unicode));
                inputs.Add(Keyboard(scan: ch, flags: unicode | keyUp));
            }
            uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), INPUT.Size);
            if (sent == inputs.Count) return true;
            if (sent % 2 != 0)
                SendInput(1, [inputs[(int)sent]], INPUT.Size);
            return false;
        }

        private static bool FocusBetBox(IntPtr hwnd, double nx, double ny)
        {
            if (!TryPoint(hwnd, nx, ny, out var point, foregroundOnly: true) ||
                !NativeMethods.GetCursorPos(out var original) || !OwnsPoint(hwnd, original) ||
                NativeMethods.GetForegroundWindow() != hwnd || NativeMethods.IsInMoveSize(hwnd))
                return false;

            // Queue focus and restoration together. A physical input event cannot enter the batch.
            var screen = SystemInformation.VirtualScreen;
            INPUT[] inputs =
            [
                MouseMove(point, screen),
                MouseButton(MOUSEEVENTF_LEFTDOWN),
                MouseButton(MOUSEEVENTF_LEFTUP),
                MouseMove(original, screen),
            ];
            if (NativeMethods.GetForegroundWindow() != hwnd || !OwnsPoint(hwnd, point) ||
                !NativeMethods.GetCursorPos(out var current) || current.X != original.X || current.Y != original.Y)
                return false;
            uint sent = SendInput((uint)inputs.Length, inputs, INPUT.Size);
            if (sent == inputs.Length) return true;
            if (sent == 2) SendInput(1, [inputs[2]], INPUT.Size);
            if (sent > 0) RestoreCursor(point, original);
            return false;
        }

        private static INPUT MouseMove(NativeMethods.POINT point, Rectangle screen)
        {
            const uint absoluteVirtualMove = 0xE001; // ABSOLUTE | VIRTUALDESK | MOVE_NOCOALESCE | MOVE
            return new INPUT
            {
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = NormalizeMouseCoordinate(point.X, screen.Left, screen.Width),
                        dy = NormalizeMouseCoordinate(point.Y, screen.Top, screen.Height),
                        dwFlags = absoluteVirtualMove
                    }
                }
            };
        }

        internal static int NormalizeMouseCoordinate(int point, int origin, int extent) =>
            (int)Math.Clamp(((long)(point - origin) * 65536 + extent - 1) / extent, 0, 65535);

        private static INPUT MouseButton(uint flags) =>
            new() { U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags } } };

        private static INPUT Keyboard(ushort scan, uint flags) => new()
        {
            type = 1,
            U = new InputUnion { ki = new KEYBDINPUT { wScan = scan, dwFlags = flags } }
        };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
            public static int Size => Marshal.SizeOf<INPUT>();
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr extraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr extraInfo; }

        private static bool TryPoint(IntPtr hwnd, double nx, double ny, out NativeMethods.POINT point, bool foregroundOnly = false)
        {
            point = default;
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || IsHungAppWindow(hwnd)) return false;
            if (foregroundOnly)
            {
                if (IsIconic(hwnd)) return false;
            }
            else
            {
                if (IsIconic(hwnd)) ShowWindowAsync(hwnd, 9); // SW_RESTORE
                if (NativeMethods.GetForegroundWindow() != hwnd)
                {
                    if (!SetForegroundWindow(hwnd)) return false;
                    Thread.Sleep(60);
                }
            }
            if (NativeMethods.GetForegroundWindow() != hwnd ||
                !NativeMethods.GetWindowRect(hwnd, out var rect)) return false;
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return false;
            point.X = rect.Left + (int)(nx * width);
            point.Y = rect.Top + (int)(ny * height);
            return OwnsPoint(hwnd, point);
        }

        private static bool OwnsPoint(IntPtr hwnd, NativeMethods.POINT point)
        {
            var target = NativeMethods.WindowFromPoint(point);
            return target == hwnd || NativeMethods.IsChild(hwnd, target);
        }

        private static void RestoreCursor(NativeMethods.POINT point, NativeMethods.POINT original)
        {
            if (NativeMethods.GetCursorPos(out var current) && current.X == point.X && current.Y == point.Y)
                SetCursorPos(original.X, original.Y);
        }

        /// <summary>
        /// Cattura il contenuto della finestra (PrintWindow RENDERFULLCONTENT,
        /// funziona anche su finestre OpenGL composte; fallback: copia dallo schermo).
        /// </summary>
        public static Bitmap Capture(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return null;
            NativeMethods.RECT r = new();
            NativeMethods.GetWindowRect(hwnd, out r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0) return null;

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            var hdc = g.GetHdc();
            bool ok = PrintWindow(hwnd, hdc, 2 /*PW_RENDERFULLCONTENT*/);
            g.ReleaseHdc(hdc);
            if (!ok || IsBlank(bmp))
            {
                // fallback: copia dallo schermo (la finestra deve essere visibile)
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h));
            }
            return bmp;
        }

        internal static Bitmap CaptureVisibleRegion(IntPtr hwnd, RectangleF region)
        {
            if (IsIconic(hwnd) || !NativeMethods.IsWindowVisible(hwnd) ||
                !NativeMethods.GetWindowRect(hwnd, out var window)) return null;

            region = RectangleF.Intersect(region, new RectangleF(0, 0, 1, 1));
            int width = window.Right - window.Left, height = window.Bottom - window.Top;
            var bounds = Rectangle.FromLTRB(
                window.Left + (int)(region.Left * width), window.Top + (int)(region.Top * height),
                window.Left + (int)(region.Right * width), window.Top + (int)(region.Bottom * height));
            if (bounds.Width <= 0 || bounds.Height <= 0 || !SystemInformation.VirtualScreen.Contains(bounds))
                return null;

            // Avoid counting a hand from another window covering the sampled region.
            foreach (int x in new[] { bounds.Left, bounds.Left + bounds.Width / 2, bounds.Right - 1 })
            foreach (int y in new[] { bounds.Top, bounds.Top + bounds.Height / 2, bounds.Bottom - 1 })
            {
                var target = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = x, Y = y });
                if (target != hwnd && !NativeMethods.IsChild(hwnd, target)) return null;
            }

            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            try
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                return bitmap;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                bitmap.Dispose();
                return null;
            }
        }

        private static bool IsBlank(Bitmap b)
        {
            // campione: se >95% dei pixel campionati è uguale, è vuota
            int same = 0, tot = 0;
            for (int y = 0; y < b.Height; y += 16)
                for (int x = 0; x < b.Width; x += 16)
                {
                    tot++;
                    if (b.GetPixel(x, y).ToArgb() == b.GetPixel(0, 0).ToArgb()) same++;
                }
            return tot > 0 && same * 20 > tot * 19;
        }

        public static void SavePng(Bitmap b, string path)
        {
            if (b == null) return;
            b.Save(path, ImageFormat.Png);
        }

        /// <summary>Hash di una regione (per pixel-watch: rileva cambiamenti).</summary>
        public static long RegionHash(Bitmap b, RectangleF norm)
        {
            if (b == null) return 0;
            var rect = new Rectangle(
                (int)(norm.X * b.Width), (int)(norm.Y * b.Height),
                Math.Max(1, (int)(norm.Width * b.Width)), Math.Max(1, (int)(norm.Height * b.Height)));
            ulong hash = 14695981039346656037;
            for (int y = rect.Y; y < rect.Y + rect.Height && y < b.Height; y += 4)
                for (int x = rect.X; x < rect.X + rect.Width && x < b.Width; x += 4)
                {
                    var p = (uint)b.GetPixel(x, y).ToArgb();
                    hash = (hash ^ p) * 1099511628211;
                }
            return unchecked((long)hash);
        }
    }
}
