using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Jus
{
    /// <summary>L'appli : icône près de l'horloge, menu, alertes et réglages.</summary>
    sealed class TrayApp : ApplicationContext
    {
        const int AlertBelow = 20;

        readonly Control ui = new Control();
        readonly BatteryMonitor monitor = new BatteryMonitor();
        readonly TaskbarWidget widget = new TaskbarWidget();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
        readonly Settings settings = Settings.Load();
        readonly HashSet<string> alerted = new HashSet<string>();
        readonly EventWaitHandle quit;
        readonly RegisteredWaitHandle quitWait;
        List<BatteryReading> readings = new List<BatteryReading>();

        public TrayApp(EventWaitHandle quitSignal)
        {
            quit = quitSignal;
            ui.CreateControl();
            _ = ui.Handle;

            tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            tray.Text = "Jus";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) OpenMenu(); };

            menu.Opening += (s, e) => { BuildMenu(); e.Cancel = false; };
            widget.Clicked += OpenMenu;

            monitor.Updated += list => ui.BeginInvoke((Action)(() => Apply(list)));
            monitor.Start();

            quitWait = ThreadPool.RegisterWaitForSingleObject(quit, (s, t) => ui.BeginInvoke((Action)Quit), null, -1, true);
        }

        void Apply(List<BatteryReading> list)
        {
            readings = list;
            widget.Show(list.Where(r => !settings.Hidden.Contains(r.Id)));
            tray.Text = Shorten(list.Count == 0 ? "Jus : aucun appareil détecté" : string.Join("\n", list.Select(r => r.Name + " : " + r.Text)));

            foreach (BatteryReading r in list)
            {
                if (r.Charging || r.Percent > AlertBelow + 5) alerted.Remove(r.Id);
                else if (settings.Alerts && r.Percent <= AlertBelow && alerted.Add(r.Id))
                    tray.ShowBalloonTip(8000, "Batterie faible", r.Name + " : plus que " + r.Text, ToolTipIcon.Warning);
            }
        }

        static string Shorten(string s) => s.Length <= 127 ? s : s.Substring(0, 126) + "…";

        void OpenMenu()
        {
            BuildMenu();
            Native.SetForegroundWindow(ui.Handle);
            menu.Show(Cursor.Position, ToolStripDropDownDirection.AboveLeft);
        }

        void BuildMenu()
        {
            menu.Items.Clear();
            menu.Renderer = new MenuTheme(TaskbarWidget.LightTaskbar());

            menu.Items.Add(new ToolStripLabel("Jus") { Font = new Font(menu.Font, FontStyle.Bold) });
            menu.Items.Add(new ToolStripSeparator());

            if (readings.Count == 0)
                menu.Items.Add(new ToolStripMenuItem("Aucun appareil détecté") { Enabled = false });
            foreach (BatteryReading r in readings)
            {
                BatteryReading item = r;
                string state = r.Text + (r.Charging ? " (en charge)" : "");
                var entry = new ToolStripMenuItem(r.Name + "  ·  " + state)
                {
                    Checked = !settings.Hidden.Contains(r.Id), ToolTipText = "Afficher dans la barre des tâches",
                };
                entry.Click += (s, e) => ToggleDevice(item);
                menu.Items.Add(entry);
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Entry("Actualiser", monitor.Refresh));
            menu.Items.Add(Toggle($"Prévenir sous {AlertBelow} %", settings.Alerts, () => { settings.Alerts = !settings.Alerts; settings.Save(); }));
            menu.Items.Add(Toggle("Lancer avec Windows", Installer.AutoStart, () => Installer.AutoStart = !Installer.AutoStart));
            menu.Items.Add(Entry("Quitter", Quit));
            menu.Items.Add(new ToolStripSeparator());
            var sig = Entry("un projet du bored dev playground", () => Process.Start(new ProcessStartInfo("https://ghermin.github.io/") { UseShellExecute = true }));
            sig.Font = new Font("Consolas", menu.Font.SizeInPoints - 1);
            menu.Items.Add(sig);
        }

        void ToggleDevice(BatteryReading r)
        {
            if (!settings.Hidden.Remove(r.Id)) settings.Hidden.Add(r.Id);
            settings.Save();
            widget.Show(readings.Where(x => !settings.Hidden.Contains(x.Id)));
        }

        static ToolStripMenuItem Entry(string text, Action run)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) => run();
            return item;
        }

        static ToolStripMenuItem Toggle(string text, bool on, Action run)
        {
            ToolStripMenuItem item = Entry(text, run);
            item.Checked = on;
            return item;
        }

        void Quit()
        {
            quitWait.Unregister(null);
            monitor.Dispose();
            widget.Dispose();
            tray.Visible = false;
            tray.Dispose();
            ExitThread();
        }
    }

    /// <summary>Réglages : un petit fichier texte dans %APPDATA%\Jus.</summary>
    sealed class Settings
    {
        static readonly string File_ = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Jus", "settings.txt");
        public bool Alerts = true;
        public readonly HashSet<string> Hidden = new HashSet<string>();

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                foreach (string line in File.ReadAllLines(File_))
                {
                    if (line == "alerts=0") s.Alerts = false;
                    else if (line.StartsWith("hidden=")) s.Hidden.Add(line.Substring(7));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File_));
                File.WriteAllLines(File_, new[] { "alerts=" + (Alerts ? 1 : 0) }.Concat(Hidden.Select(h => "hidden=" + h)));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Le menu suit le thème de la barre des tâches.</summary>
    sealed class MenuTheme : ToolStripProfessionalRenderer
    {
        readonly Color back, hover, text, line;

        public MenuTheme(bool light)
        {
            back = light ? Color.FromArgb(249, 249, 249) : Color.FromArgb(32, 32, 32);
            hover = light ? Color.FromArgb(232, 232, 232) : Color.FromArgb(56, 56, 56);
            text = light ? Color.FromArgb(28, 28, 28) : Color.FromArgb(242, 242, 242);
            line = light ? Color.FromArgb(222, 222, 222) : Color.FromArgb(64, 64, 64);
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(back);
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.AffectedBounds); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using (var p = new Pen(line)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? text : Color.FromArgb(140, text); base.OnRenderItemText(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            using (var p = new Pen(text, 1.6f))
            {
                Rectangle r = e.ImageRectangle;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(p, new[] { new Point(r.Left + 3, r.Top + r.Height / 2), new Point(r.Left + r.Width / 2 - 1, r.Bottom - 4), new Point(r.Right - 3, r.Top + 4) });
            }
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var p = new Pen(line)) e.Graphics.DrawLine(p, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            using (var b = new SolidBrush(e.Item.Selected && e.Item.Enabled ? hover : back))
                e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
        }
    }
}
