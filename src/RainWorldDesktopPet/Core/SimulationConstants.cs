namespace RainWorldDesktopPet.Core
{
    // Values in this block are intentionally expressed per Rain World logic tick.
    // Their provenance and desktop adaptations are recorded in docs/RainWorldBehaviorMap.md.
    public static class SimulationConstants
    {
        public const double LogicTicksPerSecond = 40.0;
        public const double LogicStepSeconds = 1.0 / LogicTicksPerSecond;
        public const double DesktopWorldScale = 2.20;
        public const double CharacterRenderScale = DesktopWorldScale;

        public const double MainChunkRadius = 9.0;
        // Player::.ctor assigns half of (0.7 * bodyWeightFac) to each chunk.
        public const double MainChunkMass = 0.35;
        public const double HipsChunkRadius = 8.0;
        public const double HipsChunkMass = 0.35;
        public const double BodyConnectionDistance = 17.0;
        public const double BodyConnectionElasticity = 1.0;
        public const double BodyConnectionSymmetry = 0.5;

        public const double GravityPerTick = 0.9;
        public const double AirFriction = 0.999;
        public const double SurfaceFriction = 0.5;
        public const double UnconsciousSurfaceFriction = 0.3;
        public const double Bounce = 0.1;
        // PhysicalObject.impactTreshhold defaults to one Rain World unit/tick.
        public const double ImpactThreshold = 1.0;
        // Desktop-pet safety policy. Original TerrainImpact severity is kept,
        // but terrain alone may never hold the pet unconscious beyond this
        // absolute recovery horizon.
        public const double MaxImpactStunDurationSeconds = 3.0;
        public static readonly int MaxImpactStunTicks =
            (int)(MaxImpactStunDurationSeconds * LogicTicksPerSecond + 0.5);

        public const int ConstraintIterations = 1;
        public const int TailSegmentCount = 4;
        // Expressed in Rain World simulation units. The desktop distance is
        // multiplied by DesktopWorldScale at the input/output boundary.
        public const double MouseAttentionRadius = 90.0;
        public const double MouseAttentionTimeoutSeconds = 1.5;
        public const double WindowRefreshSeconds = 0.25;
        // Window tops are dynamic desktop terrain. Give their collision slab
        // enough depth to catch a supported BodyChunk when the HWND rises
        // between two 40 Hz physics ticks without turning the whole window
        // movement into a direct Slugcat translation.
        public const int WindowPlatformThicknessDesktopPixels = 32;
        // Standing on a window closer than this to the work-area ceiling would
        // place the body and head outside the visible desktop.
        public const double VisibleWindowTopClearance = 32.0;
        public const int MissingWindowRefreshGrace = 2;
    }
}
