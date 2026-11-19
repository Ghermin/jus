using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Jus
{
    /// <summary>Couleurs du panneau, reprises de la page du projet (variables --pg-* de docs/index.html).</summary>
    sealed class Palette
    {
        public Color Back, Hover, Text, Muted, Line, Accent, Accent2, Low;

        public static readonly Palette Light = new Palette
        {
            Back = Color.FromArgb(0xFF, 0xFF, 0xFF), Hover = Color.FromArgb(0xF3, 0xEC, 0xDF),
            Text = Color.FromArgb(0x1C, 0x15, 0x0C), Muted = Color.FromArgb(0x62, 0x57, 0x4A), Line = Color.FromArgb(0xE6, 0xDC, 0xCB),
            Accent = Color.FromArgb(0xC4, 0x3D, 0x0C), Accent2 = Color.FromArgb(0xE0, 0xA8, 0x00), Low = Color.FromArgb(0xC4, 0x2B, 0x1C),
        };

        public static readonly Palette Dark = new Palette
        {
            Back = Color.FromArgb(0x1A, 0x15, 0x10), Hover = Color.FromArgb(0x2C, 0x24, 0x1A),
            Text = Color.FromArgb(0xF6, 0xEF, 0xE4), Muted = Color.FromArgb(0xB3, 0xA7, 0x97), Line = Color.FromArgb(0x33, 0x29, 0x1E),
            Accent = Color.FromArgb(0xFF, 0x8A, 0x4C), Accent2 = Color.FromArgb(0xF2, 0xB7, 0x05), Low = Color.FromArgb(0xFF, 0x6B, 0x60),
        };

        /// <summary>Mode des applications choisi dans Windows (0 = sombre).</summary>
        public static bool WindowsIsDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch (System.Security.SecurityException) { return false; }
        }
    }

    /// <summary>Panneau moderne, comme le Gestionnaire RGB : coins arrondis, survol arrondi, coches fines, clair ou sombre.</summary>
    sealed class MenuTheme : ToolStripRenderer
    {
        public static readonly Font TextFont = new Font("Segoe UI", 9.75f);
        public static readonly Font TitleFont = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font MonoFont = new Font("Consolas", 8f);

        readonly bool fixedPalette;
        public Palette P { get; private set; }

        public MenuTheme() { P = Palette.WindowsIsDark() ? Palette.Dark : Palette.Light; }
        public MenuTheme(Palette palette) { P = palette; fixedPalette = true; }

        public void Attach(ContextMenuStrip menu)
        {
            menu.Renderer = this;
            menu.Font = TextFont;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false; // les lignes dessinent leurs coches elles-mêmes
            menu.DropShadowEnabled = true;
            menu.Padding = new Padding(4, 6, 4, 6);
            menu.ShowItemToolTips = true;
            menu.HandleCreated += (s, e) => ApplyWindow(menu);
            // Relu à chaque ouverture : le panneau suit un changement de thème sans redémarrer l'appli.
            menu.Opening += (s, e) =>
            {
                if (!fixedPalette) P = Palette.WindowsIsDark() ? Palette.Dark : Palette.Light;
                ApplyWindow(menu);
            };
            menu.ItemAdded += (s, e) =>
            {
                if (e.Item is ToolStripMenuItem m) m.Padding = new Padding(m.Padding.Left, 3, m.Padding.Right, 3);
            };
        }

        /// <summary>Coins arrondis et bordure par DWM (Windows 11 ; sans effet ailleurs).</summary>
        void ApplyWindow(Control c)
        {
            if (!c.IsHandleCreated) return;
            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(c.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            int border = ColorTranslator.ToWin32(P.Line);
            DwmSetWindowAttribute(c.Handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }

        static float Scale(ToolStripItem item) => (item.Owner?.DeviceDpi ?? 96) / 96f;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(P.Back)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (Environment.OSVersion.Version.Build >= 22000) return; // Windows 11 dessine la bordure arrondie
            using (var pen = new Pen(P.Line)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled || !e.Item.Selected) return;
            FillHover(e.Graphics, P, e.Item.Width, e.Item.Height, Scale(e.Item));
        }

        public static void FillHover(Graphics g, Palette p, int width, int height, float s)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Rounded(new RectangleF(0, .5f, width - .5f, height - 1f), 6 * s))
            using (var b = new SolidBrush(p.Hover))
                g.FillPath(b, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            bool sig = "sig".Equals(e.Item.Tag);
            Color c = !e.Item.Enabled || (sig && !e.Item.Selected) ? P.Muted : P.Text;
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, c, e.TextFormat);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            Check(e.Graphics, P.Text, r.X + r.Width / 2f, r.Y + r.Height / 2f, Scale(e.Item));
        }

        public static void Check(Graphics g, Color color, float cx, float cy, float s)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(cx - 4.5f * s, cy + .2f * s), new PointF(cx - 1.5f * s, cy + 3.2f * s), new PointF(cx + 4.5f * s, cy - 3.3f * s) });
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            float s = Scale(e.Item);
            int y = e.Item.Height / 2;
            using (var pen = new Pen(P.Line)) e.Graphics.DrawLine(pen, 8 * s, y, e.Item.Width - 8 * s, y);
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34, DWMWCP_ROUND = 2;
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }

    /// <summary>Panneau de largeur fixe, quelle que soit la longueur des noms d'appareils.</summary>
    sealed class PanelMenu : ContextMenuStrip
    {
        const int VK_LBUTTON = 1, VK_RBUTTON = 2, VK_MBUTTON = 4, ReopenMs = 600;

        readonly Timer watch = new Timer { Interval = 40 };
        bool wasDown;
        int dismissedAt = Environment.TickCount - ReopenMs;

        public PanelMenu()
        {
            AutoClose = false; // ni le survol ni les boutons ne ferment le panneau
            watch.Tick += (s, e) => Watch();
        }

        /// <summary>Le clic qui vient de fermer le panneau ne doit pas le rouvrir en arrivant sur le widget.</summary>
        public bool JustDismissed => Environment.TickCount - dismissedAt < ReopenMs;

        public override Size GetPreferredSize(Size proposedSize)
            => new Size((int)(390 * DeviceDpi / 96f), base.GetPreferredSize(proposedSize).Height);

        /// <summary>
        /// WinForms décale les lignes dessinées à la main de la largeur de la marge des coches. On les ramène au bord, comme
        /// les séparateurs : elles placent elles-mêmes leur coche et leur texte dans les colonnes des entrées du bas (Columns).
        /// </summary>
        protected override void OnLayout(LayoutEventArgs e)
        {
            int pull = Padding.Left - (int)(Columns.Edge * DeviceDpi / 96f);
            foreach (ToolStripItem item in Items)
                if (item is CustomRow && item.Margin.Left != -pull)
                    item.Margin = new Padding(-pull, item.Margin.Top, item.Margin.Right, item.Margin.Bottom);
            base.OnLayout(e);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            wasDown = AnyButtonDown();
            watch.Start();
        }

        protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
        {
            watch.Stop();
            base.OnClosed(e);
        }

        /// <summary>Seule façon de fermer le panneau : un clic en dehors de sa fenêtre.</summary>
        void Watch()
        {
            bool down = AnyButtonDown();
            bool pressed = down && !wasDown;
            wasDown = down;
            if (!pressed || Bounds.Contains(Cursor.Position)) return;
            dismissedAt = Environment.TickCount;
            Close(ToolStripDropDownCloseReason.CloseCalled);
        }

        static bool IsDown(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
        static bool AnyButtonDown() => IsDown(VK_LBUTTON) || IsDown(VK_RBUTTON) || IsDown(VK_MBUTTON);

        protected override void Dispose(bool disposing)
        {
            if (disposing) watch.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Colonnes du panneau à 96 ppp, communes à toutes les lignes (toutes dessinées à la main).</summary>
    static class Columns
    {
        public const float Edge = 2;    // bord gauche des lignes (comme les séparateurs)
        public const float Check = 20;  // centre des coches
        public const float Text = 34;   // début du texte, de l'icône des appareils et du titre
        public const float Width = 386; // largeur des lignes
    }

    /// <summary>Ligne dessinée à la main, posée au bord du panneau (voir PanelMenu.OnLayout).</summary>
    abstract class CustomRow : ToolStripItem
    {
        protected float S => (Owner?.DeviceDpi ?? 96) / 96f;
        /// <summary>Position, dans la ligne, d'une colonne du panneau.</summary>
        protected float At(float column) => (column - Columns.Edge) * S;
    }

    /// <summary>En-tête du panneau : le nom de l'appli et le nombre d'appareils.</summary>
    sealed class HeaderRow : CustomRow
    {
        readonly MenuTheme theme;
        readonly string right;

        public HeaderRow(MenuTheme theme, string right) { this.theme = theme; this.right = right; }

        public override bool CanSelect => false;
        protected override bool DismissWhenClicked => false;
        public override Size GetPreferredSize(Size constrainingSize) => new Size((int)(Columns.Width * S), (int)(30 * S));

        protected override void OnPaint(PaintEventArgs e)
        {
            const TextFormatFlags flat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            int left = (int)At(Columns.Text);
            var box = new Rectangle(left, 0, Width - left - (int)(12 * S), Height);
            TextRenderer.DrawText(e.Graphics, "Jus", MenuTheme.TitleFont, box, theme.P.Text, flat);
            // La version à côté du nom : on voit tout de suite si la mise à jour est passée.
            int name = TextRenderer.MeasureText(e.Graphics, "Jus", MenuTheme.TitleFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(e.Graphics, Version, MenuTheme.TextFont, new Rectangle(left + name + (int)(6 * S), 0, box.Width, Height), theme.P.Muted, flat);
            TextRenderer.DrawText(e.Graphics, right, MenuTheme.TextFont, box, theme.P.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        static readonly string Version = typeof(HeaderRow).Assembly.GetName().Version.ToString(3);
    }

    /// <summary>Une entrée du bas : texte, coche éventuelle (réglage), même survol que les appareils.</summary>
    sealed class ActionRow : CustomRow
    {
        readonly MenuTheme theme;
        readonly string text;
        readonly Action run;
        readonly bool toggle, signature;
        bool on, hover;

        public ActionRow(MenuTheme theme, string text, Action run, bool? check = null, bool signature = false)
        {
            this.theme = theme;
            this.text = text;
            this.run = run;
            this.signature = signature;
            toggle = check != null;
            on = check == true;
            Enabled = run != null;
            AccessibleName = text;
            AccessibleRole = toggle ? AccessibleRole.CheckButton : AccessibleRole.MenuItem;
        }

        public override bool CanSelect => Enabled;
        protected override bool DismissWhenClicked => !toggle; // une coche change sur place, le panneau reste ouvert
        public override Size GetPreferredSize(Size constrainingSize) => new Size((int)(Columns.Width * S), (int)((signature ? 26 : 30) * S));

        protected override void OnMouseEnter(EventArgs e) { hover = Enabled; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            if (toggle) { on = !on; Invalidate(); }
            run();
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette p = theme.P;
            if (hover) MenuTheme.FillHover(e.Graphics, p, Width, Height, S);
            if (on) MenuTheme.Check(e.Graphics, p.Text, At(Columns.Check), Height / 2f, S);
            Color color = !Enabled || (signature && !hover) ? p.Muted : p.Text;
            int left = (int)At(Columns.Text);
            TextRenderer.DrawText(e.Graphics, text, signature ? MenuTheme.MonoFont : MenuTheme.TextFont, new Rectangle(left, 0, Width - left - (int)(12 * S), Height), color,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Une ligne par appareil : coche, icône, nom, pourcentage et barre de niveau.</summary>
    sealed class DeviceRow : CustomRow
    {
        const TextFormatFlags Flat = TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;

        readonly BatteryReading reading;
        readonly MenuTheme theme;
        readonly Action toggle;
        bool shown, hover;

        public DeviceRow(BatteryReading reading, MenuTheme theme, bool shown, Action toggle)
        {
            this.reading = reading;
            this.theme = theme;
            this.shown = shown;
            this.toggle = toggle;
            AccessibleName = reading.Name + " " + reading.Text;
            ToolTipText = "Afficher dans la barre des tâches";
        }

        protected override bool DismissWhenClicked => false; // le panneau reste ouvert pour cocher plusieurs appareils
        public override Size GetPreferredSize(Size constrainingSize) => new Size((int)(Columns.Width * S), (int)(48 * S));

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            shown = !shown;
            toggle();
            Invalidate();
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = theme.P;
            float s = S;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (hover) MenuTheme.FillHover(g, p, Width, Height, s);

            bool low = reading.IsLow;
            Color text = shown ? p.Text : p.Muted;
            Color tone = low && shown ? p.Low : text;
            string value = reading.PluggedIn ? "Branchée" : reading.NoLevel ? "Sans fil" : reading.Text;
            Font valueFont = reading.NoLevel ? MenuTheme.TextFont : MenuTheme.TitleFont;
            Color valueColor = reading.PluggedIn ? text : reading.NoLevel ? p.Muted : tone;

            if (shown) MenuTheme.Check(g, p.Text, At(Columns.Check), Height / 2f, s);
            float iconSize = 20 * s;
            Glyphs.Draw(g, reading.Kind, At(Columns.Text) - 2 * s, (Height - iconSize) / 2, iconSize, tone); // le dessin a 2 px de marge

            float left = At(Columns.Text) + 28 * s, right = Width - 12 * s;
            int top = (int)(7 * s), line = (int)(20 * s);
            Size size = TextRenderer.MeasureText(g, value, valueFont, Size.Empty, TextFormatFlags.NoPadding);
            int percentX = (int)(right - size.Width);
            TextRenderer.DrawText(g, value, valueFont, new Rectangle(percentX, top, size.Width + 2, line), valueColor, Flat);
            float reserved = size.Width + 14 * s;
            if (reading.NoLevel)
            {
                if (reading.PluggedIn) Glyphs.Plug(g, percentX - 18 * s, top + (line - 14 * s) / 2, 14 * s, text);
                else Glyphs.Wireless(g, percentX - 18 * s, top + (line - 14 * s) / 2, 14 * s, valueColor);
                reserved += 18 * s;
            }
            if (reading.Charging)
            {
                Glyphs.Bolt(g, percentX - 10 * s, top + line / 2f, 7 * s, tone);
                reserved += 10 * s;
            }
            var name = new Rectangle((int)left, top, (int)(right - left - reserved), line);
            TextRenderer.DrawText(g, reading.Name, MenuTheme.TextFont, name, text, Flat | TextFormatFlags.EndEllipsis);

            // Barre de niveau
            var track = new RectangleF(left, Height - 14 * s, right - left, 4 * s);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = MenuTheme.Rounded(track, 2 * s))
            using (var b = new SolidBrush(p.Line))
                g.FillPath(b, path);
            if (reading.NoLevel) return;
            float width = Math.Max(track.Height, track.Width * Math.Max(0, Math.Min(100, reading.Percent)) / 100f);
            var fill = new RectangleF(track.X, track.Y, width, track.Height);
            using (GraphicsPath path = MenuTheme.Rounded(fill, 2 * s))
            using (Brush b = !shown ? new SolidBrush(p.Muted)
                           : low ? new SolidBrush(p.Low)
                           : (Brush)new LinearGradientBrush(new RectangleF(track.X - 1, track.Y, track.Width + 2, track.Height), p.Accent2, p.Accent, LinearGradientMode.Horizontal))
                g.FillPath(b, path);
        }
    }
}
