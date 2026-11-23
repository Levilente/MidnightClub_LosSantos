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

        private sealed class App { public string Name, Icon; public Color Accent; public Func<Menu> Open; public Func<string> Badge, Meta; }
        private readonly List<App> apps = new List<App>();

        public Func<Menu> MilestonesMenu, AbilityMenu;

        public PhoneManager(SaveManager saves, MenuStack stack, MidnightMenu menu, WorldCalendar calendar, RaceManager races, Func<Menu> rivalsMenu, Func<Menu> clubsMenu)
        {
            this.saves = saves; this.stack = stack; this.menu = menu; this.calendar = calendar; this.races = races; this.rivalsMenu = rivalsMenu; this.clubsMenu = clubsMenu;
            apps.Add(new App { Name = "Messages", Accent = Ds.Violet, Open = Inbox, Badge = () => { int n = saves.Data.Messages.Count(m => !m.Read); return n > 0 ? n + " NEW" : null; } });
            apps.Add(new App { Name = "Progress", Accent = Ds.Violet, Open = () => Progression.Instance != null ? Progression.Instance.ProgressMenu() : null, Meta = () => Progression.Instance != null ? "LV " + Progression.Instance.Level + "  ·  " + Progression.Instance.RoadPercent.ToString("0.0") + "% ROADS" : "" });
            apps.Add(new App { Name = "Daily", Accent = Ds.Amber, Open = () => Progression.Instance != null ? Progression.Instance.DailyMenu() : null, Meta = () => Progression.Instance == null ? "" : Progression.Instance.Today.Count(c => Progression.Instance.Done(c)) + " / " + Progression.Instance.Today.Count });
            apps.Add(new App { Name = "Races", Accent = Ds.Red, Open = () => races.RacesMenu(), Meta = () => { int n = races.Races.Count(r => races.Available(r)); return n + " OPEN"; } });
            apps.Add(new App { Name = "My garage", Accent = Ds.Cyan, Open = () => menu.MyVehicles(), Meta = () => saves.Data.Vehicles.Count + (saves.Data.Vehicles.Count == 1 ? " CAR" : " CARS") });
            apps.Add(new App { Name = "Dealerships", Accent = Ds.Amber, Open = () => menu.DealerList(), Meta = () => menu.Dealers == null ? "" : "NEW STOCK " + menu.Dealers.Definitions.Min(x => menu.Dealers.DaysUntilRotation(x)) + "D" });
            apps.Add(new App { Name = "Rivals", Accent = Ds.Pink, Open = () => this.rivalsMenu(), Meta = () => saves.Data.Rivals.Count(r => r.Defeated) + " / 6 BEATEN" });
            apps.Add(new App { Name = "Clubs", Accent = Ds.Blue, Open = () => this.clubsMenu() });
            apps.Add(new App { Name = "Career", Accent = Ds.Amber, Open = () => menu.CareerMenu(), Meta = () => CareerManager.Instance != null ? "TIER " + CareerManager.Instance.Tier : "" });
            apps.Add(new App { Name = "Rankings", Accent = Ds.Lime, Open = Rankings, Meta = () => "#" + MyRank() });
            apps.Add(new App { Name = "Street buzz", Accent = Ds.Pink, Open = NewsApp, Meta = () => saves.Data.News.Count > 0 ? "DAY " + (saves.Data.News.Last().Day + 1) : "" });
            apps.Add(new App { Name = "Calendar", Accent = Ds.Cyan, Open = CalendarApp, Meta = () => calendar.DayName.Substring(0, 3).ToUpperInvariant() });
            apps.Add(new App { Name = "Bank", Accent = Ds.Lime, Open = () => menu.Finances(), Meta = () => "$" + saves.Data.Cash.ToString("N0") });
            apps.Add(new App { Name = "Property", Accent = Ds.Blue, Open = () => menu.GarageList(), Meta = () => saves.Data.Garages.Count + " OWNED" });
            apps.Add(new App { Name = "Catalogue", Accent = Ds.Violet, Open = () => menu.Catalogue() });
            apps.Add(new App { Name = "Goals", Accent = Ds.Red, Open = () => MilestonesMenu != null ? MilestonesMenu() : null, Meta = () => saves.Data.MilestonesDone.Count + " / 13" });
            apps.Add(new App { Name = "Ability", Accent = Ds.Amber, Open = () => AbilityMenu != null ? AbilityMenu() : null, Meta = () => Gameplay.Abilities.Instance != null && Gameplay.Abilities.Instance.Selected != null ? Gameplay.Abilities.Instance.Selected.ToUpperInvariant() : "" });
            apps.Add(new App { Name = "Settings", Accent = Ds.Muted, Open = () => menu.MainMenu() });
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
            if (action != null && action.StartsWith("race:") && Explore.Discovery.Instance != null) Explore.Discovery.Instance.Reveal(action, false);
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

        private int MyRank()
        {
            long me = saves.Data.Rep; int rank = 1;
            for (int i = 0; i < RaceManager.DriverNames.Length; i++)
            {
                var n = RaceManager.DriverNames[i];
                if (400L + Dealerships.RotationMath.Seed(n) % 6000 + (long)calendar.Day * (40 + i * 7) > me) rank++;
            }
            return rank;
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
        private float listY = -1f;
        private Menu lastMenu; private float screenT = 1f, appSel = -1f;
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
        private void Open() { IsOpen = true; listY = -1f; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Pull_Out", "Phone_SoundSet_Default", true); }
        private void Close() { IsOpen = false; if (stack.PhoneStyle) stack.Close(); Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Put_Away", "Phone_SoundSet_Default", true); }

        private void Tick()
        {
            Game.DisableControlThisFrame(Control.Phone);   // our phone replaces the game's in Midnight Club
            if (!stack.IsOpen && !IsOpen && !RaceManager.IsRacing && UI.Input.JustPressed(Control.Phone)) { Open(); return; }
            if (IsOpen && stack.IsOpen && !stack.PhoneStyle) IsOpen = false;   // a full-screen menu took over
            if (appActive && !stack.IsOpen) { appActive = false; if (!stack.ClosedByBack) { IsOpen = false; openT = 0f; } }
            if (stack.IsOpen && stack.PhoneStyle) appActive = true;
            openT = Ds.Step(openT, IsOpen ? 1f : 0f, IsOpen ? 4.5f : 6f);
            if (!IsVisible) return;
            if (IsOpen && stack.IsOpen) return;                      // app screen: drawn by DrawApp via the menu stack
            if (IsOpen)
            {
                foreach (var c in new[] { Control.Phone, Control.VehicleRadioWheel, Control.VehicleCinCam, Control.VehicleHeadlight, Control.FrontendPauseAlternate, Control.VehicleDuck, Control.VehicleSelectNextWeapon }) Game.DisableControlThisFrame(c);
                int old = sel;
                if (UI.Input.JustPressed(Control.FrontendDown)) sel = (sel + 1) % apps.Count;
                if (UI.Input.JustPressed(Control.FrontendUp)) sel = (sel - 1 + apps.Count) % apps.Count;
                if (sel != old) Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Menu_Navigate", "Phone_SoundSet_Default", true);
                if (UI.Input.JustPressed(Control.FrontendCancel)) { Close(); }
                else if (UI.Input.JustPressed(Control.FrontendAccept))
                {
                    var app = apps[sel]; var m = app.Open();
                    if (m != null) { current = app; stack.Open(m); stack.PhoneStyle = true; lastMenu = null; }
                    Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Menu_Accept", "Phone_SoundSet_Default", true);
                }
            }
            DrawHome();
        }

        // ---------------------------------------------------------------- frame
        private const float PH = 0.74f;
        private float px, py, pw, alpha, pad;

        private void Frame()
        {
            Theme.FullscreenFrame();
            float e = Ds.EaseOut(openT);
            alpha = Ds.Clamp01(openT * 1.5f);
            pw = Ds.Px(PH * 0.46f); px = 0.972f - pw + (1f - e) * 0.02f; py = 0.175f + (1f - e) * 0.85f;
            pad = Ds.Px(0.02f);
            Ds.Rect(px - Ds.Px(0.004f), py - 0.004f, pw + Ds.Px(0.008f), PH + 0.008f, Ds.A(Color.Black, 0.45f * alpha));
            Ds.Rect(px, py, pw, PH, Ds.A(Color.FromArgb(250, 12, 11, 19), alpha));
            Ds.Streaks(px, py + 0.02f, pw, 0.17f, Ds.Violet, 0.35f * alpha, 9, 0.35f);
            Ds.Rect(px, py, Ds.Px(0.0025f), PH, Ds.A(Ds.Violet, alpha));
            Ds.Skew(px, py, pw * 0.35f, 0.003f, Ds.Px(0.0008f), Ds.A(Ds.Violet, alpha));
            Ds.Skew(px + pw * 0.37f, py, pw * 0.06f, 0.003f, Ds.Px(0.0008f), Ds.A(Ds.Cyan, alpha));
            Ds.T("MIDNIGHT", px + pad, py + 0.012f, 0.26f, Ds.A(Ds.Muted, alpha), Ds.Display);
            Ds.T(World.CurrentTimeOfDay.ToString(@"hh\:mm"), px + pw - pad, py + 0.012f, 0.26f, Ds.A(Ds.Muted, alpha), Ds.Display, GTA.UI.Alignment.Right);
        }

        // ---------------------------------------------------------------- home
        private void DrawHome()
        {
            Frame();
            var d = saves.Data; float a = alpha, x = px + pad, w = pw - 2 * pad;
            Ds.T(World.CurrentTimeOfDay.ToString(@"hh\:mm"), x - Ds.Px(0.002f), py + 0.036f, 1.35f, Ds.A(Ds.Text, a), Ds.Display);
            Ds.T(calendar.DayName.ToUpperInvariant() + "  /  DAY " + (calendar.Day + 1) + (calendar.IsNight ? "  /  NIGHT" : ""), x, py + 0.124f, 0.27f, Ds.A(Ds.Muted, a), Ds.Display);
            float tx = x, ty = py + 0.158f;
            tx += Ds.Tag("$" + d.Cash.ToString("N0"), tx, ty, Ds.Lime, Ds.Bg, 0.3f, a) + Ds.Px(0.004f);
            tx += Ds.Tag(d.Rep.ToString("N0") + " REP", tx, ty, Ds.Violet, Ds.Text, 0.3f, a) + Ds.Px(0.004f);
            if (CareerManager.Instance != null) Ds.Tag("TIER " + CareerManager.Instance.Tier, tx, ty, Ds.Surface2, Ds.Text, 0.3f, a);
            Ds.Skew(x, py + 0.203f, w, 0.003f, Ds.Px(0.001f), Ds.A(Ds.Surface2, a));

            float ly = py + 0.218f, rh = 0.0255f;
            float target = ly + sel * rh;
            listY = listY < 0 ? target : Ds.Damp(listY, target, 26f);
            var acc = apps[sel].Accent; float sl = Ds.SlantFor(rh - 0.003f);
            Ds.Skew(x - Ds.Px(0.009f), listY, w + Ds.Px(0.013f), rh - 0.003f, sl, Ds.A(acc, a));
            Ds.Shine(x - Ds.Px(0.009f), listY, w + Ds.Px(0.013f), rh - 0.003f, sl, 2.4f, a);
            for (int i = 0; i < apps.Count; i++)
            {
                var app = apps[i]; bool s = i == sel; float y = ly + i * rh;
                float ent = Ds.EaseOut(Ds.Clamp01(openT * 1.6f - i * 0.03f)), ea = a * ent, ox = (1f - ent) * Ds.Px(0.02f);
                bool onBar = s && Math.Abs(listY - target) < rh * 0.5f;
                var dark = Ds.Bg;
                Ds.T((i + 1).ToString("00"), x + Ds.Px(0.003f) + ox, y + 0.0045f, 0.21f, Ds.A(onBar ? dark : Ds.Faint, ea), Ds.Display);
                Ds.T(app.Name.ToUpperInvariant(), x + Ds.Px(0.022f) + ox, y - 0.0025f, 0.37f, Ds.A(onBar ? dark : Ds.Text, ea), Ds.Display);
                string badge = app.Badge != null ? app.Badge() : null;
                string meta = badge ?? (app.Meta != null ? app.Meta() : null);
                if (string.IsNullOrEmpty(meta)) continue;
                if (badge != null && !onBar)
                {
                    float bw = Ds.Width(badge, 0.24f, Ds.Display) + Ds.Px(0.014f) + Ds.SlantFor(0.024f);
                    Ds.Tag(badge, x + w - bw + ox, y + 0.001f, Ds.Mix(Ds.Red, Color.White, 0.25f * Ds.Pulse(1.4f)), Ds.Text, 0.24f, ea);
                }
                else Ds.T(meta, x + w - Ds.Px(0.004f) + ox, y + 0.003f, 0.24f, Ds.A(onBar ? dark : Ds.Muted, ea), Ds.Display, GTA.UI.Alignment.Right);
            }
            // footer: next objective, else latest buzz
            float fy = py + PH - 0.052f;
            string head = null, line = null; Color hc = Ds.Violet;
            if (Gameplay.Objectives.CurrentText != null) { head = "NEXT OBJECTIVE"; line = Gameplay.Objectives.CurrentText; }
            else if (d.News.Count > 0) { head = "STREET BUZZ"; line = d.News.Last().Headline; hc = Ds.Pink; }
            if (line != null)
            {
                Ds.Rect(x, fy, w, 0.0015f, Ds.A(Ds.Surface2, a));
                Ds.T(head, x, fy + 0.007f, 0.22f, Ds.A(hc, a), Ds.Display);
                Ds.T(Ds.Fit(line, 0.28f, w), x, fy + 0.023f, 0.28f, Ds.A(Ds.Text, a));
            }
            if (IsOpen) Buttons.Draw(Control.FrontendAccept, "Open", Control.FrontendCancel, "Close", Control.FrontendDown, "Select");
        }

        // ---------------------------------------------------------------- app screen (any Menu, drawn inside the phone)
        private void DrawApp(Menu m)
        {
            openT = Ds.Step(openT, 1f, 4.5f);
            if (m != lastMenu) { bool deeper = lastMenu != null; lastMenu = m; screenT = deeper ? 0f : 0.3f; appSel = -1f; }
            screenT = Ds.Damp(screenT, 1f, 14f);
            Frame();
            m.VisibleRows = 13; m.Clamp();
            float a = alpha * Ds.Clamp01(screenT * 1.3f), off = (1f - screenT) * Ds.Px(0.04f);
            float x = px + pad + off, w = pw - 2 * pad;
            var acc = current != null ? current.Accent : Ds.Violet;
            // header: app tag, big title, subtitle
            Ds.Tag((current != null ? current.Name : "Midnight Club").ToUpperInvariant(), x, py + 0.045f, acc, Ds.Bg, 0.26f, a);
            Ds.T(Ds.Fit((m.Title ?? "").ToUpperInvariant(), 0.78f, w, Ds.Display), x - Ds.Px(0.002f), py + 0.072f, 0.78f, Ds.A(Ds.Text, a), Ds.Display);
            if (!string.IsNullOrEmpty(m.Subtitle)) Ds.T(Ds.Fit(Clean(m.Subtitle).ToUpperInvariant(), 0.26f, w * 0.75f), x, py + 0.13f, 0.26f, Ds.A(Ds.Muted, a), Ds.Display);
            if (m.Items.Count > 0) Ds.T((m.Selected + 1) + " / " + m.Items.Count, x + w, py + 0.13f, 0.26f, Ds.A(Ds.Faint, a), Ds.Display, GTA.UI.Alignment.Right);
            Ds.Stripes(x, py + 0.16f, w, acc, Ds.Cyan, a);
            // list
            float ly = py + 0.185f, rh = 0.034f;
            int first = m.Scroll, n = Math.Min(m.VisibleRows, m.Items.Count - first);
            if (m.Items.Count == 0) Ds.T("NOTHING HERE YET", x, ly + 0.01f, 0.3f, Ds.A(Ds.Faint, a), Ds.Display);
            float target = ly + (m.Selected - first) * rh;
            appSel = appSel < 0 ? target : Ds.Damp(appSel, target, 26f);
            float sl = Ds.SlantFor(rh - 0.004f);
            if (m.Items.Count > 0)
            {
                Ds.Skew(x - Ds.Px(0.009f), appSel, w + Ds.Px(0.013f), rh - 0.004f, sl, Ds.A(acc, a));
                Ds.Shine(x - Ds.Px(0.009f), appSel, w + Ds.Px(0.013f), rh - 0.004f, sl, 2.4f, a);
            }
            for (int i = 0; i < n; i++)
            {
                var it = m.Items[first + i]; bool s = first + i == m.Selected; float y = ly + i * rh;
                bool onBar = s && Math.Abs(appSel - target) < rh * 0.5f;
                string v = it.Value != null ? Clean(it.Value()) : null;
                float vw = string.IsNullOrEmpty(v) ? 0 : Math.Min(w * 0.45f, Ds.Width(v, 0.27f) + (s && it.OnAdjust != null ? Ds.Px(0.024f) : 0));
                var lc = onBar ? Ds.Bg : !it.Enabled ? Ds.Faint : Color.FromArgb(255, 222, 218, 236);
                Ds.T(Ds.Fit(Clean(it.Label), 0.31f, w - vw - Ds.Px(0.02f)), x + Ds.Px(0.006f), y + 0.006f, 0.31f, Ds.A(lc, a));
                if (!string.IsNullOrEmpty(v))
                {
                    float vx = x + w - Ds.Px(0.004f);
                    if (s && it.OnAdjust != null && it.Enabled)
                    {
                        Arrow(false, vx - Ds.Px(0.024f) - Math.Min(w * 0.4f, Ds.Width(v, 0.27f)), y + 0.009f, Ds.A(Ds.Bg, a));
                        Arrow(true, vx - Ds.Px(0.009f), y + 0.009f, Ds.A(Ds.Bg, a));
                        Ds.T(Ds.Fit(v, 0.27f, w * 0.4f), vx - Ds.Px(0.012f), y + 0.007f, 0.27f, Ds.A(Ds.Bg, a), Ds.Body, GTA.UI.Alignment.Right);
                    }
                    else Ds.T(Ds.Fit(v, 0.27f, w * 0.45f), vx, y + 0.007f, 0.27f, Ds.A(onBar ? Ds.Bg : Ds.Muted, a), Ds.Body, GTA.UI.Alignment.Right);
                }
            }
            if (m.Items.Count > m.VisibleRows)
            {
                float th = rh * m.VisibleRows, bh = th * m.VisibleRows / m.Items.Count, by = ly + th * first / m.Items.Count;
                Ds.Rect(px + pw - Ds.Px(0.007f), ly, Ds.Px(0.002f), th, Ds.A(Ds.Surface2, a));
                Ds.Rect(px + pw - Ds.Px(0.007f), by, Ds.Px(0.002f), bh, Ds.A(acc, a));
            }
            // description
            if (m.Items.Count > 0 && m.Items[m.Selected].Description != null)
            {
                var desc = m.Items[m.Selected].Description();
                if (!string.IsNullOrEmpty(desc))
                {
                    float dy = py + PH - 0.098f;
                    Ds.Rect(x, dy, w, 0.0015f, Ds.A(Ds.Surface2, a));
                    Ds.T(Clean(desc), x, dy + 0.009f, 0.26f, Ds.A(Ds.Muted, a), Ds.Body, GTA.UI.Alignment.Left, w);
                }
            }
            var cur = m.Items.Count > 0 ? m.Items[m.Selected] : null;
            if (cur != null && cur.Enabled && cur.OnAdjust != null) Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Back", Control.FrontendRight, "Change", Control.FrontendLeft, "");
            else Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Back", Control.FrontendDown, "Scroll");
        }

        private static void Arrow(bool right, float x, float y, Color c)
        { Theme.Sprite("commonmenu", right ? "arrowright" : "arrowleft", x, y, Ds.Px(0.016f), 0.016f, c); }

        /// <summary>Strips GTA colour tokens (~p~ etc.) that would clash with the phone palette.</summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('~') < 0) return s;
            return System.Text.RegularExpressions.Regex.Replace(s, "~[a-zA-Z]~", "");
        }
    }
}
