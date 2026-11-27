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
using MidnightClub.Garages;
using MidnightClub.Persistence;
using MidnightClub.UI;
using MidnightClub.Vehicles;
using MidnightClub.WorldSim;

namespace MidnightClub.Dealerships
{
    public sealed class StockItem
    {
        public VehicleInfo Info;
        public long Price, ListPrice;            // ListPrice > Price when on promotion
        public string Badge;                     // RARE / PROMO / PROJECT CAR ...
        public string SoldKey;
        public string Seller;
        public Func<OwnedVehicle> MakeRecord;    // fresh record for display / purchase
        public OwnedVehicle Preview;             // cached record used for the display car
    }

    /// <summary>Dealership ecosystem: physical locations, deterministic rotating stock driven by the
    /// Midnight Club calendar (every eligible car appears once per cycle, reloads can't reroll), promotions,
    /// used market, special orders and the validated purchase flow.</summary>
    public sealed class DealershipManager
    {
        private readonly SaveManager saves;
        private readonly TransactionManager money;
        private readonly VehicleRegistry registry;
        private readonly VehicleManager vehicles;
        private readonly GarageManager garages;
        private readonly WorldCalendar calendar;
        private readonly MenuStack stack;
        private readonly string dataDir;
        private List<DealershipDefinition> defs = new List<DealershipDefinition>();
        private readonly Dictionary<string, List<VehicleInfo>> pools = new Dictionary<string, List<VehicleInfo>>();
        private readonly Dictionary<string, Vector3> markers = new Dictionary<string, Vector3>();
        private readonly List<Blip> blips = new List<Blip>();
        private DealershipDefinition nearby;
        public bool IsBusy { get; private set; }
        public const double SpecialOrderPremium = 1.35;

        public DealershipManager(SaveManager saves, TransactionManager money, VehicleRegistry registry, VehicleManager vehicles, GarageManager garages, WorldCalendar calendar, MenuStack stack, string dataDir)
        {
            this.saves = saves; this.money = money; this.registry = registry; this.vehicles = vehicles; this.garages = garages;
            this.calendar = calendar; this.stack = stack; this.dataDir = dataDir;
            EventBus.Subscribe<Explore.DiscoveredEvent>(e => { if (e.Id.StartsWith("dl:")) PlaceMarkers(); });
            EventBus.Subscribe<ModeChangedEvent>(OnModeChanged);
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<NewDayEvent>(e => OnNewDay(e.Day));
        }

        public IEnumerable<DealershipDefinition> Definitions { get { return defs; } }
        public WorldCalendar Calendar { get { return calendar; } }
        public VehicleRegistry Registry { get { return registry; } }
        public GarageManager Garages { get { return garages; } }

        // ------------------------------------------------------------------ setup
        private void OnModeChanged(ModeChangedEvent e)
        {
            if (e.To == GameMode.MIDNIGHT_CLUB)
            {
                try { defs = DealershipDefinitions.Load(dataDir); stockCache.Clear(); BuildPools(); PlaceMarkers(); }
                catch (Exception ex) { Log.Error("Loading dealerships", ex); }
            }
            else { ClearBlips(); ClearLots(); }
        }

        private void BuildPools()
        {
            pools.Clear();
            var eligible = registry.All.Where(v => v.Enabled && v.DealershipEligible).OrderBy(v => v.Hash).ToList();
            var covered = new HashSet<int>();
            foreach (var d in defs)
            {
                var list = eligible.Where(d.Filter).ToList();
                pools[d.Id] = list;
                if (!d.IsUsedMarket) foreach (var v in list) covered.Add(v.Hash);
            }
            // Never leave an eligible car without a dealership: uncovered cars go to Southern SA Super Autos.
            List<VehicleInfo> southern;
            if (pools.TryGetValue("southern", out southern))
                foreach (var v in eligible) if (!covered.Contains(v.Hash) && v.Class != VehicleClass.OpenWheel) southern.Add(v);
            foreach (var d in defs) Log.Info("Dealership " + d.Id + ": " + pools[d.Id].Count + " eligible cars");
        }

        private void PlaceMarkers()
        {
            ClearBlips(); markers.Clear();
            foreach (var d in defs)
            {
                float h;
                var p = World.GetNextPositionOnStreetWithHeading(d.Position, out h, false);
                if (p == Vector3.Zero || p.DistanceTo(d.Position) > 60f) p = d.Position;
                markers[d.Id] = p;
                Explore.Discovery.Register("dl:" + d.Id, d.Name, "Dealership", p);
                if (!Explore.Discovery.Known("dl:" + d.Id)) continue;
                var b = UI.BlipRegistry.Own(World.CreateBlip(d.Position));
                b.Sprite = d.Sprite; b.Color = d.Color; b.Name = d.Name; b.IsShortRange = true; b.Scale = 0.9f;
                blips.Add(b);
            }
        }

        private void ClearBlips()
        {
            foreach (var b in blips) try { if (b.Exists()) b.Delete(); } catch { }
            blips.Clear();
        }

        private void OnNewDay(int day)
        {
            foreach (var d in defs)
                if (day % d.RotationDays == 0)
                    UI.Toast.Post("~p~" + d.Name + "~s~: new stock has arrived.");
        }

        // ------------------------------------------------------------------ the lot
        // The current offer is parked on the dealership's forecourt (spots found procedurally: flat, off the road,
        // clear of walls). Pull into the lot - anywhere within its area - and the car picker opens on request;
        // stopping next to a display car opens the picker on that car.
        private sealed class Lot { public List<Vector3> Spots; public List<float> Headings; public readonly List<Vehicle> Cars = new List<Vehicle>(); public int Rot = -1; }
        private readonly Dictionary<string, Lot> lots = new Dictionary<string, Lot>();
        private const float AreaRadius = 30f;

        private Lot LotFor(DealershipDefinition d)
        {
            Lot l; if (lots.TryGetValue(d.Id, out l)) return l;
            l = new Lot { Spots = new List<Vector3>(), Headings = new List<float>() };
            lots[d.Id] = l;
            var c = d.Position;
            for (float r = 6f; r <= 24f && l.Spots.Count < 6; r += 3f)
                for (int k = 0; k < 16 && l.Spots.Count < 6; k++)
                {
                    double ang = k * Math.PI * 2 / 16 + r * 0.37;
                    var p = c + new Vector3((float)Math.Cos(ang) * r, (float)Math.Sin(ang) * r, 0f);
                    float gz = UI.WorldMarker.Ground(p + new Vector3(0, 0, 4f));
                    if (Math.Abs(gz - c.Z) > 2.5f) continue;
                    p.Z = gz;
                    if (Function.Call<bool>(Hash.IS_POINT_ON_ROAD, p.X, p.Y, p.Z, 0)) continue;
                    if (l.Spots.Any(o => o.DistanceTo(p) < 5f)) continue;
                    if (Workshops.WorkshopManager.All.Any(w => w.Position.DistanceTo(p) < 9f)) continue;
                    bool flat = true, clear = true;
                    foreach (var off in new[] { new Vector3(1.4f, 0, 0), new Vector3(-1.4f, 0, 0), new Vector3(0, 2.6f, 0), new Vector3(0, -2.6f, 0) })
                    {
                        float z2 = UI.WorldMarker.Ground(p + off + new Vector3(0, 0, 4f));
                        if (Math.Abs(z2 - gz) > 0.35f) { flat = false; break; }
                        var hit = World.Raycast(p + new Vector3(0, 0, 0.9f), p + off * 1.15f + new Vector3(0, 0, 0.9f), IntersectFlags.Map);
                        if (hit.DidHit) { clear = false; break; }
                    }
                    if (!flat || !clear) continue;
                    var toC = c - p;
                    l.Spots.Add(p); l.Headings.Add((float)(Math.Atan2(-toC.X, toC.Y) * 180.0 / Math.PI) + 30f);
                }
            Log.Info("Dealership lot " + d.Id + ": " + l.Spots.Count + " display spots");
            return l;
        }

        private void ClearLot(Lot l) { foreach (var v in l.Cars) try { if (v != null && v.Exists()) v.Delete(); } catch { } l.Cars.Clear(); }
        public void ClearLots() { foreach (var l in lots.Values) ClearLot(l); }

        private void LotTick(DealershipDefinition d, float dist)
        {
            if (dist > 160f) { Lot old; if (lots.TryGetValue(d.Id, out old) && old.Cars.Count > 0) ClearLot(old); return; }
            if (dist > 130f) return;
            var l = LotFor(d);
            int rot = Rotation(d);
            if (l.Rot != rot) { ClearLot(l); l.Rot = rot; }
            var stock = Stock(d);
            int want = Math.Min(l.Spots.Count, stock.Count);
            if (l.Cars.Count >= want) return;
            int i = l.Cars.Count;
            var item = stock[i];
            if (item.Preview == null) item.Preview = item.MakeRecord();
            var model = item.Preview.ModelHash != 0 ? new Model(item.Preview.ModelHash) : new Model(item.Preview.Model);
            if (!Core.Assets.Ready(model)) return;                 // streams in the background, one car per frame
            try
            {
                var v = vehicles.SpawnDisplay(item.Preview, l.Spots[i] + new Vector3(0, 0, 0.3f), l.Headings[i]);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, v, true);
                v.IsPersistent = true;
                l.Cars.Add(v);
            }
            catch (Exception ex) { Log.Error("Lot car " + item.Info.Id, ex); l.Cars.Add(null); }
        }

        private void Tick()
        {
            if (IsBusy || garages.IsInside) return;
            if (Racing.RaceManager.IsRacing) return;
            var ped = Game.Player.Character;
            var pos = ped.Position;
            nearby = null; int nearCar = -1;
            foreach (var d in defs)
            {
                if (!Explore.Discovery.Known("dl:" + d.Id)) continue;
                float dist = pos.DistanceTo(d.Position);
                LotTick(d, dist);
                if (dist > AreaRadius) continue;
                // the workshop next door (Benny's) wins inside its own bay
                if (Workshops.WorkshopManager.All.Any(w => w.Position.DistanceTo(pos) < 10f)) continue;
                nearby = d;
                Lot l; if (lots.TryGetValue(d.Id, out l))
                    for (int i = 0; i < l.Cars.Count; i++)
                    {
                        var v = l.Cars[i]; if (v == null || !v.Exists()) continue;
                        float cd = v.Position.DistanceTo(pos);
                        if (cd < 22f)
                        {
                            var st = Stock(d);
                            if (i < st.Count) UI.WorldMarker.Label(v.Position + new Vector3(0, 0, 1.9f), st[i].Info.DisplayName, "$" + st[i].Price.ToString("N0"), cd, cd < 6f);
                            if (cd < 6f && (nearCar < 0 || cd < l.Cars[nearCar].Position.DistanceTo(pos))) nearCar = i;
                        }
                    }
            }
            if (nearby == null || stack.IsOpen) return;
            if (Workshops.WorkshopManager.Instance != null && Workshops.WorkshopManager.Instance.IsBusy) return;
            var car = vehicles.Current;
            if (car != null && car.Exists() && ped.IsInVehicle(car) && car.Speed > 2.5f) return;
            if (nearCar >= 0) Screen.ShowHelpTextThisFrame("~INPUT_CONTEXT~ Look at this car  ·  " + nearby.Name);
            else Screen.ShowHelpTextThisFrame("~INPUT_CONTEXT~ Browse " + nearby.Name + "'s offer");
            if (UI.Input.JustPressed(Control.Context)) OpenShowroom(nearby, Math.Max(0, nearCar));
        }

        private void OpenShowroom(DealershipDefinition d, int start = 0)
        {
            IsBusy = true;
            EventBus.Publish(new ShowroomOpenedEvent());
            try { foreach (var l in lots.Values) foreach (var v in l.Cars) if (v != null && v.Exists()) v.IsVisible = false; new ShowroomScreen(this, d, vehicles, saves) { StartIndex = start }.Run(); }
            catch (Exception ex) { Log.Error("Showroom failed", ex); }
            finally { IsBusy = false; foreach (var l in lots.Values) foreach (var v in l.Cars) if (v != null && v.Exists()) v.IsVisible = true; stockCache.Clear(); ClearLots(); }
        }

        // ------------------------------------------------------------------ stock
        public int Rotation(DealershipDefinition d) { return calendar.Day / d.RotationDays; }
        public int DaysUntilRotation(DealershipDefinition d) { return d.RotationDays - calendar.Day % d.RotationDays; }

        private static int Seed(params object[] parts) { return RotationMath.Seed(parts); }
        private int StockSize(DealershipDefinition d, int rotation) { return RotationMath.StockSize(d.Id, d.MinStock, d.MaxStock, rotation); }

        public VehicleClass? PromoClass(DealershipDefinition d)
        {
            List<VehicleInfo> pool;
            if (d.IsUsedMarket || !pools.TryGetValue(d.Id, out pool) || pool.Count == 0) return null;
            var classes = pool.Select(v => v.Class).Distinct().OrderBy(c => c).ToList();
            return classes[Seed(d.Id, "promo", Rotation(d)) % classes.Count];
        }

        private readonly Dictionary<string, KeyValuePair<int, List<StockItem>>> stockCache = new Dictionary<string, KeyValuePair<int, List<StockItem>>>();

        public List<StockItem> Stock(DealershipDefinition d)
        {
            List<VehicleInfo> pool;
            if (!pools.TryGetValue(d.Id, out pool) || pool.Count == 0) return new List<StockItem>();
            int rot = Rotation(d);
            KeyValuePair<int, List<StockItem>> cached;
            if (stockCache.TryGetValue(d.Id, out cached) && cached.Key == rot) return cached.Value;
            var list = d.IsUsedMarket ? UsedStock(d, pool, rot) : NewStock(d, pool, rot);
            stockCache[d.Id] = new KeyValuePair<int, List<StockItem>>(rot, list);
            return list;
        }

        private List<StockItem> NewStock(DealershipDefinition d, List<VehicleInfo> pool, int rot)
        {
            var promo = PromoClass(d);
            var items = new List<StockItem>();
            foreach (var v in RotationMath.Pick(d.Id, pool, d.MinStock, d.MaxStock, rot))
            {
                double club = Factions.ClubManager.Instance != null ? Factions.ClubManager.Instance.Discount(d.Id) : 0;
                long list = PricingEngine.Round(v.BasePrice * d.PriceFactor * (1 - club));
                bool onPromo = promo.HasValue && v.Class == promo.Value;
                var info = v;
                items.Add(new StockItem
                {
                    Info = v, ListPrice = list, Price = onPromo ? PricingEngine.Round(list * 0.85) : list,
                    Badge = onPromo ? "PROMO -15%" : v.Tier == "X" ? "RARE" : null,
                    SoldKey = d.Id + ":" + rot + ":" + v.Hash,
                    MakeRecord = () => VehicleManager.CreateRecord(info, 0)
                });
            }
            return items.OrderBy(i => i.Price).ToList();
        }

        private static readonly string[] Sellers = { "Mosley's lot", "Private seller, Davis", "Private seller, Mirror Park", "Estate sale, Rockford Hills", "Ex-rental, LSIA", "Barn find, Grapeseed", "Street racer leaving town", "Repo auction lot" };

        private List<StockItem> UsedStock(DealershipDefinition d, List<VehicleInfo> pool, int rot)
        {
            var rnd = new Random(Seed(d.Id, "used", rot));
            int size = StockSize(d, rot);
            var cheap = pool.Where(v => v.Tier == "D" || v.Tier == "C" || v.Tier == "B").ToList();
            var rare = pool.Where(v => v.Tier == "S" || v.Tier == "X").ToList();
            var items = new List<StockItem>();
            for (int slot = 0; slot < size; slot++)
            {
                bool isRare = rare.Count > 0 && rnd.NextDouble() < 0.08;
                var src = isRare ? rare : (cheap.Count > 0 && rnd.NextDouble() < 0.75 ? cheap : pool);
                var v = src[rnd.Next(src.Count)];
                float engine = rnd.Next(350, 960), body = rnd.Next(300, 980);
                double km = rnd.Next(800, 60000) / 10.0;
                int prim = rnd.Next(0, 160), sec = rnd.Next(0, 160);
                var mods = new List<ModEntry>();
                foreach (int t in new[] { 0, 1, 2, 3, 4, 6, 7, 10 }) if (rnd.NextDouble() < 0.35) mods.Add(new ModEntry(t, rnd.Next(0, 3)));
                bool project = (engine + body) / 2f < 550f;
                double sellerFactor = 0.62 + rnd.NextDouble() * 0.25;
                long price = PricingEngine.Round(PricingEngine.EstimatedValue(v, km, engine, body) * sellerFactor * (project ? 0.8 : 1.0));
                var info = v; var m = mods;
                Func<OwnedVehicle> make = () =>
                {
                    var r = VehicleManager.CreateRecord(info, 0);
                    r.EngineHealth = engine; r.BodyHealth = body; r.Stats.DistanceKm = km;
                    r.PrimaryColor = prim; r.SecondaryColor = sec;
                    r.Mods = m.Select(x => new ModEntry(x.Type, x.Index)).ToList();
                    return r;
                };
                items.Add(new StockItem
                {
                    Info = v, Price = Math.Max(1500, price), ListPrice = Math.Max(1500, price),
                    Badge = isRare ? "RARE FIND" : project ? "PROJECT CAR" : null,
                    Seller = Sellers[rnd.Next(Sellers.Length)],
                    SoldKey = "used:" + rot + ":" + slot, MakeRecord = make
                });
            }
            return items;
        }

        public bool IsSold(StockItem item) { return saves.Data.SoldStock.Contains(item.SoldKey); }

        // ------------------------------------------------------------------ purchase flow (spec 7.9)
        /// <summary>Validates, charges, creates the owned record, assigns a garage, saves and confirms.
        /// Returns null on success or a user-facing reason.</summary>
        public string Purchase(StockItem item, string sourceName)
        {
            var d = saves.Data;
            // 1. validate vehicle
            if (item == null || item.Info == null || !item.Info.Enabled) return "This car isn't available.";
            if (item.SoldKey != null && IsSold(item)) return "Already sold.";
            var career = Career.CareerManager.Instance;
            if (career != null && item.Info.RequiredCareerTier > career.Tier)
                return "Requires career tier " + item.Info.RequiredCareerTier + " (" + Career.CareerManager.TierNames[item.Info.RequiredCareerTier] + "). Earn more REP.";
            var model = new Model(item.Info.Hash);
            if (!model.IsValid || !model.IsInCdImage) return "This car doesn't exist in your game build.";
            // 2. funds
            if (!money.CanAfford(item.Price)) return "Not enough cash. You need $" + item.Price.ToString("N0") + ".";
            // 3. garage capacity
            var garageId = garages.FindFreeGarage();
            if (garageId == null) return "No free garage space. Buy a garage or move cars around.";
            // 4. charge (transaction id makes a double press / replay harmless)
            string tx = "buy:" + (item.SoldKey ?? ("order:" + item.Info.Hash)) + ":" + d.Vehicles.Count(v => v.ModelHash == item.Info.Hash);
            if (!money.TrySpend(item.Price, "vehicle", "Bought " + item.Info.Name + " (" + sourceName + ")", tx, saveNow: false))
                return "Purchase could not be processed.";
            // 5-6. owned record + garage
            var rec = item.MakeRecord();
            rec.PurchasePrice = item.Price;
            rec.PurchasedAtUtc = DateTime.UtcNow.ToString("o");
            d.Vehicles.Add(rec);
            garages.AssignToFreeGarage(rec, garageId);
            // 7-8. save, inventory
            if (item.SoldKey != null) d.SoldStock.Add(item.SoldKey);
            saves.Save();
            // 9. confirmation
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "PROPERTY_PURCHASE", "HUD_AWARDS", true);
            UI.Toast.Post("~p~MIDNIGHT CLUB~s~: " + item.Info.Name + " delivered to ~g~" + garages.GarageName(rec.GarageId) + "~s~.");
            Log.Info("Purchased " + item.Info.Id + " for " + item.Price + " from " + sourceName);
            return null;
        }

        /// <summary>Permanent acquisition path: order any dealership-eligible car from the catalogue at a premium.</summary>
        public StockItem SpecialOrderItem(VehicleInfo v)
        {
            if (!v.Enabled || !v.DealershipEligible) return null;
            var info = v;
            return new StockItem
            {
                Info = v, Price = PricingEngine.Round(v.BasePrice * SpecialOrderPremium), ListPrice = v.BasePrice,
                Badge = "SPECIAL ORDER", MakeRecord = () => VehicleManager.CreateRecord(info, 0)
            };
        }

        public DealershipDefinition WhereSold(VehicleInfo v)
        {
            foreach (var d in defs)
            {
                if (d.IsUsedMarket) continue;
                if (Stock(d).Any(i => i.Info.Hash == v.Hash && !IsSold(i))) return d;
            }
            return null;
        }
    }
}
