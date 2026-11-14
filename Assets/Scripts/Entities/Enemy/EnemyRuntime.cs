using Game.Combat;
using Game.Core;
using Game.Model;
using Game.Targeting;

namespace Game.Entity
{
    /// <summary>敌人运行时。M1 包含移动、阻滞、近战与受伤,并挂载异常状态容器。</summary>
    public sealed class EnemyRuntime : ITargetCandidate, IAoETarget
    {
        public EntityId Id { get; private set; }
        public EnemyDef Def { get; private set; } = null!;
        public Vec2 Position { get; set; }
        public float MaxHp { get; private set; }
        public float Hp { get; private set; }
        public float DamageSent { get; set; }

        /// <summary>异常状态:燃烧 / 低温 / 充电 / 衰变 / 碎甲 / 热熔。</summary>
        public StatusSet Status { get; } = new StatusSet();

        /// <summary>当前叠加的阻滞力与剩余生效时间(0.2 s)。</summary>
        public float BlockForce { get; private set; }
        public float BlockTimer { get; private set; }

        public bool ReachedBase { get; private set; }
        public float AttackTimer { get; private set; }

        /// <summary>上一帧的移动速度向量,供迫击炮 / 火箭做落点预判。</summary>
        public Vec2 Velocity { get; private set; }

        public bool Alive => Hp > 0f;
        public float Radius => Def.Radius;

        public void Initialize(EntityId id, EnemyDef def, Vec2 position, float hp, StatusRules statusRules)
        {
            Id = id;
            Def = def;
            Position = position;
            MaxHp = hp;
            Hp = hp;
            DamageSent = 0f;
            BlockForce = 0f;
            BlockTimer = 0f;
            ReachedBase = false;
            AttackTimer = 0f;
            Velocity = Vec2.Zero;
            Status.Reset(statusRules);
        }

        public void ApplyDamage(float amount, float blockForce)
        {
            if (amount <= 0f || !Alive) return;
            Hp -= amount;
            if (Hp < 0f) Hp = 0f;
            DamageSent += amount;
            if (blockForce > 0f)
            {
                BlockForce += blockForce;
                BlockTimer = 0.2f;
            }
        }

        public void ApplyBlock(float force)
        {
            if (force <= 0f) return;
            BlockForce += force;
            BlockTimer = 0.2f;
        }

        /// <summary>把异常状态折算成伤害修正;热熔由 DamageCalculator 只在带 Beam 标记时读取。</summary>
        public DamageContext BuildDamageContext() => Status.BuildDamageContext();

        /// <summary>推进异常状态并结算 DoT 跳伤,返回本帧跳伤明细(已扣血)。</summary>
        public StatusTickResult TickStatuses(float dt)
        {
            StatusTickResult result = Status.Tick(dt);
            if (result.BurnDamage > 0f)
            {
                var packet = new DamagePacket
                {
                    Amount = result.BurnDamage,
                    Type = DamageType.Elemental,
                    Pierce = result.BurnPierce,
                    IsDot = true,
                    Source = Id
                };
                ApplyDamage(DamageCalculator.Amount(packet, Def, Status.BuildDamageContext()), 0f);
            }

            if (result.DecayDamage > 0f)
            {
                var packet = new DamagePacket
                {
                    Amount = result.DecayDamage,
                    Type = DamageType.Radiation,
                    IsDot = true,
                    Source = Id
                };
                ApplyDamage(DamageCalculator.Amount(packet, Def, Status.BuildDamageContext()), 0f);
            }

            return result;
        }

        public void TickBlock(float dt)
        {
            if (BlockTimer <= 0f) return;
            BlockTimer -= dt;
            if (BlockTimer <= 0f)
            {
                BlockTimer = 0f;
                BlockForce = 0f;
            }
        }

        /// <summary>阻滞换算成前进倍率:≤阈值 1×;(阈值,2×阈值) 线性降到 0;>2×阈值 反向 0.25×。</summary>
        public float MoveMultiplier()
        {
            float threshold = Def.BlockThreshold * Status.BlockThresholdMultiplier;
            if (BlockForce <= threshold) return 1f;
            if (BlockForce < 2f * threshold) return 1f - (BlockForce - threshold) / threshold;
            return -0.25f;
        }

        public void TickMovement(float dt, Vec2 basePosition)
        {
            if (!Alive || ReachedBase) return;

            Vec2 toBase = basePosition - Position;
            float distance = toBase.Length;
            if (distance <= 1f)
            {
                ReachedBase = true;
                return;
            }

            Vec2 direction = toBase / distance;
            float speed = Def.Speed * MoveMultiplier();
            Velocity = direction * speed;
            Position = Position + direction * (speed * dt);

            if (Vec2.Distance(Position, basePosition) <= 1f) ReachedBase = true;
        }

        /// <summary>抵达基地后的连续近战,返回本 tick 对基地造成的伤害。</summary>
        public float TickMelee(float dt)
        {
            if (!Alive || !ReachedBase) return 0f;
            AttackTimer -= dt;
            if (AttackTimer > 0f) return 0f;
            AttackTimer += Def.MeleeInterval;
            return Hp * Def.BodyCoef;
        }
    }
}
