using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

namespace MidnightClub.Racing
{
    /// <summary>Starting grid laid on the road itself: rows follow the road backwards from the start line (so a grid on
    /// a bend bends with it), cars are lined up with the road's heading, two abreast in the lanes going our way (both
    /// lanes on a narrow two-way street), and every slot is checked to be on tarmac. No more cars across the lanes or in
    /// the bushes.</summary>
    public static class Grid
    {
        public struct Slot { public Vector3 Pos; public float Heading; }

        private static bool Node(Vector3 p, out Vector3 node, out float heading)
        {
            var o = new OutputArgument(); var h = new OutputArgument();
            bool ok = Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING, p.X, p.Y, p.Z, o, h, 1, 3f, 0);
            node = ok ? o.GetResult<Vector3>() : p; heading = ok ? h.GetResult<float>() : 0f;
            return ok && node.DistanceTo(p) < 40f;
        }

        private static Vector3 Fwd(float h) { double r = h * Math.PI / 180.0; return new Vector3((float)-Math.Sin(r), (float)Math.Cos(r), 0f); }
        private static float Delta(float a, float b) { return ((b - a) % 360f + 540f) % 360f - 180f; }

        private static bool OnRoad(Vector3 p) { return Function.Call<bool>(Hash.IS_POINT_ON_ROAD, p.X, p.Y, p.Z, 0); }

        private static Vector3 Ground(Vector3 p)
        {
            var z = new OutputArgument();
            if (Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD, p.X, p.Y, p.Z + 3f, z, false, false)) p.Z = z.GetResult<float>();
            return p;
        }

        /// <summary>Slots for count cars at the start (slot 0 = pole, the player's). toward: a point the race heads to.</summary>
        public static List<Slot> Build(Vector3 start, Vector3 toward, int count)
        {
            var slots = new List<Slot>();
            float want = (float)(Math.Atan2(-(toward.X - start.X), toward.Y - start.Y) * 180.0 / Math.PI);
            Vector3 node; float hd;
            if (!Node(start, out node, out hd))
            {
                // no road here (off-road events): a plain staggered grid along the direction of travel
                var f0 = Fwd(want); var r0 = new Vector3(f0.Y, -f0.X, 0);
                for (int i = 0; i < count; i++) { int row = i / 2; slots.Add(new Slot { Pos = Ground(start - f0 * (8f * row) + r0 * (i % 2 == 0 ? -2.6f : 2.6f)), Heading = want }); }
                return slots;
            }
            if (Math.Abs(Delta(hd, want)) > 90f) hd += 180f;
            // lanes on this road: one-way, narrow two-way (race on both lanes) or multi-lane (our side only)
            var p1 = new OutputArgument(); var p2 = new OutputArgument(); var ln = new OutputArgument(); var ls = new OutputArgument(); var w = new OutputArgument();
            int lanesA = 1, lanesB = 1;
            try
            {
                if (Function.Call<bool>(Hash.GET_CLOSEST_ROAD, node.X, node.Y, node.Z, 1f, 1, p1, p2, ln, ls, w, false))
                {
                    var a = p1.GetResult<Vector3>(); var b = p2.GetResult<Vector3>(); var dir = b - a; dir.Z = 0;
                    int n1 = ln.GetResult<int>(), n2 = ls.GetResult<int>();
                    bool same = dir.Length() < 0.1f || Vector3.Dot(Vector3.Normalize(dir), Fwd(hd)) >= 0f;
                    lanesA = Math.Max(0, same ? n1 : n2); lanesB = Math.Max(0, same ? n2 : n1);
                }
            }
            catch { }
            const float lane = 5.2f;
            float centre, half;     // lateral offset of our lane block from the node line, half-spacing of the two columns
            if (lanesB == 0 || lanesA == 0) { centre = 0f; half = Math.Max(lanesA, lanesB) >= 2 ? lane / 2f : 0f; }
            else if (lanesA + lanesB <= 2) { centre = 0f; half = 2.4f; }
            else { centre = lane * lanesA / 2f; half = lanesA >= 2 ? lane / 2f : 0f; }
            bool twoAbreast = half > 0.5f;
            // walk the road backwards from the start line, one row every 8 m, re-snapping to the road each step
            var cur = node; float curH = hd;
            for (int row = 0; slots.Count < count && row < 40; row++)
            {
                if (row > 0)
                {
                    var back = cur - Fwd(curH) * 8f; Vector3 n2; float h2;
                    if (Node(back, out n2, out h2) && n2.DistanceTo(back) < 12f) { if (Math.Abs(Delta(h2, curH)) > 90f) h2 += 180f; cur = n2; curH = h2; }
                    else cur = back;
                }
                var f = Fwd(curH); var right = new Vector3(f.Y, -f.X, 0);
                var mid = cur + right * centre;
                var cand = twoAbreast ? new[] { mid - right * half, mid + right * half } : new[] { mid };
                foreach (var c in cand)
                {
                    if (slots.Count >= count) break;
                    var pnt = Ground(c);
                    if (!OnRoad(pnt))
                    {
                        // slide towards the middle of our lanes until we're on tarmac
                        bool fixedUp = false;
                        for (float t = 0.25f; t <= 1.01f && !fixedUp; t += 0.25f) { var q = Ground(Vector3.Lerp(c, mid, t)); if (OnRoad(q)) { pnt = q; fixedUp = true; } }
                        if (!fixedUp) continue;
                    }
                    if (slots.Exists(sl => sl.Pos.DistanceTo(pnt) < 4.5f)) continue;
                    slots.Add(new Slot { Pos = pnt, Heading = curH });
                }
            }
            // never come back short: fill any missing slots single-file behind the last one
            while (slots.Count < count)
            {
                var last = slots.Count > 0 ? slots[slots.Count - 1] : new Slot { Pos = node, Heading = hd };
                slots.Add(new Slot { Pos = Ground(last.Pos - Fwd(last.Heading) * 8f), Heading = last.Heading });
            }
            return slots;
        }
    }
}
