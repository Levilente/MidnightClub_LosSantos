using System;
using System.Collections.Generic;
using GTA;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.UI;
using MidnightClub.Vehicles;

namespace MidnightClub.Career
{
    /// <summary>Car mastery (Forza-style bond with each car): every owned car levels up 1-10 from the kilometres
    /// you drive it, the races you enter and win with it and the pursuits it escapes. Each level pays a bonus and
    /// adds +3 % race winnings with that car.</summary>
    public sealed class Mastery
    {
        public const int MaxLevel = 10;
        private static readonly int[] Need = { 0, 300, 800, 1600, 2800, 4500, 6800, 9800, 13500, 18000 };
        private readonly SaveManager saves; private readonly TransactionManager money; private readonly VehicleManager vehicles;
        private readonly Dictionary<string, int> seen = new Dictionary<string, int>();
        private int nextCheck;

        public Mastery(SaveManager saves, TransactionManager money, VehicleManager vehicles)
        {
            this.saves = saves; this.money = money; this.vehicles = vehicles;
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
        }

        public static long Xp(OwnedVehicle r)
        {
            if (r == null || r.Stats == null) return 0;
            return (long)(r.Stats.DistanceKm * 25) + r.Stats.RacesEntered * 150L + r.Stats.RacesWon * 400L + r.Stats.PoliceEscapes * 250L;
        }
        public static int Level(OwnedVehicle r) { long x = Xp(r); int l = 1; for (int i = 1; i < Need.Length; i++) if (x >= Need[i]) l = i + 1; return Math.Min(MaxLevel, l); }
        public static float Progress(OwnedVehicle r)
        {
            int l = Level(r); if (l >= MaxLevel) return 1f;
            long x = Xp(r); return (float)(x - Need[l - 1]) / Math.Max(1, Need[l] - Need[l - 1]);
        }
        public static double CashMult(OwnedVehicle r) { return 1.0 + 0.03 * (Level(r) - 1); }
        public static string Label(OwnedVehicle r) { int l = Level(r); return l >= MaxLevel ? "MASTERED" : "MASTERY " + l; }

        private void Tick()
        {
            int now = Game.GameTime; if (now < nextCheck) return; nextCheck = now + 2000;
            var r = vehicles.CurrentRecord; if (r == null) return;
            int l = Level(r), was;
            if (!seen.TryGetValue(r.Id, out was)) { seen[r.Id] = l; return; }
            if (l <= was) return;
            seen[r.Id] = l;
            long bonus = 1500L * l;
            money.Earn(bonus, 50L * l, "mastery", r.Title() + " mastery " + l, "mastery:" + r.Id + ":" + l);
            Shard.Mid(l >= MaxLevel ? "CAR MASTERED" : "CAR MASTERY " + l, r.Title().ToUpperInvariant() + "  ·  +$" + bonus.ToString("N0") + "  ·  +" + ((l - 1) * 3) + "% race winnings in this car", Shard.Gold, 4500);
        }
    }
}
