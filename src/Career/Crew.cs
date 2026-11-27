using System;
using System.Linq;
using GTA;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.Racing;
using MidnightClub.UI;
using MidnightClub.Vehicles;

namespace MidnightClub.Career
{
    /// <summary>Crew mate (NFS Carbon wingman): hire one driver who joins your races. Blockers ram whoever is closest
    /// behind you; drafters run just ahead so you can sit in their slipstream. They level up with every race (1-10),
    /// getting faster and driving better cars.</summary>
    public static class Crew
    {
        private sealed class Candidate { public string Name, Role, Bio; public long Fee; }
        private static readonly Candidate[] Pool =
        {
            new Candidate { Name = "Rosa 'Switch' Delgado", Role = "Blocker", Bio = "Ex-demolition derby. Loves contact more than winning.", Fee = 20000 },
            new Candidate { Name = "Theo Park", Role = "Drafter", Bio = "Smooth, fast, always exactly where you need a tow.", Fee = 25000 },
            new Candidate { Name = "Mack Rourke", Role = "Blocker", Bio = "Drives a car like a linebacker. Nobody passes you twice.", Fee = 35000 },
            new Candidate { Name = "Yuki Hanamura", Role = "Drafter", Bio = "Former time-attack champ. Pulls you to the front.", Fee = 45000 },
        };
        public static SaveManager Saves;
        private static SaveData D { get { return Saves != null ? Saves.Data : null; } }
        public static bool Hired { get { return D != null && !string.IsNullOrEmpty(D.CrewName); } }
        public static string Name { get { return D != null ? D.CrewName : ""; } }
        public static string Role { get { return D != null ? D.CrewRole ?? "Blocker" : "Blocker"; } }
        public static int Level { get { return D == null ? 1 : Math.Min(10, 1 + D.CrewRaces / 3); } }
        public static float Skill { get { return 0.98f + 0.025f * Level; } }

        public static void Init(SaveManager saves)
        {
            Saves = saves;
            EventBus.Subscribe<RaceFinishedEvent>(e =>
            {
                if (!Hired || !e.Race.HasOpponents || e.Result.Position == 0) return;
                int before = Level; D.CrewRaces++; Saves.IsDirty = true;
                if (Level > before) Toast.Post("~b~" + Name + "~s~ reached crew level " + Level + ". Faster car, sharper driving.");
            });
        }

        /// <summary>Crew car: one tier above the grid at high levels, otherwise the suggested pick.</summary>
        public static VehicleInfo CarFor(Vehicle player, VehicleInfo suggested)
        {
            if (Level < 6 || VehicleRegistry.Instance == null) return suggested;
            string[] tiers = PerformanceIndex.Classes; int ti = Array.IndexOf(tiers, suggested.Tier);
            var pool = VehicleRegistry.Instance.All.Where(v => v.Enabled && v.Class == suggested.Class && Array.IndexOf(tiers, v.Tier) == Math.Min(5, ti + 1)).ToList();
            return pool.Count > 0 ? pool[new Random().Next(pool.Count)] : suggested;
        }

        public static Menu Menu(TransactionManager money, MenuStack stack)
        {
            var m = new Menu("Crew", "A wingman for your races");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                if (Hired)
                {
                    m.Add(new MenuItem(Name) { Enabled = false, Value = () => Role.ToUpperInvariant() + "  ·  LEVEL " + Level });
                    m.Add(new MenuItem("Switch role", () => { D.CrewRole = Role == "Blocker" ? "Drafter" : "Blocker"; Saves.IsDirty = true; }) { Value = () => Role, Description = () => "Blockers ram the car right behind you. Drafters run just ahead to give you a tow." });
                    m.Add(new MenuItem("Let them go", () => { D.CrewName = null; D.CrewRole = null; D.CrewRaces = 0; Saves.IsDirty = true; Toast.Post("Your crew mate moved on."); m.OnOpen(); }));
                    return;
                }
                foreach (var cc in Pool)
                {
                    var c = cc;
                    m.Add(new MenuItem(c.Name, () =>
                    {
                        if (!money.TrySpend(c.Fee, "crew", "Hired " + c.Name, null)) { Toast.Post("~r~Not enough cash~s~ to hire " + c.Name + "."); return; }
                        D.CrewName = c.Name; D.CrewRole = c.Role; D.CrewRaces = 0; Saves.IsDirty = true;
                        Shard.Mid("CREW MATE HIRED", c.Name.ToUpperInvariant() + "  ·  " + c.Role.ToUpperInvariant(), Shard.Blue, 3000);
                        m.OnOpen();
                    }) { Value = () => c.Role + "  ·  $" + c.Fee.ToString("N0"), Description = () => c.Bio });
                }
            };
            return m;
        }
    }
}
