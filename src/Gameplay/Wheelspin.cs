using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.UI;
using MidnightClub.Vehicles;

namespace MidnightClub.Gameplay
{
    /// <summary>Forza Horizon-style Wheelspins: every level-up earns a spin (every 5th level a Super Wheelspin with
    /// three reels). Reels of prize cards blur past, slow down and land: cash, XP, REP or a car, with rarity
    /// colours (common / rare / epic / legendary).</summary>
    public static class Wheelspin
    {
        private sealed class Prize { public string Title, Sub; public int Rarity; public long Cash, Xp, Rep; public string Car; }
        private static readonly Color[] RarityCol = { Color.FromArgb(255, 150, 145, 172), Color.FromArgb(255, 96, 165, 250), Color.FromArgb(255, 168, 85, 247), Color.FromArgb(255, 251, 191, 36) };
        private static readonly string[] RarityName = { "COMMON", "RARE", "EPIC", "LEGENDARY" };

        private static Prize Roll(Random rnd, VehicleRegistry reg)
        {
            double r = rnd.NextDouble();
            if (r < 0.08)
            {
                string[] tiers = r < 0.02 ? new[] { "S", "X" } : new[] { "B", "A" };
                var pool = reg.All.Where(v => v.Enabled && tiers.Contains(v.Tier) && v.Class != VehicleClass.Motorcycles && v.Class != VehicleClass.Commercial && v.Class != VehicleClass.Industrial).ToList();
                if (pool.Count > 0) { var v = pool[rnd.Next(pool.Count)]; return new Prize { Title = v.Name.ToUpperInvariant(), Sub = "CAR  ·  TIER " + v.Tier, Rarity = r < 0.02 ? 3 : 2, Car = v.Id }; }
            }
            if (r < 0.45) { long c = new[] { 5000L, 10000L, 15000L, 25000L, 50000L, 100000L }[Math.Min(5, (int)(Math.Pow(rnd.NextDouble(), 2.2) * 6))]; return new Prize { Title = "$" + c.ToString("N0"), Sub = "CREDITS", Rarity = c >= 50000 ? 2 : c >= 15000 ? 1 : 0, Cash = c }; }
            if (r < 0.75) { long x = new[] { 1000L, 2500L, 5000L, 10000L }[Math.Min(3, (int)(Math.Pow(rnd.NextDouble(), 2) * 4))]; return new Prize { Title = x.ToString("N0") + " XP", Sub = "EXPERIENCE", Rarity = x >= 5000 ? 1 : 0, Xp = x }; }
            long rp = new[] { 100L, 250L, 500L, 1000L }[Math.Min(3, (int)(Math.Pow(rnd.NextDouble(), 2) * 4))];
            return new Prize { Title = rp.ToString("N0") + " REP", Sub = "STREET REP", Rarity = rp >= 500 ? 1 : 0, Rep = rp };
        }

        public static void Spin(SaveData d, TransactionManager money, VehicleRegistry reg, bool super)
        {
            if (super) { if (d.SuperWheelspins <= 0) return; d.SuperWheelspins--; } else { if (d.Wheelspins <= 0) return; d.Wheelspins--; }
            var rnd = new Random();
            int reels = super ? 3 : 1, cards = 30;
            var strips = new List<List<Prize>>();
            for (int i = 0; i < reels; i++) { var l = new List<Prize>(); for (int k = 0; k < cards; k++) l.Add(Roll(rnd, reg)); strips.Add(l); }
            var stopAt = new int[reels]; for (int i = 0; i < reels; i++) stopAt[i] = cards - 3;
            int start = Environment.TickCount; var done = new bool[reels]; int lastTick = -1;
            float[] dur = { 3.2f, 3.9f, 4.6f };
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Bus_Schedule_Pickup", "DLC_PRISON_BREAK_HEIST_SOUNDS", true);
            while (true)
            {
                float el = (Environment.TickCount - start) / 1000f;
                Game.DisableAllControlsThisFrame(); Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
                Ds.RectPhys(0f, 0f, 1f, 1f, Ds.A(Ds.Bg, 0.9f));
                Ds.Streaks(0f, 0.2f, 1f, 0.6f, super ? Ds.Amber : Ds.Violet, 0.4f, 10, 1.2f);
                Ds.T(super ? "SUPER WHEELSPIN" : "WHEELSPIN", 0.5f, 0.08f, 1.0f, super ? Ds.Amber : Ds.Text, Ds.Display, GTA.UI.Alignment.Center);
                float cw = Ds.Px(0.24f), ch = 0.17f, gap = Ds.Px(0.03f), total = reels * cw + (reels - 1) * gap, x0 = 0.5f - total / 2f, cy = 0.5f;
                bool all = true;
                for (int i = 0; i < reels; i++)
                {
                    float t = Math.Min(1f, el / dur[i]); if (t < 1f) all = false;
                    float e = 1f - (float)Math.Pow(1f - t, 3);
                    float pos = e * stopAt[i];            // card index at centre
                    int idx = (int)Math.Floor(pos); float frac = pos - idx;
                    if (i == 0 && idx != lastTick && t < 1f) { lastTick = idx; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET", true); }
                    if (t >= 1f && !done[i]) { done[i] = true; var p0 = strips[i][stopAt[i]]; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, p0.Rarity >= 2 ? "RACE_PLACED" : "MEDAL_UP", p0.Rarity >= 2 ? "HUD_AWARDS" : "HUD_MINI_GAME_SOUNDSET", true); }
                    float x = x0 + i * (cw + gap);
                    for (int k = -1; k <= 1; k++)
                    {
                        int ci = idx + k; if (ci < 0 || ci >= cards) continue;
                        var p = strips[i][ci];
                        float y = cy + (k - frac) * (ch + 0.02f) - ch / 2f;
                        bool centre = t >= 1f && ci == stopAt[i];
                        float alpha = Math.Max(0.15f, 1f - Math.Abs(k - frac) * 0.75f);
                        var rc = RarityCol[p.Rarity];
                        if (centre) Ds.Rect(x - Ds.Px(0.005f), y - 0.005f, cw + Ds.Px(0.01f), ch + 0.01f, Ds.A(rc, 0.5f + 0.3f * Ds.Pulse(1.5f)));
                        Ds.Rect(x, y, cw, ch, Ds.A(Ds.Surface, alpha));
                        Ds.VGrad(x, y + ch * 0.55f, cw, ch * 0.45f, Ds.A(rc, 0f), Ds.A(rc, 0.35f * alpha), 6);
                        Ds.Rect(x, y + ch - 0.005f, cw, 0.005f, Ds.A(rc, alpha));
                        Ds.T(RarityName[p.Rarity], x + Ds.Px(0.014f), y + 0.012f, 0.24f, Ds.A(rc, alpha), Ds.Display);
                        Ds.T(Ds.Fit(p.Title, 0.62f, cw - Ds.Px(0.03f), Ds.Display), x + Ds.Px(0.014f), y + 0.06f, 0.62f, Ds.A(Ds.Text, alpha), Ds.Display);
                        Ds.T(p.Sub, x + Ds.Px(0.014f), y + 0.115f, 0.26f, Ds.A(Ds.Muted, alpha), Ds.Display);
                    }
                }
                if (all && el > dur[reels - 1] + 0.6f)
                {
                    Buttons.Draw(Control.FrontendAccept, "Collect");
                    if (Input.JustPressed(Control.FrontendAccept) || Input.JustPressed(Control.FrontendCancel)) break;
                }
                Script.Wait(0);
            }
            for (int i = 0; i < reels; i++)
            {
                var p = strips[i][stopAt[i]];
                if (p.Cash > 0 || p.Rep > 0) money.Earn(p.Cash, p.Rep, "wheelspin", "Wheelspin: " + p.Title, null);
                if (p.Xp > 0 && Career.Progression.Instance != null) Career.Progression.Instance.Award(p.Xp, "Wheelspin");
                if (p.Car != null && Career.Progression.Instance != null) Career.Progression.Instance.GiveCar(p.Car, "Wheelspin");
            }
        }

        public static Menu Menu(SaveManager saves, TransactionManager money, VehicleRegistry reg, MenuStack stack, Action closeHub)
        {
            var m = new Menu("Wheelspins", "Earn spins by levelling up");
            m.OnOpen = () =>
            {
                m.Items.Clear(); var d = saves.Data;
                m.Add(new MenuItem("Wheelspin", () => { if (d.Wheelspins <= 0) return; closeHub(); Spin(d, money, reg, false); saves.Save(); }) { Enabled = d.Wheelspins > 0, Value = () => d.Wheelspins.ToString(), Description = () => "One prize: credits, XP, REP or (rarely) a car." });
                m.Add(new MenuItem("Super Wheelspin", () => { if (d.SuperWheelspins <= 0) return; closeHub(); Spin(d, money, reg, true); saves.Save(); }) { Enabled = d.SuperWheelspins > 0, Value = () => d.SuperWheelspins.ToString(), Description = () => "Three prizes at once. Earned every fifth level." });
            };
            return m;
        }
    }
}
