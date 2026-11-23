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
                int sel = 0; float ang = 200f, selY = -1f; int start = Game.GameTime;
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
                    Studio.Fade(false, 0.2f, A(200));
                    Studio.Fade(true, 0.25f, A(220));
                    Ds.HGrad(0f, 0f, 0.45f, 1f, Ds.A(Ds.Bg, 0.75f * fade), Ds.A(Ds.Bg, 0f), 18);
                    Ds.Logo(0.04f, 0.03f, 1.0f, fade);
                    string tier = Career.CareerManager.Instance != null ? "TIER " + Career.CareerManager.Instance.Tier + "  ·  " + Career.CareerManager.Instance.TierName.ToUpperInvariant() : "";
                    Ds.T(World.CurrentTimeOfDay.ToString(@"hh\:mm"), 0.96f, 0.03f, 0.75f, Ds.A(Ds.Text, fade), Ds.Display, Alignment.Right);
                    Ds.T(dayName.ToUpperInvariant(), 0.96f, 0.085f, 0.25f, Ds.A(Ds.Muted, fade), Ds.Body, Alignment.Right);

                    // menu (left)
                    float mx = 0.04f, my = 0.36f, rowH = 0.072f;
                    selY = selY < 0 ? my + sel * rowH : Ds.Damp(selY, my + sel * rowH, 22f);
                    Ds.Streaks(0f, 0.3f, 0.45f, 0.4f, Ds.Violet, 0.5f * fade, 16, 0.5f);
                    Ds.Skew(mx - Ds.Px(0.014f), selY, 0.3f, 0.06f, Ds.SlantFor(0.06f), Ds.A(Ds.Violet, fade));
                    Ds.Shine(mx - Ds.Px(0.014f), selY, 0.3f, 0.06f, Ds.SlantFor(0.06f), 2.4f, fade);
                    for (int i = 0; i < Items.Length; i++)
                    {
                        float y = my + i * rowH, ent = Ds.EaseOut(Ds.Clamp01((Game.GameTime - start) / 450f - i * 0.12f));
                        Ds.T(Items[i], mx + (1f - ent) * -0.02f, y + 0.008f, 0.62f, Ds.A(i == sel ? Color.White : Ds.Muted, ent), Ds.Display);
                    }
                    Ds.T(Hints[sel], mx, my + Items.Length * rowH + 0.012f, 0.28f, Ds.A(Ds.Muted, fade), Ds.Body, Alignment.Left, 0.28f);

                    // career card (right)
                    float cx = 0.68f, cy = 0.2f, cw = 0.28f, ca = Ds.EaseOut(Ds.Clamp01((Game.GameTime - start - 150) / 500f)), co = (1f - ca) * 0.02f;
                    cx += co;
                    Ds.Round(cx, cy, cw, 0.2f, 0.018f, Ds.A(Ds.Surface, 0.94f * ca));
                    Ds.Rect(cx, cy, Ds.Px(0.003f), 0.2f, Ds.A(Ds.Violet, ca));
                    Ds.Tag("YOUR CAREER", cx + Ds.Px(0.02f), cy + 0.016f, Ds.Violet, Ds.Text, 0.24f, ca);
                    Ds.T("$" + d.Cash.ToString("N0"), cx + Ds.Px(0.02f), cy + 0.038f, 0.95f, Ds.A(Ds.Text, ca), Ds.Display);
                    Ds.T(d.Rep.ToString("N0") + " REP", cx + Ds.Px(0.02f), cy + 0.112f, 0.42f, Ds.A(Ds.Violet, ca), Ds.Display);
                    Ds.T(tier, cx + cw - Ds.Px(0.02f), cy + 0.118f, 0.25f, Ds.A(Ds.Muted, ca), Ds.Body, Alignment.Right);
                    Ds.Rect(cx + Ds.Px(0.02f), cy + 0.152f, cw - Ds.Px(0.04f), 0.0015f, Ds.A(Ds.Stroke, ca));
                    Ds.T(d.Vehicles.Count + " CARS  ·  " + d.Garages.Count + " GARAGES  ·  " + d.RaceHistory.Count + " RACES", cx + Ds.Px(0.02f), cy + 0.162f, 0.25f, Ds.A(Ds.Muted, ca));
                    var cur = car != null && car.Exists() ? Game.GetLocalizedString(Function.Call<string>(Hash.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL, car.Model.Hash)) : null;
                    float ny = cy + 0.215f;
                    if (Gameplay.Objectives.CurrentText != null)
                    {
                        Ds.Round(cx, ny, cw, 0.072f, 0.016f, Ds.A(Ds.Surface, 0.94f * ca));
                        Ds.Rect(cx, ny + 0.016f, Ds.Px(0.003f), 0.04f, Ds.A(Ds.Violet, ca));
                        Ds.T("NEXT OBJECTIVE", cx + Ds.Px(0.02f), ny + 0.012f, 0.23f, Ds.A(Ds.Violet, ca));
                        Ds.T(Ds.Fit(Gameplay.Objectives.CurrentText, 0.32f, cw - Ds.Px(0.04f)), cx + Ds.Px(0.02f), ny + 0.033f, 0.32f, Ds.A(Ds.Text, ca));
                        ny += 0.085f;
                    }
                    int unread = d.Messages.Count(m => !m.Read);
                    var news = d.News.LastOrDefault();
                    if (news != null || unread > 0)
                    {
                        Ds.Round(cx, ny, cw, 0.072f, 0.016f, Ds.A(Ds.Surface, 0.94f * ca));
                        Ds.Rect(cx, ny + 0.016f, Ds.Px(0.003f), 0.04f, Ds.A(unread > 0 ? Ds.Cyan : Ds.Pink, ca));
                        Ds.T(unread > 0 ? "PHONE  ·  " + unread + " UNREAD" : "STREET BUZZ", cx + Ds.Px(0.02f), ny + 0.012f, 0.23f, Ds.A(unread > 0 ? Ds.Cyan : Ds.Pink, ca));
                        string line = unread > 0 ? d.Messages.Last(m => !m.Read).From + ": " + d.Messages.Last(m => !m.Read).Subject : news.Headline;
                        Ds.T(Ds.Fit(line, 0.32f, cw - Ds.Px(0.04f)), cx + Ds.Px(0.02f), ny + 0.033f, 0.32f, Ds.A(Ds.Text, ca));
                    }
                    if (!string.IsNullOrEmpty(cur))
                    {
                        Ds.T("NOW DRIVING", 0.04f, 0.84f, 0.24f, Ds.A(Ds.Muted, fade));
                        Ds.T(cur.ToUpperInvariant(), 0.04f, 0.858f, 0.8f, Ds.A(Ds.Text, fade), Ds.Display);
                    }

                    Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Hit the streets", Control.FrontendDown, "Navigate");

                    if (UI.Input.JustPressed(Control.FrontendDown)) { sel = (sel + 1) % Items.Length; Sound("NAV_UP_DOWN"); }
                    if (UI.Input.JustPressed(Control.FrontendUp)) { sel = (sel + Items.Length - 1) % Items.Length; Sound("NAV_UP_DOWN"); }
                    if (UI.Input.JustPressed(Control.FrontendCancel)) { Sound("BACK"); return Choice.Streets; }
                    if (UI.Input.JustPressed(Control.FrontendAccept)) { Sound("SELECT"); return (Choice)sel; }
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
