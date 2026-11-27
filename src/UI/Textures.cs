using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using GTA.UI;
using MidnightClub.Core;

namespace MidnightClub.UI
{
    /// <summary>Anti-aliased PNG icons (embedded, extracted to scripts\MidnightClub\ui) drawn through Script Hook V's
    /// texture renderer. Unlike DRAW_RECT these don't count against GTA's per-frame script draw budget, so a screen
    /// full of icons can never make other UI elements disappear. Textures render above GTA's own HUD layer.</summary>
    public static class Textures
    {
        private static string dir; private static bool ready;
        private static readonly HashSet<string> available = new HashSet<string>();

        public static void Init(string baseDir)
        {
            try
            {
                dir = Path.Combine(baseDir, "ui"); Directory.CreateDirectory(dir);
                var asm = Assembly.GetExecutingAssembly();
                foreach (var res in asm.GetManifestResourceNames())
                {
                    if (!res.StartsWith("ui.") || !res.EndsWith(".png")) continue;
                    string name = res.Substring(3), file = Path.Combine(dir, name);
                    using (var s = asm.GetManifestResourceStream(res))
                    {
                        if (!File.Exists(file) || new FileInfo(file).Length != s.Length)
                            using (var f = File.Create(file)) s.CopyTo(f);
                    }
                    available.Add(name.Substring(0, name.Length - 4));
                }
                ready = available.Count > 0;
                Log.Info("UI textures ready: " + available.Count);
            }
            catch (Exception ex) { Log.Error("UI textures", ex); ready = false; }
        }

        public static bool Has(string name) { return ready && available.Contains(name); }

        // Script Hook V keeps a drawn texture on screen for ~100 ms and one sprite instance can only be at one place per
        // frame. So: one instance per (texture, use-in-frame) slot, and slots that were not drawn in the last frame are
        // overwritten with a transparent draw - no more ghost digits when a number changes.
        private sealed class Slot { public CustomSprite Sp; public int Frame; public bool Live; }
        private static readonly Dictionary<string, List<Slot>> slots = new Dictionary<string, List<Slot>>();
        private static readonly Dictionary<string, int> useCount = new Dictionary<string, int>();
        private static int frame = -1;

        private static void NewFrameCheck()
        {
            int f = GTA.Game.FrameCount;
            if (f == frame) return;
            int prev = frame; frame = f; useCount.Clear();
            foreach (var list in slots.Values)
                foreach (var sl in list)
                    if (sl.Live && sl.Frame != prev)
                    {
                        sl.Live = false;
                        try { sl.Sp.Color = Color.FromArgb(0, 255, 255, 255); sl.Sp.ScaledDraw(); } catch { }
                    }
        }

        private static CustomSprite Take(string name, bool centred)
        {
            NewFrameCheck();
            string key = name + (centred ? "@c" : "@b");
            int n; useCount.TryGetValue(key, out n); useCount[key] = n + 1;
            List<Slot> list; if (!slots.TryGetValue(key, out list)) slots[key] = list = new List<Slot>();
            while (list.Count <= n) list.Add(new Slot { Sp = new CustomSprite(Path.Combine(dir, name + ".png"), new SizeF(64, 64), PointF.Empty, Color.White, 0f, centred) });
            var s = list[n]; s.Frame = frame; s.Live = true; return s.Sp;
        }

        /// <summary>Call once per tick when nothing else may draw (keeps stale sprites from lingering).</summary>
        public static void Flush() { if (ready) NewFrameCheck(); }

        /// <summary>Draws a texture into a box: x, y = top-left (screen fractions), w = width (screen-width fraction), h = height (screen-height fraction).</summary>
        public static bool DrawBox(string name, float x, float y, float w, float h, Color c)
        {
            if (!Has(name)) return false;
            if (c.A == 0) return true;
            try
            {
                var sp = Take(name, false);
                float sw = 720f * Screen.AspectRatio;
                sp.Size = new SizeF(w * sw, h * 720f); sp.Position = new PointF(x * sw, y * 720f); sp.Color = c;
                sp.ScaledDraw();
                return true;
            }
            catch (Exception ex) { Log.Error("Texture " + name, ex); available.Remove(name); return false; }
        }

        /// <summary>Draws a square texture centred at (cx, cy) in normalised screen space; size = height fraction.</summary>
        public static bool Draw(string name, float cx, float cy, float size, Color c)
        {
            if (!Has(name)) return false;
            if (c.A == 0) return true;
            try
            {
                var sp = Take(name, true);
                float w = 720f * Screen.AspectRatio;
                sp.Size = new SizeF(size * 720f, size * 720f);
                sp.Position = new PointF(cx * w, cy * 720f);
                sp.Color = c;
                sp.ScaledDraw();
                return true;
            }
            catch (Exception ex) { Log.Error("Texture " + name, ex); available.Remove(name); return false; }
        }
    }
}
