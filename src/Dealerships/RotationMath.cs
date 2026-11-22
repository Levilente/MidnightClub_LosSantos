using System;
using System.Collections.Generic;
using System.Linq;

namespace MidnightClub.Dealerships
{
    /// <summary>Pure, deterministic stock rotation (no game calls, unit-tested). Items are walked through
    /// seeded permutations ("cycles"); each rotation takes the next N positions, so every item is offered
    /// once per cycle and the result depends only on (dealer id, rotation index).</summary>
    public static class RotationMath
    {
        public static int Seed(params object[] parts)
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (var p in parts) foreach (char c in p.ToString()) h = (h ^ c) * 16777619;
                return h & 0x7fffffff;
            }
        }

        public static int StockSize(string id, int min, int max, int rotation)
        {
            return min + Seed(id, "size", rotation) % (max - min + 1);
        }

        public static long CycleStart(string id, int min, int max, int rotation)
        {
            long s = 0;
            for (int r = 0; r < rotation; r++) s += StockSize(id, min, max, r);
            return s;
        }

        public static List<T> Permutation<T>(string id, IList<T> pool, long cycle)
        {
            var rnd = new Random(Seed(id, "cycle", cycle));
            var p = pool.ToList();
            for (int i = p.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = p[i]; p[i] = p[j]; p[j] = t; }
            return p;
        }

        /// <summary>The items offered in a rotation (distinct, at most pool size).</summary>
        public static List<T> Pick<T>(string id, IList<T> pool, int min, int max, int rotation)
        {
            var result = new List<T>();
            if (pool.Count == 0) return result;
            int size = Math.Min(StockSize(id, min, max, rotation), pool.Count);
            long start = CycleStart(id, min, max, rotation);
            var seen = new HashSet<T>();
            List<T> perm = null; long permCycle = -1;
            for (long q = start; result.Count < size && q < start + pool.Count * 2L; q++)
            {
                long cycle = q / pool.Count;
                if (cycle != permCycle) { perm = Permutation(id, pool, cycle); permCycle = cycle; }
                var v = perm[(int)(q % pool.Count)];
                if (seen.Add(v)) result.Add(v);
            }
            return result;
        }
    }
}
