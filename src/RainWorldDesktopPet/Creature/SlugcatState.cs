namespace RainWorldDesktopPet.Creature
{
    // Names mirror the portions of Player.AnimationIndex and BodyModeIndex that
    // have a meaningful desktop equivalent. The complete source enum map is in docs.
    public enum AnimationIndex
    {
        None,
        StandUp,
        DownOnFours,
        CrawlTurn,
        LedgeCrawl,
        HangFromBeam,
        ClimbOnBeam,
        WallClimb,
        Roll,
        Flip,
        BellySlide,
        Sit,
        Sleep,
        Dead
    }

    public enum BodyModeIndex
    {
        Default,
        Stand,
        Crawl,
        ClimbingOnBeam,
        CorridorClimb,
        WallClimb,
        Swimming,
        ZeroG,
        Stunned,
        Dead
    }

    public sealed class SlugcatState
    {
        public AnimationIndex Animation = AnimationIndex.None;
        public BodyModeIndex BodyMode = BodyModeIndex.Default;
        public int AnimationFrame;
        public int Facing = 1;
        public bool Grounded;
        public bool JustLanded;
        public double LandingCompression;
        public double RunCycle;
        public double Stillness;
        public double AerobicLevel;
        public bool Conscious = true;
        public bool Dead;
        public bool Standing;
        public int StunCounter;
        public int InitialStunValue;
        public int ImpactBlinkTicks;

        // Creature.Stunned is deliberately not equivalent to stun > 0.
        // Rain World considers a creature unconscious only while stun >= 10.
        public bool IsStunned { get { return StunCounter >= 10; } }
    }
}
