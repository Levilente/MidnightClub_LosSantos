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
                var line = Color.FromArgb(70, 255, 255, 255);
                Ds.Rect(x, y - 0.0015f, w, 0.0015f, line);
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
                float ty = y - 0.022f;
                float dw = Ds.Width(dir, 0.42f, Ds.Display);
                Ds.TM(dir, x, ty, 0.42f, Color.FromArgb(255, 255, 92, 102), Ds.Display);
                if (!string.IsNullOrEmpty(street)) Ds.TM(Ds.Fit(street.ToUpperInvariant(), 0.34f, w - dw - Ds.Px(0.012f), Ds.Display), x + dw + Ds.Px(0.008f), ty, 0.34f, Color.FromArgb(235, 255, 255, 255), Ds.Display);
            }
            catch { }
        }
    }
}
