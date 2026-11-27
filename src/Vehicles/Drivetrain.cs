using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Persistence;

namespace MidnightClub.Vehicles
{
    /// <summary>Real drivetrain on top of GTA's: every car gets a gearbox (gear count, ratios, final drive), a redline,
    /// a tyre radius and a torque curve that fit its era and type. From these:
    ///  - the top-gear speed is the real one (redline / ratios / tyre), GTA's flat-velocity limiter and hard speed cap
    ///    are moved out of the way, so top speed comes from power against drag;
    ///  - the engine pulls along its torque curve (weak off idle, peak, fall-off near the redline), final drive and
    ///    per-gear ratios change the force at the wheels;
    ///  - "inverse power": GTA cuts drive force when the car is sideways; that cut is compensated (like the
    ///    InversePower mod), so you can hold a slide on the throttle;
    ///  - a differential model: locking under power makes both rear tyres break loose together (power oversteer),
    ///    an open diff loses drive to the inside wheel; deceleration lock decides how the car rotates off throttle.
    /// No yaw helpers or drift assists: the car only gets what its drivetrain would give it.</summary>
    public static class Drivetrain
    {
        public sealed class Spec
        {
            public int Gears; public float[] Ratios; public float Final, Idle = 850f, Redline, TyreR = 0.33f;
            public float Low, PeakAt, TopDrop; public bool Turbo;
            public float DriveInertia = 1f, ShiftRate = 1f, LockA, LockD;
            public float TopGearSpeed;        // m/s at the redline in top gear
            public string Kind;
        }
        private static readonly Dictionary<int, Spec> cache = new Dictionary<int, Spec>();
        public static Spec Current { get; private set; }
        /// <summary>Engine force multiplier for this frame (multiplied into the cheat-power multiplier).</summary>
        public static float TorqueMult = 1f;
        /// <summary>Real drivetrain switched on (config [Simcade] RealDrivetrain).</summary>
        public static bool Enabled { get { return Core.Config.Current != null && Core.Config.Current.SimcadeEnabled && Core.Config.Current.RealDrivetrain; } }
        public static float EngineRpm { get; private set; }

        private static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }

        public static Spec For(Vehicle car, VehicleInfo info, string arch, int year, bool turboKit)
        {
            int key = car.Model.Hash * 3 + (turboKit ? 1 : 0);
            Spec s; if (cache.TryGetValue(key, out s)) return s;
            s = new Spec { Kind = arch };
            var r = new Random(car.Model.Hash);
            // gearbox by era and type
            s.Gears = year < 1982 ? 4 : year < 1996 ? 5 : 6;      // GTA handles up to 6 forward gears safely
            if (arch == "Classic" && year < 1975) s.Gears = 4;
            float r1, rTop;
            switch (arch)
            {
                case "Super": s.Redline = 8500; r1 = 3.2f; rTop = 0.72f; s.Final = 3.4f; s.LockA = 0.45f; s.LockD = 0.35f; break;
                case "AWD": s.Redline = 7200; r1 = 3.4f; rTop = 0.78f; s.Final = 3.7f; s.LockA = 0.40f; s.LockD = 0.30f; break;
                case "Sports": s.Redline = 7500; r1 = 3.4f; rTop = 0.78f; s.Final = 3.7f; s.LockA = 0.40f; s.LockD = 0.30f; break;
                case "Tuner": s.Redline = 8000; r1 = 3.6f; rTop = 0.82f; s.Final = 4.1f; s.LockA = 0.40f; s.LockD = 0.25f; break;
                case "Drift": s.Redline = 7800; r1 = 3.6f; rTop = 0.85f; s.Final = 4.1f; s.LockA = 0.75f; s.LockD = 0.40f; break;
                case "Muscle": s.Redline = 6000; r1 = 2.9f; rTop = 0.70f; s.Final = 3.3f; s.LockA = 0.25f; s.LockD = 0.15f; break;
                case "Luxury": s.Redline = 6500; r1 = 3.5f; rTop = 0.70f; s.Final = 3.1f; s.LockA = 0.10f; s.LockD = 0.10f; break;
                case "Classic": s.Redline = 6000; r1 = 3.3f; rTop = 0.90f; s.Final = 3.7f; s.LockA = 0.15f; s.LockD = 0.10f; break;
                case "FWD": s.Redline = 6800; r1 = 3.6f; rTop = 0.80f; s.Final = 4.0f; s.LockA = 0.10f; s.LockD = 0.10f; break;
                case "Rally": s.Redline = 7500; r1 = 3.6f; rTop = 0.85f; s.Final = 4.3f; s.LockA = 0.55f; s.LockD = 0.30f; break;
                case "Heavy": s.Redline = 4800; r1 = 4.2f; rTop = 0.75f; s.Final = 3.7f; s.LockA = 0.10f; s.LockD = 0.10f; s.TyreR = 0.38f; break;
                default: s.Redline = 6500; r1 = 3.5f; rTop = 0.78f; s.Final = 3.9f; s.LockA = 0.10f; s.LockD = 0.10f; break;
            }
            if (year < 1970) s.Redline -= 500;
            if (arch == "Super") s.TyreR = 0.345f;
            s.Ratios = new float[s.Gears];
            for (int i = 0; i < s.Gears; i++)
            {
                float t = s.Gears == 1 ? 0f : (float)Math.Pow(i / (float)(s.Gears - 1), 0.9);
                s.Ratios[i] = r1 * (float)Math.Pow(rTop / r1, t);
            }
            // torque curve
            s.Turbo = turboKit || (year >= 2006 && (arch == "Super" || arch == "AWD" || arch == "Tuner" || arch == "Sports" || arch == "Street" || arch == "FWD"));
            switch (arch)
            {
                case "Muscle": s.Low = 0.82f; s.PeakAt = 0.45f; s.TopDrop = 0.30f; s.DriveInertia = 0.85f; break;
                case "Classic": s.Low = 0.70f; s.PeakAt = 0.55f; s.TopDrop = 0.35f; s.DriveInertia = 0.85f; break;
                case "Heavy": s.Low = 0.80f; s.PeakAt = 0.35f; s.TopDrop = 0.45f; s.DriveInertia = 0.75f; break;
                case "Super": s.Low = 0.60f; s.PeakAt = 0.70f; s.TopDrop = 0.12f; s.DriveInertia = 1.25f; break;
                case "Tuner": case "Drift": s.Low = 0.55f; s.PeakAt = 0.72f; s.TopDrop = 0.12f; s.DriveInertia = 1.15f; break;
                default: s.Low = 0.62f; s.PeakAt = 0.62f; s.TopDrop = 0.20f; s.DriveInertia = 1.0f; break;
            }
            if (s.Turbo) { s.Low = Math.Min(s.Low, 0.55f); s.PeakAt = 0.45f; s.TopDrop = 0.22f; }
            // shifts: old automatics are slow, modern dual-clutch boxes snap
            s.ShiftRate = year < 1980 ? 0.8f : year < 2000 ? 1.0f : year < 2012 ? 1.4f : 2.0f;
            if (arch == "Super") s.ShiftRate *= 1.4f;
            // personality
            s.Final *= 0.96f + (float)r.NextDouble() * 0.08f;
            // match the gearing to the car: the top gear must not be the limiter (top speed comes from power and drag)
            float est = info != null && info.MaxSpeed > 10f ? info.MaxSpeed : 50f;
            Func<float> vtop = () => s.Redline * 2f * (float)Math.PI / 60f * s.TyreR / (s.Ratios[s.Gears - 1] * s.Final);
            float want = est * 1.3f;
            if (vtop() < want * 0.9f || vtop() > want * 1.5f) s.Final = Clamp(s.Final * vtop() / want, 2.4f, 5.2f);
            s.TopGearSpeed = vtop();
            cache[key] = s;
            Core.Log.Info("Drivetrain " + (info != null ? info.Id : car.Model.Hash.ToString()) + ": " + s.Gears + " gears, final " + s.Final.ToString("0.00") + ", redline " + s.Redline + ", top gear " + (s.TopGearSpeed * 3.6f).ToString("0") + " km/h" + (s.Turbo ? ", turbo" : ""));
            return s;
        }

        /// <summary>Normalised engine torque at a GTA rpm fraction (0.2 idle .. 1.0 redline).</summary>
        public static float Torque(Spec s, float rpmFrac)
        {
            float x = Clamp((rpmFrac - 0.2f) / 0.8f, 0f, 1f);   // 0 idle .. 1 redline
            if (s.Turbo)
            {
                // spool to the plateau, hold, fall off at the top
                if (x < s.PeakAt) return s.Low + (1f - s.Low) * Smooth(x / s.PeakAt);
                if (x < 0.8f) return 1f;
                return 1f - s.TopDrop * Smooth((x - 0.8f) / 0.2f);
            }
            if (x < s.PeakAt) return s.Low + (1f - s.Low) * Smooth(x / s.PeakAt);
            return 1f - s.TopDrop * Smooth((x - s.PeakAt) / (1f - s.PeakAt));
        }
        private static float Smooth(float t) { t = Clamp(t, 0f, 1f); return t * t * (3f - 2f * t); }
        private static float Mean(Spec s) { float m = 0f; for (int i = 0; i <= 20; i++) m += Torque(s, 0.2f + 0.8f * i / 20f); return m / 21f; }

        // tuning hooks (Setup): final drive factor and per-gear ratio factors (shorter = higher number = more force)
        public static float FinalFactor(OwnedVehicle r) { return 1f - 0.02f * Setup.TuneValue(r, Setup.FinalDriveIndex); }
        public static float GearFactor(OwnedVehicle r, int gear) { return gear < 1 || gear > 6 ? 1f : 1f - 0.03f * Setup.TuneValue(r, Setup.GearIndex0 + gear - 1); }

        /// <summary>Handling side: gear count, top-gear speed (GTA's flat velocity) and the hard speed cap, engine inertia,
        /// shift speed. Called by Simcade.Apply with the stock values.</summary>
        public static void ApplyHandling(HandlingData h, Spec s, OwnedVehicle r, VehicleInfo info, float stockFlat, float stockDI, float stockUp, float stockDown, int transmissionLevel)
        {
            float est = info != null && info.MaxSpeed > 10f ? info.MaxSpeed : 50f;
            float vtop = s.TopGearSpeed / FinalFactor(r) / GearFactor(r, s.Gears);
            h.InitialDriveGears = s.Gears;
            h.InitialDriveMaxFlatVelocity = stockFlat * vtop / est;
            h.InitialDriveMaxVelocity = h.InitialDriveMaxFlatVelocity * 1.6f;     // the hard cap stays well clear: drag decides
            h.DriveInertia = stockDI * s.DriveInertia;
            float shift = s.ShiftRate * (1f + 0.35f * Math.Max(0, transmissionLevel + 1));
            h.ClutchChangeRateScaleUpShift = stockUp * shift; h.ClutchChangeRateScaleDownShift = stockDown * shift;
        }

        private static float yawKick;
        /// <summary>Per frame while driving: torque curve, final drive / gear ratios, open-diff loss, inverse power and the
        /// differential's yaw contribution.</summary>
        public static void Tick(Vehicle car, Spec s, OwnedVehicle r, bool inversePower, float dt)
        {
            Current = s;
            if (car.HighGear != s.Gears) { try { car.HighGear = s.Gears; } catch { } }
            float rpm = car.CurrentRPM, thr = Math.Max(0f, car.Throttle);
            EngineRpm = s.Idle + Clamp((rpm - 0.2f) / 0.8f, 0f, 1f) * (s.Redline - s.Idle);
            int gear = Math.Max(1, Math.Min(s.Gears, car.CurrentGear));
            float mult = Torque(s, rpm) / Mean(s) * FinalFactor(r) * GearFactor(r, gear);
            var h = car.HandlingData;
            float rear = 1f - Clamp(h.DriveBiasFront, 0f, 1f);
            bool diffPart = Setup.Has(r, "diff");
            float lockA = diffPart ? Clamp((50f + 5f * Setup.TuneValue(r, 18)) / 100f, 0f, 1f) : s.LockA;
            float lockD = diffPart ? Clamp((25f + 2.5f * Setup.TuneValue(r, 19)) / 100f, 0f, 1f) : s.LockD;
            var v = car.Velocity; float speed = v.Length();
            var fwd = car.ForwardVector; var wv = car.WorldRotationVelocity;
            float latG = Math.Abs(speed * wv.Z) / 9.81f;
            // open diff: in a hard corner the unloaded inside wheel spins and eats drive
            if (speed > 5f && thr > 0.2f) mult *= 1f - (1f - lockA) * Clamp(latG - 0.4f, 0f, 0.8f) * 0.35f * thr;
            TorqueMult = Clamp(mult, 0.25f, 2.5f);
            if (car.IsInAir || speed < 3f) return;
            var fv = new Vector3(fwd.X, fwd.Y, 0); var vv = new Vector3(v.X, v.Y, 0);
            if (fv.Length() < 0.1f || vv.Length() < 1f) return;
            fv.Normalize(); vv.Normalize();
            float cos = Vector3.Dot(fv, vv); if (cos < 0.2f) return;          // reversing or spinning out
            float angle = (float)Math.Acos(Clamp(cos, -1f, 1f));
            float fwdSpeed = Vector3.Dot(v, fwd);
            float wheelspin = Clamp((car.WheelSpeed - fwdSpeed) / Math.Max(4f, fwdSpeed), 0f, 1f);
            var nv = v;
            // inverse power: GTA scales drive force down with the slide angle; give it back on the driven axle
            if (inversePower && thr > 0.05f && rear > 0.3f && angle > 0.08f)
            {
                float baseAccel = h.InitialDriveForce * 9.81f * TorqueMult * (s.Ratios[gear - 1] / s.Ratios[0]) * thr;
                float comp = 1f / Math.Max(0.35f, cos) - 1f;
                nv += fwd * (baseAccel * Clamp(comp, 0f, 1.6f) * rear * 0.9f * dt);
            }
            if (nv != v) car.Velocity = nv;
            // differential yaw: a locked rear axle spinning both tyres lets the rear step out under power;
            // decel lock calms or frees the rear when you lift
            float dir = Math.Abs(wv.Z) > 0.05f ? Math.Sign(wv.Z) : Math.Sign(car.SteeringAngle);
            float yaw = 0f;
            if (rear > 0.3f && thr > 0.4f && wheelspin > 0.06f)
                yaw += dir * lockA * rear * thr * Math.Min(1f, wheelspin * 3f) * 1.1f;
            else if (thr < 0.1f && speed > 12f && latG > 0.3f)
                yaw += dir * (0.5f - lockD) * Math.Min(1f, latG) * 0.35f;
            yawKick += (yaw - yawKick) * Math.Min(1f, dt * 8f);
            if (Math.Abs(yawKick) > 0.005f)
                Function.Call(Hash.SET_ENTITY_ANGULAR_VELOCITY, car, wv.X, wv.Y, wv.Z + yawKick * dt);
        }

        public static void Off() { Current = null; TorqueMult = 1f; yawKick = 0f; }
    }
}
