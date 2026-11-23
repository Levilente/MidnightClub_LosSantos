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
            const int steps = 24;
            for (int i = 0; i < steps; i++)
            {
                float t = (i + 0.5f) / steps;                     // 0 at the edge of the screen
                int a = (int)(maxAlpha * Math.Pow(1f - t, 1.6));
                float h = height / steps, y = bottom ? 1f - (i + 1) * h : i * h;
                Ds.Rect(0f, y, 1f, h, Color.FromArgb(a, 4, 3, 10));
            }
        }

        /// <summary>Compact horizontal stat: label above, thin bar below.</summary>
        public static void MiniStat(string label, float v, float x, float y, float w)
        {
            v = v < 0.03f ? 0.03f : v > 1f ? 1f : v;
            Theme.Text(label, x, y, 0.24f, Theme.TextDim);
            Theme.Text(((int)Math.Round(v * 10f * 10f) / 10f).ToString("0.0"), x + w, y - 0.004f, 0.3f, Color.White, GTA.UI.Font.ChaletComprimeCologne, GTA.UI.Alignment.Right);
            Ds.Skew(x, y + 0.028f, w, 0.006f, Ds.Px(0.0016f), Color.FromArgb(70, 255, 255, 255));
            Ds.Skew(x, y + 0.028f, w * v, 0.006f, Ds.Px(0.0016f), Ds.Violet);
        }

        /// <summary>NFS-style bottom block: kicker, big car name, subtitle, four stats, details on the right.</summary>
        public static void CarStrip(string kicker, string name, string sub, float[] stats, string[] rightLines, string counter)
        {
            Fade(true, 0.34f, 235);
            Ds.Streaks(0f, 0.72f, 1f, 0.2f, Ds.Violet, 0.35f, 14, 0.4f);
            float x = 0.05f, y = 0.735f;
            float kw = Ds.Tag(kicker, x, y, Ds.Violet, Ds.Text, 0.26f);
            if (counter != null) Ds.T(counter, x + kw + Ds.Px(0.008f), y - 0.001f, 0.28f, Ds.Muted, Ds.Display);
            Theme.Text(name, x - 0.002f, y + 0.022f, 1.05f, Color.White, GTA.UI.Font.ChaletComprimeCologne);
            Ds.Stripes(x, y + 0.1f, 0.09f, Ds.Violet, Ds.Cyan);
            Theme.Text(sub, x, y + 0.114f, 0.3f, Color.FromArgb(230, 255, 255, 255));
            if (stats != null)
            {
                string[] labels = { "SPEED", "ACCELERATION", "BRAKING", "HANDLING" };
                float sx = x, sw = 0.105f;
                for (int i = 0; i < 4 && i < stats.Length; i++) MiniStat(labels[i], stats[i], sx + i * (sw + 0.018f), y + 0.155f, sw);
            }
            if (rightLines != null)
            {
                float ry = y + 0.03f;
                for (int i = 0; i < rightLines.Length; i++)
                {
                    if (string.IsNullOrEmpty(rightLines[i])) { ry += 0.012f; continue; }
                    bool big = i == 0;
                    Theme.Text(rightLines[i], 0.95f, ry, big ? 0.62f : 0.27f, big ? Color.White : Theme.TextDim, big ? GTA.UI.Font.ChaletComprimeCologne : GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Right);
                    ry += big ? 0.055f : 0.026f;
                }
            }
        }

        /// <summary>Small top-left title: kicker + title, over a soft top fade.</summary>
        public static void Title(string kicker, string title, string right)
        {
            Fade(false, 0.16f, 170);
            Ds.Tag(kicker, 0.05f, 0.036f, Ds.Violet, Ds.Text, 0.24f);
            Theme.Text(title, 0.05f, 0.058f, 0.62f, Color.White, GTA.UI.Font.ChaletComprimeCologne);
            if (right != null) Theme.Text(right, 0.95f, 0.05f, 0.34f, Color.White, GTA.UI.Font.ChaletComprimeCologne, GTA.UI.Alignment.Right);
        }
    }
}
