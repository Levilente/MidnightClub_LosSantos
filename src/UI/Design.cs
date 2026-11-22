using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using GTA.UI;
using Font = GTA.UI.Font;

namespace MidnightClub.UI
{
    /// <summary>Design tokens + primitives for the modern Midnight Club UI: palette, typography helpers,
    /// rounded shapes, easing, the wordmark and a pixel-precise vector icon set (drawn from rects, no textures).</summary>
    public static class Ds
    {
        // palette (night city, violet + electric cyan, with app accents)
        public static readonly Color Bg = Color.FromArgb(255, 9, 8, 14);
        public static readonly Color Surface = Color.FromArgb(255, 19, 17, 28);
        public static readonly Color Surface2 = Color.FromArgb(255, 29, 26, 42);
        public static readonly Color Stroke = Color.FromArgb(255, 46, 42, 64);
        public static readonly Color Violet = Color.FromArgb(255, 168, 85, 247);
        public static readonly Color VioletDeep = Color.FromArgb(255, 91, 33, 182);
        public static readonly Color Cyan = Color.FromArgb(255, 34, 211, 238);
        public static readonly Color Pink = Color.FromArgb(255, 244, 114, 182);
        public static readonly Color Lime = Color.FromArgb(255, 163, 230, 53);
        public static readonly Color Amber = Color.FromArgb(255, 251, 191, 36);
        public static readonly Color Red = Color.FromArgb(255, 248, 72, 94);
        public static readonly Color Blue = Color.FromArgb(255, 96, 165, 250);
        public static readonly Color Text = Color.FromArgb(255, 244, 242, 250);
        public static readonly Color Muted = Color.FromArgb(255, 150, 145, 172);
        public static readonly Color Faint = Color.FromArgb(255, 92, 88, 112);

        public const Font Display = Font.ChaletComprimeCologne;
        public const Font Body = Font.ChaletLondon;

        private static float aspect = 1.7777f; private static int aspectFrame = -1;
        /// <summary>Screen width / height. Used so circles and icons stay square on any resolution.</summary>
        public static float Aspect
        {
            get
            {
                int f = Game.FrameCount;
                if (f != aspectFrame) { aspectFrame = f; float a = Function.Call<float>(Hash.GET_ASPECT_RATIO, false); if (a > 0.5f && a < 6f) aspect = a; }
                return aspect;
            }
        }
        /// <summary>Converts a height-relative length to the same on-screen length along x.</summary>
        public static float Px(float h) { return h / Aspect; }

        public static Color A(Color c, float a) { a = a < 0 ? 0 : a > 1 ? 1 : a; return Color.FromArgb((int)(c.A * a), c.R, c.G, c.B); }
        public static Color Mix(Color a, Color b, float t) { t = t < 0 ? 0 : t > 1 ? 1 : t; return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }

        // ---------------------------------------------------------------- motion
        public static float Dt { get { return Math.Min(0.1f, Game.LastFrameTime); } }
        /// <summary>Frame-rate independent exponential approach.</summary>
        public static float Damp(float cur, float target, float speed) { return cur + (target - cur) * (1f - (float)Math.Exp(-speed * Dt)); }
        public static float Step(float cur, float target, float perSecond) { float d = perSecond * Dt; return cur < target ? Math.Min(target, cur + d) : Math.Max(target, cur - d); }
        public static float EaseOut(float t) { t = Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }
        public static float EaseInOut(float t) { t = Clamp01(t); return t < 0.5f ? 4f * t * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 3) / 2f; }
        public static float EaseBack(float t) { t = Clamp01(t); const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * (float)Math.Pow(t - 1, 3) + c1 * (float)Math.Pow(t - 1, 2); }
        public static float Clamp01(float t) { return t < 0 ? 0 : t > 1 ? 1 : t; }
        public static float Pulse(float hz) { return 0.5f + 0.5f * (float)Math.Sin(Game.GameTime / 1000f * hz * Math.PI * 2); }

        // ---------------------------------------------------------------- shapes
        public static void Rect(float x, float y, float w, float h, Color c)
        { if (c.A > 0 && w > 0 && h > 0) Function.Call(Hash.DRAW_RECT, x + w / 2f, y + h / 2f, w, h, (int)c.R, (int)c.G, (int)c.B, (int)c.A, false); }

        /// <summary>Rounded rectangle (r = corner radius as a fraction of screen height). Pieces never overlap, so alpha stays even.</summary>
        public static void Round(float x, float y, float w, float h, float r, Color c)
        {
            float ry = Math.Min(r, h / 2f), rx = Math.Min(Px(r), w / 2f);
            Rect(x + rx, y, w - 2 * rx, h, c);
            Rect(x, y + ry, rx, h - 2 * ry, c);
            Rect(x + w - rx, y + ry, rx, h - 2 * ry, c);
            // quarter circles as three disjoint rows per corner
            float[] r0 = { 0f, 0.25f, 0.6f }, r1 = { 0.25f, 0.6f, 1f }, inset = { 0.45f, 0.15f, 0.02f };
            for (int i = 0; i < 3; i++)
            {
                float rh = (r1[i] - r0[i]) * ry, ins = inset[i] * rx, cw = rx - ins;
                float top = y + r0[i] * ry, bot = y + h - r1[i] * ry;
                Rect(x + ins, top, cw, rh, c); Rect(x + w - rx, top, cw, rh, c);
                Rect(x + ins, bot, cw, rh, c); Rect(x + w - rx, bot, cw, rh, c);
            }
        }

        /// <summary>Circle-ish dot of diameter d (height fraction), centred.</summary>
        public static void Dot(float cx, float cy, float d, Color c) { Round(cx - Px(d) / 2f, cy - d / 2f, Px(d), d, d / 2f, c); }

        /// <summary>Vertical gradient from top colour to bottom colour, drawn in strips.</summary>
        public static void VGrad(float x, float y, float w, float h, Color top, Color bottom, int steps = 16)
        {
            float sh = h / steps;
            for (int i = 0; i < steps; i++) Rect(x, y + i * sh, w, sh + 0.0004f, Mix(top, bottom, (i + 0.5f) / steps));
        }

        public static void HGrad(float x, float y, float w, float h, Color left, Color right, int steps = 16)
        {
            float sw = w / steps;
            for (int i = 0; i < steps; i++) Rect(x + i * sw, y, sw + 0.0004f, h, Mix(left, right, (i + 0.5f) / steps));
        }

        // ---------------------------------------------------------------- type
        public static void T(string s, float x, float y, float scale, Color c, Font f = Body, Alignment a = Alignment.Left, float wrap = 0f)
        { if (c.A > 2) new TextElement(s ?? "", new PointF(x * 1280f, y * 720f), scale, c, f, a, false, false, wrap * 1280f).ScaledDraw(); }

        public static float Width(string s, float scale, Font f = Body)
        {
            Function.Call(Hash.BEGIN_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, "STRING");
            Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, s ?? "");
            Function.Call(Hash.SET_TEXT_FONT, (int)f);
            Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
            return Function.Call<float>(Hash.END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, true);
        }

        private static readonly Dictionary<string, string> fitCache = new Dictionary<string, string>();
        /// <summary>Shortens text with an ellipsis so it fits maxW.</summary>
        public static string Fit(string s, float scale, float maxW, Font f = Body)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string key = s + "|" + scale + "|" + maxW + "|" + (int)f;
            string r; if (fitCache.TryGetValue(key, out r)) return r;
            r = s;
            if (Width(s, scale, f) > maxW)
            {
                int lo = 0, hi = s.Length;
                while (lo < hi) { int mid = (lo + hi + 1) / 2; if (Width(s.Substring(0, mid) + "...", scale, f) <= maxW) lo = mid; else hi = mid - 1; }
                r = s.Substring(0, Math.Max(0, lo)).TrimEnd() + "...";
            }
            if (fitCache.Count > 2000) fitCache.Clear();
            fitCache[key] = r; return r;
        }

        /// <summary>Wordmark: MIDNIGHT (white) CLUB (violet), condensed, with a thin rule and LOS SANTOS tag.</summary>
        public static void Logo(float x, float y, float size, float alpha = 1f, Alignment align = Alignment.Left, bool tag = true)
        {
            float s1 = size, w1 = Width("MIDNIGHT", s1, Display), w2 = Width("CLUB", s1, Display), gap = Px(0.006f * size);
            float total = w1 + gap + w2, x0 = align == Alignment.Right ? x - total : align == Alignment.Center ? x - total / 2 : x;
            T("MIDNIGHT", x0, y, s1, A(Text, alpha), Display);
            T("CLUB", x0 + w1 + gap, y, s1, A(Violet, alpha), Display);
            if (!tag) return;
            float lh = 0.072f * size;
            Rect(x0, y + lh, total, 0.0018f, A(Stroke, alpha));
            Rect(x0, y + lh, total * 0.32f, 0.0018f, A(Violet, alpha));
            T("LOS SANTOS", x0 + total, y + lh + 0.004f, 0.32f * size, A(Muted, alpha), Body, Alignment.Right);
        }

        /// <summary>Small uppercase label with a coloured dot.</summary>
        public static void Kicker(string s, float x, float y, Color accent, float alpha = 1f)
        {
            Dot(x + Px(0.004f), y + 0.0105f, 0.008f, A(accent, alpha));
            T(s, x + Px(0.013f), y, 0.25f, A(Muted, alpha), Body);
        }
    }

    /// <summary>Line-art icon set on a 12x12 grid, drawn with rectangles (crisp at any size, no textures needed).
    /// Each part: x, y, w, h on the grid; parts flagged 'cut' are drawn in the background colour.</summary>
    public static class Icons
    {
        private struct P { public float X, Y, W, H; public bool Cut; public P(float x, float y, float w, float h, bool cut = false) { X = x; Y = y; W = w; H = h; Cut = cut; } }
        private static readonly Dictionary<string, P[]> set = new Dictionary<string, P[]>
        {
            { "messages", new[] { new P(1, 2, 10, 7), new P(3, 9, 2.4f, 1.2f), new P(3, 10.2f, 1.2f, 1.2f),
                                   new P(3, 5, 1.5f, 1.5f, true), new P(5.25f, 5, 1.5f, 1.5f, true), new P(7.5f, 5, 1.5f, 1.5f, true) } },
            { "races", new[] { new P(1.5f, 1, 1.3f, 10.5f), new P(2.8f, 1, 8.2f, 6.5f), new P(4.85f, 1, 2.05f, 2.15f, true), new P(8.95f, 1, 2.05f, 2.15f, true),
                                new P(2.8f, 3.15f, 2.05f, 2.15f, true), new P(6.9f, 3.15f, 2.05f, 2.15f, true), new P(4.85f, 5.3f, 2.05f, 2.2f, true), new P(8.95f, 5.3f, 2.05f, 2.2f, true) } },
            { "garage", new[] { new P(5, 1, 2, 1), new P(3.5f, 2, 5, 1), new P(2, 3, 8, 1), new P(1, 4, 10, 1.2f), new P(1, 5.2f, 1.4f, 5.8f), new P(9.6f, 5.2f, 1.4f, 5.8f),
                                 new P(3.4f, 6.2f, 5.2f, 1.1f), new P(3.4f, 8.1f, 5.2f, 1.1f), new P(3.4f, 10, 5.2f, 1) } },
            { "dealers", new[] { new P(1, 3, 5.5f, 5.5f), new P(2.6f, 4.6f, 2.3f, 2.3f, true), new P(6.5f, 5.1f, 4.5f, 1.3f), new P(8.2f, 6.4f, 1.2f, 2), new P(10, 6.4f, 1, 1.4f) } },
            { "catalogue", new[] { new P(1, 1, 4.6f, 4.6f), new P(6.4f, 1, 4.6f, 4.6f), new P(1, 6.4f, 4.6f, 4.6f), new P(6.4f, 6.4f, 4.6f, 4.6f), new P(7.6f, 7.6f, 2.2f, 2.2f, true) } },
            { "career", new[] { new P(2.5f, 1, 7, 1.2f), new P(3, 2.2f, 6, 3.6f), new P(4, 5.8f, 4, 1.2f), new P(5.2f, 7, 1.6f, 2), new P(3.5f, 9, 5, 1), new P(3, 10, 6, 1.2f),
                                 new P(1, 2.2f, 1.2f, 2.6f), new P(1, 4.6f, 2, 1), new P(9.8f, 2.2f, 1.2f, 2.6f), new P(9, 4.6f, 2, 1) } },
            { "rivals", new[] { new P(1, 1, 1.6f, 1.6f), new P(2.3f, 2.3f, 1.6f, 1.6f), new P(3.6f, 3.6f, 1.6f, 1.6f), new P(6.8f, 6.8f, 1.6f, 1.6f), new P(8.1f, 8.1f, 1.6f, 1.6f), new P(9.4f, 9.4f, 1.6f, 1.6f),
                                 new P(9.4f, 1, 1.6f, 1.6f), new P(8.1f, 2.3f, 1.6f, 1.6f), new P(6.8f, 3.6f, 1.6f, 1.6f), new P(3.6f, 6.8f, 1.6f, 1.6f), new P(2.3f, 8.1f, 1.6f, 1.6f), new P(1, 9.4f, 1.6f, 1.6f),
                                 new P(5.2f, 5.2f, 1.6f, 1.6f) } },
            { "clubs", new[] { new P(2, 1, 8, 6), new P(2.6f, 7, 6.8f, 1.4f), new P(3.6f, 8.4f, 4.8f, 1.3f), new P(4.8f, 9.7f, 2.4f, 1.3f), new P(5.3f, 2.2f, 1.4f, 6.5f, true), new P(3.4f, 4.2f, 5.2f, 1.3f, true) } },
            { "news", new[] { new P(1, 1.5f, 10, 2), new P(1, 4.5f, 4.4f, 4), new P(6.2f, 4.5f, 4.8f, 1.1f), new P(6.2f, 6.2f, 4.8f, 1.1f), new P(6.2f, 7.9f, 3.4f, 1.1f), new P(1, 9.6f, 10, 1.1f) } },
            { "rankings", new[] { new P(4.4f, 2.5f, 3.2f, 8.5f), new P(1, 5.5f, 3.2f, 5.5f), new P(7.8f, 7.5f, 3.2f, 3.5f), new P(5.4f, 3.6f, 1.2f, 2, true) } },
            { "calendar", new[] { new P(1, 2.2f, 10, 2.6f), new P(3, 1, 1.3f, 2.4f), new P(7.7f, 1, 1.3f, 2.4f), new P(1, 4.8f, 1.2f, 6.2f), new P(9.8f, 4.8f, 1.2f, 6.2f), new P(1, 9.8f, 10, 1.2f),
                                   new P(3, 6, 1.5f, 1.4f), new P(5.25f, 6, 1.5f, 1.4f), new P(7.5f, 6, 1.5f, 1.4f), new P(3, 7.9f, 1.5f, 1.2f), new P(5.25f, 7.9f, 1.5f, 1.2f) } },
            { "bank", new[] { new P(1, 2.5f, 10, 7.5f), new P(1, 4.2f, 10, 1.5f, true), new P(2.4f, 7, 2.6f, 1.4f, true), new P(6, 7.3f, 3.4f, 0.8f, true) } },
            { "properties", new[] { new P(2, 1.5f, 8, 9.5f), new P(3.4f, 3, 1.6f, 1.5f, true), new P(7, 3, 1.6f, 1.5f, true), new P(3.4f, 5.6f, 1.6f, 1.5f, true), new P(7, 5.6f, 1.6f, 1.5f, true), new P(5.1f, 8.3f, 1.8f, 2.7f, true) } },
            { "milestones", new[] { new P(1, 1, 10, 10), new P(2.4f, 2.4f, 7.2f, 7.2f, true), new P(3.8f, 3.8f, 4.4f, 4.4f), new P(5.1f, 5.1f, 1.8f, 1.8f, true) } },
            { "ability", new[] { new P(6, 0.8f, 4, 1.3f), new P(5, 2.1f, 4, 1.3f), new P(4, 3.4f, 4, 1.3f), new P(3, 4.7f, 7, 1.3f), new P(5.5f, 6, 3.5f, 1.3f), new P(4.5f, 7.3f, 3.5f, 1.3f), new P(3.5f, 8.6f, 3.5f, 1.3f), new P(2.5f, 9.9f, 3, 1.3f) } },
            { "settings", new[] { new P(1, 2.6f, 10, 1), new P(1, 5.6f, 10, 1), new P(1, 8.6f, 10, 1), new P(2.6f, 1.4f, 2.4f, 3.4f), new P(7, 4.4f, 2.4f, 3.4f), new P(4, 7.4f, 2.4f, 3.4f) } },
            { "car", new[] { new P(3, 2.5f, 6, 1.2f), new P(2.2f, 3.7f, 7.6f, 1.3f), new P(1, 5, 10, 3.6f), new P(1.6f, 8.6f, 2.2f, 1.8f), new P(8.2f, 8.6f, 2.2f, 1.8f), new P(2, 6.1f, 1.6f, 1.1f, true), new P(8.4f, 6.1f, 1.6f, 1.1f, true) } },
            { "back", new[] { new P(6, 1.5f, 1.8f, 1.8f), new P(4.6f, 2.9f, 1.8f, 1.8f), new P(3.2f, 4.3f, 1.8f, 1.8f), new P(2.4f, 5.1f, 1.8f, 1.8f), new P(3.2f, 5.9f, 1.8f, 1.8f), new P(4.6f, 7.3f, 1.8f, 1.8f), new P(6, 8.7f, 1.8f, 1.8f) } },
            { "chevron", new[] { new P(4, 2, 1.6f, 1.6f), new P(5.2f, 3.2f, 1.6f, 1.6f), new P(6.4f, 4.4f, 1.6f, 1.6f), new P(7, 5.2f, 1.6f, 1.6f), new P(6.4f, 6, 1.6f, 1.6f), new P(5.2f, 7.2f, 1.6f, 1.6f), new P(4, 8.4f, 1.6f, 1.6f) } },
        };

        public static bool Has(string id) { return id != null && set.ContainsKey(id); }

        /// <summary>Draws icon centred at (cx, cy); size = icon height as a screen-height fraction.</summary>
        public static void Draw(string id, float cx, float cy, float size, Color fg, Color bg)
        {
            P[] parts; if (id == null || !set.TryGetValue(id, out parts)) return;
            float uy = size / 12f, ux = Ds.Px(uy), x0 = cx - ux * 6f, y0 = cy - uy * 6f;
            foreach (var p in parts) if (!p.Cut) Ds.Rect(x0 + p.X * ux, y0 + p.Y * uy, p.W * ux, p.H * uy, fg);
            foreach (var p in parts) if (p.Cut) Ds.Rect(x0 + p.X * ux, y0 + p.Y * uy, p.W * ux, p.H * uy, bg);
        }
    }
}
