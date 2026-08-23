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
        PreJump,
        Jump,
        Fall,
        Land,
        Sit,
        Sleep
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
        ZeroG
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
    }
}
