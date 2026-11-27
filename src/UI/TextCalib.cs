using System.Drawing;
using GTA.UI;

namespace MidnightClub.UI
{
    /// <summary>Text alignment test card (Settings > Display > Text calibration): sample text for both fonts at several
    /// sizes, each with a red line where the visual middle should be and a green line at the draw position. A
    /// screenshot of it gives the exact per-font offsets.</summary>
    public static class TextCalib
    {
        public static bool On;
        public static void Draw()
        {
            if (!On) return;
            Ds.Rect(0.02f, 0.02f, 0.96f, 0.86f, Color.FromArgb(235, 20, 20, 24));
            float[] scales = { 0.24f, 0.3f, 0.36f, 0.5f, 0.8f, 1.2f };
            for (int f = 0; f < 2; f++)
            {
                var font = f == 0 ? Ds.Display : Ds.Body;
                float x = f == 0 ? 0.05f : 0.52f;
                for (int i = 0; i < scales.Length; i++)
                {
                    float cy = 0.1f + i * 0.13f;
                    Ds.Rect(x, cy, 0.42f, 1f / 1080f, Color.FromArgb(255, 255, 40, 40));          // intended middle
                    Ds.TM("HEXA 0123 " + scales[i].ToString("0.00"), x, cy, scales[i], Color.White, font);
                    float y2 = cy + 0.045f;
                    Ds.Rect(x + 0.22f, y2, 0.2f, 1f / 1080f, Color.FromArgb(255, 40, 255, 40));   // raw draw y
                    Ds.T("HEXA", x + 0.22f, y2, scales[i], Color.FromArgb(255, 160, 200, 255), font);
                }
            }
            Ds.T("TEXT CALIBRATION  ·  take a screenshot (F12 / Print) and send it  ·  turn off in Settings > Display", 0.05f, 0.9f, 0.3f, Color.White);
        }
    }
}
