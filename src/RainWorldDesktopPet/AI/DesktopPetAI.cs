using System;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Physics;

namespace RainWorldDesktopPet.AI
{
    public enum DesktopBehavior
    {
        Idle,
        Walk,
        Run,
        Crawl,
        Explore,
        Sit,
        Sleep,
        LookAround,
        FollowMouse,
        AvoidMouse,
        Jump,
        ClimbWindow,
        DropDown,
        BalanceNearEdge,
        ObserveWindow,
        Play,
        TurnAround,
        ExplosiveJump,
        MakeSpear,
        ThrowSpear,
        TongueSwing,
        GourmandRoll
    }

    public enum PlatformTransitionMode
    {
        None,
        StandardJump,
        ExplosiveJump,
        TongueSwing
    }

    public enum AIDestinationKind
    {
        None,
        Nearby,
        FarSide,
        Center,
        Edge,
        Unvisited,
        MouseNear,
        MouseAway,
        Wander
    }

    public enum MicroBehavior
    {
        None,
        BriefPause,
        LookOpposite,
        BriefCrouch,
        MouseGlance,
        Hesitate,
        FakeJump
    }

    public sealed class PlatformTransitionPlan
    {
        public PlatformTransitionMode Mode;
        public long SourceSurfaceId;
        public long TargetSurfaceId;
        public Vec2 TargetPoint;
        public double HorizontalDistance;
        public double VerticalDistance;
        public bool IsValid;

        public void Clear()
        {
            Mode = PlatformTransitionMode.None;
            SourceSurfaceId = 0;
            TargetSurfaceId = 0;
            TargetPoint = Vec2.Zero;
            HorizontalDistance = 0.0;
            VerticalDistance = 0.0;
            IsValid = false;
        }
    }

    public sealed class DesktopPetAI
    {
        private readonly Random random;
        private int evaluationCountdown;
        private int behaviorTicks;
        private int desiredDirection = 1;
        private long explorationSurfaceId;
        private double explorationTargetX;
        private int explorationTargetTicks;
        private int explorationJumpCooldownTicks;
        private bool explorationJumpRequested;
        private int freeRoamRetargetCountdown;
        private double movementUrge;
        private int obstacleJumpAttemptTicks;
        private int obstacleJumpDirection;
        private bool obstacleJumpWasAirborne;
        private double fatigue = 0.18;
        private double curiosity = 0.65;
        private int jumpCooldownTicks;
        private int dropCooldownTicks;
        private int restCooldownTicks;
        private int wallContactGraceTicks;
        private int lastWallDirection = 1;
        private readonly double personalityEnergy;
        private readonly double personalityNervous;
        private readonly double personalityAggression;
        private readonly double personalityBravery;
        private readonly double personalityDominance;
        // Persistent per-instance modifiers. These are rolled once and remain
        // stable for the lifetime of this AI; they are never rerolled per update.
        private readonly double modifierActivity;
        private readonly double modifierCuriosity;
        private readonly double modifierPlayfulness;
        private readonly double modifierRestlessness;
        private readonly double modifierAttention;
        private readonly double modifierConfidence;
        private readonly double modifierPatience;

        // Effective character traits = Slugcat archetype + original SlugNPCAI-like
        // personality + the stable per-instance modifiers above.
        private SlugcatId aiSlugcatId;
        private bool characterProfileInitialized;
        private double traitActivity;
        private double traitCuriosity;
        private double traitPlayfulness;
        private double traitRestlessness;
        private double traitAttention;
        private double traitConfidence;
        private double traitPatience;
        private double traitImpulsiveness;
        private double traitRest;
        private double traitObservation;
        private double traitDirectionPersistence;
        private double traitMouseInterest;
        private double traitMouseApproach;
        private double traitSpecialUse;

        // Continuously evolving needs. Randomness only perturbs decisions; these
        // values provide the long-term cause for behavioral changes.
        private double activity = 0.5;
        private double boredom = 0.2;
        private double restlessness = 0.35;
        private double sleepiness = 0.2;
        private double playfulness = 0.4;
        private double attentionDrive = 0.5;
        private double confidence = 0.5;

        private AIMood mood = AIMood.Calm;
        private int moodTicksRemaining;

        // Intent timing is deliberately variable. The utility system can abandon
        // an intent early, but patient personalities resist interruption.
        private int intentPreferredTicks = 80;
        private int intentMinimumTicks = 16;
        private int intentHardLimitTicks = 240;
        private int jumpHoldTicks = 5;

        // Recent-action ring buffer prevents obvious A-B-A-B and same-action loops
        // without hard-banning a behavior.
        private const int RecentBehaviorCapacity = 8;
        private readonly DesktopBehavior[] recentBehaviors =
            new DesktopBehavior[RecentBehaviorCapacity];
        private int recentBehaviorCount;
        private int recentBehaviorCursor;

        // No Enum.GetValues or List allocation during utility evaluation.
        private static readonly DesktopBehavior[] UtilityCandidates =
        {
            DesktopBehavior.Idle,
            DesktopBehavior.Walk,
            DesktopBehavior.Run,
            DesktopBehavior.Crawl,
            DesktopBehavior.Explore,
            DesktopBehavior.Sit,
            DesktopBehavior.Sleep,
            DesktopBehavior.LookAround,
            DesktopBehavior.FollowMouse,
            DesktopBehavior.AvoidMouse,
            DesktopBehavior.Jump,
            DesktopBehavior.ClimbWindow,
            DesktopBehavior.DropDown,
            DesktopBehavior.BalanceNearEdge,
            DesktopBehavior.ObserveWindow,
            DesktopBehavior.Play,
            DesktopBehavior.TurnAround
        };
        private readonly double[] utilityScores =
            new double[UtilityCandidates.Length];
        private readonly UtilityContext utilityContext = new UtilityContext();
        private DesktopBehavior topUtility1;
        private DesktopBehavior topUtility2;
        private DesktopBehavior topUtility3;
        private double topUtilityScore1;
        private double topUtilityScore2;
        private double topUtilityScore3;

        private AIDestinationKind destinationKind;
        private Vec2 intentTarget;
        private readonly double[] recentTargetX = new double[4];
        private int recentTargetCount;
        private int recentTargetCursor;

        private MicroBehavior microBehavior;
        private int microTicksRemaining;
        private int microCooldownTicks;

        private int directionCommitmentTicks;
        private readonly int routePreferenceSalt;
        private int routeMemoryTicks;
        private long recentTransitionSourceSurfaceId;
        private long recentTransitionTargetSurfaceId;
        private int attentionRetargetCountdown;
        private bool specialTransitionArmed;
        private bool specialTransitionFreestyle;
        private long specialTransitionTargetSurfaceId;
        private int artificerFreestyleCooldownTicks;
        private bool saintTransitionArmed;
        private bool saintTransitionFreestyle;
        private bool saintAwaitingJumpRelease;
        private int saintAttachedTicks;
        private int saintAttachDurationTicks = 70;
        private int saintFreestyleCooldownTicks;
        private int saintTongueIntentCountdownTicks;
        private bool originalAttentionInitialized;
        private AttentionKind originalAttentionKind;
        private Vec2 originalAttentionTarget;
        private SpearmasterActionState spearmasterState;
        private int spearmasterStateTicks;
        private int spearmasterIdleDuration = 120;
        private int spearmasterMoveDuration = 35;
        private int spearmasterRecoveryDuration = 110;
        private int spearmasterAutonomousThrowCountdown;
        private bool spearmasterAutonomousThrow;
        private Vec2 spearmasterTarget;

        private sealed class CharacterAIProfile
        {
            public CharacterAIProfile(double activityValue, double curiosityValue,
                double playfulnessValue, double restlessnessValue, double attentionValue,
                double confidenceValue, double patienceValue, double impulsivenessValue,
                double restValue, double observationValue, double directionPersistenceValue,
                double mouseInterestValue, double mouseApproachValue, double specialUseValue)
            {
                Activity = activityValue; Curiosity = curiosityValue;
                Playfulness = playfulnessValue; Restlessness = restlessnessValue;
                Attention = attentionValue; Confidence = confidenceValue;
                Patience = patienceValue; Impulsiveness = impulsivenessValue;
                Rest = restValue; Observation = observationValue;
                DirectionPersistence = directionPersistenceValue;
                MouseInterest = mouseInterestValue; MouseApproach = mouseApproachValue;
                SpecialUse = specialUseValue;
            }
            public readonly double Activity, Curiosity, Playfulness, Restlessness;
            public readonly double Attention, Confidence, Patience, Impulsiveness;
            public readonly double Rest, Observation, DirectionPersistence;
            public readonly double MouseInterest, MouseApproach, SpecialUse;
        }

        private static readonly CharacterAIProfile WhiteAI = new CharacterAIProfile(
            0.55, 0.55, 0.46, 0.48, 0.52, 0.56, 0.55, 0.45, 0.50, 0.50, 0.55, 0.52, 0.50, 0.35);
        private static readonly CharacterAIProfile YellowAI = new CharacterAIProfile(
            0.38, 0.55, 0.35, 0.30, 0.70, 0.42, 0.72, 0.25, 0.68, 0.70, 0.62, 0.82, 0.72, 0.25);
        private static readonly CharacterAIProfile RedAI = new CharacterAIProfile(
            0.82, 0.62, 0.55, 0.78, 0.55, 0.78, 0.30, 0.70, 0.25, 0.38, 0.42, 0.45, 0.50, 0.42);
        private static readonly CharacterAIProfile GourmandAI = new CharacterAIProfile(
            0.36, 0.48, 0.32, 0.25, 0.46, 0.60, 0.82, 0.30, 0.82, 0.52, 0.82, 0.42, 0.28, 0.30);
        private static readonly CharacterAIProfile ArtificerAI = new CharacterAIProfile(
            0.84, 0.58, 0.74, 0.90, 0.48, 0.82, 0.22, 0.92, 0.20, 0.30, 0.34, 0.55, 0.55, 0.80);
        private static readonly CharacterAIProfile SpearMasterAI = new CharacterAIProfile(
            0.58, 0.66, 0.30, 0.40, 0.88, 0.68, 0.72, 0.28, 0.38, 0.88, 0.76, 0.76, 0.35, 0.46);
        private static readonly CharacterAIProfile RivuletAI = new CharacterAIProfile(
            0.94, 0.80, 0.88, 0.86, 0.55, 0.78, 0.18, 0.86, 0.22, 0.38, 0.24, 0.72, 0.62, 0.40);
        private static readonly CharacterAIProfile SaintAI = new CharacterAIProfile(
            0.34, 0.72, 0.32, 0.22, 0.90, 0.62, 0.88, 0.18, 0.64, 0.94, 0.72, 0.86, 0.18, 0.82);

        public DesktopPetAI(int seed)
            : this(seed, 0)
        {
        }

        public DesktopPetAI(int seed, int evaluationPhase)
        {
            if (evaluationPhase < 0) throw new ArgumentOutOfRangeException("evaluationPhase");
            // Multiple pets can be spawned within the same Environment.TickCount.
            // Mix the spawn/evaluation phase into the seed so they do not receive
            // identical lifetime personalities merely because they were created
            // in the same millisecond.
            int instanceSeed = unchecked(seed ^ ((evaluationPhase + 1) * 0x45D9F3B));
            random = new Random(instanceSeed);
            // SlugNPCAI reads AbstractCreature.Personality (energy, nervous
            // and aggression) instead of sharing one behavior profile among
            // every NPC.  The desktop has no AbstractCreature, so retain one
            // deterministic personality per AI instance.
            Random personalityRandom = new Random(unchecked(instanceSeed ^ 0x51A7C3D));
            personalityEnergy = personalityRandom.NextDouble();
            personalityNervous = personalityRandom.NextDouble();
            personalityAggression = personalityRandom.NextDouble();
            personalityBravery = personalityRandom.NextDouble();
            personalityDominance = personalityRandom.NextDouble();

            // ±16% 정도의 개체차. 생성 시 한 번만 결정되며 수명 동안 유지한다.
            modifierActivity = 0.84 + personalityRandom.NextDouble() * 0.32;
            modifierCuriosity = 0.84 + personalityRandom.NextDouble() * 0.32;
            modifierPlayfulness = 0.84 + personalityRandom.NextDouble() * 0.32;
            modifierRestlessness = 0.84 + personalityRandom.NextDouble() * 0.32;
            modifierAttention = 0.84 + personalityRandom.NextDouble() * 0.32;
            modifierConfidence = 0.84 + personalityRandom.NextDouble() * 0.32;
            modifierPatience = 0.84 + personalityRandom.NextDouble() * 0.32;
            routePreferenceSalt = personalityRandom.Next();

            fatigue = MathUtil.Lerp(0.30, 0.08, personalityEnergy);
            curiosity = MathUtil.Lerp(0.36, 0.86, PersonalityCuriosity());
            desiredDirection = random.Next(0, 2) == 0 ? -1 : 1;
            freeRoamRetargetCountdown = 20 + random.Next(0, 51);
            attentionRetargetCountdown = 40 + random.Next(0, 80);
            evaluationCountdown = evaluationPhase == 0 ? 0 : evaluationPhase + 1;
            Attention = new AttentionSystem();
            Behavior = DesktopBehavior.Idle;
            TransitionPlan = new PlatformTransitionPlan();
        }

        public DesktopBehavior Behavior { get; private set; }
        public AttentionSystem Attention { get; private set; }
        public UtilityContext LastContext { get; private set; }
        public AttentionKind OriginalAttentionKind { get { return originalAttentionKind; } }
        public Vec2 OriginalAttentionTarget { get { return originalAttentionTarget; } }
        public bool MouseAttentionActive { get; private set; }
        public PlatformTransitionPlan TransitionPlan { get; private set; }
        public SpearmasterActionState SpearmasterState { get { return spearmasterState; } }
        public double PersonalityEnergy { get { return personalityEnergy; } }
        public double PersonalityNervous { get { return personalityNervous; } }
        public double PersonalityAggression { get { return personalityAggression; } }
        public double PersonalityBravery { get { return personalityBravery; } }
        public double PersonalityDominance { get { return personalityDominance; } }
        public SlugcatId AISlugcatId { get { return aiSlugcatId; } }
        public AIMood CurrentMood { get { return mood; } }
        public AIDestinationKind DestinationKind { get { return destinationKind; } }
        public MicroBehavior CurrentMicroBehavior { get { return microBehavior; } }
        public Vec2 IntentTarget { get { return intentTarget; } }
        public double IntentTimeSeconds { get { return behaviorTicks * SimulationConstants.LogicStepSeconds; } }
        public double IntentPreferredSeconds { get { return intentPreferredTicks * SimulationConstants.LogicStepSeconds; } }
        public double Activity { get { return activity; } }
        public double Curiosity { get { return curiosity; } }
        public double Boredom { get { return boredom; } }
        public double Restlessness { get { return restlessness; } }
        public double Sleepiness { get { return sleepiness; } }
        public double Playfulness { get { return playfulness; } }
        public double AttentionDrive { get { return attentionDrive; } }
        public double Confidence { get { return confidence; } }
        public double TraitActivity { get { return traitActivity; } }
        public double TraitCuriosity { get { return traitCuriosity; } }
        public double TraitPlayfulness { get { return traitPlayfulness; } }
        public double TraitRestlessness { get { return traitRestlessness; } }
        public double TraitAttention { get { return traitAttention; } }
        public double TraitConfidence { get { return traitConfidence; } }
        public double TraitPatience { get { return traitPatience; } }
        public double TraitImpulsiveness { get { return traitImpulsiveness; } }
        public DesktopBehavior TopUtility1 { get { return topUtility1; } }
        public DesktopBehavior TopUtility2 { get { return topUtility2; } }
        public DesktopBehavior TopUtility3 { get { return topUtility3; } }
        public double TopUtilityScore1 { get { return topUtilityScore1; } }
        public double TopUtilityScore2 { get { return topUtilityScore2; } }
        public double TopUtilityScore3 { get { return topUtilityScore3; } }

        // Allocates only when the debug overlay explicitly requests it. Release
        // AI ticks never build this string.
        public string GetDebugSummary()
        {
            return string.Format(
                "AI type={0} mood={1} intent={2} time={3:0.00}/{4:0.00}s target={5} dest={6} micro={7}\n" +
                "needs activity={8:0.00} curiosity={9:0.00} boredom={10:0.00} restless={11:0.00} sleep={12:0.00} playful={13:0.00} attention={14:0.00} confidence={15:0.00}\n" +
                "traits activity={16:0.00} curiosity={17:0.00} playful={18:0.00} restless={19:0.00} attention={20:0.00} confidence={21:0.00} patience={22:0.00} impulse={23:0.00}\n" +
                "utility {24}={25:0.000} | {26}={27:0.000} | {28}={29:0.000}",
                aiSlugcatId, mood, Behavior, IntentTimeSeconds, IntentPreferredSeconds,
                intentTarget, destinationKind, microBehavior, activity, curiosity,
                boredom, restlessness, sleepiness, playfulness, attentionDrive,
                confidence, traitActivity, traitCuriosity, traitPlayfulness,
                traitRestlessness, traitAttention, traitConfidence, traitPatience,
                traitImpulsiveness, topUtility1, topUtilityScore1, topUtility2,
                topUtilityScore2, topUtility3, topUtilityScore3);
        }

        public VirtualInput Step(Slugcat slugcat, DesktopCollisionWorld world, MouseTracker mouse)
        {
            return Step(slugcat, world, mouse, null);
        }

        public VirtualInput Step(Slugcat slugcat, DesktopCollisionWorld world, MouseTracker mouse,
            MouseAttentionState mouseAttention)
        {
            EnsureCharacterProfile(slugcat.SelectedSlugcat.Id);

            behaviorTicks++;
            if (jumpCooldownTicks > 0) jumpCooldownTicks--;
            if (dropCooldownTicks > 0) dropCooldownTicks--;
            if (restCooldownTicks > 0) restCooldownTicks--;
            if (routeMemoryTicks > 0) routeMemoryTicks--;
            if (explorationTargetTicks > 0) explorationTargetTicks--;
            if (explorationJumpCooldownTicks > 0) explorationJumpCooldownTicks--;
            if (freeRoamRetargetCountdown > 0) freeRoamRetargetCountdown--;
            if (directionCommitmentTicks > 0) directionCommitmentTicks--;
            if (artificerFreestyleCooldownTicks > 0) artificerFreestyleCooldownTicks--;
            if (saintFreestyleCooldownTicks > 0) saintFreestyleCooldownTicks--;
            if (saintTongueIntentCountdownTicks > 0) saintTongueIntentCountdownTicks--;
            if (microCooldownTicks > 0) microCooldownTicks--;
            if (moodTicksRemaining > 0) moodTicksRemaining--;

            UtilityContext context = BuildContext(slugcat, world, mouse);
            context.MouseAttentionActive = IsMouseAttentionActive(slugcat, mouseAttention);
            UpdateNeeds(context);
            UpdateMood(context);
            PopulateDynamicContext(context);
            UpdateObstacleResponse(slugcat, world, context);

            if (--evaluationCountdown <= 0)
            {
                evaluationCountdown = EvaluationIntervalTicks();
                PlanPlatformTransition(slugcat, world);
                context.TransitionAvailable = TransitionPlan.IsValid;
                context.SpecialTraversalAvailable = IsSpecialTraversalAvailable(slugcat);
                SelectBehavior(slugcat, context);
            }
            else
            {
                context.TransitionAvailable = TransitionPlan.IsValid;
                context.SpecialTraversalAvailable = IsSpecialTraversalAvailable(slugcat);
            }

            UpdateExplorationTarget(slugcat, world, mouse, context);
            UpdateMicroBehavior(slugcat, mouse, context);
            LastContext = context;

            VirtualInput input = ProduceInput(slugcat, mouse, context);
            input = ApplyMicroBehavior(input, slugcat, context);

            // Ability controllers receive the final say. This keeps AI as an intent/input
            // layer and leaves the original movement/physics implementation untouched.
            VirtualInput abilityInput;
            if (TryProduceAbilityInput(slugcat, mouse, mouseAttention, context,
                out abilityInput)) input = abilityInput;

            UpdateAttention(slugcat, mouse, context, mouseAttention);
            Attention.Step();
            return input;
        }

        private void EnsureCharacterProfile(SlugcatId id)
        {
            if (characterProfileInitialized && aiSlugcatId == id) return;

            CharacterAIProfile profile = ProfileFor(id);
            bool first = !characterProfileInitialized;
            aiSlugcatId = id;
            characterProfileInitialized = true;

            traitActivity = EffectiveTrait(profile.Activity, modifierActivity,
                MathUtil.Lerp(0.90, 1.10, personalityEnergy));
            traitCuriosity = EffectiveTrait(profile.Curiosity, modifierCuriosity,
                MathUtil.Lerp(0.92, 1.08, PersonalityCuriosity()));
            traitPlayfulness = EffectiveTrait(profile.Playfulness, modifierPlayfulness,
                MathUtil.Lerp(0.93, 1.07, personalityEnergy));
            traitRestlessness = EffectiveTrait(profile.Restlessness, modifierRestlessness,
                MathUtil.Lerp(0.90, 1.10, personalityNervous));
            traitAttention = EffectiveTrait(profile.Attention, modifierAttention,
                MathUtil.Lerp(0.94, 1.06, 1.0 - personalityNervous));
            traitConfidence = EffectiveTrait(profile.Confidence, modifierConfidence,
                MathUtil.Lerp(0.88, 1.12, personalityBravery));
            traitPatience = EffectiveTrait(profile.Patience, modifierPatience,
                MathUtil.Lerp(1.08, 0.92, personalityNervous));
            traitImpulsiveness = MathUtil.Clamp01(profile.Impulsiveness *
                MathUtil.Lerp(0.90, 1.12, personalityAggression) *
                MathUtil.Lerp(0.94, 1.08, personalityNervous));
            traitRest = MathUtil.Clamp01(profile.Rest *
                MathUtil.Lerp(0.88, 1.10, 1.0 - personalityEnergy));
            traitObservation = MathUtil.Clamp01(profile.Observation *
                MathUtil.Lerp(0.92, 1.08, 1.0 - personalityNervous));
            traitDirectionPersistence = MathUtil.Clamp01(profile.DirectionPersistence *
                MathUtil.Lerp(0.90, 1.10, personalityDominance));
            traitMouseInterest = MathUtil.Clamp01(profile.MouseInterest *
                modifierAttention);
            traitMouseApproach = MathUtil.Clamp01(profile.MouseApproach *
                MathUtil.Lerp(1.08, 0.90, personalityNervous));
            traitSpecialUse = MathUtil.Clamp01(profile.SpecialUse *
                MathUtil.Lerp(0.92, 1.10, personalityEnergy));

            if (first)
            {
                activity = MathUtil.Lerp(0.28, 0.72, traitActivity);
                curiosity = MathUtil.Lerp(0.28, 0.76, traitCuriosity);
                boredom = MathUtil.Lerp(0.12, 0.32, 1.0 - traitPatience);
                restlessness = MathUtil.Lerp(0.20, 0.68, traitRestlessness);
                sleepiness = MathUtil.Lerp(0.08, 0.28, traitRest);
                playfulness = MathUtil.Lerp(0.22, 0.72, traitPlayfulness);
                attentionDrive = MathUtil.Lerp(0.30, 0.72, traitAttention);
                confidence = MathUtil.Lerp(0.30, 0.78, traitConfidence);
                directionCommitmentTicks = TravelCommitmentTicks();
            }
            else
            {
                // Runtime character switches keep the current mood/needs mostly intact,
                // but gently move them toward the newly selected archetype.
                activity = MathUtil.Lerp(activity, traitActivity, 0.25);
                curiosity = MathUtil.Lerp(curiosity, traitCuriosity, 0.20);
                restlessness = MathUtil.Lerp(restlessness, traitRestlessness, 0.25);
                playfulness = MathUtil.Lerp(playfulness, traitPlayfulness, 0.20);
                confidence = MathUtil.Lerp(confidence, traitConfidence, 0.20);
            }

            moodTicksRemaining = 0;
            evaluationCountdown = 1;
            freeRoamRetargetCountdown = 0;
            destinationKind = AIDestinationKind.None;
            saintTongueIntentCountdownTicks = id == SlugcatId.Saint
                ? SecondsToTicks(SampleCentered(3.0, 7.0)) : 0;
        }

        private static CharacterAIProfile ProfileFor(SlugcatId id)
        {
            switch (id)
            {
                case SlugcatId.Yellow: return YellowAI;
                case SlugcatId.Red: return RedAI;
                case SlugcatId.Gourmand: return GourmandAI;
                case SlugcatId.Artificer: return ArtificerAI;
                case SlugcatId.SpearMaster: return SpearMasterAI;
                case SlugcatId.Rivulet: return RivuletAI;
                case SlugcatId.Saint: return SaintAI;
                default: return WhiteAI;
            }
        }

        private static double EffectiveTrait(double baseValue, double modifier,
            double personalityFactor)
        {
            return MathUtil.Clamp01(baseValue * modifier * personalityFactor);
        }

        private void UpdateNeeds(UtilityContext context)
        {
            double dt = SimulationConstants.LogicStepSeconds;
            bool moving = IsLocomotionBehavior(Behavior);
            bool stimulating = Behavior == DesktopBehavior.Explore ||
                Behavior == DesktopBehavior.Jump || Behavior == DesktopBehavior.Play ||
                Behavior == DesktopBehavior.FollowMouse ||
                Behavior == DesktopBehavior.ExplosiveJump ||
                Behavior == DesktopBehavior.TongueSwing;
            bool resting = Behavior == DesktopBehavior.Idle ||
                Behavior == DesktopBehavior.Sit || Behavior == DesktopBehavior.Sleep;

            double moodActivity = mood == AIMood.Energetic ? 0.20 :
                (mood == AIMood.Lazy ? -0.24 : (mood == AIMood.Restless ? 0.12 : 0.0));
            double activityTarget = MathUtil.Clamp01(traitActivity + moodActivity -
                sleepiness * 0.32 + restlessness * 0.18);
            activity = MathUtil.MoveTowards(activity, activityTarget,
                dt * (0.045 + traitImpulsiveness * 0.045));

            // Boredom is the main anti-pattern driver. Repeating or lingering in a
            // low-stimulation intent raises it; exploration and play satisfy it.
            double boredomDelta = resting ? 0.030 : (moving ? 0.008 : 0.016);
            if (stimulating) boredomDelta = -0.055;
            if (behaviorTicks > intentPreferredTicks) boredomDelta += 0.035;
            if (RecentCount(Behavior) >= 3) boredomDelta += 0.025;
            boredom = MathUtil.Clamp01(boredom + boredomDelta * dt *
                MathUtil.Lerp(1.25, 0.72, traitPatience));

            double restlessTarget = MathUtil.Clamp01(traitRestlessness * 0.65 +
                boredom * 0.46 + playfulness * 0.14 - sleepiness * 0.28);
            if (moving) restlessTarget -= 0.18;
            restlessness = MathUtil.MoveTowards(restlessness,
                MathUtil.Clamp01(restlessTarget), dt * 0.085);

            double curiosityTarget = MathUtil.Clamp01(traitCuriosity * 0.62 +
                boredom * 0.30 + (context.MouseAttentionActive ? traitMouseInterest * 0.28 : 0.0));
            if (Behavior == DesktopBehavior.Explore || Behavior == DesktopBehavior.LookAround ||
                Behavior == DesktopBehavior.ObserveWindow || Behavior == DesktopBehavior.FollowMouse)
                curiosityTarget -= 0.16;
            curiosity = MathUtil.MoveTowards(curiosity, MathUtil.Clamp01(curiosityTarget),
                dt * 0.060);

            double playfulTarget = MathUtil.Clamp01(traitPlayfulness * 0.72 +
                boredom * 0.18 + (mood == AIMood.Playful ? 0.25 : 0.0) - sleepiness * 0.22);
            if (Behavior == DesktopBehavior.Play || Behavior == DesktopBehavior.Jump)
                playfulTarget -= 0.15;
            playfulness = MathUtil.MoveTowards(playfulness,
                MathUtil.Clamp01(playfulTarget), dt * 0.070);

            if (Behavior == DesktopBehavior.Sleep)
                sleepiness = MathUtil.Clamp01(sleepiness - dt * 0.22);
            else if (Behavior == DesktopBehavior.Sit)
                sleepiness = MathUtil.Clamp01(sleepiness - dt * 0.075);
            else
                sleepiness = MathUtil.Clamp01(sleepiness + dt *
                    (0.010 + (moving ? activity * 0.012 : 0.003)));

            double attentionTarget = MathUtil.Clamp01(traitAttention * 0.65 +
                curiosity * 0.22 + (context.MouseAttentionActive ? traitMouseInterest * 0.42 : 0.0));
            attentionDrive = MathUtil.MoveTowards(attentionDrive, attentionTarget, dt * 0.10);

            double confidenceTarget = MathUtil.Clamp01(traitConfidence * 0.78 +
                personalityBravery * 0.18 - (context.EdgeDistance < 28.0 ? 0.12 : 0.0) -
                (context.ObstacleAhead ? 0.06 : 0.0));
            confidence = MathUtil.MoveTowards(confidence, confidenceTarget, dt * 0.045);

            fatigue = sleepiness;
            movementUrge = MathUtil.Clamp01(boredom * 0.52 + restlessness * 0.48);
        }

        private void PopulateDynamicContext(UtilityContext context)
        {
            context.Activity = activity;
            context.Curiosity = curiosity;
            context.Boredom = boredom;
            context.Restlessness = restlessness;
            context.Sleepiness = sleepiness;
            context.Playfulness = playfulness;
            context.AttentionDrive = attentionDrive;
            context.Confidence = confidence;
            context.Mood = mood;
            context.Fatigue = fatigue;
            context.MovementUrge = movementUrge;

            context.TraitActivity = traitActivity;
            context.TraitCuriosity = traitCuriosity;
            context.TraitPlayfulness = traitPlayfulness;
            context.TraitRestlessness = traitRestlessness;
            context.TraitAttention = traitAttention;
            context.TraitConfidence = traitConfidence;
            context.TraitPatience = traitPatience;
            context.TraitImpulsiveness = traitImpulsiveness;
            context.TraitRest = traitRest;
            context.TraitObservation = traitObservation;
            context.TraitDirectionPersistence = traitDirectionPersistence;
            context.TraitMouseInterest = traitMouseInterest;
            context.TraitMouseApproach = traitMouseApproach;

            // Legacy diagnostics map to the new trait model.
            context.RoamAffinity = traitActivity;
            context.JumpAffinity = traitPlayfulness;
            context.RestAffinity = traitRest;
            context.ObservationAffinity = traitObservation;
            context.DirectionPersistence = traitDirectionPersistence;
        }

        private void UpdateMood(UtilityContext context)
        {
            if (moodTicksRemaining > 0)
            {
                // Strong needs can shorten a mood, but never cause per-tick rerolls.
                if ((sleepiness > 0.90 && mood != AIMood.Lazy) ||
                    (boredom > 0.90 && mood == AIMood.Calm) ||
                    (context.MouseAttentionActive && curiosity > 0.82 &&
                        mood != AIMood.Curious && moodTicksRemaining > 80))
                    moodTicksRemaining = Math.Min(moodTicksRemaining, 30);
                return;
            }

            double calm = 0.08 + traitPatience * 0.38 + traitRest * 0.17 +
                (1.0 - restlessness) * 0.20;
            double curiousMood = 0.06 + curiosity * 0.42 + traitCuriosity * 0.22 +
                attentionDrive * 0.12;
            double energetic = 0.05 + activity * 0.40 + traitActivity * 0.26 +
                restlessness * 0.12;
            double lazy = 0.04 + sleepiness * 0.50 + traitRest * 0.24 +
                (1.0 - activity) * 0.12;
            double playfulMood = 0.04 + playfulness * 0.46 + traitPlayfulness * 0.30 +
                boredom * 0.08;
            double restlessMood = 0.04 + restlessness * 0.46 + boredom * 0.24 +
                traitImpulsiveness * 0.20;
            double focused = 0.05 + attentionDrive * 0.35 + traitObservation * 0.31 +
                traitPatience * 0.12;

            // Every archetype keeps a non-zero chance of a contrasting mood.
            // This is what lets a Rivulet occasionally become lazy or a Monk playful.
            double total = calm + curiousMood + energetic + lazy + playfulMood +
                restlessMood + focused;
            double pick = random.NextDouble() * total;
            if ((pick -= calm) < 0.0) mood = AIMood.Calm;
            else if ((pick -= curiousMood) < 0.0) mood = AIMood.Curious;
            else if ((pick -= energetic) < 0.0) mood = AIMood.Energetic;
            else if ((pick -= lazy) < 0.0) mood = AIMood.Lazy;
            else if ((pick -= playfulMood) < 0.0) mood = AIMood.Playful;
            else if ((pick -= restlessMood) < 0.0) mood = AIMood.Restless;
            else mood = AIMood.Focused;

            double patienceFactor = MathUtil.Lerp(0.72, 1.32, traitPatience);
            double impulsiveFactor = MathUtil.Lerp(1.18, 0.72, traitImpulsiveness);
            double seconds = SampleCentered(4.0, 18.0) * patienceFactor * impulsiveFactor;
            if (mood == AIMood.Lazy) seconds *= 1.15;
            if (mood == AIMood.Restless || mood == AIMood.Playful) seconds *= 0.82;
            moodTicksRemaining = SecondsToTicks(MathUtil.Clamp(seconds, 2.5, 24.0));
            evaluationCountdown = Math.Min(evaluationCountdown, 2);
        }

        private int EvaluationIntervalTicks()
        {
            double seconds = MathUtil.Lerp(0.30, 0.12,
                MathUtil.Clamp01(traitImpulsiveness * 0.55 + restlessness * 0.45));
            seconds *= 0.85 + random.NextDouble() * 0.30;
            return Math.Max(3, SecondsToTicks(seconds));
        }

        private static int SecondsToTicks(double seconds)
        {
            return Math.Max(1, (int)Math.Round(seconds /
                SimulationConstants.LogicStepSeconds));
        }

        private double SampleCentered(double minimum, double maximum)
        {
            double t = (random.NextDouble() + random.NextDouble() +
                random.NextDouble()) / 3.0;
            return MathUtil.Lerp(minimum, maximum, t);
        }

        private static bool IsLocomotionBehavior(DesktopBehavior behavior)
        {
            return behavior == DesktopBehavior.Walk || behavior == DesktopBehavior.Run ||
                behavior == DesktopBehavior.Crawl || behavior == DesktopBehavior.Explore ||
                behavior == DesktopBehavior.FollowMouse || behavior == DesktopBehavior.AvoidMouse ||
                behavior == DesktopBehavior.Play;
        }

        public PlatformTransitionPlan PlanPlatformTransition(Slugcat slugcat,
            DesktopCollisionWorld world)
        {
            TransitionPlan.Clear();
            Vec2 center = slugcat.Center;
            double maximumRange = slugcat.AbilityController is SaintAbilityController
                ? 220.0 : (slugcat.AbilityController is ArtificerAbilityController ? 250.0 : 105.0);
            DesktopSurface best = null;
            Vec2 bestPoint = Vec2.Zero;
            double bestScore = double.MaxValue;
            for (int i = 0; i < world.Surfaces.Count; i++)
            {
                DesktopSurface surface = world.Surfaces[i];
                if (!surface.IsHorizontal || surface.Id == slugcat.PrimarySupportingSurfaceId) continue;
                if (IsRecentTransitionPair(slugcat.PrimarySupportingSurfaceId,
                    surface.Id)) continue;
                Vec2 candidate = new Vec2(MathUtil.Clamp(center.X, surface.Left + 12.0,
                    surface.Right - 12.0), surface.Top - SimulationConstants.HipsChunkRadius);
                Vec2 delta = candidate - center;
                if (delta.Length > maximumRange || surface.Right - surface.Left < 24.0) continue;
                // A monitor publishes overlapping work-area and physical
                // floor surfaces. Neither is a meaningful route from the
                // other when they resolve to the current position.
                if (Math.Abs(delta.X) < 24.0 && Math.Abs(delta.Y) < 20.0) continue;
                // Prefer another window above or across a modest gap. Floors
                // below remain a DropDown concern handled by the normal AI.
                if (delta.Y > 50.0) continue;
                // SlugNPCAI follows a selected MovementConnection. Do not
                // make every desktop pet choose the same nearest platform:
                // personality gives each cat a stable preference for route
                // direction and for longer/safer transitions.
                bool preferredDirection = Math.Sign(delta.X) == desiredDirection;
                double score = delta.Length + Math.Max(0.0, delta.Y) *
                    MathUtil.Lerp(2.4, 1.2, personalityBravery);
                score += preferredDirection ? -12.0 * personalityDominance :
                    24.0 * (1.0 - personalityDominance);
                // SlugNPCAI's IdleScore distinguishes viable destinations by
                // personality and recent visits. Preserve a stable per-cat
                // preference among otherwise equivalent desktop platforms so
                // co-spawned cats do not all converge on one nearest ledge.
                score -= SurfaceAffinity(surface.Id) * MathUtil.Lerp(7.0,
                    24.0, (personalityBravery + personalityDominance) * 0.5);
                if (score >= bestScore) continue;
                bestScore = score;
                best = surface;
                bestPoint = candidate;
            }
            if (best == null) return TransitionPlan;

            TransitionPlan.IsValid = true;
            TransitionPlan.SourceSurfaceId = slugcat.PrimarySupportingSurfaceId;
            TransitionPlan.TargetSurfaceId = best.Id;
            TransitionPlan.TargetPoint = bestPoint;
            TransitionPlan.HorizontalDistance = bestPoint.X - center.X;
            TransitionPlan.VerticalDistance = bestPoint.Y - center.Y;
            TransitionPlan.Mode = slugcat.AbilityController is SaintAbilityController
                ? PlatformTransitionMode.TongueSwing
                : (slugcat.AbilityController is ArtificerAbilityController
                    ? PlatformTransitionMode.ExplosiveJump
                    : PlatformTransitionMode.StandardJump);
            return TransitionPlan;
        }

        private bool TryProduceAbilityInput(Slugcat slugcat, MouseTracker mouse,
            MouseAttentionState mouseAttention, UtilityContext context,
            out VirtualInput input)
        {
            input = VirtualInput.Neutral;
            SpearmasterAbilityController spear =
                slugcat.AbilityController as SpearmasterAbilityController;
            if (spear != null)
            {
                return ProduceSpearmasterInput(slugcat, spear, mouse,
                    mouseAttention, out input);
            }
            ResetSpearmasterState();

            GourmandAbilityController gourmand =
                slugcat.AbilityController as GourmandAbilityController;
            if (gourmand != null && Behavior == DesktopBehavior.GourmandRoll &&
                !context.Grounded &&
                (slugcat.BodyChunks[0].Velocity.Y +
                 slugcat.BodyChunks[1].Velocity.Y) * 0.5 > 2.0)
            {
                input = new VirtualInput(desiredDirection, 1, false, false);
                return true;
            }

            SaintAbilityController saint =
                slugcat.AbilityController as SaintAbilityController;
            if (saint != null)
                return ProduceSaintInput(slugcat, saint, context, out input);
            ResetSaintTransition();

            ArtificerAbilityController artificer =
                slugcat.AbilityController as ArtificerAbilityController;
            if (artificer == null)
            {
                ResetArtificerTransition();
                return false;
            }

            // Keep the original Artificer input order: first perform a normal jump,
            // then send a fresh Jump + Pickup edge after CanJump has expired. Planned
            // long transitions always qualify. In addition, Artificer may deliberately
            // use the same original mechanic as a freestyle movement choice.
            if (context.Grounded && Behavior == DesktopBehavior.Jump &&
                behaviorTicks <= 8)
            {
                bool planned = RequiresExplosiveTransition();
                bool freestyle = !planned && artificerFreestyleCooldownTicks == 0 &&
                    CanFreestyleSpecial(context) &&
                    random.NextDouble() < ArtificerFreestyleChance();
                if (planned || freestyle)
                {
                    specialTransitionArmed = true;
                    specialTransitionFreestyle = freestyle;
                    specialTransitionTargetSurfaceId = planned
                        ? TransitionPlan.TargetSurfaceId : 0;
                    if (freestyle)
                        artificerFreestyleCooldownTicks = SecondsToTicks(SampleCentered(2.0, 4.2));
                    return false;
                }
            }
            if (specialTransitionArmed && !specialTransitionFreestyle &&
                (!TransitionPlan.IsValid ||
                 TransitionPlan.TargetSurfaceId != specialTransitionTargetSurfaceId))
            {
                ResetArtificerTransition();
            }
            if (specialTransitionArmed && !context.Grounded &&
                slugcat.State.CanJump <= 0 && slugcat.State.Conscious)
            {
                int direction = specialTransitionFreestyle || !TransitionPlan.IsValid
                    ? desiredDirection
                    : (TransitionPlan.HorizontalDistance < 0.0 ? -1 : 1);
                ResetArtificerTransition();
                Behavior = DesktopBehavior.ExplosiveJump;
                input = new VirtualInput(direction, -1, true, true);
                movementUrge = Math.Max(0.12, movementUrge - 0.16);
                return true;
            }
            return false;
        }

        private bool ProduceSaintInput(Slugcat slugcat,
            SaintAbilityController saint, UtilityContext context,
            out VirtualInput input)
        {
            input = VirtualInput.Neutral;
            if (saint.Mode == SaintTongueMode.AttachedToTerrain)
            {
                saintTransitionArmed = false;
                saintTransitionFreestyle = false;
                saintAwaitingJumpRelease = false;
                saintAttachedTicks++;
                if (saintAttachedTicks >= saintAttachDurationTicks)
                {
                    saintAttachedTicks = 0;
                    saintAttachDurationTicks = 45 + random.Next(0, 56);
                    Behavior = DesktopBehavior.TongueSwing;
                    // The original tongue release is a fresh jump edge.
                    input = new VirtualInput(desiredDirection, 0, true, false);
                    return true;
                }

                bool anchorAbove = saint.TonguePosition.Y < slugcat.Center.Y - 12.0;
                input = new VirtualInput(desiredDirection, anchorAbove ? -1 : 0,
                    false, false);
                return true;
            }
            saintAttachedTicks = 0;
            if (saint.Mode != SaintTongueMode.Retracted) return false;

            if (!saintTransitionArmed && saintTongueIntentCountdownTicks <= 0 &&
                saintFreestyleCooldownTicks == 0 && CanStartProactiveSaintTongue(context))
            {
                saintTransitionArmed = true;
                saintTransitionFreestyle = true;
                saintAwaitingJumpRelease = false;
                saintFreestyleCooldownTicks = SecondsToTicks(SampleCentered(1.5, 3.0));
                saintTongueIntentCountdownTicks = NextSaintTongueIntentTicks();
                EnterBehavior(slugcat, DesktopBehavior.Jump, context);
                input = new VirtualInput(desiredDirection, 0, true, false);
                return true;
            }

            if (context.Grounded && Behavior == DesktopBehavior.Jump &&
                behaviorTicks <= 8)
            {
                bool planned = RequiresTongueTransition();
                bool freestyle = !planned && saintFreestyleCooldownTicks == 0 &&
                    CanFreestyleSpecial(context) &&
                    random.NextDouble() < SaintFreestyleChance();
                if (planned || freestyle)
                {
                    saintTransitionArmed = true;
                    saintTransitionFreestyle = freestyle;
                    saintAwaitingJumpRelease = false;
                    if (freestyle)
                        saintFreestyleCooldownTicks = SecondsToTicks(0.5);
                    saintTongueIntentCountdownTicks = NextSaintTongueIntentTicks();
                    return false;
                }
            }
            if (!saintTransitionArmed) return false;
            if (!slugcat.State.Conscious)
            {
                ResetSaintTransition();
                return false;
            }
            if (context.Grounded)
            {
                // Keep a planned tongue transition armed for the same short launch
                // grace used by the original jump. A grounded contact on the frame
                // immediately after the jump input must not discard the route.
                if (Behavior != DesktopBehavior.Jump || behaviorTicks > 8)
                    ResetSaintTransition();
                return false;
            }

            // SaintTongueCheck needs a new jump edge after Player.Jump's
            // airborne grace. First release the held launch input, then wait
            // for CanJump to reach the same state the game DLL checks.
            if (!saintAwaitingJumpRelease || slugcat.LastInput.Jump ||
                slugcat.State.CanJump > 0)
            {
                saintAwaitingJumpRelease = true;
                input = new VirtualInput(desiredDirection, 0, false, false);
                return true;
            }

            saintTransitionArmed = false;
            saintTransitionFreestyle = false;
            saintAwaitingJumpRelease = false;
            Behavior = DesktopBehavior.TongueSwing;
            input = new VirtualInput(desiredDirection, 0, true, false);
            movementUrge = Math.Max(0.10, movementUrge - 0.12);
            return true;
        }

        private bool RequiresExplosiveTransition()
        {
            // Standard desktop path jumps are planned inside the normal
            // 105-unit reach.  Artificer's extended 250-unit candidate range
            // is the only desktop equivalent that needs the original
            // Player.ClassMechanicsArtificer does not impose a spatial
            // threshold. The desktop AI only asks for it when its planned
            // route is a meaningful gap or ascent, rather than using a
            // periodic airborne timer.
            return TransitionPlan.Mode == PlatformTransitionMode.ExplosiveJump &&
                TransitionPlan.IsValid &&
                (Math.Abs(TransitionPlan.HorizontalDistance) >= 44.0 ||
                 TransitionPlan.VerticalDistance <= -16.0);
        }

        private bool RequiresTongueTransition()
        {
            return TransitionPlan.Mode == PlatformTransitionMode.TongueSwing &&
                TransitionPlan.IsValid &&
                (Math.Abs(TransitionPlan.HorizontalDistance) >= 28.0 ||
                 TransitionPlan.VerticalDistance <= -12.0);
        }

        private bool IsSpecialTraversalAvailable(Slugcat slugcat)
        {
            return (slugcat.AbilityController is ArtificerAbilityController &&
                    RequiresExplosiveTransition()) ||
                (slugcat.AbilityController is SaintAbilityController &&
                    RequiresTongueTransition());
        }

        private double PersonalityCuriosity()
        {
            return MathUtil.Clamp01((personalityEnergy +
                (1.0 - personalityNervous)) * 0.5);
        }

        private bool CanFreestyleSpecial(UtilityContext context)
        {
            // Keep spontaneous abilities away from an immediate ledge or blocking wall.
            // The ability controllers still decide whether the original move can execute.
            return context.Grounded && !context.ObstacleAhead &&
                context.EdgeDistance > 34.0;
        }

        private double ArtificerFreestyleChance()
        {
            return MathUtil.Clamp01(0.10 + playfulness * 0.12 +
                restlessness * 0.10 + traitSpecialUse * 0.10 +
                (mood == AIMood.Playful ? 0.06 : 0.0));
        }

        private double SaintFreestyleChance()
        {
            return MathUtil.Clamp01(0.28 + curiosity * 0.16 +
                playfulness * 0.10 + traitSpecialUse * 0.18 +
                (mood == AIMood.Curious ? 0.08 : 0.0));
        }

        private bool CanStartProactiveSaintTongue(UtilityContext context)
        {
            bool eligibleBehavior = IsLocomotionBehavior(Behavior) ||
                Behavior == DesktopBehavior.Idle ||
                Behavior == DesktopBehavior.LookAround ||
                Behavior == DesktopBehavior.ObserveWindow;
            return eligibleBehavior && context.Grounded && !context.ObstacleAhead &&
                context.EdgeDistance > 24.0;
        }

        private int NextSaintTongueIntentTicks()
        {
            double seconds = SampleCentered(8.0, 18.0);
            seconds *= MathUtil.Lerp(1.10, 0.82, traitSpecialUse);
            return SecondsToTicks(seconds);
        }

        private void ResetArtificerTransition()
        {
            specialTransitionArmed = false;
            specialTransitionFreestyle = false;
            specialTransitionTargetSurfaceId = 0;
        }

        private void ResetSaintTransition()
        {
            saintTransitionArmed = false;
            saintTransitionFreestyle = false;
            saintAwaitingJumpRelease = false;
            saintAttachedTicks = 0;
        }

        private bool ProduceSpearmasterInput(Slugcat slugcat,
            SpearmasterAbilityController spear, MouseTracker mouse,
            MouseAttentionState mouseAttention, out VirtualInput input)
        {
            input = VirtualInput.Neutral;
            spearmasterStateTicks++;
            bool hasTarget = mouseAttention != null && mouseAttention.IsActive;
            if (hasTarget) spearmasterTarget = mouse.Position;
            bool hasThrowTarget = hasTarget || spearmasterAutonomousThrow;
            double targetDistance = hasThrowTarget
                ? Vec2.Distance(slugcat.Center, spearmasterTarget)
                : double.MaxValue;

            switch (spearmasterState)
            {
                case SpearmasterActionState.Idle:
                    spear.SetActionState(spearmasterState, spearmasterTarget);
                    if (spearmasterStateTicks >= spearmasterIdleDuration)
                    {
                        // Spearmaster always eventually performs its explicit
                        // extraction sequence; personality still varies how long it
                        // waits between repetitions instead of making the action
                        // disappear behind a failed random gate.
                        spearmasterMoveDuration = 25 + random.Next(0, 25);
                        ChangeSpearmasterState(SpearmasterActionState.Moving);
                    }
                    // The action scheduler must not replace the normal
                    // utility input while it has nothing to do. The previous
                    // neutral return here caused Spearmaster to stand frozen.
                    return false;

                case SpearmasterActionState.Moving:
                    spear.SetActionState(spearmasterState, spearmasterTarget);
                    if (spearmasterStateTicks >= spearmasterMoveDuration)
                        ChangeSpearmasterState(SpearmasterActionState.PreparingSpear);
                    return false;

                case SpearmasterActionState.PreparingSpear:
                    spear.SetActionState(spearmasterState, spearmasterTarget);
                    if (spearmasterStateTicks >= 14)
                        ChangeSpearmasterState(SpearmasterActionState.PullingSpear);
                    return false;

                case SpearmasterActionState.PullingSpear:
                    spear.SetActionState(spearmasterState, spearmasterTarget);
                    Behavior = DesktopBehavior.MakeSpear;
                    input = new VirtualInput(0, 0, false, true);
                    if (spear.HeldSpear != null)
                    {
                        ChangeSpearmasterState(SpearmasterActionState.HoldingSpear);
                    }
                    return true;

                case SpearmasterActionState.HoldingSpear:
                    spear.SetActionState(spearmasterState, spearmasterTarget);
                    if (spear.HeldSpear == null)
                    {
                        spearmasterRecoveryDuration = 90 + random.Next(0, 80);
                        ChangeSpearmasterState(SpearmasterActionState.Recovering);
                        return false;
                    }
                    if (hasTarget && targetDistance >= 50.0 &&
                        targetDistance <= 550.0)
                    {
                        // Keep the original target-driven throw transition,
                        // while making a brief desktop click-attention window
                        // reliable enough to produce an actual throw.
                        double throwChance = MathUtil.Lerp(0.05, 0.13,
                            personalityAggression);
                        if (random.NextDouble() < throwChance)
                            ChangeSpearmasterState(SpearmasterActionState.Aiming);
                        return false;
                    }
                    if (hasTarget) return false;
                    // The desktop has no prey tracker in the absence of a
                    // mouse target. Give each Spearmaster a short individual
                    // cooldown, then use the same aim-align-throw sequence
                    // against a local direction so it does not hold a spear
                    // indefinitely or synchronize with nearby cats.
                    if (--spearmasterAutonomousThrowCountdown <= 0)
                    {
                        int direction = random.NextDouble() < 0.2
                            ? -slugcat.State.Facing : slugcat.State.Facing;
                        spearmasterTarget = slugcat.Center + new Vec2(
                            direction * (130.0 + random.NextDouble() * 110.0),
                            (random.NextDouble() * 2.0 - 1.0) * 24.0);
                        spearmasterAutonomousThrow = true;
                        ChangeSpearmasterState(SpearmasterActionState.Aiming);
                    }
                    return false;

                case SpearmasterActionState.Aiming:
                    spear.SetActionState(spearmasterState, spearmasterTarget);
                    Behavior = DesktopBehavior.ThrowSpear;
                    if (!hasThrowTarget || targetDistance < 50.0 || targetDistance > 550.0)
                    {
                        spearmasterAutonomousThrow = false;
                        ChangeSpearmasterState(SpearmasterActionState.HoldingSpear);
                        return true;
                    }
                    int aimX = spearmasterTarget.X < slugcat.Center.X ? -1 : 1;
                    input = new VirtualInput(aimX, 0, false, false);
                    // SlugNPCAI first turns toward throwAtTarget, then sends
                    // the throw input on the following aligned update.
                    if (slugcat.State.Facing == aimX && spearmasterStateTicks > 0)
                        ChangeSpearmasterState(SpearmasterActionState.Throwing);
                    return true;

                case SpearmasterActionState.Throwing:
                    spear.SetActionState(spearmasterState, spearmasterTarget);
                    Behavior = DesktopBehavior.ThrowSpear;
                    if (!hasThrowTarget || targetDistance < 50.0 || targetDistance > 550.0)
                    {
                        spearmasterAutonomousThrow = false;
                        ChangeSpearmasterState(SpearmasterActionState.HoldingSpear);
                        return true;
                    }
                    int throwX = spearmasterTarget.X < slugcat.Center.X ? -1 : 1;
                    input = new VirtualInput(throwX, 0, false, false, true,
                        VirtualPosture.None, false);
                    if (spearmasterStateTicks > 1 || spear.HeldSpear == null)
                    {
                        spearmasterAutonomousThrow = false;
                        spearmasterRecoveryDuration = 5;
                        ChangeSpearmasterState(SpearmasterActionState.Recovering);
                    }
                    return true;

                case SpearmasterActionState.Recovering:
                    spear.SetActionState(spearmasterState, spearmasterTarget);
                    if (spearmasterStateTicks >= spearmasterRecoveryDuration)
                    {
                        spearmasterIdleDuration = SecondsToTicks(SampleCentered(4.0, 12.0));
                        ChangeSpearmasterState(SpearmasterActionState.Idle);
                    }
                    return false;
            }
            return false;
        }

        private void ChangeSpearmasterState(SpearmasterActionState state)
        {
            SpearmasterActionState previous = spearmasterState;
            spearmasterState = state;
            spearmasterStateTicks = 0;
            if (state == SpearmasterActionState.HoldingSpear &&
                previous != SpearmasterActionState.HoldingSpear)
            {
                // 4.5 to 7.5 seconds at the 40 Hz logic rate, with a
                // deterministic per-instance offset.
                spearmasterAutonomousThrowCountdown = SecondsToTicks(SampleCentered(5.0, 11.0));
                spearmasterAutonomousThrow = false;
            }
        }

        private void ResetSpearmasterState()
        {
            spearmasterState = SpearmasterActionState.Idle;
            spearmasterStateTicks = 0;
            spearmasterAutonomousThrowCountdown = 0;
            spearmasterAutonomousThrow = false;
            spearmasterTarget = Vec2.Zero;
        }

        private UtilityContext BuildContext(Slugcat slugcat, DesktopCollisionWorld world,
            MouseTracker mouse)
        {
            UtilityContext context = utilityContext;
            context.Grounded = slugcat.State.Grounded;
            bool contactRight = slugcat.BodyChunks[0].ContactRight ||
                slugcat.BodyChunks[1].ContactRight;
            bool contactLeft = slugcat.BodyChunks[0].ContactLeft ||
                slugcat.BodyChunks[1].ContactLeft;
            bool currentWallContact = contactLeft || contactRight;
            if (currentWallContact)
            {
                bool risingEdge = wallContactGraceTicks == 0;
                wallContactGraceTicks = 18;
                if (contactRight) lastWallDirection = 1;
                else if (contactLeft) lastWallDirection = -1;
                if (risingEdge) evaluationCountdown = 1;
            }
            else if (wallContactGraceTicks > 0)
            {
                wallContactGraceTicks--;
            }

            context.WallContact = wallContactGraceTicks > 0;
            context.ObstacleDirection = contactRight ? 1 : (contactLeft ? -1 : 0);
            context.ObstacleAhead = context.Grounded &&
                context.ObstacleDirection != 0 &&
                context.ObstacleDirection == desiredDirection;

            double leftEdge = world.DistanceToEdge(slugcat.Center, -1,
                slugcat.PrimarySupportingSurfaceId);
            double rightEdge = world.DistanceToEdge(slugcat.Center, 1,
                slugcat.PrimarySupportingSurfaceId);
            context.SaferDirection = leftEdge >= rightEdge ? -1 : 1;
            context.EdgeDistance = slugcat.State.Facing < 0 ? leftEdge : rightEdge;

            // EdgeDistance is intentionally populated first. This avoids the old
            // bug where autonomous hops were permanently disabled by a default 0.
            context.ExplorationJumpAvailable = explorationJumpRequested &&
                explorationJumpCooldownTicks == 0 && context.Grounded &&
                !context.ObstacleAhead && context.EdgeDistance > 48.0;
            context.FreeJumpOpportunity = context.Grounded &&
                jumpCooldownTicks == 0 && explorationJumpCooldownTicks == 0 &&
                !context.ObstacleAhead && context.EdgeDistance > 54.0 &&
                playfulness > 0.58 && (restlessness + boredom) > 0.82;

            context.OnWindow = slugcat.PrimarySupportingSurfaceId > 0;
            context.MouseDistance = Vec2.Distance(slugcat.Center, mouse.Position);
            context.MouseSpeed = mouse.Velocity.Length;
            context.Stillness = slugcat.State.Stillness;
            context.BehaviorAgeSeconds = behaviorTicks * SimulationConstants.LogicStepSeconds;
            context.JumpReady = jumpCooldownTicks == 0;
            context.DropReady = dropCooldownTicks == 0;
            context.RestReady = restCooldownTicks == 0 ||
                Behavior == DesktopBehavior.Sit || Behavior == DesktopBehavior.Sleep;
            context.PersonalityEnergy = personalityEnergy;
            context.PersonalityNervous = personalityNervous;
            context.PersonalityAggression = personalityAggression;
            context.PersonalityBravery = personalityBravery;
            context.PersonalityDominance = personalityDominance;
            PopulateDynamicContext(context);
            return context;
        }

        private void SelectBehavior(Slugcat slugcat, UtilityContext context)
        {
            bool urgentAvoid = context.MouseAttentionActive &&
                Behavior != DesktopBehavior.AvoidMouse && context.MouseDistance < 45.0;
            bool urgentClimb = Behavior != DesktopBehavior.ClimbWindow &&
                context.WallContact && !context.Grounded;

            topUtilityScore1 = topUtilityScore2 = topUtilityScore3 = 0.0;
            topUtility1 = topUtility2 = topUtility3 = DesktopBehavior.Idle;

            double maximumScore = 0.0;
            double totalWeight = 0.0;
            for (int i = 0; i < UtilityCandidates.Length; i++)
            {
                DesktopBehavior candidate = UtilityCandidates[i];
                // Noise is deliberately small. Needs/personality/environment remain
                // the primary cause of a choice.
                double variation = (random.NextDouble() - 0.5) * 0.055;
                double score = UtilityEvaluator.Score(candidate, context, variation);
                score = ApplyIntentContinuity(candidate, score, urgentAvoid, urgentClimb);
                score -= RecentBehaviorPenalty(candidate);
                if (score < 0.0) score = 0.0;
                utilityScores[i] = score;
                if (score > maximumScore) maximumScore = score;
                UpdateTopUtilities(candidate, score);
            }

            if (maximumScore <= 0.0001) return;

            // Weighted utility selection. Very weak candidates are ignored; viable
            // alternatives can occasionally win without becoming pure randomness.
            double floor = maximumScore * 0.34;
            for (int i = 0; i < UtilityCandidates.Length; i++)
            {
                double score = utilityScores[i];
                if (score < floor)
                {
                    utilityScores[i] = 0.0;
                    continue;
                }
                double normalized = score / maximumScore;
                double weight = normalized * normalized * normalized;
                utilityScores[i] = weight;
                totalWeight += weight;
            }
            if (totalWeight <= 0.0001) return;

            double pick = random.NextDouble() * totalWeight;
            DesktopBehavior selected = Behavior;
            for (int i = 0; i < UtilityCandidates.Length; i++)
            {
                double weight = utilityScores[i];
                if (weight <= 0.0) continue;
                pick -= weight;
                if (pick <= 0.0)
                {
                    selected = UtilityCandidates[i];
                    break;
                }
            }

            if (urgentAvoid) selected = DesktopBehavior.AvoidMouse;
            else if (urgentClimb) selected = DesktopBehavior.ClimbWindow;

            if (selected != Behavior)
                EnterBehavior(slugcat, selected, context);
        }

        private double ApplyIntentContinuity(DesktopBehavior candidate, double score,
            bool urgentAvoid, bool urgentClimb)
        {
            if (urgentAvoid && candidate == DesktopBehavior.AvoidMouse) return score + 1.0;
            if (urgentClimb && candidate == DesktopBehavior.ClimbWindow) return score + 1.0;

            if (candidate == Behavior)
            {
                // A chosen action has inertia, but boredom and overstay erode it.
                double continuity = 0.07 + traitPatience * 0.17 +
                    traitDirectionPersistence * (IsLocomotionBehavior(candidate) ? 0.08 : 0.02);
                if (behaviorTicks < intentMinimumTicks)
                    continuity += 0.23 * (1.0 - behaviorTicks /
                        (double)Math.Max(1, intentMinimumTicks));
                if (behaviorTicks > intentPreferredTicks)
                    continuity -= 0.12 + boredom * 0.18 +
                        MathUtil.InverseLerp(intentPreferredTicks, intentHardLimitTicks,
                            behaviorTicks) * 0.22;
                if (behaviorTicks >= intentHardLimitTicks) continuity -= 0.55;
                return Math.Max(0.0, score + continuity);
            }

            if (behaviorTicks < intentMinimumTicks)
            {
                // Impulsive/restless characters may change their mind before the
                // nominal minimum; patient characters usually finish the thought.
                double escape = MathUtil.Clamp01(traitImpulsiveness * 0.46 +
                    restlessness * 0.36 + boredom * 0.18);
                score *= MathUtil.Lerp(0.28, 0.92, escape);
            }
            else if (behaviorTicks > intentPreferredTicks)
            {
                score += boredom * 0.10 + restlessness * 0.06;
            }
            return score;
        }

        private double RecentBehaviorPenalty(DesktopBehavior candidate)
        {
            int count = RecentCount(candidate);
            if (count == 0) return 0.0;

            double penalty = count * MathUtil.Lerp(0.075, 0.035,
                traitImpulsiveness);
            if (IsLocomotionBehavior(candidate) && traitActivity > 0.72)
                penalty *= 0.62;

            // Explicitly break the visible A -> B -> A -> B pendulum, while
            // allowing playful/impulsive cats to do it sometimes as a quirk.
            if (recentBehaviorCount >= 2 && candidate == RecentBehaviorFromEnd(1) &&
                Behavior == RecentBehaviorFromEnd(0))
            {
                penalty += MathUtil.Lerp(0.20, 0.07,
                    MathUtil.Clamp01(traitPlayfulness * 0.45 + traitImpulsiveness * 0.55));
            }
            return penalty;
        }

        private int RecentCount(DesktopBehavior behavior)
        {
            int count = 0;
            for (int i = 0; i < recentBehaviorCount; i++)
                if (recentBehaviors[i] == behavior) count++;
            return count;
        }

        private DesktopBehavior RecentBehaviorFromEnd(int offset)
        {
            if (offset < 0 || offset >= recentBehaviorCount) return DesktopBehavior.Idle;
            int index = recentBehaviorCursor - 1 - offset;
            while (index < 0) index += RecentBehaviorCapacity;
            return recentBehaviors[index % RecentBehaviorCapacity];
        }

        private void RecordRecentBehavior(DesktopBehavior behavior)
        {
            recentBehaviors[recentBehaviorCursor] = behavior;
            recentBehaviorCursor = (recentBehaviorCursor + 1) % RecentBehaviorCapacity;
            if (recentBehaviorCount < RecentBehaviorCapacity) recentBehaviorCount++;
        }

        private void UpdateTopUtilities(DesktopBehavior behavior, double score)
        {
            if (score > topUtilityScore1)
            {
                topUtility3 = topUtility2; topUtilityScore3 = topUtilityScore2;
                topUtility2 = topUtility1; topUtilityScore2 = topUtilityScore1;
                topUtility1 = behavior; topUtilityScore1 = score;
            }
            else if (score > topUtilityScore2)
            {
                topUtility3 = topUtility2; topUtilityScore3 = topUtilityScore2;
                topUtility2 = behavior; topUtilityScore2 = score;
            }
            else if (score > topUtilityScore3)
            {
                topUtility3 = behavior; topUtilityScore3 = score;
            }
        }

        private void EnterBehavior(Slugcat slugcat, DesktopBehavior next,
            UtilityContext context)
        {
            DesktopBehavior previous = Behavior;
            if (previous != next) RecordRecentBehavior(next);
            Behavior = next;
            behaviorTicks = 0;
            AssignIntentTiming(next);
            microBehavior = MicroBehavior.None;
            microTicksRemaining = 0;

            // Cooldown begins after a rest intent ends, not while it is running.
            // Otherwise RestReady would invalidate a newly selected long rest.
            if (previous == DesktopBehavior.Sit && next != DesktopBehavior.Sit)
                restCooldownTicks = SecondsToTicks(SampleCentered(3.0, 9.0));
            else if (previous == DesktopBehavior.Sleep && next != DesktopBehavior.Sleep)
                restCooldownTicks = SecondsToTicks(SampleCentered(8.0, 20.0));

            if (IsLocomotionBehavior(next) || next == DesktopBehavior.Jump)
                movementUrge = Math.Max(0.06, movementUrge - 0.07);

            if (next == DesktopBehavior.Walk || next == DesktopBehavior.Run ||
                next == DesktopBehavior.Crawl || next == DesktopBehavior.Explore ||
                next == DesktopBehavior.Play)
            {
                MaybeChangeTravelDirection(false);
                freeRoamRetargetCountdown = 0;
            }
            else if (next == DesktopBehavior.TurnAround)
            {
                desiredDirection = -desiredDirection;
                directionCommitmentTicks = Math.Max(8, TravelCommitmentTicks() / 3);
                freeRoamRetargetCountdown = 0;
            }

            if (next == DesktopBehavior.Jump && TransitionPlan.IsValid &&
                Math.Abs(TransitionPlan.HorizontalDistance) > 0.001)
                desiredDirection = TransitionPlan.HorizontalDistance < 0.0 ? -1 : 1;

            if (next == DesktopBehavior.Jump)
            {
                if (context.ObstacleAhead)
                {
                    obstacleJumpAttemptTicks = 48;
                    obstacleJumpDirection = context.ObstacleDirection;
                    obstacleJumpWasAirborne = false;
                }
                if (context.ExplorationJumpAvailable)
                {
                    explorationJumpRequested = false;
                    explorationJumpCooldownTicks = SecondsToTicks(
                        MathUtil.Lerp(0.28, 0.75, 1.0 - traitPlayfulness));
                }
                jumpCooldownTicks = JumpCooldownFor(slugcat);
                RememberTransition(TransitionPlan);

                // Short and full-height jumps use the same original jump code; only
                // the existing input hold duration changes.
                double highJumpDrive = MathUtil.Clamp01(playfulness * 0.42 +
                    confidence * 0.32 + activity * 0.18 +
                    (mood == AIMood.Playful ? 0.20 : 0.0));
                jumpHoldTicks = 2 + (int)Math.Round(highJumpDrive * 6.0);
                jumpHoldTicks = MathUtil.Clamp(jumpHoldTicks, 2, 8);
            }

            if (next == DesktopBehavior.DropDown)
                dropCooldownTicks = SecondsToTicks(SampleCentered(5.0, 11.0));
        }

        private void AssignIntentTiming(DesktopBehavior behavior)
        {
            double minimum;
            double maximum;
            switch (behavior)
            {
                case DesktopBehavior.Idle: minimum = 0.5; maximum = 15.0; break;
                case DesktopBehavior.Walk: minimum = 1.4; maximum = 7.5; break;
                case DesktopBehavior.Run: minimum = 0.4; maximum = 4.0; break;
                case DesktopBehavior.Crawl: minimum = 0.8; maximum = 6.5; break;
                case DesktopBehavior.Explore: minimum = 1.2; maximum = 10.0; break;
                case DesktopBehavior.Sit: minimum = 0.7; maximum = 12.0; break;
                case DesktopBehavior.Sleep: minimum = 3.0; maximum = 22.0; break;
                case DesktopBehavior.LookAround: minimum = 0.8; maximum = 8.0; break;
                case DesktopBehavior.ObserveWindow: minimum = 1.0; maximum = 9.0; break;
                case DesktopBehavior.FollowMouse: minimum = 0.5; maximum = 5.0; break;
                case DesktopBehavior.AvoidMouse: minimum = 0.35; maximum = 3.0; break;
                case DesktopBehavior.Play: minimum = 0.5; maximum = 4.5; break;
                case DesktopBehavior.TurnAround: minimum = 0.15; maximum = 0.8; break;
                case DesktopBehavior.Jump: minimum = 0.20; maximum = 0.9; break;
                default: minimum = 0.5; maximum = 3.0; break;
            }

            double t = (random.NextDouble() + random.NextDouble() +
                random.NextDouble()) / 3.0;
            if (behavior == DesktopBehavior.Idle || behavior == DesktopBehavior.Sit ||
                behavior == DesktopBehavior.Sleep)
                t += traitRest * 0.24 + sleepiness * 0.20 - restlessness * 0.28;
            else if (behavior == DesktopBehavior.Run || behavior == DesktopBehavior.Play)
                t += traitActivity * 0.14 - traitPatience * 0.08;
            else if (behavior == DesktopBehavior.Explore || behavior == DesktopBehavior.Walk)
                t += traitDirectionPersistence * 0.17 + traitPatience * 0.08 -
                    traitImpulsiveness * 0.12;
            else if (behavior == DesktopBehavior.ObserveWindow ||
                behavior == DesktopBehavior.LookAround)
                t += traitObservation * 0.18 + traitPatience * 0.12 - restlessness * 0.12;

            if (mood == AIMood.Lazy && (behavior == DesktopBehavior.Idle ||
                behavior == DesktopBehavior.Sit || behavior == DesktopBehavior.Sleep)) t += 0.18;
            if (mood == AIMood.Restless && IsLocomotionBehavior(behavior)) t -= 0.10;
            t = MathUtil.Clamp01(t);

            double seconds = MathUtil.Lerp(minimum, maximum, t);
            intentPreferredTicks = SecondsToTicks(seconds);
            double minimumFraction = MathUtil.Lerp(0.22, 0.55, traitPatience);
            intentMinimumTicks = Math.Max(2, (int)Math.Round(intentPreferredTicks * minimumFraction));
            intentHardLimitTicks = Math.Max(intentPreferredTicks + 2,
                (int)Math.Round(intentPreferredTicks * MathUtil.Lerp(1.45, 2.10,
                    traitPatience)));
        }

        private int JumpCooldownFor(Slugcat slugcat)
        {
            // User-tuned hard range: 0.05~0.20 seconds. Character archetype and
            // persistent individual personality decide where inside that range.
            double jumpDrive = traitActivity * 0.30 + traitPlayfulness * 0.20 +
                traitRestlessness * 0.15 + personalityEnergy * 0.15 +
                personalityBravery * 0.12 + personalityDominance * 0.08;
            if (slugcat.AbilityController is ArtificerAbilityController)
                jumpDrive += 0.08;
            else if (slugcat.AbilityController is SaintAbilityController)
                jumpDrive += 0.04;
            jumpDrive = MathUtil.Clamp01(jumpDrive);

            double cooldownSeconds = MathUtil.Lerp(0.20, 0.05, jumpDrive);
            cooldownSeconds += personalityNervous * 0.018;
            cooldownSeconds = MathUtil.Clamp(cooldownSeconds, 0.05, 0.20);
            return SecondsToTicks(cooldownSeconds);
        }

        private bool IsRecentTransitionPair(long sourceSurfaceId, long targetSurfaceId)
        {
            if (routeMemoryTicks <= 0 || sourceSurfaceId == 0 ||
                targetSurfaceId == 0) return false;
            return (sourceSurfaceId == recentTransitionSourceSurfaceId &&
                targetSurfaceId == recentTransitionTargetSurfaceId) ||
                (sourceSurfaceId == recentTransitionTargetSurfaceId &&
                targetSurfaceId == recentTransitionSourceSurfaceId);
        }

        private void RememberTransition(PlatformTransitionPlan plan)
        {
            if (!plan.IsValid) return;
            recentTransitionSourceSurfaceId = plan.SourceSurfaceId;
            recentTransitionTargetSurfaceId = plan.TargetSurfaceId;
            // Keep the route-pair memory longer than Jump's own input
            // cooldown. A missed landing therefore cannot turn into a
            // left-right jump loop on the next evaluation.
            routeMemoryTicks = 520;
        }

        private double SurfaceAffinity(long surfaceId)
        {
            long mixed = unchecked(surfaceId * 1103515245L +
                routePreferenceSalt * 12345L);
            return ((mixed & 0x7fffffffL) / (double)int.MaxValue) * 2.0 - 1.0;
        }

        private void UpdateExplorationTarget(Slugcat slugcat,
            DesktopCollisionWorld world, MouseTracker mouse, UtilityContext context)
        {
            if (!IsLocomotionBehavior(Behavior))
            {
                explorationSurfaceId = 0;
                explorationJumpRequested = false;
                if (Behavior != DesktopBehavior.FollowMouse &&
                    Behavior != DesktopBehavior.AvoidMouse)
                    destinationKind = AIDestinationKind.None;
                return;
            }

            DesktopSurface surface;
            if (!world.TryGetSurface(slugcat.PrimarySupportingSurfaceId,
                slugcat.PrimarySupportingSurfaceKind, out surface) ||
                !surface.IsHorizontal) return;

            double width = surface.Right - surface.Left;
            double margin = Math.Min(80.0, width * 0.18);
            if (margin < 12.0) margin = 12.0;
            double left = surface.Left + margin;
            double right = surface.Right - margin;
            if (right <= left) return;

            double centerX = slugcat.Center.X;
            bool atBoundary = centerX <= surface.Left + 12.0 ||
                centerX >= surface.Right - 12.0;
            bool reached = Math.Abs(centerX - explorationTargetX) < 14.0;
            bool changedSurface = explorationSurfaceId != surface.Id;
            bool restlessAbandon = freeRoamRetargetCountdown <= 0 &&
                (restlessness > 0.52 || boredom > 0.64 ||
                 traitImpulsiveness > 0.72);

            if (!changedSurface && explorationTargetTicks > 0 && !atBoundary &&
                !reached && !restlessAbandon) return;

            explorationSurfaceId = surface.Id;
            if (atBoundary)
            {
                desiredDirection = centerX <= (surface.Left + surface.Right) * 0.5 ? 1 : -1;
                directionCommitmentTicks = TravelCommitmentTicks();
            }
            else MaybeChangeTravelDirection(false);

            destinationKind = ChooseDestinationKind(context);
            explorationTargetX = ChooseDestinationX(destinationKind, centerX,
                left, right, mouse, context);
            explorationTargetX = MathUtil.Clamp(explorationTargetX, left, right);
            intentTarget = new Vec2(explorationTargetX, slugcat.Center.Y);
            RememberTarget(explorationTargetX);

            if (Math.Abs(explorationTargetX - centerX) > 8.0)
            {
                desiredDirection = explorationTargetX < centerX ? -1 : 1;
                if (changedSurface || reached || atBoundary)
                    directionCommitmentTicks = TravelCommitmentTicks();
            }

            double targetSeconds = MathUtil.Lerp(0.8, 5.5,
                traitDirectionPersistence * 0.62 + traitPatience * 0.38);
            if (Behavior == DesktopBehavior.Run || Behavior == DesktopBehavior.Play)
                targetSeconds *= MathUtil.Lerp(0.55, 0.90, traitPatience);
            targetSeconds *= 0.78 + random.NextDouble() * 0.44;
            explorationTargetTicks = SecondsToTicks(targetSeconds);

            double abandonSeconds = MathUtil.Lerp(0.55, 4.2,
                traitDirectionPersistence * 0.50 + traitPatience * 0.50);
            abandonSeconds *= MathUtil.Lerp(0.62, 1.15, 1.0 - traitImpulsiveness);
            freeRoamRetargetCountdown = SecondsToTicks(
                abandonSeconds * (0.80 + random.NextDouble() * 0.40));

            // A new destination can create a jump intention, but this remains a
            // minority outcome. Jump cooldown itself stays at the user's 0.05-0.20s.
            if (explorationJumpCooldownTicks == 0 && context.Grounded &&
                !context.ObstacleAhead && context.EdgeDistance > 50.0)
            {
                double hopChance = 0.045 + traitPlayfulness * 0.075 +
                    playfulness * 0.075 + restlessness * 0.045 +
                    confidence * 0.025;
                if (Behavior == DesktopBehavior.Play) hopChance += 0.07;
                if (mood == AIMood.Playful) hopChance += 0.055;
                explorationJumpRequested = random.NextDouble() <
                    MathUtil.Clamp(hopChance, 0.03, 0.34);
            }
            else explorationJumpRequested = false;
        }

        private AIDestinationKind ChooseDestinationKind(UtilityContext context)
        {
            double nearby = 0.22 + traitPatience * 0.12;
            double farSide = 0.08 + activity * 0.20 + boredom * 0.20 +
                traitActivity * 0.12;
            double center = 0.07 + traitRest * 0.08;
            double edge = 0.04 + confidence * 0.13 + traitObservation * 0.08;
            double unvisited = 0.13 + curiosity * 0.25 + boredom * 0.17;
            double mouseNear = context.MouseAttentionActive
                ? 0.02 + traitMouseInterest * 0.20 + traitMouseApproach * 0.25 : 0.0;
            double mouseAway = context.MouseAttentionActive
                ? 0.02 + traitMouseInterest * 0.09 + (1.0 - traitMouseApproach) * 0.24 : 0.0;
            double wander = 0.16 + traitImpulsiveness * 0.10;

            if (aiSlugcatId == SlugcatId.Gourmand) nearby += 0.14;
            if (aiSlugcatId == SlugcatId.Red) farSide += 0.13;
            if (aiSlugcatId == SlugcatId.Rivulet) farSide += 0.12;
            if (aiSlugcatId == SlugcatId.Saint) edge += 0.10;
            if (aiSlugcatId == SlugcatId.SpearMaster) unvisited += 0.08;
            if (aiSlugcatId == SlugcatId.Yellow && context.MouseAttentionActive)
                mouseNear += 0.12;

            double total = nearby + farSide + center + edge + unvisited +
                mouseNear + mouseAway + wander;
            double pick = random.NextDouble() * total;
            if ((pick -= nearby) < 0.0) return AIDestinationKind.Nearby;
            if ((pick -= farSide) < 0.0) return AIDestinationKind.FarSide;
            if ((pick -= center) < 0.0) return AIDestinationKind.Center;
            if ((pick -= edge) < 0.0) return AIDestinationKind.Edge;
            if ((pick -= unvisited) < 0.0) return AIDestinationKind.Unvisited;
            if ((pick -= mouseNear) < 0.0) return AIDestinationKind.MouseNear;
            if ((pick -= mouseAway) < 0.0) return AIDestinationKind.MouseAway;
            return AIDestinationKind.Wander;
        }

        private double ChooseDestinationX(AIDestinationKind kind, double centerX,
            double left, double right, MouseTracker mouse, UtilityContext context)
        {
            double width = right - left;
            switch (kind)
            {
                case AIDestinationKind.Nearby:
                    {
                        double distance = MathUtil.Lerp(24.0,
                            Math.Min(120.0, width * 0.28), random.NextDouble());
                        return centerX + desiredDirection * distance;
                    }
                case AIDestinationKind.FarSide:
                    return centerX < (left + right) * 0.5
                        ? MathUtil.Lerp(right - width * 0.12, right, random.NextDouble())
                        : MathUtil.Lerp(left, left + width * 0.12, random.NextDouble());
                case AIDestinationKind.Center:
                    return (left + right) * 0.5 +
                        (random.NextDouble() - 0.5) * Math.Min(70.0, width * 0.12);
                case AIDestinationKind.Edge:
                    return desiredDirection > 0
                        ? right - Math.Min(18.0, width * 0.04)
                        : left + Math.Min(18.0, width * 0.04);
                case AIDestinationKind.Unvisited:
                    return PickLeastVisitedX(centerX, left, right);
                case AIDestinationKind.MouseNear:
                    return MathUtil.Clamp(mouse.Position.X +
                        (random.NextDouble() - 0.5) * 55.0, left, right);
                case AIDestinationKind.MouseAway:
                    return mouse.Position.X < centerX ? right : left;
                default:
                    {
                        // Wander favors the current direction without forcing it.
                        double t = random.NextDouble();
                        if (random.NextDouble() < 0.62 + traitDirectionPersistence * 0.25)
                        {
                            if (desiredDirection > 0)
                                t = MathUtil.Lerp(MathUtil.InverseLerp(left, right, centerX),
                                    1.0, 0.25 + random.NextDouble() * 0.75);
                            else
                                t = MathUtil.Lerp(0.0,
                                    MathUtil.InverseLerp(left, right, centerX),
                                    random.NextDouble() * 0.75);
                        }
                        return MathUtil.Lerp(left, right, t);
                    }
            }
        }

        private double PickLeastVisitedX(double centerX, double left, double right)
        {
            double bestX = centerX;
            double bestScore = double.MinValue;
            for (int i = 0; i < 7; i++)
            {
                double x = MathUtil.Lerp(left, right, (i + 0.5) / 7.0);
                double score = Math.Abs(x - centerX) * 0.35;
                if (recentTargetCount == 0) score += Math.Abs(x - centerX);
                for (int j = 0; j < recentTargetCount; j++)
                    score += Math.Min(160.0, Math.Abs(x - recentTargetX[j])) * 0.18;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = x;
                }
            }
            return bestX;
        }

        private void RememberTarget(double x)
        {
            recentTargetX[recentTargetCursor] = x;
            recentTargetCursor = (recentTargetCursor + 1) % recentTargetX.Length;
            if (recentTargetCount < recentTargetX.Length) recentTargetCount++;
        }

        private int TravelCommitmentTicks()
        {
            // 약 1~7초 정도 한 방향을 고집한다. 실제 지속시간은
            // 방향 지속성 + 약간의 개체별 랜덤으로 계속 달라진다.
            int minimum = 40 + (int)Math.Round(traitDirectionPersistence * 55.0);
            int extra = 35 + (int)Math.Round(traitDirectionPersistence * 170.0);
            return minimum + random.Next(0, extra + 1);
        }

        private void MaybeChangeTravelDirection(bool force)
        {
            if (force)
            {
                desiredDirection = -desiredDirection;
                directionCommitmentTicks = TravelCommitmentTicks();
                return;
            }

            if (directionCommitmentTicks > 0) return;

            // 산만하거나 긴장한 개체는 약간 더 자주 방향을 바꾸지만,
            // 50:50 즉시 반전은 하지 않는다.
            double reverseChance = 0.035 + traitImpulsiveness * 0.16 +
                restlessness * 0.12 + playfulness * 0.06 -
                traitDirectionPersistence * 0.10;
            reverseChance = Math.Max(0.03, Math.Min(0.34, reverseChance));
            if (random.NextDouble() < reverseChance)
                desiredDirection = -desiredDirection;

            directionCommitmentTicks = TravelCommitmentTicks();
        }

        private void UpdateObstacleResponse(Slugcat slugcat,
            DesktopCollisionWorld world, UtilityContext context)
        {
            if (obstacleJumpAttemptTicks <= 0) return;
            obstacleJumpAttemptTicks--;
            if (!context.Grounded)
            {
                obstacleJumpWasAirborne = true;
                return;
            }
            if (!context.ObstacleAhead)
            {
                obstacleJumpAttemptTicks = 0;
                return;
            }
            if (!obstacleJumpWasAirborne && obstacleJumpAttemptTicks > 0) return;

            // The jump did not clear its blocking wall. SlugNPCAI abandons a
            // failed connection and evaluates another destination rather
            // than continuing to run into the same collision.
            SelectObstacleBypass(slugcat, world, obstacleJumpDirection);
            obstacleJumpAttemptTicks = 0;
            obstacleJumpWasAirborne = false;
            if (Behavior != DesktopBehavior.Explore)
                RecordRecentBehavior(DesktopBehavior.Explore);
            Behavior = DesktopBehavior.Explore;
            behaviorTicks = 0;
            AssignIntentTiming(DesktopBehavior.Explore);
            freeRoamRetargetCountdown = 0;
        }

        private void SelectObstacleBypass(Slugcat slugcat,
            DesktopCollisionWorld world, int obstacleDirection)
        {
            DesktopSurface surface;
            if (!world.TryGetSurface(slugcat.PrimarySupportingSurfaceId,
                slugcat.PrimarySupportingSurfaceKind, out surface) ||
                !surface.IsHorizontal)
                return;

            double width = surface.Right - surface.Left;
            double margin = Math.Min(80.0, width * 0.18);
            if (margin < 12.0) margin = 12.0;
            double left = surface.Left + margin;
            double right = surface.Right - margin;
            if (right <= left) return;

            explorationSurfaceId = surface.Id;
            explorationTargetX = obstacleDirection > 0 ? left : right;
            explorationTargetTicks = 160 + random.Next(0, 161);
            desiredDirection = obstacleDirection > 0 ? -1 : 1;
        }

        private VirtualInput ProduceInput(Slugcat slugcat, MouseTracker mouse,
            UtilityContext context)
        {
            if (obstacleJumpAttemptTicks > 0 && context.ObstacleAhead &&
                Behavior != DesktopBehavior.Jump)
                return VirtualInput.Neutral;

            int towardMouse = mouse.Position.X < slugcat.Center.X ? -1 : 1;
            switch (Behavior)
            {
                case DesktopBehavior.Walk:
                case DesktopBehavior.Run:
                case DesktopBehavior.Explore:
                    return new VirtualInput(desiredDirection, 0, false, false);
                case DesktopBehavior.Crawl:
                    return new VirtualInput(desiredDirection, 1, false, false);
                case DesktopBehavior.Play:
                    // Play remains an intent; actual motion still goes through the
                    // existing movement system. Short targets/micro-actions make it
                    // visually distinct without inventing a new speed or physics path.
                    return new VirtualInput(desiredDirection, 0, false, false);
                case DesktopBehavior.TurnAround:
                    return new VirtualInput(desiredDirection, 0, false, false);
                case DesktopBehavior.FollowMouse:
                    if (!context.MouseAttentionActive) return VirtualInput.Neutral;
                    return new VirtualInput(towardMouse, 0, context.Grounded &&
                        mouse.Position.Y < slugcat.Center.Y - 80.0, false);
                case DesktopBehavior.AvoidMouse:
                    if (!context.MouseAttentionActive) return VirtualInput.Neutral;
                    return new VirtualInput(-towardMouse, 0, context.Grounded &&
                        context.MouseDistance < 55.0, false);
                case DesktopBehavior.Jump:
                    return new VirtualInput(desiredDirection, 0,
                        behaviorTicks <= jumpHoldTicks, false);
                case DesktopBehavior.ClimbWindow:
                    int wallDirection = slugcat.BodyChunks[0].ContactRight ||
                        slugcat.BodyChunks[1].ContactRight ? 1 :
                        (slugcat.BodyChunks[0].ContactLeft ||
                         slugcat.BodyChunks[1].ContactLeft ? -1 : lastWallDirection);
                    return new VirtualInput(wallDirection, -1, false, false);
                case DesktopBehavior.DropDown:
                    return new VirtualInput(slugcat.State.Facing, 1, false, false,
                        VirtualPosture.None, true);
                case DesktopBehavior.BalanceNearEdge:
                    return new VirtualInput(context.SaferDirection, 1, false, false);
                case DesktopBehavior.Sit:
                    return new VirtualInput(0, 0, false, false, VirtualPosture.Sit);
                case DesktopBehavior.Sleep:
                    return new VirtualInput(0, 0, false, false, VirtualPosture.Sleep);
                default:
                    return VirtualInput.Neutral;
            }
        }

        private void UpdateMicroBehavior(Slugcat slugcat, MouseTracker mouse,
            UtilityContext context)
        {
            if (microTicksRemaining > 0)
            {
                microTicksRemaining--;
                if (microTicksRemaining == 0)
                {
                    microBehavior = MicroBehavior.None;
                    microCooldownTicks = SecondsToTicks(SampleCentered(0.7, 3.4) *
                        MathUtil.Lerp(1.15, 0.68, traitImpulsiveness));
                }
                return;
            }

            if (microCooldownTicks > 0 || !context.Grounded ||
                !slugcat.State.Conscious || slugcat.State.Dead ||
                Behavior == DesktopBehavior.Jump ||
                Behavior == DesktopBehavior.ClimbWindow ||
                Behavior == DesktopBehavior.DropDown ||
                Behavior == DesktopBehavior.AvoidMouse ||
                Behavior == DesktopBehavior.FollowMouse ||
                Behavior == DesktopBehavior.Sleep ||
                Behavior == DesktopBehavior.ExplosiveJump ||
                Behavior == DesktopBehavior.TongueSwing ||
                Behavior == DesktopBehavior.MakeSpear ||
                Behavior == DesktopBehavior.ThrowSpear)
                return;

            // Micro-actions have their own small utility selection. 'None' is a real
            // candidate so the AI does not decorate every available moment.
            double none = 0.42 + traitPatience * 0.12;
            double pause = 0.05 + attentionDrive * 0.08 + sleepiness * 0.07;
            double opposite = 0.025 + curiosity * 0.055 +
                traitImpulsiveness * 0.045;
            double crouch = 0.018 + traitRest * 0.055 + sleepiness * 0.065;
            double mouseGlance = context.MouseDistance < 280.0
                ? 0.018 + traitMouseInterest * 0.085 + attentionDrive * 0.045 : 0.0;
            double hesitate = (context.EdgeDistance < 60.0 || context.ObstacleAhead)
                ? 0.06 + (1.0 - confidence) * 0.10 + personalityNervous * 0.07 : 0.012;
            double fakeJump = 0.014 + playfulness * 0.055 +
                traitPlayfulness * 0.045;
            if (mood == AIMood.Playful) fakeJump += 0.05;
            if (mood == AIMood.Focused) mouseGlance += 0.035;

            double total = none + pause + opposite + crouch + mouseGlance +
                hesitate + fakeJump;
            double pick = random.NextDouble() * total;
            if ((pick -= none) < 0.0)
            {
                microCooldownTicks = SecondsToTicks(SampleCentered(0.45, 2.2));
                return;
            }
            if ((pick -= pause) < 0.0) microBehavior = MicroBehavior.BriefPause;
            else if ((pick -= opposite) < 0.0) microBehavior = MicroBehavior.LookOpposite;
            else if ((pick -= crouch) < 0.0) microBehavior = MicroBehavior.BriefCrouch;
            else if ((pick -= mouseGlance) < 0.0) microBehavior = MicroBehavior.MouseGlance;
            else if ((pick -= hesitate) < 0.0) microBehavior = MicroBehavior.Hesitate;
            else microBehavior = MicroBehavior.FakeJump;

            switch (microBehavior)
            {
                case MicroBehavior.BriefPause:
                    microTicksRemaining = SecondsToTicks(SampleCentered(0.12, 0.65));
                    break;
                case MicroBehavior.LookOpposite:
                    microTicksRemaining = SecondsToTicks(SampleCentered(0.15, 0.75));
                    break;
                case MicroBehavior.BriefCrouch:
                    microTicksRemaining = SecondsToTicks(SampleCentered(0.22, 0.90));
                    break;
                case MicroBehavior.MouseGlance:
                    microTicksRemaining = SecondsToTicks(SampleCentered(0.25, 1.25));
                    break;
                case MicroBehavior.Hesitate:
                    microTicksRemaining = SecondsToTicks(SampleCentered(0.16, 0.70));
                    break;
                default:
                    microTicksRemaining = SecondsToTicks(SampleCentered(0.14, 0.48));
                    break;
            }
        }

        private VirtualInput ApplyMicroBehavior(VirtualInput input, Slugcat slugcat,
            UtilityContext context)
        {
            if (microTicksRemaining <= 0) return input;
            switch (microBehavior)
            {
                case MicroBehavior.BriefPause:
                case MicroBehavior.Hesitate:
                case MicroBehavior.FakeJump:
                    return VirtualInput.Neutral;
                case MicroBehavior.BriefCrouch:
                    return new VirtualInput(0, 1, false, false);
                case MicroBehavior.LookOpposite:
                    // Head-only glance. A real body reversal is represented by the
                    // TurnAround intent so it does not create a one-tick flip-flop.
                    return input;
                default:
                    return input;
            }
        }

        private void UpdateAttention(Slugcat slugcat, MouseTracker mouse,
            UtilityContext context, MouseAttentionState mouseAttention)
        {
            if (!originalAttentionInitialized)
            {
                originalAttentionInitialized = true;
                originalAttentionKind = AttentionKind.RandomPoint;
                originalAttentionTarget = slugcat.Center +
                    new Vec2(slugcat.State.Facing * 60.0, -20.0);
            }

            if (Behavior == DesktopBehavior.BalanceNearEdge)
            {
                originalAttentionKind = AttentionKind.ScreenEdge;
                originalAttentionTarget = slugcat.Center +
                    new Vec2(slugcat.State.Facing * context.EdgeDistance, 12.0);
            }
            else if (Behavior == DesktopBehavior.ObserveWindow)
            {
                originalAttentionKind = AttentionKind.Window;
                originalAttentionTarget = slugcat.Center +
                    new Vec2(slugcat.State.Facing * 90.0, 45.0);
            }
            else if (microBehavior == MicroBehavior.LookOpposite &&
                microTicksRemaining > 0)
            {
                originalAttentionKind = AttentionKind.RandomPoint;
                originalAttentionTarget = slugcat.Center +
                    new Vec2(-slugcat.State.Facing * 85.0, -12.0);
            }
            else if (microBehavior == MicroBehavior.FakeJump &&
                microTicksRemaining > 0)
            {
                originalAttentionKind = AttentionKind.RandomPoint;
                originalAttentionTarget = slugcat.Center +
                    new Vec2(slugcat.State.Facing * 35.0, -90.0);
            }
            else if (--attentionRetargetCountdown <= 0)
            {
                double reach = MathUtil.Lerp(75.0, 155.0, curiosity);
                double x = (random.NextDouble() * 2.0 - 1.0) * reach;
                double y = (random.NextDouble() * 2.0 - 1.0) *
                    MathUtil.Lerp(40.0, 85.0, traitObservation);
                originalAttentionKind = AttentionKind.RandomPoint;
                originalAttentionTarget = slugcat.Center + new Vec2(x, y);

                double seconds = MathUtil.Lerp(0.65, 4.5,
                    traitPatience * 0.55 + traitAttention * 0.45);
                if (Behavior == DesktopBehavior.LookAround) seconds *= 0.48;
                if (mood == AIMood.Focused) seconds *= 1.30;
                attentionRetargetCountdown = SecondsToTicks(seconds *
                    (0.70 + random.NextDouble() * 0.60));
            }

            // Existing click-to-look always wins. Passive mouse glances never set
            // MouseAttentionActive, so they do not steal the interaction state.
            MouseAttentionActive = IsMouseAttentionActive(slugcat, mouseAttention);
            if (MouseAttentionActive)
                Attention.SetTarget(AttentionKind.Mouse, mouse.Position);
            else if (microBehavior == MicroBehavior.MouseGlance &&
                microTicksRemaining > 0)
                Attention.SetTarget(AttentionKind.Mouse, mouse.Position);
            else
                Attention.SetTarget(originalAttentionKind, originalAttentionTarget);
        }

        private static bool IsMouseAttentionActive(Slugcat slugcat,
            MouseAttentionState mouseAttention)
        {
            return slugcat.State.Conscious && !slugcat.State.Dead &&
                slugcat.State.StunCounter < 1 && mouseAttention != null &&
                mouseAttention.IsActive;
        }

    }
}
