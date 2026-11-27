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
using MidnightClub.Vehicles;
using Font = GTA.UI.Font;

namespace MidnightClub.Gameplay
{
    /// <summary>Free-roam street style: near misses, drafting (with a real slipstream boost), airtime and
    /// drifting build a combo; banked combos pay REP and charge the special-ability meter.</summary>
    public sealed class StyleSystem
    {
        public static StyleSystem Instance { get; private set; }
        private readonly SaveManager saves;
        private readonly TransactionManager money;
        private readonly VehicleManager vehicles;
        private readonly Dictionary<int, float> closest = new Dictionary<int, float>();
        private readonly HashSet<int> touched = new HashSet<int>();
        private int nextScan, lastEvent, airStart = -1, popupUntil;
        private float draftTime, driftChain;
        public float Meter { get; set; }            // 0..1 special ability charge
        public bool Slipstreaming { get; private set; }
        private long combo; private float mult = 1f; private string popup = "";

        public StyleSystem(SaveManager saves, TransactionManager money, VehicleManager vehicles)
        {
            this.saves = saves; this.money = money; this.vehicles = vehicles; Instance = this;
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
        }

        private void Add(string name, int pts)
        {
            int now = Game.GameTime;
            if (now - lastEvent < 4000) mult = Math.Min(5f, mult + 0.5f); else { mult = 1f; combo = 0; }
            lastEvent = now;
            long gained = (long)(pts * mult);
            combo += gained;
            Meter = Math.Min(1f, Meter + pts / 2500f);
            popup = name + "  +" + gained + (mult > 1f ? "  x" + mult.ToString("0.0") : "");
            popupUntil = now + 1800;
            EventBus.Publish(new StyleEvent(name, pts));
        }

        private void Bank()
        {
            if (combo <= 0) return;
            long rep = Math.Min(250, combo / 100);
            saves.Data.StylePointsTotal += combo;
            if (rep > 0) money.Earn(0, rep, "style", "Street style combo " + combo.ToString("N0"), null);
            combo = 0; mult = 1f;
        }

        private int wheelieStart = -1; private bool stoppie; private Vector3 takeoff;
        private void Tick()
        {
            var car = vehicles.Current;
            if (car == null || !car.Exists()) return;
            int now = Game.GameTime;
            float kmh = car.Speed * 3.6f;
            if (kmh > saves.Data.BestSpeedKmh && kmh < 600f) saves.Data.BestSpeedKmh = kmh;
            if (combo > 0 && now - lastEvent > 4000) Bank();

            // airtime
            if (car.IsInAir && kmh > 30f) { if (airStart < 0) { airStart = now; takeoff = car.Position; } }
            else if (airStart >= 0)
            {
                int ms = now - airStart; airStart = -1;
                if (ms > 700) Add(ms > 2000 ? "BIG AIR" : "AIR", ms / 4);
                float dist = new Vector2(car.Position.X - takeoff.X, car.Position.Y - takeoff.Y).Length();
                if (ms > 900 && dist > 25f && dist < 600f && !car.IsUpsideDown)
                {
                    if (Career.Progression.Instance != null) Career.Progression.Instance.Bump("jump", (long)dist, true);
                    if (dist > saves.Data.BestJumpM)
                    {
                        bool first = saves.Data.BestJumpM <= 0; saves.Data.BestJumpM = dist; saves.IsDirty = true;
                        UI.Shard.Mid(first ? "JUMP" : "NEW JUMP RECORD", ((int)dist) + " M  ·  " + (ms / 1000f).ToString("0.0") + " S AIRTIME", UI.Shard.Orange, 2500);
                        if (!first) money.Earn(500, 50, "style", "Jump record " + (int)dist + " m", null);
                    }
                    else UI.Shard.Mid("JUMP", ((int)dist) + " M  ·  BEST " + (int)saves.Data.BestJumpM + " M", UI.Shard.Orange, 1500);
                }
            }

            // bikes: wheelies and stoppies
            if (car.Model.IsBike && kmh > 25f)
            {
                float pitch = car.Rotation.X; bool wheels = Function.Call<bool>(Hash.IS_VEHICLE_ON_ALL_WHEELS, car);
                if (!wheels && !car.IsInAir && Math.Abs(pitch) > 9f) { if (wheelieStart < 0) { wheelieStart = now; stoppie = pitch < 0; } }
                else if (wheelieStart >= 0) { int ms = now - wheelieStart; wheelieStart = -1; if (ms > 800) Add(stoppie ? "STOPPIE" : ms > 3000 ? "LONG WHEELIE" : "WHEELIE", ms / 5); }
            }
            else wheelieStart = -1;

            // drift
            var v = car.Velocity; v.Z = 0; var f = car.ForwardVector; f.Z = 0;
            float angle = car.Speed > 11f ? (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, Vector3.Dot(Vector3.Normalize(v), Vector3.Normalize(f))))) * 180.0 / Math.PI) : 0f;
            if (angle > 18f && angle < 80f && !car.IsInAir) driftChain += Game.LastFrameTime * angle * car.Speed * 0.05f;
            else if (driftChain > 0) { if (driftChain > 40) Add("DRIFT", (int)driftChain); driftChain = 0; }

            if (now < nextScan) { DrawPopup(); return; }
            nextScan = now + 100;

            // near misses + drafting
            bool drafting = false;
            var nearby = World.GetNearbyVehicles(car.Position, 14f);
            var seen = new HashSet<int>();
            foreach (var o in nearby)
            {
                if (o == car || !o.Exists()) continue;
                int h = o.Handle; seen.Add(h);
                var off = car.GetPositionOffset(o.Position);
                float d = car.Position.DistanceTo(o.Position);
                if (Function.Call<bool>(Hash.IS_ENTITY_TOUCHING_ENTITY, car, o)) touched.Add(h);
                float rel = (car.Velocity - o.Velocity).Length();
                if (d < 6.5f && rel > 12f) { float c; closest[h] = closest.TryGetValue(h, out c) ? Math.Min(c, d) : d; }
                // slipstream: directly ahead, same direction, close
                if (off.Y > 3f && off.Y < 14f && Math.Abs(off.X) < 1.6f && kmh > 70f && Vector3.Dot(o.ForwardVector, car.ForwardVector) > 0.9f) drafting = true;
            }
            foreach (var h in new List<int>(closest.Keys))
            {
                if (seen.Contains(h) && closest[h] > 0) { var e = Entity.FromHandle(h); if (e != null && e.Position.DistanceTo(car.Position) < 7f) continue; }
                if (closest[h] < 3.2f && !touched.Contains(h) && kmh > 60f) Add("NEAR MISS", (int)(100 + (3.2f - closest[h]) * 120));
                closest.Remove(h); touched.Remove(h);
            }
            if (drafting) { draftTime += 0.1f; if (draftTime > 1f && !Slipstreaming) { Slipstreaming = true; Add("DRAFTING", 120); } }
            else { if (Slipstreaming && draftTime > 1f) Add("SLINGSHOT", (int)(draftTime * 40)); draftTime = 0; Slipstreaming = false; }
            DrawPopup();
        }

        private void DrawPopup()
        {
            if (Game.GameTime > popupUntil) return;
            float age = (1800 - (popupUntil - Game.GameTime)) / 1000f, a = age > 1.4f ? (1.8f - age) / 0.4f : 1f;
            float pop = UI.Ds.EaseBack(Math.Min(1f, age / 0.25f)), sc = 0.62f * (0.8f + 0.2f * pop);
            UI.Ds.T(popup, 0.5f, 0.775f - (1f - pop) * 0.01f, sc, UI.Ds.A(UI.Ds.Text, a), UI.Ds.Display, Alignment.Center);
            float uw = UI.Ds.Width(popup, sc, UI.Ds.Display) * pop;
            UI.Ds.Rect(0.5f - uw / 2, 0.822f, uw, 0.003f, UI.Ds.A(UI.Ds.Cyan, a));
            if (combo > 0)
            {
                string cs = "COMBO  " + combo.ToString("N0");
                float cw = UI.Ds.Width(cs, 0.3f, UI.Ds.Display) + UI.Ds.Px(0.014f) + UI.Ds.SlantFor(0.03f);
                UI.Ds.Tag(cs, 0.5f - cw / 2, 0.832f, UI.Ds.Violet, UI.Ds.Text, 0.3f, a);
            }
        }
    }
}
