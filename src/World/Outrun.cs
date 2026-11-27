using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.Racing;
using MidnightClub.UI;
using MidnightClub.Vehicles;

namespace MidnightClub.Explore
{
    /// <summary>Outrun battles (Tokyo Xtreme Racer style): street racers cruise the city at night (purple blips).
    /// Get right behind one and flash your headlights (H / D-pad) to challenge them. Both drivers have a spirit
    /// bar; whoever is behind drains it, faster the bigger the gap. Empty their bar (or pull 350 m ahead) to win.</summary>
    public sealed class Outrun
    {
        private sealed class Racer { public Vehicle Car; public Ped Driver; public Blip Blip; public string Name; public VehicleInfo Info; }
        private readonly SaveManager saves; private readonly TransactionManager money; private readonly VehicleRegistry registry; private readonly VehicleManager vehicles;
        private readonly List<Racer> cruisers = new List<Racer>();
        private Racer foe; private float mySp, foeSp; private int battleStart, countdownEnd, nextSpawn, endShownUntil;
        private readonly Random rnd = new Random();
        public static bool Battling { get; private set; }

        public Outrun(SaveManager saves, TransactionManager money, VehicleRegistry registry, VehicleManager vehicles)
        {
            this.saves = saves; this.money = money; this.registry = registry; this.vehicles = vehicles;
            EventBus.Subscribe<MidnightTickEvent>(e => { try { Tick(); } catch (Exception ex) { Log.Error("Outrun", ex); EndBattle(false, true); } });
            EventBus.Subscribe<ModeChangedEvent>(e => { EndBattle(false, true); foreach (var c in cruisers.ToList()) Remove(c, true); });
        }

        private void Tick()
        {
            var car = vehicles.Current; var ped = Game.Player.Character; int now = Game.GameTime;
            if (car == null || !car.Exists() || !ped.IsInVehicle(car)) return;
            if (Battling) { Battle(car, now); return; }
            bool busy = RaceManager.IsRacing || Police.PursuitManager.AnyActive || Gameplay.PhotoMode.Running;
            // population
            foreach (var c in cruisers.ToList())
                if (!c.Car.Exists() || c.Driver == null || !c.Driver.Exists() || c.Car.IsDead || c.Car.Position.DistanceTo(car.Position) > 750f) Remove(c, !c.Car.Exists() || !c.Car.IsOnScreen);
            if (!busy && now > nextSpawn && cruisers.Count < 2) { nextSpawn = now + 45000; Spawn(car); }
            if (busy) return;
            // challenge: right behind a cruiser + flash the headlights
            foreach (var c in cruisers)
            {
                var rel = c.Car.Position - car.Position; float d = rel.Length();
                if (d > 28f || d < 4f) continue;
                if (Vector3.Dot(Vector3.Normalize(rel), car.ForwardVector) < 0.8f || Vector3.Dot(c.Car.ForwardVector, car.ForwardVector) < 0.7f) continue;
                Screen("FLASH YOUR LIGHTS TO CHALLENGE  " + c.Name.ToUpperInvariant());
                if (UI.Input.JustPressed(Control.VehicleHeadlight)) { Start(c); return; }
            }
        }

        private static void Screen(string s)
        {
            float w = Ds.Width(s, 0.3f, Ds.Display) + Ds.Px(0.04f), x = 0.5f - w / 2f, y = 0.78f;
            Ds.Rect(x, y, w, 0.038f, Ds.A(Ds.Bg, 0.85f)); Ds.Rect(x, y, Ds.Px(0.004f), 0.038f, Ds.Violet);
            Ds.T(s, 0.5f, y + 0.008f, 0.3f, Ds.Text, Ds.Display, GTA.UI.Alignment.Center);
        }

        private void Spawn(Vehicle near)
        {
            int node = RoadGraph.RandomNodeAround(near.Position, 260f, 420f, rnd); if (node < 0) return;
            var p = RoadGraph.Pos(node);
            if (Function.Call<bool>(Hash.IS_SPHERE_VISIBLE, p.X, p.Y, p.Z, 5f)) return;
            float h; var sp = World.GetNextPositionOnStreetWithHeading(p, out h, false); if (sp == Vector3.Zero) return;
            var mine = vehicles.CurrentRecord != null ? registry.Get(vehicles.CurrentRecord.ModelHash) : null;
            string[] tiers = { "D", "C", "B", "A", "S", "X" }; int ti = mine != null ? Array.IndexOf(tiers, mine.Tier) : 1;
            var pool = registry.All.Where(v => v.Enabled && (v.Class == VehicleClass.Sports || v.Class == VehicleClass.Super || v.Class == VehicleClass.Muscle || v.Tuner) && Math.Abs(Array.IndexOf(tiers, v.Tier) - ti) <= 1).ToList();
            if (pool.Count == 0) return;
            var info = pool[rnd.Next(pool.Count)];
            var m = new Model(info.Hash); if (!m.Request(3000)) return;
            var c = World.CreateVehicle(m, sp, h); m.MarkAsNoLongerNeeded(); if (c == null) return;
            c.IsPersistent = true; c.Mods.InstallModKit(); Function.Call(Hash.SET_VEHICLE_MOD, c, 11, 2, false); Function.Call(Hash.TOGGLE_VEHICLE_MOD, c, 18, true);
            c.Mods.PrimaryColor = (VehicleColor)rnd.Next(0, 160); Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, c, 0, true); Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, c, 1, true); Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, c, 2, true); Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, c, 3, true);
            Function.Call(Hash.SET_VEHICLE_NEON_COLOUR, c, 168, 85, 247);
            var pm = new Model("a_m_y_stwhi_01"); pm.Request(3000);
            var d = c.CreatePedOnSeat(VehicleSeat.Driver, pm); pm.MarkAsNoLongerNeeded(); if (d == null) { c.Delete(); return; }
            d.IsPersistent = true; Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, d, true); Function.Call(Hash.SET_DRIVER_ABILITY, d, 1f);
            Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, d, c, 22f, 786603);
            var b = BlipRegistry.Own(c.AddBlip()); b.Sprite = BlipSprite.PersonalVehicleCar; b.Color = BlipColor.Purple; b.Scale = 0.7f; b.Name = "Street racer"; b.IsShortRange = true;
            cruisers.Add(new Racer { Car = c, Driver = d, Blip = b, Info = info, Name = RaceManager.DriverNames[rnd.Next(RaceManager.DriverNames.Length)] });
        }

        private void Remove(Racer c, bool delete)
        {
            cruisers.Remove(c);
            try { if (c.Blip != null && c.Blip.Exists()) c.Blip.Delete(); } catch { }
            try
            {
                if (delete) { if (c.Driver != null && c.Driver.Exists()) c.Driver.Delete(); if (c.Car != null && c.Car.Exists()) c.Car.Delete(); }
                else { if (c.Driver != null && c.Driver.Exists()) c.Driver.MarkAsNoLongerNeeded(); if (c.Car != null && c.Car.Exists()) c.Car.MarkAsNoLongerNeeded(); }
            }
            catch { }
        }

        // ---------------------------------------------------------------- battle
        private void Start(Racer c)
        {
            foe = c; Battling = true; mySp = foeSp = 100f; battleStart = Game.GameTime; countdownEnd = Game.GameTime + 2400;
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "3_2_1", "HUD_MINI_GAME_SOUNDSET", true);
            if (c.Blip != null && c.Blip.Exists()) { c.Blip.Color = BlipColor.Red; c.Blip.IsShortRange = false; }
            Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, c.Driver, 0.9f);
        }

        private void Battle(Vehicle car, int now)
        {
            if (foe == null || !foe.Car.Exists() || foe.Driver == null || !foe.Driver.Exists()) { EndBattle(true, false); return; }
            if (now < countdownEnd)
            {
                int sec = (int)Math.Ceiling((countdownEnd - now) / 800.0);
                Ds.T(sec.ToString(), 0.5f, 0.22f, 2.4f, Ds.Text, Ds.Display, GTA.UI.Alignment.Center);
                DrawBars(); return;
            }
            if (now - countdownEnd < 400)
            {
                Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, foe.Driver, foe.Car, 70f, (int)VehicleDrivingFlags.DrivingModeAvoidVehiclesReckless);
                Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, foe.Car, 1.2f);
            }
            var rel = foe.Car.Position - car.Position; float gap = rel.Length();
            bool foeAhead = Vector3.Dot(rel, car.ForwardVector) > 0;
            float drain = Ds.Dt * (3f + gap * 0.18f);
            if (foeAhead) mySp -= drain; else foeSp -= drain;
            if (gap > 350f) { if (foeAhead) mySp = 0; else foeSp = 0; }
            if (mySp <= 0) { EndBattle(false, false); return; }
            if (foeSp <= 0) { EndBattle(true, false); return; }
            DrawBars();
        }

        private void DrawBars()
        {
            float w = Ds.Px(0.24f), y = 0.05f, gap = Ds.Px(0.03f);
            float lx = 0.5f - gap / 2f - w, rx = 0.5f + gap / 2f;
            Ds.Rect(lx - Ds.Px(0.006f), y - 0.008f, w * 2 + gap + Ds.Px(0.012f), 0.07f, Ds.A(Ds.Bg, 0.82f));
            Ds.T("YOU", lx, y - 0.004f, 0.28f, Ds.Text, Ds.Display);
            Ds.T(foe.Name.ToUpperInvariant(), rx + w, y - 0.004f, 0.28f, Ds.Text, Ds.Display, GTA.UI.Alignment.Right);
            Ds.Rect(lx, y + 0.03f, w, 0.016f, Ds.Stroke); Ds.Rect(lx + w * (1 - mySp / 100f), y + 0.03f, w * mySp / 100f, 0.016f, mySp < 30 ? Ds.Red : Ds.Cyan);
            Ds.Rect(rx, y + 0.03f, w, 0.016f, Ds.Stroke); Ds.Rect(rx, y + 0.03f, w * foeSp / 100f, 0.016f, foeSp < 30 ? Ds.Red : Ds.Pink);
            Ds.T("VS", 0.5f, y + 0.018f, 0.32f, Ds.Violet, Ds.Display, GTA.UI.Alignment.Center);
        }

        private void EndBattle(bool won, bool silent)
        {
            if (!Battling) return;
            Battling = false; var f = foe; foe = null;
            if (f != null)
            {
                try { if (f.Driver.Exists() && f.Car.Exists()) { Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, f.Driver, f.Car, 20f, 786603); Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, f.Car, 1f); } } catch { }
                Remove(f, false);
            }
            if (silent) return;
            string[] tiers = { "D", "C", "B", "A", "S", "X" };
            int ti = f != null && f.Info != null ? Math.Max(0, Array.IndexOf(tiers, f.Info.Tier)) : 1;
            if (won)
            {
                long cash = 2500 + 1500L * ti, rep = 80 + 40L * ti;
                money.Earn(cash, rep, "outrun", "Outran " + (f != null ? f.Name : "a street racer"), null);
                saves.Data.StreetRacesWon++; saves.IsDirty = true;
                if (Career.Progression.Instance != null) Career.Progression.Instance.Bump("outrun", 1);
                Shard.Mid("OUTRUN WON", (f != null ? f.Name.ToUpperInvariant() + "  ·  " : "") + "+$" + cash.ToString("N0") + "  ·  +" + rep + " REP", Shard.Purple, 3500);
                if (Career.Progression.Instance != null) Career.Progression.Instance.Award(300 + 100 * ti, "Outrun battle");
            }
            else Shard.Mid("OUTRUN LOST", f != null ? f.Name + " left you in the dust." : "They got away.", Shard.Red, 3000);
        }
    }
}
