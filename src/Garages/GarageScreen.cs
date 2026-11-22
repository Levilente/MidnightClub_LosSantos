using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Keys = System.Windows.Forms.Keys;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.UI;
using MidnightClub.Vehicles;
using Control = GTA.Control;
using Font = GTA.UI.Font;

namespace MidnightClub.Garages
{
    /// <summary>The Midnight Club garage screen: orbiting camera over the parked cars, car card with stats,
    /// manage / customise menus, and "drive out" with the chosen car.</summary>
    public sealed class GarageScreen
    {
        private readonly GarageManager mgr;
        private readonly GarageDefinition g;
        private readonly List<KeyValuePair<OwnedVehicle, Vehicle>> cars;
        private readonly SaveManager saves;
        private readonly TransactionManager money;
        private readonly MenuStack menus = new MenuStack();
        private readonly OwnedVehicle arrivedIn;
        private int index;
        private Camera cam;
        private Vector3 camPos, lookAt;
        private bool done; private OwnedVehicle result;
        private bool prevM, prevC;
        private string note; private int noteUntil;
        private float maxSpeed = 1, maxAccel = 1, maxBrake = 1, maxGrip = 1;

        private static readonly Color Purple = Color.FromArgb(255, 170, 90, 255);
        private static readonly Color Cyan = Color.FromArgb(255, 80, 220, 255);
        private static readonly Color Panel = Color.FromArgb(205, 8, 6, 16);

        public GarageScreen(GarageManager mgr, GarageDefinition g, List<KeyValuePair<OwnedVehicle, Vehicle>> cars, OwnedVehicle arrivedIn, SaveManager saves, TransactionManager money, string note)
        {
            this.mgr = mgr; this.g = g; this.cars = cars; this.arrivedIn = arrivedIn; this.saves = saves; this.money = money;
            int i = cars.FindIndex(c => c.Key == arrivedIn); index = i < 0 ? 0 : i;
            if (note != null) { this.note = note; noteUntil = Game.GameTime + 6000; }
            var all = mgr.Registry.All.ToList();
            if (all.Count > 0)
            {
                maxSpeed = all.Max(v => v.MaxSpeed); maxAccel = all.Max(v => v.Acceleration);
                maxBrake = all.Max(v => v.Braking); maxGrip = all.Max(v => v.Traction);
            }
        }

        public OwnedVehicle Run()
        {
            if (cars.Count == 0) return arrivedIn;
            cam = Camera.Create(ScriptedCameraNameHash.DefaultScriptedCamera, Vector3.Zero, Vector3.Zero, 45f, true, EulerRotationOrder.YXZ);
            Target(true);
            Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false, 0);
            Function.Call(Hash.DISPLAY_RADAR, false);
            try
            {
                while (!done)
                {
                    Game.DisableAllControlsThisFrame();
                    foreach (var c in new[] { Control.FrontendUp, Control.FrontendDown, Control.FrontendLeft, Control.FrontendRight, Control.FrontendAccept, Control.FrontendCancel, Control.FrontendX, Control.FrontendY })
                        Game.EnableControlThisFrame(c);
                    Hud.HideComponentThisFrame(HudComponent.HelpText);
                    if (menus.IsOpen) menus.Tick(); else Input();
                    if (Vehicles.FitmentManager.Instance != null) foreach (var kv in cars) Vehicles.FitmentManager.Instance.ApplyTo(kv.Value, kv.Key);
                    Animate();
                    DrawHud();
                    Script.Wait(0);
                }
            }
            finally
            {
                Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0);
                Function.Call(Hash.DISPLAY_RADAR, true);
                if (cam != null) { cam.IsActive = false; cam.Delete(); }
            }
            return result;
        }

        // ------------------------------------------------------------------ input
        private void Input()
        {
            if (Game.IsControlJustPressed(Control.FrontendLeft) || Game.IsControlJustPressed(Control.FrontendUp)) Move(-1);
            if (Game.IsControlJustPressed(Control.FrontendRight) || Game.IsControlJustPressed(Control.FrontendDown)) Move(+1);
            if (Game.IsControlJustPressed(Control.FrontendAccept)) { Sound("SELECT"); result = cars[index].Key; done = true; }
            if (Game.IsControlJustPressed(Control.FrontendCancel))
            {
                var back = cars.FirstOrDefault(kv => kv.Key == arrivedIn).Key;
                result = back ?? cars[index].Key; Sound("BACK"); done = true;
            }
            bool m = Game.IsKeyPressed(Keys.M), c = Game.IsKeyPressed(Keys.C);
            if ((m && !prevM) || Game.IsControlJustPressed(Control.FrontendX)) menus.Open(ManageMenu(cars[index].Key));
            if (g.Customizable && ((c && !prevC) || Game.IsControlJustPressed(Control.FrontendY))) menus.Open(CustomizeMenu());
            prevM = m; prevC = c;
        }

        private void Move(int dir)
        {
            if (cars.Count < 2) return;
            index = (index + dir + cars.Count) % cars.Count;
            Sound("NAV_LEFT_RIGHT");
            Target(false);
        }

        private void Target(bool snap)
        {
            var v = cars[index].Value;
            if (v == null || !v.Exists()) return;
            float len = Math.Max(2.2f, v.Model.Dimensions.frontTopRight.Y);
            var to = v.GetOffsetPosition(new Vector3(-len * 1.45f, len * 2.05f, 0.75f));
            lookAt = v.Position + new Vector3(0, 0, 0.1f);
            if (snap) camPos = to;
            targetPos = to;
            foreach (var kv in cars) if (kv.Value.Exists()) Function.Call(Hash.SET_VEHICLE_LIGHTS, kv.Value, 1);
        }
        private Vector3 targetPos;

        private void Animate()
        {
            camPos = Vector3.Lerp(camPos, targetPos, Math.Min(1f, Game.LastFrameTime * 6f));
            UI.Studio.Frame(cam, camPos, lookAt, 0f, 0.14f);
            UI.Studio.Light(cars[index].Value, camPos);
        }

        // ------------------------------------------------------------------ menus
        private Menu ManageMenu(OwnedVehicle r)
        {
            var m = new Menu(mgr.Title(r), "Manage vehicle");
            m.Add(new MenuItem("Nickname", () => { var s = Game.GetUserInput(r.Nickname ?? ""); if (s != null) { r.Nickname = s.Trim().Length == 0 ? null : s.Trim(); saves.IsDirty = true; } }) { Value = () => r.Nickname ?? "-" });
            m.Add(new MenuItem("License plate", () =>
            {
                var s = Game.GetUserInput(r.Plate ?? "");
                if (s == null) return; s = s.Trim().ToUpperInvariant();
                if (s.Length < 1 || s.Length > 8) { Flash("Plates hold 1-8 characters."); return; }
                r.Plate = s; saves.IsDirty = true; Police.PursuitManager.CoolDownIdentity(r, 1f);
                var v = cars[index].Value; if (v.Exists() && cars[index].Key == r) v.Mods.LicensePlate = s;
            }) { Value = () => r.Plate });
            m.Add(new MenuItem("Favourite", () => { r.Favorite = !r.Favorite; saves.IsDirty = true; }) { Value = () => r.Favorite ? "Yes" : "No" });
            m.Add(new MenuItem("Move to another garage", () =>
            {
                var to = mgr.MoveToOtherGarage(r, g.Id);
                if (to == null) { Flash("No free space in your other garages."); return; }
                int i = cars.FindIndex(c => c.Key == r);
                if (i >= 0) { if (cars[i].Value.Exists()) cars[i].Value.Delete(); cars.RemoveAt(i); }
                Flash(mgr.Title(r) + " moved to " + to + ".");
                menus.Close();
                if (cars.Count == 0) { result = r; done = true; return; }   // garage now empty: drive the moved car out
                index = Math.Min(index, cars.Count - 1); Target(false);
            }) { Description = () => "The car is delivered to the first owned garage with room." });
            return m;
        }

        private Menu CustomizeMenu()
        {
            var o = mgr.Owned(g.Id);
            int decor = Math.Max(1, o.Decor), light = Math.Max(1, o.Lighting), num = Math.Max(1, o.Numbering);
            int origDecor = decor, origLight = light, origNum = num;
            Action preview = () => { var tmp = new OwnedGarage { Decor = decor, Lighting = light, Numbering = num }; mgr.ApplyCustomization(g, tmp); };
            Func<long> cost = () => (decor != origDecor ? 25000 : 0) + (light != origLight ? 10000 : 0) + (num != origNum ? 5000 : 0);

            var m = new Menu("Customise", g.Name);
            m.OnBack = () => { mgr.ApplyCustomization(g, o); menus.Close(); };   // leaving without buying reverts the preview
            m.Add(new MenuItem("Floor & walls") { Value = () => GarageManager.DecorNames[decor - 1], OnAdjust = d => { decor = (decor - 1 + d + 4) % 4 + 1; preview(); },
                Description = () => "$25,000" });
            m.Add(new MenuItem("Lighting") { Value = () => "Style " + light, OnAdjust = d => { light = (light - 1 + d + 9) % 9 + 1; preview(); }, Description = () => "$10,000" });
            m.Add(new MenuItem("Floor numbering") { Value = () => "Style " + num, OnAdjust = d => { num = (num - 1 + d + 9) % 9 + 1; preview(); }, Description = () => "$5,000" });
            m.Add(new MenuItem("Apply changes", () =>
            {
                long c = cost();
                if (c == 0) { menus.Close(); return; }
                string tx = "garage-style:" + g.Id + ":" + decor + "-" + light + "-" + num + ":" + DateTime.UtcNow.Ticks;
                if (!money.TrySpend(c, "garage", g.Name + " restyle", tx)) { Flash("~r~Not enough cash."); return; }
                o.Decor = decor; o.Lighting = light; o.Numbering = num; saves.IsDirty = true; saves.Save();
                origDecor = decor; origLight = light; origNum = num;
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "PROPERTY_PURCHASE", "HUD_AWARDS", true);
                menus.Close();
            }) { Value = () => cost() == 0 ? "-" : "$" + cost().ToString("N0") });
            return m;
        }

        // ------------------------------------------------------------------ HUD
        private void DrawHud()
        {
            Theme.FullscreenFrame();
            var r = cars[index].Key; var info = mgr.Info(r);
            Studio.Title(g.KindLabel.ToUpperInvariant() + "  ·  " + cars.Count + " / " + g.Capacity, g.Name.ToUpperInvariant(), "$" + saves.Data.Cash.ToString("N0"));
            int cond = (int)Math.Round(Math.Max(0, Math.Min(100, (r.EngineHealth + r.BodyHealth) / 20f)));
            float[] stats = info == null ? null : new[] { info.MaxSpeed / maxSpeed, info.Acceleration / maxAccel, info.Braking / maxBrake, info.Traction / maxGrip };
            string sub = info == null ? r.Model : info.Make.ToUpperInvariant() + "   " + VehicleRegistry.ClassName(info.Class).ToUpperInvariant() + "   TIER " + info.Tier;
            Studio.CarStrip(r.Favorite ? "FAVOURITE" : "MY GARAGE", mgr.Title(r).ToUpperInvariant(), sub, stats,
                new[] { "$" + PricingEngine.EstimatedValue(info, r.Stats.DistanceKm, r.EngineHealth, r.BodyHealth).ToString("N0"), "ESTIMATED VALUE", "",
                        "CONDITION  " + cond + "%", r.Stats.DistanceKm.ToString("0.0") + " KM  ·  BEST " + r.Stats.TopSpeedKmh.ToString("0") + " KM/H", "PLATE  " + r.Plate },
                cars.Count > 1 ? "<  " + (index + 1) + " / " + cars.Count + "  >" : null);
            if (note != null && Game.GameTime < noteUntil)
            {
                Theme.Card(0.36f, 0.14f, 0.28f, 0.05f);
                Theme.Text(note, 0.5f, 0.152f, 0.3f, Color.White, Font.ChaletLondon, Alignment.Center, 0.26f);
            }
            if (!menus.IsOpen)
            {
                bool here = cars.Any(c => c.Key == arrivedIn);
                if (g.Customizable) Buttons.Draw(Control.FrontendAccept, "Drive out", Control.FrontendCancel, here ? "Leave in current car" : "Leave", Control.FrontendX, "Manage", Control.FrontendY, "Customise garage", Control.FrontendRight, "", Control.FrontendLeft, "Choose car");
                else Buttons.Draw(Control.FrontendAccept, "Drive out", Control.FrontendCancel, here ? "Leave in current car" : "Leave", Control.FrontendX, "Manage", Control.FrontendRight, "", Control.FrontendLeft, "Choose car");
            }
        }

        private void Bar(string label, float v, float x, float y)
        {
            v = Math.Max(0.03f, Math.Min(1f, v));
            Text(label, x + 0.012f, y, 0.28f, Color.FromArgb(220, 210, 210, 220), Font.ChaletLondon);
            Rect(x + 0.11f, y + 0.008f, 0.175f, 0.011f, Color.FromArgb(160, 60, 60, 70));
            Rect(x + 0.11f, y + 0.008f, 0.175f * v, 0.011f, Purple);
        }

        private void Flash(string s) { note = s; noteUntil = Game.GameTime + 4000; }

        private static void Rect(float x, float y, float w, float h, Color c)
        {
            Function.Call(Hash.DRAW_RECT, x + w / 2f, y + h / 2f, w, h, c.R, c.G, c.B, c.A, false);
        }

        private static void Text(string s, float x, float y, float scale, Color c, Font f, Alignment a = Alignment.Left, float wrap = 0f)
        {
            new TextElement(s, new PointF(x * 1280f, y * 720f), scale, c, f, a, false, true, wrap * 1280f).ScaledDraw();
        }

        private static void Sound(string name) { Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, name, "HUD_FRONTEND_DEFAULT_SOUNDSET", true); }
    }
}
