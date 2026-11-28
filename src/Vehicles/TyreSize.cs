using System;
using MidnightClub.Persistence;
using MidnightClub.Physics;

namespace MidnightClub.Vehicles
{
    /// <summary>Real tyre sizes per axle (width / profile R rim): the tyre's construction for the physics (cornering
    /// stiffness, vertical stiffness, grip, rolling radius) and the wheel's look (tyre and rim radius).</summary>
    public static class TyreSize
    {
        public static SpecTyre Stock(CarSpec s, bool front)
        {
            var t = s != null && s.Sheet != null ? s.Sheet.tyres : null;
            return t == null ? null : front ? t.front : t.rear;
        }
        public static bool Custom(OwnedVehicle r, bool front)
        {
            return r != null && (front ? (r.RimF > 0 || r.ProfF > 0 || r.WidthF > 0) : (r.RimR > 0 || r.ProfR > 0 || r.WidthR > 0));
        }
        /// <summary>The fitted size (stock where not changed).</summary>
        public static void Get(OwnedVehicle r, SpecTyre st, bool front, out float widthMm, out float prof, out float rimIn)
        {
            float w0 = st != null && st.widthM > 0.05f ? st.widthM * 1000f : 225f, a0 = st != null && st.aspect > 0.1f ? st.aspect * 100f : 50f, r0 = st != null && st.rimIn > 8f ? st.rimIn : 17f;
            widthMm = r != null && (front ? r.WidthF : r.WidthR) > 0 ? (front ? r.WidthF : r.WidthR) : w0;
            prof = r != null && (front ? r.ProfF : r.ProfR) > 0 ? (front ? r.ProfF : r.ProfR) : a0;
            rimIn = r != null && (front ? r.RimF : r.RimR) > 0 ? (front ? r.RimF : r.RimR) : r0;
        }
        public static string Label(float w, float p, float rim) { return w.ToString("0") + "/" + p.ToString("0") + " R" + rim.ToString("0"); }
        /// <summary>A copy of the stock tyre with the fitted size.</summary>
        public static SpecTyre Sized(OwnedVehicle r, SpecTyre st, bool front)
        {
            if (st == null || !Custom(r, front)) return st;
            float w, p, rim; Get(r, st, front, out w, out p, out rim);
            var t = (SpecTyre)typeof(SpecTyre).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(st, null);
            t.widthM = w / 1000f; t.aspect = p / 100f; t.rimIn = rim; t.sidewallM = t.widthM * t.aspect;
            t.radiusM = rim * 0.0127f + t.sidewallM; t.size = Label(w, p, rim);
            return t;
        }
        /// <summary>Rolling radius and rim radius against the stock tyre (for the wheel's look and contact).</summary>
        public static float RadiusRatio(OwnedVehicle r, CarSpec s, bool front)
        {
            var st = Stock(s, front); if (st == null || !Custom(r, front) || st.radiusM < 0.15f) return 1f;
            var t = Sized(r, st, front); return Math.Max(0.75f, Math.Min(1.3f, t.radiusM / st.radiusM));
        }
        public static float RimRatio(OwnedVehicle r, CarSpec s, bool front)
        {
            var st = Stock(s, front); if (st == null || !Custom(r, front) || st.rimIn < 8f) return 1f;
            float w, p, rim; Get(r, st, front, out w, out p, out rim); return Math.Max(0.6f, Math.Min(1.6f, rim / st.rimIn));
        }
    }
}
