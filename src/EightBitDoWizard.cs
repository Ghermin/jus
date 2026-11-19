using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Jus
{
    /// <summary>
    /// Fenêtre qui propose le patch batterie de la 8BitDo, puis guide l'utilisateur : un clic pour accepter, ensuite Jus
    /// attend que le dongle et la manette soient dans le bon état et fait le reste.
    /// </summary>
    sealed class EightBitDoWizard : Form
    {
        const string Page = "https://github.com/Ghermin/8bitdo-2c-battery-patch";

        readonly Palette p = Palette.WindowsIsDark() ? Palette.Dark : Palette.Light;
        readonly StepView dongle, pad;
        readonly Label intro, footer;
        readonly Button primary, secondary;
        EightBitDo.Installer installer;
        bool running, finished;

        public EightBitDoWizard()
        {
            Text = "Jus · 8BitDo";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(460, 376);
            BackColor = p.Back;
            ForeColor = p.Text;
            Font = MenuTheme.TextFont;

            var title = new Label
            {
                Text = "Afficher la batterie de ta 8BitDo", Font = new Font("Segoe UI Semibold", 13f),
                Location = new Point(24, 20), AutoSize = true, ForeColor = p.Text,
            };
            intro = new Label
            {
                Text = "En 2,4 GHz, le dongle de la 8BitDo Ultimate 2C ne transmet pas sa batterie. Jus peut corriger ça "
                     + "avec un petit patch du firmware officiel. Tes réglages sont gardés et une sauvegarde est faite avant.",
                Location = new Point(24, 56), Size = new Size(412, 86), ForeColor = p.Muted,
            };
            dongle = new StepView(p, "Dongle") { Location = new Point(24, 152), Size = new Size(412, 66) };
            pad = new StepView(p, "Manette") { Location = new Point(24, 226), Size = new Size(412, 66) };
            dongle.Set(EightBitDo.State.Waiting, "Il faudra éteindre la manette, dongle branché", -1);
            pad.Set(EightBitDo.State.Waiting, "Il faudra la brancher au PC par un câble USB", -1);

            footer = new Label
            {
                Text = "En savoir plus", ForeColor = p.Muted, AutoSize = true,
                Location = new Point(24, 324), Cursor = Cursors.Hand, Font = new Font("Segoe UI", 8.25f, FontStyle.Underline),
            };
            footer.Click += (s, e) => Process.Start(new ProcessStartInfo(Page) { UseShellExecute = true });

            primary = MakeButton("Activer", true);
            primary.Location = new Point(336, 318);
            primary.Click += (s, e) => { if (finished || running) Close(); else Start(); };
            secondary = MakeButton("Plus tard", false);
            secondary.Location = new Point(228, 318);
            secondary.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { title, intro, dongle, pad, footer, primary, secondary });
            AcceptButton = primary;
        }

        Button MakeButton(string text, bool accent)
        {
            var b = new Button
            {
                Text = text, Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = accent ? p.Accent : p.Hover, ForeColor = accent ? Color.White : p.Text,
                Font = accent ? MenuTheme.TitleFont : MenuTheme.TextFont,
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = accent ? ControlPaint.Light(p.Accent, .15f) : p.Line;
            return b;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = p == Palette.Dark ? 1 : 0, round = 2;
            DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));  // barre de titre sombre
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); // coins arrondis (Windows 11)
        }

        void Start()
        {
            running = true;
            primary.Text = "Fermer";
            secondary.Visible = false;
            intro.Text = "Suis les indications ci-dessous : Jus attend que chaque appareil soit prêt, puis s'en occupe. "
                       + "Ne débranche rien pendant une écriture.";
            installer = new EightBitDo.Installer();
            installer.Report += (part, state, text, progress) =>
            {
                if (IsDisposed) return;
                try { BeginInvoke((Action)(() => (part == EightBitDo.Part.Dongle ? dongle : pad).Set(state, text, progress))); }
                catch (InvalidOperationException) { }
            };
            var thread = new Thread(() =>
            {
                installer.Run();
                try { BeginInvoke((Action)Finish); } catch (InvalidOperationException) { }
            }) { IsBackground = true, Name = "Jus.8BitDo" };
            thread.Start();
        }

        /// <summary>Pour --preview-8bitdo : l'état pendant une installation.</summary>
        public void Demo()
        {
            primary.Text = "Fermer";
            secondary.Visible = false;
            intro.Text = "Suis les indications ci-dessous : Jus attend que chaque appareil soit prêt, puis s'en occupe. "
                       + "Ne débranche rien pendant une écriture.";
            dongle.Set(EightBitDo.State.Done, "Fait", -1);
            pad.Set(EightBitDo.State.Working, "Écriture… ne débranche rien", 40);
        }

        void Finish()
        {
            running = false;
            finished = true;
            primary.Text = "Fermer";
            if (dongle.State == EightBitDo.State.Done && pad.State == EightBitDo.State.Done)
                intro.Text = "C'est prêt. Allume la manette en 2,4 GHz : Jus affiche sa batterie dans quelques secondes.";
            else if (dongle.State == EightBitDo.State.Failed || pad.State == EightBitDo.State.Failed)
                intro.Text = "Ça n'a pas pu se faire. Le détail est ci-dessous ; rien n'a été écrit sauf indication contraire.";
        }

        /// <summary>Fermer pendant l'attente arrête tout ; pendant une écriture, elle se termine d'abord, en arrière-plan.</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (installer != null) installer.Cancel = true;
            base.OnFormClosing(e);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Une étape : pastille d'état, nom, texte et barre de progression.</summary>
        sealed class StepView : Control
        {
            readonly Palette p;
            readonly string name;
            string text = "";
            int progress = -1;
            public EightBitDo.State State { get; private set; }

            public StepView(Palette palette, string name)
            {
                p = palette;
                this.name = name;
                DoubleBuffered = true;
                BackColor = p.Back;
            }

            public void Set(EightBitDo.State state, string text, int progress)
            {
                State = state;
                this.text = text;
                this.progress = state == EightBitDo.State.Working ? progress : -1;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                float s = DeviceDpi / 96f;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath box = MenuTheme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 8 * s))
                using (var b = new SolidBrush(p.Hover))
                    g.FillPath(b, box);

                // Pastille : vide en attente, pleine en cours, coche si fait, croix si échec.
                float r = 9 * s, cx = 22 * s, cy = Height / 2f;
                Color tone = State == EightBitDo.State.Failed ? p.Low : State == EightBitDo.State.Waiting ? p.Muted : p.Accent;
                using (var pen = new Pen(tone, 1.6f * s))
                using (var fill = new SolidBrush(tone))
                {
                    if (State == EightBitDo.State.Working || State == EightBitDo.State.Done) g.FillEllipse(fill, cx - r, cy - r, r * 2, r * 2);
                    else g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                    if (State == EightBitDo.State.Done) MenuTheme.Check(g, p.Back, cx, cy, s * .9f);
                    if (State == EightBitDo.State.Failed)
                    {
                        g.DrawLine(pen, cx - 4 * s, cy - 4 * s, cx + 4 * s, cy + 4 * s);
                        g.DrawLine(pen, cx + 4 * s, cy - 4 * s, cx - 4 * s, cy + 4 * s);
                    }
                }

                const TextFormatFlags flat = TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis;
                int left = (int)(44 * s), width = Width - left - (int)(12 * s);
                TextRenderer.DrawText(g, name, MenuTheme.TitleFont, new Rectangle(left, (int)(8 * s), width, (int)(22 * s)), p.Text, flat);
                TextRenderer.DrawText(g, text, MenuTheme.TextFont, new Rectangle(left, (int)(29 * s), width, Height - (int)(37 * s)),
                                      State == EightBitDo.State.Failed ? p.Low : p.Muted, TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.WordBreak);
                if (progress < 0) return;
                var track = new RectangleF(left, Height - 7 * s, width, 3 * s);
                using (GraphicsPath path = MenuTheme.Rounded(track, 1.5f * s)) using (var b = new SolidBrush(p.Line)) g.FillPath(b, path);
                var bar = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * Math.Min(100, progress) / 100f), track.Height);
                using (GraphicsPath path = MenuTheme.Rounded(bar, 1.5f * s))
                using (var b = new LinearGradientBrush(new RectangleF(track.X - 1, track.Y, track.Width + 2, track.Height), p.Accent2, p.Accent, LinearGradientMode.Horizontal))
                    g.FillPath(b, path);
            }
        }
    }
}
