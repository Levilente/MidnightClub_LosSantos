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

        /// <summary>Applied relative to the car's own GTA values (whose units SHVDN exposes in GTA's internal form): the
        /// stock car is taken as the type's base frequency, and the real ratios (frequency squared for springs,
        /// frequency x damping ratio for dampers, roll stiffness, travel) scale it from there.</summary>
        public static void Apply(HandlingData h, CarSpec s, int level, bool coilovers, bool raceBars, byte[] raw)
        {
            if (s.Bike) return;   // bikes keep GTA's own (their suspension model is separate)
            float fBase = RideHz(s, 0, false), f = RideHz(s, level, coilovers);
            float era = s.Year < 1975 ? 0.85f : s.Year < 1990 ? 0.93f : 1f;
            float k = f / (fBase / era);                            // vs. GTA's (era-less) stock car
            float zc = coilovers ? 1.25f : 1f, zr = coilovers ? 1.3f : 1f;
            h.SuspensionForce *= k * k;
            h.SuspensionCompressionDamping *= k * zc;
            h.SuspensionReboundDamping *= k * zr;
            float tr = (1f - 0.04f * level - (coilovers ? 0.03f : 0f)) / era;
            h.SuspensionUpperLimit *= tr; h.SuspensionLowerLimit *= tr;
            h.AntiRollBarForce *= era * (1f + 0.10f * level) * (raceBars ? 1.15f : 1f);
            float arbBias = s.FrontDrive >= 0.95f ? 0.62f : s.Layout == "RR" ? 0.47f : s.Layout == "MR" || s.Layout == "AWD-M" ? 0.52f : 0.57f;
            HandlingMem.SetBias(h, HandlingMem.ArbBias, HandlingMem.GetBias(h, HandlingMem.ArbBias) * 0.5f + arbBias * 0.5f, raw);
            Core.Log.Info("Suspension " + s.Id + ": " + f.ToString("0.00") + " Hz (x" + (k * k).ToString("0.00") + " springs), travel x" + tr.ToString("0.00"));
        }

        // ---- kerb-snap filter (per frame, on the ground)
        private static Vector3 lastV, lastW; private static int lastHandle; private static bool have;
        public static bool Filtered { get; private set; }
        public static int Count;
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
                float maxUp = 9.81f * 1.5f * dt + 0.35f, maxSide = mu * 9.81f * 1.3f * dt + 0.25f;
                bool crash = dv.Length() > 6f;                    // a real impact: leave it to the game
                var nv = v;
                if (!crash && dUp > maxUp) { nv -= up * ((dUp - maxUp) * 0.85f); Filtered = true; }
                if (!crash && Math.Abs(dSide) > maxSide) { nv -= right * ((Math.Abs(dSide) - maxSide) * 0.75f * Math.Sign(dSide)); Filtered = true; }
                var fwd = car.ForwardVector;
                var dw = w - lastW;
                float dRoll = Vector3.Dot(dw, fwd), dPitch = Vector3.Dot(dw, right), maxRate = 10f * dt + 0.35f;
                var nw = w;
                if (!crash && Math.Abs(dRoll) > maxRate) { nw -= fwd * ((Math.Abs(dRoll) - maxRate) * 0.8f * Math.Sign(dRoll)); Filtered = true; }
                if (!crash && Math.Abs(dPitch) > maxRate) { nw -= right * ((Math.Abs(dPitch) - maxRate) * 0.8f * Math.Sign(dPitch)); Filtered = true; }
                if (Filtered) Count++;
                if (nv != v) { car.Velocity = nv; v = nv; }
                if (nw != w) { Function.Call(Hash.SET_ENTITY_ANGULAR_VELOCITY, car, nw.X, nw.Y, nw.Z); w = nw; }
            }
            lastV = v; lastW = w; have = true;
        }
        /// <summary>Tell the filter about velocity the simulation itself changed this frame.</summary>
        public static void Set(Vector3 v, Vector3 w) { lastV = v; lastW = w; }
    }
}
