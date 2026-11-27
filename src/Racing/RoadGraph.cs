using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using GTA.Math;
using MidnightClub.Core;

namespace MidnightClub.Racing
{
    /// <summary>The whole GTA V road network (67k vehicle path nodes, extracted from the game's paths data and embedded
    /// in the DLL). Fast A* routing on it gives road-following race routes anywhere on the map without the in-game GPS.</summary>
    public static class RoadGraph
    {
        [Flags] public enum NodeFlags : byte { Gps = 1, Freeway = 2, Gravel = 4, Water = 8, Backroad = 16, Junction = 32, TrafficLight = 64 }

        private static float[] xs, ys, zs; private static byte[] fl;
        private static int[] off, adj; private static byte[] lanes;
        private static Dictionary<long, List<int>> grid;
        private const float Cell = 100f;
        private static bool loaded, failed;
        public static int Count { get { return xs == null ? 0 : xs.Length; } }

        private static readonly object gate = new object();

        /// <summary>Starts loading on a worker thread (pure managed code, no natives) so entering Midnight Club never waits for it.</summary>
        public static void PreloadAsync() { System.Threading.ThreadPool.QueueUserWorkItem(_ => EnsureLoaded()); }

        public static bool EnsureLoaded()
        {
            if (loaded) return true; if (failed) return false;
            lock (gate) { if (loaded) return true; if (failed) return false; return LoadNow(); }
        }

        private static bool LoadNow()
        {
            try
            {
                var t0 = DateTime.Now;
                using (var s = typeof(RoadGraph).Assembly.GetManifestResourceStream("roads.bin.gz"))
                using (var gz = new GZipStream(s, CompressionMode.Decompress))
                using (var ms = new MemoryStream()) { gz.CopyTo(ms); ms.Position = 0; Read(new BinaryReader(ms)); }
                BuildGrid();
                loaded = true;
                Log.Info("Road graph: " + xs.Length + " nodes, " + adj.Length + " links in " + (int)(DateTime.Now - t0).TotalMilliseconds + " ms");
                return true;
            }
            catch (Exception ex) { failed = true; Log.Error("Road graph load failed", ex); return false; }
        }

        private static void Read(BinaryReader r)
        {
            var magic = new string(r.ReadChars(4)); r.ReadInt32(); int n = r.ReadInt32();
            if (magic != "MCRG") throw new InvalidDataException("bad road graph");
            xs = new float[n]; ys = new float[n]; zs = new float[n]; fl = new byte[n];
            for (int i = 0; i < n; i++) { xs[i] = r.ReadSingle(); ys[i] = r.ReadSingle(); zs[i] = r.ReadSingle(); fl[i] = r.ReadByte(); }
            int e = r.ReadInt32();
            var o = new int[n + 1]; for (int i = 0; i <= n; i++) o[i] = r.ReadInt32();
            var t = new int[e]; for (int i = 0; i < e; i++) t[i] = r.ReadInt32();
            var l = r.ReadBytes(e);
            // make the graph undirected (street racers ignore one-way signs) while keeping lane info
            var lists = new List<int>[n]; var lns = new List<byte>[n];
            for (int i = 0; i < n; i++) { lists[i] = new List<int>(4); lns[i] = new List<byte>(4); }
            for (int a = 0; a < n; a++)
                for (int k = o[a]; k < o[a + 1]; k++)
                {
                    int b = t[k];
                    if (!lists[a].Contains(b)) { lists[a].Add(b); lns[a].Add(l[k]); }
                    if (!lists[b].Contains(a)) { lists[b].Add(a); lns[b].Add((byte)((l[k] >> 4) | (l[k] << 4))); }
                }
            off = new int[n + 1]; int c = 0;
            for (int i = 0; i < n; i++) { off[i] = c; c += lists[i].Count; }
            off[n] = c; adj = new int[c]; lanes = new byte[c];
            for (int i = 0; i < n; i++) { lists[i].CopyTo(adj, off[i]); lns[i].CopyTo(lanes, off[i]); }
        }

        private static long Key(int cx, int cy) { return ((long)cx << 32) ^ (uint)cy; }
        private static void BuildGrid()
        {
            grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < xs.Length; i++)
            {
                if ((fl[i] & (byte)NodeFlags.Water) != 0) continue;
                long k = Key((int)Math.Floor(xs[i] / Cell), (int)Math.Floor(ys[i] / Cell));
                List<int> l; if (!grid.TryGetValue(k, out l)) grid[k] = l = new List<int>();
                l.Add(i);
            }
        }

        public static Vector3 Pos(int i) { return new Vector3(xs[i], ys[i], zs[i]); }
        public static NodeFlags Flags(int i) { return (NodeFlags)fl[i]; }

        /// <summary>Nearest drivable node (prefers GPS roads; ignores nodes far above/below, e.g. under bridges).</summary>
        public static int Nearest(Vector3 p, float maxDist = 300f)
        {
            if (!EnsureLoaded()) return -1;
            int cx = (int)Math.Floor(p.X / Cell), cy = (int)Math.Floor(p.Y / Cell), best = -1; float bd = float.MaxValue;
            int rad = Math.Max(1, (int)Math.Ceiling(maxDist / Cell));
            for (int r = 0; r <= rad; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        List<int> l; if (!grid.TryGetValue(Key(cx + dx, cy + dy), out l)) continue;
                        foreach (int i in l)
                        {
                            float ddx = xs[i] - p.X, ddy = ys[i] - p.Y, ddz = (zs[i] - p.Z) * 3f;
                            float d = ddx * ddx + ddy * ddy + ddz * ddz;
                            if ((fl[i] & (byte)NodeFlags.Gps) == 0) d *= 1.6f;
                            if (d < bd) { bd = d; best = i; }
                        }
                    }
                if (best >= 0 && Math.Sqrt(bd) < (r - 0.5f) * Cell) break;
            }
            return best;
        }

        [ThreadStatic] private static bool rallyMode;
        private static float Cost(int a, int b, int k)
        {
            float dx = xs[a] - xs[b], dy = ys[a] - ys[b], dz = zs[a] - zs[b];
            float d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            byte f = fl[b];
            if ((f & (byte)NodeFlags.Water) != 0) return d * 50f;
            float m = 1f;
            if ((f & (byte)NodeFlags.Gps) == 0) m *= 1.35f;
            if (rallyMode) { if ((f & (byte)NodeFlags.Gravel) != 0) m *= 0.45f; else if ((f & (byte)NodeFlags.Backroad) != 0) m *= 0.8f; if ((f & (byte)NodeFlags.Freeway) != 0) m *= 4f; }
            else if ((f & (byte)NodeFlags.Gravel) != 0) m *= 1.8f;
            if ((f & (byte)NodeFlags.Freeway) != 0) m *= 0.92f;
            int lanesFwd = lanes[k] & 15, lanesBack = lanes[k] >> 4;
            if (lanesFwd + lanesBack <= 1) m *= 1.15f;            // narrow lanes are slower
            return d * m;
        }

        /// <summary>Route that prefers dirt and back roads (rally stages).</summary>
        public static List<Vector3> RouteRally(Vector3 from, Vector3 to) { rallyMode = true; try { return Route(from, to); } finally { rallyMode = false; } }
        public static int NearestIndex(Vector3 p) { return Nearest(p, 30f); }

        /// <summary>A* route between two world positions. Returns node positions along the road, or null.</summary>
        public static List<Vector3> Route(Vector3 from, Vector3 to)
        {
            if (!EnsureLoaded()) return null;
            int s = Nearest(from), g = Nearest(to);
            if (s < 0 || g < 0) return null;
            if (s == g) return new List<Vector3> { from, to };
            int n = xs.Length;
            var gScore = new Dictionary<int, float> { { s, 0f } };
            var came = new Dictionary<int, int>();
            var open = new MinHeap();
            open.Push(s, H(s, g));
            var closed = new HashSet<int>();
            int expanded = 0;
            while (open.Count > 0)
            {
                int cur = open.Pop();
                if (cur == g) break;
                if (!closed.Add(cur)) continue;
                if (++expanded > 400000) return null;
                float gc = gScore[cur];
                for (int k = off[cur]; k < off[cur + 1]; k++)
                {
                    int nb = adj[k]; if (closed.Contains(nb)) continue;
                    float t = gc + Cost(cur, nb, k), old;
                    if (gScore.TryGetValue(nb, out old) && old <= t) continue;
                    gScore[nb] = t; came[nb] = cur;
                    open.Push(nb, t + H(nb, g));
                }
            }
            if (!came.ContainsKey(g)) return null;
            var path = new List<Vector3>();
            for (int c = g; ; c = came[c]) { path.Add(Pos(c)); if (c == s) break; }
            path.Reverse();
            return path;
        }

        private static float H(int a, int b) { float dx = xs[a] - xs[b], dy = ys[a] - ys[b]; return (float)Math.Sqrt(dx * dx + dy * dy) * 0.9f; }

        /// <summary>A random drivable GPS road node within [minR, maxR] of a point (for street challenges / procedural events).</summary>
        public static int RandomNodeAround(Vector3 c, float minR, float maxR, Random rnd)
        {
            if (!EnsureLoaded()) return -1;
            for (int tries = 0; tries < 60; tries++)
            {
                double ang = rnd.NextDouble() * Math.PI * 2, r = minR + rnd.NextDouble() * (maxR - minR);
                int i = Nearest(new Vector3(c.X + (float)(Math.Cos(ang) * r), c.Y + (float)(Math.Sin(ang) * r), c.Z), 150f);
                if (i >= 0 && (fl[i] & (byte)NodeFlags.Gps) != 0 && (fl[i] & (byte)(NodeFlags.Water | NodeFlags.Gravel)) == 0) return i;
            }
            return -1;
        }

        /// <summary>Breadth-first along the roads from p to the closest GPS road node that is at least 'clear' metres from
        /// every location in both lists. Returns Vector3.Zero if none within ~1.5 km. heading = road direction there.</summary>
        public static Vector3 ClearSpot(Vector3 p, List<Vector3> a, List<Vector3> b, float clear, out float heading)
        {
            heading = 0f;
            if (!EnsureLoaded()) return Vector3.Zero;
            int s = Nearest(p); if (s < 0) return Vector3.Zero;
            var q = new Queue<int>(); var seen = new HashSet<int> { s }; q.Enqueue(s);
            while (q.Count > 0 && seen.Count < 4000)
            {
                int c = q.Dequeue(); var cp = Pos(c);
                bool ok = (fl[c] & (byte)NodeFlags.Gps) != 0 && (fl[c] & (byte)(NodeFlags.Water | NodeFlags.Junction)) == 0
                    && !a.Exists(x => x.DistanceTo(cp) < clear) && !b.Exists(x => x.DistanceTo(cp) < clear * 0.5f);
                if (ok)
                {
                    int nb = off[c + 1] > off[c] ? adj[off[c]] : -1;
                    if (nb >= 0) { var d = Pos(nb) - cp; heading = (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI); }
                    return cp;
                }
                for (int k = off[c]; k < off[c + 1]; k++) if (seen.Add(adj[k])) q.Enqueue(adj[k]);
            }
            return Vector3.Zero;
        }

        private sealed class MinHeap
        {
            private readonly List<int> ids = new List<int>(); private readonly List<float> pr = new List<float>();
            public int Count { get { return ids.Count; } }
            public void Push(int id, float p)
            {
                ids.Add(id); pr.Add(p); int i = ids.Count - 1;
                while (i > 0) { int pa = (i - 1) / 2; if (pr[pa] <= pr[i]) break; Swap(i, pa); i = pa; }
            }
            public int Pop()
            {
                int top = ids[0], last = ids.Count - 1;
                ids[0] = ids[last]; pr[0] = pr[last]; ids.RemoveAt(last); pr.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < ids.Count && pr[l] < pr[m]) m = l;
                    if (r < ids.Count && pr[r] < pr[m]) m = r;
                    if (m == i) break; Swap(i, m); i = m;
                }
                return top;
            }
            private void Swap(int a, int b) { int t = ids[a]; ids[a] = ids[b]; ids[b] = t; float q = pr[a]; pr[a] = pr[b]; pr[b] = q; }
        }
    }
}
