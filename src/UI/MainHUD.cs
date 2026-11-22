using System;
using System.Drawing;
using GTA;
using GTA.UI;
using MidnightClub.Persistence;
using MidnightClub.Vehicles;

namespace MidnightClub.UI
{
    /// <summary>Minimal Phase 1 HUD: brand, cash, REP, current car. Full visual identity comes in Phase 11.</summary>
    public sealed class MainHUD
    {
        private static readonly Color Neon = Color.FromArgb(255, 190, 90, 255);
        private static readonly Color Cyan = Color.FromArgb(255, 80, 220, 255);
        private readonly TextElement brand, cash, rep, car, delta;
        private int deltaUntil;

        public MainHUD(float x, float y)
        {
            // TextElement positions are in the 1280x720 scaled UI space.
            float px = x * 1280f, py = y * 720f;
            brand = new TextElement("MIDNIGHT CLUB", new PointF(px, py), 0.55f, Neon, GTA.UI.Font.Pricedown, Alignment.Right, true, true);
            cash = new TextElement("", new PointF(px, py + 30), 0.45f, Color.White, GTA.UI.Font.ChaletLondon, Alignment.Right, true, true);
            rep = new TextElement("", new PointF(px, py + 52), 0.45f, Cyan, GTA.UI.Font.ChaletLondon, Alignment.Right, true, true);
            delta = new TextElement("", new PointF(px, py + 96), 0.42f, Color.White, GTA.UI.Font.ChaletLondon, Alignment.Right, true, true);
            Core.EventBus.Subscribe<Economy.MoneyChangedEvent>(e =>
            {
                if (e.CashDelta == 0 && e.RepDelta == 0) return;
                delta.Caption = (e.CashDelta >= 0 ? "~g~+$" : "~r~-$") + Math.Abs(e.CashDelta).ToString("N0") + (e.RepDelta > 0 ? "  ~b~+" + e.RepDelta + " REP" : "");
                deltaUntil = Game.GameTime + 3500;
            });
            car = new TextElement("", new PointF(px, py + 74), 0.38f, Color.FromArgb(220, 220, 220, 220), GTA.UI.Font.ChaletLondon, Alignment.Right, true, true);
        }

        private float rpmShown;

        /// <summary>Midnight Club speedometer: big digital speed, gear, segmented rev bar with a redline, car name.</summary>
        private void DrawGauge(Vehicle v, OwnedVehicle r)
        {
            float x = 0.80f, y = 0.80f, w = 0.18f, h = 0.13f;
            Rect(x, y, w, h, Color.FromArgb(185, 8, 6, 16));
            Rect(x, y, w, 0.004f, Neon);
            int kmh = (int)(v.Speed * 3.6f);
            gaugeSpeed.Caption = kmh.ToString();
            gaugeSpeed.Position = new PointF((x + w - 0.045f) * 1280f, (y + 0.008f) * 720f);
            gaugeSpeed.ScaledDraw();
            gaugeUnit.Position = new PointF((x + w - 0.04f) * 1280f, (y + 0.05f) * 720f); gaugeUnit.ScaledDraw();
            int gear = v.CurrentGear;
            gaugeGear.Caption = v.Speed < 0.5f ? "N" : gear == 0 ? "R" : gear.ToString();
            gaugeGear.Position = new PointF((x + 0.012f) * 1280f, (y + 0.006f) * 720f); gaugeGear.ScaledDraw();
            rpmShown += (v.CurrentRPM - rpmShown) * Math.Min(1f, Game.LastFrameTime * 14f);
            int segs = 20; float sx = x + 0.012f, sy = y + 0.085f, sw = (w - 0.024f) / segs;
            for (int i = 0; i < segs; i++)
            {
                bool lit = (i + 0.5f) / segs <= rpmShown;
                var c = i >= segs - 3 ? Color.FromArgb(lit ? 255 : 70, 255, 60, 80) : i >= segs - 7 ? Color.FromArgb(lit ? 255 : 70, 255, 190, 60) : Color.FromArgb(lit ? 255 : 70, 170, 90, 255);
                Rect(sx + i * sw + 0.001f, sy, sw - 0.002f, 0.016f, c);
            }
            car.Position = new PointF((x + 0.012f) * 1280f, (y + 0.104f) * 720f);
            car.Alignment = Alignment.Left;
            car.Caption = r == null ? "" : (string.IsNullOrEmpty(r.Nickname) ? r.Model.ToUpperInvariant() : r.Nickname.ToUpperInvariant());
            car.Scale = 0.3f;
            car.ScaledDraw();
        }

        private readonly TextElement gaugeSpeed = new TextElement("0", PointF.Empty, 0.95f, Color.White, GTA.UI.Font.ChaletComprimeCologne, Alignment.Right, true, true);
        private readonly TextElement gaugeUnit = new TextElement("KM/H", PointF.Empty, 0.32f, Color.FromArgb(255, 80, 220, 255), GTA.UI.Font.ChaletLondon, Alignment.Left, true, true);
        private readonly TextElement gaugeGear = new TextElement("N", PointF.Empty, 1.0f, Color.FromArgb(255, 170, 90, 255), GTA.UI.Font.Pricedown, Alignment.Left, true, true);

        private static void Rect(float x, float y, float w, float h, Color c)
        {
            GTA.Native.Function.Call(GTA.Native.Hash.DRAW_RECT, x + w / 2f, y + h / 2f, w, h, c.R, c.G, c.B, c.A, false);
        }

        public void Draw(SaveData d, VehicleManager vehicles)
        {
            if (d == null) return;
            Hud.HideComponentThisFrame(HudComponent.Cash);
            Hud.HideComponentThisFrame(HudComponent.WeaponIcon);
            cash.Caption = "$" + d.Cash.ToString("N0");
            rep.Caption = d.Rep.ToString("N0") + " REP" + (Career.CareerManager.Instance != null ? "  |  " + Career.CareerManager.Instance.TierName.ToUpperInvariant() : "");
            var r = vehicles.CurrentRecord; var v = vehicles.Current;
            car.Caption = r == null ? "" : (string.IsNullOrEmpty(r.Nickname) ? r.Model.ToUpperInvariant() : r.Nickname)
                + (v != null && v.Exists() ? "  " + (int)(v.Speed * 3.6f) + " KM/H" : "");
            brand.ScaledDraw(); cash.ScaledDraw(); rep.ScaledDraw();
            if (Game.GameTime < deltaUntil) delta.ScaledDraw();
            Hud.HideComponentThisFrame(HudComponent.VehicleName);
            Hud.HideComponentThisFrame(HudComponent.AreaName);
            Hud.HideComponentThisFrame(HudComponent.StreetName);
            if (v != null && v.Exists()) DrawGauge(v, r);
            else car.ScaledDraw();
        }
    }
}
