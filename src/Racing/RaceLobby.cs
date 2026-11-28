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
                    Ds.HGrad(0f, 0f, 0.5f, 1f, Ds.A(Ds.Bg, 0.7f * a), Ds.A(Ds.Bg, 0f), 8);
                    Ds.HGrad(0.62f, 0f, 0.38f, 1f, Ds.A(Ds.Bg, 0f), Ds.A(Ds.Bg, 0.7f * a), 8);
                    // event card (left)
                    float x = 0.045f, cw = Ds.Px(Th.Px(620f));
                    Ds.Logo(x, 0.045f, 0.7f, a, GTA.UI.Alignment.Left, false);
                    float y = 0.125f;
                    var rows = new List<KeyValuePair<string, string>>();
                    rows.Add(new KeyValuePair<string, string>("DISTANCE", (len / 1000f).ToString("0.0") + " KM" + (r.Lapped ? "  ·  " + r.Laps + " LAPS" : "")));
                    rows.Add(new KeyValuePair<string, string>("REWARD", "$" + r.CashReward.ToString("N0") + "  ·  " + r.RepReward + " REP"));
                    if (r.LevelNeeded > 0) rows.Add(new KeyValuePair<string, string>("LEVEL", Career.Disciplines.Name(disc) + " " + r.LevelNeeded + "+"));
                    var turf = Explore.Turf.At(r.Start); if (turf != null && r.HasOpponents) rows.Add(new KeyValuePair<string, string>("TURF", turf.Name.ToUpperInvariant()));
                    if (r.HotPursuit) rows.Add(new KeyValuePair<string, string>("POLICE", "HOT PURSUIT  ·  +50% REWARD"));
                    if (rivalName != null) rows.Add(new KeyValuePair<string, string>("RIVAL", rivalName.ToUpperInvariant() + " SET THE TIME"));
                    if (r.BikesOnly) rows.Add(new KeyValuePair<string, string>("VEHICLES", "MOTORCYCLES ONLY"));
                    float eh = Th.Px(150f) + rows.Count * Th.Px(34f);
                    Kit.Glass(x, y, cw, eh, Th.R_Lg + 4f, a);
                    float ix = x + Ds.Px(Th.Px(26f));
                    Kit.Chip(Career.Disciplines.Name(disc).ToUpperInvariant() + "  ·  " + typeLabel.ToUpperInvariant(), ix, y + Th.Px(32f), Ds.A(dc, a), Ds.A(Th.On(dc), a), 0.26f);
                    Ds.TM(Ds.Fit(r.Name.ToUpperInvariant(), 1.0f, cw - Ds.Px(Th.Px(52f)), Ds.Display), ix, y + Th.Px(84f), 1.0f, Ds.A(Th.Text, a), Ds.Display);
                    float ry = y + Th.Px(134f);
                    foreach (var kv in rows)
                    {
                        Ds.TM(kv.Key, ix, ry, 0.26f, Ds.A(Th.Sub, a), Ds.Display);
                        Ds.TM(kv.Value, ix + Ds.Px(Th.Px(150f)), ry, 0.3f, Ds.A(Th.Text, a), Ds.Display);
                        ry += Th.Px(34f);
                    }
                    y += eh + Th.Px(14f);
                    // your car
                    int pi = PerformanceIndex.Of(me);
                    float ch2 = Th.Px(78f);
                    Kit.Glass(x, y, cw, ch2, Th.R_Lg, a);
                    Ds.TM("YOUR CAR", ix, y + Th.Px(22f), 0.24f, Ds.A(Th.Sub, a), Ds.Display);
                    float bw = PerformanceIndex.Badge(pi, ix, y + Th.Px(38f), 0.028f, a);
                    Ds.TM(Ds.Fit(me != null ? me.Title().ToUpperInvariant() : "", 0.5f, cw - bw - Ds.Px(Th.Px(70f)), Ds.Display), ix + bw + Ds.Px(Th.Px(12f)), y + Th.Px(53f), 0.5f, Ds.A(Th.Text, a), Ds.Display);
                    y += ch2 + Th.Px(14f);
                    // wager + crew
                    if (r.HasOpponents)
                    {
                        float wh = Th.Px(72f);
                        Kit.Glass(x, y, cw, wh, Th.R_Lg, a);
                        Kit.Pill(x + Ds.Px(Th.Px(10f)), y + Th.Px(12f), Ds.Px(Th.Px(4f)), wh - Th.Px(24f), Ds.A(Th.Amber, a));
                        Ds.TM("SIDE BET", ix, y + Th.Px(24f), 0.3f, Ds.A(Th.Amber, a), Ds.Display);
                        Ds.TM("WIN: DOUBLE  ·  PODIUM: MONEY BACK", ix, y + Th.Px(50f), 0.22f, Ds.A(Th.Sub, a), Ds.Display);
                        float vx = x + cw - Ds.Px(Th.Px(24f));
                        string wv = Wagers[wi] == 0 ? "NONE" : "$" + Wagers[wi].ToString("N0");
                        Textures.Draw("li_chevron-right", vx - Ds.Px(Th.Px(8f)), y + wh / 2f, Th.Px(22f), Ds.A(Th.Text, a));
                        Ds.TM(wv, vx - Ds.Px(Th.Px(26f)), y + wh / 2f, 0.44f, Ds.A(Th.Text, a), Ds.Display, GTA.UI.Alignment.Right);
                        Textures.Draw("li_chevron-left", vx - Ds.Px(Th.Px(40f)) - Ds.Width(wv, 0.44f, Ds.Display), y + wh / 2f, Th.Px(22f), Ds.A(Th.Text, a));
                        y += wh + Th.Px(14f);
                        if (Career.Crew.Hired)
                        {
                            Kit.Glass(x, y, cw, Th.Px(60f), Th.R_Lg, a);
                            Kit.Pill(x + Ds.Px(Th.Px(10f)), y + Th.Px(12f), Ds.Px(Th.Px(4f)), Th.Px(36f), Ds.A(Th.Cyan, a));
                            Ds.TM("CREW  ·  " + Career.Crew.Name.ToUpperInvariant() + " (" + Career.Crew.Role.ToUpperInvariant() + ")", ix, y + Th.Px(30f), 0.3f, Ds.A(Th.Cyan, a), Ds.Display);
                            Kit.Toggle(x + cw - Ds.Px(Th.Px(24f)) - Ds.Px(Th.Px(40f)), y + Th.Px(30f), crew, Th.Cyan);
                        }
                    }
                    // grid (right)
                    if (names != null)
                    {
                        float gw = Ds.Px(Th.Px(560f)), gx = 0.955f - gw, gy = 0.125f, rh = Th.Px(62f);
                        Kit.Glass(gx, gy, gw, Th.Px(56f) + names.Count * rh + Th.Px(10f), Th.R_Lg + 4f, a);
                        Ds.TM("STARTING GRID", gx + Ds.Px(Th.Px(24f)), gy + Th.Px(30f), 0.3f, Ds.A(Th.Sub, a), Ds.Display);
                        gy += Th.Px(56f);
                        for (int i = 0; i < names.Count; i++)
                        {
                            float ra = Ds.EaseOut((Game.GameTime - start - 120 - i * 70) / 350f) * a; if (ra <= 0) continue;
                            float yy = gy + i * rh;
                            Kit.Round(gx + Ds.Px(Th.Px(10f)), yy, gw - Ds.Px(Th.Px(20f)), rh - Th.Px(6f), Th.R_Md, Ds.A(Th.Track, ra));
                            Ds.TM((i + 2).ToString(), gx + Ds.Px(Th.Px(34f)), yy + (rh - Th.Px(6f)) / 2f, 0.44f, Ds.A(Th.Sub, ra), Ds.Display, GTA.UI.Alignment.Center);
                            int opi = PerformanceIndex.Of(cars[i]);
                            float bw2 = PerformanceIndex.Badge(opi, gx + Ds.Px(Th.Px(56f)), yy + Th.Px(16f), 0.026f, ra);
                            float tx2 = gx + Ds.Px(Th.Px(68f)) + bw2;
                            Ds.TM(names[i].ToUpperInvariant(), tx2, yy + Th.Px(18f), 0.32f, Ds.A(Th.Text, ra), Ds.Display);
                            Ds.TM(Ds.Fit(cars[i].Name.ToUpperInvariant(), 0.24f, gw - bw2 - Ds.Px(Th.Px(100f)), Ds.Display), tx2, yy + Th.Px(40f), 0.24f, Ds.A(Th.Sub, ra), Ds.Display);
                        }
                    }
                    var pp = new List<object> { Control.FrontendAccept, "Start", Control.FrontendCancel, "Back" };
                    if (r.HasOpponents) { pp.Add(Control.FrontendLeft); pp.Add(""); pp.Add(Control.FrontendRight); pp.Add("Side bet"); }
                    if (Career.Crew.Hired && r.HasOpponents) { pp.Add(Control.FrontendX); pp.Add("Crew"); }
                    Buttons.Draw(pp.ToArray());
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
