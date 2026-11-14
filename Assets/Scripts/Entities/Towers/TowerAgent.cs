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

    /// <summary>
    /// 防御塔运行时:直线 hitscan(机枪)、矩形光束(激光)、连锁(电弧)、
    /// 抛射 / 追踪弹丸(迫击炮 / 火箭)与无人机库。
    /// </summary>
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

        /// <summary>无人机库:剩余可部署机体与当前编队数量。</summary>
        public int DroneReserve { get; private set; }
        public int DeployedDrones { get; private set; }
        public float DeployTimer { get; private set; }
        public int SquadsDeployed { get; private set; }

        public bool Reloading => ReloadTimer > 0f;
        public int Level => 1 + Upgrades.Count;

        private readonly List<EnemyRuntime> _rangeBuffer = new List<EnemyRuntime>();
        private readonly List<int> _excluded = new List<int>();
        private readonly List<int> _salvoBuffer = new List<int>();

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
            DroneReserve = Stats.DroneCapacity;
            DeployedDrones = 0;
            SquadsDeployed = 0;
            DeployTimer = Stats.DroneDeployInterval;
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
            int oldCapacity = Stats.DroneCapacity;
            int oldReserve = DroneReserve;
            int oldSquad = Stats.DroneSquad;

            Upgrades.Add(node);
            Stats = TowerStatsBuilder.Build(Def, Upgrades);
            Ammo = Stats.Ammo;
            Cooldown = 0f;
            ReloadTimer = 0f;

            // 容量扩张补足库存;容量收缩只扣减未部署部分。
            int delta = Stats.DroneCapacity - oldCapacity;
            DroneReserve = oldReserve + delta;
            if (DroneReserve > Stats.DroneCapacity) DroneReserve = Stats.DroneCapacity;
            if (DroneReserve < 0) DroneReserve = 0;

            if (Stats.DroneSquad > oldSquad && DeployedDrones > 0)
            {
                DroneReserve += Stats.DroneSquad - oldSquad;
                if (DroneReserve > Stats.DroneCapacity) DroneReserve = Stats.DroneCapacity;
            }
        }

        /// <summary>返回本帧是否开火 / 派出编队。</summary>
        public bool Tick(
            float dt,
            IReadOnlyList<EnemyRuntime> enemies,
            List<EnemyRuntime> hitBuffer,
            CombatWorld combat)
        {
            if (Stats.DroneCapacity > 0)
            {
                TickDroneCarrier(dt, enemies, combat);
                return false;
            }

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

            var probe = BuildPacket(Stats.Damage);
            int targetIndex = SelectTarget(enemies, probe);
            if (targetIndex < 0)
            {
                LastHitCount = 0;
                return false;
            }

            EnemyRuntime target = enemies[targetIndex];
            Vec2 toTarget = target.Position - Position;
            float desired = (float)Math.Atan2(toTarget.Y, toTarget.X);

            if (Stats.NoTurn)
            {
                Motor.SnapTo(desired);
            }
            else
            {
                float faceDistance = Math.Max(1f, toTarget.Length - target.Radius);
                float tolerance = Math.Max(
                    TurretMotor.PointBlankToleranceRad,
                    (float)Math.Atan2(HalfWidth(), faceDistance));
                if (!Motor.TurnTowards(desired, Stats.TurnSpeed, tolerance, dt))
                {
                    LastHitCount = 0;
                    return false;
                }
            }

            if (Cooldown > 0f)
            {
                LastHitCount = 0;
                return false;
            }

            if (Stats.Delivery == DeliveryKind.Chain &&
                Stats.Mechanic != TowerMechanics.BallLightning &&
                Stats.Mechanic != TowerMechanics.LeakCurrent &&
                Stats.Mechanic != TowerMechanics.RollingThunder)
            {
                FireChain(target, enemies, combat);
                return true;
            }

            if (Stats.ProjectileSpeed > 0f ||
                Stats.Delivery == DeliveryKind.Projectile ||
                Stats.Mechanic == TowerMechanics.BallLightning ||
                Stats.Mechanic == TowerMechanics.LeakCurrent ||
                Stats.Mechanic == TowerMechanics.RollingThunder)
            {
                FireProjectileVolley(enemies, probe, combat);
                return true;
            }

            Fire(enemies, hitBuffer);
            return true;
        }

        private DamagePacket BuildPacket(float amount) => new DamagePacket
        {
            Amount = amount,
            Type = Stats.DamageType,
            Pierce = Stats.Pierce,
            Retention = Stats.Retention,
            BlockForce = Stats.BlockForce,
            Beam = Stats.Delivery == DeliveryKind.HitscanRect,
            Source = Id
        };

        private float HalfWidth() =>
            Stats.Delivery == DeliveryKind.HitscanRect ? Stats.BeamWidth * 0.5f : Stats.BulletRadius;

        // ---- 索敌 --------------------------------------------------------------

        private int SelectTarget(IReadOnlyList<EnemyRuntime> enemies, in DamagePacket probe)
        {
            if (Stats.MinRange <= 0f)
                return TargetSelector.SelectBest(enemies, Position, Stats.Range, probe, Stats.Strategy);

            _rangeBuffer.Clear();
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyRuntime enemy = enemies[i];
                if (!enemy.Alive) continue;
                if (Vec2.Distance(Position, enemy.Position) - enemy.Radius < Stats.MinRange) continue;
                _rangeBuffer.Add(enemy);
            }
            return TargetSelector.SelectBest(_rangeBuffer, Position, Stats.Range, probe, Stats.Strategy);
        }

        private int SelectTargetExcluding(
            IReadOnlyList<EnemyRuntime> enemies,
            in DamagePacket probe,
            List<int> excluded)
        {
            _salvoBuffer.Clear();
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyRuntime enemy = enemies[i];
                if (!enemy.Alive) continue;
                if (Vec2.Distance(Position, enemy.Position) - enemy.Radius < Stats.MinRange) continue;
                bool skip = false;
                for (int e = 0; e < excluded.Count; e++)
                {
                    if (excluded[e] == enemy.Id.Index) { skip = true; break; }
                }
                if (!skip) _salvoBuffer.Add(i);
            }

            if (_salvoBuffer.Count == 0) return -1;

            // 用下标子集包装成候选序列:直接按索引逐个比较权重代价高,
            // 这里复用 TargetSelector 的语义,临时按距离/权重扫描。
            int best = -1;
            float bestWeight = 0f;
            float bestHp = 0f;
            int bestKey = int.MaxValue;
            for (int k = 0; k < _salvoBuffer.Count; k++)
            {
                EnemyRuntime enemy = enemies[_salvoBuffer[k]];
                float center = Vec2.Distance(Position, enemy.Position);
                if (center - enemy.Radius > Stats.Range) continue;

                float coef = DamageCalculator.Multiplier(probe, enemy.Def);
                if (coef <= 0f) continue;

                float edge = Math.Max(1f, center - enemy.Radius);
                float weight = coef * enemy.MaxHp / edge;
                if (Stats.Strategy == TowerStrategy.Spray)
                {
                    float remaining = enemy.Hp - enemy.DamageSent;
                    if (remaining <= 0f) continue;
                    weight *= MathUtil.Clamp01(remaining / enemy.MaxHp);
                }

                bool better = best < 0 || weight > bestWeight + 1e-4f;
                if (!better && Math.Abs(weight - bestWeight) <= 1e-4f)
                {
                    better = enemy.Hp > bestHp + 1e-4f ||
                             (Math.Abs(enemy.Hp - bestHp) <= 1e-4f && enemy.Id.Index < bestKey);
                }
                if (!better) continue;

                best = _salvoBuffer[k];
                bestWeight = weight;
                bestHp = enemy.Hp;
                bestKey = enemy.Id.Index;
            }
            return best;
        }

        // ---- 弹丸 --------------------------------------------------------------

        private void FireProjectileVolley(
            IReadOnlyList<EnemyRuntime> enemies,
            in DamagePacket probe,
            CombatWorld combat)
        {
            int shots = Math.Max(1, Stats.Salvo);
            _excluded.Clear();
            int fired = 0;

            for (int s = 0; s < shots; s++)
            {
                int index = shots == 1
                    ? SelectTarget(enemies, probe)
                    : SelectTargetExcluding(enemies, probe, _excluded);
                if (index < 0) break;

                EnemyRuntime target = enemies[index];
                _excluded.Add(target.Id.Index);
                LaunchProjectile(target, combat);
                fired++;
            }

            if (fired == 0) return;

            if (shots > 1)
            {
                Cooldown = 0f;
                ReloadTimer = Stats.ReloadTime > 0f
                    ? Stats.ReloadTime
                    : 1f / Math.Max(0.0001f, Stats.FireRate);
                if (Stats.Ammo > 0) Ammo = 0;
            }
            else
            {
                Cooldown = 1f / Math.Max(0.0001f, Stats.FireRate);
                if (Stats.Ammo > 0)
                {
                    Ammo--;
                    if (Ammo <= 0)
                    {
                        Ammo = 0;
                        ReloadTimer = Stats.ReloadTime;
                    }
                }
            }

            LastHitCount = fired;
        }

        private void LaunchProjectile(EnemyRuntime target, CombatWorld combat)
        {
            float speed = Math.Max(1f, Stats.ProjectileSpeed);
            float flight = Vec2.Distance(Position, target.Position) / speed;
            Vec2 aim = target.Position + target.Velocity * flight;

            var spec = new ProjectileSpec
            {
                Speed = speed,
                ExplosionRadius = Stats.ExplosionRadius,
                Mechanic = Stats.Mechanic,
                SubCount = Stats.SubCount,
                SubDamage = Stats.SubDamage,
                SubRadius = Stats.SubRadius,
                SpreadRadius = Stats.SpreadRadius,
                FirstStageDamage = Stats.FirstStageDamage,
                SecondStageDamage = Stats.SecondStageDamage,
                SecondStageRadius = Stats.SecondStageRadius,
                ZoneScale = Stats.ZoneScale,
                Homing = Stats.HomingProjectile,
                ChainCount = Stats.ChainCount,
                ChainRange = Stats.ChainRange,
                ChainFalloff = Stats.ChainFalloff,
                ChargeStacks = Stats.ChargeStacks,
                AuraRadius = Stats.AuraRadius,
                AuraInterval = Stats.AuraInterval,
                Lifetime = Stats.ProjectileLifetime
            };

            // 球形闪电:爆炸半径随连锁次数增长。
            if (Stats.Mechanic == TowerMechanics.BallLightning)
            {
                spec.ExplosionRadius = Stats.ExplosionRadius + Stats.ChainCount * 0.5f;
            }

            // 漏电:每次周期电击继承全部连锁次数。
            if (Stats.Mechanic == TowerMechanics.LeakCurrent)
            {
                spec.SubDamage = 1f + Stats.ChainCount;
            }

            combat.Launch(Position, aim, target.Id, Id, BuildPacket(Stats.Damage), spec);
        }

        // ---- 连锁 --------------------------------------------------------------

        private void FireChain(
            EnemyRuntime target,
            IReadOnlyList<EnemyRuntime> enemies,
            CombatWorld combat)
        {
            ConsumeShot();
            Cooldown = 1f / Math.Max(0.0001f, Stats.FireRate);

            var packet = BuildPacket(Stats.Damage);

            if (Stats.Mechanic == TowerMechanics.Fission)
            {
                LastHitCount = combat.ApplyFission(
                    Position, target, packet, Stats.ChainCount, Stats.ChainRange);
                return;
            }

            int hits = combat.ApplyChain(
                Position, target, packet, Stats.ChainCount, Stats.ChainRange, Stats.ChainFalloff);
            LastHitCount = hits;

            bool capacitor = Stats.Mechanic == TowerMechanics.Capacitor ||
                             Stats.Mechanic == TowerMechanics.Rod;

            // 范围内没有第二个可跳目标 → 引爆,按剩余连锁次数提升单次伤害。
            if (!capacitor || hits > 1) return;

            float scale = 1f + Math.Max(0, Stats.ChainCount) * 0.20f;
            combat.ApplySingle(target, BuildPacket(Stats.Damage * scale));

            if (Stats.Mechanic == TowerMechanics.Rod)
            {
                int charges = Math.Clamp(Stats.ChainCount, 1, 3);
                combat.SpawnRod(target.Position, charges);
            }
        }

        // ---- 无人机库 ----------------------------------------------------------

        private void TickDroneCarrier(
            float dt,
            IReadOnlyList<EnemyRuntime> enemies,
            CombatWorld combat)
        {
            if (Reloading)
            {
                ReloadTimer -= dt;
                if (ReloadTimer <= 0f)
                {
                    ReloadTimer = 0f;
                    DroneReserve = Stats.DroneCapacity;
                }
                return;
            }

            if (DeployedDrones > 0 && DeployedDrones * 2 <= Stats.DroneSquad)
            {
                // 半数以下损耗:回收计数,准备下一队。
                DroneReserve += DeployedDrones;
                DeployedDrones = 0;
                DeployTimer = Stats.DroneDeployInterval;
            }

            if (DroneReserve < Stats.DroneSquad)
            {
                if (DeployedDrones == 0 && DroneReserve > 0)
                {
                    DeployTimer -= dt;
                    if (DeployTimer <= 0f)
                    {
                        DeployedDrones = DroneReserve;
                        DroneReserve = 0;
                        SquadsDeployed++;
                        DeployTimer = Stats.DroneDeployInterval;
                        combat.DeployDrones(Id.Index, Position, Stats, DeployedDrones);
                    }
                }
                return;
            }

            if (DeployedDrones > 0) return;

            DeployTimer -= dt;
            if (DeployTimer > 0f) return;

            DeployedDrones = Stats.DroneSquad;
            DroneReserve -= Stats.DroneSquad;
            SquadsDeployed++;
            DeployTimer = Stats.DroneDeployInterval;
            combat.DeployDrones(Id.Index, Position, Stats, DeployedDrones);
        }

        /// <summary>编队自爆 / 耗尽后由无人机系统回调。</summary>
        public void OnSquadConsumed(int drones)
        {
            DeployedDrones -= drones;
            if (DeployedDrones < 0) DeployedDrones = 0;
            if (DroneReserve <= 0 && DeployedDrones <= 0)
            {
                ReloadTimer = Stats.DroneReload;
                DroneReserve = Stats.DroneCapacity;
            }
        }

        private void ConsumeShot()
        {
            if (Stats.Ammo <= 0) return;
            Ammo--;
            if (Ammo <= 0)
            {
                Ammo = 0;
                ReloadTimer = Stats.ReloadTime;
            }
        }

        // ---- hitscan -----------------------------------------------------------

        /// <summary>多束激光的相邻束夹角(扫荡透镜的扇形近似)。</summary>
        private const float BeamSpreadRad = 0.1047f;

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

            int beams = Math.Max(1, Stats.BeamCount);
            float baseAngle = Motor.AimAngle;
            int totalHits = 0;

            for (int b = 0; b < beams; b++)
            {
                float offset = beams == 1 ? 0f : (b - (beams - 1) * 0.5f) * BeamSpreadRad;
                float angle = baseAngle + offset;
                var direction = new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle));
                HitscanResolver.Query(Position, direction, Stats.Range, HalfWidth(), enemies, hitBuffer);
                totalHits += ApplyBeam(hitBuffer);
            }

            LastHitCount = totalHits;
        }

        private int ApplyBeam(List<EnemyRuntime> hitBuffer)
        {
            var packet = BuildPacket(Stats.Damage);

            float retention = Stats.Retention;
            int hits = 0;
            for (int i = 0; i < hitBuffer.Count; i++)
            {
                EnemyRuntime enemy = hitBuffer[i];
                if (!enemy.Alive) continue;

                float amount = DamageCalculator.Amount(packet, enemy.Def, enemy.BuildDamageContext());
                enemy.ApplyDamage(amount, Stats.BlockForce);
                hits++;

                retention = PenetrationResolver.Consume(retention, packet, enemy.Def);
                if (!PenetrationResolver.CanContinue(retention)) break;
            }

            return hits;
        }
    }
}
