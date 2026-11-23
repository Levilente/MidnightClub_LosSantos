using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Core;

namespace MidnightClub.Racing
{
    /// <summary>Time-trial ghosts: your best run on every solo race is recorded (10 samples/s plus checkpoint
    /// splits) and replayed as a see-through car the next time you race it, with live split deltas.</summary>
    public sealed class Ghost
    {
        public struct Sample { public int T; public Vector3 P; public Quaternion Q; }
        public static string Dir;
        public int Model;
        public readonly List<Sample> Samples = new List<Sample>();
        public readonly List<int> Splits = new List<int>();
        public int TotalMs;

        private static string FileFor(string raceId)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) raceId = raceId.Replace(c, '_');
            return Path.Combine(Dir, raceId + ".ghost");
        }

        public static Ghost Load(string raceId)
        {
            try
            {
                if (Dir == null) return null; var f = FileFor(raceId); if (!File.Exists(f)) return null;
                using (var br = new BinaryReader(File.OpenRead(f)))
                {
                    if (br.ReadInt32() != 1) return null;
                    var g = new Ghost { Model = br.ReadInt32(), TotalMs = br.ReadInt32() };
                    int n = br.ReadInt32();
                    for (int i = 0; i < n; i++) g.Samples.Add(new Sample { T = br.ReadInt32(), P = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()), Q = new Quaternion(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()) });
                    int m = br.ReadInt32(); for (int i = 0; i < m; i++) g.Splits.Add(br.ReadInt32());
                    return g.Samples.Count > 2 ? g : null;
                }
            }
            catch (Exception ex) { Log.Error("Ghost load", ex); return null; }
        }

        public void Save(string raceId)
        {
            try
            {
                if (Dir == null || Samples.Count < 3) return;
                Directory.CreateDirectory(Dir);
                using (var bw = new BinaryWriter(File.Create(FileFor(raceId))))
                {
                    bw.Write(1); bw.Write(Model); bw.Write(TotalMs); bw.Write(Samples.Count);
                    foreach (var s in Samples) { bw.Write(s.T); bw.Write(s.P.X); bw.Write(s.P.Y); bw.Write(s.P.Z); bw.Write(s.Q.X); bw.Write(s.Q.Y); bw.Write(s.Q.Z); bw.Write(s.Q.W); }
                    bw.Write(Splits.Count); foreach (var t in Splits) bw.Write(t);
                }
                Log.Info("Ghost saved for " + raceId + " (" + Samples.Count + " samples, " + TotalMs + " ms)");
            }
            catch (Exception ex) { Log.Error("Ghost save", ex); }
        }

        // ---------------------------------------------------------------- recording
        private int lastRec = -1000;
        public void Record(Vehicle car, int t)
        {
            while (Samples.Count > 0 && Samples[Samples.Count - 1].T >= t) Samples.RemoveAt(Samples.Count - 1);   // rewound
            if (t - lastRec < 100 && Samples.Count > 0) return;
            lastRec = t;
            Samples.Add(new Sample { T = t, P = car.Position, Q = car.Quaternion });
        }
        public void Split(int cp, int t) { while (Splits.Count > cp) Splits.RemoveAt(Splits.Count - 1); while (Splits.Count < cp) Splits.Add(t); Splits.Add(t); }

        // ---------------------------------------------------------------- playback
        private Vehicle car; private int idx;
        public void Spawn()
        {
            try
            {
                var m = new Model(Model); if (!m.Request(3000)) return;
                car = World.CreateVehicle(m, Samples[0].P, 0f); m.MarkAsNoLongerNeeded();
                if (car == null || !car.Exists()) return;
                car.IsPersistent = true; car.Quaternion = Samples[0].Q;
                Function.Call(Hash.SET_ENTITY_COLLISION, car, false, false);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, car, true);
                Function.Call(Hash.SET_ENTITY_ALPHA, car, 110, false);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, car, true, true, false);
                car.AreLightsOn = true; car.IsInvincible = true;
                Function.Call(Hash.SET_ENTITY_CAN_BE_DAMAGED, car, false);
            }
            catch (Exception ex) { Log.Error("Ghost spawn", ex); }
        }

        public void Play(int t)
        {
            if (car == null || !car.Exists()) return;
            if (t < Samples[0].T) t = Samples[0].T;
            while (idx > 0 && Samples[idx].T > t) idx--;
            while (idx < Samples.Count - 2 && Samples[idx + 1].T <= t) idx++;
            var a = Samples[idx]; var b = Samples[Math.Min(idx + 1, Samples.Count - 1)];
            float k = b.T > a.T ? Math.Max(0f, Math.Min(1f, (t - a.T) / (float)(b.T - a.T))) : 0f;
            var p = Vector3.Lerp(a.P, b.P, k);
            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, car, p.X, p.Y, p.Z, false, false, false);
            car.Quaternion = Quaternion.Slerp(a.Q, b.Q, k);
            bool done = t > Samples[Samples.Count - 1].T + 1500;
            Function.Call(Hash.SET_ENTITY_ALPHA, car, done ? 0 : 110, false);
        }

        public void Delete() { try { if (car != null && car.Exists()) car.Delete(); } catch { } car = null; }
    }
}
