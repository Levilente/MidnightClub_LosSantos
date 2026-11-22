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

        private sealed class App { public string Name, Icon; public Func<Menu> Open; public Func<string> Badge; }
        private readonly List<App> apps = new List<App>();

        private static readonly Color Body = Color.FromArgb(240, 12, 10, 22);
        private static readonly Color Tile = Color.FromArgb(230, 32, 24, 52);
        private static readonly Color Purple = Color.FromArgb(255, 170, 90, 255);

        public PhoneManager(SaveManager saves, MenuStack stack, MidnightMenu menu, WorldCalendar calendar, RaceManager races, Func<Menu> rivalsMenu, Func<Menu> clubsMenu)
        {
            this.saves = saves; this.stack = stack; this.menu = menu; this.calendar = calendar; this.races = races; this.rivalsMenu = rivalsMenu; this.clubsMenu = clubsMenu;
            apps.Add(new App { Name = "Messages", Icon = "MSG", Open = Inbox, Badge = () => { int n = saves.Data.Messages.Count(m => !m.Read); return n > 0 ? n.ToString() : null; } });
            apps.Add(new App { Name = "Races", Icon = "RACE", Open = () => races.RacesMenu() });
            apps.Add(new App { Name = "Garage", Icon = "CARS", Open = () => menu.MyVehicles() });
            apps.Add(new App { Name = "Dealers", Icon = "SHOP", Open = () => menu.DealerList() });
            apps.Add(new App { Name = "Catalogue", Icon = "CAT", Open = () => menu.Catalogue() });
            apps.Add(new App { Name = "Career", Icon = "REP", Open = () => menu.CareerMenu() });
            apps.Add(new App { Name = "Rivals", Icon = "VS", Open = () => this.rivalsMenu() });
            apps.Add(new App { Name = "Clubs", Icon = "CLUB", Open = () => this.clubsMenu() });
            apps.Add(new App { Name = "Street Buzz", Icon = "NEWS", Open = NewsApp });
            apps.Add(new App { Name = "Rankings", Icon = "TOP", Open = Rankings });
            apps.Add(new App { Name = "Calendar", Icon = "CAL", Open = CalendarApp });
            apps.Add(new App { Name = "Bank", Icon = "$", Open = () => menu.Finances() });
            apps.Add(new App { Name = "Properties", Icon = "GAR", Open = () => menu.GarageList() });
            apps.Add(new App { Name = "Settings", Icon = "SYS", Open = () => menu.MainMenu() });

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
        public void Toggle() { if (IsOpen || stack.IsOpen) { IsOpen = false; stack.Close(); } else { IsOpen = true; sel = 0; } }

        private void Tick()
        {
            Game.DisableControlThisFrame(Control.Phone);   // our phone replaces the game's in Midnight Club
            if (!stack.IsOpen && !IsOpen && !RaceManager.IsRacing && Game.IsControlJustPressed(Control.Phone)) { IsOpen = true; sel = 0; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Pull_Out", "Phone_SoundSet_Default", true); return; }
            if (!IsOpen || stack.IsOpen) return;
            foreach (var c in new[] { Control.Phone, Control.VehicleRadioWheel, Control.VehicleCinCam, Control.VehicleHeadlight, Control.FrontendPauseAlternate }) Game.DisableControlThisFrame(c);
            int cols = 3;
            if (Game.IsControlJustPressed(Control.FrontendRight)) sel = (sel + 1) % apps.Count;
            if (Game.IsControlJustPressed(Control.FrontendLeft)) sel = (sel - 1 + apps.Count) % apps.Count;
            if (Game.IsControlJustPressed(Control.FrontendDown)) sel = Math.Min(apps.Count - 1, sel + cols);
            if (Game.IsControlJustPressed(Control.FrontendUp)) sel = Math.Max(0, sel - cols);
            if (Game.IsControlJustPressed(Control.FrontendCancel)) { IsOpen = false; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Put_Away", "Phone_SoundSet_Default", true); return; }
            if (Game.IsControlJustPressed(Control.FrontendAccept)) { var m = apps[sel].Open(); if (m != null) stack.Open(m); Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Menu_Accept", "Phone_SoundSet_Default", true); }
            Draw(cols);
        }

        private void Draw(int cols)
        {
            float x = 0.77f, y = 0.30f, w = 0.20f, h = 0.62f;
            Rect(x - 0.006f, y - 0.01f, w + 0.012f, h + 0.02f, Color.FromArgb(255, 4, 4, 8));
            Rect(x, y, w, h, Body);
            Text("MIDNIGHT CLUB", x + w / 2, y + 0.012f, 0.42f, Purple, Font.Pricedown, Alignment.Center);
            Text(calendar.Hour.ToString("00") + ":" + GTA.Chrono.GameClock.Minute.ToString("00") + "   " + calendar.DayName + "   $" + saves.Data.Cash.ToString("N0"), x + w / 2, y + 0.05f, 0.26f, Color.White, Font.ChaletLondon, Alignment.Center);
            float tw = (w - 0.02f) / cols, th = 0.105f;
            for (int i = 0; i < apps.Count; i++)
            {
                int r = i / cols, c = i % cols;
                float tx = x + 0.01f + c * tw, ty = y + 0.085f + r * (th + 0.006f);
                Rect(tx + 0.002f, ty, tw - 0.004f, th, i == sel ? Purple : Tile);
                Text(apps[i].Icon, tx + tw / 2, ty + 0.018f, 0.55f, Color.White, Font.ChaletComprimeCologne, Alignment.Center);
                Text(apps[i].Name, tx + tw / 2, ty + 0.068f, 0.24f, Color.White, Font.ChaletLondon, Alignment.Center);
                var b = apps[i].Badge != null ? apps[i].Badge() : null;
                if (b != null) { Rect(tx + tw - 0.022f, ty + 0.004f, 0.016f, 0.026f, Color.FromArgb(255, 220, 40, 60)); Text(b, tx + tw - 0.014f, ty + 0.005f, 0.26f, Color.White, Font.ChaletLondon, Alignment.Center); }
            }
            Text("ARROWS  select    ENTER  open    BACKSPACE  close", x + w / 2, y + h - 0.03f, 0.22f, Color.FromArgb(200, 200, 200, 210), Font.ChaletLondon, Alignment.Center);
        }

        private static void Rect(float x, float y, float w, float h, Color c) { Function.Call(Hash.DRAW_RECT, x + w / 2f, y + h / 2f, w, h, c.R, c.G, c.B, c.A, false); }
        private static void Text(string s, float x, float y, float scale, Color c, Font f, Alignment a)
        { new TextElement(s, new PointF(x * 1280f, y * 720f), scale, c, f, a, false, true).ScaledDraw(); }
    }
}
