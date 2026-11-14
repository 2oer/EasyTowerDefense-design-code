using System.Collections.Generic;
using Game.Core;

namespace Game.Combat
{
    /// <summary>场地实体:避雷针。持续 60 秒或被连锁次数耗尽。</summary>
    public sealed class LightningRod
    {
        public Vec2 Position { get; set; }
        public float Radius { get; set; }
        public float Remaining { get; set; }
        public int Charges { get; set; }
    }

    /// <summary>
    /// 避雷针系统。电弧连锁时优先锁定最近的避雷针:不消耗连锁次数,并额外 +1 次连锁。
    /// 电弧塔尚未接入,当前只提供查询与寿命 / 次数管理。
    /// </summary>
    public sealed class LightningRodSystem
    {
        /// <summary>避雷针提供的额外连锁次数。</summary>
        public const int ExtraChainLinks = 1;

        private readonly List<LightningRod> _rods = new List<LightningRod>();

        public IReadOnlyList<LightningRod> Rods => _rods;

        public LightningRod Add(Vec2 position, float radius, float duration, int charges)
        {
            var rod = new LightningRod
            {
                Position = position,
                Radius = radius,
                Remaining = duration,
                Charges = charges
            };
            _rods.Add(rod);
            return rod;
        }

        /// <summary>推进寿命;次数耗尽或超时的避雷针被移除。</summary>
        public void Tick(float dt)
        {
            for (int i = _rods.Count - 1; i >= 0; i--)
            {
                LightningRod rod = _rods[i];
                rod.Remaining -= dt;
                if (rod.Remaining <= 0f || rod.Charges <= 0) _rods.RemoveAt(i);
            }
        }

        /// <summary>找出 origin 附近可用的最近避雷针(不消耗次数)。</summary>
        public LightningRod? FindForChain(Vec2 origin, float range)
        {
            LightningRod? best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _rods.Count; i++)
            {
                LightningRod rod = _rods[i];
                if (rod.Charges <= 0 || rod.Remaining <= 0f) continue;
                float distance = Vec2.Distance(origin, rod.Position);
                if (distance > range || distance >= bestDistance) continue;
                best = rod;
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>连锁实际落到避雷针上时扣一次次数。</summary>
        public static void Consume(LightningRod rod)
        {
            if (rod.Charges > 0) rod.Charges--;
        }

        public void Clear()
        {
            _rods.Clear();
        }
    }
}
