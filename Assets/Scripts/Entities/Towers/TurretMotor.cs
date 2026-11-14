using System;
using Game.Core;

namespace Game.Entity
{
    /// <summary>炮塔转向。20 点 = 60°/s,即每点 3°/s。</summary>
    public sealed class TurretMotor
    {
        public const float DegreesPerSecondPerPoint = 3f;
        public const float PointBlankToleranceRad = 2f * MathUtil.Deg2Rad;

        public float AimAngle { get; private set; }

        public void Reset(float radians)
        {
            AimAngle = radians;
        }

        /// <summary>朝目标最大速度转动一帧,返回是否已进入容差。</summary>
        public bool TurnTowards(float desiredRadians, float turnSpeedPoints, float toleranceRad, float dt)
        {
            float maxStep = turnSpeedPoints * DegreesPerSecondPerPoint * MathUtil.Deg2Rad * dt;
            float delta = MathUtil.NormalizeAngle(desiredRadians - AimAngle);
            if (Math.Abs(delta) <= maxStep)
            {
                AimAngle = desiredRadians;
            }
            else
            {
                AimAngle = MathUtil.NormalizeAngle(AimAngle + (delta > 0f ? maxStep : -maxStep));
            }
            return Math.Abs(MathUtil.NormalizeAngle(desiredRadians - AimAngle)) <= toleranceRad + 1e-5f;
        }
    }
}
