using System;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;

namespace MidnightClub.UI
{
    /// <summary>Car-game minimap: no health / armour / ability bars, a clean frame, and the road name with a compass
    /// heading above it (Forza style). The game's own street / area names are hidden by the HUD.</summary>
    public static class Minimap
    {
        private static Scaleform mm; private static int setupAt = -100000;
        private static string street = ""; private static int streetAt;

        /// <summary>Minimap rectangle in screen fractions (from the safe zone and aspect ratio, like the game lays it out).</summary>
        public static void Rect(out float x, out float y, out float w, out float h)
        {
            float sz = Function.Call<float>(Hash.GET_SAFE_ZONE_SIZE);
            float margin = 0.5f * Math.Abs(sz - 1f);
            w = 1f / (4f * Ds.Aspect) * 1.0f; h = 1f / 5.674f;
            x = margin; y = 1f - margin - h;
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
