using System;
using Game.Model;

namespace Game.Combat
{
    /// <summary>
    /// 一次伤害结算需要读取的状态修正。中性值 = 无碎甲、热熔 ×1、属性衰减不缩放。
    /// </summary>
    public struct DamageContext
    {
        /// <summary>碎甲易伤:护甲系数 50→70、0→20。</summary>
        public bool ArmorBroken;

        /// <summary>热熔易伤倍率,只对带 Beam 标记的伤害生效。</summary>
        public float HeatMeltMultiplier;

        /// <summary>属性衰减缩放(低温满层 0.5)。</summary>
        public float ElementalDecayScale;

        public static DamageContext Neutral =>
            new DamageContext { HeatMeltMultiplier = 1f, ElementalDecayScale = 1f };
    }

    /// <summary>充电引爆参数。引爆伤害不再施加充电。</summary>
    public readonly struct ChargePayload
    {
        public readonly int Stacks;
        public readonly float Damage;
        public readonly float Radius;
        public readonly ArmorPierce Pierce;

        public ChargePayload(int stacks, float damage, float radius, ArmorPierce pierce)
        {
            Stacks = stacks;
            Damage = damage;
            Radius = radius;
            Pierce = pierce;
        }
    }

    /// <summary>本帧 DoT 跳伤(尚未经过护甲 / 衰减减免)。</summary>
    public struct StatusTickResult
    {
        public float BurnDamage;
        public ArmorPierce BurnPierce;
        public float DecayDamage;
    }

    /// <summary>
    /// 单个敌人的异常状态容器。只做状态与跳伤计算,不直接写敌人血量;
    /// 由 Entity 层把 StatusTickResult 交给 DamageCalculator 结算。
    /// </summary>
    public sealed class StatusSet
    {
        private StatusRules _rules = null!;

        private int _burnStacks;
        private float _burnRemaining;
        private float _burnTick;
        private ArmorPierce _burnPierce = ArmorPierce.Low;

        private int _chillStacks;
        private float _chillRemaining;

        private int _chargeStacks;
        private ArmorPierce _chargePierce = ArmorPierce.Low;

        private int _decayStacks;
        private float _decayTick;

        private float _armorBreakRemaining;
        private float _heatMeltSeconds;

        public int BurnStacks => _burnStacks;
        public float BurnRemaining => _burnRemaining;
        public int ChillStacks => _chillStacks;
        public int ChargeStacks => _chargeStacks;
        public int DecayStacks => _decayStacks;
        public float ArmorBreakRemaining => _armorBreakRemaining;
        public float HeatMeltSeconds => _heatMeltSeconds;

        public bool HasArmorBreak => _armorBreakRemaining > 0f;
        public bool HasHeatMelt => _heatMeltSeconds > 0f;
        public bool HasAny => _burnStacks > 0 || _chillStacks > 0 || _chargeStacks > 0 ||
                              _decayStacks > 0 || HasArmorBreak || HasHeatMelt;

        public void Reset(StatusRules rules)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Clear();
        }

        public void Clear()
        {
            _burnStacks = 0;
            _burnRemaining = 0f;
            _burnTick = 0f;
            _burnPierce = ArmorPierce.Low;
            _chillStacks = 0;
            _chillRemaining = 0f;
            _chargeStacks = 0;
            _chargePierce = ArmorPierce.Low;
            _decayStacks = 0;
            _decayTick = 0f;
            _armorBreakRemaining = 0f;
            _heatMeltSeconds = 0f;
        }

        // ---- 施加 --------------------------------------------------------------

        /// <summary>燃烧 +N 层,刷新时长;同一层数池,不超过上限。</summary>
        public void ApplyBurn(int stacks, ArmorPierce sourcePierce)
        {
            if (stacks <= 0) return;
            bool wasActive = _burnStacks > 0;
            _burnStacks = Math.Min(_rules.Burn.MaxStacks, _burnStacks + stacks);
            _burnRemaining = _rules.Burn.Duration;
            _burnPierce = sourcePierce;
            if (!wasActive) _burnTick = _rules.Burn.TickInterval;
        }

        /// <summary>低温 +N 层,刷新时长。</summary>
        public void ApplyChill(int stacks)
        {
            if (stacks <= 0) return;
            _chillStacks = Math.Min(_rules.Chill.MaxStacks, _chillStacks + stacks);
            _chillRemaining = _rules.Chill.Duration;
        }

        /// <summary>碎甲易伤,刷新时长。</summary>
        public void ApplyArmorBreak()
        {
            _armorBreakRemaining = _rules.ArmorBreak.Duration;
        }

        /// <summary>热熔累计:只在激光束持续照射同一目标时调用。</summary>
        public void AddHeatMelt(float seconds)
        {
            if (seconds <= 0f) return;
            _heatMeltSeconds += seconds;
            if (_heatMeltSeconds > _rules.HeatMelt.RampSeconds)
                _heatMeltSeconds = _rules.HeatMelt.RampSeconds;
        }

        /// <summary>切换目标时清零热熔累计。</summary>
        public void ResetHeatMelt()
        {
            _heatMeltSeconds = 0f;
        }

        /// <summary>衰变 +N 层;首次施加时重置 2 秒结算计时。</summary>
        public void ApplyDecay(int stacks)
        {
            if (stacks <= 0) return;
            bool wasActive = _decayStacks > 0;
            _decayStacks = Math.Min(_rules.Decay.MaxStacks, _decayStacks + stacks);
            if (!wasActive) _decayTick = _rules.Decay.TickInterval;
        }

        /// <summary>
        /// 施加充电。未满层返回 false;已满层后再次施加返回 true 并给出引爆参数,
        /// 同时清空层数。死亡引爆请调用 <see cref="TryDetonateOnDeath"/>。
        /// </summary>
        public bool TryApplyCharge(int stacks, ArmorPierce sourcePierce, out ChargePayload payload)
        {
            payload = default;
            if (_chargeStacks >= _rules.Charge.MaxStacks)
            {
                payload = BuildChargePayload(_chargeStacks, _chargePierce);
                _chargeStacks = 0;
                return true;
            }

            _chargeStacks = Math.Min(_rules.Charge.MaxStacks, _chargeStacks + Math.Max(1, stacks));
            _chargePierce = sourcePierce;
            return false;
        }

        /// <summary>携带充电的敌人死亡时引爆并清空层数。</summary>
        public bool TryDetonateOnDeath(out ChargePayload payload)
        {
            if (_chargeStacks <= 0)
            {
                payload = default;
                return false;
            }

            payload = BuildChargePayload(_chargeStacks, _chargePierce);
            _chargeStacks = 0;
            return true;
        }

        // ---- 查询 --------------------------------------------------------------

        /// <summary>低温对阻滞阈值的乘数(满层 0.75)。</summary>
        public float BlockThresholdMultiplier
        {
            get
            {
                if (_chillStacks <= 0) return 1f;
                int count = Math.Min(_chillStacks, _rules.Chill.ThresholdReductions.Count);
                float reduction = 0f;
                for (int i = 0; i < count; i++) reduction += _rules.Chill.ThresholdReductions[i];
                return 1f - reduction;
            }
        }

        /// <summary>低温满层时属性衰减率减半。</summary>
        public float ElementalDecayScale =>
            _chillStacks >= _rules.Chill.MaxStacks ? _rules.Chill.MaxStackElementalDecayScale : 1f;

        /// <summary>热熔易伤倍率:3 秒线性从 ×1 升到 ×2。</summary>
        public float HeatMeltMultiplier
        {
            get
            {
                if (_heatMeltSeconds <= 0f) return 1f;
                float t = _heatMeltSeconds / _rules.HeatMelt.RampSeconds;
                if (t > 1f) t = 1f;
                return 1f + (_rules.HeatMelt.MaxMultiplier - 1f) * t;
            }
        }

        public DamageContext BuildDamageContext()
        {
            DamageContext context = DamageContext.Neutral;
            context.ArmorBroken = HasArmorBreak;
            context.HeatMeltMultiplier = HeatMeltMultiplier;
            context.ElementalDecayScale = ElementalDecayScale;
            return context;
        }

        public float BurnDamagePerTick
        {
            get
            {
                if (_burnStacks <= 0) return 0f;
                int index = Math.Min(_burnStacks, _rules.Burn.StackMultipliers.Count) - 1;
                return _rules.Burn.BaseDamagePerSecond * _rules.Burn.StackMultipliers[index];
            }
        }

        // ---- 结算 --------------------------------------------------------------

        /// <summary>推进状态计时并返回本帧跳伤。燃烧每 1 s、衰变每 2 s 各结算一次。</summary>
        public StatusTickResult Tick(float dt)
        {
            var result = new StatusTickResult();
            if (dt <= 0f || _rules == null) return result;

            if (_burnStacks > 0)
            {
                _burnTick -= dt;
                while (_burnTick <= 0f && _burnStacks > 0)
                {
                    result.BurnDamage += BurnDamagePerTick;
                    result.BurnPierce = _burnPierce;
                    _burnTick += _rules.Burn.TickInterval;
                }

                _burnRemaining -= dt;
                if (_burnRemaining <= 0f)
                {
                    _burnStacks = 0;
                    _burnRemaining = 0f;
                    _burnTick = 0f;
                }
            }

            if (_chillStacks > 0)
            {
                _chillRemaining -= dt;
                if (_chillRemaining <= 0f)
                {
                    _chillStacks = 0;
                    _chillRemaining = 0f;
                }
            }

            if (_armorBreakRemaining > 0f)
            {
                _armorBreakRemaining -= dt;
                if (_armorBreakRemaining < 0f) _armorBreakRemaining = 0f;
            }

            if (_decayStacks > 0)
            {
                _decayTick -= dt;
                while (_decayTick <= 0f && _decayStacks > 0)
                {
                    result.DecayDamage += (float)Math.Pow(_rules.Decay.ExponentBase, _decayStacks);
                    _decayStacks /= 2;
                    _decayTick += _rules.Decay.TickInterval;
                }
            }

            return result;
        }

        private ChargePayload BuildChargePayload(int stacks, ArmorPierce pierce)
        {
            int index = Math.Min(Math.Max(stacks, 1), _rules.Charge.RadiusByStacks.Count) - 1;
            float damage = _rules.Charge.BaseDamage * (1f + _rules.Charge.StackDamageBonus * (stacks - 1));
            return new ChargePayload(stacks, damage, _rules.Charge.RadiusByStacks[index], pierce);
        }
    }
}
