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
        private double fatigue = 0.18;
        private double curiosity = 0.65;
        private int jumpCooldownTicks;
        private int dropCooldownTicks;
        private int restCooldownTicks;
        private int idlePostureCheckCountdown;
        private int idleRestTicks;
        private int wallContactGraceTicks;
        private int lastWallDirection = 1;
        private readonly double personalityEnergy;
        private readonly double personalityNervous;
        private readonly double personalityAggression;
        private readonly double personalityBravery;
        private readonly double personalityDominance;
        private readonly int routePreferenceSalt;
        private int routeMemoryTicks;
        private long recentTransitionSourceSurfaceId;
        private long recentTransitionTargetSurfaceId;
        private int attentionRetargetCountdown;
        private bool specialTransitionArmed;
        private long specialTransitionTargetSurfaceId;
        private bool originalAttentionInitialized;
        private AttentionKind originalAttentionKind;
        private Vec2 originalAttentionTarget;
        private SpearmasterActionState spearmasterState;
        private int spearmasterStateTicks;
        private int spearmasterIdleDuration = 80;
        private int spearmasterMoveDuration = 35;
        private int spearmasterRecoveryDuration = 110;
        private int spearmasterAutonomousThrowCountdown;
        private bool spearmasterAutonomousThrow;
        private Vec2 spearmasterTarget;

        public DesktopPetAI(int seed)
            : this(seed, 0)
        {
        }

        public DesktopPetAI(int seed, int evaluationPhase)
        {
            if (evaluationPhase < 0) throw new ArgumentOutOfRangeException("evaluationPhase");
            random = new Random(seed);
            // SlugNPCAI reads AbstractCreature.Personality (energy, nervous
            // and aggression) instead of sharing one behavior profile among
            // every NPC.  The desktop has no AbstractCreature, so retain one
            // deterministic personality per AI instance.
            Random personalityRandom = new Random(unchecked(seed ^ 0x51A7C3D));
            personalityEnergy = personalityRandom.NextDouble();
            personalityNervous = personalityRandom.NextDouble();
            personalityAggression = personalityRandom.NextDouble();
            personalityBravery = personalityRandom.NextDouble();
            personalityDominance = personalityRandom.NextDouble();
            routePreferenceSalt = personalityRandom.Next();
            fatigue = MathUtil.Lerp(0.30, 0.08, personalityEnergy);
            curiosity = MathUtil.Lerp(0.36, 0.86,
                MathUtil.Clamp01((personalityEnergy + (1.0 - personalityNervous)) * 0.5));
            desiredDirection = random.Next(0, 2) == 0 ? -1 : 1;
            attentionRetargetCountdown = 40 + random.Next(0, 80);
            idlePostureCheckCountdown = 120 + random.Next(0, 121);
            // Avoid making every spawned Slugcat scan all desktop surfaces on
            // the same 40 Hz tick. The first still evaluates immediately.
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

        public VirtualInput Step(Slugcat slugcat, DesktopCollisionWorld world, MouseTracker mouse)
        {
            return Step(slugcat, world, mouse, null);
        }

        public VirtualInput Step(Slugcat slugcat, DesktopCollisionWorld world, MouseTracker mouse,
            MouseAttentionState mouseAttention)
        {
            behaviorTicks++;
            if (jumpCooldownTicks > 0) jumpCooldownTicks--;
            if (dropCooldownTicks > 0) dropCooldownTicks--;
            if (restCooldownTicks > 0) restCooldownTicks--;
            if (idlePostureCheckCountdown > 0) idlePostureCheckCountdown--;
            if (routeMemoryTicks > 0) routeMemoryTicks--;
            fatigue = MathUtil.Clamp01(fatigue + FatigueDelta(Behavior));
            curiosity = MathUtil.Clamp01(curiosity + CuriosityDelta(Behavior));

            UtilityContext context = BuildContext(slugcat, world, mouse);
            LastContext = context;
            if (--evaluationCountdown <= 0)
            {
                evaluationCountdown = 8 + random.Next(0, 8);
                PlanPlatformTransition(slugcat, world);
                context.TransitionAvailable = TransitionPlan.IsValid;
                SelectBehavior(context);
            }
            else context.TransitionAvailable = TransitionPlan.IsValid;
            ApplyOriginalIdlePosture(slugcat, context);

            VirtualInput input = ProduceInput(slugcat, mouse, context);
            VirtualInput abilityInput;
            if (TryProduceAbilityInput(slugcat, mouse, mouseAttention, context,
                out abilityInput)) input = abilityInput;
            UpdateAttention(slugcat, mouse, context, mouseAttention);
            Attention.Step();
            return input;
        }

        public PlatformTransitionPlan PlanPlatformTransition(Slugcat slugcat,
            DesktopCollisionWorld world)
        {
            TransitionPlan.Clear();
            Vec2 center = slugcat.Center;
            double maximumRange = slugcat.AbilityController is SaintAbilityController
                ? 195.0 : (slugcat.AbilityController is ArtificerAbilityController ? 250.0 : 105.0);
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

            ArtificerAbilityController artificer =
                slugcat.AbilityController as ArtificerAbilityController;
            if (artificer == null)
            {
                specialTransitionArmed = false;
                return false;
            }

            // Player.ClassMechanicsArtificer only reacts to a real Jump +
            // Pickup request after the normal jump grace has elapsed.  Arm a
            // special jump while taking a planned long transition, then send
            // one matching input pulse; never synthesize one from a modulo
            // timer while the cat happens to be airborne.
            if (context.Grounded && Behavior == DesktopBehavior.Jump &&
                behaviorTicks <= 8 && RequiresExplosiveTransition())
            {
                specialTransitionArmed = true;
                specialTransitionTargetSurfaceId = TransitionPlan.TargetSurfaceId;
                return false;
            }
            if (specialTransitionArmed &&
                (!TransitionPlan.IsValid ||
                 TransitionPlan.TargetSurfaceId != specialTransitionTargetSurfaceId))
            {
                specialTransitionArmed = false;
            }
            if (specialTransitionArmed && !context.Grounded &&
                slugcat.State.CanJump <= 0 && slugcat.State.Conscious)
            {
                int direction = TransitionPlan.HorizontalDistance < 0.0 ? -1 : 1;
                specialTransitionArmed = false;
                Behavior = DesktopBehavior.ExplosiveJump;
                input = new VirtualInput(direction, -1, true, true);
                return true;
            }
            return false;
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
                (Math.Abs(TransitionPlan.HorizontalDistance) >= 64.0 ||
                 TransitionPlan.VerticalDistance <= -24.0);
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
                        spearmasterIdleDuration = 55 + random.Next(0, 85);
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
                spearmasterAutonomousThrowCountdown = 180 + random.Next(0, 121);
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

        private void ApplyOriginalIdlePosture(Slugcat slugcat, UtilityContext context)
        {
            if (idleRestTicks > 0)
            {
                idleRestTicks--;
                if (idleRestTicks == 0) slugcat.State.Standing = true;
                return;
            }
            // SlugNPCAI.Move changes standing state only after reaching its
            // idle destination. A desktop has no room tile destination, so
            // require sustained stillness and rate-limit the equivalent
            // check; running the original probability on every idle tick
            // made low-energy cats remain prone almost continuously.
            if (Behavior != DesktopBehavior.Idle || !context.Grounded ||
                context.Stillness < 0.9 || idlePostureCheckCountdown > 0) return;
            idlePostureCheckCountdown = 160 + random.Next(0, 161);
            if (random.NextDouble() < MathUtil.InverseLerp(0.35, 0.0,
                personalityEnergy) * 0.01)
            {
                slugcat.State.Standing = false;
                idleRestTicks = 80 + random.Next(0, 161);
            }
            else if (random.NextDouble() < MathUtil.InverseLerp(0.65, 1.0,
                personalityEnergy) * 0.01)
            {
                slugcat.State.Standing = true;
            }
        }

        private UtilityContext BuildContext(Slugcat slugcat, DesktopCollisionWorld world, MouseTracker mouse)
        {
            UtilityContext context = new UtilityContext();
            context.Grounded = slugcat.State.Grounded;
            bool contactRight = slugcat.BodyChunks[0].ContactRight || slugcat.BodyChunks[1].ContactRight;
            bool contactLeft = slugcat.BodyChunks[0].ContactLeft || slugcat.BodyChunks[1].ContactLeft;
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
            context.OnWindow = slugcat.PrimarySupportingSurfaceId > 0;
            context.MouseDistance = Vec2.Distance(slugcat.Center, mouse.Position);
            context.MouseSpeed = mouse.Velocity.Length;
            context.Fatigue = fatigue;
            context.Curiosity = curiosity;
            context.Stillness = slugcat.State.Stillness;
            context.BehaviorAgeSeconds = behaviorTicks * SimulationConstants.LogicStepSeconds;
            context.JumpReady = jumpCooldownTicks == 0;
            context.DropReady = dropCooldownTicks == 0;
            context.RestReady = restCooldownTicks == 0;
            context.PersonalityEnergy = personalityEnergy;
            context.PersonalityNervous = personalityNervous;
            context.PersonalityAggression = personalityAggression;
            context.PersonalityBravery = personalityBravery;
            context.PersonalityDominance = personalityDominance;
            double leftEdge = world.DistanceToEdge(slugcat.Center, -1, slugcat.PrimarySupportingSurfaceId);
            double rightEdge = world.DistanceToEdge(slugcat.Center, 1, slugcat.PrimarySupportingSurfaceId);
            context.SaferDirection = leftEdge >= rightEdge ? -1 : 1;
            context.EdgeDistance = slugcat.State.Facing < 0 ? leftEdge : rightEdge;
            return context;
        }

        private void SelectBehavior(UtilityContext context)
        {
            int minimumTicks = MinimumTicks(Behavior);
            bool urgentAvoid = Behavior != DesktopBehavior.AvoidMouse && context.MouseDistance < 55.0;
            bool urgentClimb = Behavior != DesktopBehavior.ClimbWindow && context.WallContact && !context.Grounded;
            if (behaviorTicks < minimumTicks && !urgentAvoid && !urgentClimb)
            {
                return;
            }

            DesktopBehavior best = Behavior;
            double currentScore = UtilityEvaluator.Score(Behavior, context, 0.08); // hysteresis
            double bestScore = currentScore;
            Array values = Enum.GetValues(typeof(DesktopBehavior));
            for (int i = 0; i < values.Length; i++)
            {
                DesktopBehavior candidate = (DesktopBehavior)values.GetValue(i);
                double variation = (random.NextDouble() - 0.5) * 0.12;
                double score = UtilityEvaluator.Score(candidate, context, variation);
                if (score > bestScore + 0.07)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            if (best != Behavior)
            {
                Behavior = best;
                behaviorTicks = 0;
                if (best == DesktopBehavior.Walk || best == DesktopBehavior.Explore)
                {
                    desiredDirection = random.Next(0, 2) == 0 ? -1 : 1;
                }
                if (best == DesktopBehavior.Jump && TransitionPlan.IsValid &&
                    Math.Abs(TransitionPlan.HorizontalDistance) > 0.001)
                {
                    // MovementConnection.direction is the source of NPC jump
                    // input in the game DLL. A random stale direction here
                    // was the cause of mirrored, repeated left/right jumps.
                    desiredDirection = TransitionPlan.HorizontalDistance < 0.0 ? -1 : 1;
                }
                if (best == DesktopBehavior.Jump)
                {
                    jumpCooldownTicks = 240;
                    RememberTransition(TransitionPlan);
                }
                if (best == DesktopBehavior.DropDown) dropCooldownTicks = 400;
                if (best == DesktopBehavior.Sit) restCooldownTicks = 520;
                if (best == DesktopBehavior.Sleep) restCooldownTicks = 1200;
            }
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

        private VirtualInput ProduceInput(Slugcat slugcat, MouseTracker mouse, UtilityContext context)
        {
            int towardMouse = mouse.Position.X < slugcat.Center.X ? -1 : 1;
            switch (Behavior)
            {
                case DesktopBehavior.Walk:
                case DesktopBehavior.Explore:
                    if (context.EdgeDistance < 24.0)
                    {
                        desiredDirection = context.SaferDirection;
                    }
                    return new VirtualInput(desiredDirection, 0, false, false);
                case DesktopBehavior.FollowMouse:
                    return new VirtualInput(towardMouse, 0, context.Grounded && mouse.Position.Y < slugcat.Center.Y - 80.0, false);
                case DesktopBehavior.AvoidMouse:
                    return new VirtualInput(-towardMouse, 0, context.Grounded && context.MouseDistance < 55.0, false);
                case DesktopBehavior.Jump:
                    return new VirtualInput(desiredDirection, 0, behaviorTicks <= 8, false);
                case DesktopBehavior.ClimbWindow:
                    int wallDirection = slugcat.BodyChunks[0].ContactRight || slugcat.BodyChunks[1].ContactRight
                        ? 1
                        : (slugcat.BodyChunks[0].ContactLeft || slugcat.BodyChunks[1].ContactLeft ? -1 : lastWallDirection);
                    return new VirtualInput(wallDirection, -1, false, false);
                case DesktopBehavior.DropDown:
                    return new VirtualInput(slugcat.State.Facing, 1, false, false, VirtualPosture.None, true);
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

        private void UpdateAttention(Slugcat slugcat, MouseTracker mouse, UtilityContext context,
            MouseAttentionState mouseAttention)
        {
            if (!originalAttentionInitialized)
            {
                originalAttentionInitialized = true;
                originalAttentionKind = AttentionKind.RandomPoint;
                originalAttentionTarget = slugcat.Center + new Vec2(slugcat.State.Facing * 60.0, -20.0);
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
            else if (--attentionRetargetCountdown <= 0)
            {
                double x = (random.NextDouble() * 2.0 - 1.0) * 130.0;
                double y = (random.NextDouble() * 2.0 - 1.0) * 70.0;
                originalAttentionKind = AttentionKind.RandomPoint;
                originalAttentionTarget = slugcat.Center + new Vec2(x, y);
                attentionRetargetCountdown = 40 + random.Next(0, 160);
            }

            MouseAttentionActive = slugcat.State.Conscious && !slugcat.State.Dead &&
                slugcat.State.StunCounter < 1 &&
                mouseAttention != null && mouseAttention.IsActive;
            if (MouseAttentionActive)
                Attention.SetTarget(AttentionKind.Mouse, mouse.Position);
            else
                Attention.SetTarget(originalAttentionKind, originalAttentionTarget);
        }

        private static int MinimumTicks(DesktopBehavior behavior)
        {
            switch (behavior)
            {
                case DesktopBehavior.Sleep: return 320;
                case DesktopBehavior.Sit: return 100;
                case DesktopBehavior.Explore: return 80;
                case DesktopBehavior.ObserveWindow: return 90;
                case DesktopBehavior.Jump: return 16;
                case DesktopBehavior.AvoidMouse: return 18;
                case DesktopBehavior.ExplosiveJump:
                case DesktopBehavior.TongueSwing: return 16;
                case DesktopBehavior.MakeSpear: return 55;
                case DesktopBehavior.ThrowSpear: return 10;
                case DesktopBehavior.GourmandRoll: return 24;
                default: return 45;
            }
        }

        private static double FatigueDelta(DesktopBehavior behavior)
        {
            if (behavior == DesktopBehavior.Sleep) return -0.0045;
            if (behavior == DesktopBehavior.Sit || behavior == DesktopBehavior.Idle) return -0.0007;
            return 0.00055;
        }

        private static double CuriosityDelta(DesktopBehavior behavior)
        {
            if (behavior == DesktopBehavior.Explore || behavior == DesktopBehavior.FollowMouse || behavior == DesktopBehavior.ObserveWindow)
            {
                return -0.0016;
            }

            return 0.00075;
        }
    }
}
