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
    /// <summary>Test Drive Unlimited-style car deliveries: a client needs their car moved across the city on a
    /// deadline. Pick it up, drive it there, get paid by condition and time left. Three new jobs every day.</summary>
    public sealed class Deliveries
    {
        private sealed class Job { public string Client, CarId, CarName; public Vector3 From, To; public float Km; public int Seconds; public long Pay; }
        private static readonly string[] Clients = { "Rockford Hills collector", "Vinewood producer", "Del Perro lawyer", "Paleto Bay rancher", "Legion Square banker", "Chumash surfer", "Mirror Park architect", "Richman heiress" };
        public static Deliveries Instance; public static bool Active { get { return Instance != null && Instance.state > 0; } }
        private readonly SaveManager saves; private readonly TransactionManager money; private readonly VehicleRegistry registry; private readonly VehicleManager vehicles; private readonly MenuStack stack;
        private List<Job> offers = new List<Job>(); private int offersDay = -1;
        private Job job; private int state; // 1 go to pickup, 2 driving
        private Vehicle jobCar; private Blip blip; private int deadline;

        public Deliveries(SaveManager saves, TransactionManager money, VehicleRegistry registry, VehicleManager vehicles, MenuStack stack)
        {
            this.saves = saves; this.money = money; this.registry = registry; this.vehicles = vehicles; this.stack = stack; Instance = this;
            Core.GameModeManager.VehicleCheckSuspended = () => state == 2;
            EventBus.Subscribe<MidnightTickEvent>(e => { try { Tick(); } catch (Exception ex) { Log.Error("Delivery", ex); End(false, "Something went wrong."); } });
            EventBus.Subscribe<ModeChangedEvent>(e => { if (state > 0) End(false, null); });
        }

        private void Roll()
        {
            int day = saves.Data != null ? saves.Data.CalendarDay : DateTime.Now.DayOfYear;
            if (day == offersDay && offers.Count > 0) return;
            offersDay = day; offers.Clear();
            var rnd = new Random(day * 131 + 7); var me = Game.Player.Character.Position;
            var pool = registry.All.Where(v => v.Enabled && (v.Tier == "A" || v.Tier == "S" || v.Tier == "X" || v.Tier == "B") && v.Class != VehicleClass.Motorcycles && v.Class != VehicleClass.Commercial).ToList();
            for (int i = 0; i < 3 && pool.Count > 0; i++)
            {
                int a = RoadGraph.RandomNodeAround(me, 300f, 900f, rnd), b = RoadGraph.RandomNodeAround(me, 2500f, 5500f, rnd);
                if (a < 0 || b < 0) continue;
                var v = pool[rnd.Next(pool.Count)]; var pa = RoadGraph.Pos(a); var pb = RoadGraph.Pos(b);
                float km = pa.DistanceTo2D(pb) / 1000f * 1.3f;
                offers.Add(new Job { Client = Clients[rnd.Next(Clients.Length)], CarId = v.Id, CarName = v.Name, From = pa, To = pb, Km = km, Seconds = (int)(km / 62f * 3600f) + 30, Pay = (long)(1500 + km * 900 + v.BasePrice * 0.004) });
            }
        }

        public Menu Menu()
        {
            Roll();
            var m = new Menu("Car deliveries", "Clients pay for speed and a clean car");
            foreach (var jj in offers)
            {
                var j = jj;
                m.Add(new MenuItem(j.CarName, () => { if (state > 0 || RaceManager.IsRacing) return; stack.Close(); Accept(j); })
                {
                    Value = () => "$" + j.Pay.ToString("N0"),
                    Description = () => j.Client + ". About " + j.Km.ToString("0.0") + " km, " + (j.Seconds / 60) + ":" + (j.Seconds % 60).ToString("00") + " on the clock. Damage costs you; time left pays a bonus."
                });
            }
            if (offers.Count == 0) m.Add(new MenuItem("No jobs right now") { Enabled = false });
            return m;
        }

        private void Accept(Job j)
        {
            job = j; state = 1; offers.Remove(j);
            blip = BlipRegistry.Own(World.CreateBlip(j.From)); blip.Sprite = BlipSprite.PersonalVehicleCar; blip.Color = BlipColor.Green; blip.ShowRoute = true; blip.Name = "Delivery pickup";
            Toast.Post("~g~Delivery~s~: pick up the " + j.CarName + " for the " + j.Client + ".");
        }

        private void Tick()
        {
            if (state == 0) return;
            var ped = Game.Player.Character; int now = Game.GameTime;
            if (state == 1)
            {
                var c = vehicles.Current; if (c == null || !c.Exists()) return;
                Function.Call(Hash.DRAW_MARKER, 1, job.From.X, job.From.Y, job.From.Z - 1f, 0f, 0f, 0f, 0f, 0f, 0f, 6f, 6f, 1.5f, 163, 230, 53, 110, false, false, 2, false, 0, 0, false);
                if (c.Position.DistanceTo(job.From) < 9f && c.Speed < 2f) Swap(c);
                return;
            }
            // driving the client's car
            if (jobCar == null || !jobCar.Exists() || jobCar.IsDead) { End(false, "The client's car was wrecked."); return; }
            int left = deadline - now;
            if (left <= 0) { End(false, "Too late. The client found someone else."); return; }
            if (!ped.IsInVehicle(jobCar) && now % 1000 < 20) ped.SetIntoVehicle(jobCar, VehicleSeat.Driver);
            Function.Call(Hash.DRAW_MARKER, 1, job.To.X, job.To.Y, job.To.Z - 1f, 0f, 0f, 0f, 0f, 0f, 0f, 7f, 7f, 1.5f, 168, 85, 247, 110, false, false, 2, false, 0, 0, false);
            float cond = Condition();
            float w = Ds.Px(0.28f), x = 0.5f - w / 2f, y = 0.035f;
            Ds.Rect(x, y, w, 0.075f, Ds.A(Ds.Bg, 0.85f)); Ds.Rect(x, y, w, 0.004f, Ds.Lime);
            Ds.T("DELIVERY  ·  " + job.CarName.ToUpperInvariant(), x + Ds.Px(0.012f), y + 0.012f, 0.26f, Ds.Lime, Ds.Display);
            Ds.T((left / 60000) + ":" + (left / 1000 % 60).ToString("00"), x + w - Ds.Px(0.012f), y + 0.006f, 0.68f, left < 30000 ? Ds.Red : Ds.Text, Ds.Display, GTA.UI.Alignment.Right);
            Ds.T("CONDITION " + (int)(cond * 100) + "%  ·  " + ((int)(jobCar.Position.DistanceTo(job.To) / 10) * 10) + " M", x + Ds.Px(0.012f), y + 0.042f, 0.26f, cond < 0.7f ? Ds.Red : Ds.Muted, Ds.Display);
            if (jobCar.Position.DistanceTo(job.To) < 10f && jobCar.Speed < 2f)
            {
                long pay = (long)(job.Pay * (0.25f + 0.75f * cond) * (1f + 0.3f * left / (job.Seconds * 1000f)));
                money.Earn(pay, 60, "delivery", "Delivered " + job.CarName, null);
                if (Career.Progression.Instance != null) Career.Progression.Instance.Award(400, "Car delivery");
                End(true, "$" + pay.ToString("N0") + "  ·  condition " + (int)(cond * 100) + "%");
            }
        }

        private float Condition() { return Math.Max(0f, Math.Min(1f, (jobCar.BodyHealth / 1000f) * 0.6f + (jobCar.EngineHealth / 1000f) * 0.4f)); }

        private void Swap(Vehicle own)
        {
            var info = registry.Get(job.CarId); if (info == null) { End(false, null); return; }
            GTA.UI.Screen.FadeOut(350); Script.Wait(400);
            var m = new Model(info.Hash); m.Request(4000);
            var h = own.Heading; var pos = own.Position;
            jobCar = World.CreateVehicle(m, pos + own.RightVector * 3.5f, h); m.MarkAsNoLongerNeeded();
            if (jobCar == null) { GTA.UI.Screen.FadeIn(300); End(false, null); return; }
            jobCar.IsPersistent = true; jobCar.Mods.PrimaryColor = (VehicleColor)new Random().Next(0, 160);
            Function.Call(Hash.SET_ENTITY_VISIBLE, own, false, false); Function.Call(Hash.SET_ENTITY_COLLISION, own, false, false); Function.Call(Hash.FREEZE_ENTITY_POSITION, own, true);
            state = 2; Game.Player.Character.SetIntoVehicle(jobCar, VehicleSeat.Driver);
            jobCar.IsEngineRunning = true;
            deadline = Game.GameTime + job.Seconds * 1000;
            try { if (blip != null && blip.Exists()) blip.Delete(); } catch { }
            blip = BlipRegistry.Own(World.CreateBlip(job.To)); blip.Sprite = BlipSprite.Standard; blip.Color = BlipColor.Purple; blip.ShowRoute = true; blip.Name = "Delivery drop-off";
            Script.Wait(200); GTA.UI.Screen.FadeIn(400);
            Shard.Mid("DELIVERY", job.CarName.ToUpperInvariant() + "  ·  " + (job.Seconds / 60) + " MIN", Shard.Green, 2500);
        }

        private void End(bool ok, string text)
        {
            int was = state; state = 0;
            try { if (blip != null && blip.Exists()) blip.Delete(); } catch { } blip = null;
            if (was == 2)
            {
                var own = vehicles.Current; var ped = Game.Player.Character;
                try
                {
                    GTA.UI.Screen.FadeOut(350); Script.Wait(400);
                    var at = jobCar != null && jobCar.Exists() ? jobCar.Position : ped.Position; float h = jobCar != null && jobCar.Exists() ? jobCar.Heading : ped.Heading;
                    if (own != null && own.Exists())
                    {
                        Function.Call(Hash.FREEZE_ENTITY_POSITION, own, false); Function.Call(Hash.SET_ENTITY_COLLISION, own, true, true); Function.Call(Hash.SET_ENTITY_VISIBLE, own, true, false);
                        own.Position = at + new Vector3(0, 0, 0.5f); own.Heading = h; own.PlaceOnGround();
                    }
                    if (jobCar != null && jobCar.Exists()) jobCar.Delete();
                    if (own != null && own.Exists()) ped.SetIntoVehicle(own, VehicleSeat.Driver);
                    Script.Wait(200);
                }
                catch (Exception ex) { Log.Error("Delivery end", ex); }
                GTA.UI.Screen.FadeIn(400);
            }
            jobCar = null; job = null;
            if (text != null) Shard.Mid(ok ? "DELIVERED" : "DELIVERY FAILED", text, ok ? Shard.Green : Shard.Red, 3500);
        }
    }
}
