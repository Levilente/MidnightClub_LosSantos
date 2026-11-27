using System;
using System.Collections.Generic;
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
    /// <summary>GTA VI-style street takeovers: at night a crowd shuts down an intersection (orange blip). Roll in,
    /// and for 60 seconds every donut and drift inside the circle builds hype; the crowd films and cheers. Cops show
    /// up at the end, so leave fast.</summary>
    public sealed class Takeovers
    {
        private readonly SaveManager saves; private readonly TransactionManager money; private readonly VehicleManager vehicles;
        private Vector3 spot; private Blip blip; private bool has; private int nextRoll;
        private readonly List<Ped> crowd = new List<Ped>(); private readonly List<Vehicle> parked = new List<Vehicle>();
        private int state, start; private float hype, score; // state 0 idle, 1 running, 2 leave
        private readonly Random rnd = new Random();
        private const float R = 13f;

        public Takeovers(SaveManager saves, TransactionManager money, VehicleManager vehicles)
        {
            this.saves = saves; this.money = money; this.vehicles = vehicles;
            EventBus.Subscribe<MidnightTickEvent>(e => { try { Tick(); } catch (Exception ex) { Log.Error("Takeover", ex); Clear(); } });
            EventBus.Subscribe<ModeChangedEvent>(e => Clear());
        }

        private static bool Night { get { int h = Function.Call<int>(Hash.GET_CLOCK_HOURS); return h >= 21 || h < 5; } }

        private void Tick()
        {
            var car = vehicles.Current; int now = Game.GameTime;
            if (car == null || !car.Exists()) return;
            if (!has)
            {
                if (!Night || now < nextRoll || RaceManager.IsRacing) return;
                nextRoll = now + 90000;
                for (int k = 0; k < 30; k++)
                {
                    int n = RoadGraph.RandomNodeAround(car.Position, 700f, 1600f, rnd); if (n < 0) continue;
                    var f = RoadGraph.Flags(n);
                    if ((f & RoadGraph.NodeFlags.Junction) == 0 || (f & RoadGraph.NodeFlags.Freeway) != 0) continue;
                    spot = RoadGraph.Pos(n); has = true; break;
                }
                if (!has) return;
                blip = BlipRegistry.Own(World.CreateBlip(spot)); blip.Sprite = BlipSprite.CarWash; blip.Color = BlipColor.Orange; blip.Name = "Street takeover"; blip.IsShortRange = false;
                Toast.Post("~o~Street takeover~s~ going down nearby. Orange marker on the map.");
                return;
            }
            if (!Night && state == 0) { Clear(); return; }
            float d = car.Position.DistanceTo(spot);
            if (state == 0)
            {
                if (d < 140f && crowd.Count == 0) SpawnCrowd();
                if (d > 400f && crowd.Count > 0) DespawnCrowd();
                Function.Call(Hash.DRAW_MARKER, 25, spot.X, spot.Y, spot.Z + 0.05f, 0f, 0f, 0f, 0f, 0f, 0f, R * 2f, R * 2f, 1f, 251, 146, 60, 90, false, false, 2, false, 0, 0, false);
                if (d < R) { state = 1; start = now; hype = 0; score = 0; Shard.Mid("STREET TAKEOVER", "Donuts and drifts inside the circle. 60 seconds.", Shard.Orange, 2500); }
                return;
            }
            int left = 60000 - (now - start);
            if (state == 1)
            {
                Function.Call(Hash.DRAW_MARKER, 25, spot.X, spot.Y, spot.Z + 0.05f, 0f, 0f, 0f, 0f, 0f, 0f, R * 2f, R * 2f, 1f, 251, 146, 60, 140, false, false, 2, false, 0, 0, false);
                var v = car.Velocity; v.Z = 0; var fw = car.ForwardVector; fw.Z = 0;
                float ang = car.Speed > 3f ? (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, Vector3.Dot(Vector3.Normalize(v), Vector3.Normalize(fw))))) * 180.0 / Math.PI) : 0f;
                float spin = Math.Abs(Function.Call<Vector3>(Hash.GET_ENTITY_ROTATION_VELOCITY, car).Z);
                bool inside = d < R * 1.4f;
                if (inside && (ang > 15f || spin > 1.2f)) { float gain = Ds.Dt * (ang * 0.6f + spin * 25f) * (1f + hype / 100f); score += gain * 10f; hype = Math.Min(100f, hype + Ds.Dt * 9f); }
                else hype = Math.Max(0f, hype - Ds.Dt * (inside ? 4f : 12f));
                float w = Ds.Px(0.3f), x = 0.5f - w / 2f, y = 0.04f;
                Ds.Rect(x, y, w, 0.08f, Ds.A(Ds.Bg, 0.85f)); Ds.Rect(x, y, w, 0.004f, Ds.Amber);
                Ds.T("STREET TAKEOVER", x + Ds.Px(0.012f), y + 0.012f, 0.26f, Ds.Amber, Ds.Display);
                Ds.T(((long)score).ToString("N0"), x + w - Ds.Px(0.012f), y + 0.006f, 0.68f, Ds.Text, Ds.Display, GTA.UI.Alignment.Right);
                Ds.T("HYPE", x + Ds.Px(0.012f), y + 0.045f, 0.22f, Ds.Muted, Ds.Display);
                Ds.Rect(x + Ds.Px(0.06f), y + 0.052f, w * 0.5f, 0.008f, Ds.Stroke); Ds.Rect(x + Ds.Px(0.06f), y + 0.052f, w * 0.5f * hype / 100f, 0.008f, hype > 70 ? Ds.Red : Ds.Amber);
                Ds.T((Math.Max(0, left) / 1000) + "s", x + w - Ds.Px(0.012f), y + 0.045f, 0.26f, left < 10000 ? Ds.Red : Ds.Muted, Ds.Display, GTA.UI.Alignment.Right);
                if (hype > 60 && now % 900 < 20) foreach (var p in crowd.Take(6)) if (p.Exists()) Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, p, "GENERIC_CHEER", "SPEECH_PARAMS_FORCE_SHOUTED", 0);
                if (left <= 0)
                {
                    state = 2; start = now;
                    long cash = (long)Math.Min(25000, score * 0.4f), rep = (long)Math.Min(800, score / 40f);
                    money.Earn(cash, rep, "takeover", "Street takeover " + ((long)score).ToString("N0"), null);
                    if (Career.Progression.Instance != null) { Career.Progression.Instance.Award(300 + (long)(score / 50), "Street takeover"); Career.Progression.Instance.Bump("style", (long)score); }
                    if (Career.Story.Instance != null) Career.Story.Instance.Track("takeover", (long)score, true);
                    if (Career.Disciplines.Instance != null) Career.Disciplines.Instance.Add(Racing.Discipline.Drift, 150 + (long)(score / 60), "takeover");
                    Shard.Big("TAKEOVER  " + ((long)score).ToString("N0"), "+$" + cash.ToString("N0") + "  ·  +" + rep + " REP  ·  cops inbound, scatter!", Shard.Orange, 4500);
                    foreach (var p in crowd) if (p.Exists()) { Function.Call(Hash.CLEAR_PED_TASKS, p); p.Task.FleeFrom(spot); }
                }
                return;
            }
            if (state == 2 && now - start > 25000) Clear();
        }

        private void SpawnCrowd()
        {
            string[] models = { "a_m_y_hipster_01", "a_f_y_hipster_02", "a_m_y_stwhi_02", "a_m_y_latino_01", "a_f_y_genhot_01", "a_m_y_beachvesp_01", "a_m_y_soucent_02", "a_f_y_soucent_01" };
            string[] scen = { "WORLD_HUMAN_MOBILE_FILM_SHOCKING", "WORLD_HUMAN_CHEERING", "WORLD_HUMAN_PARTYING", "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_SMOKING" };
            for (int i = 0; i < 16; i++)
            {
                double a = i / 16.0 * Math.PI * 2 + rnd.NextDouble() * 0.2;
                var p = spot + new Vector3((float)Math.Cos(a) * (R + 4f + (float)rnd.NextDouble() * 3f), (float)Math.Sin(a) * (R + 4f + (float)rnd.NextDouble() * 3f), 0.5f);
                var m = new Model(models[rnd.Next(models.Length)]); if (!m.Request(1500)) continue;
                var ped = World.CreatePed(m, p, (float)(a * 180 / Math.PI) + 90f); m.MarkAsNoLongerNeeded();
                if (ped == null) continue;
                ped.IsPersistent = true; Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped, true);
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_COORD, ped, spot.X, spot.Y, spot.Z, 1000);
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped, scen[rnd.Next(scen.Length)], 0, true);
                crowd.Add(ped);
            }
            string[] cars = { "sultan3", "futo", "elegy", "dominator7", "zr350", "remus" };
            for (int i = 0; i < 4; i++)
            {
                double a = i / 4.0 * Math.PI * 2 + 0.4;
                var p = spot + new Vector3((float)Math.Cos(a) * (R + 9f), (float)Math.Sin(a) * (R + 9f), 0.5f);
                var m = new Model(cars[rnd.Next(cars.Length)]); if (!m.IsValid || !m.Request(2000)) continue;
                var c = World.CreateVehicle(m, p, (float)(a * 180 / Math.PI)); m.MarkAsNoLongerNeeded(); if (c == null) continue;
                c.IsPersistent = true; c.PlaceOnGround(); c.AreLightsOn = true;
                for (int n = 0; n < 4; n++) Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, c, n, true);
                Function.Call(Hash.SET_VEHICLE_NEON_COLOUR, c, 251, 146, 60);
                parked.Add(c);
            }
        }

        private void DespawnCrowd()
        {
            foreach (var p in crowd) try { if (p.Exists()) p.Delete(); } catch { }
            foreach (var c in parked) try { if (c.Exists()) c.Delete(); } catch { }
            crowd.Clear(); parked.Clear();
        }

        private void Clear()
        {
            foreach (var p in crowd) try { if (p.Exists()) { if (p.IsOnScreen) p.MarkAsNoLongerNeeded(); else p.Delete(); } } catch { }
            foreach (var c in parked) try { if (c.Exists()) { if (c.IsOnScreen) c.MarkAsNoLongerNeeded(); else c.Delete(); } } catch { }
            crowd.Clear(); parked.Clear();
            try { if (blip != null && blip.Exists()) blip.Delete(); } catch { }
            blip = null; has = false; state = 0; nextRoll = Game.GameTime + 240000;
        }
    }
}
