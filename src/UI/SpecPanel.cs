using System;
using System.Drawing;
using GTA;
using GTA.UI;
using MidnightClub.Vehicles;
using Control = GTA.Control;
using Font = GTA.UI.Font;
using Keys = System.Windows.Forms.Keys;

namespace MidnightClub.UI
{
    /// <summary>Real data view for car info (garage, showroom, workshop): R3 / R switches between the game ratings
    /// (speed, handling ...) and the vehicle's real figures from the physics model - power and torque with their rpm,
    /// weight, balance, centre of gravity, drivetrain, tyres, aero, and the performance the model works out
    /// (0-100, 0-200, quarter mile, top speed, braking, lateral grip) - plus the torque and power curves.</summary>
    public static class SpecPanel
    {
        public static bool Real;
        private static bool rDown;

        /// <summary>Call once per frame on a screen that shows car info.</summary>
        public static void HandleToggle()
        {
            bool r = Game.IsKeyPressed(Keys.R);
            if (Input.JustPressed(Control.FrontendRs) || (r && !rDown))
            {
                Real = !Real;
                GTA.Native.Function.Call(GTA.Native.Hash.PLAY_SOUND_FRONTEND, -1, "NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
            }
            rDown = r;
        }
        public static string PromptLabel { get { return Real ? "Game stats" : "Real data"; } }

        private static string F0(float v) { return v.ToString("0", System.Globalization.CultureInfo.InvariantCulture); }
        private static string F1(float v) { return v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture); }
        private static string F2(float v) { return v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture); }

        public static string Chip(Drivetrain.Live L)
        {
            if (L == null) return "";
            return (L.Spec.LayoutLabel + "  ·  " + L.Gear.FullGears + "-SPEED " + BoxShort(L) + "  ·  " + (L.Engine.Ind == Physics.Induction.Turbo ? "TURBO" : L.Engine.Ind == Physics.Induction.Supercharged ? "SUPERCHARGED" : L.Engine.Ind == Physics.Induction.Electric ? "ELECTRIC" : "NA")).ToUpperInvariant();
        }

        /// <summary>The real gearbox type (the race gearbox makes any car a sequential).</summary>
        public static string BoxName(Drivetrain.Live L)
        {
            if (L.Gear.RaceBox) return "sequential (race)";
            var gb = L.Spec.Sheet != null && L.Spec.Sheet.drivetrain != null ? L.Spec.Sheet.drivetrain.gearbox : null;
            string t = gb != null && !string.IsNullOrEmpty(gb.type) ? gb.type : "automatic";
            return t == "automatic" ? "automatic" : t == "dual-clutch" ? "dual-clutch" : t;
        }
        private static string BoxShort(Drivetrain.Live L)
        {
            string t = BoxName(L);
            return t.StartsWith("sequential") ? "SEQ" : t == "manual" ? "MANUAL" : t == "dual-clutch" ? "DCT" : t == "automated manual" ? "AMT" : t == "CVT" ? "CVT" : t == "single-speed" ? "DIRECT" : "AUTO";
        }
        private static string Ratios(Drivetrain.Live L)
        {
            var f = L.Gear.Full ?? L.Gear.Ratios; if (f == null) return "";
            var parts = new string[Math.Min(8, f.Length)];
            for (int i = 0; i < parts.Length; i++) parts[i] = f[i].ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            return string.Join(" ", parts) + (f.Length > 8 ? " …" : "");
        }

        /// <summary>The cells: label, value, small line.</summary>
        public static string[][] Cells(Drivetrain.Live L)
        {
            var p = L.Perf; var e = L.Engine; var s = L.Spec;
            return new[]
            {
                new[] { "POWER", F0(e.PeakHp) + " HP", "@ " + F0(e.PeakHpRpm) + " RPM  ·  " + F0(e.PeakHp * 0.7457f) + " KW" },
                new[] { "TORQUE", F0(e.PeakNm) + " NM", "@ " + F0(e.PeakNmRpm) + " RPM" },
                new[] { "WEIGHT", F0(p.Kg) + " KG", F0(p.HpPerTonne) + " HP/T" },
                new[] { "BALANCE", F0(s.FrontPct * 100f) + " / " + F0(100f - s.FrontPct * 100f), "COG " + F2(s.CgH) + " M  ·  WB " + F2(s.Wheelbase) + " M" },
                new[] { "0-100 KM/H", p.T100 > 0 ? F1(p.T100) + " S" : "-", p.T200 > 0 ? "0-200 " + F1(p.T200) + " S" : "0-200 -" },
                new[] { "TOP SPEED", F0(p.TopKmh) + " KM/H", p.TopByRedline ? "REV-LIMITED" : "DRAG-LIMITED" },
                new[] { "GEARBOX", L.Gear.FullGears + "-SPEED " + BoxName(L).ToUpperInvariant(), Ratios(L) },
                new[] { "QUARTER MILE", p.Quarter > 0 ? F2(p.Quarter) + " S" : "-", p.Quarter > 0 ? "@ " + F0(p.QuarterKmh) + " KM/H" : "" },
                new[] { "GRIP", F2(p.LatG) + " G", "100-0 IN " + F1(p.Brake100) + " M" },
                new[] { "ENGINE", s.EngineLabel.ToUpperInvariant(), e.InductionLabel.ToUpperInvariant() },
                new[] { "DRIVETRAIN", L.Spec.LayoutLabel, "FINAL " + F2(L.Gear.Final) + "  ·  REDLINE " + F0(s.Redline) },
                new[] { "TYRES", L.Tyre.Name.ToUpperInvariant(), "PEAK μ " + F2(L.Tyre.MuY) + " @ " + F1(L.Tyre.PeakDeg) + "°" },
                new[] { "AERO", "CDA " + F2(L.Cda), (L.Cla >= 0 ? "DOWNFORCE " : "LIFT ") + F0(Math.Abs(p.DownforceKg200)) + " KG @200" },
            };
        }

        /// <summary>A grid of real figures: n columns, as many rows as fit.</summary>
        public static void Grid(Drivetrain.Live L, float x, float y, float w, int cols, int maxCells)
        {
            if (L == null) return;
            var cells = Cells(L);
            float gap = Ds.Px(Th.Px(20f)), cw = (w - gap * (cols - 1)) / cols, rowH = Th.Px(58f);
            int n = Math.Min(maxCells, cells.Length);
            for (int i = 0; i < n; i++)
            {
                float cx = x + (i % cols) * (cw + gap), cy = y + (i / cols) * rowH;
                Ds.TM(cells[i][0], cx, cy + Th.Px(8f), 0.22f, Th.Sub, Ds.Display);
                Ds.TM(Ds.Fit(cells[i][1], 0.36f, cw, Ds.Display), cx, cy + Th.Px(28f), 0.36f, Th.Text, Ds.Display);
                Ds.TM(Ds.Fit(cells[i][2], 0.2f, cw, Ds.Body), cx, cy + Th.Px(47f), 0.2f, Th.Sub, Ds.Body);
            }
        }

        /// <summary>Torque and power over the rev range (glass card with legend and peaks).</summary>
        public static void Curve(Drivetrain.Live L, float x, float y, float w, float h)
        {
            if (L == null) return;
            var e = L.Engine;
            Kit.Glass(x, y, w, h, Th.R_Lg);
            float pad = Ds.Px(Th.Px(18f));
            float gx = x + pad, gw = w - pad * 2f, gy = y + Th.Px(44f), gh = h - Th.Px(70f);
            Ds.TM("TORQUE", gx, y + Th.Px(22f), 0.24f, Th.Accent, Ds.Display);
            Ds.TM("POWER", gx + Ds.Width("TORQUE", 0.24f, Ds.Display) + Ds.Px(Th.Px(16f)), y + Th.Px(22f), 0.24f, Th.Amber, Ds.Display);
            Ds.TM(F0(e.PeakNm) + " NM  ·  " + F0(e.PeakHp) + " HP", x + w - pad, y + Th.Px(22f), 0.24f, Th.Text, Ds.Display, Alignment.Right);
            // grid
            for (int i = 0; i <= 4; i++) Textures.DrawBox("px", gx, gy + gh * i / 4f, gw, Th.Px(1f), Color.FromArgb(40, Th.Text.R, Th.Text.G, Th.Text.B));
            float maxRpm = L.Spec.Redline, nmMax = Math.Max(1f, e.PeakNm * 1.08f), hpMax = Math.Max(1f, e.PeakHp * 1.08f);
            int n = 56;
            float th = Th.Px(3f);
            float pt = 0f, pp = 0f;
            for (int i = 0; i <= n; i++)
            {
                float rpm = L.Spec.Idle + (maxRpm - L.Spec.Idle) * i / n;
                float t = e.SteadyTorque(rpm), hp = t * rpm / 7121f;
                float ty = gy + gh * (1f - t / nmMax), py = gy + gh * (1f - hp / hpMax);
                float sx = gx + gw * i / n, dx = gw / n;
                if (i > 0)
                {
                    Textures.DrawBox("px", sx - dx, Math.Min(ty, pt) - th / 2f, dx + Ds.Px(th) * 0.5f, Math.Abs(ty - pt) + th, Th.Accent);
                    Textures.DrawBox("px", sx - dx, Math.Min(py, pp) - th / 2f, dx + Ds.Px(th) * 0.5f, Math.Abs(py - pp) + th, Th.Amber);
                }
                pt = ty; pp = py;
            }
            // peak markers + rpm axis
            Ds.Dot(gx + gw * (e.PeakNmRpm - L.Spec.Idle) / (maxRpm - L.Spec.Idle), gy + gh * (1f - e.PeakNm / nmMax), Th.Px(9f), Th.Accent);
            Ds.Dot(gx + gw * (e.PeakHpRpm - L.Spec.Idle) / (maxRpm - L.Spec.Idle), gy + gh * (1f - e.PeakHp / hpMax), Th.Px(9f), Th.Amber);
            Ds.TM(F0(L.Spec.Idle), gx, y + h - Th.Px(16f), 0.2f, Th.Sub, Ds.Body);
            Ds.TM(F0(maxRpm / 2f + L.Spec.Idle / 2f), gx + gw / 2f, y + h - Th.Px(16f), 0.2f, Th.Sub, Ds.Body, Alignment.Center);
            Ds.TM(F0(maxRpm) + " RPM", gx + gw, y + h - Th.Px(16f), 0.2f, Th.Sub, Ds.Body, Alignment.Right);
        }
    }
}
