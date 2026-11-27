using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.Racing;
using MidnightClub.UI;

namespace MidnightClub.Career
{
    public sealed class LevelUpEvent { public int Level; public LevelUpEvent(int l) { Level = l; } }
    public sealed class XpEvent { public long Amount; public string Reason; public XpEvent(long a, string r) { Amount = a; Reason = r; } }

    /// <summary>Driver level (XP from everything you do), level rewards, road discovery % and daily challenges.
    /// REP still decides your street standing (tiers, races, rivals); level is the long-term progression track.</summary>
    public sealed class Progression
    {
        public static Progression Instance { get; private set; }
        public const int MaxLevel = 50;
        private readonly SaveManager saves; private readonly TransactionManager money; private readonly Garages.GarageManager garages;
        private readonly WorldSim.WorldCalendar calendar;
        private readonly Random rnd = new Random();

        public Progression(SaveManager saves, TransactionManager money, Garages.GarageManager garages, WorldSim.WorldCalendar calendar)
        {
            Instance = this; this.saves = saves; this.money = money; this.garages = garages; this.calendar = calendar;
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<RaceFinishedEvent>(e =>
            {
                int pos = e.Result.Position;
                long xp = pos == 0 ? 100 : pos == 1 ? 900 : pos == 2 ? 550 : pos == 3 ? 400 : 250;
                if (e.RivalId != null && e.Won) xp += 1200;
                Award(xp, e.Race.Name);
                if (pos == 1 && e.Race.HasOpponents) Bump("win", 1);
                if (pos > 0) Bump("race", 1);
            });
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To == GameMode.MIDNIGHT_CLUB) loginPending = Game.GameTime + 9000; });
            EventBus.Subscribe<MidnightTickEvent>(e => { if (loginPending > 0 && Game.GameTime > loginPending) { loginPending = 0; DailyLogin(); } });
            EventBus.Subscribe<Rivals.RivalDefeatedEvent>(e => Award(2500, "Rival dethroned"));
            EventBus.Subscribe<Police.PursuitEndedEvent>(e => { if (e.Escaped) { Award(150 * e.Level, "Escaped the police"); Bump("escape", 1); } });
            EventBus.Subscribe<MeetShowoffEvent>(e => Award(250, "Car meet"));
            EventBus.Subscribe<WorkshopPurchaseEvent>(e => Award(60, "Upgrade"));
            EventBus.Subscribe<StyleEvent>(e => { styleBank += e.Points; Bump("style", e.Points); });
            EventBus.Subscribe<WorldSim.NewDayEvent>(e => RollDaily());
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To == GameMode.MIDNIGHT_CLUB) { LoadRoads(); RollDaily(); } });
        }

        // ------------------------------------------------------------------ levels
        public static long XpForLevel(int level) { return level <= 1 ? 0 : (long)(level - 1) * 900 + (long)(level - 1) * (level - 2) * 110; }
        public int Level { get { long xp = saves.Data == null ? 0 : saves.Data.Xp; int l = 1; while (l < MaxLevel && xp >= XpForLevel(l + 1)) l++; return l; } }
        public float LevelProgress
        {
            get { int l = Level; if (l >= MaxLevel) return 1f; long a = XpForLevel(l), b = XpForLevel(l + 1); return (saves.Data.Xp - a) / (float)(b - a); }
        }
        private long styleBank;

        public void Award(long xp, string reason)
        {
            if (saves.Data == null || xp <= 0) return;
            int before = Level;
            saves.Data.Xp += xp; saves.IsDirty = true;
            EventBus.Publish(new XpEvent(xp, reason));
            int after = Level;
            for (int l = Math.Max(before + 1, saves.Data.LevelRewarded + 1); l <= after; l++) LevelUp(l);
        }

        public sealed class Reward { public int Level; public string Text; public long Cash; public string Car; public int Barn = -1; }
        public static readonly List<Reward> Rewards = BuildRewards();
        private static List<Reward> BuildRewards()
        {
            var l = new List<Reward>();
            for (int i = 2; i <= MaxLevel; i++) l.Add(new Reward { Level = i, Cash = 1500 + i * 600, Text = "$" + (1500 + i * 600).ToString("N0") });
            Action<int, string, string> car = (lv, model, name) => { var r = l.First(x => x.Level == lv); r.Car = model; r.Text += " + " + name; };
            Action<int, int> barn = (lv, i) => { var r = l.First(x => x.Level == lv); r.Barn = i; r.Text += " + barn find rumour"; };
            barn(3, 0); barn(7, 1); barn(12, 2); barn(17, 3); barn(23, 4); barn(30, 5);
            car(5, "sultan", "Karin Sultan"); car(10, "elegy2", "Annis Elegy RH8"); car(15, "jester", "Dinka Jester");
            car(20, "banshee2", "Bravado Banshee 900R"); car(25, "comet2", "Pfister Comet"); car(35, "zentorno", "Pegassi Zentorno"); car(50, "entityxf", "Overflod Entity XF");
            return l;
        }

        private void LevelUp(int level)
        {
            saves.Data.LevelRewarded = level;
            var r = Rewards.FirstOrDefault(x => x.Level == level);
            Shard.Big("LEVEL " + level, r != null ? r.Text : "", Shard.Purple, 5500);
            if (r != null)
            {
                if (r.Cash > 0) money.Earn(r.Cash, 0, "level", "Level " + level + " reward", "lvl:" + level);
                if (r.Car != null) GiveCar(r.Car, "Level " + level + " reward");
                if (r.Barn >= 0 && Explore.Discovery.Instance != null) Explore.Discovery.Instance.RevealRumour(r.Barn);
            }
            EventBus.Publish(new LevelUpEvent(level));
            Log.Info("Level up: " + level);
        }

        private int loginPending;
        /// <summary>Daily streak: the first session each real-world day pays out, more for consecutive days (max 7).</summary>
        private void DailyLogin()
        {
            var d = saves.Data; if (d == null) return;
            int today = int.Parse(DateTime.Now.ToString("yyyyMMdd"));
            if (d.LastLoginDay == today) return;
            int yesterday = int.Parse(DateTime.Now.AddDays(-1).ToString("yyyyMMdd"));
            d.LoginStreak = d.LastLoginDay == yesterday ? Math.Min(d.LoginStreak + 1, 99) : 1;
            d.LastLoginDay = today; saves.IsDirty = true;
            int k = Math.Min(7, d.LoginStreak); long cash = 2000L * k;
            money.Earn(cash, 25L * k, "login", "Daily streak day " + d.LoginStreak, "login:" + today);
            Award(150 * k, "Daily streak");
            UI.Shard.Mid("DAILY STREAK  ·  DAY " + d.LoginStreak, "+$" + cash.ToString("N0") + (k < 7 ? "  ·  come back tomorrow for more" : "  ·  max bonus"), UI.Shard.Gold, 4500);
        }

        public void GiveCar(string model, string why)
        {
            var m = new Model(model);
            if (!m.IsValid || !m.IsVehicle) { Log.Warn("Reward car " + model + " not in this game build"); return; }
            var rec = Vehicles.VehicleManager.CreateRecord(model, 0);
            if (garages.AssignToFreeGarage(rec)) { saves.Data.Vehicles.Add(rec); Notification.PostTicker("~p~" + why + "~s~: a new car is waiting in " + garages.GarageName(rec.GarageId) + ".", true, false); }
            else { saves.Data.PendingCars.Add(model); Notification.PostTicker("~p~" + why + "~s~: your garages are full. The car will be delivered once you have a free space.", true, false); }
            saves.IsDirty = true;
        }

        private void DeliverPending()
        {
            if (saves.Data.PendingCars.Count == 0) return;
            var model = saves.Data.PendingCars[0];
            var rec = Vehicles.VehicleManager.CreateRecord(model, 0);
            if (!garages.AssignToFreeGarage(rec)) return;
            saves.Data.PendingCars.RemoveAt(0); saves.Data.Vehicles.Add(rec); saves.IsDirty = true;
            Notification.PostTicker("~p~Delivered~s~: a reward car is now in " + garages.GarageName(rec.GarageId) + ".", true, false);
        }

        // ------------------------------------------------------------------ road discovery
        private byte[] roadBits; private int roadCount, roadTotal; private int nextRoad, nextSlow;
        public float RoadPercent { get { return roadTotal == 0 ? 0f : roadCount * 100f / roadTotal; } }
        private static readonly int[] RoadSteps = { 1, 5, 10, 25, 50, 75, 100 };

        private void LoadRoads()
        {
            if (!RoadGraph.EnsureLoaded()) return;
            int n = RoadGraph.Count; roadBits = new byte[(n + 7) / 8];
            try { if (!string.IsNullOrEmpty(saves.Data.RoadBits)) { var b = Convert.FromBase64String(saves.Data.RoadBits); Array.Copy(b, roadBits, Math.Min(b.Length, roadBits.Length)); } } catch { }
            roadTotal = 0; roadCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (!Countable(i)) continue;
                roadTotal++; if ((roadBits[i >> 3] & (1 << (i & 7))) != 0) roadCount++;
            }
        }
        private static bool Countable(int i) { var f = RoadGraph.Flags(i); return (f & RoadGraph.NodeFlags.Gps) != 0 && (f & RoadGraph.NodeFlags.Water) == 0; }

        private void RoadTick(Vehicle car)
        {
            if (roadBits == null || Game.GameTime < nextRoad) return;
            nextRoad = Game.GameTime + 250;
            int i = RoadGraph.Nearest(car.Position, 40f);
            if (i < 0 || !Countable(i) || RoadGraph.Pos(i).DistanceTo(car.Position) > 22f) return;
            if ((roadBits[i >> 3] & (1 << (i & 7))) != 0) return;
            roadBits[i >> 3] |= (byte)(1 << (i & 7)); roadCount++;
            if (roadCount % 25 == 0) { saves.Data.RoadBits = Convert.ToBase64String(roadBits); saves.IsDirty = true; }
            if (roadCount % 10 == 0) Award(10, "New roads");
            foreach (int st in RoadSteps)
                if (RoadPercent >= st && !saves.Data.RoadMilestones.Contains(st))
                {
                    saves.Data.RoadMilestones.Add(st);
                    long cash = st * 400 + 1000; long xp = st * 60 + 300;
                    Shard.Mid("ROADS DISCOVERED " + st + "%", "$" + cash.ToString("N0") + "  +" + xp + " XP", Shard.Blue);
                    money.Earn(cash, 0, "explore", "Road discovery " + st + "%", "road:" + st);
                    Award(xp, "Road discovery");
                }
        }

        // ------------------------------------------------------------------ daily challenges
        public sealed class Daily { public string Id, Kind, Text; public long Target, Cash, Xp; }
        public readonly List<Daily> Today = new List<Daily>();

        private static readonly string[] Kinds = { "win", "race", "km", "speed", "style", "zone", "discover", "escape", "trap" };
        private void RollDaily()
        {
            var d = saves.Data; if (d == null) return;
            int day = calendar.Day;
            if (d.DailyDay != day) { d.DailyDay = day; d.Daily.Clear(); d.DailyDone.Clear(); saves.IsDirty = true; }
            Today.Clear();
            var r = new Random(day * 7919 + 17); var picks = Kinds.OrderBy(k => r.Next()).Take(3).ToList();
            int tier = CareerManager.Instance != null ? CareerManager.Instance.Tier : 1;
            foreach (var k in picks)
            {
                var c = new Daily { Id = day + ":" + k, Kind = k, Cash = 2000 + tier * 1500, Xp = 600 + tier * 200 };
                switch (k)
                {
                    case "win": c.Target = 1; c.Text = "Win a race with opponents"; break;
                    case "race": c.Target = 2; c.Text = "Finish 2 races"; break;
                    case "km": c.Target = 10 + tier * 5; c.Text = "Drive " + c.Target + " km"; break;
                    case "speed": c.Target = 180 + tier * 20; c.Text = "Hit " + c.Target + " km/h"; break;
                    case "style": c.Target = 3000 * tier; c.Text = "Earn " + c.Target.ToString("N0") + " style points"; break;
                    case "zone": c.Target = 1; c.Text = "Earn a star in a speed or drift zone"; break;
                    case "discover": c.Target = 1; c.Text = "Discover a new location"; break;
                    case "escape": c.Target = 1; c.Text = "Escape a police pursuit"; break;
                    case "trap": c.Target = 1; c.Text = "Set a new speed trap record"; break;
                }
                Today.Add(c);
            }
        }

        public long Progress(Daily c) { var kv = saves.Data.Daily.FirstOrDefault(x => x.Key == c.Id); long v; return kv != null && long.TryParse(kv.Value, out v) ? v : 0; }
        public bool Done(Daily c) { return saves.Data.DailyDone.Contains(c.Id); }

        /// <summary>Adds progress to today's challenges of a kind ("max" kinds like speed keep the best value).</summary>
        public void Bump(string kind, long amount, bool max = false)
        {
            if (saves.Data == null) return;
            WeeklyBump(kind, amount, max);
            foreach (var c in Today)
            {
                if (c.Kind != kind || Done(c)) continue;
                long v = max ? Math.Max(Progress(c), amount) : Progress(c) + amount;
                saves.Data.Daily.RemoveAll(x => x.Key == c.Id);
                saves.Data.Daily.Add(new KeyValue { Key = c.Id, Value = v.ToString() });
                if (v >= c.Target)
                {
                    saves.Data.DailyDone.Add(c.Id);
                    Shard.Mid("DAILY CHALLENGE COMPLETE", c.Text + "   $" + c.Cash.ToString("N0") + "  +" + c.Xp + " XP", Shard.Gold);
                    money.Earn(c.Cash, 0, "daily", "Daily: " + c.Text, "daily:" + c.Id);
                    Award(c.Xp, "Daily challenge");
                }
                saves.IsDirty = true;
            }
        }

        // ------------------------------------------------------------------ weekly playlist (real-world week)
        public readonly List<Daily> Week = new List<Daily>();
        private static readonly string[] WeekKinds = { "win", "race", "km", "escape", "token", "outrun", "takedown", "jump", "turf", "photo", "zone", "trap" };
        public static int WeekNumber { get { var n = DateTime.Now; return n.Year * 100 + System.Globalization.CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(n, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday); } }
        public string WeekCarModel { get { string[] cars = { "jester4", "zr350", "comet6", "euros", "growler", "cypher", "vectre", "remus", "calico", "tailgater2", "sultan3", "dominator7" }; return cars[WeekNumber % cars.Length]; } }

        private void RollWeekly()
        {
            var d = saves.Data; if (d == null) return;
            int w = WeekNumber;
            if (d.WeeklyId != w) { d.WeeklyId = w; d.WeeklyProgress.Clear(); d.WeeklyDone.Clear(); saves.IsDirty = true; }
            Week.Clear();
            var r = new Random(w * 104729 + 3); var picks = WeekKinds.OrderBy(k => r.Next()).Take(5).ToList();
            foreach (var k in picks)
            {
                var c = new Daily { Id = "w" + w + ":" + k, Kind = k, Cash = 6000, Xp = 1500 };
                switch (k)
                {
                    case "win": c.Target = 5; c.Text = "Win 5 races with opponents"; break;
                    case "race": c.Target = 10; c.Text = "Finish 10 races"; break;
                    case "km": c.Target = 100; c.Text = "Drive 100 km"; break;
                    case "escape": c.Target = 3; c.Text = "Escape 3 police pursuits"; break;
                    case "token": c.Target = 3; c.Text = "Collect 3 Midnight Tokens"; break;
                    case "outrun": c.Target = 3; c.Text = "Win 3 outrun battles"; break;
                    case "takedown": c.Target = 5; c.Text = "Take down 5 opponents in races"; break;
                    case "jump": c.Target = 60; c.Text = "Jump 60 m in one go"; break;
                    case "turf": c.Target = 3; c.Text = "Win 3 races for turf"; break;
                    case "photo": c.Target = 5; c.Text = "Take 5 photos in photo mode"; break;
                    case "zone": c.Target = 4; c.Text = "Earn 4 skill zone stars"; break;
                    case "trap": c.Target = 4; c.Text = "Set 4 speed trap records"; break;
                }
                Week.Add(c);
            }
        }
        private long WProgress(Daily c) { var kv = saves.Data.WeeklyProgress.FirstOrDefault(x => x.Key == c.Id); long v; return kv != null && long.TryParse(kv.Value, out v) ? v : 0; }
        private bool WDone(Daily c) { return saves.Data.WeeklyDone.Contains(c.Id); }
        private void WeeklyBump(string kind, long amount, bool max)
        {
            if (saves.Data.WeeklyId != WeekNumber || Week.Count == 0) RollWeekly();
            foreach (var c in Week)
            {
                if (c.Kind != kind || WDone(c)) continue;
                long v = max ? Math.Max(WProgress(c), amount) : WProgress(c) + amount;
                saves.Data.WeeklyProgress.RemoveAll(x => x.Key == c.Id); saves.Data.WeeklyProgress.Add(new KeyValue { Key = c.Id, Value = v.ToString() });
                if (v >= c.Target)
                {
                    saves.Data.WeeklyDone.Add(c.Id);
                    money.Earn(c.Cash, 100, "weekly", "Weekly: " + c.Text, "weekly:" + c.Id);
                    Award(c.Xp, "Weekly challenge");
                    if (Week.All(WDone))
                    {
                        Shard.Big("WEEKLY PLAYLIST COMPLETE", "Reward car on its way: " + WeekCarModel.ToUpperInvariant(), Shard.Gold, 6000);
                        GiveCar(WeekCarModel, "Weekly playlist");
                    }
                    else Shard.Mid("WEEKLY CHALLENGE COMPLETE", c.Text + "   $" + c.Cash.ToString("N0") + "  ·  " + Week.Count(WDone) + " / " + Week.Count, Shard.Gold);
                }
                saves.IsDirty = true;
            }
        }

        public UI.Menu WeeklyMenu()
        {
            if (saves.Data.WeeklyId != WeekNumber || Week.Count == 0) RollWeekly();
            var m = new UI.Menu("Weekly playlist", "Complete all five for a reward car");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                foreach (var c in Week) { var x = c; m.Add(new UI.MenuItem(x.Text) { Value = () => WDone(x) ? "DONE" : WProgress(x).ToString("N0") + " / " + x.Target.ToString("N0"), Description = () => "$" + x.Cash.ToString("N0") + " and " + x.Xp + " XP." }); }
                m.Add(new UI.MenuItem("Reward car") { Value = () => WeekCarModel.ToUpperInvariant(), Enabled = false, Description = () => "A new playlist every Monday." });
            };
            return m;
        }
        public int WeekDone { get { if (saves.Data == null) return 0; if (saves.Data.WeeklyId != WeekNumber || Week.Count == 0) RollWeekly(); return Week.Count(WDone); } }

        public UI.Menu DailyMenu()
        {
            var m = new UI.Menu("Daily", "New challenges every Midnight Club day");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                foreach (var c in Today)
                {
                    var x = c;
                    m.Add(new UI.MenuItem(x.Text) { Value = () => Done(x) ? "DONE" : Progress(x).ToString("N0") + " / " + x.Target.ToString("N0"), Description = () => "Reward: $" + x.Cash.ToString("N0") + " and " + x.Xp + " XP." });
                }
            };
            return m;
        }

        public UI.Menu ProgressMenu()
        {
            var m = new UI.Menu("Progress", "Level " + Level + "  ·  " + saves.Data.Xp.ToString("N0") + " XP");
            m.OnOpen = () =>
            {
                m.Items.Clear(); m.Subtitle = "Level " + Level + "  ·  " + saves.Data.Xp.ToString("N0") + " XP";
                int l = Level;
                m.Add(new UI.MenuItem("Next level") { Value = () => l >= MaxLevel ? "MAX" : (XpForLevel(l + 1) - saves.Data.Xp).ToString("N0") + " XP to go", Enabled = false });
                m.Add(new UI.MenuItem("Roads discovered") { Value = () => RoadPercent.ToString("0.0") + "%", Enabled = false, Description = () => "Drive new roads anywhere in San Andreas. Rewards at 1, 5, 10, 25, 50, 75 and 100%." });
                if (Explore.Discovery.Instance != null)
                {
                    var dsc = Explore.Discovery.Instance;
                    m.Add(new UI.MenuItem("Locations found") { Value = () => dsc.KnownCount + " / " + dsc.TotalCount, Enabled = false, Description = () => "Workshops, dealerships, garages, races, speed traps and skill zones show up on the map once you find them. Look for the ? marks." });
                    m.Add(new UI.MenuItem("Skill zone stars") { Value = () => dsc.StarsEarned + " / " + dsc.StarsTotal, Enabled = false });
                    m.Add(new UI.MenuItem("Barn finds") { Value = () => saves.Data.BarnFound.Count + " / " + dsc.BarnCount, Enabled = false });
                }
                foreach (var r in Rewards.Where(x => x.Level > l).Take(6)) { var rr = r; m.Add(new UI.MenuItem("Level " + rr.Level) { Value = () => rr.Text, Enabled = false }); }
            };
            return m;
        }

        // ------------------------------------------------------------------ tick
        private Vector3 lastPos; private bool hasLast;
        private void Tick()
        {
            if (saves.Data == null) return;
            var ped = Game.Player.Character; var car = ped.CurrentVehicle;
            if (car != null && car.Exists() && !garages.IsInside)
            {
                RoadTick(car);
                if (hasLast) { float d = car.Position.DistanceTo(lastPos); if (d < 60f) { saves.Data.KmDriven += d / 1000f; kmAcc += d / 1000f; } }
                lastPos = car.Position; hasLast = true;
                if (kmAcc >= 1f) { Bump("km", 1); kmAcc -= 1f; }
                if (Game.GameTime > nextSlow) { nextSlow = Game.GameTime + 500; Bump("speed", (long)(car.Speed * 3.6f), true); }
            }
            else hasLast = false;
            if (styleBank >= 1000) { Award(styleBank / 20, "Style"); styleBank = 0; }
            if (Game.GameTime % 30000 < 20) DeliverPending();
        }
        private float kmAcc;
    }
}
