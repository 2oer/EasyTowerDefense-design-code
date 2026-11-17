using System.Collections.Generic;
using Game.Combat;
using Game.Core;
using Game.Model;

namespace Game.Targeting
{
    /// <summary>
    /// 权重 = 伤害系数 × 最大生命 ÷ 距离;距离取边缘距离 max(1, 中心距 − 半径)。
    /// 集中:直接取权重最高。扫射:再把"已被分配伤害"的覆盖率折进权重,避免反复砸同一只。
    /// </summary>
    public static class TargetSelector
    {
        private const float WeightEpsilon = 1e-4f;

        public static int SelectBest(
            IReadOnlyList<ITargetCandidate> candidates,
            Vec2 origin,
            float range,
            in DamagePacket probe,
            TowerStrategy strategy)
        {
            int bestIndex = -1;
            float bestWeight = 0f;
            float bestHp = 0f;
            int bestKey = int.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                ITargetCandidate c = candidates[i];
                if (c.Hp <= 0f) continue;

                float center = Vec2.Distance(origin, c.Position);
                if (center - c.Radius > range) continue;

                float coef = DamageCalculator.Multiplier(probe, c.Def);
                if (coef <= 0f) continue;

                float edge = center - c.Radius;
                if (edge < 1f) edge = 1f;

                float weight = coef * c.MaxHp / edge;

                if (strategy == TowerStrategy.Spray)
                {
                    float remaining = c.Hp - c.DamageSent;
                    if (remaining <= 0f) continue;
                    weight *= MathUtil.Clamp01(remaining / c.MaxHp);
                }
                else if (strategy == TowerStrategy.Strategic)
                {
                    // 斩首:按当前生命值排序,不按最大生命与距离。
                    weight = c.Hp;
                    if (weight <= 0f) continue;
                }

                bool better;
                if (bestIndex < 0)
                {
                    better = true;
                }
                else if (weight > bestWeight + WeightEpsilon)
                {
                    better = true;
                }
                else if (weight < bestWeight - WeightEpsilon)
                {
                    better = false;
                }
                else if (c.Hp > bestHp + WeightEpsilon)
                {
                    better = true;
                }
                else if (c.Hp < bestHp - WeightEpsilon)
                {
                    better = false;
                }
                else
                {
                    better = c.Id.Index < bestKey;
                }

                if (!better) continue;
                bestIndex = i;
                bestWeight = weight;
                bestHp = c.Hp;
                bestKey = c.Id.Index;
            }

            return bestIndex;
        }
    }
}
