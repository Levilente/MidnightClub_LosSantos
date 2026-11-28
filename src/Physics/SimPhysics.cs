using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.Persistence;
using MidnightClub.Vehicles;

namespace MidnightClub.Physics
{
    /// <summary>The only physics model: simulation, built from each vehicle's real data.
    /// Handling (only for the model you drive, put back afterwards): real mass, centre of gravity (height and
    /// front/rear position from the wheel geometry), real drive split, suspension by era and upgrade level, brakes,
    /// tyres from the Pacejka set of the fitted compound (peak friction, peak slip angle, fall-off).
    /// Every frame: aero drag and downforce (or lift) from real CdA/ClA, tyre load sensitivity with longitudinal
    /// and lateral load transfer, combined slip (a spinning or locked tyre loses side grip, the handbrake really
    /// locks the rears), aero balance, wet grip per compound, wheelies when the car can really lift its nose, and
    /// the driver aids: traction control, ABS and stability management, each 0 (off) to 10.</summary>
    public sealed class SimPhysics
    {
        public static SimPhysics Instance { get; private set; }
        public static bool Active { get { return Instance != null; } }
        public const float GripK = 2.25f;          // GTA traction units per unit of real tyre friction

        private sealed class Stock { public float TMax, TMin, TLat, LowLoss, Steer, Brake, HBrake, SForce, SComp, SReb, SUp, SLow, Arb, RollF, RollR, BiasT, BiasB, Raise, FlatVel, DriveF, Mass, DriveBias, ArbBias, SuspBias, TLoss, MaxVel, Drag, DriveInertia, ClutchUp, ClutchDown; public int Gears; public Vector3 Com, Inertia; }

        private readonly VehicleManager vehicles; private readonly VehicleRegistry registry; private readonly Config cfg;
        private readonly Dictionary<int, Stock> stocks = new Dictionary<int, Stock>();
        private int appliedModel; private string appliedKey; private Vehicle appliedCar;
        private float baseTMax, baseSlide, baseBias = 0.5f, baseLat, baseBrake, baseSteer;
        public Drivetrain.Live Live { get; private set; }
        public float SlipDeg { get; private set; }
        public bool TcsActive { get; private set; }
        public bool AbsActive { get; private set; }
        public bool EscActive { get; private set; }

        public SimPhysics(VehicleManager vehicles, VehicleRegistry registry, Config cfg, string dataDir)
        {
            Instance = this; this.vehicles = vehicles; this.registry = registry; this.cfg = cfg;
            Drivetrain.Init(dataDir);
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To != GameMode.MIDNIGHT_CLUB) Restore(); });
        }

        // ------------------------------------------------------------------ spawn: gearbox
        private int spawnPatched; private int spawnStockGears;
        /// <summary>Called right before an owned car is created: GTA builds the gearbox at spawn, so the gear count
        /// (race gearbox = six speeds, otherwise the real car's count) is set on the model's handling first.</summary>
        public void PrepareSpawn(Model model, OwnedVehicle rec)
        {
            try
            {
                if (appliedModel == model.Hash) Restore();
                var h = HandlingData.GetByVehicleModel(model); if (h == null || !h.IsValid) return;
                Capture(model.Hash, h);
                var s = stocks[model.Hash];
                int want = Drivetrain.SpawnGears(rec, s.Gears, RealData.PlannedGears(model.Hash, s.Gears));
                if (want != h.InitialDriveGears) { spawnStockGears = s.Gears; h.InitialDriveGears = want; spawnPatched = model.Hash; }
            }
            catch (Exception ex) { Log.Error("PrepareSpawn", ex); }
        }
        public void AfterSpawn(Model model)
        {
            if (spawnPatched != model.Hash) return;
            try { var h = HandlingData.GetByVehicleModel(model); if (h != null && h.IsValid) h.InitialDriveGears = spawnStockGears; } catch { }
            spawnPatched = 0;
        }

        public int StockGears(Vehicle car) { Stock s; return stocks.TryGetValue(car.Model.Hash, out s) ? s.Gears : car.HandlingData.InitialDriveGears; }
        public float StockMass(Vehicle car) { Stock s; return stocks.TryGetValue(car.Model.Hash, out s) ? s.Mass : car.HandlingData.Mass; }
        /// <summary>The gearbox GTA built doesn't match what the car should have now (race gearbox fitted / removed).</summary>
        public bool NeedsRebuild(Vehicle car, OwnedVehicle rec)
        {
            if (car == null || !car.Exists() || rec == null || car.Model.IsBike) return false;
            int stockGears = StockGears(car);
            return car.HighGear != Drivetrain.SpawnGears(rec, stockGears, RealData.PlannedGears(car.Model.Hash, stockGears));
        }

        private Stock Capture(int hash, HandlingData h)
        {
            Stock s; if (stocks.TryGetValue(hash, out s)) return s;
            stocks[hash] = s = new Stock { TMax = h.TractionCurveMax, TMin = h.TractionCurveMin, TLat = h.TractionCurveLateral, LowLoss = h.LowSpeedTractionLossMultiplier, Steer = h.SteeringLock, Brake = h.BrakeForce, HBrake = h.HandBrakeForce, Com = h.CenterOfMassOffset,
                SForce = h.SuspensionForce, SComp = h.SuspensionCompressionDamping, SReb = h.SuspensionReboundDamping, SUp = h.SuspensionUpperLimit, SLow = h.SuspensionLowerLimit,
                Arb = h.AntiRollBarForce, RollF = h.RollCenterHeightFront, RollR = h.RollCenterHeightRear, Inertia = h.InertiaMultiplier,
                BiasT = h.TractionBiasFront, BiasB = h.BrakeBiasFront, Raise = h.SuspensionRaise, FlatVel = h.InitialDriveMaxFlatVelocity, DriveF = h.InitialDriveForce,
                Mass = h.Mass, DriveBias = h.DriveBiasFront, ArbBias = h.AntiRollBarBiasFront, SuspBias = h.SuspensionBiasFront, TLoss = h.TractionLossMultiplier,
                MaxVel = h.InitialDriveMaxVelocity, Drag = h.InitialDragCoefficient, DriveInertia = h.DriveInertia, ClutchUp = h.ClutchChangeRateScaleUpShift, ClutchDown = h.ClutchChangeRateScaleDownShift, Gears = h.InitialDriveGears };
            return s;
        }

        private static void Put(HandlingData h, Stock s)
        {
            h.TractionCurveMax = s.TMax; h.TractionCurveMin = s.TMin; h.TractionCurveLateral = s.TLat; h.LowSpeedTractionLossMultiplier = s.LowLoss; h.SteeringLock = s.Steer; h.BrakeForce = s.Brake; h.HandBrakeForce = s.HBrake; h.CenterOfMassOffset = s.Com;
            h.SuspensionForce = s.SForce; h.SuspensionCompressionDamping = s.SComp; h.SuspensionReboundDamping = s.SReb; h.SuspensionUpperLimit = s.SUp; h.SuspensionLowerLimit = s.SLow;
            h.AntiRollBarForce = s.Arb; h.RollCenterHeightFront = s.RollF; h.RollCenterHeightRear = s.RollR; h.InertiaMultiplier = s.Inertia;
            h.TractionBiasFront = s.BiasT; h.BrakeBiasFront = s.BiasB; h.SuspensionRaise = s.Raise; h.InitialDriveMaxFlatVelocity = s.FlatVel; h.InitialDriveForce = s.DriveF;
            h.Mass = s.Mass; h.DriveBiasFront = s.DriveBias; h.AntiRollBarBiasFront = s.ArbBias; h.SuspensionBiasFront = s.SuspBias; h.TractionLossMultiplier = s.TLoss;
            h.InitialDriveMaxVelocity = s.MaxVel; h.InitialDragCoefficient = s.Drag; h.DriveInertia = s.DriveInertia; h.ClutchChangeRateScaleUpShift = s.ClutchUp; h.ClutchChangeRateScaleDownShift = s.ClutchDown; h.InitialDriveGears = s.Gears;
        }

        // ------------------------------------------------------------------ handling (per model, only while driving)
        private static int Mod(Vehicle v, int t) { return Function.Call<int>(Hash.GET_VEHICLE_MOD, v, t); }
        private static float ToHandlingAngle(float stock, float deg) { return stock > 3f ? deg : deg * (float)Math.PI / 180f; }

        private void Apply(Vehicle car, OwnedVehicle rec)
        {
            SyncInduction(car, rec);
            int hash = car.Model.Hash, compound = Math.Max(0, Math.Min(5, rec.TyreCompound));
            string key = hash + "|" + Setup.Key(rec) + "|" + compound + "|" + Mod(car, 11) + "|" + Mod(car, 12) + "|" + Mod(car, 13) + "|" + Mod(car, 15) + "|" + rec.EcuLevel + "|" + car.HighGear + "|" + car.Handle;
            if (appliedModel == hash && appliedKey == key) return;
            Restore();
            var h = car.HandlingData; if (h == null || !h.IsValid) return;
            var s = Capture(hash, h);
            var spec = RealData.For(car);
            var L = Drivetrain.Build(car, rec, spec, compound, s.Mass);
            var info = registry.Get(hash);
            if (info != null && info.MaxSpeed > 10f && s.FlatVel > 1f) L.S0Top = Clamp(info.MaxSpeed / s.FlatVel, 0.05f, 5f);   // GTA's stock top speed per flat unit (whatever unit SHVDN uses)
            Live = L;
            int susp = Math.Max(0, Math.Min(4, Mod(car, 15) + 1));
            bool bike = spec.Bike;
            // mass (Setup applies the weight-reduction parts)
            h.Mass = spec.Curated ? spec.Kg : s.Mass;
            // centre of gravity: real front/rear balance and height, from the model's wheel positions
            if (spec.HasGeo && !bike)
            {
                float y = spec.GeoRearY + spec.FrontPct * (spec.GeoFrontY - spec.GeoRearY);
                float ground = spec.GeoAxleZ - spec.TyreR;
                float cg = spec.CgH - 0.012f * susp - (Setup.Has(rec, "springs") ? 0.01f : 0f);
                float z = ground + cg;
                var com = s.Com;
                com.Y = s.Com.Y + Clamp(y - s.Com.Y, -0.35f, 0.35f);
                com.Z = s.Com.Z + Clamp(z - s.Com.Z, -0.15f, 0.04f);   // never raise GTA's CoM much: its bodies roll on a higher roll axis than real cars
                h.CenterOfMassOffset = com;
            }
            if (spec.Curated && !bike) h.DriveBiasFront = spec.FrontDrive >= 0.95f ? 1f : spec.FrontDrive;
            h.InitialDriveGears = Math.Max(1, car.HighGear);
            h.InitialDragCoefficient = s.Drag * 0.03f;                    // the real aero drag is applied every frame instead
            h.InitialDriveForce = s.DriveF;
            // tyres: Pacejka set of the fitted compound
            var t = L.Tyre;
            h.TractionCurveMax = GripK * t.MuY;
            h.TractionCurveMin = h.TractionCurveMax * (0.5f + 0.5f * (0.7f * t.SlideY + 0.3f * t.SlideX));   // GTA scrubs past the peak far more than a real tyre: only half the MF fall-off
            h.TractionCurveLateral = ToHandlingAngle(s.TLat, t.PeakDeg * 1.4f);   // GTA's slip angle is measured at the body, the tyre's at the contact patch
            h.LowSpeedTractionLossMultiplier = s.LowLoss;
            h.TractionLossMultiplier = s.TLoss * t.OffroadLoss;
            if (!bike) h.TractionBiasFront = 0.5f;
            Suspension.Apply(h, spec, susp, Setup.Has(rec, "springs"), Setup.Has(rec, "arb"));
            h.BrakeForce = s.Brake * 1.15f;
            h.HandBrakeForce = s.HBrake * 1.2f;
            if (rec != null) Setup.Apply(h, rec);
            baseTMax = h.TractionCurveMax; baseSlide = h.TractionCurveMin / Math.Max(0.01f, h.TractionCurveMax); baseBias = h.TractionBiasFront; baseLat = h.TractionCurveLateral; baseBrake = h.BrakeForce; baseSteer = h.SteeringLock;
            appliedModel = hash; appliedKey = key; appliedCar = car;
            Log.Info("Sim handling " + spec.Id + ": " + h.Mass.ToString("0") + " kg, CoG " + h.CenterOfMassOffset.Y.ToString("0.00") + "/" + h.CenterOfMassOffset.Z.ToString("0.00") + ", drive front " + h.DriveBiasFront.ToString("0.00") + ", grip " + h.TractionCurveMax.ToString("0.00") + " (" + t.Name + ")");
        }

        private static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }

        /// <summary>Old saves: GTA's simple turbo becomes turbo stage 1 on a naturally aspirated engine. GTA's turbo
        /// toggle is kept in step with the real induction (it only gives the whistle and blow-off sound).</summary>
        private static void SyncInduction(Vehicle car, OwnedVehicle rec)
        {
            if (rec.Parts == null) rec.Parts = new List<string>();
            if (rec.Toggles == null) rec.Toggles = new List<int>();
            var spec = RealData.For(car);
            if (rec.Toggles.Contains(18) && spec.Ind == Induction.NA && Engine.TurboStage(rec) == 0 && Engine.ScStage(rec) == 0) { rec.Parts.Add("turbo1"); Log.Info("Migrated GTA turbo to turbo stage 1 on " + spec.Id); }
            bool turbo = Engine.TurboStage(rec) > 0 || (spec.Ind == Induction.Turbo && Engine.ScStage(rec) == 0);
            if (Function.Call<bool>(Hash.IS_TOGGLE_MOD_ON, car, 18) != turbo) Function.Call(Hash.TOGGLE_VEHICLE_MOD, car, 18, turbo);
            if (turbo && !rec.Toggles.Contains(18)) rec.Toggles.Add(18); else if (!turbo) rec.Toggles.Remove(18);
        }

        public void Restore()
        {
            if (appliedModel == 0) return;
            try
            {
                Stock s; var h = HandlingData.GetByVehicleModel(new Model(appliedModel));
                if (h != null && h.IsValid && stocks.TryGetValue(appliedModel, out s)) Put(h, s);
                if (appliedCar != null && appliedCar.Exists()) Function.Call(Hash.MODIFY_VEHICLE_TOP_SPEED, appliedCar, 1f);
            }
            catch (Exception ex) { Log.Error("Sim restore", ex); }
            appliedModel = 0; appliedKey = null; appliedCar = null; Live = null;
            Drivetrain.Off();
        }

        // ------------------------------------------------------------------ per frame
        private float prevVx = float.NaN, setVx = float.NaN, axS, fF = 1f, fR = 1f, absMult = 1f, tcs, esc; private int lastHandle;

        private void Tick()
        {
            var car = vehicles.Current; var rec = vehicles.CurrentRecord; var ped = Game.Player.Character;
            if (car == null || !car.Exists() || rec == null || !ped.IsInVehicle(car) || car.Driver != ped || car.Model.IsBicycle || car.IsDead) { Restore(); prevVx = float.NaN; return; }
            try
            {
                Apply(car, rec);
                var L = Live; if (L == null) return;
                float dt = Math.Min(0.05f, Math.Max(0.001f, Game.LastFrameTime));
                if (!car.IsInAir) Suspension.Filter(car, dt, L.Tyre.MuY);
                Frame(car, rec, L, dt);
                Suspension.Set(car.Velocity, car.WorldRotationVelocity);
            }
            catch (Exception ex) { Log.Error("SimPhysics", ex); Restore(); }
        }

        private void Frame(Vehicle car, OwnedVehicle rec, Drivetrain.Live L, float dt)
        {
            var spec = L.Spec; var h = car.HandlingData; Stock st = stocks[car.Model.Hash];
            if (car.Handle != lastHandle) { lastHandle = car.Handle; prevVx = setVx = float.NaN; }
            var v = car.Velocity; float speed = v.Length();
            var fwd = car.ForwardVector; var right = car.RightVector; var up = car.UpVector;
            float vx = Vector3.Dot(v, fwd), vy = Vector3.Dot(v, right);
            var w = car.WorldRotationVelocity; float yaw = Vector3.Dot(w, up);
            bool ground = !car.IsInAir && car.IsOnAllWheels;
            float thr = Math.Max(0f, car.Throttle);
            float brake = Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, (int)Control.VehicleBrake);
            bool hand = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.VehicleHandbrake);
            float pitch = (float)Math.Asin(Clamp(fwd.Z, -1f, 1f));
            // measured accelerations: GTA's own (for calibration) and the real one (for load transfer)
            float gtaAccel = float.NaN;
            if (!float.IsNaN(setVx)) gtaAccel = (vx - setVx) / dt + 9.81f * (float)Math.Sin(pitch);
            if (!float.IsNaN(prevVx)) axS += ((vx - prevVx) / dt - axS) * Math.Min(1f, dt / 0.08f);
            prevVx = vx;
            float ay = vx * yaw;
            float ws = car.WheelSpeed, kd = speed > 0.5f ? (ws - vx) / Math.Max(Math.Abs(vx), 3f) : 0f;
            kd = Clamp(kd, -1f, 1f);
            float beta = speed > 3f ? (float)Math.Atan2(vy, Math.Abs(vx)) : 0f;
            SlipDeg = speed > 4f && vx > 0f ? -beta * 57.2958f : 0f;
            bool boosted = Gameplay.Abilities.NosActive || (Gameplay.StyleSystem.Instance != null && Gameplay.StyleSystem.Instance.Slipstreaming);
            bool clean = ground && !boosted && speed > 4f && vx > 0f && Math.Abs(kd) < 0.06f && Math.Abs(beta) < 0.08f && Math.Abs(pitch) < 0.1f && brake < 0.05f && !hand;
            // ---- driver aids
            int tcsL = Math.Max(0, Math.Min(10, cfg.TcsLevel)), absL = Math.Max(0, Math.Min(10, cfg.AbsLevel)), escL = Math.Max(0, Math.Min(10, cfg.EscLevel));
            float rearDrive = 1f - Clamp(h.DriveBiasFront, 0f, 1f);
            // traction control: hold the driven wheels near the tyre's peak slip
            float tcsWant = 0f;
            if (tcsL > 0 && thr > 0.05f && vx > -0.5f)
            {
                float target = L.Tyre.PeakK * (1f + (10 - tcsL) * 0.18f);
                if (kd > target) tcsWant = Math.Min(0.9f, (kd - target) / target * (0.3f + 0.07f * tcsL));
                if (spec.Bike && pitch > (20f - 1.4f * tcsL) * 0.01745f) tcsWant = Math.Max(tcsWant, Math.Min(0.9f, (pitch * 57.3f - (20f - 1.4f * tcsL)) * 0.08f));   // wheelie control
            }
            tcs += (tcsWant - tcs) * Math.Min(1f, dt / (tcsWant > tcs ? 0.03f : 0.15f));
            TcsActive = tcs > 0.05f;
            Drivetrain.TcsCut = tcs;
            // stability management: when the body slip grows past the limit and the yaw makes it worse, brake the yaw and cut power
            float escWant = 0f;
            if (escL > 0 && ground && speed > 6f && vx > 0f)
            {
                float lim = (30f - 2f * escL) * 0.01745f, ab = Math.Abs(beta);
                bool worsening = Math.Sign(yaw) == Math.Sign(beta) || Math.Abs(yaw) < 0.05f;
                if (ab > lim * 0.8f && worsening)
                {
                    float over = Math.Min(1f, (ab - lim * 0.8f) / (lim * 0.6f));
                    escWant = Math.Min(0.85f, over * (0.25f + 0.06f * escL));
                    float damp = 1f - Math.Min(0.7f, (0.02f + 0.012f * escL) * over * 60f * dt);
                    var nw = w - up * (yaw * (1f - damp));
                    Function.Call(Hash.SET_ENTITY_ANGULAR_VELOCITY, car, nw.X, nw.Y, nw.Z);
                    w = nw; yaw *= damp;
                }
            }
            esc += (escWant - esc) * Math.Min(1f, dt / (escWant > esc ? 0.05f : 0.25f));
            EscActive = esc > 0.05f;
            Drivetrain.EscCut = esc;
            // ---- powertrain
            float slideCos = speed > 3f && vx > 0f ? Math.Abs(vx) / Math.Max(0.1f, (float)Math.Sqrt(vx * vx + vy * vy)) : 1f;
            Drivetrain.Tick(car, L, rec, dt, st.DriveF, gtaAccel, clean && !float.IsNaN(gtaAccel), slideCos);
            // ---- aero: drag and downforce (lift on old shapes), plus the extra rolling drag of soft or knobbly tyres
            var nv = v;
            if (speed > 1f)
            {
                float q = 0.5f * Perf.Rho * speed * speed;
                float drag = q * L.Cda / L.Mass + (L.Tyre.Rolling - 0.012f) * 9.81f;
                nv -= v * (Math.Min(speed, drag * dt) / speed);
                if (ground || car.HeightAboveGround < 1.5f) nv -= up * (q * L.Cla / L.Mass * dt);
            }
            if (nv != v) car.Velocity = nv;
            setVx = Vector3.Dot(nv, fwd);
            if (!ground) { WriteGrip(h, L, 1f, 1f, 1f); SteeringByspeed(h, speed); return; }
            // ---- tyres: loads, load sensitivity, combined slip
            float m = L.Mass, g = 9.81f, wb = Math.Max(1f, spec.Wheelbase), track = Math.Max(0.6f, spec.Track);
            float wf = spec.FrontPct, cgh = Math.Max(0.25f, spec.CgH);
            float fa = 0.5f * Perf.Rho * L.Cla * speed * speed;
            float fzf0 = m * g * wf, fzr0 = m * g * (1f - wf);
            float dl = m * Clamp(axS, -15f, 15f) * cgh / wb;
            float fzf = Math.Max(0.05f * fzf0, fzf0 - dl + fa * L.AeroFront), fzr = Math.Max(0.05f * fzr0, fzr0 + dl + fa * (1f - L.AeroFront));
            float fzfG = Math.Max(0.05f * fzf0, fzf0 - dl + fa * wf), fzrG = Math.Max(0.05f * fzr0, fzr0 + dl + fa * (1f - wf));
            float fz0 = m * g / 4f;
            var t = L.Tyre;
            float wet = 1f - t.WetLoss * Clamp(WorldSim.Weather.Wetness, 0f, 1f);
            float gf, gr;
            if (spec.Bike)
            {
                float capR = Tyres.Mu(t, fzr, m * g / 2f, false) * fzr;
                gf = 1f; gr = capR / Math.Max(1f, t.MuX * fzrG);
            }
            else
            {
                float dy = m * Math.Abs(ay) * cgh / track;
                float rs = Clamp(0.5f + (h.AntiRollBarBiasFront - 0.5f) * 0.9f + (h.SuspensionBiasFront - 0.5f) * 0.3f, 0.25f, 0.75f);
                Func<float, float, float> cap = (axle, shift) => { float o = axle / 2f + shift, i = Math.Max(0f, axle / 2f - shift); o = axle - i; return Tyres.Mu(t, o, fz0, true) * o + Tyres.Mu(t, i, fz0, true) * i; };
                gf = 1f + (cap(fzf, dy * rs) / Math.Max(1f, t.MuY * fzfG) - 1f) * 0.5f;
                gr = 1f + (cap(fzr, dy * (1f - rs)) / Math.Max(1f, t.MuY * fzrG) - 1f) * 0.5f;
                // part of the fore/aft load transfer explicitly (trail-braking bite, lift-off rotation)
                gf *= (float)Math.Pow(Math.Max(0.2f, (fzf0 - dl) / fzf0), 0.25);
                gr *= (float)Math.Pow(Math.Max(0.2f, (fzr0 + dl) / fzr0), 0.25);
                // combined slip: longitudinal slip of each axle eats its side grip
                float alpha = Math.Abs(beta);
                float frontDrive = Clamp(h.DriveBiasFront, 0f, 1f);
                bool absOn = absL > 0;
                float kb = brake > 0.05f && vx > 2f ? -brake * t.PeakK * (absOn ? 1f + (10 - absL) * 0.12f : 3f) : 0f;
                float bf = Clamp(h.BrakeBiasFront, 0.2f, 0.8f);
                // only the driven wheels' measured spin counts (GTA has a single grip value per axle, so cutting it
                // for braking slip would cut the braking too); a locked handbrake takes most of the rear side grip
                float kF = Math.Max(0f, kd) * frontDrive * (thr > 0.05f ? 1f : 0f);
                float kR = Math.Max(0f, kd) * rearDrive * (thr > 0.05f ? 1f : 0f);
                if (hand && speed > 2f) kR = 1f;
                float lockA = Setup.Has(rec, "diff") ? Clamp((50f + 5f * Setup.TuneValue(rec, 18)) / 100f, 0f, 1f) : spec.Kind == "Super" || spec.Kind == "Sports" ? 0.4f : 0.15f;
                float gyF = Tyres.Gyk(t, kF, alpha), gyR = Tyres.Gyk(t, kR, alpha);
                gf *= 1f - (1f - gyF) * 0.3f;
                gr *= 1f - (1f - gyR) * (hand ? 0.6f : 0.45f * (0.5f + 0.5f * (rearDrive > 0.3f ? lockA : 1f)));
            }
            fF += (Clamp(gf, 0.06f, 1.8f) - fF) * Math.Min(1f, dt / 0.05f);
            fR += (Clamp(gr, 0.06f, 1.8f) - fR) * Math.Min(1f, dt / 0.05f);
            float load = (fzf + fzr) / Math.Max(1f, m * g);
            WriteGrip(h, L, fF, fR, wet, (float)Math.Pow(Math.Max(0.5f, load), 0.25));
            // ---- ABS: modulate brake pressure when the measured wheels start to lock
            AbsActive = false;
            if (absL > 0 && brake > 0.05f && vx > 3f)
            {
                float target = t.PeakK * (1f + (10 - absL) * 0.12f);
                if (kd < -target && kd > -0.95f) { absMult = Math.Max(0.6f, absMult - dt * 8f); AbsActive = true; }
                else absMult = Math.Min(1f, absMult + dt * 5f);
            }
            else absMult = Math.Min(1f, absMult + dt * 5f);
            h.BrakeForce = baseBrake * absMult;
            // ---- wheelies: the front lifts only when the real thrust can overcome the weight on the front axle
            if (thr > 0.5f && vx > 0.5f && car.CurrentGear > 0)
            {
                float lr = wf * wb;
                float aLift = g * lr / cgh;
                float capRear = Tyres.Mu(t, fzr, spec.Bike ? m * g / 2f : fz0, false) * fzr * wet;
                float a = Math.Min(Drivetrain.AccelDemand * thr * (1f - tcs), (spec.Bike || rearDrive > 0.9f ? capRear : capRear + Tyres.Mu(t, fzf, fz0, false) * fzf * (1f - rearDrive)) / m);
                if (rearDrive > 0.3f && a > aLift && pitch < 1.0f)
                {
                    float k = spec.Bike ? 0.42f : 0.35f;
                    float alpha = (a - aLift) * cgh / (k * k * wb * wb) * (spec.Bike ? 0.6f : 1f);
                    var nw = w + right * (alpha * dt);
                    Function.Call(Hash.SET_ENTITY_ANGULAR_VELOCITY, car, nw.X, nw.Y, nw.Z);
                }
            }
            SteeringByspeed(h, speed);
        }

        private void WriteGrip(HandlingData h, Drivetrain.Live L, float f, float r, float wet, float peakShift = 1f)
        {
            float b0 = baseBias;
            if (L.Spec.Bike) { h.TractionCurveMax = baseTMax * wet * Math.Min(f, r); h.TractionCurveMin = h.TractionCurveMax * baseSlide; return; }
            float sum = b0 * f + (1f - b0) * r;
            h.TractionCurveMax = baseTMax * wet * sum;
            h.TractionCurveMin = h.TractionCurveMax * baseSlide;
            h.TractionBiasFront = Clamp(b0 * f / Math.Max(0.01f, sum), 0.12f, 0.88f);
            h.TractionCurveLateral = baseLat * peakShift;
        }

        private void SteeringByspeed(HandlingData h, float speed)
        {
            float t = Math.Min(1f, speed / 70f);
            h.SteeringLock = baseSteer * (1f - 0.25f * t * t * (3f - 2f * t));
        }
    }
}
