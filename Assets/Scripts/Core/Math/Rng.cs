using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>xorshift128+ 自带种子随机数。核心层禁用 UnityEngine.Random。</summary>
    public sealed class Rng
    {
        private ulong _s0;
        private ulong _s1;

        public Rng(ulong seed)
        {
            ulong z = seed;
            _s0 = SplitMix64(ref z);
            _s1 = SplitMix64(ref z);
            if (_s0 == 0UL && _s1 == 0UL) _s0 = 0x9E3779B97F4A7C15UL;
        }

        private static ulong SplitMix64(ref ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            ulong x = z;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        public ulong NextULong()
        {
            ulong x = _s0;
            ulong y = _s1;
            _s0 = y;
            x ^= x << 23;
            _s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
            return _s1 + y;
        }

        public uint NextUInt() => (uint)(NextULong() >> 32);

        /// <summary>[0,1) 均匀分布。</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>[minInclusive, maxExclusive)。</summary>
        public int RangeInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }

        public bool NextBool() => (NextUInt() & 1u) == 1u;

        /// <summary>按权重抽取,权重列表与选项一一对应。全零权重时退化为均匀抽取。</summary>
        public int PickWeighted(IReadOnlyList<float> weights)
        {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++) total += weights[i] > 0f ? weights[i] : 0f;
            if (total <= 0f) return RangeInt(0, weights.Count);
            float roll = NextFloat() * total;
            float acc = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                float w = weights[i] > 0f ? weights[i] : 0f;
                acc += w;
                if (roll < acc) return i;
            }
            return weights.Count - 1;
        }
    }
}
