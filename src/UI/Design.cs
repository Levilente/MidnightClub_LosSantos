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
        /// <summary>Real (unscaled) frame time, so UI animations keep their speed in slow motion.</summary>
        public static float Dt { get { float ts = Game.TimeScale; return Math.Min(0.1f, ts > 0.05f ? Game.LastFrameTime / ts : 1f / 60f); } }
        /// <summary>Frame-rate independent exponential approach.</summary>
        public static float Damp(float cur, float target, float speed) { return cur + (target - cur) * (1f - (float)Math.Exp(-speed * Dt)); }
        public static float Step(float cur, float target, float perSecond) { float d = perSecond * Dt; return cur < target ? Math.Min(target, cur + d) : Math.Max(target, cur - d); }
        public static float EaseOut(float t) { t = Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }
        public static float EaseInOut(float t) { t = Clamp01(t); return t < 0.5f ? 4f * t * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 3) / 2f; }
        public static float EaseBack(float t) { t = Clamp01(t); const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * (float)Math.Pow(t - 1, 3) + c1 * (float)Math.Pow(t - 1, 2); }
        public static float Clamp01(float t) { return t < 0 ? 0 : t > 1 ? 1 : t; }
        public static float Pulse(float hz) { return 0.5f + 0.5f * (float)Math.Sin(Game.GameTime / 1000f * hz * Math.PI * 2); }

        // ---------------------------------------------------------------- shapes
        private static int resW = 1920, resH = 1080, resFrame = -1;
        private static void Res()
        {
            int f = Game.FrameCount; if (f == resFrame) return; resFrame = f;
            var ow = new OutputArgument(); var oh = new OutputArgument();
            Function.Call(Hash.GET_ACTUAL_SCREEN_RESOLUTION, ow, oh);
            int w = ow.GetResult<int>(), h = oh.GetResult<int>();
            if (w > 200 && h > 200) { resW = w; resH = h; }
        }

        public static void Resolution(out int w, out int h) { Res(); w = resW; h = resH; }

        /// <summary>Rectangle snapped to whole screen pixels, so neighbouring strips (gradients, slanted rows)
        /// meet exactly: no overlapping seams (visible lines) and no gaps.</summary>
        public static void Rect(float x, float y, float w, float h, Color c)
        {
            if (c.A <= 0 || w <= 0 || h <= 0) return;
            Res();
            float x0 = (float)Math.Round(x * resW), x1 = (float)Math.Round((x + w) * resW), y0 = (float)Math.Round(y * resH), y1 = (float)Math.Round((y + h) * resH);
            if (x1 <= x0) x1 = x0 + 1; if (y1 <= y0) y1 = y0 + 1;
            float cx = (x0 + x1) / 2f / resW, cy = (y0 + y1) / 2f / resH, ww = (x1 - x0) / resW, hh = (y1 - y0) / resH;
            Function.Call(Hash.DRAW_RECT, cx, cy, ww, hh, (int)c.R, (int)c.G, (int)c.B, (int)c.A, false);
        }

        /// <summary>Sharp panel (the design is deliberately corner-free; kept for call-site compatibility).</summary>
        public static void Round(float x, float y, float w, float h, float r, Color c) { Rect(x, y, w, h, c); }

        private const float Row = 2f / 1080f;
        /// <summary>Parallelogram leaning right: the top edge is shifted right by 'slant' (screen-x fraction). Drawn as
        /// 2-pixel rows so the slanted edges are clean. The street-racing shape of the whole UI.</summary>
        public static void Skew(float x, float y, float w, float h, float slant, Color c)
        {
            if (c.A == 0 || w <= 0 || h <= 0) return;
            if (slant == 0) { Rect(x, y, w, h, c); return; }
            int rows = Math.Max(1, (int)Math.Ceiling(h / Row)); float rh = h / rows;
            for (int i = 0; i < rows; i++)
            {
                float off = slant * (1f - (i + 0.5f) / rows);
                Rect(x + off, y + i * rh, w, rh, c);
            }
        }
        public static void Dot(float cx, float cy, float d, Color c) { Skew(cx - Px(d) / 2f, cy - d / 2f, Px(d) * 0.8f, d, Px(d * 0.3f), c); }

        /// <summary>Slant that matches a 14 degree lean for a given height.</summary>
        public static float SlantFor(float h) { return Px(h * 0.25f); }

        /// <summary>A light band sweeping across a skewed bar every 'period' seconds.</summary>
        public static void Shine(float x, float y, float w, float h, float slant, float period, float alpha)
        {
            float t = (Game.GameTime % (int)(period * 1000)) / 1000f / 0.55f;
            if (t > 1f) return;
            float bw = Px(0.03f), bx = x + (w - bw) * EaseInOut(t);
            Skew(bx, y, bw, h, slant, A(Color.White, 0.22f * alpha * (float)Math.Sin(t * Math.PI)));
        }

        /// <summary>Small skewed tag with text (used for kickers, chips and badges). Returns its width.</summary>
        public static float Tag(string s, float x, float y, Color bg, Color fg, float scale = 0.24f, float alpha = 1f)
        {
            float h = 0.024f * scale / 0.24f, sl = SlantFor(h), w = Width(s, scale, Display) + Px(0.014f);
            Skew(x, y, w, h, sl, A(bg, alpha));
            T(s, x + sl / 2 + w / 2, y - 0.0005f, scale * 1.05f, A(fg, alpha), Display, Alignment.Center);
            return w + sl;
        }

        /// <summary>Racing stripes: a thick and a thin skewed line.</summary>
        public static void Stripes(float x, float y, float w, Color a, Color b, float alpha = 1f)
        {
            Skew(x, y, w, 0.0045f, Px(0.0012f), A(a, alpha));
            Skew(x + Px(0.01f), y + 0.0075f, w * 0.6f, 0.002f, Px(0.0006f), A(b, alpha));
        }

        /// <summary>Animated horizontal speed streaks inside a region (background energy).</summary>
        public static void Streaks(float x, float y, float w, float h, Color c, float alpha, int count = 14, float speed = 0.6f)
        {
            for (int i = 0; i < count; i++)
            {
                float seed = (i * 0.618034f) % 1f, len = Px(0.05f + 0.12f * ((i * 0.37f) % 1f));
                float sp = speed * (0.6f + seed), t = ((Game.GameTime / 1000f) * sp + seed * 7f) % 1.6f;
                float sx = x + w - t * (w + len), sy = y + h * ((i * 0.4142f + 0.13f) % 1f);
                float l = Math.Max(0f, Math.Min(sx + len, x + w) - Math.Max(sx, x));
                if (l <= 0) continue;
                Rect(Math.Max(sx, x), sy, l, 0.0012f, A(c, alpha * (0.25f + 0.5f * seed)));
            }
        }

        /// <summary>Vertical gradient from top colour to bottom colour, drawn in strips.</summary>
        public static void VGrad(float x, float y, float w, float h, Color top, Color bottom, int steps = 16)
        {
            float sh = h / steps;
            for (int i = 0; i < steps; i++) Rect(x, y + i * sh, w, sh, Mix(top, bottom, (i + 0.5f) / steps));
        }

        public static void HGrad(float x, float y, float w, float h, Color left, Color right, int steps = 16)
        {
            float sw = w / steps;
            for (int i = 0; i < steps; i++) Rect(x + i * sw, y, sw, h, Mix(left, right, (i + 0.5f) / steps));
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
            float lh = 0.074f * size;
            Skew(x0, y + lh, total * 0.62f, 0.0035f * size, Px(0.001f * size), A(Violet, alpha));
            Skew(x0 + total * 0.64f, y + lh, total * 0.08f, 0.0035f * size, Px(0.001f * size), A(Cyan, alpha));
            T("LOS SANTOS", x0 + total, y + lh + 0.006f * size, 0.3f * size, A(Muted, alpha), Display, Alignment.Right);
        }

        /// <summary>Small uppercase label with a coloured dot.</summary>
        public static void Kicker(string s, float x, float y, Color accent, float alpha = 1f)
        {
            Skew(x, y + 0.003f, Px(0.0045f), 0.018f, Px(0.0045f), A(accent, alpha));
            T(s, x + Px(0.016f), y - 0.001f, 0.3f, A(Muted, alpha), Display);
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
