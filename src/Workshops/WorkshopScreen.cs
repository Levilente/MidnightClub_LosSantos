using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.UI;
using MidnightClub.Vehicles;
using Control = GTA.Control;
using Font = GTA.UI.Font;
using Keys = System.Windows.Forms.Keys;

namespace MidnightClub.Workshops
{
    /// <summary>Workshop UI: performance (engine, brakes, gearbox, suspension, turbo, Midnight Club ECU tune,
    /// drift tyres), body parts, wheels, respray, lights, tint, livery, repair. Every option previews live
    /// while highlighted and reverts if you back out without buying. All upgrades are real game mods
    /// with real handling/power effects.</summary>
    public sealed class WorkshopScreen
    {
        private readonly WorkshopDefinition w;
        private readonly VehicleManager vehicles;
        private readonly VehicleRegistry registry;
        private readonly SaveManager saves;
        private readonly TransactionManager money;
        private readonly MenuStack menus = new MenuStack();
        private Vehicle car; private OwnedVehicle rec; private VehicleInfo info;
        private Camera cam; private float orbit = 35f; private string focus = "default";
        private bool done; private string note; private int noteUntil;
        private long bp;

        private static readonly Color Orange = Color.FromArgb(255, 255, 170, 40);
        private static readonly Color Panel = Color.FromArgb(205, 8, 6, 16);

        public WorkshopScreen(WorkshopDefinition w, VehicleManager vehicles, VehicleRegistry registry, SaveManager saves, TransactionManager money)
        {
            this.w = w; this.vehicles = vehicles; this.registry = registry; this.saves = saves; this.money = money;
        }

        public void Run()
        {
            car = vehicles.Current; rec = vehicles.CurrentRecord;
            if (car == null || !car.Exists() || rec == null) return;
            info = rec.ModelHash != 0 ? registry.Get(rec.ModelHash) : registry.Get(rec.Model);
            bp = info != null ? info.BasePrice : 20000;
            if (Cameras.CameraSystem.Instance != null) Cameras.CameraSystem.Instance.Release();
            Screen.FadeOut(300); Script.Wait(350);
            car.Mods.InstallModKit();
            Function.Call(Hash.FREEZE_ENTITY_POSITION, car, true);
            car.IsEngineRunning = true;
            cam = Camera.Create(ScriptedCameraNameHash.DefaultScriptedCamera, car.Position, Vector3.Zero, 50f, true, EulerRotationOrder.YXZ);
            Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false, 0);
            Function.Call(Hash.DISPLAY_RADAR, false);
            TakeSnapshot();
            BuildTabs();
            menus.CustomRenderer = DrawShop;
            OpenTab(w.Specialty != null && w.Id == "lsc_lamesa" ? 0 : Math.Max(0, Math.Min(5, 1)));
            shopCardUntil = Game.GameTime + 6000;
            Screen.FadeIn(300);
            try
            {
                while (!done && car.Exists())
                {
                    Game.DisableAllControlsThisFrame();
                    foreach (var c in new[] { Control.FrontendUp, Control.FrontendDown, Control.FrontendLeft, Control.FrontendRight, Control.FrontendAccept, Control.FrontendCancel, Control.FrontendLb, Control.FrontendRb })
                        Game.EnableControlThisFrame(c);
                    UI.Theme.FullscreenFrame();
                    TabInput();
                    if (!menus.IsOpen) done = true; else menus.Tick();
                    if (FitmentManager.Instance != null) FitmentManager.Instance.Tick();
                    Orbit();
                    Script.Wait(0);
                }
            }
            finally
            {
                Screen.FadeOut(300); Script.Wait(350);
                if (car.Exists()) { try { RestoreSnapshot(); } catch { } vehicles.Capture(); Function.Call(Hash.FREEZE_ENTITY_POSITION, car, false); }
                saves.Save();
                Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0);
                Function.Call(Hash.DISPLAY_RADAR, true);
                if (cam != null) { cam.IsActive = false; cam.Delete(); }
                Screen.FadeIn(400);
            }
        }

        // ------------------------------------------------------------------ menus
        // ------------------------------------------------------------------ installed snapshot & previews
        // Everything you highlight is shown on the car (and in the stat card as green/red against what is fitted);
        // only buying makes it stick. The snapshot is what is actually installed.
        private readonly Dictionary<int, int> instMods = new Dictionary<int, int>();
        private readonly HashSet<int> instToggles = new HashSet<int>();
        private VehicleWheelType instWheelType; private int instTint;
        private int pvCompound = -1, pvEcu = -1, pvNos = -1;

        private void TakeSnapshot()
        {
            instMods.Clear(); instToggles.Clear();
            for (int t = 0; t <= 48; t++) if (t < 17 || t > 22) instMods[t] = Function.Call<int>(Hash.GET_VEHICLE_MOD, car, t);
            foreach (int t in new[] { 18, 20, 22 }) if (Function.Call<bool>(Hash.IS_TOGGLE_MOD_ON, car, t)) instToggles.Add(t);
            instWheelType = car.Mods.WheelType; instTint = Function.Call<int>(Hash.GET_VEHICLE_WINDOW_TINT, car);
        }

        private void RestoreSnapshot()
        {
            car.Mods.WheelType = instWheelType;
            foreach (var kv in instMods) if (Function.Call<int>(Hash.GET_VEHICLE_MOD, car, kv.Key) != kv.Value) Function.Call(Hash.SET_VEHICLE_MOD, car, kv.Key, kv.Value, kv.Key == 23 && rec.LetteredTyres);
            foreach (int t in new[] { 18, 20, 22 }) Function.Call(Hash.TOGGLE_VEHICLE_MOD, car, t, instToggles.Contains(t));
            Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, car, instTint);
            Function.Call(Hash.SET_DRIFT_TYRES, car, rec.TyreCompound == 4);
            pvCompound = pvEcu = pvNos = -1;
        }

        // ------------------------------------------------------------------ tabs (GTA VI mod shop layout)
        private static readonly string[] TabNames = { "PRO BUILDS", "COSMETICS", "BODYWORK", "PERFORMANCE", "WHEELS", "INTERIOR" };
        private Menu[] tabs; private int curTab; private int shopCardUntil;

        private void BuildTabs()
        {
            tabs = new[] { ProBuilds(), Cosmetics(), Body(), Performance(), Wheels(), Interior() };
            for (int i = 0; i < tabs.Length; i++) { tabs[i].Tab = i; tabs[i].OnBack = () => done = true; }
        }

        private void OpenTab(int t)
        {
            RestoreSnapshot();
            curTab = (t + tabs.Length) % tabs.Length;
            menus.Open(tabs[curTab]);
            menus.CustomRenderer = DrawShop;
        }

        private void TabInput()
        {
            if (menus.Depth != 1) return;
            bool q = Game.IsKeyPressed(Keys.Q), e = Game.IsKeyPressed(Keys.E);
            if (UI.Input.JustPressed(Control.FrontendLb) || (q && !prevQ)) { OpenTab(curTab - 1); Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true); }
            if (UI.Input.JustPressed(Control.FrontendRb) || (e && !prevE)) { OpenTab(curTab + 1); Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true); }
            prevQ = q; prevE = e;
        }
        private bool prevQ, prevE;

        /// <summary>Specialty discount: -15 % on the shop's specialty tab.</summary>
        private long Disc(long price, int tab) { return w.DiscountTab == tab ? PricingEngine.Round(price * 0.85) : price; }

        private Menu Cosmetics()
        {
            var m = new Menu("Cosmetics", Title());
            m.OnOpen = () => { focus = "default"; curTab = 1; };
            if (Condition() < 99)
                m.Add(new MenuItem("Repair", () => Repair()) { Value = () => Condition() >= 99 ? "~g~DONE" : "$" + RepairCost().ToString("N0"), Description = () => "Condition " + Condition() + "%." });
            m.Add(new MenuItem("Respray", () => menus.Push(Respray())));
            if (Function.Call<int>(Hash.GET_VEHICLE_LIVERY_COUNT, car) > 0) m.Add(new MenuItem("Liveries", () => menus.Push(Livery())));
            m.Add(new MenuItem("Window tint", () => menus.Push(Tint())));
            m.Add(Toggle("Xenon lights", 22, Disc(1200, 1)));
            m.Add(new MenuItem("Plates", () => menus.Push(Plates())));
            m.Add(new MenuItem("Build sheet", () => menus.Push(BuildSheet())) { Description = () => "Spent on this car: $" + rec.Stats.CustomizationSpent.ToString("N0") + ", " + rec.Stats.ModificationCount + " modifications." });
            return m;
        }

        private Menu Plates()
        {
            string[] names = { "Blue on white 1", "Yellow on black", "Yellow on blue", "Blue on white 2", "Blue on white 3", "Yankton" };
            int installed = Function.Call<int>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT_INDEX, car);
            var m = new Menu("Plates", Title()) { IsShopList = true };
            m.OnOpen = () => focus = "rear";
            m.OnHighlight = i => Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT_INDEX, car, i);
            m.OnBack = () => { Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT_INDEX, car, installed); menus.Pop(); };
            long price = Disc(400, 1);
            for (int i = 0; i < names.Length; i++)
            {
                int t = i;
                m.Add(new MenuItem(names[t], () => { if (t == installed || !Pay(price, "plates")) return; installed = t; Bought("Plates: " + names[t], price); })
                { Value = () => t == installed ? "~g~CURRENT" : "$" + price.ToString("N0") });
            }
            m.Selected = Math.Max(0, Math.Min(names.Length - 1, installed));
            return m;
        }

        private Menu Interior()
        {
            var m = new Menu("Interior", Title());
            m.OnOpen = () => { focus = "interior"; curTab = 5; };
            var names = new Dictionary<int, string> { { 27, "Trim" }, { 28, "Ornaments" }, { 29, "Dashboard" }, { 30, "Dials" }, { 31, "Door speakers" }, { 32, "Seats" }, { 33, "Steering wheel" }, { 34, "Shifter" }, { 35, "Plaques" }, { 36, "Speakers" }, { 37, "Trunk" } };
            foreach (var kv in names)
            {
                if (Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, car, kv.Key) <= 0) continue;
                AddModMenu(m, kv.Value, kv.Key, 0.007, "interior", cosmetic: true);
            }
            m.Add(new MenuItem("Interior colour", () => menus.Push(ColorList("Interior", () => { var o = new OutputArgument(); Function.Call(Hash.GET_VEHICLE_EXTRA_COLOUR_5, car, o); return o.GetResult<int>(); }, c => Function.Call(Hash.SET_VEHICLE_EXTRA_COLOUR_5, car, c), 600))));
            m.Add(new MenuItem("Dashboard colour", () => menus.Push(ColorList("Dashboard", () => { var o = new OutputArgument(); Function.Call(Hash.GET_VEHICLE_EXTRA_COLOUR_6, car, o); return o.GetResult<int>(); }, c => Function.Call(Hash.SET_VEHICLE_EXTRA_COLOUR_6, car, c), 500))));
            return m;
        }

        // ------------------------------------------------------------------ pro builds
        private sealed class Build { public string Name, Desc; public int Engine, Brakes, Gearbox, Susp, Ecu, Nos, Compound = -1, Tint = -1; public bool Turbo, Xenon; public VehicleWheelType? Wheels; }

        private List<Build> Builds()
        {
            var l = new List<Build>
            {
                new Build { Name = "Street Build", Desc = "Engine 2, brakes 2, gearbox 2, sport tyres, light tint. A quick, honest daily.", Engine = 2, Brakes = 2, Gearbox = 2, Susp = 1, Compound = 1, Tint = 3 },
                new Build { Name = "Performance Build", Desc = "Engine 3, brakes 3, gearbox 3, suspension 2, turbo, semi-slicks, ECU stage 1.", Engine = 3, Brakes = 3, Gearbox = 3, Susp = 2, Turbo = true, Compound = 2, Ecu = 1 },
            };
            switch (w.Specialty)
            {
                case "Retro": l.Add(new Build { Name = "Retro Build", Desc = "Period wheels, chrome-friendly tint, engine 2 and a sorted chassis.", Engine = 2, Brakes = 2, Susp = 1, Tint = 4, Wheels = VehicleWheelType.Muscle }); break;
                case "Luxury": l.Add(new Build { Name = "Executive Build", Desc = "High-end wheels, limo tint, xenons, smooth gearbox and brakes.", Brakes = 3, Gearbox = 3, Xenon = true, Tint = 5, Wheels = VehicleWheelType.HighEnd }); break;
                case "Tuner": l.Add(new Build { Name = "Street Racer Build", Desc = "Tuner wheels, turbo, engine 3, drift compound, xenons, dark tint.", Engine = 3, Gearbox = 2, Turbo = true, Compound = 4, Xenon = true, Tint = 2, Wheels = VehicleWheelType.Tuner }); break;
                case "Racing": l.Add(new Build { Name = "Track Build", Desc = "Everything maxed, race slicks, ECU stage 3, nitrous stage 1.", Engine = 4, Brakes = 3, Gearbox = 3, Susp = 3, Turbo = true, Compound = 3, Ecu = 3, Nos = 1 }); break;
                case "Muscle": l.Add(new Build { Name = "Muscle Build", Desc = "Engine maxed, turbo, ECU stage 2, muscle wheels. Straight-line monster.", Engine = 4, Gearbox = 2, Turbo = true, Ecu = 2, Wheels = VehicleWheelType.Muscle }); break;
                case "Off-road": l.Add(new Build { Name = "Overland Build", Desc = "Off-road wheels, engine 2, brakes 2, stock-height suspension for travel.", Engine = 2, Brakes = 2, Gearbox = 1, Wheels = VehicleWheelType.Offroad }); break;
                case "Lowrider": l.Add(new Build { Name = "Lowrider Build", Desc = "Lowrider wheels, lowered suspension, xenons, limo tint.", Susp = 4, Xenon = true, Tint = 5, Wheels = VehicleWheelType.Lowrider }); break;
            }
            return l;
        }

        private long BuildPrice(Build b)
        {
            long p = 0;
            Func<int, double, long> perf = (lv, f) => lv <= 0 ? 0 : PricingEngine.Round((800 + bp * f) * lv);
            p += perf(b.Engine, 0.06) + perf(b.Brakes, 0.03) + perf(b.Gearbox, 0.045) + perf(b.Susp, 0.03);
            if (b.Turbo) p += 2500 + bp * 8 / 100;
            if (b.Ecu > 0) p += PricingEngine.Round((1500 + bp * 0.05) * b.Ecu);
            if (b.Nos > 0) p += PricingEngine.Round((4000 + bp * 0.03) * b.Nos);
            if (b.Compound > 0) p += b.Compound == 4 ? 3000 : PricingEngine.Round(1500 * b.Compound + bp * 0.012 * b.Compound);
            if (b.Wheels.HasValue) p += PricingEngine.Round(500 + bp * 0.008);
            if (b.Xenon) p += 1200; if (b.Tint >= 0) p += 300;
            return Disc(PricingEngine.Round(p * 0.9), 0);
        }

        private Menu ProBuilds()
        {
            var m = new Menu("Pro Builds", Title()) { IsShopList = true };
            m.OnOpen = () => { focus = "default"; curTab = 0; };
            foreach (var b in Builds())
            {
                var bb = b;
                m.Add(new MenuItem(bb.Name, () =>
                {
                    long price = BuildPrice(bb);
                    RestoreSnapshot();
                    if (!Pay(price, bb.Name)) return;
                    ApplyBuild(bb, false);
                    Bought(bb.Name, price);
                })
                {
                    Value = () => "$" + BuildPrice(bb).ToString("N0"), Description = () => bb.Desc + " 10% cheaper than buying the parts one by one.",
                    OnPreview = () => { RestoreSnapshot(); ApplyBuild(bb, true); },
                    OnUnpreview = () => RestoreSnapshot()
                });
            }
            return m;
        }

        private void ApplyBuild(Build b, bool preview)
        {
            Action<int, int> mod = (type, lv) => { if (lv <= 0) return; int n = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, car, type); if (n > 0) Function.Call(Hash.SET_VEHICLE_MOD, car, type, Math.Min(n - 1, lv - 1), false); };
            mod(11, b.Engine); mod(12, b.Brakes); mod(13, b.Gearbox); mod(15, b.Susp);
            if (b.Turbo) Function.Call(Hash.TOGGLE_VEHICLE_MOD, car, 18, true);
            if (b.Xenon) Function.Call(Hash.TOGGLE_VEHICLE_MOD, car, 22, true);
            if (b.Tint >= 0) Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, car, b.Tint);
            if (preview)
            {
                if (b.Ecu > rec.EcuLevel) pvEcu = b.Ecu;
                if (b.Nos > rec.NosLevel) pvNos = b.Nos;
                if (b.Compound >= 0) { pvCompound = b.Compound; Function.Call(Hash.SET_DRIFT_TYRES, car, b.Compound == 4); }
            }
            else
            {
                if (b.Ecu > rec.EcuLevel) { rec.EcuLevel = b.Ecu; Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, car, 1f + 0.06f * b.Ecu); }
                if (b.Nos > rec.NosLevel) rec.NosLevel = b.Nos;
                if (b.Compound >= 0) { rec.TyreCompound = b.Compound; Function.Call(Hash.SET_DRIFT_TYRES, car, b.Compound == 4); }
            }
            if (b.Wheels.HasValue && car.Mods.AllowedWheelTypes.Contains(b.Wheels.Value))
            {
                car.Mods.WheelType = b.Wheels.Value;
                int n = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, car, 23);
                if (n > 0) Function.Call(Hash.SET_VEHICLE_MOD, car, 23, new Random(car.Model.Hash).Next(n), rec.LetteredTyres);
            }
        }

        private Menu Performance()
        {
            var m = new Menu("Performance", Title());
            m.Add(new MenuItem("Tyre compound", () => menus.Push(Compounds())) { Value = () => FitmentManager.CompoundNames[rec.TyreCompound] });
            m.OnOpen = () => { focus = "default"; curTab = 3; };
            AddModMenu(m, "Engine", 11, 0.06, "front");
            AddModMenu(m, "Brakes", 12, 0.03, "side");
            AddModMenu(m, "Transmission", 13, 0.045, "default");
            AddModMenu(m, "Suspension", 15, 0.03, "side");
            m.Add(Toggle("Turbo", 18, Disc(2500 + bp * 8 / 100, 3)));
            m.Add(new MenuItem("Nitrous kit", () => menus.Push(Nitrous())) { Value = () => rec.NosLevel == 0 ? "None" : "Stage " + rec.NosLevel, Description = () => "Hold Sprint (keyboard) or the boost button (controller) to fire. Bigger stages: more power, longer burn, faster refill." });
            m.Add(new MenuItem("Race parts", () => menus.Push(RaceParts())) { Value = () => (rec.Parts != null ? rec.Parts.Count : 0) + " / " + Setup.Parts.Length, Description = () => "Coilovers, anti-roll bars, differential, gearbox, brakes, aero, weight reduction, drivetrain swaps, anti-lag, straight pipe. Unlocks the matching tuning in Midnight Hub > Garage > Tuning." });
            m.Add(new MenuItem("Engine swap (sound)", () => menus.Push(EngineSwap())) { Value = () => SwapName(rec.EngineSound), Description = () => "Swap in a different engine note. Preview revs it in the bay before you buy." });
            m.Add(new MenuItem("ECU tune (Midnight Club)", () => menus.Push(Ecu())) { Value = () => rec.EcuLevel == 0 ? "Stock" : "Stage " + rec.EcuLevel,
                Description = () => "Remapped engine management: +6% power per stage on top of the engine upgrade." });
            return m;
        }

        private Menu Nitrous()
        {
            var m = new Menu("Nitrous", Title()) { IsShopList = true };
            m.OnHighlight = i => pvNos = i;
            m.OnBack = () => { pvNos = -1; menus.Pop(); };
            m.OnOpen = () => focus = "rear";
            for (int lvl = 0; lvl <= 3; lvl++)
            {
                int l = lvl;
                long price = l == 0 ? 0 : Disc(PricingEngine.Round((4000 + bp * 0.03) * l), 3);
                m.Add(new MenuItem(l == 0 ? "Remove kit" : "Stage " + l + (l == 1 ? " (street)" : l == 2 ? " (sport)" : " (race)"), () =>
                {
                    if (rec.NosLevel == l) return;
                    bool upgrade = l > rec.NosLevel;
                    if (upgrade && !Pay(price, "nitrous stage " + l)) return;
                    rec.NosLevel = l; Bought(l == 0 ? "Nitrous removed" : "Nitrous stage " + l, upgrade ? price : 0);
                }) { Value = () => rec.NosLevel == l ? "~g~INSTALLED" : (l < rec.NosLevel ? "Free" : "$" + price.ToString("N0")) });
            }
            return m;
        }

        private static readonly string[] SwapIds = { null, "sultan", "elegy2", "zr350", "comet2", "dominator", "sabregt", "banshee2", "zentorno", "openwheel1", "sandking", "voltic" };
        private static readonly string[] SwapNames = { "Stock", "Turbo inline-4", "JDM inline-6", "Rotary", "Flat-6 boxer", "American V8", "Old-school V8", "Race V8", "V12 supercar", "Formula V10", "Big-block truck", "Electric" };
        private static string SwapName(string id) { int i = Array.IndexOf(SwapIds, id); return i <= 0 ? "Stock" : SwapNames[i]; }
        private void Sound(string id)
        {
            try
            {
                Function.Call(Hash.FORCE_USE_AUDIO_GAME_OBJECT, car, id ?? rec.Model);
                car.IsEngineRunning = true;
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, car, true, true, false);
                Function.Call(Hash.PLAY_SOUND_FROM_ENTITY, -1, "Engine_Revs", car, "DLC_Tuner_Car_Meet_Test_Area_Events_Sounds", false, 0);
            }
            catch { }
        }

        private Menu RaceParts()
        {
            var m = new Menu("Race parts", Title()) { IsShopList = true };
            m.OnOpen = () => focus = "default";
            if (rec.Parts == null) rec.Parts = new List<string>();
            foreach (var pp in Setup.Parts)
            {
                var p = pp;
                long price = Disc(PricingEngine.Round(p.Price + bp * 0.015), 3);
                m.Add(new MenuItem(p.Name, () =>
                {
                    if (rec.Parts.Contains(p.Id)) { Flash("Already installed."); return; }
                    if (p.Needs != null && !rec.Parts.Contains(p.Needs)) { Flash("Needs " + Setup.Parts.First(x => x.Id == p.Needs).Name + " first."); return; }
                    if (p.Id == "als" && !(rec.Toggles != null && rec.Toggles.Contains(18))) { Flash("Anti-lag needs a turbo."); return; }
                    if (!Pay(price, p.Name)) return;
                    rec.Parts.Add(p.Id);
                    if (p.Id == "awd") rec.Parts.Remove("rwd"); if (p.Id == "rwd") rec.Parts.Remove("awd");
                    Setup.ApplyAudio(car, rec);
                    Bought(p.Name, price);
                }) { Value = () => rec.Parts.Contains(p.Id) ? "~g~INSTALLED" : "$" + price.ToString("N0") + "  ·  PI +" + p.Pi, Description = () => p.Desc });
            }
            return m;
        }

        private Menu EngineSwap()
        {
            var m = new Menu("Engine swap", Title()) { IsShopList = true };
            m.OnHighlight = i => Sound(SwapIds[i]);
            m.OnBack = () => { Sound(rec.EngineSound); menus.Pop(); };
            m.OnOpen = () => focus = "front";
            for (int k = 0; k < SwapIds.Length; k++)
            {
                int i = k;
                long price = i == 0 ? 0 : Disc(PricingEngine.Round(6000 + bp * 0.02), 3);
                m.Add(new MenuItem(SwapNames[i], () =>
                {
                    if (rec.EngineSound == SwapIds[i]) return;
                    if (i > 0 && !Pay(price, "engine swap " + SwapNames[i])) return;
                    rec.EngineSound = SwapIds[i]; Sound(rec.EngineSound);
                    Bought(i == 0 ? "Stock engine note" : "Engine swap: " + SwapNames[i], price);
                }) { Value = () => rec.EngineSound == SwapIds[i] ? "~g~INSTALLED" : i == 0 ? "Free" : "$" + price.ToString("N0") });
            }
            return m;
        }

        private Menu Ecu()
        {
            var m = new Menu("ECU tune", Title()) { IsShopList = true };
            m.OnHighlight = i => pvEcu = i;
            m.OnBack = () => { pvEcu = -1; menus.Pop(); };
            for (int lvl = 0; lvl <= 3; lvl++)
            {
                int l = lvl;
                long price = l == 0 ? 0 : Disc(PricingEngine.Round((1500 + bp * 0.05) * l), 3);
                m.Add(new MenuItem(l == 0 ? "Stock map" : "Stage " + l + (l == 3 ? "  [EXCLUSIVE]" : ""), () =>
                {
                    if (rec.EcuLevel == l) return;
                    if (l == 3 && w.Specialty != "Racing" && rec.EcuLevel < 3) { Flash("Stage 3 maps: Southside Speed Lab only."); return; }
                    bool upgrade = l > rec.EcuLevel;
                    if (upgrade && !Pay(price, "ECU stage " + l)) return;
                    rec.EcuLevel = l; Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, car, 1f + 0.06f * l);
                    Bought(l == 0 ? "ECU back to stock" : "ECU stage " + l, upgrade ? price : 0);
                }) { Value = () => rec.EcuLevel == l ? "~g~INSTALLED" : (l < rec.EcuLevel ? "Free" : "$" + price.ToString("N0")) });
            }
            return m;
        }

        private Menu Body()
        {
            var m = new Menu("Bodywork", Title());
            m.OnOpen = () => { focus = "default"; curTab = 2; };
            var names = new Dictionary<int, string> { { 0, "Spoiler" }, { 1, "Front bumper" }, { 2, "Rear bumper" }, { 3, "Side skirts" }, { 4, "Exhaust" }, { 5, "Roll cage" },
                { 6, "Grille" }, { 7, "Hood" }, { 8, "Left fender" }, { 9, "Right fender" }, { 10, "Roof" }, { 48, "Livery (mod)" } };
            var focusOf = new Dictionary<int, string> { { 0, "rear" }, { 1, "front" }, { 2, "rear" }, { 3, "side" }, { 4, "rear" }, { 6, "front" }, { 7, "front" }, { 8, "side" }, { 9, "side" } };
            var types = new List<int>(names.Keys);
            if (w.Bennys) for (int t = 38; t <= 46; t++) types.Add(t);   // Benny's extras (interior slots live in the Interior tab)
            foreach (int t in types.Distinct().OrderBy(x => x))
            {
                if (t == 23 || t == 24) continue;
                if (Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, car, t) <= 0) continue;
                string label = names.ContainsKey(t) ? names[t] : SlotName(t);
                string f; if (!focusOf.TryGetValue(t, out f)) f = "default";
                AddModMenu(m, label, t, t >= 25 ? 0.009 : 0.006, f, cosmetic: true);
            }
            if (m.Items.Count == 0) m.Add(new MenuItem("No body parts for this car") { Enabled = false });
            return m;
        }

        private Menu Wheels()
        {
            var m = new Menu("Wheels", Title());
            m.OnOpen = () => { focus = "side"; curTab = 4; };
            m.Add(new MenuItem("Adjust fitment", () => menus.Push(Fitment())) { Value = () => rec.FitKit ? "" : "$" + FitKitPrice().ToString("N0"),
                Description = () => "Camber, track width and tyre diameter per axle (stance). One-time kit, then adjust for free." });
            foreach (var wt in car.Mods.AllowedWheelTypes)
            {
                var type = wt;
                m.Add(new MenuItem(car.Mods.GetLocalizedWheelTypeName(type), () => menus.Push(WheelList(type))) { Value = () => car.Mods.WheelType == type ? "~g~CURRENT" : "" });
            }
            m.Add(new MenuItem("Tyre compound", () => menus.Push(Compounds())) { Value = () => FitmentManager.CompoundNames[rec.TyreCompound] });
            m.Add(new MenuItem("Lettered sidewalls", () =>
            {
                bool on = !rec.LetteredTyres;
                if (on && !Pay(750, "lettered sidewalls")) return;
                rec.LetteredTyres = on;
                int idx = Function.Call<int>(Hash.GET_VEHICLE_MOD, car, 23);
                Function.Call(Hash.SET_VEHICLE_MOD, car, 23, idx, on);
                Bought(on ? "Lettered sidewalls" : "Plain sidewalls", on ? 750 : 0);
            }) { Value = () => rec.LetteredTyres ? "~g~FITTED" : "$750", Description = () => "Custom tyres with lettered / styled sidewalls (look depends on the wheel). Select again to remove (free)." });
            m.Add(Toggle("Tyre smoke", 20, Disc(1000, 4)));
            return m;
        }

        private Menu Compounds()
        {
            var m = new Menu("Tyre compound", Title()) { IsShopList = true };
            m.OnOpen = () => focus = "side";
            m.OnHighlight = i => { pvCompound = i; Function.Call(Hash.SET_DRIFT_TYRES, car, i == 4); };
            m.OnBack = () => { pvCompound = -1; Function.Call(Hash.SET_DRIFT_TYRES, car, rec.TyreCompound == 4); menus.Pop(); };
            long[] prices = { 0, Disc(PricingEngine.Round(1500 + bp * 0.01), 4), Disc(PricingEngine.Round(3000 + bp * 0.02), 4), Disc(PricingEngine.Round(6000 + bp * 0.035), 4), Disc(3000, 4) };
            string[] desc = { "Everyday road tyres. Stock grip.", "Sticky road tyres: +5% grip.", "Track-day rubber: +10% grip.", "Full race slicks: +16% grip.", "Hard low-grip compound for drifting." };
            for (int i = 0; i < FitmentManager.CompoundNames.Length; i++)
            {
                int c = i;
                bool locked = (c == 3 && w.Specialty != "Racing") || (c == 4 && w.Specialty != "Tuner" && w.Specialty != "Racing");
                m.Add(new MenuItem(FitmentManager.CompoundNames[c] + (locked ? "" : (c >= 3 ? "  [EXCLUSIVE]" : "")), () =>
                {
                    if (rec.TyreCompound == c) return;
                    if (locked) { Flash(c == 3 ? "Race slicks: Southside Speed Lab only." : "Drift compound: Runway Tuning LSIA or Southside Speed Lab."); return; }
                    if (!Pay(prices[c], FitmentManager.CompoundNames[c] + " tyres")) return;
                    rec.TyreCompound = c;
                    Function.Call(Hash.SET_DRIFT_TYRES, car, c == 4);
                    Bought(FitmentManager.CompoundNames[c] + " tyres", prices[c]);
                }) { Value = () => rec.TyreCompound == c ? "~g~FITTED" : (prices[c] == 0 ? "Free" : "$" + prices[c].ToString("N0")), Description = () => desc[c] });
            }
            return m;
        }

        private long FitKitPrice() { return PricingEngine.Round(2500 + bp * 0.01); }

        private Menu Fitment()
        {
            var m = new Menu("Adjust fitment", Title());
            m.OnOpen = () =>
            {
                focus = "front";
                m.Items.Clear();
                if (!rec.FitKit)
                {
                    m.Add(new MenuItem("Install adjustable fitment kit", () =>
                    {
                        if (!Pay(FitKitPrice(), "fitment kit")) return;
                        rec.FitKit = true; Bought("Adjustable fitment kit", FitKitPrice());
                        m.OnOpen(); m.ResetHighlight();
                    }) { Value = () => "$" + FitKitPrice().ToString("N0"), Description = () => "Coilover tops, adjustable arms and hub spacers. After this, every adjustment is free." });
                    return;
                }
                m.Add(Adjust("Front camber", () => rec.CamberF, v => rec.CamberF = v, -15f, 5f, 0.5f, "0.0 deg", "front"));
                m.Add(Adjust("Rear camber", () => rec.CamberR, v => rec.CamberR = v, -15f, 5f, 0.5f, "0.0 deg", "rear"));
                m.Add(Adjust("Front track width", () => rec.TrackF, v => rec.TrackF = v, -8f, 15f, 0.5f, "+0.0 cm;-0.0 cm;0 cm", "front"));
                m.Add(Adjust("Rear track width", () => rec.TrackR, v => rec.TrackR = v, -8f, 15f, 0.5f, "+0.0 cm;-0.0 cm;0 cm", "rear"));
                m.Add(Adjust("Front tyre diameter", () => rec.TyreF <= 0 ? 1f : rec.TyreF, v => rec.TyreF = v, 0.85f, 1.2f, 0.01f, "0%", "front", percent: true));
                m.Add(Adjust("Rear tyre diameter", () => rec.TyreR <= 0 ? 1f : rec.TyreR, v => rec.TyreR = v, 0.85f, 1.2f, 0.01f, "0%", "rear", percent: true));
                m.Add(new MenuItem("Reset to stock", () =>
                {
                    rec.CamberF = rec.CamberR = rec.TrackF = rec.TrackR = 0f; rec.TyreF = rec.TyreR = 0f;
                    saves.IsDirty = true; Flash("Fitment reset to stock.");
                }));
            };
            return m;
        }

        private MenuItem Adjust(string label, Func<float> get, Action<float> set, float min, float max, float step, string fmt, string camFocus, bool percent = false)
        {
            return new MenuItem(label)
            {
                Value = () => percent ? (get() * 100f).ToString("0") + "%" : get().ToString(fmt),
                OnAdjust = dir =>
                {
                    float v = (float)Math.Round((get() + dir * step) / step) * step;
                    set(Math.Max(min, Math.Min(max, v)));
                    saves.IsDirty = true; focus = camFocus;
                },
                Description = () => "Left / right to adjust (free). Changes show live."
            };
        }

        private Menu WheelList(VehicleWheelType type)
        {
            var origType = car.Mods.WheelType;
            int origIdx = Function.Call<int>(Hash.GET_VEHICLE_MOD, car, 23);
            car.Mods.WheelType = type;
            int count = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, car, 23);
            var m = new Menu(car.Mods.GetLocalizedWheelTypeName(type), Title()) { IsShopList = true };
            long price = Disc(PricingEngine.Round(500 + bp * 0.008), 4);
            var opts = Enumerable.Range(-1, count + 1).ToList();
            m.OnHighlight = i => { car.Mods.WheelType = type; Function.Call(Hash.SET_VEHICLE_MOD, car, 23, opts[i], rec.LetteredTyres); };
            m.OnBack = () => { car.Mods.WheelType = origType; Function.Call(Hash.SET_VEHICLE_MOD, car, 23, origIdx, rec.LetteredTyres); menus.Pop(); };
            foreach (var idx in opts)
            {
                int i = idx;
                m.Add(new MenuItem(i < 0 ? "Stock wheels" : ModName(23, i), () =>
                {
                    if (origType == type && origIdx == i) return;
                    if (!Pay(price, "wheels")) return;
                    origType = type; origIdx = i; Bought("Wheels: " + (i < 0 ? "stock" : ModName(23, i)), price);
                }) { Value = () => origType == type && origIdx == i ? "~g~INSTALLED" : "$" + price.ToString("N0") });
            }
            return m;
        }

        private Menu Respray()
        {
            var m = new Menu("Respray", Title());
            m.OnOpen = () => focus = "default";
            m.Add(new MenuItem("Primary colour", () => menus.Push(ColorList("Primary", () => (int)car.Mods.PrimaryColor, c => car.Mods.PrimaryColor = (VehicleColor)c, 500))));
            m.Add(new MenuItem("Secondary colour", () => menus.Push(ColorList("Secondary", () => (int)car.Mods.SecondaryColor, c => car.Mods.SecondaryColor = (VehicleColor)c, 400))));
            m.Add(new MenuItem("Pearlescent", () => menus.Push(ColorList("Pearlescent", () => (int)car.Mods.PearlescentColor, c => car.Mods.PearlescentColor = (VehicleColor)c, 800))));
            m.Add(new MenuItem("Wheel colour", () => menus.Push(ColorList("Wheel colour", () => (int)car.Mods.RimColor, c => car.Mods.RimColor = (VehicleColor)c, 400))));
            return m;
        }

        private Menu ColorList(string what, Func<int> get, Action<int> set, long price)
        {
            int installed = get();
            var m = new Menu(what, Title()) { IsShopList = true };
            price = Disc(price, 1);
            var colors = Enum.GetValues(typeof(VehicleColor)).Cast<VehicleColor>().Select(c => (int)c).Distinct().OrderBy(c => c).Where(c => c >= 0 && c <= 160).ToList();
            m.OnHighlight = i => set(colors[i]);
            m.OnBack = () => { set(installed); menus.Pop(); };
            foreach (var c in colors)
            {
                int col = c;
                m.Add(new MenuItem(Pretty(((VehicleColor)col).ToString()), () =>
                {
                    if (col == installed) return;
                    if (!Pay(price, what)) return;
                    installed = col; Bought(what + ": " + Pretty(((VehicleColor)col).ToString()), price);
                    if (what == "Primary") Police.PursuitManager.CoolDownIdentity(rec, 1f);
                }) { Value = () => col == installed ? "~g~CURRENT" : "$" + price.ToString("N0") });
            }
            m.Selected = Math.Max(0, colors.IndexOf(installed));
            return m;
        }

        private Menu Tint()
        {
            string[] names = { "None", "Pure black", "Dark smoke", "Light smoke", "Stock", "Limo", "Green" };
            int installed = Function.Call<int>(Hash.GET_VEHICLE_WINDOW_TINT, car);
            if (installed < 0) installed = 0;
            var m = new Menu("Window tint", Title()) { IsShopList = true };
            m.OnHighlight = i => Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, car, i);
            m.OnBack = () => { Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, car, installed); menus.Pop(); };
            for (int i = 0; i < names.Length; i++)
            {
                int t = i;
                m.Add(new MenuItem(names[t], () => { if (t == installed || !Pay(300, "tint")) return; installed = t; Bought("Window tint: " + names[t], 300); })
                { Value = () => t == installed ? "~g~CURRENT" : "$300" });
            }
            return m;
        }

        private Menu Livery()
        {
            int count = Function.Call<int>(Hash.GET_VEHICLE_LIVERY_COUNT, car);
            int installed = Function.Call<int>(Hash.GET_VEHICLE_LIVERY, car);
            var m = new Menu("Livery", Title()) { IsShopList = true };
            m.OnHighlight = i => Function.Call(Hash.SET_VEHICLE_LIVERY, car, i);
            m.OnBack = () => { Function.Call(Hash.SET_VEHICLE_LIVERY, car, installed); menus.Pop(); };
            for (int i = 0; i < count; i++)
            {
                int l = i;
                m.Add(new MenuItem("Livery " + (l + 1), () => { if (l == installed || !Pay(900, "livery")) return; installed = l; Bought("Livery " + (l + 1), 900); })
                { Value = () => l == installed ? "~g~CURRENT" : "$900" });
            }
            return m;
        }

        private Menu BuildSheet()
        {
            var m = new Menu("Build sheet", Title());
            m.Add(new MenuItem("Spent on modifications") { Value = () => "$" + rec.Stats.CustomizationSpent.ToString("N0") });
            m.Add(new MenuItem("Modifications") { Value = () => rec.Stats.ModificationCount.ToString() });
            m.Add(new MenuItem("Repairs") { Value = () => rec.Stats.Repairs.ToString() });
            foreach (var h in Enumerable.Reverse(rec.BuildHistory).Take(20)) { var line = h; m.Add(new MenuItem(line) { Enabled = false }); }
            return m;
        }

        // ------------------------------------------------------------------ generic mod list
        private void AddModMenu(Menu parent, string label, int type, double factor, string camFocus, bool cosmetic = false)
        {
            int count = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, car, type);
            if (count <= 0) return;
            parent.Add(new MenuItem(label, () => menus.Push(ModList(label, type, count, factor, camFocus, cosmetic)))
            { Value = () => { int i = Function.Call<int>(Hash.GET_VEHICLE_MOD, car, type); return i < 0 ? "Stock" : (cosmetic ? (i + 1) + "/" + count : "Level " + (i + 1)); } });
        }

        private Menu ModList(string label, int type, int count, double factor, string camFocus, bool cosmetic)
        {
            int installed = Function.Call<int>(Hash.GET_VEHICLE_MOD, car, type);
            var m = new Menu(label, Title()) { IsShopList = true };
            m.OnOpen = () => focus = camFocus;
            var opts = Enumerable.Range(-1, count + 1).ToList();
            int tab = curTab;
            Func<int, long> price = i =>
            {
                if (i < 0) return 0;
                if (cosmetic) return Disc(PricingEngine.Round((200 + bp * factor) * (1 + i * 0.15)), tab);
                return Disc(PricingEngine.Round((800 + bp * factor) * (i + 1)), tab);
            };
            m.OnHighlight = i => Function.Call(Hash.SET_VEHICLE_MOD, car, type, opts[i], false);
            m.OnBack = () => { Function.Call(Hash.SET_VEHICLE_MOD, car, type, installed, false); menus.Pop(); };
            foreach (var o in opts)
            {
                int i = o;
                string name = i < 0 ? "Stock" : (cosmetic ? ModName(type, i) : PerfName(type, i));
                m.Add(new MenuItem(name, () =>
                {
                    if (i == installed) return;
                    long p = price(i);
                    if (p > 0 && !Pay(p, label)) return;
                    installed = i; Bought(label + ": " + name, p);
                }) { Value = () => i == installed ? "~g~INSTALLED" : (price(i) == 0 ? "Free" : "$" + price(i).ToString("N0")) });
            }
            m.Selected = installed + 1;
            return m;
        }

        private MenuItem Toggle(string label, int type, long price)
        {
            Func<bool> inst = () => instToggles.Contains(type);
            return new MenuItem(label, () =>
            {
                if (inst()) { Function.Call(Hash.TOGGLE_VEHICLE_MOD, car, type, false); Bought("Removed " + label, 0); return; }
                if (!Pay(price, label)) { Function.Call(Hash.TOGGLE_VEHICLE_MOD, car, type, false); return; }
                Function.Call(Hash.TOGGLE_VEHICLE_MOD, car, type, true); Bought(label, price);
            })
            {
                Value = () => inst() ? "~g~FITTED" : "$" + price.ToString("N0"), Description = () => "Select again to remove (free).",
                OnPreview = () => { if (!inst()) Function.Call(Hash.TOGGLE_VEHICLE_MOD, car, type, true); },
                OnUnpreview = () => Function.Call(Hash.TOGGLE_VEHICLE_MOD, car, type, inst())
            };
        }

        // ------------------------------------------------------------------ repair / money
        private int Condition() { return (int)Math.Round(Math.Max(0, Math.Min(100, (car.EngineHealth + car.BodyHealth) / 20f))); }
        private long RepairCost() { return PricingEngine.Round(Math.Max(0, (2000 - car.EngineHealth - car.BodyHealth) / 2000.0) * (bp * 0.06 + 400)); }

        private void Repair()
        {
            if (Condition() >= 99) return;
            long c = RepairCost();
            if (!Pay(c, "repair")) return;
            Function.Call(Hash.SET_VEHICLE_FIXED, car); Function.Call(Hash.SET_VEHICLE_DEFORMATION_FIXED, car);
            car.EngineHealth = 1000f; car.BodyHealth = 1000f; car.DirtLevel = 0f;
            rec.Stats.Repairs++;
            Bought("Full repair", c, countAsMod: false);
        }

        private bool Pay(long price, string what)
        {
            if (price <= 0) return true;
            string tx = "ws:" + rec.Id + ":" + rec.Stats.ModificationCount + ":" + rec.Stats.Repairs + ":" + what;
            if (!money.TrySpend(price, "workshop", what + " (" + Title() + ")", tx, saveNow: false)) { Flash("~r~Not enough cash~s~ ($" + price.ToString("N0") + ")."); return false; }
            return true;
        }

        private void Bought(string what, long price, bool countAsMod = true)
        {
            pvCompound = pvEcu = pvNos = -1;
            TakeSnapshot();
            if (countAsMod) rec.Stats.ModificationCount++;
            rec.Stats.CustomizationSpent += price;
            rec.BuildHistory.Add(DateTime.Now.ToString("MM-dd") + "  " + what + (price > 0 ? "  $" + price.ToString("N0") : ""));
            if (rec.BuildHistory.Count > 60) rec.BuildHistory.RemoveAt(0);
            vehicles.Capture();
            saves.Save();
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "PURCHASE", "HUD_LIQUOR_STORE_SOUNDSET", true);
            Flash("~g~" + what);
            if (price > 0) EventBus.Publish(new WorkshopPurchaseEvent(what));
        }

        // ------------------------------------------------------------------ names, camera, HUD
        private string Title() { return !string.IsNullOrEmpty(rec.Nickname) ? rec.Nickname : info != null ? info.Name : rec.Model; }

        private string ModName(int type, int i)
        {
            var label = Function.Call<string>(Hash.GET_MOD_TEXT_LABEL, car, type, i);
            if (!string.IsNullOrEmpty(label))
            {
                var s = Game.GetLocalizedString(label);
                if (!string.IsNullOrEmpty(s) && s != "NULL") return s;
            }
            return "Option " + (i + 1);
        }

        private string PerfName(int type, int i)
        {
            string n = ModName(type, i);
            return n.StartsWith("Option ") ? "Level " + (i + 1) : n;
        }

        private string SlotName(int type)
        {
            var label = Function.Call<string>(Hash.GET_MOD_SLOT_NAME, car, type);
            if (!string.IsNullOrEmpty(label)) { var s = Game.GetLocalizedString(label); if (!string.IsNullOrEmpty(s) && s != "NULL") return s; }
            return "Benny's part " + type;
        }

        private static string Pretty(string enumName) { return Regex.Replace(enumName, "(?<=[a-z])(?=[A-Z0-9])", " "); }

        private void Orbit()
        {
            float dt = Game.LastFrameTime;
            float radius = 5.2f, height = 1.3f, target = orbit;
            switch (focus)
            {
                case "front": target = 25f; radius = 4.3f; height = 1.0f; break;
                case "rear": target = 200f; radius = 4.6f; height = 1.2f; break;
                case "side": target = 95f; radius = 3.8f; height = 0.6f; break;
                default: target = orbit + dt * 10f; break;
            }
            orbit = focus == "default" ? target % 360f : orbit + ((((target - orbit) % 360f) + 540f) % 360f - 180f) * Math.Min(1f, dt * 3f);
            if (focus == "interior")
            {
                var ip = car.GetOffsetPosition(new Vector3(0.55f, -0.75f, 0.62f));
                cam.Position = Vector3.Lerp(cam.Position, ip, Math.Min(1f, dt * 4f));
                cam.PointAt(car.GetOffsetPosition(new Vector3(-0.35f, 0.9f, 0.35f)));
                Function.Call(Hash.SET_ENTITY_LOCALLY_INVISIBLE, Game.Player.Character);
                UI.Studio.Light(car, ip);
                return;
            }
            double r = orbit * Math.PI / 180.0;
            var local = new Vector3((float)Math.Sin(r) * radius, (float)Math.Cos(r) * radius, height);
            var cp = car.GetOffsetPosition(local);
            UI.Studio.Frame(cam, cp, car.Position + new Vector3(0, 0, 0.15f), 0.12f, 0.06f);
            UI.Studio.Light(car, cp);
        }

        // ------------------------------------------------------------------ GTA VI-style shop UI
        // Top left: shop name + tab bar (LB/RB), or "TAB /" + part name inside a category. Left: list with a white
        // highlighted row. Bottom left: price, part name, n / m, ownership. Bottom right: vehicle stat card with
        // live preview deltas and road / off-road rating. Top right: cash, and the shop card on arrival.
        private float[] baseStats; private Menu statsFor;
        private float listSel = -1f;

        private static readonly Color White = Color.FromArgb(255, 245, 245, 245), Dim = Color.FromArgb(255, 170, 170, 175), Ink = Color.FromArgb(255, 18, 18, 22);
        private static readonly Font Cond = Font.ChaletComprimeCologne;

        private void DrawShop(Menu m)
        {
            m.Clamp();
            var dark = Color.FromArgb(255, 10, 10, 14);
            UI.Ds.HGrad(0f, 0f, 0.5f, 1f, UI.Ds.A(dark, 0.72f), UI.Ds.A(dark, 0f), 20);
            UI.Studio.Fade(true, 0.22f, 150);
            float x = 0.045f, y = 0.055f;
            bool root = menus.Depth == 1;
            if (root)
            {
                UI.Ds.T(w.Name.ToUpperInvariant(), x, y, 0.78f, White, Cond);
                // tab bar
                float tx = x, ty = y + 0.058f, th = 0.03f;
                Key(tx, ty + 0.004f, "LB"); tx += UI.Ds.Px(0.032f);
                for (int i = 0; i < TabNames.Length; i++)
                {
                    float tw = UI.Ds.Width(TabNames[i], 0.36f, Cond) + UI.Ds.Px(0.02f);
                    bool sel = i == curTab;
                    UI.Ds.Rect(tx, ty, tw, th, sel ? White : Color.FromArgb(150, 20, 20, 26));
                    UI.Ds.T(TabNames[i], tx + tw / 2, ty + 0.0015f, 0.36f, sel ? Ink : (i == w.DiscountTab ? UI.Ds.Amber : Dim), Cond, Alignment.Center);
                    tx += tw + UI.Ds.Px(0.004f);
                }
                Key(tx + UI.Ds.Px(0.004f), ty + 0.004f, "RB");
                y = ty + 0.06f;
            }
            else
            {
                string crumb = TabNames[Math.Max(0, Math.Min(5, curTab))] + " /";
                UI.Ds.T(crumb, x, y, 0.42f, Dim, Cond);
                UI.Ds.T((m.Title ?? "").ToUpperInvariant(), x, y + 0.03f, 0.82f, White, Cond);
                y += 0.11f;
            }
            // list
            float lw = 0.215f, rh = 0.043f;
            int first = m.Scroll, n = Math.Min(m.VisibleRows, m.Items.Count - first);
            float target = y + (m.Selected - first) * rh;
            listSel = listSel < 0 ? target : UI.Ds.Damp(listSel, target, 28f);
            for (int i = 0; i < n; i++)
            {
                var it = m.Items[first + i]; float ry = y + i * rh;
                UI.Ds.HGrad(x, ry, lw, rh - 0.003f, Color.FromArgb(120, 30, 30, 36), Color.FromArgb(0, 30, 30, 36), 10);
                UI.Ds.Rect(x, ry + rh - 0.003f, lw, 0.001f, Color.FromArgb(60, 255, 255, 255));
            }
            if (m.Items.Count > 0) UI.Ds.HGrad(x, listSel, lw, rh - 0.003f, Color.FromArgb(240, 245, 245, 245), Color.FromArgb(200, 225, 225, 228), 8);
            for (int i = 0; i < n; i++)
            {
                var it = m.Items[first + i]; bool sel = first + i == m.Selected; float ry = y + i * rh;
                bool onBar = sel && Math.Abs(listSel - target) < rh * 0.5f;
                string label = Clean(it.Label).Replace("  [EXCLUSIVE]", "");
                var c = onBar ? Ink : !it.Enabled ? Color.FromArgb(255, 110, 110, 115) : White;
                UI.Ds.T(UI.Ds.Fit(label, 0.36f, lw - UI.Ds.Px(0.05f)), x + UI.Ds.Px(0.009f), ry + 0.0075f, 0.36f, c, Font.ChaletLondon);
                if (it.Label.Contains("[EXCLUSIVE]")) UI.Ds.Rect(x + lw - UI.Ds.Px(0.012f), ry + 0.014f, UI.Ds.Px(0.006f), 0.012f, UI.Ds.Cyan);
                if (!m.IsShopList && it.Value != null)
                {
                    string v = Clean(it.Value());
                    if (!string.IsNullOrEmpty(v)) UI.Ds.T(UI.Ds.Fit(v, 0.27f, lw * 0.4f), x + lw - UI.Ds.Px(0.008f), ry + 0.011f, 0.27f, onBar ? Ink : Dim, Font.ChaletLondon, Alignment.Right);
                }
            }
            if (m.Items.Count > m.VisibleRows || !m.IsShopList)
                UI.Ds.T((m.Selected + 1) + "  /  " + m.Items.Count, x, y + n * rh + 0.012f, 0.36f, Dim, Cond);
            // part detail (bottom left)
            var cur = m.Items.Count > 0 ? m.Items[m.Selected] : null;
            if (m.IsShopList && cur != null)
            {
                string v = cur.Value != null ? Clean(cur.Value()) : "";
                bool owned = v.Contains("INSTALLED") || v.Contains("CURRENT") || v.Contains("FITTED");
                bool excl = cur.Label.Contains("[EXCLUSIVE]"), disc = w.DiscountTab == curTab;
                float dy = 0.70f;
                if (excl || disc) { UI.Ds.Rect(x, dy - 0.04f, UI.Ds.Width(excl ? "EXCLUSIVE" : "SPECIALTY -15%", 0.34f, Cond) + UI.Ds.Px(0.014f), 0.03f, UI.Ds.Cyan); UI.Ds.T(excl ? "EXCLUSIVE" : "SPECIALTY -15%", x + UI.Ds.Px(0.007f), dy - 0.0385f, 0.34f, Ink, Cond); }
                UI.Ds.T(owned ? "OWNED" : (v.StartsWith("$") ? v : (v.Length > 0 ? v.ToUpperInvariant() : "")), x, dy, 0.95f, White, Cond);
                UI.Ds.T(Clean(cur.Label).Replace("  [EXCLUSIVE]", "").ToUpperInvariant(), x, dy + 0.07f, 0.55f, White, Cond);
                UI.Ds.T((m.Selected + 1) + "  /  " + m.Items.Count, x, dy + 0.115f, 0.36f, Dim, Cond);
                UI.Ds.Rect(x, dy + 0.155f, 0.3f, 0.001f, Color.FromArgb(80, 255, 255, 255));
                UI.Ds.T(owned ? "Installed" : "Not owned", x + UI.Ds.Px(0.004f), dy + 0.165f, 0.32f, Dim, Font.ChaletLondon);
                if (cur.Description != null) { var d = Clean(cur.Description()); if (!string.IsNullOrEmpty(d)) UI.Ds.T(d, x + UI.Ds.Px(0.004f), dy + 0.195f, 0.27f, Dim, Font.ChaletLondon, Alignment.Left, 0.32f); }
            }
            else if (cur != null && cur.Description != null)
            {
                var d = Clean(cur.Description());
                if (!string.IsNullOrEmpty(d)) UI.Ds.T(d, x, 0.86f, 0.3f, Dim, Font.ChaletLondon, Alignment.Left, 0.3f);
            }
            // cash (top right)
            UI.Ds.T("$" + saves.Data.Cash.ToString("N0"), 0.955f, 0.045f, 0.62f, White, Cond, Alignment.Right);
            // shop card (top right, on arrival)
            if (Game.GameTime < shopCardUntil)
            {
                float a = UI.Ds.Clamp01((shopCardUntil - Game.GameTime) / 500f), cx = 0.70f, cw = 0.255f, cy = 0.1f;
                UI.Ds.Rect(cx, cy, cw, 0.14f, UI.Ds.A(Color.FromArgb(235, 22, 24, 34), a));
                UI.Ds.T(w.Name.ToUpperInvariant(), cx + UI.Ds.Px(0.012f), cy + 0.008f, 0.5f, UI.Ds.A(White, a), Cond);
                UI.Ds.T(w.Description ?? "", cx + UI.Ds.Px(0.012f), cy + 0.048f, 0.28f, UI.Ds.A(White, a), Font.ChaletLondon, Alignment.Left, cw - UI.Ds.Px(0.024f));
                UI.Ds.T("SPECIALTY", cx + UI.Ds.Px(0.012f), cy + 0.112f, 0.34f, UI.Ds.A(White, a), Cond);
                UI.Ds.T((w.Specialty ?? "").ToUpperInvariant(), cx + cw - UI.Ds.Px(0.012f), cy + 0.112f, 0.34f, UI.Ds.A(White, a), Cond, Alignment.Right);
            }
            DrawStatCard(m);
            if (note != null && Game.GameTime < noteUntil)
            {
                float nw = UI.Ds.Width(Clean(note), 0.34f) + UI.Ds.Px(0.03f);
                UI.Ds.Rect(0.5f - nw / 2, 0.055f, nw, 0.038f, Color.FromArgb(230, 22, 24, 34));
                UI.Ds.T(Clean(note), 0.5f, 0.06f, 0.34f, White, Font.ChaletLondon, Alignment.Center);
            }
            bool adjust = cur != null && cur.Enabled && cur.OnAdjust != null;
            if (root) Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Exit", Control.FrontendRb, "", Control.FrontendLb, "Category");
            else if (adjust) Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Back", Control.FrontendRight, "", Control.FrontendLeft, "Adjust");
            else Buttons.Draw(Control.FrontendAccept, m.IsShopList ? "Buy" : "Select", Control.FrontendCancel, "Back");
        }

        private static void Key(float x, float y, string k)
        {
            float kw = UI.Ds.Px(0.024f);
            UI.Ds.Rect(x, y, kw, 0.022f, Color.FromArgb(255, 235, 235, 235));
            UI.Ds.T(k, x + kw / 2, y - 0.0005f, 0.3f, Ink, Cond, Alignment.Center);
        }

        // ---------- stats (0-10), computed live from the car's current parts, so highlighting a part previews it
        private float[] Stats() { return Stats(false); }
        private float[] Stats(bool installed)
        {
            float spd0 = info != null ? info.MaxSpeed : 45f, acc0 = info != null ? info.Acceleration : 0.25f, brk0 = info != null ? info.Braking : 0.6f, trc0 = info != null ? info.Traction : 2.2f;
            Func<int, int> lv = t => { int v; return installed ? (instMods.TryGetValue(t, out v) ? v + 1 : 0) : Function.Call<int>(Hash.GET_VEHICLE_MOD, car, t) + 1; };
            bool turbo = installed ? instToggles.Contains(18) : Function.Call<bool>(Hash.IS_TOGGLE_MOD_ON, car, 18);
            int ecu = installed || pvEcu < 0 ? rec.EcuLevel : pvEcu, nos = installed || pvNos < 0 ? rec.NosLevel : pvNos, cmp = installed || pvCompound < 0 ? rec.TyreCompound : pvCompound;
            int eng = lv(11), brk = lv(12), box = lv(13), sus = lv(15), arm = lv(16);
            float speed = Scale(spd0, 35f, 95f) + eng * 0.12f + (turbo ? 0.2f : 0) + ecu * 0.1f + box * 0.05f;
            float accel = Scale(acc0, 0.12f, 0.55f) + eng * 0.22f + box * 0.15f + (turbo ? 0.3f : 0) + ecu * 0.12f + nos * 0.2f;
            float comp = FitmentManager.CompoundGrip[Math.Max(0, Math.Min(4, cmp))];
            float asphalt = Scale(trc0 * comp, 1.5f, 3.4f) * 0.75f + Scale(brk0, 0.3f, 1.6f) * 0.25f + brk * 0.1f + sus * 0.12f - (cmp == 4 ? 0.6f : 0);
            var cls = info != null ? info.Class : VehicleClass.Sedans;
            bool offWheels = (installed ? instWheelType : car.Mods.WheelType) == VehicleWheelType.Offroad;
            float offroad = (cls == VehicleClass.OffRoad ? 7.5f : cls == VehicleClass.SUVs ? 6f : cls == VehicleClass.Vans || cls == VehicleClass.Utility ? 4.5f : 3f) + (offWheels ? 1.2f : 0) - sus * 0.25f - (cmp >= 2 ? 0.8f : 0);
            float mass = 1500f; try { mass = car.HandlingData.Mass; } catch { }
            float strength = Scale(mass, 900f, 3500f) * 0.7f + 1.2f + arm * 0.35f;
            return new[] { Clamp10(speed), Clamp10(accel), Clamp10(asphalt), Clamp10(offroad), Clamp10(strength) };
        }
        private static float Scale(float v, float lo, float hi) { return Math.Max(0f, Math.Min(10f, (v - lo) / (hi - lo) * 10f)); }
        private static float Clamp10(float v) { return Math.Max(0.5f, Math.Min(10f, v)); }

        private void DrawStatCard(Menu m)
        {
            var st = Stats(false);
            baseStats = Stats(true);
            float cw = 0.25f, cx = 0.955f - cw, cy = 0.56f, ch = 0.33f;
            UI.Ds.Rect(cx, cy, cw, ch, Color.FromArgb(225, 22, 24, 34));
            float px = cx + UI.Ds.Px(0.012f), pw = cw - UI.Ds.Px(0.024f);
            UI.Ds.T((info != null ? info.Make : "").ToUpperInvariant(), px, cy + 0.008f, 0.36f, Dim, Cond);
            UI.Ds.T(UI.Ds.Fit(Title().ToUpperInvariant(), 0.46f, pw, Cond), px, cy + 0.03f, 0.46f, White, Cond);
            string[] names = { "SPEED", "ACCELERATION", "ASPHALT HANDLING", "OFF-ROAD HANDLING", "STRENGTH" };
            float sy = cy + 0.075f;
            for (int i = 0; i < 5; i++)
            {
                float v = st[i], b = baseStats != null ? baseStats[i] : v;
                UI.Ds.T(names[i], px, sy, 0.38f, White, Cond);
                UI.Ds.T(v.ToString("0.0"), px + pw, sy, 0.38f, v > b + 0.05f ? UI.Ds.Lime : v < b - 0.05f ? UI.Ds.Red : White, Cond, Alignment.Right);
                float by = sy + 0.03f;
                UI.Ds.Rect(px, by, pw, 0.004f, Color.FromArgb(90, 255, 255, 255));
                UI.Ds.Rect(px, by, pw * Math.Min(v, b) / 10f, 0.004f, White);
                if (v > b) UI.Ds.Rect(px + pw * b / 10f, by, pw * (v - b) / 10f, 0.004f, UI.Ds.Lime);
                else if (v < b) UI.Ds.Rect(px + pw * v / 10f, by, pw * (b - v) / 10f, 0.004f, UI.Ds.Red);
                UI.Ds.Rect(px + pw * b / 10f - UI.Ds.Px(0.0008f), by - 0.004f, UI.Ds.Px(0.0016f), 0.012f, White);
                sy += 0.042f;
            }
            int road = (int)Math.Round((st[0] * 0.35f + st[1] * 0.35f + st[2] * 0.3f) * 100f);
            int off = (int)Math.Round((st[0] * 0.25f + st[1] * 0.3f + st[3] * 0.45f) * 100f);
            UI.Ds.Rect(px, sy + 0.004f, pw, 0.001f, Color.FromArgb(80, 255, 255, 255));
            UI.Ds.T("RATING", px, sy + 0.012f, 0.4f, White, Cond);
            UI.Ds.T("ROAD " + road + "    OFF-ROAD " + off, px + pw, sy + 0.012f, 0.4f, White, Cond, Alignment.Right);
            if (Simcade.Active) { var pr = Simcade.Instance.For(car); UI.Ds.T(pr.Archetype.ToUpperInvariant() + " HANDLING", px, cy - 0.032f, 0.34f, Dim, Cond); }
        }

        private static string Clean(string s) { return string.IsNullOrEmpty(s) ? s : Regex.Replace(s, "~[a-zA-Z]~", ""); }

        private void Flash(string s) { note = s; noteUntil = Game.GameTime + 3500; }
        private static void Rect(float x, float y, float w, float h, Color c) { Function.Call(Hash.DRAW_RECT, x + w / 2f, y + h / 2f, w, h, c.R, c.G, c.B, c.A, false); }
        private static void Text(string s, float x, float y, float scale, Color c, Font f, Alignment a = Alignment.Left, float wrap = 0f)
        { new TextElement(s, new PointF(x * 1280f, y * 720f), scale, c, f, a, false, true, wrap * 1280f).ScaledDraw(); }
    }
}
