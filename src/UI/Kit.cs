using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using GTA.UI;
using Control = GTA.Control;

namespace MidnightClub.UI
{
    public enum UiMode { Auto, Dark, Light }

    /// <summary>Design tokens (docs\DESIGN.md). Glass surfaces in a dark and a light theme, five category accents,
    /// a radius / spacing scale in 1080p pixels. Auto mode follows the in-game clock (light by day, dark at night).</summary>
    public static class Th
    {
        public static UiMode Mode = UiMode.Auto;
        private static int frame = -1; private static bool light;
        public static bool Light
        {
            get
            {
                int f = Game.FrameCount;
                if (f != frame)
                {
                    frame = f;
                    if (Mode == UiMode.Auto) { int h = World.CurrentTimeOfDay.Hours; light = h >= 8 && h < 19; }
                    else light = Mode == UiMode.Light;
                }
                return light;
            }
        }
        private static Color P(Color dark, Color lite) { return Light ? lite : dark; }

        // surfaces
        public static Color Glass { get { return P(Color.FromArgb(196, 18, 20, 28), Color.FromArgb(214, 246, 247, 250)); } }
        public static Color GlassStrong { get { return P(Color.FromArgb(232, 20, 22, 31), Color.FromArgb(238, 250, 251, 253)); } }
        public static Color GlassSoft { get { return P(Color.FromArgb(120, 18, 20, 28), Color.FromArgb(150, 246, 247, 250)); } }
        public static Color Raised { get { return P(Color.FromArgb(255, 44, 47, 60), Color.FromArgb(255, 228, 231, 238)); } }
        public static Color Line { get { return P(Color.FromArgb(40, 255, 255, 255), Color.FromArgb(34, 10, 14, 30)); } }
        public static Color Highlight { get { return P(Color.FromArgb(46, 255, 255, 255), Color.FromArgb(200, 255, 255, 255)); } }
        public static Color Scrim { get { return P(Color.FromArgb(150, 6, 7, 10), Color.FromArgb(70, 230, 232, 238)); } }
        // text (contrast >= 4.5:1 on the glass in both themes)
        public static Color Text { get { return P(Color.FromArgb(255, 245, 246, 250), Color.FromArgb(255, 14, 16, 22)); } }
        public static Color Sub { get { return P(Color.FromArgb(255, 172, 177, 194), Color.FromArgb(255, 70, 76, 92)); } }
        public static Color Faint { get { return P(Color.FromArgb(255, 112, 117, 136), Color.FromArgb(255, 130, 135, 150)); } }
        public static Color Track { get { return P(Color.FromArgb(60, 255, 255, 255), Color.FromArgb(46, 10, 14, 30)); } }

        // accents (same saturation / lightness family)
        public static readonly Color Red = Color.FromArgb(255, 255, 59, 92);
        public static readonly Color Cyan = Color.FromArgb(255, 34, 184, 240);
        public static readonly Color Amber = Color.FromArgb(255, 255, 176, 32);
        public static readonly Color Green = Color.FromArgb(255, 61, 220, 151);
        public static readonly Color Violet = Color.FromArgb(255, 139, 124, 255);
        public static Color Accent = Red;
        /// <summary>Text colour to put on an accent fill (dark on the bright amber / green, white on the rest).</summary>
        public static Color On(Color accent) { return accent.R * 0.299 + accent.G * 0.587 + accent.B * 0.114 > 160 ? Color.FromArgb(255, 14, 16, 22) : Color.White; }

        // scale: 1080p pixels -> screen height fraction
        public static float Px(float px) { return px / 1080f; }
        public const float R_Sm = 6f, R_Md = 10f, R_Lg = 14f;
    }

    /// <summary>Drawing kit: rounded glass panels (GTA rects for the body, anti-aliased corner textures, so text drawn
    /// afterwards stays on top), outlines, pills, bars, badges and the button-prompt bar.</summary>
    public static class Kit
    {
        private static int RW { get { int w, h; Ds.Resolution(out w, out h); return w; } }
        private static int RH { get { int w, h; Ds.Resolution(out w, out h); return h; } }

        /// <summary>Rounded rectangle. x, y, w, h: screen fractions (w along x). r: radius in 1080p pixels.</summary>
        public static void Round(float x, float y, float w, float h, float r, Color c)
        {
            if (c.A <= 0 || w <= 0 || h <= 0) return;
            int W = RW, H = RH;
            float X0 = (float)Math.Round(x * W), X1 = (float)Math.Round((x + w) * W), Y0 = (float)Math.Round(y * H), Y1 = (float)Math.Round((y + h) * H);
            float R = (float)Math.Round(Math.Min(r * H / 1080f, Math.Min((X1 - X0) / 2f, (Y1 - Y0) / 2f)));
            if (R < 1.5f || !Textures.Has("q_tl")) { Ds.Rect(x, y, w, h, c); return; }
            Func<float, float> fx = v => v / W; Func<float, float> fy = v => v / H;
            // one GTA rect for the centre column (where text sits; GTA rects are budgeted per frame), the two side
            // columns are textures: corner pieces + a plain strip. Text never sits in the radius-wide side columns.
            Ds.Rect(fx(X0 + R), fy(Y0), fx(X1 - X0 - 2 * R), fy(Y1 - Y0), c);
            if (Y1 - Y0 - 2 * R > 0.5f)
            {
                Textures.DrawBox("px", fx(X0), fy(Y0 + R), fx(R), fy(Y1 - Y0 - 2 * R), c);
                Textures.DrawBox("px", fx(X1 - R), fy(Y0 + R), fx(R), fy(Y1 - Y0 - 2 * R), c);
            }
            Textures.DrawBox("q_tl", fx(X0), fy(Y0), fx(R), fy(R), c);
            Textures.DrawBox("q_tr", fx(X1 - R), fy(Y0), fx(R), fy(R), c);
            Textures.DrawBox("q_bl", fx(X0), fy(Y1 - R), fx(R), fy(R), c);
            Textures.DrawBox("q_br", fx(X1 - R), fy(Y1 - R), fx(R), fy(R), c);
        }

        /// <summary>Rounded outline (1-2 px). Corner rings are textures, straight edges are rects.</summary>
        public static void Outline(float x, float y, float w, float h, float r, float thickPx, Color c)
        {
            if (c.A <= 0) return;
            int W = RW, H = RH;
            float X0 = (float)Math.Round(x * W), X1 = (float)Math.Round((x + w) * W), Y0 = (float)Math.Round(y * H), Y1 = (float)Math.Round((y + h) * H);
            float R = (float)Math.Round(Math.Min(r * H / 1080f, Math.Min((X1 - X0) / 2f, (Y1 - Y0) / 2f)));
            float t = Math.Max(1f, (float)Math.Round(thickPx * H / 1080f));
            if (R < 2f) R = 0f;
            Func<float, float> fx = v => v / W; Func<float, float> fy = v => v / H;
            // outlines are textures only (no GTA rect budget): edges are thin strips that never cross text
            Textures.DrawBox("px", fx(X0 + R), fy(Y0), fx(X1 - X0 - 2 * R), fy(t), c);
            Textures.DrawBox("px", fx(X0 + R), fy(Y1 - t), fx(X1 - X0 - 2 * R), fy(t), c);
            Textures.DrawBox("px", fx(X0), fy(Y0 + R), fx(t), fy(Y1 - Y0 - 2 * R), c);
            Textures.DrawBox("px", fx(X1 - t), fy(Y0 + R), fx(t), fy(Y1 - Y0 - 2 * R), c);
            if (R <= 0f) return;
            // corner rings come in four stroke ratios; pick the one whose stroke matches t
            float ratio = t / R; string q = ratio < 0.095f ? "qo7" : ratio < 0.16f ? "qo12" : ratio < 0.26f ? "qo20" : "qo32";
            Textures.DrawBox(q + "_tl", fx(X0), fy(Y0), fx(R), fy(R), c);
            Textures.DrawBox(q + "_tr", fx(X1 - R), fy(Y0), fx(R), fy(R), c);
            Textures.DrawBox(q + "_bl", fx(X0), fy(Y1 - R), fx(R), fy(R), c);
            Textures.DrawBox(q + "_br", fx(X1 - R), fy(Y1 - R), fx(R), fy(R), c);
        }

        /// <summary>Frosted glass card: translucent fill, hairline border and a top highlight (light source).</summary>
        public static void Glass(float x, float y, float w, float h, float r = Th.R_Lg, float alpha = 1f, bool strong = false)
        {
            Round(x, y, w, h, r, Ds.A(strong ? Th.GlassStrong : Th.Glass, alpha));
            Outline(x, y, w, h, r, 1f, Ds.A(Th.Line, alpha));
            float rx = Ds.Px(Th.Px(r));
            Textures.DrawBox("px", x + rx, y + Th.Px(1f), w - 2 * rx, Th.Px(1f), Ds.A(Th.Highlight, alpha));
        }

        /// <summary>Fully rounded pill.</summary>
        public static void Pill(float x, float y, float w, float h, Color c) { Round(x, y, w, h, h * 1080f / 2f, c); }

        /// <summary>Rounded progress bar with track.</summary>
        public static void Bar(float x, float y, float w, float h, float k, Color fill, Color? track = null)
        {
            k = Ds.Clamp01(k);
            Pill(x, y, w, h, track ?? Th.Track);
            if (k > 0.001f) Pill(x, y, Math.Max(Ds.Px(h), w * k), h, fill);
        }

        /// <summary>Small chip: text on a rounded tinted background. Returns its width.</summary>
        public static float Chip(string s, float x, float cy, Color bg, Color fg, float scale = 0.26f)
        {
            float tw = Ds.Width(s, scale, Ds.Display), h = Ds.Eff(scale) * 0.075f + Th.Px(8f), w = tw + Ds.Px(Th.Px(16f));
            Round(x, cy - h / 2f, w, h, Th.R_Sm, bg);
            Ds.TM(s, x + w / 2f, cy, scale, fg, Ds.Display, Alignment.Center);
            return w;
        }

        /// <summary>Count badge (circle / pill).</summary>
        public static void Badge(string s, float cx, float cy, Color bg)
        {
            float h = Th.Px(24f), w = Math.Max(Ds.Px(h), Ds.Width(s, 0.3f, Ds.Display) + Ds.Px(Th.Px(12f)));
            Pill(cx - w / 2f, cy - h / 2f, w, h, bg);
            Ds.TM(s, cx, cy, 0.3f, Th.On(bg), Ds.Display, Alignment.Center);
        }

        /// <summary>Selection ring around a focused element (2px accent).</summary>
        public static void Focus(float x, float y, float w, float h, float r, Color c, float alpha = 1f)
        {
            float g = Th.Px(3f);
            Outline(x - Ds.Px(g), y - g, w + 2 * Ds.Px(g), h + 2 * g, r + 3f, 2f, Ds.A(c, alpha));
        }

        /// <summary>Toggle switch (on/off) drawn at x, cy.</summary>
        public static void Toggle(float x, float cy, bool on, Color accent)
        {
            float h = Th.Px(22f), w = Ds.Px(Th.Px(40f));
            Pill(x, cy - h / 2f, w, h, on ? accent : Th.Track);
            float d = h - Th.Px(6f);
            Textures.Draw("disc", on ? x + w - Ds.Px(h / 2f) : x + Ds.Px(h / 2f), cy, d, Color.White);
        }

        // ------------------------------------------------------------------ button prompts
        // ---- controller family: PlayStation pads get PlayStation glyphs (like the game itself)
        public static string ButtonStyle = "Auto";      // Auto / Xbox / PlayStation (config [HUD] ButtonStyle)
        private static volatile bool sonyPad, logged; private static int padCheckAt = -100000; private static volatile bool checking;
        public static bool PlayStation
        {
            get
            {
                if (ButtonStyle == "PlayStation") return true;
                if (ButtonStyle == "Xbox") return false;
                int now = Environment.TickCount;
                if (!checking && now - padCheckAt > 20000)
                {
                    padCheckAt = now; checking = true;
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            using (var q = new System.Management.ManagementObjectSearcher("SELECT DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE '%054C%'"))
                            using (var r = q.Get())
                            {
                                bool found = false;
                                foreach (System.Management.ManagementObject o in r)
                                {   // USB: VID_054C, Bluetooth: VID&0002054C; Sony pads: DS4 05C4/09CC, DualSense 0CE6, Edge 0DF2
                                    string id = (o["DeviceID"] as string ?? "").ToUpperInvariant();
                                    if (id.Contains("VID_054C") || id.Contains("VID&0002054C") || id.Contains("054C&PID")) { found = true; break; }
                                }
                                if (found != sonyPad || !logged) { logged = true; Core.Log.Info("Controller glyphs: " + (found ? "PlayStation pad found" : "no PlayStation pad")); }
                                sonyPad = found;
                            }
                        }
                        catch (Exception ex) { if (!logged) { logged = true; Core.Log.Error("Controller detection", ex); } }
                        checking = false;
                    });
                }
                return sonyPad;
            }
        }
        private static string Ps(string k)
        {
            switch (k)
            {
                case "PA": return "PSX"; case "PB": return "PSO"; case "PX": return "PSQ"; case "PY": return "PST";
                case "PLB": return "PL1"; case "PRB": return "PR1"; case "PLT": return "PL2"; case "PRT": return "PR2";
                case "PLS": return "PL3"; case "PRS": return "PR3"; case "PSTART": return "POPT"; case "PBACK": return "PSHARE";
            }
            return k;
        }

        private static string KeyFor(Control c, bool pad)
        {
            string k = KeyFor0(c, pad);
            return pad && PlayStation ? Ps(k) : k;
        }

        private static string KeyFor0(Control c, bool pad)
        {
            if (pad)
            {
                switch (c)
                {
                    case Control.FrontendAccept: case Control.Jump: return "PA";
                    case Control.FrontendCancel: return "PB";
                    case Control.FrontendX: return "PX";
                    case Control.FrontendY: return "PY";
                    case Control.FrontendLb: return "PLB";
                    case Control.FrontendRb: case Control.VehicleHandbrake: return "PRB";
                    case Control.FrontendLt: case Control.VehicleBrake: return "PLT";
                    case Control.FrontendRt: case Control.VehicleAccelerate: return "PRT";
                    case Control.FrontendUp: return "PDUP";
                    case Control.FrontendDown: return "PDDOWN";
                    case Control.FrontendLeft: return "PDLEFT";
                    case Control.FrontendRight: case Control.Context: return "PDRIGHT";
                    case Control.FrontendPause: case Control.FrontendPauseAlternate: return "PSTART";
                    case Control.FrontendSelect: case Control.NextCamera: return "PBACK";
                    case Control.VehicleLookBehind: return "PRS";
                    case Control.Sprint: case Control.VehicleDuck: return "PA";
                }
            }
            try
            {
                string s = Function.Call<string>(Hash.GET_CONTROL_INSTRUCTIONAL_BUTTONS_STRING, 2, (int)c, true);
                if (!pad && s != null && s.StartsWith("t_"))
                {
                    string k = s.Substring(2).ToUpperInvariant();
                    if (KeycapWidths.W.ContainsKey(k)) return k;
                }
            }
            catch { }
            switch (c)
            {
                case Control.FrontendAccept: return pad ? "PA" : "ENTER";
                case Control.FrontendCancel: case Control.FrontendPause: case Control.FrontendPauseAlternate: return pad ? "PB" : "ESC";
                case Control.FrontendUp: return "UP";
                case Control.FrontendDown: return "DOWN";
                case Control.FrontendLeft: return "LEFT";
                case Control.FrontendRight: return "RIGHT";
                case Control.FrontendX: case Control.Jump: case Control.VehicleHandbrake: return "SPACE";
                case Control.FrontendY: return "TAB";
                case Control.FrontendLb: return "Q";
                case Control.FrontendRb: case Control.Context: return "E";
                case Control.Sprint: return "SHIFT";
                case Control.VehicleLookBehind: return "C";
                case Control.NextCamera: return "V";
                case Control.VehicleDuck: return "X";
            }
            return pad ? "PA" : "ENTER";
        }

        private static string Merge(string a, string b)
        {
            var p = new HashSet<string> { a, b };
            if (p.SetEquals(new[] { "UP", "DOWN" })) return "UPDOWN";
            if (p.SetEquals(new[] { "LEFT", "RIGHT" })) return "LEFTRIGHT";
            if (p.SetEquals(new[] { "PDUP", "PDDOWN" })) return "PDUD";
            if (p.SetEquals(new[] { "PDLEFT", "PDRIGHT" })) return "PDLR";
            return null;
        }

        /// <summary>Button prompt bar, bottom right: glass pill with keycaps (keyboard or pad, follows the last input) and
        /// labels. Pairs of (Control, label); an empty label joins that key to the next one (Q E Tabs).</summary>
        public static void Prompts(params object[] pairs)
        {
            bool pad = Game.LastInputMethod == InputMethod.GamePad;
            var groups = new List<KeyValuePair<List<string>, string>>();
            var keys = new List<string>();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                keys.Add(KeyFor((Control)pairs[i], pad));
                string label = pairs[i + 1] as string;
                if (string.IsNullOrEmpty(label) && i + 3 < pairs.Length) continue;
                if (keys.Count == 2) { string m = Merge(keys[0], keys[1]); if (m != null) keys = new List<string> { m }; }
                groups.Add(new KeyValuePair<List<string>, string>(keys, label ?? ""));
                keys = new List<string>();
            }
            if (groups.Count == 0) return;
            float kh = Th.Px(30f), gap = Ds.Px(Th.Px(6f)), sep = Ds.Px(Th.Px(18f)), padX = Ds.Px(Th.Px(12f));
            Func<string, float> kw = k => { int w; return Ds.Px(kh) * (KeycapWidths.W.TryGetValue(k, out w) ? w : 64) / 64f; };
            float total = padX * 2;
            foreach (var g in groups)
            {
                foreach (var k in g.Key) total += kw(k) + Ds.Px(Th.Px(3f));
                total += gap + Ds.Width(g.Value, 0.3f, Ds.Display) + sep;
            }
            total -= sep;
            float h = Th.Px(44f), right = 1f - Ds.Px(Th.Px(28f)), y = 1f - Th.Px(28f) - h, cy = y + h / 2f;
            float x = right - total;
            Glass(x, y, total, h, h * 1080f / 2f, 1f, true);
            x += padX;
            foreach (var g in groups)
            {
                foreach (var k in g.Key)
                {
                    float w = kw(k);
                    Textures.DrawBox("kc_" + k, x, cy - kh / 2f, w, kh, Color.White);
                    x += w + Ds.Px(Th.Px(3f));
                }
                x += gap - Ds.Px(Th.Px(3f));
                Ds.TM(g.Value.ToUpperInvariant(), x, cy, 0.3f, Th.Text, Ds.Display);
                x += Ds.Width(g.Value.ToUpperInvariant(), 0.3f, Ds.Display) + sep;
            }
        }

        /// <summary>A single inline key hint (for world prompts): keycap + text, left-aligned at x, centred on cy.</summary>
        public static float KeyHint(Control c, string label, float x, float cy, float scale = 0.32f)
        {
            bool pad = Game.LastInputMethod == InputMethod.GamePad;
            string k = KeyFor(c, pad); int wi; KeycapWidths.W.TryGetValue(k, out wi); if (wi == 0) wi = 64;
            float kh = Th.Px(30f), w = Ds.Px(kh) * wi / 64f;
            Textures.DrawBox("kc_" + k, x, cy - kh / 2f, w, kh, Color.White);
            Ds.TM(label, x + w + Ds.Px(Th.Px(8f)), cy, scale, Th.Text, Ds.Display);
            return w + Ds.Px(Th.Px(8f)) + Ds.Width(label, scale, Ds.Display);
        }

        /// <summary>Interaction prompt (replaces GTA's help box): glass capsule low in the centre with the key and the
        /// action, e.g. [E] Enter garage. An optional title sits above the action.</summary>
        public static void Interact(Control c, string action, string title = null)
        {
            Function.Call(Hash.HIDE_HELP_TEXT_THIS_FRAME);
            bool pad = Game.LastInputMethod == InputMethod.GamePad;
            string k = KeyFor(c, pad); int wi; KeycapWidths.W.TryGetValue(k, out wi); if (wi == 0) wi = 64;
            float kh = Th.Px(34f), kw = Ds.Px(kh) * wi / 64f, h = Th.Px(title != null ? 66f : 52f);
            string a = (action ?? "").ToUpperInvariant();
            float tw = Math.Max(Ds.Width(a, 0.36f, Ds.Display), title != null ? Ds.Width(title, 0.24f, Ds.Display) : 0f);
            float w = Ds.Px(Th.Px(10f)) + kw + Ds.Px(Th.Px(12f)) + tw + Ds.Px(Th.Px(22f));
            float x = 0.5f - w / 2f, y = 0.8f - h, cy = y + h / 2f;
            Glass(x, y, w, h, h * 1080f / 2f, 1f, true);
            Textures.DrawBox("kc_" + k, x + Ds.Px(Th.Px(10f)), cy - kh / 2f, kw, kh, Color.White);
            float tx = x + Ds.Px(Th.Px(10f)) + kw + Ds.Px(Th.Px(12f));
            if (title != null) { Ds.TM(title.ToUpperInvariant(), tx, cy - Th.Px(11f), 0.24f, Th.Accent, Ds.Display); Ds.TM(a, tx, cy + Th.Px(10f), 0.36f, Th.Text, Ds.Display); }
            else Ds.TM(a, tx, cy, 0.36f, Th.Text, Ds.Display);
        }
    }
}
