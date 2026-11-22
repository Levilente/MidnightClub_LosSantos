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
            brand.ScaledDraw(); cash.ScaledDraw(); rep.ScaledDraw(); car.ScaledDraw();
            if (Game.GameTime < deltaUntil) delta.ScaledDraw();
        }
    }
}
