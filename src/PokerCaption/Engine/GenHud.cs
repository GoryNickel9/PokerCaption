using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PokerCaption.Config;

namespace PokerCaption.Engine
{
    /// <summary>
    /// General HUD: pannelli fissi sul desktop con i tag
    /// di sessione (default: Session time + [sessionTime], Hands + [allHandsCount]).
    /// Configurazione: Count, formatStrN, FColorN, FBkColorN, FSizeN, left/top e active.
    /// </summary>
    public sealed class GenHud : IDisposable
    {
        public sealed class Item
        {
            public string Format;
            public Color Color = Color.Empty;
            public Color Back;
            public int Size = 9;
        }

        public readonly List<Item> Items = new List<Item>();
        public int Left = 4, Top = 938;
        public bool Active = true;
        public bool LockPanels = true;   // [HUD] Lock ('Lock HUD'): pannelli non spostabili col mouse
        private Action<string> _log;
        private IReadOnlyList<TableInfo> _tables;
        private GenHudWindow _win;

        public GenHud(Action<string> log) { _log = log; }
        public void Load(IniFile ini)
        {
            Items.Clear();
            Active = ini.GetBool("GenHUD", "active", true);
            Left = ini.GetInt("GenHUD", "left", 4);
            Top = ini.GetInt("GenHUD", "top", 938);
            LockPanels = ini.GetBool("HUD", "LockGeneral", false);   // 'Lock General': default SBLOCCATO
            int count = ini.GetInt("GenHUD", "Count", 0);
            for (int i = 0; i < count; i++)
            {
                var f = ini.Get("GenHUD", "formatStr" + i);
                if (f == null) continue;
                var back = IniColors.FromInt(ini.GetLong("GenHUD", "FBkColor" + i, 0x404040));
                Items.Add(new Item
                {
                    Format = f,
                    Color = IniColors.FromInt(ini.GetLong("GenHUD", "FColor" + i, 0xFFFFFF)),
                    Back = back.A == 0 ? Color.FromArgb(64, 64, 64) : back,
                    Size = ini.GetInt("GenHUD", "FSize" + i, 9)
                });
            }
        }

        public void SetTables(IReadOnlyList<TableInfo> tables) => _tables = tables;

        public void OnTick()
        {
            if (!Active) { DisposeAll(); return; }
            if (_win == null)
            {
                _win = new GenHudWindow();
                _win.LockDrag = LockPanels;
                // Salva la posizione del pannello in [GenHUD].
                _win.MovedTo += (left, top) =>
                {
                    Left = left; Top = top;
                    try
                    {
                        Program.Ini?.Set("GenHUD", "left", left.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        Program.Ini?.Set("GenHUD", "top", top.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        Program.Ini?.Save();
                        _log?.Invoke($"GenHUD: posizione {left},{top} salvata");
                    }
                    catch (Exception ex) { _log?.Invoke("GenHUD: save posizione ERR: " + ex.Message); }
                };
                _win.Show();
                OverlayUtil.ShowNoActivate(_win);
                _win.ContextMenuStrip = BuildGenHudMenu();
            }
            var ctx = new TagContext { Tables = _tables, Session = SessionStats.Current, Rng = new Random() };
            _win.LockDrag = LockPanels; // 'Lock HUD' può cambiare a runtime
            _win.Boxes.Clear();
            foreach (var it in Items)
            {
                _win.Boxes.Add(new GenHudWindow.Box
                {
                    Text = HudTags.Resolve(it.Format, ctx),
                    Fore = it.Color == Color.Empty ? Color.White : it.Color,
                    Back = it.Back,
                    FontSizePt = it.Size,
                    // Mostra in grassetto i valori prodotti dai tag.
                    Bold = (it.Format ?? "").IndexOf('[', StringComparison.Ordinal) >= 0
                });
            }
            _win.UpdateLayout();
            if (!_win.Dragging) // durante il drag la posizione è dell'utente
                _win.Bounds = new Rectangle(Left, Top, GenHudWindow.CanvasWidth, _win.Height);
        }

        /// <summary>Menu per azzerare le statistiche di sessione.</summary>
        private ContextMenuStrip BuildGenHudMenu()
        {
            var menu = new ContextMenuStrip();
            void ResetItem(string text, Action act)
            {
                var it = menu.Items.Add(text) as ToolStripMenuItem;
                it.Click += (s, e) => { try { act(); Program.Ini.Save(); _log?.Invoke("GenHUD: " + text); } catch (Exception ex) { _log?.Invoke("GenHUD reset ERR: " + ex.Message); } };
            }
            ResetItem("Reset session tags", () => SessionStats.Current.ResetSessionTags());
            ResetItem("Reset [sessionTime]", () => SessionStats.Current.ResetSessionTime());
            ResetItem("Reset [allHandsCount]", () => SessionStats.Current.ResetSessionHands());
            return menu;
        }

        private void DisposeAll()
        {
            if (_win != null) { _win.Close(); _win.Dispose(); _win = null; }
        }

        public void Dispose() => DisposeAll();
    }

    /// <summary>
    /// Disegna tutti i pannelli General HUD in una finestra con larghezza fissa.
    /// L'altezza di ciascuno dipende dal font e da un margine fisso per la leggibilità.
    /// </summary>
    public sealed class GenHudWindow : OverlayWindow
    {
        public const int CanvasWidth = 101;
        private const int TextMargin = 4;

        public sealed class Box
        {
            public string Text = "";
            public Color Fore = Color.White;
            public Color Back;
            public int FontSizePt = 9;
            public bool Bold;
        }

        public readonly List<Box> Boxes = new List<Box>();

        /// <summary>Rilascio dopo un drag: (left, top) nuovi — salvati su INI dal motore.</summary>
        public event Action<int, int> MovedTo;

        public GenHudWindow() : base(clickThrough: false) { }

        /// <summary>True con 'Lock HUD': il pannello non si lascia trascinare.</summary>
        public bool LockDrag;

        // Al rilascio, salva la posizione in [GenHUD].
        private bool _dragging;
        private Point _dragOffset;
        public bool Dragging => _dragging;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !LockDrag)
            {
                _dragging = true;
                _dragOffset = e.Location;
                Capture = true;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                var p = new Point(Left + e.X - _dragOffset.X, Top + e.Y - _dragOffset.Y);
                Location = p;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
                MovedTo?.Invoke(Left, Top);
            }
            base.OnMouseUp(e);
        }

        private static Font FontOf(Box b) =>
            new Font("Tahoma", b.FontSizePt <= 0 ? 9 : b.FontSizePt,
                     b.Bold ? FontStyle.Bold : FontStyle.Regular);

        private static int BoxHeight(Box b)
        {
            using var f = FontOf(b);
            return (int)Math.Ceiling(f.GetHeight()) + 2 * TextMargin;
        }

        /// <summary>Ricalcola l'altezza della canvas e ridipinge (ogni tick).</summary>
        public void UpdateLayout()
        {
            int h = 0;
            foreach (var b in Boxes) h += BoxHeight(b);
            ClientSize = new Size(CanvasWidth, Math.Max(h, 8));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int y = 0;
            foreach (var b in Boxes)
            {
                int h = BoxHeight(b);
                using var br = new SolidBrush(b.Back);
                e.Graphics.FillRectangle(br, 0, y, CanvasWidth, h);
                if (!string.IsNullOrEmpty(b.Text))
                {
                    using var f = FontOf(b);
                    var rect = new Rectangle(0, y, CanvasWidth, h);
                    var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine;
                    TextRenderer.DrawText(e.Graphics, b.Text, f, rect, b.Fore, flags);
                }
                y += h;
            }
        }
    }
}
