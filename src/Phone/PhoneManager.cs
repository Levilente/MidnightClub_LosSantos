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
        public static PhoneManager Instance { get; private set; }
        public bool IsOpen { get; private set; }
        private int sel;
        private readonly Random rnd = new Random();

        private sealed class App { public string Name, Icon; public Color Accent; public Func<Menu> Open; public Func<string> Badge, Meta; }

        private static KeyValuePair<string, KeyValuePair<Func<Menu>, Func<string>>> Sub(string name, Func<Menu> open, Func<string> meta)
        { return new KeyValuePair<string, KeyValuePair<Func<Menu>, Func<string>>>(name, new KeyValuePair<Func<Menu>, Func<string>>(open, meta)); }

        private Menu Category(string title, string icon, KeyValuePair<string, KeyValuePair<Func<Menu>, Func<string>>>[] subs)
        {
            var m = new Menu(title, "");
            foreach (var sb in subs)
            {
                var s = sb;
                m.Add(new MenuItem(s.Key, () => { var x = s.Value.Key(); if (x != null) stack.Push(x); }) { Value = s.Value.Value });
            }
            return m;
        }

        private string CurrentCarName()
        {
            var r = saves.Data.Vehicles.FirstOrDefault(v => v.Id == saves.Data.CurrentVehicleId);
            if (r == null) return "";
            return "  ·  " + (string.IsNullOrEmpty(r.Nickname) ? r.Model : r.Nickname).ToUpperInvariant();
        }

        private Menu ExitMenu()
        {
            var m = new Menu("Exit", "Your progress is saved");
            m.Add(new MenuItem("Exit to Story Mode", () => { stack.Close(); IsOpen = false; menu.Modes.Exit(); }) { Description = () => "Back to Franklin, Michael and Trevor. Midnight Club keeps your progress." });
            m.Add(new MenuItem("Quit game", () => { menu.Modes.SaveNow(); Script.Wait(300); Function.Call(Hash.QUIT_GAME); }) { Description = () => "Saves and closes GTA V." });
            m.Add(new MenuItem("Cancel", () => stack.Pop()));
            return m;
        }

        // ------------------------------------------------------------------ settings (live, saved to config.ini)
        private Menu Records()
        {
            var d = saves.Data; var m = new Menu("Records", "Your career in numbers");
            Action<string, Func<string>> row = (l, v) => m.Add(new MenuItem(l) { Value = v, Enabled = true });
            row("Races entered", () => d.RacesStarted.ToString("N0"));
            row("Race wins", () => d.RaceHistory.Count(r => r.Position == 1).ToString("N0"));
            row("Podiums", () => d.RaceHistory.Count(r => r.Position >= 1 && r.Position <= 3).ToString("N0"));
            row("Gold medals", () => d.RaceHistory.Count(r => r.Medal == "Gold").ToString("N0"));
            row("Street races won", () => d.StreetRacesWon.ToString("N0"));
            row("Rivals beaten", () => d.Rivals.Count(r => r.Defeated) + " / " + RivalManager.Profiles.Count);
            row("Districts owned", () => Explore.Turf.Instance != null ? Explore.Turf.Instance.Owned + " / " + Explore.Turf.Districts.Length : "-");
            row("Longest jump", () => ((int)d.BestJumpM) + " m");
            row("Top speed", () => ((int)d.BestSpeedKmh) + " km/h");
            row("Distance driven", () => d.Vehicles.Sum(v => v.Stats != null ? v.Stats.DistanceKm : 0).ToString("N0") + " km");
            row("Style points", () => d.StylePointsTotal.ToString("N0"));
            row("Police escapes", () => d.PursuitEscapes + " of " + d.Pursuits + "  ·  " + d.Busts + " busts");
            row("Cars owned", () => d.Vehicles.Count.ToString());
            row("Cars mastered", () => d.Vehicles.Count(v => Career.Mastery.Level(v) >= Career.Mastery.MaxLevel).ToString());
            row("Places discovered", () => Explore.Discovery.Instance != null ? Explore.Discovery.Instance.KnownCount + " / " + Explore.Discovery.Instance.TotalCount : "-");
            row("Photos taken", () => d.PhotosTaken.ToString());
            row("Midnight Club", () => d.ClubMember ? "MEMBER" : "Not yet");
            return m;
        }

        private Menu SettingsMenu()
        {
            var cfg = Config.Current;
            var m = new Menu("Game settings", "Saved instantly");
            Func<string, string, Func<float>, Action<float>, float, float, float, string, MenuItem> num = (label, key, get, set, min, max, step, sec) =>
                new MenuItem(label) { Value = () => get().ToString("0.0#"), OnAdjust = d => { float v = (float)Math.Round(Math.Max(min, Math.Min(max, get() + d * step)) / step) * step; set(v); Config.Save(sec, key, v); } };
            Func<string, string, Func<bool>, Action<bool>, string, MenuItem> flag = (label, key, get, set, sec) =>
                new MenuItem(label, () => { set(!get()); Config.Save(sec, key, get()); }) { Value = () => get() ? "On" : "Off", OnAdjust = d => { set(!get()); Config.Save(sec, key, get()); } };
            m.Add(flag("Midnight Club cameras", "Enabled", () => cfg.CustomCameras, v => cfg.CustomCameras = v, "Camera"));
            m.Add(num("Camera motion (G)", "GForceIntensity", () => cfg.CameraGForce, v => cfg.CameraGForce = v, 0f, 2f, 0.1f, "Camera"));
            m.Add(num("Speed FOV", "DynamicFovIntensity", () => cfg.CameraFovScale, v => cfg.CameraFovScale = v, 0f, 2f, 0.1f, "Camera"));
            m.Add(num("Camera shake", "ShakeIntensity", () => cfg.CameraShake, v => cfg.CameraShake = v, 0f, 2f, 0.1f, "Camera"));
            m.Add(flag("Simcade handling", "Enabled", () => cfg.SimcadeEnabled, v => cfg.SimcadeEnabled = v, "Simcade"));
            m.Add(num("Handling intensity", "Intensity", () => cfg.SimcadeIntensity, v => cfg.SimcadeIntensity = v, 0f, 1.5f, 0.1f, "Simcade"));
            m.Add(num("Car weight feel", "Weight", () => cfg.SimcadeWeight, v => cfg.SimcadeWeight = v, 0f, 1.5f, 0.1f, "Simcade"));
            m.Add(num("Steering assist", "SteeringAssist", () => cfg.SimcadeSteerAssist, v => cfg.SimcadeSteerAssist = v, 0f, 1.5f, 0.1f, "Simcade"));
            m.Add(num("Stability assist", "StabilityAssist", () => cfg.SimcadeStability, v => cfg.SimcadeStability = v, 0f, 1.5f, 0.1f, "Simcade"));
            m.Add(flag("HUD", "Show", () => cfg.ShowHud, v => cfg.ShowHud = v, "HUD"));
            m.Add(flag("Speed effects", "SpeedEffects", () => cfg.SpeedEffects, v => cfg.SpeedEffects = v, "HUD"));
            m.Add(flag("Welcome screen", "WelcomeScreen", () => cfg.ShowWelcome, v => cfg.ShowWelcome = v, "HUD"));
            m.Add(num("Traffic density", "TrafficDensity", () => cfg.TrafficDensity, v => cfg.TrafficDensity = v, 0f, 2f, 0.1f, "World"));
            m.Add(num("Traffic in races", "RaceTraffic", () => cfg.RaceTraffic, v => cfg.RaceTraffic = v, 0f, 2f, 0.1f, "World"));
            m.Add(num("Pedestrians", "PedDensity", () => cfg.PedDensity, v => cfg.PedDensity = v, 0f, 2f, 0.1f, "World"));
            m.Add(flag("Dynamic weather", "DynamicWeather", () => cfg.DynamicWeather, v => cfg.DynamicWeather = v, "World"));
            m.Add(flag("Always night", "AlwaysNight", () => cfg.AlwaysNight, v => cfg.AlwaysNight = v, "World"));
            m.Add(flag("Start in your last garage", "StartInGarage", () => cfg.StartInGarage, v => cfg.StartInGarage = v, "Gameplay"));
            m.Add(flag("Police", "Enabled", () => cfg.PoliceEnabled, v => cfg.PoliceEnabled = v, "Police"));
            return m;
        }
        private readonly List<App> apps = new List<App>();

        public Func<Menu> MilestonesMenu, AbilityMenu;

        public PhoneManager(SaveManager saves, MenuStack stack, MidnightMenu menu, WorldCalendar calendar, RaceManager races, Func<Menu> rivalsMenu, Func<Menu> clubsMenu)
        {
            Instance = this;
            this.saves = saves; this.stack = stack; this.menu = menu; this.calendar = calendar; this.races = races; this.rivalsMenu = rivalsMenu; this.clubsMenu = clubsMenu;
            apps.Add(new App { Name = "Races", Icon = "races", Accent = Ds.Red, Open = () => Category("Races", "races", new[] {
                    Sub("Race events", () => races.RacesMenu(), () => races.Races.Count(r => races.Available(r)) + " open"),
                    Sub("Licence tests", () => Gameplay.Licenses.Instance != null ? Gameplay.Licenses.Instance.Menu() : null, () => Gameplay.Licenses.Instance != null ? "Licence " + Gameplay.Licenses.Instance.LicenseLevel : ""),
                Sub("Route creator", () => RouteCreator.Instance != null ? RouteCreator.Instance.MyRoutes() : null, () => "Build your own"),
                Sub("Rivals", () => this.rivalsMenu(), () => saves.Data.Rivals.Count(r => r.Defeated) + " / " + RivalManager.Profiles.Count + " beaten"),
                    Sub("Rankings", Rankings, () => "#" + MyRank()) }),
                Meta = () => races.Races.Count(r => races.Available(r)) + " OPEN  ·  " + races.Championships.Count() + " CHAMPIONSHIPS" });
            apps.Add(new App { Name = "Garage", Icon = "garage", Accent = Ds.Cyan, Open = () => Category("Garage", "garage", new[] {
                    Sub("My vehicles", () => menu.MyVehicles(), () => saves.Data.Vehicles.Count.ToString()),
                    Sub("Properties", () => menu.GarageList(), () => saves.Data.Garages.Count + " owned"),
                    Sub("Vehicle catalogue", () => menu.Catalogue(), null),
                    Sub("Tuning setup", () => Vehicles.Tuning.Menu(Vehicles.VehicleManager.Instance, stack, saves), () => Vehicles.VehicleManager.Instance != null && Vehicles.VehicleManager.Instance.CurrentRecord != null && Vehicles.Tuning.Any(Vehicles.VehicleManager.Instance.CurrentRecord.Tune) ? "Custom" : "Stock"),
                    Sub("Photo mode", () => { stack.Close(); IsOpen = false; openT = 0f; Effects(false); Gameplay.PhotoMode.Run(saves, Vehicles.VehicleManager.Instance); return null; }, () => saves.Data.PhotosTaken > 0 ? saves.Data.PhotosTaken + " shots" : "New") }),
                Meta = () => saves.Data.Vehicles.Count + (saves.Data.Vehicles.Count == 1 ? " CAR" : " CARS") + CurrentCarName() });
            apps.Add(new App { Name = "Dealers", Icon = "dealers", Accent = Ds.Amber, Open = () => menu.DealerList(),
                Meta = () => menu.Dealers == null ? "" : "NEW STOCK IN " + menu.Dealers.Definitions.Min(x => menu.Dealers.DaysUntilRotation(x)) + " DAYS" });
            apps.Add(new App { Name = "Career", Icon = "career", Accent = Ds.Amber, Open = () => Category("Career", "career", new[] {
                    Sub("Career", () => menu.CareerMenu(), () => CareerManager.Instance != null ? "Tier " + CareerManager.Instance.Tier : ""),
                    Sub("Progress", () => Progression.Instance != null ? Progression.Instance.ProgressMenu() : null, () => Progression.Instance != null ? "Level " + Progression.Instance.Level : ""),
                    Sub("Goals", () => MilestonesMenu != null ? MilestonesMenu() : null, () => saves.Data.MilestonesDone.Count + " / 13"),
                    Sub("Special ability", () => AbilityMenu != null ? AbilityMenu() : null, () => Gameplay.Abilities.Instance != null && Gameplay.Abilities.Instance.Selected != null ? Gameplay.Abilities.Instance.Selected : ""),
                    Sub("Wheelspins", () => Gameplay.Wheelspin.Menu(saves, Economy.TransactionManager.Instance, Vehicles.VehicleRegistry.Instance, stack, () => { stack.Close(); IsOpen = false; openT = 0f; Effects(false); }), () => (saves.Data.Wheelspins + saves.Data.SuperWheelspins) > 0 ? (saves.Data.Wheelspins + saves.Data.SuperWheelspins) + " to spin" : "None"),
                    Sub("Records", Records, () => saves.Data.RaceHistory.Count(r => r.Position == 1) + " wins"),
                    Sub("Turf war", () => Explore.Turf.Instance != null ? Explore.Turf.Instance.TurfMenu() : null, () => Explore.Turf.Instance != null ? Explore.Turf.Instance.Owned + " / " + Explore.Turf.Districts.Length + " districts" : ""),
                    Sub("Bank", () => menu.Finances(), () => "$" + saves.Data.Cash.ToString("N0")) }),
                Meta = () => (CareerManager.Instance != null ? "TIER " + CareerManager.Instance.Tier : "") + (Progression.Instance != null ? "  ·  LEVEL " + Progression.Instance.Level : ""),
                Badge = () => { int n = saves.Data.Wheelspins + saves.Data.SuperWheelspins; return n > 0 ? n.ToString() : null; } });
            apps.Add(new App { Name = "Social", Icon = "social", Accent = Ds.Violet, Open = () => Category("Social", "social", new[] {
                    Sub("Messages", Inbox, () => saves.Data.Messages.Count(m => !m.Read) + " unread"),
                    Sub("Street buzz", NewsApp, null),
                    Sub("Clubs", () => this.clubsMenu(), null) }),
                Badge = () => { int n = saves.Data.Messages.Count(m => !m.Read); return n > 0 ? n.ToString() : null; },
                Meta = () => { int n = saves.Data.Messages.Count(m => !m.Read); return n > 0 ? n + " UNREAD" : "MESSAGES  ·  CLUBS  ·  NEWS"; } });
            apps.Add(new App { Name = "Explore", Icon = "explore", Accent = Ds.Lime, Open = () => Category("Explore", "explore", new[] {
                    Sub("Exploration", () => Progression.Instance != null ? Progression.Instance.ProgressMenu() : null, () => Progression.Instance != null ? Progression.Instance.RoadPercent.ToString("0.0") + "% roads" : ""),
                    Sub("Car deliveries", () => Explore.Deliveries.Instance != null ? Explore.Deliveries.Instance.Menu() : null, () => "Jobs"),
                    Sub("Midnight Tokens", () => Explore.Tokens.Instance != null ? Explore.Tokens.Instance.TokensMenu() : null, () => Explore.Tokens.Instance != null ? Explore.Tokens.Instance.Collected + " / " + Explore.Tokens.Total : ""),
                    Sub("Calendar", CalendarApp, () => calendar.DayName) }),
                Meta = () => (Progression.Instance != null ? Progression.Instance.RoadPercent.ToString("0.0") + "% ROADS" : "") + (Explore.Discovery.Instance != null ? "  ·  " + Explore.Discovery.Instance.KnownCount + " / " + Explore.Discovery.Instance.TotalCount + " FOUND" : "") });
            apps.Add(new App { Name = "Daily", Icon = "daily", Accent = Ds.Pink, Open = () => Category("Daily", "daily", new[] {
                    Sub("Daily challenges", () => Progression.Instance != null ? Progression.Instance.DailyMenu() : null, () => Progression.Instance == null ? "" : Progression.Instance.Today.Count(c => Progression.Instance.Done(c)) + " / " + Progression.Instance.Today.Count),
                    Sub("Weekly playlist", () => Progression.Instance != null ? Progression.Instance.WeeklyMenu() : null, () => Progression.Instance == null ? "" : Progression.Instance.WeekDone + " / 5") }),
                Meta = () => Progression.Instance == null ? "" : Progression.Instance.Today.Count(c => Progression.Instance.Done(c)) + " / " + Progression.Instance.Today.Count + " TODAY  ·  " + Progression.Instance.WeekDone + " / 5 THIS WEEK" });
            apps.Add(new App { Name = "Settings", Icon = "settings", Accent = Ds.Muted, Open = () => Category("Settings", "settings", new[] {
                    Sub("Game settings", SettingsMenu, null),
                    Sub("Edit driver", () => { stack.Close(); IsOpen = false; menu.Modes.EditCharacter(); return null; }, null),
                    Sub("Save game", () => { menu.Modes.SaveNow(); UI.Shard.Mid("SAVED", "", UI.Shard.Green, 1500); return null; }, null) }),
                Meta = () => "CAMERA  ·  HANDLING  ·  HUD" });
            apps.Add(new App { Name = "Exit", Icon = "exit", Accent = Ds.Red, Open = ExitMenu });
            MenuStack.PhoneRenderer = DrawApp;

            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e => { IsOpen = false; openT = 0f; Effects(false); if (e.To == GameMode.MIDNIGHT_CLUB) Welcome(); });
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
            UI.Toast.Post("~p~" + from + "~s~: " + subject);
        }

        public void News(string headline, string body)
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
                if (r != null) m.Add(new MenuItem("Set GPS to the start", () => { Function.Call(Hash.SET_NEW_WAYPOINT, r.Start.X, r.Start.Y); UI.Toast.Post("GPS set to " + r.Name + "."); }));
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

        // ------------------------------------------------------------------ UI: the Midnight Hub
        // Full-screen main menu: the world slows down and blurs, eight big icon cards (live info, badges, accent
        // colours, spring selection, shine) plus EXIT and the next objective. Every sub-screen (any Menu) is shown in
        // the same full-screen style with the category icon as a huge watermark.
        private float openT;
        private Menu lastMenu; private float screenT = 1f, listSel = -1f;
        private App current;
        private bool appActive, fxOn;
        private readonly float[] cardK = new float[16];

        /// <summary>True while any part of the hub is on screen (including the fade-out).</summary>
        public bool IsVisible { get { return openT > 0.001f; } }

        public void Toggle()
        {
            if (IsOpen) { Close(); return; }
            if (stack.IsOpen) stack.Close();
            Open();
        }
        /// <summary>Opens the hub straight on the EXIT confirmation (welcome screen, pause shortcut).</summary>
        public void OpenExit()
        {
            if (stack.IsOpen) stack.Close();
            IsOpen = true; sel = apps.Count - 1; current = apps[sel];
            stack.Open(ExitMenu()); stack.PhoneStyle = true; lastMenu = null;
        }

        /// <summary>Restores time scale and screen effects (script abort / emergency).</summary>
        public static void ResetEffects()
        {
            try { Function.Call(Hash.ANIMPOSTFX_STOP, "MenuMGIn"); Function.Call(Hash.SET_TIME_SCALE, 1f); } catch { }
        }

        private void Open() { IsOpen = true; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true); }
        private void Close() { IsOpen = false; if (stack.PhoneStyle) stack.Close(); Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET", true); }

        private void Effects(bool on)
        {
            if (on == fxOn) return; fxOn = on;
            try
            {
                if (on) { Function.Call(Hash.ANIMPOSTFX_PLAY, "MenuMGIn", 0, true); Function.Call(Hash.SET_TIME_SCALE, 0.15f); }
                else { Function.Call(Hash.ANIMPOSTFX_STOP, "MenuMGIn"); Function.Call(Hash.SET_TIME_SCALE, 1f); }
            }
            catch { }
        }

        private void Tick()
        {
            Game.DisableControlThisFrame(Control.Phone);
            if (!stack.IsOpen && !IsOpen && !RaceManager.IsRacing && UI.Input.JustPressed(Control.Phone)) { Open(); return; }
            if (IsOpen && stack.IsOpen && !stack.PhoneStyle) IsOpen = false;
            if (appActive && !stack.IsOpen) { appActive = false; if (!stack.ClosedByBack) { IsOpen = false; openT = 0f; } }
            if (stack.IsOpen && stack.PhoneStyle) appActive = true;
            Effects(IsOpen && !RaceManager.IsRacing);
            openT = Ds.Step(openT, IsOpen ? 1f : 0f, IsOpen ? 5f : 7f);
            if (!IsVisible) return;
            if (IsOpen && stack.IsOpen) return;   // sub-screen: drawn by DrawApp through the menu stack
            if (IsOpen)
            {
                foreach (var c in new[] { Control.Phone, Control.VehicleRadioWheel, Control.VehicleCinCam, Control.VehicleHeadlight, Control.FrontendPauseAlternate, Control.VehicleDuck, Control.VehicleSelectNextWeapon }) Game.DisableControlThisFrame(c);
                int old = sel, n = apps.Count - 1;   // last app = EXIT (bottom row)
                if (sel == n)
                {
                    if (UI.Input.JustPressed(Control.FrontendUp)) sel = 4;
                    if (UI.Input.JustPressed(Control.FrontendDown)) sel = 0;
                }
                else
                {
                    int r = sel / 4, c = sel % 4;
                    if (UI.Input.JustPressed(Control.FrontendRight)) sel = r * 4 + (c + 1) % 4;
                    if (UI.Input.JustPressed(Control.FrontendLeft)) sel = r * 4 + (c + 3) % 4;
                    if (UI.Input.JustPressed(Control.FrontendDown)) sel = r == 0 ? sel + 4 : n;
                    if (UI.Input.JustPressed(Control.FrontendUp)) sel = r == 0 ? n : sel - 4;
                }
                if (sel != old) Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
                if (UI.Input.JustPressed(Control.FrontendCancel)) Close();
                else if (UI.Input.JustPressed(Control.FrontendAccept))
                {
                    var app = apps[sel]; var m = app.Open();
                    if (m != null) { current = app; stack.Open(m); stack.PhoneStyle = true; lastMenu = null; }
                    Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
                }
            }
            DrawHub();
        }

        // ---------------------------------------------------------------- shared backdrop + top bar
        private const float CW = 0.2f, CH = 0.25f, GX = 0.015f, GY = 0.025f;
        private static float X0 { get { return (1f - (4 * CW + 3 * GX)) / 2f; } }
        private static float XR { get { return X0 + 4 * CW + 3 * GX; } }

        private float Backdrop()
        {
            Theme.FullscreenFrame();
            Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
            float a = Ds.EaseOut(openT);
            Ds.HGrad(0f, 0f, 1f, 1f, Ds.A(Color.FromArgb(255, 9, 8, 14), 0.92f * a), Ds.A(Color.FromArgb(255, 9, 8, 14), 0.55f * a), 10);
            Ds.VGrad(0f, 0.82f, 1f, 0.18f, Ds.A(Ds.Bg, 0f), Ds.A(Ds.Bg, 0.8f * a), 6);
            Ds.Streaks(0f, 0.15f, 1f, 0.7f, Ds.Violet, 0.25f * a, 8, 0.25f);
            // top bar
            float ty = 0.05f - (1f - a) * 0.02f;
            Ds.Logo(X0, ty, 0.95f, a, GTA.UI.Alignment.Left, true);
            var d = saves.Data;
            float ringX = XR - Ds.Px(0.042f), ringY = ty + 0.035f;
            if (Progression.Instance != null)
            {
                VectorIcons.Ring(ringX, ringY, 0.084f, 0.008f, 1f, Ds.A(Ds.Stroke, a));
                VectorIcons.Ring(ringX, ringY, 0.084f, 0.008f, Progression.Instance.LevelProgress, Ds.A(Ds.Violet, a));
                Ds.T(Progression.Instance.Level.ToString(), ringX, ringY - 0.032f, 0.72f, Ds.A(Ds.Text, a), Ds.Display, GTA.UI.Alignment.Center);
                Ds.T("LEVEL", ringX, ringY + 0.012f, 0.2f, Ds.A(Ds.Muted, a), Ds.Display, GTA.UI.Alignment.Center);
            }
            float rx = XR - Ds.Px(0.1f);
            Ds.T("$" + d.Cash.ToString("N0"), rx, ty - 0.006f, 0.75f, Ds.A(Ds.Text, a), Ds.Display, GTA.UI.Alignment.Right);
            Ds.T((d.ClubMember ? "MIDNIGHT CLUB MEMBER  ·  " : "") + d.Rep.ToString("N0") + " REP" + (CareerManager.Instance != null ? "  ·  " + CareerManager.Instance.TierName.ToUpperInvariant() : ""), rx, ty + 0.055f, 0.3f, Ds.A(Ds.Muted, a), Ds.Display, GTA.UI.Alignment.Right);
            return a;
        }

        // ---------------------------------------------------------------- hub grid
        private void DrawHub()
        {
            float a = Backdrop();
            int n = apps.Count - 1;
            for (int i = 0; i < n && i < 8; i++)
            {
                var app = apps[i]; int r = i / 4, c = i % 4;
                cardK[i] = Ds.Damp(cardK[i], i == sel ? 1f : 0f, 14f);
                float k = cardK[i], ent = Ds.EaseOut(Ds.Clamp01(openT * 1.5f - i * 0.05f)), ea = a * ent;
                float g = 0.007f * k, x = X0 + c * (CW + GX) - Ds.Px(g), y = 0.2f + r * (CH + GY) - g + (1f - ent) * 0.03f, w = CW + Ds.Px(2 * g), h = CH + 2 * g;
                if (k > 0.02f) Ds.Rect(x - Ds.Px(0.003f), y - 0.003f, w + Ds.Px(0.006f), h + 0.006f, Ds.A(app.Accent, 0.35f * k * ea));
                Ds.Rect(x, y, w, h, Ds.A(Color.FromArgb(235, 19, 17, 28), ea));
                if (k > 0.01f) Ds.VGrad(x, y, w, h, Ds.A(app.Accent, k * ea), Ds.A(Ds.Mix(app.Accent, Color.FromArgb(255, 20, 10, 30), 0.55f), k * ea), 10);
                if (k > 0.5f) Ds.Shine(x, y, w, h, 0f, 3f, ea);
                Ds.Rect(x, y + h - 0.004f, w, 0.004f, Ds.A(app.Accent, (1f - k) * 0.9f * ea));
                VectorIcons.Draw(app.Icon, x + Ds.Px(0.065f), y + 0.075f, 0.075f + 0.01f * k, Ds.A(Ds.Mix(app.Accent, Color.White, k), ea));
                Ds.T(app.Name.ToUpperInvariant(), x + Ds.Px(0.03f), y + h - 0.1f - 0.004f * k, 0.72f + 0.08f * k, Ds.A(Color.White, ea), Ds.Display);
                string meta = app.Meta != null ? app.Meta() : "";
                Ds.T(Ds.Fit(meta, 0.26f, w - Ds.Px(0.05f), Ds.Display), x + Ds.Px(0.03f), y + h - 0.045f, 0.26f, Ds.A(k > 0.5f ? Color.FromArgb(230, 255, 255, 255) : Ds.Muted, ea), Ds.Display);
                string b = app.Badge != null ? app.Badge() : null;
                if (b != null)
                {
                    float bw = Ds.Px(0.034f);
                    Ds.Rect(x + w - bw - Ds.Px(0.016f), y + 0.015f, bw, 0.034f, Ds.A(Ds.Mix(Ds.Red, Color.White, 0.2f * Ds.Pulse(1.4f)), ea));
                    Ds.T(b, x + w - bw / 2 - Ds.Px(0.016f), y + 0.012f, 0.42f, Ds.A(Color.White, ea), Ds.Display, GTA.UI.Alignment.Center);
                }
            }
            // bottom row: EXIT + next objective
            var ex = apps[n]; cardK[n] = Ds.Damp(cardK[n], sel == n ? 1f : 0f, 14f);
            float ek = cardK[n], yb = 0.2f + 2 * (CH + GY) + 0.005f;
            Ds.Rect(X0, yb, CW, 0.085f, Ds.A(Color.FromArgb(235, 19, 17, 28), a));
            if (ek > 0.01f) Ds.HGrad(X0, yb, CW, 0.085f, Ds.A(Ds.Red, ek * a), Ds.A(Ds.Mix(Ds.Red, Color.Black, 0.5f), ek * a), 16);
            VectorIcons.Draw("exit", X0 + Ds.Px(0.04f), yb + 0.0425f, 0.045f, Ds.A(Ds.Mix(Ds.Muted, Color.White, ek), a));
            Ds.T("EXIT", X0 + Ds.Px(0.075f), yb + 0.014f, 0.72f, Ds.A(Color.White, a), Ds.Display);
            float ox = X0 + CW + GX, ow = 3 * CW + 2 * GX;
            Ds.Rect(ox, yb, ow, 0.085f, Ds.A(Color.FromArgb(235, 19, 17, 28), a));
            string head = null, line = null; Color hc = Ds.Violet;
            if (Gameplay.Objectives.CurrentText != null) { head = "NEXT OBJECTIVE"; line = Gameplay.Objectives.CurrentText; }
            else if (Progression.Instance != null && Progression.Instance.Today.Any(c => !Progression.Instance.Done(c))) { var dc = Progression.Instance.Today.First(c => !Progression.Instance.Done(c)); head = "DAILY CHALLENGE"; line = dc.Text; hc = Ds.Pink; }
            else if (saves.Data.News.Count > 0) { head = "STREET BUZZ"; line = saves.Data.News.Last().Headline; hc = Ds.Pink; }
            if (line != null)
            {
                Ds.Rect(ox, yb, Ds.Px(0.004f), 0.085f, Ds.A(hc, a));
                Ds.T(head, ox + Ds.Px(0.02f), yb + 0.012f, 0.26f, Ds.A(hc, a), Ds.Display);
                Ds.T(Ds.Fit(line, 0.42f, ow - Ds.Px(0.04f)), ox + Ds.Px(0.02f), yb + 0.034f, 0.42f, Ds.A(Ds.Text, a));
            }
            if (IsOpen) Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Close", Control.FrontendDown, "Navigate");
        }

        // ---------------------------------------------------------------- sub-screens (any Menu)
        private void DrawApp(Menu m)
        {
            openT = Ds.Step(openT, 1f, 5f);
            if (m != lastMenu) { lastMenu = m; screenT = 0f; listSel = -1f; }
            screenT = Ds.Damp(screenT, 1f, 12f);
            float a = Backdrop() * Ds.Clamp01(screenT * 1.4f), off = (1f - Ds.EaseOut(screenT)) * Ds.Px(0.03f);
            m.VisibleRows = 10; m.Clamp();
            var acc = current != null ? current.Accent : Ds.Violet;
            string icon = current != null ? current.Icon : "settings";
            // watermark icon
            VectorIcons.Draw(icon, 0.74f, 0.55f, 0.55f, Ds.A(acc, 0.07f * a));
            float x = X0 + off, w = 0.42f, y = 0.19f;
            VectorIcons.Draw(icon, x + Ds.Px(0.024f), y + 0.028f, 0.05f, Ds.A(acc, a));
            Ds.T((current != null ? current.Name : "").ToUpperInvariant() + (m.Title != null && current != null && !string.Equals(m.Title, current.Name, StringComparison.OrdinalIgnoreCase) ? "  /" : ""), x + Ds.Px(0.06f), y - 0.004f, 0.3f, Ds.A(acc, a), Ds.Display);
            Ds.T(Ds.Fit((m.Title ?? "").ToUpperInvariant(), 0.95f, w, Ds.Display), x + Ds.Px(0.058f), y + 0.012f, 0.95f, Ds.A(Ds.Text, a), Ds.Display);
            if (!string.IsNullOrEmpty(m.Subtitle)) Ds.T(Ds.Fit(Clean(m.Subtitle).ToUpperInvariant(), 0.28f, w), x, y + 0.085f, 0.28f, Ds.A(Ds.Muted, a), Ds.Display);
            if (m.Items.Count > 0) Ds.T((m.Selected + 1) + " / " + m.Items.Count, x + w, y + 0.085f, 0.28f, Ds.A(Ds.Faint, a), Ds.Display, GTA.UI.Alignment.Right);
            float ly = y + 0.125f, rh = 0.05f;
            int first = m.Scroll, n = Math.Min(m.VisibleRows, m.Items.Count - first);
            if (m.Items.Count == 0) Ds.T("NOTHING HERE YET", x, ly + 0.01f, 0.4f, Ds.A(Ds.Faint, a), Ds.Display);
            float target = ly + (m.Selected - first) * rh;
            listSel = listSel < 0 ? target : Ds.Damp(listSel, target, 26f);
            for (int i = 0; i < n; i++) Ds.Rect(x, ly + i * rh, w, rh - 0.004f, Ds.A(Color.FromArgb(200, 19, 17, 28), a));
            if (m.Items.Count > 0)
            {
                Ds.HGrad(x, listSel, w, rh - 0.004f, Ds.A(acc, a), Ds.A(Ds.Mix(acc, Color.FromArgb(255, 20, 10, 30), 0.5f), a), 16);
                Ds.Shine(x, listSel, w, rh - 0.004f, 0f, 3f, a);
            }
            for (int i = 0; i < n; i++)
            {
                var it = m.Items[first + i]; bool s = first + i == m.Selected; float yy = ly + i * rh;
                bool onBar = s && Math.Abs(listSel - target) < rh * 0.5f;
                string v = it.Value != null ? Clean(it.Value()) : null;
                float vw = string.IsNullOrEmpty(v) ? 0 : Math.Min(w * 0.45f, Ds.Width(v, 0.32f, Ds.Display) + (s && it.OnAdjust != null ? Ds.Px(0.03f) : 0));
                var lc = !it.Enabled ? Ds.Faint : onBar ? Color.White : Color.FromArgb(255, 222, 218, 236);
                Ds.T(Ds.Fit(Clean(it.Label), 0.36f, w - vw - Ds.Px(0.03f)), x + Ds.Px(0.014f), yy + 0.01f, 0.36f, Ds.A(lc, a));
                if (!string.IsNullOrEmpty(v))
                {
                    float vx = x + w - Ds.Px(0.012f);
                    if (s && it.OnAdjust != null && it.Enabled)
                    {
                        Theme.Sprite("commonmenu", "arrowright", vx - Ds.Px(0.012f), yy + 0.015f, Ds.Px(0.016f), 0.016f, Ds.A(Color.White, a));
                        Theme.Sprite("commonmenu", "arrowleft", vx - Ds.Px(0.03f) - Math.Min(w * 0.4f, Ds.Width(v, 0.32f, Ds.Display)), yy + 0.015f, Ds.Px(0.016f), 0.016f, Ds.A(Color.White, a));
                        Ds.T(Ds.Fit(v, 0.32f, w * 0.4f, Ds.Display), vx - Ds.Px(0.016f), yy + 0.011f, 0.32f, Ds.A(Color.White, a), Ds.Display, GTA.UI.Alignment.Right);
                    }
                    else Ds.T(Ds.Fit(v, 0.32f, w * 0.45f, Ds.Display), vx, yy + 0.011f, 0.32f, Ds.A(onBar ? Color.White : Ds.Muted, a), Ds.Display, GTA.UI.Alignment.Right);
                }
            }
            if (m.Items.Count > m.VisibleRows)
            {
                float th = rh * m.VisibleRows - 0.004f, bh = th * m.VisibleRows / m.Items.Count, by = ly + th * first / m.Items.Count;
                Ds.Rect(x + w + Ds.Px(0.008f), ly, Ds.Px(0.003f), th, Ds.A(Ds.Stroke, a));
                Ds.Rect(x + w + Ds.Px(0.008f), by, Ds.Px(0.003f), bh, Ds.A(acc, a));
            }
            if (m.Items.Count > 0 && m.Items[m.Selected].Description != null)
            {
                var desc = m.Items[m.Selected].Description();
                if (!string.IsNullOrEmpty(desc))
                {
                    float dy = ly + n * rh + 0.015f;
                    Ds.Rect(x, dy, Ds.Px(0.004f), 0.07f, Ds.A(acc, a));
                    Ds.T(Clean(desc), x + Ds.Px(0.016f), dy + 0.006f, 0.3f, Ds.A(Ds.Muted, a), Ds.Body, GTA.UI.Alignment.Left, w - Ds.Px(0.02f));
                }
            }
            var cur = m.Items.Count > 0 ? m.Items[m.Selected] : null;
            if (cur != null && cur.Enabled && cur.OnAdjust != null) Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Back", Control.FrontendRight, "", Control.FrontendLeft, "Change");
            else Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Back", Control.FrontendDown, "Navigate");
        }

        /// <summary>Strips GTA colour tokens (~p~ etc.) that would clash with the phone palette.</summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('~') < 0) return s;
            return System.Text.RegularExpressions.Regex.Replace(s, "~[a-zA-Z]~", "");
        }
    }
}
