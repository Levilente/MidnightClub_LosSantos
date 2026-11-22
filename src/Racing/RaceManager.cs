using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.UI;
using MidnightClub.Vehicles;
using MidnightClub.WorldSim;
using Font = GTA.UI.Font;

namespace MidnightClub.Racing
{
    public sealed class RaceFinishedEvent
    {
        public RaceDefinition Race; public RaceResult Result; public string RivalId; public bool Won;
        public RaceFinishedEvent(RaceDefinition r, RaceResult res, string rival, bool won) { Race = r; Result = res; RivalId = rival; Won = won; }
    }

    /// <summary>Opponent override for rival challenges.</summary>
    public sealed class RivalSpec { public string Id, Name; public VehicleInfo Car; public float Skill = 1.05f; public float DriftTarget; }

    /// <summary>Race starts on the map, race menus, the race lifecycle, rewards, history and championships.</summary>
    public sealed class RaceManager
    {
        public static bool IsRacing { get; private set; }

        private readonly SaveManager saves;
        private readonly TransactionManager money;
        private readonly VehicleRegistry registry;
        private readonly VehicleManager vehicles;
        private readonly WorldCalendar calendar;
        private readonly MenuStack stack;
        private readonly string dataDir;
        private RaceLibraryFile lib = new RaceLibraryFile();
        private readonly Dictionary<string, Blip> blips = new Dictionary<string, Blip>();
        private RaceSession session;
        private string championshipId;
        private RivalSpec rival;
        private RaceDefinition nearby;

        public static readonly string[] DriverNames =
        {
            "Kenji Mori", "Dee 'Blackout' Ramos", "Vance Holloway", "Lexi Cruz", "Marco 'Torque' Bellini", "Tasha Okafor", "Ruben Ibarra", "Nikki Sato",
            "Grayson Pike", "Ivy Chen", "Andre 'Static' Duval", "Sofia Marquez", "Dmitri Volkov", "Jules Baptiste", "Rico Santana", "Harper Quinn"
        };

        public RaceManager(SaveManager saves, TransactionManager money, VehicleRegistry registry, VehicleManager vehicles, WorldCalendar calendar, MenuStack stack, string dataDir)
        {
            this.saves = saves; this.money = money; this.registry = registry; this.vehicles = vehicles; this.calendar = calendar; this.stack = stack; this.dataDir = dataDir;
            EventBus.Subscribe<ModeChangedEvent>(e =>
            {
                if (e.To == GameMode.MIDNIGHT_CLUB) { try { lib = RaceLibrary.Load(this.dataDir); } catch (Exception ex) { Log.Error("Race library", ex); } RefreshBlips(); }
                else { EndSession(true); ClearBlips(); }
            });
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
        }

        public IEnumerable<RaceDefinition> Races { get { return lib.Races; } }
        public IEnumerable<ChampionshipDefinition> Championships { get { return lib.Championships; } }
        public RaceDefinition Race(string id) { return lib.Races.FirstOrDefault(r => r.Id == id); }

        public bool Available(RaceDefinition r) { return saves.Data.Rep >= r.RequiredRep && (!r.NightOnly || calendar.IsNight); }

        public long BestTime(string id)
        {
            var kv = saves.Data.BestTimes.FirstOrDefault(x => x.Key == id);
            long v; return kv != null && long.TryParse(kv.Value, out v) ? v : 0;
        }

        // ------------------------------------------------------------------ map presence
        private int lastBlipRefresh;
        private void RefreshBlips()
        {
            ClearBlips();
            foreach (var r in lib.Races)
            {
                if (r.Points.Count == 0) continue;
                var b = World.CreateBlip(r.Start);
                bool open = Available(r);
                b.Sprite = BlipSprite.RaceCar; b.IsShortRange = true; b.Scale = 0.8f;
                b.Color = !open ? BlipColor.White : r.Kind == RaceType.Drift ? BlipColor.Yellow2 : r.Kind == RaceType.TimeTrial ? BlipColor.Blue : BlipColor.Red;
                b.Name = open ? "Race: " + r.Name : "Race (locked)";
                blips[r.Id] = b;
            }
            lastBlipRefresh = Game.GameTime;
        }

        private void ClearBlips() { foreach (var b in blips.Values) try { if (b.Exists()) b.Delete(); } catch { } blips.Clear(); }

        private void Tick()
        {
            if (session != null) { TickSession(); return; }
            if (Game.GameTime - lastBlipRefresh > 60000) RefreshBlips();   // night-only / REP changes
            var pos = Game.Player.Character.Position;
            nearby = null;
            foreach (var r in lib.Races)
            {
                if (r.Points.Count < 2) continue;
                float d = pos.DistanceTo(r.Start);
                if (d > 70f) continue;
                World.DrawMarker(MarkerType.RingFlag, r.Start + new Vector3(0, 0, 1.6f), Vector3.Zero, Vector3.Zero, new Vector3(2.5f, 2.5f, 2.5f),
                    Available(r) ? Color.FromArgb(200, 170, 90, 255) : Color.FromArgb(120, 200, 200, 200), true, true, false, null, null, false);
                if (d < 7f) nearby = r;
            }
            if (nearby == null || stack.IsOpen) return;
            var car = vehicles.Current;
            if (car == null || !car.Exists() || car.Speed > 3f) return;
            Screen.ShowHelpTextThisFrame("~INPUT_CONTEXT~ " + nearby.Name);
            if (Game.IsControlJustPressed(Control.Context)) stack.Open(RaceMenu(nearby, null));
        }

        // ------------------------------------------------------------------ menus
        public Menu RaceMenu(RaceDefinition r, string champ)
        {
            var m = new Menu(r.Kind.ToString().ToUpperInvariant(), r.Name);
            bool open = Available(r);
            m.Add(new MenuItem("Type") { Value = () => TypeLabel(r) });
            if (r.HasOpponents) m.Add(new MenuItem("Opponents") { Value = () => r.Opponents.ToString() });
            if (r.Kind == RaceType.Circuit) m.Add(new MenuItem("Laps") { Value = () => r.Laps.ToString() });
            m.Add(new MenuItem("Entry fee") { Value = () => r.EntryFee == 0 ? "Free" : "$" + r.EntryFee.ToString("N0") });
            m.Add(new MenuItem("Prize (1st)") { Value = () => "$" + r.CashReward.ToString("N0") + "  " + r.RepReward + " REP" });
            long best = BestTime(r.Id);
            if (best > 0) m.Add(new MenuItem("Your best") { Value = () => RaceSession.FormatTime((int)best) });
            if (r.RequiredRep > saves.Data.Rep) m.Add(new MenuItem("Locked") { Value = () => "Needs " + r.RequiredRep.ToString("N0") + " REP", Enabled = false });
            else if (r.NightOnly && !calendar.IsNight) m.Add(new MenuItem("Night race") { Value = () => "21:00 - 05:00", Enabled = false });
            m.Add(new MenuItem(champ != null ? "~p~Start championship round" : "~p~Start race", () => { stack.Close(); StartRace(r, champ); }) { Enabled = open || champ != null, Description = () => "Hold BACKSPACE during the race to quit (entry fee is not refunded)." });
            return m;
        }

        private static string TypeLabel(RaceDefinition r)
        {
            switch (r.Kind) { case RaceType.Circuit: return "Circuit"; case RaceType.TimeTrial: return "Time trial"; case RaceType.Drift: return "Drift challenge"; case RaceType.HeadToHead: return "Head-to-head"; default: return "Sprint"; }
        }

        public Menu RacesMenu()
        {
            var m = new Menu("Races", "REP " + saves.Data.Rep.ToString("N0") + (calendar.IsNight ? "  |  night" : ""));
            m.OnOpen = () =>
            {
                m.Items.Clear();
                m.Add(new MenuItem("Championships", () => stack.Push(ChampionshipsMenu())));
                m.Add(new MenuItem("Race history", () => stack.Push(HistoryMenu())) { Value = () => saves.Data.RaceHistory.Count.ToString() });
                foreach (var race in lib.Races)
                {
                    var r = race;
                    m.Add(new MenuItem(r.Name, () => { Function.Call(Hash.SET_NEW_WAYPOINT, r.Start.X, r.Start.Y); Notification.PostTicker("GPS set to " + r.Name + ".", true, false); })
                    {
                        Value = () => Available(r) ? TypeLabel(r) : (r.RequiredRep > saves.Data.Rep ? r.RequiredRep.ToString("N0") + " REP" : "Night"),
                        Description = () => TypeLabel(r) + ", $" + r.CashReward.ToString("N0") + " / " + r.RepReward + " REP for the win. Select to set GPS."
                    });
                }
            };
            return m;
        }

        private Menu HistoryMenu()
        {
            var m = new Menu("Race history", "Last 20");
            foreach (var h in Enumerable.Reverse(saves.Data.RaceHistory).Take(20))
            {
                var e = h;
                m.Add(new MenuItem(e.Name) { Value = () => (e.Position == 0 ? "DNF" : e.Type == "Drift" || e.Type == "TimeTrial" ? (e.Medal ?? "-") : e.Position + "/" + e.Of) + "  " + RaceSession.FormatTime((int)e.TimeMs), Enabled = false });
            }
            return m;
        }

        // ------------------------------------------------------------------ championships
        private ChampionshipProgress Progress(string id) { return saves.Data.Championships.FirstOrDefault(c => c.Id == id); }

        private Menu ChampionshipsMenu()
        {
            var m = new Menu("Championships", "Series");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                foreach (var cd in lib.Championships)
                {
                    var c = cd; var pr = Progress(c.Id);
                    m.Add(new MenuItem(c.Name, () => stack.Push(ChampionshipMenu(c)))
                    { Value = () => pr == null ? (saves.Data.Rep >= c.RequiredRep ? "$" + c.EntryFee.ToString("N0") : c.RequiredRep.ToString("N0") + " REP") : pr.Finished ? "Done" : "Round " + (pr.Round + 1) + "/" + c.Races.Count });
                }
            };
            return m;
        }

        private Menu ChampionshipMenu(ChampionshipDefinition c)
        {
            var m = new Menu(c.Name, c.Races.Count + " rounds");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                var pr = Progress(c.Id);
                if (pr == null || pr.Finished)
                {
                    bool can = saves.Data.Rep >= c.RequiredRep;
                    m.Add(new MenuItem(pr != null ? "Enter again" : "Enter championship", () =>
                    {
                        if (!money.TrySpend(c.EntryFee, "race", c.Name + " entry", "champ:" + c.Id + ":" + saves.Data.RacesStarted)) { Notification.PostTicker("~r~Not enough cash.", true, false); return; }
                        saves.Data.Championships.RemoveAll(x => x.Id == c.Id);
                        var np = new ChampionshipProgress { Id = c.Id };
                        np.Points.Add(new KeyValue { Key = "You", Value = "0" });
                        foreach (var n in Roster(c)) np.Points.Add(new KeyValue { Key = n, Value = "0" });
                        saves.Data.Championships.Add(np); saves.Save();
                        m.OnOpen(); m.ResetHighlight();
                    }) { Enabled = can, Value = () => can ? "$" + c.EntryFee.ToString("N0") : "Needs " + c.RequiredRep.ToString("N0") + " REP", Description = () => "Prize: $" + c.CashPrize.ToString("N0") + " and " + c.RepPrize + " REP for the overall win." });
                }
                if (pr != null && !pr.Finished)
                {
                    var next = Race(c.Races[Math.Min(pr.Round, c.Races.Count - 1)]);
                    if (next != null) m.Add(new MenuItem("Next: " + next.Name, () => { Function.Call(Hash.SET_NEW_WAYPOINT, next.Start.X, next.Start.Y); stack.Push(RaceMenu(next, c.Id)); })
                    { Description = () => "Set GPS and open the round." });
                }
                if (pr != null)
                    foreach (var kv in pr.Points.OrderByDescending(x => int.Parse(x.Value)))
                    { var e = kv; m.Add(new MenuItem(e.Key) { Value = () => e.Value + " pts", Enabled = false }); }
            };
            return m;
        }

        private List<string> Roster(ChampionshipDefinition c)
        {
            var rnd = new Random(RotationSeed(c.Id));
            return DriverNames.OrderBy(x => rnd.Next()).Take(5).ToList();
        }
        private static int RotationSeed(string s) { return Dealerships.RotationMath.Seed("champ", s); }

        // ------------------------------------------------------------------ lifecycle
        public void StartRivalRace(RaceDefinition r, RivalSpec spec)
        {
            if (session != null) return;
            StartRace(r, null, spec);
        }

        private void StartRace(RaceDefinition r, string champ, RivalSpec rivalSpec = null)
        {
            var car = vehicles.Current; var ped = Game.Player.Character;
            if (car == null || !car.Exists()) return;
            if (!string.IsNullOrEmpty(r.Classes) && !r.Classes.Split(',').Any(c => c.Trim().Equals(car.ClassType.ToString(), StringComparison.OrdinalIgnoreCase)))
            { Notification.PostTicker("~r~Wrong car class.~s~ Allowed: " + r.Classes, true, false); return; }
            if (champ == null && rivalSpec == null && r.EntryFee > 0 && !money.TrySpend(r.EntryFee, "race", r.Name + " entry", "race-fee:" + r.Id + ":" + saves.Data.RacesStarted))
            { Notification.PostTicker("~r~Not enough cash~s~ for the entry fee.", true, false); return; }
            saves.Data.RacesStarted++;
            if (Cameras.CameraSystem.Instance != null) Cameras.CameraSystem.Instance.Release();
            Screen.FadeOut(500); Script.Wait(550);
            List<Vector3> lap = null;
            try { lap = RouteBuilder.Build(r, car); }
            catch (Exception ex) { Log.Error("Route build", ex); }
            if (lap == null)
            {
                Screen.FadeIn(500);
                Notification.PostTicker("~r~Couldn't plot this route~s~ (GPS found no road path). Entry fee refunded.", true, false);
                if (champ == null && r.EntryFee > 0) money.Earn(r.EntryFee, 0, "race", "Refund " + r.Name, "race-refund:" + r.Id + ":" + saves.Data.RacesStarted);
                return;
            }
            championshipId = champ; rival = rivalSpec;
            Function.Call(Hash.CLEAR_AREA_OF_VEHICLES, r.Start.X, r.Start.Y, r.Start.Z, 45f, false, false, false, false, false, false, 0);
            float heading = (float)(Math.Atan2(-(lap[0].X - r.Start.X), lap[0].Y - r.Start.Y) * 180.0 / Math.PI);
            car.Position = r.Start; car.Heading = heading; car.PlaceOnGround(); car.Repair();
            session = new RaceSession(r, lap);
            session.AddPlayer(car, ped);
            if (rivalSpec != null && rivalSpec.Car != null && r.Kind != RaceType.Drift)
            {
                session.SpawnOpponents(new[] { rivalSpec.Name }, new[] { rivalSpec.Car }, r.Start, heading, saves.Data.RacesStarted);
                foreach (var x in session.Racers) if (!x.IsPlayer) { x.Skill = rivalSpec.Skill; if (x.Blip != null && x.Blip.Exists()) x.Blip.Color = BlipColor.Purple; }
            }
            else if (r.HasOpponents)
            {
                int n = r.Kind == RaceType.HeadToHead ? 1 : Math.Max(1, Math.Min(7, r.Opponents));
                var names = champ != null ? Roster(lib.Championships.First(c => c.Id == champ)).Take(n).ToList() : DriverNames.OrderBy(x => Guid.NewGuid()).Take(n).ToList();
                session.SpawnOpponents(names, PickCars(car, n, r), r.Start, heading, saves.Data.RacesStarted);
            }
            IsRacing = true;
            Function.Call(Hash.SET_GAMEPLAY_CAM_RELATIVE_HEADING, 0f);
            Screen.FadeIn(500);
            session.BeginCountdown();
            Log.Info("Race " + r.Id + " started with " + (session.Racers.Count - 1) + " opponents");
        }

        /// <summary>Opponent cars matched to the player's car tier (head-to-head rivals one tier up).</summary>
        private List<VehicleInfo> PickCars(Vehicle playerCar, int n, RaceDefinition r)
        {
            var pinfo = registry.Get(playerCar.Model.Hash);
            string tier = pinfo != null ? pinfo.Tier : "C";
            string[] tiers = { "D", "C", "B", "A", "S", "X" };
            int ti = Array.IndexOf(tiers, tier); if (r.Kind == RaceType.HeadToHead) ti = Math.Min(5, ti + 1);
            bool bikes = pinfo != null && pinfo.Class == VehicleClass.Motorcycles;
            var pool = registry.All.Where(v => v.Enabled && (v.Class == VehicleClass.Motorcycles) == bikes && v.Class != VehicleClass.OpenWheel && v.Class != VehicleClass.OffRoad && v.Class != VehicleClass.SUVs)
                .Where(v => Math.Abs(Array.IndexOf(tiers, v.Tier) - ti) <= 0).ToList();
            if (pool.Count < 3) pool = registry.All.Where(v => v.Enabled && v.Class == VehicleClass.Sports).ToList();
            var rnd = new Random(saves.Data.RacesStarted * 31 + 7);
            return Enumerable.Range(0, n).Select(_ => pool[rnd.Next(pool.Count)]).ToList();
        }

        private void TickSession()
        {
            try { session.Tick(); }
            catch (Exception ex) { Log.Error("Race tick", ex); session.Aborted = true; session.Done = true; }
            if (session.Done) Finish();
        }

        private void Finish()
        {
            var s = session; var r = s.Def;
            if (r.Kind == RaceType.Drift) s.BankDrift();
            var order = s.Standings();
            int pos = s.Aborted || !s.Player.Finished ? 0 : order.IndexOf(s.Player) + 1;
            var res = new RaceResult { RaceId = r.Id, Name = r.Name, Type = r.Kind.ToString(), Utc = DateTime.UtcNow.ToString("o"), Position = pos, Of = s.Racers.Count, TimeMs = s.Player.Finished ? s.Player.FinishTime : 0, Score = (long)s.DriftScore };
            var rec = vehicles.CurrentRecord; if (rec != null) res.Car = rec.Nickname ?? rec.Model;

            // rewards
            double cashF = 0, repF = 0;
            if (pos > 0)
            {
                if (r.Kind == RaceType.TimeTrial || r.Kind == RaceType.Drift)
                {
                    float gold = r.Kind == RaceType.TimeTrial ? TrialTarget(r, s) : DriftTarget(r, s);
                    Func<float, bool> better = k => r.Kind == RaceType.TimeTrial ? res.TimeMs <= gold * 1000f * k : res.Score >= gold / k;
                    res.Medal = better(1f) ? "Gold" : better(1.12f) ? "Silver" : better(1.3f) ? "Bronze" : "No medal";
                    cashF = res.Medal == "Gold" ? 1 : res.Medal == "Silver" ? 0.6 : res.Medal == "Bronze" ? 0.35 : 0.1;
                    repF = cashF;
                }
                else
                {
                    cashF = pos == 1 ? 1 : pos == 2 ? 0.55 : pos == 3 ? 0.3 : 0;
                    repF = pos == 1 ? 1 : pos == 2 ? 0.6 : pos == 3 ? 0.4 : 0.2;
                }
            }
            res.Cash = (long)(r.CashReward * cashF); res.Rep = (long)(r.RepReward * repF);
            if (res.Cash > 0 || res.Rep > 0) money.Earn(res.Cash, res.Rep, "race", r.Name + (pos > 0 ? " (P" + pos + ")" : ""), "race-reward:" + r.Id + ":" + saves.Data.RacesStarted);
            if (rec != null) { rec.Stats.RacesEntered++; if (pos == 1 && r.HasOpponents) rec.Stats.RacesWon++; }
            if (pos > 0 && res.TimeMs > 0 && r.Kind != RaceType.Drift)
            {
                long best = BestTime(r.Id);
                if (best == 0 || res.TimeMs < best) { saves.Data.BestTimes.RemoveAll(x => x.Key == r.Id); saves.Data.BestTimes.Add(new KeyValue { Key = r.Id, Value = res.TimeMs.ToString() }); }
            }
            saves.Data.RaceHistory.Add(res);
            string champLine = championshipId != null ? ScoreChampionship(order, s) : null;
            saves.Save();
            bool won = rival != null && (rival.DriftTarget > 0 ? pos > 0 && res.Score >= rival.DriftTarget : pos == 1);
            if (rival != null) res.Name = r.Name + " vs " + rival.Name;
            EventBus.Publish(new RaceFinishedEvent(r, res, rival != null ? rival.Id : null, won));
            rival = null;
            Log.Info("Race " + r.Id + " finished: P" + pos + " time " + res.TimeMs + " score " + res.Score + " cash " + res.Cash + " rep " + res.Rep);

            var lines = order.Select((x, i) => (i + 1) + ".  " + x.Name + "   " + (x.Finished ? RaceSession.FormatTime(x.FinishTime) : "DNF")).ToList();
            EndSession(false);
            ShowResults(r, res, r.HasOpponents ? lines : null, champLine);
        }

        private float TrialTarget(RaceDefinition r, RaceSession s)
        {
            if (r.TargetTimeSec > 0) return r.TargetTimeSec;
            float km = RouteBuilder.Length(s.Cps, r.Start) / 1000f;
            return km / 95f * 3600f;   // gold = 95 km/h average through traffic
        }
        private float DriftTarget(RaceDefinition r, RaceSession s) { return r.TargetScore > 0 ? r.TargetScore : RouteBuilder.Length(s.Cps, r.Start) * 12f; }

        private string ScoreChampionship(List<Racer> order, RaceSession s)
        {
            var c = lib.Championships.FirstOrDefault(x => x.Id == championshipId); var pr = c == null ? null : Progress(c.Id);
            if (c == null || pr == null || pr.Finished) return null;
            int[] pts = { 10, 8, 6, 5, 4, 3, 2, 1 };
            for (int i = 0; i < order.Count; i++)
            {
                if (!order[i].Finished && !order[i].IsPlayer) continue;
                if (order[i].IsPlayer && (s.Aborted || !order[i].Finished)) continue;
                var kv = pr.Points.FirstOrDefault(x => x.Key == order[i].Name);
                if (kv != null) kv.Value = (int.Parse(kv.Value) + pts[Math.Min(i, pts.Length - 1)]).ToString();
            }
            pr.Round++;
            if (pr.Round >= c.Races.Count)
            {
                pr.Finished = true;
                var final = pr.Points.OrderByDescending(x => int.Parse(x.Value)).ToList();
                int place = final.FindIndex(x => x.Key == "You") + 1;
                double f = place == 1 ? 1 : place == 2 ? 0.4 : place == 3 ? 0.2 : 0;
                if (f > 0) money.Earn((long)(c.CashPrize * f), (long)(c.RepPrize * f), "race", c.Name + " P" + place, "champ-prize:" + c.Id + ":" + saves.Data.RacesStarted);
                championshipId = null;
                return c.Name + " finished: P" + place + (f > 0 ? "  ~g~$" + ((long)(c.CashPrize * f)).ToString("N0") : "");
            }
            championshipId = null;
            return c.Name + ": round " + pr.Round + "/" + c.Races.Count + " done. You have " + pr.Points.First(x => x.Key == "You").Value + " pts.";
        }

        private void EndSession(bool silent)
        {
            if (session == null) { IsRacing = false; return; }
            try { session.Cleanup(); } catch (Exception ex) { Log.Error("Race cleanup", ex); }
            session = null; IsRacing = false;
            Function.Call(Hash.SET_WAYPOINT_OFF);
        }

        private void ShowResults(RaceDefinition r, RaceResult res, List<string> lines, string champLine)
        {
            int until = Game.GameTime + 12000;
            while (Game.GameTime < until)
            {
                UI.Theme.FullscreenFrame();
                Game.DisableAllControlsThisFrame();
                UI.Theme.Card(0.29f, 0.17f, 0.42f, 0.54f);
                UI.Theme.Rect(0.29f, 0.17f, 0.42f, 0.003f, UI.Theme.Cyan);
                T(r.Name.ToUpperInvariant(), 0.5f, 0.19f, 0.6f, Color.FromArgb(255, 170, 90, 255), Font.Pricedown);
                string head = res.Position == 0 ? "DID NOT FINISH" : res.Medal != null ? res.Medal.ToUpperInvariant() + (r.Kind == RaceType.Drift ? "   " + res.Score.ToString("N0") + " pts" : "   " + RaceSession.FormatTime((int)res.TimeMs)) : "POSITION " + res.Position + "/" + res.Of + "   " + RaceSession.FormatTime((int)res.TimeMs);
                T(head, 0.5f, 0.25f, 0.55f, Color.White, Font.ChaletComprimeCologne);
                float y = 0.31f;
                if (lines != null) foreach (var l in lines.Take(8)) { T(l, 0.5f, y, 0.34f, Color.White, Font.ChaletLondon); y += 0.03f; }
                y += 0.01f;
                T("~g~+$" + res.Cash.ToString("N0") + "~s~    ~b~+" + res.Rep + " REP", 0.5f, y, 0.45f, Color.White, Font.ChaletComprimeCologne); y += 0.045f;
                if (champLine != null) T(champLine, 0.5f, y, 0.34f, Color.White, Font.ChaletLondon);
                UI.Buttons.Draw(Control.FrontendAccept, "Continue");
                if (Game.IsControlJustPressed(Control.FrontendAccept) || Game.IsControlJustPressed(Control.FrontendCancel)) break;
                Script.Wait(0);
            }
        }

        private static void T(string s, float x, float y, float scale, Color c, Font f)
        { new TextElement(s, new PointF(x * 1280f, y * 720f), scale, c, f, Alignment.Center, true, true).ScaledDraw(); }
    }
}
