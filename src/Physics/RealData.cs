using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Vehicles;

namespace MidnightClub.Physics
{
    /// <summary>Spec database: hand-entered real figures for the cars the GTA models are based on, and a generator that
    /// builds a consistent real-world spec for every other vehicle (cars and bikes) from its class, era, mass and
    /// GTA's own drive figures.</summary>
    public static class RealData
    {
        private static readonly Dictionary<string, CarSpec> curated = new Dictionary<string, CarSpec>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, CarSpec> cache = new Dictionary<int, CarSpec>();

        //      id, hp @rpm, Nm @rpm, redline, induction(boost), litres, cyl, layout, gears, front drive, kg, front %, CoG h, wheelbase, CdA, ClA
        static RealData()
        {
            // ---- super
            C("adder", 1001, 6000, 1250, 3500, 6400, Induction.Turbo, 1.2f, 8.0f, 16, "W", 6, "AWD-M", 0.30f, 1888, 45, 0.43f, 2.71f, 0.74f, 0.40f);
            C("zentorno", 570, 8000, 540, 6500, 8500, Induction.NA, 0, 5.2f, 10, "V", 6, "AWD-M", 0.30f, 999, 43, 0.40f, 2.56f, 0.70f, 0.90f);
            C("t20", 903, 7500, 900, 4000, 8500, Induction.Turbo, 1.4f, 3.8f, 8, "V", 6, "MR", 0, 1490, 42, 0.40f, 2.67f, 0.70f, 1.70f, true);
            C("osiris", 730, 5800, 1000, 3300, 6500, Induction.Turbo, 1.2f, 6.0f, 12, "V", 6, "MR", 0, 1350, 44, 0.40f, 2.80f, 0.65f, 0.90f);
            C("entityxf", 806, 7000, 920, 5500, 7800, Induction.Supercharged, 0.9f, 4.7f, 8, "V", 6, "MR", 0, 1456, 45, 0.40f, 2.66f, 0.68f, 0.50f);
            C("cheetah", 660, 7800, 657, 5500, 8200, Induction.NA, 0, 6.0f, 12, "V", 6, "MR", 0, 1365, 44, 0.40f, 2.65f, 0.70f, 0.80f);
            C("infernus", 640, 8000, 660, 6000, 8500, Induction.NA, 0, 6.5f, 12, "V", 6, "AWD-M", 0.30f, 1665, 42, 0.43f, 2.67f, 0.75f, 0.30f);
            C("vacca", 520, 8000, 510, 4250, 8250, Induction.NA, 0, 5.0f, 10, "V", 6, "AWD-M", 0.30f, 1520, 42, 0.42f, 2.56f, 0.69f, 0.20f);
            C("bullet", 550, 6500, 678, 3750, 6500, Induction.Supercharged, 0.83f, 5.4f, 8, "V", 6, "MR", 0, 1520, 43, 0.43f, 2.71f, 0.68f, 0.40f);
            C("turismor", 950, 9000, 900, 6750, 9250, Induction.NA, 0, 6.3f, 12, "V", 6, "MR", 0, 1365, 41, 0.38f, 2.65f, 0.70f, 1.20f, true);
            C("tempesta", 610, 8250, 560, 6500, 8500, Induction.NA, 0, 5.2f, 10, "V", 6, "AWD-M", 0.30f, 1422, 43, 0.42f, 2.62f, 0.68f, 0.30f);
            C("italigtb", 670, 8000, 760, 3000, 8000, Induction.Turbo, 1.4f, 3.9f, 8, "V", 6, "MR", 0, 1475, 42, 0.42f, 2.65f, 0.65f, 0.65f);
            C("nero", 1500, 6700, 1600, 4000, 6900, Induction.Turbo, 1.5f, 8.0f, 16, "W", 6, "AWD-M", 0.30f, 1995, 43, 0.43f, 2.71f, 0.78f, 0.50f);
            C("xa21", 890, 9500, 1000, 4000, 10200, Induction.Turbo, 1.6f, 1.6f, 4, "I", 6, "AWD-M", 0.25f, 1700, 44, 0.40f, 2.75f, 0.65f, 1.00f, true);
            C("krieger", 1063, 11000, 1000, 6000, 11000, Induction.Turbo, 3.5f, 1.6f, 6, "V", 6, "AWD-M", 0.25f, 1695, 44, 0.38f, 2.72f, 0.70f, 2.00f, true);
            C("vagner", 1160, 10500, 900, 6000, 11100, Induction.NA, 0, 6.5f, 12, "V", 6, "MR", 0, 1270, 43, 0.36f, 2.74f, 0.75f, 3.00f, true);
            // ---- sports
            C("elegy2", 480, 6400, 588, 4000, 7000, Induction.Turbo, 0.8f, 3.8f, 6, "V", 6, "AWD-F", 0.25f, 1740, 54, 0.46f, 2.78f, 0.60f, 0.25f);
            C("elegy", 330, 6800, 392, 4400, 8000, Induction.Turbo, 0.9f, 2.6f, 6, "I", 6, "AWD-F", 0.20f, 1560, 55, 0.48f, 2.67f, 0.70f, 0.15f);
            C("futo", 130, 6600, 149, 5200, 7600, Induction.NA, 0, 1.6f, 4, "I", 5, "FR", 0, 940, 53, 0.50f, 2.40f, 0.65f, -0.05f);
            C("futo2", 165, 7400, 172, 5800, 8000, Induction.NA, 0, 1.6f, 4, "I", 5, "FR", 0, 960, 53, 0.49f, 2.40f, 0.65f, 0.00f);
            C("jester", 573, 7500, 645, 4000, 7500, Induction.Turbo, 1.1f, 3.5f, 6, "V", 6, "AWD-M", 0.30f, 1725, 42, 0.42f, 2.63f, 0.68f, 0.20f, true);
            C("jester3", 320, 5600, 427, 4000, 6800, Induction.Turbo, 0.8f, 3.0f, 6, "I", 6, "FR", 0, 1570, 53, 0.48f, 2.55f, 0.65f, 0.05f);
            C("jester4", 340, 5800, 500, 3400, 7000, Induction.Turbo, 1.1f, 3.0f, 6, "I", 6, "FR", 0, 1540, 50, 0.46f, 2.47f, 0.63f, 0.05f);
            C("comet2", 355, 6600, 400, 4600, 7300, Induction.NA, 0, 3.8f, 6, "F", 6, "RR", 0, 1420, 38, 0.45f, 2.35f, 0.60f, 0.05f);
            C("comet3", 260, 6100, 325, 4800, 6720, Induction.NA, 0, 3.6f, 6, "F", 5, "RR", 0, 1220, 39, 0.48f, 2.27f, 0.65f, 0.00f);
            C("comet5", 700, 7000, 750, 3500, 7200, Induction.Turbo, 1.5f, 3.8f, 6, "F", 6, "RR", 0, 1470, 39, 0.43f, 2.45f, 0.70f, 0.90f);
            C("comet6", 450, 6500, 530, 3500, 7500, Induction.Turbo, 1.2f, 3.0f, 6, "F", 6, "RR", 0, 1515, 38, 0.45f, 2.45f, 0.63f, 0.10f);
            C("coquette", 455, 6000, 624, 4600, 6600, Induction.NA, 0, 6.2f, 8, "V", 6, "FR", 0, 1496, 50, 0.46f, 2.71f, 0.65f, 0.15f);
            C("coquette4", 495, 6450, 637, 5150, 6600, Induction.NA, 0, 6.2f, 8, "V", 6, "MR", 0, 1530, 40, 0.44f, 2.72f, 0.68f, 0.30f);
            C("coquette2", 360, 6000, 475, 4000, 6500, Induction.NA, 0, 5.4f, 8, "V", 4, "FR", 0, 1430, 48, 0.50f, 2.49f, 0.75f, -0.20f);
            C("banshee", 400, 4600, 630, 3600, 6000, Induction.NA, 0, 8.0f, 10, "V", 6, "FR", 0, 1490, 49, 0.46f, 2.44f, 0.75f, -0.05f);
            C("ninef", 420, 7800, 430, 4500, 8250, Induction.NA, 0, 4.2f, 8, "V", 6, "AWD-M", 0.15f, 1560, 44, 0.44f, 2.65f, 0.68f, 0.15f);
            C("massacro", 573, 6750, 620, 5500, 7000, Induction.NA, 0, 6.0f, 12, "V", 6, "FR", 0, 1739, 51, 0.46f, 2.74f, 0.70f, 0.10f);
            C("rapidgt", 450, 6000, 570, 5000, 6800, Induction.NA, 0, 5.9f, 12, "V", 6, "FR", 0, 1760, 50, 0.47f, 2.74f, 0.70f, 0.05f);
            C("kuruma", 291, 6500, 407, 3500, 7000, Induction.Turbo, 1.0f, 2.0f, 4, "I", 5, "AWD-F", 0.50f, 1520, 59, 0.50f, 2.65f, 0.70f, 0.10f);
            C("zr350", 255, 6500, 294, 5000, 8000, Induction.Turbo, 0.8f, 1.3f, 2, "R", 5, "FR", 0, 1280, 50, 0.46f, 2.43f, 0.55f, 0.00f);
            C("calico", 255, 6000, 304, 4000, 7200, Induction.Turbo, 0.9f, 2.0f, 4, "I", 5, "AWD-F", 0.50f, 1380, 60, 0.50f, 2.54f, 0.65f, 0.00f);
            C("remus", 205, 6000, 274, 4000, 7500, Induction.Turbo, 0.7f, 2.0f, 4, "I", 5, "FR", 0, 1240, 55, 0.48f, 2.48f, 0.65f, 0.00f);
            C("euros", 287, 6200, 363, 4800, 6600, Induction.NA, 0, 3.5f, 6, "V", 6, "FR", 0, 1440, 53, 0.47f, 2.65f, 0.68f, 0.00f);
            C("rt3000", 240, 8300, 208, 7500, 9000, Induction.NA, 0, 2.0f, 4, "I", 6, "FR", 0, 1250, 50, 0.46f, 2.40f, 0.65f, 0.00f);
            C("sultan", 300, 6000, 407, 4000, 8000, Induction.Turbo, 1.0f, 2.5f, 4, "F", 6, "AWD-F", 0.41f, 1495, 59, 0.52f, 2.54f, 0.69f, 0.05f);
            C("sultan2", 280, 6000, 363, 3200, 8000, Induction.Turbo, 1.0f, 2.2f, 4, "F", 5, "AWD-F", 0.50f, 1270, 59, 0.50f, 2.52f, 0.70f, 0.05f);
            C("vectre", 472, 7100, 535, 4800, 7300, Induction.NA, 0, 5.0f, 8, "V", 6, "FR", 0, 1765, 54, 0.47f, 2.73f, 0.68f, 0.00f);
            C("growler", 420, 7600, 420, 5000, 8000, Induction.NA, 0, 4.0f, 6, "F", 6, "MR", 0, 1420, 45, 0.44f, 2.48f, 0.65f, 0.40f);
            C("sugoi", 306, 6500, 400, 3500, 7000, Induction.Turbo, 1.4f, 2.0f, 4, "I", 6, "FF", 1, 1380, 62, 0.50f, 2.70f, 0.70f, 0.10f);
            // ---- muscle
            C("dominator", 300, 6000, 434, 4250, 6500, Induction.NA, 0, 4.6f, 8, "V", 5, "FR", 0, 1600, 54, 0.52f, 2.72f, 0.80f, -0.10f);
            C("gauntlet", 425, 6200, 569, 4800, 6400, Induction.NA, 0, 6.1f, 8, "V", 5, "FR", 0, 1880, 56, 0.54f, 2.95f, 0.83f, -0.10f);
            C("dukes", 375, 4600, 650, 3200, 5500, Induction.NA, 0, 7.2f, 8, "V", 4, "FR", 0, 1700, 56, 0.56f, 2.97f, 0.95f, -0.25f);
            C("sabregt", 360, 4600, 691, 2800, 5200, Induction.NA, 0, 7.5f, 8, "V", 4, "FR", 0, 1700, 56, 0.56f, 2.84f, 0.95f, -0.25f);
            C("vigero", 375, 5600, 563, 3600, 6000, Induction.NA, 0, 6.5f, 8, "V", 4, "FR", 0, 1550, 56, 0.55f, 2.74f, 0.90f, -0.20f);
            C("gt500", 355, 5400, 569, 3200, 5800, Induction.NA, 0, 7.0f, 8, "V", 4, "FR", 0, 1500, 57, 0.55f, 2.74f, 0.88f, -0.20f);
            C("ruiner", 190, 4800, 325, 3200, 5500, Induction.NA, 0, 5.0f, 8, "V", 5, "FR", 0, 1500, 57, 0.52f, 2.57f, 0.75f, -0.10f);
            C("dominator7", 235, 4600, 380, 3400, 6000, Induction.NA, 0, 5.0f, 8, "V", 5, "FR", 0, 1450, 57, 0.52f, 2.55f, 0.80f, -0.10f);
            C("buffalo2", 425, 6200, 569, 4800, 6400, Induction.NA, 0, 6.1f, 8, "V", 5, "FR", 0, 1890, 54, 0.55f, 3.05f, 0.85f, -0.05f);
            // ---- classics
            C("jb700", 282, 5500, 390, 3850, 6000, Induction.NA, 0, 4.0f, 6, "I", 5, "FR", 0, 1468, 51, 0.52f, 2.49f, 0.80f, -0.20f);
            C("monroe", 350, 7000, 370, 5100, 7700, Induction.NA, 0, 3.9f, 12, "V", 5, "MR", 0, 1180, 42, 0.46f, 2.50f, 0.70f, -0.30f);
            C("cheetah2", 390, 6300, 490, 4500, 6800, Induction.NA, 0, 4.9f, 12, "F", 5, "MR", 0, 1506, 41, 0.47f, 2.55f, 0.68f, -0.05f);
            C("infernus2", 492, 7000, 580, 5200, 7300, Induction.NA, 0, 5.7f, 12, "V", 5, "MR", 0, 1576, 41, 0.46f, 2.65f, 0.70f, -0.05f);
            C("torero", 375, 8000, 361, 5500, 8000, Induction.NA, 0, 3.9f, 12, "V", 5, "MR", 0, 1065, 42, 0.45f, 2.45f, 0.70f, -0.10f);
            C("turismo2", 478, 7000, 577, 4000, 7750, Induction.Turbo, 1.1f, 2.9f, 8, "V", 5, "MR", 0, 1100, 42, 0.43f, 2.45f, 0.65f, 0.40f);
            C("gp1", 618, 7400, 650, 5600, 7500, Induction.NA, 0, 6.1f, 12, "V", 6, "MR", 0, 1138, 42, 0.42f, 2.72f, 0.60f, 0.30f);
            C("ztype", 200, 5500, 260, 3500, 5800, Induction.Supercharged, 0.5f, 3.3f, 8, "I", 4, "FR", 0, 950, 52, 0.55f, 2.98f, 0.85f, -0.20f);
            C("michelli", 115, 6200, 113, 5000, 7000, Induction.NA, 0, 1.3f, 4, "V", 5, "FF", 1, 850, 62, 0.50f, 2.33f, 0.72f, -0.10f);
            // ---- bikes (mass without rider)
            B("bati", 160, 9750, 123, 8000, 10700, 1.1f, 2, "V", 199, 1.43f, 0.40f);
            B("bati2", 160, 9750, 123, 8000, 10700, 1.1f, 2, "V", 199, 1.43f, 0.40f);
            B("akuma", 110, 12500, 65, 10500, 14000, 0.6f, 4, "I", 190, 1.39f, 0.40f);
            B("hakuchou", 175, 9800, 138, 7000, 11000, 1.3f, 4, "I", 220, 1.48f, 0.38f);
            B("hakuchou2", 195, 9800, 150, 7200, 11000, 1.3f, 4, "I", 230, 1.65f, 0.38f);
            B("defiler", 162, 9250, 128, 8000, 10500, 1.2f, 2, "V", 210, 1.59f, 0.55f);
            B("sanchez", 40, 8000, 28, 7500, 9500, 0.25f, 1, "I", 100, 1.48f, 0.60f);
            B("sanchez2", 40, 8000, 28, 7500, 9500, 0.25f, 1, "I", 100, 1.48f, 0.60f);
        }

        private static void C(string id, float hp, float hpRpm, float nm, float nmRpm, float redline, Induction ind, float boost, float litres, int cyl, string eng, int gears,
            string layout, float frontDrive, float kg, float frontPct, float cgH, float wb, float cdA, float clA, bool hybrid = false)
        {
            curated[id] = new CarSpec { Id = id, Curated = true, Hp = hp, HpRpm = hpRpm, Nm = nm, NmRpm = nmRpm, Redline = redline, Ind = ind, Boost = boost, Litres = litres, Cyl = cyl, Engine = eng,
                Gears = Math.Min(6, gears), Layout = layout, FrontDrive = layout == "FF" ? 1f : frontDrive, Kg = kg, FrontPct = frontPct / 100f, CgH = cgH, Wheelbase = wb, CdA = cdA, ClA = clA, Hybrid = hybrid,
                AeroFront = layout.EndsWith("M") || layout == "MR" ? 0.42f : 0.45f, Idle = redline > 9000 ? 1000f : 850f };
        }
        private static void B(string id, float hp, float hpRpm, float nm, float nmRpm, float redline, float litres, int cyl, string eng, float kg, float wb, float cdA)
        {
            curated[id] = new CarSpec { Id = id, Curated = true, Bike = true, Kind = "Bike", Hp = hp, HpRpm = hpRpm, Nm = nm, NmRpm = nmRpm, Redline = redline, Ind = Induction.NA, Litres = litres, Cyl = cyl, Engine = eng,
                Gears = 6, Layout = "Bike", FrontDrive = 0f, Kg = kg, FrontPct = 0.50f, CgH = 0.62f, Wheelbase = wb, CdA = cdA, ClA = 0f, Idle = 1200f, TyreR = 0.31f, Track = 0f };
        }

        private static readonly HashSet<string> Electric = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "voltic", "voltic2", "raiden", "cyclone", "cyclone2", "neon", "tezeract", "imorgon", "khamelion", "surge", "iwagen", "omnisegt", "virtue", "powersurge", "buffalo5", "dilettante", "dilettante2", "caddy", "caddy2", "caddy3", "airtug" };

        /// <summary>Gear count to build a model with (before it exists): the real car's, or by era; bikes and EVs keep theirs.</summary>
        public static int PlannedGears(int hash, int stockGears)
        {
            CarSpec c;
            if (cache.TryGetValue(hash, out c)) return c.Bike || c.Ind == Induction.Electric ? stockGears : c.Gears;
            var info = VehicleRegistry.Instance != null ? VehicleRegistry.Instance.Get(hash) : null;
            if (info == null) return stockGears;
            if (curated.TryGetValue(info.Id, out c)) return c.Bike ? stockGears : c.Gears;
            if (Electric.Contains(info.Id) || new Model(hash).IsBike || stockGears <= 1) return stockGears;
            int year = Era.Year(info);
            if (info.Class == VehicleClass.Commercial || info.Class == VehicleClass.Industrial) return Math.Max(stockGears, 6);
            return year < 1955 ? 3 : year < 1975 ? 4 : year < 1995 ? 5 : 6;
        }

        /// <summary>The spec of a model. Needs the vehicle (or a live one of that model) for its geometry the first time.</summary>
        public static CarSpec For(Vehicle car)
        {
            int hash = car.Model.Hash;
            CarSpec s; if (cache.TryGetValue(hash, out s)) return s;
            var info = VehicleRegistry.Instance != null ? VehicleRegistry.Instance.Get(hash) : null;
            string id = info != null ? info.Id : car.DisplayName.ToLowerInvariant();
            int year = Era.Year(info);
            CarSpec c;
            var h = car.HandlingData;
            if (curated.TryGetValue(id, out c)) s = Clone(c);
            else s = Generate(id, info, h, car, year);
            s.Year = year;
            if (s.Kind == null) s.Kind = car.Model.IsBike ? "Bike" : info != null ? info.Class.ToString() : "Sedans";
            // geometry from the model: wheelbase and track
            try { Geometry(car, s); } catch { }
            if (s.Tyre < 0 && !s.Bike) s.Tyre = Tyres.Factory(year, s.Layout != null && (s.Layout == "MR" || s.Layout == "AWD-M") && s.Hp / Math.Max(1f, s.Kg) * 1000f > 350f ? "Super" : s.Kind == "OffRoad" ? "OffRoad" : s.Kind, s.Hp / Math.Max(1f, s.Kg) * 1000f);
            if (s.Bike) s.Tyre = id.StartsWith("sanchez") || id == "manchez" || id == "bf400" || id == "esskey" ? Tyres.Knobbly : Tyres.BikeSport;
            cache[hash] = s;
            Core.Log.Info("Spec " + id + (s.Curated ? " (real data)" : " (generated)") + ": " + s.Hp.ToString("0") + " hp @" + s.HpRpm + ", " + s.Nm.ToString("0") + " Nm @" + s.NmRpm + ", " + s.EngineLabel + ", " + s.Gears + "-speed " + s.LayoutLabel
                + ", " + s.Kg.ToString("0") + " kg, " + (s.FrontPct * 100f).ToString("0") + "% front, CoG " + s.CgH.ToString("0.00") + " m, CdA " + s.CdA.ToString("0.00") + ", ClA " + s.ClA.ToString("0.00") + ", tyres " + Tyres.Types[s.Tyre].Name);
            return s;
        }

        private static CarSpec Clone(CarSpec c) { return (CarSpec)c.GetType().GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(c, null); }

        private static void Geometry(Vehicle car, CarSpec s)
        {
            Func<string, Vector3?> bone = n =>
            {
                int i = Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, car, n);
                if (i < 0) return null;
                return car.GetPositionOffset(Function.Call<Vector3>(Hash.GET_WORLD_POSITION_OF_ENTITY_BONE, car, i));
            };
            var lf = bone("wheel_lf"); var rf = bone("wheel_rf"); var lr = bone("wheel_lr"); var rr = bone("wheel_rr");
            if (s.Bike) { lf = bone("wheel_lf") ?? bone("wheel_f"); lr = bone("wheel_lr") ?? bone("wheel_r"); }
            if (lf.HasValue && lr.HasValue)
            {
                float wb = Math.Abs(lf.Value.Y - lr.Value.Y);
                if (wb > 1.0f && wb < 6f && !s.Curated) s.Wheelbase = wb;
                s.GeoFrontY = lf.Value.Y; s.GeoRearY = lr.Value.Y; s.GeoAxleZ = (lf.Value.Z + lr.Value.Z) * 0.5f; s.HasGeo = true;
            }
            if (lf.HasValue && rf.HasValue) { float t = Math.Abs(lf.Value.X - rf.Value.X); if (t > 0.8f && t < 3f) s.Track = t; }
        }

        // ------------------------------------------------------------------ generator
        private static CarSpec Generate(string id, VehicleInfo info, HandlingData h, Vehicle car, int year)
        {
            var s = new CarSpec { Id = id };
            bool bike = car.Model.IsBike;
            string k = bike ? "Bike" : info != null ? info.Class.ToString() : "Sedans";
            s.Kind = k; s.Bike = bike;
            float mass = h.Mass, df = h.InitialDriveForce, bias = HandlingMem.GetBias(h, HandlingMem.DriveBias);
            s.Kg = mass;
            // power: the class and era set the level, GTA's drive force places the car within it
            float hpt, dfMean, redline, cgh, cda, cla, tyreR = 0.33f; string eng = "I"; int cyl = 4; float litres = 2.0f; Induction ind = Induction.NA;
            switch (k)
            {
                case "Super": hpt = year >= 2010 ? 520f : year >= 1995 ? 400f : 300f; dfMean = 0.33f; redline = 8500f; cgh = 0.42f; cda = 0.70f; cla = year >= 2012 ? 0.8f : 0.3f; eng = "V"; cyl = year >= 2014 ? 8 : 12; litres = year >= 2014 ? 4.0f : 6.0f; ind = year >= 2014 ? Induction.Turbo : Induction.NA; tyreR = 0.35f; break;
                case "Sports": hpt = year >= 2010 ? 270f : year >= 1990 ? 210f : 160f; dfMean = 0.30f; redline = 7400f; cgh = 0.47f; cda = 0.66f; cla = year >= 2005 ? 0.15f : 0.0f; eng = "I"; cyl = 6; litres = 3.0f; ind = year >= 2010 || (info != null && info.Tuner && year >= 1988) ? Induction.Turbo : Induction.NA; break;
                case "Muscle": hpt = year >= 2008 ? 240f : year >= 1975 ? 130f : 230f; dfMean = 0.30f; redline = year >= 2005 ? 6400f : 5600f; cgh = 0.54f; cda = 0.85f; cla = -0.15f; eng = "V"; cyl = 8; litres = year >= 1975 && year < 2005 ? 5.0f : 6.4f; tyreR = 0.34f; break;
                case "SportsClassics": hpt = 180f; dfMean = 0.27f; redline = 6500f; cgh = 0.50f; cda = 0.78f; cla = -0.15f; eng = "V"; cyl = 8; litres = 4.0f; break;
                case "Coupes": hpt = year >= 2005 ? 190f : 140f; dfMean = 0.26f; redline = 6800f; cgh = 0.50f; cda = 0.68f; cla = 0f; eng = "V"; cyl = 8; litres = 4.4f; ind = year >= 2012 ? Induction.Turbo : Induction.NA; break;
                case "Sedans": hpt = year >= 2005 ? 130f : 100f; dfMean = 0.22f; redline = 6500f; cgh = 0.55f; cda = 0.70f; cla = -0.05f; eng = "I"; cyl = 4; litres = 2.4f; ind = year >= 2014 ? Induction.Turbo : Induction.NA; break;
                case "Compacts": hpt = year >= 2005 ? 95f : 75f; dfMean = 0.20f; redline = 6500f; cgh = 0.55f; cda = 0.65f; cla = -0.05f; eng = "I"; cyl = 4; litres = 1.6f; tyreR = 0.30f; break;
                case "SUVs": hpt = year >= 2005 ? 140f : 100f; dfMean = 0.24f; redline = 6000f; cgh = 0.72f; cda = 1.00f; cla = -0.1f; eng = "V"; cyl = 8; litres = 5.0f; tyreR = 0.38f; break;
                case "OffRoad": hpt = 120f; dfMean = 0.26f; redline = 6000f; cgh = 0.70f; cda = 1.05f; cla = -0.1f; eng = "V"; cyl = 8; litres = 5.0f; tyreR = 0.40f; break;
                case "Vans": hpt = 80f; dfMean = 0.18f; redline = 5000f; cgh = 0.85f; cda = 1.30f; cla = -0.1f; eng = "V"; cyl = 8; litres = 5.0f; tyreR = 0.36f; break;
                case "Commercial": case "Industrial": hpt = 45f; dfMean = 0.13f; redline = 2600f; cgh = 1.10f; cda = 3.5f; cla = 0f; cyl = 6; litres = 12.0f; ind = Induction.Turbo; tyreR = 0.50f; s.Diesel = true; break;
                case "Utility": case "Service": hpt = 60f; dfMean = 0.16f; redline = 4000f; cgh = 0.90f; cda = 1.6f; cla = 0f; cyl = 6; litres = 6.7f; ind = Induction.Turbo; tyreR = 0.40f; s.Diesel = true; break;
                case "Bike":
                    {
                        bool dirt = id.Contains("sanchez") || id.Contains("manchez") || id == "bf400" || id == "esskey" || id == "enduro" || id == "cliffhanger" || id == "gargoyle";
                        bool cruiser = id.Contains("daemon") || id.Contains("avarus") || id.Contains("chimera") || id.Contains("hexer") || id.Contains("innovation") || id.Contains("nightblade") || id.Contains("sanctus") || id.Contains("wolfsbane") || id.Contains("zombie") || id.Contains("sovereign") || id.Contains("rrocket") || id.Contains("bagger") || id.Contains("cliff");
                        hpt = dirt ? 330f : cruiser ? 320f : 620f; dfMean = 0.28f; redline = dirt ? 10000f : cruiser ? 6000f : 13000f; cgh = 0.62f; cda = dirt ? 0.60f : cruiser ? 0.65f : 0.40f; cla = 0f;
                        eng = cruiser ? "V" : "I"; cyl = dirt ? 1 : cruiser ? 2 : 4; litres = dirt ? 0.45f : cruiser ? 1.6f : 0.9f; tyreR = 0.31f; break;
                    }
                default: hpt = 110f; dfMean = 0.22f; redline = 6000f; cgh = 0.60f; cda = 0.9f; cla = 0f; break;
            }
            float rel = Clamp((float)Math.Pow(Math.Max(0.05f, df) / dfMean, 1.5), 0.6f, 1.6f);
            float massKg = bike ? mass + 75f : mass;
            s.Hp = (float)Math.Round(hpt * rel * massKg / 1000f / 5f) * 5f;
            s.Hp = Math.Max(bike ? 25f : 60f, s.Hp);
            if (Electric.Contains(id)) { ind = Induction.Electric; redline = 12000f; cyl = 0; litres = 0f; }
            if (k == "Muscle" && year >= 2015 && s.Hp > 600f) ind = Induction.Supercharged;
            if (eng == "V" && cyl == 12 && s.Hp < 500f) cyl = 8;
            if (k == "Sports" && s.Hp > 500f) { eng = "V"; cyl = 8; litres = 4.0f; }
            s.Ind = ind; s.Engine = eng; s.Cyl = cyl; s.Litres = litres; s.Redline = redline;
            s.Idle = s.Diesel ? 650f : redline > 9000f ? 1200f : 850f;
            s.Boost = ind == Induction.Turbo ? (s.Diesel ? 1.6f : 1.0f) : ind == Induction.Supercharged ? 0.8f : 0f;
            switch (ind)
            {
                case Induction.Turbo: s.HpRpm = Round50(redline * (s.Diesel ? 0.85f : 0.82f)); s.NmRpm = Round50(redline * 0.42f); s.Nm = s.Hp * 7121f / s.HpRpm * (s.Diesel ? 2.0f : 1.42f); break;
                case Induction.Supercharged: s.HpRpm = Round50(redline * 0.9f); s.NmRpm = Round50(redline * 0.55f); s.Nm = s.Hp * 7121f / s.HpRpm * 1.28f; break;
                case Induction.Electric: s.HpRpm = 6000f; s.NmRpm = 1000f; s.Nm = s.Hp * 7121f / 6000f; break;
                default: s.HpRpm = Round50(redline * 0.9f); s.NmRpm = Round50(redline * (cyl >= 8 && litres >= 5f ? 0.55f : 0.65f)); s.Nm = s.Hp * 7121f / s.HpRpm * (cyl >= 8 && litres >= 5f ? 1.35f : 1.18f); break;
            }
            s.Nm = (float)Math.Round(s.Nm / 5f) * 5f;
            // gearbox by era
            int g = year < 1955 ? 3 : year < 1975 ? 4 : year < 1995 ? 5 : 6;
            if (s.Diesel) g = 6; if (ind == Induction.Electric) g = 1; if (bike) g = k == "Bike" && redline <= 6000f ? 5 : 6;
            s.Gears = Math.Min(Math.Max(1, h.InitialDriveGears > 0 && ind == Induction.Electric ? 1 : g), 6);
            // layout and balance
            bool mid = k == "Super" || (info != null && (info.Make ?? "").ToLowerInvariant().Contains("pegassi"));
            bool rearEngine = id.StartsWith("comet") || id.StartsWith("pfister") || id == "weevil" || id == "issi3";
            if (bike) { s.Layout = "Bike"; s.FrontDrive = 0f; s.FrontPct = 0.5f; }
            else if (bias >= 0.9f) { s.Layout = "FF"; s.FrontDrive = 1f; s.FrontPct = 0.61f; }
            else if (bias > 0.1f) { s.Layout = mid ? "AWD-M" : "AWD-F"; s.FrontDrive = bias; s.FrontPct = mid ? 0.43f : 0.57f; }
            else { s.Layout = mid ? "MR" : rearEngine ? "RR" : "FR"; s.FrontDrive = 0f; s.FrontPct = mid ? 0.42f : rearEngine ? 0.39f : k == "Muscle" ? 0.56f : 0.53f; }
            if (k == "SUVs" || k == "Vans" || k == "OffRoad") s.FrontPct = 0.55f;
            s.CgH = cgh; s.CdA = cda; s.ClA = cla; s.TyreR = tyreR; s.AeroFront = mid ? 0.42f : 0.45f;
            return s;
        }
        private static float Round50(float v) { return (float)Math.Round(v / 50f) * 50f; }
        private static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
    }
}
