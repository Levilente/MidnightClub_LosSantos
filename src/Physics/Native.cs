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
        private static readonly System.Collections.Generic.Dictionary<int, float[]> rideHeight = new System.Collections.Generic.Dictionary<int, float[]>();
        private static int stillSince; private static float[] compSum = new float[2]; private static int compN;


        public static bool Active { get; private set; }
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
            try { if (available) { MCP_SetModel(new float[] { 0f }, 1); MCP_SetAxle(0, new float[] { 0f }, 1); MCP_SetAxle(1, new float[] { 0f }, 1); MCP_SetWheels(0, 0, 0, 0); } } catch { }
            Active = false; carHandle = 0;
        }
    }
}
