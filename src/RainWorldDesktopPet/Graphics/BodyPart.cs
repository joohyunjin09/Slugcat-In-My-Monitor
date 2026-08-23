using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Graphics
{
    public class BodyPart
    {
        public BodyPart(Vec2 position, double radius)
            : this(position, radius, 0.8, 0.99)
        {
        }

        public BodyPart(Vec2 position, double radius, double surfaceFriction, double airFriction)
        {
            Position = position;
            LastPosition = position;
            Velocity = Vec2.Zero;
            Radius = radius;
            SurfaceFriction = surfaceFriction;
            AirFriction = airFriction;
        }

        public Vec2 Position;
        public Vec2 LastPosition;
        public Vec2 Velocity;
        public readonly double Radius;
        public readonly double SurfaceFriction;
        public readonly double AirFriction;

        // GenericBodyPart.Update: snapshot first, integrate once, then apply air
        // friction. Draw code only interpolates these two snapshots.
        public void Update()
        {
            LastPosition = Position;
            Position += Velocity;
            Velocity *= AirFriction;
        }

        // BodyPart.ConnectToPoint ported from Assembly-CSharp.dll. The formula
        // is coordinate-system agnostic; desktop Y simply points down.
        public void ConnectToPoint(Vec2 point, double connectionRadius, bool push,
            double elasticMovement, Vec2 hostVelocity, double adaptVelocity,
            double exaggerateVelocity)
        {
            if (elasticMovement > 0.0)
            {
                Velocity += (point - Position).Normalized *
                    (Vec2.Distance(Position, point) * elasticMovement);
            }

            Velocity += hostVelocity * exaggerateVelocity;
            double distance = Vec2.Distance(Position, point);
            if (push || distance >= connectionRadius)
            {
                Vec2 correction = (point - Position).Normalized * (connectionRadius - distance);
                Position -= correction;
                Velocity -= correction;
            }

            Velocity -= hostVelocity;
            Velocity *= 1.0 - adaptVelocity;
            Velocity += hostVelocity;
        }

        public void Step(Vec2 connection, Vec2 target, double maximumLength, double spring, double damping, double gravity)
        {
            LastPosition = Position;
            Velocity *= damping;
            Velocity += (target - Position) * spring;
            Velocity.Y += gravity;
            Position += Velocity;

            Vec2 fromConnection = Position - connection;
            double distance = fromConnection.Length;
            if (distance > maximumLength && distance > 0.0001)
            {
                Position = connection + fromConnection / distance * maximumLength;
                Velocity *= 0.65;
            }
        }

        public Vec2 RenderPosition(double interpolation)
        {
            return Vec2.Lerp(LastPosition, Position, interpolation);
        }

        public void Translate(Vec2 delta)
        {
            Position += delta;
            LastPosition += delta;
        }
    }
}
