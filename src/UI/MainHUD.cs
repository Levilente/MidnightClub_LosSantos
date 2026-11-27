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
        private float rpmShown, speedShown, cashShown = -1, xpShown;
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

            // top right: wordmark, rolling cash, REP + tier
            float rx = hx;
            Ds.Logo(rx, hy, 0.62f, 1f, Alignment.Right, false);
            if (cashShown < 0) cashShown = d.Cash;
            cashShown = Math.Abs(d.Cash - cashShown) < 1f ? d.Cash : Ds.Damp(cashShown, d.Cash, 6f);
            Ds.T("$" + ((long)Math.Round(cashShown)).ToString("N0"), rx, hy + 0.045f, 0.5f, Ds.Text, Ds.Display, Alignment.Right);
            string tier = Career.CareerManager.Instance != null ? Career.CareerManager.Instance.TierName.ToUpperInvariant() : "";
            float tw = Ds.Width(tier, 0.24f), rw = Ds.Width(d.Rep.ToString("N0") + " REP", 0.27f, Ds.Display);
            Ds.T(tier, rx, hy + 0.092f, 0.24f, Ds.Muted, Ds.Body, Alignment.Right);
            Ds.T(d.Rep.ToString("N0") + " REP", rx - tw - Ds.Px(0.012f), hy + 0.089f, 0.27f, Coral, Ds.Display, Alignment.Right);
            Ds.Dot(rx - tw - Ds.Px(0.006f), hy + 0.1005f, 0.005f, Ds.Faint);
            var pr = Career.Progression.Instance;
            if (pr != null)
            {
                float bw = Ds.Px(0.13f), by = hy + 0.122f;
                Ds.T("LV " + pr.Level, rx - bw - Ds.Px(0.008f), by - 0.009f, 0.27f, Ds.Text, Ds.Display, Alignment.Right);
                xpShown = Ds.Damp(xpShown, pr.LevelProgress, 5f);
                Ds.Rect(rx - bw, by, bw, 0.004f, Dim);
                Ds.Rect(rx - bw, by, bw * Ds.Clamp01(xpShown), 0.004f, Color.White);
                float xa = (Game.GameTime - xpAt) / 1000f;
                if (xpText != null && xa < 2.2f) Ds.T(xpText, rx, by + 0.008f - Ds.EaseOut(Math.Min(1f, xa / 0.3f)) * 0.006f, 0.27f, Ds.A(Coral, xa > 1.6f ? (2.2f - xa) / 0.6f : 1f), Ds.Display, Alignment.Right);
            }
            float age = (Game.GameTime - deltaAt) / 1000f;
            if (deltaText != null && age < 3.2f)
            {
                float a = age < 0.2f ? age / 0.2f : age > 2.6f ? (3.2f - age) / 0.6f : 1f;
                float rise = Ds.EaseOut(Math.Min(1f, age / 0.35f)) * 0.012f;
                Ds.T(deltaText, rx, hy + 0.15f - rise, 0.34f, Ds.A(deltaColor, a), Ds.Display, Alignment.Right);
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

        /// <summary>Forza Horizon-style speedometer: a curved rev arc with a red line zone, the gear in coral, a big
        /// speed readout, a boost gauge for turbo cars and a slim nitrous bar.</summary>
        private void Speedo(Vehicle v, OwnedVehicle r)
        {
            if (Core.Config.Current != null && Core.Config.Current.SpeedEffects) SpeedLines(v);
            float right = 0.965f, bottom = 0.935f;
            speedShown = Ds.Damp(speedShown, v.Speed * 3.6f, 14f);
            rpmShown = Ds.Damp(rpmShown, Math.Max(0f, Math.Min(1f, v.CurrentRPM)), 18f);
            int kmh = (int)Math.Round(speedShown);
            bool turbo = r != null && r.Toggles != null && r.Toggles.Contains(18);
            bool nos = r != null && r.NosLevel > 0;
            int rows = (turbo ? 1 : 0) + (nos ? 1 : 0) + (Gameplay.Abilities.Instance != null && Gameplay.Abilities.Instance.Selected != null ? 1 : 0);
            float rowH = 0.034f;
            float ah = 0.13f, aw = Ds.Px(ah * 1.7778f);
            float bx = right - aw, by = bottom - rows * rowH - 0.1f - ah * 0.62f;
            bool red = rpmShown > 0.86f;
            // rev arc
            Textures.DrawBox("tach_track", bx, by, aw, ah, Dim);
            Textures.DrawBox("tach_red", bx, by, aw, ah, Color.FromArgb(red ? 230 : 120, 255, 92, 102));
            int step = Math.Max(0, Math.Min(50, (int)Math.Round(rpmShown * 50f))) * 2;
            Textures.DrawBox("tach_" + step.ToString("000"), bx, by, aw, ah, red ? Coral : Color.White);
            int rpmNum = (int)(rpmShown * 90f) * 100;
            Ds.T("•  " + rpmNum.ToString("N0") + " RPM", right - Ds.Px(0.004f), by + ah * 0.3f, 0.24f, red ? Coral : Color.FromArgb(200, 255, 255, 255), Ds.Display, Alignment.Right);
            // gear + speed
            int gear = v.CurrentGear;
            string g = v.Speed < 0.5f && v.Throttle <= 0 ? "N" : gear == 0 ? "R" : gear.ToString();
            float ty = by + ah * 0.52f;
            Ds.T(g, bx + aw * 0.14f, ty, 1.35f, Coral, Ds.Display, Alignment.Center);
            Ds.T(kmh.ToString(), right - Ds.Px(0.052f), ty, 1.35f, Color.White, Ds.Display, Alignment.Right);
            Ds.T("KM/H", right - Ds.Px(0.047f), ty + 0.054f, 0.3f, Color.FromArgb(220, 255, 255, 255), Ds.Display);
            // rows: boost, nos, (ability meter by Abilities)
            float rx = bx + aw * 0.04f, rw = right - rx, ry = ty + 0.105f;
            if (turbo)
            {
                float maxBar = 1.0f + 0.15f * r.EcuLevel + (Vehicles.Setup.Has(r, "als") ? 0.2f : 0f);
                float want = Math.Max(0f, v.Throttle) * Math.Max(0f, Math.Min(1f, (rpmShown - 0.25f) / 0.45f)) * maxBar;
                if (Vehicles.Setup.Has(r, "als") && v.Throttle <= 0.05f) want = 0.35f * maxBar;
                boostShown = Ds.Damp(boostShown, want, want > boostShown ? (Vehicles.Setup.Has(r, "als") ? 9f : 3.5f) : 6f);
                Ds.T("BOOST", rx, ry - 0.012f, 0.3f, Color.FromArgb(230, 255, 255, 255), Ds.Display);
                float gx = rx + Ds.Px(0.065f), gw = rw - Ds.Px(0.065f) - Ds.Px(0.06f);
                Ds.Rect(gx, ry - 0.008f, gw, 0.002f, Color.White); Ds.Rect(gx, ry + 0.012f, gw, 0.002f, Color.White);
                Ds.Rect(gx, ry - 0.008f, Ds.Px(0.002f), 0.022f, Color.White); Ds.Rect(gx + gw - Ds.Px(0.002f), ry - 0.008f, Ds.Px(0.002f), 0.022f, Color.White);
                Ds.Rect(gx + Ds.Px(0.004f), ry - 0.004f, (gw - Ds.Px(0.008f)) * Math.Min(1f, boostShown / (maxBar * 1.1f)), 0.012f, Color.FromArgb(210, 190, 205, 230));
                Ds.T(boostShown.ToString("0.0") + " BAR", right, ry - 0.012f, 0.3f, Color.White, Ds.Display, Alignment.Right);
                ry += rowH;
            }
            if (nos)
            {
                Ds.T("NOS", rx, ry - 0.012f, 0.3f, Gameplay.Abilities.NosActive ? Ds.Cyan : Color.FromArgb(230, 255, 255, 255), Ds.Display);
                float nx = rx + Ds.Px(0.065f), nw = rw - Ds.Px(0.065f);
                Ds.Rect(nx, ry, nw, 0.006f, Dim);
                Ds.Rect(nx, ry, nw * Gameplay.Abilities.NosFill, 0.006f, Gameplay.Abilities.NosActive ? Ds.Cyan : Color.White);
                ry += rowH;
            }
            RowX = rx; RowW = rw; RowY = ry;
            // car line above the arc
            string name = r == null ? "" : (string.IsNullOrEmpty(r.Nickname) ? r.Model : r.Nickname);
            float ny = by - 0.03f;
            float pbw = r != null ? Vehicles.PerformanceIndex.Badge(Vehicles.PerformanceIndex.Of(r), right - Ds.Px(0.24f), ny, 0.02f) + Ds.Px(0.008f) : 0f;
            Ds.T(Ds.Fit(name.ToUpperInvariant() + (r != null ? "  ·  " + Career.Mastery.Label(r) : ""), 0.24f, Ds.Px(0.24f) - pbw, Ds.Display), right - Ds.Px(0.24f) + pbw, ny + 0.001f, 0.24f, Color.FromArgb(200, 255, 255, 255), Ds.Display);
            if (WorldSim.Weather.Wetness > 0.3f) Ds.T("WET ROADS", right, ny + 0.001f, 0.24f, Ds.A(Ds.Cyan, Math.Min(1f, (WorldSim.Weather.Wetness - 0.3f) * 4f)), Ds.Display, Alignment.Right);
        }
    }
}
