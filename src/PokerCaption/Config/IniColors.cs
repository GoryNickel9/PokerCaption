using System.Drawing;

namespace PokerCaption.Config
{
    /// <summary>Conversione colori VCL (TColor BGR + costanti sistema) → System.Drawing.Color.</summary>
    internal static class IniColors
    {
        /// <summary>Converte un intero TColor (BGR o costante sistema) in Color.</summary>
        public static Color FromInt(long c)
        {
            if (c == -1) return Color.Empty;
            if ((c & 0xFF000000L) == 0xFF000000L) return Color.Transparent;
            if ((c & 0x80000000L) != 0)
            {
                switch ((int)(c & 0xFF))
                {
                    case 0: return SystemColors.ScrollBar;
                    case 1: return SystemColors.Desktop;
                    case 2: return SystemColors.ActiveCaption;
                    case 3: return SystemColors.InactiveCaption;
                    case 4: return SystemColors.Menu;
                    case 5: return SystemColors.Window;
                    case 6: return SystemColors.WindowFrame;
                    case 7: return SystemColors.MenuText;
                    case 8: return SystemColors.WindowText;
                    case 9: return SystemColors.ActiveCaptionText;
                    case 10: return SystemColors.ActiveBorder;
                    case 11: return SystemColors.InactiveBorder;
                    case 12: return SystemColors.AppWorkspace;
                    case 13: return SystemColors.Highlight;
                    case 14: return SystemColors.HighlightText;
                    case 15: return SystemColors.Control;
                    case 16: return SystemColors.ControlDark;
                    case 17: return SystemColors.GrayText;
                    case 18: return SystemColors.ControlText;
                    case 19: return SystemColors.InactiveCaptionText;
                    case 20: return SystemColors.ControlLight;
                    case 21: return SystemColors.ControlDarkDark;
                    case 22: return SystemColors.ControlLightLight;
                    case 23: return SystemColors.InfoText;
                    case 24: return SystemColors.Info;
                    default: return Color.Empty;
                }
            }
            int bgr = (int)(c & 0xFFFFFF);
            return Color.FromArgb(bgr & 0xFF, (bgr >> 8) & 0xFF, (bgr >> 16) & 0xFF);
        }

    }
}
