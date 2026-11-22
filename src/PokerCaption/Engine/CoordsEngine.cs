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
        public static bool TypeInto(IntPtr hwnd, double nx, double ny, string text)
        {
            if (!Click(hwnd, nx, ny)) return false;
            Thread.Sleep(50);
            if (NativeMethods.GetForegroundWindow() != hwnd || IsHungAppWindow(hwnd)) return false;
            SendKey(0xA2, true);  // Ctrl down
            SendKey(0x41, true);  // A
            SendKey(0x41, false);
            SendKey(0xA2, false); // Ctrl up
            foreach (var ch in text) SendChar(ch);
            return true;
        }

        private static void SendChar(char c)
        {
            var down = new INPUT { type = 1 }; // INPUT_KEYBOARD
            down.U.ki.wScan = c;
            down.U.ki.dwFlags = 0x0004;       // KEYEVENTF_UNICODE
            var up = down;
            up.U.ki.dwFlags = 0x0004 | 0x0002; // | KEYEVENTF_KEYUP
            var arr = new INPUT[] { down, up };
            SendInput(2, arr, Marshal.SizeOf<INPUT>());
        }

        private static void SendKey(uint vk, bool down)
        {
            var inp = new INPUT { type = 1 };
            inp.U.ki.wVk = (ushort)vk;
            inp.U.ki.dwFlags = down ? 0u : 0x0002;
            SendInput(1, new INPUT[] { inp }, Marshal.SizeOf<INPUT>());
        }

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

        private static bool TryPoint(IntPtr hwnd, double nx, double ny, out NativeMethods.POINT point)
        {
            point = default;
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || IsHungAppWindow(hwnd)) return false;
            if (IsIconic(hwnd)) ShowWindowAsync(hwnd, 9); // SW_RESTORE
            if (NativeMethods.GetForegroundWindow() != hwnd)
            {
                if (!SetForegroundWindow(hwnd)) return false;
                Thread.Sleep(60);
            }
            if (NativeMethods.GetForegroundWindow() != hwnd ||
                !NativeMethods.GetWindowRect(hwnd, out var rect)) return false;
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return false;
            point.X = rect.Left + (int)(nx * width);
            point.Y = rect.Top + (int)(ny * height);
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
