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
    }

    /// <summary>One race in progress (non-blocking: ticked every frame). Opponents drive checkpoint to
    /// checkpoint with the game's racing AI, with mild catch-up and un-stick help.</summary>
    public sealed class RaceSession
    {
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
            int laps = def.Kind == RaceType.Circuit ? Math.Max(1, def.Laps) : 1;
            for (int l = 0; l < laps; l++) Cps.AddRange(lap);
        }

        public int LapOf(Racer r) { return Math.Min(Def.Laps, r.Cp / Math.Max(1, Lap.Count) + 1); }

        // ------------------------------------------------------------------ setup
        public void AddPlayer(Vehicle car, Ped ped) { Racers.Add(new Racer { Name = "You", IsPlayer = true, Car = car, Ped = ped }); }

        public void SpawnOpponents(IList<string> names, IList<VehicleInfo> cars, Vector3 start, float heading, int seed)
        {
            var rnd = new Random(seed);
            string[] peds = { "a_m_y_hipster_01", "a_m_y_beach_01", "a_m_y_genstreet_01", "a_f_y_hipster_01", "a_m_y_ktown_01", "a_m_y_stwhi_01", "a_f_y_genhot_01", "a_m_y_vinewood_01" };
            double h = heading * Math.PI / 180.0;
            var fwd = new Vector3((float)-Math.Sin(h), (float)Math.Cos(h), 0); var right = new Vector3(fwd.Y, -fwd.X, 0);
            for (int i = 0; i < names.Count && i < cars.Count; i++)
            {
                int row = (i + 1) / 2, side = (i + 1) % 2 == 0 ? -1 : 1;
                var pos = start - fwd * (7.5f * row) + right * (i == 0 ? 3.2f : side * 3.2f);
                if (i == 0) pos = start + right * 3.4f;
                try
                {
                    var vm = new Model(cars[i].Hash); vm.Request(5000);
                    var car = World.CreateVehicle(vm, pos, heading); vm.MarkAsNoLongerNeeded();
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
                    var b = car.AddBlip(); b.Color = BlipColor.Red; b.Scale = 0.7f; b.Name = names[i]; b.IsShortRange = false;
                    Racers.Add(new Racer { Name = names[i], Ped = ped, Car = car, Blip = b, Skill = 0.92f + (float)rnd.NextDouble() * 0.16f });
                }
                catch (Exception ex) { Log.Error("Spawning opponent", ex); }
            }
        }

        public void Freeze(bool on)
        {
            foreach (var r in Racers) if (r.Car != null && r.Car.Exists()) Function.Call(Hash.FREEZE_ENTITY_POSITION, r.Car, on);
        }

        public void BeginCountdown() { CountdownEnd = Game.GameTime + 3500; Freeze(true); ShowCheckpoint(); }

        // ------------------------------------------------------------------ per frame
        public void Tick()
        {
            if (Done) return;
            int now = Game.GameTime;
            if (!Running)
            {
                int left = CountdownEnd - now;
                int sec = (int)Math.Ceiling(left / 1000.0);
                if (sec != lastBeep && sec >= 1 && sec <= 3) { lastBeep = sec; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "3_2_1", "HUD_MINI_GAME_SOUNDSET", true); }
                BigText(left > 0 ? Math.Max(1, sec).ToString() : "GO!");
                if (left <= 0)
                {
                    Running = true; StartTime = now; Freeze(false);
                    Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "GO", "HUD_MINI_GAME_SOUNDSET", true);
                    foreach (var r in Racers) if (!r.IsPlayer) Task(r);
                }
                DrawHud();
                return;
            }
            if (now - StartTime < 1200) BigText("GO!");

            // player
            var p = Player;
            if (!p.Finished && p.Car != null && p.Car.Exists())
            {
                float radius = p.Cp == Cps.Count - 1 ? 14f : 13f;
                if (p.Car.Position.DistanceTo(Cps[p.Cp]) < radius) Advance(p, now);
                if (Def.Kind == RaceType.Drift) Drift(p.Car);
            }

            // opponents
            foreach (var r in Racers)
            {
                if (r.IsPlayer || r.Finished || r.Car == null || !r.Car.Exists() || r.Ped == null || !r.Ped.Exists()) continue;
                if (r.Car.Position.DistanceTo(Cps[r.Cp]) < 18f) { Advance(r, now); if (!r.Finished) Task(r); }
                else if (now - r.LastTask > 4000) Task(r);
                CatchUp(r);
                Unstick(r, now);
            }

            // progress / ranking
            foreach (var r in Racers)
                if (!r.Finished && r.Car != null && r.Car.Exists())
                    r.Progress = r.Cp * 100000f - r.Car.Position.DistanceTo(Cps[Math.Min(r.Cp, Cps.Count - 1)]);

            // hold Backspace to quit
            Game.DisableControlThisFrame(Control.VehicleCinCam);
            if (Game.IsKeyPressed(System.Windows.Forms.Keys.Back) || Game.IsControlPressed(Control.FrontendCancel)) { if (quitHeld < 0) quitHeld = now; else if (now - quitHeld > 1500) { Aborted = true; Done = true; } }
            else quitHeld = -1;

            if (p.Finished)
            {
                bool allIn = Racers.All(r => r.Finished || r.Car == null || !r.Car.Exists());
                if (allIn || now - (StartTime + p.FinishTime) > 15000) Done = true;
            }
            DrawHud();
        }

        private void Advance(Racer r, int now)
        {
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
            cpBlip = World.CreateBlip(c); cpBlip.Color = BlipColor.Yellow2; cpBlip.ShowRoute = true; cpBlip.Name = last ? "Finish" : "Checkpoint";
            if (!last) { cpBlipNext = World.CreateBlip(next); cpBlipNext.Color = BlipColor.Yellow2; cpBlipNext.Scale = 0.6f; }
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
            return Racers.OrderByDescending(r => r.Finished ? 2e9f - r.FinishTime : r.Progress).ToList();
        }

        private void DrawHud()
        {
            var order = Standings();
            int pos = order.IndexOf(Player) + 1;
            int t = Running ? Game.GameTime - StartTime : 0;
            if (Player.Finished) t = Player.FinishTime;
            Rect(0.80f, 0.30f, 0.18f, Def.HasOpponents ? 0.22f : 0.17f, Panel);
            float y = 0.305f;
            if (Def.HasOpponents) { Text(pos + "/" + Racers.Count, 0.97f, y, 1.0f, Color.White, Font.Pricedown, Alignment.Right); y += 0.075f; }
            Text(Def.Name.ToUpperInvariant(), 0.97f, y, 0.3f, Purple, Font.ChaletLondon, Alignment.Right); y += 0.025f;
            if (Def.Kind == RaceType.Circuit) { Text("LAP " + Math.Min(Def.Laps, LapOf(Player)) + "/" + Def.Laps, 0.97f, y, 0.42f, Color.White, Font.ChaletComprimeCologne, Alignment.Right); y += 0.035f; }
            Text("TIME " + FormatTime(t), 0.97f, y, 0.42f, Color.White, Font.ChaletComprimeCologne, Alignment.Right); y += 0.035f;
            Text("CHECKPOINT " + Math.Min(Player.Cp + 1, Cps.Count) + "/" + Cps.Count, 0.97f, y, 0.32f, Color.FromArgb(220, 200, 200, 210), Font.ChaletLondon, Alignment.Right); y += 0.028f;
            if (Def.Kind == RaceType.Drift)
                Text("DRIFT " + ((long)DriftScore).ToString("N0") + (driftChain > 0 ? "  ~y~+" + ((long)PendingDrift).ToString("N0") + " x" + DriftCombo.ToString("0.0") : ""), 0.97f, y, 0.42f, Color.White, Font.ChaletComprimeCologne, Alignment.Right);
            if (quitHeld > 0) Text("Hold ~INPUT_FRONTEND_CANCEL~ to quit", 0.5f, 0.88f, 0.4f, Color.White, Font.ChaletLondon, Alignment.Center);
        }

        public static string FormatTime(int ms) { if (ms < 0) ms = 0; return (ms / 60000) + ":" + (ms / 1000 % 60).ToString("00") + "." + (ms % 1000 / 10).ToString("00"); }

        private static void BigText(string s) { Text(s, 0.5f, 0.32f, 2.2f, Color.FromArgb(255, 170, 90, 255), Font.Pricedown, Alignment.Center); }
        private static void Rect(float x, float y, float w, float h, Color c) { Function.Call(Hash.DRAW_RECT, x + w / 2f, y + h / 2f, w, h, c.R, c.G, c.B, c.A, false); }
        private static void Text(string s, float x, float y, float scale, Color c, Font f, Alignment a = Alignment.Left)
        { new TextElement(s, new PointF(x * 1280f, y * 720f), scale, c, f, a, true, true).ScaledDraw(); }

        // ------------------------------------------------------------------ cleanup
        public void Cleanup()
        {
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
