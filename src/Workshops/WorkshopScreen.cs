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
            var root = Root();
            root.OnBack = () => done = true;
            menus.Open(root);
            Screen.FadeIn(300);
            try
            {
                while (!done && car.Exists())
                {
                    Game.DisableAllControlsThisFrame();
                    foreach (var c in new[] { Control.FrontendUp, Control.FrontendDown, Control.FrontendLeft, Control.FrontendRight, Control.FrontendAccept, Control.FrontendCancel })
                        Game.EnableControlThisFrame(c);
                    UI.Theme.FullscreenFrame();
                    if (!menus.IsOpen) done = true; else menus.Tick();
                    if (FitmentManager.Instance != null) FitmentManager.Instance.Tick();
                    Orbit();
                    DrawHud();
                    Script.Wait(0);
                }
            }
            finally
            {
                Screen.FadeOut(300); Script.Wait(350);
                if (car.Exists()) { vehicles.Capture(); Function.Call(Hash.FREEZE_ENTITY_POSITION, car, false); }
                saves.Save();
                Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0);
                Function.Call(Hash.DISPLAY_RADAR, true);
                if (cam != null) { cam.IsActive = false; cam.Delete(); }
                Screen.FadeIn(400);
            }
        }

        // ------------------------------------------------------------------ menus
        private Menu Root()
        {
            var m = new Menu(w.Bennys ? "Benny's" : "Workshop", Title());
            m.OnOpen = () => { focus = "default"; };
            int cond = Condition();
            if (cond < 99)
                m.Add(new MenuItem("Repair", () => Repair()) { Value = () => Condition() >= 99 ? "~g~DONE" : "$" + RepairCost().ToString("N0"), Description = () => "Condition " + Condition() + "%." });
            m.Add(new MenuItem("Performance", () => menus.Push(Performance())) { Description = () => "Engine, brakes, gearbox, suspension, turbo, ECU tune, drift tyres." });
            m.Add(new MenuItem("Body", () => menus.Push(Body())));
            m.Add(new MenuItem("Wheels & tyres", () => menus.Push(Wheels())));
            m.Add(new MenuItem("Adjust fitment", () => menus.Push(Fitment())) { Value = () => rec.FitKit ? "" : "$" + FitKitPrice().ToString("N0"),
                Description = () => "Camber, track width and tyre diameter per axle (stance). One-time kit, then adjust for free." });
            m.Add(new MenuItem("Respray", () => menus.Push(Respray())));
            m.Add(Toggle("Xenon headlights", 22, 1200));
            m.Add(new MenuItem("Window tint", () => menus.Push(Tint())));
            if (Function.Call<int>(Hash.GET_VEHICLE_LIVERY_COUNT, car) > 0) m.Add(new MenuItem("Livery", () => menus.Push(Livery())));
            m.Add(new MenuItem("Build sheet", () => menus.Push(BuildSheet())) { Description = () => "Spent on this car: $" + rec.Stats.CustomizationSpent.ToString("N0") + ", " + rec.Stats.ModificationCount + " modifications." });
            return m;
        }

        private Menu Performance()
        {
            var m = new Menu("Performance", Title());
            m.Add(new MenuItem("Tyre compound", () => menus.Push(Compounds())) { Value = () => FitmentManager.CompoundNames[rec.TyreCompound] });
            m.OnOpen = () => focus = "default";
            AddModMenu(m, "Engine", 11, 0.06, "front");
            AddModMenu(m, "Brakes", 12, 0.03, "side");
            AddModMenu(m, "Transmission", 13, 0.045, "default");
            AddModMenu(m, "Suspension", 15, 0.03, "side");
            m.Add(Toggle("Turbo", 18, 2500 + bp * 8 / 100));
            m.Add(new MenuItem("Nitrous kit", () => menus.Push(Nitrous())) { Value = () => rec.NosLevel == 0 ? "None" : "Stage " + rec.NosLevel, Description = () => "Hold Sprint (keyboard) or the boost button (controller) to fire. Bigger stages: more power, longer burn, faster refill." });
            m.Add(new MenuItem("ECU tune (Midnight Club)", () => menus.Push(Ecu())) { Value = () => rec.EcuLevel == 0 ? "Stock" : "Stage " + rec.EcuLevel,
                Description = () => "Remapped engine management: +6% power per stage on top of the engine upgrade." });
            return m;
        }

        private Menu Nitrous()
        {
            var m = new Menu("Nitrous", Title());
            m.OnOpen = () => focus = "rear";
            for (int lvl = 0; lvl <= 3; lvl++)
            {
                int l = lvl;
                long price = l == 0 ? 0 : PricingEngine.Round((4000 + bp * 0.03) * l);
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

        private Menu Ecu()
        {
            var m = new Menu("ECU tune", Title());
            for (int lvl = 0; lvl <= 3; lvl++)
            {
                int l = lvl;
                long price = l == 0 ? 0 : PricingEngine.Round((1500 + bp * 0.05) * l);
                m.Add(new MenuItem(l == 0 ? "Stock map" : "Stage " + l, () =>
                {
                    if (rec.EcuLevel == l) return;
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
            var m = new Menu("Body", Title());
            m.OnOpen = () => focus = "default";
            var names = new Dictionary<int, string> { { 0, "Spoiler" }, { 1, "Front bumper" }, { 2, "Rear bumper" }, { 3, "Side skirts" }, { 4, "Exhaust" }, { 5, "Roll cage" },
                { 6, "Grille" }, { 7, "Hood" }, { 8, "Left fender" }, { 9, "Right fender" }, { 10, "Roof" }, { 48, "Livery (mod)" } };
            var focusOf = new Dictionary<int, string> { { 0, "rear" }, { 1, "front" }, { 2, "rear" }, { 3, "side" }, { 4, "rear" }, { 6, "front" }, { 7, "front" }, { 8, "side" }, { 9, "side" } };
            var types = new List<int>(names.Keys);
            if (w.Bennys) for (int t = 25; t <= 46; t++) types.Add(t);
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
            m.OnOpen = () => focus = "side";
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
            m.Add(Toggle("Tyre smoke", 20, 1000));
            return m;
        }

        private Menu Compounds()
        {
            var m = new Menu("Tyre compound", Title());
            m.OnOpen = () => focus = "side";
            long[] prices = { 0, PricingEngine.Round(1500 + bp * 0.01), PricingEngine.Round(3000 + bp * 0.02), PricingEngine.Round(6000 + bp * 0.035), 3000 };
            string[] desc = { "Everyday road tyres. Stock grip.", "Sticky road tyres: +5% grip.", "Track-day rubber: +10% grip.", "Full race slicks: +16% grip.", "Hard low-grip compound for drifting." };
            for (int i = 0; i < FitmentManager.CompoundNames.Length; i++)
            {
                int c = i;
                m.Add(new MenuItem(FitmentManager.CompoundNames[c], () =>
                {
                    if (rec.TyreCompound == c) return;
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
            var m = new Menu(car.Mods.GetLocalizedWheelTypeName(type), Title());
            long price = PricingEngine.Round(500 + bp * 0.008);
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
            var m = new Menu(what, Title());
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
            var m = new Menu("Window tint", Title());
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
            var m = new Menu("Livery", Title());
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
            var m = new Menu(label, Title());
            m.OnOpen = () => focus = camFocus;
            var opts = Enumerable.Range(-1, count + 1).ToList();
            Func<int, long> price = i =>
            {
                if (i < 0) return 0;
                if (cosmetic) return PricingEngine.Round((200 + bp * factor) * (1 + i * 0.15));
                return PricingEngine.Round((800 + bp * factor) * (i + 1));
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
            return new MenuItem(label, () =>
            {
                bool on = Function.Call<bool>(Hash.IS_TOGGLE_MOD_ON, car, type);
                if (on) { Function.Call(Hash.TOGGLE_VEHICLE_MOD, car, type, false); Bought("Removed " + label, 0); return; }
                if (!Pay(price, label)) return;
                Function.Call(Hash.TOGGLE_VEHICLE_MOD, car, type, true); Bought(label, price);
            }) { Value = () => Function.Call<bool>(Hash.IS_TOGGLE_MOD_ON, car, type) ? "~g~FITTED" : "$" + price.ToString("N0"), Description = () => "Select again to remove (free)." };
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
            double r = orbit * Math.PI / 180.0;
            var local = new Vector3((float)Math.Sin(r) * radius, (float)Math.Cos(r) * radius, height);
            var cp = car.GetOffsetPosition(local);
            UI.Studio.Frame(cam, cp, car.Position + new Vector3(0, 0, 0.15f), 0.22f, 0.04f);
            UI.Studio.Light(car, cp);
        }

        private void DrawHud()
        {
            UI.Studio.Fade(false, 0.16f, 150);
            UI.Theme.Text("WORKSHOP", 0.95f, 0.04f, 0.26f, UI.Theme.Purple, Font.ChaletLondon, Alignment.Right);
            UI.Theme.Text(w.Name.ToUpperInvariant(), 0.95f, 0.058f, 0.62f, Color.White, Font.ChaletComprimeCologne, Alignment.Right);
            UI.Theme.Text("$" + saves.Data.Cash.ToString("N0") + "   ·   CONDITION " + Condition() + "%" + (rec.EcuLevel > 0 ? "   ·   ECU STAGE " + rec.EcuLevel : ""), 0.95f, 0.11f, 0.28f, UI.Theme.TextDim, Font.ChaletLondon, Alignment.Right);
            if (note != null && Game.GameTime < noteUntil)
            {
                Rect(0.35f, 0.88f, 0.30f, 0.05f, Panel);
                Text(note, 0.5f, 0.89f, 0.32f, Color.White, Font.ChaletLondon, Alignment.Center, 0.29f);
            }
        }

        private void Flash(string s) { note = s; noteUntil = Game.GameTime + 3500; }
        private static void Rect(float x, float y, float w, float h, Color c) { Function.Call(Hash.DRAW_RECT, x + w / 2f, y + h / 2f, w, h, c.R, c.G, c.B, c.A, false); }
        private static void Text(string s, float x, float y, float scale, Color c, Font f, Alignment a = Alignment.Left, float wrap = 0f)
        { new TextElement(s, new PointF(x * 1280f, y * 720f), scale, c, f, a, false, true, wrap * 1280f).ScaledDraw(); }
    }
}
