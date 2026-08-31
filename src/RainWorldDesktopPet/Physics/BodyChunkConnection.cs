using System;
using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Physics
{
    public enum BodyChunkConnectionType
    {
        Normal,
        Pull,
        Push
    }

    public sealed class BodyChunkConnection
    {
        private double distance;
        private double maximumDistance = double.PositiveInfinity;

        public BodyChunkConnection(
            BodyChunk first,
            BodyChunk second,
            double distance,
            BodyChunkConnectionType type,
            double elasticity,
            double weightSymmetry)
        {
            First = first;
            Second = second;
            Distance = distance;
            Type = type;
            Elasticity = elasticity;
            WeightSymmetry = weightSymmetry;
        }

        public readonly BodyChunk First;
        public readonly BodyChunk Second;
        public double Distance
        {
            get { return distance; }
            set
            {
                if (value <= 0.0 || double.IsNaN(value) || double.IsInfinity(value))
                    throw new ArgumentOutOfRangeException("value");
                distance = Math.Min(value, maximumDistance);
            }
        }
        public double MaximumDistance { get { return maximumDistance; } }
        public BodyChunkConnectionType Type;
        public double Elasticity;
        public double WeightSymmetry;

        public void SetMaximumDistance(double value)
        {
            if (value <= 0.0 || double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException("value");
            maximumDistance = value;
            if (distance > maximumDistance) distance = maximumDistance;
        }

        public void ClearMaximumDistance()
        {
            maximumDistance = double.PositiveInfinity;
        }

        public void Solve()
        {
            Vec2 delta = Second.Position - First.Position;
            double currentDistance = delta.Length;
            if (currentDistance < 0.00001)
            {
                delta = Vec2.Down;
                currentDistance = 1.0;
            }

            double error = currentDistance - Distance;
            if ((Type == BodyChunkConnectionType.Pull && error <= 0.0) ||
                (Type == BodyChunkConnectionType.Push && error >= 0.0))
            {
                return;
            }

            Vec2 correction = delta / currentDistance * (error * Elasticity);
            double firstShare = MathUtil.Clamp01(WeightSymmetry);
            double secondShare = 1.0 - firstShare;
            First.Position += correction * firstShare;
            Second.Position -= correction * secondShare;
            First.Velocity += correction * firstShare;
            Second.Velocity -= correction * secondShare;
        }
    }
}
