using System;
using MidnightClub.Persistence;
using MidnightClub.Vehicles;

namespace MidnightClub.Physics
{
    /// <summary>An engine as installed: full-load torque curve (with boost), the off-boost curve, boost behaviour
    /// (spool rpm, lag), supercharger drive loss, and the live boost state.</summary>
    public sealed class EngineState
    {
        public const int N = 48;
        public float[] Full = new float[N], NoBoost = new float[N];
        public float Redline, Idle, MaxRpm, PeakHp, PeakHpRpm, PeakNm, PeakNmRpm;
        public Induction Ind; public float BoostBar, SpoolTau = 0.5f, ScLoss; public bool Als, Kit;
        public string InductionLabel = "Naturally aspirated";
        // live
        public float BoostState, BoostNowBar;
        public float At(float[] t, float rpm)
        {
            float x = Math.Max(0f, rpm) / MaxRpm * (N - 1);
            int i = (int)x; if (i >= N - 1) return t[N - 1];
            float f = x - i; return t[i] + (t[i + 1] - t[i]) * f;
        }
        /// <summary>Torque now (Nm) at an rpm and throttle, advancing the boost state.</summary>
        public float Torque(float rpm, float throttle, float dt)
        {
            if (rpm > Redline * 1.01f) return 0f;                                   // fuel cut
            float full = At(Full, rpm), nb = At(NoBoost, rpm);
            if (Ind == Induction.Turbo)
            {
                float want = Math.Max(0f, Math.Min(1f, throttle));
                if (Als && throttle < 0.1f) want = Math.Max(want, 0.7f);             // anti-lag keeps it spinning
                float tau = want > BoostState ? SpoolTau : 0.25f;
                BoostState += (want - BoostState) * Math.Min(1f, dt / Math.Max(0.05f, tau));
                BoostNowBar = BoostBar * BoostState * Math.Min(1f, (full - nb) / Math.Max(1f, nb * 0.85f * BoostBar));
                return nb + (full - nb) * BoostState;
            }
            BoostState = Math.Max(0f, Math.Min(1f, throttle));
            BoostNowBar = Ind == Induction.Supercharged ? BoostBar * BoostState * Math.Min(1f, rpm / (Redline * 0.35f)) : 0f;
            return full - (Ind == Induction.Supercharged ? full * ScLoss * (rpm / Redline) * (rpm / Redline) * (1f - BoostState * 0.5f) : 0f);
        }
        /// <summary>Full-load torque curve at the boost the engine has right now (for the native drivetrain, which looks
        /// it up at physics rate). Advances the boost state like Torque().</summary>
        public void CurveNow(float[] dst, float rpm, float throttle, float dt)
        {
            Torque(rpm, throttle, dt);
            for (int i = 0; i < N && i < dst.Length; i++)
            {
                float r = MaxRpm * i / (N - 1), full = Full[i], nb = NoBoost[i];
                if (Ind == Induction.Turbo) dst[i] = nb + (full - nb) * BoostState;
                else if (Ind == Induction.Supercharged) dst[i] = full - full * ScLoss * (r / Redline) * (r / Redline) * (1f - Math.Max(0f, Math.Min(1f, throttle)) * 0.5f);
                else dst[i] = full;
            }
        }
        public float SteadyTorque(float rpm) { return rpm > Redline * 1.01f ? 0f : At(Full, rpm); }
    }

    /// <summary>Builds torque curves from the spec (cubic Hermite through idle, peak torque, peak power and the
    /// redline, with the slope that puts the power peak exactly where the spec says) and applies upgrades:
    /// GTA engine levels, ECU stages, turbo kits / bigger turbos (3 stages), supercharger kits (3 stages),
    /// anti-lag, straight pipe and engine swaps. Turbo and supercharger are exclusive: fitting one removes the other.</summary>
    public static class Engine
    {
        public static int TurboStage(OwnedVehicle r) { return Setup.Has(r, "turbo3") ? 3 : Setup.Has(r, "turbo2") ? 2 : Setup.Has(r, "turbo1") ? 1 : 0; }
        public static int ScStage(OwnedVehicle r) { return Setup.Has(r, "sc3") ? 3 : Setup.Has(r, "sc2") ? 2 : Setup.Has(r, "sc1") ? 1 : 0; }

        public static EngineState Build(CarSpec s, OwnedVehicle r, int engineMod)
        {
            var e = new EngineState { Redline = s.Redline, Idle = s.Idle };
            e.MaxRpm = s.Redline * 1.06f;
            var rpm = new float[EngineState.N];
            for (int i = 0; i < EngineState.N; i++) rpm[i] = e.MaxRpm * i / (EngineState.N - 1);
            // factory full-load curve
            var fac = new float[EngineState.N];
            float thp = s.Hp * 7121f / s.HpRpm, nm = Math.Max(s.Nm, thp * 1.01f), nmRpm = Math.Min(s.NmRpm, s.HpRpm * 0.92f);
            float tred = (s.Ind == Induction.NA ? 0.93f : 0.88f) * s.Hp * 7121f / s.Redline;
            if (s.Ind == Induction.Electric)
            {
                float baseRpm = s.Hp * 7121f / nm;
                for (int i = 0; i < EngineState.N; i++) fac[i] = rpm[i] <= baseRpm ? nm : s.Hp * 7121f / Math.Max(1f, rpm[i]);
            }
            else
            {
                float low = s.Ind == Induction.Turbo ? (s.Diesel ? 0.55f : 0.42f) : s.Ind == Induction.Supercharged ? 0.72f : s.Cyl >= 8 && s.Litres >= 5f ? 0.78f : 0.62f;
                float plateauEnd = s.Ind == Induction.Turbo || s.Ind == Induction.Supercharged ? Math.Min(s.HpRpm * 0.9f, Math.Max(nmRpm * 1.05f, s.Hp * 7121f / nm)) : nmRpm;
                float[] px = { s.Idle * 0.6f, s.Idle, nmRpm, plateauEnd, s.HpRpm, s.Redline, e.MaxRpm };
                float[] py = { low * nm * 0.8f, low * nm, nm, nm * (plateauEnd > nmRpm * 1.01f ? 0.995f : 1f), thp, tred, tred * 0.9f };
                float[] pd = { (low * nm * 0.2f) / (s.Idle * 0.4f), (nm - low * nm) / Math.Max(100f, nmRpm - s.Idle) * 1.1f, 0f, -0.25f * nm / Math.Max(1f, s.HpRpm), -thp / s.HpRpm, -2.2f * tred / s.Redline, -2.2f * tred / s.Redline };
                for (int i = 0; i < EngineState.N; i++) fac[i] = Hermite(px, py, pd, rpm[i]);
                if (s.Hybrid) for (int i = 0; i < EngineState.N; i++) if (rpm[i] < nmRpm) fac[i] = Math.Max(fac[i], nm * (0.92f - 0.2f * (1f - rpm[i] / nmRpm)));
            }
            // off-boost (naturally aspirated equivalent) of the factory engine
            int ts = TurboStage(r), ss = ScStage(r);
            float facBoost = s.Boost > 0f ? s.Boost : s.Ind == Induction.Turbo ? 1.0f : s.Ind == Induction.Supercharged ? 0.7f : 0f;
            var na = new float[EngineState.N];
            for (int i = 0; i < EngineState.N; i++)
            {
                float b = s.Ind == Induction.Turbo ? Ramp(rpm[i], s.Redline * 0.2f, nmRpm) : s.Ind == Induction.Supercharged ? Ramp(rpm[i], s.Idle, s.Redline * 0.3f) : 0f;
                float k = s.Ind == Induction.Turbo ? 0.85f : 0.75f;
                na[i] = s.Ind == Induction.Turbo || s.Ind == Induction.Supercharged ? fac[i] / (1f + k * facBoost * b) : fac[i];
            }
            // the installed induction
            Induction ind = s.Ind; float bar = facBoost; float spoolLo = s.Redline * 0.2f, spoolHi = nmRpm; float tau = s.Diesel ? 0.7f : 0.5f;
            switch (s.Charger ?? "")
            {   // the factory charger's own character
                case "single turbo": tau = s.Diesel ? 0.7f : 0.6f; break;
                case "twin turbo": tau = 0.42f; break;
                case "twin-scroll turbo": tau = 0.45f; spoolHi = nmRpm * 0.95f; break;
                case "sequential twin turbo": tau = 0.35f; spoolLo = s.Redline * 0.15f; break;
                case "quad turbo": tau = 0.38f; break;
                case "electric-assisted turbo": tau = 0.18f; spoolLo = s.Idle; break;
                case "turbo + supercharger": tau = 0.25f; spoolLo = s.Idle; break;
                case "centrifugal supercharger": spoolLo = s.Idle; spoolHi = s.Redline * 0.9f; break;
            }
            bool keepFactory = true;
            if (s.Ind != Induction.Electric)
            {
                if (ts > 0)
                {
                    if (s.Ind == Induction.Turbo) { bar = facBoost * new[] { 1f, 1.2f, 1.4f, 1.65f }[ts]; spoolHi = nmRpm + new[] { 0f, 300f, 700f, 1200f }[ts]; tau *= new[] { 1f, 1.1f, 1.25f, 1.45f }[ts]; }
                    else { ind = Induction.Turbo; bar = Math.Max(new[] { 0f, 0.5f, 0.8f, 1.1f }[ts], facBoost * new[] { 0f, 1.0f, 1.25f, 1.5f }[ts]); spoolLo = s.Redline * 0.25f; spoolHi = s.Redline * new[] { 0f, 0.45f, 0.52f, 0.6f }[ts]; tau = new[] { 0.5f, 0.45f, 0.6f, 0.85f }[ts]; e.Kit = true; }
                    keepFactory = false;
                }
                else if (ss > 0)
                {
                    if (s.Ind == Induction.Supercharged) bar = facBoost * new[] { 1f, 1.2f, 1.4f, 1.65f }[ss];
                    else { ind = Induction.Supercharged; bar = Math.Max(new[] { 0f, 0.45f, 0.7f, 0.95f }[ss], facBoost * new[] { 0f, 1.0f, 1.25f, 1.5f }[ss]); e.Kit = true; }
                    spoolLo = s.Idle; spoolHi = s.Redline * 0.3f;
                    keepFactory = false;
                }
            }
            e.Ind = ind; e.BoostBar = ind == Induction.Turbo || ind == Induction.Supercharged ? bar : 0f;
            // tuning on top
            int lvl = Math.Max(0, Math.Min(4, engineMod + 1)), ecu = r != null ? Math.Max(0, Math.Min(3, r.EcuLevel)) : 0;
            bool boosted = ind == Induction.Turbo || ind == Induction.Supercharged;
            float tuneMult = (1f + 0.03f * lvl) * (1f + (boosted ? 0.07f : 0.03f) * ecu) * (Setup.Has(r, "pipe") ? 1.02f : 1f) * (1f + (r != null ? Setup.SwapPower(r.EngineSound) : 0f));
            if (ind == Induction.Turbo) bar *= 1f + 0.08f * ecu;
            e.BoostBar = boosted ? bar : 0f;
            e.Als = ind == Induction.Turbo && Setup.Has(r, "als");
            if (e.Als) { tau *= 0.35f; spoolHi -= 500f; }
            if (Setup.Has(r, "pipe")) tau *= 0.95f;
            e.SpoolTau = tau;
            e.ScLoss = ind == Induction.Supercharged ? 0.03f + 0.02f * Math.Max(ss, 1) : 0f;
            for (int i = 0; i < EngineState.N; i++)
            {
                float full;
                if (keepFactory) full = fac[i];
                else if (ind == Induction.Turbo) full = na[i] * (1f + 0.85f * bar * Ramp(rpm[i], spoolLo, spoolHi));
                else if (ind == Induction.Supercharged) full = na[i] * (1f + 0.75f * bar * Ramp(rpm[i], spoolLo, spoolHi));
                else full = na[i];
                e.Full[i] = full * tuneMult;
                e.NoBoost[i] = (ind == Induction.Turbo ? na[i] : full) * tuneMult;
            }
            // peaks
            for (int i = 0; i < EngineState.N; i++)
            {
                if (rpm[i] > s.Redline * 1.001f) break;
                float t = e.Full[i] - (ind == Induction.Supercharged ? e.Full[i] * e.ScLoss * (rpm[i] / s.Redline) * (rpm[i] / s.Redline) * 0.5f : 0f);
                float hp = t * rpm[i] / 7121f;
                if (t > e.PeakNm) { e.PeakNm = t; e.PeakNmRpm = rpm[i]; }
                if (hp > e.PeakHp) { e.PeakHp = hp; e.PeakHpRpm = rpm[i]; }
            }
            e.InductionLabel = ind == Induction.Electric ? "Electric" : ind == Induction.Turbo ? (e.Kit ? "Turbo kit stage " + ts : ts > 0 ? "Uprated turbo stage " + ts : "Turbocharged") + " · " + e.BoostBar.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " bar"
                : ind == Induction.Supercharged ? (e.Kit ? "Supercharger kit stage " + ss : ss > 0 ? "Uprated supercharger stage " + ss : "Supercharged") + " · " + e.BoostBar.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " bar" : "Naturally aspirated";
            return e;
        }

        private static float Ramp(float x, float a, float b) { if (b <= a) return x >= a ? 1f : 0f; float t = Math.Max(0f, Math.Min(1f, (x - a) / (b - a))); return t * t * (3f - 2f * t); }

        private static float Hermite(float[] x, float[] y, float[] d, float v)
        {
            if (v <= x[0]) return Math.Max(0f, y[0] + d[0] * (v - x[0]));
            for (int i = 0; i < x.Length - 1; i++)
            {
                if (v > x[i + 1]) continue;
                float h = x[i + 1] - x[i]; if (h < 1f) return y[i + 1];
                float t = (v - x[i]) / h, t2 = t * t, t3 = t2 * t;
                float r = (2 * t3 - 3 * t2 + 1) * y[i] + (t3 - 2 * t2 + t) * h * d[i] + (-2 * t3 + 3 * t2) * y[i + 1] + (t3 - t2) * h * d[i + 1];
                return Math.Max(0f, r);
            }
            return Math.Max(0f, y[y.Length - 1]);
        }
    }

    /// <summary>Gear ratios and final drive. Stock: geared so the power peak lands at the drag-limited top speed in the
    /// top gear, with an era/type spread. Race gearbox: close-ratio six-speed. Final drive and (with the fully
    /// adjustable gearbox) every gear can be tuned.</summary>
    public sealed class Gearing
    {
        public int Gears; public float[] Ratios, BaseRatios, Full;   // Full: every real gear (own gearbox), Ratios: spread over GTA's gear count
        public int FullGears { get { return Full != null ? Full.Length : Gears; } }
        public float FullOverall(int gear) { if (Full == null) return Overall(gear); int g = Math.Max(1, Math.Min(Full.Length, gear)); return Full[g - 1] * Final; }
        public float Final, BaseFinal, TyreR, Efficiency; public bool RaceBox;
        public float Overall(int gear) { int g = Math.Max(1, Math.Min(Gears, gear)); return Ratios[g - 1] * Final; }
        /// <summary>Speed (m/s) at an rpm in a gear.</summary>
        public float Speed(int gear, float rpm) { return rpm * 2f * (float)Math.PI / 60f * TyreR / Overall(gear); }
        public float Rpm(int gear, float v) { return v / TyreR * Overall(gear) * 60f / (2f * (float)Math.PI); }

        public static readonly float[] Close6 = { 3.15f, 2.18f, 1.62f, 1.27f, 1.03f, 0.86f };

        public static Gearing Build(CarSpec s, EngineState e, OwnedVehicle r, int gears, float massKg, Tyres.Mf tyre)
        {
            var g = new Gearing { Gears = Math.Max(1, gears), TyreR = s.TyreR, RaceBox = Setup.Has(r, "gearbox") };
            g.Efficiency = s.Bike ? 0.92f : s.FrontDrive >= 0.95f ? 0.90f : s.FrontDrive > 0.05f ? 0.83f : 0.88f;
            g.Ratios = new float[g.Gears];
            var sb = s.Sheet != null && s.Sheet.drivetrain != null ? s.Sheet.drivetrain.gearbox : null;
            if (sb != null && sb.ratios != null && sb.ratios.Length > 0 && !(g.RaceBox && g.Gears == 6))
            {   // the real gearbox: its own ratios and final drive (spread over GTA's gear count if that differs)
                g.Efficiency = s.Sheet.drivetrain.efficiency > 0.5f ? s.Sheet.drivetrain.efficiency : g.Efficiency;
                int n = sb.ratios.Length;
                for (int i = 0; i < g.Gears; i++) g.Ratios[i] = g.Gears == n ? sb.ratios[i] : sb.ratios[g.Gears == 1 ? 0 : (int)Math.Round(i * (n - 1) / (float)(g.Gears - 1))];
                g.Final = sb.finalDrive > 0.5f ? sb.finalDrive : 3.5f;
                g.BaseFinal = g.Final; g.BaseRatios = (float[])g.Ratios.Clone();
                g.Final *= 1f - 0.02f * Setup.TuneValue(r, Setup.FinalDriveIndex);
                if (Setup.Has(r, "gearbox2")) for (int i = 0; i < g.Gears && i < 6; i++) g.Ratios[i] *= 1f - 0.03f * Setup.TuneValue(r, Setup.GearIndex0 + i);
                g.Full = new float[Math.Min(11, n)];
                for (int i = 0; i < g.Full.Length; i++) g.Full[i] = sb.ratios[i] * (Setup.Has(r, "gearbox2") && i < 6 ? 1f - 0.03f * Setup.TuneValue(r, Setup.GearIndex0 + i) : 1f);
                return g;
            }
            if (g.RaceBox && g.Gears == 6) Array.Copy(Close6, g.Ratios, 6);
            else if (g.Gears == 1) g.Ratios[0] = 1f;
            else
            {
                float spread = g.Gears == 2 ? 1.8f : g.Gears == 3 ? 2.4f : g.Gears == 4 ? 3.0f : g.Gears == 5 ? (s.Year >= 1995 ? 4.0f : 3.6f) : 5.0f;
                if (s.Bike) spread = 2.7f;
                if (s.Diesel) spread = 5.5f;
                float rTop = g.Gears >= 6 ? 0.75f : g.Gears == 5 ? 0.82f : 1.0f;
                for (int i = 0; i < g.Gears; i++) { float t = (float)Math.Pow(i / (float)(g.Gears - 1), 0.88); g.Ratios[i] = rTop * spread * (float)Math.Pow(1f / spread, t); }
            }
            // final drive: the drag-limited top speed is reached at the power peak in the top gear
            float vTop = Perf.DragTopSpeed(s, e, massKg, g.Efficiency, tyre);
            float wantRedlineTop = vTop * (s.Redline / Math.Max(1000f, e.PeakHpRpm > 0 ? e.PeakHpRpm : s.HpRpm));
            float ratioTop = g.Ratios[g.Gears - 1];
            g.Final = s.Redline * 2f * (float)Math.PI / 60f * g.TyreR / (wantRedlineTop * ratioTop);
            if (s.Ind == Induction.Electric && g.Gears == 1) g.Final = Math.Max(5f, Math.Min(12f, g.Final));
            else g.Final = Math.Max(s.Bike ? 4f : 2.2f, Math.Min(s.Bike ? 14f : 6.5f, g.Final));
            // tuning
            g.BaseFinal = g.Final; g.BaseRatios = (float[])g.Ratios.Clone();
            g.Final *= 1f - 0.02f * Setup.TuneValue(r, Setup.FinalDriveIndex);
            if (Setup.Has(r, "gearbox2")) for (int i = 0; i < g.Gears && i < 6; i++) g.Ratios[i] *= 1f - 0.03f * Setup.TuneValue(r, Setup.GearIndex0 + i);
            g.Full = (float[])g.Ratios.Clone();
            return g;
        }
    }
}
