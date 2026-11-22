using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.Persistence;

namespace MidnightClub.Vehicles
{
    /// <summary>Stance (camber, track width, tyre diameter) written straight into each wheel (CWheel) and
    /// tyre compounds (grip) via the model's handling data while you drive it.
    /// CWheel offsets 0x008 / 0x010 (Y rotation and its negation), 0x030 (X offset), 0x110 (tyre radius)
    /// are the constants FiveM's SET_VEHICLE_WHEEL_* natives use; the wheel address comes from SHVDN.
    /// Every wheel's stock values are read and sanity-checked before anything is written, and the
    /// values are re-applied every frame because the game resets them.</summary>
    public sealed unsafe class FitmentManager
    {
        public static FitmentManager Instance { get; private set; }
        private const int OffYRot = 0x008, OffInvYRot = 0x010, OffXOffset = 0x030, OffTyreRadius = 0x110, OffRimRadius = 0x114;

        private sealed class WheelStock { public IntPtr Addr; public float YRot, X, Tyre, Rim; public bool Left, Front; }
        private readonly Dictionary<int, List<WheelStock>> stock = new Dictionary<int, List<WheelStock>>();
        private readonly Dictionary<int, KeyValuePair<float, float>> gripStock = new Dictionary<int, KeyValuePair<float, float>>();
        private int gripModel; private float gripMult = 1f;
        private readonly VehicleManager vehicles;
        private readonly Config cfg;
        private readonly HashSet<int> warned = new HashSet<int>();

        public static readonly string[] CompoundNames = { "Street", "Sport", "Semi-slick", "Race slick", "Drift" };
        public static readonly float[] CompoundGrip = { 1.00f, 1.05f, 1.10f, 1.16f, 1.00f };

        public FitmentManager(VehicleManager vehicles, Config cfg)
        {
            this.vehicles = vehicles; this.cfg = cfg; Instance = this;
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To != GameMode.MIDNIGHT_CLUB) { RestoreGrip(); stock.Clear(); } });
        }

        public void Tick()
        {
            var car = vehicles.Current; var rec = vehicles.CurrentRecord;
            if (car == null || !car.Exists() || rec == null) { RestoreGrip(); return; }
            ApplyTo(car, rec);
            ApplyGrip(car, CompoundGrip[Math.Max(0, Math.Min(4, rec.TyreCompound))]);
        }

        /// <summary>Writes the record's stance onto any spawned car (driven or parked in a garage).</summary>
        public void ApplyTo(Vehicle car, OwnedVehicle rec)
        {
            if (!cfg.FitmentEnabled || car == null || !car.Exists() || rec == null) return;
            var wheels = Stock(car);
            if (wheels == null) return;
            bool stockOnly = !rec.FitKit || (rec.CamberF == 0 && rec.CamberR == 0 && rec.TrackF == 0 && rec.TrackR == 0 && Mult(rec.TyreF) == 1f && Mult(rec.TyreR) == 1f);
            if (stockOnly && !rec.FitKit) return;   // never touch cars without a kit
            foreach (var w in wheels)
            {
                float camber = (w.Front ? rec.CamberF : rec.CamberR) * (float)(Math.PI / 180.0) * cfg.CamberSign;
                float track = (w.Front ? rec.TrackF : rec.TrackR) / 100f;
                float tyre = Mult(w.Front ? rec.TyreF : rec.TyreR);
                float y = w.YRot + (w.Left ? camber : -camber);
                float x = w.X + Math.Sign(w.X) * track;
                float t = w.Tyre * tyre;
                byte* a = (byte*)w.Addr.ToPointer();
                *(float*)(a + OffYRot) = y;
                *(float*)(a + OffInvYRot) = -y;
                *(float*)(a + OffXOffset) = x;
                *(float*)(a + OffTyreRadius) = t;
                if (*(float*)(a + OffRimRadius) > t * 0.95f) *(float*)(a + OffRimRadius) = Math.Min(w.Rim, t * 0.95f);
            }
        }

        private static float Mult(float v) { return v <= 0f ? 1f : v; }

        /// <summary>Reads and validates every wheel's stock values once per car (null = not safe to touch).</summary>
        private List<WheelStock> Stock(Vehicle car)
        {
            List<WheelStock> list;
            if (stock.TryGetValue(car.Handle, out list)) return list;
            list = new List<WheelStock>();
            try
            {
                foreach (var wheel in car.Wheels.GetAllWheels())
                {
                    var addr = wheel.MemoryAddress;
                    if (addr == IntPtr.Zero) continue;
                    byte* a = (byte*)addr.ToPointer();
                    var s = new WheelStock
                    {
                        Addr = addr, YRot = *(float*)(a + OffYRot), X = *(float*)(a + OffXOffset),
                        Tyre = *(float*)(a + OffTyreRadius), Rim = *(float*)(a + OffRimRadius),
                        Left = wheel.BoneId.ToString().Contains("Left"),
                        Front = wheel.BoneId == VehicleWheelBoneId.WheelLeftFront || wheel.BoneId == VehicleWheelBoneId.WheelRightFront
                    };
                    bool ok = Finite(s.YRot) && Math.Abs(s.YRot) < 0.6f && Finite(s.X) && Math.Abs(s.X) > 0.1f && Math.Abs(s.X) < 3f
                              && Finite(s.Tyre) && s.Tyre > 0.1f && s.Tyre < 1.6f && Finite(s.Rim) && s.Rim > 0.02f && s.Rim <= s.Tyre + 0.01f;
                    if (!ok)
                    {
                        if (warned.Add(car.Model.Hash)) Log.Warn("Fitment: wheel " + wheel.BoneId + " of model " + car.Model.Hash + " has unexpected values (yRot " + s.YRot + ", x " + s.X + ", tyre " + s.Tyre + ", rim " + s.Rim + "); stance disabled for this car.");
                        list = null; break;
                    }
                    list.Add(s);
                }
            }
            catch (Exception ex) { Log.Error("Fitment read", ex); list = null; }
            if (list != null && list.Count == 0) list = null;
            stock[car.Handle] = list;
            if (list != null) Log.Info("Fitment: " + list.Count + " wheels OK on model " + car.Model.Hash + " (front tyre r " + list[0].Tyre.ToString("0.000") + ")");
            if (stock.Count > 64) stock.Clear();
            return list;
        }

        private static bool Finite(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }

        // ---------------------------------------------------------------- grip (tyre compounds)
        private void ApplyGrip(Vehicle car, float mult)
        {
            int model = car.Model.Hash;
            if (model == gripModel && mult == gripMult) return;
            RestoreGrip();
            if (mult == 1f) return;
            try
            {
                var h = car.HandlingData;
                if (h == null || !h.IsValid) return;
                if (!gripStock.ContainsKey(model)) gripStock[model] = new KeyValuePair<float, float>(h.TractionCurveMax, h.TractionCurveMin);
                var st = gripStock[model];
                h.TractionCurveMax = st.Key * mult; h.TractionCurveMin = st.Value * mult;
                gripModel = model; gripMult = mult;
            }
            catch (Exception ex) { Log.Error("Grip apply", ex); }
        }

        /// <summary>Handling data is shared per model, so it's only changed while you drive and put back after.</summary>
        public void RestoreGrip()
        {
            if (gripModel == 0) return;
            try
            {
                KeyValuePair<float, float> st;
                var h = GTA.HandlingData.GetByVehicleModel(new Model(gripModel));
                if (h != null && h.IsValid && gripStock.TryGetValue(gripModel, out st)) { h.TractionCurveMax = st.Key; h.TractionCurveMin = st.Value; }
            }
            catch (Exception ex) { Log.Error("Grip restore", ex); }
            gripModel = 0; gripMult = 1f;
        }
    }
}
