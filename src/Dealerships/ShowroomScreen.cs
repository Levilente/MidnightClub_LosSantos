using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Persistence;
using MidnightClub.UI;
using MidnightClub.Vehicles;
using Control = GTA.Control;
using Font = GTA.UI.Font;
using Keys = System.Windows.Forms.Keys;

namespace MidnightClub.Dealerships
{
    /// <summary>Showroom: the current stock is shown one car at a time on a turntable where you parked,
    /// with specs and price; test drive (T) or buy (Enter).</summary>
    public sealed class ShowroomScreen
    {
        private readonly DealershipManager mgr;
        private readonly DealershipDefinition d;
        private readonly VehicleManager vehicles;
        private readonly SaveManager saves;
        private readonly MenuStack menus = new MenuStack();
        private List<StockItem> stock;
        private int index;
        private Vehicle shown;
        private Camera cam;
        private Vector3 spot; private float spin;
        private bool done, prevT;
        private string note; private int noteUntil;
        private float maxSpeed = 1, maxAccel = 1, maxBrake = 1, maxGrip = 1;

        private static readonly Color Purple = Color.FromArgb(255, 170, 90, 255);
        private static readonly Color Cyan = Color.FromArgb(255, 80, 220, 255);
        private static readonly Color Panel = Color.FromArgb(205, 8, 6, 16);

        public ShowroomScreen(DealershipManager mgr, DealershipDefinition d, VehicleManager vehicles, SaveManager saves)
        {
            this.mgr = mgr; this.d = d; this.vehicles = vehicles; this.saves = saves;
            var all = mgr.Registry.All.ToList();
            if (all.Count > 0) { maxSpeed = all.Max(v => v.MaxSpeed); maxAccel = all.Max(v => v.Acceleration); maxBrake = all.Max(v => v.Braking); maxGrip = all.Max(v => v.Traction); }
        }

        public void Run()
        {
            stock = mgr.Stock(d);
            if (stock.Count == 0) { Notification.PostTicker(d.Name + " has nothing in stock.", true, false); return; }
            var ped = Game.Player.Character;
            var own = vehicles.Current;
            if (Cameras.CameraSystem.Instance != null) Cameras.CameraSystem.Instance.Release();
            Screen.FadeOut(400); Script.Wait(450);
            spot = own != null && own.Exists() ? own.Position : ped.Position;
            float heading = own != null && own.Exists() ? own.Heading : ped.Heading;
            Hide(own, ped, true);
            cam = Camera.Create(ScriptedCameraNameHash.DefaultScriptedCamera, Vector3.Zero, Vector3.Zero, 45f, true, EulerRotationOrder.YXZ);
            var back = new Vector3((float)Math.Sin(heading * Math.PI / 180) * 6.0f, -(float)Math.Cos(heading * Math.PI / 180) * 6.0f, 1.6f);
            cam.Position = spot + new Vector3(-back.X, -back.Y, back.Z) + new Vector3(back.Y * 0.35f, -back.X * 0.35f, 0);
            cam.PointAt(spot + new Vector3(0, 0, 0.3f));
            Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false, 0);
            Function.Call(Hash.DISPLAY_RADAR, false);
            Show();
            Screen.FadeIn(400);
            try
            {
                while (!done)
                {
                    Game.DisableAllControlsThisFrame();
                    foreach (var c in new[] { Control.FrontendUp, Control.FrontendDown, Control.FrontendLeft, Control.FrontendRight, Control.FrontendAccept, Control.FrontendCancel, Control.FrontendX })
                        Game.EnableControlThisFrame(c);
                    Hud.HideComponentThisFrame(HudComponent.HelpText);
                    if (menus.IsOpen) menus.Tick(); else Input();
                    if (shown != null && shown.Exists()) { spin = (spin + Game.LastFrameTime * 14f) % 360f; shown.Heading = spin; Studio.Frame(cam, cam.Position, spot + new Vector3(0, 0, 0.1f), 0f, 0.14f); Studio.Light(shown, cam.Position); }
                    DrawHud();
                    Script.Wait(0);
                }
            }
            finally
            {
                Screen.FadeOut(300); Script.Wait(350);
                if (shown != null && shown.Exists()) shown.Delete();
                Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0);
                Function.Call(Hash.DISPLAY_RADAR, true);
                if (cam != null) { cam.IsActive = false; cam.Delete(); }
                var me = Game.Player.Character; var mine = vehicles.Current;
                if (mine != null && mine.Exists() && !me.IsInVehicle(mine)) me.SetIntoVehicle(mine, VehicleSeat.Driver);
                Hide(mine, me, false);
                Screen.FadeIn(400);
            }
        }

        private static void Hide(Vehicle car, Ped ped, bool hide)
        {
            if (car != null && car.Exists())
            {
                Function.Call(Hash.SET_ENTITY_VISIBLE, car, !hide, false);
                Function.Call(Hash.SET_ENTITY_COLLISION, car, !hide, !hide);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, car, hide);
            }
            if (ped != null && ped.Exists()) Function.Call(Hash.SET_ENTITY_VISIBLE, ped, !hide, false);
        }

        private void Show()
        {
            if (shown != null && shown.Exists()) shown.Delete();
            shown = null;
            var item = stock[index];
            try
            {
                if (item.Preview == null) item.Preview = item.MakeRecord();
                shown = vehicles.SpawnDisplay(item.Preview, spot + new Vector3(0, 0, 0.3f), spin);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, shown, true);
                Function.Call(Hash.SET_VEHICLE_LIGHTS, shown, 2);
            }
            catch (Exception ex) { Core.Log.Error("Showroom display " + item.Info.Id, ex); Flash("~r~This car could not be displayed."); }
        }

        private void Input()
        {
            if (Game.IsControlJustPressed(Control.FrontendLeft) || Game.IsControlJustPressed(Control.FrontendUp)) Move(-1);
            if (Game.IsControlJustPressed(Control.FrontendRight) || Game.IsControlJustPressed(Control.FrontendDown)) Move(+1);
            if (Game.IsControlJustPressed(Control.FrontendCancel)) { done = true; return; }
            var item = stock[index];
            if (Game.IsControlJustPressed(Control.FrontendAccept))
            {
                if (mgr.IsSold(item)) { Flash("Already sold."); return; }
                menus.Open(ConfirmMenu(item));
            }
            bool t = Game.IsKeyPressed(Keys.T);
            if ((t && !prevT) || Game.IsControlJustPressed(Control.FrontendX)) TestDrive(item);
            prevT = t;
        }

        private void Move(int dir)
        {
            if (stock.Count < 2) return;
            index = (index + dir + stock.Count) % stock.Count;
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
            Show();
        }

        private Menu ConfirmMenu(StockItem item)
        {
            var m = new Menu("Confirm purchase", item.Info.Name);
            string garage = mgr.Garages.FindFreeGarage();
            m.Add(new MenuItem("Buy for $" + item.Price.ToString("N0"), () =>
            {
                var err = mgr.Purchase(item, d.Name);
                menus.Close();
                if (err != null) Flash("~r~" + err); else Flash("~g~Bought!~s~ Delivered to " + mgr.Garages.GarageName(garage) + ".");
            }) { Description = () => garage == null ? "~r~No free garage space." : "Delivered to " + mgr.Garages.GarageName(garage) + "." });
            m.Add(new MenuItem("Your cash") { Value = () => "$" + saves.Data.Cash.ToString("N0") });
            m.Add(new MenuItem("Cancel", () => menus.Close()));
            return m;
        }

        // ------------------------------------------------------------------ test drive
        private void TestDrive(StockItem item)
        {
            var ped = Game.Player.Character;
            Screen.FadeOut(300); Script.Wait(350);
            if (shown != null && shown.Exists()) shown.Delete();
            shown = null;
            Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0);
            Function.Call(Hash.DISPLAY_RADAR, true);
            Vehicle car = null;
            try
            {
                car = vehicles.SpawnDisplay(item.Preview ?? (item.Preview = item.MakeRecord()), spot + new Vector3(0, 0, 0.4f), spin);
                Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, car, 1);
                Function.Call(Hash.SET_ENTITY_VISIBLE, ped, true, false);
                ped.SetIntoVehicle(car, VehicleSeat.Driver);
                car.IsEngineRunning = true;
                Function.Call(Hash.SET_GAMEPLAY_CAM_RELATIVE_HEADING, 0f);
                Screen.FadeIn(300);
                int end = Game.GameTime + 90000;
                while (Game.GameTime < end && car.Exists() && !car.IsDead)
                {
                    Game.DisableControlThisFrame(Control.VehicleExit);
                    if (Game.IsControlJustPressed(Control.FrontendCancel) || Game.IsKeyPressed(Keys.Back)) break;
                    if (!ped.IsInVehicle(car)) ped.SetIntoVehicle(car, VehicleSeat.Driver);
                    int s = (end - Game.GameTime) / 1000;
                    Rect(0.4f, 0.04f, 0.2f, 0.07f, Panel);
                    Text("TEST DRIVE  " + s / 60 + ":" + (s % 60).ToString("00"), 0.5f, 0.045f, 0.5f, Purple, Font.ChaletComprimeCologne, Alignment.Center);
                    Text(item.Info.Name + "   ·   " + (int)(car.Speed * 3.6f) + " KM/H", 0.5f, 0.085f, 0.3f, Color.White, Font.ChaletLondon, Alignment.Center);
                    Buttons.Draw(Control.FrontendCancel, "End test drive");
                    Script.Wait(0);
                }
            }
            catch (Exception ex) { Core.Log.Error("Test drive", ex); }
            Screen.FadeOut(400); Script.Wait(450);
            if (car != null && car.Exists()) car.Delete();
            var mine2 = vehicles.Current;
            if (mine2 != null && mine2.Exists()) ped.SetIntoVehicle(mine2, VehicleSeat.Driver); else ped.Position = spot;
            Function.Call(Hash.SET_ENTITY_VISIBLE, ped, false, false);
            Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false, 0);
            Function.Call(Hash.DISPLAY_RADAR, false);
            Show();
            Screen.FadeIn(400);
        }

        // ------------------------------------------------------------------ HUD
        private void DrawHud()
        {
            Theme.FullscreenFrame();
            var item = stock[index]; var v = item.Info; bool sold = mgr.IsSold(item);
            var promo = mgr.PromoClass(d); int days = mgr.DaysUntilRotation(d);
            Studio.Title(d.Tagline.ToUpperInvariant(), d.Name.ToUpperInvariant(), "$" + saves.Data.Cash.ToString("N0"));
            Theme.Text("NEW STOCK IN " + days + (days == 1 ? " DAY" : " DAYS") + (promo.HasValue ? "  ·  " + VehicleRegistry.ClassName(promo.Value).ToUpperInvariant() + " -15%" : ""), 0.95f, 0.085f, 0.26f, promo.HasValue ? Theme.Gold : Theme.TextDim, Font.ChaletLondon, Alignment.Right);
            var right = new System.Collections.Generic.List<string>();
            right.Add(sold ? "SOLD" : "$" + item.Price.ToString("N0"));
            right.Add(item.ListPrice > item.Price ? "WAS $" + item.ListPrice.ToString("N0") : "PRICE");
            if (d.IsUsedMarket && item.Preview != null)
            {
                int cond = (int)Math.Round((item.Preview.EngineHealth + item.Preview.BodyHealth) / 20f);
                right.Add(""); right.Add("CONDITION  " + cond + "%"); right.Add(item.Preview.Stats.DistanceKm.ToString("0") + " KM  ·  " + item.Preview.Mods.Count + " MODS"); right.Add("SELLER  " + item.Seller.ToUpperInvariant());
            }
            Studio.CarStrip(item.Badge != null ? item.Badge.ToUpperInvariant() : "IN STOCK", v.DisplayName.ToUpperInvariant(),
                v.Make.ToUpperInvariant() + "   " + VehicleRegistry.ClassName(v.Class).ToUpperInvariant() + "   TIER " + v.Tier,
                new[] { v.MaxSpeed / maxSpeed, v.Acceleration / maxAccel, v.Braking / maxBrake, v.Traction / maxGrip }, right.ToArray(),
                "<  " + (index + 1) + " / " + stock.Count + "  >");
            if (note != null && Game.GameTime < noteUntil)
            {
                Theme.Card(0.36f, 0.14f, 0.28f, 0.05f);
                Theme.Text(note, 0.5f, 0.152f, 0.3f, Color.White, Font.ChaletLondon, Alignment.Center, 0.26f);
            }
            if (!menus.IsOpen) Buttons.Draw(Control.FrontendAccept, "Buy", Control.FrontendCancel, "Leave", Control.FrontendX, "Test drive", Control.FrontendRight, "", Control.FrontendLeft, "Browse");
        }

        private void Bar(string label, float val, float x, float y)
        {
            val = Math.Max(0.03f, Math.Min(1f, val));
            Text(label, x + 0.012f, y, 0.28f, Color.FromArgb(220, 210, 210, 220), Font.ChaletLondon);
            Rect(x + 0.11f, y + 0.008f, 0.2f, 0.011f, Color.FromArgb(160, 60, 60, 70));
            Rect(x + 0.11f, y + 0.008f, 0.2f * val, 0.011f, Purple);
        }

        private void Flash(string s) { note = s; noteUntil = Game.GameTime + 4000; }
        private static void Rect(float x, float y, float w, float h, Color c) { Function.Call(Hash.DRAW_RECT, x + w / 2f, y + h / 2f, w, h, c.R, c.G, c.B, c.A, false); }
        private static void Text(string s, float x, float y, float scale, Color c, Font f, Alignment a = Alignment.Left, float wrap = 0f)
        { new TextElement(s, new PointF(x * 1280f, y * 720f), scale, c, f, a, false, true, wrap * 1280f).ScaledDraw(); }
    }
}
