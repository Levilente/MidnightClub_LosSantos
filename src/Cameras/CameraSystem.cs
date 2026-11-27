using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Core;
using MidnightClub.Persistence;
using MidnightClub.Vehicles;
using Font = GTA.UI.Font;

namespace MidnightClub.Cameras
{
    public enum CameraMode { Bumper, Hood, Roof, Cockpit, CloseChase, FarChase, ActionChase, GameDefault }

    public sealed class CameraProfile
    {
        public string Name, Layers; public bool Chase;
        public float Height, RadiusExtra, TargetHeight, LookAhead, BaseFov, Pitch;
    }

    /// <summary>Driving cameras built from the player's Forza Horizon camera files (CameraSettings.ini +
    /// CameraPhysics.xml): chase cams use Forza's heights above ground, radius / target offsets, yaw and height
    /// springs, and every enabled physics layer (G-forces, speed FOV, slip, suspension pitch/roll, collisions, noise).
    /// Mounted cams (bumper, hood, roof, cockpit) follow the body's pitch and roll exactly.</summary>
    public sealed class CameraSystem
    {
        public static CameraSystem Instance { get; private set; }
        public static float BoostFov, BoostShake;    // set per frame by nitrous / abilities
        /// <summary>Set while another script camera (race intro, finish cinematic) owns the screen.</summary>
        public static bool Suspended;
        public static string DataDir;

        private readonly SaveManager saves;
        private readonly VehicleManager vehicles;
        private readonly Config cfg;
        private Camera cam;
        private CameraMode mode = CameraMode.CloseChase;
        private bool active, attached;
        private int viewPressedAt, labelUntil, measuredHandle;
        private float time, lastLookInput;
        private float lookYaw, lookPitch;
        private Vector3 hoodLocal, bumperLocal, cockpitLocal, roofLocal; private float groundOff, halfLen;
        private Dictionary<string, ForzaLayers> layerSets;
        private readonly CamInputs inp = new CamInputs();
        // chase state
        private float camYaw, camYawVel, collide = 99f, collideVel, fov;
        private float lastSpeed, gLong, gLat, lastHeading, airTime, lastVz, pitchAvg, rollAvg; private Vector3 lastVel;

        private static CameraProfile[] profiles;
        private static CameraProfile[] Profiles
        {
            get
            {
                if (profiles != null) return profiles;
                Func<string, float, float> S = (k, d) => ForzaSettings.Get(k, d);
                profiles = new[]
                {
                    new CameraProfile { Name = "Bumper", Layers = "BumperHigh", BaseFov = S("BumperHighCam\\FOV", 68f), Pitch = -1.5f },
                    new CameraProfile { Name = "Hood", Layers = "Hood", BaseFov = S("HoodCam\\FOV", 72f), Pitch = -S("HoodCam\\CamPitchOffset", 0.04f) * 57.3f },
                    new CameraProfile { Name = "Roof", Layers = "Hood", BaseFov = 66f, Pitch = -5f },
                    new CameraProfile { Name = "Cockpit", Layers = "DriverCam", BaseFov = Math.Min(80f, S("Driver\\FOV", 85f)), Pitch = 0f },
                    new CameraProfile { Name = "Chase near", Layers = "FollowCam", Chase = true, Height = (S("FollowLowCam\\HeightMinAboveGround", 1.6f) + S("FollowLowCam\\HeightMaxAboveGround", 1.65f)) / 2f,
                        RadiusExtra = 3.3f + S("FollowLowCam\\RadiusOffsetGlobal", -0.6f), TargetHeight = 1.0f + 0.25f * S("FollowLowCam\\TargetYOffsetGlobal", 0.7f), LookAhead = 2.0f, BaseFov = S("FollowLowCam\\FOV", 55f) },
                    new CameraProfile { Name = "Chase far", Layers = "FollowCam2", Chase = true, Height = (S("FollowHighCam\\HeightMinAboveGround", 2.03f) + S("FollowHighCam\\HeightMaxAboveGround", 2.03f)) / 2f,
                        RadiusExtra = 4.6f + S("FollowHighCam\\RadiusOffsetGlobal", -0.3f), TargetHeight = Math.Max(0.45f, 1.0f + 0.25f * S("FollowHighCam\\TargetYOffsetGlobal", -1.8f)), LookAhead = 2.5f, BaseFov = S("FollowHighCam\\FOV", 60f) },
                    new CameraProfile { Name = "Action", Layers = "FollowCam", Chase = true, Height = 1.3f, RadiusExtra = 2.6f, TargetHeight = 0.95f, LookAhead = 3.0f, BaseFov = 62f },
                };
                return profiles;
            }
        }

        public CameraSystem(SaveManager saves, VehicleManager vehicles, Config cfg)
        {
            this.saves = saves; this.vehicles = vehicles; this.cfg = cfg; Instance = this;
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e =>
            {
                if (e.To == GameMode.MIDNIGHT_CLUB && saves.Data != null) mode = saves.Data.CameraView <= 0 ? CameraMode.CloseChase : (CameraMode)Math.Max(0, Math.Min((int)CameraMode.GameDefault, saves.Data.CameraView - 1));
                if (e.To != GameMode.MIDNIGHT_CLUB) Destroy();
                active = false;
            });
        }

        public CameraMode Mode { get { return mode; } }

        public void Release()
        {
            ShowDriver(null);
            if (cam != null && active)
            {
                if (attached) { Function.Call(Hash.DETACH_CAM, cam); attached = false; }
                cam.IsActive = false; Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0);
            }
            active = false;
        }

        private bool driverHidden; private int hiddenPed;
        private void ShowDriver(Ped ped)
        {
            if (!driverHidden) return;
            driverHidden = false;
            try { if (Function.Call<bool>(Hash.DOES_ENTITY_EXIST, hiddenPed)) Function.Call(Hash.SET_ENTITY_VISIBLE, hiddenPed, true, false); } catch { }
        }

        private void Destroy() { Release(); if (cam != null) { try { cam.Delete(); } catch { } cam = null; } }

        private void Tick()
        {
            var ped = Game.Player.Character; var car = vehicles.Current;
            float dt = Math.Max(0.001f, Math.Min(0.05f, Game.LastFrameTime / Math.Max(0.1f, Game.TimeScale)));
            time += dt;

            Game.DisableControlThisFrame(Control.NextCamera);
            if (UI.Input.JustPressed(Control.NextCamera)) viewPressedAt = Game.GameTime;
            if (UI.Input.JustReleased(Control.NextCamera) && Game.GameTime - viewPressedAt < 450) Cycle();
            if (Game.GameTime < labelUntil) DrawLabel();
            if (Suspended) return;

            if (mode == CameraMode.GameDefault || !cfg.CustomCameras || car == null || !car.Exists() || !ped.IsInVehicle(car) || Game.IsCutsceneActive)
            { if (active) Release(); BoostFov = BoostShake = 0f; return; }

            if (layerSets == null) { var dir = DataDir; ForzaSettings.Load(dir); profiles = null; layerSets = ForzaLayers.LoadAll(dir); }
            if (cam == null || !cam.Exists()) { cam = Camera.Create(ScriptedCameraNameHash.DefaultScriptedCamera, car.Position, Vector3.Zero, 60f, false, EulerRotationOrder.YXZ); attached = false; active = false; }
            // another script (welcome screen, lobby, photo mode...) may have switched script cameras off: take the screen back
            if (active && !Function.Call<bool>(Hash.IS_CAM_RENDERING, cam)) active = false;
            var p = Profiles[(int)mode];
            if (!p.Chase && car.Model.IsBike) p = Profiles[(int)CameraMode.CloseChase];   // bikes: no bumper/hood/cockpit mounts
            if (car.Handle != measuredHandle) Measure(car, ped);
            if (!active)
            {
                cam.IsActive = true;
                Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false, 0);
                active = true;
                camYaw = car.Heading; camYawVel = 0; collide = 99f; fov = p.BaseFov; lastSpeed = car.Speed; lastHeading = car.Heading; lastVel = car.Velocity;
                pitchAvg = car.Rotation.X; rollAvg = car.Rotation.Y;
                foreach (var l in layerSets.Values) l.Reset();
            }
            UpdateLook(dt);
            Inputs(car, dt);
            ForzaLayers set; layerSets.TryGetValue(p.Layers, out set);
            var o = set != null ? set.Update(inp, time, dt, cfg.CameraGForce) : new CamOutput();
            o.Fov *= cfg.CameraFovScale;

            if (mode != CameraMode.Cockpit || p.Chase) ShowDriver(ped);
            if (p.Chase) Chase(car, p, o, dt); else Mounted(car, ped, p, o, dt);
            BoostFov = 0f; BoostShake = 0f;
        }

        /// <summary>Forza layer inputs from the car's motion.</summary>
        private void Inputs(Vehicle car, float dt)
        {
            float speed = car.Speed; var vel = car.Velocity;
            float a = (speed - lastSpeed) / dt; lastSpeed = speed; if (Math.Abs(a) > 80f) a = 0f;
            gLong += (Clamp(a / 9.81f, -1.5f, 1.5f) - gLong) * Math.Min(1f, dt * 8f);
            float yawRate = DeltaAngle(lastHeading, car.Heading) / dt * (float)(Math.PI / 180.0); lastHeading = car.Heading; if (Math.Abs(yawRate) > 6f) yawRate = 0f;
            gLat += (Clamp(speed * yawRate / 9.81f, -1.5f, 1.5f) - gLat) * Math.Min(1f, dt * 8f);
            inp.GLong = gLong; inp.GLat = -gLat;
            inp.SpeedMph = speed * 2.23694f;
            float vmax = Math.Max(20f, Function.Call<float>(Hash.GET_VEHICLE_ESTIMATED_MAX_SPEED, car));
            inp.SpeedNorm = speed / vmax;
            float slip = 0f;
            if (speed > 3f) { var f = car.ForwardVector; var v = vel; v.Z = 0; f.Z = 0; float ang = (float)(Math.Atan2(f.X * v.Y - f.Y * v.X, f.X * v.X + f.Y * v.Y) * 180.0 / Math.PI); slip = ang / 15f; }
            inp.SlipLat = Clamp(slip, -4f, 4f); inp.Slip = Math.Abs(inp.SlipLat) * 0.8f; inp.SlipLong = 0f;
            bool air = car.IsInAir; airTime = air ? airTime + dt : 0f; inp.TimeInAir = airTime;
            float vz = vel.Z; inp.VertAccel = Clamp((vz - lastVz) / dt / 9.81f, -3f, 3f); lastVz = vz;
            var rot = car.Rotation; inp.Pitch = rot.X;
            pitchAvg += (rot.X - pitchAvg) * Math.Min(1f, dt * 1.2f); rollAvg += (rot.Y - rollAvg) * Math.Min(1f, dt * 1.2f);
            inp.SuspPitch = Clamp(rot.X - pitchAvg, -6f, 6f); inp.SuspRoll = Clamp(rot.Y - rollAvg, -10f, 10f);
            inp.Stationary = speed < 0.5f ? 1f : 0f;
            // collisions: sudden velocity change while touching something
            var dv = vel - lastVel; lastVel = vel;
            if (Function.Call<bool>(Hash.HAS_ENTITY_COLLIDED_WITH_ANYTHING, car) && dv.Length() > 2.5f)
            {
                inp.CollideLong = Vector3.Dot(dv, car.ForwardVector) / 10f; inp.CollideLat = Vector3.Dot(dv, car.RightVector) / 10f; inp.Collide = dv.Length() / 10f;
            }
            else { inp.CollideLong = inp.CollideLat = inp.Collide = 0f; }
        }

        private void Cycle()
        {
            mode = (CameraMode)(((int)mode + 1) % ((int)CameraMode.GameDefault + 1));
            if (saves.Data != null) { saves.Data.CameraView = (int)mode + 1; saves.IsDirty = true; }
            lookYaw = lookPitch = 0;
            if (active) { var c = vehicles.Current; if (c != null && c.Exists()) camYaw = c.Heading; collide = 99f; fov = Profiles[Math.Min((int)mode, Profiles.Length - 1)].BaseFov; if (attached) { Function.Call(Hash.STOP_CAM_POINTING, cam); } }
            if (mode == CameraMode.GameDefault) Release();
            labelUntil = Game.GameTime + 1500;
        }

        private void DrawLabel()
        {
            string n = mode == CameraMode.GameDefault ? "GAME DEFAULT" : Profiles[(int)mode].Name.ToUpperInvariant();
            UI.Ds.T("CAMERA  ·  " + n, 0.5f, 0.88f, 0.34f, UI.Ds.Text, UI.Ds.Display, Alignment.Center);
        }

        private void UpdateLook(float dt)
        {
            float x = Game.GetControlValueNormalized(Control.LookLeftRight), y = Game.GetControlValueNormalized(Control.LookUpDown);
            if (Game.LastInputMethod == InputMethod.GamePad)
            {
                lookYaw += (-x * 160f - lookYaw) * Math.Min(1f, dt * 10f);
                lookPitch += (-y * 25f - lookPitch) * Math.Min(1f, dt * 10f);
                return;
            }
            if (Math.Abs(x) > 0.02f || Math.Abs(y) > 0.02f)
            {
                lookYaw = Clamp(lookYaw - x * 7f, -170f, 170f);
                lookPitch = Clamp(lookPitch - y * 4f, -30f, 20f);
                lastLookInput = time;
            }
            else if (time - lastLookInput > 1.2f) { lookYaw += -lookYaw * Math.Min(1f, dt * 4f); lookPitch += -lookPitch * Math.Min(1f, dt * 4f); }
        }

        // ---------------------------------------------------------------- chase (Forza follow cam)
        private void Chase(Vehicle car, CameraProfile p, CamOutput o, float dt)
        {
            float speed = car.Speed;
            // yaw spring (FollowCam YRotSpringK / D / MaxSpeed): the camera trails the car's heading, shows a bit of drift
            string sec = p.Layers == "FollowCam2" ? "FollowHighCam" : "FollowLowCam";
            float k = ForzaSettings.Get(sec + "\\Spring\\YRotSpringK", 8.5f), dr = ForzaSettings.Get(sec + "\\Spring\\YRotSpringD", 1.2f), maxS = ForzaSettings.Get(sec + "\\Spring\\YRotMaxSpeed", 4.05f) * 57.3f;
            float target = car.Heading;
            var vel = car.Velocity;
            if (speed > 4f && Vector3.Dot(vel, car.ForwardVector) > 0f) target = LerpAngle(car.Heading, (float)(Math.Atan2(-vel.X, vel.Y) * 180.0 / Math.PI), 0.3f);
            float err = DeltaAngle(camYaw, target);
            camYawVel += (err * k - camYawVel * dr * 2f * (float)Math.Sqrt(k)) * dt;
            camYawVel = Clamp(camYawVel, -maxS, maxS);
            camYaw += camYawVel * dt;
            float rel = DeltaAngle(car.Heading, camYaw);
            if (Math.Abs(rel) > 35f) { camYaw = car.Heading + Math.Sign(rel) * 35f; rel = Math.Sign(rel) * 35f; camYawVel *= 0.5f; }

            bool behind = Game.IsControlPressed(Control.VehicleLookBehind);
            float orbit = behind ? 180f : rel + lookYaw + o.CarYpr.X;
            float radius = halfLen + p.RadiusExtra - o.CarXyz.Z;     // Forza Z = forward: + pulls the camera in
            float height = p.Height - groundOff + o.CarXyz.Y;
            var anchor = car.Position + new Vector3(0, 0, 0.4f);
            var wantWorld = car.GetOffsetPosition(Arm(orbit, radius, o.CarXyz.X, height));
            var hit = World.Raycast(anchor, wantWorld, IntersectFlags.Map, car);
            float allowed = hit.DidHit ? Math.Max(1.6f, hit.HitPosition.DistanceTo(anchor) - 0.35f) : 99f;
            collide = allowed < collide ? allowed : SmoothDamp(collide, allowed, ref collideVel, 0.45f, dt);
            float arm = Math.Min(radius, collide);
            var local = Arm(orbit, arm, o.CarXyz.X, height);
            // camera-space position offsets (Forza X right, Y up, Z forward) rotated by the orbit
            double orr = orbit * Math.PI / 180.0; float cs = (float)Math.Cos(orr), sn = (float)Math.Sin(orr);
            local += new Vector3(o.CamXyz.X * cs - o.CamXyz.Z * sn * 0f, o.CamXyz.Z * cs + o.CamXyz.X * sn, o.CamXyz.Y);
            Function.Call(Hash.ATTACH_CAM_TO_ENTITY, cam, car, local.X, local.Y, local.Z, true);
            attached = true;
            // look at a point ahead of the car, then add the layer rotations
            double lr = (behind ? 180f : rel * 0.5f + lookYaw * 0.6f) * Math.PI / 180.0;
            var lookLocal = new Vector3((float)(-Math.Sin(lr) * p.LookAhead), (float)(Math.Cos(lr) * p.LookAhead), p.TargetHeight - groundOff - lookPitch * 0.04f);
            var camW = car.GetOffsetPosition(local); var tgtW = car.GetOffsetPosition(lookLocal);
            var d = tgtW - camW;
            float pitch = (float)(Math.Atan2(d.Z, Math.Sqrt(d.X * d.X + d.Y * d.Y)) * 180.0 / Math.PI) - (o.CarYpr.Y + o.CamYpr.Y);
            float yawW = (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI) + o.CamYpr.X;
            float roll = o.CarYpr.Z * 0.6f + o.CamYpr.Z;
            if (BoostShake > 0f) { pitch += Noise(31f) * BoostShake * 0.4f; yawW += Noise(23f) * BoostShake * 0.3f; }
            Function.Call(Hash.SET_CAM_ROT, cam, pitch, roll, yawW, 2);
            fov = SmoothDamp(fov, Clamp(p.BaseFov + o.Fov + BoostFov, 30f, 120f), ref fovVel, 0.25f, dt);
            cam.FieldOfView = fov;
        }
        private float fovVel;

        /// <summary>Camera position in car space for an orbit angle (deg, + = left of the car's heading).</summary>
        private static Vector3 Arm(float rel, float dist, float lat, float height)
        {
            double r = rel * Math.PI / 180.0; float s = (float)Math.Sin(r), c = (float)Math.Cos(r);
            return new Vector3(dist * s + lat * c, -dist * c + lat * s, height);
        }

        // ---------------------------------------------------------------- mounted (bumper, hood, roof, cockpit)
        private void Mounted(Vehicle car, Ped ped, CameraProfile p, CamOutput o, float dt)
        {
            var local = mode == CameraMode.Bumper ? bumperLocal : mode == CameraMode.Hood ? hoodLocal : mode == CameraMode.Roof ? roofLocal : cockpitLocal;
            bool behind = Game.IsControlPressed(Control.VehicleLookBehind);
            float maxLook = mode == CameraMode.Cockpit ? 110f : 70f;
            float ry = behind ? 180f : Clamp(lookYaw, -maxLook, maxLook);
            if (mode == CameraMode.Cockpit)
            {
                if (!driverHidden) { Function.Call(Hash.SET_ENTITY_VISIBLE, ped, false, false); driverHidden = true; hiddenPed = ped.Handle; }
                Function.Call(Hash.SET_ENTITY_LOCALLY_INVISIBLE, ped);
            }
            bool cockpit = mode == CameraMode.Cockpit;
            float layerScale = cockpit ? 0.7f : 0f;   // hood / bumper / roof: rigidly fixed to the body, only the speed FOV moves
            local += new Vector3(o.CarXyz.X + o.CamXyz.X, -(o.CarXyz.Z + o.CamXyz.Z), o.CarXyz.Y + o.CamXyz.Y) * layerScale;
            Function.Call(Hash.ATTACH_CAM_TO_ENTITY, cam, car, local.X, local.Y, local.Z, true);
            attached = true;
            // rotation follows the body exactly (pitch AND roll), plus look and the layer rotations
            var r = car.Rotation;
            float pitch = r.X + p.Pitch + lookPitch * 0.6f - (o.CarYpr.Y + o.CamYpr.Y) * layerScale;   // Forza pitch sign is opposite to GTA's
            float roll = r.Y + (o.CarYpr.Z + o.CamYpr.Z) * layerScale;
            float yaw = r.Z + ry + (o.CarYpr.X + o.CamYpr.X) * layerScale;
            Function.Call(Hash.SET_CAM_ROT, cam, pitch, roll, yaw, 2);
            float speedFov = (float)Math.Pow(Clamp(car.Speed * 3.6f / 300f, 0f, 1f), 1.2) * 14f * cfg.CameraFovScale;
            fov = SmoothDamp(fov, Clamp(p.BaseFov + (cockpit ? o.Fov * 0.4f : speedFov) + BoostFov * 0.5f, 30f, 115f), ref fovVel, 0.25f, dt);
            cam.FieldOfView = fov;
        }

        private void Measure(Vehicle car, Ped ped)
        {
            measuredHandle = car.Handle;
            Vector3 min, max; car.Model.GetDimensions(out min, out max);
            groundOff = Math.Max(0.2f, -min.Z);
            halfLen = Math.Max(1.6f, (max.Y - min.Y) / 2f);
            int bonnet = Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, car, "bonnet");
            if (bonnet >= 0)
            {
                var b = car.GetPositionOffset(Function.Call<Vector3>(Hash.GET_WORLD_POSITION_OF_ENTITY_BONE, car, bonnet));
                hoodLocal = new Vector3(0f, b.Y - 0.25f, b.Z + 0.40f + cfg.HoodCamHeight);
            }
            else hoodLocal = new Vector3(0f, max.Y * 0.3f, max.Z * 0.7f + cfg.HoodCamHeight);
            bumperLocal = new Vector3(0f, max.Y + ForzaSettings.Get("BumperHighCam\\CamZOffset", -0.22f) + 0.25f, ForzaSettings.Get("BumperHighCam\\RestHeightAboveGround", 0.72f) - groundOff);
            roofLocal = new Vector3(0f, -0.25f, max.Z + 0.30f);
            var head = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS, ped, 31086, 0f, 0f, 0f);   // SKEL_Head
            var h = car.GetPositionOffset(head);
            cockpitLocal = (h.Length() > 0.1f && h.Length() < 4f) ? new Vector3(h.X, h.Y + 0.14f, h.Z + cfg.CockpitCamHeight) : new Vector3(-0.35f, 0f, max.Z * 0.5f);
        }

        // ---------------------------------------------------------------- math
        private float Noise(float f) { return (float)(Math.Sin(time * f) * 0.6 + Math.Sin(time * f * 2.3 + 1.7) * 0.4); }
        private static Vector3 Dir(float h) { double r = h * Math.PI / 180.0; return new Vector3((float)-Math.Sin(r), (float)Math.Cos(r), 0f); }
        private static Vector3 LookRotation(Vector3 from, Vector3 to)
        {
            var d = to - from;
            return new Vector3((float)(Math.Atan2(d.Z, Math.Sqrt(d.X * d.X + d.Y * d.Y)) * 180.0 / Math.PI), 0f, (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI));
        }
        private static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }
        private static float DeltaAngle(float a, float b) { return ((b - a) % 360f + 540f) % 360f - 180f; }
        private static float LerpAngle(float a, float b, float t) { return a + DeltaAngle(a, b) * t; }

        /// <summary>Critically damped spring (Unity-style SmoothDamp): fast, no overshoot, frame-rate independent.</summary>
        private static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float dt)
        {
            smoothTime = Math.Max(0.0001f, smoothTime);
            float omega = 2f / smoothTime, x = omega * dt;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            float change = current - target;
            float temp = (velocity + omega * change) * dt;
            velocity = (velocity - omega * temp) * exp;
            return target + (change + temp) * exp;
        }
        private static float SmoothDampAngle(float current, float target, ref float velocity, float smoothTime, float dt)
        { return SmoothDamp(current, current + DeltaAngle(current, target), ref velocity, smoothTime, dt); }
    }
}
