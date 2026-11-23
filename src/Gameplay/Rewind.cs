using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.UI;
using MidnightClub.Vehicles;

namespace MidnightClub.Gameplay
{
    /// <summary>Anything besides the player's car that rewinds with it (a race: opponents, checkpoints, the clock).</summary>
    public interface IRewindable
    {
        object Capture();
        void Restore(object state, bool final);
    }

    /// <summary>Forza-style rewind: the last 12 seconds of driving are recorded 20 times a second. Hold the
    /// rewind button (R / controller B) to scrub back at 2x with a VHS-style effect; let go to carry on
    /// from that moment with the speed you had then.</summary>
    public sealed class Rewind
    {
        private struct Snap { public Vector3 Pos, Vel, Ang; public Quaternion Q; public float Rpm; public object[] Extra; }

        public static Rewind Instance;
        public static readonly List<IRewindable> Participants = new List<IRewindable>();
        /// <summary>True while the world is being scrubbed back (races pause their logic).</summary>
        public static bool Active { get; private set; }
        public static int UsedThisRace;

        private const int Hz = 20, Max = Hz * 12;
        private readonly VehicleManager vehicles;
        private readonly Config cfg;
        private readonly List<Snap> buf = new List<Snap>(Max + 4);
        private int lastRec, cooldownUntil, crashHintUntil, holdSince = -1;
        private float idx, fx;
        private Vehicle car;
        private float lastSpeed; private int lastSpeedT;
        public Func<bool> Blocked = () => false;

        public Rewind(VehicleManager vehicles, Config cfg)
        {
            this.vehicles = vehicles; this.cfg = cfg; Instance = this;
            EventBus.Subscribe<MidnightTickEvent>(e => { try { Tick(); } catch (Exception ex) { Log.Error("Rewind", ex); Stop(true); } });
            EventBus.Subscribe<ModeChangedEvent>(e => { Stop(true); buf.Clear(); });
        }

        public void Clear() { buf.Clear(); }

        private void Tick()
        {
            var ped = Game.Player.Character;
            var c = vehicles.Current;
            if (!cfg.Rewind || c == null || !c.Exists() || !ped.IsInVehicle(c) || c.IsDead) { if (Active) Stop(false); buf.Clear(); car = null; return; }
            if (c != car) { buf.Clear(); car = c; }
            Game.DisableControlThisFrame(Control.VehicleCinCam);
            int now = Game.GameTime;
            bool blocked = Blocked() || Police.PursuitManager.AnyActive || Game.IsPaused;
            bool hold = !blocked && Game.IsControlPressed(Control.VehicleCinCam);

            if (!Active)
            {
                if (hold && holdSince < 0) holdSince = now;
                if (!hold) holdSince = -1;
                if (hold && now > cooldownUntil && buf.Count > Hz / 2 && now - holdSince > 120) Begin();
                else
                {
                    if (now - lastRec >= 1000 / Hz) { Record(); lastRec = now; }
                    CrashHint(now, blocked);
                }
            }
            if (Active)
            {
                if (!hold || blocked) { Stop(false); return; }
                idx = Math.Max(0f, idx - Ds.Dt * Hz * 2f);
                Apply(buf[(int)idx], false);
                if (idx <= 0f) { Stop(false); return; }
            }
            fx = Ds.Damp(fx, Active ? 1f : 0f, Active ? 18f : 8f);
            if (fx > 0.01f) Overlay();
        }

        private void Record()
        {
            var s = new Snap { Pos = car.Position, Q = car.Quaternion, Vel = car.Velocity, Rpm = car.CurrentRPM };
            s.Ang = Function.Call<Vector3>(Hash.GET_ENTITY_ROTATION_VELOCITY, car);
            if (Participants.Count > 0) { s.Extra = new object[Participants.Count]; for (int i = 0; i < Participants.Count; i++) s.Extra[i] = Participants[i].Capture(); }
            buf.Add(s); if (buf.Count > Max) buf.RemoveAt(0);
        }

        private void Begin()
        {
            Active = true; idx = buf.Count - 1;
            Function.Call(Hash.FREEZE_ENTITY_POSITION, car, true);
            try { Function.Call(Hash.ANIMPOSTFX_PLAY, "MinigameTransitionIn", 0, true); } catch { }
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Hit_Out", "PLAYER_SWITCH_CUSTOM_SOUNDSET", true);
        }

        private void Apply(Snap s, bool final)
        {
            if (car == null || !car.Exists()) return;
            car.Position = s.Pos; car.Quaternion = s.Q;
            if (s.Extra != null) for (int i = 0; i < s.Extra.Length && i < Participants.Count; i++) { try { Participants[i].Restore(s.Extra[i], final); } catch (Exception ex) { Log.Error("Rewind participant", ex); } }
            if (final)
            {
                Function.Call(Hash.FREEZE_ENTITY_POSITION, car, false);
                car.Velocity = s.Vel;
                Function.Call(Hash.SET_ENTITY_ANGULAR_VELOCITY, car, s.Ang.X, s.Ang.Y, s.Ang.Z);
                Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, car, Math.Max(0f, Vector3.Dot(s.Vel, car.ForwardVector)));
                car.CurrentRPM = s.Rpm;
            }
        }

        private void Stop(bool hard)
        {
            if (!Active) return;
            Active = false; holdSince = -1;
            try { Function.Call(Hash.ANIMPOSTFX_STOP, "MinigameTransitionIn"); } catch { }
            if (!hard && buf.Count > 0)
            {
                int i = Math.Max(0, Math.Min(buf.Count - 1, (int)idx));
                Apply(buf[i], true);
                buf.RemoveRange(i + 1, buf.Count - i - 1);
                UsedThisRace++;
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Hit_In", "PLAYER_SWITCH_CUSTOM_SOUNDSET", true);
            }
            else if (car != null && car.Exists()) Function.Call(Hash.FREEZE_ENTITY_POSITION, car, false);
            cooldownUntil = Game.GameTime + 350; lastRec = Game.GameTime;
        }

        /// <summary>A hard hit pops a small "hold to rewind" prompt for a few seconds.</summary>
        private void CrashHint(int now, bool blocked)
        {
            float sp = car.Speed;
            if (now - lastSpeedT > 150)
            {
                if (lastSpeed - sp > 11f && lastSpeed > 18f && !blocked) crashHintUntil = now + 3500;
                lastSpeed = sp; lastSpeedT = now;
            }
            if (now < crashHintUntil)
            {
                float a = Math.Min(1f, (crashHintUntil - now) / 500f);
                float w = Ds.Px(0.17f), x = 0.5f - w / 2f, y = 0.8f;
                Ds.Rect(x, y, w, 0.042f, Ds.A(Ds.Bg, 0.85f * a));
                Ds.Rect(x, y, Ds.Px(0.004f), 0.042f, Ds.A(Ds.Cyan, a));
                Chevrons(x + Ds.Px(0.03f), y + 0.021f, 0.018f, Ds.A(Ds.Cyan, a));
                Ds.T("HOLD  R / B  TO REWIND", x + Ds.Px(0.055f), y + 0.009f, 0.34f, Ds.A(Ds.Text, a), Ds.Display);
            }
        }

        private static void Chevrons(float cx, float cy, float h, Color c)
        {
            // two left-pointing chevrons built from thin rects (pixel-snapped)
            for (int k = 0; k < 2; k++)
            {
                float x0 = cx - Ds.Px(h * 0.55f) * k;
                int steps = 7;
                for (int i = 0; i < steps; i++)
                {
                    float t = i / (float)(steps - 1), yy = cy - h / 2f + t * h, dx = Math.Abs(t - 0.5f) * 2f * Ds.Px(h * 0.5f);
                    Ds.Rect(x0 + dx - Ds.Px(h * 0.25f), yy - h / (steps * 2f), Ds.Px(h * 0.22f), h / steps + 0.0015f, c);
                }
            }
        }

        private void Overlay()
        {
            float a = fx;
            // vignette + scanlines
            Ds.HGrad(0f, 0f, 0.18f, 1f, Ds.A(Ds.Cyan, 0.22f * a), Ds.A(Ds.Cyan, 0f), 10);
            Ds.HGrad(0.82f, 0f, 0.18f, 1f, Ds.A(Ds.Cyan, 0f), Ds.A(Ds.Cyan, 0.22f * a), 10);
            int t = Game.GameTime;
            for (int i = 0; i < 3; i++) { float yy = ((t / 7 + i * 260) % 1000) / 1000f; Ds.Rect(0f, yy, 1f, 0.004f, Ds.A(Color.White, 0.08f * a)); }
            // title + timeline
            Chevrons(0.5f - Ds.Px(0.08f), 0.11f, 0.05f, Ds.A(Color.White, a));
            Ds.T("REWIND", 0.5f - Ds.Px(0.045f), 0.08f, 0.95f, Ds.A(Color.White, a), Ds.Display);
            float w = Ds.Px(0.32f), x = 0.5f - w / 2f, y = 0.16f;
            Ds.Rect(x, y, w, 0.006f, Ds.A(Ds.Stroke, a));
            float k = buf.Count > 1 ? idx / (buf.Count - 1) : 0f, filled = w * buf.Count / Max;
            Ds.Rect(x + w - filled, y, filled * k, 0.006f, Ds.A(Ds.Cyan, a));
            Ds.Rect(x + w - filled + filled * k - Ds.Px(0.003f), y - 0.008f, Ds.Px(0.006f), 0.022f, Ds.A(Color.White, a));
            Ds.T("-" + ((buf.Count - 1 - idx) / Hz).ToString("0.0") + " s", 0.5f, y + 0.014f, 0.3f, Ds.A(Ds.Muted, a), Ds.Display, GTA.UI.Alignment.Center);
        }
    }
}
