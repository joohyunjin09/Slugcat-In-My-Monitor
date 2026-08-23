using System;
using System.Drawing;

namespace RainWorldDesktopPet.Core
{
    public struct Vec2
    {
        public double X;
        public double Y;

        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0.0, 0.0);
        public static readonly Vec2 Up = new Vec2(0.0, -1.0);
        public static readonly Vec2 Down = new Vec2(0.0, 1.0);
        public static readonly Vec2 Right = new Vec2(1.0, 0.0);

        public double LengthSquared { get { return X * X + Y * Y; } }
        public double Length { get { return Math.Sqrt(LengthSquared); } }

        public Vec2 Normalized
        {
            get
            {
                double length = Length;
                return length > 0.000001 ? this / length : Zero;
            }
        }

        public Vec2 Perpendicular { get { return new Vec2(-Y, X); } }

        public PointF ToPointF()
        {
            return new PointF((float)X, (float)Y);
        }

        public static Vec2 FromPoint(Point value)
        {
            return new Vec2(value.X, value.Y);
        }

        public static Vec2 Lerp(Vec2 from, Vec2 to, double amount)
        {
            amount = MathUtil.Clamp01(amount);
            return from + (to - from) * amount;
        }

        public static double Dot(Vec2 a, Vec2 b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        public static double Distance(Vec2 a, Vec2 b)
        {
            return (a - b).Length;
        }

        public static Vec2 ClampMagnitude(Vec2 value, double maximum)
        {
            double lengthSquared = value.LengthSquared;
            if (lengthSquared <= maximum * maximum)
            {
                return value;
            }

            return value.Normalized * maximum;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b)
        {
            return new Vec2(a.X + b.X, a.Y + b.Y);
        }

        public static Vec2 operator -(Vec2 a, Vec2 b)
        {
            return new Vec2(a.X - b.X, a.Y - b.Y);
        }

        public static Vec2 operator -(Vec2 value)
        {
            return new Vec2(-value.X, -value.Y);
        }

        public static Vec2 operator *(Vec2 value, double scale)
        {
            return new Vec2(value.X * scale, value.Y * scale);
        }

        public static Vec2 operator *(double scale, Vec2 value)
        {
            return value * scale;
        }

        public static Vec2 operator /(Vec2 value, double scale)
        {
            return new Vec2(value.X / scale, value.Y / scale);
        }

        public override string ToString()
        {
            return string.Format("({0:0.0}, {1:0.0})", X, Y);
        }
    }
}
