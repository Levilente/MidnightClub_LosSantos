using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Persistence;

namespace MidnightClub.Physics
{
    /// <summary>Every vehicle around you runs on the native simulation (MidnightClubPhysics.asi): your car, the race
    /// opponents, traffic, police, trucks, motorbikes and parked cars. Each one is registered with its real data
    /// (engine curve, gearbox, tyres, brakes, differentials, suspension, aero, mass); the .asi reads the driver's inputs
    /// (player or AI) straight from the vehicle at physics rate. Real masses go into each model's handling, so
    /// collisions carry real momentum; everything is put back when you leave the mode.</summary>
    public static class SimWorld
    {
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern int MCP_DriveAvailable();
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern int MCP_VehSet(long veh, float[] p, int n);
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern void MCP_VehDrop(long veh);
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern void MCP_VehDropAll();
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern int MCP_VehCount();
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern int MCP_VehState(long veh, float[] o, int n);
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern int MCP_AxisFallbacks();

        public const int VP = 212, CurveN = 48;
        /// <summary>Opponents in the current race (handles), set by the race code: they get the race assists.</summary>
        public static readonly HashSet<int> Rivals = new HashSet<int>();
        public static bool Available { get; private set; }
        public static int Count { get; private set; }

        private sealed class Entry { public Vehicle Car; public long Addr; public string Key; public int Refresh; public bool Player; public bool Seen; }
        private sealed class ModelStock { public byte[] Raw; public int Hash; }
        private static readonly Dictionary<int, Entry> reg = new Dictionary<int, Entry>();
        private static readonly Dictionary<int, ModelStock> models = new Dictionary<int, ModelStock>();
        private static readonly Dictionary<string, float[]> paramCache = new Dictionary<string, float[]>();
        private static int avail = -1, nextScan, nextLog, playerHandle; private static long playerAddr;
        private static readonly float[] st = new float[74];
        private static float lastSteps, lastOwn;

        // live state of your car
        public static float Rpm { get; private set; }
        public static bool TcsOn { get; private set; }
        public static bool AbsOn { get; private set; }
        public static bool EscOn { get; private set; }
        public static float Boost { get; private set; }
        public static bool PlayerActive { get; private set; }
        /// <summary>Your tyres (front left, front right, rear left, rear right): temperature (C), wear (0..1), hot pressure (psi).</summary>
        public static readonly float[] TyreTemp = new float[4], TyreWear = new float[4], TyrePsi = new float[4];
        /// <summary>Front tyres' slip angle (tan) and the tyre's peak slip angle (tan), for the gamepad assist.</summary>
        public static float FrontSlip { get; private set; }
        public static float PeakSlip { get; private set; }
        public static float OptTemp { get; private set; }
        /// <summary>Mass of the physics body GTA simulates for your car (kg).</summary>
        public static float BodyMass { get; private set; }
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern void MCP_VehTyresNew(long veh);
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private static extern int MCP_SimDump(string path);
        private static int nextDump;
        /// <summary>New tyres on your car (workshop repair / compound change): wear back to zero.</summary>
        public static void NewTyres(Vehicle car) { try { if (Available && car != null && car.Exists()) MCP_VehTyresNew(car.MemoryAddress.ToInt64()); } catch { } }

        private sealed class Pw { public float Mult; public int Until; }
        private static readonly Dictionary<int, Pw> power = new Dictionary<int, Pw>();
        /// <summary>Engine power multiplier for a vehicle (nitrous, slipstream, race catch-up, police), held for holdMs.
        /// Also sets GTA's own power cheat for when the native simulation isn't running.</summary>
        public static void Power(Vehicle car, float mult, int holdMs)
        {
            if (car == null || !car.Exists()) return;
            Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, car, mult);
            Pw w; int h = car.Handle;
            if (!power.TryGetValue(h, out w)) power[h] = w = new Pw { Mult = 1f };
            bool changed = Math.Abs(w.Mult - mult) > 0.02f;
            w.Mult = mult; w.Until = holdMs <= 0 ? 0 : Game.GameTime + holdMs;
            Entry e; if (changed && reg.TryGetValue(h, out e)) e.Refresh = 0;
        }
        private static float PowerOf(Vehicle car)
        {
            Pw w; if (!power.TryGetValue(car.Handle, out w)) return 1f;
            if (w.Until != 0 && Game.GameTime > w.Until) { power.Remove(car.Handle); Entry e; if (reg.TryGetValue(car.Handle, out e)) e.Refresh = 0; return 1f; }
            if (w.Until == 0) { power.Remove(car.Handle); return 1f; }
            return w.Mult;
        }

        public static void Tick(Vehicle playerCar, Vehicles.Drivetrain.Live playerLive, OwnedVehicle rec, Core.Config cfg, bool enabled)
        {
            if (!enabled) { Off(); return; }
            try
            {
                if (avail < 0)
                {
                    try { avail = Native.EnsureInstalled() && MCP_DriveAvailable() != 0 ? 1 : 0; }
                    catch (Exception) { avail = 0; }
                    Core.Log.Info("Native world simulation: " + (avail == 1 ? "available" : "not available (wheel step not hooked / old .asi)"));
                }
                Available = avail == 1;
                if (!Available) { PlayerActive = false; return; }
                bool pOk = playerCar != null && playerCar.Exists() && playerLive != null;
                if (pOk && playerCar.Handle != playerHandle) { playerHandle = playerCar.Handle; playerAddr = playerCar.MemoryAddress.ToInt64(); }
                if (!pOk) { playerHandle = 0; playerAddr = 0; }
                // your car: every frame (inputs come from the car itself; parameters change with tuning / weather)
                if (pOk)
                {
                    var p = Build(playerCar, playerLive, rec, cfg, true, false);
                    PlayerActive = p != null && MCP_VehSet(playerAddr, p, p.Length) != 0;
                    if (PlayerActive && MCP_VehState(playerAddr, st, st.Length) > 0)
                    {
                        Rpm = st[0]; TcsOn = st[1] > 0.5f; AbsOn = st[2] > 0.5f; EscOn = st[3] > 0.5f; Boost = st[4];
                        int nw = Math.Min(4, (int)st[9]);
                        for (int i = 0; i < 4; i++) { TyreTemp[i] = i < nw ? st[30 + i] : 0f; TyreWear[i] = i < nw ? st[40 + i] : 0f; TyrePsi[i] = i < nw ? st[50 + i] : 0f; }
                        FrontSlip = nw >= 2 ? 0.5f * (st[60] + st[61]) : 0f; PeakSlip = st[70]; OptTemp = playerLive.Tyre.TOpt; BodyMass = st[73];
                    }
                }
                else PlayerActive = false;
                if (Game.GameTime >= nextScan) { nextScan = Game.GameTime + 400; Scan(playerCar, cfg); }
                // developer mode: the last ~90 s of your car's physics, for analysis (probe\sim.bin)
                if (cfg.DevMode && PlayerActive && Game.GameTime > nextDump && Probe.BaseDir != null)
                {
                    nextDump = Game.GameTime + 30000;
                    try { var dir = System.IO.Path.Combine(Probe.BaseDir, "probe"); System.IO.Directory.CreateDirectory(dir); MCP_SimDump(System.IO.Path.Combine(dir, "sim.bin")); }
                    catch (EntryPointNotFoundException) { nextDump = int.MaxValue; }
                }
                if (Game.GameTime > nextLog && PlayerActive)
                {
                    nextLog = Game.GameTime + 20000;
                    Core.Log.Info("Native world: " + Count + " vehicles simulated; your car " + (st[5] - lastSteps) + " steps, " + (st[6] - lastOwn) + " tyre evaluations in 20 s, body coupling "
                        + (st[7] < -1.5f ? "MISMATCH (own forces off)" : "error " + (st[7] * 100f).ToString("0.0") + " %") + ", suspension " + (st[8] > 0.5f ? "own" : "measuring") + ", axis fallbacks " + MCP_AxisFallbacks()
                        + ", strut " + (st[71] > 0.5f ? "own" : st[71] < -0.5f ? "GTA direction" : "checking") + ", axes " + (st[72] > 0.5f ? "learned " + st[72].ToString("0") : "learning")
                        + ", tyres " + st[30].ToString("0") + "/" + st[31].ToString("0") + "/" + st[32].ToString("0") + "/" + st[33].ToString("0") + " C"
                        + ", GTA body " + st[73].ToString("0") + " kg (real " + (playerLive != null ? playerLive.Mass.ToString("0") : "?") + ")"
                        + ", rpm " + Rpm.ToString("0") + ", slip " + st[20].ToString("0.00") + "/" + st[21].ToString("0.00") + "/" + st[22].ToString("0.00") + "/" + st[23].ToString("0.00"));
                    lastSteps = st[5]; lastOwn = st[6];
                }
            }
            catch (Exception ex) { Core.Log.Error("Native world", ex); avail = 0; Off(); }
        }

        private static void Scan(Vehicle playerCar, Core.Config cfg)
        {
            foreach (var e in reg.Values) e.Seen = false;
            var ped = Game.Player.Character;
            var near = World.GetNearbyVehicles(ped.Position, 160f);
            Array.Sort(near, (a, b) => a.Position.DistanceToSquared(ped.Position).CompareTo(b.Position.DistanceToSquared(ped.Position)));
            int n = 0, built = 0;
            foreach (var car in near)
            {
                if (n >= 72) break;
                if (car == null || !car.Exists() || car.IsDead) continue;
                if (playerCar != null && car.Handle == playerCar.Handle) continue;
                var m = car.Model;
                if (!(m.IsCar || m.IsBike || m.IsQuadBike) || m.IsBicycle || m.IsTrain) continue;
                int wc = car.Wheels.Count; if (wc < 2 || wc > 10) continue;
                n++;
                Entry e;
                if (!reg.TryGetValue(car.Handle, out e)) { e = new Entry { Car = car, Addr = car.MemoryAddress.ToInt64() }; reg[car.Handle] = e; }
                e.Seen = true;
                if (power.ContainsKey(car.Handle)) PowerOf(car);
                if (Game.GameTime < e.Refresh && e.Addr == car.MemoryAddress.ToInt64()) continue;
                if (++built > 10) { e.Seen = true; continue; }          // spread the work: at most 10 vehicles (re)built per scan
                e.Refresh = Game.GameTime + 2000; e.Addr = car.MemoryAddress.ToInt64();
                try
                {
                    var L = Vehicles.Drivetrain.ForDisplay(car, null); if (L == null) continue;
                    ModelHandling(car, L);
                    var p = Build(car, L, null, cfg, false, Rivals.Contains(car.Handle));
                    if (p != null) MCP_VehSet(e.Addr, p, p.Length);
                    // GTA's own gearbox shifts on the real ratios
                    var tops = new float[L.Gear.Gears]; for (int i = 0; i < tops.Length; i++) tops[i] = L.Gear.Speed(i + 1, L.Spec.Redline);
                    VehMem.WriteRatios(car, tops);
                }
                catch (Exception ex) { Core.Log.Error("Native world vehicle", ex); }
            }
            var gone = new List<int>();
            foreach (var kv in reg) if (!kv.Value.Seen) gone.Add(kv.Key);
            var live = new HashSet<long>(); foreach (var kv in reg) if (kv.Value.Seen) live.Add(kv.Value.Addr);
            if (playerAddr != 0) live.Add(playerAddr);
            foreach (var h in gone) { try { if (!live.Contains(reg[h].Addr)) MCP_VehDrop(reg[h].Addr); } catch { } reg.Remove(h); power.Remove(h); }
            Count = MCP_VehCount();
        }

        /// <summary>Real mass and a minimal GTA drag (aero is simulated) in the model's handling; restored on exit.
        /// The model you drive is handled by SimPhysics.</summary>
        private static void ModelHandling(Vehicle car, Vehicles.Drivetrain.Live L)
        {
            int hash = car.Model.Hash;
            if (models.ContainsKey(hash)) return;
            if (SimPhysics.Instance != null && SimPhysics.Instance.AppliedModel == hash) return;
            var h = car.HandlingData; if (h == null || !h.IsValid) return;
            var ms = new ModelStock { Hash = hash, Raw = HandlingMem.Snapshot(h) };
            models[hash] = ms;
            if (L.Spec.Curated) h.Mass = L.Mass;
            h.InitialDragCoefficient = h.InitialDragCoefficient * 0.03f;
            // GTA's AI plans its corner and braking speeds from the handling grip: give it the real tyre's
            h.TractionCurveMax = SimPhysics.GripK * L.Tyre.MuY;
            h.TractionCurveMin = h.TractionCurveMax * (0.5f + 0.5f * L.Tyre.SlideY);
            HandlingMem.SyncAll(h, ms.Raw);
        }

        public static void ReleaseModel(int hash)
        {
            ModelStock ms; if (!models.TryGetValue(hash, out ms)) return;
            try { var h = HandlingData.GetByVehicleModel(new Model(hash)); if (h != null && h.IsValid && ms.Raw != null) HandlingMem.Restore(h, ms.Raw); } catch { }
            models.Remove(hash);
        }

        /// <summary>Parameter block for the .asi (layout = VParams in asi/sim_core.h).</summary>
        public static float[] Build(Vehicle car, Vehicles.Drivetrain.Live L, OwnedVehicle rec, Core.Config cfg, bool player, bool rival)
        {
            var s = L.Spec; var t = L.Tyre; var h = car.HandlingData;
            int wc = Math.Min(10, car.Wheels.Count);
            bool parked = !player && (car.Driver == null || !car.Driver.Exists());
            float wet = 1f - t.WetLoss * Math.Max(0f, Math.Min(1f, WorldSim.Weather.Wetness));
            int tcs, abs, esc;
            if (player) { tcs = cfg.TcsLevel; abs = cfg.AbsLevel; esc = cfg.EscLevel; }
            else if (rival) { tcs = 7; abs = 9; esc = 7; }
            else { abs = s.Year >= 1992 ? 8 : 0; tcs = s.Year >= 2006 ? 7 : 0; esc = s.Year >= 2008 ? 7 : 0; }
            if (s.Bike) { esc = 0; tcs = player ? tcs : Math.Min(tcs, 5); }
            float pw = PowerOf(car);
            string key = (player ? "P|" : "A|") + (int)(pw * 50f) + "|" + L.Key + "|" + wc + "|" + tcs + "|" + abs + "|" + esc + "|" + (parked ? 1 : 0) + "|" + (int)(wet * 50f) + (player ? "|" + Vehicles.Setup.Key(rec) : "");
            float[] p;
            if (paramCache.TryGetValue(key, out p) && !player) return p;
            p = new float[VP];
            p[0] = 1f; p[1] = t.MuY; p[2] = t.MuX; p[3] = t.LoadSens; p[4] = t.By; p[5] = t.Cy; p[6] = t.Ey; p[7] = t.Bx; p[8] = t.Cx; p[9] = t.Ex;
            p[10] = wet; p[11] = t.Rolling; p[12] = s.Idle; p[13] = s.Redline; p[14] = L.Engine.PeakNm; p[15] = L.Engine.MaxRpm;
            p[16] = s.Ind == Induction.Electric ? 0.06f : 0.07f + 0.03f * Math.Max(1f, s.Litres > 0f ? s.Litres : 3f);
            p[17] = L.Gear.Efficiency; p[18] = Math.Min(s.Redline * 0.55f, Math.Max(s.Idle * 1.8f, L.Engine.PeakNmRpm * 0.7f)); p[19] = L.Engine.SpoolTau;
            p[20] = L.Engine.Ind == Induction.Turbo ? 1f : 0f; p[21] = L.Engine.Als ? 1f : 0f; p[22] = L.Gear.TyreR;
            string kind = s.Kind ?? "";
            bool heavy = kind == "SUVs" || kind == "Vans" || kind == "OffRoad" || kind == "Industrial" || kind == "Commercial" || kind == "Utility" || kind == "Service" || kind == "Emergency";
            p[23] = (s.Bike ? 0.6f : 1.15f) * (L.Gear.TyreR / 0.33f) * (L.Gear.TyreR / 0.33f) * (heavy ? 1.6f : 1f) * (wc > 4 ? 2.2f : 1f);
            int brakeLvl = Math.Max(0, Function.Call<int>(Hash.GET_VEHICLE_MOD, car, 12) + 1);
            float m = L.Mass, g = 9.81f;
            p[24] = m * g * (s.Bike ? 1.25f : wc > 4 ? 1.0f : 1.55f) * (1f + 0.07f * brakeLvl);
            p[25] = m * g * (1f - s.FrontPct) * 1.1f;
            bool diff = Vehicles.Setup.Has(rec, "diff");
            float lockA = diff ? Math.Max(0f, Math.Min(1f, (50f + 5f * Vehicles.Setup.TuneValue(rec, 18)) / 100f)) : kind == "Super" || kind == "Sports" ? 0.35f : kind == "Muscle" ? 0.25f : heavy ? 0.15f : 0.08f;
            p[26] = diff ? 60f : 15f; p[27] = lockA; p[28] = lockA * 0.5f; p[29] = 120f; p[30] = wc > 4 ? 0.8f : 0.35f;
            p[31] = 0.25f; p[32] = tcs; p[33] = abs; p[34] = esc; p[35] = m; p[36] = L.Cda; p[37] = L.Cla; p[38] = Math.Max(1f, s.Wheelbase); p[39] = s.Bike ? 1f : 0f;
            p[40] = parked ? 1f : 0f; p[41] = L.Gear.Gears;
            p[42] = player && Vehicles.Setup.Has(rec, "awd") ? 0.35f : player && Vehicles.Setup.Has(rec, "rwd") ? 0f : s.FrontDrive;
            p[43] = Math.Max(0.35f, Math.Min(0.8f, h != null && h.IsValid ? HandlingMem.GetBias(h, HandlingMem.BrakeBias) : 0.62f));
            p[44] = 1f; p[45] = pw;
            // suspension
            int level = Math.Max(0, Math.Min(4, Function.Call<int>(Hash.GET_VEHICLE_MOD, car, 15) + 1));
            bool coil = Vehicles.Setup.Has(rec, "springs"), bars = Vehicles.Setup.Has(rec, "arb");
            float f = Suspension.RideHz(s, level, coil);
            float arb = kind == "Super" ? 0.9f : kind == "Sports" ? 0.75f : heavy ? 0.6f : kind == "OffRoad" ? 0.3f : kind == "Muscle" || kind == "Sedans" ? 0.45f : 0.55f;
            arb *= (1f + 0.1f * level) * (bars ? 1.15f : 1f);
            float split = s.FrontDrive >= 0.95f ? 0.62f : s.Layout == "RR" ? 0.47f : s.Layout == "MR" || s.Layout == "AWD-M" ? 0.52f : 0.57f;
            split = Math.Max(0.2f, Math.Min(0.8f, split + 0.02f * (Vehicles.Setup.TuneValue(rec, 7) - Vehicles.Setup.TuneValue(rec, 8))));
            arb *= 1f + 0.045f * (Vehicles.Setup.TuneValue(rec, 7) + Vehicles.Setup.TuneValue(rec, 8)) / 2f;
            float bump = 1f + 0.05f * Vehicles.Setup.TuneValue(rec, 13), reb = 1f + 0.05f * Vehicles.Setup.TuneValue(rec, 12);
            p[50] = s.Bike ? 0f : 1f;
            p[51] = f * (float)Math.Sqrt(Math.Max(0.3f, 1f + 0.045f * Vehicles.Setup.TuneValue(rec, 9)));
            p[52] = f * (float)Math.Sqrt(Math.Max(0.3f, 1f + 0.045f * Vehicles.Setup.TuneValue(rec, 10)));
            // dampers (share of critical damping of the corner): firm low-speed body control, digressive above the knee
            p[53] = (coil ? 0.55f : 0.5f) * bump; p[54] = (coil ? 0.28f : 0.25f) * bump; p[55] = (coil ? 0.85f : 0.8f) * reb; p[56] = (coil ? 0.45f : 0.4f) * reb;
            p[57] = arb * 2f * split; p[58] = arb * 2f * (1f - split); p[59] = 0.12f;
            p[60] = 1.7f; p[61] = 4f; p[62] = 3.5f;
            // gearbox: overall ratios ([0] reverse)
            p[70] = L.Gear.Overall(1) * 1.05f;
            for (int i = 1; i <= Math.Min(11, L.Gear.Gears); i++) p[70 + i] = L.Gear.Overall(i);
            // driven wheels (GTA's own flags, our drive split decides the torque)
            var wheels = car.Wheels.GetAllWheels();
            for (int i = 0; i < wc && i < wheels.Length; i++) p[82 + i] = wheels[i].IsDrivingWheel ? 1f : 0f;
            bool anyDriven = false; for (int i = 0; i < wc; i++) if (p[82 + i] > 0.5f) anyDriven = true;
            if (!anyDriven) for (int i = 0; i < wc; i++) p[82 + i] = 1f;
            if (player || s.FrontDrive > 0.05f && s.FrontDrive < 0.95f)
            {   // our layout wins over GTA's flags (AWD / RWD conversions, real drive layouts)
                for (int i = 0; i < wc && i < wheels.Length; i++)
                {
                    bool front = IsFront(wheels[i]);
                    p[82 + i] = front ? (p[42] > 0.02f ? 1f : 0f) : (p[42] < 0.98f ? 1f : 0f);
                }
            }
            // tyre temperature / pressure / alignment / wheel and tyre masses
            float amb = WorldSim.Weather.Wetness > 0.3f ? 14f : 22f;
            p[188] = amb; p[189] = t.TOpt; p[190] = t.TWin; p[191] = t.Relax;
            float coldOpt = t.POpt * (amb + 273f) / (amb + 273f + (t.TOpt - amb) * 0.8f);
            p[192] = player ? 30f + 0.4f * Vehicles.Setup.TuneValue(rec, 0) : coldOpt;
            p[193] = player ? 30f + 0.4f * Vehicles.Setup.TuneValue(rec, 1) : coldOpt;
            p[194] = t.POpt;
            bool sporty = kind == "Super" || kind == "Sports" || kind == "Muscle";
            const float D = 0.017453f;
            p[195] = (player && coil ? -1.0f + 0.25f * Vehicles.Setup.TuneValue(rec, 3) : sporty ? -1.0f : -0.5f) * D;
            p[196] = (player && coil ? -0.8f + 0.2f * Vehicles.Setup.TuneValue(rec, 4) : sporty ? -0.8f : -0.3f) * D;
            p[197] = (5.5f + 0.15f * Vehicles.Setup.TuneValue(rec, 6)) * D;
            p[198] = 0.05f * Vehicles.Setup.TuneValue(rec, 5) * D;
            p[199] = wc > 4 ? 110f : heavy ? 60f : s.Bike ? 15f : 38f;
            p[200] = (wc > 4 ? 800000f : heavy ? 330000f : 220000f) * (L.Gear.TyreR / 0.33f);
            p[201] = kind == "Super" ? 0.5f : sporty ? 0.4f : 0.25f;
            p[202] = t.ColdLoss; p[203] = t.HotLoss;
            p[204] = 0.65f * t.OffroadLoss; p[205] = player && cfg.DevMode ? 1f : 0f; p[206] = L.AeroFront;
            // real roll-centre heights (m above the ground) by suspension type
            float rcF = kind == "Super" ? 0.04f : sporty ? 0.07f : heavy ? 0.15f : 0.06f, rcR = kind == "Super" ? 0.08f : kind == "Muscle" ? 0.25f : sporty ? 0.11f : heavy ? 0.28f : 0.12f;
            if (s.Year < 1975) { rcF += 0.03f; rcR = Math.Max(rcR, 0.25f); }          // old cars: live rear axles
            float rh = 0.009f * Vehicles.Setup.TuneValue(rec, 11);                       // ride height (0.9 cm per step) moves the roll centres with it
            p[207] = Math.Max(0.0f, rcF + rh * 1.5f); p[208] = Math.Max(0.0f, rcR + rh * 1.5f);
            // torque curves: full load and off-boost
            for (int i = 0; i < CurveN; i++) { p[92 + i] = L.Engine.Full[i]; p[92 + CurveN + i] = L.Engine.NoBoost[i]; }
            if (L.Engine.Ind == Induction.Supercharged)
                for (int i = 0; i < CurveN; i++) { float r = L.Engine.MaxRpm * i / (CurveN - 1); p[92 + i] -= L.Engine.Full[i] * L.Engine.ScLoss * (r / s.Redline) * (r / s.Redline) * 0.5f; }
            if (!player) { if (paramCache.Count > 400) paramCache.Clear(); paramCache[key] = p; }
            return p;
        }
        private static bool IsFront(VehicleWheel w)
        {
            try { var a = w.MemoryAddress; if (a == IntPtr.Zero) return false; return BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(a + 0x140)), 0) >= 0f; } catch { return false; }
        }

        public static void Off()
        {
            if (reg.Count == 0 && models.Count == 0 && !PlayerActive) return;
            try { if (avail == 1) MCP_VehDropAll(); } catch { }
            reg.Clear(); Rivals.Clear(); power.Clear();
            foreach (var k in new List<int>(models.Keys)) ReleaseModel(k);
            PlayerActive = false; playerHandle = 0; playerAddr = 0; Count = 0; TcsOn = AbsOn = EscOn = false; Rpm = 0f;
        }
    }
}
