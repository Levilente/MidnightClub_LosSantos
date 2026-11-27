using System;
using System.Drawing;
using System.Linq;
using MidnightClub.Persistence;
using MidnightClub.UI;

namespace MidnightClub.Vehicles
{
    /// <summary>Forza-style Performance Index: one number (100-999) for how fast a car is as built, from the car's
    /// base performance plus every upgrade (engine, gearbox, brakes, suspension, turbo, ECU, nitrous, tyres).
    /// Classes D / C / B / A / S / X; events match opponents to your class.</summary>
    public static class PerformanceIndex
    {
        public static readonly string[] Classes = { "D", "C", "B", "A", "S", "X" };
        private static readonly int[] Limits = { 300, 450, 600, 740, 870 };
        public static readonly Color[] ClassColors =
        {
            Color.FromArgb(255, 96, 196, 255), Color.FromArgb(255, 163, 230, 53), Color.FromArgb(255, 251, 191, 36),
            Color.FromArgb(255, 248, 72, 94), Color.FromArgb(255, 168, 85, 247), Color.FromArgb(255, 34, 211, 238)
        };

        private static int Mod(OwnedVehicle r, int type) { var m = r.Mods.FirstOrDefault(x => x.Type == type); return m == null ? 0 : m.Index + 1; }

        public static int Of(OwnedVehicle r)
        {
            if (r == null) return 100;
            var info = VehicleRegistry.Instance != null ? (r.ModelHash != 0 ? VehicleRegistry.Instance.Get(r.ModelHash) : VehicleRegistry.Instance.Get(r.Model)) : null;
            float basePerf = info != null ? info.Performance : 0.4f;
            int pi = 120 + (int)(basePerf * 720f);
            pi += Mod(r, 11) * 22 + Mod(r, 13) * 12 + Mod(r, 12) * 7 + Mod(r, 15) * 7;
            if (r.Toggles != null && r.Toggles.Contains(18)) pi += 30;
            pi += r.EcuLevel * 18 + r.NosLevel * 9 + Setup.PiBonus(r) + (int)(Setup.SwapPower(r.EngineSound) * 300);
            pi += new[] { 0, 10, 22, 34, -5 }[Math.Max(0, Math.Min(4, r.TyreCompound))];
            return Math.Max(100, Math.Min(999, pi));
        }
        public static int Of(VehicleInfo info) { return info == null ? 100 : Math.Max(100, Math.Min(999, 120 + (int)(info.Performance * 720f))); }

        public static int ClassIndex(int pi) { for (int i = 0; i < Limits.Length; i++) if (pi < Limits[i]) return i; return 5; }
        public static string ClassOf(int pi) { return Classes[ClassIndex(pi)]; }
        public static string Label(int pi) { return ClassOf(pi) + " " + pi; }

        /// <summary>Draws a class badge (letter block + PI) at x,y; returns its width.</summary>
        public static float Badge(int pi, float x, float y, float h, float alpha = 1f)
        {
            int c = ClassIndex(pi); var col = ClassColors[c];
            float lw = Ds.Px(h * 1.05f), nw = Ds.Px(h * 1.9f);
            Ds.Rect(x, y, lw, h, Ds.A(col, alpha));
            Ds.T(Classes[c], x + lw / 2f, y - h * 0.08f, h * 13f, Ds.A(Ds.Bg, alpha), Ds.Display, GTA.UI.Alignment.Center);
            Ds.Rect(x + lw, y, nw, h, Ds.A(Ds.Bg, 0.9f * alpha));
            Ds.Rect(x + lw, y, nw, Ds.Px(0.0015f) * 0 + 0.002f, Ds.A(col, alpha));
            Ds.T(pi.ToString(), x + lw + nw / 2f, y - h * 0.08f, h * 13f, Ds.A(Ds.Text, alpha), Ds.Display, GTA.UI.Alignment.Center);
            return lw + nw;
        }
    }
}
