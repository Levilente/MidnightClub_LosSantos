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
        private static readonly Dictionary<string, CustomSprite> sprites = new Dictionary<string, CustomSprite>();
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

        /// <summary>Draws a texture into a box: x, y = top-left (screen fractions), w = width (screen-width fraction), h = height (screen-height fraction).</summary>
        public static bool DrawBox(string name, float x, float y, float w, float h, Color c)
        {
            if (!Has(name)) return false;
            if (c.A == 0) return true;
            try
            {
                CustomSprite sp;
                if (!sprites.TryGetValue(name, out sp)) sprites[name] = sp = new CustomSprite(Path.Combine(dir, name + ".png"), new SizeF(64, 64), PointF.Empty, Color.White, 0f, false);
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
                CustomSprite sp;
                if (!sprites.TryGetValue(name, out sp)) sprites[name] = sp = new CustomSprite(Path.Combine(dir, name + ".png"), new SizeF(64, 64), PointF.Empty, Color.White, 0f, true);
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
