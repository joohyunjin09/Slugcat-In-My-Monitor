using System;
using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.AI
{
    public enum AttentionKind
    {
        Mouse,
        Window,
        ScreenEdge,
        RandomPoint
    }

    public sealed class AttentionSystem
    {
        private Vec2 target;
        private Vec2 smoothed;
        private bool initialized;

        public AttentionKind Kind { get; private set; }
        public Vec2 Target { get { return target; } }
        public Vec2 Smoothed { get { return smoothed; } }

        public void SetTarget(AttentionKind kind, Vec2 value)
        {
            Kind = kind;
            target = value;
            if (!initialized)
            {
                initialized = true;
                smoothed = value;
            }
        }

        public void Step()
        {
            Vec2 delta = target - smoothed;
            double distance = delta.Length;
            double amount = MathUtil.Lerp(0.08, 0.28, MathUtil.InverseLerp(5.0, 250.0, distance));
            smoothed = Vec2.Lerp(smoothed, target, amount);
        }
    }
}
