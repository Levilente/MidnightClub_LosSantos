using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.Persistence;

namespace MidnightClub.Vehicles
{
    /// <summary>Per-car simcade physics, built at runtime for every model you drive.
    /// 1) A profile is derived from the car's own stock handling (mass, drivetrain, power-to-weight, grip curve)
    ///    and its class/tags, giving it an archetype (Super, AWD grip, Sports, Tuner, Drift, Muscle, Classic, FWD,
    ///    Street, Heavy, Rally) plus a small per-model personality. Profiles are written to data\simcade.ini where
    ///    every value can be edited per model.
    /// 2) Handling is retuned for that model only while you drive it (and put back afterwards): grip level, slip
    ///    angle, low-speed wheelspin, steering lock, centre of mass, brakes; tyre compounds stack on top.
    /// 3) Live assists every frame: speed-sensitive steering, anti-spin yaw damping (drift cars allow far more
    ///    angle), drift sustain (less scrub while sliding on throttle), speed-squared downforce, airborne
    ///    stabilisation so jumps land flat.</summary>
    public sealed class Simcade
    {
        public static Simcade Instance { get; private set; }
        public static bool Active { get { return Instance != null && Instance.cfg.SimcadeEnabled; } }

        public sealed class Profile
        {
            public string Archetype;
            public float Grip, SlipAngle, Wheelspin, SteerLock, HighSpeedSteer, Stability, SpinAngle, DriftSustain, Downforce, AirControl, ComDrop, Brakes;
        }
        private sealed class Stock { public float TMax, TMin, TLat, LowLoss, Steer, Brake; public Vector3 Com; }

        private readonly VehicleManager vehicles; private readonly VehicleRegistry registry; private readonly Config cfg;
        private readonly string file;
        private readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private readonly Dictionary<int, Stock> stocks = new Dictionary<int, Stock>();
        private ScriptSettings ini;
        private int appliedModel; private float appliedCompound = -1f;

        public Simcade(VehicleManager vehicles, VehicleRegistry registry, Config cfg, string dataDir)
        {
            Instance = this; this.vehicles = vehicles; this.registry = registry; this.cfg = cfg;
            file = Path.Combine(dataDir, "simcade.ini");
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To != GameMode.MIDNIGHT_CLUB) Restore(); });
        }

        // ------------------------------------------------------------------ profiles
        private static readonly Dictionary<string, float[]> Base = new Dictionary<string, float[]>
        {   //            grip   slip  spin   lock  hiSteer stab  spinAng sustain down  air   com    brake
            { "Super",   new[] { 1.10f, 17f, 0.80f, 1.00f, 0.50f, 0.80f, 32f, 0.00f, 1.00f, 0.80f, -0.05f, 1.15f } },
            { "AWD",     new[] { 1.08f, 18f, 0.60f, 1.00f, 0.55f, 0.75f, 34f, 0.05f, 0.50f, 0.70f, -0.04f, 1.10f } },
            { "Sports",  new[] { 1.04f, 20f, 1.00f, 1.05f, 0.60f, 0.55f, 42f, 0.25f, 0.35f, 0.60f, -0.03f, 1.08f } },
            { "Tuner",   new[] { 1.00f, 22f, 1.10f, 1.10f, 0.62f, 0.45f, 52f, 0.45f, 0.20f, 0.60f, -0.03f, 1.05f } },
            { "Drift",   new[] { 0.96f, 26f, 1.25f, 1.25f, 0.70f, 0.30f, 68f, 0.80f, 0.10f, 0.60f, -0.02f, 1.00f } },
            { "Muscle",  new[] { 0.97f, 21f, 1.50f, 1.00f, 0.60f, 0.50f, 48f, 0.40f, 0.10f, 0.50f, -0.02f, 0.95f } },
            { "Classic", new[] { 0.95f, 20f, 1.10f, 1.00f, 0.60f, 0.50f, 45f, 0.20f, 0.00f, 0.50f, -0.02f, 0.90f } },
            { "FWD",     new[] { 1.02f, 17f, 0.80f, 1.00f, 0.58f, 0.70f, 36f, 0.00f, 0.10f, 0.60f, -0.03f, 1.05f } },
            { "Street",  new[] { 1.00f, 19f, 1.00f, 1.00f, 0.60f, 0.60f, 40f, 0.15f, 0.10f, 0.60f, -0.03f, 1.00f } },
            { "Heavy",   new[] { 0.98f, 17f, 0.90f, 0.95f, 0.55f, 0.80f, 32f, 0.00f, 0.00f, 0.50f, -0.05f, 1.00f } },
            { "Rally",   new[] { 1.00f, 22f, 1.00f, 1.05f, 0.62f, 0.50f, 50f, 0.40f, 0.00f, 0.70f, -0.02f, 1.00f } },
        };

        public Profile For(Vehicle car)
        {
            int hash = car.Model.Hash;
            Profile p; if (profiles.TryGetValue(hash, out p)) return p;
            var h = car.HandlingData;
            var info = registry.Get(hash);
            string arch = Classify(info, h);
            var b = Base[arch];
            p = new Profile { Archetype = arch, Grip = b[0], SlipAngle = b[1], Wheelspin = b[2], SteerLock = b[3], HighSpeedSteer = b[4], Stability = b[5], SpinAngle = b[6], DriftSustain = b[7], Downforce = b[8], AirControl = b[9], ComDrop = b[10], Brakes = b[11] };
            // the car's own numbers shape it further
            float mass = h.Mass, force = h.InitialDriveForce;
            float pw = force / Math.Max(0.1f, mass / 1500f);                     // relative power-to-weight
            if (pw > 0.35f) { p.Stability = Math.Min(1f, p.Stability + 0.1f); p.Wheelspin *= 1.1f; }
            if (mass > 2000f) { p.SteerLock *= 0.95f; p.HighSpeedSteer *= 0.95f; }
            if (mass < 1150f) { p.SteerLock *= 1.04f; p.Stability *= 0.92f; }
            // personality: deterministic per model
            var r = new Random(hash);
            p.Grip *= 0.97f + (float)r.NextDouble() * 0.06f;
            p.SlipAngle += (float)r.NextDouble() * 3f - 1.5f;
            p.DriftSustain = Math.Max(0f, p.DriftSustain + (float)r.NextDouble() * 0.1f - 0.05f);
            p = LoadOverrides(car, p);
            profiles[hash] = p;
            Log.Info("Simcade profile " + Name(car) + ": " + p.Archetype + " grip " + p.Grip.ToString("0.00") + " slip " + p.SlipAngle.ToString("0.0") + " stab " + p.Stability.ToString("0.00"));
            return p;
        }

        private static string Classify(VehicleInfo info, HandlingData h)
        {
            float drive = h.DriveBiasFront;
            bool awd = drive > 0.1f && drive < 0.9f, fwd = drive >= 0.9f;
            if (info != null && info.Drift) return "Drift";
            var c = info != null ? info.Class : VehicleClass.Sedans;
            switch (c)
            {
                case VehicleClass.Super: return awd ? "Super" : "Sports";
                case VehicleClass.Sports: return awd ? "AWD" : info != null && info.Tuner ? "Tuner" : "Sports";
                case VehicleClass.Muscle: return "Muscle";
                case VehicleClass.SportsClassics: return "Classic";
                case VehicleClass.OffRoad: return "Rally";
                case VehicleClass.SUVs: case VehicleClass.Vans: case VehicleClass.Industrial: case VehicleClass.Commercial: case VehicleClass.Utility: return "Heavy";
                default:
                    if (info != null && info.Tuner) return fwd ? "FWD" : awd ? "AWD" : "Tuner";
                    return fwd ? "FWD" : awd ? "AWD" : "Street";
            }
        }

        private string Name(Vehicle car) { var i = registry.Get(car.Model.Hash); return i != null ? i.Id : car.Model.Hash.ToString(); }

        private Profile LoadOverrides(Vehicle car, Profile p)
        {
            try
            {
                if (ini == null) ini = ScriptSettings.Load(file);
                string sec = Name(car);
                Func<string, float, float> g = (k, v) =>
                {
                    string s = ini.GetValue(sec, k, "");
                    float f; if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) return f;
                    ini.SetValue(sec, k, v.ToString("0.###", CultureInfo.InvariantCulture)); dirty = true; return v;
                };
                string a = ini.GetValue(sec, "Archetype", "");
                if (a.Length > 0 && Base.ContainsKey(a) && a != p.Archetype) { var b = Base[a]; p.Archetype = a; p.SpinAngle = b[6]; }
                else if (a.Length == 0) { ini.SetValue(sec, "Archetype", p.Archetype); dirty = true; }
                p.Grip = g("Grip", p.Grip); p.SlipAngle = g("SlipAngle", p.SlipAngle); p.Wheelspin = g("Wheelspin", p.Wheelspin);
                p.SteerLock = g("SteerLock", p.SteerLock); p.HighSpeedSteer = g("HighSpeedSteer", p.HighSpeedSteer);
                p.Stability = g("Stability", p.Stability); p.SpinAngle = g("SpinAngle", p.SpinAngle); p.DriftSustain = g("DriftSustain", p.DriftSustain);
                p.Downforce = g("Downforce", p.Downforce); p.AirControl = g("AirControl", p.AirControl); p.ComDrop = g("ComDrop", p.ComDrop); p.Brakes = g("Brakes", p.Brakes);
                if (dirty) { ini.Save(); dirty = false; }
            }
            catch (Exception ex) { Log.Error("simcade.ini", ex); }
            return p;
        }
        private bool dirty;

        // ------------------------------------------------------------------ handling (per model, only while driving)
        private void Apply(Vehicle car, Profile p, float compound)
        {
            int hash = car.Model.Hash;
            if (appliedModel == hash && appliedCompound == compound) return;
            Restore();
            var h = car.HandlingData; if (h == null || !h.IsValid) return;
            Stock s; if (!stocks.TryGetValue(hash, out s)) stocks[hash] = s = new Stock { TMax = h.TractionCurveMax, TMin = h.TractionCurveMin, TLat = h.TractionCurveLateral, LowLoss = h.LowSpeedTractionLossMultiplier, Steer = h.SteeringLock, Brake = h.BrakeForce, Com = h.CenterOfMassOffset };
            float k = cfg.SimcadeIntensity;
            float grip = 1f + (p.Grip * compound - 1f) * k;
            h.TractionCurveMax = s.TMax * grip;
            h.TractionCurveMin = s.TMin * (1f + (p.Grip * compound * (p.Archetype == "Drift" ? 0.97f : 1f) - 1f) * k);
            h.TractionCurveLateral = Lerp(s.TLat, ToHandlingAngle(s.TLat, p.SlipAngle), k);
            h.LowSpeedTractionLossMultiplier = s.LowLoss * (1f + (p.Wheelspin - 1f) * k);
            h.BrakeForce = s.Brake * (1f + (p.Brakes - 1f) * k);
            h.CenterOfMassOffset = s.Com + new Vector3(0, 0, p.ComDrop * k);
            appliedModel = hash; appliedCompound = compound;
        }

        /// <summary>GTA stores the lateral curve either in degrees or radians depending on the build; keep the stock unit.</summary>
        private static float ToHandlingAngle(float stock, float deg) { return stock > 3f ? deg : deg * (float)Math.PI / 180f; }
        private static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        public void Restore()
        {
            if (appliedModel == 0) return;
            try
            {
                Stock s; var h = HandlingData.GetByVehicleModel(new Model(appliedModel));
                if (h != null && h.IsValid && stocks.TryGetValue(appliedModel, out s))
                { h.TractionCurveMax = s.TMax; h.TractionCurveMin = s.TMin; h.TractionCurveLateral = s.TLat; h.LowSpeedTractionLossMultiplier = s.LowLoss; h.SteeringLock = s.Steer; h.BrakeForce = s.Brake; h.CenterOfMassOffset = s.Com; }
            }
            catch (Exception ex) { Log.Error("Simcade restore", ex); }
            appliedModel = 0; appliedCompound = -1f;
        }

        // ------------------------------------------------------------------ live assists
        public Profile Current { get; private set; }
        public float SlipDeg { get; private set; }

        private void Tick()
        {
            var car = vehicles.Current; var rec = vehicles.CurrentRecord; var ped = Game.Player.Character;
            if (!cfg.SimcadeEnabled || car == null || !car.Exists() || rec == null || !ped.IsInVehicle(car) || car.Driver != ped) { Restore(); Current = null; return; }
            try
            {
                var p = For(car); Current = p;
                Apply(car, p, FitmentManager.CompoundGrip[Math.Max(0, Math.Min(4, rec.TyreCompound))]);
                Assists(car, p);
            }
            catch (Exception ex) { Log.Error("Simcade", ex); cfg.SimcadeEnabled = false; Restore(); }
        }

        private void Assists(Vehicle car, Profile p)
        {
            float dt = Math.Min(0.05f, Game.LastFrameTime), k = cfg.SimcadeIntensity;
            var v = car.Velocity; float speed = v.Length();
            var h = car.HandlingData; Stock s;
            // speed-sensitive steering lock
            if (stocks.TryGetValue(car.Model.Hash, out s))
            {
                float t = Math.Min(1f, speed / 70f), keep = 1f - (1f - p.HighSpeedSteer) * cfg.SimcadeSteerAssist;
                h.SteeringLock = s.Steer * (1f + (p.SteerLock - 1f) * k) * Lerp(1f, keep, t * t * (3f - 2f * t));
            }
            if (car.IsInAir || !car.IsOnAllWheels && car.HeightAboveGround > 1.2f)
            {
                // airborne: settle pitch and roll so the car lands flat
                var w = car.WorldRotationVelocity; float damp = 1f - Math.Min(0.9f, p.AirControl * k * 4f * dt);
                Function.Call(Hash.SET_ENTITY_ANGULAR_VELOCITY, car, w.X * damp, w.Y * damp, w.Z);
                return;
            }
            if (speed < 4f) { SlipDeg = 0f; return; }
            var fwd = car.ForwardVector; var flatV = new Vector3(v.X, v.Y, 0); var flatF = new Vector3(fwd.X, fwd.Y, 0);
            if (flatV.Length() < 0.5f || flatF.Length() < 0.1f) return;
            flatV.Normalize(); flatF.Normalize();
            float cross = flatF.X * flatV.Y - flatF.Y * flatV.X;
            float slip = (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, Vector3.Dot(flatF, flatV)))) * 180.0 / Math.PI) * Math.Sign(cross);
            if (Vector3.Dot(flatF, flatV) < 0) { SlipDeg = 0f; return; }   // reversing
            SlipDeg = slip;
            float abs = Math.Abs(slip);
            var wv = car.WorldRotationVelocity;
            // anti-spin: past the car's spin angle, damp the yaw that is making the slide worse
            float spin = p.SpinAngle;
            if (abs > spin * 0.75f)
            {
                bool worsening = Math.Sign(wv.Z) == -Math.Sign(slip);   // nose rotating away from the direction of travel
                float over = Math.Min(1f, (abs - spin * 0.75f) / (spin * 0.5f));
                float damp = 1f - Math.Min(0.6f, p.Stability * cfg.SimcadeStability * k * over * 6f * dt);
                if (worsening) Function.Call(Hash.SET_ENTITY_ANGULAR_VELOCITY, car, wv.X, wv.Y, wv.Z * damp);
            }
            float throttle = Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, (int)Control.VehicleAccelerate);
            var nv = v;
            // drift sustain: while sliding on throttle, scrub less speed
            if (p.DriftSustain > 0f && abs > 12f && abs < spin && throttle > 0.3f && speed > 8f)
                nv += Vector3.Normalize(v) * (p.DriftSustain * k * 2.2f * throttle * dt);
            // downforce: grows with speed squared
            if (p.Downforce > 0f && speed > 25f)
                nv += new Vector3(0, 0, -p.Downforce * k * 3.5f * (speed / 60f) * (speed / 60f) * dt);
            if (nv != v) car.Velocity = nv;
        }
    }
}
