using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.UI;
using MidnightClub.Vehicles;

namespace MidnightClub.Garages
{
    /// <summary>Garage ownership, capacity, upgrades and vehicle storage. Garages are drive-in points on the map
    /// (interiors arrive with the Online-location work in Phase 5). Driven entirely by EventBus events.</summary>
    public sealed class GarageManager
    {
        private readonly SaveManager saves;
        private readonly TransactionManager money;
        private readonly VehicleRegistry registry;
        private readonly VehicleManager vehicles;
        private readonly GameModeManager modes;
        private readonly MenuStack stack;
        private readonly string dataDir;
        private List<GarageDefinition> defs = new List<GarageDefinition>();
        private readonly Dictionary<string, Blip> blips = new Dictionary<string, Blip>();
        private GarageDefinition nearby;
        private const float InteractRadius = 6f, MarkerDrawDistance = 80f;

        public GarageManager(SaveManager saves, TransactionManager money, VehicleRegistry registry, VehicleManager vehicles, GameModeManager modes, MenuStack stack, string dataDir)
        {
            this.saves = saves; this.money = money; this.registry = registry; this.vehicles = vehicles; this.modes = modes; this.stack = stack; this.dataDir = dataDir;
            EventBus.Subscribe<ModeChangedEvent>(OnModeChanged);
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
        }

        public IEnumerable<GarageDefinition> Definitions { get { return defs; } }
        public GarageDefinition Def(string id) { return defs.FirstOrDefault(g => g.Id == id); }
        public OwnedGarage Owned(string id) { return saves.Data == null ? null : saves.Data.Garages.FirstOrDefault(g => g.Id == id); }
        public int Capacity(string id) { var d = Def(id); var o = Owned(id); return d == null || o == null ? 0 : d.CapacityAt(o.UpgradeLevel); }
        public int Used(string id) { return saves.Data.Vehicles.Count(v => v.GarageId == id); }
        public string GarageName(string id) { var d = Def(id); return d == null ? "No garage" : d.Name; }

        private void OnModeChanged(ModeChangedEvent e)
        {
            if (e.To == GameMode.MIDNIGHT_CLUB)
            {
                try { defs = GarageDefinitions.Load(dataDir); } catch (Exception ex) { Log.Error("Loading garages.ini", ex); }
                RefreshBlips();
            }
            else ClearBlips();
        }

        private void RefreshBlips()
        {
            ClearBlips();
            foreach (var g in defs)
            {
                bool owned = Owned(g.Id) != null;
                var b = World.CreateBlip(g.Position);
                b.Sprite = owned ? BlipSprite.Garage : BlipSprite.GarageForSale;
                b.Color = owned ? BlipColor.Green : BlipColor.White;
                b.IsShortRange = !owned;
                b.Name = owned ? "MC Garage: " + g.Name : "Garage for sale";
                blips[g.Id] = b;
            }
        }

        private void ClearBlips()
        {
            foreach (var b in blips.Values) try { if (b.Exists()) b.Delete(); } catch { }
            blips.Clear();
        }

        private void Tick()
        {
            var ped = Game.Player.Character;
            var pos = ped.Position;
            nearby = null;
            foreach (var g in defs)
            {
                float dist = pos.DistanceTo(g.Position);
                if (dist > MarkerDrawDistance) continue;
                var c = Owned(g.Id) != null ? Color.FromArgb(150, 60, 220, 120) : Color.FromArgb(150, 170, 90, 255);
                World.DrawMarker(MarkerType.Cylinder, g.Position - new Vector3(0, 0, 1f), Vector3.Zero, Vector3.Zero,
                    new Vector3(InteractRadius * 1.6f, InteractRadius * 1.6f, 1.2f), c, false, false, false, null, null, false);
                if (dist < InteractRadius) nearby = g;
            }
            if (nearby == null || stack.IsOpen) return;
            var car = vehicles.Current;
            if (car != null && car.Exists() && car.Speed > 3f) return;
            Screen.ShowHelpTextThisFrame("~INPUT_CONTEXT~ " + (Owned(nearby.Id) != null ? "Enter " : "View ") + nearby.Name);
            if (Game.IsControlJustPressed(Control.Context)) stack.Open(GarageMenu(nearby));
        }

        /// <summary>The garage the player is currently parked at (used to gate car switching).</summary>
        public GarageDefinition AtGarage { get { return nearby; } }

        // ------------------------------------------------------------------ menus
        private Menu GarageMenu(GarageDefinition g)
        {
            var m = new Menu(g.Name, g.Category + " garage");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                var owned = Owned(g.Id);
                if (owned == null)
                {
                    m.Add(new MenuItem("Buy garage", () => Buy(g)) { Value = () => "$" + g.Price.ToString("N0"),
                        Description = () => "Stores " + g.CapacityAt(0) + " cars, upgradable to " + g.CapacityAt(g.MaxUpgrades) + "." + (g.CareerTier > 1 ? " Recommended career tier: " + g.CareerTier + "." : "") });
                    m.Add(new MenuItem("Capacity") { Value = () => g.CapacityAt(0) + " (max " + g.CapacityAt(g.MaxUpgrades) + ")" });
                    return;
                }
                m.Add(new MenuItem("Stored here") { Value = () => Used(g.Id) + " / " + Capacity(g.Id) });
                foreach (var rec in saves.Data.Vehicles.Where(v => v.GarageId == g.Id).OrderByDescending(v => v.Favorite))
                {
                    var r = rec;
                    bool current = r.Id == saves.Data.CurrentVehicleId;
                    m.Add(new MenuItem((r.Favorite ? "* " : "") + VehicleTitle(r), current ? null : (Action)(() => { if (modes.SwitchVehicle(r)) stack.Close(); }))
                    { Value = () => current ? "~p~DRIVING" : "Take out", Enabled = !current });
                }
                var cur = CurrentRecord();
                if (cur != null && cur.GarageId != g.Id)
                    m.Add(new MenuItem("Make this the home of your " + VehicleTitle(cur), () => Register(cur, g))
                    { Description = () => cur.GarageId == null ? "Your current car has no garage yet." : "Moves it from " + GarageName(cur.GarageId) + "." });
                if (owned.UpgradeLevel < g.MaxUpgrades)
                {
                    long cost = g.UpgradeCost(owned.UpgradeLevel + 1);
                    m.Add(new MenuItem("Upgrade capacity", () => Upgrade(g)) { Value = () => "$" + cost.ToString("N0"),
                        Description = () => "Level " + (owned.UpgradeLevel + 1) + ": " + g.CapacityAt(owned.UpgradeLevel + 1) + " cars." });
                }
                else m.Add(new MenuItem("Capacity fully upgraded") { Enabled = false });
            };
            return m;
        }

        private void Buy(GarageDefinition g)
        {
            if (Owned(g.Id) != null) return;
            if (!money.CanAfford(g.Price)) { Notify("~r~Not enough cash~s~ for " + g.Name + "."); return; }
            if (!money.TrySpend(g.Price, "garage", "Bought " + g.Name, "garage-buy:" + g.Id, saveNow: false)) return;
            saves.Data.Garages.Add(new OwnedGarage { Id = g.Id, PurchasedAtUtc = DateTime.UtcNow.ToString("o") });
            var cur = CurrentRecord();
            if (cur != null && cur.GarageId == null) cur.GarageId = g.Id;   // first garage becomes home of the current car
            saves.Save();
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "PROPERTY_PURCHASE", "HUD_AWARDS", true);
            Notify("~p~MIDNIGHT CLUB~s~: " + g.Name + " is yours.");
            RefreshBlips();
            stack.Open(GarageMenu(g));
        }

        private void Upgrade(GarageDefinition g)
        {
            var o = Owned(g.Id); if (o == null || o.UpgradeLevel >= g.MaxUpgrades) return;
            int next = o.UpgradeLevel + 1;
            if (!money.TrySpend(g.UpgradeCost(next), "garage", g.Name + " upgrade " + next, "garage-upg:" + g.Id + ":" + next, saveNow: false)) { Notify("~r~Not enough cash~s~ for the upgrade."); return; }
            o.UpgradeLevel = next;
            saves.Save();
            Notify(g.Name + " now holds ~g~" + Capacity(g.Id) + "~s~ cars.");
            if (stack.Current != null && stack.Current.OnOpen != null) stack.Current.OnOpen();
        }

        private void Register(OwnedVehicle r, GarageDefinition g)
        {
            if (Used(g.Id) >= Capacity(g.Id)) { Notify("~r~" + g.Name + " is full.~s~ Upgrade it or move a car out."); return; }
            r.GarageId = g.Id; saves.IsDirty = true;
            Notify(VehicleTitle(r) + " now lives at " + g.Name + ".");
            if (stack.Current != null && stack.Current.OnOpen != null) stack.Current.OnOpen();
        }

        /// <summary>Finds a garage with free space, preferring the given one. Used when acquiring cars.</summary>
        public string FindFreeGarage(string preferred = null)
        {
            if (preferred != null && Owned(preferred) != null && Used(preferred) < Capacity(preferred)) return preferred;
            foreach (var o in saves.Data.Garages) if (Def(o.Id) != null && Used(o.Id) < Capacity(o.Id)) return o.Id;
            return null;
        }

        public int TotalFreeSlots() { return saves.Data.Garages.Where(o => Def(o.Id) != null).Sum(o => Capacity(o.Id) - Used(o.Id)); }

        private OwnedVehicle CurrentRecord() { return saves.Data.Vehicles.FirstOrDefault(v => v.Id == saves.Data.CurrentVehicleId); }

        private string VehicleTitle(OwnedVehicle r)
        {
            if (!string.IsNullOrEmpty(r.Nickname)) return r.Nickname;
            var i = r.ModelHash != 0 ? registry.Get(r.ModelHash) : registry.Get(r.Model);
            return i != null ? i.Name : r.Model;
        }

        private static void Notify(string s) { Notification.PostTicker(s, true, false); }
    }
}
