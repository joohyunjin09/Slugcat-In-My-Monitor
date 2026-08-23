using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;

namespace RainWorldDesktopPet.Physics
{
    public enum DesktopPetImpactResult
    {
        None,
        Stun,
        MaximumStun
    }

    // One resolved BodyChunk/terrain collision. ImpactDirection mirrors the
    // IntVector2 passed to Rain World's TerrainImpact (therefore floor=-Y),
    // while CollisionNormal is expressed in desktop y-down simulation space.
    public sealed class TerrainImpactData
    {
        public int BodyChunkIndex;
        public Vec2 PreImpactVelocity;
        public Vec2 PostImpactVelocity;
        public Vec2 ImpactDirection;
        public Vec2 CollisionNormal;
        public double ImpactSpeed;
        public long SurfaceId;
        public DesktopSurfaceKind SurfaceKind;
        public string SurfaceLabel = string.Empty;
        public bool FirstContact;
        public bool TerrainImpactTriggered;
        public int CalculatedStun;
        public int AppliedStun;
        public int FinalStunCounter;
        public bool WasOriginallyLethal;
        public bool SafetyOverrideApplied;
        public DesktopPetImpactResult DesktopResult;
        public long ImpactStunDeadlineTick;
        public bool CausedDeath;

        public void Reset()
        {
            BodyChunkIndex = 0;
            PreImpactVelocity = Vec2.Zero;
            PostImpactVelocity = Vec2.Zero;
            ImpactDirection = Vec2.Zero;
            CollisionNormal = Vec2.Zero;
            ImpactSpeed = 0.0;
            SurfaceId = 0;
            SurfaceKind = DesktopSurfaceKind.ScreenEdge;
            SurfaceLabel = string.Empty;
            FirstContact = false;
            TerrainImpactTriggered = false;
            CalculatedStun = 0;
            AppliedStun = 0;
            FinalStunCounter = 0;
            WasOriginallyLethal = false;
            SafetyOverrideApplied = false;
            DesktopResult = DesktopPetImpactResult.None;
            ImpactStunDeadlineTick = -1;
            CausedDeath = false;
        }

        public void CopyFrom(TerrainImpactData other)
        {
            BodyChunkIndex = other.BodyChunkIndex;
            PreImpactVelocity = other.PreImpactVelocity;
            PostImpactVelocity = other.PostImpactVelocity;
            ImpactDirection = other.ImpactDirection;
            CollisionNormal = other.CollisionNormal;
            ImpactSpeed = other.ImpactSpeed;
            SurfaceId = other.SurfaceId;
            SurfaceKind = other.SurfaceKind;
            SurfaceLabel = other.SurfaceLabel;
            FirstContact = other.FirstContact;
            TerrainImpactTriggered = other.TerrainImpactTriggered;
            CalculatedStun = other.CalculatedStun;
            AppliedStun = other.AppliedStun;
            FinalStunCounter = other.FinalStunCounter;
            WasOriginallyLethal = other.WasOriginallyLethal;
            SafetyOverrideApplied = other.SafetyOverrideApplied;
            DesktopResult = other.DesktopResult;
            ImpactStunDeadlineTick = other.ImpactStunDeadlineTick;
            CausedDeath = other.CausedDeath;
        }
    }
}
