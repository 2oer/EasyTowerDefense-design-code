using System;

namespace Game.Core
{
    /// <summary>二维向量。核心层不引用 UnityEngine,世界单位 = 文档单位。</summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 Zero => new Vec2(0f, 0f);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);

        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);

        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);

        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);

        public float SqrLength => X * X + Y * Y;

        public float Length => (float)Math.Sqrt(X * X + Y * Y);

        public Vec2 Normalized
        {
            get
            {
                float len = Length;
                return len > 1e-6f ? new Vec2(X / len, Y / len) : Zero;
            }
        }

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>二维叉积标量,用于求点到射线的法向偏移。</summary>
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;

        public static float SqrDistance(Vec2 a, Vec2 b) => (a - b).SqrLength;

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);

        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();

        public override string ToString() => "(" + X.ToString("0.###") + "," + Y.ToString("0.###") + ")";
    }
}
