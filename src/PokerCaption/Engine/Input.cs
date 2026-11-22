using System;
using System.Runtime.InteropServices;
using PokerCaption.Native;

namespace PokerCaption.Engine
{
    /// <summary>
    /// Input sintetico verso i controlli PokerStars (controlli custom, non Win32
    /// standard): WM_SETTEXT per la bet box, messaggi mouse per i click, Enter
    /// via WM_KEYDOWN. Il client può anche richiedere click reali (SendInput)
    /// per i controlli che validano la posizione del cursore.
    /// </summary>
    public static class Input
    {
        public const uint WM_KEYDOWN = 0x0100;
        public const uint WM_KEYUP = 0x0101;
        public const uint WM_CHAR = 0x0102;
        public const uint WM_LBUTTONDOWN = 0x0201;
        public const uint WM_LBUTTONUP = 0x0202;
        public const uint MK_LBUTTON = 0x0001;

        /// <summary>Imposta il testo della bet box (PokerStarsSliderEditorClass).</summary>
        public static bool SetBetBox(IntPtr editor, string text)
        {
            if (editor == IntPtr.Zero || text == null) return false;
            return NativeMethods.SendMessageSetText(editor, WM_SETTEXT, IntPtr.Zero, text) != IntPtr.Zero;
        }

        /// <summary>Conferma la bet box con Enter (WM_KEYDOWN VK_RETURN).</summary>
        public static void PressEnter(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            NativeMethods.PostMessageW(hwnd, WM_KEYDOWN, (IntPtr)0x0D, unchecked((IntPtr)0x001C0001));
            NativeMethods.PostMessageW(hwnd, WM_KEYUP, (IntPtr)0x0D, unchecked((IntPtr)0xC01C0001));
        }

        /// <summary>Click sintetico su un controllo (coordinate client del controllo).</summary>
        public static bool Click(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            NativeMethods.GetWindowRect(hwnd, out var r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            return ClickAt(hwnd, w / 2, h / 2);
        }

        public static bool ClickAt(IntPtr hwnd, int x, int y)
        {
            if (hwnd == IntPtr.Zero) return false;
            var lp = (IntPtr)((y << 16) | (x & 0xFFFF));
            NativeMethods.PostMessageW(hwnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lp);
            NativeMethods.PostMessageW(hwnd, WM_LBUTTONUP, IntPtr.Zero, lp);
            return true;
        }

        /// <summary>WM_SETTEXT (usato anche per finestre non standard).</summary>
        public const uint WM_SETTEXT = 0x000C;
    }
}
