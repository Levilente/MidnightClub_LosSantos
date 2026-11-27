using System;
using System.Linq;
using GTA;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.Racing;
using MidnightClub.UI;

namespace MidnightClub.Career
{
    /// <summary>Five racing disciplines (Street, Drift, Off-road, Drag, Bikes), each with its own level 1-10. Events
    /// and activities of a discipline earn its XP; higher levels open that discipline's harder, better-paying events
    /// and pay out a themed reward (cash at every level, a car at 5 and 10).</summary>
    public sealed class Disciplines
    {
        public static Disciplines Instance;
        private static readonly string[] Names = { "Street", "Drift", "Off-road", "Drag", "Bikes" };
        public static readonly string[] Icons = { "races", "progress", "explore", "daily", "bike" };
        private static readonly long[] Need = { 0, 600, 1500, 2800, 4600, 7000, 10000, 14000, 19000, 25000 };
        private static readonly string[][] Cars =
        {
            new[] { "sultan2", "jester4" }, new[] { "futo2", "euros" }, new[] { "kamacho", "trophytruck2" }, new[] { "gauntlet4", "dominator7" }, new[] { "bati2", "shinobi" }
        };
        private readonly SaveManager saves; private readonly TransactionManager money;

        public Disciplines(SaveManager saves, TransactionManager money)
        {
            this.saves = saves; this.money = money; Instance = this;
            EventBus.Subscribe<RaceFinishedEvent>(OnRace);
        }

        public static string Name(Discipline d) { return Names[(int)d]; }
        public static long Xp(Discipline d) { var s = Instance != null ? Instance.saves.Data : null; if (s == null) return 0; var kv = s.DisciplineXp.FirstOrDefault(x => x.Key == d.ToString()); long v; return kv != null && long.TryParse(kv.Value, out v) ? v : 0; }
        public static int Level(Discipline d) { long x = Xp(d); int l = 1; for (int i = 1; i < Need.Length; i++) if (x >= Need[i]) l = i + 1; return l; }
        public static float Progress(Discipline d) { int l = Level(d); if (l >= 10) return 1f; long x = Xp(d); return (float)(x - Need[l - 1]) / (Need[l] - Need[l - 1]); }

        public void Add(Discipline d, long xp, string why)
        {
            if (saves.Data == null || xp <= 0) return;
            int before = Level(d);
            long v = Xp(d) + xp;
            saves.Data.DisciplineXp.RemoveAll(x => x.Key == d.ToString()); saves.Data.DisciplineXp.Add(new KeyValue { Key = d.ToString(), Value = v.ToString() }); saves.IsDirty = true;
            int after = Level(d);
            for (int l = before + 1; l <= after; l++)
            {
                long cash = 2500L * l;
                money.Earn(cash, 100L * l, "discipline", Name(d) + " level " + l, "disc:" + d + ":" + l);
                string car = l == 5 ? Cars[(int)d][0] : l == 10 ? Cars[(int)d][1] : null;
                Shard.Big(Name(d).ToUpperInvariant() + " LEVEL " + l, "+$" + cash.ToString("N0") + (car != null ? "  ·  reward car on its way" : "") + "  ·  new " + Name(d).ToLowerInvariant() + " events unlocked", Shard.Purple, 5000);
                if (car != null && Progression.Instance != null) Progression.Instance.GiveCar(car, Name(d) + " level " + l);
                if (Story.Instance != null) Story.Instance.Track("lvl:" + d, l, true);
            }
        }

        private void OnRace(RaceFinishedEvent e)
        {
            var r = e.Race; var res = e.Result; if (res.Position == 0) { Add(r.Discipline, 40, "attempt"); return; }
            var d = r.Discipline;
            var cur = Vehicles.VehicleManager.Instance != null ? Vehicles.VehicleManager.Instance.Current : null;
            if (d == Discipline.Street && cur != null && cur.Exists() && cur.Model.IsBike) d = Discipline.Bikes;
            long xp = 120;
            if (r.HasOpponents) xp += res.Position == 1 ? 380 : res.Position <= 3 ? 180 : 60;
            else xp += res.Medal == "Gold" ? 380 : res.Medal == "Silver" ? 220 : res.Medal == "Bronze" ? 120 : 30;
            Add(d, xp, r.Name);
        }

        public Menu Menu()
        {
            var m = new Menu("Disciplines", "Level each style of racing");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                foreach (Discipline d in Enum.GetValues(typeof(Discipline)))
                {
                    var dd = d; int l = Level(dd);
                    m.Add(new MenuItem(Name(dd)) { Value = () => "LEVEL " + Level(dd) + (Level(dd) < 10 ? "  ·  " + (int)(Progress(dd) * 100) + "%" : "  ·  MAX"), Description = () => Desc(dd) });
                }
            };
            return m;
        }

        private static string Desc(Discipline d)
        {
            switch (d)
            {
                case Discipline.Street: return "Sprints, circuits, eliminations, head-to-heads and outrun battles. Level 5 and 10 pay a reward car.";
                case Discipline.Drift: return "Drift events, gymkhana, drift zones and street takeovers.";
                case Discipline.Offroad: return "Rally stages, off-road races, cross country and hill climbs.";
                case Discipline.Drag: return "Drag strips. Launch, shift on the redline, don't lift.";
                default: return "Bike races (and any race you ride a bike in).";
            }
        }
    }
}
