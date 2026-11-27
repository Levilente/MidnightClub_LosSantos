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
            Ds.T(d.Rep.ToString("N0") + " REP", rx - tw - Ds.Px(0.012f), hy + 0.089f, 0.27f, Ds.Violet, Ds.Display, Alignment.Right);
            Ds.Dot(rx - tw - Ds.Px(0.006f), hy + 0.1005f, 0.005f, Ds.Faint);
            var pr = Career.Progression.Instance;
            if (pr != null)
            {
                float bw = Ds.Px(0.13f), by = hy + 0.122f;
                Ds.T("LV " + pr.Level, rx - bw - Ds.Px(0.008f), by - 0.009f, 0.27f, Ds.Text, Ds.Display, Alignment.Right);
                xpShown = Ds.Damp(xpShown, pr.LevelProgress, 5f);
                Ds.Skew(rx - bw, by, bw, 0.005f, Ds.Px(0.0012f), Ds.Stroke);
                Ds.Skew(rx - bw, by, bw * Ds.Clamp01(xpShown), 0.005f, Ds.Px(0.0012f), Ds.Violet);
                float xa = (Game.GameTime - xpAt) / 1000f;
                if (xpText != null && xa < 2.2f) Ds.T(xpText, rx, by + 0.008f - Ds.EaseOut(Math.Min(1f, xa / 0.3f)) * 0.006f, 0.27f, Ds.A(Ds.Violet, xa > 1.6f ? (2.2f - xa) / 0.6f : 1f), Ds.Display, Alignment.Right);
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

        private void Speedo(Vehicle v, OwnedVehicle r)
        {
            float right = 0.965f, bottom = 0.93f;
            speedShown = Ds.Damp(speedShown, v.Speed * 3.6f, 14f);
            int kmh = (int)Math.Round(speedShown);
            // speed
            Ds.T(kmh.ToString(), right - Ds.Px(0.05f), bottom - 0.115f, 1.25f, Ds.Text, Ds.Display, Alignment.Right);
            Ds.T("KM/H", right - Ds.Px(0.047f), bottom - 0.062f, 0.27f, Ds.Muted, Ds.Body);
            // gear badge
            int gear = v.CurrentGear;
            string g = v.Speed < 0.5f && v.Throttle <= 0 ? "N" : gear == 0 ? "R" : gear.ToString();
            float gs = 0.042f, gx = right - Ds.Px(0.042f), gy = bottom - 0.105f;
            bool red = rpmShown > 0.93f;
            Ds.Skew(gx, gy, Ds.Px(gs), gs, Ds.SlantFor(gs), red ? Ds.Red : Ds.Violet);
            Ds.T(g, gx + Ds.Px(gs) / 2 + Ds.SlantFor(gs) / 2, gy + 0.001f, 0.6f, Color.White, Ds.Display, Alignment.Center);
            // rev bar: 24 slim segments, violet -> amber -> red, with a lit "head"
            rpmShown = Ds.Damp(rpmShown, v.CurrentRPM, 18f);
            int segs = 24; float bw = Ds.Px(0.2f), bx = right - bw, by = bottom - 0.028f, sw = bw / segs;
            for (int i = 0; i < segs; i++)
            {
                float t = (i + 0.5f) / segs; bool lit = t <= rpmShown;
                var c = t > 0.88f ? Ds.Red : t > 0.72f ? Ds.Amber : Ds.Violet;
                float h = 0.006f + 0.008f * t;
                Ds.Skew(bx + i * sw, by + 0.014f - h, sw - Ds.Px(0.0022f), h, Ds.SlantFor(h), lit ? c : Ds.A(Ds.Stroke, 0.8f));
            }
            // car name
            string name = r == null ? "" : (string.IsNullOrEmpty(r.Nickname) ? r.Model : r.Nickname);
            Ds.T(name.ToUpperInvariant() + (r != null ? "  ·  " + Career.Mastery.Label(r) : ""), bx, bottom - 0.052f, 0.24f, Ds.Muted, Ds.Body);
        }
    }
}
