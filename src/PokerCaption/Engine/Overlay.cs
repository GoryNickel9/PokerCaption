using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PokerCaption.Native;

namespace PokerCaption.Engine
{
    /// <summary>
    /// Finestra overlay (Fase 7): borderless, always-on-top, no-activate,
    /// click-through opzionale (WS_EX_TRANSPARENT), trasparenza per color-key.
    /// </summary>
    public class OverlayWindow : Form
    {
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOOLWINDOW = 0x80;
        private static readonly Color KeyColor = Color.FromArgb(1, 2, 3);
        internal static Color Key => KeyColor;

        public bool ClickThrough { get; }
        private readonly IntPtr _tableOwner;

        public OverlayWindow(bool clickThrough, IntPtr tableOwner = default)
        {
            ClickThrough = clickThrough;
            _tableOwner = tableOwner;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = KeyColor;
            TransparencyKey = KeyColor;
            TopMost = tableOwner == IntPtr.Zero;
            ShowInTaskbar = false;
            DoubleBuffered = true;
        }

        protected override bool ShowWithoutActivation => true;

        internal void ShowOwned()
        {
            if (!Visible) Show(new TableHandle(_tableOwner));
        }

        private sealed record TableHandle(IntPtr Handle) : IWin32Window;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                if (ClickThrough) cp.ExStyle |= WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => false;
    }

    /// <summary>Un pannello di testo overlay con per-pixel alpha (UpdateLayeredWindow):
    /// sbloccato → tutta la superficie è cliccabile (drag); bloccato → solo il testo
    /// è "solido" e il resto è click-through.</summary>
    public sealed class HudTextPanel : OverlayWindow
    {
        public Color BackColorOverride = Color.Empty;

        /// <summary>True con 'Lock HUD' deselezionato: il pannello si trascina col mouse.</summary>
        public bool Draggable;
        /// <summary>Rilascio dopo un drag: coordinate schermo del pannello.</summary>
        public event Action<int, int> DraggedTo;
        private MouseButtons _dragButton;
        private Point _dragCursor, _dragOrigin;
        public bool Dragging => _dragButton != MouseButtons.None;

        public HudTextPanel(bool clickThrough, IntPtr tableOwner = default) : base(clickThrough, tableOwner)
        {
            if (!clickThrough)
            {
                /* superficie cliccabile: un velo quasi invisibile rende l'intero
                   pannello hit-testabile senza color key (che faceva passare i click
                   attraverso il fondo trasparente) */
                Draggable = true;
            }
            /* ULW (per-pixel) ed SLWA (color key di WinForms) sono esclusivi:
               in modalità ULW il color key va tolto */
            TransparencyKey = Color.Empty;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr dst, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION bf, int dwFlags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);
        private struct POINT { public int X, Y; }
        private struct SIZE { public int X, Y; }
        private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        protected override void OnPaintBackground(PaintEventArgs e) { /* la superficie è ULW */ }
        protected override void OnPaint(PaintEventArgs e) { /* idem */ }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Draggable && !Dragging && e.Button is MouseButtons.Left or MouseButtons.Right)
            {
                _dragCursor = PointToScreen(e.Location);
                _dragOrigin = Location;
                _dragButton = e.Button;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (Dragging)
            {
                var pt = PointToScreen(e.Location);
                MoveTo(new Rectangle(_dragOrigin.X + pt.X - _dragCursor.X,
                    _dragOrigin.Y + pt.Y - _dragCursor.Y, Width, Height));
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool moved = Dragging && Location != _dragOrigin;
            if (Dragging && e.Button == _dragButton)
            {
                _dragButton = MouseButtons.None;
                Capture = false;
                if (moved) DraggedTo?.Invoke(Left, Top);
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture) _dragButton = MouseButtons.None;
            base.OnMouseCaptureChanged(e);
        }

        private void ApplyLayered(Bitmap bmp)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
            IntPtr old = SelectObject(memDc, hBmp);
            var dst = new POINT { X = Left, Y = Top };
            var sz = new SIZE { X = bmp.Width, Y = bmp.Height };
            var src = new POINT { X = 0, Y = 0 };
            var bf = new BLENDFUNCTION { BlendOp = 0 /*AC_SRC_OVER*/, SourceConstantAlpha = 255, AlphaFormat = 1 /*AC_SRC_ALPHA*/ };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref sz, memDc, ref src, 0, ref bf, 2 /*ULW_ALPHA*/);
            SelectObject(memDc, old);
            DeleteObject(hBmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }

        /// <summary>Misura e disegna le righe di testo sulla superficie del pannello.</summary>
        public void SetRuns(List<(string text, Color color, Color back, int fontSize, bool bold)> runs, Padding pad)
        {
            /* misura: larghezza/altezza necessarie */
            using var measure = new Bitmap(1, 1);
            using var mg = Graphics.FromImage(measure);
            var fonts = new List<Font>();
            var placed = new List<(string text, Font font, Color color, Color back, float x, float y, float w, float h)>();
            float y = pad.Top, maxW = 0;
            foreach (var r in runs)
            {
                if (string.IsNullOrEmpty(r.text)) continue;
                var font = new Font("Tahoma", r.fontSize <= 0 ? 9 : r.fontSize, r.bold ? FontStyle.Bold : FontStyle.Regular);
                fonts.Add(font);
                var sz = mg.MeasureString(r.text, font);
                float rw = sz.Width + 4, rh = sz.Height + 1;
                float rx = pad.Left;
                placed.Add((r.text, font, r.color, r.back, rx, y, rw, rh));
                maxW = Math.Max(maxW, rx + rw + pad.Right);
                y += rh;
            }
            int w = (int)Math.Ceiling(Math.Max(maxW + pad.Horizontal, 24));
            int h = (int)Math.Ceiling(y + pad.Vertical);
            if (w < 8) w = 8;
            if (h < 8) h = 8;

            using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(0, 0, 0, 0));
                if (BackColorOverride != Color.Empty)
                {
                    using var background = new SolidBrush(BackColorOverride);
                    g.FillRectangle(background, 0, 0, w, h);
                }
                /* sbloccato: velo quasi invisibile (alpha 6) → l'intero pannello è cliccabile */
                if (Draggable)
                {
                    using var veil = new SolidBrush(Color.FromArgb(6, 20, 20, 24));
                    g.FillRectangle(veil, 0, 0, w, h);
                }
                foreach (var p in placed)
                {
                    bool opaqueBack = p.back != Color.Empty;
                    if (opaqueBack)
                    {
                        var b = p.back != Color.Empty ? p.back : BackColorOverride;
                        using var br = new SolidBrush(b);
                        g.FillRectangle(br, pad.Left, p.y, w - pad.Horizontal, p.h);
                    }
                    using var brush = new SolidBrush(p.color);
                    g.DrawString(p.text, p.font, brush, p.x, p.y + 1);
                }
            }
            foreach (var f in fonts) f.Dispose();

            ClientSize = new Size(w, h);
            ApplyLayered(bmp);
        }

        public void MoveTo(Rectangle r)
        {
            Bounds = r;
        }
    }

    /// <summary>Finestra cornice/riempimento colorata (Table highlight, Fase 12).</summary>
    public sealed class FrameWindow : OverlayWindow
    {
        private readonly Color _ringColor;
        private readonly Color _fillColor;
        private readonly int _width;
        private readonly bool _fill;
        private readonly double _opacity;

        public FrameWindow(Color ringColor, Color fillColor, int width, int alphaPercent, bool fill)
            : base(clickThrough: true)
        {
            _ringColor = ringColor;
            _fillColor = fillColor;
            _width = Math.Max(2, width);
            _fill = fill;
            if (fill)
            {
                // riempimento TRASLUCIDO (Opacity 0 -> default 35%)
                BackColor = fillColor == Color.Empty ? Color.Yellow : fillColor;
                Opacity = alphaPercent <= 0 ? 0.35 : Math.Min(1.0, alphaPercent / 100.0);
            }
            else
            {
                // cornice: sfondo = key color (trasparente), anello colorato
                BackColor = Color.FromArgb(255, ringColor);
                Opacity = 1.0;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_fill) return; // il riempimento è il BackColor
            using var p = new Pen(_ringColor, _width);
            e.Graphics.DrawRectangle(p, _width / 2, _width / 2, Width - _width - 1, Height - _width - 1);
        }

        /// <summary>Adatta la finestra al tavolo (riempimento = rettangolo esatto; cornice = con margine).</summary>
        public void Wrap(Rectangle r, bool fill)
        {
            if (fill) Bounds = r;
            else Bounds = new Rectangle(r.X - _width, r.Y - _width, r.Width + 2 * _width, r.Height + 2 * _width);
        }
    }

    public static class OverlayUtil
    {
        /// <summary>Mostra un form senza rubare il focus.</summary>
        public static void ShowNoActivate(Form f)
        {
            if (!f.Visible) f.Show();
            var hwnd = f.Handle;
            NativeMethods.SetWindowPos(hwnd, new IntPtr(-1) /*HWND_TOPMOST*/, 0, 0, 0, 0, 0x0013 /*NOSIZE|NOMOVE|NOACTIVATE*/);
        }
    }
}
