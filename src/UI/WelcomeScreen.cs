using System;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Persistence;
using Control = GTA.Control;
using Font = GTA.UI.Font;

namespace MidnightClub.UI
{
    /// <summary>Front-end shown each time you enter Midnight Club: orbit shot of your car, career card, news and a main menu.</summary>
    public static class WelcomeScreen
    {
        public enum Choice { Streets, Menu, Phone, Story }

        private static readonly string[] Items = { "HIT THE STREETS", "CAREER & GARAGE", "PHONE & MESSAGES", "BACK TO STORY MODE" };
        private static readonly string[] Hints =
        {
            "Drive. Races, rivals, meets and the police are out there.",
            "Career, your cars, garages, dealerships, finances and settings.",
            "Messages, news, rivals, clubs, special orders and your ability.",
            "Save and return to Story Mode."
        };

        public static Choice Show(SaveData d, string dayName)
        {
            var ped = Game.Player.Character; var car = ped.CurrentVehicle;
            Camera cam = null;
            try
            {
                if (car != null && car.Exists())
                {
                    cam = Camera.Create(ScriptedCameraNameHash.DefaultScriptedCamera, true);
                    cam.FieldOfView = 42f;
                    World.RenderingCamera = cam;
                }
                int sel = 0; float ang = 200f; int start = Game.GameTime;
                Wait(); // swallow the key that opened us
                while (true)
                {
                    Game.DisableAllControlsThisFrame();
                    Theme.FullscreenFrame();
                    Hud.HideComponentThisFrame(HudComponent.WantedStars);
                    Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
                    if (cam != null && car.Exists())
                    {
                        ang += 0.012f * Game.LastFrameTime * 1000f / 16f;
                        float r = Math.Max(5.5f, car.Model.Dimensions.frontTopRight.Y * 2.4f), rad = ang * (float)Math.PI / 180f;
                        cam.Position = car.Position + new Vector3((float)Math.Cos(rad) * r, (float)Math.Sin(rad) * r, 1.1f);
                        cam.PointAt(car.Position + new Vector3(0, 0, 0.35f));
                    }
                    float fade = Math.Min(1f, (Game.GameTime - start) / 500f);
                    Func<int, int> A = a => (int)(a * fade);
                    // vignette bars
                    Theme.Rect(0f, 0f, 1f, 0.12f, Color.FromArgb(A(200), 4, 3, 10));
                    Theme.Rect(0f, 0.88f, 1f, 0.12f, Color.FromArgb(A(200), 4, 3, 10));
                    Theme.Rect(0f, 0.12f, 1f, 0.002f, Color.FromArgb(A(255), Theme.Purple));
                    Theme.Text("MIDNIGHT CLUB", 0.04f, 0.022f, 1.0f, Color.FromArgb(A(255), Theme.Purple), Font.Pricedown);
                    Theme.Text("LOS SANTOS", 0.235f, 0.058f, 0.5f, Color.FromArgb(A(255), Theme.Cyan), Font.ChaletComprimeCologne);
                    string tier = Career.CareerManager.Instance != null ? "TIER " + Career.CareerManager.Instance.Tier + "  ·  " + Career.CareerManager.Instance.TierName.ToUpperInvariant() : "";
                    Theme.Text(dayName.ToUpperInvariant() + "  ·  " + World.CurrentTimeOfDay.ToString(@"hh\:mm"), 0.96f, 0.035f, 0.34f, Color.White, Font.ChaletLondon, Alignment.Right);
                    Theme.Text(tier, 0.96f, 0.065f, 0.3f, Theme.Cyan, Font.ChaletLondon, Alignment.Right);

                    // menu (left)
                    float mx = 0.04f, my = 0.34f;
                    for (int i = 0; i < Items.Length; i++)
                    {
                        bool s = i == sel; float y = my + i * 0.07f;
                        if (s)
                        {
                            Theme.Sprite("commonmenu", "gradient_nav", mx, y, 0.28f, 0.058f, Color.FromArgb(A(230), Theme.Purple));
                            Theme.Rect(mx, y, 0.004f, 0.058f, Color.White);
                        }
                        else Theme.Rect(mx, y, 0.28f, 0.058f, Color.FromArgb(A(150), 8, 6, 18));
                        Theme.Text(Items[i], mx + 0.016f, y + 0.009f, 0.55f, s ? Color.White : Color.FromArgb(A(220), Theme.TextDim), Font.ChaletComprimeCologne);
                    }
                    Theme.Text(Hints[sel], mx, my + Items.Length * 0.07f + 0.01f, 0.28f, Theme.TextDim, Font.ChaletLondon, Alignment.Left, 0.28f);

                    // career card (right)
                    float cx = 0.68f, cy = 0.2f, cw = 0.28f;
                    Theme.Card(cx, cy, cw, 0.2f);
                    Theme.Text("YOUR CAREER", cx + 0.014f, cy + 0.01f, 0.3f, Theme.Purple);
                    Theme.Text("$" + d.Cash.ToString("N0"), cx + 0.014f, cy + 0.04f, 0.75f, Color.White, Font.ChaletComprimeCologne);
                    Theme.Text(d.Rep.ToString("N0") + " REP", cx + 0.014f, cy + 0.1f, 0.4f, Theme.Cyan, Font.ChaletComprimeCologne);
                    Theme.Text(d.Vehicles.Count + " CARS  ·  " + d.Garages.Count + " GARAGES  ·  " + d.RaceHistory.Count + " RACES", cx + 0.014f, cy + 0.145f, 0.27f, Theme.TextDim);
                    var cur = car != null && car.Exists() ? Game.GetLocalizedString(Function.Call<string>(Hash.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL, car.Model.Hash)) : null;
                    if (!string.IsNullOrEmpty(cur)) Theme.Text("DRIVING  " + cur.ToUpperInvariant(), cx + 0.014f, cy + 0.17f, 0.27f, Color.White);

                    float ny = cy + 0.22f;
                    if (Gameplay.Objectives.CurrentText != null)
                    {
                        Theme.Card(cx, ny, cw, 0.07f);
                        Theme.Text("NEXT OBJECTIVE", cx + 0.014f, ny + 0.008f, 0.26f, Theme.Purple);
                        Theme.Text(Gameplay.Objectives.CurrentText, cx + 0.014f, ny + 0.03f, 0.3f, Color.White, Font.ChaletLondon, Alignment.Left, cw - 0.03f);
                        ny += 0.08f;
                    }
                    int unread = d.Messages.Count(m => !m.Read);
                    var news = d.News.LastOrDefault();
                    if (news != null || unread > 0)
                    {
                        Theme.Card(cx, ny, cw, 0.09f);
                        Theme.Text(unread > 0 ? "PHONE  ·  " + unread + " UNREAD" : "STREET NEWS", cx + 0.014f, ny + 0.008f, 0.26f, Theme.Cyan);
                        string line = unread > 0 ? d.Messages.Last(m => !m.Read).From + ": " + d.Messages.Last(m => !m.Read).Subject : news.Headline;
                        Theme.Text(line, cx + 0.014f, ny + 0.03f, 0.29f, Color.White, Font.ChaletLondon, Alignment.Left, cw - 0.03f);
                    }

                    Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Hit the streets", Control.FrontendDown, "Navigate");

                    if (Game.IsControlJustPressed(Control.FrontendDown)) { sel = (sel + 1) % Items.Length; Sound("NAV_UP_DOWN"); }
                    if (Game.IsControlJustPressed(Control.FrontendUp)) { sel = (sel + Items.Length - 1) % Items.Length; Sound("NAV_UP_DOWN"); }
                    if (Game.IsControlJustPressed(Control.FrontendCancel)) { Sound("BACK"); return Choice.Streets; }
                    if (Game.IsControlJustPressed(Control.FrontendAccept)) { Sound("SELECT"); return (Choice)sel; }
                    Script.Wait(0);
                }
            }
            finally
            {
                if (cam != null) { World.RenderingCamera = null; cam.Delete(); }
            }
        }

        private static void Wait() { int t = Game.GameTime + 250; while (Game.GameTime < t) { Game.DisableAllControlsThisFrame(); Script.Wait(0); } }
        private static void Sound(string n) { Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, n, "HUD_FRONTEND_DEFAULT_SOUNDSET", true); }
    }
}
