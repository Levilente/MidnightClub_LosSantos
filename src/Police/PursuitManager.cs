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
using MidnightClub.Vehicles;
using Font = GTA.UI.Font;

namespace MidnightClub.Police
{
    public sealed class PursuitEndedEvent { public bool Escaped; public int Level; public PursuitEndedEvent(bool e, int l) { Escaped = e; Level = l; } }

    /// <summary>Vehicle-only police: units chase, box in and ram, never shoot; the player's car can't explode.
    /// Pursuits start when patrol cars see you speeding (or you hit one), escalate over 4 levels (more units,
    /// interceptors, helicopter), end by escaping (break line of sight long enough, cover hides you from the
    /// helicopter) or getting boxed in (busted: fine). Each car carries heat that makes police notice it sooner.</summary>
    public sealed class PursuitManager
    {
        private sealed class Unit { public Vehicle Car; public Ped Driver; public bool Heli; public Blip Blip; }
        private readonly SaveManager saves;
        private readonly TransactionManager money;
        private readonly VehicleManager vehicles;
        private readonly Config cfg;
        private readonly List<Unit> units = new List<Unit>();
        public bool Active { get; private set; }
        public static bool AnyActive { get { return Game.GameTime - lastHud < 500; } }
        private static int lastHud = -10000;
        public int Level { get; private set; }
        private int startedAt, lastScan, lastSpawn, cooldownUntil, bustSince = -1;
        private float evade; private bool seen;
        private static readonly string[] CarModels = { "police", "police2", "police3", "sheriff", "sheriff2" };
        private static readonly string[] FastModels = { "police3", "police2", "polgauntlet", "police5", "polcoquette4" };
        private static readonly HashSet<int> PoliceHashes = new HashSet<int>();

        public PursuitManager(SaveManager saves, TransactionManager money, VehicleManager vehicles, Config cfg)
        {
            this.saves = saves; this.money = money; this.vehicles = vehicles; this.cfg = cfg;
            foreach (var m in CarModels.Concat(FastModels).Concat(new[] { "police4", "policeb", "polmav", "pranger", "policet" })) PoliceHashes.Add(new Model(m).Hash);
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To != GameMode.MIDNIGHT_CLUB) End(false, silent: true); });
            EventBus.Subscribe<WorldSim.NewDayEvent>(e => { foreach (var v in saves.Data.Vehicles) v.Heat = Math.Max(0f, v.Heat - 0.5f); });
        }

        private OwnedVehicle Rec { get { return vehicles.CurrentRecord; } }
        private float Heat { get { var r = Rec; return r == null ? 0 : r.Heat; } }

        /// <summary>Changing plate or paint makes the car harder to recognise.</summary>
        public static void CoolDownIdentity(OwnedVehicle r, float amount) { if (r != null) r.Heat = Math.Max(0f, r.Heat - amount); }

        private void Tick()
        {
            if (!cfg.PoliceEnabled) return;
            var car = vehicles.Current; var ped = Game.Player.Character;
            if (car == null || !car.Exists()) return;
            int now = Game.GameTime;
            if (!Active) { if (now > cooldownUntil && now - lastScan > 500) { lastScan = now; Scan(car, ped); } return; }
            Update(car, ped, now);
        }

        // ------------------------------------------------------------------ noticing
        private void Scan(Vehicle car, Ped ped)
        {
            float kmh = car.Speed * 3.6f;
            float trigger = cfg.PoliceSpeedKmh - Heat * 6f - (Racing.RaceManager.IsRacing ? 25f : 0f);
            foreach (var v in World.GetNearbyVehicles(car.Position, 70f))
            {
                if (v == car || !PoliceHashes.Contains(v.Model.Hash) || units.Any(u => u.Car == v)) continue;
                var drv = v.Driver;
                if (drv == null || !drv.Exists() || drv.IsPlayer) continue;
                bool hit = Function.Call<bool>(Hash.IS_ENTITY_TOUCHING_ENTITY, car, v);
                bool sees = Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, v, car, 17);
                if (hit || (sees && kmh > trigger))
                {
                    Start(hit ? "You hit a police car!" : "Speeding past the police at " + (int)kmh + " km/h!", v, drv);
                    return;
                }
            }
        }

        private void Start(string why, Vehicle firstCar, Ped firstDriver)
        {
            Active = true; Level = Heat >= 3.5f ? 2 : 1; startedAt = Game.GameTime; evade = 0; lastSpawn = 0; bustSince = -1;
            saves.Data.Pursuits++; saves.IsDirty = true;
            if (Rec != null) Rec.Heat = Math.Min(5f, Rec.Heat + 0.5f);
            if (firstCar != null) Recruit(firstCar, firstDriver, false);
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Out_Of_Area", "DLC_Lowrider_Relay_Race_Sounds", true);
            Notification.PostTicker("~r~POLICE PURSUIT~s~: " + why, true, false);
            Log.Info("Pursuit started (" + why + "), heat " + Heat.ToString("0.0"));
        }

        // ------------------------------------------------------------------ during
        private void Update(Vehicle car, Ped ped, int now)
        {
            float diff = cfg.PoliceDifficulty;
            int elapsed = now - startedAt;
            int target = elapsed > 150000 ? 4 : elapsed > 90000 ? 3 : elapsed > 40000 ? 2 : 1;
            if (Heat >= 3.5f) target = Math.Min(4, target + 1);
            if (target > Level) { Level = target; Notification.PostTicker("~r~Pursuit level " + Level + "~s~" + (Level == 3 ? ": helicopter inbound." : Level == 4 ? ": citywide pursuit." : "."), true, false); }

            units.RemoveAll(u => !u.Car.Exists() || u.Driver == null || !u.Driver.Exists() || u.Car.IsDead || u.Car.Position.DistanceTo(car.Position) > 900f && Dismiss(u));
            int wantCars = (int)Math.Round(new[] { 0, 2, 4, 6, 8 }[Level] * diff);
            if (units.Count(u => !u.Heli) < wantCars && now - lastSpawn > 3500) { lastSpawn = now; SpawnCar(car, Level >= 3); }
            if (Level >= 3 && !units.Any(u => u.Heli) && now - lastSpawn > 2000) { lastSpawn = now; SpawnHeli(car); }

            // line of sight / evade
            bool covered = UnderCover(car);
            seen = false; float nearest = 9999f;
            foreach (var u in units)
            {
                float d = u.Car.Position.DistanceTo(car.Position); nearest = Math.Min(nearest, d);
                if (u.Heli && covered) continue;
                if (d < (u.Heli ? 250f : 160f) && Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, u.Car, car, 17)) seen = true;
            }
            float need = (8f + Level * 4f) * diff;
            if (seen) evade = Math.Max(0f, evade - Game.LastFrameTime * 2f);
            else evade += Game.LastFrameTime;
            if (evade >= need) { End(true); return; }

            // busted: boxed in and stopped with a unit right next to you
            if (car.Speed < 1.5f && nearest < 8f && !Racing.RaceManager.IsRacing || car.Speed < 1.0f && nearest < 6f)
            {
                if (bustSince < 0) bustSince = now;
                else if (now - bustSince > 5000) { End(false); return; }
            }
            else bustSince = -1;

            DrawHud(need, nearest, covered);
        }

        /// <summary>Stand a unit down: blip off, sirens off, the officer forgets you and drives away; deleted if unseen.</summary>
        private bool Dismiss(Unit u)
        {
            try
            {
                if (u.Blip != null && u.Blip.Exists()) u.Blip.Delete();
                if (u.Car != null && u.Car.Exists())
                {
                    Function.Call(Hash.SET_VEHICLE_SIREN, u.Car, false);
                    Function.Call(Hash.SET_VEHICLE_HAS_MUTED_SIRENS, u.Car, true);
                }
                if (u.Driver != null && u.Driver.Exists())
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, u.Driver);
                    Function.Call(Hash.SET_PED_KEEP_TASK, u.Driver, false);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, u.Driver, true);
                    if (u.Car != null && u.Car.Exists() && u.Driver.IsInVehicle(u.Car))
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, u.Driver, u.Car, 14f, 786603);
                }
                bool seen = u.Car != null && u.Car.Exists() && (u.Car.IsOnScreen || u.Car.Position.DistanceTo(Game.Player.Character.Position) < 60f);
                if (!seen) { if (u.Driver != null && u.Driver.Exists()) u.Driver.Delete(); if (u.Car != null && u.Car.Exists()) u.Car.Delete(); }
                else { u.Driver.MarkAsNoLongerNeeded(); u.Car.MarkAsNoLongerNeeded(); }
            }
            catch { }
            return true;
        }

        /// <summary>Something solid above the car (tunnel, bridge, parking structure) hides it from the helicopter.</summary>
        private static bool UnderCover(Vehicle car)
        {
            var p = car.Position + new Vector3(0, 0, 2f);
            var hit = World.Raycast(p, p + new Vector3(0, 0, 25f), IntersectFlags.Map, car);
            return hit.DidHit;
        }

        private void SpawnCar(Vehicle target, bool fast)
        {
            try
            {
                var back = target.Position - target.ForwardVector * 180f;
                float h; var pos = World.GetNextPositionOnStreetWithHeading(back, out h, false);
                if (pos == Vector3.Zero || pos.DistanceTo(target.Position) < 90f || Screen.IsSphereVisible(pos, 4f)) return;
                var names = fast ? FastModels : CarModels;
                string name = names[new Random().Next(names.Length)];
                var m = new Model(name);
                if (!m.IsValid || !m.IsInCdImage) m = new Model("police3");
                if (!m.Request(3000)) return;
                var car = World.CreateVehicle(m, pos, h); m.MarkAsNoLongerNeeded();
                if (car == null) return;
                var pm = new Model("s_m_y_cop_01"); pm.Request(3000);
                var drv = car.CreatePedOnSeat(VehicleSeat.Driver, pm); pm.MarkAsNoLongerNeeded();
                if (drv == null) { car.Delete(); return; }
                Recruit(car, drv, false);
            }
            catch (Exception ex) { Log.Error("Spawn police car", ex); }
        }

        private void SpawnHeli(Vehicle target)
        {
            try
            {
                var m = new Model("polmav"); if (!m.Request(3000)) return;
                var pos = target.Position - target.ForwardVector * 250f + new Vector3(0, 0, 90f);
                var heli = World.CreateVehicle(m, pos, target.Heading); m.MarkAsNoLongerNeeded();
                if (heli == null) return;
                var pm = new Model("s_m_y_pilot_01"); pm.Request(3000);
                var pilot = heli.CreatePedOnSeat(VehicleSeat.Driver, pm); pm.MarkAsNoLongerNeeded();
                if (pilot == null) { heli.Delete(); return; }
                heli.IsEngineRunning = true;
                Function.Call(Hash.SET_HELI_BLADES_FULL_SPEED, heli);
                Recruit(heli, pilot, true);
            }
            catch (Exception ex) { Log.Error("Spawn helicopter", ex); }
        }

        private void Recruit(Vehicle car, Ped drv, bool heli)
        {
            car.IsPersistent = true; drv.IsPersistent = true;
            drv.Weapons.RemoveAll();                                         // no firearms, ever
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, drv, 2, false);   // no drive-bys
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, drv, 1, false);   // don't use vehicle weapons
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, drv, true);
            Function.Call(Hash.SET_PED_KEEP_TASK, drv, true);
            Function.Call(Hash.SET_DRIVER_ABILITY, drv, 1f);
            Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, drv, Math.Min(1f, 0.5f + 0.15f * Level));
            drv.CanBeDraggedOutOfVehicle = false;
            var player = Game.Player.Character;
            if (heli)
            {
                Function.Call(Hash.TASK_HELI_CHASE, drv, player, 0f, 0f, 45f);
                Function.Call(Hash.SET_VEHICLE_SEARCHLIGHT, car, true, true);
            }
            else
            {
                Function.Call(Hash.SET_VEHICLE_SIREN, car, true);
                Function.Call(Hash.TASK_VEHICLE_CHASE, drv, player);
                Function.Call(Hash.SET_TASK_VEHICLE_CHASE_IDEAL_PURSUIT_DISTANCE, drv, 0f);
                Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, car, 1f + 0.08f * Level);
            }
            var b = UI.BlipRegistry.Own(car.AddBlip()); b.Sprite = heli ? BlipSprite.PoliceHelicopterAnimated : BlipSprite.PoliceCar; b.Color = BlipColor.Red; b.Scale = 0.75f;
            units.Add(new Unit { Car = car, Driver = drv, Heli = heli, Blip = b });
        }

        // ------------------------------------------------------------------ end
        private void End(bool escaped, bool silent = false)
        {
            if (!Active) return;
            int lvl = Level;
            Active = false; Level = 0;
            foreach (var u in units) Dismiss(u);
            try { Game.Player.WantedLevel = 0; Function.Call(Hash.CLEAR_PLAYER_WANTED_LEVEL, Game.Player); } catch { }
            units.Clear();
            cooldownUntil = Game.GameTime + 60000;
            if (silent) return;
            var rec = Rec;
            if (escaped)
            {
                saves.Data.PursuitEscapes++;
                if (rec != null) rec.Stats.PoliceEscapes++;
                money.Earn(0, 40 * lvl, "police", "Escaped the police (level " + lvl + ")", "escape:" + saves.Data.Pursuits);
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Mission_Pass_Notify", "DLC_HEISTS_GENERAL_FRONTEND_SOUNDS", true);
                Notification.PostTicker("~g~You lost the police.~s~ +" + (40 * lvl) + " REP", true, false);
            }
            else
            {
                saves.Data.Busts++;
                if (rec != null) rec.Heat = Math.Min(5f, rec.Heat + 0.5f);
                long fine = money.ChargeUpTo(750L * lvl, "police", "Busted: fine (level " + lvl + ")");
                Notification.PostTicker("~r~BUSTED.~s~ Fine: $" + fine.ToString("N0"), true, false);
            }
            saves.IsDirty = true;
            EventBus.Publish(new PursuitEndedEvent(escaped, lvl));
            Log.Info("Pursuit ended: " + (escaped ? "escaped" : "busted") + " at level " + lvl);
        }

        private void DrawHud(float need, float nearest, bool covered)
        {
            lastHud = Game.GameTime;
            float w = 0.24f, x = 0.5f - w / 2, y = 0.02f;
            UI.Ds.Skew(x, y, w, 0.078f, UI.Ds.SlantFor(0.078f) * 0.5f, UI.Ds.A(UI.Ds.Surface, 0.94f));
            UI.Ds.Tag("PURSUIT", x + UI.Ds.Px(0.022f), y + 0.012f, (Game.GameTime / 250) % 2 == 0 ? UI.Ds.Red : UI.Ds.Blue, UI.Ds.Text, 0.26f);
            UI.Ds.T("LEVEL " + Level, x + UI.Ds.Px(0.02f), y + 0.032f, 0.5f, UI.Ds.Text, UI.Ds.Display);
            UI.Ds.T(units.Count + " UNITS" + (covered ? "  ·  UNDER COVER" : ""), x + w - UI.Ds.Px(0.014f), y + 0.034f, 0.25f, covered ? UI.Ds.Cyan : UI.Ds.Muted, UI.Ds.Body, Alignment.Right);
            float fr = Math.Min(1f, evade / need), bx = x + UI.Ds.Px(0.018f), bw = w - UI.Ds.Px(0.036f);
            UI.Ds.Skew(bx, y + 0.064f, bw, 0.005f, UI.Ds.Px(0.0012f), UI.Ds.Stroke);
            if (fr > 0.01f) UI.Ds.Skew(bx, y + 0.064f, bw * fr, 0.005f, UI.Ds.Px(0.0012f), seen ? UI.Ds.Red : UI.Ds.Lime);
        }

    }
}
