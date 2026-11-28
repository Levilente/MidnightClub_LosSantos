using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Core;
using Font = GTA.UI.Font;

namespace MidnightClub.Vehicles
{
    /// <summary>DevMode diagnostic for reverse-engineering wheel fields on GTA V Enhanced.
    /// F9 toggles. (1) Auto-scan: finds the CWheel offset whose value equals the wheel bone's local X
    /// (= track/X offset field) and lists tyre-radius candidates, written to the log.
    /// (2) Experiment: PageUp/PageDown pick a field, +/- scale it on all wheels (held every frame),
    /// Backspace resets, so you can SEE which field changes the wheel's look.</summary>
    public sealed unsafe class WheelLab
    {
        private readonly VehicleManager vehicles;
        private readonly Config cfg;
        private bool on; private int field; private readonly float[] scale;
        private bool pu, pd, pp, pm, pb;
        private string scanText = "";
        private int scannedHandle;
        private readonly List<KeyValuePair<string, int>> fields = new List<KeyValuePair<string, int>>
        {
            new KeyValuePair<string, int>("0x110 tyre radius (FiveM)", 0x110),
            new KeyValuePair<string, int>("0x114 rim radius (FiveM)", 0x114),
            new KeyValuePair<string, int>("0x118 tyre width (FiveM)", 0x118),
            new KeyValuePair<string, int>("0x030 X offset (FiveM)", 0x030),
            new KeyValuePair<string, int>("0x008 Y rotation (FiveM)", 0x008),
        };
        private readonly Dictionary<long, float> stock = new Dictionary<long, float>();

        public WheelLab(VehicleManager vehicles, Config cfg)
        {
            this.vehicles = vehicles; this.cfg = cfg;
            scale = new float[16]; for (int i = 0; i < scale.Length; i++) scale[i] = 1f;
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
        }

        private static bool Edge(Keys k, ref bool prev) { bool now = Game.IsKeyPressed(k); bool r = now && !prev; prev = now; return r; }

        private void Tick()
        {
            if (!cfg.DevMode) return;
            if (Game.IsKeyPressed(Keys.F9)) { if (!f9) { on = !on; f9 = true; if (on) scannedHandle = 0; } } else f9 = false;
            if (!on) return;
            var car = vehicles.Current;
            if (car == null || !car.Exists()) return;
            var wheels = car.Wheels.GetAllWheels().Where(w => w.MemoryAddress != IntPtr.Zero).ToArray();
            if (wheels.Length == 0) return;
            if (scannedHandle != car.Handle) { Scan(car, wheels); scannedHandle = car.Handle; stock.Clear(); for (int i = 0; i < scale.Length; i++) scale[i] = 1f; }

            if (Edge(Keys.PageUp, ref pu)) field = (field - 1 + fields.Count) % fields.Count;
            if (Edge(Keys.PageDown, ref pd)) field = (field + 1) % fields.Count;
            if (Edge(Keys.Oemplus, ref pp) || Edge(Keys.Add, ref pp2)) { scale[field] += 0.1f; LogField(wheels); }
            if (Edge(Keys.OemMinus, ref pm) || Edge(Keys.Subtract, ref pm2)) { scale[field] -= 0.1f; LogField(wheels); }
            if (Edge(Keys.Back, ref pb)) { for (int i = 0; i < scale.Length; i++) scale[i] = 1f; Log.Info("WheelLab: reset"); }

            // Hold every experimental value each frame (the game rewrites these fields).
            for (int f = 0; f < fields.Count; f++)
            {
                int off = fields[f].Value;
                foreach (var w in wheels)
                {
                    long key = w.MemoryAddress.ToInt64() + off;
                    float* p = (float*)(w.MemoryAddress + off).ToPointer();
                    if (!stock.ContainsKey(key)) stock[key] = *p;
                    if (scale[f] != 1f) *p = off == 0x008 ? stock[key] + (scale[f] - 1f) * 0.5f * (w.BoneId.ToString().Contains("Left") ? 1 : -1)
                                                        : stock[key] * scale[f];
                    else if (*p != stock[key] && scaleTouched[f]) *p = stock[key];
                }
                scaleTouched[f] = scale[f] != 1f || scaleTouched[f];
            }
            Draw(wheels);
        }
        private bool f9, pp2, pm2; private readonly bool[] scaleTouched = new bool[16];

        private void LogField(VehicleWheel[] wheels)
        {
            var f = fields[field];
            float* p = (float*)(wheels[0].MemoryAddress + f.Value).ToPointer();
            Log.Info("WheelLab: " + f.Key + " x" + scale[field].ToString("0.00") + " (wheel0 now " + (*p).ToString("0.0000") + ")");
        }

        /// <summary>Find which offsets hold the X offset (= bone local X) and which look like a tyre radius.</summary>
        private void Scan(Vehicle car, VehicleWheel[] wheels)
        {
            var sb = new StringBuilder();
            var boneNames = new Dictionary<VehicleWheelBoneId, string> { { VehicleWheelBoneId.WheelLeftFront, "wheel_lf" }, { VehicleWheelBoneId.WheelRightFront, "wheel_rf" }, { VehicleWheelBoneId.WheelLeftRear, "wheel_lr" }, { VehicleWheelBoneId.WheelRightRear, "wheel_rr" } };
            var localX = new Dictionary<VehicleWheel, float>();
            foreach (var w in wheels)
            {
                string bn; if (!boneNames.TryGetValue(w.BoneId, out bn)) continue;
                int idx = Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, car, bn);
                if (idx < 0) continue;
                var world = Function.Call<Vector3>(Hash.GET_WORLD_POSITION_OF_ENTITY_BONE, car, idx);
                localX[w] = car.GetPositionOffset(world).X;
            }
            var xCands = new List<int>(); var radiusCands = new List<int>();
            for (int off = 0; off < 0x300; off += 4)
            {
                bool xMatch = localX.Count >= 2;
                foreach (var kv in localX) { float v = *(float*)(kv.Key.MemoryAddress + off).ToPointer(); if (float.IsNaN(v) || Math.Abs(v - kv.Value) > 0.03f) { xMatch = false; break; } }
                if (xMatch) xCands.Add(off);
                float a = *(float*)(wheels[0].MemoryAddress + off).ToPointer();
                if (a > 0.2f && a < 0.65f && wheels.All(w => { float b = *(float*)(w.MemoryAddress + off).ToPointer(); return Math.Abs(b - a) < 0.08f; })) radiusCands.Add(off);
            }
            sb.Append("X-offset field (bone match): ").Append(xCands.Count == 0 ? "none" : string.Join(", ", xCands.Select(o => "0x" + o.ToString("X3"))));
            sb.Append("\nTyre/rim radius candidates: ").Append(string.Join(", ", radiusCands.Take(14).Select(o => "0x" + o.ToString("X3") + "=" + (*(float*)(wheels[0].MemoryAddress + o).ToPointer()).ToString("0.000"))));
            scanText = sb.ToString();
            Log.Info("WheelLab scan, model " + car.Model.Hash + " (" + wheels.Length + " wheels), bone local X: " + string.Join(", ", localX.Values.Select(v => v.ToString("0.000"))));
            Log.Info("WheelLab " + scanText.Replace("\n", " | "));
            var dump = new StringBuilder("WheelLab dump wheel0 0x000-0x200:");
            for (int off = 0; off < 0x200; off += 4) dump.Append(off % 0x40 == 0 ? "\n  0x" + off.ToString("X3") + ": " : " ").Append((*(float*)(wheels[0].MemoryAddress + off).ToPointer()).ToString("0.###"));
            Log.Info(dump.ToString());
        }

        private void Draw(VehicleWheel[] wheels)
        {
            UI.Ds.Rect(0.19f, 0.045f, 0.62f, 0.25f, Color.FromArgb(200, 0, 0, 0));
            var lines = new List<string> { "~y~WHEEL LAB~s~   PgUp/PgDn field   +/- scale   Backspace reset   F9 close", scanText };
            for (int f = 0; f < fields.Count; f++)
            {
                float v = *(float*)(wheels[0].MemoryAddress + fields[f].Value).ToPointer();
                lines.Add((f == field ? "~p~> " : "   ") + fields[f].Key + "   x" + scale[f].ToString("0.00") + "   wheel0 = " + v.ToString("0.0000") + "~s~");
            }
            float y = 0.05f;
            foreach (var l in lines)
                foreach (var part in l.Split('\n'))
                { UI.Ds.Txt(part, 0.2f, y, 0.3f, Color.White, Font.ChaletLondon, Alignment.Left, false, true); y += 0.026f; }
        }
    }
}
