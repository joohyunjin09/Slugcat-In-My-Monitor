using System;
using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.AI
{
    public sealed class UtilityContext
    {
        public bool Grounded;
        public bool WallContact;
        public bool OnWindow;
        public double MouseDistance;
        public double MouseSpeed;
        public double Fatigue;
        public double Curiosity;
        public double EdgeDistance;
        public double BehaviorAgeSeconds;
        public double Stillness;
        public int SaferDirection;
        public bool JumpReady;
        public bool DropReady;
        public bool TransitionAvailable;
    }

    public static class UtilityEvaluator
    {
        public static double Score(DesktopBehavior behavior, UtilityContext context, double variation)
        {
            double score;
            switch (behavior)
            {
                case DesktopBehavior.Idle:
                    score = 0.32 + context.Stillness * 0.16;
                    break;
                case DesktopBehavior.Walk:
                    score = context.Grounded ? 0.34 + context.Curiosity * 0.22 : 0.02;
                    break;
                case DesktopBehavior.Explore:
                    score = context.Grounded ? 0.18 + context.Curiosity * 0.72 : 0.01;
                    break;
                case DesktopBehavior.Sit:
                    score = context.Grounded ? 0.1 + context.Fatigue * 0.65 + context.Stillness * 0.18 : 0.0;
                    break;
                case DesktopBehavior.Sleep:
                    score = context.Grounded ? Math.Max(0.0, context.Fatigue - 0.58) * 1.9 : 0.0;
                    break;
                case DesktopBehavior.LookAround:
                    score = 0.17 + context.Curiosity * 0.34 + context.Stillness * 0.12;
                    break;
                case DesktopBehavior.FollowMouse:
                    score = context.MouseDistance > 90.0 && context.MouseDistance < 650.0
                        ? 0.15 + context.Curiosity * 0.48 + MathUtil.InverseLerp(650.0, 140.0, context.MouseDistance) * 0.25
                        : 0.02;
                    break;
                case DesktopBehavior.AvoidMouse:
                    score = context.MouseDistance < 105.0
                        ? 0.72 + MathUtil.InverseLerp(105.0, 20.0, context.MouseDistance) * 0.5 + MathUtil.Clamp01(context.MouseSpeed / 1400.0) * 0.25
                        : 0.0;
                    break;
                case DesktopBehavior.Jump:
                    score = context.Grounded && context.TransitionAvailable &&
                            context.JumpReady && context.Curiosity > 0.5
                        ? 0.72 + context.Curiosity * 0.42
                        : 0.0;
                    break;
                case DesktopBehavior.ClimbWindow:
                    score = context.WallContact && !context.Grounded ? 0.88 : 0.0;
                    break;
                case DesktopBehavior.DropDown:
                    score = context.Grounded && context.OnWindow && context.DropReady &&
                            context.EdgeDistance < 24.0 && context.Curiosity > 0.72
                        ? 1.15
                        : 0.0;
                    break;
                case DesktopBehavior.BalanceNearEdge:
                    score = context.Grounded && context.EdgeDistance < 34.0 ? 0.78 : 0.0;
                    break;
                case DesktopBehavior.ObserveWindow:
                    score = context.OnWindow ? 0.26 + context.Curiosity * 0.42 : 0.0;
                    break;
                default:
                    score = 0.0;
                    break;
            }

            return Math.Max(0.0, score + variation);
        }
    }
}
