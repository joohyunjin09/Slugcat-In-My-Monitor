using System;

namespace RainWorldDesktopPet.Core
{
    public static class MathUtil
    {
        public static double Clamp(double value, double minimum, double maximum)
        {
            return value < minimum ? minimum : (value > maximum ? maximum : value);
        }

        public static int Clamp(int value, int minimum, int maximum)
        {
            return value < minimum ? minimum : (value > maximum ? maximum : value);
        }

        public static double Clamp01(double value)
        {
            return Clamp(value, 0.0, 1.0);
        }

        public static double Lerp(double from, double to, double amount)
        {
            return from + (to - from) * Clamp01(amount);
        }

        public static double InverseLerp(double from, double to, double value)
        {
            if (Math.Abs(to - from) < 0.000001)
            {
                return 0.0;
            }

            return Clamp01((value - from) / (to - from));
        }

        public static double MoveTowards(double current, double target, double maximumDelta)
        {
            if (Math.Abs(target - current) <= maximumDelta)
            {
                return target;
            }

            return current + Math.Sign(target - current) * maximumDelta;
        }

        public static double SmoothStep(double from, double to, double amount)
        {
            amount = Clamp01(amount);
            amount = amount * amount * (3.0 - 2.0 * amount);
            return Lerp(from, to, amount);
        }

        public static Vec2 Direction(Vec2 from, Vec2 to)
        {
            return (to - from).Normalized;
        }

        public static Vec2 SlerpDirection(Vec2 from, Vec2 to, double amount)
        {
            Vec2 mixed = Vec2.Lerp(from.Normalized, to.Normalized, amount);
            return mixed.LengthSquared < 0.000001 ? to.Normalized : mixed.Normalized;
        }
    }
}
