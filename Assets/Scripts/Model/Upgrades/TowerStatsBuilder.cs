using System.Collections.Generic;

namespace Game.Model
{
    /// <summary>把基础面板与已选升级节点合成为运行时属性。增幅一律乘算。</summary>
    public static class TowerStatsBuilder
    {
        public static TowerRuntimeStats Build(TowerDef def, IReadOnlyList<UpgradeNodeDef> nodes)
        {
            TowerRuntimeStats stats = TowerRuntimeStats.FromDef(def);
            if (nodes == null) return stats;

            for (int i = 0; i < nodes.Count; i++)
            {
                Apply(stats, nodes[i]);
            }

            return stats;
        }

        private static void Apply(TowerRuntimeStats stats, UpgradeNodeDef node)
        {
            if (node == null) return;

            for (int i = 0; i < node.Modifiers.Count; i++)
            {
                StatModifier mod = node.Modifiers[i];
                ApplyModifier(stats, mod);
            }

            if (node.Pierce.HasValue) stats.Pierce = node.Pierce.Value;
            if (node.Strategy.HasValue) stats.Strategy = node.Strategy.Value;
        }

        private static void ApplyModifier(TowerRuntimeStats stats, StatModifier mod)
        {
            switch (mod.Key)
            {
                case StatKey.Damage:
                    stats.Damage = Combine(stats.Damage, mod);
                    break;
                case StatKey.FireRate:
                    stats.FireRate = Combine(stats.FireRate, mod);
                    break;
                case StatKey.Range:
                    stats.Range = Combine(stats.Range, mod);
                    break;
                case StatKey.TurnSpeed:
                    stats.TurnSpeed = Combine(stats.TurnSpeed, mod);
                    break;
                case StatKey.BlockForce:
                    stats.BlockForce = Combine(stats.BlockForce, mod);
                    break;
                case StatKey.Retention:
                    stats.Retention = Combine(stats.Retention, mod);
                    break;
                case StatKey.Ammo:
                    stats.Ammo = mod.Op == ModifierOp.Set ? (int)mod.Value : (int)Combine(stats.Ammo, mod);
                    break;
                case StatKey.ReloadTime:
                    stats.ReloadTime = Combine(stats.ReloadTime, mod);
                    break;
                case StatKey.BeamWidth:
                    stats.BeamWidth = Combine(stats.BeamWidth, mod);
                    break;
                case StatKey.BulletRadius:
                    stats.BulletRadius = Combine(stats.BulletRadius, mod);
                    break;
                case StatKey.ExplosionRadius:
                    stats.ExplosionRadius = Combine(stats.ExplosionRadius, mod);
                    break;
            }
        }

        private static float Combine(float current, StatModifier mod)
        {
            switch (mod.Op)
            {
                case ModifierOp.Add: return current + mod.Value;
                case ModifierOp.Mul: return current * mod.Value;
                case ModifierOp.Set: return mod.Value;
                default: return current;
            }
        }
    }
}
