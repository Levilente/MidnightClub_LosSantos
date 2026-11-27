using System;
using System.Collections.Generic;
using System.Drawing;
using GTA.Math;
using GTA.Native;

namespace MidnightClub.UI
{
    /// <summary>Car pictures for menu tiles: the in-game website (Legendary Motorsport, Southern San Andreas Super Autos,
    /// Benny's ...) texture dictionaries hold a thumbnail per model. The dictionary that has a model is found lazily,
    /// one dictionary per frame, and cached; models without a picture fall back to the tile's icon.</summary>
    public static class CarThumbs
    {
        private static readonly string[] Dicts =
        {
            "lgm_default", "sssa_default", "candc_default", "elt_default", "dock_default", "pandm_default",
            "lgm_dlc_luxe", "lgm_dlc_executive1", "lgm_dlc_stunt", "lgm_dlc_specialraces", "lgm_dlc_importexport", "lgm_dlc_gunrunning",
            "lgm_dlc_smuggler", "lgm_dlc_xmas2017", "lgm_dlc_assault", "lgm_dlc_battle", "lgm_dlc_arena", "lgm_dlc_casino", "lgm_dlc_casinoheist",
            "lgm_dlc_summer2020", "lgm_dlc_heist4", "lgm_dlc_tuner", "lgm_dlc_security", "lgm_dlc_sum2", "lgm_dlc_mpchristmas3", "lgm_dlc_2023_01",
            "lgm_dlc_mp2023_01", "lgm_dlc_mp2023_02", "lgm_dlc_mp2024_01", "lgm_dlc_mp2024_02", "lgm_dlc_apartments", "lgm_dlc_business", "lgm_dlc_business2",
            "lgm_dlc_lts_creator", "lgm_dlc_pilot", "lgm_dlc_valentines", "lgm_dlc_valentines2", "lgm_dlc_biker", "lgm_dlc_vinewood",
            "sssa_dlc_business", "sssa_dlc_business2", "sssa_dlc_christmas_2", "sssa_dlc_christmas_3", "sssa_dlc_executive_1", "sssa_dlc_halloween",
            "sssa_dlc_heist", "sssa_dlc_hipster", "sssa_dlc_independence", "sssa_dlc_lts_creator", "sssa_dlc_mp_to_sp", "sssa_dlc_stunt",
            "sssa_dlc_valentines", "sssa_dlc_biker", "sssa_dlc_assault", "sssa_dlc_battle", "sssa_dlc_arena", "sssa_dlc_casino", "sssa_dlc_heist4",
            "sssa_dlc_tuner", "sssa_dlc_security", "sssa_dlc_sum2", "sssa_dlc_mpchristmas3", "sssa_dlc_mp2023_01", "sssa_dlc_mp2023_02",
            "lsc_default", "lsc_dlc_import_export", "lsc_lowrider2", "lsc_jan2016", "lsc_dlc_switch_ii",
        };
        private static readonly Dictionary<string, string> found = new Dictionary<string, string>();
        private static readonly HashSet<string> missing = new HashSet<string>();
        private static string searching; private static int idx; private static int frames;

        /// <summary>Draws the model's picture into the box (top-left x, y; screen fractions). Returns false while unknown.</summary>
        public static bool Draw(string model, float x, float y, float w, float h, Color tint)
        {
            if (string.IsNullOrEmpty(model)) return false;
            model = model.ToLowerInvariant();
            string dict;
            if (found.TryGetValue(model, out dict))
            {
                Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, dict, false);
                if (!Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, dict)) return false;
                Function.Call(Hash.DRAW_SPRITE, dict, model, x + w / 2f, y + h / 2f, w, h, 0f, tint.R, tint.G, tint.B, tint.A, false, 0);
                return true;
            }
            if (missing.Contains(model)) return false;
            if (searching == null) { searching = model; idx = 0; frames = 0; }
            if (searching != model) return false;
            try { Search(); } catch { missing.Add(model); searching = null; }
            return false;
        }

        private static void Search()
        {
            if (idx >= Dicts.Length) { missing.Add(searching); searching = null; return; }
            string d = Dicts[idx];
            Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, d, false);
            if (!Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, d))
            {
                if (++frames > 20) { frames = 0; idx++; }        // dictionary not in this game build
                return;
            }
            frames = 0;
            var res = Function.Call<Vector3>(Hash.GET_TEXTURE_RESOLUTION, d, searching);
            if (res.X > 4f) { found[searching] = d; searching = null; return; }
            Function.Call(Hash.SET_STREAMED_TEXTURE_DICT_AS_NO_LONGER_NEEDED, d);
            idx++;
        }
    }
}
