using System;
using System.Linq;
using GTA;
using MidnightClub.Persistence;
using MidnightClub.UI;

namespace MidnightClub.Vehicles
{
    /// <summary>Forza-style tuning setup per car: nine sliders (-5..+5, 0 = stock) applied on top of the stock /
    /// simcade handling while you drive it. Presets for grip, drift, drag and rally. Free to change any time
    /// outside races.</summary>
    public static class Tuning
    {
        public static readonly string[] Names = { "Spring stiffness", "Damping", "Ride height", "Anti-roll bars", "Grip balance", "Brake balance", "Brake pressure", "Gearing", "Steering lock" };
        private static readonly string[] Lo = { "Soft", "Soft", "Low", "Soft", "Oversteer", "Rear", "Light", "Acceleration", "Narrow" };
        private static readonly string[] Hi = { "Stiff", "Stiff", "High", "Stiff", "Understeer", "Front", "Strong", "Top speed", "Wide" };
        private static readonly string[] Desc =
        {
            "Stiffer springs: sharper turn-in and less body roll, but less forgiving over bumps.",
            "How fast the suspension settles. Stiffer: more responsive, twitchier over kerbs.",
            "Lower: lower centre of gravity and better cornering. Higher: copes with jumps and rough roads.",
            "Stiffer anti-roll bars cut body roll; too stiff and the car loses grip mid-corner.",
            "Shift grip to the front (understeer, stable) or to the rear (oversteer, playful, drifty).",
            "Move braking force front or rear. Rear-biased brakes rotate the car on corner entry.",
            "Overall braking force.",
            "Short gears for punchy acceleration or long gears for a higher top speed.",
            "More steering lock helps drifting and tight corners; less is calmer at speed."
        };
        public static readonly int Count = Names.Length;

        public static bool Any(int[] t) { return t != null && t.Any(v => v != 0); }
        private static int[] Get(OwnedVehicle r) { if (r.Tune == null || r.Tune.Length != Count) { var n = new int[Count]; if (r.Tune != null) Array.Copy(r.Tune, n, Math.Min(Count, r.Tune.Length)); r.Tune = n; } return r.Tune; }

        public static void Apply(HandlingData h, int[] t)
        {
            Func<int, float> V = i => i < t.Length ? Math.Max(-5, Math.Min(5, t[i])) : 0;
            h.SuspensionForce *= 1f + 0.08f * V(0);
            h.SuspensionCompressionDamping *= 1f + 0.08f * V(1);
            h.SuspensionReboundDamping *= 1f + 0.08f * V(1);
            h.SuspensionRaise += 0.012f * V(2);
            h.AntiRollBarForce *= 1f + 0.12f * V(3);
            h.TractionBiasFront = Math.Max(0.38f, Math.Min(0.62f, h.TractionBiasFront + 0.016f * V(4)));
            h.BrakeBiasFront = Math.Max(0.3f, Math.Min(0.8f, h.BrakeBiasFront + 0.03f * V(5)));
            h.BrakeForce *= 1f + 0.06f * V(6);
            h.InitialDriveMaxFlatVelocity *= 1f + 0.03f * V(7);
            h.InitialDriveForce *= 1f - 0.025f * V(7);
            h.SteeringLock *= 1f + 0.04f * V(8);
        }

        private static string Bar(int v)
        {
            var c = new char[11]; for (int i = 0; i < 11; i++) c[i] = i == v + 5 ? '|' : i == 5 ? '+' : '-';
            return new string(c);
        }

        public static Menu Menu(VehicleManager vehicles, MenuStack stack, Persistence.SaveManager saves)
        {
            var rec = vehicles.CurrentRecord;
            var m = new Menu("Tuning setup", rec == null ? "No car" : (rec.Nickname ?? rec.Model));
            if (rec == null) { m.Add(new MenuItem("Get in one of your cars first") { Enabled = false }); return m; }
            var t = Get(rec);
            for (int k = 0; k < Count; k++)
            {
                int i = k;
                m.Add(new MenuItem(Names[i])
                {
                    Value = () => (t[i] == 0 ? "Stock" : (t[i] < 0 ? Lo[i] : Hi[i]) + " " + Math.Abs(t[i])) + "  " + Bar(t[i]),
                    OnAdjust = d => { if (Racing.RaceManager.IsRacing) return; t[i] = Math.Max(-5, Math.Min(5, t[i] + d)); saves.IsDirty = true; },
                    Description = () => Desc[i]
                });
            }
            Action<string, int[]> preset = (name, vals) => m.Add(new MenuItem("Preset: " + name, () => { Array.Copy(vals, t, Count); saves.IsDirty = true; Shard.Mid("TUNING", name.ToUpperInvariant() + " SETUP LOADED", Shard.Purple, 1500); }));
            preset("Grip", new[] { 2, 2, -2, 2, 0, 0, 1, 0, 0 });
            preset("Drift", new[] { 1, 0, -1, 3, -4, -2, 0, -1, 5 });
            preset("Drag", new[] { 0, 0, -1, 0, 1, 1, 0, 5, -2 });
            preset("Rally", new[] { -3, -2, 4, -2, 0, 0, 0, -1, 1 });
            preset("Stock", new int[Count]);
            return m;
        }
    }
}
