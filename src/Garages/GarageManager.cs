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
    /// <summary>Real GTA Online garages: buy at the entrance, drive in, the interior loads with every car you
    /// keep there parked in its slot, pick a car on the Midnight Club garage screen and drive out.</summary>
    public sealed class GarageManager
    {
        private readonly SaveManager saves;
        private readonly TransactionManager money;
        private readonly VehicleRegistry registry;
        private readonly VehicleManager vehicles;
        private readonly MenuStack stack;
        private readonly string dataDir;
        private List<GarageDefinition> defs = new List<GarageDefinition>();
        private readonly Dictionary<string, Blip> blips = new Dictionary<string, Blip>();
        private readonly List<Vehicle> display = new List<Vehicle>();
        private GarageDefinition nearby;
        private int pinnedInterior;
        public bool IsInside { get; private set; }
        private const float MarkerDrawDistance = 60f;

        public string StarterGarageId { get; set; }

        public GarageManager(SaveManager saves, TransactionManager money, VehicleRegistry registry, VehicleManager vehicles, GameModeManager modes, MenuStack stack, string dataDir)
        {
            this.saves = saves; this.money = money; this.registry = registry; this.vehicles = vehicles; this.stack = stack; this.dataDir = dataDir;
            EventBus.Subscribe<ModeChangedEvent>(OnModeChanged);
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
        }

        // ------------------------------------------------------------------ queries
        public IEnumerable<GarageDefinition> Definitions { get { return defs; } }
        public GarageDefinition Def(string id) { return defs.FirstOrDefault(g => g.Id == id); }
        public OwnedGarage Owned(string id) { return saves.Data == null ? null : saves.Data.Garages.FirstOrDefault(g => g.Id == id); }
        public int Capacity(string id) { var d = Def(id); return d == null || Owned(id) == null ? 0 : d.Capacity; }
        public int Used(string id) { return saves.Data.Vehicles.Count(v => v.GarageId == id); }
        public string GarageName(string id) { var d = Def(id); return d == null ? "No garage" : d.Name; }
        public GarageDefinition AtGarage { get { return nearby; } }
        public VehicleRegistry Registry { get { return registry; } }

        public string FindFreeGarage(string preferred = null)
        {
            if (preferred != null && Owned(preferred) != null && Used(preferred) < Capacity(preferred)) return preferred;
            foreach (var o in saves.Data.Garages) if (Def(o.Id) != null && Used(o.Id) < Capacity(o.Id)) return o.Id;
            return null;
        }

        /// <summary>Gives a car a home garage and parking slot if any owned garage has room. Returns false if none.</summary>
        public bool AssignToFreeGarage(OwnedVehicle r, string preferred = null)
        {
            var id = FindFreeGarage(preferred);
            if (id == null) return false;
            r.GarageId = id; r.ParkingSlot = FreeSlot(id, r) + 1;
            saves.IsDirty = true;
            return true;
        }

        private int FreeSlot(string garageId, OwnedVehicle except)
        {
            var def = Def(garageId); if (def == null) return -1;
            var taken = new HashSet<int>(saves.Data.Vehicles.Where(v => v != except && v.GarageId == garageId && v.ParkingSlot > 0).Select(v => v.ParkingSlot - 1));
            for (int i = 0; i < def.Capacity; i++) if (!taken.Contains(i)) return i;
            return -1;
        }

        /// <summary>Repairs slot assignments (duplicates/out of range) for one garage.</summary>
        private void NormalizeSlots(GarageDefinition g)
        {
            var seen = new HashSet<int>();
            foreach (var v in saves.Data.Vehicles.Where(x => x.GarageId == g.Id))
            {
                int s = v.ParkingSlot - 1;
                if (s < 0 || s >= g.Capacity || seen.Contains(s)) { v.ParkingSlot = 0; }
                else seen.Add(s);
            }
            foreach (var v in saves.Data.Vehicles.Where(x => x.GarageId == g.Id && x.ParkingSlot == 0))
            {
                int s = FreeSlot(g.Id, v);
                if (s < 0) { v.GarageId = null; continue; }   // over capacity: becomes homeless (shouldn't happen)
                v.ParkingSlot = s + 1; seen.Add(s);
            }
        }

        // ------------------------------------------------------------------ world presence
        private void OnModeChanged(ModeChangedEvent e)
        {
            if (e.To == GameMode.MIDNIGHT_CLUB)
            {
                try
                {
                    defs = GarageDefinitions.Load(dataDir);
                    foreach (var v in saves.Data.Vehicles.Where(v => v.GarageId != null && Def(v.GarageId) == null)) { v.GarageId = null; v.ParkingSlot = 0; }
                    saves.Data.Garages.RemoveAll(o => Def(o.Id) == null);
                    GrantStarterGarage();
                }
                catch (Exception ex) { Log.Error("Loading garages", ex); }
                RefreshBlips();
            }
            else
            {
                Cleanup();
                ClearBlips();
            }
        }

        /// <summary>Every profile gets one free small garage, once (also existing profiles from before 0.4.1).</summary>
        private void GrantStarterGarage()
        {
            var g = Def(StarterGarageId ?? "");
            if (g == null || Owned(g.Id) != null || !saves.TryBeginTransaction("starter-garage")) return;
            saves.Data.Garages.Add(new OwnedGarage { Id = g.Id, PurchasedAtUtc = DateTime.UtcNow.ToString("o") });
            var cur = CurrentRecord();
            if (cur != null && cur.GarageId == null) { cur.GarageId = g.Id; cur.ParkingSlot = FreeSlot(g.Id, cur) + 1; }
            saves.Save();
            Notify("~p~MIDNIGHT CLUB~s~: you got a place to start: ~g~" + g.Name + "~s~ (" + g.Capacity + " cars). Find it on the map.");
            Log.Info("Granted starter garage " + g.Id);
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
                b.Scale = owned ? 0.9f : 0.7f;
                b.Name = owned ? "Midnight Club garage" : "Garage for sale";
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
            if (IsInside) return;
            var pos = Game.Player.Character.Position;
            nearby = null;
            foreach (var g in defs)
            {
                float dist = pos.DistanceTo(g.Position);
                if (dist > MarkerDrawDistance) continue;
                bool owned = Owned(g.Id) != null;
                var c = owned ? Color.FromArgb(140, 60, 220, 120) : Color.FromArgb(140, 170, 90, 255);
                World.DrawMarker(MarkerType.Cylinder, g.Position - new Vector3(0, 0, 0.95f), Vector3.Zero, Vector3.Zero,
                    new Vector3(g.Radius * 1.4f, g.Radius * 1.4f, 0.8f), c, false, false, false, null, null, false);
                if (dist < g.Radius) nearby = g;
            }
            if (nearby == null || stack.IsOpen) return;
            var car = vehicles.Current;
            if (car != null && car.Exists() && car.Speed > 3f) return;
            bool own = Owned(nearby.Id) != null;
            Screen.ShowHelpTextThisFrame("~INPUT_CONTEXT~ " + (own ? "Enter " + nearby.Name : nearby.Name + " – for sale"));
            if (Game.IsControlJustPressed(Control.Context))
            {
                if (own) Enter(nearby);
                else stack.Open(BuyMenu(nearby));
            }
        }

        private Menu BuyMenu(GarageDefinition g)
        {
            var m = new Menu(g.Name, g.KindLabel);
            m.Add(new MenuItem("Buy garage", () => Buy(g)) { Value = () => "$" + g.Price.ToString("N0"),
                Description = () => "Stores " + g.Capacity + " cars." + (g.Customizable ? " Customisable floor, lighting and numbering." : "") });
            m.Add(new MenuItem("Type") { Value = () => g.KindLabel });
            m.Add(new MenuItem("Capacity") { Value = () => g.Capacity + " cars" });
            m.Add(new MenuItem("Your cash") { Value = () => "$" + saves.Data.Cash.ToString("N0") });
            return m;
        }

        private void Buy(GarageDefinition g)
        {
            if (Owned(g.Id) != null) return;
            if (!money.CanAfford(g.Price)) { Notify("~r~Not enough cash~s~ for " + g.Name + "."); return; }
            if (!money.TrySpend(g.Price, "garage", "Bought " + g.Name, "garage-buy:" + g.Id, saveNow: false)) return;
            saves.Data.Garages.Add(new OwnedGarage { Id = g.Id, PurchasedAtUtc = DateTime.UtcNow.ToString("o") });
            var cur = CurrentRecord();
            if (cur != null && cur.GarageId == null) { cur.GarageId = g.Id; cur.ParkingSlot = FreeSlot(g.Id, cur) + 1; }
            saves.Save();
            stack.Close();
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "PROPERTY_PURCHASE", "HUD_AWARDS", true);
            Notify("~p~MIDNIGHT CLUB~s~: " + g.Name + " is yours. Drive in with ~INPUT_CONTEXT~.");
            RefreshBlips();
        }

        // ------------------------------------------------------------------ inside
        private void Enter(GarageDefinition g)
        {
            var cur = CurrentRecord();
            NormalizeSlots(g);
            string note = null;
            if (cur != null && cur.GarageId != g.Id)
            {
                int slot = FreeSlot(g.Id, cur);
                if (slot >= 0)
                {
                    if (cur.GarageId != null) note = Title(cur) + " now lives here (moved from " + GarageName(cur.GarageId) + ").";
                    cur.GarageId = g.Id; cur.ParkingSlot = slot + 1;
                }
                else if (cur.GarageId != null)
                    note = "This garage is full: " + Title(cur) + " was sent back to " + GarageName(cur.GarageId) + ".";
                else { Notify("~r~" + g.Name + " is full~s~ and your car has nowhere else to go."); return; }
            }
            if (!saves.Data.Vehicles.Any(v => v.GarageId == g.Id)) { Notify("No cars stored here."); return; }

            IsInside = true;
            if (Cameras.CameraSystem.Instance != null) Cameras.CameraSystem.Instance.Release();
            var ped = Game.Player.Character;
            OwnedVehicle chosen = null;
            try
            {
                Screen.FadeOut(500); Script.Wait(550);
                vehicles.Capture();
                vehicles.Despawn(false);
                saves.IsDirty = true;

                ped.Position = g.SpawnIn.Position; ped.Heading = g.SpawnIn.Heading;
                Function.Call(Hash.FREEZE_ENTITY_POSITION, ped, true);
                Function.Call(Hash.SET_ENTITY_VISIBLE, ped, false, false);
                LoadInterior(g);

                var parked = new List<KeyValuePair<OwnedVehicle, Vehicle>>();
                foreach (var r in saves.Data.Vehicles.Where(v => v.GarageId == g.Id).OrderBy(v => v.ParkingSlot))
                {
                    try
                    {
                        var spot = g.Slots[r.ParkingSlot - 1];
                        var v = vehicles.SpawnDisplay(r, spot.Position + new Vector3(0, 0, 0.6f), spot.Heading);
                        display.Add(v);
                        parked.Add(new KeyValuePair<OwnedVehicle, Vehicle>(r, v));
                    }
                    catch (Exception ex) { Log.Error("Display spawn " + r.Model, ex); }
                }
                Script.Wait(300);
                foreach (var v in display) if (v.Exists()) Function.Call(Hash.FREEZE_ENTITY_POSITION, v, true);

                var screen = new GarageScreen(this, g, parked, cur, saves, money, note);
                Screen.FadeIn(500);
                chosen = screen.Run();

                Screen.FadeOut(500); Script.Wait(550);
            }
            catch (Exception ex) { Log.Error("Garage session failed", ex); }
            finally
            {
                Cleanup();
                ped = Game.Player.Character;
                Function.Call(Hash.FREEZE_ENTITY_POSITION, ped, false);
                Function.Call(Hash.SET_ENTITY_VISIBLE, ped, true, false);
                if (chosen == null) chosen = cur ?? saves.Data.Vehicles.FirstOrDefault();
                try
                {
                    var car = vehicles.Spawn(chosen, g.SpawnOut.Position, g.SpawnOut.Heading);
                    ped.SetIntoVehicle(car, VehicleSeat.Driver);
                    car.IsEngineRunning = true;
                    saves.Data.CurrentVehicleId = chosen.Id;
                }
                catch (Exception ex) { Log.Error("Spawning car outside garage", ex); }
                IsInside = false;
                saves.Save();
                Function.Call(Hash.SET_GAMEPLAY_CAM_RELATIVE_HEADING, 0f);
                Screen.FadeIn(600);
            }
        }

        private void LoadInterior(GarageDefinition g)
        {
            if (!string.IsNullOrEmpty(g.Ipl) && !Function.Call<bool>(Hash.IS_IPL_ACTIVE, g.Ipl)) Function.Call(Hash.REQUEST_IPL, g.Ipl);
            var p = g.Slots.Count > 0 ? g.Slots[0].Position : g.SpawnIn.Position;
            int interior = 0, t0 = Game.GameTime;
            while (interior == 0 && Game.GameTime - t0 < 3000) { interior = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, p.X, p.Y, p.Z); if (interior == 0) Script.Wait(50); }
            if (interior == 0) { Log.Warn("No interior found at " + p + " for " + g.Id); return; }
            bool disabled = Function.Call<bool>(Hash.IS_INTERIOR_DISABLED, interior), capped = Function.Call<bool>(Hash.IS_INTERIOR_CAPPED, interior);
            if (disabled) Function.Call(Hash.DISABLE_INTERIOR, interior, false);
            if (capped) Function.Call(Hash.CAP_INTERIOR, interior, false);
            Function.Call(Hash.SET_INTERIOR_ACTIVE, interior, true);
            Function.Call(Hash.PIN_INTERIOR_IN_MEMORY, interior);
            t0 = Game.GameTime;
            while (!Function.Call<bool>(Hash.IS_INTERIOR_READY, interior) && Game.GameTime - t0 < 8000) Script.Wait(50);
            Log.Info("Interior " + interior + " was disabled=" + disabled + " capped=" + capped);
            pinnedInterior = interior;
            if (g.Customizable) ApplyCustomization(g, Owned(g.Id), interior);
            Log.Info("Garage interior " + g.Id + " id " + interior + " ready=" + Function.Call<bool>(Hash.IS_INTERIOR_READY, interior));
        }

        // CEO garage entity sets (names from the game's import/export garage interiors)
        public static readonly string[] DecorSets = { "Garage_Decor_01", "Garage_Decor_02", "Garage_Decor_03", "Garage_Decor_04" };
        public static readonly string[] DecorNames = { "Concrete", "Plain", "Marble", "Wooden" };
        public static string LightingSet(int i) { return "Lighting_Option0" + i; }
        public static string NumberingSet(int i) { return "Numbering_Style0" + i + "_N1"; }

        public void ApplyCustomization(GarageDefinition g, OwnedGarage o, int interior = 0)
        {
            if (interior == 0) interior = pinnedInterior;
            if (interior == 0 || o == null) return;
            int decor = Math.Max(1, o.Decor), light = Math.Max(1, o.Lighting), num = Math.Max(1, o.Numbering);
            for (int i = 1; i <= 4; i++) Function.Call(Hash.DEACTIVATE_INTERIOR_ENTITY_SET, interior, DecorSets[i - 1]);
            for (int i = 1; i <= 9; i++) { Function.Call(Hash.DEACTIVATE_INTERIOR_ENTITY_SET, interior, LightingSet(i)); Function.Call(Hash.DEACTIVATE_INTERIOR_ENTITY_SET, interior, NumberingSet(i)); }
            Function.Call(Hash.ACTIVATE_INTERIOR_ENTITY_SET, interior, DecorSets[decor - 1]);
            Function.Call(Hash.ACTIVATE_INTERIOR_ENTITY_SET, interior, LightingSet(light));
            Function.Call(Hash.ACTIVATE_INTERIOR_ENTITY_SET, interior, NumberingSet(num));
            Function.Call(Hash.REFRESH_INTERIOR, interior);
        }

        /// <summary>Removes display cars and unpins the interior. Safe to call any time.</summary>
        private void Cleanup()
        {
            foreach (var v in display) try { if (v.Exists()) v.Delete(); } catch { }
            display.Clear();
            if (pinnedInterior != 0) { try { Function.Call(Hash.UNPIN_INTERIOR, pinnedInterior); } catch { } pinnedInterior = 0; }
            try { Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0); Function.Call(Hash.DISPLAY_RADAR, true); } catch { }
        }

        /// <summary>Moves a car to another owned garage with free space. Returns the new garage name or null.</summary>
        public string MoveToOtherGarage(OwnedVehicle r, string fromId)
        {
            foreach (var o in saves.Data.Garages)
            {
                if (o.Id == fromId || Def(o.Id) == null) continue;
                int slot = FreeSlot(o.Id, r);
                if (slot < 0) continue;
                r.GarageId = o.Id; r.ParkingSlot = slot + 1; saves.IsDirty = true;
                return Def(o.Id).Name;
            }
            return null;
        }

        private OwnedVehicle CurrentRecord() { return saves.Data.Vehicles.FirstOrDefault(v => v.Id == saves.Data.CurrentVehicleId); }

        public string Title(OwnedVehicle r)
        {
            if (!string.IsNullOrEmpty(r.Nickname)) return r.Nickname;
            var i = Info(r);
            return i != null ? i.Name : r.Model;
        }

        public VehicleInfo Info(OwnedVehicle r) { return r.ModelHash != 0 ? registry.Get(r.ModelHash) : registry.Get(r.Model); }

        private static void Notify(string s) { Notification.PostTicker(s, true, false); }
    }
}
