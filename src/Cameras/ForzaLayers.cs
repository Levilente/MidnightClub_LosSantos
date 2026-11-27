using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using GTA.Math;
using MidnightClub.Core;

namespace MidnightClub.Cameras
{
    /// <summary>Everything the camera layers read each frame (Forza CameraPhysics input names).</summary>
    public sealed class CamInputs
    {
        public float GLong, GLat, SpeedMph, SpeedNorm, SlipLat, SlipLong, Slip, VertAccel, TimeInAir, Pitch, SuspPitch, SuspRoll, Stationary, CollideLong, CollideLat, Collide;
        public float Get(string name)
        {
            switch (name)
            {
                case "Gs_Longitudinal": return GLong;
                case "Gs_Lateral": return GLat;
                case "SpeedMPH": return SpeedMph;
                case "Speed": return SpeedNorm;
                case "Slip_Lateral": return SlipLat;
                case "Slip_Longitudinal": return SlipLong;
                case "Slip": return Slip;
                case "VerticalAcceleration": case "OnRoadVerticalAcceleration": return VertAccel;
                case "OffRoadVerticalAcceleration": return 0f;
                case "TimeInAir": return TimeInAir;
                case "PitchAngleDegrees": return Pitch;
                case "SuspensionAngleBias_Longitudinal": case "SuspensionBias_Longitudinal": return SuspPitch;
                case "SuspensionAngleBias_Lateral": case "SuspensionBias_Lateral": return SuspRoll;
                case "ConstantOne": case "Constant": return 1f;
                case "OneWhenStationary": return Stationary;
                case "WorldCollision_Longitudinal": case "CarCollision_Longitudinal": return CollideLong;
                case "WorldCollision_Lateral": case "CarCollision_Lateral": return CollideLat;
                case "WorldCollision": case "CarCollision": return Collide;
            }
            return 0f;   // water, wind, head tracking, surface bumps...: not available in GTA
        }
    }

    /// <summary>The summed result of all layers for one frame (Forza axes: X right, Y up, Z forward; YPR in degrees).</summary>
    public struct CamOutput { public Vector3 CarXyz, CarYpr, CamXyz, CamYpr; public float Fov; }

    /// <summary>Interpreter for Forza Horizon's CameraPhysics.xml: each layer maps an input (G-forces, speed, slip,
    /// suspension, collisions...) through a 3/5-point curve, optionally drives a noise generator, scales it by Param
    /// and feeds one output (position / rotation offsets in car or camera space, impulses, FOV), optionally through
    /// a damped spring. Loaded from the user's own Forza camera file, so the cameras feel like theirs.</summary>
    public sealed class ForzaLayers
    {
        private sealed class Layer
        {
            public string Input, Output, Noise; public bool Deriv, Sprung, Mirrored;
            public float ClampMin = -1e9f, ClampMax = 1e9f, NoiseFreq, NoiseSeed, NoiseMin = -1f, NoiseMax = 1f, SpringK, SpringD;
            public float[] CurveIn, CurveOut, NfIn, NfOut; public Vector3 Param;
            public float LastInput; public Vector3 Pos, Vel;   // spring state
        }
        private readonly List<Layer> layers = new List<Layer>();
        public int Count { get { return layers.Count; } }

        public static Dictionary<string, ForzaLayers> LoadAll(string overrideDir)
        {
            var result = new Dictionary<string, ForzaLayers>();
            string text = null;
            try
            {
                var f = overrideDir != null ? Path.Combine(overrideDir, "CameraPhysics.xml") : null;
                if (f != null && File.Exists(f)) text = File.ReadAllText(f);
                else using (var s = typeof(ForzaLayers).Assembly.GetManifestResourceStream("camera.CameraPhysics.xml")) if (s != null) using (var r = new StreamReader(s)) text = r.ReadToEnd();
            }
            catch (Exception ex) { Log.Error("CameraPhysics.xml", ex); }
            if (text == null) return result;
            var heads = Regex.Matches(text, "<(\\w+) NumLayers=\"\\d+\">");
            for (int i = 0; i < heads.Count; i++)
            {
                int a = heads[i].Index, b = i + 1 < heads.Count ? heads[i + 1].Index : text.Length;
                var set = new ForzaLayers();
                foreach (Match m in Regex.Matches(text.Substring(a, b - a), "<Layer ([^/]*)/>"))
                {
                    var d = new Dictionary<string, string>();
                    foreach (Match kv in Regex.Matches(m.Groups[1].Value, "(\\S+?)=\"([^\"]*)\"")) d[kv.Groups[1].Value] = kv.Groups[2].Value;
                    if (Get(d, "Enabled") != "1") continue;
                    var loc = Get(d, "LocationType"); if (loc == "IslandOnly") continue;
                    var l = new Layer { Input = Get(d, "InputType"), Output = Get(d, "OutputType"), Noise = Get(d, "NoiseType") ?? "None" };
                    l.Deriv = Get(d, "InputTakeDerivative") == "1"; l.Sprung = Get(d, "Sprung") == "1"; l.Mirrored = Get(d, "InputToParamCurveMirrored") == "1";
                    l.ClampMin = F(d, "InputClampMin", -1e9f); l.ClampMax = F(d, "InputClampMax", 1e9f);
                    l.NoiseFreq = F(d, "NoiseFreq", 1f); l.NoiseSeed = F(d, "NoiseSeed", 0f); l.NoiseMin = F(d, "NoiseOutputRangeMin", -1f); l.NoiseMax = F(d, "NoiseOutputRangeMax", 1f);
                    l.SpringK = F(d, "SpringK", 100f); l.SpringD = F(d, "SpringDPercent", 1f);
                    l.Param = new Vector3(F(d, "Param.x", 0f), F(d, "Param.y", 0f), F(d, "Param.z", 0f));
                    l.CurveIn = Curve(d, "InputToParamCurveInput"); l.CurveOut = Curve(d, "InputToParamCurveOutput");
                    l.NfIn = Curve(d, "InputToNoiseFreqCurveInput"); l.NfOut = Curve(d, "InputToNoiseFreqCurveOutput");
                    if (l.CurveIn.Length == 0 || l.CurveIn.Length != l.CurveOut.Length) continue;
                    set.layers.Add(l);
                }
                result[heads[i].Groups[1].Value] = set;
            }
            foreach (var kv in result) Log.Info("Forza camera layers: " + kv.Key + " " + kv.Value.Count);
            return result;
        }

        private static string Get(Dictionary<string, string> d, string k) { string v; return d.TryGetValue(k, out v) ? v : null; }
        private static float F(Dictionary<string, string> d, string k, float def) { string v; float f; return d.TryGetValue(k, out v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : def; }
        private static float[] Curve(Dictionary<string, string> d, string prefix)
        {
            var l = new List<float>();
            for (int i = 0; i < 8; i++) { string v; float f; if (!d.TryGetValue(prefix + i, out v) || !float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) break; l.Add(f); }
            return l.ToArray();
        }

        private static float Eval(float[] xs, float[] ys, float x)
        {
            if (xs.Length == 0) return 0f;
            if (x <= xs[0]) return ys[0];
            for (int i = 1; i < xs.Length; i++)
                if (x <= xs[i]) { float t = xs[i] - xs[i - 1] < 1e-5f ? 1f : (x - xs[i - 1]) / (xs[i] - xs[i - 1]); return ys[i - 1] + (ys[i] - ys[i - 1]) * t; }
            return ys[ys.Length - 1];
        }

        private static float Hash(float n) { float s = (float)Math.Sin(n * 127.1f + 311.7f) * 43758.5453f; return s - (float)Math.Floor(s); }
        /// <summary>Smooth value noise in -1..1.</summary>
        private static float ValueNoise(float t, float seed)
        {
            float i = (float)Math.Floor(t), f = t - i; float u = f * f * (3f - 2f * f);
            return (Hash(i + seed * 13.1f) * (1f - u) + Hash(i + 1f + seed * 13.1f) * u) * 2f - 1f;
        }

        /// <summary>Runs all layers for one frame and sums their outputs. scale: global intensity (0..2).</summary>
        public CamOutput Update(CamInputs inp, float time, float dt, float scale)
        {
            var o = new CamOutput();
            foreach (var l in layers)
            {
                float raw = inp.Get(l.Input);
                float x = raw;
                if (l.Deriv) { x = dt > 0 ? (raw - l.LastInput) / dt : 0f; l.LastInput = raw; }
                x = Math.Max(l.ClampMin, Math.Min(l.ClampMax, x));
                // curves defined only for positive inputs act symmetrically on lateral inputs
                float sign = 1f;
                bool positiveCurve = l.CurveIn[0] >= 0f;
                if ((l.Mirrored || (positiveCurve && l.Input.EndsWith("Lateral") && l.Input != "Slip_Lateral")) && x < 0f) { sign = -1f; x = -x; }
                float v = Eval(l.CurveIn, l.CurveOut, x) * sign;
                if (l.Noise != "None")
                {
                    float freq = l.NoiseFreq * (l.NfIn.Length > 0 && l.NfIn.Length == l.NfOut.Length ? Eval(l.NfIn, l.NfOut, Math.Abs(x)) : 1f);
                    float n = l.Noise == "Sin" ? (float)Math.Sin(time * freq * 2.0 * Math.PI * 0.25 + l.NoiseSeed) : ValueNoise(time * freq * 0.5f, l.NoiseSeed + l.Param.X);
                    float lo = l.NoiseMin, hi = l.NoiseMax; v *= lo + (n * 0.5f + 0.5f) * (hi - lo);
                }
                var target = l.Param * (v * scale);
                Vector3 val;
                bool impulse = l.Output.EndsWith("Impulse");
                if (l.Sprung || impulse)
                {
                    float k = Math.Max(1f, l.SpringK), c = Math.Max(0.05f, l.SpringD) * 2f * (float)Math.Sqrt(k);
                    int steps = Math.Max(2, Math.Min(16, (int)Math.Ceiling(dt * Math.Sqrt(k) * 6f)));
                    float h = dt / steps;
                    if (impulse) l.Vel += target * 6f;   // kick the spring
                    var goal = impulse ? Vector3.Zero : target;
                    for (int s = 0; s < steps; s++) { l.Vel = (l.Vel + (goal - l.Pos) * (k * h)) / (1f + c * h); l.Pos += l.Vel * h; }
                    if (float.IsNaN(l.Pos.X) || float.IsNaN(l.Pos.Y) || float.IsNaN(l.Pos.Z) || l.Pos.Length() > 50f) { l.Pos = Vector3.Zero; l.Vel = Vector3.Zero; }
                    val = l.Pos;
                }
                else val = target;
                switch (l.Output)
                {
                    case "CarSpaceXYZOffset": case "CarSpaceXYZImpulse": o.CarXyz += val; break;
                    case "CarSpaceYPROffset": case "CarSpaceYPRImpulse": o.CarYpr += val; break;
                    case "CameraSpaceXYZOffset": case "CameraSpaceXYZImpulse": o.CamXyz += val; break;
                    case "CameraSpaceYPROffset": case "CameraSpaceYPRImpulse": o.CamYpr += val; break;
                    case "FOV": o.Fov += val.X; break;
                }
            }
            o.CarXyz = Clamp(o.CarXyz, 1.5f); o.CamXyz = Clamp(o.CamXyz, 1.5f); o.CarYpr = Clamp(o.CarYpr, 12f); o.CamYpr = Clamp(o.CamYpr, 12f);
            o.Fov = Math.Max(-15f, Math.Min(40f, o.Fov));
            return o;
        }
        private static Vector3 Clamp(Vector3 v, float m) { return new Vector3(Math.Max(-m, Math.Min(m, v.X)), Math.Max(-m, Math.Min(m, v.Y)), Math.Max(-m, Math.Min(m, v.Z))); }

        public void Reset() { foreach (var l in layers) { l.Pos = Vector3.Zero; l.Vel = Vector3.Zero; l.LastInput = 0f; } }
    }

    /// <summary>Forza CameraSettings.ini values (key → number).</summary>
    public static class ForzaSettings
    {
        private static Dictionary<string, float> vals;
        public static float Get(string key, float def)
        {
            if (vals == null) Load(null);
            float v; return vals.TryGetValue(key, out v) ? v : def;
        }
        public static void Load(string overrideDir)
        {
            vals = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string text = null; var f = overrideDir != null ? Path.Combine(overrideDir, "CameraSettings.ini") : null;
                if (f != null && File.Exists(f)) text = File.ReadAllText(f);
                else using (var s = typeof(ForzaSettings).Assembly.GetManifestResourceStream("camera.CameraSettings.ini")) if (s != null) using (var r = new StreamReader(s)) text = r.ReadToEnd();
                if (text == null) return;
                foreach (var line in text.Split('\n'))
                {
                    var t = line.Trim(); if (t.Length == 0 || t.StartsWith(";")) continue;
                    int sp = t.LastIndexOf(' '); if (sp <= 0) continue;
                    float v; if (float.TryParse(t.Substring(sp + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) vals[t.Substring(0, sp).Trim()] = v;
                }
            }
            catch (Exception ex) { Log.Error("CameraSettings.ini", ex); }
        }
    }
}
