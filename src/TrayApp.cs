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
    /// <summary>Ce que déclenchent les entrées du panneau.</summary>
    sealed class PanelActions
    {
        public Action<BatteryReading> ToggleDevice = r => { };
        public Action Refresh = () => { }, ToggleAlerts = () => { }, ToggleAutoStart = () => { }, Quit = () => { }, EightBitDo = () => { };
    }

    /// <summary>L'appli : icône près de l'horloge, panneau, alertes et réglages.</summary>
    sealed class TrayApp : ApplicationContext
    {
        public const int AlertBelow = 20;

        readonly Control ui = new Control();
        readonly BatteryMonitor monitor = new BatteryMonitor();
        readonly TaskbarWidget widget = new TaskbarWidget();
        readonly DeviceChangeWindow devices = new DeviceChangeWindow();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly PanelMenu menu = new PanelMenu();
        readonly MenuTheme theme = new MenuTheme();
        readonly Settings settings = Settings.Load();
        readonly HashSet<string> alerted = new HashSet<string>();
        readonly EventWaitHandle quit;
        readonly RegisteredWaitHandle quitWait;
        readonly PanelActions actions;
        List<BatteryReading> readings = new List<BatteryReading>();
        EightBitDoWizard wizard;

        public TrayApp(EventWaitHandle quitSignal)
        {
            quit = quitSignal;
            ui.CreateControl();
            _ = ui.Handle;

            actions = new PanelActions
            {
                ToggleDevice = ToggleDevice,
                Refresh = monitor.Refresh,
                ToggleAlerts = () => { settings.Alerts = !settings.Alerts; settings.Save(); },
                ToggleAutoStart = () => Installer.AutoStart = !Installer.AutoStart,
                Quit = Quit,
                EightBitDo = () => { menu.Close(); ShowWizard(); },
            };

            theme.Attach(menu);
            menu.Opening += (s, e) =>
            {
                Fill(menu, theme, readings, settings.Hidden, settings.Alerts, Installer.AutoStart, actions);
                e.Cancel = false;
            };

            tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            tray.Text = "Jus";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) OpenMenu(Cursor.Position); };
            widget.Clicked += () => OpenMenu(widget.PanelAnchor);

            monitor.Updated += list => ui.BeginInvoke((Action)(() => Apply(list)));
            devices.Changed += monitor.DevicesChanged;
            monitor.Start();

            quitWait = ThreadPool.RegisterWaitForSingleObject(quit, (s, t) => ui.BeginInvoke((Action)Quit), null, -1, true);

            if (Installer.IsInstalled)
            {
                ThreadPool.QueueUserWorkItem(_ => { Updater.CleanUp(); Installer.RepairAutoStart(); });
                updates = new System.Threading.Timer(_ => CheckForUpdate(), null, UpdateFirstMs, UpdateEveryMs);
            }
        }

        const int UpdateFirstMs = 2 * 60 * 1000, UpdateEveryMs = 6 * 60 * 60 * 1000;
        System.Threading.Timer updates;

        /// <summary>Sur un fil à part : la nouvelle version ferme celle-ci par le signal de fermeture habituel.</summary>
        static void CheckForUpdate()
        {
            Updater.CleanUp();
            string exe = Updater.Fetch();
            if (exe != null) Updater.Apply(exe);
        }

        void Apply(List<BatteryReading> list)
        {
            readings = list;
            widget.Show(list.Where(r => !settings.Hidden.Contains(r.Id)));
            if (menu.Visible) Fill(menu, theme, readings, settings.Hidden, settings.Alerts, Installer.AutoStart, actions); // le panneau reste à jour s'il est ouvert
            tray.Text = Shorten(list.Count == 0 ? "Jus : aucun appareil détecté" : string.Join("\n", list.Select(r => r.Name + " : " + r.Text)));

            // Une 8BitDo sans niveau : on propose le patch une fois de soi-même, ensuite il reste dans le panneau.
            if (list.Any(r => r.Patchable) && !settings.EightBitDoOffered)
            {
                settings.EightBitDoOffered = true;
                settings.Save();
                ShowWizard();
            }

            foreach (BatteryReading r in list)
            {
                if (r.NoLevel) continue;
                if (r.Charging || r.Percent > AlertBelow + 5) alerted.Remove(r.Id);
                else if (settings.Alerts && r.Percent <= AlertBelow && alerted.Add(r.Id))
                    tray.ShowBalloonTip(8000, "Batterie faible", r.Name + " : plus que " + r.Text, ToolTipIcon.Warning);
            }
        }

        void ShowWizard()
        {
            if (wizard != null && !wizard.IsDisposed) { wizard.Activate(); return; }
            wizard = new EightBitDoWizard();
            wizard.FormClosed += (s, e) => { wizard = null; monitor.Refresh(); };
            wizard.Show();
            wizard.Activate();
        }

        /// <summary>NotifyIcon.Text lève une exception à partir de 64 caractères.</summary>
        static string Shorten(string s) => s.Length <= 63 ? s : s.Substring(0, 62) + "…";

        void OpenMenu(Point at)
        {
            if (menu.Visible) { menu.Close(); return; }
            if (menu.JustDismissed) return;
            Native.SetForegroundWindow(ui.Handle);
            menu.Show(at, ToolStripDropDownDirection.AboveLeft);
        }

        /// <summary>Remplit le panneau. Séparé de l'appli pour que --preview-menu dessine exactement le même.</summary>
        public static void Fill(ContextMenuStrip menu, MenuTheme theme, IList<BatteryReading> list, ICollection<string> hidden, bool alerts, bool autoStart, PanelActions actions)
        {
            menu.Items.Clear();
            string count = list.Count == 0 ? "aucun appareil" : list.Count == 1 ? "1 appareil" : list.Count + " appareils";
            menu.Items.Add(new HeaderRow(theme, count));
            menu.Items.Add(new ToolStripSeparator());

            if (list.Count == 0)
                menu.Items.Add(new ToolStripMenuItem("Allume un appareil sans fil pour le voir ici") { Enabled = false });
            foreach (BatteryReading r in list)
            {
                BatteryReading item = r;
                menu.Items.Add(new DeviceRow(item, theme, !hidden.Contains(item.Id), () => actions.ToggleDevice(item)));
            }

            if (list.Any(r => r.Patchable))
                menu.Items.Add(Entry("Afficher la batterie de la 8BitDo…", actions.EightBitDo));

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Entry("Actualiser", actions.Refresh));
            menu.Items.Add(Toggle($"Prévenir sous {AlertBelow} %", alerts, actions.ToggleAlerts));
            menu.Items.Add(Toggle("Lancer avec Windows", autoStart, actions.ToggleAutoStart));
            menu.Items.Add(Entry("Quitter", actions.Quit));
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem sig = Entry("un projet du bored dev playground", () =>
            {
                menu.Close();
                Process.Start(new ProcessStartInfo("https://ghermin.github.io/") { UseShellExecute = true });
            });
            sig.Font = MenuTheme.MonoFont;
            sig.Tag = "sig";
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
            item.CheckOnClick = true; // la coche change sur place, le panneau reste ouvert
            return item;
        }

        void Quit()
        {
            quitWait.Unregister(null);
            updates?.Dispose();
            monitor.Dispose();
            widget.Dispose();
            if (wizard != null && !wizard.IsDisposed) wizard.Close();
            tray.Visible = false;
            tray.Dispose();
            ExitThread();
        }
    }

    /// <summary>Fenêtre invisible que Windows prévient dès qu'un appareil est branché ou retiré.</summary>
    sealed class DeviceChangeWindow : NativeWindow
    {
        const int WM_DEVICECHANGE = 0x219;

        public event Action Changed;

        public DeviceChangeWindow() { CreateHandle(new CreateParams { Caption = "Jus.Devices" }); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE) Changed?.Invoke();
            base.WndProc(ref m);
        }
    }

    /// <summary>Réglages : un petit fichier texte dans %APPDATA%\Jus.</summary>
    sealed class Settings
    {
        static readonly string File_ = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Jus", "settings.txt");
        public bool Alerts = true;
        /// <summary>Le patch de la 8BitDo a déjà été proposé : on ne rouvre plus la fenêtre de soi-même.</summary>
        public bool EightBitDoOffered;
        public readonly HashSet<string> Hidden = new HashSet<string>();

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                foreach (string line in File.ReadAllLines(File_))
                {
                    if (line == "alerts=0") s.Alerts = false;
                    else if (line == "8bitdo=offered") s.EightBitDoOffered = true;
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
                File.WriteAllLines(File_, new[] { "alerts=" + (Alerts ? 1 : 0) }.Concat(EightBitDoOffered ? new[] { "8bitdo=offered" } : new string[0]).Concat(Hidden.Select(h => "hidden=" + h)));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
