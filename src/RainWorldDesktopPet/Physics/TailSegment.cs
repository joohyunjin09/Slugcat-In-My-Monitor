using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Physics
{
    public sealed class TailSegment
    {
        public TailSegment(Vec2 position, double radius, double length, double affectPrevious)
        {
            Position = position;
            LastPosition = position;
            Velocity = Vec2.Zero;
            Radius = radius;
            Length = length;
            AffectPrevious = affectPrevious;
            Stretched = 1.0;
            LastStretched = 1.0;
        }

        public Vec2 Position;
        public Vec2 LastPosition;
        public Vec2 Velocity;
        public readonly double Radius;
        public readonly double Length;
        public readonly double AffectPrevious;
        public double Stretched;
        public double LastStretched;

        public void BeginUpdate()
        {
            LastPosition = Position;
            LastStretched = Stretched;
            Position += Velocity;
            Stretched = 1.0;
        }

        public void ConstrainTo(Vec2 connectedPoint, TailSegment connectedSegment)
        {
            Vec2 delta = connectedPoint - Position;
            double distance = delta.Length;
            if (distance <= Length || distance < 0.000001) return;

            Vec2 direction = delta / distance;
            double excess = distance - Length;
            if (connectedSegment == null)
            {
                Vec2 correction = direction * excess;
                Position += correction;
                Velocity += correction;
            }
            else
            {
                Vec2 currentCorrection = direction * (excess * (1.0 - AffectPrevious));
                Vec2 previousCorrection = direction * (excess * AffectPrevious);
                Position += currentCorrection;
                Velocity += currentCorrection;
                connectedSegment.Position -= previousCorrection;
                connectedSegment.Velocity -= previousCorrection;
            }

            // TailSegment.StretchedRad uses this value while the mesh is drawn.
            Stretched = MathUtil.Clamp((Length / (distance * 0.5) + 2.0) / 3.0, 0.2, 1.0);
        }

        public void ApplyEnvironment(double damping, double gravity)
        {
            Velocity *= damping;
            Velocity.Y += gravity;
        }

        public Vec2 RenderPosition(double interpolation)
        {
            return Vec2.Lerp(LastPosition, Position, interpolation);
        }
    }
}
