using System;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;

namespace MidnightClub.UI
{
    /// <summary>Photo-studio look for car screens (garage, showroom, workshop): key/rim/fill lights drawn every frame,
    /// shallow depth of field and camera framing that leaves room for the UI.</summary>
    public static class Studio
    {
        public static float Brightness = 1f;

        /// <summary>Call every frame. Lights the car like a showroom: soft overhead key, two rim lights, front/rear fill.</summary>
        public static void Light(Vehicle v, Vector3 camPos)
        {
            if (v == null || !v.Exists()) return;
            var p = v.Position; float b = Brightness;
            float len = Math.Max(2.2f, v.Model.Dimensions.frontTopRight.Y);
            // overhead key (big soft spot straight down)
            Function.Call(Hash.DRAW_SPOT_LIGHT, p.X, p.Y, p.Z + 4.5f, 0f, 0f, -1f, 255, 244, 230, 9f, 6f * b, 0f, 75f, 1f);
            // camera-side key, so the visible flank is never black
            var toCam = camPos - p; toCam.Z = 0; toCam.Normalize();
            var kp = p + toCam * 3.2f + new Vector3(0, 0, 2.2f); var kd = p - kp; kd.Normalize();
            Function.Call(Hash.DRAW_SPOT_LIGHT, kp.X, kp.Y, kp.Z, kd.X, kd.Y, kd.Z, 235, 240, 255, 8f, 4f * b, 0f, 55f, 1f);
            // rim lights left/right of the car (purple / cyan accents, like a club studio)
            var right = Vector3.Cross(toCam, Vector3.WorldUp); right.Normalize();
            Point(p - right * 3.0f - toCam * 1.5f + new Vector3(0, 0, 1.2f), Color.FromArgb(170, 110, 255), 6f, 2.2f * b);
            Point(p + right * 3.0f - toCam * 1.5f + new Vector3(0, 0, 1.2f), Color.FromArgb(60, 210, 255), 6f, 2.2f * b);
            // low warm fills front and back
            Point(v.GetOffsetPosition(new Vector3(0, len + 1.6f, 0.4f)), Color.FromArgb(255, 235, 215), 5f, 1.4f * b);
            Point(v.GetOffsetPosition(new Vector3(0, -len - 1.6f, 0.4f)), Color.FromArgb(255, 235, 215), 5f, 1.4f * b);
        }

        private static void Point(Vector3 p, Color c, float range, float intensity)
        { Function.Call(Hash.DRAW_LIGHT_WITH_RANGE, p.X, p.Y, p.Z, (int)c.R, (int)c.G, (int)c.B, range, intensity); }

        /// <summary>Points the camera so the target sits at (sx, sy) of the screen (0 = centre, +x right, +y up, in fractions of half the view).</summary>
        public static void Frame(Camera cam, Vector3 camPos, Vector3 target, float sx, float sy)
        {
            cam.Position = camPos;
            var fwd = target - camPos; float dist = fwd.Length(); fwd.Normalize();
            var right = Vector3.Cross(fwd, Vector3.WorldUp); right.Normalize();
            var up = Vector3.Cross(right, fwd);
            float halfV = (float)Math.Tan(cam.FieldOfView * 0.5f * Math.PI / 180.0) * dist;
            float halfH = halfV * 16f / 9f;
            cam.PointAt(target - right * (sx * halfH) - up * (sy * halfV));
            Dof(cam, dist);
        }

        /// <summary>Shallow depth of field focused on the car; the room behind softens.</summary>
        public static void Dof(Camera cam, float focus)
        {
            Function.Call(Hash.SET_CAM_USE_SHALLOW_DOF_MODE, cam.Handle, true);
            Function.Call(Hash.SET_CAM_NEAR_DOF, cam.Handle, Math.Max(0.5f, focus - 3.5f));
            Function.Call(Hash.SET_CAM_FAR_DOF, cam.Handle, focus + 4.5f);
            Function.Call(Hash.SET_CAM_DOF_STRENGTH, cam.Handle, 0.7f);
            Function.Call(Hash.SET_USE_HI_DOF);
        }

        /// <summary>Vertical darkening strip (bottom or top of the screen), drawn as stacked rects for a smooth fade.</summary>
        public static void Fade(bool bottom, float height, int maxAlpha)
        {
            const int steps = 10;
            var bg = Ds.Bg;
            for (int i = 0; i < steps; i++)
            {
                float t = (i + 0.5f) / steps;
                int a = (int)(maxAlpha * 0.8f * Math.Pow(1f - t, 1.6));
                float h = height / steps, y = bottom ? 1f - (i + 1) * h : i * h;
                Ds.Rect(0f, y, 1f, h, Color.FromArgb(a, bg.R, bg.G, bg.B));
            }
        }

        /// <summary>Compact stat: label, value, rounded bar.</summary>
        public static void MiniStat(string label, float v, float x, float y, float w)
        {
            v = v < 0.03f ? 0.03f : v > 1f ? 1f : v;
            Ds.TM(label, x, y + Th.Px(10f), 0.24f, Th.Sub, Ds.Display);
            Ds.TM((Math.Round(v * 100f) / 10f).ToString("0.0"), x + w, y + Th.Px(10f), 0.3f, Th.Text, Ds.Display, GTA.UI.Alignment.Right);
            Kit.Bar(x, y + Th.Px(26f), w, Th.Px(6f), v, Th.Accent);
        }

        /// <summary>Bottom car card (garage / showroom): chip + counter, big car name, subtitle, four stats, details right.</summary>
        public static void CarStrip(string kicker, string name, string sub, float[] stats, string[] rightLines, string counter, Vehicles.Drivetrain.Live real = null)
        {
            bool showReal = SpecPanel.Real && real != null;
            float x = 0.045f, w = 0.91f, h = Th.Px(showReal ? 262f : 190f), y = 1f - Th.Px(100f) - h;
            if (showReal) SpecPanel.Curve(real, 0.62f, y - Th.Px(16f) - Th.Px(200f), 0.335f, Th.Px(200f));
            Kit.Glass(x, y, w, h, Th.R_Lg + 4f);
            float ix = x + Ds.Px(Th.Px(28f));
            float kw = Kit.Chip(kicker, ix, y + Th.Px(30f), Th.Accent, Th.On(Th.Accent), 0.24f);
            if (counter != null) Ds.TM(counter, ix + kw + Ds.Px(Th.Px(12f)), y + Th.Px(30f), 0.28f, Th.Sub, Ds.Display);
            Ds.TM(Ds.Fit(name, 1.0f, w * 0.55f, Ds.Display), ix, y + Th.Px(78f), 1.0f, Th.Text, Ds.Display);
            Ds.TM(sub, ix, y + Th.Px(116f), 0.28f, Th.Sub, Ds.Display);
            if (showReal) SpecPanel.Grid(real, ix, y + Th.Px(136f), w * 0.62f, 4, 8);
            else if (stats != null)
            {
                string[] labels = { "SPEED", "ACCELERATION", "BRAKING", "HANDLING" };
                float sw = Ds.Px(Th.Px(170f)), gap = Ds.Px(Th.Px(22f));
                for (int i = 0; i < 4 && i < stats.Length; i++) MiniStat(labels[i], stats[i], ix + i * (sw + gap), y + Th.Px(136f), sw);
            }
            if (rightLines != null)
            {
                float rx = x + w - Ds.Px(Th.Px(28f)), ry = y + Th.Px(42f);
                for (int i = 0; i < rightLines.Length; i++)
                {
                    if (string.IsNullOrEmpty(rightLines[i])) { ry += Th.Px(10f); continue; }
                    bool big = i == 0;
                    Ds.TM(rightLines[i], rx, ry, big ? 0.62f : 0.26f, big ? Th.Text : Th.Sub, big ? Ds.Display : Ds.Body, GTA.UI.Alignment.Right);
                    ry += big ? Th.Px(44f) : Th.Px(26f);
                }
            }
        }

        /// <summary>Screen title (top left) + optional value capsule (top right).</summary>
        public static void Title(string kicker, string title, string right)
        {
            Fade(false, 0.14f, 120);
            float x = 0.045f, y = 0.045f, h = Th.Px(68f);
            float tw = Math.Max(Ds.Width(title, 0.62f, Ds.Display), Ds.Width(kicker, 0.24f, Ds.Display)) + Ds.Px(Th.Px(48f));
            Kit.Glass(x, y, tw, h, Th.R_Lg);
            Ds.TM(kicker, x + Ds.Px(Th.Px(22f)), y + Th.Px(22f), 0.24f, Th.Accent, Ds.Display);
            Ds.TM(title, x + Ds.Px(Th.Px(22f)), y + Th.Px(46f), 0.62f, Th.Text, Ds.Display);
            if (right != null)
            {
                float rw = Ds.Width(right, 0.5f, Ds.Display) + Ds.Px(Th.Px(44f)), rh2 = Th.Px(50f);
                Kit.Glass(0.955f - rw, y + Th.Px(9f), rw, rh2, rh2 * 1080f / 2f);
                Ds.TM(right, 0.955f - rw / 2f, y + Th.Px(9f) + rh2 / 2f, 0.5f, Th.Text, Ds.Display, GTA.UI.Alignment.Center);
            }
        }
    }
}
