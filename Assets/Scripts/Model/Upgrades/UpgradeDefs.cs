using System.Collections.Generic;

namespace Game.Model
{
    /// <summary>可被升级改写的运行时数值。枚举名即 JSON 中的 stat key。</summary>
    public enum StatKey
    {
        Damage = 0,
        FireRate = 1,
        Range = 2,
        TurnSpeed = 3,
        BlockForce = 4,
        Retention = 5,
        Ammo = 6,
        ReloadTime = 7,
        BeamWidth = 8,
        BulletRadius = 9,
        ExplosionRadius = 10,

        /// <summary>激光束数量;默认 1。</summary>
        BeamCount = 11
    }

    public enum ModifierOp
    {
        /// <summary>加法;文档里的“小幅/中幅”换算成百分比后也可用 Mul。</summary>
        Add = 0,

        /// <summary>乘法,增幅一律乘算。</summary>
        Mul = 1,

        /// <summary>直接覆盖(弹匣 10 发、留存 10.0 这类定值)。</summary>
        Set = 2
    }

    public struct StatModifier
    {
        public StatKey Key { get; set; }
        public ModifierOp Op { get; set; }
        public float Value { get; set; }
    }

    /// <summary>升级节点。费用由 TowerDef.Price × CostFactor × (Level−1)² 推出。</summary>
    public sealed class UpgradeNodeDef
    {
        public string Id { get; set; } = string.Empty;
        public string TowerId { get; set; } = string.Empty;

        /// <summary>目标等级(2–7)。</summary>
        public int Level { get; set; }

        /// <summary>所属分支;唯一升级 I / II 用 "unique"。仅用于分组展示,不参与结算。</summary>
        public string Branch { get; set; } = "common";

        public float CostFactor { get; set; } = 1f;

        /// <summary>Lv4 / Lv7 的唯一升级,费用额外 ×1.25。</summary>
        public bool Unique { get; set; }

        /// <summary>前置节点 id;空表示同级任选。</summary>
        public List<string> Requires { get; set; } = new List<string>();

        public List<StatModifier> Modifiers { get; set; } = new List<StatModifier>();

        /// <summary>非空时覆盖穿甲等级。</summary>
        public ArmorPierce? Pierce { get; set; }

        /// <summary>非空时覆盖索敌策略。</summary>
        public TowerStrategy? Strategy { get; set; }

        /// <summary>转为持续照射型(聚焦 / 扫荡透镜):按弹容与重装折算等效射速。</summary>
        public bool Continuous { get; set; }
    }
}
