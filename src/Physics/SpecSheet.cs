using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace MidnightClub.Physics
{
    // ------------------------------------------------------------------ spec sheet (data\specs\*.json, built by tools/specgen)
    // One sheet per vehicle: the real counterpart's chassis, engine, drivetrain, suspension, brakes, tyres and aero.
    // "kind" leaves room for boats and aircraft (their own sections, same file layout).

    [DataContract] public sealed class SpecFile { [DataMember] public int schema; [DataMember] public string gtaClass; [DataMember] public List<SpecSheet> vehicles; }

    [DataContract] public sealed class SpecSheet
    {
        [DataMember] public string id, kind, gtaClass, character, body;
        [DataMember] public SpecCounterpart counterpart;
        [DataMember] public SpecChassis chassis;
        [DataMember] public SpecEngine engine;
        [DataMember] public SpecDrivetrain drivetrain;
        [DataMember] public SpecSuspension suspension;
        [DataMember] public SpecBrakes brakes;
        [DataMember] public SpecTyres tyres;
        [DataMember] public SpecAero aero;
        [DataMember] public Dictionary<string, string> provenance;
        public bool Bike { get { return kind == "bike"; } }
        public bool Truck { get { return kind == "truck"; } }
    }
    [DataContract] public sealed class SpecCounterpart { [DataMember] public string name; [DataMember] public int year; [DataMember] public bool estimated; }
    [DataContract] public sealed class SpecChassis
    {
        [DataMember] public float massKg, frontPct, cgHeightM, wheelbaseM, trackFrontM, trackRearM;
        [DataMember] public SpecInertia inertia;
    }
    [DataContract] public sealed class SpecInertia { [DataMember] public float yawDI, rollK; }
    [DataContract] public sealed class SpecEngine
    {
        [DataMember] public string layout, code, induction, chargerType;
        [DataMember] public int cyl, chargerCount;
        [DataMember] public float litres, boostBar, powerHp, powerRpm, torqueNm, torqueRpm, redline, idle;
        [DataMember] public bool diesel, hybrid;
    }
    [DataContract] public sealed class SpecDrivetrain
    {
        [DataMember] public string layout;
        [DataMember] public float frontShare, efficiency;
        [DataMember] public SpecGearbox gearbox;
        [DataMember] public SpecDiffs diffs;
    }
    [DataContract] public sealed class SpecGearbox
    {
        [DataMember] public string type;
        [DataMember] public int gears, shiftMs;
        [DataMember] public float[] ratios;
        [DataMember] public float reverse, finalDrive;
    }
    [DataContract] public sealed class SpecDiffs { [DataMember(Name = "F")] public SpecDiff F; [DataMember(Name = "R")] public SpecDiff R; [DataMember(Name = "C")] public SpecDiff C; }
    [DataContract] public sealed class SpecDiff { [DataMember] public string type; [DataMember] public float preloadNm, lockPower, lockCoast; }
    [DataContract] public sealed class SpecSuspension { [DataMember] public SpecAxle front, rear; [DataMember] public float rollGradientDegG; }
    [DataContract] public sealed class SpecAxle
    {
        [DataMember] public string type, code;
        [DataMember] public float rideHz, wheelRateNmm, springRateNmm, motionRatio, rollCentreM, antiPct, travelBumpM, travelDroopM, camberDeg, toeDeg, casterDeg;
        [DataMember] public SpecDamping damping;
        [DataMember] public SpecArb arb;
    }
    [DataContract] public sealed class SpecDamping { [DataMember] public float bumpLow, bumpHigh, reboundLow, reboundHigh, kneeMs; }
    [DataContract] public sealed class SpecArb { [DataMember] public bool fitted; [DataMember] public float rollNmDeg, wheelRateNmm, ratioToSprings; }
    [DataContract] public sealed class SpecBrakes
    {
        [DataMember] public SpecBrake front, rear;
        [DataMember] public float padMu, biasFront, maxForceN, maxDecelG;
        [DataMember] public bool abs;
    }
    [DataContract] public sealed class SpecBrake { [DataMember] public string type; [DataMember] public int sizeMm, pistons, count; [DataMember] public float torqueNm; }
    [DataContract] public sealed class SpecTyres { [DataMember] public SpecTyre front, rear; [DataMember] public float unsprungFrontKg, unsprungRearKg; }
    [DataContract] public sealed class SpecTyre
    {
        [DataMember] public string size, compound, compoundName;
        [DataMember] public float widthM, aspect, rimIn, sidewallM, radiusM, pressureBar, verticalNm, lateralCarcassNm, corneringPerLoad, peakSlipDeg, relaxM, contactLengthM, tOpt, tWin;
    }
    [DataContract] public sealed class SpecAero { [DataMember] public float cdA, clA, balanceFront; }

    /// <summary>All spec sheets: built into the DLL, and data\specs\*.json (same layout) can add or replace any vehicle.</summary>
    public static class SpecDb
    {
        private static readonly Dictionary<string, SpecSheet> byId = new Dictionary<string, SpecSheet>(StringComparer.OrdinalIgnoreCase);
        private static bool loaded;
        public static int Count { get { Load(null); return byId.Count; } }
        public static SpecSheet Get(string id) { Load(null); SpecSheet s; return id != null && byId.TryGetValue(id, out s) ? s : null; }

        public static void Load(string dataDir)
        {
            if (loaded) return; loaded = true;
            var ser = new DataContractJsonSerializer(typeof(SpecFile), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            int n = 0, files = 0;
            var asm = typeof(SpecDb).Assembly;
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (!name.StartsWith("specs.", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                try { using (var st = asm.GetManifestResourceStream(name)) n += Add((SpecFile)ser.ReadObject(st)); files++; }
                catch (Exception ex) { Core.Log.Error("Spec sheets " + name, ex); }
            }
            try
            {
                string dir = dataDir != null ? Path.Combine(dataDir, "specs") : null;
                if (dir != null && Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.json"))
                    {
                        try { using (var st = File.OpenRead(f)) { int k = Add((SpecFile)ser.ReadObject(st)); Core.Log.Info("Spec sheets: " + k + " from " + Path.GetFileName(f) + " (your own)"); } }
                        catch (Exception ex) { Core.Log.Error("Spec sheets " + f, ex); }
                    }
            }
            catch { }
            Core.Log.Info("Spec sheets: " + byId.Count + " vehicles (" + files + " built-in classes)");
        }
        private static int Add(SpecFile f)
        {
            if (f == null || f.vehicles == null) return 0;
            int n = 0; foreach (var s in f.vehicles) if (s != null && !string.IsNullOrEmpty(s.id)) { byId[s.id] = s; n++; }
            return n;
        }
    }

    /// <summary>Tyre physics from size, construction and compound (same rules as tools/specgen): cornering stiffness
    /// from the sidewall, peak slip angle, carcass vertical and lateral stiffness, relaxation length.</summary>
    public static class TyrePhys
    {
        public sealed class Comp { public string Key, Name; public float Mu, Peak, C, Cs, TOpt, TWin; }
        public sealed class Result { public float By, Cy, Ey, MuY, MuX, Kt, Klat, Relax, PeakDeg, Radius, Csc; public string Compound; }

        public static readonly Dictionary<string, Comp> Comps = new Dictionary<string, Comp>
        {
            { "bias",  new Comp { Key = "bias",  Name = "Bias-ply",         Mu = 0.82f, Peak = 9.0f, C = 1.45f, Cs = -3f, TOpt = 55, TWin = 45 } },
            { "tour",  new Comp { Key = "tour",  Name = "Touring",          Mu = 0.92f, Peak = 7.0f, C = 1.50f, Cs = 0f,  TOpt = 55, TWin = 45 } },
            { "at",    new Comp { Key = "at",    Name = "All-terrain",      Mu = 0.86f, Peak = 8.0f, C = 1.45f, Cs = -2f, TOpt = 50, TWin = 50 } },
            { "mt",    new Comp { Key = "mt",    Name = "Mud-terrain",      Mu = 0.80f, Peak = 9.5f, C = 1.45f, Cs = -4f, TOpt = 45, TWin = 50 } },
            { "uhp",   new Comp { Key = "uhp",   Name = "Ultra-high perf.", Mu = 0.99f, Peak = 6.0f, C = 1.60f, Cs = 1.5f, TOpt = 65, TWin = 42 } },
            { "sport", new Comp { Key = "sport", Name = "Sport",            Mu = 1.04f, Peak = 5.5f, C = 1.65f, Cs = 2.5f, TOpt = 70, TWin = 40 } },
            { "semi",  new Comp { Key = "semi",  Name = "Semi-slick",       Mu = 1.14f, Peak = 5.0f, C = 1.70f, Cs = 4f,  TOpt = 80, TWin = 38 } },
            { "slick", new Comp { Key = "slick", Name = "Race slick",       Mu = 1.40f, Peak = 4.5f, C = 1.80f, Cs = 7f,  TOpt = 90, TWin = 30 } },
            { "drift", new Comp { Key = "drift", Name = "Drift",            Mu = 0.88f, Peak = 8.0f, C = 1.45f, Cs = 0.5f, TOpt = 80, TWin = 45 } },
            { "drag",  new Comp { Key = "drag",  Name = "Drag radial",      Mu = 0.86f, Peak = 8.0f, C = 1.60f, Cs = -3f, TOpt = 70, TWin = 35 } },
            { "hgv",   new Comp { Key = "hgv",   Name = "Truck",            Mu = 0.80f, Peak = 6.0f, C = 1.50f, Cs = -4f, TOpt = 50, TWin = 50 } },
            { "btour", new Comp { Key = "btour", Name = "Touring (bike)",   Mu = 1.00f, Peak = 7.0f, C = 1.55f, Cs = 0f,  TOpt = 60, TWin = 45 } },
            { "bsport",new Comp { Key = "bsport",Name = "Sport (bike)",     Mu = 1.08f, Peak = 6.0f, C = 1.65f, Cs = 2f,  TOpt = 70, TWin = 35 } },
            { "btrack",new Comp { Key = "btrack",Name = "Track (bike)",     Mu = 1.22f, Peak = 5.0f, C = 1.70f, Cs = 4f,  TOpt = 85, TWin = 28 } },
            { "knob",  new Comp { Key = "knob",  Name = "Off-road knobbly", Mu = 0.80f, Peak = 10f,  C = 1.45f, Cs = -6f, TOpt = 45, TWin = 50 } },
        };
        /// <summary>The compound key of a shop tyre type (Tyres.Types index).</summary>
        public static string KeyOf(int typeId)
        {
            switch (typeId) { case 0: return "bias"; case 2: return "uhp"; case 3: return "sport"; case 4: return "semi"; case 5: return "slick"; case 6: return "drift"; case 7: return "drag"; case 8: return "bsport"; case 9: return "btrack"; case 10: return "knob"; default: return "tour"; }
        }

        private static readonly Dictionary<string, float> eCache = new Dictionary<string, float>();
        private static float FitE(float B, float C, float peakRad)
        {
            string k = B.ToString("0.00") + "|" + C.ToString("0.00") + "|" + peakRad.ToString("0.0000");
            float e; if (eCache.TryGetValue(k, out e)) return e;
            float bestD = 9f; e = 0f;
            for (int i = -400; i < 99; i++)
            {
                float E = i / 100f, at = 0f, bv = 0f;
                for (int j = 1; j < 600; j++) { float a = j * 0.001f, f = Tyres.Shape(a, B, C, E); if (f > bv) { bv = f; at = a; } }
                float d = Math.Abs(at - peakRad); if (d < bestD) { bestD = d; e = E; }
            }
            eCache[k] = e; return e;
        }

        public static Result Compute(SpecTyre t, string compound, float fzNom, string kind)
        {
            Comp c; if (!Comps.TryGetValue(compound ?? "tour", out c)) c = Comps["tour"];
            float w = t.widthM > 0.05f ? t.widthM : 0.205f, ar = t.aspect > 0.1f ? t.aspect : 0.55f, sw = w * ar, press = t.pressureBar > 0.5f ? t.pressureBar : 2.4f;
            if (c.Key == "slick" && kind == "car") press = Math.Min(press, 1.9f);
            float csc = 11f + 22f * (float)Math.Pow(0.80f - Math.Min(0.80f, ar), 0.85) + c.Cs;
            if (kind != "truck") csc *= (float)Math.Pow(press / 2.4f, 0.15);
            csc = Math.Max(6f, Math.Min(32f, csc));
            float peak = c.Peak * (float)Math.Pow(16f / csc, 0.55);
            float B = csc / c.C, E = FitE(B, c.C, peak * 0.0174533f);
            float kt = (60000f * press + 70000f) * (float)Math.Pow(w / 0.205f, 0.5) * (float)Math.Pow(0.113f / sw, 0.3);
            if (kind == "truck") kt = (52000f * press + 120000f) * (float)Math.Pow(w / 0.295f, 0.5) * (float)Math.Pow(0.24f / sw, 0.3);
            float klat = 180000f * (w / 0.225f) * (float)Math.Pow(0.12f / sw, 0.8) * (float)Math.Pow(press / 2.4f, 0.5);
            if (kind == "truck") klat *= 2f;
            float relax = Math.Max(0.18f, Math.Min(0.9f, csc * fzNom / klat));
            return new Result { By = B, Cy = c.C, Ey = E, MuY = c.Mu, MuX = c.Mu * 1.04f, Kt = kt, Klat = klat, Relax = relax, PeakDeg = peak, Radius = t.radiusM > 0.15f ? t.radiusM : 0.33f, Csc = csc, Compound = c.Key };
        }
    }
}
