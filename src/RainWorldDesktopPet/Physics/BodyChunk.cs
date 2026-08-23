using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;

namespace RainWorldDesktopPet.Physics
{
    public sealed class BodyChunk
    {
        public BodyChunk(int index, Vec2 position, double radius, double mass)
        {
            Index = index;
            Position = position;
            LastPosition = position;
            Velocity = Vec2.Zero;
            Radius = radius;
            Mass = mass;
        }

        public int Index { get; private set; }
        public Vec2 Position;
        public Vec2 LastPosition;
        public Vec2 Velocity;
        public readonly double Radius;
        public double Mass { get; private set; }
        public bool ContactFloor;
        public bool ContactLeft;
        public bool ContactRight;
        public long SupportingSurfaceId;
        public long WallSurfaceId;
        public DesktopSurfaceKind WallSurfaceKind;
        public double FloorImpactSpeed;

        public void SetMass(double mass)
        {
            Mass = mass;
        }

        public void BeginTick()
        {
            LastPosition = Position;
            ContactFloor = false;
            ContactLeft = false;
            ContactRight = false;
            SupportingSurfaceId = 0;
            WallSurfaceId = 0;
            WallSurfaceKind = DesktopSurfaceKind.ScreenEdge;
            FloorImpactSpeed = 0.0;
        }

        public void Integrate(double gravity, double airFriction)
        {
            Velocity.Y += gravity;
            Velocity *= airFriction;
            Velocity = Vec2.ClampMagnitude(Velocity, SimulationConstants.MaximumVelocity);
            Position += Velocity;
        }

        public Vec2 RenderPosition(double interpolation)
        {
            return Vec2.Lerp(LastPosition, Position, interpolation);
        }
    }
}
