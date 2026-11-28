using System;
using System.Runtime.InteropServices;
using GTA;

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

        public static bool Active { get; private set; }
        private static bool tried, available; private static int carHandle; private static int nextUpdate, lastScaled, nextLog;

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
                    var p = new float[] { 1f, t.MuY, t.MuX, t.LoadSens, t.By, t.Cy, t.Ey, t.Bx, t.Cx, t.Ex, t.Rb1, t.Rb2, t.Rc, wheelLoad, wet, wheelLoad, 2.2f, 0.3f, 2.5f, 0f };
                    MCP_SetModel(p, p.Length);
                }
                Active = true;
                if (Game.GameTime > nextLog) { nextLog = Game.GameTime + 20000; int s = MCP_Scaled(); Core.Log.Info("Native tyre model: " + (s - lastScaled) + " wheel force evaluations in 20 s"); lastScaled = s; }
            }
            catch (Exception ex) { Core.Log.Error("Native physics update", ex); available = false; Off(); }
        }

        public static void Off()
        {
            if (!Active && carHandle == 0) return;
            try { if (available) { MCP_SetModel(new float[] { 0f }, 1); MCP_SetWheels(0, 0, 0, 0); } } catch { }
            Active = false; carHandle = 0;
        }
    }
}
