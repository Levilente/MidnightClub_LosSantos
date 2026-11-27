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

namespace MidnightClub.Explore
{
    /// <summary>Midnight Tokens: 40 glowing collectibles hidden on roads all over San Andreas (same spots for every
    /// player). Each pays cash and XP; every 10th pays a big bonus, all 40 unlock a reward car. The phone sells
    /// tips that mark the nearest token on the map.</summary>
    public sealed class Tokens
    {
        public const int Total = 40;
        public static Tokens Instance;
        private readonly SaveManager saves; private readonly TransactionManager money; private readonly MenuStack stack;
        private List<Vector3> spots; private int nextCheck; private Blip tip; private int tipIdx = -1;

        public Tokens(SaveManager saves, TransactionManager money, MenuStack stack)
        {
            this.saves = saves; this.money = money; this.stack = stack; Instance = this;
            EventBus.Subscribe<MidnightTickEvent>(e => { try { Tick(); } catch (Exception ex) { Log.Error("Tokens", ex); } });
            EventBus.Subscribe<ModeChangedEvent>(e => ClearTip());
        }

        private static string Key(int i) { return "tok:" + i; }
        public bool Has(int i) { return saves.Data != null && saves.Data.Discovered.Contains(Key(i)); }
        public int Collected { get { if (saves.Data == null) return 0; int n = 0; for (int i = 0; i < Total; i++) if (Has(i)) n++; return n; } }

        private bool Ensure()
        {
            if (spots != null) return true;
            if (RoadGraph.Count == 0) return false;
            var rnd = new Random(20261123); var list = new List<Vector3>(); int guard = 0;
            while (list.Count < Total && guard++ < 200000)
            {
                int i = rnd.Next(RoadGraph.Count);
                var f = RoadGraph.Flags(i);
                if ((f & RoadGraph.NodeFlags.Gps) == 0 || (f & (RoadGraph.NodeFlags.Water | RoadGraph.NodeFlags.Gravel)) != 0) continue;
                var p = RoadGraph.Pos(i);
                if (list.Any(q => q.DistanceTo2D(p) < 450f)) continue;
                list.Add(p);
            }
            spots = list;
            return true;
        }

        private void Tick()
        {
            if (!Ensure() || saves.Data == null || RaceManager.IsRacing) return;
            var ped = Game.Player.Character; var pos = ped.Position; int now = Game.GameTime;
            for (int i = 0; i < spots.Count; i++)
            {
                if (Has(i)) continue;
                var s = spots[i]; float d = pos.DistanceTo(s);
                if (d > 140f) continue;
                float bob = (float)Math.Sin(now / 400.0 + i) * 0.25f;
                float pulse = 0.85f + 0.15f * (float)Math.Sin(now / 160.0);
                Function.Call(Hash.DRAW_MARKER, 28, s.X, s.Y, s.Z + 1.6f + bob, 0f, 0f, 0f, 0f, 0f, 0f, 0.9f * pulse, 0.9f * pulse, 0.9f * pulse, 168, 85, 247, 190, false, false, 2, false, 0, 0, false);
                Function.Call(Hash.DRAW_MARKER, 27, s.X, s.Y, s.Z + 0.05f, 0f, 0f, 0f, 0f, 0f, (float)(now / 8 % 360), 3.2f, 3.2f, 1f, 34, 211, 238, 150, false, false, 2, false, 0, 0, false);
                Function.Call(Hash.DRAW_LIGHT_WITH_RANGE, s.X, s.Y, s.Z + 2f, 168, 85, 247, 9f, 6f);
                if (d < 4.5f) Collect(i);
            }
        }

        private void Collect(int i)
        {
            saves.Data.Discovered.Add(Key(i)); saves.IsDirty = true;
            if (i == tipIdx) ClearTip();
            int n = Collected;
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "PICK_UP", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
            long cash = 1000 + (n % 10 == 0 ? 15000L * (n / 10) : 0);
            money.Earn(cash, 40, "token", "Midnight Token " + n + "/" + Total, "token:" + i);
            if (Career.Progression.Instance != null) Career.Progression.Instance.Award(n % 10 == 0 ? 2000 : 200, "Midnight Token");
            if (n == Total)
            {
                Shard.Big("ALL MIDNIGHT TOKENS", "Every token in San Andreas. A one-off car is on its way to your garage.", Shard.Gold, 7000);
                if (Career.Progression.Instance != null) Career.Progression.Instance.GiveCar("vigilante", "All Midnight Tokens");
            }
            else if (n % 10 == 0) Shard.Big("MIDNIGHT TOKENS " + n + " / " + Total, "Milestone bonus: $" + (15000L * (n / 10)).ToString("N0"), Shard.Purple, 4500);
            else Shard.Mid("MIDNIGHT TOKEN", n + " / " + Total + "  ·  +$1,000", Shard.Purple, 2200);
        }

        private void ClearTip() { try { if (tip != null && tip.Exists()) tip.Delete(); } catch { } tip = null; tipIdx = -1; }

        public Menu TokensMenu()
        {
            var m = new Menu("Midnight Tokens", Collected + " / " + Total + " collected");
            m.Add(new MenuItem("Collected") { Value = () => Collected + " / " + Total, Enabled = false });
            m.Add(new MenuItem("Buy a tip ($5,000)", () =>
            {
                if (!Ensure()) return;
                var pos = Game.Player.Character.Position; int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < spots.Count; i++) if (!Has(i)) { float d = pos.DistanceTo2D(spots[i]); if (d < bd) { bd = d; best = i; } }
                if (best < 0) { Shard.Mid("NO TOKENS LEFT", "You found them all.", Shard.Purple, 2000); return; }
                if (!money.TrySpend(5000, "token", "Token tip", null)) { Shard.Mid("NOT ENOUGH CASH", "A tip costs $5,000.", Shard.Red, 2000); return; }
                ClearTip(); tipIdx = best;
                var b = BlipRegistry.Own(World.CreateBlip(spots[best], 120f)); b.Color = BlipColor.Purple; b.Alpha = 110; tip = b;
                stack.Close();
                Shard.Mid("TOKEN TIP", "One's somewhere in the marked circle.", Shard.Purple, 3000);
            }) { Description = () => "Marks a circle around the nearest token you haven't collected." });
            m.Add(new MenuItem("Rewards") { Value = () => "$1,000 each, big bonus every 10, a car for all 40", Enabled = false });
            return m;
        }
    }
}
