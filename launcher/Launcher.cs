using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Midnight Club: Los Santos Launcher")]
[assembly: AssemblyProduct("Midnight Club: Los Santos")]
[assembly: AssemblyVersion("2.0.0.0")]

namespace MidnightClubLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 5 && args[0] == "--render") { LauncherForm.Render(args[1], int.Parse(args[2]), int.Parse(args[3]), args[4] == "1"); return; }
            Application.Run(new LauncherForm());
        }
    }

    // ====================================================================== config.ini (ScriptHookVDotNet format)
    /// <summary>Line-preserving INI editor: reads and writes single keys without touching anything else.</summary>
    internal sealed class Ini
    {
        private readonly string path; private List<string> lines = new List<string>();
        public bool Exists { get { return File.Exists(path); } }
        public Ini(string path) { this.path = path; Reload(); }
        public void Reload() { lines = path != null && File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>(); }

        private int Find(string sec, string key, out int secEnd)
        {
            secEnd = -1; bool inSec = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string l = lines[i].Trim();
                if (l.StartsWith("[") && l.EndsWith("]")) { if (inSec) { secEnd = i; return -1; } inSec = string.Equals(l.Substring(1, l.Length - 2), sec, StringComparison.OrdinalIgnoreCase); if (inSec) secEnd = i + 1; continue; }
                if (!inSec) continue;
                int eq = l.IndexOf('='); if (eq < 0) { continue; }
                if (string.Equals(l.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)) return i;
                secEnd = i + 1;
            }
            if (inSec) secEnd = lines.Count;
            return -1;
        }

        public string Get(string sec, string key, string def)
        {
            int e; int i = Find(sec, key, out e);
            if (i < 0) return def;
            string l = lines[i]; return l.Substring(l.IndexOf('=') + 1).Trim();
        }

        public void Set(string sec, string key, string value)
        {
            if (path == null) return;
            int e; int i = Find(sec, key, out e);
            string line = key + " = " + value;
            if (i >= 0) lines[i] = line;
            else if (e >= 0) lines.Insert(e, line);
            else { if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add(""); lines.Add("[" + sec + "]"); lines.Add(line); }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);
        }

        public float GetF(string sec, string key, float def) { float v; return float.TryParse(Get(sec, key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def; }
        public bool GetB(string sec, string key, bool def) { string v = Get(sec, key, ""); return v.Length == 0 ? def : v.Equals("true", StringComparison.OrdinalIgnoreCase); }
    }

    // ====================================================================== settings model
    internal enum Kind { Toggle, Slider, Choice, Key }
    internal sealed class Setting
    {
        public string Sec, Key, Label, Desc, Icon; public Kind Kind; public float Min, Max, Step; public string Def; public string[] Options; public string Fmt = "0.0";
        public Func<Ini, int> GetChoice; public Action<Ini, int> SetChoice;    // custom choice (driving model)
    }
    internal sealed class Category { public string Name, Icon; public Color Accent; public List<Setting> Items = new List<Setting>(); }

    // ====================================================================== the window
    internal sealed class LauncherForm : Form
    {
        private const string SteamAppId = "3240220";
        private readonly string gameDir, modDir;
        private readonly Ini ini;
        private readonly List<Category> cats = new List<Category>();

        // theme
        private bool light;
        private static readonly Color Red = Color.FromArgb(255, 59, 92), Cyan = Color.FromArgb(34, 184, 240), Amber = Color.FromArgb(255, 176, 32), Green = Color.FromArgb(61, 220, 151), Violet = Color.FromArgb(139, 124, 255);
        private Color Text1 { get { return light ? Color.FromArgb(14, 16, 22) : Color.FromArgb(245, 246, 250); } }
        private Color Text2 { get { return light ? Color.FromArgb(70, 76, 92) : Color.FromArgb(172, 177, 194); } }
        private Color Text3 { get { return light ? Color.FromArgb(130, 135, 150) : Color.FromArgb(112, 117, 136); } }
        private Color GlassTint { get { return light ? Color.FromArgb(178, 250, 251, 253) : Color.FromArgb(168, 14, 16, 24); } }
        private Color GlassStrong { get { return light ? Color.FromArgb(220, 252, 252, 254) : Color.FromArgb(214, 16, 18, 26); } }
        private Color Line { get { return light ? Color.FromArgb(40, 10, 14, 30) : Color.FromArgb(46, 255, 255, 255); } }
        private Color Hi { get { return light ? Color.FromArgb(220, 255, 255, 255) : Color.FromArgb(50, 255, 255, 255); } }
        private Color Track { get { return light ? Color.FromArgb(40, 10, 14, 30) : Color.FromArgb(60, 255, 255, 255); } }
        private static Color On(Color c) { return c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 160 ? Color.FromArgb(14, 16, 22) : Color.White; }

        // assets
        private readonly Dictionary<string, Image> img = new Dictionary<string, Image>();
        private readonly PrivateFontCollection[] pfc = new PrivateFontCollection[5];
        private Font fHero, fH1, fH2, fBtn, fBody, fSmall, fLabel, fCaps;

        // state
        private int page;            // 0 home, 1 settings, 2 about
        private int cat;
        private float scroll, scrollTarget;
        private string capture;      // key-binding capture: "Sec/Key"
        private string toast; private DateTime toastAt;
        private string status;
        private readonly Dictionary<string, float> hot = new Dictionary<string, float>();
        private string hoverId, pressId, dragId;
        private sealed class Hit { public RectangleF R; public string Id; public Action Click; public Action<float> Drag; }
        private readonly List<Hit> hits = new List<Hit>();
        private Point dragFrom; private bool moving;
        private readonly Timer anim = new Timer { Interval = 15 };
        private DateTime started = DateTime.Now;

        public LauncherForm()
        {
            Text = "Midnight Club: Los Santos";
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1180, 720); DoubleBuffered = true; KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            LoadAssets();
            gameDir = FindGame();
            modDir = gameDir != null ? Path.Combine(gameDir, "scripts", "MidnightClub") : null;
            ini = new Ini(modDir != null ? Path.Combine(modDir, "config.ini") : null);
            BuildSettings();
            ApplyTheme();
            status = gameDir == null ? "GTA V Enhanced not found: put the launcher in the game folder." : "Ready to race.";
            anim.Tick += (s, e) => { Animate(); Invalidate(); };
            anim.Start();
            MouseWheel += (s, e) => { if (page == 1) scrollTarget = Math.Max(0, scrollTarget - e.Delta * 0.6f); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int pref = 2; DwmSetWindowAttribute(Handle, 33, ref pref, 4); } catch { }       // Windows 11 rounded window
            try { int dark = light ? 0 : 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }   // drop shadow

        // ------------------------------------------------------------------ assets
        private void LoadAssets()
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var n in asm.GetManifestResourceNames())
            {
                if (n.EndsWith(".png") || n.EndsWith(".jpg"))
                    using (var s = asm.GetManifestResourceStream(n)) img[n.Substring(0, n.Length - 4)] = new Bitmap(Image.FromStream(s));
            }
            string[] fonts = { "font.barlow-400.ttf", "font.barlow-500.ttf", "font.barlow-600.ttf", "font.barlow-condensed-600.ttf", "font.barlow-condensed-700.ttf" };
            for (int i = 0; i < fonts.Length; i++)
            {
                pfc[i] = new PrivateFontCollection();
                try
                {
                    // fonts are written to %TEMP% and loaded from file (more reliable than memory fonts for GDI+ text)
                    string tf = Path.Combine(Path.GetTempPath(), "MidnightClubLauncher", fonts[i].Substring(5));
                    if (!File.Exists(tf))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(tf));
                        using (var s = asm.GetManifestResourceStream(fonts[i])) using (var o = File.Create(tf)) s.CopyTo(o);
                    }
                    pfc[i].AddFontFile(tf);
                }
                catch { }
            }
            Func<int, float, Font> F = (i, size) =>
            {
                try
                {   // a system-installed copy of the same family wins (preview builds); otherwise the bundled file
                    string want = pfc[i].Families.Length > 0 ? pfc[i].Families[0].Name : null;
                    using (var ifc = new InstalledFontCollection())
                        foreach (var f in ifc.Families)
                            if (want != null && f.Name == want) foreach (FontStyle st in new[] { FontStyle.Regular, FontStyle.Bold }) if (f.IsStyleAvailable(st)) return new Font(f, size, st, GraphicsUnit.Pixel);
                }
                catch { }
                try { var fam = pfc[i].Families[0]; foreach (FontStyle st in new[] { FontStyle.Regular, FontStyle.Bold }) if (fam.IsStyleAvailable(st)) return new Font(fam, size, st, GraphicsUnit.Pixel); }
                catch { }
                return new Font("Segoe UI", size * 0.8f, FontStyle.Regular, GraphicsUnit.Pixel);
            };
            fHero = F(4, 96f); fH1 = F(4, 40f); fH2 = F(3, 24f); fBtn = F(4, 24f); fCaps = F(3, 14f);
            fBody = F(0, 16f); fLabel = F(2, 17f); fSmall = F(0, 13.5f);
        }

        private void ApplyTheme()
        {
            string t = ini.Get("HUD", "Theme", "Auto");
            if (t == "Dark") light = false;
            else if (t == "Light") light = true;
            else
            {
                try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) light = k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 0)) == 1; }
                catch { light = false; }
            }
            BackColor = light ? Color.FromArgb(236, 238, 244) : Color.FromArgb(10, 11, 16);
            if (IsHandleCreated) { try { int dark = light ? 0 : 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { } }
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

        // ------------------------------------------------------------------ settings definitions (mirror Core/ConfigurationManager.cs)
        private void BuildSettings()
        {
            Func<string, string, string, string, string, bool, Setting> T = (sec, key, label, desc, icon, def) => new Setting { Sec = sec, Key = key, Label = label, Desc = desc, Icon = icon, Kind = Kind.Toggle, Def = def ? "True" : "False" };
            Func<string, string, string, string, string, float, float, float, float, Setting> S = (sec, key, label, desc, icon, min, max, step, def) => new Setting { Sec = sec, Key = key, Label = label, Desc = desc, Icon = icon, Kind = Kind.Slider, Min = min, Max = max, Step = step, Def = def.ToString(CultureInfo.InvariantCulture) };

            var drive = new Category { Name = "Driving", Icon = "gauge", Accent = Red };
            drive.Items.Add(new Setting { Sec = "Gameplay", Key = "Difficulty", Label = "Difficulty", Desc = "Opponent pace. Harder AI and fewer aids pay more.", Icon = "flag", Kind = Kind.Choice, Options = new[] { "Novice", "Easy", "Medium", "Hard", "Expert", "Max" },
                GetChoice = c => { int v; return int.TryParse(c.Get("Gameplay", "Difficulty", "2"), out v) ? Math.Max(0, Math.Min(5, v)) : 2; }, SetChoice = (c, i) => c.Set("Gameplay", "Difficulty", i.ToString()) });
            { var a = S("Assists", "TractionControl", "Traction control", "0 = off. Higher levels hold the driven wheels closer to the tyre's peak slip and cut power sooner (on bikes it also stops wheelies).", "car-front", 0f, 10f, 1f, 4f); a.Fmt = "0"; drive.Items.Add(a); }
            { var a = S("Assists", "ABS", "ABS", "0 = off: the wheels can lock. Higher levels keep the braking slip closer to the tyre's peak.", "gauge", 0f, 10f, 1f, 6f); a.Fmt = "0"; drive.Items.Add(a); }
            { var a = S("Assists", "StabilityManagement", "Stability management", "0 = off. Higher levels step in earlier when the car slides: they brake the rotation and cut power.", "sparkles", 0f, 10f, 1f, 3f); a.Fmt = "0"; drive.Items.Add(a); }
            drive.Items.Add(T("Fitment", "Enabled", "Wheel fitment", "Camber, offset and stance from the workshops.", "wrench", true));
            cats.Add(drive);

            var cam = new Category { Name = "Cameras", Icon = "eye", Accent = Cyan };
            cam.Items.Add(T("Camera", "Enabled", "Midnight Club cameras", "Bumper, hood, roof, cockpit and three chase cameras. Off: GTA's own cameras.", "eye", true));
            cam.Items.Add(S("Camera", "DynamicFovIntensity", "Speed FOV", "How much the field of view widens with speed.", "eye", 0f, 2f, 0.1f, 1f));
            cam.Items.Add(S("Camera", "GForceIntensity", "Camera motion", "How much the chase cameras react to acceleration and braking.", "eye", 0f, 2f, 0.1f, 1f));
            cam.Items.Add(S("Camera", "ShakeIntensity", "Camera shake", "Rumble at speed and over rough ground.", "eye", 0f, 2f, 0.1f, 1f));
            var ch = S("Camera", "CockpitHeight", "Cockpit height", "Raise or lower the cockpit camera (metres).", "eye", -0.3f, 0.3f, 0.01f, -0.03f); ch.Fmt = "0.00"; cam.Items.Add(ch);
            var hh = S("Camera", "HoodHeight", "Hood height", "Raise or lower the hood camera (metres).", "eye", -0.3f, 0.3f, 0.01f, 0f); hh.Fmt = "0.00"; cam.Items.Add(hh);
            cats.Add(cam);

            var disp = new Category { Name = "Display", Icon = "monitor", Accent = Violet };
            disp.Items.Add(new Setting { Sec = "HUD", Key = "Theme", Label = "Theme", Desc = "Light or dark glass, in game and here. Auto follows the in-game clock (Windows' setting in the launcher).", Icon = "sun-moon", Kind = Kind.Choice, Options = new[] { "Auto", "Dark", "Light" }, Def = "Auto" });
            disp.Items.Add(new Setting { Sec = "HUD", Key = "ButtonStyle", Label = "Controller buttons", Desc = "Auto shows PlayStation glyphs with a DualShock / DualSense, Xbox otherwise.", Icon = "monitor", Kind = Kind.Choice, Options = new[] { "Auto", "Xbox", "PlayStation" }, Def = "Auto" });
            disp.Items.Add(T("HUD", "Show", "HUD", "Speedometer, minimap and race HUD.", "monitor", true));
            disp.Items.Add(T("HUD", "SpeedEffects", "Speed effects", "Speed lines and edge vignette at high speed.", "sparkles", true));
            disp.Items.Add(T("HUD", "WelcomeScreen", "Welcome screen", "The title card when you switch into Midnight Club.", "sparkles", true));
            disp.Items.Add(S("HUD", "StudioLight", "Showroom lighting", "Brightness of the garage and showroom studio lights.", "sun", 0.5f, 1.5f, 0.1f, 1f));
            cats.Add(disp);

            var world = new Category { Name = "World", Icon = "cloud-sun", Accent = Green };
            world.Items.Add(S("World", "TrafficDensity", "Traffic", "Cars on the road in free roam.", "car-front", 0f, 2f, 0.1f, 1f));
            world.Items.Add(S("World", "RaceTraffic", "Traffic in races", "Cars on the road during races.", "flag", 0f, 2f, 0.1f, 0.8f));
            world.Items.Add(S("World", "PedDensity", "Pedestrians", "People on the pavements.", "users", 0f, 2f, 0.1f, 1f));
            world.Items.Add(S("World", "ParkedDensity", "Parked cars", "Parked cars along the streets.", "car-front", 0f, 2f, 0.1f, 1f));
            world.Items.Add(T("World", "DynamicWeather", "Dynamic weather", "Weather changes over the day, with seasonal bias.", "cloud-sun", true));
            world.Items.Add(T("World", "AlwaysNight", "Always night", "The clock stays between dusk and dawn.", "moon", false));
            world.Items.Add(T("World", "StreetChallenges", "Street challenges", "Racers who rev next to you and want a race.", "flag", true));
            world.Items.Add(T("World", "CarMeets", "Car meets", "Meets around the city on weekends.", "users", true));
            world.Items.Add(T("World", "SpeedTraps", "Speed traps", "Speed cameras with leaderboards.", "gauge", true));
            world.Items.Add(T("World", "LoadOnlineMap", "Online map additions", "Load GTA Online interiors and map pieces.", "map", true));
            cats.Add(world);

            var pol = new Category { Name = "Police", Icon = "siren", Accent = Amber };
            pol.Items.Add(T("Police", "Enabled", "Police", "Police chase you for speeding, crashes and races.", "siren", true));
            pol.Items.Add(S("Police", "Difficulty", "Police difficulty", "How hard pursuits are to shake.", "shield-alert", 0.5f, 2f, 0.1f, 1f));
            var sp = S("Police", "SpeedTriggerKmh", "Speeding trigger", "Speed (km/h) at which a patrol takes notice.", "gauge", 60f, 220f, 5f, 115f); sp.Fmt = "0"; pol.Items.Add(sp);
            pol.Items.Add(T("Gameplay", "StartInGarage", "Start in your last garage", "Loading in puts you in the last garage you used.", "house", true));
            pol.Items.Add(T("Gameplay", "BlockVehicleExit", "Stay in the car", "Holding exit doesn't leave the car in Midnight Club.", "car-front", true));
            cats.Add(pol);

            var ctl = new Category { Name = "Controls", Icon = "keyboard", Accent = Cyan };
            ctl.Items.Add(new Setting { Sec = "General", Key = "ToggleKey", Label = "Switch to Midnight Club", Desc = "Enter or leave Midnight Club from Story Mode. Controller: hold Back / View.", Icon = "keyboard", Kind = Kind.Key, Def = "F7" });
            ctl.Items.Add(new Setting { Sec = "General", Key = "MenuKey", Label = "Midnight Hub", Desc = "Open the Hub (phone). Controller: d-pad up.", Icon = "keyboard", Kind = Kind.Key, Def = "F6" });
            cats.Add(ctl);

            var adv = new Category { Name = "Advanced", Icon = "settings", Accent = Violet };
            var a1 = S("Save", "AutoSaveSeconds", "Autosave interval", "Seconds between automatic saves.", "save", 30f, 600f, 30f, 120f); a1.Fmt = "0"; adv.Items.Add(a1);
            var a2 = S("Save", "BackupCount", "Save backups", "How many old saves are kept.", "save", 1f, 20f, 1f, 5f); a2.Fmt = "0"; adv.Items.Add(a2);
            adv.Items.Add(S("Economy", "PriceMultiplier", "Car prices", "Multiplier on every car price.", "trophy", 0.25f, 3f, 0.05f, 1f));
            adv.Items.Add(T("General", "DevMode", "Developer mode", "Extra logging and debug shortcuts.", "wrench", false));
            cats.Add(adv);
        }
        private static string Fs(float v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }

        // ------------------------------------------------------------------ actions
        private void Launch(bool midnight)
        {
            if (gameDir == null) { status = "GTA V Enhanced not found: put the launcher in the game folder."; return; }
            try
            {
                string flag = Path.Combine(modDir, "autostart.flag");
                if (midnight) { Directory.CreateDirectory(modDir); File.WriteAllText(flag, DateTime.UtcNow.ToString("o")); }
                else if (File.Exists(flag)) File.Delete(flag);
                status = midnight ? "Starting Midnight Club..." : "Starting Story Mode...";
                try { Process.Start(new ProcessStartInfo("steam://rungameid/" + SteamAppId) { UseShellExecute = true }); }
                catch { Process.Start(new ProcessStartInfo(Path.Combine(gameDir, "PlayGTAV.exe")) { WorkingDirectory = gameDir, UseShellExecute = true }); }
                var close = new Timer { Interval = 2500 }; close.Tick += (s, e) => Close(); close.Start();
            }
            catch (Exception ex) { status = "Could not start the game: " + ex.Message; }
        }

        private void Saved(string what) { toast = what; toastAt = DateTime.Now; }

        // ------------------------------------------------------------------ input
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (moving) { Location = new Point(Location.X + e.X - dragFrom.X, Location.Y + e.Y - dragFrom.Y); return; }
            if (dragId != null) { var h = hits.FirstOrDefault(x => x.Id == dragId); if (h != null && h.Drag != null) h.Drag(e.X); return; }
            var hit = hits.LastOrDefault(x => x.R.Contains(e.Location));
            hoverId = hit != null ? hit.Id : null;
            Cursor = hit != null ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var hit = hits.LastOrDefault(x => x.R.Contains(e.Location));
            if (hit == null) { if (e.Y < 56) { moving = true; dragFrom = e.Location; } return; }
            pressId = hit.Id;
            if (hit.Drag != null) { dragId = hit.Id; hit.Drag(e.X); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            moving = false;
            if (dragId != null) { dragId = null; pressId = null; return; }
            var hit = hits.LastOrDefault(x => x.R.Contains(e.Location));
            if (hit != null && hit.Id == pressId && hit.Click != null) hit.Click();
            pressId = null;
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (capture != null)
            {
                var parts = capture.Split('/');
                if (e.KeyCode != Keys.Escape) { ini.Set(parts[0], parts[1], e.KeyCode.ToString()); Saved("Key set to " + e.KeyCode); }
                capture = null; e.Handled = true; return;
            }
            if (e.KeyCode == Keys.Enter && page == 0) Launch(true);
            if (e.KeyCode == Keys.Escape) { if (page != 0) page = 0; else Close(); }
        }

        private void Animate()
        {
            foreach (var k in hot.Keys.ToList())
            {
                float target = (k == hoverId ? 1f : 0f);
                hot[k] += (target - hot[k]) * 0.25f;
            }
            if (hoverId != null && !hot.ContainsKey(hoverId)) hot[hoverId] = 0f;
            scroll += (scrollTarget - scroll) * 0.25f;
        }
        private float Hot(string id) { float v; return hot.TryGetValue(id, out v) ? v : 0f; }
        private void AddHit(RectangleF r, string id, Action click, Action<float> drag = null) { hits.Add(new Hit { R = r, Id = id, Click = click, Drag = drag }); }

        // ------------------------------------------------------------------ drawing primitives
        private static GraphicsPath RoundPath(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
            return p;
        }
        private static void Fill(Graphics g, RectangleF r, float rad, Color c) { using (var p = RoundPath(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p); }
        private static void Stroke(Graphics g, RectangleF r, float rad, Color c, float w = 1f) { using (var p = RoundPath(r, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p); }

        /// <summary>Real frosted glass: the pre-blurred background is drawn inside the shape, tinted, with a hairline border
        /// and a top highlight.</summary>
        private void Glass(Graphics g, RectangleF r, float rad, bool strong = false)
        {
            Image blur; img.TryGetValue("art.bgblur_" + (light ? "light" : "dark"), out blur);
            using (var p = RoundPath(r, rad))
            {
                if (blur != null) { var st = g.Save(); g.SetClip(p); g.DrawImage(blur, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height)); g.Restore(st); }
                using (var b = new SolidBrush(strong ? GlassStrong : GlassTint)) g.FillPath(b, p);
                using (var pen = new Pen(Line, 1f)) g.DrawPath(pen, p);
            }
            using (var hp = new Pen(Hi, 1f)) g.DrawLine(hp, r.X + rad, r.Y + 1, r.Right - rad, r.Y + 1);
        }

        private void Ico(Graphics g, string name, float cx, float cy, float size, Color c)
        {
            Image im; if (!img.TryGetValue("icon." + name, out im)) return;
            var cm = new ColorMatrix(new[] { new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, c.A / 255f, 0 }, new float[] { c.R / 255f, c.G / 255f, c.B / 255f, 0, 1 } });
            using (var ia = new ImageAttributes()) { ia.SetColorMatrix(cm); g.DrawImage(im, new Rectangle((int)(cx - size / 2), (int)(cy - size / 2), (int)size, (int)size), 0, 0, im.Width, im.Height, GraphicsUnit.Pixel, ia); }
        }

        private void IconBadge(Graphics g, string icon, float cx, float cy, float size, Color accent)
        {
            Fill(g, new RectangleF(cx - size / 2, cy - size / 2, size, size), size * 0.28f, accent);
            Ico(g, icon, cx, cy, size * 0.58f, On(accent));
        }

        private static void Str(Graphics g, string s, Font f, Color c, float x, float y, StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Center, float w = 0f)
        {
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat(StringFormatFlags.NoWrap) { Alignment = h, LineAlignment = v, Trimming = StringTrimming.EllipsisCharacter })
            {
                if (w > 0) { sf.FormatFlags = 0; g.DrawString(s, f, b, new RectangleF(x, y, w, 200), sf); return; }
                float ox = h == StringAlignment.Center ? -2000 : h == StringAlignment.Far ? -4000 : 0;
                g.DrawString(s, f, b, new RectangleF(x + ox, y - 100, 4000, 200), sf);
            }
        }

        private bool Button(Graphics g, RectangleF r, string id, string text, string icon, Color fill, Color fg, Action click, bool glass = false)
        {
            float h = Hot(id); bool down = pressId == id;
            var rr = RectangleF.Inflate(r, h * 2f - (down ? 2f : 0f), h * 1.2f - (down ? 1f : 0f));
            if (glass) { Glass(g, rr, rr.Height / 2f, true); if (h > 0.01f) Fill(g, rr, rr.Height / 2f, Color.FromArgb((int)(30 * h), Text1)); }
            else
            {
                Fill(g, rr, rr.Height / 2f, Mix(fill, Color.White, 0.12f * h));
                using (var p = RoundPath(rr, rr.Height / 2f)) using (var lb = new LinearGradientBrush(rr, Color.FromArgb(50, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f)) g.FillPath(lb, p);
            }
            var sz = g.MeasureString(text, fBtn);
            float iw = icon != null ? rr.Height * 0.42f + 10f : 0f, tx = rr.X + (rr.Width - sz.Width - iw) / 2f;
            if (icon != null) Ico(g, icon, tx + rr.Height * 0.21f, rr.Y + rr.Height / 2f, rr.Height * 0.42f, fg);
            Str(g, text, fBtn, fg, tx + iw, rr.Y + rr.Height / 2f + 1);
            AddHit(r, id, click);
            return h > 0.5f;
        }

        private static Color Mix(Color a, Color b, float t) { t = Math.Max(0, Math.Min(1, t)); return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
        private static Color A(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c); }

        /// <summary>Design preview: renders one page to a PNG (used for screenshots in development).</summary>
        public static void Render(string file, int page, int cat, bool light)
        {
            var f = new LauncherForm(); f.page = page; f.cat = cat; f.light = light; f.started = DateTime.Now.AddSeconds(-3);
            using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
            using (var g = Graphics.FromImage(bmp))
            {
                f.OnPaint(new PaintEventArgs(g, new Rectangle(0, 0, bmp.Width, bmp.Height)));
                bmp.Save(file, ImageFormat.Png);
            }
        }

        // ------------------------------------------------------------------ paint
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            hits.Clear();
            int W = ClientSize.Width, H = ClientSize.Height;
            Image bg; if (img.TryGetValue("art.bg_" + (light ? "light" : "dark"), out bg)) g.DrawImage(bg, 0, 0, W, H); else g.Clear(BackColor);
            // readability scrim: the left / content side is calmer, the art stays vivid on the right
            using (var sb = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(light ? 170 : 175, light ? Color.FromArgb(240, 242, 247) : Color.FromArgb(8, 9, 14)), Color.FromArgb(0, 0, 0, 0), 0f))
            {
                var sc = light ? Color.FromArgb(242, 243, 248) : Color.FromArgb(8, 9, 14);
                var blend = new ColorBlend { Positions = new[] { 0f, 0.5f, 0.78f, 1f }, Colors = new[] { Color.FromArgb(225, sc), Color.FromArgb(light ? 200 : 190, sc), Color.FromArgb(light ? 60 : 50, sc), Color.FromArgb(0, sc) } };
                sb.InterpolationColors = blend; g.FillRectangle(sb, 0, 0, W, H);
            }
            // slow drifting light over the background
            float tt = (float)(DateTime.Now - started).TotalSeconds;
            using (var path = new GraphicsPath())
            {
                float cx = W * (0.55f + 0.15f * (float)Math.Sin(tt * 0.25)), cy = H * 0.35f;
                path.AddEllipse(cx - 420, cy - 300, 840, 600);
                using (var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb(light ? 40 : 34, Red), SurroundColors = new[] { Color.FromArgb(0, Red) } }) g.FillPath(pg, path);
            }
            DrawSidebar(g, W, H);
            var content = new RectangleF(272, 24, W - 296, H - 48);
            if (page == 0) DrawHome(g, content);
            else if (page == 1) DrawSettings(g, content);
            else DrawAbout(g, content);
            // window controls (on a glass pill so they read over the artwork)
            Glass(g, new RectangleF(W - 96, 10, 84, 40), 20, true);
            float bx = W - 52;
            foreach (var wc in new[] { "x", "minus" })
            {
                var r = new RectangleF(bx, 14, 32, 32); string id = "win_" + wc; float h = Hot(id);
                if (h > 0.01f) Fill(g, r, 10, wc == "x" ? A(Red, (int)(220 * h)) : A(Text1, (int)(30 * h)));
                Ico(g, wc, r.X + 16, r.Y + 16, 18, wc == "x" && h > 0.5f ? Color.White : Text2);
                AddHit(r, id, wc == "x" ? (Action)Close : () => WindowState = FormWindowState.Minimized);
                bx -= 36;
            }
            // toast
            if (toast != null)
            {
                float age = (float)(DateTime.Now - toastAt).TotalSeconds;
                if (age > 2.2f) toast = null;
                else
                {
                    float a = Math.Min(1f, Math.Min(age / 0.15f, (2.2f - age) / 0.3f));
                    var sz = g.MeasureString(toast, fLabel); var r = new RectangleF(W / 2f + 136 - sz.Width / 2f - 30, H - 70 + (1 - a) * 10, sz.Width + 60, 44);
                    Glass(g, r, 22, true);
                    Ico(g, "circle-check", r.X + 24, r.Y + 22, 18, Green);
                    Str(g, toast, fLabel, Text1, r.X + 40, r.Y + 22);
                }
            }
        }

        private void DrawSidebar(Graphics g, int W, int H)
        {
            var r = new RectangleF(16, 16, 240, H - 32);
            Glass(g, r, 22, true);
            // wordmark
            Image logo;
            if (img.TryGetValue("art.logo", out logo)) g.DrawImage(logo, new RectangleF(32, 34, 208, 208f * logo.Height / logo.Width));
            // nav
            string[] names = { "Play", "Settings", "About" }; string[] icons = { "play", "settings", "info" };
            float ny = 150;
            for (int i = 0; i < 3; i++)
            {
                var nr = new RectangleF(28, ny, 216, 48); string id = "nav" + i; bool on = page == i; float h = Hot(id);
                if (on) Fill(g, nr, 14, Red); else if (h > 0.01f) Fill(g, nr, 14, A(Text1, (int)(22 * h)));
                Ico(g, icons[i], nr.X + 26, nr.Y + 24, 20, on ? Color.White : Text2);
                Str(g, names[i].ToUpperInvariant(), fH2, on ? Color.White : Text1, nr.X + 48, nr.Y + 25);
                int pi = i; AddHit(nr, id, () => { page = pi; scroll = scrollTarget = 0; });
                ny += 56;
            }
            // bottom: mod folder + theme switch
            var fr = new RectangleF(28, H - 140, 216, 44);
            float fh = Hot("folder"); if (fh > 0.01f) Fill(g, fr, 14, A(Text1, (int)(22 * fh)));
            Ico(g, "folder-open", fr.X + 26, fr.Y + 22, 18, Text2);
            Str(g, "MOD FOLDER", fCaps, Text1, fr.X + 48, fr.Y + 23);
            AddHit(fr, "folder", () => { if (modDir != null && Directory.Exists(modDir)) Process.Start(new ProcessStartInfo(modDir) { UseShellExecute = true }); });
            // theme segmented
            var tr = new RectangleF(28, H - 86, 216, 44);
            Fill(g, tr, 22, Track);
            string cur = ini.Get("HUD", "Theme", "Auto"); string[] th = { "Auto", "Dark", "Light" }; string[] ti = { "sun-moon", "moon", "sun" };
            for (int i = 0; i < 3; i++)
            {
                var sr = new RectangleF(tr.X + 4 + i * 70, tr.Y + 4, 68, 36); bool on = cur == th[i];
                if (on) Fill(g, sr, 18, light ? Color.White : Color.FromArgb(60, 64, 80));
                Ico(g, ti[i], sr.X + 34, sr.Y + 18, 18, on ? Text1 : Text2);
                string v = th[i]; AddHit(sr, "theme" + i, () => { ini.Set("HUD", "Theme", v); ApplyTheme(); Saved("Theme: " + v); });
            }
        }

        private void DrawHome(Graphics g, RectangleF c)
        {
            float x = c.X + 24, y = c.Y + 70;
            Str(g, "STREET RACING CAREER FOR GTA V ENHANCED", fCaps, Red, x + 4, y);
            Str(g, "HIT THE STREETS", fHero, Text1, x - 4, y + 62);
            Str(g, "Race the city, build your garage, climb from nobody to the Midnight Club.", fBody, Text2, x + 2, y + 122);
            // buttons
            var pr = new RectangleF(x, y + 160, 380, 72);
            Button(g, pr, "play", "PLAY", "play", Red, Color.White, () => Launch(true));
            float kx = x + 6, ky = y + 254;
            kx += Keycap(g, "ENTER", kx, ky) + 8; Str(g, "play", fSmall, Text2, kx, ky); kx += 46;
            kx += Keycap(g, "ESC", kx, ky) + 8; Str(g, "close", fSmall, Text2, kx, ky);

            // cards row
            float cy = c.Bottom - 262, cw = (c.Right - 24 - x - 16) / 2f, chh = 226;
            var cr = new RectangleF(x, cy, cw, chh);
            Glass(g, cr, 20);
            Str(g, "SYSTEM CHECK", fCaps, Text2, cr.X + 22, cr.Y + 28);
            Func<string, bool> has = f => gameDir != null && File.Exists(Path.Combine(gameDir, f));
            var checks = new[]
            {
                new KeyValuePair<string, bool>("GTA V Enhanced", gameDir != null),
                new KeyValuePair<string, bool>("Script Hook V", has("ScriptHookV.dll")),
                new KeyValuePair<string, bool>("ScriptHookVDotNet", has("ScriptHookVDotNet.asi")),
                new KeyValuePair<string, bool>("MidnightClub.dll", has(Path.Combine("scripts", "MidnightClub.dll"))),
                new KeyValuePair<string, bool>("Settings file", ini.Exists),
            };
            float ry = cr.Y + 60;
            foreach (var ck in checks)
            {
                Ico(g, ck.Value ? "circle-check" : "circle-x", cr.X + 32, ry, 20, ck.Value ? Green : Red);
                Str(g, ck.Key, fLabel, Text1, cr.X + 52, ry);
                if (ck.Key == "MidnightClub.dll" && ck.Value) Str(g, File.GetLastWriteTime(Path.Combine(gameDir, "scripts", "MidnightClub.dll")).ToString("yyyy-MM-dd"), fSmall, Text3, cr.Right - 22, ry, StringAlignment.Far);
                ry += 34;
            }
            string save = modDir != null ? Path.Combine(modDir, "saves", "profile.json") : null;
            var kr = new RectangleF(x + cw + 16, cy, cw, chh);
            Glass(g, kr, 20);
            Str(g, "YOUR CAREER", fCaps, Text2, kr.X + 22, kr.Y + 28);
            if (save != null && File.Exists(save))
            {
                string js = ""; try { js = File.ReadAllText(save); } catch { }
                Func<string, string> num = k => { var m = Regex.Match(js, "\"" + k + "\":(-?\\d+)"); return m.Success ? m.Groups[1].Value : "0"; };
                long cash; long.TryParse(num("Cash"), out cash); long rep; long.TryParse(num("Rep"), out rep); int chap; int.TryParse(num("StoryChapter"), out chap);
                Str(g, "$" + cash.ToString("N0", CultureInfo.InvariantCulture), fHero, Text1, kr.X + 20, kr.Y + 92);
                var chip = rep.ToString("N0", CultureInfo.InvariantCulture) + " REP  ·  CHAPTER " + (chap + 1);
                var csz = g.MeasureString(chip, fCaps);
                Fill(g, new RectangleF(kr.X + 22, kr.Y + 140, csz.Width + 24, 30), 15, Red);
                Str(g, chip, fCaps, Color.White, kr.X + 34, kr.Y + 156);
                Str(g, "Last played " + File.GetLastWriteTime(save).ToString("yyyy-MM-dd HH:mm"), fSmall, Text3, kr.X + 22, kr.Y + 196);
            }
            else Str(g, "No save yet. Your career starts the first time you play.", fBody, Text2, kr.X + 22, kr.Y + 56, StringAlignment.Near, StringAlignment.Near, kr.Width - 44);
            // status line
            var ssz = g.MeasureString(status, fSmall);
            var sr2 = new RectangleF(x, c.Bottom - 12, ssz.Width + 44, 30);
            Glass(g, sr2, 15, true);
            Ico(g, gameDir != null ? "circle-check" : "circle-x", sr2.X + 16, sr2.Y + 15, 14, gameDir != null ? Green : Red);
            Str(g, status, fSmall, Text2, sr2.X + 30, sr2.Y + 15);
        }

        /// <summary>Small keycap (same look as the in-game prompts). Returns its width.</summary>
        private float Keycap(Graphics g, string k, float x, float cy)
        {
            var sz = g.MeasureString(k, fCaps); float w = Math.Max(28, sz.Width + 14), h = 26;
            Fill(g, new RectangleF(x, cy - h / 2 + 2, w, h), 6, Color.FromArgb(150, 154, 168));
            Fill(g, new RectangleF(x, cy - h / 2, w, h - 1), 6, Color.FromArgb(244, 245, 248));
            Str(g, k, fCaps, Color.FromArgb(18, 20, 26), x + w / 2, cy, StringAlignment.Center);
            return w;
        }

        private void DrawSettings(Graphics g, RectangleF c)
        {
            float x = c.X + 24;
            Str(g, "SETTINGS", fH1, Text1, x, c.Y + 50);
            Str(g, ini.Exists ? "Saved instantly to config.ini · applied the next time the game loads" : "config.ini is created on the first change", fSmall, Text2, x + 2, c.Y + 82);
            // category chips
            float cx = x, cy = c.Y + 108;
            for (int i = 0; i < cats.Count; i++)
            {
                var k = cats[i]; var sz = g.MeasureString(k.Name.ToUpperInvariant(), fCaps);
                var r = new RectangleF(cx, cy, sz.Width + 48, 38); bool on = i == cat; string id = "cat" + i; float h = Hot(id);
                if (on) Fill(g, r, 19, k.Accent); else { Glass(g, r, 19, false); if (h > 0.01f) Fill(g, r, 19, A(Text1, (int)(24 * h))); }
                Ico(g, k.Icon, r.X + 20, r.Y + 19, 16, on ? On(k.Accent) : k.Accent);
                Str(g, k.Name.ToUpperInvariant(), fCaps, on ? On(k.Accent) : Text1, r.X + 34, r.Y + 20);
                int ci = i; AddHit(r, id, () => { cat = ci; scroll = scrollTarget = 0; capture = null; });
                cx += r.Width + 8;
            }
            // rows
            var catv = cats[cat];
            var area = new RectangleF(x, cy + 56, c.Right - x - 24, c.Bottom - cy - 64);
            float rowH = 84, gap = 10;
            float total = catv.Items.Count * (rowH + gap);
            scrollTarget = Math.Max(0, Math.Min(scrollTarget, Math.Max(0, total - area.Height)));
            var st = g.Save();
            g.SetClip(area);
            float ry = area.Y - scroll;
            foreach (var s in catv.Items)
            {
                var r = new RectangleF(area.X, ry, area.Width - 12, rowH);
                if (r.Bottom > area.Y - 4 && r.Y < area.Bottom + 4) DrawRow(g, r, s, catv.Accent);
                ry += rowH + gap;
            }
            g.Restore(st);
            if (total > area.Height)
            {
                float th = area.Height * area.Height / total, ty = area.Y + (area.Height - th) * scroll / Math.Max(1, total - area.Height);
                Fill(g, new RectangleF(area.Right - 4, area.Y, 4, area.Height), 2, Track);
                Fill(g, new RectangleF(area.Right - 4, ty, 4, th), 2, catv.Accent);
            }
            // reset
            var rr = new RectangleF(c.Right - 200, c.Y + 36, 176, 40);
            Button(g, rr, "reset", "RESET PAGE", "rotate-ccw", Color.Empty, Text1, () =>
            {
                foreach (var s in catv.Items) { if (s.SetChoice != null) s.SetChoice(ini, 1); else if (s.Sec != null) ini.Set(s.Sec, s.Key, s.Def); }
                ApplyTheme(); Saved(catv.Name + " reset to defaults");
            }, true);
        }

        private void DrawRow(Graphics g, RectangleF r, Setting s, Color acc)
        {
            string id = "row_" + s.Sec + "_" + s.Key + "_" + s.Label;
            Glass(g, r, 18, false);
            if (Hot(id) > 0.01f) Fill(g, r, 18, A(Text1, (int)(14 * Hot(id))));
            IconBadge(g, s.Icon, r.X + 42, r.Y + r.Height / 2, 40, acc);
            Str(g, s.Label, fLabel, Text1, r.X + 78, r.Y + 30);
            Str(g, s.Desc, fSmall, Text2, r.X + 78, r.Y + 41, StringAlignment.Near, StringAlignment.Near, r.Width - 78 - (s.Kind == Kind.Choice && s.Options != null ? Math.Max(340, (s.Options.Length > 4 ? 66 : 96) * s.Options.Length + 40) : 340));
            float ctlR = r.Right - 24, cy = r.Y + r.Height / 2;
            AddHit(r, id, null);
            switch (s.Kind)
            {
                case Kind.Toggle:
                {
                    bool v = ini.GetB(s.Sec, s.Key, s.Def == "True");
                    var tr = new RectangleF(ctlR - 56, cy - 15, 56, 30);
                    Fill(g, tr, 15, v ? acc : Track);
                    float kx = v ? tr.Right - 15 : tr.X + 15;
                    Fill(g, new RectangleF(kx - 11, cy - 11, 22, 22), 11, Color.White);
                    AddHit(RectangleF.Inflate(tr, 8, 8), id + "_t", () => { ini.Set(s.Sec, s.Key, v ? "False" : "True"); Saved(s.Label + (v ? " off" : " on")); });
                    break;
                }
                case Kind.Slider:
                {
                    float def; float.TryParse(s.Def, NumberStyles.Float, CultureInfo.InvariantCulture, out def);
                    float v = ini.GetF(s.Sec, s.Key, def);
                    float w = 220, sx = ctlR - w - 64;
                    var tr = new RectangleF(sx, cy - 3, w, 6);
                    float k = (v - s.Min) / (s.Max - s.Min); k = Math.Max(0, Math.Min(1, k));
                    Fill(g, tr, 3, Track);
                    Fill(g, new RectangleF(tr.X, tr.Y, Math.Max(6, w * k), 6), 3, acc);
                    float kx = tr.X + w * k; bool active = dragId == id + "_s" || Hot(id + "_s") > 0.5f;
                    Fill(g, new RectangleF(kx - (active ? 11 : 9), cy - (active ? 11 : 9), active ? 22 : 18, active ? 22 : 18), 11, Color.White);
                    Stroke(g, new RectangleF(kx - (active ? 11 : 9), cy - (active ? 11 : 9), active ? 22 : 18, active ? 22 : 18), 11, A(acc, 200), 2f);
                    var vr = new RectangleF(ctlR - 54, cy - 15, 54, 30);
                    Fill(g, vr, 10, Track);
                    Str(g, v.ToString(s.Fmt, CultureInfo.InvariantCulture), fLabel, Text1, vr.X + vr.Width / 2, cy + 1, StringAlignment.Center);
                    var hitR = new RectangleF(tr.X - 12, cy - 16, w + 24, 32);
                    AddHit(hitR, id + "_s", null, mx =>
                    {
                        float kk = Math.Max(0, Math.Min(1, (mx - tr.X) / w));
                        float nv = s.Min + (float)Math.Round(kk * (s.Max - s.Min) / s.Step) * s.Step;
                        if (Math.Abs(nv - ini.GetF(s.Sec, s.Key, def)) > 1e-4f) { ini.Set(s.Sec, s.Key, nv.ToString(s.Fmt == "0" ? "0" : "0.##", CultureInfo.InvariantCulture)); Saved(s.Label + ": " + nv.ToString(s.Fmt, CultureInfo.InvariantCulture)); }
                    });
                    break;
                }
                case Kind.Choice:
                {
                    int cur = s.GetChoice != null ? s.GetChoice(ini) : Array.IndexOf(s.Options, ini.Get(s.Sec, s.Key, s.Def));
                    float segW = s.Options.Length > 4 ? 66 : 96, w = segW * s.Options.Length + 8, sx = ctlR - w;
                    var tr = new RectangleF(sx, cy - 20, w, 40);
                    Fill(g, tr, 20, Track);
                    for (int i = 0; i < s.Options.Length; i++)
                    {
                        var sr = new RectangleF(sx + 4 + i * segW, cy - 16, segW, 32); bool on = i == cur;
                        if (on) Fill(g, sr, 16, acc);
                        Str(g, s.Options[i].ToUpperInvariant(), fCaps, on ? On(acc) : Text1, sr.X + segW / 2, cy + 1, StringAlignment.Center);
                        int ii = i;
                        AddHit(sr, id + "_c" + i, () =>
                        {
                            if (s.SetChoice != null) s.SetChoice(ini, ii); else ini.Set(s.Sec, s.Key, s.Options[ii]);
                            if (s.Key == "Theme") ApplyTheme();
                            Saved(s.Label + ": " + s.Options[ii]);
                        });
                    }
                    if (cur < 0) Str(g, "CUSTOM", fCaps, Text3, sx - 14, cy, StringAlignment.Far);
                    break;
                }
                case Kind.Key:
                {
                    string v = ini.Get(s.Sec, s.Key, s.Def); bool cap = capture == s.Sec + "/" + s.Key;
                    string label = cap ? "PRESS A KEY" : v.ToUpperInvariant();
                    var sz = g.MeasureString(label, fBtn);
                    var kr = new RectangleF(ctlR - sz.Width - 36, cy - 22, sz.Width + 36, 44);
                    // keycap look: light key with a bottom edge
                    Fill(g, new RectangleF(kr.X, kr.Y + 3, kr.Width, kr.Height), 10, Color.FromArgb(150, 154, 168));
                    Fill(g, kr, 10, cap ? acc : Color.FromArgb(244, 245, 248));
                    Str(g, label, fBtn, cap ? On(acc) : Color.FromArgb(18, 20, 26), kr.X + kr.Width / 2, kr.Y + kr.Height / 2 + 1, StringAlignment.Center);
                    AddHit(kr, id + "_k", () => { capture = cap ? null : s.Sec + "/" + s.Key; });
                    break;
                }
            }
        }

        private void DrawAbout(Graphics g, RectangleF c)
        {
            float x = c.X + 24;
            Str(g, "ABOUT", fH1, Text1, x, c.Y + 50);
            var r = new RectangleF(x, c.Y + 96, c.Width - 48, 300);
            Glass(g, r, 20);
            string text = "Midnight Club: Los Santos turns GTA V Enhanced into a street racing career: story chapters, five disciplines, rivals, crews, " +
                          "workshops with Forza-style tuning, dealerships, garages, police pursuits and a city full of events.\n\n" +
                          "Press F7 in Story Mode (or hold Back / View on a controller) to switch. The Midnight Hub opens with F6 or d-pad up.\n\n" +
                          "Icons: Lucide (ISC). Fonts: Barlow and Barlow Condensed (SIL OFL). Made with Script Hook V and ScriptHookVDotNet.";
            Str(g, text, fBody, Text2, r.X + 28, r.Y + 28, StringAlignment.Near, StringAlignment.Near, r.Width - 56);
            var gr = new RectangleF(x, r.Bottom + 20, 260, 52);
            Button(g, gr, "github", "SOURCE ON GITHUB", "chevron-right", Color.Empty, Text1, () => { try { Process.Start(new ProcessStartInfo("https://github.com/Levilente/MidnightClub_LosSantos") { UseShellExecute = true }); } catch { } }, true);
        }
    }
}
