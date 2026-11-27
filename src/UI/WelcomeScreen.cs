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

        private static readonly string[] Items = { "HIT THE STREETS", "CAREER & GARAGE", "MIDNIGHT HUB", "EXIT" };
        private static readonly string[] Hints =
        {
            "Drive. Races, rivals, meets and the police are out there.",
            "Career, your cars, garages, dealerships, finances and settings.",
            "Races, garage, dealers, career, social, daily challenges and settings.",
            "Exit to Story Mode or quit the game. Progress is saved."
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
                if (Screen.IsFadedOut || Screen.IsFadingOut) Screen.FadeIn(600);     // entering Midnight Club: straight from black into the welcome screen
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
                    // soft scrim on the left, glass cards
                    Studio.Fade(true, 0.25f, A(180));
                    Ds.HGrad(0f, 0f, 0.5f, 1f, Ds.A(Ds.Bg, 0.6f * fade), Ds.A(Ds.Bg, 0f), 8);
                    Ds.Logo(0.045f, 0.045f, 1.0f, fade);
                    string tier = Career.CareerManager.Instance != null ? "TIER " + Career.CareerManager.Instance.Tier + "  ·  " + Career.CareerManager.Instance.TierName.ToUpperInvariant() : "";
                    // clock capsule
                    string clock = World.CurrentTimeOfDay.ToString(@"hh\:mm");
                    float clw = Ds.Width(clock, 0.6f, Ds.Display) + Ds.Width(dayName.ToUpperInvariant(), 0.26f, Ds.Display) + Ds.Px(Th.Px(60f)), clh = Th.Px(54f);
                    Kit.Glass(0.955f - clw, 0.045f, clw, clh, clh * 1080f / 2f, fade);
                    Ds.TM(clock, 0.955f - clw + Ds.Px(Th.Px(22f)), 0.045f + clh / 2f, 0.6f, Ds.A(Th.Text, fade), Ds.Display);
                    Ds.TM(dayName.ToUpperInvariant(), 0.955f - Ds.Px(Th.Px(22f)), 0.045f + clh / 2f, 0.26f, Ds.A(Th.Sub, fade), Ds.Display, Alignment.Right);

                    // menu card (left)
                    float mx = 0.045f, mw = Ds.Px(Th.Px(520f)), my = 0.33f, rowH = Th.Px(66f), pad = Th.Px(12f);
                    Kit.Glass(mx, my, mw, Items.Length * rowH + 2 * pad, Th.R_Lg + 4f, fade);
                    float sy = my + pad + sel * rowH;
                    selY = selY < 0 ? sy : Ds.Damp(selY, sy, 28f);
                    Kit.Round(mx + Ds.Px(pad), selY, mw - 2 * Ds.Px(pad), rowH - Th.Px(6f), Th.R_Md + 2f, Ds.A(Th.Accent, fade));
                    string[] icons = { "flag", "trophy", "layers", "log-out" };
                    for (int i = 0; i < Items.Length; i++)
                    {
                        float y = my + pad + i * rowH, cyy = y + (rowH - Th.Px(6f)) / 2f, ent = Ds.EaseOut(Ds.Clamp01((Game.GameTime - start) / 450f - i * 0.12f));
                        bool on = i == sel && Math.Abs(selY - sy) < rowH * 0.5f;
                        var fg = on ? Th.On(Th.Accent) : Th.Text;
                        Textures.Draw("li_" + icons[Math.Min(i, icons.Length - 1)], mx + Ds.Px(Th.Px(40f)), cyy, Th.Px(26f), Ds.A(on ? fg : Th.Accent, ent * fade));
                        Ds.TM(Items[i], mx + Ds.Px(Th.Px(70f)) + (1f - ent) * -0.02f, cyy, 0.56f, Ds.A(fg, ent * fade), Ds.Display);
                    }
                    float hy = my + Items.Length * rowH + 2 * pad + Th.Px(12f);
                    Ds.T(Hints[sel], mx + Ds.Px(Th.Px(8f)), hy, 0.3f, Ds.A(Th.Sub, fade), Ds.Body, Alignment.Left, mw);

                    // career cards (right)
                    float cw = Ds.Px(Th.Px(560f)), cx = 0.955f - cw, cy = 0.16f, ca = Ds.EaseOut(Ds.Clamp01((Game.GameTime - start - 150) / 500f)), co = (1f - ca) * 0.02f;
                    cx += co;
                    float ix = cx + Ds.Px(Th.Px(26f)), iw = cw - Ds.Px(Th.Px(52f));
                    float h1 = Th.Px(200f);
                    Kit.Glass(cx, cy, cw, h1, Th.R_Lg + 4f, ca);
                    Kit.Chip("YOUR CAREER", ix, cy + Th.Px(30f), Ds.A(Th.Accent, ca), Ds.A(Th.On(Th.Accent), ca), 0.24f);
                    Ds.TM("$" + d.Cash.ToString("N0"), ix, cy + Th.Px(86f), 1.0f, Ds.A(Th.Text, ca), Ds.Display);
                    Ds.TM(d.Rep.ToString("N0") + " REP", ix, cy + Th.Px(130f), 0.44f, Ds.A(Th.Accent, ca), Ds.Display);
                    Ds.TM(tier, ix + iw, cy + Th.Px(130f), 0.26f, Ds.A(Th.Sub, ca), Ds.Display, Alignment.Right);
                    Ds.Rect(ix, cy + Th.Px(152f), iw, Th.Px(1f), Ds.A(Th.Line, ca));
                    Ds.TM(d.Vehicles.Count + " CARS  ·  " + d.Garages.Count + " GARAGES  ·  " + d.RaceHistory.Count + " RACES", ix, cy + Th.Px(176f), 0.26f, Ds.A(Th.Sub, ca), Ds.Display);
                    var cur = car != null && car.Exists() ? Game.GetLocalizedString(Function.Call<string>(Hash.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL, car.Model.Hash)) : null;
                    float ny = cy + h1 + Th.Px(14f);
                    Action<string, Color, string, string, string, float> card = (icon, col, head, line, foot, hgt) =>
                    {
                        Kit.Glass(cx, ny, cw, hgt, Th.R_Lg, ca);
                        float bs = Th.Px(44f), bcx = cx + Ds.Px(Th.Px(22f)) + Ds.Px(bs / 2f), bcy = ny + hgt / 2f;
                        Kit.Round(bcx - Ds.Px(bs / 2f), bcy - bs / 2f, Ds.Px(bs), bs, 12f, Ds.A(col, ca));
                        Textures.Draw("li_" + icon, bcx, bcy, bs * 0.58f, Ds.A(Th.On(col), ca));
                        float tx = cx + Ds.Px(Th.Px(84f)), tw = cw - Ds.Px(Th.Px(104f));
                        Ds.TM(head, tx, ny + Th.Px(24f), 0.24f, Ds.A(col, ca), Ds.Display);
                        Ds.TM(Ds.Fit(line, 0.34f, tw), tx, ny + Th.Px(50f), 0.34f, Ds.A(Th.Text, ca));
                        if (foot != null) Ds.TM(foot, tx, ny + Th.Px(76f), 0.24f, Ds.A(Th.Sub, ca), Ds.Display);
                        ny += hgt + Th.Px(12f);
                    };
                    var story = Career.Story.Instance;
                    if (story != null && story.Current != null)
                    {
                        string g = story.NextGoalText ?? ""; int c = g.IndexOf(": "); if (c >= 0) g = g.Substring(c + 2);
                        card("book-marked", Th.Amber, "CHAPTER " + (d.StoryChapter + 1) + "  ·  " + story.Current.Title.ToUpperInvariant(), g, story.Current.Goals.Count(story.Done) + " / " + story.Current.Goals.Length + " GOALS", Th.Px(96f));
                    }
                    else if (Gameplay.Objectives.CurrentText != null) card("target", Th.Accent, "NEXT OBJECTIVE", Gameplay.Objectives.CurrentText, null, Th.Px(76f));
                    int unread = d.Messages.Count(m => !m.Read);
                    var news = d.News.LastOrDefault();
                    if (news != null || unread > 0)
                    {
                        string line = unread > 0 ? d.Messages.Last(m => !m.Read).From + ": " + d.Messages.Last(m => !m.Read).Subject : news.Headline;
                        card(unread > 0 ? "mail" : "newspaper", unread > 0 ? Th.Cyan : Th.Green, unread > 0 ? "PHONE  ·  " + unread + " UNREAD" : "STREET BUZZ", line, null, Th.Px(76f));
                    }
                    if (!string.IsNullOrEmpty(cur))
                    {
                        float dw = Math.Max(Ds.Width(cur.ToUpperInvariant(), 0.72f, Ds.Display), Ds.Width("NOW DRIVING", 0.24f, Ds.Display)) + Ds.Px(Th.Px(48f)), dh = Th.Px(84f);
                        Kit.Glass(0.045f, 0.955f - dh, dw, dh, Th.R_Lg, fade);
                        Ds.TM("NOW DRIVING", 0.045f + Ds.Px(Th.Px(24f)), 0.955f - dh + Th.Px(24f), 0.24f, Ds.A(Th.Accent, fade), Ds.Display);
                        Ds.TM(cur.ToUpperInvariant(), 0.045f + Ds.Px(Th.Px(24f)), 0.955f - dh + Th.Px(56f), 0.72f, Ds.A(Th.Text, fade), Ds.Display);
                    }

                    Buttons.Draw(Control.FrontendUp, "", Control.FrontendDown, "Navigate", Control.FrontendAccept, "Select", Control.FrontendCancel, "Hit the streets");

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
