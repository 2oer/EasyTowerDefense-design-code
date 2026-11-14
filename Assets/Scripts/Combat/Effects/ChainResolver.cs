using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Combat
{
    /// <summary>电弧连锁:从首个目标开始,逐跳选择范围内最近的未命中目标。</summary>
    public static class ChainResolver
    {
        public static int Apply(
            Vec2 origin,
            IAoETarget first,
            in DamagePacket packet,
            int extraJumps,
            float range,
            float falloff,
            IReadOnlyList<IAoETarget> targets,
            List<IAoETarget> hitBuffer)
        {
            hitBuffer.Clear();

            IAoETarget? current = first;
            float scale = 1f;
            int hits = 0;
            int maxHits = 1 + (extraJumps > 0 ? extraJumps : 0);

            while (current != null && hits < maxHits)
            {
                if (!current.Alive) break;

                var scaled = packet;
                scaled.Amount = packet.Amount * scale;
                float amount = DamageCalculator.Amount(scaled, current.Def, current.BuildDamageContext());
                current.ApplyDamage(amount, packet.BlockForce);
                hitBuffer.Add(current);
                hits++;

                scale *= 1f - falloff;
                if (scale < 0.30f) scale = 0.30f;

                current = FindNext(current, range, targets, hitBuffer);
            }

            // origin 只用于语义/未来的电弧表现;不参与命中判定。
            _ = origin;
            return hits;
        }

        private static IAoETarget? FindNext(
            IAoETarget from,
            float range,
            IReadOnlyList<IAoETarget> targets,
            List<IAoETarget> visited)
        {
            IAoETarget? best = null;
            float bestDistance = range;

            for (int i = 0; i < targets.Count; i++)
            {
                IAoETarget candidate = targets[i];
                if (!candidate.Alive) continue;
                if (Contains(visited, candidate)) continue;

                float distance = Vec2.Distance(from.Position, candidate.Position);
                if (distance > bestDistance) continue;
                if (best != null && Math.Abs(distance - bestDistance) < 1e-4f &&
                    candidate.Id.Index > best.Id.Index) continue;

                best = candidate;
                bestDistance = distance;
            }

            return best;
        }

        private static bool Contains(List<IAoETarget> list, IAoETarget target)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id.Index == target.Id.Index && list[i].Id.Generation == target.Id.Generation)
                    return true;
            }
            return false;
        }
    }
}
