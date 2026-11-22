using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Native;
using GTA.UI;
using MidnightClub.Career;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.Police;
using MidnightClub.Racing;
using MidnightClub.Rivals;
using MidnightClub.UI;
using MidnightClub.WorldSim;
using Font = GTA.UI.Font;

namespace MidnightClub.Phone
{
    /// <summary>The Midnight Club phone: app grid (Messages, Races, Garage, Dealers, Career, Rivals, Clubs,
    /// Street Buzz news, Rankings, Calendar, Finances, Settings), a persistent inbox fed by game events,
    /// a fictional automotive news feed and an underground leaderboard. Opens with Up (Phone) or F6.</summary>
    public sealed class PhoneManager
    {
        private readonly SaveManager saves;
        private readonly MenuStack stack;
        private readonly MidnightMenu menu;
        private readonly WorldCalendar calendar;
        private readonly RaceManager races;
        private readonly Func<Menu> rivalsMenu, clubsMenu;
        public bool IsOpen { get; private set; }
        private int sel;
        private readonly Random rnd = new Random();

        private sealed class App { public string Name, Icon; public Color Accent; public Func<Menu> Open; public Func<string> Badge; }
        private readonly List<App> apps = new List<App>();

        public Func<Menu> MilestonesMenu, AbilityMenu;

        public PhoneManager(SaveManager saves, MenuStack stack, MidnightMenu menu, WorldCalendar calendar, RaceManager races, Func<Menu> rivalsMenu, Func<Menu> clubsMenu)
        {
            this.saves = saves; this.stack = stack; this.menu = menu; this.calendar = calendar; this.races = races; this.rivalsMenu = rivalsMenu; this.clubsMenu = clubsMenu;
            apps.Add(new App { Name = "Messages", Icon = "messages", Accent = Ds.Violet, Open = Inbox, Badge = () => { int n = saves.Data.Messages.Count(m => !m.Read); return n > 0 ? n.ToString() : null; } });
            apps.Add(new App { Name = "Races", Icon = "races", Accent = Ds.Red, Open = () => races.RacesMenu() });
            apps.Add(new App { Name = "Garage", Icon = "garage", Accent = Ds.Cyan, Open = () => menu.MyVehicles() });
            apps.Add(new App { Name = "Dealers", Icon = "dealers", Accent = Ds.Amber, Open = () => menu.DealerList() });
            apps.Add(new App { Name = "Rivals", Icon = "rivals", Accent = Ds.Pink, Open = () => this.rivalsMenu() });
            apps.Add(new App { Name = "Clubs", Icon = "clubs", Accent = Ds.Blue, Open = () => this.clubsMenu() });
            apps.Add(new App { Name = "Career", Icon = "career", Accent = Ds.Amber, Open = () => menu.CareerMenu() });
            apps.Add(new App { Name = "Rankings", Icon = "rankings", Accent = Ds.Lime, Open = Rankings });
            apps.Add(new App { Name = "Buzz", Icon = "news", Accent = Ds.Pink, Open = NewsApp });
            apps.Add(new App { Name = "Calendar", Icon = "calendar", Accent = Ds.Cyan, Open = CalendarApp });
            apps.Add(new App { Name = "Bank", Icon = "bank", Accent = Ds.Lime, Open = () => menu.Finances() });
            apps.Add(new App { Name = "Property", Icon = "properties", Accent = Ds.Blue, Open = () => menu.GarageList() });
            apps.Add(new App { Name = "Catalogue", Icon = "catalogue", Accent = Ds.Violet, Open = () => menu.Catalogue() });
            apps.Add(new App { Name = "Goals", Icon = "milestones", Accent = Ds.Red, Open = () => MilestonesMenu != null ? MilestonesMenu() : null });
            apps.Add(new App { Name = "Ability", Icon = "ability", Accent = Ds.Amber, Open = () => AbilityMenu != null ? AbilityMenu() : null });
            apps.Add(new App { Name = "Settings", Icon = "settings", Accent = Ds.Muted, Open = () => menu.MainMenu() });
            MenuStack.PhoneRenderer = DrawApp;

            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e => { IsOpen = false; if (e.To == GameMode.MIDNIGHT_CLUB) Welcome(); });
            EventBus.Subscribe<TierUpEvent>(e => { Send("Midnight Club", "Promotion: " + CareerManager.TierNames[e.Tier], "You're now " + CareerManager.TierNames[e.Tier] + ". " + CareerManager.TierUnlocks[e.Tier], ""); News("New name on the scene", "Word is a " + CareerManager.TierNames[e.Tier].ToLowerInvariant() + " has been tearing up the streets of Los Santos."); });
            EventBus.Subscribe<RaceFinishedEvent>(OnRace);
            EventBus.Subscribe<RivalDefeatedEvent>(e => { var p = RivalManager.Profiles.FirstOrDefault(x => x.Id == e.Id); if (p != null) News(p.Name + " dethroned", p.Name + ", the city's " + p.Archetype.ToLowerInvariant() + ", lost a third straight duel and handed over the keys."); });
            EventBus.Subscribe<PursuitEndedEvent>(e => { if (e.Escaped && e.Level >= 3) News("Police lose racer in citywide chase", "LSPD units and a helicopter failed to stop a street racer last night (level " + e.Level + " pursuit)."); });
            EventBus.Subscribe<NewDayEvent>(e => DailyMail());
        }

        // ------------------------------------------------------------------ inbox & news
        public void Send(string from, string subject, string body, string action)
        {
            saves.Data.Messages.Add(new PhoneMessage { From = from, Subject = subject, Body = body, Action = action ?? "", Utc = DateTime.UtcNow.ToString("o") });
            saves.IsDirty = true;
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Text_Arrive_Tone", "Phone_SoundSet_Default", true);
            Notification.PostTicker("~p~" + from + "~s~: " + subject, false, false);
        }

        private void News(string headline, string body)
        {
            saves.Data.News.Add(new NewsItem { Headline = headline, Body = body, Utc = DateTime.UtcNow.ToString("o"), Day = calendar.Day });
            saves.IsDirty = true;
        }

        private void Welcome()
        {
            if (saves.Data.Messages.Count > 0) return;
            Send("Midnight Club", "Welcome to the streets", "Nobody knows your name yet. Win races, beat rivals, build your garage. Open this phone with UP or F6.", "");
            DailyMail();
        }

        private void DailyMail()
        {
            if (saves.Data.LastInviteDay == calendar.Day) return;
            saves.Data.LastInviteDay = calendar.Day;
            var open = races.Races.Where(r => races.Available(r) || r.NightOnly && saves.Data.Rep >= r.RequiredRep).ToList();
            if (open.Count > 0)
            {
                var r = open[rnd.Next(open.Count)];
                string from = RaceManager.DriverNames[rnd.Next(RaceManager.DriverNames.Length)];
                Send(from, "Race tonight: " + r.Name, "Think you can keep up? " + r.Name + (r.NightOnly ? " (night only)" : "") + ". $" + r.CashReward.ToString("N0") + " for the winner.", "race:" + r.Id);
            }
            if (calendar.IsWeekend) Send("LS Car Meet", "Weekend meets are on", "Car meets run all day this weekend. Check the purple blips.", "");
        }

        private void OnRace(RaceFinishedEvent e)
        {
            var res = e.Result;
            if (e.RivalId != null && e.Won) News("Upset on the streets", "An up-and-comer beat " + e.Race.Name.Split(new[] { " vs " }, StringSplitOptions.None).Last() + " head-to-head.");
            else if (res.Position == 1 && e.Race.HasOpponents && e.Race.RepReward >= 300) News(e.Race.Name + " won", "The " + e.Race.Name + " went to a newcomer in " + RaceSession.FormatTime((int)res.TimeMs) + ".");
            if (res.Medal == "Gold") News("Gold on the clock", "Someone set a gold time on " + e.Race.Name + ".");
        }

        // ------------------------------------------------------------------ apps
        private Menu Inbox()
        {
            var m = new Menu("Messages", saves.Data.Messages.Count(x => !x.Read) + " unread");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                foreach (var msg in Enumerable.Reverse(saves.Data.Messages).Take(30))
                {
                    var x = msg;
                    m.Add(new MenuItem((x.Read ? "" : "~p~* ~s~") + x.From + ": " + x.Subject, () => { x.Read = true; saves.IsDirty = true; stack.Push(MessageView(x)); }) { Description = () => x.Body });
                }
                if (m.Items.Count == 0) m.Add(new MenuItem("No messages") { Enabled = false });
            };
            return m;
        }

        private Menu MessageView(PhoneMessage x)
        {
            var m = new Menu(x.From, x.Subject);
            m.Add(new MenuItem(x.Subject) { Description = () => x.Body, Enabled = false });
            if (x.Action.StartsWith("race:"))
            {
                var r = races.Race(x.Action.Substring(5));
                if (r != null) m.Add(new MenuItem("Set GPS to the start", () => { Function.Call(Hash.SET_NEW_WAYPOINT, r.Start.X, r.Start.Y); Notification.PostTicker("GPS set to " + r.Name + ".", true, false); }));
            }
            m.Add(new MenuItem("Delete", () => { saves.Data.Messages.Remove(x); saves.IsDirty = true; stack.Pop(); }));
            return m;
        }

        private Menu NewsApp()
        {
            var m = new Menu("Street Buzz", "Los Santos car culture");
            foreach (var n in Enumerable.Reverse(saves.Data.News).Take(20)) { var x = n; m.Add(new MenuItem("Day " + (x.Day + 1) + ": " + x.Headline) { Description = () => x.Body }); }
            if (m.Items.Count == 0) m.Add(new MenuItem("Nothing to report. Yet.") { Enabled = false });
            return m;
        }

        /// <summary>Underground leaderboard: the named drivers' REP grows with the calendar (deterministic), you slot in by REP.</summary>
        private Menu Rankings()
        {
            var board = RaceManager.DriverNames.Select((n, i) => new KeyValuePair<string, long>(n, 400L + Dealerships.RotationMath.Seed(n) % 6000 + (long)calendar.Day * (40 + i * 7)))
                .Concat(new[] { new KeyValuePair<string, long>("YOU", saves.Data.Rep) }).OrderByDescending(x => x.Value).ToList();
            int rank = board.FindIndex(x => x.Key == "YOU") + 1;
            var m = new Menu("Rankings", "You are #" + rank + " of " + board.Count);
            for (int i = 0; i < board.Count; i++) { var e = board[i]; int pos = i + 1; m.Add(new MenuItem(pos + ". " + (e.Key == "YOU" ? "~p~YOU~s~" : e.Key)) { Value = () => e.Value.ToString("N0") + " REP", Enabled = e.Key == "YOU" }); }
            return m;
        }

        private Menu CalendarApp()
        {
            var m = new Menu("Calendar", "Day " + (calendar.Day + 1) + ", " + calendar.DayName);
            m.Add(new MenuItem("Time") { Value = () => calendar.Hour.ToString("00") + ":00" + (calendar.IsNight ? " (night)" : ""), Enabled = false });
            m.Add(new MenuItem("Car meets") { Value = () => calendar.IsWeekend ? "All day" : "21:00 - 04:00", Enabled = false });
            m.Add(new MenuItem("Night-only races") { Value = () => "21:00 - 05:00", Enabled = false });
            if (menu.Dealers != null) foreach (var d in menu.Dealers.Definitions) { var dd = d; m.Add(new MenuItem(dd.Name) { Value = () => "new stock in " + menu.Dealers.DaysUntilRotation(dd) + "d", Enabled = false }); }
            return m;
        }

        // ------------------------------------------------------------------ UI
        private float openT;                 // 0 hidden .. 1 fully up (animated)
        private float hlX, hlY;              // animated highlight position on the grid
        private readonly float[] tileK = new float[32];
        private Menu lastMenu; private float screenT = 1f, listSel = -1f;
        private App current;
        private bool appActive;

        /// <summary>True while any part of the phone is on screen (including the slide-out).</summary>
        public bool IsVisible { get { return openT > 0.001f; } }

        public void Toggle()
        {
            if (IsOpen) { Close(); return; }
            if (stack.IsOpen) stack.Close();
            Open();
        }
        private void Open() { IsOpen = true; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Pull_Out", "Phone_SoundSet_Default", true); }
        private void Close() { IsOpen = false; if (stack.PhoneStyle) stack.Close(); Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Put_Away", "Phone_SoundSet_Default", true); }

        private void Tick()
        {
            Game.DisableControlThisFrame(Control.Phone);   // our phone replaces the game's in Midnight Club
            if (!stack.IsOpen && !IsOpen && !RaceManager.IsRacing && Game.IsControlJustPressed(Control.Phone)) { Open(); return; }
            if (IsOpen && stack.IsOpen && !stack.PhoneStyle) IsOpen = false;   // a full-screen menu took over
            if (appActive && !stack.IsOpen) { appActive = false; if (!stack.ClosedByBack) { IsOpen = false; openT = 0f; } }
            if (stack.IsOpen && stack.PhoneStyle) appActive = true;
            openT = Ds.Step(openT, IsOpen ? 1f : 0f, IsOpen ? 4.2f : 6f);
            if (!IsVisible) return;
            if (IsOpen && stack.IsOpen) return;                      // app screen: drawn by DrawApp via the menu stack
            if (IsOpen)
            {
                foreach (var c in new[] { Control.Phone, Control.VehicleRadioWheel, Control.VehicleCinCam, Control.VehicleHeadlight, Control.FrontendPauseAlternate, Control.VehicleDuck, Control.VehicleSelectNextWeapon }) Game.DisableControlThisFrame(c);
                int cols = 4, old = sel;
                if (Game.IsControlJustPressed(Control.FrontendRight)) sel = (sel + 1) % apps.Count;
                if (Game.IsControlJustPressed(Control.FrontendLeft)) sel = (sel - 1 + apps.Count) % apps.Count;
                if (Game.IsControlJustPressed(Control.FrontendDown)) sel = (sel + cols) % apps.Count;
                if (Game.IsControlJustPressed(Control.FrontendUp)) sel = (sel - cols + apps.Count) % apps.Count;
                if (sel != old) Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Menu_Navigate", "Phone_SoundSet_Default", true);
                if (Game.IsControlJustPressed(Control.FrontendCancel)) { Close(); }
                else if (Game.IsControlJustPressed(Control.FrontendAccept))
                {
                    var app = apps[sel]; var m = app.Open();
                    if (m != null) { current = app; stack.Open(m); stack.PhoneStyle = true; lastMenu = null; }
                    Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Menu_Accept", "Phone_SoundSet_Default", true);
                }
            }
            DrawHome();
        }

        // ---------------------------------------------------------------- layout
        private const float PH = 0.70f;
        private float sx, sy, sw, sh, alpha;

        private void Frame(bool home)
        {
            Theme.FullscreenFrame();
            float e = Ds.EaseOut(openT);
            alpha = Ds.Clamp01(openT * 1.6f);
            float W = Ds.Px(PH * 0.49f), X = 0.975f - W, Y = 0.205f + (1f - e) * 0.85f;
            Ds.Round(X - Ds.Px(0.007f), Y - 0.007f, W + Ds.Px(0.014f), PH + 0.014f, 0.052f, Ds.A(Color.Black, 0.45f * alpha));
            Ds.Round(X, Y, W, PH, 0.046f, Ds.A(Color.FromArgb(255, 42, 37, 60), alpha));
            Ds.Rect(X + W - Ds.Px(0.0015f), Y + 0.12f, Ds.Px(0.0015f), 0.05f, Ds.A(Color.FromArgb(255, 70, 64, 96), alpha));
            sx = X + Ds.Px(0.006f); sy = Y + 0.006f; sw = W - Ds.Px(0.012f); sh = PH - 0.012f;
            Ds.Round(sx, sy, sw, sh, 0.04f, Ds.A(Ds.Bg, alpha));
            // wallpaper: violet glow + city skyline (home), a faint glow on app screens
            float gw = sw - Ds.Px(0.03f);
            Ds.VGrad(sx + Ds.Px(0.015f), sy + 0.015f, gw, home ? 0.2f : 0.1f, Ds.A(Color.FromArgb(255, 58, 22, 108), alpha * (home ? 1f : 0.55f)), Ds.A(Ds.Bg, alpha), 20);
            if (home) Skyline(sx + Ds.Px(0.015f), sy + 0.205f, gw);
            // status bar
            float pad = Ds.Px(0.022f);
            Ds.T("MIDNIGHT", sx + pad, sy + 0.013f, 0.27f, Ds.A(Ds.Text, alpha), Ds.Display);
            Ds.Round(sx + sw / 2 - Ds.Px(0.034f), sy + 0.012f, Ds.Px(0.068f), 0.018f, 0.009f, Ds.A(Color.Black, alpha));
            for (int i = 0; i < 4; i++) Ds.Rect(sx + sw - pad - Ds.Px(0.031f) + i * Ds.Px(0.0047f), sy + 0.028f - 0.0032f * (i + 1), Ds.Px(0.0031f), 0.0032f * (i + 1), Ds.A(Ds.Text, alpha));
            Ds.Round(sx + sw - pad - Ds.Px(0.012f), sy + 0.018f, Ds.Px(0.012f), 0.0095f, 0.0025f, Ds.A(Ds.Text, alpha));
            // home indicator
            Ds.Round(sx + sw / 2 - Ds.Px(0.05f), sy + sh - 0.016f, Ds.Px(0.1f), 0.0055f, 0.0027f, Ds.A(Color.FromArgb(255, 125, 118, 148), alpha));
        }

        private float[] sky;
        private void Skyline(float x, float baseY, float w)
        {
            if (sky == null)
            {
                var r = new Random(7); var l = new List<float>(); float u = 0;
                while (u < 1f) { float bw = 0.035f + (float)r.NextDouble() * 0.06f, bh = 0.02f + (float)r.NextDouble() * 0.05f; l.Add(u); l.Add(Math.Min(bw, 1f - u)); l.Add(bh); l.Add((float)r.NextDouble()); u += bw + 0.008f; }
                sky = l.ToArray();
            }
            for (int i = 0; i + 3 < sky.Length; i += 4)
            {
                float bx = x + sky[i] * w, bw = sky[i + 1] * w, bh = sky[i + 2];
                Ds.Rect(bx, baseY - bh, bw, bh, Ds.A(Color.FromArgb(255, 38, 17, 70), alpha));
                int rows = (int)(bh / 0.012f);
                for (int k = 0; k < rows; k++)
                    if (((int)(sky[i + 3] * 997) + k * 31) % 3 == 0)
                    {
                        float flick = ((Game.GameTime / 1700 + k + i) % 11 == 0) ? 0.4f : 1f;
                        Ds.Rect(bx + bw * 0.3f, baseY - bh + 0.006f + k * 0.012f, Ds.Px(0.0024f), 0.003f, Ds.A(Color.FromArgb(255, 150, 95, 235), alpha * flick));
                    }
            }
            Ds.Rect(x, baseY, w, 0.0015f, Ds.A(Color.FromArgb(255, 90, 45, 160), alpha));
        }

        // ---------------------------------------------------------------- home screen
        private void DrawHome()
        {
            Frame(true);
            var d = saves.Data; float pad = Ds.Px(0.022f), a = alpha;
            string time = World.CurrentTimeOfDay.ToString(@"hh\:mm");
            Ds.T(time, sx + pad - Ds.Px(0.002f), sy + 0.04f, 1.3f, Ds.A(Ds.Text, a), Ds.Display);
            Ds.T(calendar.DayName.ToUpperInvariant() + "  ·  DAY " + (calendar.Day + 1) + (calendar.IsNight ? "  ·  NIGHT" : ""), sx + pad, sy + 0.125f, 0.25f, Ds.A(Ds.Muted, a));
            float cx = sx + pad, cy = sy + 0.153f;
            cx = Chip(cx, cy, "$" + d.Cash.ToString("N0"), "CASH", Ds.Lime);
            cx = Chip(cx, cy, d.Rep.ToString("N0"), "REP", Ds.Violet);
            if (CareerManager.Instance != null) Chip(cx, cy, "T" + CareerManager.Instance.Tier, "", Ds.Cyan);

            int cols = 4; float gx = sx + pad, gw = sw - 2 * pad, cell = gw / cols, tile = 0.058f, tw = Ds.Px(tile), gy = sy + 0.215f, rowH = 0.093f;
            int sr = sel / cols, sc = sel % cols;
            float tx0 = gx + sc * cell + (cell - tw) / 2, ty0 = gy + sr * rowH;
            if (hlY == 0 && hlX == 0) { hlX = tx0; hlY = ty0; }
            hlX = Ds.Damp(hlX, tx0, 22f); hlY = Ds.Damp(hlY, ty0, 22f);
            var acc = apps[sel].Accent;
            float glow = 0.006f + 0.002f * Ds.Pulse(1.2f);
            Ds.Round(hlX - Ds.Px(glow), hlY - glow, tw + Ds.Px(2 * glow), tile + 2 * glow, 0.02f, Ds.A(acc, 0.32f * a));
            for (int i = 0; i < apps.Count; i++)
            {
                var app = apps[i]; int r = i / cols, c = i % cols;
                tileK[i] = Ds.Damp(tileK[i], i == sel ? 1f : 0f, 16f);
                float k = tileK[i], grow = 0.004f * k;
                float tx = gx + c * cell + (cell - tw) / 2, ty = gy + r * rowH;
                // staggered entrance
                float ent = Ds.EaseBack(Ds.Clamp01(openT * 1.4f - (r * cols + c) * 0.025f));
                ty += (1f - ent) * 0.02f;
                var bg = Ds.Mix(Ds.Surface2, app.Accent, k);
                Ds.Round(tx - Ds.Px(grow), ty - grow, tw + Ds.Px(2 * grow), tile + 2 * grow, 0.016f, Ds.A(bg, a * ent));
                Icons.Draw(app.Icon, tx + tw / 2, ty + tile / 2, 0.03f + 0.002f * k, Ds.A(Ds.Mix(app.Accent, Color.White, k), a * ent), Ds.A(bg, a * ent));
                Ds.T(app.Name, tx + tw / 2, ty + tile + 0.005f, 0.22f, Ds.A(Ds.Mix(Ds.Muted, Ds.Text, k), a * ent), Ds.Body, GTA.UI.Alignment.Center);
                var b = app.Badge != null ? app.Badge() : null;
                if (b != null)
                {
                    float bd = 0.02f + 0.002f * Ds.Pulse(1.5f);
                    Ds.Dot(tx + tw - Ds.Px(0.002f), ty + 0.002f, bd, Ds.A(Ds.Red, a * ent));
                    Ds.T(b, tx + tw - Ds.Px(0.002f), ty - 0.0085f, 0.25f, Ds.A(Color.White, a * ent), Ds.Display, GTA.UI.Alignment.Center);
                }
            }
            // bottom card: next objective, else latest buzz
            float oy = gy + 4 * rowH + 0.004f;
            string head = null, line = null; Color hc = Ds.Violet;
            if (Gameplay.Objectives.CurrentText != null) { head = "NEXT OBJECTIVE"; line = Gameplay.Objectives.CurrentText; }
            else if (d.News.Count > 0) { head = "STREET BUZZ"; line = d.News.Last().Headline; hc = Ds.Pink; }
            if (line != null)
            {
                Ds.Round(gx, oy, gw, 0.06f, 0.014f, Ds.A(Ds.Surface, a));
                Ds.Rect(gx, oy + 0.012f, Ds.Px(0.003f), 0.036f, Ds.A(hc, a));
                Ds.T(head, gx + Ds.Px(0.014f), oy + 0.008f, 0.21f, Ds.A(hc, a));
                Ds.T(Ds.Fit(line, 0.3f, gw - Ds.Px(0.04f)), gx + Ds.Px(0.014f), oy + 0.027f, 0.3f, Ds.A(Ds.Text, a));
            }
            if (IsOpen) Buttons.Draw(Control.FrontendAccept, "Open", Control.FrontendCancel, "Close", Control.FrontendDown, "Move");
        }

        private float Chip(float x, float y, string val, string label, Color col)
        {
            float vs = 0.34f, ls = 0.21f, w = Ds.Width(val, vs, Ds.Display) + (label.Length > 0 ? Ds.Width(label, ls) + Ds.Px(0.006f) : 0) + Ds.Px(0.028f);
            Ds.Round(x, y, w, 0.032f, 0.016f, Ds.A(Ds.Surface2, alpha));
            Ds.Dot(x + Ds.Px(0.011f), y + 0.016f, 0.009f, Ds.A(col, alpha));
            Ds.T(val, x + Ds.Px(0.02f), y + 0.0015f, vs, Ds.A(Ds.Text, alpha), Ds.Display);
            if (label.Length > 0) Ds.T(label, x + w - Ds.Px(0.009f), y + 0.0095f, ls, Ds.A(Ds.Muted, alpha), Ds.Body, GTA.UI.Alignment.Right);
            return x + w + Ds.Px(0.007f);
        }

        // ---------------------------------------------------------------- app screen (any Menu, drawn inside the phone)
        private void DrawApp(Menu m)
        {
            openT = Ds.Step(openT, 1f, 4.2f);
            if (m != lastMenu) { bool deeper = lastMenu != null; lastMenu = m; screenT = deeper ? 0f : 0.4f; listSel = -1f; }
            screenT = Ds.Damp(screenT, 1f, 14f);
            Frame(false);
            m.VisibleRows = 8; m.Clamp();
            float a = alpha * Ds.Clamp01(screenT * 1.3f), off = (1f - screenT) * Ds.Px(0.04f);
            float pad = Ds.Px(0.022f), x = sx + pad + off, w = sw - 2 * pad;
            var acc = current != null ? current.Accent : Ds.Violet;
            // header
            Icons.Draw("back", x + Ds.Px(0.008f), sy + 0.058f, 0.02f, Ds.A(Ds.Muted, a), Ds.A(Ds.Bg, a));
            Ds.T(current != null ? current.Name.ToUpperInvariant() : "MIDNIGHT CLUB", x + Ds.Px(0.022f), sy + 0.049f, 0.25f, Ds.A(acc, a));
            Ds.T(Ds.Fit((m.Title ?? "").ToUpperInvariant(), 0.62f, w, Ds.Display), x, sy + 0.072f, 0.62f, Ds.A(Ds.Text, a), Ds.Display);
            if (!string.IsNullOrEmpty(m.Subtitle)) Ds.T(Ds.Fit(m.Subtitle, 0.25f, w), x, sy + 0.118f, 0.25f, Ds.A(Ds.Muted, a));
            // list
            float ly = sy + 0.152f, rh = 0.047f;
            int first = m.Scroll, n = Math.Min(m.VisibleRows, m.Items.Count - first);
            if (m.Items.Count == 0) Ds.T("Nothing here yet.", x, ly + 0.01f, 0.3f, Ds.A(Ds.Faint, a));
            float target = ly + (m.Selected - first) * rh;
            listSel = listSel < 0 ? target : Ds.Damp(listSel, target, 24f);
            if (m.Items.Count > 0)
            {
                Ds.Round(x - Ds.Px(0.006f), listSel, w + Ds.Px(0.012f), rh - 0.005f, 0.012f, Ds.A(Ds.Surface2, a));
                Ds.Rect(x - Ds.Px(0.006f), listSel + 0.011f, Ds.Px(0.0032f), rh - 0.027f, Ds.A(acc, a));
            }
            for (int i = 0; i < n; i++)
            {
                var it = m.Items[first + i]; bool s = first + i == m.Selected; float y = ly + i * rh;
                string v = it.Value != null ? it.Value() : null;
                float vw = string.IsNullOrEmpty(v) ? 0 : Math.Min(w * 0.45f, Ds.Width(v, 0.27f) + (s && it.OnAdjust != null ? Ds.Px(0.03f) : 0));
                var lc = !it.Enabled ? Ds.Faint : s ? Ds.Text : Color.FromArgb(255, 205, 200, 222);
                Ds.T(Ds.Fit(Clean(it.Label), 0.3f, w - vw - Ds.Px(0.02f)), x + Ds.Px(0.008f), y + 0.0095f, 0.3f, Ds.A(lc, a));
                if (!string.IsNullOrEmpty(v))
                {
                    float vx = x + w - Ds.Px(0.006f);
                    if (s && it.OnAdjust != null && it.Enabled)
                    {
                        Icons.Draw("chevron", vx - Ds.Px(0.004f), y + rh / 2 - 0.0025f, 0.016f, Ds.A(acc, a), Ds.A(Ds.Surface2, a));
                        Ds.T(Ds.Fit(v, 0.27f, w * 0.45f), vx - Ds.Px(0.014f), y + 0.011f, 0.27f, Ds.A(Ds.Text, a), Ds.Body, GTA.UI.Alignment.Right);
                    }
                    else Ds.T(Ds.Fit(v, 0.27f, w * 0.45f), vx, y + 0.011f, 0.27f, Ds.A(s ? Ds.Text : Ds.Muted, a), Ds.Body, GTA.UI.Alignment.Right);
                }
            }
            // scrollbar
            if (m.Items.Count > m.VisibleRows)
            {
                float th = rh * m.VisibleRows, bh = th * m.VisibleRows / m.Items.Count, by = ly + th * first / m.Items.Count;
                Ds.Rect(sx + sw - Ds.Px(0.008f), ly, Ds.Px(0.002f), th, Ds.A(Ds.Surface2, a));
                Ds.Rect(sx + sw - Ds.Px(0.008f), by, Ds.Px(0.002f), bh, Ds.A(acc, a));
            }
            // description card
            if (m.Items.Count > 0 && m.Items[m.Selected].Description != null)
            {
                var desc = m.Items[m.Selected].Description();
                if (!string.IsNullOrEmpty(desc))
                {
                    float dy = sy + sh - 0.125f;
                    Ds.Round(x - Ds.Px(0.006f), dy, w + Ds.Px(0.012f), 0.1f, 0.014f, Ds.A(Ds.Surface, a));
                    Ds.T(Clean(desc), x + Ds.Px(0.006f), dy + 0.01f, 0.25f, Ds.A(Ds.Muted, a), Ds.Body, GTA.UI.Alignment.Left, w - Ds.Px(0.012f));
                }
            }
            var cur = m.Items.Count > 0 ? m.Items[m.Selected] : null;
            if (cur != null && cur.Enabled && cur.OnAdjust != null) Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Back", Control.FrontendRight, "Change", Control.FrontendLeft, "");
            else Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Back", Control.FrontendDown, "Scroll");
        }

        /// <summary>Strips GTA colour tokens (~p~ etc.) that would clash with the phone palette.</summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('~') < 0) return s;
            return System.Text.RegularExpressions.Regex.Replace(s, "~[a-zA-Z]~", "");
        }
    }
}
