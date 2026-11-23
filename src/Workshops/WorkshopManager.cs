using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.UI;
using MidnightClub.Vehicles;

namespace MidnightClub.Workshops
{
    public sealed class WorkshopDefinition
    {
        public string Id, Name; public Vector3 Position; public bool Bennys;
        /// <summary>Specialty (GTA VI-style mod shops): Retro, Muscle, Luxury, Tuner, Lowrider, Racing, OffRoad.</summary>
        public string Specialty, Description; public int DiscountTab = -1;
        public WorkshopDefinition(string id, string name, float x, float y, float z, bool bennys = false) { Id = id; Name = name; Position = new Vector3(x, y, z); Bennys = bennys; }
    }

    /// <summary>LS Customs / Beeker's / Benny's: drive in, tune and style the car you're driving.</summary>
    public sealed class WorkshopManager
    {
        private readonly SaveManager saves;
        private readonly TransactionManager money;
        private readonly VehicleRegistry registry;
        private readonly VehicleManager vehicles;
        private readonly MenuStack stack;
        private readonly List<Blip> blips = new List<Blip>();
        private WorkshopDefinition nearby;
        public bool IsBusy { get; private set; }

        public static readonly WorkshopDefinition[] All =
        {
            // Tabs: 0 Pro builds, 1 Cosmetics, 2 Bodywork, 3 Performance, 4 Wheels, 5 Interior. DiscountTab = specialty (-15 %).
            new WorkshopDefinition("lsc_lamesa", "Coastal Auto Care", 731.81f, -1088.82f, 22.17f) { Specialty = "Retro", DiscountTab = 4, Description = "Family-run shop. Custom parts and retro cosmetics for anything with a little history." },
            new WorkshopDefinition("lsc_burton", "Euroform Automotive", -337.38f, -136.92f, 39.01f) { Specialty = "Luxury", DiscountTab = 1, Description = "Custom parts and high-end cosmetics for European and luxury cars." },
            new WorkshopDefinition("lsc_lsia", "Vice Auto Customs", -1155.53f, -2007.18f, 13.18f) { Specialty = "Tuner", DiscountTab = 2, Description = "Custom parts and street-racing cosmetics. Stance, aero and drift setups." },
            new WorkshopDefinition("hayes", "Whisler Motorsport", 483.0f, -1316.0f, 29.2f) { Specialty = "Racing", DiscountTab = 3, Description = "Race shop. Custom parts and track-racing setups, race slicks and full ECU maps." },
            new WorkshopDefinition("beekers", "Capri Classic", 110.99f, 6626.39f, 31.89f) { Specialty = "Muscle", DiscountTab = 3, Description = "Custom parts and American muscle car cosmetics. Big engines, loud exhausts." },
            new WorkshopDefinition("lsc_harmony", "Wasteland Off-Road", 1175.04f, 2640.21f, 37.75f) { Specialty = "Off-road", DiscountTab = 4, Description = "Custom parts and all-terrain cosmetics. Lift, armour and off-road rubber." },
            new WorkshopDefinition("bennys_ws", "Lowballers Customs", -211.5f, -1324.0f, 30.9f, true) { Specialty = "Lowrider", DiscountTab = 5, Description = "Custom rims and interiors for classic and antique sedans. Full custom bodywork." },
        };

        public WorkshopManager(SaveManager saves, TransactionManager money, VehicleRegistry registry, VehicleManager vehicles, MenuStack stack)
        {
            this.saves = saves; this.money = money; this.registry = registry; this.vehicles = vehicles; this.stack = stack;
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To == GameMode.MIDNIGHT_CLUB) PlaceBlips(); else { ClearBlips(); CloseDoors(); CloseAllGates(); } });
            EventBus.Subscribe<Explore.DiscoveredEvent>(e => { if (e.Id.StartsWith("ws:")) PlaceBlips(); });
        }

        // Benny's roll-up door stays shut in Story Mode; register it with the door system and hold it open.
        private static readonly Vector3 BennysDoorPos = new Vector3(-205.6828f, -1310.683f, 30.29572f);
        private static readonly int BennysDoorModel = new Model("lr_prop_supermod_door_01").Hash;
        private static readonly int BennysDoorId = unchecked((int)0x4D43B001);
        private bool doorOpen;

        private void KeepDoorsOpen(Vector3 playerPos)
        {
            if (playerPos.DistanceTo(BennysDoorPos) > 60f) return;
            try
            {
                if (!Function.Call<bool>(Hash.IS_DOOR_REGISTERED_WITH_SYSTEM, BennysDoorId))
                    Function.Call(Hash.ADD_DOOR_TO_SYSTEM, BennysDoorId, BennysDoorModel, BennysDoorPos.X, BennysDoorPos.Y, BennysDoorPos.Z, false, false, false);
                if (!doorOpen)
                {
                    Function.Call(Hash.DOOR_SYSTEM_SET_DOOR_STATE, BennysDoorId, 0, false, true);   // unlocked
                    Function.Call(Hash.DOOR_SYSTEM_SET_HOLD_OPEN, BennysDoorId, true);
                    Log.Info("Benny's door opened");
                    doorOpen = true;
                }
                Function.Call(Hash.DOOR_SYSTEM_SET_OPEN_RATIO, BennysDoorId, 1f, false, true);
            }
            catch (Exception ex) { Log.Error("Benny's door", ex); doorOpen = true; }
        }

        private void CloseDoors()
        {
            if (!doorOpen) return;
            try
            {
                Function.Call(Hash.DOOR_SYSTEM_SET_HOLD_OPEN, BennysDoorId, false);
                Function.Call(Hash.DOOR_SYSTEM_SET_OPEN_RATIO, BennysDoorId, 0f, false, true);
                Function.Call(Hash.DOOR_SYSTEM_SET_DOOR_STATE, BennysDoorId, 1, false, true);
            }
            catch { }
            doorOpen = false;
        }

        private void PlaceBlips()
        {
            ClearBlips();
            foreach (var w in All)
            {
                Explore.Discovery.Register("ws:" + w.Id, w.Name, "Workshop", w.Position);
                if (!Explore.Discovery.Known("ws:" + w.Id)) continue;
                var b = UI.BlipRegistry.Own(World.CreateBlip(w.Position));
                b.Sprite = w.Bennys ? BlipSprite.Bennys : BlipSprite.LosSantosCustoms;
                b.Name = w.Bennys ? "Benny's workshop" : "Workshop"; b.IsShortRange = true;
                blips.Add(b);
            }
        }

        private void ClearBlips() { foreach (var b in blips) try { if (b.Exists()) b.Delete(); } catch { } blips.Clear(); }

        private void Tick()
        {
            if (IsBusy) return;
            if (Racing.RaceManager.IsRacing) return;
            var pos = Game.Player.Character.Position;
            KeepDoorsOpen(pos);
            nearby = null;
            foreach (var w in All)
            {
                float d = pos.DistanceTo(w.Position);
                if (d < 90f) OpenGates(w);
                else if (d > 140f) CloseGates(w);
                if (d > 90f) continue;
                if (!Explore.Discovery.Known("ws:" + w.Id)) continue;
                var ent = Entrance(w);
                bool inside = d < 5f;
                // marker outside the door says "drive in"; inside the bay the shop opens once you stop
                UI.WorldMarker.Draw(inside ? w.Position : ent, 3f, Color.FromArgb(255, 251, 191, 36), MarkerType.Boxes, w.Name, inside ? "Stop to open the shop" : "Drive in  ·  " + (w.Specialty ?? "Mod") + " mod shop", pos.DistanceTo(inside ? w.Position : ent), inside);
                if (inside) nearby = w;
            }
            var car = vehicles.Current;
            if (nearby == null || stack.IsOpen || car == null || !car.Exists() || !Game.Player.Character.IsInVehicle(car)) { stoppedSince = -1; return; }
            if (car.Speed > 0.8f) { stoppedSince = -1; Screen.ShowHelpTextThisFrame("Stop in the bay to open " + nearby.Name + "."); return; }
            if (stoppedSince < 0) stoppedSince = Game.GameTime;
            if (Game.GameTime - stoppedSince > 600 || UI.Input.JustPressed(Control.Context)) { stoppedSince = -1; Enter(nearby); }
        }
        private int stoppedSince = -1;

        private void Enter(WorkshopDefinition w)
        {
            IsBusy = true;
            try { new WorkshopScreen(w, vehicles, registry, saves, money).Run(); }
            catch (Exception ex) { Log.Error("Workshop failed", ex); }
            finally { IsBusy = false; leaveBlockUntil = Game.GameTime; }
            // don't reopen until the car has left the bay
            while (vehicles.Current != null && vehicles.Current.Exists() && vehicles.Current.Position.DistanceTo(w.Position) < 6f && Game.GameTime - leaveBlockUntil < 30000)
            {
                Screen.ShowHelpTextThisFrame("Drive out to leave " + w.Name + ".");
                UI.Shard.Tick();
                Script.Wait(0);
                if (Racing.RaceManager.IsRacing) break;
            }
        }
        private int leaveBlockUntil;

        private readonly Dictionary<string, Vector3> entrances = new Dictionary<string, Vector3>();
        private Vector3 Entrance(WorkshopDefinition w)
        {
            Vector3 e;
            if (entrances.TryGetValue(w.Id, out e)) return e;
            float h;
            e = Racing.RoadGraph.ClearSpot(w.Position, new List<Vector3> { w.Position }, new List<Vector3>(), 13f, out h);
            if (e == Vector3.Zero || e.DistanceTo(w.Position) > 45f) e = w.Position;
            entrances[w.Id] = e;
            return e;
        }

        // ------------------------------------------------------------------ gates
        // Story Mode keeps the shop roller doors shut without the vanilla shop scripts. Find the door props between
        // the street and the bay: known door models are opened through the game's door system (animated); if one
        // doesn't move (or the model isn't known) it is made passable (hidden, no collision) while you're nearby.
        private static readonly int[] DoorModels =
        {
            new Model("prop_com_ls_door_01").Hash, new Model("prop_id2_11_gdoor").Hash, new Model("prop_com_gar_door_01").Hash,
            new Model("lr_prop_supermod_door_01").Hash, new Model("v_ilev_carmod3door").Hash, new Model("prop_sc1_21_g_door_01").Hash,
            new Model("prop_bkr_door_01").Hash, new Model("prop_gar_door_01").Hash, new Model("prop_gar_door_02").Hash,
            new Model("prop_gar_door_03").Hash, new Model("prop_gar_door_04").Hash, new Model("prop_gar_door_05").Hash,
        };
        private sealed class Gate { public Prop P; public int DoorId; public Vector3 Rot0, Pos0; public int CheckAt; public bool Hidden; }
        private readonly Dictionary<string, List<Gate>> gates = new Dictionary<string, List<Gate>>();
        private readonly Dictionary<string, int> gateRescan = new Dictionary<string, int>();
        private readonly HashSet<string> loggedNone = new HashSet<string>();

        private void OpenGates(WorkshopDefinition w)
        {
            List<Gate> list;
            int rescan; gateRescan.TryGetValue(w.Id, out rescan);
            if ((!gates.TryGetValue(w.Id, out list) || list.Count == 0) && Game.GameTime >= rescan)
            {
                gateRescan[w.Id] = Game.GameTime + 3000;
                list = new List<Gate>(); gates[w.Id] = list;
                var ent = Entrance(w); var mid = (ent + w.Position) * 0.5f;
                try
                {
                    foreach (var p in World.GetNearbyProps(mid, 18f))
                    {
                        if (p == null || !p.Exists()) continue;
                        int hash = p.Model.Hash; bool known = Array.IndexOf(DoorModels, hash) >= 0;
                        Vector3 mn, mx; p.Model.GetDimensions(out mn, out mx); var size = mx - mn;
                        float wide = Math.Max(size.X, size.Y), thin = Math.Min(size.X, size.Y);
                        bool gateShape = wide >= 2.8f && thin <= 0.9f && size.Z >= 2.0f && size.Z <= 7f;
                        if (!known && !gateShape) continue;
                        // must sit between the street and the bay
                        if (DistToSegment(p.Position, ent, w.Position) > 6f) continue;
                        var g = new Gate { P = p, Rot0 = p.Rotation, Pos0 = p.Position, CheckAt = Game.GameTime + 1500 };
                        if (known)
                        {
                            g.DoorId = unchecked((int)(0x4D430000 ^ p.Handle));
                            var dp = p.Position;
                            Function.Call(Hash.ADD_DOOR_TO_SYSTEM, g.DoorId, hash, dp.X, dp.Y, dp.Z, false, false, false);
                            Function.Call(Hash.DOOR_SYSTEM_SET_DOOR_STATE, g.DoorId, 0, false, true);
                            Function.Call(Hash.DOOR_SYSTEM_SET_HOLD_OPEN, g.DoorId, true);
                            Function.Call(Hash.DOOR_SYSTEM_SET_OPEN_RATIO, g.DoorId, 1f, false, true);
                        }
                        else Hide(g);
                        list.Add(g);
                        Log.Info("Shop gate at " + w.Id + ": model " + hash + (known ? " (door system)" : " (made passable)") + " size " + size);
                    }
                }
                catch (Exception ex) { Log.Error("Shop gates " + w.Id, ex); }
                if (list.Count == 0 && !loggedNone.Contains(w.Id)) { loggedNone.Add(w.Id); Log.Info("Shop gate at " + w.Id + ": none found yet"); }
            }
            if (list == null) return;
            foreach (var g in list)
            {
                if (g.P == null || !g.P.Exists()) continue;
                if (g.DoorId != 0 && !g.Hidden)
                {
                    Function.Call(Hash.DOOR_SYSTEM_SET_OPEN_RATIO, g.DoorId, 1f, false, true);
                    if (g.CheckAt > 0 && Game.GameTime > g.CheckAt)
                    {
                        g.CheckAt = 0;
                        bool moved = (g.P.Rotation - g.Rot0).Length() > 5f || g.P.Position.DistanceTo(g.Pos0) > 0.5f;
                        if (!moved) { Hide(g); Log.Info("Shop gate at " + w.Id + " did not open through the door system; made passable"); }
                    }
                }
                if (g.Hidden) Function.Call(Hash.SET_ENTITY_LOCALLY_INVISIBLE, g.P);
            }
        }

        private static void Hide(Gate g)
        {
            g.Hidden = true;
            Function.Call(Hash.SET_ENTITY_COLLISION, g.P, false, false);
            Function.Call(Hash.SET_ENTITY_VISIBLE, g.P, false, false);
        }

        private void CloseGates(WorkshopDefinition w)
        {
            List<Gate> list; if (!gates.TryGetValue(w.Id, out list)) return;
            foreach (var g in list)
                try
                {
                    if (g.DoorId != 0) { Function.Call(Hash.DOOR_SYSTEM_SET_HOLD_OPEN, g.DoorId, false); Function.Call(Hash.REMOVE_DOOR_FROM_SYSTEM, g.DoorId); }
                    if (g.Hidden && g.P != null && g.P.Exists()) { Function.Call(Hash.SET_ENTITY_COLLISION, g.P, true, true); Function.Call(Hash.SET_ENTITY_VISIBLE, g.P, true, false); }
                }
                catch { }
            gates.Remove(w.Id);
        }

        public void CloseAllGates() { foreach (var w in All) CloseGates(w); }

        private static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; ab.Z = 0; var ap = p - a; ap.Z = 0;
            float l2 = ab.LengthSquared(); if (l2 < 0.01f) return ap.Length();
            float t = Math.Max(0f, Math.Min(1f, Vector3.Dot(ap, ab) / l2));
            return (ap - ab * t).Length();
        }
    }
}
