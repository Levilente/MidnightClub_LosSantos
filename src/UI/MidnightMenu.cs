using System;
using System.Linq;
using GTA;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.Vehicles;

namespace MidnightClub.UI
{
    /// <summary>Phase 2 in-car menu (F6). Becomes the smartphone in Phase 10.</summary>
    public sealed class MidnightMenu
    {
        private readonly MenuStack stack;
        private readonly GameModeManager modes;
        private readonly SaveManager saves;
        private readonly VehicleRegistry registry;
        private readonly VehicleManager vehicles;
        private readonly Config cfg;

        public Garages.GarageManager Garages { get; set; }
        public Dealerships.DealershipManager Dealers { get; set; }
        public Racing.RaceManager Races { get; set; }
        public Rivals.RivalManager RivalMgr { get; set; }
        public Factions.ClubManager ClubMgr { get; set; }

        public MidnightMenu(MenuStack stack, GameModeManager modes, SaveManager saves, VehicleRegistry registry, VehicleManager vehicles, Config cfg)
        {
            this.stack = stack; this.modes = modes; this.saves = saves; this.registry = registry; this.vehicles = vehicles; this.cfg = cfg;
        }

        public void Toggle()
        {
            if (stack.IsOpen) { stack.Close(); return; }
            stack.Open(Main());
        }

        private Menu Main()
        {
            var m = new Menu("Midnight Club", "Main menu");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                var d = saves.Data;
                if (Career.CareerManager.Instance != null) m.Add(new MenuItem("Career", () => stack.Push(CareerMenu())) { Value = () => "Tier " + Career.CareerManager.Instance.Tier });
                if (RivalMgr != null) m.Add(new MenuItem("Rivals", () => stack.Push(RivalMgr.RivalsMenu())));
                if (ClubMgr != null) m.Add(new MenuItem("Clubs", () => stack.Push(ClubMgr.ClubsMenu())));
                if (Races != null) m.Add(new MenuItem("Races", () => stack.Push(Races.RacesMenu())) { Value = () => d.Rep.ToString("N0") + " REP", Description = () => "Races, championships and your history." });
                m.Add(new MenuItem("My vehicles", () => stack.Push(MyVehicles())) { Value = () => d.Vehicles.Count.ToString() });
                m.Add(new MenuItem("Garages", () => stack.Push(GarageList())) { Value = () => d.Garages.Count + " owned" });
                m.Add(new MenuItem("Finances", () => stack.Push(Finances())) { Value = () => "$" + d.Cash.ToString("N0") });
                m.Add(new MenuItem("Dealerships", () => stack.Push(DealerList())) { Description = () => "This week's stock and GPS." });
                m.Add(new MenuItem("Vehicle catalogue", () => stack.Push(Catalogue())) { Value = () => registry.Count.ToString(),
                    Description = () => "Every car available in your game build, with Midnight Club prices." });
                m.Add(new MenuItem("Edit driver", () => { stack.Close(); modes.EditCharacter(); }) { Description = () => "Stop the car first." });
                m.Add(new MenuItem("Save", () => { modes.SaveNow(); }));
                m.Add(new MenuItem("Return to Story Mode", () => { stack.Close(); modes.Exit(); }));
            };
            return m;
        }

        private Menu MyVehicles()
        {
            var m = new Menu("My vehicles", "Your collection");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                vehicles.Capture();
                foreach (var rec in saves.Data.Vehicles)
                {
                    var r = rec;
                    var info = Info(r);
                    m.Add(new MenuItem(Title(r, info), () => stack.Push(VehicleDetail(r)))
                    {
                        Value = () => r.Id == saves.Data.CurrentVehicleId ? "~p~IN USE" : Condition(r) + "%",
                        Description = () => info == null ? r.Model : info.Name + "  |  " + info.Class + "  |  Tier " + info.Tier
                    });
                }
            };
            return m;
        }

        private Menu VehicleDetail(OwnedVehicle r)
        {
            var m = new Menu(Title(r, Info(r)), "Vehicle");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                var info = Info(r);
                bool current = r.Id == saves.Data.CurrentVehicleId;
                m.Add(new MenuItem(current ? "Currently driving" : (cfg.DevMode ? "Drive this car (dev)" : "Swap at its garage"), () => { if (modes.SwitchVehicle(r)) stack.Close(); })
                    { Enabled = !current, Description = () => cfg.DevMode ? null : "Drive into the car's home garage and pick it on the garage screen." });
                m.Add(new MenuItem("Home garage") { Value = () => Garages == null ? "-" : Garages.GarageName(r.GarageId) });
                m.Add(new MenuItem("Favourite", () => { r.Favorite = !r.Favorite; saves.IsDirty = true; }) { Value = () => r.Favorite ? "Yes" : "No" });
                m.Add(new MenuItem("Nickname", () => { var s = Game.GetUserInput(r.Nickname ?? ""); if (s != null) { r.Nickname = s.Trim().Length == 0 ? null : s.Trim(); saves.IsDirty = true; } }) { Value = () => r.Nickname ?? "-" });
                m.Add(new MenuItem("License plate", () => modes.SetPlate(r, Game.GetUserInput(r.Plate ?? ""))) { Value = () => r.Plate });
                m.Add(new MenuItem("Condition") { Value = () => Condition(r) + "%" });
                m.Add(new MenuItem("Distance driven") { Value = () => r.Stats.DistanceKm.ToString("0.0") + " km" });
                m.Add(new MenuItem("Top speed recorded") { Value = () => r.Stats.TopSpeedKmh.ToString("0") + " km/h" });
                m.Add(new MenuItem("Recoveries") { Value = () => r.Stats.Recoveries.ToString() });
                m.Add(new MenuItem("Police escapes") { Value = () => r.Stats.PoliceEscapes.ToString() });
                m.Add(new MenuItem("Police heat") { Value = () => r.Heat.ToString("0.0") + " / 5", Description = () => "High heat: police notice this car sooner and start at a higher level. Drops each day; a new plate or respray cuts it." });
                m.Add(new MenuItem("Races won / entered") { Value = () => r.Stats.RacesWon + " / " + r.Stats.RacesEntered });
                m.Add(new MenuItem("Purchase price") { Value = () => "$" + r.PurchasePrice.ToString("N0") });
                m.Add(new MenuItem("Estimated value") { Value = () => "$" + PricingEngine.EstimatedValue(info, r.Stats.DistanceKm, r.EngineHealth, r.BodyHealth).ToString("N0") });
            };
            return m;
        }

        private Menu GarageList()
        {
            var m = new Menu("Garages", "Owned and for sale");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                if (Garages == null) return;
                foreach (var g in Garages.Definitions.OrderBy(x => Garages.Owned(x.Id) == null).ThenBy(x => x.Price))
                {
                    var def = g; bool owned = Garages.Owned(def.Id) != null;
                    m.Add(new MenuItem(def.Name, () => { Function.Call(Hash.SET_NEW_WAYPOINT, def.Position.X, def.Position.Y); GTA.UI.Notification.PostTicker("GPS set to " + def.Name + ".", true, false); })
                    {
                        Value = () => owned ? Garages.Used(def.Id) + " / " + Garages.Capacity(def.Id) : "$" + def.Price.ToString("N0"),
                        Description = () => def.KindLabel + (owned ? ", owned." : ", for sale.") + " Select to set GPS."
                    });
                }
            };
            return m;
        }

        private Menu CareerMenu()
        {
            var c = Career.CareerManager.Instance;
            var m = new Menu("Career", c.TierName);
            m.OnOpen = () =>
            {
                m.Items.Clear();
                m.Add(new MenuItem("Tier") { Value = () => c.Tier + " / 5", Enabled = false });
                m.Add(new MenuItem("REP") { Value = () => saves.Data.Rep.ToString("N0") + (c.Tier < 5 ? " / " + c.NextThreshold.ToString("N0") : ""), Enabled = false });
                m.Add(new MenuItem("Progress to next tier") { Value = () => (c.ProgressToNext * 100).ToString("0") + "%", Enabled = false });
                for (int t = 1; t <= 5; t++)
                {
                    int tier = t;
                    m.Add(new MenuItem(tier + ". " + Career.CareerManager.TierNames[tier]) { Value = () => tier <= c.Tier ? "~g~UNLOCKED" : c.Threshold(tier).ToString("N0") + " REP", Description = () => Career.CareerManager.TierUnlocks[tier] });
                }
                int wins = saves.Data.RaceHistory.Count(h => h.Position == 1);
                m.Add(new MenuItem("Races won / entered") { Value = () => wins + " / " + saves.Data.RaceHistory.Count, Enabled = false });
            };
            return m;
        }

        private Menu DealerList()
        {
            var m = new Menu("Dealerships", Dealers == null ? "" : "Day " + (Dealers.Calendar.Day + 1) + ", " + Dealers.Calendar.DayName);
            m.OnOpen = () =>
            {
                m.Items.Clear();
                if (Dealers == null) return;
                foreach (var dl in Dealers.Definitions)
                {
                    var def = dl;
                    m.Add(new MenuItem(def.Name, () => { Function.Call(Hash.SET_NEW_WAYPOINT, def.Position.X, def.Position.Y); GTA.UI.Notification.PostTicker("GPS set to " + def.Name + ".", true, false); })
                    {
                        Value = () => Dealers.Stock(def).Count + " cars",
                        Description = () => def.Tagline + " New stock in " + Dealers.DaysUntilRotation(def) + " day(s). Select to set GPS."
                    });
                }
            };
            return m;
        }

        private Menu Finances()
        {
            var m = new Menu("Finances", "Recent transactions");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                var d = saves.Data;
                m.Add(new MenuItem("Cash") { Value = () => "$" + d.Cash.ToString("N0") });
                m.Add(new MenuItem("REP") { Value = () => d.Rep.ToString("N0") });
                foreach (var e in Enumerable.Reverse(d.Ledger).Take(15))
                {
                    var entry = e;
                    m.Add(new MenuItem(entry.Description ?? entry.Category)
                    { Value = () => (entry.Cash >= 0 ? "~g~+$" : "~r~-$") + Math.Abs(entry.Cash).ToString("N0") + (entry.Rep != 0 ? "~s~ +" + entry.Rep + " REP" : "") });
                }
            };
            return m;
        }

        private Menu Catalogue()
        {
            var m = new Menu("Catalogue", "Classes");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                foreach (var g in registry.All.Where(v => v.Enabled).GroupBy(v => v.Class).OrderBy(g => g.Key))
                {
                    var cls = g.Key;
                    m.Add(new MenuItem(VehicleRegistry.ClassName(cls), () => stack.Push(ClassList(cls))) { Value = () => g.Count().ToString() });
                }
            };
            return m;
        }

        private Menu ClassList(VehicleClass cls)
        {
            var m = new Menu(VehicleRegistry.ClassName(cls), cfg.DevMode ? "DEV MODE: select = add free" : "Prices");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                foreach (var v in registry.All.Where(x => x.Enabled && x.Class == cls).OrderBy(x => x.BasePrice))
                {
                    var info = v;
                    string where = null; bool looked = false;
                    m.Add(new MenuItem(info.Name, cfg.DevMode ? (Action)(() => modes.DevGiveVehicle(info)) : () => stack.Push(OrderMenu(info)))
                    {
                        Value = () => "$" + info.BasePrice.ToString("N0"),
                        Description = () =>
                        {
                            if (!looked && Dealers != null) { var dd = Dealers.WhereSold(info); where = dd == null ? null : dd.Name; looked = true; }
                            return string.Format("Tier {0}  |  {1:0} km/h  |  Accel {2:0.00}  |  Grip {3:0.00}{4}  |  {5}",
                                info.Tier, info.MaxSpeed * 3.6f, info.Acceleration, info.Traction,
                                info.Source == VehicleSource.AddOn ? "  |  newer DLC / add-on" : "",
                                where != null ? "~g~In stock at " + where : "Not in stock this week, special order possible");
                        }
                    });
                }
            };
            return m;
        }

        private Menu OrderMenu(VehicleInfo info)
        {
            var m = new Menu("Special order", info.Name);
            var item = Dealers == null ? null : Dealers.SpecialOrderItem(info);
            if (item == null) { m.Add(new MenuItem("Not available for order") { Enabled = false }); return m; }
            m.Add(new MenuItem("Order for $" + item.Price.ToString("N0"), () =>
            {
                var err = Dealers.Purchase(item, "special order");
                if (err != null) GTA.UI.Notification.PostTicker("~r~" + err, true, false);
                stack.Pop();
            }) { Description = () => "Special orders cost 35% more than the showroom price ($" + info.BasePrice.ToString("N0") + ") and are delivered to your first garage with space." });
            m.Add(new MenuItem("Your cash") { Value = () => "$" + saves.Data.Cash.ToString("N0") });
            m.Add(new MenuItem("Cancel", () => stack.Pop()));
            return m;
        }

        private VehicleInfo Info(OwnedVehicle r) { return r.ModelHash != 0 ? registry.Get(r.ModelHash) : registry.Get(r.Model); }
        private static string Title(OwnedVehicle r, VehicleInfo info) { return !string.IsNullOrEmpty(r.Nickname) ? r.Nickname : info != null ? info.Name : r.Model; }
        private static int Condition(OwnedVehicle r) { return (int)Math.Round(Math.Max(0, Math.Min(100, (r.EngineHealth + r.BodyHealth) / 20f))); }

        public void Tick() { stack.Tick(); }
        public void Close() { stack.Close(); }
        public bool IsOpen { get { return stack.IsOpen; } }
    }
}
