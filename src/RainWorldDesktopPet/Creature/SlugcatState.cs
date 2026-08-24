namespace RainWorldDesktopPet.Creature
{
    // Retail Rain World v1.11.8 Player.AnimationIndex. Sit and Sleep are the
    // only desktop-only values and deliberately live after the original map.
    public enum AnimationIndex
    {
        None,
        CrawlTurn,
        StandUp,
        DownOnFours,
        LedgeCrawl,
        LedgeGrab,
        HangFromBeam,
        GetUpOnBeam,
        StandOnBeam,
        ClimbOnBeam,
        GetUpToBeamTip,
        HangUnderVerticalBeam,
        BeamTip,
        CorridorTurn,
        SurfaceSwim,
        DeepSwim,
        Roll,
        Flip,
        RocketJump,
        BellySlide,
        AntlerClimb,
        GrapplingSwing,
        ZeroGSwim,
        ZeroGPoleGrab,
        VineGrab,
        Dead,
        Sit,
        Sleep
    }

    // Retail Rain World v1.11.8 Player.BodyModeIndex.
    public enum BodyModeIndex
    {
        Default,
        Crawl,
        Stand,
        CorridorClimb,
        ClimbIntoShortCut,
        WallClimb,
        ClimbingOnBeam,
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
        public double Adrenaline;
        public bool Conscious = true;
        public bool Dead;
        // Player.standing is an intent/state flag, not a BodyMode-derived pose.
        public bool Standing = true;
        public int StunCounter;
        public int SlowMovementStun;
        public int InitialStunValue;
        public int ImpactBlinkTicks;

        // Names and counter semantics mirror Player's connected movement state.
        public int WantToJump;
        public int CanJump;
        public int CanWallJump;
        public int JumpStun;
        public int SuperLaunchJump;
        public int KillSuperLaunchJumpCounter;
        public int WallSlideCounter;
        public int AllowRoll;
        public int RollDirection;
        public int RollCounter;
        public int SlideCounter;
        public int SlideDirection;
        public int InitSlideCounter;
        public int BackwardsCounter;
        public int LandingDelay;
        public int CrawlTurnDelay;
        public int ExitBellySlideCounter;
        public int StopRollingCounter;
        public int ConsistentDownDiagonal;
        public int LowerBodyFramesOnGround;
        public int UpperBodyFramesOffGround;
        // Player.flipFromSlide selects the 2.5x angular impulse used only by
        // the belly-slide reversal. A standing backflip uses the base impulse.
        public bool FlipFromSlide;

        // Creature.Stunned is deliberately not equivalent to stun > 0.
        // Rain World considers a creature unconscious only while stun >= 10.
        public bool IsStunned { get { return StunCounter >= 10; } }
    }
}
