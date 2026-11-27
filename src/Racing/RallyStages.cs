using System;
using System.Collections.Generic;
using System.Linq;
using GTA.Math;
using MidnightClub.Core;

namespace MidnightClub.Racing
{
    /// <summary>Rally stages generated from the road network: point-to-point runs of 2.5-6 km that keep to dirt and
    /// back roads (Blaine County, Paleto forest, Tongva hills...). Built on a worker thread when Midnight Club starts
    /// (same stages every time: fixed seed), added to the race list on the main thread.</summary>
    public static class RallyStages
    {
        private static readonly object gate = new object();
        private static List<RaceDefinition> ready; private static bool running, built;
        private static readonly string[] Names = { "Grapeseed Fields", "Mount Chiliad Descent", "Raton Canyon", "Paleto Forest", "Sandy Shores Dunes", "Tongva Hills", "Great Chaparral", "Harmony Outback", "Zancudo Ridge", "Alamo Sea Shore" };

        public static void Generate(RaceManager races)
        {
            if (built) { lock (gate) { if (ready != null) foreach (var r in ready) races.AddCustom(r); } return; }
            if (running) return; running = true;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                var list = new List<RaceDefinition>();
                try { list = Build(); list.AddRange(BuildDrag()); }
                catch (Exception ex) { Log.Error("Rally stage generation", ex); }
                lock (gate) { ready = list; pending = true; }
                built = true; running = false;
            });
        }

        private static bool pending;
        /// <summary>Main thread: hand finished stages to the race manager.</summary>
        public static void Drain(RaceManager races)
        {
            if (!pending) return;
            List<RaceDefinition> l; lock (gate) { l = ready; pending = false; }
            if (l == null) return;
            foreach (var r in l)
            {
                try { var z = GTA.World.GetZoneLocalizedName(r.Start); if (!string.IsNullOrEmpty(z)) r.Name = z + " " + (r.Type == "Drag" ? "Drag Strip" : r.Type == "Gymkhana" ? "Gymkhana" : r.Type == "Offroad" ? "Trail Race" : r.Type == "HillClimb" ? "Hill Climb" : r.Type == "Crawl" ? "Rock Crawl" : r.Type == "CrossCountry" ? "Cross Country" : "Rally Stage"); } catch { }
                races.AddCustom(r);
            }
            Log.Info("Rally stages: " + l.Count);
        }

        /// <summary>Dead-straight 400-600 m stretches (freeways, airport roads, desert highways) for drag races.</summary>
        private static List<RaceDefinition> BuildDrag()
        {
            var outList = new List<RaceDefinition>();
            var rnd = new Random(1402); int n = RoadGraph.Count; var used = new List<Vector3>();
            for (int attempt = 0; attempt < 4000 && outList.Count < 8; attempt++)
            {
                int a = rnd.Next(n); var f = RoadGraph.Flags(a);
                if ((f & RoadGraph.NodeFlags.Gps) == 0 || (f & (RoadGraph.NodeFlags.Gravel | RoadGraph.NodeFlags.Water | RoadGraph.NodeFlags.Junction)) != 0) continue;
                var pa = RoadGraph.Pos(a);
                if (used.Any(u => u.DistanceTo2D(pa) < 1500f)) continue;
                double ang = rnd.NextDouble() * Math.PI * 2; float len = 420f + (float)rnd.NextDouble() * 180f;
                var target = pa + new Vector3((float)Math.Cos(ang) * len, (float)Math.Sin(ang) * len, 0);
                int b = RoadGraph.Nearest(target, 25f); if (b < 0) continue;
                var pb = RoadGraph.Pos(b);
                if (Math.Abs(pb.Z - pa.Z) > 12f) continue;
                var route = RoadGraph.Route(pa, pb); if (route == null || route.Count < 3) continue;
                var dir = pb - pa; dir.Z = 0; float L = dir.Length(); if (L < 380f) continue; dir.Normalize();
                bool straight = true; float total = 0;
                for (int i = 0; i < route.Count; i++)
                {
                    var v = route[i] - pa; v.Z = 0; float along = Vector3.Dot(v, dir); var lat = v - dir * along;
                    if (lat.Length() > 3.5f) { straight = false; break; }
                    if (i > 0) total += route[i].DistanceTo2D(route[i - 1]);
                }
                if (!straight || total > L * 1.04f) continue;
                float h = (float)(Math.Atan2(-dir.X, dir.Y) * 180.0 / Math.PI);
                int ix = outList.Count; bool gym = ix >= 5;
                var pl = new List<float[]> { new[] { pa.X, pa.Y, pa.Z, h }, new[] { pb.X, pb.Y, pb.Z, 0f } };
                if (gym) outList.Add(new RaceDefinition { Id = "gym_" + (ix - 5), Name = "Gymkhana", Type = "Gymkhana", Laps = 1, Opponents = 0, Points = pl, CashReward = 6000 + 3000 * (ix - 5), RepReward = 220, Tier = 1 + (ix - 5), TargetScore = 14000 + 6000 * (ix - 5) });
                else outList.Add(new RaceDefinition { Id = "drag_" + ix, Name = "Drag Strip", Type = "Drag", Laps = 1, Opponents = 1, Points = pl, CashReward = 3500 + 2500 * (ix / 2), RepReward = 140 + 60 * (ix / 2), Tier = 1 + ix / 2 });
                used.Add(pa);
            }
            return outList;
        }

        private static List<RaceDefinition> Build()
        {
            var all = new List<RaceDefinition>();
            if (!RoadGraph.EnsureLoaded()) return all;
            all.AddRange(Dirt(4242, 9, 2500f, 6500f, 0.55f, 0f, "Rally", "rally_", 0, 2200, 90));
            all.AddRange(Dirt(777, 6, 2000f, 4500f, 0.5f, 0f, "Offroad", "offroad_", 5, 2600, 110));
            all.AddRange(Dirt(9091, 5, 1500f, 4000f, 0.35f, 110f, "HillClimb", "hill_", 0, 2600, 110));
            all.AddRange(Cross(5150, 5));
            all.AddRange(Dirt(31337, 4, 500f, 1400f, 0.6f, 70f, "Crawl", "crawl_", 0, 4500, 180));
            return all;
        }

        /// <summary>Dirt / back-road routes. minClimb > 0: the finish must be that many metres above the start.</summary>
        private static List<RaceDefinition> Dirt(int seed, int count, float minLen, float maxLen, float dirtShare, float minClimb, string type, string prefix, int opponents, int cashPerKm, int repPerKm)
        {
            var outList = new List<RaceDefinition>();
            var rnd = new Random(seed); int n = RoadGraph.Count; var used = new List<Vector3>();
            for (int attempt = 0; attempt < 900 && outList.Count < count; attempt++)
            {
                int a = rnd.Next(n);
                var fa = RoadGraph.Flags(a);
                if ((fa & (RoadGraph.NodeFlags.Gravel | RoadGraph.NodeFlags.Backroad)) == 0 || (fa & RoadGraph.NodeFlags.Water) != 0) continue;
                if (minClimb <= 0 && (fa & RoadGraph.NodeFlags.Gravel) == 0) continue;
                var pa = RoadGraph.Pos(a);
                if (used.Any(u => u.DistanceTo2D(pa) < 900f)) continue;
                int b = -1;
                for (int k = 0; k < 120; k++)
                {
                    int c = rnd.Next(n); var fc = RoadGraph.Flags(c);
                    if ((fc & (RoadGraph.NodeFlags.Gravel | RoadGraph.NodeFlags.Backroad)) == 0) continue;
                    var pc = RoadGraph.Pos(c); float d = pc.DistanceTo2D(pa);
                    if (d < minLen * 0.6f || d > maxLen * 0.8f) continue;
                    if (minClimb > 0 && pc.Z - pa.Z < minClimb) continue;
                    b = c; break;
                }
                if (b < 0) continue;
                var route = RoadGraph.RouteRally(pa, RoadGraph.Pos(b));
                if (route == null || route.Count < 20) continue;
                float len = 0; for (int i = 1; i < route.Count; i++) len += route[i].DistanceTo(route[i - 1]);
                if (len < minLen || len > maxLen) continue;
                int dirt = 0; foreach (var p in route) { int idx = RoadGraph.NearestIndex(p); if (idx >= 0 && (RoadGraph.Flags(idx) & (RoadGraph.NodeFlags.Gravel | RoadGraph.NodeFlags.Backroad)) != 0 && (RoadGraph.Flags(idx) & RoadGraph.NodeFlags.Freeway) == 0) dirt++; }
                if (dirt < route.Count * dirtShare) continue;
                var pts = new List<float[]>(); float acc = 0;
                float h0 = (float)(Math.Atan2(-(route[3].X - route[0].X), route[3].Y - route[0].Y) * 180.0 / Math.PI);
                pts.Add(new[] { route[0].X, route[0].Y, route[0].Z, h0 });
                for (int i = 1; i < route.Count - 1; i++) { acc += route[i].DistanceTo(route[i - 1]); if (acc >= 300f) { pts.Add(new[] { route[i].X, route[i].Y, route[i].Z, 0f }); acc = 0; } }
                var last = route[route.Count - 1]; pts.Add(new[] { last.X, last.Y, last.Z, 0f });
                float km = len / 1000f; int tier = 1 + outList.Count * 4 / Math.Max(1, count);
                outList.Add(new RaceDefinition { Id = prefix + outList.Count, Name = type, Type = type, Laps = 1, Opponents = opponents, Points = pts, CashReward = (long)(km * cashPerKm * (1 + 0.25 * (tier - 1))), RepReward = (long)(km * repPerKm), Tier = tier, RequiredRep = 0 });
                used.Add(pa);
            }
            return outList;
        }

        /// <summary>Cross country (Forza Horizon): far-apart checkpoints on dirt with no road to follow; cut across the terrain.</summary>
        private static List<RaceDefinition> Cross(int seed, int count)
        {
            var outList = new List<RaceDefinition>();
            var rnd = new Random(seed); int n = RoadGraph.Count; var used = new List<Vector3>();
            for (int attempt = 0; attempt < 2000 && outList.Count < count; attempt++)
            {
                int a = rnd.Next(n); if ((RoadGraph.Flags(a) & RoadGraph.NodeFlags.Gravel) == 0) continue;
                var pa = RoadGraph.Pos(a); if (used.Any(u => u.DistanceTo2D(pa) < 1500f)) continue;
                var pts = new List<float[]>(); var cur = pa; double heading = rnd.NextDouble() * Math.PI * 2;
                bool ok = true;
                for (int k = 0; k < 6 && ok; k++)
                {
                    int found = -1;
                    for (int t = 0; t < 60; t++)
                    {
                        double ang = heading + (rnd.NextDouble() - 0.5) * 1.4; float dist = 450f + (float)rnd.NextDouble() * 350f;
                        var target = cur + new Vector3((float)Math.Cos(ang) * dist, (float)Math.Sin(ang) * dist, 0);
                        int c = RoadGraph.Nearest(target, 120f);
                        if (c < 0 || (RoadGraph.Flags(c) & (RoadGraph.NodeFlags.Gravel | RoadGraph.NodeFlags.Backroad)) == 0 || (RoadGraph.Flags(c) & RoadGraph.NodeFlags.Water) != 0) continue;
                        var pc = RoadGraph.Pos(c); if (Math.Abs(pc.Z - cur.Z) > 90f || pc.DistanceTo2D(cur) < 350f) continue;
                        found = c; heading = Math.Atan2(pc.Y - cur.Y, pc.X - cur.X); break;
                    }
                    if (found < 0) { ok = false; break; }
                    if (pts.Count == 0) { float h0 = (float)(Math.Atan2(-(Math.Cos(heading)), Math.Sin(heading)) * 180.0 / Math.PI); pts.Add(new[] { cur.X, cur.Y, cur.Z, (float)(Math.Atan2(-Math.Cos(heading), Math.Sin(heading)) * 180.0 / Math.PI) }); }
                    cur = RoadGraph.Pos(found); pts.Add(new[] { cur.X, cur.Y, cur.Z, 0f });
                }
                if (!ok || pts.Count < 5) continue;
                int tier = 1 + outList.Count * 4 / count;
                outList.Add(new RaceDefinition { Id = "xc_" + outList.Count, Name = "Cross Country", Type = "CrossCountry", Laps = 1, Opponents = 5, Points = pts, CashReward = 9000 + 3000 * tier, RepReward = 350, Tier = tier });
                used.Add(pa);
            }
            return outList;
        }
    }
}
