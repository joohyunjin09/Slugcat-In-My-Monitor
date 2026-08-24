using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Physics
{
    public enum DesktopSpearMode
    {
        Held,
        Thrown,
        Stuck,
        Free
    }

    public sealed class DesktopSpear
    {
        public DesktopSpear(Vec2 position)
            : this(position, 0)
        {
        }

        public DesktopSpear(Vec2 position, int needleType)
        {
            Chunk = new BodyChunk(0, position, 5.0, 0.07);
            Mode = DesktopSpearMode.Held;
            Rotation = Vec2.Right;
            NeedleType = needleType < 0 ? 0 : (needleType > 2 ? 2 : needleType);
        }

        public readonly BodyChunk Chunk;
        public DesktopSpearMode Mode { get; private set; }
        public Vec2 Rotation { get; private set; }
        public int NeedleType { get; private set; }
        public long StuckSurfaceId { get; private set; }
        public int Age { get; private set; }
        public string LastImpactSound { get; private set; }

        public void SetCreationVelocity(Vec2 velocity)
        {
            Chunk.Velocity = velocity;
        }

        public void HoldAt(Vec2 position, Vec2 direction)
        {
            Mode = DesktopSpearMode.Held;
            Chunk.LastPosition = Chunk.Position;
            Chunk.Position = position;
            Chunk.Velocity = Vec2.Zero;
            if (direction.LengthSquared > 0.001) Rotation = direction.Normalized;
        }

        public void Throw(Vec2 velocity)
        {
            Mode = DesktopSpearMode.Thrown;
            Chunk.Velocity = velocity;
            if (velocity.LengthSquared > 0.001) Rotation = velocity.Normalized;
            Age = 0;
            LastImpactSound = null;
        }

        public bool Step(DesktopCollisionWorld world)
        {
            if (Mode != DesktopSpearMode.Thrown && Mode != DesktopSpearMode.Free)
                return false;
            Age++;
            Chunk.BeginTick();
            // PhysicalObject gravity (.9) plus Spear.Update's thrown-only .45.
            Chunk.Integrate(Mode == DesktopSpearMode.Thrown ? 1.35 : 0.9, 0.999);
            Vec2 preImpactVelocity = Chunk.Velocity;
            world.Resolve(Chunk, world.CurrentSnapshot, 0, 0.4);
            if (Chunk.ContactFloor || Chunk.ContactLeft || Chunk.ContactRight)
            {
                bool alignedWall = Mode == DesktopSpearMode.Thrown &&
                    ((Chunk.ContactRight && Rotation.X > 0.5) ||
                    (Chunk.ContactLeft && Rotation.X < -0.5));
                bool alignedFloor = Mode == DesktopSpearMode.Thrown &&
                    Chunk.ContactFloor && Rotation.Y > 0.5;
                if (alignedWall || alignedFloor)
                {
                    Mode = DesktopSpearMode.Stuck;
                    Chunk.Velocity = Vec2.Zero;
                    StuckSurfaceId = Chunk.SupportingSurfaceId != 0
                        ? Chunk.SupportingSurfaceId : Chunk.WallSurfaceId;
                    LastImpactSound = alignedFloor
                        ? "Spear_Stick_In_Ground" : "Spear_Stick_In_Wall";
                }
                else
                {
                    Mode = DesktopSpearMode.Free;
                    if (Chunk.ContactFloor) Chunk.Velocity.Y = -preImpactVelocity.Y * 0.4;
                    if (Chunk.ContactLeft || Chunk.ContactRight)
                        Chunk.Velocity.X = -preImpactVelocity.X * 0.4;
                    LastImpactSound = "Spear_Bounce_Off_Wall";
                }
                return true;
            }
            return false;
        }
    }
}
