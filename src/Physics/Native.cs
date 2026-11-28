using System;
using System.Runtime.InteropServices;
using GTA;
using GTA.Native;

namespace MidnightClub.Physics
{
    /// <summary>Stage 3 of the native simulation (MidnightClubPhysics.asi): inside GTA's own physics step, every
    /// friction evaluation of the player's wheels gets its force from the Pacejka Magic Formula (peak friction with
    /// load sensitivity at the wheel's real load, lateral and longitudinal slip from GTA's wheel state, combined slip),
    /// and the suspension gets a bump rubber so kerb contacts can't fire load spikes through the spring.
    /// The C# side only hands over the parameters of the fitted tyre and the car.</summary>
    public static class Native
    {
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern int MCP_Install();
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr MCP_Status();
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern void MCP_SetWheels(long a, long b, long c, long d);
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern void MCP_SetModel(float[] p, int n);
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern int MCP_Scaled();
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern int MCP_SuspDone();
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern void MCP_SetAxle(int which, float[] p, int n);
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern int MCP_DriveAvailable();
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern void MCP_SetDrive(float[] p, int n);
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern void MCP_SetCurve(float[] t, int n);
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern int MCP_GetState(float[] o, int n);
        private static readonly System.Collections.Generic.Dictionary<int, float[]> rideHeight = new System.Collections.Generic.Dictionary<int, float[]>();
        private static int stillSince; private static float[] compSum = new float[2]; private static int compN;


        public static bool Active { get; private set; }
        /// <summary>Our own tyre forces, wheel spin, engine, clutch, differentials, brakes, ABS and TCS run natively.</summary>
        public static bool DriveActive { get; private set; }
        public static float Rpm { get; private set; }
        public static bool TcsOn { get; private set; }
        public static bool AbsOn { get; private set; }
        private static int driveAvail = -1; private static readonly float[] curveBuf = new float[EngineState.N], state = new float[20];
        private static float lastSteps, lastOwn, lastFallback; private static int nextDriveLog;

        /// <summary>Per frame: the driver's inputs and the powertrain for the native drivetrain.</summary>
        public static void Drive(Vehicle car, Vehicles.Drivetrain.Live L, Persistence.OwnedVehicle rec, float escCut, int tcsL, int absL, bool hand, float dt)
        {
            if (!Active || L == null || L.Spec.Bike || car.Wheels.Count != 4) { DriveOff(); return; }
            try
            {
                if (driveAvail < 0) { driveAvail = MCP_DriveAvailable(); Core.Log.Info("Native drivetrain: " + (driveAvail != 0 ? "available" : "wheel step not hooked, not available")); }
                if (driveAvail == 0) { DriveActive = false; return; }
                var s = L.Spec; var h = car.HandlingData;
                int gear = car.CurrentGear;
                float thr = Math.Max(0f, Math.Min(1f, gear == 0 ? Math.Abs(car.Throttle) : car.Throttle)) * (1f - Math.Max(0f, Math.Min(0.95f, escCut)));
                float brake = Math.Max(0f, Math.Min(1f, car.BrakePower));
                float ratio = gear > 0 ? L.Gear.Overall(gear) : gear == 0 ? -L.Gear.Overall(1) * 1.05f : 0f;
                float rpmNow = Rpm > 100f ? Rpm : s.Idle;
                L.Engine.CurveNow(curveBuf, rpmNow, thr, dt);
                MCP_SetCurve(curveBuf, curveBuf.Length);
                float m = L.Mass, g = 9.81f;
                int brakeLvl = Math.Max(0, Function.Call<int>(GTA.Native.Hash.GET_VEHICLE_MOD, car, 12) + 1);
                float bias = Math.Max(0.35f, Math.Min(0.8f, HandlingMem.GetBias(h, HandlingMem.BrakeBias)));
                float cap = m * g * 1.55f * (1f + 0.07f * brakeLvl);       // the brakes can lock the tyres; the tyre sets the limit
                float fd = Vehicles.Setup.Has(rec, "awd") ? 0.35f : Vehicles.Setup.Has(rec, "rwd") ? 0f : s.FrontDrive;
                string kind = s.Kind ?? "";
                bool diff = Vehicles.Setup.Has(rec, "diff");
                float lockA = diff ? Math.Max(0f, Math.Min(1f, (50f + 5f * Vehicles.Setup.TuneValue(rec, 18)) / 100f)) : kind == "Super" || kind == "Sports" ? 0.35f : kind == "Muscle" ? 0.25f : 0.08f;
                float preload = diff ? 60f : 15f;
                float frontLock = fd > 0.95f ? (diff ? lockA * 0.6f : 0.05f) : 0.1f;
                float tyreR = L.Gear.TyreR;
                float wheelI = 1.15f * (tyreR / 0.33f) * (tyreR / 0.33f) * (kind == "SUVs" || kind == "Vans" || kind == "OffRoad" || kind == "Industrial" ? 1.6f : 1f);
                float engI = s.Ind == Induction.Electric ? 0.06f : 0.07f + 0.03f * Math.Max(1f, s.Litres > 0f ? s.Litres : 3f);
                float launch = Math.Min(s.Redline * 0.55f, Math.Max(s.Idle * 1.8f, L.Engine.PeakNmRpm * 0.7f));
                var p = new float[]
                {
                    1f, thr, brake, hand ? 1f : 0f, Math.Max(0f, Math.Min(1f, car.Clutch)), ratio, tyreR, L.Gear.Efficiency, engI, wheelI,
                    cap * bias, cap * (1f - bias), m * g * (1f - s.FrontPct) * 1.1f, fd, preload * 0.5f, frontLock, frontLock * 0.5f, preload, lockA, lockA * 0.5f,
                    120f, 0.35f, s.Idle, s.Redline, L.Engine.PeakNm, launch, tcsL, absL, 1f, L.Engine.MaxRpm,
                    0.25f, 0f, 0f, 0f
                };
                MCP_SetDrive(p, p.Length);
                MCP_GetState(state, state.Length);
                Rpm = state[0]; TcsOn = state[1] > 0.5f; AbsOn = state[2] > 0.5f;
                DriveActive = true;
                if (Game.GameTime > nextDriveLog)
                {
                    nextDriveLog = Game.GameTime + 20000;
                    Core.Log.Info("Native drivetrain: " + (state[15] - lastSteps) + " steps, " + (state[16] - lastOwn) + " own tyre evaluations, " + (state[17] - lastFallback) + " axis fallbacks in 20 s; body coupling error " + (state[18] * 100f).ToString("0.0") + " %; rpm " + Rpm.ToString("0")
                        + "; spin " + state[3].ToString("0.0") + "/" + state[4].ToString("0.0") + "/" + state[5].ToString("0.0") + "/" + state[6].ToString("0.0") + " rad/s, slip " + state[7].ToString("0.00") + "/" + state[8].ToString("0.00") + "/" + state[9].ToString("0.00") + "/" + state[10].ToString("0.00"));
                    lastSteps = state[15]; lastOwn = state[16]; lastFallback = state[17];
                }
            }
            catch (EntryPointNotFoundException) { driveAvail = 0; DriveActive = false; Core.Log.Info("Native drivetrain: old MidnightClubPhysics.asi, not available"); }
            catch (Exception ex) { Core.Log.Error("Native drivetrain", ex); driveAvail = 0; DriveOff(); }
        }
        public static void DriveOff()
        {
            if (DriveActive) { try { MCP_SetDrive(new float[] { 0f }, 1); } catch { } }
            DriveActive = false; TcsOn = AbsOn = false; Rpm = 0f;
        }
        private static bool tried, available; private static int carHandle; private static int nextUpdate, lastScaled, lastSusp, nextLog;

        public static void Update(Vehicle car, Vehicles.Drivetrain.Live L, float wet, bool enabled)
        {
            if (!enabled || car == null || L == null) { Off(); return; }
            if (!tried)
            {
                tried = true;
                try { available = MCP_Install() != 0; Core.Log.Info("Native physics: " + Marshal.PtrToStringAnsi(MCP_Status())); }
                catch (DllNotFoundException) { available = false; Core.Log.Info("Native physics: MidnightClubPhysics.asi not found, using the script model"); }
                catch (Exception ex) { available = false; Core.Log.Error("Native physics", ex); }
            }
            if (!available) { Active = false; return; }
            try
            {
                if (car.Handle != carHandle)
                {
                    var w = car.Wheels.GetAllWheels(); long[] a = new long[4];
                    for (int i = 0; i < 4 && i < w.Length; i++) a[i] = w[i].MemoryAddress.ToInt64();
                    MCP_SetWheels(a[0], a[1], a[2], a[3]); carHandle = car.Handle;
                    nextUpdate = 0;
                }
                if (Game.GameTime >= nextUpdate)
                {
                    nextUpdate = Game.GameTime + 500;
                    var t = L.Tyre; float wheelLoad = L.Mass * 9.81f / (L.Spec.Bike ? 2f : 4f);
                    var p = new float[] { 1f, t.MuY, t.MuX, t.LoadSens, t.By, t.Cy, t.Ey, t.Bx, t.Cx, t.Ex, t.Rb1, t.Rb2, t.Rc, wheelLoad, wet, wheelLoad, 2.2f, 0.5f, 1.3f, 0f };
                    MCP_SetModel(p, p.Length);
                    Axles(car, L);
                }
                // static ride height per axle, measured while the car stands still (the preload keeps GTA's ride height)
                if (car.Speed < 0.3f && car.IsOnAllWheels) { if (stillSince == 0) stillSince = Game.GameTime; }
                else { stillSince = 0; compN = 0; compSum[0] = compSum[1] = 0f; }
                if (stillSince != 0 && Game.GameTime - stillSince > 1200 && compN < 60)
                {
                    var w = car.Wheels.GetAllWheels();
                    if (w.Length >= 4)
                    {
                        float f0 = (VehMem.Compression(w[0]) + VehMem.Compression(w[1])) / 2f, r0 = (VehMem.Compression(w[2]) + VehMem.Compression(w[3])) / 2f;
                        if (!float.IsNaN(f0) && !float.IsNaN(r0)) { compSum[0] += f0; compSum[1] += r0; compN++; }
                        if (compN == 60) { rideHeight[car.Model.Hash] = new[] { compSum[0] / 60f, compSum[1] / 60f }; nextUpdate = 0; Core.Log.Info("Native suspension: static compression front " + (compSum[0] / 60f).ToString("0.000") + " m, rear " + (compSum[1] / 60f).ToString("0.000") + " m"); }
                    }
                }
                Active = true;
                if (Game.GameTime > nextLog) { nextLog = Game.GameTime + 20000; int s = MCP_Scaled(), sd = MCP_SuspDone(); Core.Log.Info("Native model: " + (s - lastScaled) + " tyre and " + (sd - lastSusp) + " suspension evaluations in 20 s"); lastScaled = s; lastSusp = sd; }
            }
            catch (Exception ex) { Core.Log.Error("Native physics update", ex); available = false; Off(); }
        }

        /// <summary>Real suspension per axle: spring rate from the ride frequency, preload that keeps the ride height,
        /// damping from real damping ratios (digressive above 0.1 m/s), anti-roll bars from the roll stiffness split,
        /// bump stop near the end of travel; tuning sliders and upgrade levels included.</summary>
        private static void Axles(Vehicle car, Vehicles.Drivetrain.Live L)
        {
            var s = L.Spec; if (s.Bike) { MCP_SetAxle(0, new float[] { 0f }, 1); MCP_SetAxle(1, new float[] { 0f }, 1); return; }
            var rec = Vehicles.VehicleManager.Instance != null ? Vehicles.VehicleManager.Instance.CurrentRecord : null;
            int level = Math.Max(0, Math.Min(4, Function.Call<int>(GTA.Native.Hash.GET_VEHICLE_MOD, car, 15) + 1));
            bool coil = Vehicles.Setup.Has(rec, "springs"), bars = Vehicles.Setup.Has(rec, "arb");
            float f = Suspension.RideHz(s, level, coil);
            float[] rh; if (!rideHeight.TryGetValue(car.Model.Hash, out rh)) rh = new[] { 0.11f, 0.11f };
            string kind = s.Kind ?? "";
            float arbF = kind == "Super" ? 0.9f : kind == "Sports" ? 0.75f : kind == "SUVs" || kind == "Vans" ? 0.6f : kind == "OffRoad" ? 0.3f : kind == "Muscle" || kind == "Sedans" ? 0.45f : 0.55f;
            arbF *= (1f + 0.1f * level) * (bars ? 1.15f : 1f);
            float split = s.FrontDrive >= 0.95f ? 0.62f : s.Layout == "RR" ? 0.47f : s.Layout == "MR" || s.Layout == "AWD-M" ? 0.52f : 0.57f;
            split = Math.Max(0.2f, Math.Min(0.8f, split + 0.02f * (Vehicles.Setup.TuneValue(rec, 7) - Vehicles.Setup.TuneValue(rec, 8))));
            arbF *= 1f + 0.045f * (Vehicles.Setup.TuneValue(rec, 7) + Vehicles.Setup.TuneValue(rec, 8)) / 2f;
            for (int ax = 0; ax < 2; ax++)
            {
                float share = ax == 0 ? s.FrontPct : 1f - s.FrontPct;
                float mc = L.Mass * share / 2f;
                float fa = f * (float)Math.Sqrt(Math.Max(0.3f, 1f + 0.045f * Vehicles.Setup.TuneValue(rec, ax == 0 ? 9 : 10)));
                float k = mc * (2f * (float)Math.PI * fa) * (2f * (float)Math.PI * fa);
                float cc = 2f * (float)Math.Sqrt(k * mc);
                float bump = 1f + 0.05f * Vehicles.Setup.TuneValue(rec, 13), reb = 1f + 0.05f * Vehicles.Setup.TuneValue(rec, 12);
                float x0 = Math.Max(0.03f, rh[ax]);
                float pre = mc * 9.81f - k * x0;
                float karb = k * arbF * 2f * (ax == 0 ? split : 1f - split);
                var p = new float[] { 1f, k, pre, cc * (coil ? 0.36f : 0.30f) * bump, cc * 0.14f * bump, cc * (coil ? 0.62f : 0.55f) * reb, cc * 0.24f * reb, karb, 0.10f, Math.Min(0.22f, x0 * 1.7f), k * 4f, mc * 9.81f * 3.5f };
                MCP_SetAxle(ax, p, p.Length);
            }
        }

        public static void Off()
        {
            if (!Active && carHandle == 0) return;
            DriveOff();
            try { if (available) { MCP_SetModel(new float[] { 0f }, 1); MCP_SetAxle(0, new float[] { 0f }, 1); MCP_SetAxle(1, new float[] { 0f }, 1); MCP_SetWheels(0, 0, 0, 0); } } catch { }
            Active = false; carHandle = 0;
        }
    }
}
