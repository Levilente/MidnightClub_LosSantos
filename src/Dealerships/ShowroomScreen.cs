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
                    if (shown != null && shown.Exists()) { spin = (spin + Game.LastFrameTime * 18f) % 360f; shown.Heading = spin; }
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
            Theme.Card(0.03f, 0.05f, 0.40f, 0.12f);
            Theme.Text(d.Name.ToUpperInvariant(), 0.045f, 0.055f, 0.58f, Theme.Purple, Font.Pricedown);
            Theme.Text(d.Tagline, 0.045f, 0.1f, 0.27f, Color.White);
            var promo = mgr.PromoClass(d); int days = mgr.DaysUntilRotation(d);
            Theme.Text("NEW STOCK IN " + days + (days == 1 ? " DAY" : " DAYS") + "   ·   " + mgr.Calendar.DayName.ToUpperInvariant() + "   ·   $" + saves.Data.Cash.ToString("N0")
                + (promo.HasValue ? "   ·   ~y~" + VehicleRegistry.ClassName(promo.Value).ToUpperInvariant() + " -15%" : ""), 0.045f, 0.13f, 0.26f, Theme.Cyan);

            float x = 0.03f, y = 0.5f, w = 0.36f, h = 0.38f;
            Theme.Card(x, y, w, h);
            Theme.Text((index + 1) + " / " + stock.Count, x + w - 0.012f, y + 0.012f, 0.28f, Theme.Cyan, Font.ChaletLondon, Alignment.Right);
            Theme.Text(v.Name.ToUpperInvariant(), x + 0.014f, y + 0.01f, 0.62f, Color.White, Font.ChaletComprimeCologne);
            Theme.Text(VehicleRegistry.ClassName(v.Class).ToUpperInvariant() + "   ·   TIER " + v.Tier + (item.Badge != null ? "   ·   ~y~" + item.Badge : ""), x + 0.014f, y + 0.058f, 0.3f, Theme.Cyan);
            float by = y + 0.1f;
            Theme.StatBar("TOP SPEED", v.MaxSpeed / maxSpeed, x + 0.014f, by, w - 0.03f); by += 0.03f;
            Theme.StatBar("ACCELERATION", v.Acceleration / maxAccel, x + 0.014f, by, w - 0.03f); by += 0.03f;
            Theme.StatBar("BRAKING", v.Braking / maxBrake, x + 0.014f, by, w - 0.03f); by += 0.03f;
            Theme.StatBar("GRIP", v.Traction / maxGrip, x + 0.014f, by, w - 0.03f); by += 0.04f;
            if (d.IsUsedMarket && item.Preview != null)
            {
                int cond = (int)Math.Round((item.Preview.EngineHealth + item.Preview.BodyHealth) / 20f);
                Theme.Text("CONDITION " + cond + "%    ODOMETER " + item.Preview.Stats.DistanceKm.ToString("0") + " KM    " + item.Preview.Mods.Count + " MODS", x + 0.014f, by, 0.27f, Color.White);
                Theme.Text("Seller: " + item.Seller, x + 0.014f, by + 0.027f, 0.27f, Theme.TextDim);
            }
            Theme.Rect(x + 0.014f, y + h - 0.07f, w - 0.028f, 0.001f, Theme.Line);
            string price = sold ? "~r~SOLD" : "$" + item.Price.ToString("N0") + (item.ListPrice > item.Price ? "  ~c~$" + item.ListPrice.ToString("N0") : "");
            Theme.Text(price, x + 0.014f, y + h - 0.062f, 0.7f, Color.White, Font.ChaletComprimeCologne);
            if (note != null && Game.GameTime < noteUntil)
            {
                Theme.Card(0.36f, 0.2f, 0.28f, 0.05f);
                Theme.Text(note, 0.5f, 0.212f, 0.3f, Color.White, Font.ChaletLondon, Alignment.Center, 0.26f);
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
