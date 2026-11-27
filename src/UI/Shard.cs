using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.UI;
using GTA.Native;

namespace MidnightClub.UI
{
    /// <summary>The game's own animated "shard" banners (GTA Online style): big centred messages for level-ups and
    /// race results, mid-sized ones for discoveries and skill zones. Queued so they never overlap.</summary>
    public static class Shard
    {
        private sealed class Msg { public bool Big; public string Title, Text; public int Colour; public int Ms; }
        private static readonly Queue<Msg> queue = new Queue<Msg>();
        private static Msg cur; private static int shownAt; private static bool animOut;

        // HUD colour indices
        public const int Purple = 21, Gold = 107, Green = 18, Red = 6, Blue = 9, Orange = 15;

        public static void Big(string title, string text, int colour = Purple, int ms = 5000) { queue.Enqueue(new Msg { Big = true, Title = title, Text = text, Colour = colour, Ms = ms }); }
        public static void Mid(string title, string text, int colour = Purple, int ms = 4000) { queue.Enqueue(new Msg { Big = false, Title = title, Text = text, Colour = colour, Ms = ms }); }

        private static Color AccentOf(int c)
        {
            switch (c) { case Gold: case Orange: return Th.Amber; case Green: return Th.Green; case Red: return Th.Red; case Blue: return Th.Cyan; default: return Th.Accent; }
        }

        /// <summary>Call every frame. Banners are Midnight Club glass cards: big (results, level-ups) centred high,
        /// mid-size (discoveries, zones) as a slim card. They slide / fade in and out and are queued.</summary>
        public static void Tick()
        {
            if (cur == null)
            {
                if (queue.Count == 0) return;
                cur = queue.Dequeue(); shownAt = Game.GameTime; animOut = false;
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, cur.Big ? "RANK_UP" : "MEDAL_UP", cur.Big ? "HUD_AWARDS" : "HUD_MINI_GAME_SOUNDSET", true);
            }
            int age = Game.GameTime - shownAt;
            if (age > cur.Ms) { cur = null; return; }
            float ain = Ds.EaseOut(age / 350f), aout = Ds.Clamp01((cur.Ms - age) / 300f), a = Math.Min(ain, aout);
            var acc = AccentOf(cur.Colour);
            string title = (cur.Title ?? "").ToUpperInvariant(), text = System.Text.RegularExpressions.Regex.Replace(cur.Text ?? "", "~[a-zA-Z_]+~", "");
            if (cur.Big)
            {
                float ts = 1.25f, tw = Ds.Width(title, ts, Ds.Display), xw = Math.Min(0.5f, Ds.Width(text, 0.36f));
                float w = Math.Max(tw, xw) + Ds.Px(Th.Px(96f)), h = Th.Px(string.IsNullOrEmpty(text) ? 120f : 160f);
                float x = 0.5f - w / 2f, y = 0.2f - (1f - ain) * Th.Px(30f);
                Kit.Glass(x, y, w, h, Th.R_Lg + 6f, a, true);
                Kit.Pill(0.5f - Ds.Px(Th.Px(40f)), y + Th.Px(14f), Ds.Px(Th.Px(80f)), Th.Px(5f), Ds.A(acc, a));
                Ds.TM(title, 0.5f, y + Th.Px(68f), ts, Ds.A(acc, a), Ds.Display, Alignment.Center);
                if (!string.IsNullOrEmpty(text)) Ds.T(text, 0.5f, y + Th.Px(104f), 0.36f, Ds.A(Th.Text, a), Ds.Body, Alignment.Center, 0.5f);
            }
            else
            {
                float tw = Ds.Width(title, 0.6f, Ds.Display), xw = Ds.Width(text, 0.32f);
                float w = Math.Min(0.5f, Math.Max(tw, xw)) + Ds.Px(Th.Px(64f)), h = Th.Px(string.IsNullOrEmpty(text) ? 64f : 92f);
                float x = 0.5f - w / 2f, y = 0.16f - (1f - ain) * Th.Px(20f);
                Kit.Glass(x, y, w, h, Th.R_Lg, a, true);
                Kit.Pill(x + Ds.Px(Th.Px(14f)), y + Th.Px(16f), Ds.Px(Th.Px(5f)), h - Th.Px(32f), Ds.A(acc, a));
                Ds.TM(title, 0.5f, y + Th.Px(32f), 0.6f, Ds.A(acc, a), Ds.Display, Alignment.Center);
                if (!string.IsNullOrEmpty(text)) Ds.TM(Ds.Fit(text, 0.32f, 0.5f), 0.5f, y + Th.Px(66f), 0.32f, Ds.A(Th.Text, a), Ds.Body, Alignment.Center);
            }
        }

        public static bool Busy { get { return cur != null || queue.Count > 0; } }
    }
}
