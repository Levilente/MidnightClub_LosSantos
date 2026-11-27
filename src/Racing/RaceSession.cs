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
                    var b = UI.BlipRegistry.Own(car.AddBlip()); b.Color = BlipColor.Red; b.Scale = 0.7f; b.Name = names[i]; b.IsShortRange = false;
                    Racers.Add(new Racer { Name = names[i], Ped = ped, Car = car, Blip = b, Skill = 0.92f + (float)rnd.NextDouble() * 0.16f });
                }
                catch (Exception ex) { Log.Error("Spawning opponent", ex); }
            }
        }

        public void Freeze(bool on)
        {
            foreach (var r in Racers) if (r.Car != null && r.Car.Exists()) Function.Call(Hash.FREEZE_ENTITY_POSITION, r.Car, on);
        }

        public void BeginCountdown()
        {
            IntroEnd = Game.GameTime + 4600; CountdownEnd = IntroEnd + 3500; Freeze(true); ShowCheckpoint();
            Active = this;
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
                }
                DrawHud();
                return;
            }
            if (now - StartTime < 900) Countdown(now - StartTime > 0 ? -(now - StartTime) : 0);
            LaunchBoost(now);
            Slipstream();
            if (Def.Kind == RaceType.Rally) PaceNotes();
            if (!Player.Finished && Player.Car != null && Player.Car.Exists()) { Recording.Model = Player.Car.Model.Hash; Recording.Record(Player.Car, now - StartTime); }
            if (Rival != null) Rival.Play(now - StartTime);
            DrawSplit(now);

            // player
            var p = Player;
            if (!p.Finished && p.Car != null && p.Car.Exists())
            {
                float radius = p.Cp == Cps.Count - 1 ? 14f : 13f;
                if (p.Car.Position.DistanceTo(Cps[p.Cp]) < radius) Advance(p, now);
                if (Def.Kind == RaceType.Drift) Drift(p.Car);
            }

            Takedowns(now);

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
            return Racers.OrderByDescending(r => r.Finished ? 2e9f - r.FinishTime : r.Progress).ToList();
        }

        private void DrawSplit(int now)
        {
            if (!splitValid || now - splitShownAt > 2600) return;
            float a = Math.Min(1f, (2600 - (now - splitShownAt)) / 400f);
            bool ahead = splitDelta <= 0;
            string s = (ahead ? "-" : "+") + (Math.Abs(splitDelta) / 1000f).ToString("0.00");
            float w = UI.Ds.Px(0.13f), x = 0.5f - w / 2f, y = 0.2f;
            UI.Ds.Rect(x, y, w, 0.05f, UI.Ds.A(UI.Ds.Bg, 0.8f * a));
            UI.Ds.Rect(x, y + 0.046f, w, 0.004f, UI.Ds.A(ahead ? UI.Ds.Lime : UI.Ds.Red, a));
            UI.Ds.T("GHOST", x + UI.Ds.Px(0.01f), y + 0.006f, 0.22f, UI.Ds.A(UI.Ds.Muted, a), UI.Ds.Display);
            UI.Ds.T(s, x + w - UI.Ds.Px(0.01f), y + 0.004f, 0.55f, UI.Ds.A(ahead ? UI.Ds.Lime : UI.Ds.Red, a), UI.Ds.Display, Alignment.Right);
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
                return Def.Type == "Rally" ? "RALLY STAGE" : Def.Type == "Drag" ? "DRAG RACE" : Def.Type == "Elimination" ? "ELIMINATION" : "SPRINT";
            }
        }
        private void Intro(int now)
        {
            var car = Player.Car; if (car == null || !car.Exists()) return;
            float t = 1f - (IntroEnd - now) / 4600f;
            if (introCam == null)
            {
                if (Cameras.CameraSystem.Instance != null) Cameras.CameraSystem.Instance.Release();
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
            string info = (introLen / 1000f).ToString("0.0") + " KM" + (Def.Kind == RaceType.Circuit ? "  ·  " + Def.Laps + " LAPS" : "") + (Racers.Count > 1 ? "  ·  " + (Racers.Count - 1) + (Racers.Count == 2 ? " OPPONENT" : " OPPONENTS") : "") + "  ·  $" + Def.CashReward.ToString("N0") + " TO THE WINNER";
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
            IntroEnd = 0;
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
                if (r.IsPlayer || r.Car == null || !r.Car.Exists() || r.Finished) continue;
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
            finCam = null;
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
            float xr = 0.965f, y = 0.05f;
            // position block
            if (Def.HasOpponents)
            {
                UI.Ds.T(pos.ToString(), xr - UI.Ds.Px(0.055f), y - 0.01f, 1.5f, UI.Ds.Text, UI.Ds.Display, Alignment.Right);
                UI.Ds.T(Ord(pos), xr - UI.Ds.Px(0.052f), y + 0.012f, 0.42f, UI.Ds.Violet, UI.Ds.Display);
                UI.Ds.T("/" + Racers.Count, xr, y + 0.05f, 0.42f, UI.Ds.Muted, UI.Ds.Display, Alignment.Right);
                y += 0.12f;
            }
            UI.Ds.T(Def.Name.ToUpperInvariant(), xr, y, 0.26f, UI.Ds.Violet, UI.Ds.Display, Alignment.Right); y += 0.026f;
            if (Def.Kind == RaceType.Circuit) { UI.Ds.T("LAP  " + Math.Min(Def.Laps, LapOf(Player)) + " / " + Def.Laps, xr, y, 0.44f, UI.Ds.Text, UI.Ds.Display, Alignment.Right); y += 0.036f; }
            UI.Ds.T(FormatTime(t), xr, y, 0.5f, UI.Ds.Text, UI.Ds.Display, Alignment.Right); y += 0.04f;
            UI.Ds.T("CHECKPOINT  " + Math.Min(Player.Cp + 1, Cps.Count) + " / " + Cps.Count, xr, y, 0.26f, UI.Ds.Muted, UI.Ds.Display, Alignment.Right); y += 0.03f;
            if (Def.Kind == RaceType.Drift)
            {
                UI.Ds.T(((long)DriftScore).ToString("N0"), xr, y, 0.6f, UI.Ds.Amber, UI.Ds.Display, Alignment.Right); y += 0.045f;
                if (driftChain > 0) UI.Ds.T("+" + ((long)PendingDrift).ToString("N0") + "  x" + DriftCombo.ToString("0.0"), xr, y, 0.36f, UI.Ds.Text, UI.Ds.Display, Alignment.Right);
            }
            // standings tower (left)
            if (Def.HasOpponents && Racers.Count > 1)
            {
                float x = 0.03f, ty = 0.28f, w = UI.Ds.Px(0.19f), rh = 0.03f;
                for (int i = 0; i < order.Count && i < 8; i++)
                {
                    var r = order[i]; bool me = r.IsPlayer;
                    UI.Ds.Rect(x, ty + i * rh, w, rh - 0.003f, me ? UI.Ds.A(UI.Ds.Violet, 0.92f) : UI.Ds.A(UI.Ds.Bg, 0.72f));
                    UI.Ds.Rect(x, ty + i * rh, UI.Ds.Px(0.026f), rh - 0.003f, me ? UI.Ds.A(Color.White, 0.18f) : UI.Ds.A(UI.Ds.Surface2, 0.9f));
                    UI.Ds.T((i + 1).ToString(), x + UI.Ds.Px(0.013f), ty + i * rh + 0.004f, 0.3f, Color.White, UI.Ds.Display, Alignment.Center);
                    UI.Ds.T(UI.Ds.Fit((me ? "YOU" : r.Name).ToUpperInvariant(), 0.28f, w - UI.Ds.Px(0.09f), UI.Ds.Display), x + UI.Ds.Px(0.034f), ty + i * rh + 0.005f, 0.28f, me ? Color.White : UI.Ds.Text, UI.Ds.Display);
                    string gap = r.Finished ? "FIN" : me ? "" : Gap(r);
                    if (gap != "") UI.Ds.T(gap, x + w - UI.Ds.Px(0.008f), ty + i * rh + 0.005f, 0.26f, me ? Color.White : UI.Ds.Muted, UI.Ds.Display, Alignment.Right);
                }
            }
            if (quitHeld > 0) Screen.ShowHelpTextThisFrame("Keep holding ~INPUT_FRONTEND_CANCEL~ to quit the race.");
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
        { new TextElement(s, new PointF(x * 1280f, y * 720f), scale, c, f, a, true, true).ScaledDraw(); }

        // ------------------------------------------------------------------ cleanup
        public void Cleanup()
        {
            if (Rival != null) Rival.Delete();
            if (finStart >= 0) EndCinematic();
            if (introCam != null) { Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0); try { introCam.Delete(); } catch { } introCam = null; }
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
