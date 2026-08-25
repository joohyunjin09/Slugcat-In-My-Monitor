using System;
using System.Collections.Generic;
using RainWorldDesktopPet.AI;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Graphics;
using RainWorldDesktopPet.Physics;

namespace RainWorldDesktopPet.Core
{
    public enum FoodInteractionState
    {
        None,
        Seeking,
        Holding,
        Eating
    }

    // Food is owned by one GameLoop. That ownership is the reservation: two
    // desktop pets never race for one item and no extra composition surface is
    // needed. A future shared-food mode can replace this policy at this seam.
    public sealed class DesktopFoodManager
    {
        public const int MaximumActiveFoods = 5;
        public const double MaximumFullness = 3.0;
        public const int DigestionTicksPerFoodPoint = 3600;
        private const double ApproachDistance = 17.0;
        private const double PickupDistance = 25.0;
        private const double PickupVerticalTolerance = 32.0;
        private const int HoldBeforeBitingTicks = 8;
        private const int BiteIntervalTicks = 18;
        // One original-style bite cycle is a held/raised pose, a short snap to
        // the mouth, then a snap back to the held pose before the next bite.
        private const int BiteMouthStartTick = 12;
        private const int BiteMouthEndTick = 9;
        private const int BiteFaceTicks = 2;
        private const double FoodHandReachDistance = 34.0;
        private const double SeekingHandBlend = 0.34;

        private readonly List<DesktopFood> foods = new List<DesktopFood>(MaximumActiveFoods);
        private readonly IList<DesktopFood> foodView;
        private readonly Random random;
        private DesktopFood target;
        private int interactionCountdown;
        private int biteFaceTicks;
        private double fullness;

        public DesktopFoodManager()
            : this(Environment.TickCount)
        {
        }

        public DesktopFoodManager(int randomSeed)
        {
            foodView = foods.AsReadOnly();
            random = new Random(randomSeed);
        }

        public IList<DesktopFood> Foods { get { return foodView; } }
        public DesktopFood Target { get { return target; } }
        public FoodInteractionState InteractionState { get; private set; }
        public int FoodPointsEaten { get; private set; }
        public int TotalBites { get; private set; }
        public string LastEvent { get; private set; }
        public bool LastSpawnAccepted { get; private set; }
        public double Fullness { get { return fullness; } }
        public double FullnessRatio { get { return fullness / MaximumFullness; } }

        public bool TryAddDangleFruit(Vec2 position)
        {
            RemoveInactive();
            if (foods.Count >= MaximumActiveFoods) return false;
            DesktopFood fruit = new DesktopFood(DesktopFoodKind.DangleFruit, position);
            foods.Add(fruit);
            LastEvent = "DangleFruit_Spawn";
            return true;
        }

        public bool TryAddEggBugEgg(Vec2 position)
        {
            RemoveInactive();
            if (foods.Count >= MaximumActiveFoods) return false;
            DesktopFood egg = new DesktopFood(DesktopFoodKind.EggBugEgg, position,
                FoodRenderPalette.CreateNormalEggHue(random));
            foods.Add(egg);
            LastEvent = "EggBugEgg_Spawn";
            return true;
        }

        public bool TrySpawnDangleFruit(Slugcat slugcat, DesktopCollisionWorld world)
        {
            return TrySpawnFood(DesktopFoodKind.DangleFruit, slugcat, world);
        }

        public bool TrySpawnEggBugEgg(Slugcat slugcat, DesktopCollisionWorld world)
        {
            return TrySpawnFood(DesktopFoodKind.EggBugEgg, slugcat, world);
        }

        private bool TrySpawnFood(DesktopFoodKind kind, Slugcat slugcat,
            DesktopCollisionWorld world)
        {
            if (slugcat == null || world == null) return false;
            RemoveInactive();
            if (foods.Count >= MaximumActiveFoods) return false;

            double radius = kind == DesktopFoodKind.EggBugEgg
                ? DesktopFood.EggBugEggRadius : DesktopFood.DangleFruitRadius;
            double minimumDistance = DesktopWorldTransform.ToSimulationLength(140.0);
            double maximumDistance = DesktopWorldTransform.ToSimulationLength(360.0);
            double distance = MathUtil.Lerp(minimumDistance, maximumDistance,
                random.NextDouble());
            int facing = slugcat.State.Facing == 0 ? 1 : slugcat.State.Facing;
            int direction = random.NextDouble() < 0.68 ? facing : -facing;
            double x = slugcat.Center.X + direction * distance;
            double y;
            double left;
            double right;
            DesktopSurface surface;
            if (slugcat.PrimarySupportingSurfaceId != 0 && world.TryGetSurface(
                slugcat.PrimarySupportingSurfaceId, slugcat.PrimarySupportingSurfaceKind,
                out surface) && surface.IsHorizontal)
            {
                left = surface.Left + radius + 3.0;
                right = surface.Right - radius - 3.0;
                y = surface.Top - radius;
            }
            else
            {
                MonitorInfo monitor = world.FindMonitor(slugcat.Center);
                left = DesktopWorldTransform.ToSimulationLength(monitor.WorkArea.Left) +
                    radius + 3.0;
                right = DesktopWorldTransform.ToSimulationLength(monitor.WorkArea.Right) -
                    radius - 3.0;
                y = DesktopWorldTransform.ToSimulationLength(monitor.FloorY) - radius;
            }

            if (right <= left) return false;
            x = MathUtil.Clamp(x, left, right);
            if (Math.Abs(x - slugcat.Center.X) < minimumDistance)
            {
                double opposite = MathUtil.Clamp(slugcat.Center.X - direction * distance,
                    left, right);
                if (Math.Abs(opposite - slugcat.Center.X) > Math.Abs(x - slugcat.Center.X))
                    x = opposite;
            }

            double dropHeight = DesktopWorldTransform.ToSimulationLength(
                MathUtil.Lerp(45.0, 120.0, random.NextDouble()));
            double visualHue = kind == DesktopFoodKind.EggBugEgg
                ? FoodRenderPalette.CreateNormalEggHue(random) : 0.0;
            DesktopFood food = new DesktopFood(kind,
                new Vec2(x, y - dropHeight), visualHue);
            food.SetCreationVelocity(new Vec2(direction *
                MathUtil.Lerp(0.15, 0.75, random.NextDouble()), 0.0));
            foods.Add(food);
            LastSpawnAccepted = ConsiderFood(food);
            if (LastSpawnAccepted && target == null) target = food;
            LastEvent = FoodEventName(food, LastSpawnAccepted
                ? "Spawn_Accepted" : "Spawn_Ignored");
            return true;
        }

        public void StepPhysics(DesktopCollisionWorld world)
        {
            if (biteFaceTicks > 0) biteFaceTicks--;
            StepMetabolism();
            RemoveInactive();
            for (int i = 0; i < foods.Count; i++) foods[i].StepPhysics(world);
            RemoveInactive();
        }

        public void StepMetabolism()
        {
            fullness = Math.Max(0.0, fullness -
                1.0 / DigestionTicksPerFoodPoint);
        }

        public bool TryProduceInput(Slugcat slugcat, SlugcatGraphics graphics,
            AttentionSystem attention, out VirtualInput input)
        {
            input = VirtualInput.Neutral;
            if (slugcat == null || graphics == null) return false;
            SelectTarget();
            if (target == null)
            {
                InteractionState = FoodInteractionState.None;
                return false;
            }

            if (slugcat.IsGrabbed || !slugcat.State.Conscious || slugcat.State.Dead ||
                slugcat.State.StunCounter > 0)
            {
                DropTarget(slugcat);
                return false;
            }

            if (attention != null)
                attention.SetTarget(AttentionKind.Food, target.Chunk.Position);

            if (target.State == DesktopFoodState.Held ||
                target.State == DesktopFoodState.Biting)
            {
                InteractionState = target.State == DesktopFoodState.Biting
                    ? FoodInteractionState.Eating : FoodInteractionState.Holding;
                return true;
            }

            target.Claim();
            InteractionState = FoodInteractionState.Seeking;
            Vec2 offset = target.Chunk.Position - slugcat.Center;
            if (Math.Abs(offset.X) > ApproachDistance)
            {
                input = new VirtualInput(offset.X < 0.0 ? -1 : 1, 0, false, false);
                return true;
            }

            if (offset.Length <= PickupDistance &&
                Math.Abs(offset.Y) <= PickupVerticalTolerance && slugcat.State.Grounded)
            {
                // Preserve the pickup point for this tick. StepInteraction snaps
                // the held item and hand into the slightly raised eating pose.
                if (target.PickUp(target.Chunk.Position))
                {
                    interactionCountdown = HoldBeforeBitingTicks;
                    InteractionState = FoodInteractionState.Holding;
                    LastEvent = FoodEventName(target, "PickUp");
                }
            }
            return true;
        }

        public void StepInteraction(Slugcat slugcat, SlugcatGraphics graphics)
        {
            if (slugcat == null || graphics == null)
            {
                target = null;
                interactionCountdown = 0;
                biteFaceTicks = 0;
                InteractionState = FoodInteractionState.None;
                return;
            }

            try
            {
                if (target == null || !target.IsActive) return;
                if (slugcat.IsGrabbed || !slugcat.State.Conscious || slugcat.State.Dead ||
                    slugcat.State.StunCounter > 0)
                {
                    DropTarget(slugcat);
                    return;
                }
                if (target.State != DesktopFoodState.Held &&
                    target.State != DesktopFoodState.Biting) return;

                // Rain World's bite motion is deliberately step-like rather than
                // a smooth orbit: hold slightly raised, snap hand+food to the
                // mouth for the bite, then snap back before repeating.
                Vec2 presentation = FoodPresentationPosition(slugcat, graphics);
                target.HoldAt(presentation);
                target.Chunk.LastPosition = presentation;

                if (target.State == DesktopFoodState.Held)
                {
                    if (interactionCountdown > 0)
                    {
                        interactionCountdown--;
                        return;
                    }

                    target.BeginBiting();
                    InteractionState = FoodInteractionState.Eating;
                    interactionCountdown = BiteIntervalTicks;
                    return;
                }

                bool mouthPhase = IsBiteMouthPhase();
                if (mouthPhase)
                    biteFaceTicks = Math.Max(biteFaceTicks, BiteFaceTicks);

                // Consume exactly once on the final mouth-contact tick. The
                // remaining countdown is the visible return-to-raised part of
                // the cycle; when it reaches zero the next bite cycle begins.
                if (interactionCountdown == BiteMouthEndTick)
                {
                    if (!target.Bite()) return;
                    TotalBites++;
                    LastEvent = FoodEventName(target, "Bite");
                    if (target.State == DesktopFoodState.Consumed)
                    {
                        FoodPointsEaten += target.FoodPoints;
                        fullness = Math.Min(MaximumFullness,
                            fullness + target.FoodPoints);
                        LastEvent = FoodEventName(target, "Eaten");
                        target = null;
                        InteractionState = FoodInteractionState.None;
                        return;
                    }
                }

                if (interactionCountdown > 0)
                {
                    interactionCountdown--;
                    return;
                }

                interactionCountdown = BiteIntervalTicks;
            }
            finally
            {
                // Graphics.Step has already run for this simulation tick. Apply
                // the food-specific hand pose afterwards so normal limb control
                // resumes untouched as soon as the interaction ends.
                ApplyFoodAnimation(slugcat, graphics);
            }
        }

        public void ApplyMovingSurfaceDelta(DesktopCollisionWorld world)
        {
            for (int i = 0; i < foods.Count; i++)
                foods[i].ApplyMovingSurfaceDelta(world);
        }

        public void Clear()
        {
            foods.Clear();
            target = null;
            interactionCountdown = 0;
            biteFaceTicks = 0;
            InteractionState = FoodInteractionState.None;
            LastSpawnAccepted = false;
            LastEvent = "Food_Clear";
        }

        private void SelectTarget()
        {
            if (target != null && target.IsActive) return;
            target = null;
            for (int i = 0; i < foods.Count; i++)
            {
                if (!foods[i].IsActive ||
                    foods[i].State == DesktopFoodState.Ignored) continue;
                if (foods[i].State == DesktopFoodState.Free &&
                    !ConsiderFood(foods[i])) continue;
                target = foods[i];
                break;
            }
        }

        private void DropTarget(Slugcat slugcat)
        {
            if (target != null && (target.State == DesktopFoodState.Held ||
                target.State == DesktopFoodState.Biting))
            {
                Vec2 velocity = slugcat == null
                    ? Vec2.Zero : slugcat.BodyChunks[0].Velocity * 0.5;
                target.Drop(velocity);
                LastEvent = FoodEventName(target, "Drop");
            }
            target = null;
            interactionCountdown = 0;
            biteFaceTicks = 0;
            InteractionState = FoodInteractionState.None;
        }

        private void RemoveInactive()
        {
            bool removedTarget = false;
            for (int i = foods.Count - 1; i >= 0; i--)
            {
                if (foods[i].IsActive) continue;
                if (ReferenceEquals(foods[i], target)) removedTarget = true;
                foods.RemoveAt(i);
            }
            if (!removedTarget) return;
            target = null;
            interactionCountdown = 0;
            biteFaceTicks = 0;
            InteractionState = FoodInteractionState.None;
        }

        private Vec2 FoodPresentationPosition(Slugcat slugcat, SlugcatGraphics graphics)
        {
            int facing = slugcat.State.Facing == 0 ? 1 : slugcat.State.Facing;
            Vec2 raised = graphics.Head.Position + new Vec2(facing * 8.5, 5.5);
            if (target != null && target.State == DesktopFoodState.Biting &&
                IsBiteMouthPhase())
                return MouthPosition(slugcat, graphics);
            return raised;
        }

        private bool IsBiteMouthPhase()
        {
            return target != null && target.State == DesktopFoodState.Biting &&
                interactionCountdown <= BiteMouthStartTick &&
                interactionCountdown >= BiteMouthEndTick;
        }

        private void ApplyFoodAnimation(Slugcat slugcat, SlugcatGraphics graphics)
        {
            if (biteFaceTicks > 0)
            {
                // PlayerGraphics maps ImpactBlinkTicks into the original closed
                // eye FaceB family. Refresh it only around mouth contact so the
                // face change follows each individual bite instead of staying on.
                slugcat.State.ImpactBlinkTicks = Math.Max(
                    slugcat.State.ImpactBlinkTicks, biteFaceTicks);
            }

            if (target == null || !target.IsActive) return;
            bool carrying = target.State == DesktopFoodState.Held ||
                target.State == DesktopFoodState.Biting;
            if (!carrying && InteractionState != FoodInteractionState.Seeking) return;

            Vec2 connection = slugcat.BodyChunks[0].Position;
            if (!carrying && Vec2.Distance(connection, target.Chunk.Position) >
                FoodHandReachDistance) return;

            int facing = slugcat.State.Facing == 0 ? 1 : slugcat.State.Facing;
            int handIndex = facing < 0 ? 0 : 1;
            SpearmasterAbilityController spear =
                slugcat.AbilityController as SpearmasterAbilityController;
            if (spear != null && spear.HeldSpear != null && spear.HeldHand == handIndex)
                handIndex = 1 - handIndex;

            Limb hand = graphics.Arms[handIndex];
            Vec2 handTarget = carrying
                ? target.Chunk.Position + new Vec2(-facing * 1.5, 1.5)
                : target.Chunk.Position;
            Vec2 offset = handTarget - connection;
            double maximumReach = hand.Length * 0.95;
            if (offset.Length > maximumReach)
                handTarget = connection + offset.Normalized * maximumReach;

            hand.Mode = LimbMode.HuntAbsolutePosition;
            hand.AbsoluteHuntPosition = handTarget;
            hand.TargetPosition = handTarget;
            hand.GripSurfaceId = 0;
            hand.RetractCounter = 0;
            hand.HuntSpeed = carrying ? 12.0 : 9.0;
            hand.Quickness = carrying ? 0.85 : 0.65;

            Vec2 previous = hand.End.Position;
            if (carrying)
            {
                // The eating pose changes on logic ticks just like the original
                // sprite animation: hand and food jump together between raised
                // and mouth positions instead of easing through the space.
                hand.End.Position = handTarget;
                hand.End.LastPosition = handTarget;
                hand.End.Velocity = Vec2.Zero;
            }
            else
            {
                hand.End.Position = Vec2.Lerp(previous, handTarget,
                    SeekingHandBlend);
                hand.End.LastPosition = previous;
                hand.End.Velocity = hand.End.Position - previous;
            }
        }

        private static Vec2 MouthPosition(Slugcat slugcat, SlugcatGraphics graphics)
        {
            int facing = slugcat.State.Facing == 0 ? 1 : slugcat.State.Facing;
            return graphics.Head.Position + new Vec2(facing * 5.0, 1.5);
        }

        private bool ConsiderFood(DesktopFood food)
        {
            if (food.State == DesktopFoodState.Claimed) return true;
            if (food.State == DesktopFoodState.Ignored) return false;
            if (food.State != DesktopFoodState.Free) return true;

            double projected = ProjectedFullness();
            bool accepted;
            if (projected <= 0.001)
            {
                // An empty desktop pet always accepts the first offered food.
                accepted = true;
            }
            else if (projected >= MaximumFullness)
            {
                accepted = false;
            }
            else
            {
                double chance = MathUtil.Lerp(0.78, 0.12,
                    projected / MaximumFullness);
                accepted = random.NextDouble() < chance;
            }

            if (accepted) food.Claim();
            else food.Ignore();
            return accepted;
        }

        private double ProjectedFullness()
        {
            double projected = fullness;
            for (int i = 0; i < foods.Count; i++)
            {
                DesktopFood food = foods[i];
                if (food.State == DesktopFoodState.Claimed ||
                    food.State == DesktopFoodState.Held ||
                    food.State == DesktopFoodState.Biting)
                    projected += food.FoodPoints;
            }
            return projected;
        }

        private static string FoodEventName(DesktopFood food, string action)
        {
            return food.Kind + "_" + action;
        }
    }
}
