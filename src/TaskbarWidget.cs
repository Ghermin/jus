using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Jus
{
    /// <summary>Fenêtre transparente posée dans la barre des tâches, juste à gauche de la zone de notification.</summary>
    sealed class TaskbarWidget : NativeWindow, IDisposable
    {
        const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000;
        const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80;
        const int WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
        const int WM_MOUSEMOVE = 0x200, WM_MOUSELEAVE = 0x2A3, TME_LEAVE = 2;

        readonly Timer timer = new Timer { Interval = 1000 };
        List<BatteryReading> items = new List<BatteryReading>();
        IntPtr taskbar;
        string painted = "";
        int paintedWidth;
        bool hover;

        /// <summary>Le coin haut droit du widget : le panneau s'ouvre juste au-dessus.</summary>
        public Point PanelAnchor => Native.GetWindowRect(Handle, out Native.RECT r) ? new Point(r.Right, r.Top) : Cursor.Position;

        public event Action Clicked;

        public TaskbarWidget()
        {
            timer.Tick += (s, e) => Sync();
            timer.Start();
            Sync();
        }

        public void Show(IEnumerable<BatteryReading> readings)
        {
            items = readings.ToList();
            Sync();
        }

        /// <summary>Recrée la fenêtre si l'Explorateur a redémarré, puis la replace et la redessine si besoin.</summary>
        void Sync()
        {
            IntPtr bar = Native.FindWindow("Shell_TrayWnd", null);
            if (bar == IntPtr.Zero) return;
            if (Handle != IntPtr.Zero && (!Native.IsWindow(Handle) || Native.GetParent(Handle) != bar))
            {
                ReleaseHandle();
                painted = "";
            }
            if (Handle == IntPtr.Zero)
            {
                taskbar = bar;
                CreateHandle(new CreateParams
                {
                    Caption = "Jus", Parent = bar,
                    Style = WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                    ExStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                });
            }
            Render();
        }

        void Render()
        {
            if (!Native.GetClientRect(taskbar, out Native.RECT client)) return;
            int barHeight = client.Bottom - client.Top;
            float scale = Math.Max(1f, Native.GetDpiForWindow(taskbar) / 96f);
            bool light = LightTaskbar();

            if (items.Count == 0 || barHeight > client.Right - client.Left) // rien à montrer, ou barre verticale
            {
                Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_HIDEWINDOW);
                painted = "";
                return;
            }

            string state = $"{barHeight}|{scale}|{light}|{hover}|" + string.Join(";", items.Select(i => $"{i.Kind},{i.Text},{i.Charging}"));
            if (state == painted)
            {
                Place(paintedWidth, barHeight, scale);
                return;
            }
            using (Bitmap bmp = Paint(items, barHeight, scale, light, hover))
            {
                Place(bmp.Width, barHeight, scale);
                Present(bmp);
                paintedWidth = bmp.Width;
                painted = state;
            }
        }

        /// <summary>À chaque passage : la barre des tâches repasse parfois devant ses fenêtres enfants.</summary>
        void Place(int width, int height, float scale)
        {
            Rectangle t = Target(width, height, scale);
            Native.SetWindowPos(Handle, IntPtr.Zero, t.X, t.Y, t.Width, t.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        /// <summary>Dessine les appareils sur fond transparent.</summary>
        public static Bitmap Paint(IList<BatteryReading> list, int barHeight, float scale, bool light, bool hover = false)
        {
            using (Font font = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                float iconSize = 16 * scale, gap = 4 * scale, itemGap = 12 * scale, bolt = 7 * scale, pad = 6 * scale;
                float[] textWidths;
                using (var probe = new Bitmap(1, 1))
                using (Graphics g = Graphics.FromImage(probe))
                    textWidths = list.Select(i => i.NoLevel ? 14 * scale : g.MeasureString(i.Text, font, PointF.Empty, StringFormat.GenericTypographic).Width + 1).ToArray();

                float total = pad * 2 + itemGap * (list.Count - 1);
                for (int i = 0; i < list.Count; i++)
                    total += iconSize + gap + textWidths[i] + (list[i].Charging ? bolt : 0);

                var bmp = new Bitmap((int)Math.Ceiling(total), barHeight, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.FromArgb(1, 0, 0, 0)); // presque transparent : la zone reste cliquable
                    if (hover) // même survol que la flèche des icônes cachées
                    {
                        float inset = 4 * scale;
                        using (GraphicsPath path = MenuTheme.Rounded(new RectangleF(0, inset, bmp.Width - 1, barHeight - inset * 2), 4 * scale))
                        using (var brush = new SolidBrush(light ? Color.FromArgb(20, 0, 0, 0) : Color.FromArgb(30, 255, 255, 255)))
                            g.FillPath(brush, path);
                    }

                    Color normal = light ? Color.FromArgb(28, 28, 28) : Color.White;
                    Color low = light ? Color.FromArgb(196, 43, 28) : Color.FromArgb(255, 107, 96);
                    float x = pad;
                    for (int i = 0; i < list.Count; i++)
                    {
                        BatteryReading item = list[i];
                        Color c = item.IsLow ? low : normal;
                        Glyphs.Draw(g, item.Kind, x, (barHeight - iconSize) / 2, iconSize, c);
                        x += iconSize + gap;
                        if (item.Charging)
                        {
                            Glyphs.Bolt(g, x, barHeight / 2f, bolt, c);
                            x += bolt;
                        }
                        if (item.PluggedIn)
                            Glyphs.Plug(g, x, (barHeight - 14 * scale) / 2, 14 * scale, c);
                        else if (item.NoLevel)
                            Glyphs.Wireless(g, x, (barHeight - 14 * scale) / 2, 14 * scale, c);
                        else
                            using (var brush = new SolidBrush(c))
                                g.DrawString(item.Text, font, brush, x, (barHeight - font.GetHeight(g)) / 2, StringFormat.GenericTypographic);
                        x += textWidths[i] + itemGap;
                    }
                }
                return bmp;
            }
        }

        Rectangle Target(int width, int height, float scale)
        {
            IntPtr tray = Native.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            int right;
            if (tray != IntPtr.Zero && Native.GetWindowRect(tray, out Native.RECT r))
            {
                var p = new Native.POINT(r.Left, r.Top);
                Native.MapWindowPoints(IntPtr.Zero, taskbar, ref p, 1);
                right = p.X;
            }
            else
            {
                Native.GetClientRect(taskbar, out Native.RECT c);
                right = c.Right - (int)(240 * scale);
            }
            return new Rectangle(right - width - (int)(4 * scale), 0, width, height);
        }

        void Present(Bitmap bmp)
        {
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            IntPtr mem = Native.CreateCompatibleDC(screen);
            IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = Native.SelectObject(mem, hbmp);
            try
            {
                var size = new Native.SIZE(bmp.Width, bmp.Height);
                var src = new Native.POINT(0, 0);
                var blend = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                Native.UpdateLayeredWindow(Handle, screen, IntPtr.Zero, ref size, mem, ref src, 0, ref blend, 2);
            }
            finally
            {
                Native.SelectObject(mem, old);
                Native.DeleteObject(hbmp);
                Native.DeleteDC(mem);
                Native.ReleaseDC(IntPtr.Zero, screen);
            }
        }

        public static bool LightTaskbar()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return k?.GetValue("SystemUsesLightTheme") is int v && v != 0;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
            if (m.Msg == WM_MOUSEMOVE && !hover)
            {
                hover = true;
                var track = new Native.TRACKMOUSEEVENT { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.TRACKMOUSEEVENT)), dwFlags = TME_LEAVE, hwndTrack = Handle };
                Native.TrackMouseEvent(ref track);
                Render();
            }
            if (m.Msg == WM_MOUSELEAVE) { hover = false; Render(); }
            if (m.Msg == WM_LBUTTONUP || m.Msg == WM_RBUTTONUP) { Clicked?.Invoke(); return; }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            timer.Dispose();
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }

    /// <summary>Les petites icônes, dessinées en vectoriel pour rester nettes à toutes les échelles.</summary>
    static class Glyphs
    {
        public static void Draw(Graphics g, DeviceKind kind, float x, float y, float size, Color color)
        {
            GraphicsState saved = g.Save();
            g.TranslateTransform(x, y);
            g.ScaleTransform(size / 16f, size / 16f);
            using (var pen = new Pen(color, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var fill = new SolidBrush(color))
            {
                switch (kind)
                {
                    case DeviceKind.Mouse:
                        Round(g, pen, 4, 1.5f, 8, 13, 4);
                        g.DrawLine(pen, 8, 4, 8, 6.5f);
                        break;
                    case DeviceKind.Headset:
                        g.DrawArc(pen, 2.5f, 2, 11, 11, 180, 180);
                        g.DrawLine(pen, 2.5f, 7.5f, 2.5f, 9);
                        g.DrawLine(pen, 13.5f, 7.5f, 13.5f, 9);
                        Round(g, pen, 1.5f, 9, 3, 5, 1.2f);
                        Round(g, pen, 11.5f, 9, 3, 5, 1.2f);
                        break;
                    case DeviceKind.Earbuds:
                        g.DrawEllipse(pen, 1.5f, 2, 5.5f, 5.5f);
                        g.DrawLine(pen, 4.2f, 7.5f, 4.2f, 14);
                        g.DrawEllipse(pen, 9, 2, 5.5f, 5.5f);
                        g.DrawLine(pen, 11.8f, 7.5f, 11.8f, 14);
                        break;
                    case DeviceKind.Gamepad:
                        Round(g, pen, 1, 4, 14, 8.5f, 4);
                        g.DrawLine(pen, 3.5f, 8.2f, 6.5f, 8.2f);
                        g.DrawLine(pen, 5, 6.7f, 5, 9.7f);
                        g.FillEllipse(fill, 10, 6.2f, 1.7f, 1.7f);
                        g.FillEllipse(fill, 11.6f, 8.4f, 1.7f, 1.7f);
                        break;
                    case DeviceKind.Keyboard:
                        Round(g, pen, 1, 3.5f, 14, 9, 1.8f);
                        g.DrawLine(pen, 4, 6.5f, 4.1f, 6.5f);
                        g.DrawLine(pen, 7, 6.5f, 7.1f, 6.5f);
                        g.DrawLine(pen, 10, 6.5f, 10.1f, 6.5f);
                        g.DrawLine(pen, 12.5f, 6.5f, 12.6f, 6.5f);
                        g.DrawLine(pen, 5, 9.7f, 11, 9.7f);
                        break;
                    case DeviceKind.Laptop:
                        Round(g, pen, 3, 3, 10, 8, 1.2f);
                        g.DrawLine(pen, 1, 13.2f, 15, 13.2f);
                        break;
                    default:
                        Round(g, pen, 1, 4.5f, 12, 7, 1.8f);
                        g.DrawLine(pen, 15, 7, 15, 9);
                        g.FillRectangle(fill, 3, 6.5f, 5, 3);
                        break;
                }
            }
            g.Restore(saved);
        }

        /// <summary>Des ondes : l'appareil est relié sans fil, mais ne transmet pas son niveau.</summary>
        public static void Wireless(Graphics g, float x, float y, float size, Color color)
        {
            GraphicsState saved = g.Save();
            g.TranslateTransform(x, y);
            g.ScaleTransform(size / 16f, size / 16f);
            using (var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var dot = new SolidBrush(color))
            {
                foreach (float r in new[] { 4.5f, 8f, 11.5f })
                    g.DrawArc(pen, 8 - r, 13 - r, r * 2, r * 2, 225, 90);
                g.FillEllipse(dot, 6.8f, 11.8f, 2.4f, 2.4f);
            }
            g.Restore(saved);
        }

        /// <summary>Une prise : l'appareil est relié par un câble.</summary>
        public static void Plug(Graphics g, float x, float y, float size, Color color)
        {
            GraphicsState saved = g.Save();
            g.TranslateTransform(x, y);
            g.ScaleTransform(size / 16f, size / 16f);
            using (var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var body = new GraphicsPath())
            {
                g.DrawLine(pen, 5.5f, 1.5f, 5.5f, 5);
                g.DrawLine(pen, 10.5f, 1.5f, 10.5f, 5);
                body.AddLine(3.5f, 5, 12.5f, 5);
                body.AddLine(12.5f, 5, 12.5f, 7.5f);
                body.AddArc(3.5f, 3, 9, 9, 0, 180);
                body.CloseFigure();
                g.DrawPath(pen, body);
                g.DrawLine(pen, 8, 12, 8, 14.5f);
            }
            g.Restore(saved);
        }

        public static void Bolt(Graphics g, float x, float middle, float size, Color color)
        {
            float s = size / 7f;
            PointF[] p =
            {
                new PointF(x + 3.6f * s, middle - 5 * s), new PointF(x + .4f * s, middle + .8f * s), new PointF(x + 2.8f * s, middle + .8f * s),
                new PointF(x + 2.2f * s, middle + 5 * s), new PointF(x + 5.6f * s, middle - 1 * s), new PointF(x + 3.2f * s, middle - 1 * s),
            };
            using (var brush = new SolidBrush(color)) g.FillPolygon(brush, p);
        }

        static void Round(Graphics g, Pen pen, float x, float y, float w, float h, float r)
        {
            using (var path = new GraphicsPath())
            {
                float d = r * 2;
                path.AddArc(x, y, d, d, 180, 90);
                path.AddArc(x + w - d, y, d, d, 270, 90);
                path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
                path.AddArc(x, y + h - d, d, d, 90, 90);
                path.CloseFigure();
                g.DrawPath(pen, path);
            }
        }
    }
}
