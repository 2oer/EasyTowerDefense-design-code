using System.Collections.Generic;

namespace Game.Model
{
    /// <summary>异常效果种类。数值全部来自 effects.json,本枚举只做分派。</summary>
    public enum StatusKind
    {
        Burn = 0,
        Chill = 1,
        HeatMelt = 2,
        ArmorBreak = 3,
        Charge = 4,
        Decay = 5
    }

    /// <summary>燃烧:每秒一跳的火属性 DoT,层数共用同一池。</summary>
    public sealed class BurnRule
    {
        public int MaxStacks { get; set; } = 5;
        public float Duration { get; set; } = 3f;
        public float TickInterval { get; set; } = 1f;
        public float BaseDamagePerSecond { get; set; } = 20f;

        /// <summary>逐层倍率,下标 = 层数 − 1;长度必须等于 MaxStacks。</summary>
        public List<float> StackMultipliers { get; set; } = new List<float> { 1f, 1.6f, 2.4f, 3.5f, 5f };
    }

    /// <summary>低温:降低阻滞阈值;满层时属性衰减率减半。</summary>
    public sealed class ChillRule
    {
        public int MaxStacks { get; set; } = 3;
        public float Duration { get; set; } = 3f;

        /// <summary>逐层降幅(累加),下标 = 层数 − 1;满层合计 25%。</summary>
        public List<float> ThresholdReductions { get; set; } = new List<float> { 0.05f, 0.10f, 0.10f };

        /// <summary>满层时属性衰减率的乘数。</summary>
        public float MaxStackElementalDecayScale { get; set; } = 0.5f;
    }

    /// <summary>热熔易伤:只在激光束持续照射同一目标时累计,切换目标清零。</summary>
    public sealed class HeatMeltRule
    {
        public float MaxMultiplier { get; set; } = 2f;
        public float RampSeconds { get; set; } = 3f;
    }

    /// <summary>碎甲易伤:只修正护甲系数(50→70、0→20)。</summary>
    public sealed class ArmorBreakRule
    {
        public float Duration { get; set; } = 5f;
    }

    /// <summary>充电:满层再施加或携带者死亡时引爆为电属性范围伤害。</summary>
    public sealed class ChargeRule
    {
        public int MaxStacks { get; set; } = 3;
        public float BaseDamage { get; set; } = 80f;

        /// <summary>每层加算的伤害增幅(1 层 0%、2 层 +30%、3 层 +60%)。</summary>
        public float StackDamageBonus { get; set; } = 0.3f;

        /// <summary>各层数的引爆半径,下标 = 层数 − 1。</summary>
        public List<float> RadiusByStacks { get; set; } = new List<float> { 1f, 2f, 4f };
    }

    /// <summary>衰变:每 2 秒结算一次 2^层数 的辐射伤害,结算后层数减半。</summary>
    public sealed class DecayRule
    {
        public int MaxStacks { get; set; } = 8;
        public float TickInterval { get; set; } = 2f;
        public float ExponentBase { get; set; } = 2f;
    }

    /// <summary>地面区域:每 ApplyInterval 秒为区域内全体敌人施加一次状态。</summary>
    public sealed class AreaEffectRule
    {
        public string Id { get; set; } = string.Empty;
        public StatusKind Kind { get; set; } = StatusKind.Burn;
        public float Duration { get; set; } = 3f;
        public float ApplyInterval { get; set; } = 1f;
        public int StacksPerApply { get; set; } = 1;
        public float BlockForce { get; set; }
    }

    /// <summary>effects.json 的根。所有特殊效果与区域效果的数值权威源。</summary>
    public sealed class StatusRules
    {
        public BurnRule Burn { get; set; } = new BurnRule();
        public ChillRule Chill { get; set; } = new ChillRule();
        public HeatMeltRule HeatMelt { get; set; } = new HeatMeltRule();
        public ArmorBreakRule ArmorBreak { get; set; } = new ArmorBreakRule();
        public ChargeRule Charge { get; set; } = new ChargeRule();
        public DecayRule Decay { get; set; } = new DecayRule();
        public List<AreaEffectRule> Areas { get; set; } = new List<AreaEffectRule>();

        public AreaEffectRule? FindArea(string id)
        {
            for (int i = 0; i < Areas.Count; i++)
            {
                if (string.Equals(Areas[i].Id, id, System.StringComparison.Ordinal)) return Areas[i];
            }
            return null;
        }
    }
}
