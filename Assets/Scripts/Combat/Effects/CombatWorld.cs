using System;
using System.Collections.Generic;
using Game.Core;
using Game.Model;

namespace Game.Combat
{
    /// <summary>发射时从塔属性快照下来的弹丸参数。</summary>
    public struct ProjectileSpec
    {
        public float Speed;
        public float ExplosionRadius;
        public string Mechanic;
        public int SubCount;
        public float SubDamage;
        public float SubRadius;
        public float SpreadRadius;
        public float FirstStageDamage;
        public float SecondStageDamage;
        public float SecondStageRadius;
        public float ZoneScale;
        public bool Homing;

        // ---- 电弧 / 电浆 -------------------------------------------------------
        public int ChainCount;
        public float ChainRange;
        public float ChainFalloff;
        public int ChargeStacks;

        /// <summary>漏电:飞行途中对周围敌人周期放电的半径 / 间隔;0 表示不启用。</summary>
        public float AuraRadius;
        public float AuraInterval;

        /// <summary>投掷物最长寿命(漏电 / 雷电滚滚用);0 表示不限制。</summary>
        public float Lifetime;
    }

    public sealed class ProjectileRuntime
    {
        public EntityId Source;
        public EntityId TargetId;
        public Vec2 Start;
        public Vec2 Position;
        public Vec2 TargetPosition;
        public bool Homing;
        public float Speed;
        public float Elapsed;
        public float FlightTime;
        public float Age;
        public float AuraTimer;
        public DamagePacket Packet;
        public ProjectileSpec Spec;
    }

    /// <summary>
    /// 战斗运行时容器:投射物、地面区域、避雷针与范围命中缓冲。
    /// Entity 层把该对象传给 TowerAgent,塔只描述“要发射什么”,由这里推进与结算。
    /// </summary>
    public sealed class CombatWorld
    {
        private readonly StatusRules _rules;

        public CombatWorld(StatusRules rules)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        /// <summary>每帧由 BattleSimulation 同步的存活敌人视图。</summary>
        public List<IAoETarget> Targets { get; } = new List<IAoETarget>();

        public List<ProjectileRuntime> Projectiles { get; } = new List<ProjectileRuntime>();
        public AreaEffectSystem Areas { get; } = new AreaEffectSystem();
        public LightningRodSystem Rods { get; } = new LightningRodSystem();
        public DroneSystem Drones { get; } = new DroneSystem();

        private readonly List<IAoETarget> _hitBuffer = new List<IAoETarget>();
        private readonly List<IAoETarget> _chargeBuffer = new List<IAoETarget>();

        /// <summary>投射物落点事件(供模拟器打印 / 表现层挂特效)。</summary>
        public Action<ProjectileRuntime>? Impacted;

        /// <summary>编队消耗回调(towerIndex, 损失架数),由 BattleSimulation 接到塔上。</summary>
        public Action<int, int>? SquadConsumed
        {
            get => Drones.SquadConsumed;
            set => Drones.SquadConsumed = value;
        }

        /// <summary>只清理场上临时物(投射物 / 区域 / 避雷针 / 编队),保留敌人视图。</summary>
        public void ClearTransient()
        {
            Projectiles.Clear();
            Areas.Clear();
            Rods.Clear();
            Drones.Clear();
        }

        public void Clear()
        {
            Targets.Clear();
            Projectiles.Clear();
            Areas.Clear();
            Rods.Clear();
            Drones.Clear();
        }

        /// <summary>连锁闪电。避雷针优先:在连锁范围内存在避雷针时先触发它,并额外 +1 次连锁。</summary>
        public int ApplyChain(
            Vec2 origin,
            IAoETarget first,
            in DamagePacket packet,
            int extraJumps,
            float range,
            float falloff)
        {
            int jumps = extraJumps;

            LightningRod? rod = Rods.FindForChain(origin, range);
            if (rod != null)
            {
                var rodPacket = new DamagePacket
                {
                    Amount = 30f,
                    Type = DamageType.Elemental,
                    Pierce = ArmorPierce.Mid,
                    BlockForce = 0f,
                    Source = packet.Source
                };
                ExplosionResolver.Apply(rodPacket, rod.Position, rod.Radius, Targets, _hitBuffer);
                LightningRodSystem.Consume(rod);
                jumps += LightningRodSystem.ExtraChainLinks;
            }

            return ChainResolver.Apply(
                origin, first, packet, jumps, range, falloff, Targets, _hitBuffer);
        }

        /// <summary>部署一个无人机编队。</summary>
        public void DeployDrones(int towerIndex, Vec2 position, TowerRuntimeStats stats, int count)
        {
            Drones.Deploy(towerIndex, position, stats, count);
        }

        /// <summary>单体结算(电容引爆等“不跳链”的额外伤害)。</summary>
        public void ApplySingle(IAoETarget target, in DamagePacket packet)
        {
            if (target == null || !target.Alive) return;
            float amount = DamageCalculator.Amount(packet, target.Def, target.BuildDamageContext());
            target.ApplyDamage(amount, packet.BlockForce);
        }

        /// <summary>放置避雷针。</summary>
        public void SpawnRod(Vec2 position, int charges, float radius = 12f, float duration = 60f)
        {
            Rods.Add(position, radius, duration, charges);
        }

        /// <summary>裂变闪电:每代分裂为两倍链条,逐代伤害递减。</summary>
        public int ApplyFission(
            Vec2 origin,
            IAoETarget first,
            in DamagePacket packet,
            int generations,
            float range)
        {
            _ = origin;
            var visited = new List<IAoETarget> { first };
            ApplySingle(first, packet);

            float scale = 0.75f;
            for (int gen = 1; gen <= generations; gen++)
            {
                int need = 1 << gen;
                int found = 0;
                while (found < need)
                {
                    IAoETarget? next = FindNearestUnvisited(visited, range);
                    if (next == null) break;

                    var scaled = packet;
                    scaled.Amount = packet.Amount * scale;
                    ApplySingle(next, scaled);
                    visited.Add(next);
                    found++;
                }
                if (found == 0) break;
                scale *= 0.75f;
            }

            return visited.Count;
        }

        private IAoETarget? FindNearestUnvisited(List<IAoETarget> visited, float range)
        {
            IAoETarget? best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < Targets.Count; i++)
            {
                IAoETarget candidate = Targets[i];
                if (!candidate.Alive || ContainsRef(visited, candidate)) continue;

                float distance = float.MaxValue;
                for (int v = 0; v < visited.Count; v++)
                {
                    float d = Vec2.Distance(visited[v].Position, candidate.Position);
                    if (d < distance) distance = d;
                }
                if (distance > range || distance >= bestDistance) continue;
                best = candidate;
                bestDistance = distance;
            }
            return best;
        }

        private static bool ContainsRef(List<IAoETarget> list, IAoETarget target)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], target)) return true;
            }
            return false;
        }

        public void Launch(
            Vec2 origin,
            Vec2 targetPosition,
            EntityId targetId,
            EntityId source,
            in DamagePacket packet,
            in ProjectileSpec spec)
        {
            float distance = Vec2.Distance(origin, targetPosition);
            float speed = spec.Speed > 0.01f ? spec.Speed : 60f;
            var projectile = new ProjectileRuntime
            {
                Source = source,
                TargetId = targetId,
                Start = origin,
                Position = origin,
                TargetPosition = targetPosition,
                Homing = spec.Homing,
                Speed = speed,
                Elapsed = 0f,
                FlightTime = Math.Max(0.02f, distance / speed),
                Packet = packet,
                Spec = spec
            };
            Projectiles.Add(projectile);
        }

        public void Tick(float dt)
        {
            UpdateProjectiles(dt);
            Areas.Tick(dt, Targets);
            Rods.Tick(dt);
            Drones.Tick(dt, Targets);
        }

        private void UpdateProjectiles(float dt)
        {
            for (int i = Projectiles.Count - 1; i >= 0; i--)
            {
                ProjectileRuntime projectile = Projectiles[i];
                projectile.Age += dt;
                bool impacted = false;

                if (projectile.Spec.AuraRadius > 0f && projectile.Spec.AuraInterval > 0f)
                {
                    projectile.AuraTimer -= dt;
                    if (projectile.AuraTimer <= 0f)
                    {
                        projectile.AuraTimer += projectile.Spec.AuraInterval;
                        ApplyAura(projectile);
                    }
                }

                if (projectile.Homing)
                {
                    IAoETarget? target = FindTarget(projectile.TargetId);
                    if (target != null && target.Alive) projectile.TargetPosition = target.Position;

                    Vec2 delta = projectile.TargetPosition - projectile.Position;
                    float distance = delta.Length;
                    float step = projectile.Speed * dt;
                    if (distance <= Math.Max(step, 0.5f))
                    {
                        projectile.Position = projectile.TargetPosition;
                        impacted = true;
                    }
                    else
                    {
                        projectile.Position += delta / distance * step;
                    }
                }
                else
                {
                    projectile.Elapsed += dt;
                    float t = projectile.FlightTime > 0f ? projectile.Elapsed / projectile.FlightTime : 1f;
                    if (t >= 1f)
                    {
                        projectile.Position = projectile.TargetPosition;
                        impacted = true;
                    }
                    else
                    {
                        projectile.Position = projectile.Start +
                            (projectile.TargetPosition - projectile.Start) * t;
                    }
                }

                // 漏电:到达目标后驻留放电,不做单体爆炸,直到寿命耗尽。
                if (impacted && projectile.Spec.AuraRadius > 0f)
                {
                    projectile.Position = projectile.TargetPosition;
                    if (projectile.Spec.Lifetime > 0f && projectile.Age >= projectile.Spec.Lifetime)
                    {
                        ApplyChargeDirect(projectile, projectile.Position, projectile.Spec.ChargeStacks);
                        Projectiles.RemoveAt(i);
                    }
                    continue;
                }

                if (!impacted)
                {
                    bool expired = projectile.Spec.Lifetime > 0f &&
                                   projectile.Age >= projectile.Spec.Lifetime;
                    if (!expired) continue;
                }

                if (impacted)
                {
                    ResolveImpact(projectile);
                    Impacted?.Invoke(projectile);
                }
                Projectiles.RemoveAt(i);
            }
        }

        private IAoETarget? FindTarget(EntityId id)
        {
            for (int i = 0; i < Targets.Count; i++)
            {
                if (Targets[i].Id.Index == id.Index && Targets[i].Id.Generation == id.Generation)
                    return Targets[i];
            }
            return null;
        }

        private void ResolveImpact(ProjectileRuntime projectile)
        {
            Vec2 center = projectile.TargetPosition;
            ProjectileSpec spec = projectile.Spec;
            float baseRadius = spec.ExplosionRadius;

            switch (spec.Mechanic)
            {
                case TowerMechanics.Fragmentation:
                    ExplodeStage(projectile.Packet, center, baseRadius,
                        spec.FirstStageDamage * 0.70f, ArmorPierce.High);
                    ExplodeStage(projectile.Packet, center, baseRadius * 3f * spec.SecondStageRadius,
                        spec.SecondStageDamage * 0.35f, ArmorPierce.Low);
                    break;

                case TowerMechanics.Cluster:
                case TowerMechanics.Swarm:
                    ResolveCluster(projectile, center);
                    break;

                case TowerMechanics.SplitWarhead:
                    ResolveSplit(projectile, center);
                    break;

                case TowerMechanics.Napalm:
                    SplitElemental(projectile, center, baseRadius, DamageType.Elemental,
                        TowerMechanics.Napalm);
                    AddZone("burn_zone", center, baseRadius * spec.ZoneScale);
                    break;

                case TowerMechanics.Cryo:
                    SplitElemental(projectile, center, baseRadius, DamageType.Elemental,
                        TowerMechanics.Cryo);
                    AddZone("chill_zone", center, baseRadius * spec.ZoneScale);
                    break;

                case TowerMechanics.Thermobaric:
                    ExplodeStage(projectile.Packet, center, baseRadius, 1f, ArmorPierce.Mid);
                    AddZone("burn_zone", center, baseRadius * spec.ZoneScale);
                    break;

                case TowerMechanics.Hydrogen:
                    ExplodeStage(projectile.Packet, center, 15f * spec.SecondStageRadius,
                        1f, ArmorPierce.High, DamageType.Radiation);
                    ExplodeStage(projectile.Packet, center, 60f * spec.SecondStageRadius,
                        spec.SecondStageDamage * 0.35f, ArmorPierce.Mid);
                    break;

                case TowerMechanics.SmartImplosion:
                    ExplodeStage(projectile.Packet, center, baseRadius,
                        spec.FirstStageDamage, projectile.Packet.Pierce);
                    ApplyArmorBreakToHits();
                    break;

                case TowerMechanics.PlasmaMissile:
                    ExplodeStage(projectile.Packet, center, baseRadius, 1f, ArmorPierce.Mid,
                        DamageType.Elemental);
                    ApplyChargeToHits(spec.ChargeStacks);
                    break;

                case TowerMechanics.BallLightning:
                    ExplodeStage(projectile.Packet, center, baseRadius, 1f, ArmorPierce.Mid,
                        DamageType.Elemental);
                    ApplyChargeToHits(spec.ChargeStacks);
                    break;

                case TowerMechanics.RollingThunder:
                    ResolveRollingThunder(projectile, center);
                    break;

                case TowerMechanics.LeakCurrent:
                    // 漏电:只在直接命中的目标上施加充电,范围伤害由光环逐帧结算。
                    ApplyChargeDirect(projectile, center, spec.ChargeStacks);
                    break;

                default:
                    ExplodeStage(projectile.Packet, center, baseRadius, 1f, projectile.Packet.Pierce);
                    break;
            }
        }

        private void ApplyAura(ProjectileRuntime projectile)
        {
            ProjectileSpec spec = projectile.Spec;
            var packet = new DamagePacket
            {
                Amount = projectile.Packet.Amount,
                Type = DamageType.Elemental,
                Pierce = projectile.Packet.Pierce,
                Source = projectile.Packet.Source
            };

            // 每次电击最多命中 1 + 连锁次数 个目标(继承连锁次数,而不是放大单次伤害)。
            int maxTargets = 1 + Math.Max(0, spec.ChainCount);
            var visited = new List<IAoETarget>();
            for (int hit = 0; hit < maxTargets; hit++)
            {
                IAoETarget? target = FindNextUnvisitedAfter(visited, projectile.Position, spec.AuraRadius);
                if (target == null) break;
                ApplySingle(target, packet);
                visited.Add(target);
            }
        }

        private void ApplyChargeToHits(int stacks)
        {
            if (stacks <= 0) return;
            for (int i = 0; i < _hitBuffer.Count; i++)
            {
                IAoETarget target = _hitBuffer[i];
                if (target.Status.TryApplyCharge(stacks, ArmorPierce.Mid, out ChargePayload payload))
                {
                    ChargeResolver.Detonate(payload, target.Position, Targets, _chargeBuffer);
                }
            }
        }

        private void ApplyChargeDirect(ProjectileRuntime projectile, Vec2 center, int stacks)
        {
            IAoETarget? target = FindTarget(projectile.TargetId);
            if (target == null || !target.Alive || Vec2.Distance(target.Position, center) > target.Radius + 1f)
                return;
            if (target.Status.TryApplyCharge(stacks, ArmorPierce.Mid, out ChargePayload payload))
            {
                ChargeResolver.Detonate(payload, target.Position, Targets, _chargeBuffer);
            }
        }

        private void ResolveRollingThunder(ProjectileRuntime projectile, Vec2 center)
        {
            ProjectileSpec spec = projectile.Spec;
            float radius = Math.Max(1f, spec.ExplosionRadius);

            IAoETarget? current = FindTarget(projectile.TargetId);
            if (current == null) current = FindNearestTo(center, radius);

            var visited = new List<IAoETarget>();
            for (int step = 0; step <= spec.ChainCount && current != null; step++)
            {
                var packet = new DamagePacket
                {
                    Amount = projectile.Packet.Amount,
                    Type = DamageType.Elemental,
                    Pierce = projectile.Packet.Pierce,
                    Source = projectile.Packet.Source
                };
                ExplosionResolver.Apply(packet, current.Position, radius, Targets, _hitBuffer);

                if (spec.ChargeStacks > 0 &&
                    current.Status.TryApplyCharge(spec.ChargeStacks, ArmorPierce.Mid, out ChargePayload payload))
                {
                    ChargeResolver.Detonate(payload, current.Position, Targets, _chargeBuffer);
                }

                visited.Add(current);
                current = FindNextUnvisitedAfter(visited, current.Position, spec.ChainRange);
            }
        }

        private IAoETarget? FindNearestTo(Vec2 point, float range)
        {
            IAoETarget? best = null;
            float bestDistance = range;
            for (int i = 0; i < Targets.Count; i++)
            {
                IAoETarget candidate = Targets[i];
                if (!candidate.Alive) continue;
                float distance = Vec2.Distance(point, candidate.Position);
                if (distance > bestDistance) continue;
                best = candidate;
                bestDistance = distance;
            }
            return best;
        }

        private IAoETarget? FindNextUnvisitedAfter(List<IAoETarget> visited, Vec2 from, float range)
        {
            IAoETarget? best = null;
            float bestDistance = range;
            for (int i = 0; i < Targets.Count; i++)
            {
                IAoETarget candidate = Targets[i];
                if (!candidate.Alive || ContainsRef(visited, candidate)) continue;
                float distance = Vec2.Distance(from, candidate.Position);
                if (distance > bestDistance) continue;
                best = candidate;
                bestDistance = distance;
            }
            return best;
        }

        private void ExplodeStage(
            in DamagePacket template,
            Vec2 center,
            float radius,
            float damageScale,
            ArmorPierce pierce,
            DamageType? overrideType = null)
        {
            var packet = new DamagePacket
            {
                Amount = template.Amount * damageScale,
                Type = overrideType ?? template.Type,
                Pierce = pierce,
                Retention = template.Retention,
                BlockForce = template.BlockForce,
                IsDot = false,
                Beam = false,
                Source = template.Source
            };
            ExplosionResolver.Apply(packet, center, radius, Targets, _hitBuffer);
        }

        private void ResolveSplit(ProjectileRuntime projectile, Vec2 center)
        {
            ProjectileSpec spec = projectile.Spec;
            float radius = spec.SubRadius > 0f ? spec.SubRadius : projectile.Spec.ExplosionRadius;
            for (int i = 0; i < 3; i++)
            {
                float angle = i * 1.0471976f; // 60°
                var point = new Vec2(
                    center.X + (float)Math.Cos(angle) * spec.SpreadRadius,
                    center.Y + (float)Math.Sin(angle) * spec.SpreadRadius);
                var packet = new DamagePacket
                {
                    Amount = projectile.Packet.Amount * spec.SubDamage,
                    Type = projectile.Packet.Type,
                    Pierce = projectile.Packet.Pierce,
                    BlockForce = projectile.Packet.BlockForce,
                    Source = projectile.Packet.Source
                };
                ExplosionResolver.Apply(packet, point, radius, Targets, _hitBuffer);
            }
        }

        private void SplitElemental(
            ProjectileRuntime projectile,
            Vec2 center,
            float radius,
            DamageType elementalType,
            string mechanic)
        {
            var explosive = new DamagePacket
            {
                Amount = projectile.Packet.Amount * 0.5f,
                Type = DamageType.Explosive,
                Pierce = projectile.Packet.Pierce,
                BlockForce = projectile.Packet.BlockForce,
                Source = projectile.Packet.Source
            };
            ExplosionResolver.Apply(explosive, center, radius, Targets, _hitBuffer);

            var elemental = new DamagePacket
            {
                Amount = projectile.Packet.Amount * 0.5f,
                Type = elementalType,
                Pierce = projectile.Packet.Pierce,
                BlockForce = projectile.Packet.BlockForce,
                Source = projectile.Packet.Source
            };
            ExplosionResolver.Apply(elemental, center, radius, Targets, _hitBuffer);

            if (mechanic == TowerMechanics.Cryo)
            {
                for (int i = 0; i < _hitBuffer.Count; i++) _hitBuffer[i].Status.ApplyChill(1);
            }
        }

        private void ResolveCluster(ProjectileRuntime projectile, Vec2 center)
        {
            ProjectileSpec spec = projectile.Spec;
            int count = Math.Max(1, spec.SubCount);
            float spread = Math.Max(0.5f, spec.SpreadRadius);
            float subRadius = spec.SubRadius > 0f ? spec.SubRadius : spec.ExplosionRadius;

            for (int i = 0; i < count; i++)
            {
                // 黄金角均匀铺满外圈,保证确定性与覆盖面积。
                float radius = spread * (float)Math.Sqrt((i + 0.5f) / count);
                float angle = i * 2.39996323f;
                var point = new Vec2(
                    center.X + (float)Math.Cos(angle) * radius,
                    center.Y + (float)Math.Sin(angle) * radius);

                var packet = new DamagePacket
                {
                    Amount = projectile.Packet.Amount * spec.SubDamage,
                    Type = projectile.Packet.Type,
                    Pierce = projectile.Packet.Pierce,
                    BlockForce = projectile.Packet.BlockForce,
                    Source = projectile.Packet.Source
                };
                ExplosionResolver.Apply(packet, point, subRadius, Targets, _hitBuffer);
            }
        }

        private void ApplyArmorBreakToHits()
        {
            for (int i = 0; i < _hitBuffer.Count; i++) _hitBuffer[i].Status.ApplyArmorBreak();
        }

        private void AddZone(string id, Vec2 center, float radius)
        {
            AreaEffectRule? rule = _rules.FindArea(id);
            if (rule == null) return;
            Areas.Add(rule, center, radius);
        }
    }
}
