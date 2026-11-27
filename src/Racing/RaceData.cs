using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using GTA;
using GTA.Math;
using MidnightClub.Core;

namespace MidnightClub.Racing
{
    public enum RaceType { Sprint, Circuit, TimeTrial, Drift, HeadToHead, Rally, Drag, Elimination, Offroad, CrossCountry, HillClimb, Gymkhana, Moto }
    public enum Discipline { Street, Drift, Offroad, Drag, Bikes }

    [DataContract]
    public sealed class RaceDefinition
    {
        [DataMember] public string Id, Name, Type = "Sprint";
        [DataMember] public int Laps = 1, Opponents = 5;
        [DataMember] public List<float[]> Points = new List<float[]>();   // [x,y,z,heading]: start, via..., finish (circuits loop back to start)
        [DataMember] public long EntryFee, CashReward, RepReward, RequiredRep;
        [DataMember] public bool NightOnly;
        [DataMember] public float TargetTimeSec, TargetScore;            // gold targets for time trials / drift
        [DataMember] public string Classes;                              // optional vehicle class restriction, comma separated
        [DataMember] public int Tier = 1;

        public RaceType Kind { get { RaceType t; return Enum.TryParse(Type, true, out t) ? t : RaceType.Sprint; } }
        public Vector3 Start { get { var p = Points[0]; return new Vector3(p[0], p[1], p[2]); } }
        public float StartHeading { get { var p = Points[0]; return p.Length > 3 ? p[3] : 0f; } }
        public bool HasOpponents { get { var k = Kind; return k != RaceType.TimeTrial && k != RaceType.Drift && k != RaceType.Rally && k != RaceType.HillClimb && k != RaceType.Gymkhana; } }
        /// <summary>Scored by points (drift, gymkhana) rather than time.</summary>
        public bool Scored { get { return Kind == RaceType.Drift || Kind == RaceType.Gymkhana; } }
        public bool BikesOnly { get { return Kind == RaceType.Moto; } }
        public Discipline Discipline
        {
            get
            {
                switch (Kind)
                {
                    case RaceType.Drift: case RaceType.Gymkhana: return Discipline.Drift;
                    case RaceType.Rally: case RaceType.Offroad: case RaceType.CrossCountry: case RaceType.HillClimb: return Discipline.Offroad;
                    case RaceType.Drag: return Discipline.Drag;
                    case RaceType.Moto: return Discipline.Bikes;
                }
                return Discipline.Street;
            }
        }
        /// <summary>Discipline level needed (generated events climb 1 / 3 / 5 / 7 with their tier).</summary>
        public int LevelNeeded { get { return Discipline == Discipline.Street ? 0 : Math.Max(1, (Tier - 1) * 2 + 1); } }
        public bool Solo { get { return !HasOpponents; } }
        public bool Lapped { get { return Kind == RaceType.Circuit; } }
    }

    [DataContract]
    public sealed class ChampionshipDefinition
    {
        [DataMember] public string Id, Name;
        [DataMember] public List<string> Races = new List<string>();
        [DataMember] public long EntryFee, CashPrize, RepPrize, RequiredRep;
        [DataMember] public int Tier = 1;
    }

    [DataContract]
    public sealed class RaceLibraryFile
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<RaceDefinition> Races = new List<RaceDefinition>();
        [DataMember] public List<ChampionshipDefinition> Championships = new List<ChampionshipDefinition>();
    }

    /// <summary>Data-driven race list (data\races.json). Generated once from verified road-side locations
    /// (GTA Online garage entrances); routes between points come from the game's GPS at race start,
    /// so races always run on real roads. Edit or add races in the JSON without touching code.</summary>
    public static class RaceLibrary
    {
        private static readonly DataContractJsonSerializer Ser = new DataContractJsonSerializer(typeof(RaceLibraryFile), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public static RaceLibraryFile Load(string dataDir)
        {
            var file = Path.Combine(dataDir, "races.json");
            if (File.Exists(file))
            {
                try { using (var fs = File.OpenRead(file)) { var lib = (RaceLibraryFile)Ser.ReadObject(fs); if (lib != null && lib.Races.Count > 0) return lib; } }
                catch (Exception ex) { Log.Error("races.json unreadable, regenerating (old file kept as races.json.bad)", ex); try { File.Copy(file, file + ".bad", true); } catch { } }
            }
            var gen = Generate();
            try { using (var fs = File.Create(file)) Ser.WriteObject(fs, gen); } catch (Exception ex) { Log.Error("Writing races.json", ex); }
            Log.Info("Generated " + gen.Races.Count + " races and " + gen.Championships.Count + " championships");
            return gen;
        }

        private static float[] P(float[] e) { return new[] { e[0], e[1], e[2], e.Length > 3 ? e[3] : 0f }; }
        private static float D(float[] a, float[] b) { return (float)Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1])); }
        private static string Zone(float[] p) { try { var z = GTA.World.GetZoneLocalizedName(new Vector3(p[0], p[1], p[2])); return string.IsNullOrEmpty(z) ? "Los Santos" : z; } catch { return "Los Santos"; } }

        public static RaceLibraryFile Generate()
        {
            // Road-side nodes: Online garage entrances in Los Santos proper (verified positions).
            var nodes = Garages.OnlineGarageCatalog.All.Select(g => P(g.Entrance)).Where(p => p[1] < 900f && p[1] > -3000f).ToList();
            var rnd = new Random(4242);
            var lib = new RaceLibraryFile();
            Func<float, float, float[][]> pair = (min, max) =>
            {
                for (int tries = 0; tries < 400; tries++)
                {
                    var a = nodes[rnd.Next(nodes.Count)]; var b = nodes[rnd.Next(nodes.Count)];
                    float d = D(a, b);
                    if (d >= min && d <= max) return new[] { a, b };
                }
                return new[] { nodes[0], nodes[1] };
            };
            int n = 0;
            Action<string, RaceType, float, float, int, int, long, long, long> add = (prefix, type, min, max, opp, laps, fee, cash, rep) =>
            {
                var ab = pair(min, max);
                var r = new RaceDefinition { Id = "r" + (++n).ToString("00"), Type = type.ToString(), Opponents = opp, Laps = laps, EntryFee = fee, CashReward = cash, RepReward = rep };
                r.Points.Add(ab[0]);
                if (type == RaceType.Circuit)
                {
                    var via = nodes.Where(x => D(x, ab[0]) > 500 && D(x, ab[0]) < max && D(x, ab[1]) > 400).OrderBy(x => rnd.Next()).FirstOrDefault() ?? ab[1];
                    r.Points.Add(ab[1]); r.Points.Add(via);
                    r.Name = Zone(ab[0]) + " " + prefix;
                }
                else { r.Points.Add(ab[1]); r.Name = Zone(ab[0]) + " to " + Zone(ab[1]) + " " + prefix; }
                lib.Races.Add(r);
            };
            // Tier 1-2 street races, then longer/faster ones.
            add("Sprint", RaceType.Sprint, 1200, 2200, 3, 1, 250, 2500, 120);
            add("Sprint", RaceType.Sprint, 1300, 2500, 4, 1, 300, 3000, 140);
            add("Time Trial", RaceType.TimeTrial, 1500, 2800, 0, 1, 0, 2000, 100);
            add("Drift", RaceType.Drift, 700, 1300, 0, 1, 0, 2000, 120);
            add("Circuit", RaceType.Circuit, 900, 1800, 3, 2, 400, 3500, 160);
            add("Run", RaceType.HeadToHead, 1500, 2600, 1, 1, 500, 4000, 180);
            add("Sprint", RaceType.Sprint, 2000, 3200, 5, 1, 600, 5000, 220);
            add("Circuit", RaceType.Circuit, 1200, 2200, 5, 2, 800, 6500, 260);
            add("Time Trial", RaceType.TimeTrial, 2500, 3800, 0, 1, 0, 4000, 200);
            add("Drift", RaceType.Drift, 900, 1600, 0, 1, 0, 4000, 220);
            add("Sprint", RaceType.Sprint, 2600, 4200, 5, 1, 1200, 9000, 320);
            add("Run", RaceType.HeadToHead, 2500, 4000, 1, 1, 1500, 11000, 360);
            add("Circuit", RaceType.Circuit, 1500, 2600, 6, 3, 1500, 12000, 380);
            add("Sprint", RaceType.Sprint, 3200, 5000, 6, 1, 2500, 16000, 480);
            add("Circuit", RaceType.Circuit, 1800, 3000, 7, 3, 3000, 22000, 600);
            add("Sprint", RaceType.Sprint, 3800, 6000, 7, 1, 5000, 30000, 750);
            long[] reqRep = { 0, 0, 0, 0, 150, 250, 400, 600, 800, 1000, 1500, 1800, 2400, 3200, 4500, 6000 };
            for (int i = 0; i < lib.Races.Count; i++)
            {
                lib.Races[i].RequiredRep = reqRep[Math.Min(i, reqRep.Length - 1)];
                lib.Races[i].Tier = 1 + Math.Min(4, (int)(reqRep[Math.Min(i, reqRep.Length - 1)] / 1200));
                lib.Races[i].NightOnly = i >= 6 && i % 3 == 0;
            }
            lib.Championships.Add(new ChampionshipDefinition { Id = "c1", Name = "Street Beginner Cup", Races = new List<string> { "r01", "r02", "r05" }, EntryFee = 1000, CashPrize = 12000, RepPrize = 500, RequiredRep = 300, Tier = 1 });
            lib.Championships.Add(new ChampionshipDefinition { Id = "c2", Name = "Underground Series", Races = new List<string> { "r07", "r08", "r11", "r13" }, EntryFee = 4000, CashPrize = 45000, RepPrize = 1500, RequiredRep = 1500, Tier = 3 });
            lib.Championships.Add(new ChampionshipDefinition { Id = "c3", Name = "Midnight Club Invitational", Races = new List<string> { "r13", "r14", "r15", "r16" }, EntryFee = 15000, CashPrize = 150000, RepPrize = 5000, RequiredRep = 6000, Tier = 5 });
            return lib;
        }
    }
}
