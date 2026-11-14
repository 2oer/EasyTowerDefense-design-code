using System;
using System.Collections.Generic;
using Game.Core;
using Game.Model;

namespace Game.Combat
{
    /// <summary>一个编队的运行时。攻击按“整队齐射”结算,只保留对行为事件有意义的状态。</summary>
    public sealed class DroneSquad
    {
        public int TowerIndex;
        public TowerRuntimeStats Stats = null!;
        public int Alive;
        public Vec2 Position;
        public Vec2 Anchor;
        public float Ammo;
        public float Rockets;
        public float Missiles;
        public float FireTimer;
        public float SpecialTimer;
        public float Life;
        public bool Returning;
        public float ReturnTimer;
        public float BombTimer;
        public int BombsLeft;
        public bool Expired;

        // 移动工厂 / 巡洋舰:被测本体位置由 DroneSystem 维护。
        public Vec2 CarrierPosition;
        public float RefitTimer;
        public float PositronTimer;
        public int PositronAmmo;
    }

    /// <summary>
    /// 无人机系统。负责编队部署、雷达引导、原地开火、弹药耗尽自爆 / 返航。
    /// 设计口径:编队统一飞行,雷达只引导接近,进入自身射程后停下攻击(不追击)。
    /// </summary>
    public sealed class DroneSystem
    {
        /// <summary>编队被消耗时回调(towerIndex, 损失架数)。</summary>
        public Action<int, int>? SquadConsumed;

        private readonly List<DroneSquad> _squads = new List<DroneSquad>();
        private readonly List<IAoETarget> _hitBuffer = new List<IAoETarget>();

        public IReadOnlyList<DroneSquad> Squads => _squads;

        public void Clear()
        {
            _squads.Clear();
        }

        public void Deploy(int towerIndex, Vec2 position, TowerRuntimeStats stats, int count)
        {
            int alive = count > 0 ? count : stats.DroneSquad;
            if (alive <= 0) return;

            bool bastion = IsBastion(stats.Mechanic);
            var squad = new DroneSquad
            {
                TowerIndex = towerIndex,
                Stats = stats,
                Alive = alive,
                Position = position,
                Anchor = position,
                CarrierPosition = position,
                Ammo = bastion ? stats.DroneAmmo : stats.DroneAmmo * alive,
                Rockets = IsBastion(stats.Mechanic) ? 8f : 0f,
                Missiles = stats.Mechanic == TowerMechanics.RefinedHangar ? alive * 2f : 0f,
                FireTimer = 0f,
                Life = 0f,
                ReturnTimer = 0f,
                BombTimer = 0f,
                BombsLeft = stats.Mechanic == TowerMechanics.Thunderstorm ? 18 : 0,
                PositronAmmo = stats.Mechanic == TowerMechanics.Battleship ? 8 : 0
            };
            _squads.Add(squad);
        }

        private static bool IsBastion(string mechanic) =>
            string.Equals(mechanic, TowerMechanics.Hurricane, StringComparison.Ordinal) ||
            string.Equals(mechanic, TowerMechanics.Aurora, StringComparison.Ordinal);

        public void Tick(float dt, IReadOnlyList<IAoETarget> targets)
        {
            for (int i = _squads.Count - 1; i >= 0; i--)
            {
                DroneSquad squad = _squads[i];
                squad.Life += dt;

                if (squad.Returning)
                {
                    squad.ReturnTimer -= dt;
                    if (squad.ReturnTimer <= 0f) Consume(i, squad.Alive);
                    continue;
                }

                TickCarrier(squad, dt, targets);
                TickSquad(squad, dt, targets);
            }
        }

        private void Consume(int index, int losses)
        {
            DroneSquad squad = _squads[index];
            if (squad.Expired) return;
            squad.Expired = true;
            _squads.RemoveAt(index);
            SquadConsumed?.Invoke(squad.TowerIndex, losses);
        }

        // ---- 本体(移动工厂 / 巡洋舰) -----------------------------------------

        private static void TickCarrier(DroneSquad squad, float dt, IReadOnlyList<IAoETarget> targets)
        {
            if (squad.Stats.Mechanic != TowerMechanics.MobileFactory &&
                squad.Stats.Mechanic != TowerMechanics.Battleship) return;

            squad.RefitTimer -= dt;
            if (squad.RefitTimer > 0f) return;
            squad.RefitTimer = 1.5f;

            // 自动拟合:向雷达范围内敌人重心靠拢,但仍限制在活动半径内。
            float activity = squad.Stats.RadarRange * 2f;
            Vec2 centroid = Vec2.Zero;
            int count = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                IAoETarget t = targets[i];
                if (!t.Alive) continue;
                if (Vec2.Distance(squad.CarrierPosition, t.Position) > activity) continue;
                centroid += t.Position;
                count++;
            }
            if (count == 0) return;

            centroid /= count;
            Vec2 offset = centroid - squad.Anchor;
            if (offset.Length > activity) offset = offset.Normalized * activity;
            Vec2 desired = squad.Anchor + offset;

            // 仅当新点显著更好(20% 以上)才移动,抑制抖动。
            if (Vec2.Distance(desired, squad.CarrierPosition) < squad.Stats.DroneSpeed * 0.2f) return;

            Vec2 delta = desired - squad.CarrierPosition;
            float distance = delta.Length;
            float step = squad.Stats.DroneSpeed * dt;
            squad.CarrierPosition = distance <= step
                ? desired
                : squad.CarrierPosition + delta / distance * step;
        }

        // ---- 编队行为 ----------------------------------------------------------

        private void TickSquad(DroneSquad squad, float dt, IReadOnlyList<IAoETarget> targets)
        {
            TowerRuntimeStats stats = squad.Stats;

            if (stats.Mechanic == TowerMechanics.Thunderstorm)
            {
                TickBomber(squad, dt, targets);
                return;
            }

            if (stats.Mechanic == TowerMechanics.Battleship)
            {
                TickPositron(squad, targets, dt);
            }

            IAoETarget? inRange = FindNearest(targets, squad.Position, stats.DroneRange);
            if (inRange != null)
            {
                SquadAttack(squad, inRange, targets, dt);
                return;
            }

            // 远程信号:只有雷达覆盖内才移动;否则原地待命。
            IAoETarget? signal = FindNearest(targets, squad.CarrierPosition, stats.RadarRange);
            if (signal == null) return;

            Vec2 delta = signal.Position - squad.Position;
            float distance = delta.Length;
            if (distance <= 0.01f) return;
            float step = stats.DroneSpeed * dt;
            squad.Position = distance <= step ? signal.Position : squad.Position + delta / distance * step;
        }

        private void SquadAttack(DroneSquad squad, IAoETarget target, IReadOnlyList<IAoETarget> targets, float dt)
        {
            TowerRuntimeStats stats = squad.Stats;
            squad.FireTimer -= dt;

            if (stats.Mechanic == TowerMechanics.Hurricane || stats.Mechanic == TowerMechanics.Aurora)
            {
                AttackBastion(squad, target, targets, stats);
                return;
            }

            if (squad.FireTimer > 0f) return;
            float rate = Math.Max(0.1f, stats.DroneFireRate);
            squad.FireTimer = 1f / rate;

            // 精装修机库:优先打空导弹,再回到机枪。
            if (squad.Missiles > 0f && (target.Def.Size != EnemySize.Small || squad.Ammo <= 0f))
            {
                squad.Missiles -= 1f;
                var missile = new DamagePacket
                {
                    Amount = 45f,
                    Type = DamageType.Explosive,
                    Pierce = ArmorPierce.Mid,
                    BlockForce = stats.BlockForce * 0.5f
                };
                ExplosionResolver.Apply(missile, target.Position, 3f, targets, _hitBuffer);
            }

            if (squad.Ammo <= 0f)
            {
                SelfDestruct(squad, targets);
                return;
            }

            var packet = new DamagePacket
            {
                Amount = stats.DroneDamage * squad.Alive,
                Type = stats.DamageType,
                Pierce = stats.Pierce,
                BlockForce = stats.BlockForce,
                Source = new EntityId(squad.TowerIndex, 0)
            };
            ApplyTo(target, packet);
            squad.Ammo -= 1f;
            if (squad.Ammo <= 0f) SelfDestruct(squad, targets);
        }

        private void AttackBastion(
            DroneSquad squad, IAoETarget target, IReadOnlyList<IAoETarget> targets, TowerRuntimeStats stats)
        {
            if (squad.FireTimer > 0f) return;
            float rate = Math.Max(0.1f, stats.DroneFireRate);
            squad.FireTimer = 1f / rate;

            bool laser = stats.Mechanic == TowerMechanics.Aurora;
            bool rocketsFirst = !laser && (target.Def.Size != EnemySize.Small || squad.Ammo <= 0f);

            if (rocketsFirst && squad.Rockets > 0f)
            {
                squad.Rockets -= 1f;
                var rocket = new DamagePacket
                {
                    Amount = 100f,
                    Type = DamageType.Explosive,
                    Pierce = ArmorPierce.Mid,
                    BlockForce = stats.BlockForce
                };
                ExplosionResolver.Apply(rocket, target.Position, 4f, targets, _hitBuffer);
                return;
            }

            if (squad.Ammo <= 0f)
            {
                BeginReturn(squad);
                return;
            }

            var packet = new DamagePacket
            {
                Amount = laser ? stats.DroneDamage * 0.75f * 2f : stats.DroneDamage * 2f,
                Type = laser ? DamageType.Energy : DamageType.Ballistic,
                Pierce = stats.Pierce,
                Retention = laser ? stats.Retention : 0f,
                BlockForce = stats.BlockForce,
                Beam = laser,
                Source = new EntityId(squad.TowerIndex, 0)
            };
            ApplyTo(target, packet);
            squad.Ammo -= laser ? 1f : 2f;
            if (squad.Ammo <= 0f) BeginReturn(squad);
        }

        private static void BeginReturn(DroneSquad squad)
        {
            squad.Returning = true;
            squad.ReturnTimer = squad.Stats.DroneReload > 0f ? squad.Stats.DroneReload : 12f;
        }

        private void SelfDestruct(DroneSquad squad, IReadOnlyList<IAoETarget> targets)
        {
            var packet = new DamagePacket
            {
                Amount = squad.Stats.DroneSelfDestructDamage * squad.Alive,
                Type = DamageType.Explosive,
                Pierce = ArmorPierce.Mid,
                BlockForce = squad.Stats.BlockForce
            };
            ExplosionResolver.Apply(
                packet, squad.Position, squad.Stats.DroneSelfDestructRadius, targets, _hitBuffer);

            squad.Expired = true;
            int index = _squads.IndexOf(squad);
            if (index >= 0) _squads.RemoveAt(index);
            SquadConsumed?.Invoke(squad.TowerIndex, squad.Alive);
        }

        // ---- 大舰巨炮:阳电子炮 -------------------------------------------------

        /// <summary>
        /// 阳电子炮:每 4 s 对本体 30 距离内的最近目标打一发高穿甲能量束,
        /// 共 8 发,只有在移动工厂返回停机坪整备时才补满。
        /// </summary>
        private void TickPositron(DroneSquad squad, IReadOnlyList<IAoETarget> targets, float dt)
        {
            if (squad.PositronAmmo <= 0) return;

            squad.PositronTimer -= dt;
            if (squad.PositronTimer > 0f) return;

            IAoETarget? victim = FindNearest(targets, squad.CarrierPosition, 30f);
            if (victim == null) return;

            squad.PositronTimer = 4f;
            squad.PositronAmmo--;

            var packet = new DamagePacket
            {
                Amount = 1120f,
                Type = DamageType.Energy,
                Pierce = ArmorPierce.High,
                BlockForce = 10f,
                Beam = true,
                Source = new EntityId(squad.TowerIndex, 0)
            };
            ApplyTo(victim, packet);
        }

        // ---- 雷暴:矩形覆盖 + 按距离投弹 ---------------------------------------

        private void TickBomber(DroneSquad squad, float dt, IReadOnlyList<IAoETarget> targets)
        {
            TowerRuntimeStats stats = squad.Stats;
            if (squad.BombsLeft <= 0)
            {
                if (!squad.Returning) BeginBomberReturn(squad);
                return;
            }

            // 沿雷达指示的矩形带飞行:前向 1.0R、后向 0.2R、单侧 0.55R。
            IAoETarget? signal = FindNearest(targets, squad.CarrierPosition, stats.RadarRange);
            if (signal != null)
            {
                Vec2 delta = signal.Position - squad.Position;
                float distance = delta.Length;
                if (distance > 0.01f)
                {
                    float step = stats.DroneSpeed * dt;
                    squad.Position = distance <= step
                        ? signal.Position
                        : squad.Position + delta / distance * step;
                }
            }

            squad.BombTimer -= dt;
            if (squad.BombTimer > 0f) return;

            float baseSpeed = 12f;
            float interval = MathUtil.Clamp(0.5f * baseSpeed / Math.Max(1f, stats.DroneSpeed), 0.15f, 0.55f);
            squad.BombTimer += interval;

            IAoETarget? victim = FindNearest(targets, squad.Position, stats.RadarRange);
            if (victim == null) return;

            bool heavy = squad.BombsLeft <= 2;
            squad.BombsLeft--;
            var bomb = new DamagePacket
            {
                Amount = heavy ? 400f : 150f,
                Type = DamageType.Explosive,
                Pierce = heavy ? ArmorPierce.High : ArmorPierce.Mid,
                BlockForce = stats.BlockForce
            };
            ExplosionResolver.Apply(bomb, victim.Position, heavy ? 2f : 4f, targets, _hitBuffer);
        }

        private static void BeginBomberReturn(DroneSquad squad)
        {
            squad.Returning = true;
            squad.ReturnTimer = 10f;
        }

        // ---- 工具 --------------------------------------------------------------

        private static void ApplyTo(IAoETarget target, in DamagePacket packet)
        {
            float amount = DamageCalculator.Amount(packet, target.Def, target.BuildDamageContext());
            target.ApplyDamage(amount, packet.BlockForce);
        }

        private static IAoETarget? FindNearest(
            IReadOnlyList<IAoETarget> targets, Vec2 origin, float range)
        {
            IAoETarget? best = null;
            float bestDistance = range;
            for (int i = 0; i < targets.Count; i++)
            {
                IAoETarget t = targets[i];
                if (!t.Alive) continue;
                float distance = Vec2.Distance(origin, t.Position) - t.Radius;
                if (distance > bestDistance) continue;
                if (best != null && Math.Abs(distance - bestDistance) < 1e-4f &&
                    t.Id.Index > best.Id.Index) continue;
                best = t;
                bestDistance = distance;
            }
            return best;
        }
    }
}
