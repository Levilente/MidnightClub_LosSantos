using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;

namespace MidnightClub.UI
{
    /// <summary>The game's own animated "shard" banners (GTA Online style): big centred messages for level-ups and
    /// race results, mid-sized ones for discoveries and skill zones. Queued so they never overlap.</summary>
    public static class Shard
    {
        private sealed class Msg { public bool Big; public string Title, Text; public int Colour; public int Ms; }
        private static readonly Queue<Msg> queue = new Queue<Msg>();
        private static Scaleform big, mid; private static Msg cur; private static int shownAt; private static bool animOut;

        // HUD colour indices
        public const int Purple = 21, Gold = 107, Green = 18, Red = 6, Blue = 9, Orange = 15;

        public static void Big(string title, string text, int colour = Purple, int ms = 5000) { queue.Enqueue(new Msg { Big = true, Title = title, Text = text, Colour = colour, Ms = ms }); }
        public static void Mid(string title, string text, int colour = Purple, int ms = 4000) { queue.Enqueue(new Msg { Big = false, Title = title, Text = text, Colour = colour, Ms = ms }); }

        /// <summary>Call every frame.</summary>
        public static void Tick()
        {
            if (cur == null)
            {
                if (queue.Count == 0) return;
                cur = queue.Dequeue(); shownAt = Game.GameTime; animOut = false;
                var sf = Movie(cur.Big);
                if (sf == null || !sf.IsLoaded) { shownAt = -1; return; }
                Show(sf, cur);
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, cur.Big ? "RANK_UP" : "MEDAL_UP", cur.Big ? "HUD_AWARDS" : "HUD_MINI_GAME_SOUNDSET", true);
            }
            var m = Movie(cur.Big);
            if (m == null) { cur = null; return; }
            if (shownAt < 0) { if (!m.IsLoaded) return; Show(m, cur); shownAt = Game.GameTime; }
            int age = Game.GameTime - shownAt;
            if (!animOut && age > cur.Ms - 600) { animOut = true; try { m.CallFunction("SHARD_ANIM_OUT", cur.Colour, 0.33f); } catch { } }
            if (age > cur.Ms) { cur = null; return; }
            m.Render2D();
        }

        private static void Show(Scaleform sf, Msg msg)
        {
            if (msg.Big) sf.CallFunction("SHOW_SHARD_CENTERED_MP_MESSAGE", msg.Title, msg.Text, msg.Colour, 0);
            else sf.CallFunction("SHOW_SHARD_MIDSIZED_MESSAGE", msg.Title, msg.Text, msg.Colour, false, false);
        }

        private static Scaleform Movie(bool isBig)
        {
            try
            {
                if (isBig) { if (big == null) big = Scaleform.RequestMovie("MP_BIG_MESSAGE_FREEMODE"); return big; }
                if (mid == null) mid = Scaleform.RequestMovie("MIDSIZED_MESSAGE"); return mid;
            }
            catch { return null; }
        }

        public static bool Busy { get { return cur != null || queue.Count > 0; } }
    }
}
