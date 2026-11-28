using System;
using GTA;
using GTA.Math;
using GTA.Native;

namespace MidnightClub.Physics
{
    /// <summary>Suspension from real chassis numbers instead of GTA's per-car guesses:
    /// ride frequency (Hz) by vehicle type and era - a luxury sedan floats at ~1.1 Hz, a sports car ~1.6, a supercar ~1.9,
    /// a race car on coilovers 2.4+ - becomes the spring rate (GTA's suspension force goes with frequency squared);
    /// damping ratios (compression ~0.3, rebound ~0.5, stiffer on coilovers) become the dampers; travel and roll
    /// stiffness (anti-roll bars, front/rear split by layout) follow the type. Each GTA suspension level adds
    /// frequency and takes travel; race springs and bars on top. The tuning sliders work on these values.
    /// Kerbs: GTA's wheel-on-kerb contact can fire the car up or sideways in a single frame ("kerb snapping");
    /// those one-frame spikes, far beyond what tyres and springs can produce, are filtered out.</summary>
    public static class Suspension
    {
        public static float RideHz(CarSpec s, int level, bool coilovers)
        {
            float f;
            switch (s.Kind ?? "")
            {
                case "Super": f = 1.9f; break;
                case "Sports": f = 1.6f; break;
                case "Coupes": case "Compacts": f = 1.35f; break;
                case "Muscle": f = 1.25f; break;
                case "Sedans": f = 1.2f; break;
                case "SportsClassics": f = 1.3f; break;
                case "SUVs": f = 1.15f; break;
                case "Vans": case "Commercial": case "Industrial": case "Utility": case "Service": f = 1.1f; break;
                case "OffRoad": f = 1.0f; break;
                default: f = 1.3f; break;
            }
            if (s.Year < 1975) f *= 0.85f; else if (s.Year < 1990) f *= 0.93f;
            return f + 0.15f * level + (coilovers ? 0.25f : 0f);
        }

        public static void Apply(HandlingData h, CarSpec s, int level, bool coilovers, bool raceBars)
        {
            if (s.Bike) return;   // bikes keep GTA's own (their suspension model is separate)
            float f = RideHz(s, level, coilovers);
            float zc = coilovers ? 0.36f : 0.28f, zr = coilovers ? 0.62f : 0.48f;
            h.SuspensionForce = 0.95f * f * f;
            h.SuspensionCompressionDamping = 3.2f * zc * f;
            h.SuspensionReboundDamping = 3.2f * zr * f;
            float up, low, arb;
            switch (s.Kind ?? "")
            {
                case "Super": up = 0.08f; low = -0.09f; arb = 0.9f; break;
                case "Sports": up = 0.10f; low = -0.11f; arb = 0.75f; break;
                case "SUVs": case "Vans": up = 0.16f; low = -0.18f; arb = 0.6f; break;
                case "OffRoad": up = 0.22f; low = -0.25f; arb = 0.3f; break;
                case "Muscle": case "Sedans": up = 0.12f; low = -0.14f; arb = 0.45f; break;
                default: up = 0.12f; low = -0.13f; arb = 0.55f; break;
            }
            float tr = 1f - 0.08f * level - (coilovers ? 0.06f : 0f);
            h.SuspensionUpperLimit = up * tr; h.SuspensionLowerLimit = low * tr;
            h.AntiRollBarForce = arb * (1f + 0.10f * level) * (raceBars ? 1.15f : 1f);
            h.AntiRollBarBiasFront = s.FrontDrive >= 0.95f ? 0.62f : s.Layout == "RR" ? 0.47f : s.Layout == "MR" || s.Layout == "AWD-M" ? 0.52f : 0.57f;
            h.SuspensionBiasFront = 0.5f + (s.FrontPct - 0.5f) * 0.6f;         // springs follow the axle loads
            h.TractionSpringDeltaMax = 0.10f + 0.4f * (up - low) * 0.5f;
        }

        // ---- kerb-snap filter (per frame, on the ground)
        private static Vector3 lastV, lastW; private static int lastHandle; private static bool have;
        public static bool Filtered { get; private set; }
        public static void Filter(Vehicle car, float dt, float mu)
        {
            Filtered = false;
            if (car.Handle != lastHandle) { lastHandle = car.Handle; have = false; }
            var v = car.Velocity; var w = car.WorldRotationVelocity;
            if (have && car.IsOnAllWheels == false && car.HeightAboveGround > 1.5f) have = false;
            if (have && dt > 0f)
            {
                var up = car.UpVector; var right = car.RightVector;
                var dv = v - lastV;
                float dUp = Vector3.Dot(dv, up), dSide = Vector3.Dot(dv, right);
                float maxUp = 9.81f * 2.5f * dt + 0.6f, maxSide = mu * 9.81f * 1.8f * dt + 0.7f;
                bool crash = dv.Length() > 6f || Function.Call<bool>(Hash.HAS_ENTITY_COLLIDED_WITH_ANYTHING, car);   // a real impact (body contact): leave it to the game; wheel-on-kerb contacts are not body collisions
                var nv = v;
                if (!crash && dUp > maxUp) { nv -= up * ((dUp - maxUp) * 0.85f); Filtered = true; }
                if (!crash && Math.Abs(dSide) > maxSide) { nv -= right * ((Math.Abs(dSide) - maxSide) * 0.75f * Math.Sign(dSide)); Filtered = true; }
                var fwd = car.ForwardVector;
                var dw = w - lastW;
                float dRoll = Vector3.Dot(dw, fwd), dPitch = Vector3.Dot(dw, right), maxRate = 18f * dt + 0.6f;
                var nw = w;
                if (!crash && Math.Abs(dRoll) > maxRate) { nw -= fwd * ((Math.Abs(dRoll) - maxRate) * 0.8f * Math.Sign(dRoll)); Filtered = true; }
                if (!crash && Math.Abs(dPitch) > maxRate) { nw -= right * ((Math.Abs(dPitch) - maxRate) * 0.8f * Math.Sign(dPitch)); Filtered = true; }
                if (nv != v) { car.Velocity = nv; v = nv; }
                if (nw != w) { Function.Call(Hash.SET_ENTITY_ANGULAR_VELOCITY, car, nw.X, nw.Y, nw.Z); w = nw; }
            }
            lastV = v; lastW = w; have = true;
        }
        /// <summary>Tell the filter about velocity the simulation itself changed this frame.</summary>
        public static void Set(Vector3 v, Vector3 w) { lastV = v; lastW = w; }
    }
}
