using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;

namespace MidnightClub.UI
{
    /// <summary>Smooth vector icons (flag, car, tag, trophy, chat, pin, card, gear, power, gauge, bike, stopwatch).
    /// Shapes (circles, rounded rects, polygons, thick lines, arcs; added or cut out in order) are rasterised once per
    /// pixel size into horizontal spans at the real screen resolution, merged vertically, cached, and drawn as a
    /// handful of pixel-exact rectangles. Crisp at any size, no textures needed.</summary>
    public static class VectorIcons
    {
        private sealed class S
        {
            public char K; public float[] V; public bool Sub;
            public static S C(float cx, float cy, float r, bool sub) { return new S { K = 'C', V = new[] { cx, cy, r }, Sub = sub }; }
            public static S R(float x, float y, float w, float h, float rad, bool sub) { return new S { K = 'R', V = new[] { x, y, w, h, rad }, Sub = sub }; }
            public static S P(float[] pts, bool sub) { return new S { K = 'P', V = pts, Sub = sub }; }
            public static S L(float x1, float y1, float x2, float y2, float t, bool sub) { return new S { K = 'L', V = new[] { x1, y1, x2, y2, t }, Sub = sub }; }
            public static S A(float cx, float cy, float r, float t, float a0, float a1, bool sub) { return new S { K = 'A', V = new[] { cx, cy, r, t, a0, a1 }, Sub = sub }; }
        }

        private static readonly Dictionary<string, S[]> set = new Dictionary<string, S[]>
        {
            { "races", new[] { S.R(14.000f,8.000f,7.000f,86.000f,3.000f,false), S.P(new[]{21.000f,10.000f,88.000f,10.000f,80.000f,32.000f,88.000f,54.000f,21.000f,54.000f},false), S.R(37.750f,10.000f,16.750f,14.670f,0.000f,true), S.R(71.250f,10.000f,16.750f,14.670f,0.000f,true), S.R(21.000f,24.670f,16.750f,14.670f,0.000f,true), S.R(54.500f,24.670f,16.750f,14.670f,0.000f,true), S.R(37.750f,39.340f,16.750f,14.670f,0.000f,true), S.R(71.250f,39.340f,16.750f,14.670f,0.000f,true) } },
            { "garage", new[] { S.R(4.000f,40.000f,92.000f,26.000f,10.000f,false), S.P(new[]{20.000f,42.000f,33.000f,22.000f,67.000f,22.000f,84.000f,42.000f},false), S.P(new[]{31.000f,40.000f,38.000f,28.000f,51.000f,28.000f,51.000f,40.000f},true), S.P(new[]{55.000f,40.000f,55.000f,28.000f,64.000f,28.000f,74.000f,40.000f},true), S.C(27.000f,68.000f,16.000f,true), S.C(73.000f,68.000f,16.000f,true), S.C(27.000f,68.000f,12.000f,false), S.C(73.000f,68.000f,12.000f,false), S.C(27.000f,68.000f,5.000f,true), S.C(73.000f,68.000f,5.000f,true) } },
            { "dealers", new[] { S.P(new[]{12.000f,46.000f,46.000f,12.000f,88.000f,12.000f,88.000f,54.000f,54.000f,88.000f},false), S.C(72.000f,28.000f,7.000f,true) } },
            { "career", new[] { S.R(26.000f,10.000f,48.000f,10.000f,3.000f,false), S.P(new[]{26.000f,18.000f,74.000f,18.000f,70.000f,46.000f,30.000f,46.000f},false), S.C(50.000f,44.000f,20.000f,false), S.A(24.000f,30.000f,13.000f,6.000f,90.000f,270.000f,false), S.A(76.000f,30.000f,13.000f,6.000f,-90.000f,90.000f,false), S.R(44.000f,60.000f,12.000f,14.000f,2.000f,false), S.R(30.000f,74.000f,40.000f,8.000f,3.000f,false), S.R(24.000f,84.000f,52.000f,8.000f,3.000f,false) } },
            { "social", new[] { S.R(6.000f,10.000f,62.000f,44.000f,12.000f,false), S.P(new[]{16.000f,50.000f,16.000f,66.000f,32.000f,52.000f},false), S.R(30.000f,32.000f,70.000f,52.000f,16.000f,true), S.R(36.000f,38.000f,58.000f,40.000f,12.000f,false), S.P(new[]{82.000f,74.000f,84.000f,90.000f,68.000f,76.000f},false) } },
            { "explore", new[] { S.C(50.000f,38.000f,28.000f,false), S.P(new[]{26.000f,52.000f,74.000f,52.000f,50.000f,92.000f},false), S.C(50.000f,38.000f,11.000f,true) } },
            { "bank", new[] { S.R(8.000f,22.000f,84.000f,58.000f,10.000f,false), S.R(8.000f,34.000f,84.000f,10.000f,0.000f,true), S.R(18.000f,56.000f,22.000f,12.000f,3.000f,true), S.R(48.000f,62.000f,30.000f,5.000f,2.000f,true) } },
            { "settings", new[] { S.P(new[]{93.744f,45.258f,93.744f,54.742f,82.631f,59.550f,79.827f,66.321f,84.285f,77.578f,77.578f,84.285f,66.321f,79.827f,59.550f,82.631f,54.742f,93.744f,45.258f,93.744f,40.450f,82.631f,33.679f,79.827f,22.422f,84.285f,15.715f,77.578f,20.173f,66.321f,17.369f,59.550f,6.256f,54.742f,6.256f,45.258f,17.369f,40.450f,20.173f,33.679f,15.715f,22.422f,22.422f,15.715f,33.679f,20.173f,40.450f,17.369f,45.258f,6.256f,54.742f,6.256f,59.550f,17.369f,66.321f,20.173f,77.578f,15.715f,84.285f,22.422f,79.827f,33.679f,82.631f,40.450f},false), S.C(50.000f,50.000f,34.000f,false), S.C(50.000f,50.000f,14.000f,true) } },
            { "exit", new[] { S.A(50.000f,54.000f,34.000f,10.000f,125.000f,55.000f,false), S.R(45.000f,6.000f,10.000f,44.000f,5.000f,false) } },
            { "progress", new[] { S.A(50.000f,66.000f,40.000f,10.000f,0.000f,180.000f,false), S.L(50.000f,66.000f,72.000f,38.000f,8.000f,false), S.C(50.000f,66.000f,9.000f,false), S.R(8.000f,76.000f,84.000f,6.000f,3.000f,false) } },
            { "bike", new[] { S.A(22.000f,66.000f,15.000f,7.000f,0.000f,360.000f,false), S.A(78.000f,66.000f,15.000f,7.000f,0.000f,360.000f,false), S.P(new[]{22.000f,66.000f,40.000f,40.000f,62.000f,40.000f,78.000f,66.000f,70.000f,66.000f,58.000f,48.000f,44.000f,48.000f,30.000f,66.000f},false), S.P(new[]{36.000f,38.000f,56.000f,30.000f,70.000f,34.000f,64.000f,42.000f,40.000f,44.000f},false), S.R(60.000f,24.000f,14.000f,6.000f,3.000f,false) } },
            { "daily", new[] { S.A(50.000f,56.000f,36.000f,9.000f,0.000f,360.000f,false), S.R(42.000f,6.000f,16.000f,10.000f,3.000f,false), S.L(50.000f,56.000f,50.000f,34.000f,7.000f,false), S.L(50.000f,56.000f,64.000f,64.000f,7.000f,false) } },
        };

        public static bool Has(string id) { return id != null && set.ContainsKey(id); }

        private struct Box { public int X, Y, W, H; }
        private static readonly Dictionary<string, List<Box>> cache = new Dictionary<string, List<Box>>();

        /// <summary>Draws the icon centred at (cx, cy) (screen fractions), size = icon height as a screen-height fraction.</summary>
        public static void Draw(string id, float cx, float cy, float size, Color c)
        {
            if (id != null && Textures.Draw("icon_" + id, cx, cy, size, c)) return;
            S[] shapes; if (id == null || !set.TryGetValue(id, out shapes) || c.A == 0) return;
            int resW, resH; Ds.Resolution(out resW, out resH);
            int px = Math.Max(8, (int)Math.Round(size * resH));
            string key = id + ":" + px;
            List<Box> boxes; if (!cache.TryGetValue(key, out boxes)) cache[key] = boxes = Raster(shapes, px);
            int ox = (int)Math.Round(cx * resW - px / 2f), oy = (int)Math.Round(cy * resH - px / 2f);
            foreach (var b in boxes)
                Function.Call(Hash.DRAW_RECT, (ox + b.X + b.W / 2f) / resW, (oy + b.Y + b.H / 2f) / resH, (float)b.W / resW, (float)b.H / resH, (int)c.R, (int)c.G, (int)c.B, (int)c.A, false);
        }

        /// <summary>Progress ring (0..1, clockwise from the top), e.g. the level ring.</summary>
        public static void Ring(float cx, float cy, float size, float thickness, float progress, Color c)
        {
            progress = Math.Max(0f, Math.Min(1f, progress)); if (progress <= 0.005f) return;
            if (Textures.Draw("ring_" + ((int)Math.Round(progress * 50) * 2).ToString("000"), cx, cy, size, c)) return;
            int pct = (int)Math.Round(progress * 100);
            string key = "ring:" + pct + ":" + (int)(thickness * 1000);
            S[] shapes;
            if (!set.TryGetValue(key, out shapes))
            {
                float t = thickness / size * 100f, r = 50f - t / 2f - 0.5f;
                shapes = pct >= 100 ? new[] { S.A(50, 50, r, t, 0, 360, false) } : new[] { S.A(50, 50, r, t, 90 - 360f * pct / 100f, 90, false) };
                set[key] = shapes;
            }
            Draw(key, cx, cy, size, c);
        }

        private static List<Box> Raster(S[] shapes, int px)
        {
            var rows = new List<List<int>>();   // per row: x0,x1 pairs
            for (int i = 0; i < px; i++)
            {
                float y = (i + 0.5f) * 100f / px;
                var spans = Row(shapes, y);
                var r = new List<int>();
                foreach (var s in spans) { int a = (int)Math.Round(s[0] * px / 100f), b = (int)Math.Round(s[1] * px / 100f); if (b > a) { r.Add(a); r.Add(b); } }
                rows.Add(r);
            }
            // merge identical consecutive rows into tall boxes
            var boxes = new List<Box>();
            int start = 0;
            for (int i = 1; i <= px; i++)
            {
                if (i < px && Same(rows[i], rows[start])) continue;
                var r = rows[start];
                for (int k = 0; k + 1 < r.Count; k += 2) boxes.Add(new Box { X = r[k], Y = start, W = r[k + 1] - r[k], H = i - start });
                start = i;
            }
            return boxes;
        }

        private static bool Same(List<int> a, List<int> b) { if (a.Count != b.Count) return false; for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false; return true; }

        private static List<float[]> Row(S[] shapes, float y)
        {
            var cur = new List<float[]>();
            foreach (var s in shapes)
            {
                var iv = Intervals(s, y);
                if (s.Sub)
                {
                    foreach (var sb in Union(iv))
                    {
                        var nxt = new List<float[]>();
                        foreach (var c in cur)
                        {
                            if (sb[1] <= c[0] || sb[0] >= c[1]) { nxt.Add(c); continue; }
                            if (sb[0] > c[0]) nxt.Add(new[] { c[0], sb[0] });
                            if (sb[1] < c[1]) nxt.Add(new[] { sb[1], c[1] });
                        }
                        cur = nxt;
                    }
                }
                else { cur.AddRange(iv); cur = Union(cur); }
            }
            return cur;
        }

        private static List<float[]> Union(List<float[]> iv)
        {
            iv.Sort((a, b) => a[0].CompareTo(b[0]));
            var o = new List<float[]>();
            foreach (var x in iv)
            {
                if (o.Count > 0 && x[0] <= o[o.Count - 1][1]) o[o.Count - 1][1] = Math.Max(o[o.Count - 1][1], x[1]);
                else o.Add(new[] { x[0], x[1] });
            }
            return o;
        }

        private static List<float[]> Intervals(S s, float y)
        {
            var r = new List<float[]>(); var v = s.V;
            switch (s.K)
            {
                case 'C':
                {
                    float dy = y - v[1]; if (Math.Abs(dy) >= v[2]) break;
                    float dx = (float)Math.Sqrt(v[2] * v[2] - dy * dy); r.Add(new[] { v[0] - dx, v[0] + dx }); break;
                }
                case 'R':
                {
                    float x = v[0], yy = v[1], w = v[2], h = v[3], rad = Math.Min(v[4], Math.Min(w / 2, h / 2));
                    if (y < yy || y > yy + h) break;
                    float inset = 0;
                    if (y < yy + rad) { float d = yy + rad - y; inset = rad - (float)Math.Sqrt(Math.Max(0, rad * rad - d * d)); }
                    else if (y > yy + h - rad) { float d = y - (yy + h - rad); inset = rad - (float)Math.Sqrt(Math.Max(0, rad * rad - d * d)); }
                    r.Add(new[] { x + inset, x + w - inset }); break;
                }
                case 'P':
                {
                    var xs = new List<float>(); int n = v.Length / 2;
                    for (int i = 0; i < n; i++)
                    {
                        float x1 = v[i * 2], y1 = v[i * 2 + 1], x2 = v[(i + 1) % n * 2], y2 = v[(i + 1) % n * 2 + 1];
                        if ((y1 <= y && y < y2) || (y2 <= y && y < y1)) xs.Add(x1 + (y - y1) * (x2 - x1) / (y2 - y1));
                    }
                    xs.Sort();
                    for (int i = 0; i + 1 < xs.Count; i += 2) r.Add(new[] { xs[i], xs[i + 1] });
                    break;
                }
                case 'L': Sampled(r, x => { float dx = v[2] - v[0], dy = v[3] - v[1], l = dx * dx + dy * dy; float t = l > 0 ? Math.Max(0, Math.Min(1, ((x - v[0]) * dx + (y - v[1]) * dy) / l)) : 0; float px = v[0] + t * dx, py = v[1] + t * dy; return (x - px) * (x - px) + (y - py) * (y - py) <= v[4] * v[4] / 4; }); break;
                case 'A':
                {
                    float cx = v[0], cy = v[1], ro = v[2] + v[3] / 2, ri = v[2] - v[3] / 2;
                    Sampled(r, x =>
                    {
                        float dx = x - cx, dy = y - cy, d2 = dx * dx + dy * dy;
                        if (d2 > ro * ro || d2 < ri * ri) return false;
                        float ang = (float)(Math.Atan2(-dy, dx) * 180.0 / Math.PI); ang = (ang % 360 + 360) % 360;
                        float a0 = (v[4] % 360 + 360) % 360, a1 = (v[5] % 360 + 360) % 360;
                        if (Math.Abs(v[5] - v[4]) >= 360) return true;
                        return a0 < a1 ? ang >= a0 && ang <= a1 : ang >= a0 || ang <= a1;
                    });
                    break;
                }
            }
            return r;
        }

        private static void Sampled(List<float[]> r, Func<float, bool> inside)
        {
            float start = -1;
            for (int j = 0; j <= 400; j++)
            {
                float x = j * 0.25f; bool ok = inside(x);
                if (ok && start < 0) start = x;
                if ((!ok || j == 400) && start >= 0) { r.Add(new[] { start, x }); start = -1; }
            }
        }
    }
}
