using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.UI;
using MidnightClub.Core;
using MidnightClub.Persistence;
using MidnightClub.Racing;
using MidnightClub.UI;
using MidnightClub.Vehicles;

namespace MidnightClub.Factions
{
    public sealed class Club
    {
        public string Id, Name, Description, DealerId;
        public Func<RaceDefinition, VehicleInfo, OwnedVehicle, bool> Counts;
    }

    /// <summary>Automotive communities with independent reputation, earned by racing their way.
    /// Levels give discounts at the dealership the club is tied to.</summary>
    public sealed class ClubManager
    {
        public static ClubManager Instance { get; private set; }
        private readonly SaveManager saves;
        private readonly VehicleRegistry registry;
        private readonly VehicleManager vehicles;
        public static readonly long[] LevelRep = { 0, 250, 750, 1500, 3000, 6000 };

        public static readonly List<Club> Clubs = new List<Club>
        {
            new Club { Id = "tuners", Name = "Underground Tuners", Description = "Tuner and sports builds.", DealerId = "carmeet", Counts = (r, v, o) => v != null && (v.Tuner || v.Class == VehicleClass.Sports) },
            new Club { Id = "muscle", Name = "Muscle Car Society", Description = "American iron and big displacement.", DealerId = "southern", Counts = (r, v, o) => v != null && v.Class == VehicleClass.Muscle },
            new Club { Id = "supercar", Name = "Supercar Elite", Description = "Exotic machinery only.", DealerId = "legendary", Counts = (r, v, o) => v != null && v.Class == VehicleClass.Super },
            new Club { Id = "drift", Name = "Drift Community", Description = "Angle, smoke and style.", DealerId = "bennys", Counts = (r, v, o) => r.Kind == RaceType.Drift || (o != null && o.TyreCompound == 4) },
            new Club { Id = "highway", Name = "Highway Racers", Description = "Long sprints and top speed.", DealerId = "pdm", Counts = (r, v, o) => r.Kind == RaceType.Sprint || r.Kind == RaceType.HeadToHead },
        };

        public ClubManager(SaveManager saves, VehicleRegistry registry, VehicleManager vehicles)
        {
            this.saves = saves; this.registry = registry; this.vehicles = vehicles; Instance = this;
            EventBus.Subscribe<RaceFinishedEvent>(OnRace);
        }

        public long Rep(string id) { var kv = saves.Data.ClubRep.FirstOrDefault(x => x.Key == id); long v; return kv != null && long.TryParse(kv.Value, out v) ? v : 0; }
        public int Level(string id) { long r = Rep(id); int l = 0; for (int i = 1; i < LevelRep.Length; i++) if (r >= LevelRep[i]) l = i; return l; }

        /// <summary>Discount (0..0.10) at a dealership from the club tied to it.</summary>
        public double Discount(string dealerId)
        {
            double best = 0;
            foreach (var c in Clubs.Where(c => c.DealerId == dealerId)) { int l = Level(c.Id); best = Math.Max(best, l >= 4 ? 0.10 : l >= 2 ? 0.05 : 0); }
            return best;
        }

        private void OnRace(RaceFinishedEvent e)
        {
            if (e.Result.Position == 0 || e.Result.Rep <= 0) return;
            var rec = vehicles.CurrentRecord;
            var info = rec == null ? null : (rec.ModelHash != 0 ? registry.Get(rec.ModelHash) : registry.Get(rec.Model));
            foreach (var c in Clubs)
            {
                if (!c.Counts(e.Race, info, rec)) continue;
                int before = Level(c.Id);
                long add = Math.Max(10, e.Result.Rep / 2);
                saves.Data.ClubRep.RemoveAll(x => x.Key == c.Id);
                saves.Data.ClubRep.Add(new KeyValue { Key = c.Id, Value = (Rep(c.Id) + add).ToString() });
                int after = Level(c.Id);
                if (after > before) Notification.PostTicker("~p~" + c.Name + "~s~: you're now level " + after + "." + (after == 2 || after == 4 ? " New discount at their dealership." : ""), true, false);
            }
            saves.IsDirty = true;
        }

        public Menu ClubsMenu()
        {
            var m = new Menu("Clubs", "Community reputation");
            foreach (var cl in Clubs)
            {
                var c = cl;
                m.Add(new MenuItem(c.Name)
                {
                    Value = () => "Lv " + Level(c.Id) + "  (" + Rep(c.Id).ToString("N0") + ")",
                    Description = () => c.Description + (Level(c.Id) < 5 ? " Next level at " + LevelRep[Level(c.Id) + 1].ToString("N0") + "." : " Max level.") + (Discount(c.DealerId) > 0 ? " Discount: " + (Discount(c.DealerId) * 100).ToString("0") + "% at their dealership." : ""),
                    Enabled = true
                });
            }
            return m;
        }
    }
}
