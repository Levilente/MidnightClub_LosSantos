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
            public string Archetype; public int Year;
            public float Spring = 1f, Damping = 1f, Travel = 1f, AntiRoll = 1f, Inertia = 1f;
            public float Grip, SlipAngle, Wheelspin, SteerLock, HighSpeedSteer, Stability, SpinAngle, DriftSustain, Downforce, AirControl, ComDrop, Brakes;
        }
        private sealed class Stock { public float TMax, TMin, TLat, LowLoss, Steer, Brake, SForce, SComp, SReb, SUp, SLow, Arb, RollF, RollR, BiasT, BiasB, Raise, FlatVel, DriveF, Mass, DriveBias, ArbBias, SuspBias, TLoss, MaxVel, DriveInertia, ClutchUp, ClutchDown; public int Gears; public Vector3 Com, Inertia; }

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
        // GTA VI-style tuning: heavier and more physical than GTA V (softer springs, more travel, more body roll,
        // more rotational inertia, less sticky tyres, longer braking), but still forgiving (light anti-spin,
        // speed-sensitive steering). Muscle and classic cars wallow, modern supercars stay tight.
        private static readonly Dictionary<string, float[]> Base = new Dictionary<string, float[]>
        {   //            grip   slip  spin   lock  hiSteer stab  spinAng sustain down  air   com    brake | spring damp  travel arb   inertia
            { "Super",   new[] { 1.02f, 11f, 0.90f, 1.00f, 0.62f, 0.55f, 30f, 0.00f, 0.45f, 0.40f, -0.03f, 1.05f, 0.95f, 0.95f, 1.00f, 0.90f, 1.04f } },
            { "AWD",     new[] { 1.00f, 11.5f, 0.70f, 1.00f, 0.65f, 0.50f, 32f, 0.00f, 0.20f, 0.40f, -0.02f, 1.00f, 0.92f, 0.90f, 1.05f, 0.80f, 1.04f } },
            { "Sports",  new[] { 0.97f, 12f, 1.15f, 1.02f, 0.66f, 0.40f, 38f, 0.05f, 0.15f, 0.35f, -0.02f, 1.00f, 0.90f, 0.88f, 1.08f, 0.75f, 1.04f } },
            { "Tuner",   new[] { 0.96f, 12.5f, 1.20f, 1.05f, 0.68f, 0.35f, 44f, 0.10f, 0.10f, 0.35f, -0.02f, 1.00f, 0.92f, 0.90f, 1.05f, 0.80f, 1.04f } },
            { "Drift",   new[] { 0.93f, 14f, 1.30f, 1.20f, 0.72f, 0.25f, 60f, 0.35f, 0.05f, 0.35f, -0.01f, 0.97f, 0.95f, 0.92f, 1.00f, 0.85f, 1.04f } },
            { "Muscle",  new[] { 0.92f, 14f, 1.75f, 0.97f, 0.63f, 0.33f, 42f, 0.10f, 0.00f, 0.30f, 0.02f, 0.86f, 0.80f, 0.70f, 1.22f, 0.55f, 1.04f } },
            { "Luxury",  new[] { 0.95f, 12f, 0.95f, 0.98f, 0.64f, 0.50f, 32f, 0.00f, 0.00f, 0.30f, 0.01f, 0.96f, 0.80f, 0.72f, 1.18f, 0.65f, 1.04f } },
            { "Classic", new[] { 0.95f, 14f, 1.20f, 0.98f, 0.65f, 0.40f, 40f, 0.05f, 0.00f, 0.30f, 0.00f, 0.95f, 0.92f, 0.90f, 1.05f, 0.85f, 1.04f } },
            { "FWD",     new[] { 0.97f, 11f, 0.90f, 1.00f, 0.66f, 0.50f, 34f, 0.00f, 0.00f, 0.35f, -0.02f, 0.98f, 0.88f, 0.85f, 1.10f, 0.75f, 1.04f } },
            { "Street",  new[] { 0.95f, 12.5f, 1.05f, 1.00f, 0.66f, 0.45f, 36f, 0.05f, 0.00f, 0.35f, -0.01f, 0.95f, 0.85f, 0.82f, 1.12f, 0.70f, 1.04f } },
            { "Heavy",   new[] { 0.92f, 12f, 0.90f, 0.95f, 0.62f, 0.55f, 30f, 0.00f, 0.00f, 0.30f, 0.00f, 0.90f, 0.82f, 0.80f, 1.15f, 0.60f, 1.04f } },
            { "Rally",   new[] { 0.95f, 13.5f, 1.05f, 1.03f, 0.68f, 0.40f, 46f, 0.15f, 0.00f, 0.45f, -0.01f, 0.95f, 0.85f, 0.85f, 1.25f, 0.70f, 1.04f } },
        };

        public Profile For(Vehicle car)
        {
            int hash = car.Model.Hash;
            Profile p; if (profiles.TryGetValue(hash, out p)) return p;
            var h = car.HandlingData;
            var info = registry.Get(hash);
            string arch = Classify(info, h);
            var b = Base[arch];
            int year = Era.Year(info); var ef = Era.Factors(year);
            p = new Profile { Year = year, Archetype = arch, Grip = b[0], SlipAngle = b[1], Wheelspin = b[2], SteerLock = b[3], HighSpeedSteer = b[4], Stability = b[5], SpinAngle = b[6], DriftSustain = b[7], Downforce = b[8], AirControl = b[9], ComDrop = b[10], Brakes = b[11], Spring = b[12], Damping = b[13], Travel = b[14], AntiRoll = b[15], Inertia = b[16] };
            // the era: old cars float and roll, modern ones sit flat and stop hard
            p.Grip *= ef[0]; p.Spring *= ef[1]; p.Damping *= ef[2]; p.Travel *= ef[3]; p.AntiRoll *= ef[4]; p.Brakes *= ef[5];
            p.SteerLock *= ef[6]; p.HighSpeedSteer *= ef[7]; p.Inertia *= ef[8]; p.ComDrop += ef[9]; p.Stability *= ef[10];
            p.SlipAngle += year < 1980 ? 2f : 0f;
            p.Spring = Math.Max(0.55f, p.Spring); p.Damping = Math.Max(0.48f, p.Damping); p.AntiRoll = Math.Max(0.3f, p.AntiRoll);
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
            Log.Info("Simcade profile " + Name(car) + " (" + Era.Label(p.Year) + "): " + p.Archetype + " grip " + p.Grip.ToString("0.00") + " slip " + p.SlipAngle.ToString("0.0") + " stab " + p.Stability.ToString("0.00"));
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
                case VehicleClass.Sedans: case VehicleClass.Coupes: if (!fwd && info != null && info.BasePrice > 90000) return "Luxury"; return fwd ? "FWD" : awd ? "AWD" : "Street";
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
                if (ini == null)
                {
                    ini = ScriptSettings.Load(file);
                    if (ini.GetValue("_meta", "Version", 0) < 4)
                    {
                        // 0.21: new GTA VI-style base tuning; old per-model values would override it
                        try { if (File.Exists(file)) File.Copy(file, file + ".v1.bak", true); File.Delete(file); } catch { }
                        ini = ScriptSettings.Load(file); ini.SetValue("_meta", "Version", 4); ini.Save();
                        Log.Info("simcade.ini reset for the 0.38 tyre model (old file kept as simcade.ini.v1.bak)");
                    }
                }
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
                p.Spring = g("Spring", p.Spring); p.Damping = g("Damping", p.Damping); p.Travel = g("Travel", p.Travel); p.AntiRoll = g("AntiRoll", p.AntiRoll); p.Inertia = g("Inertia", p.Inertia);
                if (dirty) { ini.Save(); dirty = false; }
            }
            catch (Exception ex) { Log.Error("simcade.ini", ex); }
            return p;
        }
        private bool dirty;

        // ------------------------------------------------------------------ handling (per model, only while driving)
        private string appliedTune;
        private void Apply(Vehicle car, Profile p, float compound, bool simcade, OwnedVehicle tune)
        {
            int hash = car.Model.Hash;
            string tk = (simcade ? "S" : "-") + (Drivetrain.Enabled ? "D" : "") + Setup.Key(tune) + "|" + Function.Call<int>(Hash.GET_VEHICLE_MOD, car, 13) + (tune != null && tune.Toggles != null && tune.Toggles.Contains(18) ? "T" : "");
            if (appliedModel == hash && appliedCompound == compound && appliedTune == tk) return;
            Restore();
            var h = car.HandlingData; if (h == null || !h.IsValid) return;
            Stock s; if (!stocks.TryGetValue(hash, out s)) stocks[hash] = s = new Stock { TMax = h.TractionCurveMax, TMin = h.TractionCurveMin, TLat = h.TractionCurveLateral, LowLoss = h.LowSpeedTractionLossMultiplier, Steer = h.SteeringLock, Brake = h.BrakeForce, Com = h.CenterOfMassOffset,
                SForce = h.SuspensionForce, SComp = h.SuspensionCompressionDamping, SReb = h.SuspensionReboundDamping, SUp = h.SuspensionUpperLimit, SLow = h.SuspensionLowerLimit,
                Arb = h.AntiRollBarForce, RollF = h.RollCenterHeightFront, RollR = h.RollCenterHeightRear, Inertia = h.InertiaMultiplier,
                BiasT = h.TractionBiasFront, BiasB = h.BrakeBiasFront, Raise = h.SuspensionRaise, FlatVel = h.InitialDriveMaxFlatVelocity, DriveF = h.InitialDriveForce,
                Mass = h.Mass, DriveBias = h.DriveBiasFront, ArbBias = h.AntiRollBarBiasFront, SuspBias = h.SuspensionBiasFront, TLoss = h.TractionLossMultiplier,
                MaxVel = h.InitialDriveMaxVelocity, DriveInertia = h.DriveInertia, ClutchUp = h.ClutchChangeRateScaleUpShift, ClutchDown = h.ClutchChangeRateScaleDownShift, Gears = h.InitialDriveGears };
            float k = simcade ? cfg.SimcadeIntensity : 0f;
            float grip = 1f + (p.Grip * compound - 1f) * k;
            h.TractionCurveMax = s.TMax * grip;
            h.TractionCurveMin = s.TMin * (1f + (p.Grip * compound * (p.Archetype == "Drift" ? 0.97f : 1f) - 1f) * k);
            h.TractionCurveLateral = Lerp(s.TLat, ToHandlingAngle(s.TLat, p.SlipAngle), k);
            h.LowSpeedTractionLossMultiplier = s.LowLoss * (1f + (p.Wheelspin - 1f) * k);
            h.BrakeForce = s.Brake * (1f + (p.Brakes - 1f) * k);
            h.CenterOfMassOffset = s.Com + new Vector3(0, 0, p.ComDrop * k);
            // weight: softer springs and dampers, more travel, more roll, more rotational inertia
            float wgt = k * (simcade ? cfg.SimcadeWeight : 0f);
            h.SuspensionForce = s.SForce * (1f + (p.Spring - 1f) * wgt);
            h.SuspensionCompressionDamping = s.SComp * (1f + (p.Damping - 1f) * wgt);
            h.SuspensionReboundDamping = s.SReb * (1f + (p.Damping - 1f) * wgt);
            h.SuspensionUpperLimit = s.SUp * (1f + (p.Travel - 1f) * wgt);
            h.SuspensionLowerLimit = s.SLow * (1f + (p.Travel - 1f) * wgt);
            h.AntiRollBarForce = s.Arb * (1f + (p.AntiRoll - 1f) * wgt);
            h.RollCenterHeightFront = s.RollF * (1f + (p.AntiRoll - 1f) * 0.5f * wgt);
            h.RollCenterHeightRear = s.RollR * (1f + (p.AntiRoll - 1f) * 0.5f * wgt);
            h.InertiaMultiplier = s.Inertia * (1f + (p.Inertia - 1f) * wgt);
            h.TractionBiasFront = s.BiasT; h.BrakeBiasFront = s.BiasB; h.SuspensionRaise = s.Raise; h.InitialDriveMaxFlatVelocity = s.FlatVel; h.InitialDriveForce = s.DriveF;
            h.Mass = s.Mass; h.DriveBiasFront = s.DriveBias; h.AntiRollBarBiasFront = s.ArbBias; h.SuspensionBiasFront = s.SuspBias; h.TractionLossMultiplier = s.TLoss;
            h.InitialDriveMaxVelocity = s.MaxVel; h.DriveInertia = s.DriveInertia; h.ClutchChangeRateScaleUpShift = s.ClutchUp; h.ClutchChangeRateScaleDownShift = s.ClutchDown; h.InitialDriveGears = s.Gears;
            // a touch more front grip in the balance: GTA cars push wide with softer, heavier simcade suspension
            if (simcade && s.DriveBias < 0.9f) h.TractionBiasFront = Math.Min(0.6f, s.BiasT + 0.025f * k);
            if (tune != null) Setup.Apply(h, tune);
            if (simcade && Drivetrain.Enabled)
            {
                var info = registry.Get(hash);
                var spec = Drivetrain.For(car, info, p.Archetype, p.Year, tune != null && tune.Toggles != null && tune.Toggles.Contains(18));
                Drivetrain.ApplyHandling(h, spec, tune, info, h.InitialDriveMaxFlatVelocity, s.DriveInertia, s.ClutchUp, s.ClutchDown, Function.Call<int>(Hash.GET_VEHICLE_MOD, car, 13));
            }
            appliedModel = hash; appliedCompound = compound; appliedTune = tk;
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
                { h.TractionCurveMax = s.TMax; h.TractionCurveMin = s.TMin; h.TractionCurveLateral = s.TLat; h.LowSpeedTractionLossMultiplier = s.LowLoss; h.SteeringLock = s.Steer; h.BrakeForce = s.Brake; h.CenterOfMassOffset = s.Com;
                  h.SuspensionForce = s.SForce; h.SuspensionCompressionDamping = s.SComp; h.SuspensionReboundDamping = s.SReb; h.SuspensionUpperLimit = s.SUp; h.SuspensionLowerLimit = s.SLow;
                  h.AntiRollBarForce = s.Arb; h.RollCenterHeightFront = s.RollF; h.RollCenterHeightRear = s.RollR; h.InertiaMultiplier = s.Inertia;
                  h.TractionBiasFront = s.BiasT; h.BrakeBiasFront = s.BiasB; h.SuspensionRaise = s.Raise; h.InitialDriveMaxFlatVelocity = s.FlatVel; h.InitialDriveForce = s.DriveF;
                  h.Mass = s.Mass; h.DriveBiasFront = s.DriveBias; h.AntiRollBarBiasFront = s.ArbBias; h.SuspensionBiasFront = s.SuspBias; h.TractionLossMultiplier = s.TLoss;
                  h.InitialDriveMaxVelocity = s.MaxVel; h.DriveInertia = s.DriveInertia; h.ClutchChangeRateScaleUpShift = s.ClutchUp; h.ClutchChangeRateScaleDownShift = s.ClutchDown; h.InitialDriveGears = s.Gears; }
            }
            catch (Exception ex) { Log.Error("Simcade restore", ex); }
            appliedModel = 0; appliedCompound = -1f; appliedTune = null;
            Drivetrain.Off();
        }

        // ------------------------------------------------------------------ live assists
        public Profile Current { get; private set; }
        public float SlipDeg { get; private set; }

        private void Tick()
        {
            var car = vehicles.Current; var rec = vehicles.CurrentRecord; var ped = Game.Player.Character;
            bool tuned = Setup.Any(rec);
            if ((!cfg.SimcadeEnabled && !tuned) || car == null || !car.Exists() || rec == null || !ped.IsInVehicle(car) || car.Driver != ped || car.Model.IsBike || car.Model.IsBicycle) { Restore(); Current = null; return; }
            try
            {
                var p = For(car); Current = cfg.SimcadeEnabled ? p : null;
                Apply(car, p, cfg.SimcadeEnabled ? (float)Math.Round(FitmentManager.CompoundGrip[Math.Max(0, Math.Min(4, rec.TyreCompound))] * WorldSim.Weather.GripMult * 50f) / 50f : 1f, cfg.SimcadeEnabled, rec);
                if (cfg.SimcadeEnabled) Assists(car, p);
                if (Drivetrain.Enabled)
                {
                    var spec = Drivetrain.For(car, registry.Get(car.Model.Hash), p.Archetype, p.Year, rec.Toggles != null && rec.Toggles.Contains(18));
                    Drivetrain.Tick(car, spec, rec, cfg.InversePower, Math.Min(0.05f, Game.LastFrameTime));
                }
                else Drivetrain.Off();
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
            if (cfg.SimcadeStability > 0.3f && abs > spin * 0.75f)
            {
                bool worsening = Math.Sign(wv.Z) == -Math.Sign(slip);   // nose rotating away from the direction of travel
                float over = Math.Min(1f, (abs - spin * 0.75f) / (spin * 0.5f));
                float damp = 1f - Math.Min(0.6f, p.Stability * cfg.SimcadeStability * k * over * 6f * dt);
                if (worsening) Function.Call(Hash.SET_ENTITY_ANGULAR_VELOCITY, car, wv.X, wv.Y, wv.Z * damp);
            }
            float throttle = Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, (int)Control.VehicleAccelerate);
            var nv = v;
            // (no drift sustain: holding a slide comes from the drivetrain - inverse power + differential)
            // downforce: grows with speed squared
            if (p.Downforce > 0f && speed > 25f)
                nv += new Vector3(0, 0, -p.Downforce * k * 3.5f * (speed / 60f) * (speed / 60f) * dt);
            if (nv != v) car.Velocity = nv;
        }
    }
}
