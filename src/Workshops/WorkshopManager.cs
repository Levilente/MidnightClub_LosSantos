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
            new WorkshopDefinition("lsc_burton", "Los Santos Customs Burton", -337.38f, -136.92f, 39.01f),
            new WorkshopDefinition("lsc_lsia", "Los Santos Customs LSIA", -1155.53f, -2007.18f, 13.18f),
            new WorkshopDefinition("lsc_lamesa", "Los Santos Customs La Mesa", 731.81f, -1088.82f, 22.17f),
            new WorkshopDefinition("lsc_harmony", "Los Santos Customs Route 68", 1175.04f, 2640.21f, 37.75f),
            new WorkshopDefinition("beekers", "Beeker's Garage", 110.99f, 6626.39f, 31.89f),
            new WorkshopDefinition("bennys_ws", "Benny's Original Motor Works", -211.5f, -1324.0f, 30.9f, true),
        };

        public WorkshopManager(SaveManager saves, TransactionManager money, VehicleRegistry registry, VehicleManager vehicles, MenuStack stack)
        {
            this.saves = saves; this.money = money; this.registry = registry; this.vehicles = vehicles; this.stack = stack;
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To == GameMode.MIDNIGHT_CLUB) PlaceBlips(); else { ClearBlips(); CloseDoors(); } });
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
                var b = World.CreateBlip(w.Position);
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
                if (d > 60f) continue;
                World.DrawMarker(MarkerType.Cylinder, w.Position - new Vector3(0, 0, 0.95f), Vector3.Zero, Vector3.Zero, new Vector3(6f, 6f, 0.8f),
                    Color.FromArgb(140, 255, 170, 40), false, false, false, null, null, false);
                if (d < 4.5f) nearby = w;
            }
            if (nearby == null || stack.IsOpen) return;
            var car = vehicles.Current;
            if (car == null || !car.Exists() || car.Speed > 3f) return;
            Screen.ShowHelpTextThisFrame("~INPUT_CONTEXT~ Enter " + nearby.Name);
            if (Game.IsControlJustPressed(Control.Context))
            {
                IsBusy = true;
                try { new WorkshopScreen(nearby, vehicles, registry, saves, money).Run(); }
                catch (Exception ex) { Log.Error("Workshop failed", ex); }
                finally { IsBusy = false; }
            }
        }
    }
}
