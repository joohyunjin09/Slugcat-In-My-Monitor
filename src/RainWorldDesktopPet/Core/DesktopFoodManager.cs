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
        // Player.eatCounter normally rests at 40. Holding pickup over an edible
        // counts it down to the first bite, then resets it to 15 for repeats.
        private const int InitialEatCounter = 40;
        private const int BiteIntervalTicks = 15;
        private const double FoodHandReachDistance = 34.0;
        private const double SeekingHandBlend = 0.34;

        private readonly List<DesktopFood> foods = new List<DesktopFood>(MaximumActiveFoods);
        private readonly IList<DesktopFood> foodView;
        private readonly Random random;
        private readonly Random rotationRandom;
        private DesktopFood target;
        private DesktopFood draggedFood;
        private Vec2 dragOffset;
        private int interactionCountdown;
        private int foodHand = -1;
        private double fullness;

        public DesktopFoodManager()
            : this(Environment.TickCount)
        {
        }

        public DesktopFoodManager(int randomSeed)
        {
            foodView = foods.AsReadOnly();
            random = new Random(randomSeed);
            rotationRandom = new Random(unchecked(randomSeed ^ 0x53C7A1));
        }

        public IList<DesktopFood> Foods { get { return foodView; } }
        public DesktopFood Target { get { return target; } }
        public DesktopFood DraggedFood { get { return draggedFood; } }
        public bool IsDragging { get { return draggedFood != null; } }
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
            DesktopFood fruit = new DesktopFood(DesktopFoodKind.DangleFruit,
                position, 0.13, RandomItemRotation());
            foods.Add(fruit);
            LastEvent = "DangleFruit_Spawn";
            return true;
        }

        public bool TryAddEggBugEgg(Vec2 position)
        {
            RemoveInactive();
            if (foods.Count >= MaximumActiveFoods) return false;
            DesktopFood egg = new DesktopFood(DesktopFoodKind.EggBugEgg, position,
                FoodRenderPalette.CreateNormalEggHue(random), RandomItemRotation());
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
                new Vec2(x, y - dropHeight), visualHue, RandomItemRotation());
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
            if (draggedFood != null) return false;
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
                EnsureFoodHand(slugcat);
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
                // Preserve the pickup point for this tick. StepInteraction then
                // transfers it to the fixed grasp hand used by the edible loop.
                if (target.PickUp(target.Chunk.Position))
                {
                    EnsureFoodHand(slugcat);
                    interactionCountdown = 0;
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
                foodHand = -1;
                InteractionState = FoodInteractionState.None;
                return;
            }

            bool biteOccurred = false;
            DesktopFood animatedFood = target;
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

                EnsureFoodHand(slugcat);

                if (target.State == DesktopFoodState.Held)
                {
                    // The original starts the edible loop on the update after
                    // pickup with eatCounter at its resting value. SlugcatHand
                    // raises the grasp throughout this first countdown.
                    target.BeginBiting();
                    InteractionState = FoodInteractionState.Eating;
                    interactionCountdown = InitialEatCounter;
                    return;
                }

                if (interactionCountdown > 0)
                {
                    interactionCountdown--;
                    return;
                }

                if (!target.Bite()) return;
                biteOccurred = true;
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
                // This tick is already the first tick of the original 15-tick
                // cycle, so fourteen non-bite updates follow it.
                interactionCountdown = BiteIntervalTicks - 1;
            }
            finally
            {
                ApplyFoodAnimation(slugcat, graphics, animatedFood, biteOccurred);
            }
        }

        public void ApplyMovingSurfaceDelta(DesktopCollisionWorld world)
        {
            for (int i = 0; i < foods.Count; i++)
                foods[i].ApplyMovingSurfaceDelta(world);
        }

        public bool HitTest(Vec2 point)
        {
            return FindDraggableFood(point) != null;
        }

        public bool TryBeginDrag(Vec2 point)
        {
            if (draggedFood != null) return false;
            DesktopFood food = FindDraggableFood(point);
            if (food == null || !food.BeginDrag()) return false;

            draggedFood = food;
            dragOffset = food.Chunk.Position - point;
            if (ReferenceEquals(target, food))
            {
                target = null;
                interactionCountdown = 0;
                foodHand = -1;
                InteractionState = FoodInteractionState.None;
            }
            LastEvent = FoodEventName(food, "MouseGrab");
            return true;
        }

        public void MoveDraggedFood(Vec2 pointerPosition)
        {
            if (draggedFood == null) return;
            draggedFood.DragTo(pointerPosition + dragOffset);
        }

        public bool EndDrag(Vec2 velocity)
        {
            DesktopFood food = draggedFood;
            draggedFood = null;
            dragOffset = Vec2.Zero;
            if (food == null || !food.EndDrag(velocity)) return false;
            LastEvent = FoodEventName(food, "MouseRelease");
            return true;
        }

        public void Clear()
        {
            foods.Clear();
            target = null;
            draggedFood = null;
            dragOffset = Vec2.Zero;
            interactionCountdown = 0;
            foodHand = -1;
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
                    foods[i].State == DesktopFoodState.Ignored ||
                    foods[i].State == DesktopFoodState.Dragged) continue;
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
            foodHand = -1;
            InteractionState = FoodInteractionState.None;
        }

        private void RemoveInactive()
        {
            bool removedTarget = false;
            bool removedDraggedFood = false;
            for (int i = foods.Count - 1; i >= 0; i--)
            {
                if (foods[i].IsActive) continue;
                if (ReferenceEquals(foods[i], target)) removedTarget = true;
                if (ReferenceEquals(foods[i], draggedFood)) removedDraggedFood = true;
                foods.RemoveAt(i);
            }
            if (removedDraggedFood)
            {
                draggedFood = null;
                dragOffset = Vec2.Zero;
            }
            if (!removedTarget) return;
            target = null;
            interactionCountdown = 0;
            foodHand = -1;
            InteractionState = FoodInteractionState.None;
        }

        private DesktopFood FindDraggableFood(Vec2 point)
        {
            DesktopFood closest = null;
            double closestDistance = double.MaxValue;
            for (int i = foods.Count - 1; i >= 0; i--)
            {
                DesktopFood food = foods[i];
                if (!food.IsActive || !food.IsDraggable) continue;
                double distance = Vec2.Distance(point, food.Chunk.Position);
                if (distance > food.VisualReach + 5.0 ||
                    distance >= closestDistance) continue;
                closest = food;
                closestDistance = distance;
            }
            return closest;
        }

        private void EnsureFoodHand(Slugcat slugcat)
        {
            if (foodHand >= 0) return;
            // Player.FreeHand returns grasp 0 first. Preserve that stable grasp
            // index through the entire eating cycle; only a held spear occupies
            // a hand in the desktop model.
            foodHand = 0;
            SpearmasterAbilityController spear =
                slugcat.AbilityController as SpearmasterAbilityController;
            if (spear != null && spear.HeldSpear != null && spear.HeldHand == foodHand)
                foodHand = 1;
        }

        private void ApplyFoodAnimation(Slugcat slugcat, SlugcatGraphics graphics,
            DesktopFood animatedFood, bool biteOccurred)
        {
            DesktopFood food = animatedFood != null ? animatedFood : target;
            if (food == null) return;
            bool carrying = food.State == DesktopFoodState.Held ||
                food.State == DesktopFoodState.Biting || biteOccurred;
            if (!carrying && InteractionState != FoodInteractionState.Seeking) return;

            Vec2 connection = slugcat.BodyChunks[0].Position;
            if (!carrying && Vec2.Distance(connection, food.Chunk.Position) >
                FoodHandReachDistance) return;

            EnsureFoodHand(slugcat);
            int handIndex = foodHand;

            Limb hand = graphics.Arms[handIndex];
            if (carrying)
            {
                // SlugcatHand.Update uses its grasp-relative pose between bites.
                // On the bite tick PlayerGraphics.BiteFly snaps the hand to the
                // upper draw position while BitByPlayer moves the edible to the
                // main body chunk for exactly one frame.
                graphics.SetEdibleHandPose(handIndex, interactionCountdown);
                if (biteOccurred)
                {
                    graphics.ApplyEdibleBiteAfterGraphicsStep(handIndex);
                    if (food.IsActive)
                    {
                        food.HoldAt(slugcat.BodyChunks[0].Position,
                            slugcat.BodyChunks[0].Position);
                        food.Chunk.LastPosition = food.Chunk.Position;
                    }
                }
                else
                {
                    food.HoldAt(hand.End.Position,
                        slugcat.BodyChunks[0].Position);
                    food.Chunk.LastPosition = food.Chunk.Position;
                }
                return;
            }

            Vec2 handTarget = food.Chunk.Position;
            Vec2 offset = handTarget - connection;
            double maximumReach = hand.Length * 0.95;
            if (offset.Length > maximumReach)
                handTarget = connection + offset.Normalized * maximumReach;

            hand.Mode = LimbMode.HuntAbsolutePosition;
            hand.AbsoluteHuntPosition = handTarget;
            hand.TargetPosition = handTarget;
            hand.GripSurfaceId = 0;
            hand.RetractCounter = 0;
            hand.HuntSpeed = 9.0;
            hand.Quickness = 0.65;

            Vec2 previous = hand.End.Position;
            hand.End.Position = Vec2.Lerp(previous, handTarget,
                SeekingHandBlend);
            hand.End.LastPosition = previous;
            hand.End.Velocity = hand.End.Position - previous;
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

        private Vec2 RandomItemRotation()
        {
            double angle = rotationRandom.NextDouble() * Math.PI * 2.0;
            return new Vec2(Math.Cos(angle), Math.Sin(angle));
        }

        private static string FoodEventName(DesktopFood food, string action)
        {
            return food.Kind + "_" + action;
        }
    }
}
