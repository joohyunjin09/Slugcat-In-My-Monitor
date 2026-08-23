using System;
using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Desktop
{
    public sealed class MouseAttentionState
    {
        public MouseAttentionState()
            : this(SimulationConstants.MouseAttentionRadius,
                SimulationConstants.MouseAttentionTimeoutSeconds)
        {
        }

        public MouseAttentionState(double radius, double timeoutSeconds)
        {
            if (radius <= 0.0) throw new ArgumentOutOfRangeException("radius");
            if (timeoutSeconds <= 0.0) throw new ArgumentOutOfRangeException("timeoutSeconds");
            Radius = radius;
            TimeoutSeconds = timeoutSeconds;
            LastRelevantClickTime = double.NegativeInfinity;
            TimeSinceRelevantClick = double.PositiveInfinity;
        }

        public double Radius { get; private set; }
        public double TimeoutSeconds { get; private set; }
        public Vec2 MousePosition { get; private set; }
        public double DistanceToHead { get; private set; }
        public bool IsMouseNear { get; private set; }
        public bool HasRecentRelevantClick { get; private set; }
        public double LastRelevantClickTime { get; private set; }
        public double TimeSinceRelevantClick { get; private set; }
        public bool IsActive { get; private set; }

        public void Update(double currentTime, Vec2 mousePosition, bool clicked, Vec2 headPosition)
        {
            MousePosition = mousePosition;
            DistanceToHead = Vec2.Distance(mousePosition, headPosition);
            IsMouseNear = DistanceToHead <= Radius;

            if (clicked && IsMouseNear)
            {
                LastRelevantClickTime = currentTime;
                HasRecentRelevantClick = true;
            }

            TimeSinceRelevantClick = HasRecentRelevantClick
                ? Math.Max(0.0, currentTime - LastRelevantClickTime)
                : double.PositiveInfinity;
            if (HasRecentRelevantClick && TimeSinceRelevantClick > TimeoutSeconds)
                HasRecentRelevantClick = false;

            IsActive = IsMouseNear && HasRecentRelevantClick;
        }

        public void Suppress(double currentTime, Vec2 mousePosition, Vec2 headPosition)
        {
            MousePosition = mousePosition;
            DistanceToHead = Vec2.Distance(mousePosition, headPosition);
            IsMouseNear = DistanceToHead <= Radius;
            HasRecentRelevantClick = false;
            LastRelevantClickTime = double.NegativeInfinity;
            TimeSinceRelevantClick = double.PositiveInfinity;
            IsActive = false;
        }
    }
}
