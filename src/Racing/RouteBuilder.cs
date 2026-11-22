using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Core;

namespace MidnightClub.Racing
{
    /// <summary>Turns a race's control points into road-following checkpoints using the game's own GPS
    /// (waypoint route sampled every 20 m). The GPS always routes from the player, so each leg is built with
    /// the car briefly placed at the leg start (done behind a fade). Checkpoints are denser in corners.</summary>
    public static class RouteBuilder
    {
        private static readonly Dictionary<string, List<Vector3>> cache = new Dictionary<string, List<Vector3>>();
        private static int workingRouteType = -1;

        /// <summary>Returns the checkpoint list for one lap (for circuits the last checkpoint is the start), or null.</summary>
        public static List<Vector3> Build(RaceDefinition r, Vehicle car)
        {
            List<Vector3> cached;
            if (cache.TryGetValue(r.Id, out cached)) return cached;
            var pts = new List<Vector3>();
            foreach (var p in r.Points) pts.Add(new Vector3(p[0], p[1], p[2]));
            if (r.Kind == RaceType.Circuit) pts.Add(pts[0]);

            var origPos = car.Position; float origHeading = car.Heading;
            var all = new List<Vector3>();
            try
            {
                for (int leg = 0; leg + 1 < pts.Count; leg++)
                {
                    var samples = Leg(car, pts[leg], pts[leg + 1]);
                    if (samples == null) { Log.Warn("Route leg " + leg + " of " + r.Id + " not found"); return null; }
                    if (all.Count > 0 && samples.Count > 0) samples.RemoveAt(0);
                    all.AddRange(samples);
                }
            }
            finally
            {
                Function.Call(Hash.SET_WAYPOINT_OFF);
                car.Position = origPos; car.Heading = origHeading; car.PlaceOnGround();
            }
            var cps = Thin(all, pts[pts.Count - 1]);
            if (cps.Count < 2) return null;
            cache[r.Id] = cps;
            float len = 0; for (int i = 1; i < all.Count; i++) len += all[i].DistanceTo(all[i - 1]);
            Log.Info("Route " + r.Id + ": " + all.Count + " samples, " + cps.Count + " checkpoints, " + (len / 1000f).ToString("0.00") + " km (GPS type " + workingRouteType + ")");
            return cps;
        }

        public static float Length(List<Vector3> cps, Vector3 start)
        {
            float len = 0; var prev = start;
            foreach (var c in cps) { len += prev.DistanceTo(c); prev = c; }
            return len;
        }

        private static List<Vector3> Leg(Vehicle car, Vector3 from, Vector3 to)
        {
            car.Position = from; car.PlaceOnGround();
            Function.Call(Hash.NEW_LOAD_SCENE_START_SPHERE, from.X, from.Y, from.Z, 80f, 0);
            Script.Wait(250);
            Function.Call(Hash.NEW_LOAD_SCENE_STOP);
            Function.Call(Hash.SET_WAYPOINT_OFF);
            Script.Wait(0);
            Function.Call(Hash.SET_NEW_WAYPOINT, to.X, to.Y);
            int t0 = Game.GameTime;
            while (!Function.Call<bool>(Hash.GET_GPS_BLIP_ROUTE_FOUND, true) && Game.GameTime - t0 < 4000) Script.Wait(50);
            int length = Function.Call<int>(Hash.GET_GPS_BLIP_ROUTE_LENGTH);
            if (length <= 0) return null;

            int[] types = workingRouteType >= 0 ? new[] { workingRouteType } : new[] { 0, 1, 2, 3 };
            foreach (int type in types)
            {
                var list = new List<Vector3> { from };
                bool ok = true;
                for (float d = 20f; d < length; d += 20f)
                {
                    var outArg = new OutputArgument();
                    if (!Function.Call<bool>(Hash.GET_POS_ALONG_GPS_TYPE_ROUTE, outArg, true, d, type)) { ok = false; break; }
                    var v = outArg.GetResult<Vector3>();
                    if (v == Vector3.Zero || v.DistanceTo(list[list.Count - 1]) > 120f) { ok = false; break; }
                    list.Add(v);
                }
                if (!ok || list.Count < 2) continue;
                if (list[list.Count - 1].DistanceTo(to) > 150f) continue;
                list.Add(to);
                workingRouteType = type;
                return list;
            }
            return null;
        }

        /// <summary>Keep a checkpoint every ~260 m, or sooner where the road turns (> 35° since the last one).</summary>
        private static List<Vector3> Thin(List<Vector3> s, Vector3 finish)
        {
            var cps = new List<Vector3>();
            if (s.Count < 2) return cps;
            var last = s[0]; float since = 0f; Vector3 lastDir = Vector3.Normalize(s[1] - s[0]);
            for (int i = 1; i < s.Count - 1; i++)
            {
                since += s[i].DistanceTo(s[i - 1]);
                var dir = Vector3.Normalize(s[i + 1] - s[i]);
                float turn = (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, Vector3.Dot(dir, lastDir)))) * 180.0 / Math.PI);
                if (since >= 260f || (turn > 35f && since >= 70f))
                {
                    cps.Add(s[i]); last = s[i]; since = 0f; lastDir = dir;
                }
            }
            if (cps.Count > 0 && cps[cps.Count - 1].DistanceTo(finish) < 60f) cps.RemoveAt(cps.Count - 1);
            cps.Add(finish);
            return cps;
        }
    }
}
