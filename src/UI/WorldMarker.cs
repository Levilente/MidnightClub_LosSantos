using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;

namespace MidnightClub.UI
{
    /// <summary>Premium in-world location markers: pulsing ground ring sitting on the real ground, a soft light column,
    /// a floating rotating icon and a 3D-projected label (name + action) when you are close.</summary>
    public static class WorldMarker
    {
        private static readonly Dictionary<long, float> groundCache = new Dictionary<long, float>();

        public static float Ground(Vector3 p)
        {
            long k = ((long)Math.Round(p.X) << 32) ^ (long)Math.Round(p.Y) * 7919;
            float z; if (groundCache.TryGetValue(k, out z)) return z;
            var o = new OutputArgument();
            if (Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD, p.X, p.Y, p.Z + 2f, o, false, false)) { z = o.GetResult<float>(); groundCache[k] = z; return z; }
            return p.Z - 1f;
        }

        /// <summary>dist = player distance. inRange = the player is inside the interaction radius.</summary>
        public static void Draw(Vector3 pos, float radius, Color c, MarkerType icon, string label, string action, float dist, bool inRange)
        {
            if (dist > 90f) return;
            float gz = Ground(pos) + 0.05f;
            var g = new Vector3(pos.X, pos.Y, gz);
            float pulse = 0.5f + 0.5f * (float)Math.Sin(Game.GameTime / 1000f * Math.PI * 1.2f);
            float fade = dist > 60f ? (90f - dist) / 30f : 1f;
            int a = (int)((inRange ? 200 : 120 + 40 * pulse) * fade);
            float ring = radius * 2f * (inRange ? 1f : 0.96f + 0.04f * pulse);
            World.DrawMarker(MarkerType.HorizontalCircleFat, g, Vector3.Zero, Vector3.Zero, new Vector3(ring, ring, 1f), Color.FromArgb(a, c), false, false, false, null, null, false);
            World.DrawMarker(MarkerType.VerticalCylinder, g, Vector3.Zero, Vector3.Zero, new Vector3(ring * 0.98f, ring * 0.98f, 1.2f), Color.FromArgb((int)(45 * fade), c), false, false, false, null, null, false);
            float bob = 0.12f * (float)Math.Sin(Game.GameTime / 1000f * Math.PI * 0.8f);
            World.DrawMarker(icon, g + new Vector3(0, 0, 2.6f + bob), Vector3.Zero, Vector3.Zero, new Vector3(1.4f, 1.4f, 1.4f), Color.FromArgb((int)(230 * fade), c), false, false, true, null, null, false);
            if (dist > 40f || string.IsNullOrEmpty(label)) return;
            var sp = Screen.WorldToScreen(g + new Vector3(0, 0, 3.7f), false);
            if (sp.X <= 0 && sp.Y <= 0) return;
            float x = sp.X / 1280f, y = sp.Y / 720f, la = Ds.Clamp01((40f - dist) / 12f);
            Ds.T(label.ToUpperInvariant(), x, y - 0.03f, 0.42f, Ds.A(Color.White, la), Ds.Display, Alignment.Center);
            if (!string.IsNullOrEmpty(action)) Ds.T(action.ToUpperInvariant(), x, y + 0.004f, 0.25f, Ds.A(inRange ? c : Ds.Muted, la), Ds.Display, Alignment.Center);
        }

        /// <summary>Floating price tag over a world position (display cars): name + price on a dark pill, no marker.</summary>
        public static void Label(Vector3 pos, string title, string sub, float dist, bool focus)
        {
            if (dist > 25f) return;
            var sp = Screen.WorldToScreen(pos, false);
            if (sp.X <= 0 && sp.Y <= 0) return;
            float x = sp.X / 1280f, y = sp.Y / 720f, a = Ds.Clamp01((25f - dist) / 8f);
            string t = (title ?? "").ToUpperInvariant();
            float w = Math.Max(Ds.Width(t, 0.4f, Ds.Display), Ds.Width(sub ?? "", 0.34f, Ds.Display)) + Ds.Px(0.024f);
            Ds.Rect(x - w / 2f, y - 0.05f, w, 0.06f, Ds.A(Color.FromArgb(215, 12, 11, 17), a));
            Ds.Rect(x - w / 2f, y + 0.007f, w, 0.003f, Ds.A(focus ? Ds.Violet : Color.FromArgb(120, 255, 255, 255), a));
            Ds.TM(t, x, y - 0.034f, 0.4f, Ds.A(Color.White, a), Ds.Display, Alignment.Center);
            if (!string.IsNullOrEmpty(sub)) Ds.TM(sub, x, y - 0.009f, 0.34f, Ds.A(Ds.Violet, a), Ds.Display, Alignment.Center);
        }
    }
}
