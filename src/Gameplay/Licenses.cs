using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.Racing;
using MidnightClub.UI;
using MidnightClub.Vehicles;

namespace MidnightClub.Gameplay
{
    /// <summary>Gran Turismo-style license tests on a closed-off straight: B-1 launch and stop, B-2 slalom,
    /// A-1 braking point, A-2 top speed. Bronze / silver / gold targets scale with your car; earning every
    /// test of a license pays out and adds the license to your profile.</summary>
    public sealed class Licenses
    {
        private sealed class Test { public string Code, Name, Goal; public int Kind; }
        private static readonly Test[] Tests =
        {
            new Test { Code = "B-1", Name = "Launch and stop", Goal = "Accelerate down the strip and come to a complete stop inside the box.", Kind = 0 },
            new Test { Code = "B-2", Name = "Slalom", Goal = "Weave through the cones, passing each on alternate sides, then cross the line.", Kind = 1 },
            new Test { Code = "A-1", Name = "Braking point", Goal = "You start at 120 km/h. Stop as close to the centre of the box as you can.", Kind = 2 },
            new Test { Code = "A-2", Name = "Top speed", Goal = "Standing start. Hit the highest speed you can through the trap at the end.", Kind = 3 },
        };
        private static readonly string[] Medals = { "", "BRONZE", "SILVER", "GOLD" };
        private static readonly Color[] MedalCol = { Color.Gray, Color.FromArgb(255, 205, 127, 70), Color.FromArgb(255, 200, 205, 220), Color.FromArgb(255, 251, 191, 36) };

        public static Licenses Instance; public static bool Active { get { return Instance != null && Instance.state != 0; } }
        private readonly SaveManager saves; private readonly TransactionManager money; private readonly VehicleManager vehicles; private readonly RaceManager races; private readonly MenuStack stack;
        private int state;   // 0 none, 1 intro, 2 countdown, 3 running, 4 result
        private int cur, t0, goAt; private Vector3 a, dir, right; private float len, vmax;
        private readonly List<Prop> cones = new List<Prop>(); private readonly List<float> coneAlong = new List<float>(); private int conesPassed, coneMiss;
        private float resultValue; private int resultMedal; private string resultText;

        public Licenses(SaveManager saves, TransactionManager money, VehicleManager vehicles, RaceManager races, MenuStack stack)
        {
            this.saves = saves; this.money = money; this.vehicles = vehicles; this.races = races; this.stack = stack; Instance = this;
            EventBus.Subscribe<MidnightTickEvent>(e => { try { Tick(); } catch (Exception ex) { Log.Error("License test", ex); Abort(); } });
            EventBus.Subscribe<ModeChangedEvent>(e => Abort());
        }

        private int Medal(int i) { var kv = saves.Data.Licenses.FirstOrDefault(x => x.Key == i.ToString()); int v; return kv != null && int.TryParse(kv.Value, out v) ? v : 0; }
        public string LicenseLevel { get { bool b = Medal(0) > 0 && Medal(1) > 0, aa = b && Medal(2) > 0 && Medal(3) > 0; return aa ? "A" : b ? "B" : "None"; } }

        public Menu Menu()
        {
            var m = new Menu("License tests", "Licence: " + LicenseLevel);
            m.OnOpen = () =>
            {
                m.Items.Clear(); m.Subtitle = "Licence: " + LicenseLevel;
                for (int k = 0; k < Tests.Length; k++)
                {
                    int i = k; var t = Tests[i];
                    bool locked = i >= 2 && (Medal(0) == 0 || Medal(1) == 0);
                    m.Add(new MenuItem(t.Code + "  " + t.Name, () => { if (locked) return; stack.Close(); Begin(i); })
                    { Enabled = !locked, Value = () => locked ? "Needs B licence" : Medal(i) == 0 ? "Not passed" : Medals[Medal(i)], Description = () => t.Goal });
                }
            };
            return m;
        }

        // ---------------------------------------------------------------- setup
        private void Begin(int i)
        {
            var car = vehicles.Current; if (car == null || !car.Exists() || RaceManager.IsRacing) return;
            var strip = races.Races.Where(r => r.Kind == RaceType.Drag && r.Points.Count >= 2).OrderBy(r => r.Start.DistanceTo(car.Position)).FirstOrDefault();
            if (strip == null) { Toast.Post("~r~The test track isn't ready yet.~s~ Try again in a moment."); return; }
            cur = i; a = strip.Start; var b = new Vector3(strip.Points[1][0], strip.Points[1][1], strip.Points[1][2]);
            dir = b - a; dir.Z = 0; len = dir.Length(); dir.Normalize(); right = new Vector3(dir.Y, -dir.X, 0);
            vmax = Math.Max(30f, Math.Min(85f, Function.Call<float>(Hash.GET_VEHICLE_ESTIMATED_MAX_SPEED, car)));
            Screen(true);
            ClearCones();
            Function.Call(Hash.CLEAR_AREA_OF_VEHICLES, a.X + dir.X * len / 2, a.Y + dir.Y * len / 2, a.Z, len * 0.7f, false, false, false, false, false, false, 0);
            car.Position = a; car.Heading = (float)(Math.Atan2(-dir.X, dir.Y) * 180.0 / Math.PI); car.PlaceOnGround(); car.Repair();
            Function.Call(Hash.FREEZE_ENTITY_POSITION, car, true);
            if (Tests[i].Kind == 1)
            {
                var m = new Model("prop_roadcone02a"); m.Request(2000);
                for (int c = 0; c < 10; c++)
                {
                    float along = 60f + c * 28f; if (along > len - 40f) break;
                    var p = a + dir * along; float gz; if (World.GetGroundHeight(p + new Vector3(0, 0, 3f), out gz)) p.Z = gz;
                    var pr = World.CreateProp(m, p, false, true); if (pr != null) { pr.IsPersistent = true; cones.Add(pr); coneAlong.Add(along); }
                }
                m.MarkAsNoLongerNeeded();
            }
            conesPassed = 0; coneMiss = 0;
            state = 1; t0 = Game.GameTime;
            Screen(false);
        }

        private static void Screen(bool outFade) { if (outFade) { GTA.UI.Screen.FadeOut(400); Script.Wait(450); } else GTA.UI.Screen.FadeIn(400); }
        private void ClearCones() { foreach (var c in cones) try { if (c.Exists()) c.Delete(); } catch { } cones.Clear(); coneAlong.Clear(); }
        private void Abort() { if (state == 0) return; state = 0; ClearCones(); var car = vehicles.Current; if (car != null && car.Exists()) Function.Call(Hash.FREEZE_ENTITY_POSITION, car, false); }

        // ---------------------------------------------------------------- targets
        private float[] Targets(int kind)
        {
            float v = Math.Min(vmax, 70f);
            switch (kind)
            {
                case 0: { float g = (len - 20f) / (v * 0.42f); return new[] { g, g * 1.12f, g * 1.3f }; }
                case 1: { float g = (Math.Min(len, 340f)) / (v * 0.36f); return new[] { g, g * 1.12f, g * 1.3f }; }
                case 2: return new[] { 1.5f, 3f, 5f };
                default: return new[] { vmax * 3.6f * 0.72f, vmax * 3.6f * 0.64f, vmax * 3.6f * 0.55f };
            }
        }
        private int Grade(int kind, float v)
        {
            var t = Targets(kind);
            if (kind == 3) return v >= t[0] ? 3 : v >= t[1] ? 2 : v >= t[2] ? 1 : 0;
            return v <= t[0] ? 3 : v <= t[1] ? 2 : v <= t[2] ? 1 : 0;
        }
        private string Fmt(int kind, float v) { return kind == 3 ? ((int)v) + " km/h" : kind == 2 ? v.ToString("0.00") + " m" : v.ToString("0.000") + " s"; }

        // ---------------------------------------------------------------- per frame
        private void Tick()
        {
            if (state == 0) return;
            var car = vehicles.Current; if (car == null || !car.Exists()) { Abort(); return; }
            var test = Tests[cur]; int now = Game.GameTime;
            Function.Call(Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f); Function.Call(Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f); Function.Call(Hash.SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            Function.Call(Hash.SET_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            DrawTrack(test.Kind);
            if (state == 1) { Card(test, now); return; }
            if (state == 2)
            {
                int left = goAt - now; Ds.T(left > 0 ? ((left / 1000) + 1).ToString() : "GO", 0.5f, 0.25f, 2.4f, left > 0 ? Ds.Text : Ds.Lime, Ds.Display, GTA.UI.Alignment.Center);
                if (left <= 0)
                {
                    state = 3; t0 = now; Function.Call(Hash.FREEZE_ENTITY_POSITION, car, false);
                    if (test.Kind == 2) Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, car, 33.4f);
                    Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "GO", "HUD_MINI_GAME_SOUNDSET", true);
                }
                return;
            }
            if (state == 4) { Result(test, now); return; }
            // running
            float t = (now - t0) / 1000f;
            var rel = car.Position - a; rel.Z = 0; float along = Vector3.Dot(rel, dir), lat = Vector3.Dot(rel, right);
            if (Math.Abs(lat) > 25f || along < -90f || t > 90f) { Finish(test, float.NaN, "Left the test area"); return; }
            if (Game.IsControlPressed(Control.FrontendCancel) && t > 1f) { Finish(test, float.NaN, "Abandoned"); return; }
            switch (test.Kind)
            {
                case 0:
                case 2:
                {
                    float boxC = test.Kind == 0 ? len - 20f : Math.Min(220f, len - 30f);
                    if (along > boxC + 12f) { Finish(test, float.NaN, "Overshot the box"); return; }
                    if (t > 1.5f && car.Speed < 0.3f && along > 20f)
                    {
                        bool inside = Math.Abs(along - boxC) <= 4f && Math.Abs(lat) <= 3.5f;
                        if (!inside) { Finish(test, float.NaN, "Stopped outside the box"); return; }
                        Finish(test, test.Kind == 0 ? t : Math.Abs(along - boxC), null); return;
                    }
                    break;
                }
                case 1:
                {
                    while (conesPassed < coneAlong.Count && along > coneAlong[conesPassed])
                    {
                        bool needLeft = conesPassed % 2 == 0; bool ok = needLeft ? lat < -0.4f : lat > 0.4f;
                        if (!ok) { coneMiss++; Toast.Post("~r~Missed gate " + (conesPassed + 1) + "~s~ (+3 s)"); }
                        conesPassed++;
                    }
                    foreach (var c in cones) if (c.Exists() && Function.Call<bool>(Hash.IS_ENTITY_TOUCHING_ENTITY, car, c)) { }
                    float endLine = coneAlong.Count > 0 ? coneAlong[coneAlong.Count - 1] + 30f : 320f;
                    if (along > endLine) { Finish(test, t + coneMiss * 3f, null); return; }
                    break;
                }
                case 3:
                {
                    if (along > len - 15f) { Finish(test, car.Speed * 3.6f, null); return; }
                    break;
                }
            }
            // live HUD
            float w = Ds.Px(0.26f), x = 0.5f - w / 2f, y = 0.04f;
            Ds.Rect(x, y, w, 0.07f, Ds.A(Ds.Bg, 0.85f)); Ds.Rect(x, y, w, 0.004f, Ds.Cyan);
            Ds.T(test.Code + "  " + test.Name.ToUpperInvariant(), x + Ds.Px(0.012f), y + 0.012f, 0.28f, Ds.Cyan, Ds.Display);
            string live = test.Kind == 3 ? ((int)(car.Speed * 3.6f)) + " KM/H" : test.Kind == 2 ? ((int)(car.Speed * 3.6f)) + " KM/H" : t.ToString("0.00") + " S";
            Ds.T(live, x + w - Ds.Px(0.012f), y + 0.006f, 0.7f, Ds.Text, Ds.Display, GTA.UI.Alignment.Right);
            if (test.Kind == 1) Ds.T("GATE " + Math.Min(conesPassed + 1, coneAlong.Count) + " / " + coneAlong.Count + (coneMiss > 0 ? "  ·  +" + (coneMiss * 3) + " S" : ""), x + Ds.Px(0.012f), y + 0.04f, 0.26f, Ds.Muted, Ds.Display);
        }

        private void DrawTrack(int kind)
        {
            Func<float, float, Vector3> P = (al, la) => { var p = a + dir * al + right * la; return p; };
            Action<Vector3, Vector3, Color> line = (p, q, c) => Function.Call(Hash.DRAW_LINE, p.X, p.Y, p.Z + 0.15f, q.X, q.Y, q.Z + 0.15f, c.R, c.G, c.B, c.A);
            if (kind == 0 || kind == 2)
            {
                float bc = kind == 0 ? len - 20f : Math.Min(220f, len - 30f); var c = Color.FromArgb(255, 163, 230, 53);
                for (int k = 0; k < 3; k++)
                {
                    float o = k * 0.05f;
                    line(P(bc - 4 - o, -3.5f), P(bc - 4 - o, 3.5f), c); line(P(bc + 4 + o, -3.5f), P(bc + 4 + o, 3.5f), c);
                    line(P(bc - 4, -3.5f - o), P(bc + 4, -3.5f - o), c); line(P(bc - 4, 3.5f + o), P(bc + 4, 3.5f + o), c);
                }
                var m = P(bc, 0); Function.Call(Hash.DRAW_MARKER, 1, m.X, m.Y, m.Z - 1f, 0f, 0f, 0f, 0f, 0f, 0f, 1.2f, 1.2f, 1.2f, 163, 230, 53, 120, false, false, 2, false, 0, 0, false);
            }
            if (kind == 1) for (int i = 0; i < coneAlong.Count; i++)
            {
                bool needLeft = i % 2 == 0; var p = P(coneAlong[i], needLeft ? -2.5f : 2.5f);
                Function.Call(Hash.DRAW_MARKER, 2, p.X, p.Y, p.Z + 1.4f, 0f, 0f, 0f, 180f, 0f, 0f, 0.8f, 0.8f, 0.8f, 34, 211, 238, i == conesPassed ? 220 : 90, true, true, 2, false, 0, 0, false);
            }
            if (kind == 3 || kind == 1)
            {
                float e = kind == 3 ? len - 15f : (coneAlong.Count > 0 ? coneAlong[coneAlong.Count - 1] + 30f : 320f);
                var c = Color.FromArgb(255, 168, 85, 247); line(P(e, -8f), P(e, 8f), c);
                var m = P(e, 0); Function.Call(Hash.DRAW_MARKER, 4, m.X, m.Y, m.Z + 2.5f, 0f, 0f, 0f, 0f, 0f, 0f, 2f, 2f, 2f, 168, 85, 247, 160, true, true, 2, false, 0, 0, false);
            }
        }

        private void Card(Test test, int now)
        {
            Game.DisableAllControlsThisFrame(); Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
            float a2 = Math.Min(1f, (now - t0) / 300f), x = 0.08f;
            Ds.HGrad(0f, 0f, 0.62f, 1f, Ds.A(Ds.Bg, 0.92f * a2), Ds.A(Ds.Bg, 0f), 10);
            Ds.T("LICENCE TEST", x, 0.2f, 0.3f, Ds.A(Ds.Cyan, a2), Ds.Display);
            Ds.T(test.Code, x, 0.225f, 2.0f, Ds.A(Ds.Text, a2), Ds.Display);
            Ds.T(test.Name.ToUpperInvariant(), x, 0.39f, 0.9f, Ds.A(Ds.Text, a2), Ds.Display);
            Ds.T(test.Goal, x, 0.46f, 0.36f, Ds.A(Ds.Muted, a2), Ds.Body, GTA.UI.Alignment.Left, 0.38f);
            var t = Targets(test.Kind);
            for (int i = 0; i < 3; i++)
            {
                float yy = 0.57f + i * 0.05f; int med = 3 - i;
                Ds.Rect(x, yy, Ds.Px(0.006f), 0.036f, Ds.A(MedalCol[med], a2));
                Ds.T(Medals[med], x + Ds.Px(0.02f), yy + 0.004f, 0.4f, Ds.A(MedalCol[med], a2), Ds.Display);
                Ds.T(Fmt(test.Kind, t[i]), x + Ds.Px(0.2f), yy + 0.004f, 0.4f, Ds.A(Ds.Text, a2), Ds.Display);
            }
            if (Medal(cur) > 0) Ds.T("YOUR BEST: " + Medals[Medal(cur)], x, 0.75f, 0.3f, Ds.A(MedalCol[Medal(cur)], a2), Ds.Display);
            Buttons.Draw(Control.FrontendAccept, "Start", Control.FrontendCancel, "Leave");
            if (now - t0 < 400) return;
            if (Input.JustPressed(Control.FrontendAccept)) { state = 2; goAt = now + 3000; }
            else if (Input.JustPressed(Control.FrontendCancel)) Abort();
        }

        private void Finish(Test test, float value, string fail)
        {
            state = 4; t0 = Game.GameTime; resultValue = value; resultText = fail;
            resultMedal = fail != null || float.IsNaN(value) ? 0 : Grade(test.Kind, value);
            int old = Medal(cur);
            if (resultMedal > old)
            {
                saves.Data.Licenses.RemoveAll(k => k.Key == cur.ToString()); saves.Data.Licenses.Add(new KeyValue { Key = cur.ToString(), Value = resultMedal.ToString() }); saves.IsDirty = true;
                long pay = (resultMedal - old) * 2500L;
                money.Earn(pay, 30L * resultMedal, "licence", test.Code + " " + Medals[resultMedal], "lic:" + cur + ":" + resultMedal);
                if (Career.Progression.Instance != null) Career.Progression.Instance.Award(300 * resultMedal, "Licence test");
                string lvl = LicenseLevel;
                if ((cur <= 1 && lvl != "None" && old == 0) || (cur >= 2 && lvl == "A" && old == 0))
                    Shard.Big(lvl + " LICENCE", lvl == "A" ? "You hold an A licence. +$25,000" : "You hold a B licence. A-class tests unlocked. +$10,000", Shard.Gold, 5000);
                if (old == 0 && lvl == "B" && cur <= 1) money.Earn(10000, 200, "licence", "B licence", "lic-b");
                if (old == 0 && lvl == "A" && cur >= 2) money.Earn(25000, 500, "licence", "A licence", "lic-a");
            }
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, resultMedal > 0 ? "RACE_PLACED" : "LOSER", resultMedal > 0 ? "HUD_AWARDS" : "HUD_AWARDS", true);
        }

        private void Result(Test test, int now)
        {
            Game.DisableAllControlsThisFrame(); Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
            float a2 = Ds.EaseOut((now - t0) / 400f), x = 0.08f;
            Ds.HGrad(0f, 0f, 0.62f, 1f, Ds.A(Ds.Bg, 0.92f * a2), Ds.A(Ds.Bg, 0f), 10);
            Ds.T(test.Code + "  " + test.Name.ToUpperInvariant(), x, 0.22f, 0.36f, Ds.A(Ds.Cyan, a2), Ds.Display);
            Ds.T(resultMedal > 0 ? Medals[resultMedal] : "FAILED", x, 0.25f, 2.2f, Ds.A(resultMedal > 0 ? MedalCol[resultMedal] : Ds.Red, a2), Ds.Display);
            Ds.T(resultText ?? Fmt(test.Kind, resultValue), x, 0.44f, 0.7f, Ds.A(Ds.Text, a2), Ds.Display);
            Buttons.Draw(Control.FrontendAccept, "Retry", Control.FrontendX, cur + 1 < Tests.Length ? "Next test" : "", Control.FrontendCancel, "Leave");
            if (now - t0 < 700) return;
            if (Input.JustPressed(Control.FrontendAccept)) { int i = cur; state = 0; ClearCones(); Begin(i); }
            else if (Input.JustPressed(Control.FrontendX) && cur + 1 < Tests.Length && !(cur + 1 >= 2 && LicenseLevel == "None")) { int i = cur + 1; state = 0; ClearCones(); Begin(i); }
            else if (Input.JustPressed(Control.FrontendCancel)) Abort();
        }
    }
}
