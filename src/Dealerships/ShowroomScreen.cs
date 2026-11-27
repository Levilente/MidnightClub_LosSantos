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
    /// <summary>Showroom: the whole current stock parked in an arc on a lit stage; the camera flies between cars and orbits freely,
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
        public int StartIndex;
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

        // ------------------------------------------------------------------ stage
        // The whole current offer is parked in an arc on a quiet, flat stage (LSIA apron), lit like a photo studio.
        // Switching cars flies the camera over; the camera can be orbited and zoomed freely. Nothing respawns.
        private static readonly Vector3 StageCenter = new Vector3(-1336.0f, -3044.0f, 13.94f);
        private readonly List<Vehicle> cars = new List<Vehicle>();
        private readonly List<Vector3> carPos = new List<Vector3>();
        private Vector3 camPos, camLook; private float yaw, pitch = 8f, zoom = 1f, idle;
        private float stageZ;

        public void Run()
        {
            stock = mgr.Stock(d);
            index = Math.Max(0, Math.Min(StartIndex, stock.Count - 1));
            if (stock.Count == 0) { UI.Toast.Post(d.Name + " has nothing in stock."); return; }
            var ped = Game.Player.Character;
            var own = vehicles.Current;
            if (Cameras.CameraSystem.Instance != null) Cameras.CameraSystem.Instance.Release();
            Screen.FadeOut(400); Script.Wait(450);
            spot = own != null && own.Exists() ? own.Position : ped.Position;
            Hide(own, ped, true);
            try { BuildStage(); }
            catch (Exception ex) { Core.Log.Error("Showroom stage", ex); }
            cam = Camera.Create(ScriptedCameraNameHash.DefaultScriptedCamera, Vector3.Zero, Vector3.Zero, 40f, true, EulerRotationOrder.YXZ);
            Target(out camPos, out camLook); cam.Position = camPos; cam.PointAt(camLook);
            Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false, 0);
            Function.Call(Hash.DISPLAY_RADAR, false);
            Script.Wait(200);
            Screen.FadeIn(500);
            try
            {
                while (!done)
                {
                    Game.DisableAllControlsThisFrame();
                    foreach (var c in new[] { Control.FrontendUp, Control.FrontendDown, Control.FrontendLeft, Control.FrontendRight, Control.FrontendAccept, Control.FrontendCancel, Control.FrontendX, Control.FrontendLt, Control.FrontendRt })
                        Game.EnableControlThisFrame(c);
                    Hud.HideComponentThisFrame(HudComponent.HelpText);
                    if (menus.IsOpen) menus.Tick(); else Input();
                    CameraTick();
                    StageTick();
                    DrawHud();
                    Script.Wait(0);
                }
            }
            finally
            {
                Screen.FadeOut(300); Script.Wait(350);
                ClearStage();
                Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0);
                Function.Call(Hash.DISPLAY_RADAR, true);
                if (cam != null) { cam.IsActive = false; cam.Delete(); }
                var me = Game.Player.Character; var mine = vehicles.Current;
                if (mine != null && mine.Exists() && !me.IsInVehicle(mine)) me.SetIntoVehicle(mine, VehicleSeat.Driver);
                Hide(mine, me, false);
                Screen.FadeIn(400);
            }
        }

        private void BuildStage()
        {
            Function.Call(Hash.SET_FOCUS_POS_AND_VEL, StageCenter.X, StageCenter.Y, StageCenter.Z, 0f, 0f, 0f);
            Function.Call(Hash.NEW_LOAD_SCENE_START_SPHERE, StageCenter.X, StageCenter.Y, StageCenter.Z, 80f, 0);
            int t0 = Game.GameTime;
            while (!Function.Call<bool>(Hash.IS_NEW_LOAD_SCENE_LOADED) && Game.GameTime - t0 < 4000) Script.Wait(50);
            Function.Call(Hash.NEW_LOAD_SCENE_STOP);
            stageZ = UI.WorldMarker.Ground(StageCenter + new Vector3(0, 0, 3f));
            if (Math.Abs(stageZ - StageCenter.Z) > 5f) stageZ = StageCenter.Z;
            Function.Call(Hash.CLEAR_AREA_OF_VEHICLES, StageCenter.X, StageCenter.Y, StageCenter.Z, 90f, false, false, false, false, false, false, 0);
            int n = stock.Count;
            float span = (float)(Math.PI * Math.Min(1.25, 0.35 + n * 0.09)), radius = Math.Max(9f, n * 4.4f / span);
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0.5f : i / (float)(n - 1);
                double ang = Math.PI / 2 - span / 2 + span * t;
                var p = StageCenter + new Vector3((float)Math.Cos(ang) * radius, (float)Math.Sin(ang) * radius, 0f);
                p.Z = stageZ + 0.4f;
                var toC = StageCenter - p;
                float heading = (float)(Math.Atan2(-toC.X, toC.Y) * 180.0 / Math.PI) + 25f;   // nose to the centre, 3/4 on
                carPos.Add(new Vector3(p.X, p.Y, stageZ));
                Vehicle v = null;
                try
                {
                    var item = stock[i];
                    if (item.Preview == null) item.Preview = item.MakeRecord();
                    v = vehicles.SpawnDisplay(item.Preview, p, heading);
                    Function.Call(Hash.SET_VEHICLE_LIGHTS, v, 1);
                }
                catch (Exception ex) { Core.Log.Error("Showroom display " + stock[i].Info.Id, ex); }
                cars.Add(v);
            }
            Script.Wait(300);
            foreach (var v in cars) if (v != null && v.Exists()) Function.Call(Hash.FREEZE_ENTITY_POSITION, v, true);
        }

        private void ClearStage()
        {
            foreach (var v in cars) try { if (v != null && v.Exists()) v.Delete(); } catch { }
            cars.Clear(); carPos.Clear();
            Function.Call(Hash.CLEAR_FOCUS);
        }

        private Vehicle Selected { get { return index < cars.Count ? cars[index] : null; } }

        /// <summary>Where the camera wants to be for the selected car: in front-left, orbited by yaw/pitch, zoomed.</summary>
        private void Target(out Vector3 pos, out Vector3 look)
        {
            var v = Selected; var c = index < carPos.Count ? carPos[index] : StageCenter;
            float len = v != null && v.Exists() ? Math.Max(2.2f, v.Model.Dimensions.frontTopRight.Y) : 2.4f;
            look = c + new Vector3(0, 0, 0.55f);
            var toC = StageCenter - c; toC.Z = 0; if (toC.Length() < 0.1f) toC = new Vector3(0, 1, 0); toC.Normalize();
            double baseAng = Math.Atan2(toC.Y, toC.X) + (-35.0 + yaw) * Math.PI / 180.0;
            float dist = len * 2.3f * zoom, pr = pitch * (float)Math.PI / 180f;
            pos = c + new Vector3((float)(Math.Cos(baseAng) * Math.Cos(pr) * dist), (float)(Math.Sin(baseAng) * Math.Cos(pr) * dist), 0.6f + (float)Math.Sin(pr) * dist);
        }

        private void CameraTick()
        {
            float dt = Math.Min(0.05f, Game.LastFrameTime);
            // free orbit: right stick / mouse; zoom: triggers / mouse wheel
            float lx = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)Control.LookLeftRight);
            float ly = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)Control.LookUpDown);
            bool pad = Game.LastInputMethod == InputMethod.GamePad;
            float k = pad ? 140f * dt : 40f;
            yaw += lx * k; pitch = Math.Max(-2f, Math.Min(40f, pitch + ly * k * 0.6f));
            if (Math.Abs(lx) + Math.Abs(ly) > 0.02f) idle = 0f; else idle += dt;
            if (idle > 6f) yaw += dt * 6f;                                          // slow showcase drift when idle
            if (Game.IsControlPressed(Control.FrontendRt) || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)Control.CursorScrollUp)) zoom = Math.Max(0.6f, zoom - (pad ? dt : 0.08f));
            if (Game.IsControlPressed(Control.FrontendLt) || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)Control.CursorScrollDown)) zoom = Math.Min(1.8f, zoom + (pad ? dt : 0.08f));
            Vector3 tp, tl; Target(out tp, out tl);
            float sp = 1f - (float)Math.Exp(-4.5f * dt);
            camPos = Vector3.Lerp(camPos, tp, sp);
            camLook = Vector3.Lerp(camLook, tl, 1f - (float)Math.Exp(-6f * dt));
            float cz = UI.WorldMarker.Ground(camPos) + 0.35f; if (camPos.Z < cz) camPos.Z = cz;
            Studio.Frame(cam, camPos, camLook, 0f, 0.12f);
        }

        private void StageTick()
        {
            Function.Call(Hash.CLEAR_AREA_OF_PEDS, StageCenter.X, StageCenter.Y, StageCenter.Z, 60f, 0);
            for (int i = 0; i < cars.Count; i++)
            {
                var v = cars[i]; if (v == null || !v.Exists()) continue;
                bool sel = i == index, sold = mgr.IsSold(stock[i]);
                var g = carPos[i] + new Vector3(0, 0, 0.03f);
                var col = sold ? Color.FromArgb(60, 248, 72, 94) : sel ? Color.FromArgb(150, 168, 85, 247) : Color.FromArgb(45, 168, 85, 247);
                float rr = Math.Max(5f, v.Model.Dimensions.frontTopRight.Y * 2.6f);
                World.DrawMarker(MarkerType.HorizontalCircleFat, g, Vector3.Zero, Vector3.Zero, new Vector3(rr, rr, 1f), col, false, false, false, null, null, false);
                if (sel) Studio.Light(v, camPos);
                else Function.Call(Hash.DRAW_LIGHT_WITH_RANGE, v.Position.X, v.Position.Y, v.Position.Z + 3f, 230, 225, 255, 7f, 1.6f);
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

        private void Input()
        {
            if (UI.Input.JustPressed(Control.FrontendLeft) || UI.Input.JustPressed(Control.FrontendUp)) Move(-1);
            if (UI.Input.JustPressed(Control.FrontendRight) || UI.Input.JustPressed(Control.FrontendDown)) Move(+1);
            if (UI.Input.JustPressed(Control.FrontendCancel)) { done = true; return; }
            var item = stock[index];
            if (UI.Input.JustPressed(Control.FrontendAccept))
            {
                if (mgr.IsSold(item)) { Flash("Already sold."); return; }
                menus.Open(ConfirmMenu(item));
            }
            bool t = Game.IsKeyPressed(Keys.T);
            if ((t && !prevT) || UI.Input.JustPressed(Control.FrontendX)) TestDrive(item);
            prevT = t;
        }

        private void Move(int dir)
        {
            if (stock.Count < 2) return;
            index = (index + dir + stock.Count) % stock.Count;
            yaw = 0f; idle = 0f;
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
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
            Function.Call(Hash.CLEAR_FOCUS);
            Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0);
            Function.Call(Hash.DISPLAY_RADAR, true);
            Vehicle car = null;
            try
            {
                car = vehicles.SpawnDisplay(item.Preview ?? (item.Preview = item.MakeRecord()), spot + new Vector3(0, 0, 0.4f), Game.Player.Character.Heading);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, car, false); car.IsInvincible = false;
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
                    if (UI.Input.JustPressed(Control.FrontendCancel) || Game.IsKeyPressed(Keys.Back)) break;
                    if (!ped.IsInVehicle(car)) ped.SetIntoVehicle(car, VehicleSeat.Driver);
                    int s = (end - Game.GameTime) / 1000;
                    Kit.Glass(0.4f, 0.04f, 0.2f, Th.Px(84f), Th.R_Lg, 1f, true);
                    Ds.TM("TEST DRIVE  " + s / 60 + ":" + (s % 60).ToString("00"), 0.5f, 0.04f + Th.Px(28f), 0.5f, Th.Accent, Ds.Display, Alignment.Center);
                    Ds.TM(item.Info.Name + "   ·   " + (int)(car.Speed * 3.6f) + " KM/H", 0.5f, 0.04f + Th.Px(60f), 0.3f, Th.Text, Ds.Body, Alignment.Center);
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
            Function.Call(Hash.SET_FOCUS_POS_AND_VEL, StageCenter.X, StageCenter.Y, StageCenter.Z, 0f, 0f, 0f);
            Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false, 0);
            Function.Call(Hash.DISPLAY_RADAR, false);
            Script.Wait(500);
            Screen.FadeIn(400);
        }

        // ------------------------------------------------------------------ HUD
        private void DrawHud()
        {
            Theme.FullscreenFrame();
            var item = stock[index]; var v = item.Info; bool sold = mgr.IsSold(item);
            var promo = mgr.PromoClass(d); int days = mgr.DaysUntilRotation(d);
            Studio.Title(d.Tagline.ToUpperInvariant(), d.Name.ToUpperInvariant(), "$" + saves.Data.Cash.ToString("N0"));
            Theme.Text("NEW STOCK IN " + days + (days == 1 ? " DAY" : " DAYS") + (promo.HasValue ? "  ·  " + VehicleRegistry.ClassName(promo.Value).ToUpperInvariant() + " -15%" : ""), 0.955f, 0.125f, 0.26f, promo.HasValue ? Th.Amber : Th.Sub, Font.ChaletLondon, Alignment.Right);
            var right = new System.Collections.Generic.List<string>();
            right.Add(sold ? "SOLD" : "$" + item.Price.ToString("N0"));
            right.Add(item.ListPrice > item.Price ? "WAS $" + item.ListPrice.ToString("N0") : "PRICE");
            if (d.IsUsedMarket && item.Preview != null)
            {
                int cond = (int)Math.Round((item.Preview.EngineHealth + item.Preview.BodyHealth) / 20f);
                right.Add(""); right.Add("CONDITION  " + cond + "%"); right.Add(item.Preview.Stats.DistanceKm.ToString("0") + " KM  ·  " + item.Preview.Mods.Count + " MODS"); right.Add("SELLER  " + item.Seller.ToUpperInvariant());
            }
            Studio.CarStrip(item.Badge != null ? item.Badge.ToUpperInvariant() : "IN STOCK", v.DisplayName.ToUpperInvariant(),
                v.Make.ToUpperInvariant() + "   " + VehicleRegistry.ClassName(v.Class).ToUpperInvariant() + "   TIER " + v.Tier
                    + (Simcade.Active && Selected != null && Selected.Exists() ? "   " + Simcade.Instance.For(Selected).Archetype.ToUpperInvariant() + " HANDLING" : ""),
                new[] { v.MaxSpeed / maxSpeed, v.Acceleration / maxAccel, v.Braking / maxBrake, v.Traction / maxGrip }, right.ToArray(),
                "<  " + (index + 1) + " / " + stock.Count + "  >");
            if (note != null && Game.GameTime < noteUntil)
            {
                Theme.Card(0.36f, 0.14f, 0.28f, 0.05f);
                Ds.TM(note, 0.5f, 0.165f, 0.3f, Th.Text, Ds.Body, Alignment.Center);
            }
            if (!menus.IsOpen) Buttons.Draw(Control.FrontendAccept, "Buy", Control.FrontendCancel, "Leave", Control.FrontendX, "Test drive", Control.LookLeftRight, "Look around", Control.FrontendRt, "Zoom", Control.FrontendRight, "", Control.FrontendLeft, "Browse");
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
        { new TextElement(s, new PointF(x * 1280f, y * 720f), MidnightClub.UI.Ds.Eff(scale), c, f, a, false, true, wrap * 1280f).ScaledDraw(); }
    }
}
