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

    public sealed class CameraProfile
    {
        public string Name; public bool Chase;
        public float Distance, Height, LookAhead, LookHeight, YawSmooth, DriftShow;
        public float BaseFov, SpeedFov, AccelFov;
    }

    /// <summary>Driving cameras.
    /// Mounted (bumper, hood, cockpit): hard-attached to the car with the game's own camera attach, so they are
    /// rigid and jitter-free; only the FOV is dynamic.
    /// Chase (close, far, action), NFS Unbound style: low and tight behind the car, looking slightly down so the
    /// car sits in the lower third; critically damped follow of the car's heading (blended a little towards
    /// the direction of travel so drifts show the car's angle); pulls back slightly with speed and acceleration;
    /// strong speed FOV. 0.22: Driveclub + Unbound pass (lateral swing, squat/stretch, lean, road vibration).</summary>
    public sealed class CameraSystem
    {
        public static CameraSystem Instance { get; private set; }
        public static float BoostFov, BoostShake;    // set per frame by nitrous / abilities

        private readonly SaveManager saves;
        private readonly VehicleManager vehicles;
        private readonly Config cfg;
        private Camera cam;
        private CameraMode mode = CameraMode.CloseChase;
        private bool active, attached;
        private int viewPressedAt, labelUntil, measuredHandle;
        private float time, lastLookInput;
        // chase state
        private float yaw, yawVel, dist, distVel, fov, fovVel, collide = 99f, collideVel, accelPull, lastSpeed;
        private float latG, lastHeading, headPitch, headRoll, headX, headY;
        private float lookYaw, lookPitch;
        private Vector3 hoodLocal, bumperLocal, cockpitLocal;

        private static readonly CameraProfile[] Profiles =
        {
            new CameraProfile { Name = "Bumper",       Chase = false, BaseFov = 60, SpeedFov = 18, AccelFov = 3 },
            new CameraProfile { Name = "Hood",         Chase = false, BaseFov = 58, SpeedFov = 16, AccelFov = 3 },
            new CameraProfile { Name = "Cockpit",      Chase = false, BaseFov = 62, SpeedFov = 10, AccelFov = 2 },
            new CameraProfile { Name = "Close chase",  Chase = true, Distance = 4.6f, Height = 1.30f, LookAhead = 2.6f, LookHeight = 0.72f, YawSmooth = 0.17f, DriftShow = 0.42f, BaseFov = 57, SpeedFov = 24, AccelFov = 6 },
            new CameraProfile { Name = "Far chase",    Chase = true, Distance = 6.9f, Height = 1.95f, LookAhead = 3.6f, LookHeight = 0.95f, YawSmooth = 0.22f, DriftShow = 0.36f, BaseFov = 54, SpeedFov = 19, AccelFov = 4 },
            new CameraProfile { Name = "Action chase", Chase = true, Distance = 3.7f, Height = 0.98f, LookAhead = 4.6f, LookHeight = 0.55f, YawSmooth = 0.12f, DriftShow = 0.50f, BaseFov = 63, SpeedFov = 28, AccelFov = 8 },
        };

        public CameraSystem(SaveManager saves, VehicleManager vehicles, Config cfg)
        {
            this.saves = saves; this.vehicles = vehicles; this.cfg = cfg; Instance = this;
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e =>
            {
                if (e.To == GameMode.MIDNIGHT_CLUB && saves.Data != null) mode = (CameraMode)Math.Max(0, Math.Min((int)CameraMode.GameDefault, saves.Data.CameraMode));
                if (e.To != GameMode.MIDNIGHT_CLUB) Destroy();
            });
        }

        public CameraMode Mode { get { return mode; } }

        public void Release()
        {
            if (shaking && cam != null) { try { Function.Call(Hash.STOP_CAM_SHAKING, cam, true); } catch { } shaking = false; }
            if (cam != null && active)
            {
                if (attached) { Function.Call(Hash.DETACH_CAM, cam); attached = false; }
                cam.IsActive = false; Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0);
            }
            active = false;
        }

        private void Destroy() { Release(); if (cam != null) { try { cam.Delete(); } catch { } cam = null; } }

        private void Tick()
        {
            var ped = Game.Player.Character; var car = vehicles.Current;
            float dt = Math.Max(0.001f, Math.Min(0.05f, Game.LastFrameTime / Math.Max(0.1f, Game.TimeScale)));
            time += dt;

            Game.DisableControlThisFrame(Control.NextCamera);
            if (Game.IsControlJustPressed(Control.NextCamera)) viewPressedAt = Game.GameTime;
            if (Game.IsControlJustReleased(Control.NextCamera) && Game.GameTime - viewPressedAt < 450) Cycle();
            if (Game.GameTime < labelUntil) DrawLabel();

            if (mode == CameraMode.GameDefault || !cfg.CustomCameras || car == null || !car.Exists() || !ped.IsInVehicle(car) || Game.IsCutsceneActive)
            { if (active) Release(); BoostFov = BoostShake = 0f; return; }

            if (cam == null || !cam.Exists()) { cam = Camera.Create(ScriptedCameraNameHash.DefaultScriptedCamera, car.Position, Vector3.Zero, 60f, false, EulerRotationOrder.YXZ); attached = false; }
            var p = Profiles[(int)mode];
            if (!active)
            {
                cam.IsActive = true;
                Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false, 0);
                active = true;
                yaw = car.Heading; yawVel = 0; lastHeading = car.Heading; latG = 0; headPitch = headRoll = headX = headY = 0; dist = p.Distance; distVel = 0; fov = p.BaseFov; fovVel = 0; collide = 99f; lastSpeed = car.Speed; accelPull = 0;
            }
            UpdateLook(dt);
            float speed = car.Speed;
            // Acceleration from the speed change, heavily filtered (no frame noise).
            float a = (speed - lastSpeed) / dt; lastSpeed = speed;
            if (Math.Abs(a) > 60f) a = 0f;
            accelPull += (Clamp(a / 9.81f, -0.6f, 1f) - accelPull) * Math.Min(1f, dt * 2.5f);
            // Lateral G from yaw rate x speed (much smoother than differentiating velocity).
            float yawRate = DeltaAngle(lastHeading, car.Heading) / dt * (float)(Math.PI / 180.0); lastHeading = car.Heading;
            if (Math.Abs(yawRate) > 6f) yawRate = 0f;
            latG += (Clamp(speed * yawRate / 9.81f, -1.5f, 1.5f) - latG) * Math.Min(1f, dt * 4f);

            if (p.Chase) { if (attached) { Function.Call(Hash.DETACH_CAM, cam); attached = false; } Chase(car, p, speed, dt); }
            else Mounted(car, ped, p, speed, dt);
            BoostFov = 0f; BoostShake = 0f;
        }

        private void Cycle()
        {
            mode = (CameraMode)(((int)mode + 1) % ((int)CameraMode.GameDefault + 1));
            if (saves.Data != null) { saves.Data.CameraMode = (int)mode; saves.IsDirty = true; }
            lookYaw = lookPitch = 0; measuredHandle = 0;
            if (active) { var c = vehicles.Current; if (c != null && c.Exists()) yaw = c.Heading; var p = Profiles[Math.Min((int)mode, Profiles.Length - 1)]; dist = p.Distance; fov = p.BaseFov; collide = 99f; }
            if (mode == CameraMode.GameDefault) Release();
            labelUntil = Game.GameTime + 1500;
        }

        private void DrawLabel()
        {
            string n = mode == CameraMode.GameDefault ? "GAME DEFAULT" : Profiles[(int)mode].Name.ToUpperInvariant();
            new TextElement("CAMERA: " + n, new PointF(640f, 640f), 0.42f, Color.FromArgb(255, 170, 90, 255), Font.ChaletComprimeCologne, Alignment.Center, true, true).ScaledDraw();
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

        private float TargetFov(CameraProfile p, float speed)
        {
            float kmh = speed * 3.6f;
            float s = (float)Math.Pow(Clamp(kmh / 300f, 0f, 1f), 0.9) * p.SpeedFov;
            float acc = Clamp(accelPull, 0f, 1f) * p.AccelFov;
            return Clamp(p.BaseFov + (s + acc) * cfg.CameraFovScale + BoostFov, 30f, 115f);
        }

        // ---------------------------------------------------------------- chase
        // Driveclub + NFS Unbound: the camera is positionally tight but rotationally lazy, so the car visibly
        // swings and slides on screen; it squats closer under braking and stretches back under acceleration;
        // lowers and widens with speed (strong FOV curve), leans gently into corners, pitches with braking,
        // and picks up a fine road-vibration shake at speed (stronger with nitrous).
        private float latOff, latOffVel, roll, rollVel, pitchOff, pitchOffVel; private bool shaking;

        private void Chase(Vehicle car, CameraProfile p, float speed, float dt)
        {
            float kmh = speed * 3.6f, sp = Clamp(kmh / 280f, 0f, 1f);
            var vel = car.Velocity;
            float target = car.Heading;
            if (speed > 4f && Vector3.Dot(vel, car.ForwardVector) > 0f)
            {
                float velYaw = (float)(Math.Atan2(-vel.X, vel.Y) * 180.0 / Math.PI);
                target = LerpAngle(car.Heading, velYaw, p.DriftShow);   // show the drift angle
            }
            yaw = SmoothDampAngle(yaw, target, ref yawVel, p.YawSmooth * (1f + sp * 0.35f), dt);

            bool behind = Game.IsControlPressed(Control.VehicleLookBehind);
            float viewYaw = yaw + (behind ? 180f : lookYaw);
            float g = cfg.CameraGForce;
            float brake = Clamp(-accelPull, 0f, 1f), thr = Clamp(accelPull, 0f, 1f);
            float targetDist = p.Distance + sp * 0.9f + thr * 0.65f * g - brake * 0.45f * g;
            dist = SmoothDamp(dist, targetDist, ref distVel, 0.32f, dt);
            float height = p.Height - sp * 0.2f + brake * 0.08f * g;
            // lateral swing: the camera slides to the outside of the corner
            latOff = SmoothDamp(latOff, Clamp(-latG * 0.38f * g, -0.7f, 0.7f), ref latOffVel, 0.25f, dt);

            var right = Dir(viewYaw - 90f) * -1f;
            var anchor = car.Position + new Vector3(0, 0, 0.3f);
            var desired = car.Position - Dir(viewYaw) * dist + right * latOff + new Vector3(0, 0, height);
            var hit = World.Raycast(anchor, desired, IntersectFlags.Map, car);
            float allowed = hit.DidHit ? Math.Max(1.4f, hit.HitPosition.DistanceTo(anchor) - 0.3f) : 99f;
            collide = allowed < collide ? allowed : SmoothDamp(collide, allowed, ref collideVel, 0.4f, dt);
            var toCam = desired - anchor; float len = toCam.Length();
            if (len > collide && len > 0.01f) desired = anchor + toCam * (collide / len);

            // look slightly ahead along the velocity so fast corners are anticipated
            var lead = speed > 3f ? Vector3.Normalize(new Vector3(vel.X, vel.Y, 0)) * Math.Min(2.5f, speed * 0.04f) : Vector3.Zero;
            var look = car.Position + Dir(behind ? yaw + 180f : yaw + lookYaw * 0.5f) * p.LookAhead + lead + new Vector3(0, 0, p.LookHeight);
            var rot = LookRotation(desired, look);
            pitchOff = SmoothDamp(pitchOff, (-brake * 1.6f + thr * 0.8f) * g, ref pitchOffVel, 0.3f, dt);
            roll = SmoothDamp(roll, Clamp(latG * 2.4f * g, -4f, 4f), ref rollVel, 0.3f, dt);
            rot.X += lookPitch + pitchOff; rot.Y = roll;
            float micro = (BoostShake * 0.5f + Clamp((kmh - 160f) / 180f, 0f, 1f) * 0.12f) * cfg.CameraShake;
            if (micro > 0f) { rot.X += Noise(31f) * micro; rot.Z += Noise(23f) * micro * 0.7f; }
            // native road vibration on top (subtle)
            float amp = (Clamp((kmh - 60f) / 240f, 0f, 1f) * 0.35f + BoostShake * 0.8f) * cfg.CameraShake;
            if (!shaking && amp > 0.02f) { Function.Call(Hash.SHAKE_CAM, cam, "ROAD_VIBRATION_SHAKE", amp); shaking = true; }
            if (shaking) { Function.Call(Hash.SET_CAM_SHAKE_AMPLITUDE, cam, amp); if (amp <= 0.02f) { Function.Call(Hash.STOP_CAM_SHAKING, cam, true); shaking = false; } }

            fov = SmoothDamp(fov, TargetFov(p, speed), ref fovVel, 0.4f, dt);
            cam.Position = desired; cam.Rotation = rot; cam.FieldOfView = fov;
        }

        // ---------------------------------------------------------------- mounted
        private void Mounted(Vehicle car, Ped ped, CameraProfile p, float speed, float dt)
        {
            if (shaking) { Function.Call(Hash.STOP_CAM_SHAKING, cam, true); shaking = false; }
            if (car.Handle != measuredHandle) Measure(car, ped);
            var local = mode == CameraMode.Bumper ? bumperLocal : mode == CameraMode.Hood ? hoodLocal : cockpitLocal;
            bool behind = Game.IsControlPressed(Control.VehicleLookBehind);
            float maxLook = mode == CameraMode.Cockpit ? 110f : 70f;
            float ry = behind ? 180f : Clamp(lookYaw, -maxLook, maxLook);
            // Rigid, relative hard attach: moves exactly with the car, no lag, no G effects.
            float pitch = lookPitch * 0.6f, roll = 0f;
            if (mode == CameraMode.Cockpit)
            {
                // Driver's head: hide the driver model (local only) and let the head move against the load.
                Function.Call(Hash.SET_ENTITY_LOCALLY_INVISIBLE, ped);
                float g = cfg.CameraGForce;
                headPitch += (-accelPull * 2.2f * g - headPitch) * Math.Min(1f, dt * 5f);     // brake: nose down, throttle: head back
                headRoll += (latG * 2.5f * g - headRoll) * Math.Min(1f, dt * 5f);             // lean into the load in corners
                headX += (-latG * 0.035f * g - headX) * Math.Min(1f, dt * 5f);               // head shifts to the outside
                headY += (-accelPull * 0.04f * g - headY) * Math.Min(1f, dt * 5f);           // pushed back under acceleration
                pitch += headPitch; roll = headRoll;
                local = local + new Vector3(headX, headY, 0f);
            }
            Function.Call(Hash.HARD_ATTACH_CAM_TO_ENTITY, cam, car, pitch, roll, ry, local.X, local.Y, local.Z, true);
            attached = true;
            fov = SmoothDamp(fov, TargetFov(p, speed), ref fovVel, 0.35f, dt);
            cam.FieldOfView = fov;
        }

        private void Measure(Vehicle car, Ped ped)
        {
            measuredHandle = car.Handle;
            Vector3 min, max; car.Model.GetDimensions(out min, out max);
            int bonnet = Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, car, "bonnet");
            if (bonnet >= 0)
            {
                var b = car.GetPositionOffset(Function.Call<Vector3>(Hash.GET_WORLD_POSITION_OF_ENTITY_BONE, car, bonnet));
                hoodLocal = new Vector3(0f, b.Y - 0.25f, b.Z + 0.40f + cfg.HoodCamHeight);
            }
            else hoodLocal = new Vector3(0f, max.Y * 0.3f, max.Z * 0.7f + cfg.HoodCamHeight);
            bumperLocal = new Vector3(0f, max.Y + 0.08f, min.Z + 0.45f);
            var head = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS, ped, 31086, 0f, 0f, 0f);   // SKEL_Head
            var h = car.GetPositionOffset(head);
            cockpitLocal = (h.Length() > 0.1f && h.Length() < 4f)
                ? new Vector3(h.X, h.Y + 0.14f, h.Z + cfg.CockpitCamHeight)
                : new Vector3(-0.35f, 0f, max.Z * 0.5f);
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
