using Game.Core;
using Game.Model;
using Game.Targeting;

namespace Game.Entity
{
    /// <summary>敌人运行时。M1 只包含移动、阻滞、近战与受伤,不含光环 / 投放 / 干扰。</summary>
    public sealed class EnemyRuntime : ITargetCandidate
    {
        public EntityId Id { get; private set; }
        public EnemyDef Def { get; private set; } = null!;
        public Vec2 Position { get; set; }
        public float MaxHp { get; private set; }
        public float Hp { get; private set; }
        public float DamageSent { get; set; }

        /// <summary>当前叠加的阻滞力与剩余生效时间(0.2 s)。</summary>
        public float BlockForce { get; private set; }
        public float BlockTimer { get; private set; }

        public bool ReachedBase { get; private set; }
        public float AttackTimer { get; private set; }

        public bool Alive => Hp > 0f;
        public float Radius => Def.Radius;

        public void Initialize(EntityId id, EnemyDef def, Vec2 position, float hp)
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
            float threshold = Def.BlockThreshold;
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
            float step = Def.Speed * MoveMultiplier() * dt;
            Position = Position + direction * step;

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
