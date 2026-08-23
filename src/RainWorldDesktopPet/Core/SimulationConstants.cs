namespace RainWorldDesktopPet.Core
{
    // Values in this block are intentionally expressed per Rain World logic tick.
    // Their provenance and desktop adaptations are recorded in docs/RainWorldBehaviorMap.md.
    public static class SimulationConstants
    {
        public const double LogicTicksPerSecond = 40.0;
        public const double LogicStepSeconds = 1.0 / LogicTicksPerSecond;
        public const double CharacterRenderScale = 2.20;

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
        public const double Bounce = 0.1;
        public const double MaximumVelocity = 35.0;

        public const int ConstraintIterations = 1;
        public const int TailSegmentCount = 4;
        public const double WindowRefreshSeconds = 0.25;
        // Standing on a window closer than this to the work-area ceiling would
        // place the body and head outside the visible desktop.
        public const double VisibleWindowTopClearance = 32.0;
        public const double RecoveryMargin = 240.0;
    }
}
