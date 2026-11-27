using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using GTA;
using GTA.Native;

namespace MidnightClub.UI
{
    /// <summary>Midnight Club's own notification stack (replaces GTA's feed): slim dark cards above the minimap with
    /// an accent edge picked from the message's first colour token, slide-in / fade-out, up to four at a time.</summary>
    public static class Toast
    {
        private sealed class Item { public string Text, Plain, Icon = "sparkles"; public Color Accent; public int Born, Life; public float Y = -1f, Lines = 1; }
        private static readonly List<Item> items = new List<Item>();
        private static readonly Queue<Item> waiting = new Queue<Item>();
        private const float W = 0.25f, X = 0.015f, Bottom = 0.745f;

        public static void Post(string text, bool longer = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            var accent = Ds.Violet;
            var m = Regex.Match(text, "~([rgbypoc])~");
            string icon = "mail";
            if (m.Success) switch (m.Groups[1].Value[0]) { case 'r': accent = Ds.Red; icon = "siren"; break; case 'g': accent = Ds.Lime; icon = "circle-dollar-sign"; break; case 'b': accent = Ds.Cyan; icon = "map"; break; case 'y': case 'o': accent = Ds.Amber; icon = "star"; break; case 'c': accent = Ds.Pink; icon = "sparkles"; break; }
            var plain = Regex.Replace(text, "~[a-z]~", "");
            if (items.Exists(i => i.Plain == plain) || waiting.Count > 6) return;
            waiting.Enqueue(new Item { Text = text.Replace("~p~", "~HUD_COLOUR_PURPLELIGHT~").Replace("~g~", "~HUD_COLOUR_GREENLIGHT~"), Plain = plain, Accent = accent, Icon = icon, Life = longer ? 8000 : 5500 });
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Text_Arrive_Tone", "Phone_SoundSet_Default", true);
        }

        /// <summary>Call every frame.</summary>
        public static void Tick()
        {
            int now = Game.GameTime;
            while (waiting.Count > 0 && items.Count < 4) { var it = waiting.Dequeue(); it.Born = now; items.Insert(0, it); }
            items.RemoveAll(i => now - i.Born > i.Life);
            if (items.Count == 0) return;
            bool hud = !Function.Call<bool>(Hash.IS_RADAR_HIDDEN);
            float y = hud ? Bottom : 0.95f;
            float tw = W - Ds.Px(Th.Px(84f));
            foreach (var it in items)
            {
                if (it.Lines < 1.5f && it.Y < 0) it.Lines = Math.Max(1, (float)Math.Ceiling(Ds.Width(it.Plain, 0.3f, Ds.Body) / tw));
                float h = Th.Px(26f) + it.Lines * Th.Px(30f);
                if (h < Th.Px(60f)) h = Th.Px(60f);
                y -= h + Th.Px(8f);
                it.Y = it.Y < 0 ? y : Ds.Damp(it.Y, y, 14f);
                int age = now - it.Born, left = it.Life - age;
                float a = Math.Min(1f, Math.Min(age / 200f, left / 350f));
                float slide = (1f - Ds.EaseOut(age / 280f)) * Ds.Px(0.04f);
                float x = X - slide;
                Kit.Glass(x, it.Y, W, h, Th.R_Lg, a, true);
                float bs = Th.Px(36f), bcx = x + Ds.Px(Th.Px(16f)) + Ds.Px(bs / 2f), bcy = it.Y + h / 2f;
                Kit.Round(bcx - Ds.Px(bs / 2f), bcy - bs / 2f, Ds.Px(bs), bs, 10f, Ds.A(it.Accent, a));
                Textures.Draw("li_" + it.Icon, bcx, bcy, bs * 0.58f, Ds.A(Th.On(it.Accent), a));
                float k = Math.Min(1f, age / (float)it.Life);
                Kit.Pill(x + Ds.Px(Th.Px(66f)), it.Y + h - Th.Px(7f), (W - Ds.Px(Th.Px(82f))) * (1f - k), Th.Px(3f), Ds.A(it.Accent, 0.6f * a));
                Ds.T(it.Plain, x + Ds.Px(Th.Px(66f)), it.Y + Th.Px(13f), 0.3f, Ds.A(Th.Text, a), Ds.Body, GTA.UI.Alignment.Left, tw);
            }
        }
    }
}
