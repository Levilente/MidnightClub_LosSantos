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
        private sealed class Item { public string Text, Plain; public Color Accent; public int Born, Life; public float Y = -1f, Lines = 1; }
        private static readonly List<Item> items = new List<Item>();
        private static readonly Queue<Item> waiting = new Queue<Item>();
        private const float W = 0.235f, X = 0.015f, Bottom = 0.735f;

        public static void Post(string text, bool longer = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            var accent = Ds.Violet;
            var m = Regex.Match(text, "~([rgbypoc])~");
            if (m.Success) switch (m.Groups[1].Value[0]) { case 'r': accent = Ds.Red; break; case 'g': accent = Ds.Lime; break; case 'b': accent = Ds.Cyan; break; case 'y': case 'o': accent = Ds.Amber; break; case 'c': accent = Ds.Pink; break; }
            var plain = Regex.Replace(text, "~[a-z]~", "");
            if (items.Exists(i => i.Plain == plain) || waiting.Count > 6) return;
            waiting.Enqueue(new Item { Text = text.Replace("~p~", "~HUD_COLOUR_PURPLELIGHT~").Replace("~g~", "~HUD_COLOUR_GREENLIGHT~"), Plain = plain, Accent = accent, Life = longer ? 8000 : 5500 });
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
            float tw = W - Ds.Px(0.03f);
            foreach (var it in items)
            {
                if (it.Lines < 1.5f && it.Y < 0) it.Lines = Math.Max(1, (float)Math.Ceiling(Ds.Width(it.Plain, 0.3f, Ds.Body) / tw));
                float h = 0.018f + it.Lines * 0.024f;
                y -= h + 0.006f;
                it.Y = it.Y < 0 ? y : Ds.Damp(it.Y, y, 14f);
                int age = now - it.Born, left = it.Life - age;
                float a = Math.Min(1f, Math.Min(age / 220f, left / 400f));
                float slide = (1f - Ds.EaseOut(age / 300f)) * Ds.Px(0.05f);
                float x = X - slide;
                Ds.Rect(x, it.Y, W, h, Ds.A(Color.FromArgb(235, 12, 11, 20), a));
                Ds.Rect(x, it.Y, Ds.Px(0.0035f), h, Ds.A(it.Accent, a));
                float k = Math.Min(1f, age / (float)it.Life);
                Ds.Rect(x + Ds.Px(0.0035f), it.Y + h - 0.002f, (W - Ds.Px(0.0035f)) * (1f - k), 0.002f, Ds.A(it.Accent, 0.5f * a));
                Ds.T(it.Text, x + Ds.Px(0.016f), it.Y + 0.008f, 0.3f, Ds.A(Ds.Text, a), Ds.Body, GTA.UI.Alignment.Left, tw);
            }
        }
    }
}
