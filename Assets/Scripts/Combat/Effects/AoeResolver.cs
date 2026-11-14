using System.Collections.Generic;
using Game.Core;
using Game.Model;

namespace Game.Combat
{
    /// <summary>
    /// 范围伤害 / 区域效果的目标视图。Entity 层实现,Combat 层不反向依赖 Entity。
    /// </summary>
    public interface IAoETarget
    {
        EntityId Id { get; }
        Vec2 Position { get; }
        float Radius { get; }
        bool Alive { get; }
        EnemyDef Def { get; }
        StatusSet Status { get; }
        DamageContext BuildDamageContext();
        void ApplyDamage(float amount, float blockForce);
        void ApplyBlock(float force);
    }

    /// <summary>圆形命中判定:圆心距 ≤ 半径 + 目标体积半径。</summary>
    public static class AoeQuery
    {
        public static void Query(
            Vec2 center,
            float radius,
            IReadOnlyList<IAoETarget> targets,
            List<IAoETarget> results)
        {
            results.Clear();
            if (radius <= 0f) return;
            for (int i = 0; i < targets.Count; i++)
            {
                IAoETarget target = targets[i];
                if (Contains(center, radius, target)) results.Add(target);
            }
        }

        public static bool Contains(Vec2 center, float radius, IAoETarget target) =>
            target.Alive && Vec2.Distance(center, target.Position) <= radius + target.Radius;
    }

    /// <summary>爆炸结算:范围内每个敌人各自按自己的护甲 / 衰减 / 状态吃满额伤害(无距离衰减)。</summary>
    public static class ExplosionResolver
    {
        public static int Apply(
            in DamagePacket packet,
            Vec2 center,
            float radius,
            IReadOnlyList<IAoETarget> targets,
            List<IAoETarget> hitBuffer)
        {
            AoeQuery.Query(center, radius, targets, hitBuffer);
            int hits = 0;
            for (int i = 0; i < hitBuffer.Count; i++)
            {
                IAoETarget target = hitBuffer[i];
                if (!target.Alive) continue;
                float amount = DamageCalculator.Amount(packet, target.Def, target.BuildDamageContext());
                target.ApplyDamage(amount, packet.BlockForce);
                hits++;
            }
            return hits;
        }
    }

    /// <summary>充电引爆:电属性范围伤害(走护甲系数与全额属性衰减)。</summary>
    public static class ChargeResolver
    {
        public static int Detonate(
            in ChargePayload payload,
            Vec2 center,
            IReadOnlyList<IAoETarget> targets,
            List<IAoETarget> hitBuffer)
        {
            var packet = new DamagePacket
            {
                Amount = payload.Damage,
                Type = DamageType.Elemental,
                Pierce = payload.Pierce,
                IsDot = false,
                BlockForce = 0f
            };
            return ExplosionResolver.Apply(packet, center, payload.Radius, targets, hitBuffer);
        }
    }
}
