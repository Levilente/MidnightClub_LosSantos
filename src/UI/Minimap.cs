using System;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;

namespace MidnightClub.UI
{
    /// <summary>Car-game minimap: no health / armour / ability bars, a clean frame, and the road name with a compass
    /// heading above it (Forza style). The game's own street / area names are hidden by the HUD.</summary>
    public static class Minimap
    {
        private static Scaleform mm; private static int setupAt = -100000;
        private static string street = ""; private static int streetAt;
        private static int refreshFrames, refreshDelay;

        /// <summary>After the GTA Online map is loaded the radar keeps its old (empty) tiles until the big map is opened
        /// once: briefly switches the expanded radar on and off (and lifts the fog of war) so the tiles are rebuilt.</summary>
        public static void Refresh(int delayFrames = 30) { refreshDelay = delayFrames; refreshFrames = 12; }
        /// <summary>Every frame in the mode (also when the HUD isn't drawn).</summary>
        public static void Tick() { try { RefreshTick(); } catch { } }
        private static void RefreshTick()
        {
            if (refreshFrames <= 0) return;
            if (refreshFrames == 12 && (Function.Call<bool>(Hash.IS_RADAR_HIDDEN) || Screen.IsFadedOut || Screen.IsFadingIn)) return;   // wait until the radar is really on screen (garage, fades)
            if (refreshDelay > 0) { refreshDelay--; return; }
            if (refreshFrames == 12)
            {
                Function.Call(Hash.SET_MINIMAP_IN_PROLOGUE, false);
                Function.Call(Hash.SET_USE_ISLAND_MAP, false);
                Function.Call(Hash.SET_MINIMAP_HIDE_FOW, true);
                Function.Call(Hash.SET_RADAR_ZOOM, 0);
            }
            Function.Call(Hash.SET_BIGMAP_ACTIVE, refreshFrames > 1, false);
            refreshFrames--;
        }

        /// <summary>Minimap rectangle in screen fractions (from the safe zone and aspect ratio, like the game lays it out).</summary>
        public static void Rect(out float x, out float y, out float w, out float h)
        {
            float sz = Function.Call<float>(Hash.GET_SAFE_ZONE_SIZE);
            float margin = 0.5f * Math.Abs(sz - 1f);
            // the game draws the radar on the real screen: compute it there, then express it on the UI canvas
            float pw = 1f / (4f * Ds.PhysAspect), ph = 1f / 5.674f, px = margin, py = 1f - margin - ph;
            x = Ds.UX(px); y = Ds.UY(py); w = Ds.UW(pw); h = ph / Ds.MS;
        }

        public static void Draw()
        {
            try
            {
                if (mm == null) mm = Scaleform.RequestMovie("minimap");
                if (mm != null && mm.IsLoaded && Game.GameTime - setupAt > 1000)
                {
                    setupAt = Game.GameTime;
                    mm.CallFunction("SETUP_HEALTH_ARMOUR", 3);      // golf layout: hides health, armour and ability bars
                }
                GTA.UI.Hud.HideComponentThisFrame(GTA.UI.HudComponent.StreetName);
                GTA.UI.Hud.HideComponentThisFrame(GTA.UI.HudComponent.AreaName);
                if (Function.Call<bool>(Hash.IS_RADAR_HIDDEN)) return;
                float x, y, w, h; Rect(out x, out y, out w, out h);
                // thin frame
                // road name + heading
                var ped = Game.Player.Character;
                if (Game.GameTime - streetAt > 500)
                {
                    streetAt = Game.GameTime;
                    var p = ped.Position;
                    var hash = new OutputArgument(); var cross = new OutputArgument();
                    Function.Call(Hash.GET_STREET_NAME_AT_COORD, p.X, p.Y, p.Z, hash, cross);
                    street = Function.Call<string>(Hash.GET_STREET_NAME_FROM_HASH_KEY, hash.GetResult<int>()) ?? "";
                }
                float hd = ped.IsInVehicle() ? ped.CurrentVehicle.Heading : ped.Heading;
                string[] dirs = { "N", "NW", "W", "SW", "S", "SE", "E", "NE" };
                string dir = dirs[(int)Math.Round(((hd % 360f) + 360f) % 360f / 45f) % 8];
                float ph = Th.Px(34f), ty = y - ph - Th.Px(8f);
                float dw = Ds.Width(dir, 0.34f, Ds.Display);
                string st = string.IsNullOrEmpty(street) ? "" : Ds.Fit(street.ToUpperInvariant(), 0.28f, w - dw - Ds.Px(Th.Px(60f)), Ds.Display);
                float pw = Ds.Px(Th.Px(12f)) + Ds.Px(Th.Px(30f)) + Ds.Px(Th.Px(10f)) + Ds.Width(st, 0.28f, Ds.Display) + Ds.Px(Th.Px(16f));
                Kit.Glass(x, ty, Math.Min(w, pw), ph, ph * 1080f / 2f, 0.92f);
                float bcx = x + Ds.Px(Th.Px(6f)) + Ds.Px(Th.Px(15f)), cy = ty + ph / 2f;
                Kit.Pill(bcx - Ds.Px(Th.Px(15f)), cy - Th.Px(13f), Ds.Px(Th.Px(30f)), Th.Px(26f), Th.Accent);
                Ds.TM(dir, bcx, cy, 0.3f, Color.White, Ds.Display, GTA.UI.Alignment.Center);
                if (st.Length > 0) Ds.TM(st, bcx + Ds.Px(Th.Px(25f)), cy, 0.28f, Th.Text, Ds.Display);
            }
            catch { }
        }
    }
}
