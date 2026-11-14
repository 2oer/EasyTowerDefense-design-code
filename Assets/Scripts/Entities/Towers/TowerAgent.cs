using System;
using System.Collections.Generic;
using Game.Combat;
using Game.Core;
using Game.Model;
using Game.Targeting;

namespace Game.Entity
{
    /// <summary>hitscan 命中查询:矩形 / 线段的中心 + 半径判定,结果按距离升序。</summary>
    public static class HitscanResolver
    {
        public static void Query(
            Vec2 origin,
            Vec2 direction,
            float range,
            float halfWidth,
            IReadOnlyList<EnemyRuntime> enemies,
            List<EnemyRuntime> results)
        {
            results.Clear();
            if (enemies.Count == 0) return;

            var hits = new List<Hit>();
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyRuntime enemy = enemies[i];
                if (!enemy.Alive) continue;

                Vec2 offset = enemy.Position - origin;
                float t = Vec2.Dot(offset, direction);
                if (t + enemy.Radius < 0f) continue;
                if (t - enemy.Radius > range) continue;

                float lateral = Math.Abs(Vec2.Cross(direction, offset));
                if (lateral > halfWidth + enemy.Radius) continue;

                hits.Add(new Hit(t, enemy));
            }

            hits.Sort(CompareHits);
            for (int i = 0; i < hits.Count; i++) results.Add(hits[i].Enemy);
        }

        private static int CompareHits(Hit a, Hit b)
        {
            int cmp = a.T.CompareTo(b.T);
            if (cmp != 0) return cmp;
            return a.Enemy.Id.Index.CompareTo(b.Enemy.Id.Index);
        }

        private readonly struct Hit
        {
            public readonly float T;
            public readonly EnemyRuntime Enemy;

            public Hit(float t, EnemyRuntime enemy)
            {
                T = t;
                Enemy = enemy;
            }
        }
    }

    /// <summary>防御塔运行时:M1 只支持 hitscan 直线(机枪)与 hitscan 矩形(激光)。</summary>
    public sealed class TowerAgent
    {
        public EntityId Id { get; private set; }
        public TowerDef Def { get; private set; } = null!;
        public TowerRuntimeStats Stats { get; private set; } = null!;
        public Vec2 Position { get; private set; }
        public TurretMotor Motor { get; } = new TurretMotor();

        /// <summary>已选升级节点(按等级升序)。</summary>
        public List<UpgradeNodeDef> Upgrades { get; } = new List<UpgradeNodeDef>();

        public float Cooldown { get; private set; }
        public float ReloadTimer { get; private set; }
        public int Ammo { get; private set; }
        public int LastHitCount { get; private set; }

        public bool Reloading => ReloadTimer > 0f;
        public int Level => 1 + Upgrades.Count;

        public void Initialize(EntityId id, TowerDef def, Vec2 position)
        {
            Id = id;
            Def = def;
            Position = position;
            Upgrades.Clear();
            Stats = TowerStatsBuilder.Build(def, Upgrades);
            Ammo = Stats.Ammo;
            Cooldown = 0f;
            ReloadTimer = 0f;
            LastHitCount = 0;
            Motor.Reset((float)Math.PI * 0.5f);
        }

        public bool HasUpgrade(string nodeId)
        {
            for (int i = 0; i < Upgrades.Count; i++)
            {
                if (string.Equals(Upgrades[i].Id, nodeId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>应用一次升级并整份重建运行时属性;新弹匣立即装满。</summary>
        public void ApplyUpgrade(UpgradeNodeDef node)
        {
            Upgrades.Add(node);
            Stats = TowerStatsBuilder.Build(Def, Upgrades);
            Ammo = Stats.Ammo;
            Cooldown = 0f;
            ReloadTimer = 0f;
        }

        /// <summary>返回本帧是否开火。</summary>
        public bool Tick(float dt, IReadOnlyList<EnemyRuntime> enemies, List<EnemyRuntime> hitBuffer)
        {
            if (Reloading)
            {
                ReloadTimer -= dt;
                if (ReloadTimer <= 0f)
                {
                    ReloadTimer = 0f;
                    Ammo = Stats.Ammo;
                }
                LastHitCount = 0;
                return false;
            }

            if (Cooldown > 0f) Cooldown -= dt;

            var probe = new DamagePacket
            {
                Amount = Stats.Damage,
                Type = Stats.DamageType,
                Pierce = Stats.Pierce,
                Retention = Stats.Retention,
                BlockForce = Stats.BlockForce,
                Source = Id
            };

            int targetIndex = TargetSelector.SelectBest(enemies, Position, Stats.Range, probe, Stats.Strategy);
            if (targetIndex < 0)
            {
                LastHitCount = 0;
                return false;
            }

            EnemyRuntime target = enemies[targetIndex];
            Vec2 toTarget = target.Position - Position;
            float desired = (float)Math.Atan2(toTarget.Y, toTarget.X);
            float faceDistance = Math.Max(1f, toTarget.Length - target.Radius);
            float halfWidth = HalfWidth();
            float tolerance = Math.Max(
                TurretMotor.PointBlankToleranceRad,
                (float)Math.Atan2(halfWidth, faceDistance));

            if (!Motor.TurnTowards(desired, Stats.TurnSpeed, tolerance, dt))
            {
                LastHitCount = 0;
                return false;
            }

            if (Cooldown > 0f)
            {
                LastHitCount = 0;
                return false;
            }

            Fire(enemies, hitBuffer);
            return true;
        }

        private float HalfWidth() =>
            Stats.Delivery == DeliveryKind.HitscanRect ? Stats.BeamWidth * 0.5f : Stats.BulletRadius;

        private void Fire(IReadOnlyList<EnemyRuntime> enemies, List<EnemyRuntime> hitBuffer)
        {
            Cooldown = 1f / Stats.FireRate;

            if (Stats.Ammo > 0)
            {
                Ammo--;
                if (Ammo <= 0)
                {
                    Ammo = 0;
                    ReloadTimer = Stats.ReloadTime;
                }
            }

            float angle = Motor.AimAngle;
            var direction = new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle));
            HitscanResolver.Query(Position, direction, Stats.Range, HalfWidth(), enemies, hitBuffer);

            var packet = new DamagePacket
            {
                Amount = Stats.Damage,
                Type = Stats.DamageType,
                Pierce = Stats.Pierce,
                Retention = Stats.Retention,
                BlockForce = Stats.BlockForce,
                Source = Id
            };

            float retention = Stats.Retention;
            int hits = 0;
            for (int i = 0; i < hitBuffer.Count; i++)
            {
                EnemyRuntime enemy = hitBuffer[i];
                if (!enemy.Alive) continue;

                float amount = DamageCalculator.Amount(packet, enemy.Def);
                enemy.ApplyDamage(amount, Stats.BlockForce);
                hits++;

                retention = PenetrationResolver.Consume(retention, packet, enemy.Def);
                if (!PenetrationResolver.CanContinue(retention)) break;
            }

            LastHitCount = hits;
        }
    }
}
