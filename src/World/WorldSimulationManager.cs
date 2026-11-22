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
using MidnightClub.Racing;
using MidnightClub.UI;
using MidnightClub.Vehicles;
using Font = GTA.UI.Font;

namespace MidnightClub.WorldSim
{
    /// <summary>The one place that decides what the living world spawns (spec §23): car meets, spontaneous
    /// street challenges, enthusiast cars in traffic and speed traps, all on one schedule and one entity
    /// budget, activated by proximity and cleaned up by distance.</summary>
    public sealed class WorldSimulationManager
    {
        private readonly SaveManager saves;
        private readonly TransactionManager money;
        private readonly VehicleRegistry registry;
        private readonly VehicleManager vehicles;
        private readonly WorldCalendar calendar;
        private readonly RaceManager races;
        private readonly MenuStack stack;
        private readonly Config cfg;
        private readonly Random rnd = new Random();
        private int nextSlowTick;

        public WorldSimulationManager(SaveManager saves, TransactionManager money, VehicleRegistry registry, VehicleManager vehicles, WorldCalendar calendar, RaceManager races, MenuStack stack, Config cfg)
        {
            this.saves = saves; this.money = money; this.registry = registry; this.vehicles = vehicles; this.calendar = calendar; this.races = races; this.stack = stack; this.cfg = cfg;
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To == GameMode.MIDNIGHT_CLUB) Setup(); else CleanupAll(); });
            EventBus.Subscribe<RaceFinishedEvent>(e => { if (e.Race.Id.StartsWith("street_") && e.Won) { saves.Data.StreetRacesWon++; saves.IsDirty = true; } });
        }

        private bool Busy { get { return RaceManager.IsRacing || stack.IsOpen; } }

        private void Tick()
        {
            // Per-frame, cheap: traffic density and the bits that draw markers or read input.
            if (cfg.TrafficDensity != 1f) { Function.Call(Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, cfg.TrafficDensity); Function.Call(Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, cfg.TrafficDensity); }
            if (cfg.ParkedDensity != 1f) Function.Call(Hash.SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, cfg.ParkedDensity);
            var car = vehicles.Current;
            if (car == null || !car.Exists()) return;
            if (cfg.CarMeets) MeetFrame(car);
            if (cfg.StreetChallenges) ChallengeFrame(car);
            if (cfg.SpeedTraps) TrapFrame(car);
            // Slow tick (1 Hz): scheduling, spawning, cleanup.
            if (Game.GameTime < nextSlowTick) return;
            nextSlowTick = Game.GameTime + 1000;
            try
            {
                if (cfg.CarMeets) MeetSlow(car);
                if (cfg.StreetChallenges && !Busy) ChallengeSlow(car);
                EnthusiastSlow(car);
            }
            catch (Exception ex) { Log.Error("World simulation", ex); }
        }

        private void Setup()
        {
            traps.Clear();
            foreach (var g in Garages.OnlineGarageCatalog.All.Where((g, i) => i % 5 == 0).Take(9))
            {
                float h; var p = World.GetNextPositionOnStreetWithHeading(new Vector3(g.Entrance[0], g.Entrance[1], g.Entrance[2]), out h, false);
                if (p == Vector3.Zero) continue;
                traps.Add(new Trap { Id = "trap_" + g.Id, Name = World.GetStreetName(p), Pos = p });
            }
            PlaceTrapBlips();
        }

        private void CleanupAll()
        {
            DespawnMeet(); DismissChallenger();
            foreach (var e in enthusiasts) Release(e);
            enthusiasts.Clear();
            foreach (var b in trapBlips) try { if (b.Exists()) b.Delete(); } catch { }
            trapBlips.Clear();
            foreach (var b in meetBlips) try { if (b.Exists()) b.Delete(); } catch { }
            meetBlips.Clear();
        }

        private static void Release(Vehicle v)
        {
            try { var d = v.Driver; if (d != null && d.Exists()) d.MarkAsNoLongerNeeded(); if (v.Exists()) v.MarkAsNoLongerNeeded(); } catch { }
        }

        private VehicleInfo RandomCar(Func<VehicleInfo, bool> filter)
        {
            var pool = registry.All.Where(v => v.Enabled && v.Class != VehicleClass.OpenWheel && v.Class != VehicleClass.Motorcycles && filter(v)).ToList();
            return pool.Count == 0 ? null : pool[rnd.Next(pool.Count)];
        }

        private Vehicle SpawnTuned(VehicleInfo info, Vector3 pos, float heading)
        {
            var m = new Model(info.Hash);
            if (!m.Request(3000)) return null;
            var v = World.CreateVehicle(m, pos, heading); m.MarkAsNoLongerNeeded();
            if (v == null || !v.Exists()) return null;
            v.Mods.InstallModKit();
            foreach (int t in new[] { 0, 1, 2, 3, 4, 7, 11, 12, 13, 15, 23 })
            {
                int c = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, v, t);
                if (c > 0 && rnd.NextDouble() < 0.7) Function.Call(Hash.SET_VEHICLE_MOD, v, t, rnd.Next(c), false);
            }
            v.Mods.PrimaryColor = (VehicleColor)rnd.Next(0, 160); v.Mods.PearlescentColor = (VehicleColor)rnd.Next(0, 160);
            Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, v, rnd.Next(0, 4));
            if (rnd.NextDouble() < 0.5) Function.Call(Hash.TOGGLE_VEHICLE_MOD, v, 22, true);
            v.DirtLevel = 0f;
            return v;
        }

        // =================================================================== car meets
        private sealed class MeetSpot { public string Id, Name, Theme; public Vector3 Pos; public float Heading; }
        private static readonly MeetSpot[] Spots =
        {
            new MeetSpot { Id = "legion", Name = "Legion Square lot", Pos = new Vector3(215.8f, -810.0f, 30.7f), Heading = 160f, Theme = "mixed" },
            new MeetSpot { Id = "cypress", Name = "Cypress Flats (LS Car Meet)", Pos = new Vector3(778.9f, -1867.4f, 29.3f), Heading = 260f, Theme = "tuner" },
            new MeetSpot { Id = "lsia", Name = "LSIA long-term parking", Pos = new Vector3(-796.9f, -2022.8f, 9.2f), Heading = 45f, Theme = "muscle" },
            new MeetSpot { Id = "sandy", Name = "Sandy Shores airfield", Pos = new Vector3(1715.0f, 3260.0f, 41.1f), Heading = 105f, Theme = "offroad" },
            new MeetSpot { Id = "vinewood", Name = "Vinewood Bowl", Pos = new Vector3(684.0f, 577.0f, 130.5f), Heading = 160f, Theme = "super" },
        };
        private MeetSpot activeMeet; private readonly List<Vehicle> meetCars = new List<Vehicle>(); private readonly List<Ped> meetPeds = new List<Ped>();
        private readonly List<Blip> meetBlips = new List<Blip>();
        private int meetDay = -1;

        /// <summary>Two meets a night (21:00-04:00) or all day at weekends; which ones rotates with the calendar.</summary>
        private List<MeetSpot> TonightsMeets()
        {
            int h = calendar.Hour;
            bool on = calendar.IsWeekend || h >= 21 || h < 4;
            if (!on) return new List<MeetSpot>();
            int d = calendar.Day;
            return new List<MeetSpot> { Spots[d % Spots.Length], Spots[(d * 3 + 2) % Spots.Length] }.Distinct().ToList();
        }

        private void MeetSlow(Vehicle car)
        {
            var tonight = TonightsMeets();
            int key = calendar.Day * 100 + (tonight.Count > 0 ? 1 : 0);
            if (key != meetDay)
            {
                meetDay = key;
                foreach (var b in meetBlips) try { if (b.Exists()) b.Delete(); } catch { }
                meetBlips.Clear();
                foreach (var s in tonight)
                {
                    var b = World.CreateBlip(s.Pos); b.Sprite = BlipSprite.CarMeetOrganizer; b.Color = BlipColor.Purple; b.Name = "Car meet: " + s.Name; b.IsShortRange = false;
                    meetBlips.Add(b);
                }
                if (tonight.Count > 0) Notification.PostTicker("~p~Car meets tonight:~s~ " + string.Join(", ", tonight.Select(s => s.Name)) + ".", false, false);
            }
            var near = tonight.FirstOrDefault(s => s.Pos.DistanceTo(car.Position) < 250f);
            if (near != activeMeet)
            {
                DespawnMeet();
                if (near != null) SpawnMeet(near);
            }
            else if (activeMeet != null && activeMeet.Pos.DistanceTo(car.Position) > 350f) DespawnMeet();
        }

        private void SpawnMeet(MeetSpot s)
        {
            activeMeet = s;
            Func<VehicleInfo, bool> theme;
            switch (s.Theme)
            {
                case "tuner": theme = v => v.Tuner || v.Class == VehicleClass.Sports; break;
                case "muscle": theme = v => v.Class == VehicleClass.Muscle; break;
                case "super": theme = v => v.Class == VehicleClass.Super || v.Class == VehicleClass.Sports; break;
                case "offroad": theme = v => v.Class == VehicleClass.OffRoad || v.Class == VehicleClass.Muscle; break;
                default: theme = v => v.Class == VehicleClass.Sports || v.Class == VehicleClass.Muscle || v.Class == VehicleClass.SportsClassics || v.Tuner; break;
            }
            double h = s.Heading * Math.PI / 180; var fwd = new Vector3((float)-Math.Sin(h), (float)Math.Cos(h), 0); var right = new Vector3(fwd.Y, -fwd.X, 0);
            int n = 8 + rnd.Next(5);
            for (int i = 0; i < n; i++)
            {
                var info = RandomCar(theme); if (info == null) break;
                int row = i / 4, col = i % 4;
                var pos = s.Pos + right * ((col - 1.5f) * 3.4f) + fwd * (row * 7f - 4f) + new Vector3(0, 0, 0.5f);
                var v = SpawnTuned(info, pos, s.Heading + (row == 1 ? 180f : 0f));
                if (v == null) continue;
                v.IsPersistent = true; v.PlaceOnGround(); Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, v, 2);
                if (rnd.NextDouble() < 0.4) Function.Call(Hash.SET_VEHICLE_LIGHTS, v, 2);
                meetCars.Add(v);
                if (rnd.NextDouble() < 0.5)
                {
                    string[] models = { "a_m_y_hipster_01", "a_f_y_hipster_02", "a_m_y_vinewood_01", "a_m_y_stwhi_01", "a_f_y_genhot_01" };
                    var pm = new Model(models[rnd.Next(models.Length)]);
                    if (pm.Request(2000))
                    {
                        var ped = World.CreatePed(pm, pos + right * 2.0f, s.Heading + 90f); pm.MarkAsNoLongerNeeded();
                        if (ped != null)
                        {
                            ped.IsPersistent = true;
                            string[] scen = { "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_CHEERING" };
                            Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped, scen[rnd.Next(scen.Length)], 0, true);
                            meetPeds.Add(ped);
                        }
                    }
                }
            }
            Log.Info("Car meet spawned at " + s.Id + " with " + meetCars.Count + " cars");
        }

        private void DespawnMeet()
        {
            foreach (var p in meetPeds) try { if (p.Exists()) p.Delete(); } catch { }
            foreach (var v in meetCars) try { if (v.Exists()) v.Delete(); } catch { }
            meetPeds.Clear(); meetCars.Clear(); activeMeet = null;
        }

        private void MeetFrame(Vehicle car)
        {
            if (activeMeet == null || Busy) return;
            if (car.Position.DistanceTo(activeMeet.Pos) > 22f || car.Speed > 2f) return;
            Screen.ShowHelpTextThisFrame("~INPUT_CONTEXT~ Car meet: " + activeMeet.Name);
            if (Game.IsControlJustPressed(Control.Context)) stack.Open(MeetMenu(activeMeet));
        }

        private Menu MeetMenu(MeetSpot s)
        {
            var m = new Menu("Car meet", s.Name);
            string key = s.Id + ":" + calendar.Day;
            m.Add(new MenuItem("Show off your car", () =>
            {
                if (saves.Data.MeetShowoffs.Contains(key)) { Notification.PostTicker("You already showed off here tonight.", true, false); return; }
                var rec = vehicles.CurrentRecord; var info = rec == null ? null : registry.Get(rec.ModelHash);
                long rep = 20 + (rec == null ? 0 : Math.Min(150, rec.Stats.ModificationCount * 6 + (rec.FitKit ? 30 : 0))) + (info == null ? 0 : (long)(info.Performance * 60));
                saves.Data.MeetShowoffs.Add(key);
                money.Earn(0, rep, "meet", "Showed off at " + s.Name, "meet:" + key);
                Notification.PostTicker("~p~The crowd likes it.~s~ +" + rep + " REP", true, false);
                stack.Close();
            }) { Value = () => saves.Data.MeetShowoffs.Contains(key) ? "Done" : "", Description = () => "More modifications and a quicker car impress more. Once per meet per night." });
            m.Add(new MenuItem("Race someone from the meet", () => { stack.Close(); MeetRace(s); }) { Description = () => "A one-on-one sprint against a car from the meet." });
            return m;
        }

        private void MeetRace(MeetSpot s)
        {
            var car = vehicles.Current; if (car == null) return;
            var pinfo = registry.Get(car.Model.Hash);
            var opp = RandomCar(v => pinfo == null || v.Tier == pinfo.Tier) ?? RandomCar(v => true);
            var def = StreetRaceFrom(car.Position, car.Heading, 1500f, 3200f, "Meet challenge", 2500, 120);
            if (def == null || opp == null) { Notification.PostTicker("Nobody wants to race right now.", true, false); return; }
            races.StartRivalRace(def, new RivalSpec { Name = RaceManager.DriverNames[rnd.Next(RaceManager.DriverNames.Length)], Car = opp, Skill = 1.0f });
        }

        /// <summary>A one-off sprint from here to a known road-side point ahead of you.</summary>
        private RaceDefinition StreetRaceFrom(Vector3 from, float heading, float min, float max, string name, long cash, long rep)
        {
            double h = heading * Math.PI / 180; var fwd = new Vector3((float)-Math.Sin(h), (float)Math.Cos(h), 0);
            var cand = Garages.OnlineGarageCatalog.All.Select(g => new Vector3(g.Entrance[0], g.Entrance[1], g.Entrance[2]))
                .Where(p => { float d = p.DistanceTo2D(from); return d >= min && d <= max && Vector3.Dot(Vector3.Normalize(p - from), fwd) > 0.2f; }).ToList();
            if (cand.Count == 0) cand = Garages.OnlineGarageCatalog.All.Select(g => new Vector3(g.Entrance[0], g.Entrance[1], g.Entrance[2])).Where(p => { float d = p.DistanceTo2D(from); return d >= min && d <= max; }).ToList();
            if (cand.Count == 0) return null;
            var to = cand[rnd.Next(cand.Count)];
            var def = new RaceDefinition { Id = "street_" + saves.Data.RacesStarted + "_" + rnd.Next(1000), Type = "HeadToHead", Opponents = 1, CashReward = cash, RepReward = rep, Name = name + ": to " + World.GetZoneLocalizedName(to) };
            def.Points.Add(new[] { from.X, from.Y, from.Z, heading });
            def.Points.Add(new[] { to.X, to.Y, to.Z, 0f });
            return def;
        }

        // =================================================================== spontaneous street challenges
        private Vehicle challenger; private int challengeUntil, stoppedSince = -1, nextChallengeAt;

        private void ChallengeSlow(Vehicle car)
        {
            int now = Game.GameTime;
            if (challenger != null) return;
            if (car.Speed < 0.8f) { if (stoppedSince < 0) stoppedSince = now; } else { stoppedSince = -1; return; }
            if (now < nextChallengeAt || now - stoppedSince < 1500) return;
            double chance = calendar.IsNight ? 0.25 : 0.08;
            nextChallengeAt = now + 45000;
            if (rnd.NextDouble() > chance) return;
            // Pull up a challenger in the next lane.
            var pinfo = registry.Get(car.Model.Hash);
            var info = RandomCar(x => pinfo == null || x.Tier == pinfo.Tier) ?? RandomCar(x => true);
            if (info == null) return;
            var pos = car.Position + car.RightVector * 3.6f + new Vector3(0, 0, 0.4f);
            if (World.GetNearbyVehicles(pos, 2.5f).Any()) pos = car.Position - car.RightVector * 3.6f + new Vector3(0, 0, 0.4f);
            if (World.GetNearbyVehicles(pos, 2.5f).Any()) return;
            var v = SpawnTuned(info, pos, car.Heading);
            if (v == null) return;
            v.PlaceOnGround(); v.IsPersistent = true;
            var pm = new Model("a_m_y_stwhi_01"); pm.Request(2000);
            var d = v.CreatePedOnSeat(VehicleSeat.Driver, pm); pm.MarkAsNoLongerNeeded();
            if (d == null) { v.Delete(); return; }
            d.IsPersistent = true;
            v.IsEngineRunning = true;
            Function.Call(Hash.START_VEHICLE_HORN, v, 800, new Model("HELDDOWN").Hash, false);
            challenger = v; challengeUntil = now + 10000;
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Text_Arrive_Tone", "Phone_SoundSet_Default", true);
        }

        private void ChallengeFrame(Vehicle car)
        {
            if (challenger == null) return;
            if (!challenger.Exists()) { challenger = null; return; }
            if (Game.GameTime > challengeUntil || car.Speed > 6f || Busy) { DismissChallenger(); return; }
            Screen.ShowHelpTextThisFrame("A racer revs next to you. ~INPUT_CONTEXT~ Accept street race");
            if (Game.IsControlJustPressed(Control.Context))
            {
                var info = registry.Get(challenger.Model.Hash);
                var def = StreetRaceFrom(car.Position, car.Heading, 1000f, 2600f, "Street race", 1800, 90);
                challenger.Delete(); challenger = null;
                if (def == null || info == null) return;
                races.StartRivalRace(def, new RivalSpec { Name = RaceManager.DriverNames[rnd.Next(RaceManager.DriverNames.Length)], Car = info, Skill = 0.98f + (float)rnd.NextDouble() * 0.1f });
            }
        }

        private void DismissChallenger()
        {
            if (challenger == null) return;
            try
            {
                var d = challenger.Driver;
                if (d != null && d.Exists()) d.Task.CruiseWithVehicle(challenger, 25f, VehicleDrivingFlags.DrivingModeAvoidVehicles);
                Release(challenger);
            }
            catch { }
            challenger = null;
        }

        // =================================================================== enthusiast cars in traffic
        private readonly List<Vehicle> enthusiasts = new List<Vehicle>();
        private int nextEnthusiast;

        private void EnthusiastSlow(Vehicle car)
        {
            enthusiasts.RemoveAll(v => { if (!v.Exists()) return true; if (v.Position.DistanceTo(car.Position) > 400f) { Release(v); return true; } return false; });
            if (enthusiasts.Count >= cfg.MaxEnthusiasts || Game.GameTime < nextEnthusiast) return;
            nextEnthusiast = Game.GameTime + 15000 + rnd.Next(20000);
            var around = car.Position + new Vector3((float)(rnd.NextDouble() - 0.5) * 300f, (float)(rnd.NextDouble() - 0.5) * 300f, 0);
            float h; var pos = World.GetNextPositionOnStreetWithHeading(around, out h, false);
            if (pos == Vector3.Zero || pos.DistanceTo(car.Position) < 120f || Screen.IsSphereVisible(pos, 3f)) return;
            var info = RandomCar(v => v.Class == VehicleClass.Sports || v.Class == VehicleClass.Muscle || v.Class == VehicleClass.SportsClassics || v.Class == VehicleClass.Super || v.Tuner);
            if (info == null) return;
            var veh = SpawnTuned(info, pos, h); if (veh == null) return;
            string[] models = { "a_m_y_hipster_01", "a_m_y_vinewood_01", "a_f_y_hipster_02", "a_m_y_ktown_01" };
            var pm = new Model(models[rnd.Next(models.Length)]); pm.Request(2000);
            var d = veh.CreatePedOnSeat(VehicleSeat.Driver, pm); pm.MarkAsNoLongerNeeded();
            if (d == null) { veh.Delete(); return; }
            Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, d, veh, 22f + (float)rnd.NextDouble() * 12f, (int)VehicleDrivingFlags.DrivingModeAvoidVehicles);
            enthusiasts.Add(veh);
        }

        // =================================================================== speed traps
        private sealed class Trap { public string Id, Name; public Vector3 Pos; public bool Inside; public float Peak; }
        private readonly List<Trap> traps = new List<Trap>();
        private readonly List<Blip> trapBlips = new List<Blip>();

        private void PlaceTrapBlips()
        {
            foreach (var t in traps)
            {
                var b = World.CreateBlip(t.Pos); b.Sprite = BlipSprite.Camera; b.Color = BlipColor.Yellow2; b.IsShortRange = true; b.Scale = 0.6f; b.Name = "Speed trap";
                trapBlips.Add(b);
            }
        }

        public float Record(string id) { var kv = saves.Data.SpeedTrapRecords.FirstOrDefault(x => x.Key == id); float v; return kv != null && float.TryParse(kv.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0f; }

        private void TrapFrame(Vehicle car)
        {
            foreach (var t in traps)
            {
                float d = car.Position.DistanceTo(t.Pos);
                if (d < 18f) { t.Inside = true; t.Peak = Math.Max(t.Peak, car.Speed * 3.6f); continue; }
                if (!t.Inside) continue;
                t.Inside = false;
                float kmh = t.Peak; t.Peak = 0;
                if (kmh < 60f) continue;
                float rec = Record(t.Id);
                if (kmh > rec)
                {
                    saves.Data.SpeedTrapRecords.RemoveAll(x => x.Key == t.Id);
                    saves.Data.SpeedTrapRecords.Add(new KeyValue { Key = t.Id, Value = kmh.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) });
                    long rep = (long)Math.Max(5, (kmh - rec) / 2);
                    money.Earn(0, rep, "speedtrap", "Speed trap record " + t.Name + " " + (int)kmh + " km/h", "trap:" + t.Id + ":" + (int)kmh);
                    Notification.PostTicker("~y~SPEED TRAP~s~ " + t.Name + ": ~g~" + (int)kmh + " km/h~s~ new record (was " + (int)rec + ").", true, false);
                }
                else Notification.PostTicker("~y~SPEED TRAP~s~ " + t.Name + ": " + (int)kmh + " km/h (record " + (int)rec + ").", false, false);
            }
        }
    }
}
