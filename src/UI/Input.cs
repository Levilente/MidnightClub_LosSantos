using System;
using System.Collections.Generic;
using Keys = System.Windows.Forms.Keys;
using Control = GTA.Control;
using GTA;

namespace MidnightClub.UI
{
    /// <summary>Edge-triggered input that fires exactly once per press, plus hold-to-repeat for menu navigation.
    /// SHVDN Enhanced can tick a script more than once per game frame and IsControlJustPressed stays true for the whole
    /// frame, so presses are de-duplicated per frame. Timing uses the real clock (menus slow the game down).</summary>
    public static class Input
    {
        private static readonly Dictionary<int, int> lastFrame = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> lastTime = new Dictionary<int, int>();
        private static int Now { get { return Environment.TickCount; } }

        public static bool JustPressed(Control c)
        {
            if (!Game.IsControlJustPressed(c)) return false;
            int k = (int)c, f = Game.FrameCount, t = Now, lf, lt;
            if (lastFrame.TryGetValue(k, out lf) && lf == f) return false;
            if (lastTime.TryGetValue(k, out lt) && t - lt < 45) return false;
            lastFrame[k] = f; lastTime[k] = t;
            return true;
        }

        private static readonly Dictionary<int, int> holdStart = new Dictionary<int, int>(), lastRepeat = new Dictionary<int, int>();
        /// <summary>Fires on press, then repeatedly while held (after 320 ms, every 65 ms). For lists and value sliders.</summary>
        public static bool Repeat(Control c)
        {
            int k = (int)c, t = Now;
            if (JustPressed(c)) { holdStart[k] = t; lastRepeat[k] = t; return true; }
            if (!Game.IsControlPressed(c)) { holdStart.Remove(k); return false; }
            int hs, lr;
            if (!holdStart.TryGetValue(k, out hs)) return false;
            if (t - hs < 320) return false;
            lastRepeat.TryGetValue(k, out lr);
            if (t - lr < 65) return false;
            int lf; if (lastFrame.TryGetValue(k, out lf) && lf == Game.FrameCount) return false;
            lastFrame[k] = Game.FrameCount; lastRepeat[k] = t; return true;
        }

        private static readonly Dictionary<int, int> relFrame = new Dictionary<int, int>();
        public static bool JustReleased(Control c)
        {
            if (!Game.IsControlJustReleased(c)) return false;
            int k = (int)c, f = Game.FrameCount, lf;
            if (relFrame.TryGetValue(k, out lf) && lf == f) return false;
            relFrame[k] = f; return true;
        }

        // Tab switching: LB/RB on a pad, Q/E on the keyboard. GTA maps Q/E to the frontend bumpers too, so both sources
        // share one cooldown - one press = one tab, never two.
        private static int tabT; private static bool pq, pe;
        public static int TabDelta()
        {
            bool q = Game.IsKeyPressed(Keys.Q), e = Game.IsKeyPressed(Keys.E);
            int d = 0;
            if (JustPressed(Control.FrontendLb) || (q && !pq)) d = -1;
            else if (JustPressed(Control.FrontendRb) || (e && !pe)) d = 1;
            pq = q; pe = e;
            if (d != 0) { int t = Now; if (t - tabT < 180) return 0; tabT = t; }
            return d;
        }
    }
}
