using System.Collections.Generic;
using System;
using System.Drawing;
using GTA;
using GTA.UI;
using MidnightClub.Persistence;
using MidnightClub.Vehicles;

namespace MidnightClub.UI
{
    /// <summary>In-drive HUD: wordmark + cash/REP top right (with animated deltas), and a clean digital speedometer bottom right.</summary>
    public sealed class MainHUD
    {
        private readonly float hx, hy;
        private string deltaText; private Color deltaColor; private int deltaAt = -10000;
        private float rpmShown, rpmRealShown, speedShown, cashShown = -1, xpShown;
        private string xpText; private int xpAt = -10000; private long xpAcc;

        public MainHUD(float x, float y)
        {
            hx = x; hy = y;
            Core.EventBus.Subscribe<Economy.MoneyChangedEvent>(e =>
            {
                if (e.CashDelta == 0 && e.RepDelta == 0) return;
                deltaText = (e.CashDelta != 0 ? (e.CashDelta > 0 ? "+$" : "-$") + Math.Abs(e.CashDelta).ToString("N0") : "") + (e.RepDelta > 0 ? (e.CashDelta != 0 ? "   " : "") + "+" + e.RepDelta + " REP" : "");
                deltaColor = e.CashDelta < 0 ? Ds.Red : Ds.Lime;
                deltaAt = Game.GameTime;
            });
            Core.EventBus.Subscribe<Career.XpEvent>(e => { xpAcc = Game.GameTime - xpAt < 2000 ? xpAcc + e.Amount : e.Amount; xpText = "+" + xpAcc.ToString("N0") + " XP"; xpAt = Game.GameTime; });
        }

        public void Draw(SaveData d, VehicleManager vehicles)
        {
            if (d == null) return;
            Hud.HideComponentThisFrame(HudComponent.Cash);
            Hud.HideComponentThisFrame(HudComponent.WeaponIcon);
            Hud.HideComponentThisFrame(HudComponent.VehicleName);
            Hud.HideComponentThisFrame(HudComponent.AreaName);
            Hud.HideComponentThisFrame(HudComponent.StreetName);

            // top right: glass capsule with level ring, rolling cash, REP + tier (no wordmark)
            var pr = Career.Progression.Instance;
            if (cashShown < 0) cashShown = d.Cash;
            cashShown = Math.Abs(d.Cash - cashShown) < 1f ? d.Cash : Ds.Damp(cashShown, d.Cash, 6f);
            string cash = "$" + ((long)Math.Round(cashShown)).ToString("N0");
            string sub = d.Rep.ToString("N0") + " REP  ·  " + (Career.CareerManager.Instance != null ? Career.CareerManager.Instance.TierName.ToUpperInvariant() : "");
            float ch = Th.Px(56f), ringD = Th.Px(42f);
            float tcw = Math.Max(Ds.Width(cash, 0.54f, Ds.Display), Ds.Width(sub, 0.22f, Ds.Display));
            float cw = Ds.Px(Th.Px(14f)) + Ds.Px(ringD) + Ds.Px(Th.Px(12f)) + tcw + Ds.Px(Th.Px(22f));
            float rx = hx, cx0 = rx - cw, cy0 = hy, cy = cy0 + ch / 2f;
            bool racing = Racing.RaceManager.IsRacing;   // the race card owns the top right during races
            if (!racing) Kit.Glass(cx0, cy0, cw, ch, ch * 1080f / 2f, 0.92f);
            float rcx = cx0 + Ds.Px(Th.Px(12f)) + Ds.Px(ringD / 2f);
            if (pr != null && !racing)
            {
                xpShown = Ds.Damp(xpShown, pr.LevelProgress, 5f);
                Textures.Draw("ring_100", rcx, cy, ringD, Th.Track);
                VectorIcons.Ring(rcx, cy, ringD, 0.006f, Ds.Clamp01(xpShown), Coral);
                Ds.TM(pr.Level.ToString(), rcx, cy, 0.44f, Th.Text, Ds.Display, Alignment.Center);
            }
            float tx = rcx + Ds.Px(ringD / 2f) + Ds.Px(Th.Px(12f));
            if (!racing) { Ds.TM(cash, tx, cy - Th.Px(8f), 0.54f, Th.Text, Ds.Display); Ds.TM(sub, tx, cy + Th.Px(14f), 0.22f, Th.Sub, Ds.Display); }
            float xa = (Game.GameTime - xpAt) / 1000f;
            if (xpText != null && xa < 2.2f) Kit.Chip(xpText, rx - Ds.Width(xpText, 0.26f, Ds.Display) - Ds.Px(Th.Px(16f)), cy0 + ch + Th.Px(20f) - Ds.EaseOut(Math.Min(1f, xa / 0.3f)) * Th.Px(6f), Ds.A(Coral, xa > 1.6f ? (2.2f - xa) / 0.6f : 1f), Ds.A(Color.White, xa > 1.6f ? (2.2f - xa) / 0.6f : 1f), 0.26f);
            tx = rx;
            if (!Garages.GarageManager.InsideAny) Minimap.Draw();
            float age = (Game.GameTime - deltaAt) / 1000f;
            if (deltaText != null && age < 3.2f)
            {
                float a = age < 0.2f ? age / 0.2f : age > 2.6f ? (3.2f - age) / 0.6f : 1f;
                float rise = Ds.EaseOut(Math.Min(1f, age / 0.35f)) * 0.012f;
                Ds.T(deltaText, rx, hy + Th.Px(110f) - rise, 0.4f, Ds.A(deltaColor, a), Ds.Display, Alignment.Right);
            }
            var v = vehicles.Current;
            if (v != null && v.Exists() && Game.Player.Character.IsInVehicle(v)) Speedo(v, vehicles.CurrentRecord);
        }

        private float speedFx;
        /// <summary>NFS Unbound-style speed lines on the screen edges past ~170 km/h.</summary>
        private void SpeedLines(Vehicle v)
        {
            float kmh = v.Speed * 3.6f;
            speedFx = Ds.Damp(speedFx, Math.Max(0f, Math.Min(1f, (kmh - 170f) / 130f)), 4f);
            if (speedFx < 0.02f) return;
            Ds.Streaks(0f, 0.05f, 0.16f, 0.9f, Color.White, 0.35f * speedFx, 6, 3.5f);
            Ds.Streaks(0.84f, 0.05f, 0.16f, 0.9f, Color.White, 0.35f * speedFx, 6, 3.5f);
            Ds.HGrad(0f, 0f, 0.12f, 1f, Ds.A(Ds.Bg, 0.35f * speedFx), Ds.A(Ds.Bg, 0f), 4);
            Ds.HGrad(0.88f, 0f, 0.12f, 1f, Ds.A(Ds.Bg, 0f), Ds.A(Ds.Bg, 0.35f * speedFx), 4);
        }

        public static float RowX, RowY, RowW;      // next free row under the speedometer (for the ability meter)
        private float boostShown;
        private static readonly Color Coral = Color.FromArgb(255, 255, 92, 102);
        private static readonly Color Dim = Color.FromArgb(70, 255, 255, 255);

        // ---- reference-exact speedometer: every element is a texture laid out in a 240 x 186 "reference pixel" box
        private const float RefW = 240f, RefH = 186f;
        private static float su, sx0, sy0;                       // 1 ref px in screen-height units; box origin
        private static float SX(float px) { return sx0 + Ds.Px(px * su); }
        private static float SY(float px) { return sy0 + px * su; }
        private static void Box(string tex, float x, float y, float w, float h, Color c) { Textures.DrawBox(tex, SX(x), SY(y), Ds.Px(w * su), h * su, c); }
        private static float GAdv(string p, char ch) { float v; return SpeedoGlyphs.Adv.TryGetValue(p + ":" + ch, out v) ? v : 0f; }
        /// <summary>Draws a string from the pre-rendered glyph set (gb/gm/gs). align: 0 left, 1 centre, 2 right.</summary>
        private static void Glyphs(string p, string text, float x, float baseline, int align, Color c, float kern = 0f)
        {
            float tot = 0f; foreach (char ch in text) tot += GAdv(p, ch) + kern; tot -= kern;
            float cap = GAdv(p, 'H');
            float cx = align == 2 ? x - tot : align == 1 ? x - tot / 2f : x;
            foreach (char ch in text)
            {
                float adv = GAdv(p, ch); if (adv <= 0f) continue;
                string n = ch == '.' ? "dot" : ch == '\u2022' ? "bul" : ch.ToString();
                Box(p + "_" + n, cx - cap * 0.08f, baseline - cap * 1.18f, adv + cap * 0.16f, cap * 1.36f, c);   // glyph PNGs carry 18% / 8% padding
                cx += adv + kern;
            }
        }
        private static readonly Color GearPink = Color.FromArgb(255, 232, 128, 140);
        private static readonly Color RevRed = Color.FromArgb(255, 236, 64, 84);
        private static readonly Color BoostFill = Color.FromArgb(235, 190, 203, 222);
        private static readonly Color Track = Color.FromArgb(255, 255, 255, 255);
        private float boostPeak, boostPeakT;
        private int rowsDrawn; private float rowBase;

        /// <summary>Draws one bar row in the speedometer's row style (NOS / ability meter). Row 0 sits where the reference NOS row is.</summary>
        public static void Row(string label, float fill, Color fillC)
        {
            if (inst == null || su <= 0f) return;
            float dy = inst.rowBase + (inst.rowsDrawn - 1) * 22f;
            string tex = "spd_ab_" + label.ToLowerInvariant();
            if (label == "NOS") tex = "spd_nos";
            if (Textures.Has(tex)) Box(tex, 0f, dy, RefW, RefH, Color.White);
            fill = Math.Max(0f, Math.Min(1f, fill));
            if (fill > 0.005f) Box("px", 89f, 161f + dy, 131f * fill, 6f, fillC);
            inst.rowsDrawn++;
        }
        private static MainHUD inst;

        /// <summary>Forza Horizon-style speedometer, laid out pixel-for-pixel after the reference: curved rev band with a
        /// red-line zone, "• RPM" readout, pink gear, big speed, boost box gauge (turbo cars) and a slim NOS bar.</summary>
        private void Speedo(Vehicle v, OwnedVehicle r)
        {
            inst = this;
            if (Core.Config.Current != null && Core.Config.Current.SpeedEffects) SpeedLines(v);
            speedShown = Ds.Damp(speedShown, v.Speed * 3.6f, 14f);
            float rpmT = v.CurrentRPM;
            var dl = Vehicles.Drivetrain.Current;
            if (Physics.Native.DriveActive && dl != null && dl.Spec != null && dl.Spec.Redline > dl.Spec.Idle + 500f)   // the simulated engine, not GTA's
                rpmT = 0.2f + 0.8f * (Vehicles.Drivetrain.EngineRpm - dl.Spec.Idle) / (dl.Spec.Redline - dl.Spec.Idle);
            rpmShown = Ds.Damp(rpmShown, Math.Max(0f, Math.Min(1f, rpmT)), 18f);
            int kmh = (int)Math.Round(speedShown);
            bool turbo = Vehicles.Drivetrain.Current != null ? Vehicles.Drivetrain.HasBoost : r != null && r.Toggles != null && r.Toggles.Contains(18);
            bool nos = r != null && r.NosLevel > 0;
            bool ability = Gameplay.Abilities.Instance != null && Gameplay.Abilities.Instance.Selected != null;
            int extraRows = (nos ? 1 : 0) + (ability ? 1 : 0);
            // layout: 1 ref px = 1.12 px at 1080p; the box is anchored bottom-right, rows below the boost row grow downwards
            su = 1.12f / 1080f;
            float rowsBottom = turbo ? 148f : 112f;
            if (extraRows > 0) rowsBottom = (turbo ? 168f : 132f) + (extraRows - 1) * 22f;
            sx0 = 0.985f - Ds.Px(RefW * su);
            sy0 = 0.965f - rowsBottom * su;
            // rev band
            Box("spd_frame", 0f, 0f, RefW, RefH, Color.White);
            int step = Math.Max(0, Math.Min(50, (int)Math.Round(rpmShown * 50f))) * 2;
            Box("spd_rev_" + step.ToString("000"), 0f, 0f, RefW, RefH, Color.White);
            var dts = Vehicles.Drivetrain.Current;
            rpmRealShown = Ds.Damp(rpmRealShown, dts != null ? Vehicles.Drivetrain.EngineRpm : rpmShown * 9000f, 18f);
            float rpmReal = rpmRealShown;
            int rpmNum = (int)Math.Round(rpmReal / 100f) * 100;
            string rpmTxt = rpmNum.ToString();
            Glyphs("gs", rpmTxt, 195f, 57f, 2, RevRed);
            float tw = 0f; foreach (char ch in rpmTxt) tw += GAdv("gs", ch);
            Glyphs("gs", "\u2022", 195f - tw - 4f, 57f, 2, RevRed);
            // gear + speed
            int gear = v.CurrentGear;
            string g = v.Speed < 0.5f && v.Throttle <= 0 ? "N" : gear == 0 ? "R" : gear.ToString();
            if (Physics.Native.DriveActive && MidnightClub.Physics.SimPhysics.Instance != null && MidnightClub.Physics.SimPhysics.Instance.OwnBoxOn)
            {   // the real gearbox: selector and gear
                var sp = MidnightClub.Physics.SimPhysics.Instance; int og = Physics.SimWorld.OwnGear;
                if (sp.ManualMode) g = og < 0 ? "R" : og == 0 ? "N" : og.ToString();
                else { int s0 = sp.Selector; g = s0 == 3 || s0 == 4 ? Physics.SimPhysics.SelNames[s0] + Math.Max(1, og) : Physics.SimPhysics.SelNames[s0]; }
            }
            // gear: white, the shift light colour near the limiter; letter and number set tight, kept left of the speed digits
            float lim = dts != null && Vehicles.Drivetrain.Current.Spec != null ? Vehicles.Drivetrain.Current.Spec.Redline : 0f;
            float near = lim > 1000f ? (rpmReal - lim * 0.86f) / (lim * 0.1f) : (rpmShown - 0.86f) / 0.1f;
            Color gc = near <= 0f ? Color.White : near >= 1f ? GearPink : Color.FromArgb(255, 255, (int)(255 - 23 * near), (int)(255 - 115 * near));
            Glyphs("gb", g, g.Length > 1 ? 38f : 44f, 112f, 1, gc, g.Length > 1 ? -6f : 0f);
            Glyphs("gb", kmh.ToString(), 188f, 112f, 2, Color.White);
            // boost
            float y = 0f;
            if (turbo)
            {
                float maxBar = 1.0f + 0.15f * r.EcuLevel + (Vehicles.Setup.Has(r, "als") ? 0.2f : 0f);
                float want = Math.Max(0f, v.Throttle) * Math.Max(0f, Math.Min(1f, (rpmShown - 0.25f) / 0.45f)) * maxBar;
                if (Vehicles.Setup.Has(r, "als") && v.Throttle <= 0.05f) want = 0.35f * maxBar;
                if (dts != null) want = Vehicles.Drivetrain.BoostBar;          // the real boost from the engine model (lag, spool, supercharger)
                boostShown = Ds.Damp(boostShown, want, want > boostShown ? (Vehicles.Setup.Has(r, "als") ? 9f : 3.5f) : 6f);
                if (boostShown >= boostPeak) { boostPeak = boostShown; boostPeakT = 1.2f; }
                else { boostPeakT -= Ds.Dt; if (boostPeakT <= 0f) boostPeak = Math.Max(boostShown, boostPeak - Ds.Dt * 0.8f); }
                float span = Math.Max(1.2f, maxBar * 1.15f);   // the gauge spans the car's own boost range
                Box("spd_boost", 0f, 0f, RefW, RefH, Color.White);
                float f = Math.Min(1f, boostShown / span), pk = Math.Min(1f, boostPeak / span);
                const float gx0 = 93f, gw = 54f;             // the gauge stays left of the number and the BAR label
                Box("px", gx0, 136f, gw, 1f, Color.FromArgb(90, 255, 255, 255));
                if (f > 0.005f) Box("px", gx0, 130f, gw * f, 13f, BoostFill);
                float tx = gx0 + gw * pk;
                Box("px", tx, 129.5f, 1.4f, 14f, Color.White);
                Box("px", 150f, 130f, 1f, 13f, Color.FromArgb(70, 255, 255, 255));
                Glyphs("gm", boostShown.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), 188f, 143f, 2, Color.White);
            }
            // driver aid lights: light up while traction control, ABS or stability management is working
            var sim = Physics.SimPhysics.Instance;
            if (sim != null && dts != null)
            {
                float ax = 4f;
                foreach (var a in new[] { new KeyValuePair<string, bool>("TCS", sim.TcsActive), new KeyValuePair<string, bool>("ABS", sim.AbsActive), new KeyValuePair<string, bool>("ESC", sim.EscActive) })
                {
                    Ds.T(a.Key, SX(ax), SY(39f), 0.17f, a.Value ? Ds.Amber : Color.FromArgb(60, 255, 255, 255), Ds.Display);
                    ax += 17f;
                }
                // tyre temperatures (front left, front right, rear left, rear right): blue cold, green in the window, amber / red hot
                if (Physics.SimWorld.PlayerActive && Physics.SimWorld.TyreTemp[0] > 1f)
                {
                    float opt = Physics.SimWorld.OptTemp, bx = SX(6f), by = SY(14f), bw = Ds.Px(7f * su), bh = 10f * su, gx = Ds.Px(2f * su), gy = 2f * su;
                    for (int i = 0; i < 4; i++)
                    {
                        float tc = Physics.SimWorld.TyreTemp[i], d = tc - opt;
                        Color c = d < -25f ? Color.FromArgb(220, 70, 150, 255) : d < -10f ? Color.FromArgb(220, 90, 210, 230) : d <= 15f ? Color.FromArgb(220, 80, 220, 110) : d <= 30f ? Color.FromArgb(230, 255, 180, 40) : Color.FromArgb(240, 255, 60, 50);
                        float wear = Physics.SimWorld.TyreWear[i];
                        float tx0 = bx + (i % 2) * (bw + gx), ty0 = by + (i / 2) * (bh + gy);
                        Ds.Rect(tx0, ty0, bw, bh, Color.FromArgb(120, 0, 0, 0));
                        Ds.Rect(tx0, ty0 + bh * Math.Min(0.9f, wear), bw, bh * (1f - Math.Min(0.9f, wear)), c);
                    }
                    // brake discs per axle (thin bars right of the tyres): grey cool, amber getting hot, red fading
                    float fade = Physics.SimWorld.BrakeFadeC;
                    for (int ax2 = 0; ax2 < 2; ax2++)
                    {
                        float bt = Math.Max(Physics.SimWorld.BrakeTemp(ax2 * 2), Physics.SimWorld.BrakeTemp(ax2 * 2 + 1));
                        Color bc = bt < fade - 250f ? Color.FromArgb(150, 200, 200, 200) : bt < fade - 80f ? Color.FromArgb(230, 255, 180, 40) : bt < fade ? Color.FromArgb(240, 255, 110, 40) : Color.FromArgb(250, 255, 40, 40);
                        Ds.Rect(bx + 2f * (bw + gx), by + ax2 * (bh + gy), Ds.Px(2.5f * su), bh, bc);
                    }
                }
            }
            // NOS / ability rows: the reference NOS row sits 36 ref px under the boost row; without turbo the rows move up
            rowBase = turbo ? 0f : -36f;
            rowsDrawn = 1;
            if (nos) Row("NOS", Gameplay.Abilities.NosFill, Gameplay.Abilities.NosActive ? Ds.Cyan : Color.White);
            RowX = 1f; RowY = 1f; RowW = 0f;              // Abilities draws its row through MainHUD.Row()
            // car line above the arc
            string name = r == null ? "" : r.Title();
            float right = 0.985f, ny = SY(20f) - 0.026f;
            float pbw = r != null ? Vehicles.PerformanceIndex.Badge(Vehicles.PerformanceIndex.Of(r), right - Ds.Px(0.24f), ny, 0.02f) + Ds.Px(0.008f) : 0f;
            Ds.T(Ds.Fit(name.ToUpperInvariant() + (r != null ? "  ·  " + Career.Mastery.Label(r) : ""), 0.24f, Ds.Px(0.24f) - pbw, Ds.Display), right - Ds.Px(0.24f) + pbw, ny + 0.001f, 0.24f, Color.FromArgb(200, 255, 255, 255), Ds.Display);
            if (WorldSim.Weather.Wetness > 0.3f) Ds.T("WET ROADS", right, ny + 0.001f, 0.24f, Ds.A(Ds.Cyan, Math.Min(1f, (WorldSim.Weather.Wetness - 0.3f) * 4f)), Ds.Display, Alignment.Right);
        }
    }
}
