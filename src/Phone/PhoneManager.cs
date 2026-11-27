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
                private readonly Random rnd = new Random();

        /// <summary>One tab of the Forza-style hub (RACES, GARAGE ...): a horizontally scrolling mosaic of tiles.</summary>
        private sealed class App { public string Name, Icon; public Color Accent; public Func<string> Badge; public readonly List<Tile> Tiles = new List<Tile>(); }
        /// <summary>A mosaic tile: icon, title, one live info line, a description for the info strip; W x H grid cells.</summary>
        private sealed class Tile
        {
            public string Title, Icon, Desc; public Func<string> Meta, Badge, Head, Thumb; public Func<Menu> Open; public int W = 1, H = 1;
            public int Col, Row; public float K;      // packed position, selection animation
        }
        private static Tile T(string title, string icon, string desc, Func<Menu> open, Func<string> meta = null, int w = 1, int h = 1)
        { return new Tile { Title = title, Icon = icon, Desc = desc, Open = open, Meta = meta, W = w, H = h }; }
        private static void Pack(App app)
        {
            var used = new HashSet<int>();
            foreach (var t in app.Tiles)
                for (int c = 0; ; c++)
                {
                    bool placed = false;
                    for (int r = 0; r + t.H <= 2 && !placed; r++)
                    {
                        bool ok = true;
                        for (int dc = 0; dc < t.W && ok; dc++) for (int dr = 0; dr < t.H && ok; dr++) if (used.Contains((c + dc) * 2 + r + dr)) ok = false;
                        if (!ok) continue;
                        t.Col = c; t.Row = r; placed = true;
                        for (int dc = 0; dc < t.W; dc++) for (int dr = 0; dr < t.H; dr++) used.Add((c + dc) * 2 + r + dr);
                    }
                    if (placed) break;
                }
        }
        private void Leave() { stack.Close(); IsOpen = false; openT = 0f; Effects(false); }

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
            var m = new Menu("Quit game", "Your progress is saved");
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

        private string DrivingLabel()
        {
            var cfg = Config.Current; float[] iv = { 0.7f, 1f, 1.3f }; string[] nm = { "Arcade", "Simcade", "Simulation" };
            for (int i = 0; i < 3; i++) if (Math.Abs(cfg.SimcadeIntensity - iv[i]) < 0.01f) return nm[i];
            return "Custom";
        }

        private Menu SettingsMenu(string group)
        {
            var cfg = Config.Current;
            var m = new Menu(group == "Camera" ? "Cameras" : group, "Saved instantly");
            var help = new Dictionary<string, string>
            {
                { "Midnight Club cameras", "Bumper, hood, roof, cockpit and three chase cameras tuned after Forza Horizon. Off = GTA's own cameras." },
                { "Camera motion (G)", "How much the chase cameras lean into acceleration, braking and corners. Mounted cameras stay locked to the body." },
                { "Speed FOV", "How much the field of view widens with speed, in every camera." },
                { "Camera shake", "Rumble at high speed and over rough ground (chase cameras)." },
                { "Simcade handling", "Era-correct weight, roll and grip per car. Off = GTA's stock handling." },
                { "Handling intensity", "How strongly the simcade model changes the car." },
                { "Car weight feel", "Weight transfer under braking and turning in." },
                { "Steering assist", "Counter-steer help when the rear steps out." },
                { "Stability assist", "Keeps the car from spinning on throttle." },
                { "HUD", "Speedometer, minimap and race HUD." },
                { "Speed effects", "Speed lines and edge vignette at high speed." },
                { "Welcome screen", "The Midnight Club title card when you switch in with F7." },
                { "Traffic density", "Cars on the road in free roam." },
                { "Traffic in races", "Cars on the road during races." },
                { "Pedestrians", "People on the pavements." },
                { "Dynamic weather", "Weather changes over the day, with seasonal bias." },
                { "Always night", "The clock stays between dusk and dawn." },
                { "Start in your last garage", "Loading in puts you in the last garage you used." },
                { "Police", "Police chase you for speeding, crashes and races." },
            };
            Func<MenuItem, string, string, MenuItem> G = (it, g, icon) =>
            {
                if (g != group) return it;
                it.Icon = icon; string h; if (it.Description == null && help.TryGetValue(it.Label, out h)) it.Description = () => h;
                m.Add(it); return it;
            };
            Func<string, string, Func<float>, Action<float>, float, float, float, string, MenuItem> num = (label, key, get, set, min, max, step, sec) =>
                new MenuItem(label) { Value = () => get().ToString("0.0#"), OnAdjust = d => { float v = (float)Math.Round(Math.Max(min, Math.Min(max, get() + d * step)) / step) * step; set(v); Config.Save(sec, key, v); } };
            Func<string, string, Func<bool>, Action<bool>, string, MenuItem> flag = (label, key, get, set, sec) =>
                new MenuItem(label, () => { set(!get()); Config.Save(sec, key, get()); }) { Value = () => get() ? "On" : "Off", OnAdjust = d => { set(!get()); Config.Save(sec, key, get()); } };
            G(flag("Midnight Club cameras", "Enabled", () => cfg.CustomCameras, v => cfg.CustomCameras = v, "Camera"), "Camera", "eye");
            G(num("Camera motion (G)", "GForceIntensity", () => cfg.CameraGForce, v => cfg.CameraGForce = v, 0f, 2f, 0.1f, "Camera"), "Camera", "move");
            G(num("Speed FOV", "DynamicFovIntensity", () => cfg.CameraFovScale, v => cfg.CameraFovScale = v, 0f, 2f, 0.1f, "Camera"), "Camera", "aperture");
            G(num("Camera shake", "ShakeIntensity", () => cfg.CameraShake, v => cfg.CameraShake = v, 0f, 2f, 0.1f, "Camera"), "Camera", "sparkles");
            string[] models = { "Arcade", "Simcade", "Simulation" };
            float[][] mv = { new[] { 0.7f, 1.4f, 1.5f, 0.6f }, new[] { 1f, 1f, 1f, 1f }, new[] { 1.3f, 0.35f, 0.25f, 1.3f } };
            Func<int> curModel = () => { for (int i = 0; i < 3; i++) if (Math.Abs(cfg.SimcadeIntensity - mv[i][0]) < 0.01f && Math.Abs(cfg.SimcadeSteerAssist - mv[i][1]) < 0.01f && Math.Abs(cfg.SimcadeStability - mv[i][2]) < 0.01f) return i; return -1; };
            Action<int> setModel = i => { cfg.SimcadeIntensity = mv[i][0]; cfg.SimcadeSteerAssist = mv[i][1]; cfg.SimcadeStability = mv[i][2]; cfg.SimcadeWeight = mv[i][3]; Config.Save("Simcade", "Intensity", mv[i][0]); Config.Save("Simcade", "SteeringAssist", mv[i][1]); Config.Save("Simcade", "StabilityAssist", mv[i][2]); Config.Save("Simcade", "Weight", mv[i][3]); };
            G(new MenuItem("Driving model", () => setModel((Math.Max(0, curModel()) + 1) % 3)) { Value = () => curModel() < 0 ? "Custom" : models[curModel()], OnAdjust = d => setModel(((curModel() < 0 ? 1 : curModel()) + d + 3) % 3),
                Description = () => "Arcade: grippy and forgiving. Simcade: weight, roll and era-correct handling with light assists. Simulation: full weight transfer, no anti-spin assist." }, "Driving", "gauge");
            G(flag("Simcade handling", "Enabled", () => cfg.SimcadeEnabled, v => cfg.SimcadeEnabled = v, "Simcade"), "Driving", "car");
            G(flag("Real drivetrain", "RealDrivetrain", () => cfg.RealDrivetrain, v => cfg.RealDrivetrain = v, "Simcade"), "Driving", "gauge");
            G(flag("Inverse power", "InversePower", () => cfg.InversePower, v => cfg.InversePower = v, "Simcade"), "Driving", "flame");
            G(num("Handling intensity", "Intensity", () => cfg.SimcadeIntensity, v => cfg.SimcadeIntensity = v, 0f, 1.5f, 0.1f, "Simcade"), "Driving", "sliders-horizontal");
            G(num("Car weight feel", "Weight", () => cfg.SimcadeWeight, v => cfg.SimcadeWeight = v, 0f, 1.5f, 0.1f, "Simcade"), "Driving", "mountain-snow");
            G(num("Steering assist", "SteeringAssist", () => cfg.SimcadeSteerAssist, v => cfg.SimcadeSteerAssist = v, 0f, 1.5f, 0.1f, "Simcade"), "Driving", "route");
            G(num("Stability assist", "StabilityAssist", () => cfg.SimcadeStability, v => cfg.SimcadeStability = v, 0f, 1.5f, 0.1f, "Simcade"), "Driving", "shield");
            string[] themes = { "Auto", "Dark", "Light" };
            Action<int> setTheme = d => { int i = Math.Max(0, Array.IndexOf(themes, cfg.UiTheme)); i = (i + d + 3) % 3; cfg.UiTheme = themes[i]; Config.Save("HUD", "Theme", cfg.UiTheme); Th.Mode = (UiMode)i; };
            G(new MenuItem("Theme", () => setTheme(1)) { Value = () => cfg.UiTheme, OnAdjust = setTheme, Description = () => "Auto follows the in-game clock: light glass by day, dark at night." }, "Display", "palette");
            string[] bstyles = { "Auto", "Xbox", "PlayStation" };
            Action<int> setB = d => { int i = Math.Max(0, Array.IndexOf(bstyles, Kit.ButtonStyle)); i = (i + d + 3) % 3; Kit.ButtonStyle = bstyles[i]; Config.Save("HUD", "ButtonStyle", Kit.ButtonStyle); };
            G(new MenuItem("Controller buttons", () => setB(1)) { Value = () => Kit.ButtonStyle, OnAdjust = setB, Description = () => "Button glyphs for controllers. Auto shows PlayStation buttons when a DualShock / DualSense is connected, Xbox otherwise." }, "Display", "gamepad-2");
            G(flag("HUD", "Show", () => cfg.ShowHud, v => cfg.ShowHud = v, "HUD"), "Display", "monitor");
            G(new MenuItem("Text calibration", () => TextCalib.On = !TextCalib.On) { Value = () => TextCalib.On ? "On" : "Off", OnAdjust = d => TextCalib.On = !TextCalib.On, Description = () => "Shows a test card for text alignment. Take a screenshot of it and send it to fix text centring exactly." }, "Display", "eye");
            G(flag("Speed effects", "SpeedEffects", () => cfg.SpeedEffects, v => cfg.SpeedEffects = v, "HUD"), "Display", "flame");
            G(flag("Welcome screen", "WelcomeScreen", () => cfg.ShowWelcome, v => cfg.ShowWelcome = v, "HUD"), "Display", "sparkles");
            G(num("Traffic density", "TrafficDensity", () => cfg.TrafficDensity, v => cfg.TrafficDensity = v, 0f, 2f, 0.1f, "World"), "World", "car-front");
            G(num("Traffic in races", "RaceTraffic", () => cfg.RaceTraffic, v => cfg.RaceTraffic = v, 0f, 2f, 0.1f, "World"), "World", "flag");
            G(num("Pedestrians", "PedDensity", () => cfg.PedDensity, v => cfg.PedDensity = v, 0f, 2f, 0.1f, "World"), "World", "users");
            G(new MenuItem("Radio station") { Value = () => { int i = Array.IndexOf(Gameplay.Radio.Ids, cfg.RadioStation ?? ""); return Gameplay.Radio.Names[Math.Max(0, i)]; },
                OnAdjust = d => { int i = Math.Max(0, Array.IndexOf(Gameplay.Radio.Ids, cfg.RadioStation ?? "")); i = (i + d + Gameplay.Radio.Ids.Length) % Gameplay.Radio.Ids.Length; cfg.RadioStation = Gameplay.Radio.Ids[i]; Config.Save("World", "RadioStation", cfg.RadioStation); Gameplay.Radio.Retune(); },
                Description = () => "Tuned in every time you get in a car." }, "World", "radio");
            G(flag("Dynamic weather", "DynamicWeather", () => cfg.DynamicWeather, v => cfg.DynamicWeather = v, "World"), "World", "cloud-sun");
            G(flag("Always night", "AlwaysNight", () => cfg.AlwaysNight, v => cfg.AlwaysNight = v, "World"), "World", "star");
            G(flag("Start in your last garage", "StartInGarage", () => cfg.StartInGarage, v => cfg.StartInGarage = v, "Gameplay"), "Gameplay", "house");
            G(flag("Police", "Enabled", () => cfg.PoliceEnabled, v => cfg.PoliceEnabled = v, "Police"), "Gameplay", "siren");
            return m;
        }
        private readonly List<App> apps = new List<App>();

        public Func<Menu> MilestonesMenu, AbilityMenu;

        public PhoneManager(SaveManager saves, MenuStack stack, MidnightMenu menu, WorldCalendar calendar, RaceManager races, Func<Menu> rivalsMenu, Func<Menu> clubsMenu)
        {
            Instance = this;
            this.saves = saves; this.stack = stack; this.menu = menu; this.calendar = calendar; this.races = races; this.rivalsMenu = rivalsMenu; this.clubsMenu = clubsMenu;
            Func<OwnedVehicle> car = () => saves.Data.Vehicles.FirstOrDefault(v => v.Id == saves.Data.CurrentVehicleId);
            Func<OwnedVehicle, string> carName = r => r == null ? "No car" : (string.IsNullOrEmpty(r.Nickname) ? r.Model : r.Nickname).ToUpperInvariant();
            // ---------------- RACES
            var tRaces = new App { Name = "Races", Icon = "flag", Accent = Th.Red };
            tRaces.Tiles.Add(new Tile { Title = "Race events", Icon = "flag", W = 2, H = 2, Open = () => this.races.RacesMenu(),
                Desc = "Every event in the city, grouped by discipline: street, drift, off-road, drag and bikes. Pick one to set your GPS.",
                Head = () => this.races.Races.Count(r => this.races.Available(r)) + " EVENTS OPEN",
                Meta = () => this.races.Championships.Count() + " championships  ·  " + saves.Data.RaceHistory.Count(r => r.Position == 1) + " wins" });
            tRaces.Tiles.Add(T("Rivals", "swords", "The city's named racers. Beat one three times and their car is yours.", () => this.rivalsMenu(), () => saves.Data.Rivals.Count(r => r.Defeated) + " / " + RivalManager.Profiles.Count + " beaten"));
            tRaces.Tiles.Add(T("Daily challenges", "list-checks", "Three quick goals that reset every in-game day.", () => Progression.Instance != null ? Progression.Instance.DailyMenu() : null,
                () => Progression.Instance == null ? "" : Progression.Instance.Today.Count(c => Progression.Instance.Done(c)) + " / " + Progression.Instance.Today.Count + " done"));
            tRaces.Tiles.Add(T("Weekly playlist", "list-music", "Five goals for the week. Finish them all for a super wheelspin.", () => Progression.Instance != null ? Progression.Instance.WeeklyMenu() : null,
                () => Progression.Instance == null ? "" : Progression.Instance.WeekDone + " / 5"));
            tRaces.Tiles.Add(T("Rankings", "chart-no-axes-column", "The underground leaderboard, ranked by REP.", Rankings, () => "#" + MyRank()));
            tRaces.Tiles.Add(T("Route creator", "route", "Drive a route, drop checkpoints and race it against the clock or AI.", () => RouteCreator.Instance != null ? RouteCreator.Instance.MyRoutes() : null, () => "Build your own"));
            apps.Add(tRaces);
            // ---------------- GARAGE
            var garage = new App { Name = "Garage", Icon = "car", Accent = Th.Cyan };
            garage.Tiles.Add(new Tile { Title = "My vehicles", Icon = "car-front", W = 2, H = 2, Open = () => menu.MyVehicles(),
                Desc = "Spawn, swap, rename and sell your cars and bikes.", Thumb = () => car() != null ? car().Model : null,
                Head = () => carName(car()), Meta = () => saves.Data.Vehicles.Count + (saves.Data.Vehicles.Count == 1 ? " car" : " cars") + (car() != null ? "  ·  " + Career.Mastery.Label(car()) : "") });
            garage.Tiles.Add(T("Tuning setup", "sliders-horizontal", "Gearing, springs, dampers, anti-roll bars, diffs, aero and brake balance for the current car.",
                () => Vehicles.Setup.Menu(Vehicles.VehicleManager.Instance, stack, saves), () => Vehicles.VehicleManager.Instance != null && Vehicles.VehicleManager.Instance.CurrentRecord != null && Vehicles.Setup.Any(Vehicles.VehicleManager.Instance.CurrentRecord) ? "Custom" : "Stock"));
            garage.Tiles.Add(T("Dealerships", "store", "New and used cars. Stock rotates every few days.", () => menu.DealerList(),
                () => menu.Dealers == null ? "" : "New stock in " + menu.Dealers.Definitions.Min(x => menu.Dealers.DaysUntilRotation(x)) + " days", 2, 1));
            garage.Tiles.Add(T("Auction house", "gavel", "Three rare lots a day. Bid against the clock.", () => Gameplay.Auction.Instance != null ? Gameplay.Auction.Instance.Menu(Leave) : null, () => "3 lots today"));
            garage.Tiles.Add(T("Catalogue", "book-open", "Every car in Los Santos with its class, era and where to buy it.", () => menu.Catalogue(), () => "All cars"));
            garage.Tiles.Add(T("Properties", "warehouse", "Garages you own. Fast travel and where you start.", () => menu.GarageList(), () => saves.Data.Garages.Count + " owned"));
            garage.Tiles.Add(T("Collection", "gem", "Distinct cars owned, by make and era. Sets pay out.", () => Gameplay.Collection.Instance != null ? Gameplay.Collection.Instance.Menu() : null,
                () => Gameplay.Collection.Instance != null ? Gameplay.Collection.Instance.Unique + " distinct" : ""));
            garage.Tiles.Add(T("Photo mode", "camera", "Freeze the moment: free camera, filters, depth of field.", () => { Leave(); Gameplay.PhotoMode.Run(saves, Vehicles.VehicleManager.Instance); return null; },
                () => saves.Data.PhotosTaken > 0 ? saves.Data.PhotosTaken + " shots" : "New"));
            apps.Add(garage);
            // ---------------- CAREER
            var career = new App { Name = "Career", Icon = "trophy", Accent = Th.Amber, Badge = () => { int n = saves.Data.Wheelspins + saves.Data.SuperWheelspins; return n > 0 ? n.ToString() : null; } };
            career.Tiles.Add(new Tile { Title = "Story", Icon = "book-marked", W = 2, H = 2, Open = () => Career.Story.Instance != null ? Career.Story.Instance.Menu() : null,
                Desc = "From nobody to the Midnight Club. Each chapter unlocks a new series, new cars and new rivals.",
                Head = () => Career.Story.Instance != null && Career.Story.Instance.Current != null ? "CHAPTER " + (saves.Data.StoryChapter + 1) + "  " + Career.Story.Instance.Current.Title.ToUpperInvariant() : "STORY COMPLETE",
                Meta = () => Career.Story.Instance != null && Career.Story.Instance.NextGoalText != null ? Career.Story.Instance.NextGoalText : "" });
            career.Tiles.Add(T("Disciplines", "layers", "Street, drift, off-road, drag and bikes each level up separately.", () => Career.Disciplines.Instance != null ? Career.Disciplines.Instance.Menu() : null,
                () => "Street " + Career.Disciplines.Level(Racing.Discipline.Street) + "  ·  Off-road " + Career.Disciplines.Level(Racing.Discipline.Offroad)));
            career.Tiles.Add(T("Wheelspins", "disc-3", "Spin for cars, cash and clothes. Earned on every level up.", () => Gameplay.Wheelspin.Menu(saves, Economy.TransactionManager.Instance, Vehicles.VehicleRegistry.Instance, stack, Leave),
                () => (saves.Data.Wheelspins + saves.Data.SuperWheelspins) > 0 ? (saves.Data.Wheelspins + saves.Data.SuperWheelspins) + " to spin" : "None"));
            career.Tiles[career.Tiles.Count - 1].Badge = career.Badge;
            career.Tiles.Add(T("Progress", "trending-up", "Driver level, XP and what the next levels unlock.", () => Progression.Instance != null ? Progression.Instance.ProgressMenu() : null, () => Progression.Instance != null ? "Level " + Progression.Instance.Level : ""));
            career.Tiles.Add(T("Goals", "target", "Career milestones with big one-off rewards.", () => MilestonesMenu != null ? MilestonesMenu() : null, () => saves.Data.MilestonesDone.Count + " / 13"));
            career.Tiles.Add(T("Special ability", "zap", "Zone, Agro or Roar. Fill the meter with style, fire it with Caps Lock.", () => AbilityMenu != null ? AbilityMenu() : null,
                () => Gameplay.Abilities.Instance != null && Gameplay.Abilities.Instance.Selected != null ? Gameplay.Abilities.Instance.Selected : "Locked"));
            career.Tiles.Add(T("Club tier", "crown", "Your standing in the scene and what the next tier opens up.", () => menu.CareerMenu(), () => CareerManager.Instance != null ? CareerManager.Instance.TierName : ""));
            career.Tiles.Add(T("Records", "medal", "Your career in numbers.", Records, () => saves.Data.RaceHistory.Count(r => r.Position == 1) + " wins"));
            career.Tiles.Add(T("Bank", "landmark", "Income, spending and loans.", () => menu.Finances(), () => "$" + saves.Data.Cash.ToString("N0")));
            apps.Add(career);
            // ---------------- WORLD
            var world = new App { Name = "World", Icon = "compass", Accent = Th.Green, Badge = () => { int n = saves.Data.Messages.Count(m => !m.Read); return n > 0 ? n.ToString() : null; } };
            world.Tiles.Add(new Tile { Title = "Exploration", Icon = "map", W = 2, H = 2, Open = () => Progression.Instance != null ? Progression.Instance.ProgressMenu() : null,
                Desc = "Roads driven, places discovered, hidden events found.",
                Head = () => Progression.Instance != null ? Progression.Instance.RoadPercent.ToString("0.0") + "% OF ROADS DRIVEN" : "",
                Meta = () => Explore.Discovery.Instance != null ? Explore.Discovery.Instance.KnownCount + " / " + Explore.Discovery.Instance.TotalCount + " places found" : "" });
            world.Tiles.Add(T("Messages", "mail", "Invites, tips and challenges from the scene.", Inbox, () => saves.Data.Messages.Count(m => !m.Read) + " unread"));
            world.Tiles[world.Tiles.Count - 1].Badge = world.Badge;
            world.Tiles.Add(T("Turf war", "shield", "Win a district's events to take it. Owned turf pays every day.", () => Explore.Turf.Instance != null ? Explore.Turf.Instance.TurfMenu() : null,
                () => Explore.Turf.Instance != null ? Explore.Turf.Instance.Owned + " / " + Explore.Turf.Districts.Length + " districts" : ""));
            world.Tiles.Add(T("Crew", "users", "Hire a wingman who drafts, blocks and levels up with you.", () => Career.Crew.Menu(Economy.TransactionManager.Instance, stack), () => Career.Crew.Hired ? Career.Crew.Name + "  ·  lvl " + Career.Crew.Level : "Hire a wingman"));
            world.Tiles.Add(T("Midnight Tokens", "coins", "Hidden tokens all over the map.", () => Explore.Tokens.Instance != null ? Explore.Tokens.Instance.TokensMenu() : null, () => Explore.Tokens.Instance != null ? Explore.Tokens.Instance.Collected + " / " + Explore.Tokens.Total : ""));
            world.Tiles.Add(T("Car deliveries", "package", "Deliver cars across town: fast, but without a scratch.", () => Explore.Deliveries.Instance != null ? Explore.Deliveries.Instance.Menu() : null, () => "Jobs"));
            world.Tiles.Add(T("Street buzz", "newspaper", "Los Santos car culture news, written by your results.", NewsApp, () => saves.Data.News.Count + " stories"));
            world.Tiles.Add(T("Clubs", "martini", "Car clubs, their meets and their membership perks.", () => this.clubsMenu(), () => "Meets & perks"));
            world.Tiles.Add(T("Calendar", "calendar-days", "Day of the week and when dealer stock rotates.", CalendarApp, () => calendar.DayName));
            apps.Add(world);
            // ---------------- SETTINGS
            var set = new App { Name = "Settings", Icon = "settings", Accent = Th.Violet };
            set.Tiles.Add(T("Driving", "gauge", "Driving model (arcade, simcade, simulation) and assists.", () => SettingsMenu("Driving"), () => DrivingLabel(), 2, 1));
            set.Tiles.Add(T("Cameras", "eye", "Midnight Club cameras and speed FOV.", () => SettingsMenu("Camera"), () => Config.Current.CustomCameras ? "Custom" : "Game"));
            set.Tiles.Add(T("Display", "monitor", "HUD, speed effects and the welcome screen.", () => SettingsMenu("Display"), () => Config.Current.ShowHud ? "HUD on" : "HUD off"));
            set.Tiles.Add(T("World", "cloud-sun", "Traffic, pedestrians, weather, time and radio.", () => SettingsMenu("World"), () => Config.Current.AlwaysNight ? "Always night" : "Day & night"));
            set.Tiles.Add(T("Police", "siren", "Police chases and where you start.", () => SettingsMenu("Gameplay"), () => Config.Current.PoliceEnabled ? "Police on" : "Police off"));
            set.Tiles.Add(T("Edit driver", "user", "Change your driver's look.", () => { Leave(); menu.Modes.EditCharacter(); return null; }, () => ""));
            set.Tiles.Add(T("Save game", "save", "Progress also saves automatically.", () => { menu.Modes.SaveNow(); UI.Shard.Mid("SAVED", "", UI.Shard.Green, 1500); return null; }, () => ""));
            apps.Add(set);
            // ---------------- EXIT
            var exit = new App { Name = "Exit", Icon = "power", Accent = Th.Red };
            exit.Tiles.Add(T("Story mode", "log-out", "Back to Franklin, Michael and Trevor. Midnight Club keeps your progress; F7 brings you back.",
                () => { Leave(); menu.Modes.Exit(); return null; }, () => "Progress is saved", 2, 2));
            exit.Tiles.Add(T("Save game", "save", "Saves now.", () => { menu.Modes.SaveNow(); UI.Shard.Mid("SAVED", "", UI.Shard.Green, 1500); return null; }, () => ""));
            exit.Tiles.Add(T("Quit game", "power", "Saves and closes GTA V.", ExitMenu, () => "Desktop"));
            apps.Add(exit);
            foreach (var t in apps) Pack(t);
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
        private int tab; private readonly int[] tabSel = new int[8]; private float tabT = 1f, scrollX, tabBarK = -1f;
        private Tile currentTile;

        /// <summary>Spatial navigation in the mosaic: the nearest tile whose centre lies in the pressed direction.</summary>
        private static int Neighbour(App app, int from, int dx, int dy)
        {
            var a = app.Tiles[from]; float ax = a.Col + a.W / 2f, ay = a.Row + a.H / 2f;
            int best = from; float bd = float.MaxValue;
            for (int i = 0; i < app.Tiles.Count; i++)
            {
                if (i == from) continue;
                var t = app.Tiles[i]; float tx = t.Col + t.W / 2f, ty = t.Row + t.H / 2f, ddx = tx - ax, ddy = ty - ay;
                if (dx != 0 && Math.Sign(ddx) != dx) continue;
                if (dy != 0 && (Math.Sign(ddy) != dy || Math.Abs(ddx) > Math.Max(a.W, t.W) / 2f + 0.01f)) continue;
                if (dx != 0 && Math.Abs(ddx) < 0.01f) continue;
                float d = dx != 0 ? Math.Abs(ddx) + Math.Abs(ddy) * 2.5f : Math.Abs(ddy) + Math.Abs(ddx) * 2.5f;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

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
            IsOpen = true; tab = apps.Count - 1; current = apps[tab]; tabSel[tab] = 0; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
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
                var app = apps[tab]; int old = tabSel[tab], oldTab = tab;
                int td = UI.Input.TabDelta();
                if (td != 0) tab = (tab + apps.Count + td) % apps.Count;
                if (tab != oldTab) { tabT = 0f; current = apps[tab]; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true); }
                else
                {
                    int dx = UI.Input.Repeat(Control.FrontendRight) ? 1 : UI.Input.Repeat(Control.FrontendLeft) ? -1 : 0;
                    int dy = UI.Input.Repeat(Control.FrontendDown) ? 1 : UI.Input.Repeat(Control.FrontendUp) ? -1 : 0;
                    if (dx != 0 || dy != 0) tabSel[tab] = Neighbour(app, tabSel[tab], dx, dy);
                    if (tabSel[tab] != old) Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
                }
                if (UI.Input.JustPressed(Control.FrontendCancel)) Close();
                else if (UI.Input.JustPressed(Control.FrontendAccept))
                {
                    var t = apps[tab].Tiles[Math.Min(tabSel[tab], apps[tab].Tiles.Count - 1)];
                    current = apps[tab]; currentTile = t;
                    Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
                    var m = t.Open != null ? t.Open() : null;
                    if (m != null && IsOpen) { stack.Open(m); stack.PhoneStyle = true; lastMenu = null; }
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
            // the world is blurred behind (MenuMGIn); a theme scrim + a soft floor keep text readable
            Ds.Rect(0f, 0f, 1f, 1f, Ds.A(Th.Scrim, a));
            Ds.VGrad(0f, 0.7f, 1f, 0.3f, Ds.A(Ds.Bg, 0f), Ds.A(Ds.Bg, 0.55f * a), 5);
            float ty = 0.048f - (1f - a) * 0.02f;
            Ds.Logo(X0, ty, 0.8f, a, GTA.UI.Alignment.Left, false);
            // profile capsule (top right): level ring · cash · REP / tier
            var d = saves.Data;
            string cash = "$" + d.Cash.ToString("N0");
            string sub = d.Rep.ToString("N0") + " REP  ·  " + (CareerManager.Instance != null ? CareerManager.Instance.TierName.ToUpperInvariant() : "");
            float h = Th.Px(64f), ringD = Th.Px(50f);
            float tw = Math.Max(Ds.Width(cash, 0.62f, Ds.Display), Ds.Width(sub, 0.24f, Ds.Display));
            float w = Ds.Px(Th.Px(20f)) + Ds.Px(ringD) + Ds.Px(Th.Px(14f)) + tw + Ds.Px(Th.Px(24f));
            float x = XR - w, y = ty - Th.Px(6f);
            Kit.Glass(x, y, w, h, h * 1080f / 2f, a);
            float rcx = x + Ds.Px(Th.Px(14f)) + Ds.Px(ringD / 2f), rcy = y + h / 2f;
            if (Progression.Instance != null)
            {
                Textures.Draw("ring_100", rcx, rcy, ringD, Ds.A(Th.Track, a));
                VectorIcons.Ring(rcx, rcy, ringD, 0.006f, Progression.Instance.LevelProgress, Ds.A(Th.Accent, a));
                Ds.TM(Progression.Instance.Level.ToString(), rcx, rcy, 0.5f, Ds.A(Th.Text, a), Ds.Display, GTA.UI.Alignment.Center);
            }
            float tx = rcx + Ds.Px(ringD / 2f) + Ds.Px(Th.Px(14f));
            Ds.TM(cash, tx, rcy - Th.Px(10f), 0.62f, Ds.A(Th.Text, a), Ds.Display);
            Ds.TM(sub, tx, rcy + Th.Px(16f), 0.24f, Ds.A(Th.Sub, a), Ds.Display);
            Ds.TM(Gameplay.Season.Label, x - Ds.Px(Th.Px(16f)), rcy, 0.26f, Ds.A(Th.Amber, a), Ds.Display, GTA.UI.Alignment.Right);
            return a;
        }

        // ---------------------------------------------------------------- hub: tab bar + tile mosaic (Forza Horizon style)
        private const float UH = 0.21f, GAP = 0.014f, MY = 0.24f;
        private static float UW { get { return Ds.Px(UH * 1.2f); } }
        private static float TGX { get { return Ds.Px(GAP); } }

        private static void Icon(string name, float cx, float cy, float size, Color c)
        {
            if (!Textures.Draw("li_" + name, cx, cy, size, c)) VectorIcons.Draw(name, cx, cy, size, c);
        }

        private void TabBar(float a, bool interactive)
        {
            // segmented glass control: Q [tabs] E, and EXIT as a separate red pill on the right
            float y = 0.135f, h = Th.Px(48f), cy = y + h / 2f;
            int n = apps.Count - 1;
            float padX = Ds.Px(Th.Px(18f)), gap = Ds.Px(Th.Px(6f)), iw = Ds.Px(Th.Px(26f));
            var widths = new float[n]; float total = 0f;
            for (int i = 0; i < n; i++) { widths[i] = padX * 2 + iw + Ds.Width(apps[i].Name.ToUpperInvariant(), 0.42f, Ds.Display); total += widths[i] + gap; }
            float kq = Ds.Px(Th.Px(34f));
            float bw = total - gap + Ds.Px(Th.Px(12f)) + (interactive ? 2 * (kq + Ds.Px(Th.Px(10f))) : 0f);
            float x = X0;
            Kit.Glass(x, y, bw, h, h * 1080f / 2f, a);
            float cx = x + Ds.Px(Th.Px(6f));
            if (interactive) { Kit.KeyHint(Control.FrontendLb, "", cx + Ds.Px(Th.Px(6f)), cy); cx += kq + Ds.Px(Th.Px(10f)); }
            float selX = 0f, selW = 0f;
            for (int i = 0; i < n; i++) { if (i == tab) { selX = cx; selW = widths[i]; } cx += widths[i] + gap; }
            if (tabBarK < 0f) tabBarK = selX;
            tabBarK = Ds.Damp(tabBarK, selX, 22f);
            if (tab < n) Kit.Pill(tabBarK, y + Th.Px(5f), selW, h - Th.Px(10f), Ds.A(apps[tab].Accent, a));
            cx = x + Ds.Px(Th.Px(6f)) + (interactive ? kq + Ds.Px(Th.Px(10f)) : 0f);
            for (int i = 0; i < n; i++)
            {
                var t = apps[i]; bool on = i == tab && Math.Abs(tabBarK - cx) < Ds.Px(Th.Px(20f));
                var c = on ? Th.On(t.Accent) : Th.Sub;
                Icon(t.Icon, cx + padX + iw * 0.45f, cy, Th.Px(22f), Ds.A(on ? c : t.Accent, a));
                Ds.TM(t.Name.ToUpperInvariant(), cx + padX + iw, cy, 0.42f, Ds.A(on ? c : Th.Text, a), Ds.Display);
                string b = t.Badge != null ? t.Badge() : null;
                if (b != null) Kit.Badge(b, cx + widths[i] - Ds.Px(Th.Px(6f)), y + Th.Px(6f), Th.Red);
                cx += widths[i] + gap;
            }
            if (interactive) Kit.KeyHint(Control.FrontendRb, "", cx + Ds.Px(Th.Px(2f)), cy);
            // EXIT pill
            var ex = apps[n]; bool exOn = tab == n;
            float ew = Ds.Px(Th.Px(28f)) + Ds.Px(Th.Px(26f)) + Ds.Width("EXIT", 0.42f, Ds.Display), exx = XR - ew;
            if (exOn) Kit.Pill(exx, y, ew, h, Ds.A(Th.Red, a)); else Kit.Glass(exx, y, ew, h, h * 1080f / 2f, a);
            Icon("power", exx + Ds.Px(Th.Px(26f)), cy, Th.Px(22f), Ds.A(exOn ? Color.White : Th.Red, a));
            Ds.TM("EXIT", exx + Ds.Px(Th.Px(44f)), cy, 0.42f, Ds.A(exOn ? Color.White : Th.Text, a), Ds.Display);
        }

        /// <summary>Rounded icon badge: accent square with a white icon (the colourful bit of every tile and row).</summary>
        private static void IconBadge(string icon, float cx, float cy, float size, Color accent, float a)
        {
            Kit.Round(cx - Ds.Px(size / 2f), cy - size / 2f, Ds.Px(size), size, size * 1080f * 0.28f, Ds.A(accent, a));
            Icon(icon, cx, cy, size * 0.58f, Ds.A(Th.On(accent), a));
        }

        private void DrawHub()
        {
            float a = Backdrop();
            TabBar(a, true);
            tabT = Ds.Damp(tabT, 1f, 12f);
            var app = apps[tab];
            int selIdx = Math.Min(tabSel[tab], app.Tiles.Count - 1);
            float ent = Ds.EaseOut(tabT), ea0 = a * Ds.Clamp01(tabT * 1.8f);
            var st = app.Tiles[selIdx];
            float sx0 = st.Col * (UW + TGX), sx1 = sx0 + st.W * UW + (st.W - 1) * TGX, view = XR - X0;
            float want = scrollX;
            if (sx1 - want > view - UW * 0.35f) want = sx1 - view + UW * 0.35f;
            if (sx0 - want < 0f) want = sx0;
            if (st.Col == 0) want = 0f;
            scrollX = tabT < 0.05f ? want : Ds.Damp(scrollX, want, 14f);
            float R = Th.R_Lg + 2f;
            for (int i = 0; i < app.Tiles.Count; i++)
            {
                var t = app.Tiles[i]; bool on = i == selIdx;
                t.K = Ds.Damp(t.K, on ? 1f : 0f, 18f);
                float k = t.K;
                float x = X0 + t.Col * (UW + TGX) - scrollX + (1f - ent) * Ds.Px(0.03f) * (1 + t.Col * 0.25f);
                float y = MY + t.Row * (UH + GAP);
                float w = t.W * UW + (t.W - 1) * TGX, h = t.H * UH + (t.H - 1) * GAP;
                if (x > 1.02f || x + w < -0.02f) continue;
                float edge = Math.Min(Ds.Clamp01(1f - (X0 - x) / UW), Ds.Clamp01(1f - (x + w - XR) / (UW * 1.2f)));
                float ea = ea0 * (0.3f + 0.7f * edge);
                float g = Th.Px(5f) * k; x -= Ds.Px(g); y -= g; w += Ds.Px(2 * g); h += 2 * g;
                bool big = t.W == 2 && t.H == 2;
                Kit.Glass(x, y, w, h, R, ea, k > 0.5f);
                if (k > 0.02f) Kit.Focus(x, y, w, h, R, app.Accent, k * ea);
                string thumb = t.Thumb != null ? t.Thumb() : null;
                bool pic = false;
                if (thumb != null)
                {
                    float iw = w * 0.9f, ih = Math.Min(h * 0.6f, iw * Ds.Aspect * 0.5f);
                    pic = CarThumbs.Draw(thumb, x + (w - iw) / 2f, y + Th.Px(16f) + (h * 0.6f - ih) / 2f, iw, ih, Ds.A(Color.White, ea));
                }
                if (!pic && big) Icon(t.Icon, x + w * 0.68f, y + h * 0.4f, h * 0.6f, Ds.A(app.Accent, (0.08f + 0.06f * k) * ea));
                float bs = big ? Th.Px(56f) : Th.Px(44f);
                if (!pic) IconBadge(t.Icon, x + Ds.Px(Th.Px(20f)) + Ds.Px(bs / 2f), y + Th.Px(20f) + bs / 2f, bs, app.Accent, ea);
                float tx = x + Ds.Px(Th.Px(20f)), tw = w - Ds.Px(Th.Px(40f));
                string meta = t.Meta != null ? t.Meta() : "";
                if (big)
                {
                    string head = t.Head != null ? t.Head() : null;
                    if (!string.IsNullOrEmpty(head)) Kit.Chip(Ds.Fit(head, 0.26f, tw - Ds.Px(Th.Px(20f)), Ds.Display), tx, y + h - Th.Px(112f), Ds.A(app.Accent, ea), Ds.A(Th.On(app.Accent), ea));
                    Ds.TM(Ds.Fit(t.Title.ToUpperInvariant(), 0.95f, tw, Ds.Display), tx, y + h - Th.Px(66f), 0.95f, Ds.A(Th.Text, ea), Ds.Display);
                    Ds.TM(Ds.Fit(Clean(meta), 0.3f, tw), tx, y + h - Th.Px(26f), 0.3f, Ds.A(Th.Sub, ea));
                }
                else
                {
                    Ds.TM(Ds.Fit(t.Title.ToUpperInvariant(), 0.52f, tw, Ds.Display), tx, y + h - Th.Px(58f), 0.52f, Ds.A(Th.Text, ea), Ds.Display);
                    Ds.TM(Ds.Fit(Clean(meta), 0.27f, tw), tx, y + h - Th.Px(26f), 0.27f, Ds.A(Th.Sub, ea));
                }
                string b = t.Badge != null ? t.Badge() : null;
                if (b != null) Kit.Badge(b, x + w - Ds.Px(Th.Px(24f)), y + Th.Px(24f), Th.Red);
            }
            // scroll indicator
            int cols = app.Tiles.Max(t => t.Col + t.W);
            float total = cols * UW + (cols - 1) * TGX;
            float iy = MY + 2 * UH + GAP + Th.Px(22f);
            if (total > XR - X0 + 0.001f)
            {
                float trk = Ds.Px(Th.Px(120f)), tx0 = (X0 + XR) / 2f - trk / 2f;
                Kit.Bar(tx0, iy - Th.Px(8f), trk, Th.Px(5f), 1f, Th.Track);
                float k0 = scrollX / total, k1 = Math.Min(1f, (scrollX + XR - X0) / total);
                Kit.Pill(tx0 + trk * k0, iy - Th.Px(8f), trk * (k1 - k0), Th.Px(5f), Ds.A(app.Accent, a));
            }
            // info card: the selected tile explained
            float ih2 = Th.Px(92f), cw = 0.55f;
            Kit.Glass(X0, iy, cw, ih2, Th.R_Lg, a);
            IconBadge(st.Icon, X0 + Ds.Px(Th.Px(22f)) + Ds.Px(Th.Px(22f)), iy + ih2 / 2f, Th.Px(44f), app.Accent, a);
            float itx = X0 + Ds.Px(Th.Px(84f));
            Ds.TM(st.Title.ToUpperInvariant(), itx, iy + Th.Px(30f), 0.5f, Ds.A(Th.Text, a), Ds.Display);
            Ds.T(st.Desc ?? "", itx, iy + Th.Px(48f), 0.3f, Ds.A(Th.Sub, a), Ds.Body, GTA.UI.Alignment.Left, cw - Ds.Px(Th.Px(100f)));
            // next objective card
            string head2 = null, line = null; Color hc = Th.Violet;
            if (Career.Story.Instance != null && Career.Story.Instance.NextGoalText != null) { head2 = "STORY  ·  CHAPTER " + (saves.Data.StoryChapter + 1); line = Career.Story.Instance.NextGoalText; hc = Th.Amber; }
            else if (Gameplay.Objectives.CurrentText != null) { head2 = "NEXT OBJECTIVE"; line = Gameplay.Objectives.CurrentText; }
            else if (Progression.Instance != null && Progression.Instance.Today.Any(c => !Progression.Instance.Done(c))) { var dc = Progression.Instance.Today.First(c => !Progression.Instance.Done(c)); head2 = "DAILY CHALLENGE"; line = dc.Text; hc = Th.Green; }
            if (line != null)
            {
                float ox = X0 + cw + Ds.Px(Th.Px(16f)), ow = XR - ox;
                Kit.Glass(ox, iy, ow, ih2, Th.R_Lg, a);
                IconBadge("target", ox + Ds.Px(Th.Px(44f)), iy + ih2 / 2f, Th.Px(44f), hc, a);
                float ox2 = ox + Ds.Px(Th.Px(84f));
                Kit.Chip(head2, ox2, iy + Th.Px(28f), Ds.A(hc, 0.2f * a), Ds.A(hc, a), 0.22f);
                Ds.T(Clean(line), ox2, iy + Th.Px(46f), 0.3f, Ds.A(Th.Text, a), Ds.Body, GTA.UI.Alignment.Left, ow - Ds.Px(Th.Px(100f)));
            }
            if (IsOpen) Buttons.Draw(Control.FrontendLb, "", Control.FrontendRb, "Tabs", Control.FrontendAccept, "Open", Control.FrontendCancel, "Close");
        }

        // ---------------------------------------------------------------- sub-screens (any Menu): list + detail panel
        private void DrawApp(Menu m)
        {
            openT = Ds.Step(openT, 1f, 5f);
            if (m != lastMenu) { lastMenu = m; screenT = 0f; listSel = -1f; }
            screenT = Ds.Damp(screenT, 1f, 14f);
            float a = Backdrop() * Ds.Clamp01(screenT * 1.6f), off = (1f - Ds.EaseOut(screenT)) * Ds.Px(0.025f);
            TabBar(a, false);
            m.VisibleRows = 8; m.Clamp();
            var acc = current != null ? current.Accent : Th.Accent;
            string icon = currentTile != null ? currentTile.Icon : current != null ? current.Icon : "settings";
            float x = X0 + off, w = 0.4f, y = 0.215f;
            // header: icon badge, breadcrumb, title, counter
            IconBadge(icon, x + Ds.Px(Th.Px(28f)), y + Th.Px(34f), Th.Px(56f), acc, a);
            float hx = x + Ds.Px(Th.Px(74f));
            string crumb = (current != null ? current.Name.ToUpperInvariant() : "") + (currentTile != null && !string.Equals(currentTile.Title, m.Title, StringComparison.OrdinalIgnoreCase) ? "  /  " + currentTile.Title.ToUpperInvariant() : "");
            Ds.TM(crumb, hx, y + Th.Px(14f), 0.26f, Ds.A(acc, a), Ds.Display);
            Ds.TM(Ds.Fit((m.Title ?? "").ToUpperInvariant(), 0.85f, w - Ds.Px(Th.Px(80f)), Ds.Display), hx, y + Th.Px(46f), 0.85f, Ds.A(Th.Text, a), Ds.Display);
            if (m.Items.Count > 0) Kit.Chip((m.Selected + 1) + " / " + m.Items.Count, x + w - Ds.Px(Th.Px(70f)), y + Th.Px(46f), Ds.A(Th.Track, a), Ds.A(Th.Sub, a), 0.24f);
            // list card
            float ly = y + Th.Px(96f), rh = Th.Px(54f), pad = Th.Px(10f);
            int first = m.Scroll, n = Math.Min(m.VisibleRows, m.Items.Count - first);
            float ch = Math.Max(1, n) * rh + 2 * pad;
            Kit.Glass(x, ly, w, ch, Th.R_Lg, a);
            if (m.Items.Count == 0) Ds.TM("NOTHING HERE YET", x + Ds.Px(Th.Px(24f)), ly + ch / 2f, 0.4f, Ds.A(Th.Faint, a), Ds.Display);
            float rx = x + Ds.Px(pad), rw = w - 2 * Ds.Px(pad);
            float target = ly + pad + (m.Selected - first) * rh;
            listSel = listSel < 0 ? target : Ds.Damp(listSel, target, 34f);
            if (m.Items.Count > 0) Kit.Round(rx, listSel, rw, rh - Th.Px(4f), Th.R_Md, Ds.A(acc, a));
            for (int i = 0; i < n; i++)
            {
                var it = m.Items[first + i]; bool sel = first + i == m.Selected; float yy = ly + pad + i * rh, cy = yy + (rh - Th.Px(4f)) / 2f;
                bool onBar = sel && Math.Abs(listSel - target) < rh * 0.5f;
                var fg = onBar ? Th.On(acc) : !it.Enabled ? Th.Faint : Th.Text;
                var fg2 = onBar ? Ds.A(Th.On(acc), 0.85f) : Th.Sub;
                string v = it.Value != null ? Clean(it.Value()) : null;
                float lx = rx + Ds.Px(Th.Px(16f));
                if (it.Icon != null) { Icon(it.Icon, lx + Ds.Px(Th.Px(11f)), cy, Th.Px(22f), Ds.A(onBar ? fg : acc, a)); lx += Ds.Px(Th.Px(36f)); }
                else if (!it.Enabled) { Icon("lock", lx + Ds.Px(Th.Px(9f)), cy, Th.Px(18f), Ds.A(Th.Faint, a)); lx += Ds.Px(Th.Px(30f)); }
                bool isToggle = v == "On" || v == "Off";
                float vw = string.IsNullOrEmpty(v) ? 0 : isToggle ? Ds.Px(Th.Px(44f)) : Math.Min(rw * 0.42f, Ds.Width(v, 0.32f, Ds.Display) + (sel && it.OnAdjust != null ? Ds.Px(Th.Px(44f)) : 0));
                Ds.TM(Ds.Fit(Clean(it.Label), 0.36f, rx + rw - lx - vw - Ds.Px(Th.Px(28f))), lx, cy, 0.36f, Ds.A(fg, a));
                float vx = rx + rw - Ds.Px(Th.Px(16f));
                if (isToggle) Kit.Toggle(vx - Ds.Px(Th.Px(40f)), cy, v == "On", onBar ? Color.FromArgb(255, 255, 255, 255) : acc);
                else if (!string.IsNullOrEmpty(v))
                {
                    if (sel && it.OnAdjust != null && it.Enabled)
                    {
                        float vwid = Math.Min(rw * 0.36f, Ds.Width(v, 0.32f, Ds.Display));
                        Icon("chevron-right", vx - Ds.Px(Th.Px(6f)), cy, Th.Px(20f), Ds.A(fg, a));
                        Icon("chevron-left", vx - Ds.Px(Th.Px(30f)) - vwid, cy, Th.Px(20f), Ds.A(fg, a));
                        Ds.TM(Ds.Fit(v, 0.32f, rw * 0.36f, Ds.Display), vx - Ds.Px(Th.Px(20f)), cy, 0.32f, Ds.A(fg, a), Ds.Display, GTA.UI.Alignment.Right);
                    }
                    else Ds.TM(Ds.Fit(v, 0.32f, rw * 0.42f, Ds.Display), vx, cy, 0.32f, Ds.A(fg2, a), Ds.Display, GTA.UI.Alignment.Right);
                }
            }
            if (m.Items.Count > m.VisibleRows)
            {
                float th = rh * m.VisibleRows, bh = th * m.VisibleRows / m.Items.Count, by = ly + pad + th * first / m.Items.Count;
                Kit.Pill(x + w + Ds.Px(Th.Px(8f)), ly + pad, Ds.Px(Th.Px(4f)), th, Ds.A(Th.Track, a));
                Kit.Pill(x + w + Ds.Px(Th.Px(8f)), by, Ds.Px(Th.Px(4f)), bh, Ds.A(acc, a));
            }
            // detail card
            var cur = m.Items.Count > 0 ? m.Items[m.Selected] : null;
            float px = x + w + Ds.Px(Th.Px(32f)), pw = XR - px, py = ly, ph = Math.Max(ch, Th.Px(330f));
            Kit.Glass(px, py, pw, ph, Th.R_Lg, a);
            Icon(icon, px + pw * 0.78f, py + ph * 0.58f, ph * 0.62f, Ds.A(acc, 0.07f * a));
            if (cur != null)
            {
                float dx = px + Ds.Px(Th.Px(28f)), dw = pw - Ds.Px(Th.Px(56f));
                IconBadge(cur.Icon ?? icon, dx + Ds.Px(Th.Px(32f)), py + Th.Px(58f), Th.Px(64f), cur.Enabled ? acc : Th.Faint, a);
                Ds.TM(Ds.Fit(Clean(cur.Label).ToUpperInvariant(), 0.78f, dw, Ds.Display), dx, py + Th.Px(130f), 0.78f, Ds.A(Th.Text, a), Ds.Display);
                string v = cur.Value != null ? Clean(cur.Value()) : null;
                float dy = py + Th.Px(168f);
                if (!string.IsNullOrEmpty(v)) { Kit.Chip(Ds.Fit(v, 0.32f, dw * 0.8f, Ds.Display), dx, dy, Ds.A(acc, 0.18f * a), Ds.A(Th.Light ? Th.Text : acc, a), 0.32f); dy += Th.Px(34f); }
                string desc = cur.Description != null ? cur.Description() : null;
                if (!string.IsNullOrEmpty(desc)) Ds.T(Clean(desc), dx, dy, 0.32f, Ds.A(Th.Sub, a), Ds.Body, GTA.UI.Alignment.Left, dw);
                if (!cur.Enabled) Kit.Chip("NOT AVAILABLE YET", dx, py + ph - Th.Px(34f), Ds.A(Th.Track, a), Ds.A(Th.Sub, a), 0.24f);
            }
            if (cur != null && cur.Enabled && cur.OnAdjust != null) Buttons.Draw(Control.FrontendLeft, "", Control.FrontendRight, "Change", Control.FrontendAccept, "Select", Control.FrontendCancel, "Back");
            else Buttons.Draw(Control.FrontendUp, "", Control.FrontendDown, "Navigate", Control.FrontendAccept, "Select", Control.FrontendCancel, "Back");
        }

        /// <summary>Strips GTA colour tokens (~p~ etc.) that would clash with the phone palette.</summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('~') < 0) return s;
            return System.Text.RegularExpressions.Regex.Replace(s, "~[a-zA-Z]~", "");
        }
    }
}
