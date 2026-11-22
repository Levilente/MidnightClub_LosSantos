using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.Persistence;

namespace MidnightClub.Vehicles
{
    /// <summary>Spawns the player's owned vehicle, keeps its persistent record in sync and
    /// guarantees the car can never be permanently destroyed.</summary>
    public sealed class VehicleManager
    {
        public Vehicle Current { get; private set; }
        public OwnedVehicle CurrentRecord { get; private set; }
        private Vector3 lastPos; private bool hasLastPos;

        public static OwnedVehicle CreateRecord(VehicleInfo info, long price)
        {
            var r = CreateRecord(info.Id, price);
            r.ModelHash = info.Hash;
            return r;
        }

        public static OwnedVehicle CreateRecord(string model, long price)
        {
            return new OwnedVehicle
            {
                Id = Guid.NewGuid().ToString("N"),
                Model = model,
                ModelHash = new Model(model).Hash,
                Plate = "MC" + new Random().Next(100000, 999999),
                PurchasePrice = price,
                PurchasedAtUtc = DateTime.UtcNow.ToString("o"),
                PrimaryColor = -1, SecondaryColor = -1, PearlColor = -1, WheelColor = -1, WheelType = -1
            };
        }

        public static bool IsSpawnable(string modelName)
        {
            var m = new Model(modelName);
            return m.IsValid && m.IsInCdImage && m.IsVehicle;
        }

        public Vehicle Spawn(OwnedVehicle rec, Vector3 pos, float heading)
        {
            Despawn(false);
            var model = rec.ModelHash != 0 ? new Model(rec.ModelHash) : new Model(rec.Model);
            if (!model.IsValid || !model.IsInCdImage || !model.IsVehicle) throw new Exception("Vehicle model not available in this game build: " + rec.Model);
            if (!model.Request(5000)) throw new Exception("Vehicle model failed to load: " + rec.Model);
            var v = World.CreateVehicle(model, pos, heading);
            model.MarkAsNoLongerNeeded();
            if (v == null || !v.Exists()) throw new Exception("World.CreateVehicle returned nothing for " + rec.Model);

            v.IsPersistent = true;
            v.PlaceOnGround();
            Apply(v, rec);
            Protect(v);
            Current = v; CurrentRecord = rec; hasLastPos = false;
            rec.Stats.LastUsedUtc = DateTime.UtcNow.ToString("o");
            return v;
        }

        private static void Apply(Vehicle v, OwnedVehicle r)
        {
            v.Mods.InstallModKit();
            if (r.WheelType >= 0) v.Mods.WheelType = (VehicleWheelType)r.WheelType;
            foreach (var m in r.Mods) Function.Call(Hash.SET_VEHICLE_MOD, v, m.Type, m.Index, false);
            if (r.PrimaryColor >= 0) v.Mods.PrimaryColor = (VehicleColor)r.PrimaryColor;
            if (r.SecondaryColor >= 0) v.Mods.SecondaryColor = (VehicleColor)r.SecondaryColor;
            if (r.PearlColor >= 0) v.Mods.PearlescentColor = (VehicleColor)r.PearlColor;
            if (r.WheelColor >= 0) v.Mods.RimColor = (VehicleColor)r.WheelColor;
            if (!string.IsNullOrEmpty(r.Plate)) v.Mods.LicensePlate = r.Plate;
            v.EngineHealth = Math.Max(r.EngineHealth, 300f);
            v.BodyHealth = Math.Max(r.BodyHealth, 300f);
        }

        /// <summary>Records colors/mods/health of the live car into the save record.</summary>
        public void Capture()
        {
            var v = Current; var r = CurrentRecord;
            if (v == null || !v.Exists() || r == null) return;
            r.PrimaryColor = (int)v.Mods.PrimaryColor; r.SecondaryColor = (int)v.Mods.SecondaryColor;
            r.PearlColor = (int)v.Mods.PearlescentColor; r.WheelColor = (int)v.Mods.RimColor;
            r.WheelType = (int)v.Mods.WheelType;
            r.Mods.Clear();
            for (int type = 0; type <= 49; type++)
            {
                int idx = Function.Call<int>(Hash.GET_VEHICLE_MOD, v, type);
                if (idx >= 0) r.Mods.Add(new ModEntry(type, idx));
            }
            r.Plate = v.Mods.LicensePlate;
            r.EngineHealth = v.EngineHealth; r.BodyHealth = v.BodyHealth;
        }

        private static void Protect(Vehicle v)
        {
            Function.Call(Hash.SET_VEHICLE_EXPLODES_ON_HIGH_EXPLOSION_DAMAGE, v, false);
            Function.Call(Hash.SET_DISABLE_VEHICLE_PETROL_TANK_DAMAGE, v, true);
            Function.Call(Hash.SET_DISABLE_VEHICLE_PETROL_TANK_FIRES, v, true);
            v.IsExplosionProof = true;
            v.IsFireProof = true;
            v.IsBulletProof = true;
        }

        /// <summary>Per-tick bookkeeping: distance and top speed. Cheap (a few property reads).</summary>
        public void UpdateStats()
        {
            var v = Current; var r = CurrentRecord;
            if (v == null || r == null || !v.Exists()) return;
            var p = v.Position;
            if (hasLastPos)
            {
                float d = p.DistanceTo(lastPos);
                if (d < 50f) r.Stats.DistanceKm += d / 1000.0;  // ignore teleports
            }
            lastPos = p; hasLastPos = true;
            float kmh = v.Speed * 3.6f;
            if (kmh > r.Stats.TopSpeedKmh && kmh < 600f) r.Stats.TopSpeedKmh = kmh;
        }

        public bool NeedsRecovery(float engineThreshold)
        {
            var v = Current;
            if (v == null || !v.Exists() || v.IsDead) return true;
            if (v.EngineHealth < engineThreshold) return true;
            if (v.IsInWater && v.Speed < 2f && v.SubmersionLevel > 0.6f) return true;
            return false;
        }

        public void Despawn(bool capture)
        {
            if (capture) Capture();
            if (Current != null && Current.Exists()) Current.Delete();
            Current = null;
        }
    }
}
