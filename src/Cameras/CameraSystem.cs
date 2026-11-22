using System;
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
    public enum CameraMode { Bumper, Hood, Cockpit, CloseChase, FarChase, ActionChase, GameDefault }

    /// <summary>Per-mode tuning. Everything here is data so modes can be tweaked from config later.</summary>
    public sealed class CameraProfile
    {
        public string Name;
        public bool Chase;                 // false = mounted to the car
        public float Distance, Height, LookAhead, LookHeight;
        public float YawStiffness;         // how fast a chase cam follows the car's heading (1/s)
        public float BaseFov, SpeedFov, AccelFov;
        public float GPull, GSwing, GRoll, GPitch;   // camera reaction to longitudinal/lateral G
        public float Shake;
    }

    /// <summary>NFS-style driving cameras: bumper, hood, cockpit, close/far/action chase. G-force reactive
    /// (camera pulls back under acceleration, swings out in corners, rolls), speed-dependent FOV and shake,
    /// free look that springs back, look-behind. Cycle with the game's Change View key (V / Back).</summary>
    public sealed class CameraSystem
    {
        public static CameraSystem Instance { get; private set; }

        private readonly SaveManager saves;
        private readonly VehicleManager vehicles;
        private readonly Config cfg;
        private Camera cam;
        private CameraMode mode = CameraMode.CloseChase;
        private bool active;

        // dynamic state
        private Vector3 prevVel, accel; private bool havePrev;
        private float yaw, fov, lookYaw, lookPitch, lastLookInput;
        private float dist, swing, height, roll, pitchTilt;
        private Vector3 mountedRot; private bool haveMountedRot;
        private int vehicleHandle; private Vector3 hoodLocal, bumperLocal, cockpitLocal;
        private float time; private int labelUntil;
        private float collideDist = 99f;

        private static readonly CameraProfile[] Profiles =
        {
            new CameraProfile { Name = "Bumper",       Chase = false, BaseFov = 62, SpeedFov = 16, AccelFov = 4,  GRoll = 1.2f, GPitch = 1.0f, Shake = 1.0f },
            new CameraProfile { Name = "Hood",         Chase = false, BaseFov = 60, SpeedFov = 14, AccelFov = 3,  GRoll = 1.5f, GPitch = 1.2f, Shake = 0.8f },
            new CameraProfile { Name = "Cockpit",      Chase = false, BaseFov = 64, SpeedFov = 10, AccelFov = 3,  GRoll = 2.0f, GPitch = 1.5f, Shake = 0.6f },
            new CameraProfile { Name = "Close chase",  Chase = true,  Distance = 4.6f, Height = 1.35f, LookAhead = 4f, LookHeight = 0.75f, YawStiffness = 6.5f, BaseFov = 58, SpeedFov = 20, AccelFov = 5, GPull = 0.55f, GSwing = 0.45f, GRoll = 1.6f, Shake = 0.7f },
            new CameraProfile { Name = "Far chase",    Chase = true,  Distance = 7.2f, Height = 2.2f,  LookAhead = 5f, LookHeight = 1.0f,  YawStiffness = 4.0f, BaseFov = 55, SpeedFov = 14, AccelFov = 3, GPull = 0.7f,  GSwing = 0.6f,  GRoll = 1.0f, Shake = 0.4f },
            new CameraProfile { Name = "Action chase", Chase = true,  Distance = 3.6f, Height = 0.95f, LookAhead = 6f, LookHeight = 0.55f, YawStiffness = 9.0f, BaseFov = 62, SpeedFov = 28, AccelFov = 9, GPull = 0.9f,  GSwing = 0.8f,  GRoll = 4.0f, Shake = 1.6f },
        };

        public CameraSystem(SaveManager saves, VehicleManager vehicles, Config cfg)
        {
            this.saves = saves; this.vehicles = vehicles; this.cfg = cfg;
            Instance = this;
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e =>
            {
                if (e.To == GameMode.MIDNIGHT_CLUB && saves.Data != null) mode = (CameraMode)Math.Max(0, Math.Min((int)CameraMode.GameDefault, saves.Data.CameraMode));
                if (e.To != GameMode.MIDNIGHT_CLUB) Destroy();
            });
        }

        public CameraMode Mode { get { return mode; } }

        /// <summary>Hand the screen over to another script camera (garage, showroom, creator). Our camera comes back next tick.</summary>
        public void Release()
        {
            if (cam != null && active) { cam.IsActive = false; Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0); }
            active = false; havePrev = false; haveMountedRot = false;
        }

        private void Destroy()
        {
            Release();
            if (cam != null) { try { cam.Delete(); } catch { } cam = null; }
        }

        private void Tick()
        {
            var ped = Game.Player.Character;
            var car = vehicles.Current;
            float dt = Math.Max(0.001f, Math.Min(0.05f, Game.LastFrameTime));
            time += dt;

            // Change View cycles our modes.
            Game.DisableControlThisFrame(Control.NextCamera);
            if (Game.IsControlJustPressed(Control.NextCamera)) Cycle();
            if (Game.GameTime < labelUntil) DrawLabel();

            if (mode == CameraMode.GameDefault || !cfg.CustomCameras || car == null || !car.Exists() || !ped.IsInVehicle(car) || Game.IsCutsceneActive)
            { if (active) Release(); return; }

            if (cam == null || !cam.Exists())
                cam = Camera.Create(ScriptedCameraNameHash.DefaultScriptedCamera, car.Position, Vector3.Zero, 60f, false, EulerRotationOrder.YXZ);
            if (!active)
            {
                cam.IsActive = true;
                Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false, 0);
                active = true;
                yaw = car.Heading; fov = Profiles[(int)mode].BaseFov; dist = Profiles[(int)mode].Distance; height = Profiles[(int)mode].Height;
            }

            UpdatePhysics(car, dt);
            UpdateLook(dt);
            var p = Profiles[(int)mode];
            if (p.Chase) Chase(car, p, dt); else Mounted(car, ped, p, dt);
        }

        private void Cycle()
        {
            mode = (CameraMode)(((int)mode + 1) % ((int)CameraMode.GameDefault + 1));
            if (saves.Data != null) { saves.Data.CameraMode = (int)mode; saves.IsDirty = true; }
            havePrev = false; haveMountedRot = false; lookYaw = lookPitch = 0; collideDist = 99f;
            if (mode == CameraMode.GameDefault) Release();
            else if (active) { var c = vehicles.Current; if (c != null && c.Exists()) yaw = c.Heading; dist = Profiles[(int)mode].Distance; height = Profiles[(int)mode].Height; fov = Profiles[(int)mode].BaseFov; }
            labelUntil = Game.GameTime + 1500;
        }

        private void DrawLabel()
        {
            string n = mode == CameraMode.GameDefault ? "GAME DEFAULT" : Profiles[(int)mode].Name.ToUpperInvariant();
            new TextElement("CAMERA: " + n, new PointF(640f, 640f), 0.42f, Color.FromArgb(255, 170, 90, 255), Font.ChaletComprimeCologne, Alignment.Center, true, true).ScaledDraw();
        }

        // ---------------------------------------------------------------- shared dynamics
        private float longG, latG, speed;

        private void UpdatePhysics(Vehicle car, float dt)
        {
            var vel = car.Velocity;
            speed = vel.Length();
            if (havePrev)
            {
                var raw = (vel - prevVel) * (1f / dt);
                if (raw.Length() > 120f) raw = Vector3.Zero;              // teleport / collision spike
                accel = Vector3.Lerp(accel, raw, Math.Min(1f, dt * 5f));
            }
            prevVel = vel; havePrev = true;
            var f = car.ForwardVector; var r = car.RightVector;
            longG = Clamp(Vector3.Dot(accel, f) / 9.81f, -2f, 2f);
            latG = Clamp(Vector3.Dot(accel, r) / 9.81f, -2f, 2f);
        }

        private void UpdateLook(float dt)
        {
            float x = Game.GetControlValueNormalized(Control.LookLeftRight), y = Game.GetControlValueNormalized(Control.LookUpDown);
            if (Math.Abs(x) > 0.02f || Math.Abs(y) > 0.02f)
            {
                lookYaw = Clamp(lookYaw - x * 7f, -170f, 170f);
                lookPitch = Clamp(lookPitch - y * 4f, -35f, 25f);
                lastLookInput = time;
            }
            else if (time - lastLookInput > 1.2f)
            {   // NFS-style spring back to centre
                lookYaw = Lerp(lookYaw, 0f, dt * 4f);
                lookPitch = Lerp(lookPitch, 0f, dt * 4f);
            }
        }

        private float Shake(float amount, float freq)
        {
            float kmh = speed * 3.6f;
            float s = Clamp((kmh - 60f) / 200f, 0f, 1f) * amount * cfg.CameraShake;
            return s * (float)(Math.Sin(time * freq) * 0.6 + Math.Sin(time * freq * 2.3 + 1.7) * 0.3 + Math.Sin(time * freq * 5.1 + 0.4) * 0.1);
        }

        private float TargetFov(CameraProfile p)
        {
            float kmh = speed * 3.6f;
            float speedPart = (float)Math.Pow(Clamp(kmh / 260f, 0f, 1f), 1.3) * p.SpeedFov;
            float accelPart = Clamp(longG, 0f, 1.2f) * p.AccelFov;
            return Clamp(p.BaseFov + (speedPart + accelPart) * cfg.CameraFovScale, 30f, 110f);
        }

        // ---------------------------------------------------------------- chase cameras
        private void Chase(Vehicle car, CameraProfile p, float dt)
        {
            float gs = cfg.CameraGForce;
            float carYaw = car.Heading;
            // Follow the car's heading with lag; while drifting/reversing slowly the lag shows the car's angle.
            yaw = LerpAngle(yaw, carYaw, Math.Min(1f, dt * p.YawStiffness));
            bool behind = Game.IsControlPressed(Control.VehicleLookBehind);
            float viewYaw = yaw + (behind ? 180f : lookYaw);

            float targetDist = p.Distance + longG * p.GPull * gs + Clamp(speed * 3.6f / 300f, 0f, 1f) * 0.6f;
            dist = Lerp(dist, targetDist, Math.Min(1f, dt * 3f));
            swing = Lerp(swing, -latG * p.GSwing * gs, Math.Min(1f, dt * 3f));
            height = Lerp(height, p.Height - Clamp(longG, -1f, 1f) * 0.12f * gs, Math.Min(1f, dt * 3f));
            roll = Lerp(roll, latG * p.GRoll * gs, Math.Min(1f, dt * 4f));

            var dir = Dir(viewYaw);
            var right = new Vector3(dir.Y, -dir.X, 0f);
            var anchor = car.Position + new Vector3(0, 0, 0.35f);
            var desired = anchor - dir * dist + right * swing + new Vector3(0, 0, height);

            // Keep the camera out of walls: pull in along the line from the car.
            var hit = World.Raycast(anchor, desired, IntersectFlags.Map, car);
            float allowed = hit.DidHit ? Math.Max(1.2f, hit.HitPosition.DistanceTo(anchor) - 0.3f) : 99f;
            collideDist = allowed < collideDist ? allowed : Lerp(collideDist, allowed, Math.Min(1f, dt * 2f));
            var toCam = desired - anchor; float len = toCam.Length();
            if (len > collideDist && len > 0.01f) desired = anchor + toCam * (collideDist / len);

            var look = car.Position + Dir(yaw + (behind ? 180f : lookYaw * 0.5f)) * p.LookAhead + new Vector3(0, 0, p.LookHeight);
            var rot = LookRotation(desired, look);
            rot.X += lookPitch + Shake(p.Shake * 0.5f, 23f);
            rot.Z += Shake(p.Shake * 0.4f, 17f);
            rot.Y = roll + Shake(p.Shake * 0.3f, 13f);

            fov = Lerp(fov, TargetFov(p), Math.Min(1f, dt * 2.5f));
            cam.Position = desired;
            cam.Rotation = rot;
            cam.FieldOfView = fov;
        }

        // ---------------------------------------------------------------- mounted cameras
        private void Mounted(Vehicle car, Ped ped, CameraProfile p, float dt)
        {
            if (car.Handle != vehicleHandle) Measure(car, ped);
            float gs = cfg.CameraGForce;
            Vector3 local = mode == CameraMode.Bumper ? bumperLocal : mode == CameraMode.Hood ? hoodLocal : cockpitLocal;
            // Body sway: the mount moves slightly against the G load (like a head on a neck).
            var sway = new Vector3(-latG * 0.02f * gs, -longG * 0.03f * gs, 0f);
            if (mode == CameraMode.Cockpit) sway *= 2.5f;
            var pos = car.GetOffsetPosition(local + sway);

            var target = car.Rotation;   // X pitch, Y roll, Z yaw
            if (!haveMountedRot) { mountedRot = target; haveMountedRot = true; }
            float k = Math.Min(1f, dt * (mode == CameraMode.Cockpit ? 9f : 14f));
            mountedRot = new Vector3(LerpAngle(mountedRot.X, target.X, k), LerpAngle(mountedRot.Y, target.Y, k), LerpAngle(mountedRot.Z, target.Z, k));

            pitchTilt = Lerp(pitchTilt, -longG * p.GPitch * gs, Math.Min(1f, dt * 4f));
            roll = Lerp(roll, latG * p.GRoll * gs, Math.Min(1f, dt * 4f));
            bool behind = Game.IsControlPressed(Control.VehicleLookBehind);
            float maxLook = mode == CameraMode.Cockpit ? 110f : 60f;
            var rot = new Vector3(
                mountedRot.X + pitchTilt + lookPitch * 0.6f + Shake(p.Shake * 0.4f, 29f),
                mountedRot.Y + roll + Shake(p.Shake * 0.3f, 19f),
                mountedRot.Z + (behind ? 180f : Clamp(lookYaw, -maxLook, maxLook)) + Shake(p.Shake * 0.3f, 21f));

            fov = Lerp(fov, TargetFov(p), Math.Min(1f, dt * 2.5f));
            cam.Position = pos;
            cam.Rotation = rot;
            cam.FieldOfView = fov;
        }

        /// <summary>Works out mount points for this car from its bones and model size (cached per car).</summary>
        private void Measure(Vehicle car, Ped ped)
        {
            vehicleHandle = car.Handle;
            Vector3 min, max;
            car.Model.GetDimensions(out min, out max);

            int bonnet = Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, car, "bonnet");
            if (bonnet >= 0)
            {
                var b = car.GetPositionOffset(Function.Call<Vector3>(Hash.GET_WORLD_POSITION_OF_ENTITY_BONE, car, bonnet));
                hoodLocal = new Vector3(0f, b.Y - 0.15f, b.Z + 0.45f);
            }
            else hoodLocal = new Vector3(0f, max.Y * 0.3f, max.Z * 0.75f);

            bumperLocal = new Vector3(0f, max.Y + 0.05f, min.Z + 0.5f);

            var head = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS, ped, 31086, 0f, 0f, 0f);   // SKEL_Head
            var h = car.GetPositionOffset(head);
            cockpitLocal = (h.Length() > 0.1f && h.Length() < 4f) ? new Vector3(h.X, h.Y + 0.12f, h.Z + 0.07f) : new Vector3(-0.35f, 0f, max.Z * 0.6f);
        }

        // ---------------------------------------------------------------- math
        private static Vector3 Dir(float headingDeg)
        {
            double r = headingDeg * Math.PI / 180.0;
            return new Vector3((float)-Math.Sin(r), (float)Math.Cos(r), 0f);
        }

        private static Vector3 LookRotation(Vector3 from, Vector3 to)
        {
            var d = to - from;
            float yawDeg = (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI);
            float pitchDeg = (float)(Math.Atan2(d.Z, Math.Sqrt(d.X * d.X + d.Y * d.Y)) * 180.0 / Math.PI);
            return new Vector3(pitchDeg, 0f, yawDeg);
        }

        private static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        private static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }
        private static float LerpAngle(float a, float b, float t)
        {
            float d = ((b - a) % 360f + 540f) % 360f - 180f;
            return a + d * t;
        }
    }
}
