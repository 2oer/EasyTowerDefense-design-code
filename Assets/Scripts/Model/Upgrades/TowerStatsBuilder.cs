using System;
using System.Collections.Generic;

namespace Game.Model
{
    /// <summary>把基础面板与已选升级节点合成为运行时属性。增幅一律乘算。</summary>
    public static class TowerStatsBuilder
    {
        /// <summary>射程硬上限:防止后期升级覆盖整张地图,导致敌人生成区被完全覆盖。</summary>
        public const float MaxRange = 60f;

        public static TowerRuntimeStats Build(TowerDef def, IReadOnlyList<UpgradeNodeDef> nodes)
        {
            TowerRuntimeStats stats = TowerRuntimeStats.FromDef(def);
            if (nodes == null)
            {
                ClampRange(stats);
                return stats;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                Apply(stats, nodes[i]);
            }

            if (stats.Continuous) ApplyContinuous(stats);
            ClampRange(stats);

            return stats;
        }

        private static void ClampRange(TowerRuntimeStats stats)
        {
            if (stats.Range > MaxRange) stats.Range = MaxRange;
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
            if (node.Continuous) stats.Continuous = true;
            if (!string.IsNullOrEmpty(node.Mechanic)) stats.Mechanic = node.Mechanic;
        }

        /// <summary>
        /// 持续照射型:照射时长 = 弹容 ÷ 射速,重装间隔固定;结算频率 ×1.5(单次伤害 ×0.75、频率 ×2)。
        /// 折算成等效射速后,塔按普通节奏开火,不再消耗弹容。
        /// </summary>
        private static void ApplyContinuous(TowerRuntimeStats stats)
        {
            float interval = stats.ReloadTime > 0f ? stats.ReloadTime : 0.5f;
            float duration = (stats.Ammo > 0 ? stats.Ammo : 100f) / Math.Max(0.0001f, stats.FireRate);
            float duty = duration / (duration + interval);
            stats.FireRate *= 1.5f * duty;
            stats.Ammo = 1;
            stats.ReloadTime = 0f;
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
                case StatKey.BeamCount:
                    stats.BeamCount = mod.Op == ModifierOp.Set ? (int)mod.Value : (int)Combine(stats.BeamCount, mod);
                    break;
                case StatKey.MinRange:
                    stats.MinRange = Combine(stats.MinRange, mod);
                    break;
                case StatKey.ProjectileSpeed:
                    stats.ProjectileSpeed = Combine(stats.ProjectileSpeed, mod);
                    break;
                case StatKey.Salvo:
                    stats.Salvo = mod.Op == ModifierOp.Set ? (int)mod.Value : (int)Combine(stats.Salvo, mod);
                    break;
                case StatKey.ChainCount:
                    stats.ChainCount = mod.Op == ModifierOp.Set ? (int)mod.Value : (int)Combine(stats.ChainCount, mod);
                    break;
                case StatKey.ChainRange:
                    stats.ChainRange = Combine(stats.ChainRange, mod);
                    break;
                case StatKey.ChainFalloff:
                    stats.ChainFalloff = Combine(stats.ChainFalloff, mod);
                    break;
                case StatKey.SubCount:
                    stats.SubCount = mod.Op == ModifierOp.Set ? (int)mod.Value : (int)Combine(stats.SubCount, mod);
                    break;
                case StatKey.SubDamage:
                    stats.SubDamage = Combine(stats.SubDamage, mod);
                    break;
                case StatKey.SubRadius:
                    stats.SubRadius = Combine(stats.SubRadius, mod);
                    break;
                case StatKey.SpreadRadius:
                    stats.SpreadRadius = Combine(stats.SpreadRadius, mod);
                    break;
                case StatKey.FirstStageDamage:
                    stats.FirstStageDamage = Combine(stats.FirstStageDamage, mod);
                    break;
                case StatKey.SecondStageDamage:
                    stats.SecondStageDamage = Combine(stats.SecondStageDamage, mod);
                    break;
                case StatKey.SecondStageRadius:
                    stats.SecondStageRadius = Combine(stats.SecondStageRadius, mod);
                    break;
                case StatKey.ZoneScale:
                    stats.ZoneScale = Combine(stats.ZoneScale, mod);
                    break;
                case StatKey.DroneSquad:
                    stats.DroneSquad = mod.Op == ModifierOp.Set ? (int)mod.Value : (int)Combine(stats.DroneSquad, mod);
                    break;
                case StatKey.DroneCapacity:
                    stats.DroneCapacity = mod.Op == ModifierOp.Set ? (int)mod.Value : (int)Combine(stats.DroneCapacity, mod);
                    break;
                case StatKey.DroneReload:
                    stats.DroneReload = Combine(stats.DroneReload, mod);
                    break;
                case StatKey.DroneDeployInterval:
                    stats.DroneDeployInterval = Combine(stats.DroneDeployInterval, mod);
                    break;
                case StatKey.DroneAmmo:
                    stats.DroneAmmo = Combine(stats.DroneAmmo, mod);
                    break;
                case StatKey.DroneFireRate:
                    stats.DroneFireRate = Combine(stats.DroneFireRate, mod);
                    break;
                case StatKey.DroneDamage:
                    stats.DroneDamage = Combine(stats.DroneDamage, mod);
                    break;
                case StatKey.DroneRange:
                    stats.DroneRange = Combine(stats.DroneRange, mod);
                    break;
                case StatKey.DroneSpeed:
                    stats.DroneSpeed = Combine(stats.DroneSpeed, mod);
                    break;
                case StatKey.DroneSelfDestructRadius:
                    stats.DroneSelfDestructRadius = Combine(stats.DroneSelfDestructRadius, mod);
                    break;
                case StatKey.DroneSelfDestructDamage:
                    stats.DroneSelfDestructDamage = Combine(stats.DroneSelfDestructDamage, mod);
                    break;
                case StatKey.RadarRange:
                    stats.RadarRange = Combine(stats.RadarRange, mod);
                    break;
                case StatKey.ChargeStacks:
                    stats.ChargeStacks = mod.Op == ModifierOp.Set ? (int)mod.Value : (int)Combine(stats.ChargeStacks, mod);
                    break;
                case StatKey.AuraRadius:
                    stats.AuraRadius = Combine(stats.AuraRadius, mod);
                    break;
                case StatKey.AuraInterval:
                    stats.AuraInterval = Combine(stats.AuraInterval, mod);
                    break;
                case StatKey.ProjectileLifetime:
                    stats.ProjectileLifetime = Combine(stats.ProjectileLifetime, mod);
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
