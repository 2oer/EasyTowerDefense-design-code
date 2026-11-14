using System.Collections.Generic;
using Game.Core;
using Game.Model;

namespace Game.Combat
{
    /// <summary>一块地面区域(燃烧区域 / 低温区域)。同一敌人可同时处于多块区域,各自独立结算。</summary>
    public sealed class AreaEffectField
    {
        public string RuleId { get; set; } = string.Empty;
        public StatusKind Kind { get; set; }
        public Vec2 Center { get; set; }
        public float Radius { get; set; }
        public float Remaining { get; set; }
        public float ApplyInterval { get; set; }
        public float ApplyTimer { get; set; }
        public int Stacks { get; set; }
        public float BlockForce { get; set; }
    }

    /// <summary>
    /// 属性区域系统:每 ApplyInterval 秒为区域内全体敌人施加一次状态。
    /// 目前由迫击炮的凝固汽油弹 / 液氮弹使用,塔尚未接入。
    /// </summary>
    public sealed class AreaEffectSystem
    {
        private readonly List<AreaEffectField> _fields = new List<AreaEffectField>();

        public IReadOnlyList<AreaEffectField> Fields => _fields;

        public AreaEffectField Add(AreaEffectRule rule, Vec2 center, float radius)
        {
            var field = new AreaEffectField
            {
                RuleId = rule.Id,
                Kind = rule.Kind,
                Center = center,
                Radius = radius,
                Remaining = rule.Duration,
                ApplyInterval = rule.ApplyInterval,
                ApplyTimer = rule.ApplyInterval,
                Stacks = rule.StacksPerApply,
                BlockForce = rule.BlockForce
            };
            _fields.Add(field);
            return field;
        }

        /// <summary>推进所有区域;返回本次施加到敌人的次数(用于统计 / 表现)。</summary>
        public int Tick(float dt, IReadOnlyList<IAoETarget> targets)
        {
            int applies = 0;
            for (int i = _fields.Count - 1; i >= 0; i--)
            {
                AreaEffectField field = _fields[i];
                field.ApplyTimer -= dt;
                while (field.ApplyTimer <= 0f && field.Remaining > 0f)
                {
                    applies += ApplyOnce(field, targets);
                    field.ApplyTimer += field.ApplyInterval;
                }

                field.Remaining -= dt;
                if (field.Remaining <= 0f) _fields.RemoveAt(i);
            }
            return applies;
        }

        public void Clear()
        {
            _fields.Clear();
        }

        private static int ApplyOnce(AreaEffectField field, IReadOnlyList<IAoETarget> targets)
        {
            int count = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                IAoETarget target = targets[i];
                if (!AoeQuery.Contains(field.Center, field.Radius, target)) continue;

                switch (field.Kind)
                {
                    case StatusKind.Burn:
                        target.Status.ApplyBurn(field.Stacks, ArmorPierce.Low);
                        break;
                    case StatusKind.Chill:
                        target.Status.ApplyChill(field.Stacks);
                        if (field.BlockForce > 0f) target.ApplyBlock(field.BlockForce);
                        break;
                }

                count++;
            }
            return count;
        }
    }
}
