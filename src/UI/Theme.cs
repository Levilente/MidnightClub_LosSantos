using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Native;
using GTA.UI;
using Font = GTA.UI.Font;

namespace MidnightClub.UI
{
    /// <summary>Midnight Club visual language: one palette, one set of fonts, the game's own menu textures.
    /// All coordinates are 0..1 screen fractions.</summary>
    public static class Theme
    {
        public static readonly Color Purple = Color.FromArgb(255, 168, 85, 247);
        public static readonly Color PurpleDeep = Color.FromArgb(255, 76, 29, 149);
        public static readonly Color Cyan = Color.FromArgb(255, 34, 211, 238);
        public static readonly Color Panel = Color.FromArgb(225, 10, 8, 20);
        public static readonly Color PanelSoft = Color.FromArgb(170, 10, 8, 20);
        public static readonly Color Line = Color.FromArgb(70, 255, 255, 255);
        public static readonly Color TextDim = Color.FromArgb(255, 170, 170, 185);
        public static readonly Color Gold = Color.FromArgb(255, 251, 191, 36);
        private static bool texturesRequested;

        public static void Rect(float x, float y, float w, float h, Color c)
        { Ds.Rect(x, y, w, h, c); }

        /// <summary>Game texture (e.g. commonmenu/gradient_bgd). Falls back to a plain rect until streamed in.</summary>
        public static void Sprite(string dict, string name, float x, float y, float w, float h, Color c, float rot = 0f)
        {
            if (!texturesRequested) { Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, "commonmenu", false); Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, "mpleaderboard", false); texturesRequested = true; }
            if (!Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, dict)) { Rect(x, y, w, h, c); return; }
            Function.Call(Hash.DRAW_SPRITE, dict, name, x + w / 2f, y + h / 2f, w, h, rot, c.R, c.G, c.B, c.A, false, 0);
        }

        public static void Text(string s, float x, float y, float scale, Color c, Font f = Font.ChaletLondon, Alignment a = Alignment.Left, float wrap = 0f, bool shadow = false)
        { new TextElement(s ?? "", new PointF(x * 1280f, y * 720f), scale, c, f, a, shadow, false, wrap * 1280f).ScaledDraw(); }

        public static float TextWidth(string s, float scale, Font f = Font.ChaletLondon)
        {
            Function.Call(Hash.BEGIN_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, "STRING");
            Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, s ?? "");
            Function.Call(Hash.SET_TEXT_FONT, (int)f);
            Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
            return Function.Call<float>(Hash.END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, true);
        }

        /// <summary>Panel with a soft vertical gradient and a neon accent on the left.</summary>
        public static void Card(float x, float y, float w, float h, bool accent = true)
        {
            Sprite("commonmenu", "gradient_bgd", x, y, w, h, Color.FromArgb(235, 255, 255, 255));
            Rect(x, y, w, h, Color.FromArgb(120, 8, 6, 18));
            if (accent) Rect(x, y, 0.0035f, h, Purple);
        }

        public static void StatBar(string label, float v, float x, float y, float w)
        {
            v = v < 0.03f ? 0.03f : v > 1f ? 1f : v;
            Text(label, x, y, 0.26f, TextDim);
            float bx = x + w * 0.36f, bw = w * 0.64f;
            Rect(bx, y + 0.009f, bw, 0.008f, Color.FromArgb(90, 255, 255, 255));
            Rect(bx, y + 0.009f, bw * v, 0.008f, Purple);
        }

        /// <summary>Call every frame while a full-screen Midnight Club screen is up.</summary>
        public static void FullscreenFrame()
        {
            Function.Call(Hash.THEFEED_HIDE_THIS_FRAME);
            Hud.HideComponentThisFrame(HudComponent.HelpText);
            Hud.HideComponentThisFrame(HudComponent.VehicleName);
            Hud.HideComponentThisFrame(HudComponent.AreaName);
            Hud.HideComponentThisFrame(HudComponent.StreetName);
        }
    }

    /// <summary>The game's own instructional-buttons bar (bottom right) with real key/pad glyphs.</summary>
    public static class Buttons
    {
        private static Scaleform sf;
        private static string lastKey;

        public static void Draw(params object[] pairs)   // Control, label, Control, label, ...
        {
            if (sf == null) sf = Scaleform.RequestMovie("instructional_buttons");
            if (!sf.IsLoaded) return;
            var key = (Game.LastInputMethod == InputMethod.GamePad ? "p" : "k") + string.Join("|", pairs.Select(p => p.ToString()));
            if (key != lastKey)
            {
                lastKey = key;
                sf.CallFunction("CLEAR_ALL");
                sf.CallFunction("SET_CLEAR_SPACE", 200);
                for (int i = 0, slot = 0; i + 1 < pairs.Length; i += 2, slot++)
                {
                    var c = (Control)pairs[i];
                    string glyph = Function.Call<string>(Hash.GET_CONTROL_INSTRUCTIONAL_BUTTONS_STRING, 2, (int)c, true);
                    sf.CallFunction("SET_DATA_SLOT", slot, glyph, (string)pairs[i + 1]);
                }
                sf.CallFunction("SET_BACKGROUND_COLOUR", 0, 0, 0, 90);
                sf.CallFunction("DRAW_INSTRUCTIONAL_BUTTONS", -1);
            }
            sf.Render2D();
        }
    }
}
