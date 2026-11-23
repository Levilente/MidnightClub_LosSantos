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
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To == GameMode.MIDNIGHT_CLUB) PlaceBlips(); else { ClearBlips(); CloseDoors(); } });
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
                var ent = Entrance(w);
                float d = Math.Min(pos.DistanceTo(ent), pos.DistanceTo(w.Position));
                if (d > 90f) continue;
                if (!Explore.Discovery.Known("ws:" + w.Id)) continue;
                bool inRange = d < 6f;
                UI.WorldMarker.Draw(ent, 3f, Color.FromArgb(255, 251, 191, 36), MarkerType.Boxes, w.Name, inRange ? "Press to enter" : (w.Specialty ?? "Mod shop") + " mod shop", pos.DistanceTo(ent), inRange);
                if (inRange) nearby = w;
            }
            if (nearby == null || stack.IsOpen) return;
            var car = vehicles.Current;
            if (car == null || !car.Exists() || car.Speed > 3f) return;
            Screen.ShowHelpTextThisFrame("~INPUT_CONTEXT~ Enter " + nearby.Name);
            if (Game.IsControlJustPressed(Control.Context)) Enter(nearby, car);
        }

        // ------------------------------------------------------------------ entering
        // Story Mode's shop doors stay shut without the vanilla shop scripts, so the shop is entered from the
        // driveway marker: the car is moved inside behind a fade and brought back out afterwards.
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

        private void Enter(WorkshopDefinition w, Vehicle car)
        {
            IsBusy = true;
            var outPos = car.Position; float outHeading = car.Heading;
            var ent = Entrance(w);
            try
            {
                if (car.Position.DistanceTo(w.Position) > 4f)
                {
                    Screen.FadeOut(400); Script.Wait(450);
                    var dir = w.Position - ent; dir.Z = 0;
                    float inHeading = dir.Length() > 0.5f ? (float)(Math.Atan2(-dir.X, dir.Y) * 180.0 / Math.PI) + 180f : car.Heading;   // nose out
                    Function.Call(Hash.SET_FOCUS_POS_AND_VEL, w.Position.X, w.Position.Y, w.Position.Z, 0f, 0f, 0f);
                    car.Position = w.Position; car.Heading = inHeading; car.Velocity = Vector3.Zero;
                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, car, 5f);
                    int t0 = Game.GameTime;
                    while (Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, car) == 0 && Game.GameTime - t0 < 1500) Script.Wait(50);
                    Function.Call(Hash.CLEAR_FOCUS);
                }
                new WorkshopScreen(w, vehicles, registry, saves, money).Run();
            }
            catch (Exception ex) { Log.Error("Workshop failed", ex); }
            finally
            {
                try
                {
                    var c = vehicles.Current;
                    if (c != null && c.Exists() && c.Position.DistanceTo(w.Position) < 8f && ent.DistanceTo(w.Position) > 4f)
                    {
                        Screen.FadeOut(250); Script.Wait(300);
                        var dir = ent - w.Position; dir.Z = 0;
                        c.Position = ent; c.Heading = dir.Length() > 0.5f ? (float)(Math.Atan2(-dir.X, dir.Y) * 180.0 / Math.PI) : outHeading;
                        c.Velocity = Vector3.Zero; Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, c, 5f);
                        Function.Call(Hash.SET_GAMEPLAY_CAM_RELATIVE_HEADING, 0f);
                        Script.Wait(200); Screen.FadeIn(400);
                    }
                }
                catch (Exception ex) { Log.Error("Workshop exit", ex); }
                IsBusy = false;
            }
        }
    }
}
