using System;

namespace MidnightClub.Physics
{
    /// <summary>Pacejka "Magic Formula" tyre model, one parameter set per tyre type.
    ///   F = D sin(C atan(Bx - E(Bx - atan Bx)))   (x = slip angle in rad, or slip ratio)
    /// D (peak friction) falls with load (load sensitivity), combined slip uses the MF weighting function
    ///   Gyk = cos(C atan(B kappa)), B = rBy1 cos(atan(rBy2 alpha)).
    /// B, C, E were fitted offline (proto/mf_fit.py) to each type's peak slip and fall-off.</summary>
    public static class Tyres
    {
        public sealed class Mf
        {
            public string Name; public int Id;
            public float MuY, MuX, LoadSens;                 // peak friction lateral / longitudinal, dMu per unit dFz
            public float By, Cy, Ey, Bx, Cx, Ex;             // shape
            public float Rb1 = 11f, Rb2 = 8f, Rc = 1.05f;    // combined slip
            public float WetLoss, OffroadLoss = 1f, Rolling = 0.012f;
            public float PeakDeg, PeakK, SlideY, SlideX;     // derived
        }

        // factory / aftermarket / special types
        public const int Vintage = 0, Touring = 1, Uhp = 2, Sport = 3, Semi = 4, Slick = 5, Drift = 6, Drag = 7, BikeSport = 8, BikeTrack = 9, Knobbly = 10;
        public static readonly Mf[] Types =
        {
            M(Vintage, "Bias-ply",          0.82f, 0.86f, -0.14f, 7.106f, 1.5f, -0.48f, 8.507f, 1.5f, -1.70f, 0.20f, 1.0f),
            M(Touring, "Touring",           0.92f, 0.96f, -0.12f, 7.211f, 1.5f, -1.32f, 13.383f, 1.65f, 0.34f, 0.18f, 1.0f),
            M(Uhp,     "Ultra-high perf.",  0.99f, 1.03f, -0.10f, 10.304f, 1.7f, 0.38f, 14.858f, 1.65f, 0.16f, 0.20f, 1.15f),
            M(Sport,   "Sport",             1.04f, 1.08f, -0.10f, 10.223f, 1.7f, 0.22f, 14.329f, 1.65f, 0.06f, 0.24f, 1.2f),
            M(Semi,    "Semi-slick",        1.14f, 1.17f, -0.09f, 10.592f, 1.7f, -0.08f, 14.718f, 1.65f, -0.20f, 0.36f, 1.35f),
            M(Slick,   "Race slick",        1.40f, 1.42f, -0.08f, 11.419f, 1.9f, 0.34f, 14.536f, 1.65f, -0.80f, 0.62f, 1.6f),
            M(Drift,   "Drift",             0.88f, 0.92f, -0.12f, 11.107f, 1.5f, 0.40f, 14.285f, 1.65f, 0.58f, 0.22f, 1.1f),
            M(Drag,    "Drag radial",       0.86f, 1.55f, -0.08f, 5.964f, 1.9f, 0.46f, 12.381f, 1.65f, 0.58f, 0.50f, 1.5f),
            M(BikeSport, "Sport (bike)",    1.08f, 1.15f, -0.09f, 10.223f, 1.7f, 0.22f, 14.329f, 1.65f, 0.06f, 0.26f, 1.2f),
            M(BikeTrack, "Track (bike)",    1.22f, 1.28f, -0.08f, 10.592f, 1.7f, -0.08f, 14.718f, 1.65f, -0.20f, 0.40f, 1.4f),
            M(Knobbly, "Off-road knobbly",  0.80f, 0.85f, -0.12f, 7.106f, 1.5f, -0.48f, 8.507f, 1.5f, -1.70f, 0.15f, 0.55f),
        };

        private static Mf M(int id, string n, float muY, float muX, float ls, float by, float cy, float ey, float bx, float cx, float ex, float wet, float off)
        {
            var m = new Mf { Id = id, Name = n, MuY = muY, MuX = muX, LoadSens = ls, By = by, Cy = cy, Ey = ey, Bx = bx, Cx = cx, Ex = ex, WetLoss = wet, OffroadLoss = off };
            if (id == Slick || id == Semi) m.Rb1 = 13f;
            if (id == Drag) { m.Rolling = 0.02f; m.Rb1 = 9f; }
            if (id == Knobbly) m.Rolling = 0.02f;
            // derived shape numbers
            float best = 0f, at = 0f;
            for (int i = 1; i <= 400; i++) { float a = i * 0.0015f; float f = Shape(a, m.By, m.Cy, m.Ey); if (f > best) { best = f; at = a; } }
            m.PeakDeg = at * 57.2958f; m.SlideY = Shape(at * 3f, m.By, m.Cy, m.Ey) / Math.Max(0.01f, best);
            best = 0f; at = 0f;
            for (int i = 1; i <= 400; i++) { float k = i * 0.001f; float f = Shape(k, m.Bx, m.Cx, m.Ex); if (f > best) { best = f; at = k; } }
            m.PeakK = at; m.SlideX = Shape(1f, m.Bx, m.Cx, m.Ex) / Math.Max(0.01f, best);
            return m;
        }

        /// <summary>Normalised Magic Formula (D = 1).</summary>
        public static float Shape(float x, float b, float c, float e)
        {
            float bx = b * x;
            return (float)Math.Sin(c * Math.Atan(bx - e * (bx - Math.Atan(bx))));
        }

        /// <summary>Peak friction at a wheel load (load sensitivity around the nominal load fz0).</summary>
        public static float Mu(Mf t, float fz, float fz0, bool lateral)
        {
            float d = fz0 > 1f ? (fz - fz0) / fz0 : 0f;
            float mu = (lateral ? t.MuY : t.MuX) * (1f + t.LoadSens * Math.Max(-0.9f, Math.Min(3f, d)));
            return Math.Max(0.05f, mu);
        }

        /// <summary>Lateral force (N) of one tyre at slip angle alpha (rad), load fz.</summary>
        public static float Fy(Mf t, float alpha, float fz, float fz0)
        {
            return Mu(t, fz, fz0, true) * fz * Shape(Math.Abs(alpha), t.By, t.Cy, t.Ey) * Math.Sign(alpha);
        }

        /// <summary>Longitudinal force (N) of one tyre at slip ratio kappa.</summary>
        public static float Fx(Mf t, float kappa, float fz, float fz0)
        {
            return Mu(t, fz, fz0, false) * fz * Shape(Math.Abs(kappa), t.Bx, t.Cx, t.Ex) * Math.Sign(kappa);
        }

        /// <summary>Share of lateral force left when the tyre also slips longitudinally (MF combined slip).</summary>
        public static float Gyk(Mf t, float kappa, float alpha)
        {
            float b = t.Rb1 * (float)Math.Cos(Math.Atan(t.Rb2 * Math.Abs(alpha)));
            return Math.Max(0.02f, (float)Math.Cos(t.Rc * Math.Atan(b * Math.Abs(kappa))));
        }

        /// <summary>The tyre a car leaves the factory on, by era and type.</summary>
        public static int Factory(int year, string kind, float hpPerTonne)
        {
            if (kind == "Bike") return BikeSport;
            if (kind == "OffRoad") return Knobbly;
            if (year < 1972) return Vintage;
            if (kind == "Super" && year >= 2010) return Semi;
            if (hpPerTonne > 300f && year >= 2000) return Sport;
            if (hpPerTonne > 170f && year >= 1990) return Uhp;
            return Touring;
        }

        /// <summary>The compound shop index (0 stock, 1 sport, 2 semi-slick, 3 slick, 4 drift, 5 drag) to a tyre type.</summary>
        public static Mf ForCompound(int compound, int factory, bool bike)
        {
            switch (compound)
            {
                case 1: return Types[bike ? BikeSport : Math.Max(Sport, factory == Semi || factory == Slick ? factory : Sport)];
                case 2: return Types[bike ? BikeTrack : Math.Max(Semi, factory == Slick ? Slick : Semi)];
                case 3: return Types[bike ? BikeTrack : Slick];
                case 4: return Types[Drift];
                case 5: return Types[Drag];
                default: return Types[factory];
            }
        }
    }
}
