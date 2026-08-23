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
        ObserveWindow
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
        private int wallContactGraceTicks;
        private int lastWallDirection = 1;
        private bool originalAttentionInitialized;
        private AttentionKind originalAttentionKind;
        private Vec2 originalAttentionTarget;

        public DesktopPetAI(int seed)
        {
            random = new Random(seed);
            Attention = new AttentionSystem();
            Behavior = DesktopBehavior.Idle;
        }

        public DesktopBehavior Behavior { get; private set; }
        public AttentionSystem Attention { get; private set; }
        public UtilityContext LastContext { get; private set; }
        public AttentionKind OriginalAttentionKind { get { return originalAttentionKind; } }
        public Vec2 OriginalAttentionTarget { get { return originalAttentionTarget; } }
        public bool MouseAttentionActive { get; private set; }

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
            fatigue = MathUtil.Clamp01(fatigue + FatigueDelta(Behavior));
            curiosity = MathUtil.Clamp01(curiosity + CuriosityDelta(Behavior));

            UtilityContext context = BuildContext(slugcat, world, mouse);
            LastContext = context;
            if (--evaluationCountdown <= 0)
            {
                evaluationCountdown = 8 + random.Next(0, 8);
                SelectBehavior(context);
            }

            VirtualInput input = ProduceInput(slugcat, mouse, context);
            UpdateAttention(slugcat, mouse, context, mouseAttention);
            Attention.Step();
            return input;
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
                if (best == DesktopBehavior.Jump) jumpCooldownTicks = 240;
                if (best == DesktopBehavior.DropDown) dropCooldownTicks = 400;
            }
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
                    return new VirtualInput(wallDirection, -1, behaviorTicks % 12 < 3, false);
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
            else if (behaviorTicks % 80 == 1)
            {
                double x = (random.NextDouble() * 2.0 - 1.0) * 130.0;
                double y = (random.NextDouble() * 2.0 - 1.0) * 70.0;
                originalAttentionKind = AttentionKind.RandomPoint;
                originalAttentionTarget = slugcat.Center + new Vec2(x, y);
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
