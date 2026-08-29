using System;
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
            TerrainImpacts = new TerrainImpactData[4];
            for (int i = 0; i < TerrainImpacts.Length; i++)
                TerrainImpacts[i] = new TerrainImpactData();
        }

        public int Index { get; private set; }
        public Vec2 Position;
        public Vec2 LastPosition;
        public Vec2 Velocity;
        public double Radius { get; private set; }
        public double Mass { get; private set; }
        public bool ContactFloor;
        public bool ContactLeft;
        public bool ContactRight;
        public long SupportingSurfaceId;
        public DesktopSurfaceKind SupportingSurfaceKind;
        public long WallSurfaceId;
        public DesktopSurfaceKind WallSurfaceKind;
        public double FloorImpactSpeed;
        public bool PreviousContactFloor;
        public bool PreviousContactLeft;
        public bool PreviousContactRight;
        public long PreviousSupportingSurfaceId;
        public DesktopSurfaceKind PreviousSupportingSurfaceKind;
        public long PreviousWallSurfaceId;
        public DesktopSurfaceKind PreviousWallSurfaceKind;
        public long CollisionSnapshotVersion;
        public readonly TerrainImpactData[] TerrainImpacts;
        public int TerrainImpactCount;

        public void SetMass(double mass)
        {
            Mass = mass;
        }

        public void SetRadius(double radius)
        {
            if (radius <= 0.0 || double.IsNaN(radius) || double.IsInfinity(radius))
                throw new ArgumentOutOfRangeException("radius");
            Radius = radius;
        }

        public void BeginTick()
        {
            LastPosition = Position;
            PreviousContactFloor = ContactFloor;
            PreviousContactLeft = ContactLeft;
            PreviousContactRight = ContactRight;
            PreviousSupportingSurfaceId = SupportingSurfaceId;
            PreviousSupportingSurfaceKind = SupportingSurfaceKind;
            PreviousWallSurfaceId = WallSurfaceId;
            PreviousWallSurfaceKind = WallSurfaceKind;
            ContactFloor = false;
            ContactLeft = false;
            ContactRight = false;
            SupportingSurfaceId = 0;
            SupportingSurfaceKind = DesktopSurfaceKind.ScreenEdge;
            WallSurfaceId = 0;
            WallSurfaceKind = DesktopSurfaceKind.ScreenEdge;
            FloorImpactSpeed = 0.0;
            TerrainImpactCount = 0;
            for (int i = 0; i < TerrainImpacts.Length; i++) TerrainImpacts[i].Reset();
        }

        public void Integrate(double gravity, double airFriction)
        {
            Integrate(gravity, airFriction, 1.0);
        }

        public void Integrate(double gravity, double airFriction,
            double movementScale)
        {
            if (movementScale <= 0.0 || double.IsNaN(movementScale) ||
                double.IsInfinity(movementScale))
                throw new ArgumentOutOfRangeException("movementScale");
            Velocity.Y += gravity;
            Velocity *= airFriction;
            // Scale locomotion distance while retaining the original vertical
            // gravity, stance, jump, and collision cadence.
            Position += new Vec2(Velocity.X * movementScale, Velocity.Y);
        }

        public Vec2 RenderPosition(double interpolation)
        {
            return Vec2.Lerp(LastPosition, Position, interpolation);
        }

        public TerrainImpactData RecordTerrainCollision(DesktopSurface surface,
            Vec2 preImpactVelocity, Vec2 impactDirection, Vec2 collisionNormal,
            double impactSpeed, bool firstContact)
        {
            int slot = TerrainImpactCount < TerrainImpacts.Length
                ? TerrainImpactCount++
                : TerrainImpacts.Length - 1;
            TerrainImpactData impact = TerrainImpacts[slot];
            impact.Reset();
            impact.BodyChunkIndex = Index;
            impact.PreImpactVelocity = preImpactVelocity;
            impact.ImpactDirection = impactDirection;
            impact.CollisionNormal = collisionNormal;
            impact.ImpactSpeed = impactSpeed;
            impact.SurfaceId = surface.Id;
            impact.SurfaceKind = surface.Kind;
            impact.SurfaceLabel = surface.Label;
            impact.FirstContact = firstContact;
            return impact;
        }
    }
}
