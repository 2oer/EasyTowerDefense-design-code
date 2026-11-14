using System;

namespace Game.Core
{
    public static class MathUtil
    {
        public const float Epsilon = 1e-5f;
        public const float Deg2Rad = (float)Math.PI / 180f;
        public const float Rad2Deg = 180f / (float)Math.PI;

        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float Clamp01(float value) => Clamp(value, 0f, 1f);

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        /// <summary>保留一位小数,中点远离零(文档统一口径)。</summary>
        public static float Round1(float value) =>
            (float)Math.Round(value * 10.0, MidpointRounding.AwayFromZero) / 10f;

        /// <summary>四舍五入到整数,中点远离零。</summary>
        public static int RoundToInt(float value) =>
            (int)Math.Round(value, MidpointRounding.AwayFromZero);

        /// <summary>把角度差规范到 (−π, π]。</summary>
        public static float NormalizeAngle(float radians)
        {
            while (radians > Math.PI) radians -= 2f * (float)Math.PI;
            while (radians <= -Math.PI) radians += 2f * (float)Math.PI;
            return radians;
        }
    }
}
