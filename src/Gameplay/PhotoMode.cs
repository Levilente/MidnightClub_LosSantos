using System;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.Persistence;
using MidnightClub.UI;
using MidnightClub.Vehicles;

namespace MidnightClub.Gameplay
{
    /// <summary>Photo mode: frozen world, free orbit camera around your car, lens (FOV, roll, depth of field),
    /// filters, time of day, lights and a shutter that hides the UI (Steam F12 / PrintScreen captures the shot).</summary>
    public static class PhotoMode
    {
        private static readonly string[] FilterNames = { "None", "Filmic", "Cinema", "Neon night", "Warm", "Cold", "Bleach", "Noir", "Vintage", "Dreamy", "Street cam" };
        private static readonly string[] FilterIds = { null, "NG_filmic02", "NG_filmic04", "NG_filmic06", "NG_filmic09", "NG_filmic11", "NG_filmic13", "NG_filmic20", "phone_cam3", "phone_cam7", "phone_cam12" };
        private static readonly string[] Rows = { "FIELD OF VIEW", "ROLL", "DEPTH OF FIELD", "FILTER", "FILTER STRENGTH", "TIME OF DAY", "HEADLIGHTS", "DRIVER", "TAKE PHOTO", "EXIT" };

        public static bool Running { get; private set; }

        public static void Run(SaveManager saves, VehicleManager vehicles)
        {
            var car = vehicles.Current; var ped = Game.Player.Character;
            if (car == null || !car.Exists() || Running) return;
            Running = true;
            if (Cameras.CameraSystem.Instance != null) Cameras.CameraSystem.Instance.Release();
            int h0 = Function.Call<int>(Hash.GET_CLOCK_HOURS), m0 = Function.Call<int>(Hash.GET_CLOCK_MINUTES);
            bool lightsWere = car.AreLightsOn;
            float yaw = car.Heading + 145f, pitch = 8f, dist = 6.5f, fov = 42f, roll = 0f, dof = 0.6f, fstr = 1f, hour = h0 + m0 / 60f;
            int filter = 1, sel = 0; bool lights = lightsWere, driver = true, ui = true;
            int shotAt = -100000, enter = Environment.TickCount;
            var cam = World.CreateCamera(car.Position, Vector3.Zero, fov);
            cam.IsActive = true; Function.Call(Hash.RENDER_SCRIPT_CAMS, true, true, 600, true, false, 0);
            Function.Call(Hash.SET_TIME_SCALE, 0f);
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Camera_Shoot", "Phone_SoundSet_Michael", true);
            try
            {
                while (true)
                {
                    Game.DisableAllControlsThisFrame();
                    Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
                    float dt = Math.Min(0.05f, Function.Call<float>(Hash.GET_FRAME_TIME) / Math.Max(0.001f, Math.Max(Game.TimeScale, 0.001f)));
                    dt = 1f / 60f;   // time scale is 0: use a fixed UI step
                    // ---- orbit (mouse / right stick, WASD / left stick for zoom & orbit)
                    float lx = N(Control.LookLeftRight), ly = N(Control.LookUpDown), mx = N(Control.MoveLeftRight), my = N(Control.MoveUpDown);
                    yaw -= (lx * 4f + mx * 1.6f); pitch = Math.Max(-8f, Math.Min(75f, pitch + ly * 3f));
                    dist = Math.Max(2.2f, Math.Min(18f, dist + my * 0.12f));
                    if (Game.IsControlPressed(Control.CursorScrollUp)) dist = Math.Max(2.2f, dist - 0.4f);
                    if (Game.IsControlPressed(Control.CursorScrollDown)) dist = Math.Min(18f, dist + 0.4f);
                    float yr = yaw * (float)Math.PI / 180f, pr = pitch * (float)Math.PI / 180f;
                    var target = car.Position + new Vector3(0, 0, 0.45f);
                    var pos = target + new Vector3(-(float)Math.Sin(yr) * (float)Math.Cos(pr), (float)Math.Cos(yr) * (float)Math.Cos(pr), (float)Math.Sin(pr)) * dist;
                    float gz = 0f; if (World.GetGroundHeight(pos, out gz) && pos.Z < gz + 0.25f) pos.Z = gz + 0.25f;
                    cam.Position = pos; cam.PointAt(target); cam.FieldOfView = fov;
                    Function.Call(Hash.SET_CAM_ROT, cam, Function.Call<Vector3>(Hash.GET_CAM_ROT, cam, 2).X, roll, Function.Call<Vector3>(Hash.GET_CAM_ROT, cam, 2).Z, 2);
                    // ---- depth of field (focus on the car)
                    Function.Call(Hash.SET_USE_HI_DOF);
                    Function.Call(Hash.SET_CAM_USE_SHALLOW_DOF_MODE, cam, dof > 0.01f);
                    if (dof > 0.01f)
                    {
                        float fd = pos.DistanceTo(target);
                        Function.Call(Hash.SET_CAM_DOF_PLANES, cam, Math.Max(0.1f, fd - 6f * (1.2f - dof)), Math.Max(0.2f, fd - 1.5f), fd + 2f, fd + 2f + 30f * (1.2f - dof));
                        Function.Call(Hash.SET_CAM_DOF_STRENGTH, cam, dof);
                    }
                    // ---- filter + world
                    if (FilterIds[filter] != null) { Function.Call(Hash.SET_TIMECYCLE_MODIFIER, FilterIds[filter]); Function.Call(Hash.SET_TIMECYCLE_MODIFIER_STRENGTH, fstr); }
                    else Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
                    int hh = ((int)Math.Floor(hour) % 24 + 24) % 24; Function.Call(Hash.SET_CLOCK_TIME, hh, (int)((hour - Math.Floor(hour)) * 60) % 60, 0);
                    car.AreLightsOn = lights; Function.Call(Hash.SET_VEHICLE_LIGHTS, car, lights ? 3 : 4);
                    Function.Call(Hash.SET_ENTITY_VISIBLE, ped, driver, false);

                    // ---- input
                    bool shooting = Environment.TickCount - shotAt < 650;
                    if (!shooting && Environment.TickCount - enter > 300)
                    {
                        if (Input.JustPressed(Control.FrontendY) || Input.JustPressed(Control.Jump)) ui = !ui;
                        if (ui)
                        {
                            if (Input.JustPressed(Control.FrontendUp)) { sel = (sel + Rows.Length - 1) % Rows.Length; Snd("NAV_UP_DOWN"); }
                            if (Input.JustPressed(Control.FrontendDown)) { sel = (sel + 1) % Rows.Length; Snd("NAV_UP_DOWN"); }
                            int d = Input.JustPressed(Control.FrontendRight) ? 1 : Input.JustPressed(Control.FrontendLeft) ? -1 : 0;
                            bool held = Game.IsControlPressed(Control.FrontendRight) || Game.IsControlPressed(Control.FrontendLeft);
                            float hd = Game.IsControlPressed(Control.FrontendRight) ? 1 : Game.IsControlPressed(Control.FrontendLeft) ? -1 : 0;
                            switch (sel)
                            {
                                case 0: if (held) fov = Math.Max(10f, Math.Min(90f, fov + hd * 0.6f)); break;
                                case 1: if (held) roll = Math.Max(-30f, Math.Min(30f, roll + hd * 0.4f)); break;
                                case 2: if (held) dof = Math.Max(0f, Math.Min(1f, dof + hd * 0.01f)); break;
                                case 3: if (d != 0) { filter = (filter + d + FilterIds.Length) % FilterIds.Length; Snd("NAV_LEFT_RIGHT"); } break;
                                case 4: if (held) fstr = Math.Max(0f, Math.Min(1f, fstr + hd * 0.01f)); break;
                                case 5: if (held) hour = (hour + hd * 0.05f + 24f) % 24f; break;
                                case 6: if (d != 0) { lights = !lights; Snd("NAV_LEFT_RIGHT"); } break;
                                case 7: if (d != 0) { driver = !driver; Snd("NAV_LEFT_RIGHT"); } break;
                            }
                        }
                        bool accept = Input.JustPressed(Control.FrontendAccept);
                        if ((accept && ui && sel == 8) || Input.JustPressed(Control.FrontendX) || (!ui && accept))
                        {
                            shotAt = Environment.TickCount; saves.Data.PhotosTaken++; saves.IsDirty = true; if (Career.Progression.Instance != null) Career.Progression.Instance.Bump("photo", 1);
                            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Camera_Shoot", "Phone_SoundSet_Michael", true);
                            if (saves.Data.PhotosTaken == 1 && Career.Progression.Instance != null) Career.Progression.Instance.Award(250, "First photo");
                        }
                        if ((accept && ui && sel == 9) || Input.JustPressed(Control.FrontendCancel)) break;
                    }
                    // ---- UI
                    float flash = 1f - Math.Min(1f, (Environment.TickCount - shotAt) / 350f);
                    if (flash > 0) Ds.Rect(0f, 0f, 1f, 1f, Ds.A(Color.White, flash * 0.85f));
                    if (ui && !shooting) Panel(sel, fov, roll, dof, FilterNames[filter], fstr, hour, lights, driver, saves.Data.PhotosTaken);
                    else if (!ui && !shooting) Ds.T("PHOTO MODE  ·  SPACE / Y  SHOW CONTROLS  ·  ENTER / A  SHOOT", 0.5f, 0.95f, 0.26f, Ds.A(Color.White, 0.55f), Ds.Display, GTA.UI.Alignment.Center);
                    Script.Wait(0);
                }
            }
            finally
            {
                Function.Call(Hash.SET_TIME_SCALE, 1f);
                Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
                Function.Call(Hash.SET_CLOCK_TIME, h0, m0, 0);
                try { if (car.Exists()) { Function.Call(Hash.SET_VEHICLE_LIGHTS, car, 0); car.AreLightsOn = lightsWere; } } catch { }
                Function.Call(Hash.SET_ENTITY_VISIBLE, ped, true, false);
                Function.Call(Hash.RENDER_SCRIPT_CAMS, false, true, 500, true, false, 0);
                try { cam.Delete(); } catch { }
                Running = false;
                Snd("BACK");
            }
        }

        private static float N(Control c) { return Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)c); }
        private static void Snd(string s) { Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, s, "HUD_FRONTEND_DEFAULT_SOUNDSET", true); }

        private static void Panel(int sel, float fov, float roll, float dof, string filter, float fstr, float hour, bool lights, bool driver, int photos)
        {
            float x = 0.035f, w = Ds.Px(0.36f), y = 0.2f, rh = 0.042f;
            Ds.Logo(x, 0.07f, 0.7f, 1f, GTA.UI.Alignment.Left, false);
            Ds.T("PHOTO MODE", x, 0.13f, 0.62f, Ds.Text, Ds.Display);
            Ds.T(photos + " SHOTS", x + w, 0.145f, 0.26f, Ds.Muted, Ds.Display, GTA.UI.Alignment.Right);
            int hh = (int)hour; int mm = (int)((hour - hh) * 60);
            string[] vals = { fov.ToString("0") + "°", roll.ToString("0") + "°", (dof * 100).ToString("0") + "%", filter, (fstr * 100).ToString("0") + "%", hh.ToString("00") + ":" + mm.ToString("00"), lights ? "On" : "Off", driver ? "Visible" : "Hidden", "", "" };
            for (int i = 0; i < Rows.Length; i++)
            {
                float yy = y + i * rh + (i >= 8 ? 0.015f : 0f); bool s = i == sel;
                Ds.Rect(x, yy, w, rh - 0.004f, s ? Ds.Violet : Color.FromArgb(205, 12, 11, 20));
                if (s) Ds.Shine(x, yy, w, rh - 0.004f, 0f, 3f, 1f);
                Ds.T(Rows[i], x + Ds.Px(0.014f), yy + 0.009f, 0.3f, s ? Color.White : Ds.Text, Ds.Display);
                if (vals[i] != "") Ds.T((s ? "‹  " : "") + vals[i] + (s ? "  ›" : ""), x + w - Ds.Px(0.014f), yy + 0.009f, 0.3f, s ? Color.White : Ds.Muted, Ds.Display, GTA.UI.Alignment.Right);
            }
            float by = y + Rows.Length * rh + 0.03f;
            Ds.T("MOUSE / RIGHT STICK  ORBIT   ·   W S / LEFT STICK  ZOOM", x, by, 0.24f, Ds.Muted, Ds.Display);
            Ds.T("SPACE / Y  HIDE UI   ·   X  SHOOT   ·   F12 (STEAM)  SAVE SCREENSHOT", x, by + 0.022f, 0.24f, Ds.Muted, Ds.Display);
        }
    }
}
