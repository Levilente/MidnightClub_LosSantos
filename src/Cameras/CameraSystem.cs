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
            new CameraProfile { Name = "Close chase",  Chase = true, Distance = 4.5f, Height = 1.18f, LookAhead = 2.2f, LookHeight = 0.62f, YawSmooth = 0.10f, DriftShow = 0.25f, BaseFov = 54, SpeedFov = 17, AccelFov = 4 },
            new CameraProfile { Name = "Far chase",    Chase = true, Distance = 6.4f, Height = 1.70f, LookAhead = 2.8f, LookHeight = 0.80f, YawSmooth = 0.13f, DriftShow = 0.22f, BaseFov = 52, SpeedFov = 14, AccelFov = 3 },
            new CameraProfile { Name = "Action chase", Chase = true, Distance = 3.5f, Height = 0.86f, LookAhead = 3.4f, LookHeight = 0.48f, YawSmooth = 0.07f, DriftShow = 0.32f, BaseFov = 60, SpeedFov = 22, AccelFov = 6 },
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
            ShowDriver(null);
            if (shaking && cam != null) { try { Function.Call(Hash.STOP_CAM_SHAKING, cam, true); } catch { } shaking = false; }
            if (cam != null && active)
            {
                if (attached) { Function.Call(Hash.DETACH_CAM, cam); attached = false; }
                cam.IsActive = false; Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0);
            }
            active = false;
        }

        private bool driverHidden, lastMounted; private int hiddenPed;
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

            if (mode == CameraMode.GameDefault || !cfg.CustomCameras || car == null || !car.Exists() || !ped.IsInVehicle(car) || Game.IsCutsceneActive)
            { if (active) Release(); BoostFov = BoostShake = 0f; return; }

            if (cam == null || !cam.Exists()) { cam = Camera.Create(ScriptedCameraNameHash.DefaultScriptedCamera, car.Position, Vector3.Zero, 60f, false, EulerRotationOrder.YXZ); attached = false; }
            var p = Profiles[(int)mode];
            if (!p.Chase && car.Model.IsBike) p = Profiles[(int)CameraMode.CloseChase];   // bikes: no bumper/hood/cockpit mounts
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

            if (mode != CameraMode.Cockpit || p.Chase) ShowDriver(ped);
            if (p.Chase) { if (attached && lastMounted) { Function.Call(Hash.DETACH_CAM, cam); attached = false; } lastMounted = false; Chase(car, p, speed, dt); }
            else { if (attached && !lastMounted) { Function.Call(Hash.STOP_CAM_POINTING, cam); Function.Call(Hash.DETACH_CAM, cam); attached = false; } lastMounted = true; Mounted(car, ped, p, speed, dt); }
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
        // Driveclub-style: the camera is ATTACHED to the car (offset in car space, pointed at the car), so the engine
        // moves it in the same physics step as the car: no jitter at any speed. On top: a slightly lagging yaw (the
        // car rotates a touch on screen and shows a little drift angle), stretch under acceleration and squat under
        // braking, lower and wider with speed, and a very fine high-speed tremor. Horizon stays level.
        private float latOff, latOffVel, pitchOff, pitchOffVel; private bool shaking;

        private void Chase(Vehicle car, CameraProfile p, float speed, float dt)
        {
            float kmh = speed * 3.6f, sp = Clamp(kmh / 260f, 0f, 1f);
            var vel = car.Velocity;
            float target = car.Heading;
            if (speed > 4f && Vector3.Dot(vel, car.ForwardVector) > 0f)
            {
                float velYaw = (float)(Math.Atan2(-vel.X, vel.Y) * 180.0 / Math.PI);
                target = LerpAngle(car.Heading, velYaw, p.DriftShow);
            }
            yaw = SmoothDampAngle(yaw, target, ref yawVel, p.YawSmooth, dt);
            // never let the camera fall far around the side
            float rel = DeltaAngle(car.Heading, yaw);
            if (Math.Abs(rel) > 35f) { yaw = car.Heading + Math.Sign(rel) * 35f; rel = Math.Sign(rel) * 35f; }

            bool behind = Game.IsControlPressed(Control.VehicleLookBehind);
            float relView = behind ? 180f : rel + lookYaw;
            float g = cfg.CameraGForce;
            float brake = Clamp(-accelPull, 0f, 1f), thr = Clamp(accelPull, 0f, 1f);
            float targetDist = p.Distance + sp * 0.55f + thr * 0.45f * g - brake * 0.35f * g;
            dist = SmoothDamp(dist, targetDist, ref distVel, 0.45f, dt);
            float height = p.Height - sp * 0.12f;
            latOff = SmoothDamp(latOff, Clamp(-latG * 0.12f * g, -0.3f, 0.3f), ref latOffVel, 0.3f, dt);

            // collision: shorten the arm if the map is in the way
            var worldBack = car.GetOffsetPosition(LocalArm(relView, dist, latOff, height));
            var anchor = car.Position + new Vector3(0, 0, 0.4f);
            var hit = World.Raycast(anchor, worldBack, IntersectFlags.Map, car);
            float allowed = hit.DidHit ? Math.Max(1.5f, hit.HitPosition.DistanceTo(anchor) - 0.35f) : 99f;
            collide = allowed < collide ? allowed : SmoothDamp(collide, allowed, ref collideVel, 0.45f, dt);
            float armDist = Math.Min(dist, collide);

            var off = LocalArm(relView, armDist, latOff, height);
            pitchOff = SmoothDamp(pitchOff, (-brake * 0.22f + thr * 0.12f) * g, ref pitchOffVel, 0.35f, dt);
            float rl = behind ? 180f : rel * 0.6f + lookYaw * 0.5f;
            double rr = rl * Math.PI / 180.0;
            var look = new Vector3((float)(-Math.Sin(rr) * p.LookAhead), (float)(Math.Cos(rr) * p.LookAhead), p.LookHeight + pitchOff - lookPitch * 0.05f);
            float trem = Clamp((kmh - 180f) / 160f, 0f, 1f) * 0.012f * cfg.CameraShake + BoostShake * 0.02f;
            if (trem > 0f) { look.X += Noise(37f) * trem; look.Z += Noise(29f) * trem; }

            Function.Call(Hash.ATTACH_CAM_TO_ENTITY, cam, car, off.X, off.Y, off.Z, true);
            Function.Call(Hash.POINT_CAM_AT_ENTITY, cam, car, look.X, look.Y, look.Z, true);
            attached = true;
            fov = SmoothDamp(fov, TargetFov(p, speed), ref fovVel, 0.5f, dt);
            cam.FieldOfView = fov;
        }

        /// <summary>Camera position in car space for a view angle rel (deg, + = left of the car's heading).</summary>
        private static Vector3 LocalArm(float rel, float dist, float lat, float height)
        {
            double r = rel * Math.PI / 180.0; float s = (float)Math.Sin(r), c = (float)Math.Cos(r);
            return new Vector3(dist * s - lat * c, -dist * c - lat * s, height);
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
                // Driver's head: hide the driver model and let the head move against the load.
                if (!driverHidden) { Function.Call(Hash.SET_ENTITY_VISIBLE, ped, false, false); driverHidden = true; hiddenPed = ped.Handle; }
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
