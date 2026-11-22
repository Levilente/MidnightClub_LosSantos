using System;
using System.Linq;
using GTA;
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
                m.Add(new MenuItem("My vehicles", () => stack.Push(MyVehicles())) { Value = () => d.Vehicles.Count.ToString() });
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
                m.Add(new MenuItem(current ? "Currently driving" : "Drive this car", () => { if (modes.SwitchVehicle(r)) stack.Close(); }) { Enabled = !current });
                m.Add(new MenuItem("Nickname", () => { var s = Game.GetUserInput(r.Nickname ?? ""); if (s != null) { r.Nickname = s.Trim().Length == 0 ? null : s.Trim(); saves.IsDirty = true; } }) { Value = () => r.Nickname ?? "-" });
                m.Add(new MenuItem("License plate", () => modes.SetPlate(r, Game.GetUserInput(r.Plate ?? ""))) { Value = () => r.Plate });
                m.Add(new MenuItem("Condition") { Value = () => Condition(r) + "%" });
                m.Add(new MenuItem("Distance driven") { Value = () => r.Stats.DistanceKm.ToString("0.0") + " km" });
                m.Add(new MenuItem("Top speed recorded") { Value = () => r.Stats.TopSpeedKmh.ToString("0") + " km/h" });
                m.Add(new MenuItem("Recoveries") { Value = () => r.Stats.Recoveries.ToString() });
                m.Add(new MenuItem("Races won / entered") { Value = () => r.Stats.RacesWon + " / " + r.Stats.RacesEntered });
                m.Add(new MenuItem("Purchase price") { Value = () => "$" + r.PurchasePrice.ToString("N0") });
                m.Add(new MenuItem("Estimated value") { Value = () => "$" + PricingEngine.EstimatedValue(info, r.Stats.DistanceKm, r.EngineHealth, r.BodyHealth).ToString("N0") });
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
                    m.Add(new MenuItem(Vehicle.GetClassDisplayName(cls), () => stack.Push(ClassList(cls))) { Value = () => g.Count().ToString() });
                }
            };
            return m;
        }

        private Menu ClassList(VehicleClass cls)
        {
            var m = new Menu(Vehicle.GetClassDisplayName(cls), cfg.DevMode ? "DEV MODE: select = add free" : "Prices");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                foreach (var v in registry.All.Where(x => x.Enabled && x.Class == cls).OrderBy(x => x.BasePrice))
                {
                    var info = v;
                    m.Add(new MenuItem(info.Name, cfg.DevMode ? (Action)(() => modes.DevGiveVehicle(info)) : null)
                    {
                        Value = () => "$" + info.BasePrice.ToString("N0"),
                        Description = () => string.Format("Tier {0}  |  {1:0} km/h  |  Accel {2:0.00}  |  Grip {3:0.00}{4}{5}",
                            info.Tier, info.MaxSpeed * 3.6f, info.Acceleration, info.Traction,
                            info.Source == VehicleSource.AddOn ? "  |  newer DLC / add-on" : "",
                            cfg.DevMode ? "" : "  |  Sold at dealerships (coming in Phase 4)")
                    });
                }
            };
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
