using System;

namespace MidnightClub.Physics
{
    /// <summary>Real performance figures worked out from the physics model itself (engine curve, gearing, mass with
    /// rotating inertia, traction with load transfer and tyre load sensitivity, aero drag and downforce, rolling
    /// resistance, shift times).</summary>
    public sealed class PerfResult
    {
        public float Hp, HpRpm, Nm, NmRpm, Kg, FrontPct, CgH, HpPerTonne, TopKmh, T100, T200, Quarter, QuarterKmh, Brake100, LatG, DownforceKg200;
        public bool TopByRedline, CanWheelie; public int Gears; public string Layout;
    }

    public static class Perf
    {
        public const float Rho = 1.225f, G = 9.81f;

        public static float DragTopSpeed(CarSpec s, EngineState e, float massKg, float eta, Tyres.Mf tyre)
        {
            float p = Math.Max(1f, e.PeakHp) * 745.7f * eta, cda = Math.Max(0.2f, s.CdA), crr = tyre != null ? tyre.Rolling : 0.012f;
            float lo = 1f, hi = 200f;
            for (int i = 0; i < 50; i++) { float v = (lo + hi) / 2f; float need = 0.5f * Rho * cda * v * v * v + crr * massKg * G * v; if (need > p) hi = v; else lo = v; }
            return lo;
        }

        public static PerfResult Run(CarSpec s, EngineState e, Gearing g, float massKg, float cda, float cla, float aeroFront, Tyres.Mf t, float frontDrive, float frontPct, float cgH, int year)
        {
            var res = new PerfResult { Hp = e.PeakHp, HpRpm = e.PeakHpRpm, Nm = e.PeakNm, NmRpm = e.PeakNmRpm, Kg = s.Bike ? massKg - 75f : massKg, FrontPct = frontPct, CgH = cgH, Gears = g.Gears, Layout = s.LayoutLabel };
            res.HpPerTonne = e.PeakHp / Math.Max(0.1f, res.Kg / 1000f);
            float L = Math.Max(1f, s.Wheelbase), lr = frontPct * L;                     // CoG to rear axle
            float fz0 = massKg * G / (s.Bike ? 2f : 4f);
            float shiftT = year < 1990 ? 0.45f : year < 2008 ? 0.28f : 0.12f; if (s.Bike) shiftT = 0.08f;
            if (s.Sheet != null && s.Sheet.drivetrain != null && s.Sheet.drivetrain.gearbox != null && s.Sheet.drivetrain.gearbox.shiftMs > 0) shiftT = s.Sheet.drivetrain.gearbox.shiftMs / 1000f;   // the real gearbox's shift time
            float wheelieA = G * lr / Math.Max(0.2f, cgH);
            // ---- standing start
            float v = 0f, x = 0f, time = 0f, a = 0f, shiftLeft = 0f; int gear = 1;
            res.T100 = res.T200 = res.Quarter = -1f;
            float dt = 0.004f;
            while (time < 90f)
            {
                float down = 0.5f * Rho * Math.Max(0f, cla) * v * v;
                float drag = 0.5f * Rho * cda * v * v + t.Rolling * (massKg * G + down);
                float F = 0f;
                if (shiftLeft > 0f) shiftLeft -= dt;
                else
                {
                    float rpm = g.Rpm(gear, v);
                    if (gear == 1) rpm = Math.Max(rpm, Math.Min(e.PeakNmRpm > 0 ? e.PeakNmRpm : s.NmRpm, s.Redline * 0.6f));
                    if (rpm >= s.Redline * 0.995f && gear < g.Gears) { gear++; shiftLeft = shiftT; continue; }
                    if (gear < g.Gears)
                    {
                        float fNow = e.SteadyTorque(rpm) * g.Overall(gear), fNext = e.SteadyTorque(g.Rpm(gear + 1, v)) * g.Overall(gear + 1);
                        if (fNext > fNow * 1.01f && g.Rpm(gear + 1, v) > s.Idle * 1.5f) { gear++; shiftLeft = shiftT; continue; }
                    }
                    F = e.SteadyTorque(Math.Min(rpm, s.Redline)) * g.Overall(gear) * g.Efficiency / g.TyreR;
                    // traction: driven axle load with load transfer (from last step's acceleration) and aero
                    float transfer = massKg * a * cgH / L;
                    float rearZ = massKg * G * (1f - frontPct) + transfer + down * (1f - aeroFront), frontZ = Math.Max(0f, massKg * G * frontPct - transfer + down * aeroFront);
                    float lim;
                    if (s.Bike) lim = Tyres.Mu(t, rearZ, fz0, false) * rearZ;
                    else if (frontDrive >= 0.95f) lim = 2f * Tyres.Mu(t, frontZ / 2f, fz0, false) * frontZ / 2f;
                    else if (frontDrive <= 0.05f) lim = 2f * Tyres.Mu(t, rearZ / 2f, fz0, false) * rearZ / 2f;
                    else lim = 2f * Tyres.Mu(t, frontZ / 2f, fz0, false) * frontZ / 2f + 2f * Tyres.Mu(t, rearZ / 2f, fz0, false) * rearZ / 2f;
                    if (lim / massKg > wheelieA && F / massKg > wheelieA) res.CanWheelie = true;
                    F = Math.Min(F, lim);
                    if (s.Bike) F = Math.Min(F, massKg * wheelieA * 1.02f);                   // the front lifts: wheelie-limited
                }
                float overall = g.Overall(gear);
                float meff = massKg * (1.04f + 0.0025f * overall * overall * (s.Bike ? 0.3f : 1f));
                a = (F - drag) / meff;
                v = Math.Max(0f, v + a * dt); x += v * dt; time += dt;
                if (res.T100 < 0f && v >= 100f / 3.6f) res.T100 = time;
                if (res.T200 < 0f && v >= 200f / 3.6f) res.T200 = time;
                if (res.Quarter < 0f && x >= 402.34f) { res.Quarter = time; res.QuarterKmh = v * 3.6f; }
                if (res.T200 > 0f && res.Quarter > 0f) break;
                if (F > 0f && a < 0.02f && time > 5f) break;
            }
            // ---- top speed: power against drag, or the rev limiter in top gear
            float top = 0f;
            for (float vv = 5f; vv < 160f; vv += 0.25f)
            {
                float best = 0f;
                for (int gg = 1; gg <= g.Gears; gg++) { float rpm = g.Rpm(gg, vv); if (rpm > s.Redline || rpm < s.Idle) continue; best = Math.Max(best, e.SteadyTorque(rpm) * g.Overall(gg) * g.Efficiency / g.TyreR); }
                float down = 0.5f * Rho * Math.Max(0f, cla) * vv * vv;
                if (best < 0.5f * Rho * cda * vv * vv + t.Rolling * (massKg * G + down)) break;
                top = vv;
            }
            res.TopKmh = top * 3.6f;
            res.TopByRedline = g.Rpm(g.Gears, top + 0.5f) > s.Redline;
            // ---- braking 100-0 (ABS at ~97 % of peak friction) and lateral grip at 100 km/h
            float vb = 100f / 3.6f, d = 0f;
            while (vb > 0.1f)
            {
                float down = 0.5f * Rho * Math.Max(0f, cla) * vb * vb;
                float muAvg = Tyres.Mu(t, (massKg * G + down) / (s.Bike ? 2f : 4f), fz0, false);
                float dec = (muAvg * (massKg * G + down) * 0.97f + 0.5f * Rho * cda * vb * vb) / massKg;
                if (s.Bike) dec = Math.Min(dec, G * (L - lr) / Math.Max(0.2f, cgH));    // stoppie limit
                d += vb * 0.002f; vb -= dec * 0.002f;
            }
            res.Brake100 = d;
            float latA = G, v100 = 100f / 3.6f, dn = 0.5f * Rho * Math.Max(0f, cla) * v100 * v100;
            float track = Math.Max(0.5f, s.Track);
            for (int i = 0; i < 8 && !s.Bike; i++)
            {
                float tf = Math.Min(0.95f, latA * cgH / (G * track));
                float wz = (massKg * G + dn) / 4f;
                float fo = wz * (1f + tf), fi = wz * (1f - tf);
                float mu = (Tyres.Mu(t, fo, fz0, true) * fo + Tyres.Mu(t, fi, fz0, true) * fi) / (fo + fi);
                latA = mu * (massKg * G + dn) / massKg;
            }
            if (s.Bike) latA = Tyres.Mu(t, fz0, fz0, true) * G;
            res.LatG = latA / G;
            res.DownforceKg200 = 0.5f * Rho * cla * (200f / 3.6f) * (200f / 3.6f) / G;
            return res;
        }
    }
}
