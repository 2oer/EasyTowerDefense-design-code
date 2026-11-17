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

    /// <summary>一次 hitscan 射击的弹道描述,只给表现层画曳光 / 光束,不参与结算。</summary>
    public readonly struct ShotTrace
    {
        public readonly Vec2 Origin;
        public readonly Vec2 Direction;
        public readonly float Range;
        public readonly float HalfWidth;

        /// <summary>true = 矩形光束(激光),false = 直线弹道(机枪)。</summary>
        public readonly bool Rectangle;

        public ShotTrace(Vec2 origin, Vec2 direction, float range, float halfWidth, bool rectangle)
        {
            Origin = origin;
            Direction = direction;
            Range = range;
            HalfWidth = halfWidth;
            Rectangle = rectangle;
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

        /// <summary>本逻辑帧内的 hitscan 弹道(每束一条),供表现层绘制。</summary>
        private readonly List<ShotTrace> _shotTraces = new List<ShotTrace>();
        public IReadOnlyList<ShotTrace> ShotTraces => _shotTraces;

        // ---- 持续照射(聚焦 / 扫荡透镜) ----------------------------------------
        private float _beamSecondsLeft;
        private float _beamTickTimer;
        private float _beamRestTimer;
        private float _beamAngle;
        private int _beamTargetId = -1;

        private readonly List<IAoETarget> _aoeTargets = new List<IAoETarget>();
        private readonly List<IAoETarget> _aoeHitBuffer = new List<IAoETarget>();

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

        /// <summary>
        /// 进入布防阶段时重置:补满弹匣 / 机库库存,清空冷却与出击队列。
        /// 移动工厂回到停机坪、无人机库收回全部编队后重新待命。
        /// </summary>
        public void ResetForDeployment()
        {
            Ammo = Stats.Ammo;
            Cooldown = 0f;
            ReloadTimer = 0f;
            LastHitCount = 0;

            DroneReserve = Stats.DroneCapacity;
            DeployedDrones = 0;
            DeployTimer = 0f;
            SquadsDeployed = 0;

            _beamSecondsLeft = 0f;
            _beamTickTimer = 0f;
            _beamRestTimer = 0f;
            _beamAngle = 0f;
            _beamTargetId = -1;
        }

        /// <summary>返回本帧是否开火 / 派出编队。</summary>
        public bool Tick(
            float dt,
            IReadOnlyList<EnemyRuntime> enemies,
            List<EnemyRuntime> hitBuffer,
            CombatWorld combat)
        {
            _shotTraces.Clear();

            if (Stats.Continuous)
            {
                TickContinuous(dt, enemies, hitBuffer);
                return _shotTraces.Count > 0;
            }

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
                PreAim(enemies, dt);
                LastHitCount = 0;
                return false;
            }

            EnemyRuntime target = enemies[targetIndex];
            Vec2 toTarget = target.Position - Position;
            float desired = (float)Math.Atan2(toTarget.Y, toTarget.X);

            if (Stats.NoTurn || Stats.Mechanic == TowerMechanics.MissileSilo)
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
            Type = Stats.Mechanic == TowerMechanics.EnrichedPlutonium
                ? DamageType.Radiation
                : Stats.DamageType,
            Pierce = Stats.Pierce,
            Retention = Stats.Retention,
            BlockForce = Stats.BlockForce,
            Beam = Stats.Delivery == DeliveryKind.HitscanRect,
            Source = Id
        };

        private float HalfWidth() =>
            Stats.Delivery == DeliveryKind.HitscanRect ? Stats.BeamWidth * 0.5f : Stats.BulletRadius;

        /// <summary>
        /// 侦察范围 = 射程 ×125%:没有可开火目标时,先朝最近的可侦察目标预转向,进入射程即可开火。
        /// </summary>
        private void PreAim(IReadOnlyList<EnemyRuntime> enemies, float dt)
        {
            if (Stats.NoTurn) return;

            float scoutRange = Stats.Range * 1.25f;
            EnemyRuntime? nearest = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyRuntime enemy = enemies[i];
                if (!enemy.Alive) continue;
                float distance = Vec2.Distance(Position, enemy.Position) - enemy.Radius;
                if (distance > scoutRange || distance < Stats.MinRange) continue;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                nearest = enemy;
            }

            if (nearest == null) return;

            Vec2 toTarget = nearest.Position - Position;
            float desired = (float)Math.Atan2(toTarget.Y, toTarget.X);
            float faceDistance = Math.Max(1f, toTarget.Length - nearest.Radius);
            float tolerance = Math.Max(
                TurretMotor.PointBlankToleranceRad,
                (float)Math.Atan2(HalfWidth(), faceDistance));
            Motor.TurnTowards(desired, Stats.TurnSpeed, tolerance, dt);
        }

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
                if (enemy.Hp - enemy.DamageSent <= 0f) continue;

                float edge = Math.Max(1f, center - enemy.Radius);
                float weight = coef * enemy.MaxHp / edge;
                if (Stats.Strategy == TowerStrategy.Spray)
                {
                    float remaining = enemy.Hp - enemy.DamageSent;
                    if (remaining <= 0f) continue;
                    weight *= MathUtil.Clamp01(remaining / enemy.MaxHp);
                }
                else if (Stats.Strategy == TowerStrategy.Strategic)
                {
                    weight = enemy.Hp;
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
            float step = BeamStep(beams);
            int totalHits = 0;

            for (int b = 0; b < beams; b++)
            {
                float offset = beams == 1 ? 0f : (b - (beams - 1) * 0.5f) * step;
                float angle = baseAngle + offset;
                var direction = new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle));
                HitscanResolver.Query(Position, direction, Stats.Range, HalfWidth(), enemies, hitBuffer);
                totalHits += ApplyBeam(hitBuffer, Stats.Damage, 0f, enemies);
                _shotTraces.Add(new ShotTrace(
                    Position, direction, Stats.Range, HalfWidth(),
                    Stats.Delivery == DeliveryKind.HitscanRect));
            }

            if (Stats.Mechanic == TowerMechanics.EnrichedPlutonium) ApplyDecayInRange(enemies);
            LastHitCount = totalHits;
        }

        // ---- 持续照射(聚焦 / 扫荡透镜) ----------------------------------------

        /// <summary>
        /// 不可中断的持续照射:锁定方向后按固定间隔结算,直到走完整个照射时长;
        /// 期间即使目标死亡或离开,也会继续把剩余时间打完。
        /// </summary>
        private void TickContinuous(float dt, IReadOnlyList<EnemyRuntime> enemies, List<EnemyRuntime> hitBuffer)
        {
            if (_beamRestTimer > 0f)
            {
                _beamRestTimer -= dt;
                if (_beamRestTimer < 0f) _beamRestTimer = 0f;
                return;
            }

            if (_beamSecondsLeft > 0f)
            {
                _beamSecondsLeft -= dt;
                _beamTickTimer -= dt;

                if (_beamTickTimer <= 0f)
                {
                    _beamTickTimer += Math.Max(0.001f, Stats.ContinuousInterval);
                    UpdateBeamAim(enemies);
                    EmitBeamTick(enemies, hitBuffer);
                }

                if (_beamSecondsLeft <= 0f)
                {
                    _beamSecondsLeft = 0f;
                    _beamRestTimer = Math.Max(0f, Stats.ContinuousRest);
                    ResetBeamHeat(enemies);
                    _beamTargetId = -1;
                }
                return;
            }

            var probe = BuildPacket(Stats.Damage);
            int index = SelectTarget(enemies, probe);
            if (index < 0)
            {
                PreAim(enemies, dt);
                return;
            }

            EnemyRuntime target = enemies[index];
            Vec2 toTarget = target.Position - Position;
            float desired = (float)Math.Atan2(toTarget.Y, toTarget.X);
            float faceDistance = Math.Max(1f, toTarget.Length - target.Radius);
            float tolerance = Math.Max(
                TurretMotor.PointBlankToleranceRad,
                (float)Math.Atan2(HalfWidth(), faceDistance));

            if (!Motor.TurnTowards(desired, Stats.TurnSpeed, tolerance, dt)) return;
            Motor.SnapTo(desired);

            _beamTargetId = target.Id.Index;
            _beamAngle = desired;
            _beamSecondsLeft = Math.Max(0.05f, Stats.ContinuousDuration);
            _beamTickTimer = 0f;
            UpdateBeamAim(enemies);
            EmitBeamTick(enemies, hitBuffer);
        }

        /// <summary>
        /// 照射期间保持射击不中断,但允许光束跟随当前目标(目标死亡后自动改瞄下一位);
        /// 没有任何目标时维持最后方向,把剩余照射时间打完。
        /// </summary>
        private void UpdateBeamAim(IReadOnlyList<EnemyRuntime> enemies)
        {
            EnemyRuntime? target = FindEnemyById(enemies, _beamTargetId);
            if (target == null || !target.Alive || !InRange(target))
            {
                int next = SelectTarget(enemies, BuildPacket(Stats.Damage));
                if (next >= 0)
                {
                    if (_beamTargetId >= 0 && target != null) target.Status.ResetHeatMelt();
                    target = enemies[next];
                    _beamTargetId = target.Id.Index;
                }
                else
                {
                    _beamTargetId = -1;
                    target = null;
                }
            }

            if (target == null) return;
            Vec2 toTarget = target.Position - Position;
            _beamAngle = (float)Math.Atan2(toTarget.Y, toTarget.X);
            Motor.SnapTo(_beamAngle);
        }

        private EnemyRuntime? FindEnemyById(IReadOnlyList<EnemyRuntime> enemies, int id)
        {
            if (id < 0) return null;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].Id.Index == id) return enemies[i];
            }
            return null;
        }

        private bool InRange(EnemyRuntime enemy)
        {
            if (Vec2.Distance(Position, enemy.Position) - enemy.Radius > Stats.Range) return false;
            if (Stats.MinRange > 0f &&
                Vec2.Distance(Position, enemy.Position) - enemy.Radius < Stats.MinRange) return false;
            return true;
        }

        private void EmitBeamTick(IReadOnlyList<EnemyRuntime> enemies, List<EnemyRuntime> hitBuffer)
        {
            int beams = Math.Max(1, Stats.BeamCount);
            float step = BeamStep(beams);
            float amount = Stats.Damage * Math.Max(0.01f, Stats.ContinuousDamageScale);
            float heat = Stats.Mechanic == TowerMechanics.ThermalMelt
                ? Math.Max(0.001f, Stats.ContinuousInterval)
                : 0f;

            int totalHits = 0;
            for (int b = 0; b < beams; b++)
            {
                float offset = beams == 1 ? 0f : (b - (beams - 1) * 0.5f) * step;
                float angle = _beamAngle + offset;
                var direction = new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle));
                HitscanResolver.Query(Position, direction, Stats.Range, HalfWidth(), enemies, hitBuffer);
                totalHits += ApplyBeam(hitBuffer, amount, heat, enemies);
                _shotTraces.Add(new ShotTrace(
                    Position, direction, Stats.Range, HalfWidth(), true));
            }

            if (Stats.Mechanic == TowerMechanics.EnrichedPlutonium) ApplyDecayInRange(enemies);
            LastHitCount = totalHits;
        }

        /// <summary>散射棱镜把多条束摊成 60° 扇形;其余情况维持原来的窄夹角。</summary>
        private float BeamStep(int beams)
        {
            if (Stats.Mechanic == TowerMechanics.ScatterPrism && beams > 1)
            {
                return (float)(Math.PI / 3.0) / (beams - 1);
            }
            return BeamSpreadRad;
        }

        private void ResetBeamHeat(IReadOnlyList<EnemyRuntime> enemies)
        {
            if (_beamTargetId < 0) return;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].Id.Index != _beamTargetId) continue;
                enemies[i].Status.ResetHeatMelt();
                return;
            }
        }

        private int ApplyBeam(
            List<EnemyRuntime> hitBuffer,
            float amount,
            float heatSeconds,
            IReadOnlyList<EnemyRuntime> enemies)
        {
            var packet = BuildPacket(amount);

            float retention = Stats.Retention;
            int hits = 0;
            for (int i = 0; i < hitBuffer.Count; i++)
            {
                EnemyRuntime enemy = hitBuffer[i];
                if (!enemy.Alive) continue;

                // 进入该敌人瞬间尚未被其自身衰减消耗的留存,供贯穿内爆计算二段。
                float remaining = Math.Max(0f, retention - enemy.Def.Decay(packet.Type));
                float dealt = DamageCalculator.Amount(packet, enemy.Def, enemy.BuildDamageContext());
                enemy.ApplyDamage(dealt, Stats.BlockForce);
                hits++;

                if (heatSeconds > 0f) enemy.Status.AddHeatMelt(heatSeconds);
                ApplyOnHitMechanic(enemy, dealt, remaining, enemies);

                if (Stats.Mechanic == TowerMechanics.Implosion && enemy.Def.Size == EnemySize.Large)
                {
                    break;
                }

                // 热熔:能量衰减不再阻碍贯穿。
                if (Stats.Mechanic != TowerMechanics.ThermalMelt)
                {
                    retention = PenetrationResolver.Consume(retention, packet, enemy.Def);
                    if (!PenetrationResolver.CanContinue(retention)) break;
                }
            }

            return hits;
        }

        /// <summary>机枪 / 激光唯一升级的附加机制。</summary>
        private void ApplyOnHitMechanic(
            EnemyRuntime enemy, float dealt, float remainingRetention, IReadOnlyList<EnemyRuntime> enemies)
        {
            switch (Stats.Mechanic)
            {
                case TowerMechanics.Incendiary:
                    ApplyExtraElemental(enemy, 0.25f);
                    enemy.Status.ApplyBurn(1, Stats.Pierce);
                    break;

                case TowerMechanics.CryoCore:
                    ApplyExtraElemental(enemy, 0.25f);
                    enemy.Status.ApplyChill(1);
                    break;

                case TowerMechanics.HighExplosive:
                    ApplyExplosion(enemy, enemies);
                    break;

                case TowerMechanics.Implosion:
                    if (enemy.Def.Size != EnemySize.Large) break;

                    // 第二段:第一段 ×350% × 留存增伤(10%~250%),不经过护甲与衰减。
                    float bonus = MathUtil.Clamp(remainingRetention * 0.25f, 0.10f, 2.50f);
                    enemy.ApplyDamage(Stats.Damage * 3.5f * bonus, 0f);
                    enemy.Status.ApplyArmorBreak();
                    break;
            }
        }

        private void ApplyExtraElemental(EnemyRuntime enemy, float fraction)
        {
            var packet = new DamagePacket
            {
                Amount = Stats.Damage * fraction,
                Type = DamageType.Elemental,
                Pierce = Stats.Pierce,
                BlockForce = 0f,
                Source = Id
            };
            float amount = DamageCalculator.Amount(packet, enemy.Def, enemy.BuildDamageContext());
            enemy.ApplyDamage(amount, 0f);
        }

        private void ApplyExplosion(EnemyRuntime enemy, IReadOnlyList<EnemyRuntime> enemies)
        {
            _aoeTargets.Clear();
            for (int i = 0; i < enemies.Count; i++) _aoeTargets.Add(enemies[i]);

            var packet = new DamagePacket
            {
                Amount = Stats.Damage * 0.5f,
                Type = DamageType.Explosive,
                Pierce = Stats.Pierce,
                BlockForce = Stats.BlockForce,
                Source = Id
            };
            ExplosionResolver.Apply(
                packet, enemy.Position, Math.Max(1f, Stats.ExplosionRadius), _aoeTargets, _aoeHitBuffer);
        }

        private void ApplyDecayInRange(IReadOnlyList<EnemyRuntime> enemies)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyRuntime enemy = enemies[i];
                if (!enemy.Alive) continue;
                if (Vec2.Distance(Position, enemy.Position) - enemy.Radius > Stats.Range) continue;
                enemy.Status.ApplyDecay(1);
            }
        }
    }
}
