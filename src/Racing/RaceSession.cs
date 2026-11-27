using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Core;
using MidnightClub.Vehicles;
using Font = GTA.UI.Font;

namespace MidnightClub.Racing
{
    public sealed class Racer
    {
        public string Name; public bool IsPlayer;
        public Ped Ped; public Vehicle Car; public Blip Blip;
        public int Cp; public bool Finished; public int FinishTime; public float Skill = 1f;
        public int StuckSince = -1, LastTask;
        public float Progress;   // for ranking
        public bool Out;         // eliminated
        public bool IsCrew; public string Role; public int CrewTaskAt;
    }

    /// <summary>One race in progress (non-blocking: ticked every frame). Opponents drive checkpoint to
    /// checkpoint with the game's racing AI, with mild catch-up and un-stick help.</summary>
    public sealed class RaceSession
    {
        public static RaceSession Active;
        /// <summary>Best-run ghost to race against (solo races) and this run's recording.</summary>
        public Ghost Rival; public readonly Ghost Recording = new Ghost();
        private int splitShownAt = -10000, splitDelta; private bool splitValid;


        public readonly RaceDefinition Def;
        public readonly List<Vector3> Lap;      // checkpoints of one lap
        public readonly List<Vector3> Cps;      // all checkpoints over all laps
        public readonly List<Racer> Racers = new List<Racer>();
        public Racer Player { get { return Racers[0]; } }
        public int StartTime, CountdownEnd;
        public bool Running, Done, Aborted;
        public float DriftScore, DriftCombo = 1f; private float driftChain; private int driftLastHit;
        private int cpHandle = -1, cpHandleNext = -1; private Blip cpBlip, cpBlipNext;
        private int shownCp = -1, quitHeld = -1;
        private int lastBeep = -1;

        private static readonly Color Purple = Color.FromArgb(255, 170, 90, 255);
        private static readonly Color Panel = Color.FromArgb(190, 8, 6, 16);

        public RaceSession(RaceDefinition def, List<Vector3> lap)
        {
            Def = def; Lap = lap;
            Cps = new List<Vector3>();
            int laps = def.Lapped ? Math.Max(1, def.Laps) : 1;
            for (int l = 0; l < laps; l++) Cps.AddRange(lap);
        }

        public int LapOf(Racer r) { return Math.Min(Def.Laps, r.Cp / Math.Max(1, Lap.Count) + 1); }

        // ------------------------------------------------------------------ setup
        public void AddPlayer(Vehicle car, Ped ped) { Racers.Add(new Racer { Name = "You", IsPlayer = true, Car = car, Ped = ped }); }

        /// <summary>Road-aligned start slots from Grid.Build (slot 0 = the player).</summary>
        public List<Grid.Slot> GridSlots;

        public void SpawnOpponents(IList<string> names, IList<VehicleInfo> cars, Vector3 start, float heading, int seed, int slot0 = 0)
        {
            var rnd = new Random(seed);
            string[] peds = { "a_m_y_hipster_01", "a_m_y_beach_01", "a_m_y_genstreet_01", "a_f_y_hipster_01", "a_m_y_ktown_01", "a_m_y_stwhi_01", "a_f_y_genhot_01", "a_m_y_vinewood_01" };
            double h = heading * Math.PI / 180.0;
            var fwd = new Vector3((float)-Math.Sin(h), (float)Math.Cos(h), 0); var right = new Vector3(fwd.Y, -fwd.X, 0);
            for (int i = 0; i < names.Count && i < cars.Count; i++)
            {
                int g = i + slot0;
                int row = (g + 1) / 2, side = (g + 1) % 2 == 0 ? -1 : 1;
                var pos = start - fwd * (7.5f * row) + right * (g == 0 ? 3.2f : side * 3.2f);
                if (g == 0) pos = start + right * 3.4f;
                float carHeading = heading;
                if (GridSlots != null && g + 1 < GridSlots.Count) { pos = GridSlots[g + 1].Pos + new Vector3(0, 0, 0.3f); carHeading = GridSlots[g + 1].Heading; }
                try
                {
                    var vm = new Model(cars[i].Hash); vm.Request(5000);
                    var car = World.CreateVehicle(vm, pos, carHeading); vm.MarkAsNoLongerNeeded();
                    if (car == null || !car.Exists()) continue;
                    car.PlaceOnGround(); car.IsPersistent = true;
                    car.Mods.InstallModKit();
                    foreach (int t in new[] { 11, 12, 13, 15 }) { int c = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, car, t); if (c > 0) Function.Call(Hash.SET_VEHICLE_MOD, car, t, Math.Min(c - 1, rnd.Next(c)), false); }
                    Function.Call(Hash.TOGGLE_VEHICLE_MOD, car, 18, true);
                    car.Mods.PrimaryColor = (VehicleColor)rnd.Next(0, 160);
                    var pm = new Model(peds[rnd.Next(peds.Length)]); pm.Request(5000);
                    var ped = car.CreatePedOnSeat(VehicleSeat.Driver, pm); pm.MarkAsNoLongerNeeded();
                    if (ped == null || !ped.Exists()) { car.Delete(); continue; }
                    ped.IsPersistent = true;
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped, true);
                    Function.Call(Hash.SET_PED_KEEP_TASK, ped, true);
                    Function.Call(Hash.SET_DRIVER_ABILITY, ped, 1f);
                    Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, ped, 0.7f + (float)rnd.NextDouble() * 0.3f);
                    Function.Call(Hash.SET_DRIVER_RACING_MODIFIER, ped, 1f);
                    ped.CanBeDraggedOutOfVehicle = false;
                    Function.Call(Hash.SET_PED_CAN_BE_KNOCKED_OFF_VEHICLE, ped, 1);
                    car.IsInvincible = false;
                    Function.Call(Hash.SET_VEHICLE_EXPLODES_ON_HIGH_EXPLOSION_DAMAGE, car, false);
                    var b = UI.BlipRegistry.Own(car.AddBlip()); b.Color = BlipColor.Red; b.Scale = 0.7f; b.Name = names[i]; b.IsShortRange = false;
                    Racers.Add(new Racer { Name = names[i], Ped = ped, Car = car, Blip = b, Skill = 0.92f + (float)rnd.NextDouble() * 0.16f });
                }
                catch (Exception ex) { Log.Error("Spawning opponent", ex); }
            }
        }

        /// <summary>Your crew mate joins the grid (blocker rams the closest chaser, drafter runs just ahead of you).</summary>
        public void SpawnCrew(string name, VehicleInfo car, Vector3 start, float heading, string role, float skill)
        {
            if (car == null) return;
            int before = Racers.Count;
            SpawnOpponents(new[] { name }, new[] { car }, start, heading, Environment.TickCount, Racers.Count - 1);
            if (Racers.Count > before)
            {
                var c = Racers[Racers.Count - 1]; c.IsCrew = true; c.Role = role; c.Skill = skill;
                if (c.Blip != null && c.Blip.Exists()) { c.Blip.Color = BlipColor.Blue; c.Blip.Name = name + " (crew)"; }
            }
        }

        private void CrewTick(Racer r, int now)
        {
            float gap = r.Progress - Player.Progress;   // > 0 = crew ahead
            if (r.Role == "Drafter")
            {
                float mult = gap < 6f ? 1.4f : gap > 45f ? 0.82f : 1.08f;
                Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, r.Car, mult * r.Skill);
                return;
            }
            // blocker: hunt the closest opponent right behind you
            Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, r.Car, (gap < -30f ? 1.3f : 1.05f) * r.Skill);
            if (now - r.CrewTaskAt < 3000) return;
            r.CrewTaskAt = now;
            Racer target = null; float best = 60f;
            foreach (var o in Racers)
            {
                if (o.IsPlayer || o.IsCrew || o.Finished || o.Out || o.Car == null || !o.Car.Exists() || o.Ped == null || !o.Ped.Exists()) continue;
                float g = Player.Progress - o.Progress; if (g < -15f || g > 80f) continue;
                float d = o.Car.Position.DistanceTo(r.Car.Position); if (d < best) { best = d; target = o; }
            }
            if (target != null) { Function.Call(Hash.TASK_VEHICLE_CHASE, r.Ped, target.Ped); Function.Call(Hash.SET_TASK_VEHICLE_CHASE_IDEAL_PURSUIT_DISTANCE, r.Ped, 0f); }
            else Task(r);
        }

        public void Freeze(bool on)
        {
            foreach (var r in Racers) if (r.Car != null && r.Car.Exists()) Function.Call(Hash.FREEZE_ENTITY_POSITION, r.Car, on);
        }

        public void BeginCountdown()
        {
            IntroEnd = Game.GameTime + 4600; CountdownEnd = IntroEnd + 3500; Freeze(true); ShowCheckpoint();
            Active = this;
            if (Def.Kind == RaceType.Gymkhana) SpawnPillars();
        }

        // ------------------------------------------------------------------ per frame
        public void Tick()
        {
            if (Done) return;
            int now = Game.GameTime;
            if (!Running && IntroEnd > 0)
            {
                if (now < IntroEnd && !(now - (IntroEnd - 4600) > 600 && (UI.Input.JustPressed(Control.FrontendAccept) || UI.Input.JustPressed(Control.VehicleAccelerate)))) { Intro(now); return; }
                EndIntro(now);
            }
            if (!Running)
            {
                int left = CountdownEnd - now;
                int sec = (int)Math.Ceiling(left / 1000.0);
                if (sec != lastBeep && sec >= 1 && sec <= 3) { lastBeep = sec; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "3_2_1", "HUD_MINI_GAME_SOUNDSET", true); }
                LaunchTick(left);
                Countdown(left);
                if (left <= 0)
                {
                    Running = true; StartTime = now; Freeze(false);
                    Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "GO", "HUD_MINI_GAME_SOUNDSET", true);
                    foreach (var r in Racers) if (!r.IsPlayer) Task(r);
                    Launch();
                    if (Def.HotPursuit && Police.PursuitManager.Instance != null) Police.PursuitManager.Instance.StartHot(2);
                }
                DrawHud();
                return;
            }
            if (now - StartTime < 900) Countdown(now - StartTime > 0 ? -(now - StartTime) : 0);
            LaunchBoost(now);
            Slipstream();
            if (Def.Kind == RaceType.Rally || Def.Kind == RaceType.HillClimb) PaceNotes();
            if (Def.Kind == RaceType.Gymkhana) Gymkhana(now);
            if (Def.Kind == RaceType.Crawl && Player.Car != null && Player.Car.Exists() && Player.Car.IsUpsideDown && !Player.Finished) { UI.Shard.Mid("ROLLED OVER", "Crawls are about control. Try again.", UI.Shard.Red, 3000); Aborted = true; Done = true; return; }
            if (Def.Kind == RaceType.Crawl) UI.Ds.T("ROCK CRAWL  ·  DON'T ROLL IT", 0.5f, 0.04f, 0.3f, UI.Ds.Amber, UI.Ds.Display, Alignment.Center);
            if (!Player.Finished && Player.Car != null && Player.Car.Exists()) { Recording.Model = Player.Car.Model.Hash; Recording.Record(Player.Car, now - StartTime); }
            if (Rival != null) Rival.Play(now - StartTime);
            DrawSplit(now);

            // player
            var p = Player;
            if (!p.Finished && p.Car != null && p.Car.Exists())
            {
                float radius = p.Cp == Cps.Count - 1 ? 14f : 13f;
                if (p.Car.Position.DistanceTo(Cps[p.Cp]) < radius) Advance(p, now);
                if (Def.Scored) Drift(p.Car);
            }

            Takedowns(now);

            // opponents
            foreach (var r in Racers)
            {
                if (r.IsPlayer || r.Finished || r.Out || r.Car == null || !r.Car.Exists() || r.Ped == null || !r.Ped.Exists()) continue;
                if (r.Car.Position.DistanceTo(Cps[r.Cp]) < 18f) { Advance(r, now); if (!r.Finished && !(r.IsCrew && r.Role != "Drafter")) Task(r); }
                else if (now - r.LastTask > 4000 && !(r.IsCrew && r.Role != "Drafter")) Task(r);
                if (r.IsCrew) CrewTick(r, now); else CatchUp(r);
                Unstick(r, now);
            }

            // progress / ranking
            foreach (var r in Racers)
                if (!r.Finished && !r.Out && r.Car != null && r.Car.Exists())
                    r.Progress = r.Cp * 100000f - r.Car.Position.DistanceTo(Cps[Math.Min(r.Cp, Cps.Count - 1)]);
            if (Def.Kind == RaceType.Elimination && !p.Finished) Eliminations(now);
            if (Def.Kind == RaceType.Drag) DragShift(now);

            // hold Backspace to quit
            Game.DisableControlThisFrame(Control.VehicleCinCam);
            if (Game.IsKeyPressed(System.Windows.Forms.Keys.Back) || Game.IsControlPressed(Control.FrontendCancel)) { if (quitHeld < 0) quitHeld = now; else if (now - quitHeld > 1500) { Aborted = true; Done = true; } }
            else quitHeld = -1;

            if (p.Finished)
            {
                if (FinishCinematic(now)) return;
                bool allIn = Racers.All(r => r.Finished || r.Car == null || !r.Car.Exists());
                if (allIn || now - (StartTime + p.FinishTime) > 6000) Done = true;
            }
            DrawHud();
        }

        private void Advance(Racer r, int now)
        {
            if (r.IsPlayer)
            {
                int t = now - StartTime; Recording.Split(r.Cp, t);
                if (Rival != null && r.Cp < Rival.Splits.Count) { splitDelta = t - Rival.Splits[r.Cp]; splitValid = true; splitShownAt = now; }
            }
            r.Cp++;
            if (r.IsPlayer) Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "CHECKPOINT_NORMAL", "HUD_MINI_GAME_SOUNDSET", true);
            if (r.Cp >= Cps.Count)
            {
                r.Finished = true; r.FinishTime = now - StartTime; r.Cp = Cps.Count;
                r.Progress = 1e9f - Racers.Count(x => x.Finished);
                if (r.IsPlayer) { Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "FIRST_PLACE", "HUD_MINI_GAME_SOUNDSET", true); ClearCheckpoint(); }
                else if (r.Ped != null && r.Ped.Exists()) r.Ped.Task.CruiseWithVehicle(r.Car, 15f, VehicleDrivingFlags.DrivingModeStopForVehicles);
                return;
            }
            if (r.IsPlayer) ShowCheckpoint();
        }

        private void Task(Racer r)
        {
            r.LastTask = Game.GameTime;
            var target = Cps[Math.Min(r.Cp, Cps.Count - 1)];
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, r.Ped, r.Car, target.X, target.Y, target.Z, 70f * r.Skill, 0, r.Car.Model.Hash, (int)VehicleDrivingFlags.DrivingModeAvoidVehiclesReckless, 8f, -1f);
        }

        /// <summary>Mild rubber band: opponents far behind the player get a little extra power, far ahead a little less.</summary>
        private void CatchUp(Racer r)
        {
            float gap = Player.Progress - r.Progress;   // > 0 = opponent behind
            float mult = gap > 400f ? 1.25f : gap > 150f ? 1.12f : gap < -400f ? 0.92f : 1f;
            Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, r.Car, mult * r.Skill);
        }

        private void Unstick(Racer r, int now)
        {
            if (r.Car.Speed > 2f) { r.StuckSince = -1; return; }
            if (r.StuckSince < 0) { r.StuckSince = now; return; }
            if (now - r.StuckSince < 6000) return;
            if (r.Car.IsOnScreen && r.Car.Position.DistanceTo(Player.Car.Position) < 80f) return;   // don't teleport in view
            var from = r.Cp > 0 ? Cps[r.Cp - 1] : Def.Start;
            var to = Cps[r.Cp];
            float heading = (float)(Math.Atan2(-(to.X - from.X), to.Y - from.Y) * 180.0 / Math.PI);
            r.Car.Position = from; r.Car.Heading = heading; r.Car.PlaceOnGround();
            r.StuckSince = -1; Task(r);
        }

        private void Drift(Vehicle car)
        {
            float speed = car.Speed;
            var v = car.Velocity; v.Z = 0;
            float angle = 0f;
            if (speed > 8f)
            {
                var f = car.ForwardVector; f.Z = 0;
                angle = (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, Vector3.Dot(Vector3.Normalize(v), Vector3.Normalize(f))))) * 180.0 / Math.PI);
            }
            int now = Game.GameTime;
            if (angle > 12f && angle < 85f)
            {
                float dt = Game.LastFrameTime;
                driftChain += angle * speed * dt * 0.5f;
                DriftCombo = Math.Min(5f, 1f + driftChain / 2500f);
                driftLastHit = now;
            }
            else if (driftChain > 0 && now - driftLastHit > 1200)
            {   // drift ended cleanly: bank it
                DriftScore += driftChain * DriftCombo; driftChain = 0; DriftCombo = 1f;
            }
            if (Function.Call<bool>(Hash.HAS_ENTITY_COLLIDED_WITH_ANYTHING, car) && driftChain > 0) { driftChain = 0; DriftCombo = 1f; }
        }

        public float PendingDrift { get { return driftChain * DriftCombo; } }
        public void BankDrift() { DriftScore += driftChain * DriftCombo; driftChain = 0; }

        // ------------------------------------------------------------------ visuals
        private void ShowCheckpoint()
        {
            var p = Player; if (p.Cp >= Cps.Count || p.Cp == shownCp) return;
            ClearCheckpoint();
            shownCp = p.Cp;
            var c = Cps[p.Cp]; bool last = p.Cp == Cps.Count - 1;
            var next = last ? c : Cps[p.Cp + 1];
            cpHandle = Function.Call<int>(Hash.CREATE_CHECKPOINT, last ? 4 : 0, c.X, c.Y, c.Z - 1f, next.X, next.Y, next.Z, 11f, 170, 90, 255, 150, 0);
            Function.Call(Hash.SET_CHECKPOINT_CYLINDER_HEIGHT, cpHandle, 6f, 6f, 11f);
            cpBlip = UI.BlipRegistry.Own(World.CreateBlip(c)); cpBlip.Color = BlipColor.Yellow2; cpBlip.ShowRoute = true; cpBlip.Name = last ? "Finish" : "Checkpoint";
            if (!last) { cpBlipNext = UI.BlipRegistry.Own(World.CreateBlip(next)); cpBlipNext.Color = BlipColor.Yellow2; cpBlipNext.Scale = 0.6f; }
        }

        private void ClearCheckpoint()
        {
            if (cpHandle != -1) { Function.Call(Hash.DELETE_CHECKPOINT, cpHandle); cpHandle = -1; }
            if (cpHandleNext != -1) { Function.Call(Hash.DELETE_CHECKPOINT, cpHandleNext); cpHandleNext = -1; }
            if (cpBlip != null && cpBlip.Exists()) cpBlip.Delete(); cpBlip = null;
            if (cpBlipNext != null && cpBlipNext.Exists()) cpBlipNext.Delete(); cpBlipNext = null;
            shownCp = -1;
        }

        public List<Racer> Standings()
        {
            return Racers.Where(r => !r.IsCrew).OrderByDescending(r => r.Finished ? 2e9f - r.FinishTime : r.Progress).ToList();
        }

        private void DrawSplit(int now)
        {
            if (!splitValid || now - splitShownAt > 2600) return;
            float a = Math.Min(1f, (2600 - (now - splitShownAt)) / 400f);
            bool ahead = splitDelta <= 0;
            var c = ahead ? UI.Th.Green : UI.Th.Red;
            string s = (ahead ? "-" : "+") + (Math.Abs(splitDelta) / 1000f).ToString("0.00");
            float h = UI.Th.Px(52f), w = UI.Ds.Width(s, 0.6f, UI.Ds.Display) + UI.Ds.Width("GHOST", 0.24f, UI.Ds.Display) + UI.Ds.Px(UI.Th.Px(60f)), x = 0.5f - w / 2f, y = 0.17f;
            UI.Kit.Glass(x, y, w, h, h * 1080f / 2f, a, true);
            UI.Kit.Chip("GHOST", x + UI.Ds.Px(UI.Th.Px(10f)), y + h / 2f, UI.Ds.A(c, 0.2f * a), UI.Ds.A(c, a), 0.24f);
            UI.Ds.TM(s, x + w - UI.Ds.Px(UI.Th.Px(20f)), y + h / 2f, 0.6f, UI.Ds.A(c, a), UI.Ds.Display, Alignment.Right);
        }

        // ------------------------------------------------------------------ gymkhana (Ken Block style)
        private readonly List<Vector3> pillars = new List<Vector3>(); private readonly List<Prop> pillarProps = new List<Prop>();
        private int donutPillar = -1; private float donutAcc, lastYaw; private readonly Dictionary<int, int> proxAt = new Dictionary<int, int>();
        public int Donuts;
        private void SpawnPillars()
        {
            var end = Cps[Cps.Count - 1]; var dir = end - Def.Start; dir.Z = 0; float len = dir.Length(); if (len < 100f) return; dir.Normalize();
            var right = new Vector3(dir.Y, -dir.X, 0);
            var m = new Model("prop_roadcone02a"); m.Request(2000);
            int k = 0;
            for (float along = 70f; along < len - 50f; along += 75f, k++)
            {
                var p = Def.Start + dir * along + right * ((k % 2 == 0) ? 4.5f : -4.5f);
                float gz; if (World.GetGroundHeight(p + new Vector3(0, 0, 4f), out gz)) p.Z = gz;
                pillars.Add(p);
                var pr = World.CreateProp(m, p, false, true); if (pr != null) { pr.IsPersistent = true; Function.Call(Hash.FREEZE_ENTITY_POSITION, pr, true); pillarProps.Add(pr); }
            }
            m.MarkAsNoLongerNeeded();
        }
        private void Gymkhana(int now)
        {
            var car = Player.Car; if (car == null || !car.Exists() || Player.Finished) return;
            int left = 90000 - (now - StartTime);
            UI.Ds.T("TIME LEFT  " + Math.Max(0, left / 1000) + "s  ·  DONUTS " + Donuts, 0.5f, 0.04f, 0.32f, left < 15000 ? UI.Ds.Red : UI.Ds.Text, UI.Ds.Display, Alignment.Center);
            if (left <= 0) { BankDrift(); Player.Finished = true; Player.FinishTime = now - StartTime; Player.Cp = Cps.Count; ClearCheckpoint(); return; }
            for (int i = 0; i < pillars.Count; i++)
            {
                var p = pillars[i]; float pulse = 0.6f + 0.4f * (float)Math.Sin(now / 200.0 + i);
                Function.Call(Hash.DRAW_MARKER, 1, p.X, p.Y, p.Z - 0.2f, 0f, 0f, 0f, 0f, 0f, 0f, 1.6f, 1.6f, 3.5f, 251, 191, 36, (int)(90 * pulse) + 40, false, false, 2, false, 0, 0, false);
                Function.Call(Hash.DRAW_MARKER, 25, p.X, p.Y, p.Z + 0.05f, 0f, 0f, 0f, 0f, 0f, 0f, 20f, 20f, 1f, 251, 191, 36, i == donutPillar ? 120 : 45, false, false, 2, false, 0, 0, false);
            }
            // nearest pillar
            int near = -1; float nd = 11f;
            for (int i = 0; i < pillars.Count; i++) { float d = car.Position.DistanceTo2D(pillars[i]); if (d < nd) { nd = d; near = i; } }
            float yaw = car.Heading;
            if (near != donutPillar) { donutPillar = near; donutAcc = 0f; lastYaw = yaw; }
            else if (near >= 0)
            {
                float dy = yaw - lastYaw; if (dy > 180f) dy -= 360f; if (dy < -180f) dy += 360f; lastYaw = yaw;
                donutAcc += dy;
                if (Math.Abs(donutAcc) >= 360f)
                {
                    donutAcc -= Math.Sign(donutAcc) * 360f; Donuts++;
                    float pts = 1500f * Math.Max(1f, DriftCombo); DriftScore += pts;
                    Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET", true);
                    UI.Shard.Mid("DONUT", "+" + ((int)pts).ToString("N0"), UI.Shard.Gold, 1200);
                }
            }
            for (int i = 0; i < pillars.Count; i++)
            {
                float d = car.Position.DistanceTo2D(pillars[i]); int t;
                if (d < 3.2f && car.Speed > 11f && (!proxAt.TryGetValue(i, out t) || now - t > 3000)) { proxAt[i] = now; DriftScore += 300f; UI.Toast.Post("~y~CLOSE CALL~s~  +300"); }
            }
        }

        // ------------------------------------------------------------------ elimination
        private int nextElim, elimCount;
        public bool PlayerEliminated;
        private void Eliminations(int now)
        {
            if (nextElim == 0) nextElim = StartTime + 40000;
            int left = nextElim - now;
            var alive = Racers.Where(r => !r.Out && !r.Finished && !r.IsCrew).ToList();
            if (alive.Count <= 1 && !Player.Out)
            {   // last one standing
                Player.Finished = true; Player.FinishTime = now - StartTime; Player.Progress = 1e9f; ClearCheckpoint();
                foreach (var r in Racers) if (!r.IsPlayer && !r.Finished && !r.IsCrew) r.Out = true;
                return;
            }
            // timer bar
            float w = UI.Ds.Px(0.22f), x = 0.5f - w / 2f, y = 0.035f, k = Math.Max(0f, Math.Min(1f, left / 30000f));
            bool lastIsMe = Standings().Where(r => !r.Out).LastOrDefault() == Player;
            UI.Ds.Rect(x, y, w, 0.05f, UI.Ds.A(UI.Ds.Bg, 0.82f));
            UI.Ds.T("ELIMINATION IN " + Math.Max(0, left / 1000) + "s", 0.5f, y + 0.006f, 0.32f, lastIsMe && left < 10000 ? UI.Ds.Red : UI.Ds.Text, UI.Ds.Display, Alignment.Center);
            UI.Ds.Rect(x, y + 0.042f, w * k, 0.004f, lastIsMe ? UI.Ds.Red : UI.Ds.Amber);
            if (left > 0) return;
            nextElim = now + 30000;
            var victim = Standings().Where(r => !r.Out && !r.Finished).LastOrDefault();
            if (victim == null) return;
            victim.Out = true; elimCount++; victim.Progress = -1e9f + elimCount * 1000f;
            if (victim.IsPlayer)
            {
                PlayerEliminated = true; UI.Shard.Mid("ELIMINATED", "You were last. Out in " + (Racers.Count(r => !r.Out) + 1) + Ord(Racers.Count(r => !r.Out) + 1), UI.Shard.Red, 3500);
                Done = true; return;
            }
            UI.Shard.Mid("ELIMINATED", victim.Name.ToUpperInvariant(), UI.Shard.Orange, 2200);
            try { if (victim.Blip != null && victim.Blip.Exists()) victim.Blip.Delete(); if (victim.Ped != null && victim.Ped.Exists() && victim.Car != null && victim.Car.Exists()) victim.Ped.Task.CruiseWithVehicle(victim.Car, 12f, VehicleDrivingFlags.DrivingModeStopForVehicles); } catch { }
        }

        // ------------------------------------------------------------------ drag: shift timing
        private int lastGear = 1; private bool shiftUsed; private string shiftText; private int shiftShownAt; private Color shiftCol;
        private void DragShift(int now)
        {
            var car = Player.Car; if (car == null || !car.Exists() || Player.Finished) return;
            Game.DisableControlThisFrame(Control.VehicleHorn);
            int gear = car.CurrentGear; float rpm = car.CurrentRPM;
            if (gear != lastGear) { lastGear = gear; shiftUsed = false; }
            if (!shiftUsed && UI.Input.JustPressed(Control.VehicleHorn))
            {
                shiftUsed = true;
                if (rpm >= 0.88f && rpm <= 0.98f) { shiftText = "PERFECT SHIFT"; shiftCol = UI.Ds.Lime; car.Velocity += car.ForwardVector * 2.6f; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET", true); }
                else if (rpm >= 0.8f) { shiftText = "GOOD SHIFT"; shiftCol = UI.Ds.Cyan; car.Velocity += car.ForwardVector * 1.1f; }
                else { shiftText = "EARLY"; shiftCol = UI.Ds.Red; }
                shiftShownAt = now;
            }
            // shift lights (12 LEDs) + gear
            int leds = 12; float lw = UI.Ds.Px(0.022f), gap = UI.Ds.Px(0.006f), total = leds * lw + (leds - 1) * gap, x = 0.5f - total / 2f, y = 0.12f;
            for (int i = 0; i < leds; i++)
            {
                float th = 0.55f + i * (0.43f / leds); bool on = rpm >= th;
                var c = i >= leds - 2 ? UI.Ds.Red : i >= leds - 5 ? UI.Ds.Lime : UI.Ds.Amber;
                bool flash = rpm >= 0.88f && (Game.GameTime / 80) % 2 == 0;
                UI.Ds.Rect(x + i * (lw + gap), y, lw, 0.016f, on ? (flash ? UI.Ds.Lime : c) : UI.Ds.A(UI.Ds.Stroke, 0.9f));
            }
            UI.Ds.T(gear.ToString(), 0.5f, y + 0.025f, 1.1f, UI.Ds.Text, UI.Ds.Display, Alignment.Center);
            UI.Ds.T("SHIFT  [E / L3]", 0.5f, y + 0.1f, 0.24f, UI.Ds.Muted, UI.Ds.Display, Alignment.Center);
            if (shiftText != null && now - shiftShownAt < 900) UI.Ds.T(shiftText, 0.5f, y + 0.125f, 0.5f, UI.Ds.A(shiftCol, 1f - (now - shiftShownAt) / 900f), UI.Ds.Display, Alignment.Center);
        }

        // ------------------------------------------------------------------ rally pace notes (co-driver)
        private sealed class Note { public int Cp; public string Icon, Text; public Vector3 At; }
        private List<Note> notes; private int lastCalled = -1;
        private void BuildNotes()
        {
            notes = new List<Note>();
            var pts = new List<Vector3> { Def.Start }; pts.AddRange(Cps);
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var a = pts[i] - pts[i - 1]; var b = pts[i + 1] - pts[i]; a.Z = 0; b.Z = 0;
                if (a.Length() < 1f || b.Length() < 1f) continue;
                float ang = (float)(Math.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y) * 180.0 / Math.PI);   // + = left
                float abs = Math.Abs(ang); if (abs < 14f) continue;
                float radius = Math.Min(a.Length(), b.Length()) / Math.Max(0.05f, abs * (float)Math.PI / 180f);
                int sev = abs > 120 ? 1 : radius < 18 ? 2 : radius < 35 ? 3 : radius < 60 ? 4 : radius < 110 ? 5 : 6;
                if (sev == 6 && abs < 22f) continue;
                bool left = ang > 0;
                string word = sev == 1 ? "HAIRPIN" : (left ? "LEFT " : "RIGHT ") + sev;
                float dz = pts[i + 1].Z - pts[i].Z; if (dz > 4f) word += "  ·  CREST";
                notes.Add(new Note { Cp = i - 1, Icon = "pn_" + (left ? "l" : "r") + sev, Text = word, At = pts[i] });
            }
            // "into" joins: two notes close together
            for (int i = 0; i + 1 < notes.Count; i++) if (notes[i].At.DistanceTo(notes[i + 1].At) < 60f) notes[i].Text += "  INTO";
        }
        private void PaceNotes()
        {
            if (notes == null) BuildNotes();
            var car = Player.Car; if (car == null || !car.Exists() || Player.Finished) return;
            var next = notes.FirstOrDefault(n => n.Cp >= Player.Cp);
            if (next == null) return;
            float d = car.Position.DistanceTo(next.At);
            int idx = notes.IndexOf(next);
            if (d < 220f && lastCalled < idx) { lastCalled = idx; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET", true); }
            float a = d < 260f ? 1f : 0.45f;
            float cx = 0.5f, y = 0.06f, w = UI.Ds.Px(0.3f);
            UI.Ds.Rect(cx - w / 2f, y, w, 0.105f, UI.Ds.A(UI.Ds.Bg, 0.8f));
            UI.Ds.Rect(cx - w / 2f, y, w, 0.004f, UI.Ds.A(UI.Ds.Amber, a));
            var col = next.Icon.EndsWith("1") || next.Icon.EndsWith("2") ? UI.Ds.Red : next.Icon.EndsWith("3") ? UI.Ds.Amber : UI.Ds.Text;
            UI.Textures.Draw(next.Icon, cx - w / 2f + UI.Ds.Px(0.05f), y + 0.054f, 0.082f, UI.Ds.A(col, a));
            UI.Ds.T(next.Text, cx - w / 2f + UI.Ds.Px(0.1f), y + 0.016f, 0.55f, UI.Ds.A(col, a), UI.Ds.Display);
            UI.Ds.T(((int)(d / 10) * 10) + " M", cx - w / 2f + UI.Ds.Px(0.1f), y + 0.062f, 0.32f, UI.Ds.A(UI.Ds.Muted, a), UI.Ds.Display);
            if (idx + 1 < notes.Count)
            {
                var n2 = notes[idx + 1];
                UI.Textures.Draw(n2.Icon, cx + w / 2f - UI.Ds.Px(0.035f), y + 0.054f, 0.05f, UI.Ds.A(UI.Ds.Muted, 0.8f));
                UI.Ds.T("THEN", cx + w / 2f - UI.Ds.Px(0.07f), y + 0.075f, 0.22f, UI.Ds.Faint, UI.Ds.Display, Alignment.Right);
            }
        }

        // ------------------------------------------------------------------ intro cinematic (title card + grid flyover)
        public int IntroEnd;
        private Camera introCam; private float introLen = -1f;
        public string TypeName
        {
            get
            {
                switch (Def.Kind) { case RaceType.Circuit: return "CIRCUIT"; case RaceType.TimeTrial: return "TIME TRIAL"; case RaceType.Drift: return "DRIFT"; case RaceType.HeadToHead: return "HEAD TO HEAD"; }
                switch (Def.Kind) { case RaceType.Rally: return "RALLY STAGE"; case RaceType.Drag: return "DRAG RACE"; case RaceType.Elimination: return "ELIMINATION"; case RaceType.Offroad: return "TRAIL RACE"; case RaceType.CrossCountry: return "CROSS COUNTRY"; case RaceType.HillClimb: return "HILL CLIMB"; case RaceType.Gymkhana: return "GYMKHANA"; case RaceType.Moto: return "BIKE RACE"; case RaceType.Crawl: return "ROCK CRAWL"; }
                return "SPRINT";
            }
        }
        private void Intro(int now)
        {
            var car = Player.Car; if (car == null || !car.Exists()) return;
            float t = 1f - (IntroEnd - now) / 4600f;
            if (introCam == null)
            {
                if (Cameras.CameraSystem.Instance != null) Cameras.CameraSystem.Instance.Release();
                Cameras.CameraSystem.Suspended = true;
                introCam = World.CreateCamera(car.Position, Vector3.Zero, 40f); introCam.IsActive = true;
                Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false, 0);
                introLen = RouteBuilder.Length(Cps, Def.Start);
            }
            Game.DisableAllControlsThisFrame(); Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
            // sweep from high in front of the grid down to behind the player's car
            var f = car.ForwardVector; var r = car.RightVector; var c = car.Position;
            float e = UI.Ds.EaseInOut(t);
            var from = c + f * 22f + r * -9f + new Vector3(0, 0, 9f);
            var to = c - f * 6.5f + r * 1.2f + new Vector3(0, 0, 1.6f);
            introCam.Position = Vector3.Lerp(from, to, e);
            introCam.PointAt(c + f * (8f * (1f - e) + 6f * e) + new Vector3(0, 0, 0.6f));
            introCam.FieldOfView = 42f + 10f * e;
            // title card
            float a = Math.Min(1f, Math.Min(t * 5f, (1f - t) * 6f)), x = 0.07f, off = (1f - UI.Ds.EaseOut(t * 3f)) * -0.03f;
            UI.Ds.HGrad(0f, 0.62f, 0.6f, 0.3f, UI.Ds.A(UI.Ds.Bg, 0.85f * a), UI.Ds.A(UI.Ds.Bg, 0f), 8);
            UI.Ds.Tag(TypeName, x + off, 0.66f, UI.Ds.Violet, Color.White, 0.3f, a);
            UI.Ds.T(Def.Name.ToUpperInvariant(), x + off, 0.695f, 1.05f, UI.Ds.A(UI.Ds.Text, a), UI.Ds.Display);
            string info = (introLen / 1000f).ToString("0.0") + " KM" + (Def.Lapped ? "  ·  " + Def.Laps + " LAPS" : "") + (Racers.Count > 1 ? "  ·  " + (Racers.Count - 1) + (Racers.Count == 2 ? " OPPONENT" : " OPPONENTS") : "") + "  ·  $" + Def.CashReward.ToString("N0") + " TO THE WINNER";
            UI.Ds.T(info, x + off, 0.79f, 0.32f, UI.Ds.A(UI.Ds.Muted, a), UI.Ds.Display);
            var names = string.Join("   ", Racers.Where(q => !q.IsPlayer).Select(q => q.Name.ToUpperInvariant()).ToArray());
            if (names.Length > 0) UI.Ds.T("VS  " + names, x + off, 0.825f, 0.28f, UI.Ds.A(UI.Ds.Pink, a), UI.Ds.Display);
            if (now - (IntroEnd - 4600) > 600) UI.Buttons.Draw(Control.FrontendAccept, "Skip");
        }
        private void EndIntro(int now)
        {
            if (introCam != null)
            {
                Function.Call(Hash.RENDER_SCRIPT_CAMS, false, true, 400, true, false, 0);
                try { introCam.Delete(); } catch { } introCam = null;
            }
            if (now < IntroEnd) CountdownEnd = now + 3500;
            IntroEnd = 0; Cameras.CameraSystem.Suspended = false;
        }

        // ------------------------------------------------------------------ takedowns (Burnout-style)
        public int TakedownCount;
        private readonly Dictionary<int, int> contact = new Dictionary<int, int>();
        private readonly Dictionary<int, float> contactSpeed = new Dictionary<int, float>();
        private readonly HashSet<int> wrecked = new HashSet<int>();
        private void Takedowns(int now)
        {
            var car = Player.Car; if (car == null || !car.Exists()) return;
            foreach (var r in Racers)
            {
                if (r.IsPlayer || r.IsCrew || r.Car == null || !r.Car.Exists() || r.Finished) continue;
                int h = r.Car.Handle;
                if (Function.Call<bool>(Hash.IS_ENTITY_TOUCHING_ENTITY, car, r.Car)) { if (!contact.ContainsKey(h) || now - contact[h] > 2500) contactSpeed[h] = r.Car.Speed; contact[h] = now; }
                int t; if (!contact.TryGetValue(h, out t) || now - t > 2500) { if (wrecked.Contains(h) && r.Car.Speed > 12f && !r.Car.IsUpsideDown) wrecked.Remove(h); continue; }
                if (wrecked.Contains(h)) continue;
                float before; contactSpeed.TryGetValue(h, out before);
                bool spun = Vector3.Dot(Vector3.Normalize(r.Car.ForwardVector), Player.Car.ForwardVector) < -0.2f && r.Car.Speed < 8f;
                if (before > 18f && (r.Car.IsUpsideDown || r.Car.Speed < before * 0.25f || spun) && car.Speed > 12f)
                {
                    wrecked.Add(h); TakedownCount++;
                    if (Career.Progression.Instance != null) Career.Progression.Instance.Bump("takedown", 1);
                    Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET", true);
                    UI.Shard.Mid("TAKEDOWN", r.Name.ToUpperInvariant() + (TakedownCount > 1 ? "  ·  x" + TakedownCount : ""), UI.Shard.Orange, 1800);
                    r.StuckSince = now - 2000;   // help them get going again a bit sooner
                }
            }
        }

        // ------------------------------------------------------------------ finish cinematic
        private Camera finCam; private int finStart = -1, finReal; private float finYaw;
        /// <summary>Slow-motion orbit around the car for ~2.6 s after crossing the line. True while running.</summary>
        private bool FinishCinematic(int now)
        {
            var car = Player.Car; if (car == null || !car.Exists()) return false;
            if (finStart == -2) return false;
            if (finStart < 0)
            {
                finStart = now; finReal = Environment.TickCount; finYaw = car.Heading + 120f;
                if (Cameras.CameraSystem.Instance != null) Cameras.CameraSystem.Instance.Release();
                Cameras.CameraSystem.Suspended = true;
                finCam = World.CreateCamera(car.Position, Vector3.Zero, 38f);
                finCam.IsActive = true; Function.Call(Hash.RENDER_SCRIPT_CAMS, true, true, 250, true, false, 0);
                Function.Call(Hash.SET_TIME_SCALE, 0.35f);
                try { Function.Call(Hash.ANIMPOSTFX_PLAY, "MinigameEndNeutral", 0, false); } catch { }
            }
            float el = (Environment.TickCount - finReal) / 1000f;   // real seconds
            Game.DisableAllControlsThisFrame();
            Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
            finYaw += 14f * UI.Ds.Dt;
            float yr = finYaw * (float)Math.PI / 180f;
            var tgt = car.Position + new Vector3(0, 0, 0.5f);
            finCam.Position = tgt + new Vector3(-(float)Math.Sin(yr) * 6.5f, (float)Math.Cos(yr) * 6.5f, 1.1f);
            finCam.PointAt(tgt);
            var order = Standings(); int pos = order.IndexOf(Player) + 1;
            string big = Def.HasOpponents ? pos + Ord(pos) : "FINISH";
            float a = Math.Min(1f, el * 3f);
            UI.Ds.T(big, 0.5f, 0.68f, 2.4f, UI.Ds.A(pos == 1 || !Def.HasOpponents ? UI.Ds.Amber : UI.Ds.Text, a), UI.Ds.Display, Alignment.Center);
            UI.Ds.T(FormatTime(Player.FinishTime), 0.5f, 0.86f, 0.62f, UI.Ds.A(UI.Ds.Text, a), UI.Ds.Display, Alignment.Center);
            if (el < 2.7f) return true;
            EndCinematic();
            return false;
        }
        private void EndCinematic()
        {
            if (finStart == -2) return;
            finStart = -2;
            Function.Call(Hash.SET_TIME_SCALE, 1f);
            Function.Call(Hash.RENDER_SCRIPT_CAMS, false, true, 300, true, false, 0);
            try { if (finCam != null) finCam.Delete(); } catch { }
            finCam = null; Cameras.CameraSystem.Suspended = false;
        }

        // ------------------------------------------------------------------ launch control
        private float rev, revVel; private int launchUntil; private float launchPower; private string launchGrade;
        private const float GreenLo = 0.66f, GreenHi = 0.82f;
        private void LaunchTick(int left)
        {
            var car = Player.Car; if (car == null || !car.Exists()) return;
            float thr = Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, (int)Control.VehicleAccelerate);
            float target = thr > 0.05f ? 0.18f + thr * 0.9f + 0.05f * (float)Math.Sin(Game.GameTime / 90.0) : 0.12f;
            rev = UI.Ds.Damp(rev, Math.Min(1.05f, target), thr > 0.05f ? 3.2f : 5f);
            try { car.CurrentRPM = Math.Min(1f, rev); } catch { }
            if (left > 3000) return;
            // rev gauge
            float w = UI.Ds.Px(0.3f), x = 0.5f - w / 2f, y = 0.62f, h = 0.014f;
            UI.Ds.Rect(x - UI.Ds.Px(0.004f), y - 0.004f, w + UI.Ds.Px(0.008f), h + 0.008f, UI.Ds.A(UI.Ds.Bg, 0.85f));
            UI.Ds.Rect(x + w * GreenLo, y, w * (GreenHi - GreenLo), h, UI.Ds.A(UI.Ds.Lime, 0.35f));
            UI.Ds.Rect(x + w * 0.92f, y, w * 0.08f, h, UI.Ds.A(UI.Ds.Red, 0.35f));
            bool green = rev >= GreenLo && rev <= GreenHi;
            UI.Ds.HGrad(x, y, w * Math.Min(1f, rev), h, UI.Ds.Violet, green ? UI.Ds.Lime : rev > 0.92f ? UI.Ds.Red : UI.Ds.Cyan, 12);
            UI.Ds.T("LAUNCH  ·  HOLD THE REVS IN THE GREEN", 0.5f, y + 0.022f, 0.26f, UI.Ds.A(green ? UI.Ds.Lime : UI.Ds.Muted, 1f), UI.Ds.Display, Alignment.Center);
        }
        private void Launch()
        {
            if (rev >= GreenLo && rev <= GreenHi) { float c = 1f - Math.Abs(rev - (GreenLo + GreenHi) / 2f) / ((GreenHi - GreenLo) / 2f); launchGrade = c > 0.55f ? "PERFECT LAUNCH" : "GREAT LAUNCH"; launchPower = c > 0.55f ? 5.5f : 3.5f; }
            else if (rev > 0.92f) { launchGrade = "WHEELSPIN"; launchPower = 0f; }
            else if (rev > 0.45f) { launchGrade = "GOOD LAUNCH"; launchPower = 1.8f; }
            else { launchGrade = null; launchPower = 0f; }
            launchUntil = Game.GameTime + 1400;
            if (launchPower >= 5f) Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "MEDAL_UP", "HUD_MINI_GAME_SOUNDSET", true);
        }
        private void LaunchBoost(int now)
        {
            if (now > launchUntil) return;
            var car = Player.Car; if (car == null || !car.Exists()) return;
            if (launchPower > 0 && car.Speed < 45f) car.Velocity += car.ForwardVector * launchPower * UI.Ds.Dt;
            if (launchGrade != null)
            {
                float a = Math.Min(1f, (launchUntil - now) / 400f);
                var c = launchPower >= 5f ? UI.Ds.Lime : launchPower > 0 ? UI.Ds.Cyan : UI.Ds.Red;
                UI.Ds.T(launchGrade, 0.5f, 0.6f, 0.62f, UI.Ds.A(c, a), UI.Ds.Display, Alignment.Center);
            }
        }

        // ------------------------------------------------------------------ slipstream
        private float draft, draftFx;
        private void Slipstream()
        {
            var car = Player.Car; if (car == null || !car.Exists() || Player.Finished) return;
            float best = 0f;
            if (car.Speed > 18f)
            {
                var f = car.ForwardVector;
                foreach (var r in Racers)
                {
                    if (r.IsPlayer || r.Car == null || !r.Car.Exists()) continue;
                    var d = r.Car.Position - car.Position; float dist = d.Length();
                    if (dist < 3f || dist > 32f) continue;
                    float along = Vector3.Dot(d, f); if (along < 3f) continue;
                    float lateral = (d - f * along).Length();
                    if (lateral > 2.2f + dist * 0.04f) continue;
                    if (Vector3.Dot(Vector3.Normalize(r.Car.Velocity), f) < 0.85f) continue;
                    best = Math.Max(best, 1f - dist / 32f);
                }
            }
            draft = UI.Ds.Step(draft, best > 0 ? 1f : 0f, best > 0 ? 0.9f : 2.5f);
            if (draft > 0.25f && best > 0) car.Velocity += car.ForwardVector * (1.2f + 1.6f * best) * draft * UI.Ds.Dt;
            draftFx = UI.Ds.Damp(draftFx, draft > 0.25f && best > 0 ? 1f : 0f, 6f);
            if (draftFx > 0.02f)
            {
                UI.Ds.Streaks(0f, 0.1f, 1f, 0.8f, UI.Ds.Cyan, 0.35f * draftFx, 22, 2.2f);
                float w = UI.Ds.Px(0.16f), x = 0.5f - w / 2f, y = 0.86f;
                UI.Ds.Tag("SLIPSTREAM", x, y, UI.Ds.Cyan, UI.Ds.Bg, 0.26f, draftFx);
                UI.Ds.Rect(x, y + 0.03f, w, 0.005f, UI.Ds.A(UI.Ds.Stroke, draftFx));
                UI.Ds.Rect(x, y + 0.03f, w * draft, 0.005f, UI.Ds.A(UI.Ds.Cyan, draftFx));
            }
        }

        // ------------------------------------------------------------------ countdown + HUD
        private void Countdown(int left)
        {
            string s; float k; Color c;
            if (left > 0) { int sec = Math.Max(1, (int)Math.Ceiling(left / 1000.0)); if (sec > 3) return; s = sec.ToString(); k = 1f - (left % 1000) / 1000f; c = sec == 1 ? UI.Ds.Amber : UI.Ds.Text; }
            else { s = "GO"; k = Math.Min(1f, -left / 900f); c = UI.Ds.Lime; }
            float a = left > 0 ? Math.Min(1f, (1f - k) * 4f) * (k > 0.85f ? (1f - k) / 0.15f : 1f) : 1f - k;
            float sc = 2.6f + 0.8f * (1f - UI.Ds.EaseOut(Math.Min(1f, k * 3f)));
            UI.Ds.T(s, 0.5f, 0.24f, sc, UI.Ds.A(c, a), UI.Ds.Display, Alignment.Center);
            // start lights
            float lw = UI.Ds.Px(0.03f), gap = UI.Ds.Px(0.012f), total = 5 * lw + 4 * gap, lx = 0.5f - total / 2f;
            int lit = left > 0 ? Math.Min(5, 5 - (int)Math.Ceiling(left / 700.0) + 1) : 0;
            for (int i = 0; i < 5; i++)
            {
                var col = left <= 0 ? UI.Ds.A(UI.Ds.Lime, 1f - k) : i < lit ? UI.Ds.Red : UI.Ds.A(UI.Ds.Stroke, 0.9f);
                UI.Ds.Rect(lx + i * (lw + gap), 0.17f, lw, 0.03f, col);
            }
        }

        private void DrawHud()
        {
            var order = Standings();
            int pos = order.IndexOf(Player) + 1;
            int t = Running ? Game.GameTime - StartTime : 0;
            if (Player.Finished) t = Player.FinishTime;
            var acc = UI.Th.Accent;
            // race card (top right): event name, position, lap, time, checkpoint
            float cw = UI.Ds.Px(UI.Th.Px(300f)), xr = 0.97f, x = xr - cw, y = 0.04f;
            float h = UI.Th.Px(Def.HasOpponents ? 196f : 150f) + (Def.Lapped ? UI.Th.Px(0f) : 0f) + (Def.Scored ? UI.Th.Px(70f) : 0f);
            UI.Kit.Glass(x, y, cw, h, UI.Th.R_Lg + 2f);
            float ix = x + UI.Ds.Px(UI.Th.Px(20f)), ir = xr - UI.Ds.Px(UI.Th.Px(20f)), yy = y + UI.Th.Px(26f);
            UI.Ds.TM(UI.Ds.Fit(Def.Name.ToUpperInvariant(), 0.26f, cw - UI.Ds.Px(UI.Th.Px(40f)), UI.Ds.Display), ix, yy, 0.26f, acc, UI.Ds.Display);
            yy += UI.Th.Px(18f);
            if (Def.HasOpponents)
            {
                UI.Ds.T(pos.ToString(), ix, yy - UI.Th.Px(6f), 1.6f, UI.Th.Text, UI.Ds.Display);
                float pw = UI.Ds.Width(pos.ToString(), 1.6f, UI.Ds.Display);
                UI.Ds.TM(Ord(pos), ix + pw + UI.Ds.Px(UI.Th.Px(4f)), yy + UI.Th.Px(24f), 0.42f, acc, UI.Ds.Display);
                UI.Ds.TM("/ " + Racers.Count(r => !r.IsCrew), ix + pw + UI.Ds.Px(UI.Th.Px(4f)), yy + UI.Th.Px(52f), 0.36f, UI.Th.Sub, UI.Ds.Display);
                if (Def.Lapped) { UI.Ds.TM("LAP", ir, yy + UI.Th.Px(20f), 0.24f, UI.Th.Sub, UI.Ds.Display, Alignment.Right); UI.Ds.TM(Math.Min(Def.Laps, LapOf(Player)) + " / " + Def.Laps, ir, yy + UI.Th.Px(48f), 0.6f, UI.Th.Text, UI.Ds.Display, Alignment.Right); }
                yy += UI.Th.Px(86f);
            }
            else if (Def.Lapped) { UI.Ds.TM("LAP  " + Math.Min(Def.Laps, LapOf(Player)) + " / " + Def.Laps, ir, yy + UI.Th.Px(10f), 0.36f, UI.Th.Text, UI.Ds.Display, Alignment.Right); }
            UI.Ds.Rect(ix, yy, cw - UI.Ds.Px(UI.Th.Px(40f)), UI.Th.Px(1f), UI.Th.Line);
            yy += UI.Th.Px(30f);
            UI.Ds.TM(FormatTime(t), ix, yy, 0.62f, UI.Th.Text, UI.Ds.Display);
            UI.Kit.Chip("CP " + Math.Min(Player.Cp + 1, Cps.Count) + " / " + Cps.Count, ir - UI.Ds.Width("CP " + Math.Min(Player.Cp + 1, Cps.Count) + " / " + Cps.Count, 0.24f, UI.Ds.Display) - UI.Ds.Px(UI.Th.Px(16f)), yy, UI.Th.Track, UI.Th.Sub, 0.24f);
            yy += UI.Th.Px(34f);
            if (Def.Scored)
            {
                UI.Ds.Rect(ix, yy, cw - UI.Ds.Px(UI.Th.Px(40f)), UI.Th.Px(1f), UI.Th.Line);
                yy += UI.Th.Px(32f);
                UI.Ds.TM(((long)DriftScore).ToString("N0"), ix, yy, 0.66f, UI.Th.Amber, UI.Ds.Display);
                if (driftChain > 0) UI.Kit.Chip("+" + ((long)PendingDrift).ToString("N0") + "  x" + DriftCombo.ToString("0.0"), ir - UI.Ds.Width("+" + ((long)PendingDrift).ToString("N0") + "  x" + DriftCombo.ToString("0.0"), 0.28f, UI.Ds.Display) - UI.Ds.Px(UI.Th.Px(16f)), yy, UI.Th.Amber, UI.Th.On(UI.Th.Amber), 0.28f);
            }
            // route progress (top centre): every racer on one rounded track
            if (Running && Cps.Count > 1 && Def.Kind != RaceType.Drag)
            {
                float pw = UI.Ds.Px(0.42f), px = 0.5f - pw / 2f, py = 0.03f, ph = UI.Th.Px(34f);
                UI.Kit.Glass(px - UI.Ds.Px(UI.Th.Px(16f)), py, pw + UI.Ds.Px(UI.Th.Px(32f)), ph, ph * 1080f / 2f);
                float ty = py + ph / 2f - UI.Th.Px(3f);
                Func<Racer, float> frac = r =>
                {
                    if (r.Finished) return 1f;
                    int cp = Math.Min(r.Cp, Cps.Count - 1); var prev = cp > 0 ? Cps[cp - 1] : Def.Start;
                    float seg = Math.Max(1f, prev.DistanceTo(Cps[cp])), d = r.Car != null && r.Car.Exists() ? r.Car.Position.DistanceTo(Cps[cp]) : seg;
                    return Math.Max(0f, Math.Min(1f, (cp + Math.Max(0f, 1f - d / seg)) / Cps.Count));
                };
                UI.Kit.Bar(px, ty, pw, UI.Th.Px(6f), frac(Player), acc);
                foreach (var r in Racers) if (!r.IsPlayer && !r.Out) UI.Textures.Draw("disc", px + pw * frac(r), ty + UI.Th.Px(3f), UI.Th.Px(12f), r.IsCrew ? UI.Th.Cyan : UI.Th.Text);
                UI.Textures.Draw("disc", px + pw * frac(Player), ty + UI.Th.Px(3f), UI.Th.Px(18f), acc);
            }
            // standings tower (left)
            if (Def.HasOpponents && Racers.Count > 1)
            {
                float x0 = 0.03f, ty = 0.27f, w = UI.Ds.Px(UI.Th.Px(260f)), rh = UI.Th.Px(36f), pad = UI.Th.Px(8f);
                int n = Math.Min(8, order.Count);
                UI.Kit.Glass(x0, ty, w, n * rh + 2 * pad, UI.Th.R_Lg);
                for (int i = 0; i < n; i++)
                {
                    var r = order[i]; bool me = r.IsPlayer; float ry = ty + pad + i * rh, cy = ry + (rh - UI.Th.Px(4f)) / 2f;
                    float rx = x0 + UI.Ds.Px(pad), rw = w - 2 * UI.Ds.Px(pad);
                    if (me) UI.Kit.Round(rx, ry, rw, rh - UI.Th.Px(4f), UI.Th.R_Sm + 2f, acc);
                    var fg = me ? UI.Th.On(acc) : UI.Th.Text;
                    UI.Ds.TM((i + 1).ToString(), rx + UI.Ds.Px(UI.Th.Px(16f)), cy, 0.32f, me ? fg : UI.Th.Sub, UI.Ds.Display, Alignment.Center);
                    UI.Ds.TM(UI.Ds.Fit((me ? "YOU" : r.Name).ToUpperInvariant(), 0.3f, rw - UI.Ds.Px(UI.Th.Px(100f)), UI.Ds.Display), rx + UI.Ds.Px(UI.Th.Px(36f)), cy, 0.3f, fg, UI.Ds.Display);
                    string gap = r.Finished ? "FIN" : me ? "" : Gap(r);
                    if (gap != "") UI.Ds.TM(gap, rx + rw - UI.Ds.Px(UI.Th.Px(10f)), cy, 0.28f, me ? fg : UI.Th.Sub, UI.Ds.Display, Alignment.Right);
                }
            }
            if (quitHeld > 0) UI.Kit.Interact(Control.FrontendCancel, "Keep holding to quit the race");
        }

        private float seg = -1f;
        private string Gap(Racer r)
        {
            var p = Player;
            if (p.Car == null || !p.Car.Exists() || r.Car == null || !r.Car.Exists()) return "";
            if (seg < 0) { float L = 0; for (int i = 1; i < Lap.Count; i++) L += Lap[i].DistanceTo(Lap[i - 1]); seg = Math.Max(40f, L / Math.Max(1, Lap.Count - 1)); }
            float dp = p.Car.Position.DistanceTo(Cps[Math.Min(p.Cp, Cps.Count - 1)]), dr = r.Car.Position.DistanceTo(Cps[Math.Min(r.Cp, Cps.Count - 1)]);
            float d = (r.Cp - p.Cp) * seg + (dp - dr);   // > 0: opponent ahead
            float sec = -d / Math.Max(12f, p.Car.Speed);
            return (sec >= 0 ? "+" : "-") + Math.Abs(sec).ToString("0.0");
        }
        private static string Ord(int n) { return n == 1 ? "ST" : n == 2 ? "ND" : n == 3 ? "RD" : "TH"; }

        public static string FormatTime(int ms) { if (ms < 0) ms = 0; return (ms / 60000) + ":" + (ms / 1000 % 60).ToString("00") + "." + (ms % 1000 / 10).ToString("00"); }

        private static void Rect(float x, float y, float w, float h, Color c) { Function.Call(Hash.DRAW_RECT, x + w / 2f, y + h / 2f, w, h, c.R, c.G, c.B, c.A, false); }
        private static void Text(string s, float x, float y, float scale, Color c, Font f, Alignment a = Alignment.Left)
        { new TextElement(s, new PointF(x * 1280f, y * 720f), MidnightClub.UI.Ds.Eff(scale), c, f, a, true, true).ScaledDraw(); }

        // ------------------------------------------------------------------ cleanup
        public void Cleanup()
        {
            foreach (var pr in pillarProps) try { if (pr.Exists()) pr.Delete(); } catch { }
            pillarProps.Clear();
            if (Rival != null) Rival.Delete();
            if (finStart >= 0) EndCinematic();
            if (introCam != null) { Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0); try { introCam.Delete(); } catch { } introCam = null; }
            Cameras.CameraSystem.Suspended = false;
            if (Active == this) Active = null;
            ClearCheckpoint();
            foreach (var r in Racers)
            {
                if (r.IsPlayer) continue;
                try { if (r.Blip != null && r.Blip.Exists()) r.Blip.Delete(); } catch { }
                try { if (r.Ped != null && r.Ped.Exists()) r.Ped.Delete(); } catch { }
                try { if (r.Car != null && r.Car.Exists()) r.Car.Delete(); } catch { }
            }
            Freeze(false);
        }
    }
}
