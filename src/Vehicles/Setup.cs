using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Native;
using MidnightClub.Persistence;
using MidnightClub.UI;

namespace MidnightClub.Vehicles
{
    /// <summary>Forza-style tuning, stored per car: tyres, gearing, alignment, anti-roll bars, springs, ride height,
    /// damping, aero, brakes, differential, centre balance and steering, shown in real units. Most pages need the
    /// matching race part (bought at workshops). Race parts also change the car itself: weight reduction, drivetrain
    /// swaps, anti-lag (exhaust pops and quicker boost), straight-pipe exhaust (raspier note).</summary>
    public static class Setup
    {
        public sealed class Item { public string Group, Name, Part; public Func<int, string> Show; public bool Hidden; }
        public static readonly Item[] Items =
        {
            I("Tyres", "Front pressure", null, v => (30f + v * 0.4f).ToString("0.0") + " psi"),
            I("Tyres", "Rear pressure", null, v => (30f + v * 0.4f).ToString("0.0") + " psi"),
            I("Gearing", "Final drive", "gearbox", v => ((Drivetrain.Current != null ? Drivetrain.Current.Gear.BaseFinal : 3.55f) * (1f - 0.02f * v)).ToString("0.00") + " : 1"),
            H(I("Alignment", "Front camber", "springs", v => (-1.0f + v * 0.25f).ToString("0.0") + "°")),   // camber lives in the coilovers' fitment page now
            H(I("Alignment", "Rear camber", "springs", v => (-0.8f + v * 0.2f).ToString("0.0") + "°")),
            I("Alignment", "Front toe", "springs", v => (v * 0.05f).ToString("+0.00;-0.00;0.00") + "°"),
            I("Alignment", "Caster", "springs", v => (5.5f + v * 0.15f).ToString("0.0") + "°"),
            I("Anti-roll bars", "Front", "arb", v => (25f + v * 2.3f).ToString("0.0")),
            I("Anti-roll bars", "Rear", "arb", v => (25f + v * 2.3f).ToString("0.0")),
            I("Springs & dampers", "Front stiffness", "springs", v => (110f + v * 9f).ToString("0") + " N/mm"),
            I("Springs & dampers", "Rear stiffness", "springs", v => (100f + v * 8f).ToString("0") + " N/mm"),
            I("Springs & dampers", "Ride height", "springs", v => (12f + v * 0.9f).ToString("0.0") + " cm"),
            I("Springs & dampers", "Rebound", "springs", v => (8f + v * 0.6f).ToString("0.0")),
            I("Springs & dampers", "Bump", "springs", v => (5f + v * 0.4f).ToString("0.0")),
            I("Aero", "Front downforce", "aero", v => (60 + (v + 10) * 6).ToString() + " kgf @200"),
            I("Aero", "Rear downforce", "aero", v => (90 + (v + 10) * 9).ToString() + " kgf @200"),
            I("Brakes", "Balance", "brakes", v => (55 + v * 1.5f).ToString("0") + "% front"),
            I("Brakes", "Pressure", "brakes", v => (100 + v * 6).ToString() + "%"),
            I("Differential", "Acceleration lock", "diff", v => (50 + v * 5).ToString() + "%"),
            I("Differential", "Deceleration lock", "diff", v => (25 + v * 2.5f).ToString("0") + "%"),
            I("Differential", "Centre balance (AWD)", "diff", v => (65 + v * 3).ToString() + "% rear"),
            I("Steering", "Steering lock", null, v => (38f + v * 1.5f).ToString("0") + "°"),
            G(1), G(2), G(3), G(4), G(5), G(6),
        };
        public const int FinalDriveIndex = 2, GearIndex0 = 22;
        private static Item G(int n)
        {
            return I("Gearing", "Gear " + n, "gearbox2", v =>
            {
                var s = Drivetrain.Current;
                if (s == null) return (1f - 0.03f * v).ToString("x0.00");
                if (n > s.Gears) return "-";
                return (s.Gear.BaseRatios[n - 1] * (1f - 0.03f * v)).ToString("0.00") + " : 1";
            });
        }
        /// <summary>A tuning value (-10..10), 0 when the part it needs isn't installed.</summary>
        public static int TuneValue(OwnedVehicle r, int i) { if (r == null || i < 0 || i >= Count || !Has(r, Items[i].Part)) return 0; return Math.Max(-10, Math.Min(10, Get(r)[i])); }
        private static Item I(string g, string n, string p, Func<int, string> s) { return new Item { Group = g, Name = n, Part = p, Show = s }; }
        private static Item H(Item it) { it.Hidden = true; return it; }
        public static int Count { get { return Items.Length; } }

        public sealed class Part { public string Id, Name, Desc; public long Price; public int Pi; public string Needs; }
        public static readonly Part[] Parts = new Part[][]
        {

            new[] { new Part { Id = "springs", Name = "Race coilovers (adjustable)", Desc = "Adjustable coilovers with camber plates: unlocks springs, ride height (real: the car sits lower, less bump travel), damping, alignment and the camber / tyre-size fitment.", Price = 9000, Pi = 8 } },
            new[] { new Part { Id = "spacers", Name = "Wheel spacers & adjustable hubs", Desc = "Hub spacers from 0 to +15 cm per side, adjustable in Wheels > Fitment: a wider track rolls less and transfers less load (more grip), but the steering gets heavier and kerbs bite.", Price = 3500, Pi = 2 } },
            new[] { new Part { Id = "arb", Name = "Anti-roll bars: Race (adjustable)", Desc = "Adjustable front and rear bars.", Price = 4500, Pi = 4 } },
            new[] { new Part { Id = "diff", Name = "Differential: Race (adjustable)", Desc = "Limited-slip diff with adjustable lock (and centre balance on AWD).", Price = 7500, Pi = 6 } },
            new[] { new Part { Id = "gearbox", Name = "Race gearbox", Desc = "Close-ratio six-speed with automatic ratios (every car becomes a 6-speed) and an adjustable final drive. The car is rebuilt when you drive out.", Price = 8000, Pi = 6 } },
            new[] { new Part { Id = "gearbox2", Name = "Race gearbox, fully adjustable", Desc = "Arrives with the race gearbox's six ratios; every gear can then be tuned on top of the final drive.", Price = 16000, Pi = 5, Needs = "gearbox" } },
            new[] { new Part { Id = "brakes", Name = "Race brake kit (adjustable)", Desc = "Adjustable balance and pressure, better bite.", Price = 6000, Pi = 5 } },
            new[] { new Part { Id = "aero", Name = "Race splitter & wing (adjustable)", Desc = "Splitter and wing with adjustable downforce: grip at speed, costs top speed.", Price = 11000, Pi = 10 } },
            new[] { new Part { Id = "weight1", Name = "Weight reduction: Street", Desc = "-4% weight.", Price = 5000, Pi = 8 } },
            new[] { new Part { Id = "weight2", Name = "Weight reduction: Sport", Desc = "-8% weight (needs street).", Price = 12000, Pi = 10, Needs = "weight1" } },
            new[] { new Part { Id = "weight3", Name = "Weight reduction: Race", Desc = "-12% weight, stripped interior (needs sport).", Price = 25000, Pi = 14, Needs = "weight2" } },
            new[] { new Part { Id = "awd", Name = "AWD conversion", Desc = "All-wheel drive: launches and traction. Replaces RWD conversion.", Price = 30000, Pi = 15 } },
            new[] { new Part { Id = "rwd", Name = "RWD conversion", Desc = "Rear-wheel drive: the drift build. Replaces AWD conversion.", Price = 18000, Pi = 2 } },
            new[] { new Part { Id = "turbo1", Name = "Turbo stage 1", Desc = "Turbo kit (or an uprated turbo on a turbo engine): low boost, quick spool.", Price = 6500, Pi = 8 } },
            new[] { new Part { Id = "turbo2", Name = "Turbo stage 2", Desc = "Bigger turbo, more boost: strong mid-range, a little more lag.", Price = 9500, Pi = 8, Needs = "turbo1" } },
            new[] { new Part { Id = "turbo3", Name = "Turbo stage 3", Desc = "Big single turbo: huge top end, real lag below the boost threshold.", Price = 14000, Pi = 10, Needs = "turbo2" } },
            new[] { new Part { Id = "sc1", Name = "Supercharger stage 1", Desc = "Positive-displacement supercharger: instant boost from idle, no lag, some drive loss.", Price = 7500, Pi = 8 } },
            new[] { new Part { Id = "sc2", Name = "Supercharger stage 2", Desc = "Smaller pulley, more boost everywhere.", Price = 10000, Pi = 8, Needs = "sc1" } },
            new[] { new Part { Id = "sc3", Name = "Supercharger stage 3", Desc = "Large twin-screw blower: massive torque from low revs.", Price = 14500, Pi = 10, Needs = "sc2" } },
            new[] { new Part { Id = "als", Name = "Anti-lag system", Desc = "Keeps the turbo spooled: pops and crackles off throttle, boost holds between shifts. Needs a turbo.", Price = 9500, Pi = 6 } },
            new[] { new Part { Id = "pipe", Name = "Exhaust: Race (straight pipe)", Desc = "Free-flowing race exhaust: loud, raw and raspy, more top-end power, quicker spool.", Price = 3500, Pi = 2, Needs = "exh2" } },
            // engine parts, Forza style: street / sport / race each
            P("intake", "Intake", "Air filter, intake tract and throttle body: more top-end power.", 1500, 2),
            P("fuel", "Fuel system", "Injectors and pump: more power across the rev range.", 1800, 2),
            P("ign", "Ignition", "Coils, plugs and timing: a little more power everywhere.", 1200, 1),
            P("exh", "Exhaust", "Headers and a freer exhaust: more power, a deeper note.", 2000, 2, 2),
            P("cam", "Camshaft", "Hotter cams: much more top end, a little less low down.", 2500, 3),
            P("valves", "Valves", "Bigger valves and stiffer springs: more top-end power.", 2200, 2),
            P("disp", "Displacement", "Bore and stroke: more torque everywhere.", 6000, 4),
            P("pistons", "Pistons & compression", "Forged pistons and a higher compression ratio: more power.", 3500, 3),
            P("ic", "Intercooler", "Cooler charge air (turbo or supercharger only): more power under boost.", 2000, 2),
            P("oil", "Oil cooling", "Oil cooler: holds power on long full-throttle runs.", 1000, 1),
            P("fly", "Flywheel", "Lighter flywheel: the engine revs up and down much faster.", 1200, 1),
            // drivetrain parts
            P("clutch", "Clutch", "Sport / race clutch: quicker, firmer shifts.", 1500, 1, 2),
            P("driveline", "Driveline", "Lighter driveshafts and joints: less power lost, less rotating mass.", 1800, 1, 2),

        }.SelectMany(x => x).ToArray();

        /// <summary>Camber and tyre size are set on the coilovers (or the old fitment kit); track width needs the spacers.</summary>
        public static bool CanCamber(OwnedVehicle r) { return r != null && (r.FitKit || Has(r, "springs")); }
        public static bool CanSpace(OwnedVehicle r) { return r != null && (r.FitKit || Has(r, "spacers")); }
        /// <summary>A staged part family: Street / Sport / Race (or Sport / Race for two levels), each needing the one below.</summary>
        private static Part[] P(string id, string name, string desc, long price, int pi, int levels = 3)
        {
            string[] lv = levels == 2 ? new[] { "Sport", "Race" } : new[] { "Street", "Sport", "Race" };
            var l = new Part[levels];
            for (int i = 0; i < levels; i++) l[i] = new Part { Id = id + (i + 1), Name = name + ": " + lv[i], Desc = desc, Price = price * (i + 1) + price * i / 2, Pi = pi, Needs = i > 0 ? id + i : null };
            return l;
        }
        /// <summary>Level (0 stock .. 3) of a staged part family (intake1..3 etc.).</summary>
        public static int Level(OwnedVehicle r, string family) { if (r == null || r.Parts == null) return 0; for (int i = 3; i >= 1; i--) if (r.Parts.Contains(family + i)) return i; return 0; }
        public static bool Has(OwnedVehicle r, string part) { return part == null || (r != null && r.Parts != null && r.Parts.Contains(part)); }
        public static int[] Get(OwnedVehicle r)
        {
            if (r.Setup == null || r.Setup.Length != Count) { var n = new int[Count]; if (r.Setup != null) Array.Copy(r.Setup, n, Math.Min(Count, r.Setup.Length)); r.Setup = n; }
            return r.Setup;
        }
        public static bool Any(OwnedVehicle r) { return r != null && ((r.Setup != null && r.Setup.Any(v => v != 0)) || (r.Parts != null && r.Parts.Count > 0)); }
        public static string Key(OwnedVehicle r) { return r == null ? "" : (r.Setup == null ? "" : string.Join(",", r.Setup)) + "|" + (r.Parts == null ? "" : string.Join(",", r.Parts)); }
        public static int PiBonus(OwnedVehicle r) { return r == null || r.Parts == null ? 0 : Parts.Where(p => r.Parts.Contains(p.Id)).Sum(p => p.Pi); }

        /// <summary>Applies setup + parts on top of the stock/simcade handling values already in h.</summary>
        public static void Apply(HandlingData h, OwnedVehicle r)
        {
            var t = Get(r); Func<int, float> V = i => Has(r, Items[i].Part) ? Math.Max(-10, Math.Min(10, t[i])) : 0;
            // tyres: lower pressure = more grip, slower response; the balance moves grip front/rear
            float pf = V(0), pr = V(1);
            h.TractionCurveMax *= 1f - 0.004f * (pf + pr) - 0.0015f * (pf * pf + pr * pr) / 10f;
            Physics.HandlingMem.SetBias(h, Physics.HandlingMem.TractionBias, Clamp(Physics.HandlingMem.GetBias(h, Physics.HandlingMem.TractionBias) - 0.004f * (pf - pr), 0.36f, 0.64f), null);
            // gearing, aero and power are part of the simulation (Drivetrain / SimPhysics)
            // alignment: negative camber = more cornering grip, less braking; toe out = sharper turn-in; caster = stability
            float camber = -(V(3) + V(4)) / 2f;
            h.TractionCurveLateral *= 1f - 0.006f * camber; h.TractionCurveMin *= 1f + 0.004f * camber; h.BrakeForce *= 1f - 0.004f * Math.Abs(camber);
            h.SteeringLock *= 1f + 0.01f * V(5);
            // anti-roll bars
            float af = V(7), ar = V(8);
            h.AntiRollBarForce *= 1f + 0.045f * (af + ar) / 2f; Physics.HandlingMem.SetBias(h, Physics.HandlingMem.ArbBias, Clamp(Physics.HandlingMem.GetBias(h, Physics.HandlingMem.ArbBias) + 0.02f * (af - ar), 0.2f, 0.8f), null);
            // springs + height
            float sf = V(9), sr = V(10);
            h.SuspensionForce *= 1f + 0.045f * (sf + sr) / 2f; Physics.HandlingMem.SetBias(h, Physics.HandlingMem.SuspBias, Clamp(Physics.HandlingMem.GetBias(h, Physics.HandlingMem.SuspBias) + 0.015f * (sf - sr), 0.3f, 0.7f), null);
            // ride height: the native suspension sits the body lower / higher (SimWorld), not GTA's raise
            // damping
            h.SuspensionReboundDamping *= 1f + 0.05f * V(12); h.SuspensionCompressionDamping *= 1f + 0.05f * V(13);
            // aero: grip at speed, costs top speed
            // brakes
            if (Has(r, "brakes")) h.BrakeForce *= 1.08f;
            Physics.HandlingMem.SetBias(h, Physics.HandlingMem.BrakeBias, Clamp(Physics.HandlingMem.GetBias(h, Physics.HandlingMem.BrakeBias) + 0.015f * V(16), 0.3f, 0.8f), null); h.BrakeForce *= 1f + 0.03f * V(17);
            // differential: more accel lock = more power oversteer, decel lock = stability off throttle
            if (Has(r, "diff")) { h.LowSpeedTractionLossMultiplier *= 1f + 0.04f * V(18); h.TractionLossMultiplier *= 1f + 0.02f * V(18); h.TractionCurveMin *= 1f + 0.01f * V(19); }
            // drivetrain conversions + centre balance
            if (Has(r, "awd")) Physics.HandlingMem.SetBias(h, Physics.HandlingMem.DriveBias, 0.35f, null); else if (Has(r, "rwd")) Physics.HandlingMem.SetBias(h, Physics.HandlingMem.DriveBias, 0f, null);
            float db = Physics.HandlingMem.GetBias(h, Physics.HandlingMem.DriveBias);
            if (db > 0.05f && db < 0.95f && Has(r, "diff")) Physics.HandlingMem.SetBias(h, Physics.HandlingMem.DriveBias, Clamp(0.35f - 0.03f * V(20), 0.1f, 0.6f), null);
            // steering
            h.SteeringLock *= 1f + 0.035f * V(21);
            // weight
            float w = Has(r, "weight3") ? 0.88f : Has(r, "weight2") ? 0.92f : Has(r, "weight1") ? 0.96f : 1f;
            h.Mass *= w;
        }

        /// <summary>Extra engine power (via the cheat-power multiplier, combined with ECU): anti-lag at low revs, straight pipe.</summary>
        public static float PowerBonus(OwnedVehicle r, Vehicle v)
        {
            float b = 0f;
            if (Has(r, "als") && v.CurrentRPM < 0.65f && v.Throttle > 0.3f) b += 0.07f;
            if (Has(r, "pipe")) b += 0.02f;
            return b;
        }

        /// <summary>Sound side of the parts: anti-lag pops, straight-pipe rasp.</summary>
        public static void ApplyAudio(Vehicle v, OwnedVehicle r)
        {
            if (v == null || !v.Exists() || r == null) return;
            try
            {
                if (Has(r, "als")) Function.Call(Hash.ENABLE_VEHICLE_EXHAUST_POPS, v, true);
                if (Has(r, "pipe")) Function.Call(Hash.SET_VEHICLE_AUDIO_ENGINE_DAMAGE_FACTOR, v, 0.35f);
            }
            catch { }
        }

        /// <summary>Engine swaps change power too, not only the note.</summary>
        public static float SwapPower(string id)
        {
            switch (id) { case "elegy2": return 0.02f; case "zr350": case "comet2": case "voltic": return 0.03f; case "sabregt": return 0.04f; case "dominator": return 0.05f; case "banshee2": return 0.07f; case "zentorno": return 0.10f; case "openwheel1": return 0.12f; case "sandking": return 0.02f; }
            return 0f;
        }

        private static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }

        // ---------------------------------------------------------------- tuning menu
        public static Menu Menu(VehicleManager vehicles, MenuStack stack, SaveManager saves)
        {
            var rec = vehicles.CurrentRecord;
            var m = new Menu("Tuning", rec == null ? "No car" : rec.Title());
            if (rec == null) { m.Add(new MenuItem("Get in one of your cars first") { Enabled = false }); return m; }
            var info = VehicleRegistry.Instance != null ? VehicleRegistry.Instance.Get(rec.ModelHash) : null;
            m.Subtitle = rec.Title() + "  ·  " + Era.Label(Era.Year(info)) + "  ·  " + PerformanceIndex.Label(PerformanceIndex.Of(rec));
            string[] order = { "Tyres", "Alignment", "Springs & dampers", "Anti-roll bars", "Brakes", "Differential", "Gearing", "Aero", "Steering" };
            var groups = Items.Where(x => !x.Hidden).Select(x => x.Group).Distinct().OrderBy(x => { int o = Array.IndexOf(order, x); return o < 0 ? 99 : o; });
            foreach (var g in groups)
            {
                var grp = g;
                var gi = Items.Select((x, i) => new { x, i }).Where(z => z.x.Group == grp && !z.x.Hidden).ToList();
                bool locked = gi.All(z => !Has(rec, z.x.Part));
                string need = locked ? Parts.First(p => p.Id == gi[0].x.Part).Name : null;
                m.Add(new MenuItem(grp, () => { if (!locked) stack.Push(Page(rec, grp, saves)); else Toast.Post("~r~" + grp + "~s~ tuning needs " + need + " (workshops: Performance, top of its upgrade path)."); })
                { Value = () => locked ? "Needs part" : gi.Any(z => Get(rec)[z.i] != 0) ? "Tuned" : "Stock", Description = () => locked ? "Install " + need + " at a workshop to tune this." : string.Join("  ·  ", gi.Select(z => z.x.Name + " " + z.x.Show(Get(rec)[z.i])).ToArray()) });
            }
            m.Add(new MenuItem("Presets", () => stack.Push(Presets(rec, saves))) { Description = () => "Grip, drift, drag, rally and touge baselines." });
            m.Add(new MenuItem("Reset to stock", () => { Array.Clear(Get(rec), 0, Count); saves.IsDirty = true; Shard.Mid("TUNING", "BACK TO STOCK", Shard.Purple, 1500); }));
            return m;
        }

        private static Menu Page(OwnedVehicle rec, string grp, SaveManager saves)
        {
            var m = new Menu(grp, "Left / right to adjust, applies as you drive");
            var t = Get(rec);
            for (int k = 0; k < Items.Length; k++)
            {
                if (Items[k].Group != grp || Items[k].Hidden) continue; int i = k; var it = Items[i];
                m.Add(new MenuItem(it.Name)
                {
                    Enabled = Has(rec, it.Part),
                    Value = () => it.Show(t[i]) + "  " + Bar(t[i]),
                    OnAdjust = d => { if (Racing.RaceManager.IsRacing || !Has(rec, it.Part)) return; t[i] = Math.Max(-10, Math.Min(10, t[i] + d)); saves.IsDirty = true; },
                    Description = () => Tip(i)
                });
            }
            return m;
        }

        private static string Bar(int v) { var c = new char[21]; for (int i = 0; i < 21; i++) c[i] = i == v + 10 ? '|' : i == 10 ? '+' : '-'; return new string(c); }

        private static string Tip(int i)
        {
            switch (i)
            {
                case 0: case 1: return "Lower pressure: more grip, softer response. Front lower than rear = less understeer.";
                case 2: return "Shorter (higher number): more force at the wheels, quicker acceleration, lower top speed. Longer: the opposite.";
                case 22: case 23: case 24: case 25: case 26: case 27: return "Shorter gear (higher number): more pull in that gear, it runs out of revs sooner. The top gear sets the top-speed gearing.";
                case 3: case 4: return "More negative camber: more cornering grip, a little less braking and straight-line grip.";
                case 5: return "Toe out: sharper turn-in. Toe in: calmer on the straights.";
                case 6: return "More caster: more stable at speed, heavier steering.";
                case 7: case 8: return "Stiffer front bar = more understeer, stiffer rear = more oversteer. Stiff overall = less roll.";
                case 9: case 10: return "Stiffer springs: less roll and pitch, sharper; too stiff skips over bumps.";
                case 11: return "Lower: lower centre of gravity. Higher: soaks up rough roads and jumps.";
                case 12: case 13: return "Damping controls how fast the body settles. Soft = wallowy, stiff = nervous.";
                case 14: case 15: return "More downforce: more grip at speed, less top speed. Balance front/rear for the handling you want.";
                case 16: return "More front: stable under braking. More rear: the car rotates on entry.";
                case 18: return "More acceleration lock: the rear steps out under power (drift). Less: easier exits.";
                case 19: return "More deceleration lock: stable when you lift. Less: the car turns in off throttle.";
                case 20: return "More rear: the AWD car behaves more like RWD.";
                default: return "";
            }
        }

        private static Menu Presets(OwnedVehicle rec, SaveManager saves)
        {
            var m = new Menu("Presets", "Baselines to start from");
            Action<string, int[]> p = (n, vals) => m.Add(new MenuItem(n, () => { var t = Get(rec); for (int i = 0; i < Count; i++) t[i] = i < vals.Length ? vals[i] : 0; saves.IsDirty = true; Shard.Mid("TUNING", n.ToUpperInvariant() + " SETUP", Shard.Purple, 1500); }));
            //            Fp  Rp  FD  Cf  Cr  Toe Cas ARBf ARBr Sf  Sr  RH  Reb Bmp AeF AeR BB  BP  DA  DD  CB  Lock
            p("Grip",    new[] { -2, -2, 0, -4, -3, 0, 3, 3, 2, 4, 3, -4, 3, 2, 3, 4, 0, 2, -2, 2, -2, 0 });
            p("Drift",   new[] { 2, -3, 3, -6, 0, 3, 6, 6, -4, 4, 0, -3, 2, 1, 0, -4, -3, 0, 10, -6, 8, 10 });
            p("Drag",    new[] { 0, -6, -6, 0, 0, 0, 0, -2, 4, -3, -5, -2, -2, -2, -10, -10, 2, 0, 4, 0, 6, -4 });
            p("Rally",   new[] { -3, -3, 3, -2, -1, 1, 1, -4, -3, -6, -6, 8, -4, -4, 0, 0, -2, 0, 3, 0, 2, 4 });
            p("Touge",   new[] { -1, -1, 2, -3, -2, 1, 2, 2, 0, 2, 1, -3, 2, 1, 2, 2, -1, 1, 2, 1, 2, 2 });
            return m;
        }
    }
}
