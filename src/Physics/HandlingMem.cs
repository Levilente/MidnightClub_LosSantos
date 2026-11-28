using System;
using System.Runtime.InteropServices;
using GTA;

namespace MidnightClub.Physics
{
    /// <summary>Correct writes into GTA's handling data. GTA doesn't keep handling values the way handling.meta lists
    /// them: front/rear biases are stored as a pair (front x2 and rear x2 when they share a total of 2), and several
    /// curve values have a precomputed inverse next to them (traction max / min, lateral curve, spring delta).
    /// ScriptHookVDotNet writes only the single field, so a "TractionBiasFront = 0.5" halved the front grip and
    /// curve changes left GTA calculating with the old inverses. Here both halves are written, the pair convention
    /// is detected per car from its stock values, and the whole block is put back byte-exact afterwards.</summary>
    public static class HandlingMem
    {
        public const int Size = 0x140;
        public const int DriveBias = 0x48, BrakeBias = 0x74, TractionBias = 0xB0, SuspBias = 0xD4, ArbBias = 0xE0;
        public const int TractionMax = 0x88, TractionMin = 0x90, TractionLateral = 0x98, SpringDelta = 0xA0;

        public static byte[] Snapshot(HandlingData h)
        {
            var b = new byte[Size];
            if (h != null && h.IsValid) Marshal.Copy(h.MemoryAddress, b, 0, Size);
            return b;
        }
        public static void Restore(HandlingData h, byte[] snap)
        {
            if (h != null && h.IsValid && snap != null && snap.Length == Size) Marshal.Copy(snap, 0, h.MemoryAddress, Size);
        }

        private static float R(HandlingData h, int o) { return BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(h.MemoryAddress, o)), 0); }
        private static void W(HandlingData h, int o, float v) { Marshal.WriteInt32(h.MemoryAddress, o, BitConverter.ToInt32(BitConverter.GetBytes(v), 0)); }
        private static float S(byte[] snap, int o) { return BitConverter.ToSingle(snap, o); }

        /// <summary>A front/rear pair as a front share 0..1.</summary>
        public static float GetBias(HandlingData h, int front)
        {
            float f = R(h, front), r = R(h, front + 4), sum = f + r;
            return sum > 0.001f ? Math.Max(0f, Math.Min(1f, f / sum)) : 0.5f;
        }
        /// <summary>Writes a front share into the pair, keeping the car's own total (1 or 2).</summary>
        public static void SetBias(HandlingData h, int front, float share, byte[] snap)
        {
            share = Math.Max(0f, Math.Min(1f, share));
            float total = snap != null ? S(snap, front) + S(snap, front + 4) : R(h, front) + R(h, front + 4);
            float k = total > 1.5f ? 2f : 1f;
            W(h, front, share * k); W(h, front + 4, (1f - share) * k);
        }

        /// <summary>Re-derives the inverse that GTA keeps next to a curve value (only where the stock data has one).</summary>
        public static void SyncInverse(HandlingData h, int off, byte[] snap)
        {
            if (snap == null) return;
            float v0 = S(snap, off), i0 = S(snap, off + 4);
            if (Math.Abs(v0) < 1e-5f || Math.Abs(v0 * i0 - 1f) > 0.03f) return;      // no inverse there
            float v = R(h, off);
            if (Math.Abs(v) > 1e-5f) W(h, off + 4, 1f / v);
        }
        public static void SyncAll(HandlingData h, byte[] snap)
        {
            SyncInverse(h, TractionMax, snap); SyncInverse(h, TractionMin, snap); SyncInverse(h, TractionLateral, snap); SyncInverse(h, SpringDelta, snap);
        }

        public static string Describe(byte[] snap)
        {
            Func<int, string> p = o => S(snap, o).ToString("0.###") + "/" + S(snap, o + 4).ToString("0.###");
            return "drive " + p(DriveBias) + ", brake " + p(BrakeBias) + ", traction " + p(TractionBias) + ", susp " + p(SuspBias) + ", arb " + p(ArbBias)
                + ", tmax " + p(TractionMax) + ", tmin " + p(TractionMin) + ", lat " + p(TractionLateral) + ", delta " + p(SpringDelta);
        }
    }
}
