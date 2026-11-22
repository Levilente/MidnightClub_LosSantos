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
            "Hold ~INPUT_SPRINT~ / ~INPUT_VEH_ROCKET_BOOST~ for nitrous once a workshop has fitted a kit. Hold ~INPUT_NEXT_CAMERA~ on a controller to enter or leave Midnight Club."
        };

        public static void Show(int ms)
        {
            string tip = Tips[new Random().Next(Tips.Length)];
            int end = Game.GameTime + ms;
            while (Game.GameTime < end)
            {
                Function.Call(Hash.DRAW_RECT, 0.5f, 0.5f, 1f, 1f, 4, 3, 10, 255, false);
                Function.Call(Hash.DRAW_RECT, 0.5f, 0.555f, 0.36f, 0.003f, 170, 90, 255, 255, false);
                T("MIDNIGHT CLUB", 0.5f, 0.37f, 1.6f, Color.FromArgb(255, 170, 90, 255), Font.Pricedown);
                T("LOS SANTOS", 0.5f, 0.48f, 0.7f, Color.FromArgb(255, 80, 220, 255), Font.ChaletComprimeCologne);
                T(tip, 0.5f, 0.62f, 0.34f, Color.White, Font.ChaletLondon);
                int dots = (Game.GameTime / 300) % 4;
                T("LOADING" + new string('.', dots), 0.5f, 0.9f, 0.3f, Color.FromArgb(200, 200, 200, 210), Font.ChaletLondon);
                Script.Wait(0);
            }
        }

        private static void T(string s, float x, float y, float scale, Color c, Font f)
        { new TextElement(s, new PointF(x * 1280f, y * 720f), scale, c, f, Alignment.Center, false, true, 0.6f * 1280f).ScaledDraw(); }
    }
}
