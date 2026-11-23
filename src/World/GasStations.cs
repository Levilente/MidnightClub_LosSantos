using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.Persistence;
using MidnightClub.Vehicles;

namespace MidnightClub.WorldSim
{
    /// <summary>Midnight Club classic: drive through a gas station and the car is repaired (and cleaned).
    /// Uses the game's own fuel-station blips when present, plus the standard list of San Andreas stations.</summary>
    public sealed class GasStations
    {
        private static readonly float[][] Known =
        {
            new[] { 49.4f, 2778.8f, 58.0f }, new[] { 263.9f, 2606.5f, 44.9f }, new[] { 1039.9f, 2671.1f, 39.6f }, new[] { 1207.3f, 2660.2f, 37.9f },
            new[] { 2539.7f, 2594.2f, 37.9f }, new[] { 2679.9f, 3264.0f, 55.2f }, new[] { 2005.0f, 3773.9f, 32.4f }, new[] { 1687.2f, 4929.4f, 42.1f },
            new[] { 1701.3f, 6416.0f, 32.8f }, new[] { 179.9f, 6602.8f, 32.0f }, new[] { -94.5f, 6419.6f, 31.5f }, new[] { -2554.9f, 2334.4f, 33.1f },
            new[] { -1800.4f, 803.7f, 138.7f }, new[] { -1437.6f, -276.7f, 46.2f }, new[] { -2096.2f, -320.3f, 13.2f }, new[] { -724.6f, -935.2f, 19.2f },
            new[] { -526.0f, -1211.0f, 18.2f }, new[] { -70.2f, -1761.8f, 29.5f }, new[] { 265.6f, -1261.3f, 29.3f }, new[] { 819.6f, -1028.8f, 26.4f },
            new[] { 1208.9f, -1402.6f, 35.2f }, new[] { 1181.4f, -330.8f, 69.3f }, new[] { 620.8f, 269.1f, 103.1f }, new[] { 2581.3f, 362.0f, 108.5f },
            new[] { 176.6f, -1562.0f, 29.3f }, new[] { -319.3f, -1471.7f, 30.5f }, new[] { 1785.4f, 3330.4f, 41.4f },
        };
        private readonly List<Vector3> stations = new List<Vector3>();
        private readonly Dictionary<int, int> cooldown = new Dictionary<int, int>();
        private readonly VehicleManager vehicles; private readonly SaveManager saves;
        private int nextCheck;

        public GasStations(VehicleManager vehicles, SaveManager saves)
        {
            this.vehicles = vehicles; this.saves = saves;
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To == GameMode.MIDNIGHT_CLUB) Collect(); });
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
        }

        private void Collect()
        {
            stations.Clear();
            foreach (var k in Known) stations.Add(new Vector3(k[0], k[1], k[2]));
            try
            {
                foreach (var b in World.GetAllBlips(BlipSprite.JerryCan))
                {
                    var p = b.Position; bool dup = false;
                    foreach (var s in stations) if (s.DistanceTo2D(p) < 40f) { dup = true; break; }
                    if (!dup) stations.Add(p);
                }
            }
            catch { }
            Log.Info("Gas stations (drive-through repair): " + stations.Count);
        }

        private void Tick()
        {
            if (Game.GameTime < nextCheck) return;
            nextCheck = Game.GameTime + 250;
            var car = vehicles.Current; var ped = Game.Player.Character;
            if (car == null || !car.Exists() || !ped.IsInVehicle(car) || Racing.RaceManager.IsRacing) return;
            var pos = car.Position;
            for (int i = 0; i < stations.Count; i++)
            {
                if (pos.DistanceTo2D(stations[i]) > 14f || Math.Abs(pos.Z - stations[i].Z) > 6f) continue;
                int until; if (cooldown.TryGetValue(i, out until) && Game.GameTime < until) return;
                bool damaged = car.EngineHealth < 990f || car.BodyHealth < 990f || car.DirtLevel > 3f;
                for (int t = 0; t < 8 && !damaged; t++) if (Function.Call<bool>(Hash.IS_VEHICLE_TYRE_BURST, car, t, false)) damaged = true;
                cooldown[i] = Game.GameTime + 45000;
                if (!damaged) return;
                Function.Call(Hash.SET_VEHICLE_FIXED, car); Function.Call(Hash.SET_VEHICLE_DEFORMATION_FIXED, car);
                car.EngineHealth = 1000f; car.BodyHealth = 1000f; car.DirtLevel = 0f;
                var rec = vehicles.CurrentRecord; if (rec != null) { rec.Stats.Repairs++; saves.IsDirty = true; }
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "PURCHASE", "HUD_LIQUOR_STORE_SOUNDSET", true);
                UI.Shard.Mid("REPAIRED", "Gas station pit stop", UI.Shard.Green, 2200);
                return;
            }
        }
    }
}
