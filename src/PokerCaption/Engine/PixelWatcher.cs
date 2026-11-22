using System;
using System.Collections.Generic;
using System.Drawing;
using PokerCaption.Config;

namespace PokerCaption.Engine
{
    /// <summary>
    /// Rilevamento heroAct via pixel per i client renderizzati (Sisal/Aurora).
    /// Due segnali (entrare in OR):
    ///  1. pannello azione in alto a sinistra ("Call 45 BB") → testo accent
    ///     verde/teal su scuro, regione [Sisal] ActionPanelRect
    ///  2. pulsanti lime in basso a destra (Check/Raise/Pot/Max) → regione
    ///     [Sisal] ButtonsRect; visibili solo quando tocca al hero
    /// Config: ActionPanelRect=x,y,w,h | MinAccentPixels | ButtonsRect | MinButtonPixels
    /// (tutte le regioni sono normalizzate 0..1).
    /// </summary>
    public sealed class PixelWatcher
    {
        public bool Enabled = true;
        public RectangleF ActionPanelRect = new(0.05f, 0.20f, 0.23f, 0.13f);
        public int MinAccentPixels = 25;
        public RectangleF ButtonsRect = new(0.60f, 0.88f, 0.40f, 0.12f);
        public int MinButtonPixels = 150;
        // pannello hero (in basso al centro): testo BB verde = hero in mano
        public RectangleF HeroRect = new(0.39f, 0.68f, 0.24f, 0.09f);
        public int MinHeroPixels = 20;

        private readonly Dictionary<IntPtr, int> _lastAccent = new();

        public void Load(IniFile ini)
        {
            Enabled = ini.GetBool("Sisal", "PixelHero", true);
            ActionPanelRect = ParseRect(ini.Get("Sisal", "ActionPanelRect"), ActionPanelRect);
            ButtonsRect = ParseRect(ini.Get("Sisal", "ButtonsRect"), ButtonsRect);
            HeroRect = ParseRect(ini.Get("Sisal", "HeroRect"), HeroRect);
            MinAccentPixels = ini.GetInt("Sisal", "MinAccentPixels", 25);
            MinButtonPixels = ini.GetInt("Sisal", "MinButtonPixels", 150);
            MinHeroPixels = ini.GetInt("Sisal", "MinHeroPixels", 20);
        }

        private static RectangleF ParseRect(string s, RectangleF def)
        {
            if (string.IsNullOrWhiteSpace(s)) return def;
            var p = s.Split(',');
            if (p.Length != 4) return def;
            try
            {
                return new RectangleF(
                    float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture));
            }
            catch { return def; }
        }

        /// <summary>Misura accent + lime + hero-in-hand con un'unica scansione LockBits (veloce).</summary>
        public (int accent, int lime, int heroIn) Measure(TableInfo t)
        {
            if (t == null) return (-1, -1, -1);
            using var bmp = CoordsEngine.Capture(t.Hwnd);
            if (bmp == null) return (-1, -1, -1);
            int w = bmp.Width, h = bmp.Height;
            var rect = new Rectangle(0, 0, w, h);
            var bd = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int accent = 0, lime = 0, heroIn = 0;
            unsafe
            {
                byte* basePtr = (byte*)bd.Scan0;
                int stride = bd.Stride;
                int minY = Math.Max(0, (int)Math.Min(ActionPanelRect.Y * h, Math.Min(ButtonsRect.Y * h, HeroRect.Y * h)));
                int maxY = Math.Min(h, (int)Math.Max((ActionPanelRect.Y + ActionPanelRect.Height) * h, Math.Max((ButtonsRect.Y + ButtonsRect.Height) * h, (HeroRect.Y + HeroRect.Height) * h)));
                for (int y = minY; y < maxY; y += 2)
                {
                    var row = basePtr + y * stride;
                    for (int x = 0; x < w; x += 2)
                    {
                        var b = row[x * 4];
                        var g = row[x * 4 + 1];
                        var r = row[x * 4 + 2];
                        bool inAccent = x >= ActionPanelRect.X * w && x < (ActionPanelRect.X + ActionPanelRect.Width) * w && y >= ActionPanelRect.Y * h && y < (ActionPanelRect.Y + ActionPanelRect.Height) * h;
                        bool inButtons = x >= ButtonsRect.X * w && x < (ButtonsRect.X + ButtonsRect.Width) * w && y >= ButtonsRect.Y * h && y < (ButtonsRect.Y + ButtonsRect.Height) * h;
                        bool inHero = x >= HeroRect.X * w && x < (HeroRect.X + HeroRect.Width) * w && y >= HeroRect.Y * h && y < (HeroRect.Y + HeroRect.Height) * h;
                        if (!inAccent && !inButtons && !inHero) continue;
                        if (inButtons && r > 170 && g > 190 && b < 110) lime++;
                        if (inAccent && ((g > r + 30 && g > b + 20) || (g > r + 30 && b > r + 30 && b > 120))) accent++;
                        if (inHero && IsHeroPixel(r, g, b)) heroIn++;
                    }
                }
            }
            bmp.UnlockBits(bd);
            _lastAccent[t.Hwnd] = accent + lime + heroIn;
            return (accent, lime, heroIn);
        }

        internal int MeasureVisibleHero(TableInfo table)
        {
            if (!Enabled) return -1;
            using var bitmap = CoordsEngine.CaptureVisibleRegion(table.Hwnd, HeroRect);
            if (bitmap == null) return -1;

            int count = 0;
            for (int y = 0; y < bitmap.Height; y += 2)
            for (int x = 0; x < bitmap.Width; x += 2)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (IsHeroPixel(pixel.R, pixel.G, pixel.B)) count++;
            }
            return count;
        }

        private static bool IsHeroPixel(byte r, byte g, byte b) => g > r + 25 && g > b + 25 && g > 90;

        /// <summary>True quando pannello azione (alto-sx) o pulsanti lime (basso-dx) sono visibili.</summary>
        public bool? HeroAct(TableInfo t)
        {
            if (!Enabled) return null;
            var (accent, lime, _) = Measure(t);
            if (accent < 0) return null;
            return accent >= MinAccentPixels || lime >= MinButtonPixels;
        }

        /// <summary>True quando il hero è distribuito in mano (pannello hero con stack verde).</summary>
        public bool? HeroInHand(TableInfo t)
        {
            if (!Enabled) return null;
            var (_, _, heroIn) = Measure(t);
            if (heroIn < 0) return null;
            return heroIn >= MinHeroPixels;
        }

        public int LastAccent(IntPtr hwnd) => _lastAccent.TryGetValue(hwnd, out var v) ? v : -1;
    }
}
