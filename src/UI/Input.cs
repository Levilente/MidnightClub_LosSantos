using System.Collections.Generic;
using GTA;

namespace MidnightClub.UI
{
    /// <summary>Edge-triggered input that fires exactly once per press. SHVDN Enhanced can tick a script more than once
    /// per game frame, and IsControlJustPressed stays true for the whole frame, which made every menu input double.</summary>
    public static class Input
    {
        private static readonly Dictionary<int, int> lastFrame = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> lastTime = new Dictionary<int, int>();

        public static bool JustPressed(Control c)
        {
            if (!Game.IsControlJustPressed(c)) return false;
            int k = (int)c, f = Game.FrameCount, t = Game.GameTime, lf, lt;
            if (lastFrame.TryGetValue(k, out lf) && lf == f) return false;
            if (lastTime.TryGetValue(k, out lt) && t - lt < 70) return false;
            lastFrame[k] = f; lastTime[k] = t;
            return true;
        }

        private static readonly Dictionary<int, int> relFrame = new Dictionary<int, int>();
        public static bool JustReleased(Control c)
        {
            if (!Game.IsControlJustReleased(c)) return false;
            int k = (int)c, f = Game.FrameCount, lf;
            if (relFrame.TryGetValue(k, out lf) && lf == f) return false;
            relFrame[k] = f; return true;
        }
    }
}
