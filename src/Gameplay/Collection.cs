using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.UI;
using MidnightClub.Vehicles;

namespace MidnightClub.Gameplay
{
    /// <summary>Car collection (Gran Turismo / Forza collection rewards): milestones for distinct models owned, brand
    /// loyalty bonuses for three cars of one make, and a reward for owning every class from D to X.</summary>
    public sealed class Collection
    {
        private static readonly int[] Milestones = { 5, 10, 20, 35, 50, 75 };
        private readonly SaveManager saves; private readonly TransactionManager money; private int next;
        public static Collection Instance;

        public Collection(SaveManager saves, TransactionManager money)
        {
            this.saves = saves; this.money = money; Instance = this;
            EventBus.Subscribe<MidnightTickEvent>(e => { if (Game.GameTime > next) { next = Game.GameTime + 8000; try { Check(); } catch (Exception ex) { Log.Error("Collection", ex); } } });
        }

        private VehicleInfo Info(OwnedVehicle r) { var reg = VehicleRegistry.Instance; return reg == null ? null : r.ModelHash != 0 ? reg.Get(r.ModelHash) : reg.Get(r.Model); }
        public int Unique { get { return saves.Data == null ? 0 : saves.Data.Vehicles.Select(v => v.Model).Distinct(StringComparer.OrdinalIgnoreCase).Count(); } }
        private bool Got(string key) { return saves.Data.CollectionRewards.Contains(key); }
        private void Mark(string key) { saves.Data.CollectionRewards.Add(key); saves.IsDirty = true; }

        private void Check()
        {
            var d = saves.Data; if (d == null) return;
            int u = Unique;
            foreach (int m in Milestones)
                if (u >= m && !Got("m" + m))
                {
                    Mark("m" + m); long cash = m * 1500L;
                    money.Earn(cash, m * 20, "collection", "Collection: " + m + " cars", "col:m" + m);
                    if (m >= 35) d.SuperWheelspins++; else d.Wheelspins++;
                    Shard.Mid("COLLECTION  ·  " + m + " CARS", "+$" + cash.ToString("N0") + "  ·  " + (m >= 35 ? "SUPER WHEELSPIN" : "WHEELSPIN"), Shard.Gold, 4000);
                }
            foreach (var g in d.Vehicles.Select(v => Info(v)).Where(i => i != null && !string.IsNullOrEmpty(i.Make)).GroupBy(i => i.Make))
                if (g.Select(i => i.Id).Distinct().Count() >= 3 && !Got("make:" + g.Key))
                {
                    Mark("make:" + g.Key);
                    money.Earn(5000, 100, "collection", "Brand loyalty: " + g.Key, "col:make:" + g.Key);
                    Toast.Post("~y~Brand loyalty~s~: three " + g.Key + " cars in your collection. +$5,000");
                }
            var classes = d.Vehicles.Select(v => PerformanceIndex.ClassOf(PerformanceIndex.Of(v))).Distinct().Count();
            if (classes >= 6 && !Got("allclasses")) { Mark("allclasses"); money.Earn(50000, 500, "collection", "Every class D to X", "col:classes"); Shard.Big("FULL HOUSE", "A car in every class from D to X.  +$50,000", Shard.Gold, 5000); }
        }

        public Menu Menu()
        {
            var m = new Menu("Collection", "Distinct cars, brands and classes");
            m.OnOpen = () =>
            {
                m.Items.Clear(); var d = saves.Data;
                int u = Unique; int nextM = Milestones.FirstOrDefault(x => x > u);
                m.Add(new MenuItem("Distinct cars") { Enabled = false, Value = () => u + (nextM > 0 ? "  ·  next reward at " + nextM : "  ·  all milestones") });
                var classes = d.Vehicles.Select(v => PerformanceIndex.ClassOf(PerformanceIndex.Of(v))).Distinct().ToList();
                m.Add(new MenuItem("Classes owned") { Enabled = false, Value = () => string.Join(" ", PerformanceIndex.Classes.Select(c => classes.Contains(c) ? c : "·").ToArray()) });
                foreach (var g in d.Vehicles.Select(v => Info(v)).Where(i => i != null && !string.IsNullOrEmpty(i.Make)).GroupBy(i => i.Make).OrderByDescending(g => g.Count()))
                {
                    var gg = g; int have = gg.Select(i => i.Id).Distinct().Count();
                    int total = VehicleRegistry.Instance != null ? VehicleRegistry.Instance.All.Count(v => v.Enabled && v.Make == gg.Key) : have;
                    m.Add(new MenuItem(gg.Key) { Enabled = false, Value = () => have + " / " + total + (Got("make:" + gg.Key) ? "  ·  LOYALTY" : "") });
                }
            };
            return m;
        }
    }

    /// <summary>Auction house: three rare lots a day. Bidding is live against collectors: they raise until their
    /// limit, every bid puts a few seconds back on the clock. Win it, pay it, it's delivered to your garage.</summary>
    public sealed class Auction
    {
        private sealed class Lot { public VehicleInfo Car; public long Value; }
        private readonly SaveManager saves; private readonly TransactionManager money; private readonly MenuStack stack;
        public static Auction Instance; private List<Lot> lots = new List<Lot>(); private int day = -1;
        private static readonly string[] Bidders = { "Rockford collector", "Vinewood producer", "Dubai syndicate", "Paleto Bay rancher", "Anonymous phone bidder" };

        public Auction(SaveManager saves, TransactionManager money, MenuStack stack) { this.saves = saves; this.money = money; this.stack = stack; Instance = this; }

        private void Roll()
        {
            int dd = saves.Data.CalendarDay; if (dd == day && lots.Count > 0) return; day = dd; lots.Clear();
            var reg = VehicleRegistry.Instance; if (reg == null) return;
            var rnd = new Random(dd * 977 + 3);
            var pool = reg.All.Where(v => v.Enabled && (v.Tier == "S" || v.Tier == "X" || v.Class == VehicleClass.SportsClassics) && v.Class != VehicleClass.Motorcycles).ToList();
            for (int i = 0; i < 3 && pool.Count > 0; i++) { var v = pool[rnd.Next(pool.Count)]; pool.Remove(v); lots.Add(new Lot { Car = v, Value = Math.Max(40000, v.BasePrice) }); }
        }

        public Menu Menu(Action closeHub)
        {
            Roll();
            var m = new Menu("Auction house", "Three lots today, live bidding");
            foreach (var ll in lots)
            {
                var l = ll;
                m.Add(new MenuItem(l.Car.Name, () => { closeHub(); Bid(l); }) { Value = () => "est. $" + l.Value.ToString("N0"), Description = () => "Tier " + l.Car.Tier + ", " + l.Car.Class + ". Bidding opens at $" + ((long)(l.Value * 0.55)).ToString("N0") + "." });
            }
            if (lots.Count == 0) m.Add(new MenuItem("No lots today") { Enabled = false });
            return m;
        }

        private void Bid(Lot l)
        {
            var rnd = new Random(); long step = Math.Max(1000, l.Value / 20);
            long bid = (long)(l.Value * 0.55); bool mine = false; string leader = "Opening bid";
            long aiMax = (long)(l.Value * (0.85 + rnd.NextDouble() * 0.5));
            int clock = Environment.TickCount + 12000, nextAi = Environment.TickCount + 1500 + rnd.Next(1500);
            while (true)
            {
                int now = Environment.TickCount, left = clock - now;
                Game.DisableAllControlsThisFrame(); Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
                Ds.RectPhys(0f, 0f, 1f, 1f, Ds.A(Ds.Bg, 0.92f));
                Ds.T("AUCTION HOUSE", 0.5f, 0.12f, 0.32f, Ds.Muted, Ds.Display, GTA.UI.Alignment.Center);
                Ds.T(l.Car.Name.ToUpperInvariant(), 0.5f, 0.15f, 1.1f, Ds.Text, Ds.Display, GTA.UI.Alignment.Center);
                PerformanceIndex.Badge(PerformanceIndex.Of(l.Car), 0.5f - Ds.Px(0.05f), 0.25f, 0.032f);
                Ds.T("$" + bid.ToString("N0"), 0.5f, 0.36f, 2.0f, mine ? Ds.Lime : Ds.Amber, Ds.Display, GTA.UI.Alignment.Center);
                Ds.T(mine ? "YOU ARE THE HIGHEST BIDDER" : leader.ToUpperInvariant(), 0.5f, 0.52f, 0.36f, mine ? Ds.Lime : Ds.Muted, Ds.Display, GTA.UI.Alignment.Center);
                float w = Ds.Px(0.4f); Ds.Rect(0.5f - w / 2f, 0.58f, w, 0.008f, Ds.Stroke); Ds.Rect(0.5f - w / 2f, 0.58f, w * Math.Max(0f, left / 12000f), 0.008f, left < 4000 ? Ds.Red : Ds.Text);
                Ds.T(left > 0 ? (left / 1000f).ToString("0.0") + " s" : "SOLD", 0.5f, 0.6f, 0.4f, Ds.Text, Ds.Display, GTA.UI.Alignment.Center);
                Ds.T("YOUR CASH  $" + saves.Data.Cash.ToString("N0"), 0.5f, 0.68f, 0.3f, Ds.Muted, Ds.Display, GTA.UI.Alignment.Center);
                if (left <= 0) break;
                Buttons.Draw(Control.FrontendAccept, "Bid +$" + step.ToString("N0"), Control.FrontendCancel, "Walk away");
                if (Input.JustPressed(Control.FrontendAccept) && !mine && saves.Data.Cash >= bid + step) { bid += step; mine = true; clock = Math.Max(clock, now + 5000); nextAi = now + 900 + rnd.Next(1800); Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true); }
                if (Input.JustPressed(Control.FrontendCancel) && !mine) return;
                if (mine && now > nextAi && bid + step <= aiMax) { bid += step; mine = false; leader = Bidders[rnd.Next(Bidders.Length)] + " bids"; clock = Math.Max(clock, now + 5000); Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true); }
                else if (!mine && now > nextAi && bid + step <= aiMax && leader == "Opening bid") { bid += step; leader = Bidders[rnd.Next(Bidders.Length)] + " bids"; nextAi = now + 1500 + rnd.Next(2000); }
                Script.Wait(0);
            }
            if (mine && money.TrySpend(bid, "auction", "Won " + l.Car.Name + " at auction", null))
            {
                lots.Remove(l);
                if (Career.Progression.Instance != null) Career.Progression.Instance.GiveCar(l.Car.Id, "Auction");
                Shard.Big("SOLD TO YOU", l.Car.Name.ToUpperInvariant() + "  ·  $" + bid.ToString("N0"), Shard.Gold, 4000);
            }
            else Shard.Mid("SOLD", "The " + l.Car.Name + " went to another bidder for $" + bid.ToString("N0") + ".", Shard.Red, 3000);
        }
    }

    /// <summary>Your radio station: tuned in automatically whenever you get in a car in Midnight Club.</summary>
    public static class Radio
    {
        public static readonly string[] Ids = { "", "OFF", "RADIO_01_CLASS_ROCK", "RADIO_02_POP", "RADIO_03_HIPHOP_NEW", "RADIO_04_PUNK", "RADIO_06_COUNTRY", "RADIO_07_DANCE_01", "RADIO_08_MEXICAN", "RADIO_09_HIPHOP_OLD", "RADIO_12_REGGAE", "RADIO_13_JAZZ", "RADIO_14_DANCE_02", "RADIO_15_MOTOWN", "RADIO_16_SILVERLAKE", "RADIO_17_FUNK", "RADIO_18_90S_ROCK", "RADIO_20_THELAB", "RADIO_22_DLC_BATTLE_MIX1_RADIO", "RADIO_23_DLC_XM19_RADIO", "RADIO_27_DLC_PRHEI4", "RADIO_34_DLC_HEI4_KULT", "RADIO_35_DLC_HEI4_MLR", "RADIO_37_MOTOMAMI" };
        public static readonly string[] Names = { "Game default", "Radio off", "Los Santos Rock Radio", "Non-Stop-Pop FM", "Radio Los Santos", "Channel X", "Rebel Radio", "Soulwax FM", "East Los FM", "West Coast Classics", "Blue Ark", "Worldwide FM", "FlyLo FM", "The Lowdown 91.1", "Radio Mirror Park", "Space 103.2", "Vinewood Boulevard Radio", "The Lab", "LS Underground Radio", "iFruit Radio", "Still Slipping LS", "Kult FM", "The Music Locker", "MOTOMAMI LS" };
        private static int lastCar;
        public static void Tick(Config cfg)
        {
            var v = Game.Player.Character.CurrentVehicle; if (v == null || !v.Exists()) { lastCar = 0; return; }
            if (v.Handle == lastCar) return; lastCar = v.Handle;
            if (string.IsNullOrEmpty(cfg.RadioStation)) return;
            Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, v, true);
            Function.Call(Hash.SET_VEH_RADIO_STATION, v, cfg.RadioStation);
        }
        public static void Retune() { lastCar = 0; }
    }
}
