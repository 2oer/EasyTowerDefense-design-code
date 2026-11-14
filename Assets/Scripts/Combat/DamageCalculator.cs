using Game.Model;

namespace Game.Combat
{
    /// <summary>
    /// 伤害系数 = 护甲系数 × 衰减减免 × 特殊效果(易伤 / 热熔 / 碎甲)。
    /// 实弹 / 光束不吃百分比衰减;爆炸 / 属性吃对应百分比;辐射恒 100%。
    /// </summary>
    public static class DamageCalculator
    {
        public static float Multiplier(in DamagePacket packet, EnemyDef def) =>
            Multiplier(packet, def, DamageContext.Neutral);

        public static float Multiplier(in DamagePacket packet, EnemyDef def, in DamageContext context)
        {
            if (packet.Type == DamageType.Radiation) return 1f;

            float armor = ArmorTable.Coefficient(packet.Pierce, def.Armor, context.ArmorBroken);
            float special = 1f;

            if (def.Vulnerability.HasValue && def.Vulnerability.Value == packet.Type)
                special *= 1.5f;

            // 0 视为未设置,避免 default(DamageContext) 把伤害清零。
            float elementalScale = context.ElementalDecayScale > 0f ? context.ElementalDecayScale : 1f;

            switch (packet.Type)
            {
                case DamageType.Ballistic:
                case DamageType.Energy:
                    break;
                case DamageType.Explosive:
                    special *= 1f - def.DecayExplosive;
                    break;
                case DamageType.Elemental:
                    special *= 1f - (packet.IsDot ? def.DecayElemental * 0.5f : def.DecayElemental) * elementalScale;
                    break;
            }

            if (packet.Beam && context.HeatMeltMultiplier > 0f)
                special *= context.HeatMeltMultiplier;

            return armor * special;
        }

        public static float Amount(in DamagePacket packet, EnemyDef def) =>
            packet.Amount * Multiplier(packet, def);

        public static float Amount(in DamagePacket packet, EnemyDef def, in DamageContext context) =>
            packet.Amount * Multiplier(packet, def, context);
    }
}
