using Game.Model;

namespace Game.Combat
{
    /// <summary>贯穿结算:每命中一次按该敌人的实弹 / 能量衰减扣留存率,跨敌人累计。</summary>
    public static class PenetrationResolver
    {
        /// <summary>贯穿阈值 1 点:剩余 ≥1 才能继续贯穿。</summary>
        public const float Threshold = 1f;

        public static float Consume(float retention, in DamagePacket packet, EnemyDef def) =>
            retention - def.Decay(packet.Type);

        public static bool CanContinue(float retention) => retention >= Threshold;
    }
}
