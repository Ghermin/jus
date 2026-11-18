using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Jus")]
[assembly: AssemblyProduct("Jus")]
[assembly: AssemblyDescription("Le niveau de batterie de tous tes appareils dans la barre des tâches")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]

namespace Jus
{
    static class Program
    {
        const string MutexName = @"Local\Jus.Running", QuitName = @"Local\Jus.Quit";

        [STAThread]
        static int Main(string[] args)
        {
            int scan = Array.IndexOf(args, "--scan");
            if (scan >= 0) return Scan(scan + 1 < args.Length ? args[scan + 1] : "jus-scan.txt");
            int preview = Array.IndexOf(args, "--preview");
            if (preview >= 0) return Preview(preview + 1 < args.Length ? args[preview + 1] : "jus-preview.png");
            int previewMenu = Array.IndexOf(args, "--preview-menu");
            if (previewMenu >= 0) return PreviewMenu(previewMenu + 1 < args.Length ? args[previewMenu + 1] : "jus-menu.png");
            if (args.Contains("--quit")) { Signal(); return 0; }
            if (args.Contains("--uninstall")) { Installer.Uninstall(); return 0; }
            if (!args.Contains("--portable") && !Installer.IsInstalled) { Installer.Install(Signal); return 0; }

            using (var mutex = new Mutex(true, MutexName, out bool first))
            {
                if (!first) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitName))
                    Application.Run(new TrayApp(quit));
            }
            return 0;
        }

        /// <summary>Demande à la copie en cours de se fermer et attend qu'elle soit partie.</summary>
        static void Signal()
        {
            if (EventWaitHandle.TryOpenExisting(QuitName, out EventWaitHandle quit))
                using (quit) quit.Set();
            for (int i = 0; i < 30 && Mutex.TryOpenExisting(MutexName, out Mutex m); i++)
            {
                m.Dispose();
                Thread.Sleep(100);
            }
        }

        /// <summary>Diagnostic : écrit dans un fichier ce que chaque source a trouvé.</summary>
        static int Scan(string file)
        {
            using (var log = new StreamWriter(file, false, new UTF8Encoding(false)))
            {
                var monitor = new BatteryMonitor();
                List<BatteryReading> found = monitor.Poll(log);
                log.WriteLine();
                log.WriteLine("=== Résultat ===");
                foreach (BatteryReading r in found)
                    log.WriteLine($"{r.Kind,-9} {r.Text,-5} {(r.Charging ? "charge" : "      ")} {r.Name} [{r.Source}]");
                log.WriteLine();
                log.WriteLine("=== Collections HID ===");
                foreach (HidInfo h in Hid.Enumerate().OrderBy(h => h.Vid).ThenBy(h => h.Pid))
                    log.WriteLine($"{h.Vid:X4}:{h.Pid:X4} page {h.UsagePage:X4}/{h.Usage:X4} in {h.InputLen} out {h.OutputLen} feat {h.FeatureLen} bt={h.IsBluetooth} {h.Product}");
            }
            return 0;
        }

        /// <summary>Diagnostic : dessine le panneau avec des appareils d'exemple, en sombre et en clair.</summary>
        static int PreviewMenu(string file)
        {
            Application.EnableVisualStyles();
            using (Bitmap dark = RenderMenu(Palette.Dark))
            using (Bitmap light = RenderMenu(Palette.Light))
            using (var sheet = new Bitmap(dark.Width + light.Width + 60, Math.Max(dark.Height, light.Height) + 40))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(128, 128, 128));
                g.DrawImage(dark, 20, 20);
                g.DrawImage(light, dark.Width + 40, 20);
                sheet.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }
            return 0;
        }

        static Bitmap RenderMenu(Palette palette)
        {
            var samples = new List<BatteryReading>
            {
                new BatteryReading { Id = "a", Name = "Souris sans fil", Kind = DeviceKind.Mouse, Percent = 93 },
                new BatteryReading { Id = "b", Name = "Un casque avec un nom vraiment très long pour tester", Kind = DeviceKind.Headset, Percent = 30, Charging = true },
                new BatteryReading { Id = "c", Name = "Écouteurs", Kind = DeviceKind.Earbuds, Percent = 12 },
                new BatteryReading { Id = "d", Name = "Manette", Kind = DeviceKind.Gamepad, Percent = 65, Approximate = true },
                new BatteryReading { Id = "f", Name = "8BitDo Ultimate 2C", Kind = DeviceKind.Gamepad, NoLevel = true, PluggedIn = true },
                new BatteryReading { Id = "g", Name = "8BitDo Ultimate 2C ", Kind = DeviceKind.Gamepad, NoLevel = true },
                new BatteryReading { Id = "e", Name = "Clavier masqué", Kind = DeviceKind.Keyboard, Percent = 64 },
            };
            var theme = new MenuTheme(palette);
            var menu = new PanelMenu();
            theme.Attach(menu);
            TrayApp.Fill(menu, theme, samples, new[] { "e" }, true, true, new PanelActions());
            _ = menu.Handle;
            menu.PerformLayout();
            Size size = menu.GetPreferredSize(Size.Empty);
            menu.Size = size;
            menu.PerformLayout();
            var bmp = new Bitmap(size.Width, size.Height);
            menu.DrawToBitmap(bmp, new Rectangle(Point.Empty, size));
            return bmp;
        }

        /// <summary>Diagnostic : dessine la barre avec des appareils d'exemple, en sombre et en clair.</summary>
        static int Preview(string file)
        {
            var samples = new List<BatteryReading>
            {
                new BatteryReading { Kind = DeviceKind.Mouse, Percent = 93 },
                new BatteryReading { Kind = DeviceKind.Keyboard, Percent = 64 },
                new BatteryReading { Kind = DeviceKind.Headset, Percent = 30, Charging = true },
                new BatteryReading { Kind = DeviceKind.Earbuds, Percent = 12 },
                new BatteryReading { Kind = DeviceKind.Gamepad, Percent = 65, Approximate = true },
                new BatteryReading { Kind = DeviceKind.Laptop, Percent = 81 },
                new BatteryReading { Kind = DeviceKind.Gamepad, NoLevel = true, PluggedIn = true },
                new BatteryReading { Kind = DeviceKind.Gamepad, NoLevel = true },
            };
            const int height = 60;
            const float scale = 1.25f;
            using (Bitmap dark = TaskbarWidget.Paint(samples, height, scale, false))
            using (Bitmap light = TaskbarWidget.Paint(samples, height, scale, true))
            using (var sheet = new Bitmap(dark.Width + 40, height * 2))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(28, 28, 28));
                g.FillRectangle(new SolidBrush(Color.FromArgb(243, 243, 243)), 0, height, sheet.Width, height);
                g.DrawImage(dark, 20, 0);
                g.DrawImage(light, 20, height);
                sheet.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }
            return 0;
        }
    }

    /// <summary>Installation pour l'utilisateur courant : aucune fenêtre administrateur.</summary>
    static class Installer
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Jus";
        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Jus");
        static readonly string Exe = Path.Combine(Dir, "Jus.exe");
        static readonly string Shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Jus.lnk");

        public static bool IsInstalled => string.Equals(Path.GetFullPath(Application.ExecutablePath), Exe, StringComparison.OrdinalIgnoreCase);

        public static bool AutoStart
        {
            get { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue("Jus") != null; }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue("Jus", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("Jus", false);
                }
            }
        }

        public static void Install(Action stopRunning)
        {
            stopRunning();
            Directory.CreateDirectory(Dir);
            File.Copy(Application.ExecutablePath, Exe, true);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue("Jus", "\"" + Exe + "\"");
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Jus");
                k.SetValue("DisplayIcon", Exe);
                k.SetValue("DisplayVersion", Application.ProductVersion);
                k.SetValue("Publisher", "Ghermin");
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", "\"" + Exe + "\" --uninstall");
                k.SetValue("NoModify", 1);
                k.SetValue("NoRepair", 1);
            }
            CreateShortcut();
            Process.Start(new ProcessStartInfo(Exe) { UseShellExecute = true });
        }

        static void CreateShortcut()
        {
            try
            {
                Type shell = Type.GetTypeFromProgID("WScript.Shell");
                object instance = Activator.CreateInstance(shell);
                object link = shell.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, instance, new object[] { Shortcut });
                link.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Exe });
                link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            catch (Exception) { /* le raccourci est un confort, pas une condition */ }
        }

        public static void Uninstall()
        {
            if (EventWaitHandle.TryOpenExisting(@"Local\Jus.Quit", out EventWaitHandle quit))
                using (quit) quit.Set();
            Thread.Sleep(800);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey)) k.DeleteValue("Jus", false);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            try { File.Delete(Shortcut); } catch (IOException) { }
            // L'exe ne peut pas s'effacer lui-même : cmd s'en charge une fois l'appli fermée.
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{Dir}\"") { CreateNoWindow = true, UseShellExecute = false });
        }
    }
}
