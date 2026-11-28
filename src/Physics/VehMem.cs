using System;
using System.Runtime.InteropServices;
using GTA;

namespace MidnightClub.Physics
{
    /// <summary>Direct access to the parts of GTA's vehicle and wheel memory found with the physics probe
    /// (GTA V Enhanced 2.0.1158; every access is validated first and skipped if the layout doesn't match):
    ///   vehicle + 0x886  high gear (short)          vehicle + 0x88C  gear ratio table, [0] = reverse, [g] = gear g
    ///   vehicle + 0x8BC  drive max flat velocity    vehicle + 0x8C0  max velocity; top speed in gear g = this / ratio[g]
    ///   wheel   + 0x110  tyre radius                wheel   + 0x170  wheel spin (rad/s, negative = forward)
    ///   wheel   + 0x13C  static load share          wheel   + 0xA0..0xA8 tyre force (world), 0x160 suspension compression
    /// The gear table lets every gear have its real ratio (no flat-velocity tricks), the wheel spin gives each
    /// wheel's real slip ratio.</summary>
    public static class VehMem
    {
        private static float F(IntPtr a, int o) { return BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(a, o)), 0); }
        private static void WF(IntPtr a, int o, float v) { Marshal.WriteInt32(a, o, BitConverter.ToInt32(BitConverter.GetBytes(v), 0)); }
        public static bool Disabled;

        /// <summary>The gear table looks like GTA's (reverse negative, forward gears positive and falling).</summary>
        public static bool TableOk(Vehicle car)
        {
            if (Disabled || car == null || !car.Exists()) return false;
            try
            {
                var a = car.MemoryAddress; if (a == IntPtr.Zero) return false;
                int hg = Marshal.ReadInt16(a, 0x886);
                if (hg < 1 || hg > 10 || hg != car.HighGear) return false;
                float maxV = F(a, 0x8C0), rev = F(a, 0x88C);
                if (!(maxV > 3f && maxV < 500f) || !(rev < 0f)) return false;
                float prev = 1e9f;
                for (int g = 1; g <= hg; g++) { float r = F(a, 0x88C + 4 * g); if (!(r > 0.05f && r < 20f) || r > prev * 1.001f) return false; prev = r; }
                return true;
            }
            catch { Disabled = true; return false; }
        }

        public static float MaxVelocity(Vehicle car) { return F(car.MemoryAddress, 0x8C0); }
        public static float Ratio(Vehicle car, int g) { return F(car.MemoryAddress, 0x88C + 4 * g); }

        /// <summary>Writes ratios so gear g tops out at topSpeed[g-1] (m/s) at the redline. Returns false if the layout isn't recognised.</summary>
        public static bool WriteRatios(Vehicle car, float[] topSpeed)
        {
            if (!TableOk(car)) return false;
            var a = car.MemoryAddress; int hg = car.HighGear;
            if (topSpeed.Length < hg) return false;
            float maxV = F(a, 0x8C0);
            for (int g = 1; g <= hg; g++)
            {
                float want = maxV / Math.Max(1f, topSpeed[g - 1]);
                want = Math.Max(0.06f, Math.Min(19f, want));
                if (Math.Abs(F(a, 0x88C + 4 * g) - want) > want * 0.001f) WF(a, 0x88C + 4 * g, want);
            }
            float rev = F(a, 0x88C), r1 = F(a, 0x890);
            if (rev > -0.01f) WF(a, 0x88C, -r1);
            return true;
        }

        /// <summary>Each wheel's slip ratio (wheel surface speed vs. ground speed along its heading); NaN if unknown.</summary>
        public static float WheelSlip(VehicleWheel w, float groundSpeed)
        {
            if (Disabled) return float.NaN;
            try
            {
                var a = w.MemoryAddress; if (a == IntPtr.Zero) return float.NaN;
                float r = F(a, 0x110), spin = F(a, 0x170);
                if (!(r > 0.1f && r < 1.5f) || float.IsNaN(spin)) return float.NaN;
                float surface = -spin * r;
                return (surface - groundSpeed) / Math.Max(Math.Abs(groundSpeed), 2.5f);
            }
            catch { Disabled = true; return float.NaN; }
        }
    }
}
