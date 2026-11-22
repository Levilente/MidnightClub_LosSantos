using System;
using System.Drawing;
using GTA;
using GTA.Native;
using GTA.UI;
using Font = GTA.UI.Font;

namespace MidnightClub.UI
{
    /// <summary>Title card shown while Midnight Club loads (drawn over the fade).</summary>
    public static class Splash
    {
        private static readonly string[] Tips =
        {
            "Near misses, drafting, air and drifts build your style meter. A full meter powers your special ability.",
            "Police remember your car. A new plate or a respray at LS Customs cools it down.",
            "Car meets run at night and all weekend. Show off your build for REP.",
            "Stopped at a light? Someone might want to race. Press E when they rev.",
            "Every car in the game comes round at a dealership eventually. Or special-order it from the phone.",
            "Drive into a garage you own and the car you arrived in parks there.",
            "Rivals hand over their car after you beat them three times.",
            "Nitrous: Sprint on keyboard, the boost button on a controller, once a workshop has fitted a kit. On a controller, hold Back/Select to enter or leave Midnight Club."
        };

        public static void Show(int ms)
        {
            string tip = Tips[new Random().Next(Tips.Length)];
            int end = Game.GameTime + ms;
            int start = Game.GameTime;
            while (Game.GameTime < end)
            {
                float t = (Game.GameTime - start) / (float)ms, e = Ds.EaseOut(Math.Min(1f, (Game.GameTime - start) / 600f));
                Ds.Rect(0f, 0f, 1f, 1f, Ds.Bg);
                Ds.VGrad(0f, 0.25f, 1f, 0.5f, Ds.A(Ds.Bg, 0f), Ds.A(Color.FromArgb(255, 40, 14, 78), 0.9f), 20);
                Ds.VGrad(0f, 0.5f, 1f, 0.25f, Ds.A(Color.FromArgb(255, 40, 14, 78), 0.9f), Ds.A(Ds.Bg, 0f), 20);
                Ds.Logo(0.5f, 0.38f - (1f - e) * 0.02f, 2.2f, e, Alignment.Center, true);
                Ds.T(tip, 0.5f, 0.64f, 0.32f, Ds.A(Ds.Muted, e), Ds.Body, Alignment.Center, 0.5f);
                float bw = 0.22f;
                Ds.Round(0.5f - bw / 2, 0.88f, bw, 0.004f, 0.002f, Ds.Stroke);
                Ds.Round(0.5f - bw / 2, 0.88f, bw * Ds.EaseInOut(t), 0.004f, 0.002f, Ds.Violet);
                Ds.T("LOADING", 0.5f, 0.892f, 0.24f, Ds.Faint, Ds.Body, Alignment.Center);
                Script.Wait(0);
            }
        }

        private static void T(string s, float x, float y, float scale, Color c, Font f)
        { new TextElement(s, new PointF(x * 1280f, y * 720f), scale, c, f, Alignment.Center, false, true, 0.6f * 1280f).ScaledDraw(); }
    }
}
