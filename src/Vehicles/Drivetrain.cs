using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GTA;
using GTA.Native;
using MidnightClub.Persistence;
using MidnightClub.Physics;

namespace MidnightClub.Vehicles
{
    /// <summary>The powertrain of the car you drive, computed from real data: engine torque curve with boost lag, gear
    /// ratios and final drive, drivetrain losses, rotating inertia. Each frame the force the real engine would put on
    /// the road (torque x overall ratio x efficiency / tyre radius) becomes an acceleration target, and GTA's engine is
    /// driven to exactly that through its power multiplier. How much acceleration GTA gives per unit multiplier in each
    /// gear is measured while you drive (and saved per model), so the result follows the real figures, not GTA's.
    /// Gear spacing: GTA's gear ranges are re-scaled per gear (through the flat-velocity value) so every gear ends at
    /// the real redline speed of the real ratio.</summary>
    public static class Drivetrain
    {
        public sealed class Live
        {
            public string Key, CalKey; public CarSpec Spec; public EngineState Engine; public Gearing Gear; public Tyres.Mf Tyre; public PerfResult Perf;
            public float Mass, Cda, Cla, AeroFront, S0Top = 1f;   // S0Top: first guess of GTA's top-gear speed per flat-velocity unit
            public int Gears { get { return Gear.Gears; } }
            public float[] Ratios { get { return Gear.Ratios; } }
            public float Final { get { return Gear.Final; } }
            public float Idle { get { return Spec.Idle; } }
            public float Redline { get { return Spec.Redline; } }
        }

        public static Live Current { get; private set; }
        /// <summary>Power multiplier for this frame (GTA's cheat-power value).</summary>
        public static float TorqueMult = 1f;
        public static float EngineRpm { get; private set; }
        public static float BoostBar { get { return Current != null ? Current.Engine.BoostNowBar : 0f; } }
        public static bool HasBoost { get { return Current != null && Current.Engine.BoostBar > 0.01f; } }
        public static float AccelDemand { get; private set; }      // m/s2 the engine asks for at full throttle in this gear
        public static float TcsCut, EscCut;                          // set by the assists (0..1)
        public static bool Enabled { get { return true; } }

        private static readonly Dictionary<string, Live> cache = new Dictionary<string, Live>();

        public static Live Build(Vehicle car, OwnedVehicle r, CarSpec s, int compound, float stockMass)
        {
            return BuildWith(car, r, s, compound, stockMass, Math.Max(1, car.HighGear > 0 ? car.HighGear : s.Gears));
        }
        private static Live BuildWith(Vehicle car, OwnedVehicle r, CarSpec s, int compound, float stockMass, int gears)
        {
            int eng = Function.Call<int>(Hash.GET_VEHICLE_MOD, car, 11);
            string key = car.Model.Hash + "|" + Setup.Key(r) + "|" + compound + "|" + eng + "|" + gears + "|" + (r != null ? r.EcuLevel + "|" + r.EngineSound : "");
            Live L; if (cache.TryGetValue(key, out L)) return L;
            L = new Live { Key = key, Spec = s };
            bool gtaTurbo = Engine.TurboStage(r) > 0 || (s.Ind == Induction.Turbo && Engine.ScStage(r) == 0);
            L.CalKey = s.Id + "|e" + eng + (gtaTurbo ? "|t" : "");   // GTA's own engine level and turbo change its response
            L.Engine = Engine.Build(s, r, eng);
            float w = Setup.Has(r, "weight3") ? 0.88f : Setup.Has(r, "weight2") ? 0.92f : Setup.Has(r, "weight1") ? 0.96f : 1f;
            L.Mass = (s.Curated ? s.Kg : stockMass) * w + (s.Bike ? 75f : 0f);
            L.Tyre = Tyres.ForCompound(compound, s.Tyre, s.Bike);
            L.Gear = Gearing.Build(s, L.Engine, r, gears, L.Mass, L.Tyre);
            // aero: the adjustable kit (splitter + wing) adds downforce, balance and drag
            L.Cda = s.CdA; L.Cla = s.ClA; L.AeroFront = s.AeroFront;
            if (Setup.Has(r, "aero"))
            {
                float fk = 60 + (Setup.TuneValue(r, 14) + 10) * 6, rk = 90 + (Setup.TuneValue(r, 15) + 10) * 9;   // kgf at 200 km/h
                float q = 0.5f * Perf.Rho * 55.56f * 55.56f;
                float cf = fk * 9.81f / q, cr = rk * 9.81f / q;
                L.Cla = Math.Max(0f, s.ClA) * 0.5f + cf + cr; L.AeroFront = cf / (cf + cr); L.Cda = s.CdA + 0.3f * (cf + cr);
            }
            L.Perf = Perf.Run(s, L.Engine, L.Gear, L.Mass, L.Cda, L.Cla, L.AeroFront, L.Tyre, Setup.Has(r, "awd") ? 0.35f : Setup.Has(r, "rwd") ? 0f : s.FrontDrive, s.FrontPct, s.CgH, s.Year);
            if (cache.Count > 64) cache.Clear();
            cache[key] = L;
            Core.Log.Info("Powertrain " + s.Id + ": " + L.Engine.PeakHp.ToString("0") + " hp @" + L.Engine.PeakHpRpm.ToString("0") + ", " + L.Engine.PeakNm.ToString("0") + " Nm @" + L.Engine.PeakNmRpm.ToString("0")
                + ", " + L.Engine.InductionLabel + ", " + L.Gear.Gears + " gears, final " + L.Gear.Final.ToString("0.00") + ", " + L.Mass.ToString("0") + " kg, 0-100 " + L.Perf.T100.ToString("0.0") + " s, top " + L.Perf.TopKmh.ToString("0") + " km/h, tyres " + L.Tyre.Name);
            return L;
        }

        /// <summary>Real data for any vehicle on screen (showroom, garage): the gearbox it will have when it's yours.</summary>
        public static Live ForDisplay(Vehicle car, OwnedVehicle r)
        {
            try
            {
                if (car == null || !car.Exists()) return null;
                var s = RealData.For(car);
                var h = car.HandlingData;
                int stockGears = Physics.SimPhysics.Instance != null ? Physics.SimPhysics.Instance.StockGears(car) : h.InitialDriveGears;
                int gears = SpawnGears(r, stockGears, RealData.PlannedGears(car.Model.Hash, stockGears));
                float mass = Physics.SimPhysics.Instance != null ? Physics.SimPhysics.Instance.StockMass(car) : h.Mass;
                return BuildWith(car, r, s, r != null ? Math.Max(0, Math.Min(5, r.TyreCompound)) : 0, mass, gears);
            }
            catch (Exception ex) { Core.Log.Error("Spec display", ex); return null; }
        }

        // ------------------------------------------------------------------ calibration (measured per model, saved)
        private sealed class Cal { public float[] A = new float[12], S = new float[12]; public int[] NA = new int[12], NS = new int[12]; }
        private static readonly Dictionary<string, Cal> cals = new Dictionary<string, Cal>();
        private static string calFile; private static bool calDirty; private static int calSaveAt;
        public static void Init(string dataDir)
        {
            calFile = Path.Combine(dataDir, "physics_calibration.ini");
            try
            {
                if (!File.Exists(calFile)) return;
                Cal c = null;
                foreach (var line in File.ReadAllLines(calFile))
                {
                    var t = line.Trim();
                    if (t.StartsWith("[") && t.EndsWith("]")) { c = new Cal(); cals[t.Substring(1, t.Length - 2)] = c; continue; }
                    if (c == null) continue;
                    int eq = t.IndexOf('='); if (eq < 2) continue;
                    string k = t.Substring(0, eq); float v; int i;
                    if (!float.TryParse(t.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                    if (!int.TryParse(k.Substring(1), out i) || i < 0 || i >= 12) continue;
                    if (k[0] == 'A') { c.A[i] = v; c.NA[i] = 50; } else if (k[0] == 'S') { c.S[i] = v; c.NS[i] = 50; }
                }
            }
            catch (Exception ex) { Core.Log.Error("physics calibration load", ex); }
        }
        public static void SaveCal()
        {
            if (!calDirty || calFile == null) return;
            try
            {
                var sb = new System.Text.StringBuilder("; measured GTA response per model (power per unit multiplier, gear range per flat velocity). Delete to re-measure.\r\n");
                foreach (var kv in cals)
                {
                    sb.Append("[").Append(kv.Key).Append("]\r\n");
                    for (int i = 0; i < 12; i++) { if (kv.Value.NA[i] >= 8) sb.Append("A" + i + "=" + kv.Value.A[i].ToString("0.####", CultureInfo.InvariantCulture) + "\r\n"); if (kv.Value.NS[i] >= 8) sb.Append("S" + i + "=" + kv.Value.S[i].ToString("0.####", CultureInfo.InvariantCulture) + "\r\n"); }
                }
                File.WriteAllText(calFile, sb.ToString()); calDirty = false;
            }
            catch (Exception ex) { Core.Log.Error("physics calibration save", ex); }
        }
        private static Cal CalFor(Live L) { Cal c; if (!cals.TryGetValue(L.CalKey, out c)) cals[L.CalKey] = c = new Cal(); return c; }

        /// <summary>GTA acceleration (m/s2) per unit power multiplier at full throttle in a gear: measured, else a first guess.</summary>
        private static float A(Live L, float stockDriveForce, int gear, int gtaGears)
        {
            var c = CalFor(L); int g = Math.Max(1, Math.Min(11, gear));
            if (c.NA[g] >= 4) return c.A[g];
            float t = gtaGears <= 1 ? 0f : (g - 1f) / (gtaGears - 1f);
            return Math.Max(0.3f, stockDriveForce * 9.81f * (2.2f - 1.2f * t));
        }
        /// <summary>GTA's top speed of a gear divided by the flat-velocity value: measured, else a first guess.</summary>
        private static float S(Live L, int gear, int gtaGears)
        {
            var c = CalFor(L); int g = Math.Max(1, Math.Min(11, gear));
            if (c.NS[g] >= 4) return c.S[g];
            float t = gtaGears <= 1 ? 1f : (g - 1f) / (gtaGears - 1f);
            return L.S0Top * (float)Math.Pow(0.9 / 3.33, 1.0 - t);
        }

        // ------------------------------------------------------------------ per-gear range (flat velocity)
        private static int lastGear = -1, settleUntil; private static float lastFlat, settleSpeed;
        private static void Flat(Vehicle car, Live L, int gear, float speed, bool downshift)
        {
            var h = car.HandlingData; if (h == null || !h.IsValid) return;
            int g = Math.Max(1, Math.Min(L.Gear.Gears, gear));
            float want = L.Gear.Speed(g, L.Spec.Redline) / Math.Max(0.05f, S(L, g, car.HighGear));
            if (downshift) { settleUntil = Game.GameTime + 2500; settleSpeed = speed; }
            if (Game.GameTime < settleUntil && speed <= settleSpeed + 1f) want = Math.Max(want, speed / (0.92f * Math.Max(0.05f, S(L, g, car.HighGear))));   // no shift hunting right after a downshift
            want = Math.Max(5f, Math.Min(250f, want));
            if (Math.Abs(want - lastFlat) < lastFlat * 0.02f) return;
            h.InitialDriveMaxFlatVelocity = want;
            float topFlat = L.Gear.Speed(L.Gear.Gears, L.Spec.Redline) / Math.Max(0.05f, S(L, L.Gear.Gears, car.HighGear));
            h.InitialDriveMaxVelocity = Math.Max(want, topFlat) * 1.3f;
            lastFlat = want;
            refresh = !refresh;
            Function.Call(Hash.MODIFY_VEHICLE_TOP_SPEED, car, refresh ? 1f : 1.0001f);   // make the vehicle re-read the flat velocity (1 = neutral)
        }
        private static bool refresh;

        // ------------------------------------------------------------------ per frame
        private static float prevM = 1f, smoothM = 1f; private static int gearSince; private static float[] tops; private static string topsKey;
        /// <summary>The real ratios are written into GTA's own gear table.</summary>
        public static bool DirectGears { get; private set; }
        /// <summary>Power multiplier for this frame. measuredAccel: GTA's own longitudinal acceleration over the last frame
        /// (NaN when it can't be measured cleanly).</summary>
        public static void Tick(Vehicle car, Live L, OwnedVehicle r, float dt, float stockDriveForce, float measuredAccel, bool cleanSample, float slideCos)
        {
            Current = L;
            // real gear ratios straight into GTA's gear table (falls back to re-ranging via flat velocity if the layout isn't recognised)
            if (tops == null || tops.Length != L.Gear.Gears || topsKey != L.Key) { tops = new float[L.Gear.Gears]; for (int i = 0; i < tops.Length; i++) tops[i] = L.Gear.Speed(i + 1, L.Spec.Redline); topsKey = L.Key; }
            DirectGears = Physics.VehMem.WriteRatios(car, tops);
            int gear = car.CurrentGear;
            float rpmFrac = car.CurrentRPM, thr = Math.Max(0f, car.Throttle);
            float speed = car.Speed;
            // gear changes: re-range the new gear
            if (DirectGears) { if (gear != lastGear) { if (lastGear >= 0) gearSince = Game.GameTime; lastGear = gear; } }
            else if (gear != lastGear)
            {
                bool down = lastGear > 0 && gear > 0 && gear < lastGear;
                if (lastGear >= 0) gearSince = Game.GameTime;
                lastGear = gear; lastFlat = -1f;
                if (gear > 0) Flat(car, L, gear, speed, down);
            }
            else if (!DirectGears && gear > 0 && Game.GameTime < settleUntil + 100) Flat(car, L, gear, speed, false);
            int g = Math.Max(1, Math.Min(L.Gear.Gears, gear <= 0 ? 1 : gear));
            float rpm = Math.Max(L.Spec.Idle, rpmFrac * L.Spec.Redline);
            if (Physics.Native.DriveActive)
            {   // the native drivetrain owns the engine: GTA's own drive force isn't used, nothing to calibrate
                EngineRpm = Math.Max(L.Spec.Idle, Physics.Native.Rpm);
                float ovn = L.Gear.Overall(g);
                AccelDemand = L.Engine.SteadyTorque(EngineRpm) * ovn * L.Gear.Efficiency / L.Gear.TyreR / Math.Max(1f, L.Mass);
                var en = L.Engine;
                en.BoostState = Physics.SimWorld.Boost;
                en.BoostNowBar = en.Ind == Physics.Induction.Turbo ? en.BoostBar * Physics.SimWorld.Boost : en.Ind == Physics.Induction.Supercharged ? en.BoostBar * thr * Math.Min(1f, EngineRpm / (L.Spec.Redline * 0.35f)) : 0f;
                TorqueMult = 1f; prevM = smoothM = 1f;
                return;
            }
            EngineRpm = rpm;
            float torque = L.Engine.Torque(rpm, thr, dt);
            float ov = L.Gear.Overall(g) * (gear <= 0 ? 0.9f : 1f);
            float force = torque * ov * L.Gear.Efficiency / L.Gear.TyreR;
            float meff = L.Mass * (1.04f + 0.0025f * ov * ov * (L.Spec.Bike ? 0.3f : 1f));
            AccelDemand = force / meff;
            // learn GTA's response in this gear
            var c = CalFor(L); int gi = Math.Max(1, Math.Min(11, gear));
            if (cleanSample && gear > 0 && thr > 0.97f && !float.IsNaN(measuredAccel) && Game.GameTime - gearSince > 600 && rpmFrac > 0.3f && rpmFrac < 0.95f && prevM > 0.05f)
            {
                float sample = measuredAccel / prevM, cur = A(L, stockDriveForce, gi, car.HighGear);
                if (sample > cur * 0.25f && sample < cur * 4f)
                {
                    c.A[gi] = c.NA[gi] == 0 ? sample : c.A[gi] + (sample - c.A[gi]) * Math.Min(1f, dt / 0.8f);
                    if (c.NA[gi] < 1000) c.NA[gi]++;
                    calDirty = true;
                }
            }
            if (!DirectGears && cleanSample && gear > 0 && rpmFrac > 0.3f && rpmFrac < 0.97f && Game.GameTime - gearSince > 600 && car.Clutch > 0.9f && lastFlat > 0f)
            {
                float ws = Math.Abs(car.WheelSpeed);
                float sm = ws / rpmFrac / lastFlat;
                if (sm > 0.05f && sm < 20f)
                {
                    c.S[gi] = c.NS[gi] == 0 ? sm : c.S[gi] + (sm - c.S[gi]) * Math.Min(1f, dt / 1.5f);
                    if (c.NS[gi] < 1000) c.NS[gi]++;
                    calDirty = true;
                    if (c.NS[gi] == 4 || c.NS[gi] % 120 == 0) { lastFlat = -1f; Flat(car, L, gear, speed, false); }
                }
            }
            if (Game.GameTime > calSaveAt) { calSaveAt = Game.GameTime + 30000; SaveCal(); }
            float M = AccelDemand / A(L, stockDriveForce, gi, car.HighGear);
            // GTA cuts drive force when the car is sideways: give it back (a real engine doesn't care about the slide)
            float rear = 1f - Physics.HandlingMem.GetBias(car.HandlingData, Physics.HandlingMem.DriveBias);
            if (slideCos < 0.995f && slideCos > 0.2f) M *= 1f + rear * Math.Min(1.6f, 1f / Math.Max(0.35f, slideCos) - 1f);
            M *= (1f - Math.Max(0f, Math.Min(0.95f, TcsCut))) * (1f - Math.Max(0f, Math.Min(0.95f, EscCut)));
            M = Math.Max(0.02f, Math.Min(14f, M));
            smoothM += (M - smoothM) * Math.Min(1f, dt * 25f);
            TorqueMult = smoothM;
            prevM = TorqueMult;
        }

        /// <summary>Gear count the car should be built with (applied when it spawns: GTA builds the gearbox then).</summary>
        public static int SpawnGears(OwnedVehicle r, int stockGears, int specGears)
        {
            if (Setup.Has(r, "gearbox")) return 6;
            if (specGears > 0) return Math.Min(6, specGears);
            return stockGears;
        }

        public static void Off() { Current = null; TorqueMult = 1f; prevM = smoothM = 1f; lastGear = -1; lastFlat = -1f; TcsCut = EscCut = 0f; SaveCal(); }
        /// <summary>Used by the tuning menu (gear ratio values).</summary>
        public static void Forget() { cache.Clear(); }
    }
}
