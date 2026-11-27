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
                try { list = Build(); }
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
                try { var z = GTA.World.GetZoneLocalizedName(r.Start); if (!string.IsNullOrEmpty(z)) r.Name = z + " Rally Stage"; } catch { }
                races.AddCustom(r);
            }
            Log.Info("Rally stages: " + l.Count);
        }

        private static List<RaceDefinition> Build()
        {
            var outList = new List<RaceDefinition>();
            if (!RoadGraph.EnsureLoaded()) return outList;
            var rnd = new Random(4242); int n = RoadGraph.Count; var used = new List<Vector3>();
            for (int attempt = 0; attempt < 600 && outList.Count < 8; attempt++)
            {
                int a = rnd.Next(n);
                if ((RoadGraph.Flags(a) & RoadGraph.NodeFlags.Gravel) == 0) continue;
                var pa = RoadGraph.Pos(a);
                if (used.Any(u => u.DistanceTo2D(pa) < 900f)) continue;
                // a gravel end point 2-4.5 km away
                int b = -1;
                for (int k = 0; k < 80; k++) { int c = rnd.Next(n); if ((RoadGraph.Flags(c) & RoadGraph.NodeFlags.Gravel) == 0) continue; float d = RoadGraph.Pos(c).DistanceTo2D(pa); if (d > 2000f && d < 4500f) { b = c; break; } }
                if (b < 0) continue;
                var route = RoadGraph.RouteRally(pa, RoadGraph.Pos(b));
                if (route == null || route.Count < 20) continue;
                float len = 0; for (int i = 1; i < route.Count; i++) len += route[i].DistanceTo(route[i - 1]);
                if (len < 2500f || len > 6500f) continue;
                int dirt = 0; foreach (var p in route) { int idx = RoadGraph.NearestIndex(p); if (idx >= 0 && (RoadGraph.Flags(idx) & (RoadGraph.NodeFlags.Gravel | RoadGraph.NodeFlags.Backroad)) != 0 && (RoadGraph.Flags(idx) & RoadGraph.NodeFlags.Freeway) == 0) dirt++; }
                if (dirt < route.Count * 0.55f) continue;
                // via points every ~300 m so the race router follows this exact line
                var pts = new List<float[]>(); float acc = 0;
                float h0 = (float)(Math.Atan2(-(route[3].X - route[0].X), route[3].Y - route[0].Y) * 180.0 / Math.PI);
                pts.Add(new[] { route[0].X, route[0].Y, route[0].Z, h0 });
                for (int i = 1; i < route.Count - 1; i++) { acc += route[i].DistanceTo(route[i - 1]); if (acc >= 300f) { pts.Add(new[] { route[i].X, route[i].Y, route[i].Z, 0f }); acc = 0; } }
                var last = route[route.Count - 1]; pts.Add(new[] { last.X, last.Y, last.Z, 0f });
                string name = Names[outList.Count % Names.Length];
                float km = len / 1000f;
                outList.Add(new RaceDefinition { Id = "rally_" + outList.Count, Name = name + " Stage", Type = "Rally", Laps = 1, Opponents = 0, Points = pts, CashReward = (long)(km * 2200), RepReward = (long)(km * 90), Tier = 2, RequiredRep = 0 });
                used.Add(pa);
            }
            return outList;
        }
    }
}
