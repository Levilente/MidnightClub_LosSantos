using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Persistence;
using MidnightClub.UI;
using MidnightClub.Vehicles;

namespace MidnightClub.Racing
{
    /// <summary>Pre-race lobby (Forza / NFS style): the camera circles your car while the screen shows the event,
    /// your class and PI, the grid with every opponent's car and class, the rival time on solo events, an optional
    /// side bet and whether your crew mate rides along.</summary>
    public static class RaceLobby
    {
        public struct Result { public bool Start; public long Wager; public bool Crew; }
        private static readonly long[] Wagers = { 0, 1000, 5000, 10000, 25000, 50000 };

        public static Result Show(RaceDefinition r, string typeLabel, List<string> names, List<VehicleInfo> cars, OwnedVehicle me, long cash, string rivalName)
        {
            var res = new Result(); var car = Game.Player.Character.CurrentVehicle;
            int wi = 0; bool crew = Career.Crew.Hired && r.HasOpponents;
            Camera cam = null; float ang = 210f; int start = Game.GameTime;
            float len = 0; for (int i = 1; i < r.Points.Count; i++) len += new Vector3(r.Points[i][0], r.Points[i][1], r.Points[i][2]).DistanceTo(new Vector3(r.Points[i - 1][0], r.Points[i - 1][1], r.Points[i - 1][2]));
            if (r.Lapped) len *= Math.Max(1, r.Laps);
            try
            {
                if (car != null && car.Exists()) { cam = World.CreateCamera(car.Position, Vector3.Zero, 40f); World.RenderingCamera = cam; }
                while (true)
                {
                    Game.DisableAllControlsThisFrame(); Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME); Theme.FullscreenFrame();
                    if (cam != null && car.Exists())
                    {
                        ang += 6f * Ds.Dt; float rad = ang * (float)Math.PI / 180f, dist = Math.Max(5.5f, car.Model.Dimensions.frontTopRight.Y * 2.3f);
                        cam.Position = car.Position + new Vector3((float)Math.Cos(rad) * dist, (float)Math.Sin(rad) * dist, 1.0f); cam.PointAt(car.Position + new Vector3(0, 0, 0.3f));
                    }
                    float a = Ds.EaseOut((Game.GameTime - start) / 450f);
                    var disc = r.Discipline; var dc = disc == Discipline.Drift ? Ds.Amber : disc == Discipline.Offroad ? Color.FromArgb(255, 251, 146, 60) : disc == Discipline.Drag ? Ds.Lime : disc == Discipline.Bikes ? Ds.Cyan : Ds.Red;
                    Ds.HGrad(0f, 0f, 0.5f, 1f, Ds.A(Ds.Bg, 0.92f * a), Ds.A(Ds.Bg, 0f), 10);
                    Ds.HGrad(0.62f, 0f, 0.38f, 1f, Ds.A(Ds.Bg, 0f), Ds.A(Ds.Bg, 0.9f * a), 8);
                    // event (left)
                    float x = 0.05f;
                    Ds.Logo(x, 0.05f, 0.8f, a, GTA.UI.Alignment.Left, false);
                    Ds.Tag(Career.Disciplines.Name(disc).ToUpperInvariant() + "  ·  " + typeLabel.ToUpperInvariant(), x, 0.17f, dc, Ds.Bg, 0.28f, a);
                    Ds.T(Ds.Fit(r.Name.ToUpperInvariant(), 1.0f, 0.42f, Ds.Display), x, 0.2f, 1.0f, Ds.A(Ds.Text, a), Ds.Display);
                    float y = 0.3f;
                    Action<string, string> row = (k, v) => { Ds.T(k, x, y, 0.28f, Ds.A(Ds.Muted, a), Ds.Display); Ds.T(v, x + 0.2f, y, 0.28f, Ds.A(Ds.Text, a), Ds.Display); y += 0.032f; };
                    row("DISTANCE", (len / 1000f).ToString("0.0") + " KM" + (r.Lapped ? "  ·  " + r.Laps + " LAPS" : ""));
                    row("REWARD", "$" + r.CashReward.ToString("N0") + "  ·  " + r.RepReward + " REP");
                    if (r.LevelNeeded > 0) row("LEVEL", Career.Disciplines.Name(disc) + " " + r.LevelNeeded + "+");
                    var turf = Explore.Turf.At(r.Start); if (turf != null && r.HasOpponents) row("TURF", turf.Name.ToUpperInvariant());
                    if (r.HotPursuit) row("POLICE", "HOT PURSUIT  ·  +50% REWARD");
                    if (rivalName != null) row("RIVAL", rivalName.ToUpperInvariant() + " SET THE TIME TO BEAT");
                    if (r.BikesOnly) row("VEHICLES", "MOTORCYCLES ONLY");
                    // your car
                    y += 0.02f;
                    int pi = PerformanceIndex.Of(me);
                    Ds.T("YOUR CAR", x, y, 0.24f, Ds.A(Ds.Muted, a), Ds.Display); y += 0.026f;
                    float bw = PerformanceIndex.Badge(pi, x, y + 0.004f, 0.03f, a);
                    Ds.T(Ds.Fit(me != null ? (me.Nickname ?? me.Model).ToUpperInvariant() : "", 0.5f, 0.3f, Ds.Display), x + bw + Ds.Px(0.012f), y - 0.004f, 0.5f, Ds.A(Ds.Text, a), Ds.Display);
                    y += 0.06f;
                    // wager + crew
                    if (r.HasOpponents)
                    {
                        Ds.Rect(x, y, Ds.Px(0.42f), 0.05f, Ds.A(Ds.Surface, 0.92f * a)); Ds.Rect(x, y, Ds.Px(0.004f), 0.05f, Ds.A(Ds.Pink, a));
                        Ds.T("SIDE BET", x + Ds.Px(0.016f), y + 0.012f, 0.3f, Ds.A(Ds.Pink, a), Ds.Display);
                        Ds.T("‹  " + (Wagers[wi] == 0 ? "NONE" : "$" + Wagers[wi].ToString("N0")) + "  ›", x + Ds.Px(0.4f), y + 0.01f, 0.36f, Ds.A(Ds.Text, a), Ds.Display, GTA.UI.Alignment.Right);
                        Ds.T("WIN: DOUBLE  ·  PODIUM: MONEY BACK", x + Ds.Px(0.016f), y + 0.056f, 0.22f, Ds.A(Ds.Faint, a), Ds.Display);
                        y += 0.085f;
                        if (Career.Crew.Hired)
                        {
                            Ds.Rect(x, y, Ds.Px(0.42f), 0.05f, Ds.A(Ds.Surface, 0.92f * a)); Ds.Rect(x, y, Ds.Px(0.004f), 0.05f, Ds.A(Ds.Cyan, a));
                            Ds.T("CREW  ·  " + Career.Crew.Name.ToUpperInvariant() + " (" + Career.Crew.Role.ToUpperInvariant() + ")", x + Ds.Px(0.016f), y + 0.012f, 0.3f, Ds.A(Ds.Cyan, a), Ds.Display);
                            Ds.T(crew ? "RIDING ALONG" : "STAYING HOME", x + Ds.Px(0.4f), y + 0.012f, 0.3f, Ds.A(crew ? Ds.Text : Ds.Faint, a), Ds.Display, GTA.UI.Alignment.Right);
                        }
                    }
                    // grid (right)
                    if (names != null)
                    {
                        float gx = 0.67f, gy = 0.17f, gw = Ds.Px(0.5f);
                        Ds.T("STARTING GRID", gx, gy, 0.28f, Ds.A(Ds.Muted, a), Ds.Display); gy += 0.04f;
                        for (int i = 0; i < names.Count; i++)
                        {
                            float ra = Ds.EaseOut((Game.GameTime - start - 120 - i * 70) / 350f) * a; if (ra <= 0) continue;
                            float yy = gy + i * 0.058f;
                            Ds.Rect(gx, yy, gw, 0.05f, Ds.A(Ds.Surface, 0.9f * ra));
                            int opi = PerformanceIndex.Of(cars[i]);
                            float bw2 = PerformanceIndex.Badge(opi, gx + Ds.Px(0.01f), yy + 0.012f, 0.026f, ra);
                            Ds.T(names[i].ToUpperInvariant(), gx + Ds.Px(0.02f) + bw2, yy + 0.004f, 0.32f, Ds.A(Ds.Text, ra), Ds.Display);
                            Ds.T(Ds.Fit(cars[i].Name.ToUpperInvariant(), 0.24f, gw - bw2 - Ds.Px(0.04f), Ds.Display), gx + Ds.Px(0.02f) + bw2, yy + 0.028f, 0.24f, Ds.A(Ds.Muted, ra), Ds.Display);
                        }
                    }
                    Buttons.Draw(Control.FrontendAccept, "Start", Control.FrontendCancel, "Back", Control.FrontendRight, r.HasOpponents ? "Side bet" : "", Control.FrontendX, Career.Crew.Hired && r.HasOpponents ? "Crew" : "");
                    if (Game.GameTime - start > 350)
                    {
                        if (r.HasOpponents)
                        {
                            int d = Input.JustPressed(Control.FrontendRight) ? 1 : Input.JustPressed(Control.FrontendLeft) ? -1 : 0;
                            if (d != 0) { wi = (wi + d + Wagers.Length) % Wagers.Length; while (wi > 0 && Wagers[wi] > cash) wi = d > 0 ? 0 : wi - 1; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true); }
                            if (Input.JustPressed(Control.FrontendX) && Career.Crew.Hired) crew = !crew;
                        }
                        if (Input.JustPressed(Control.FrontendAccept)) { res.Start = true; res.Wager = Wagers[wi]; res.Crew = crew; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true); break; }
                        if (Input.JustPressed(Control.FrontendCancel)) break;
                    }
                    Script.Wait(0);
                }
            }
            finally
            {
                if (cam != null) { World.RenderingCamera = null; try { cam.Delete(); } catch { } }
            }
            return res;
        }
    }
}
