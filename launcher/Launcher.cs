using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Midnight Club: Los Santos Launcher")]
[assembly: AssemblyProduct("Midnight Club: Los Santos")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace MidnightClubLauncher
{
    /// <summary>Starts GTA V Enhanced straight into Midnight Club: writes scripts\MidnightClub\autostart.flag, which the
    /// mod reads on load (it keeps the screen black and enters Midnight Club as soon as the game is ready), then launches
    /// the game through Steam (falling back to PlayGTAV.exe). "Story Mode" launches without the flag.</summary>
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm());
        }
    }

    internal sealed class LauncherForm : Form
    {
        private const string SteamAppId = "3240220";
        private static readonly Color Bg = Color.FromArgb(9, 8, 14), Violet = Color.FromArgb(168, 85, 247), Cyan = Color.FromArgb(34, 211, 238), Muted = Color.FromArgb(150, 145, 172);
        private readonly string gameDir;
        private string status = "";
        private Rectangle playRect, storyRect, settingsRect, folderRect, closeRect;
        private int hover = -1; private Point dragFrom; private bool dragging;
        private readonly Timer anim = new Timer { Interval = 16 };
        private float t;
        private readonly Font titleFont, subFont, btnFont, smallFont;

        public LauncherForm()
        {
            Text = "Midnight Club: Los Santos";
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1000, 560); DoubleBuffered = true; BackColor = Bg;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            titleFont = PickFont(64f, FontStyle.Bold, "Bahnschrift SemiBold Condensed", "Bahnschrift", "Impact", "Segoe UI");
            subFont = PickFont(16f, FontStyle.Regular, "Bahnschrift SemiLight Condensed", "Bahnschrift", "Segoe UI");
            btnFont = PickFont(22f, FontStyle.Bold, "Bahnschrift SemiBold Condensed", "Bahnschrift", "Segoe UI");
            smallFont = PickFont(11f, FontStyle.Regular, "Segoe UI");
            gameDir = FindGame();
            status = gameDir == null ? "GTA V Enhanced not found. Put this launcher in the game folder." :
                     !File.Exists(Path.Combine(gameDir, "scripts", "MidnightClub.dll")) ? "MidnightClub.dll is missing from the scripts folder." : "Ready.";
            anim.Tick += (s, e) => { t += 0.016f; Invalidate(); };
            anim.Start();
        }

        private static Font PickFont(float size, FontStyle style, params string[] names)
        {
            using (var fc = new InstalledFontCollection())
                foreach (var n in names)
                    foreach (var f in fc.Families)
                        if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return new Font(f, size, f.IsStyleAvailable(style) ? style : FontStyle.Regular);
            return new Font(FontFamily.GenericSansSerif, size, style);
        }

        private static string FindGame()
        {
            string here = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(here, "GTA5_Enhanced.exe"))) return here;
            try
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(@"SOFTWARE\Rockstar Games\GTA V Enhanced"))
                    {
                        var p = k == null ? null : k.GetValue("InstallFolderSteam") as string ?? k.GetValue("InstallFolder") as string;
                        if (!string.IsNullOrEmpty(p) && File.Exists(Path.Combine(p, "GTA5_Enhanced.exe"))) return p;
                    }
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------------ actions
        private void Launch(bool midnight)
        {
            if (gameDir == null) { status = "GTA V Enhanced not found. Put this launcher in the game folder."; return; }
            try
            {
                string flag = Path.Combine(gameDir, "scripts", "MidnightClub", "autostart.flag");
                if (midnight) { Directory.CreateDirectory(Path.GetDirectoryName(flag)); File.WriteAllText(flag, DateTime.UtcNow.ToString("o")); }
                else if (File.Exists(flag)) File.Delete(flag);
                status = midnight ? "Starting Midnight Club..." : "Starting Story Mode...";
                Invalidate(); Update();
                try { Process.Start(new ProcessStartInfo("steam://rungameid/" + SteamAppId) { UseShellExecute = true }); }
                catch { Process.Start(new ProcessStartInfo(Path.Combine(gameDir, "PlayGTAV.exe")) { WorkingDirectory = gameDir, UseShellExecute = true }); }
                var close = new Timer { Interval = 2500 }; close.Tick += (s, e) => Close(); close.Start();
            }
            catch (Exception ex) { status = "Could not start the game: " + ex.Message; }
        }

        private void OpenSettings()
        {
            if (gameDir == null) return;
            string cfg = Path.Combine(gameDir, "scripts", "MidnightClub", "config.ini");
            if (File.Exists(cfg)) Process.Start(new ProcessStartInfo("notepad.exe", "\"" + cfg + "\"") { UseShellExecute = true });
            else status = "config.ini is created the first time the mod runs.";
        }

        // ------------------------------------------------------------------ input
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) { Location = new Point(Location.X + e.X - dragFrom.X, Location.Y + e.Y - dragFrom.Y); return; }
            int h = playRect.Contains(e.Location) ? 0 : storyRect.Contains(e.Location) ? 1 : settingsRect.Contains(e.Location) ? 2 : folderRect.Contains(e.Location) ? 3 : closeRect.Contains(e.Location) ? 4 : -1;
            if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; }
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (hover < 0 && e.Button == MouseButtons.Left) { dragging = true; dragFrom = e.Location; } }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            if (e.Button != MouseButtons.Left) return;
            switch (hover)
            {
                case 0: Launch(true); break;
                case 1: Launch(false); break;
                case 2: OpenSettings(); break;
                case 3: if (gameDir != null) Process.Start(new ProcessStartInfo(Path.Combine(gameDir, "scripts", "MidnightClub")) { UseShellExecute = true }); break;
                case 4: Close(); break;
            }
        }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter) Launch(true); if (e.KeyCode == Keys.Escape) Close(); }

        // ------------------------------------------------------------------ drawing
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int W = ClientSize.Width, H = ClientSize.Height;
            // night sky gradient + violet glow
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(24, 10, 44), Bg, 90f)) g.FillRectangle(bg, 0, 0, W, H);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-W / 4, H / 3, W * 3 / 2, H);
                using (var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb(90, 120, 40, 200), SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) } }) g.FillPath(pg, path);
            }
            // speed streaks
            var rnd = new Random(7);
            for (int i = 0; i < 26; i++)
            {
                float sp = 120f + (float)rnd.NextDouble() * 380f, len = 60f + (float)rnd.NextDouble() * 220f, y = 120f + (float)rnd.NextDouble() * (H - 200f);
                float x = W + len - ((t * sp + (float)rnd.NextDouble() * 2000f) % (W + len * 2));
                int a = 30 + rnd.Next(60);
                using (var p = new Pen(Color.FromArgb(a, i % 4 == 0 ? Cyan : Violet), 1.2f)) g.DrawLine(p, x, y, x + len, y);
            }
            // skyline
            var sky = new Random(3); float sx = 0;
            using (var b = new SolidBrush(Color.FromArgb(255, 18, 9, 34)))
                while (sx < W) { float bw = 20 + (float)sky.NextDouble() * 60, bh = 30 + (float)sky.NextDouble() * 120; g.FillRectangle(b, sx, H - 70 - bh, bw, bh + 70); sx += bw + 4; }
            using (var b = new SolidBrush(Color.FromArgb(240, 9, 8, 14))) g.FillRectangle(b, 0, H - 70, W, 70);

            // wordmark
            float lx = 64, ly = 70;
            var s1 = g.MeasureString("MIDNIGHT", titleFont);
            using (var w = new SolidBrush(Color.White)) g.DrawString("MIDNIGHT", titleFont, w, lx, ly);
            using (var v = new SolidBrush(Violet)) g.DrawString("CLUB", titleFont, v, lx + s1.Width - 18, ly);
            var s2 = g.MeasureString("CLUB", titleFont); float total = s1.Width - 18 + s2.Width - 8;
            DrawSkew(g, lx + 6, ly + s1.Height - 6, total * 0.62f, 6, Violet);
            DrawSkew(g, lx + 6 + total * 0.64f, ly + s1.Height - 6, total * 0.08f, 6, Cyan);
            using (var m = new SolidBrush(Muted)) g.DrawString("LOS SANTOS", subFont, m, lx + 4, ly + s1.Height + 8);

            // buttons
            playRect = new Rectangle(64, 300, 340, 64);
            storyRect = new Rectangle(64, 378, 340, 46);
            settingsRect = new Rectangle(64, 434, 165, 40);
            folderRect = new Rectangle(239, 434, 165, 40);
            closeRect = new Rectangle(W - 46, 10, 34, 30);
            Button(g, playRect, "PLAY MIDNIGHT CLUB", true, hover == 0);
            Button(g, storyRect, "STORY MODE", false, hover == 1);
            Button(g, settingsRect, "SETTINGS", false, hover == 2, true);
            Button(g, folderRect, "MOD FOLDER", false, hover == 3, true);
            using (var p = new Pen(hover == 4 ? Color.White : Muted, 2f)) { g.DrawLine(p, closeRect.X + 10, closeRect.Y + 8, closeRect.Right - 10, closeRect.Bottom - 8); g.DrawLine(p, closeRect.Right - 10, closeRect.Y + 8, closeRect.X + 10, closeRect.Bottom - 8); }

            using (var m = new SolidBrush(Muted)) g.DrawString(status, smallFont, m, 66, H - 46);
            using (var m = new SolidBrush(Color.FromArgb(110, Muted))) g.DrawString("Enter = play   ·   Esc = close", smallFont, m, W - 230, H - 46);
            using (var p = new Pen(Color.FromArgb(70, Violet), 1f)) g.DrawRectangle(p, 0, 0, W - 1, H - 1);
        }

        private static void DrawSkew(Graphics g, float x, float y, float w, float h, Color c)
        {
            using (var b = new SolidBrush(c)) g.FillPolygon(b, new[] { new PointF(x + h * 0.5f, y), new PointF(x + w + h * 0.5f, y), new PointF(x + w, y + h), new PointF(x, y + h) });
        }

        private void Button(Graphics g, Rectangle r, string text, bool primary, bool hot, bool small = false)
        {
            float sl = r.Height * 0.28f;
            var pts = new[] { new PointF(r.X + sl, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right - sl, r.Bottom), new PointF(r.X, r.Bottom) };
            Color fill = primary ? (hot ? Color.FromArgb(190, 120, 255) : Violet) : (hot ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(28, 255, 255, 255));
            using (var b = new SolidBrush(fill)) g.FillPolygon(b, pts);
            if (primary)
            {
                // light sweep
                float sw = ((t * 0.6f) % 1.6f) / 1.6f; float bx = r.X + (r.Width + 80) * sw - 60;
                var region = g.Clip; using (var gp = new GraphicsPath()) { gp.AddPolygon(pts); g.SetClip(gp); }
                using (var lb = new LinearGradientBrush(new RectangleF(bx, r.Y, 60, r.Height), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(70, 255, 255, 255), 0f) { WrapMode = WrapMode.TileFlipX })
                    g.FillRectangle(lb, bx, r.Y, 60, r.Height);
                g.Clip = region;
            }
            var f = small ? smallFont : btnFont;
            var sz = g.MeasureString(text, f);
            using (var b = new SolidBrush(primary ? Color.White : (hot ? Color.White : Color.FromArgb(220, 220, 230))))
                g.DrawString(text, f, b, r.X + (r.Width - sz.Width) / 2f + sl * 0.25f, r.Y + (r.Height - sz.Height) / 2f);
        }
    }
}
