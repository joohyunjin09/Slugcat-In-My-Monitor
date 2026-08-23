using System;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Physics;

namespace RainWorldDesktopPet.Creature
{
    public sealed class SlugcatMovement
    {
        private readonly Slugcat owner;
        private bool previousJump;
        private int preJumpCounter;
        private int landingCounter;
        private double jumpBoost;
        private long dropThroughSurfaceId;
        private int dropThroughTicks;

        public SlugcatMovement(Slugcat owner)
        {
            this.owner = owner;
        }

        public long IgnoredSurfaceId { get { return dropThroughTicks > 0 ? dropThroughSurfaceId : 0; } }

        public void ApplyInput(VirtualInput input, DesktopCollisionWorld world)
        {
            if (dropThroughTicks > 0) dropThroughTicks--;
            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];
            SlugcatState state = owner.State;
            double recoveryDenominator = (input.X == 0 && input.Y == 0 ? 400.0 : 1100.0) *
                (1.0 + 3.0 * MathUtil.InverseLerp(0.9, 1.0, state.AerobicLevel));
            state.AerobicLevel = Math.Max(0.0, state.AerobicLevel - 1.0 / recoveryDenominator);
            bool wasGrounded = chest.ContactFloor || hips.ContactFloor;
            bool wallContact = chest.ContactLeft || chest.ContactRight || hips.ContactLeft || hips.ContactRight;

            state.JustLanded = !state.Grounded && wasGrounded;
            state.Grounded = wasGrounded;
            if (state.JustLanded)
            {
                landingCounter = 6;
                double impactSpeed = Math.Max(chest.FloorImpactSpeed, hips.FloorImpactSpeed);
                state.LandingCompression = MathUtil.Clamp(impactSpeed / 12.0, 0.25, 1.0);
            }
            else if (landingCounter > 0)
            {
                landingCounter--;
                state.LandingCompression *= 0.72;
            }

            if (input.X != 0)
            {
                state.Facing = input.X;
            }

            if (input.DropThrough && wasGrounded && owner.PrimarySupportingSurfaceId > 0)
            {
                dropThroughSurfaceId = owner.PrimarySupportingSurfaceId;
                dropThroughTicks = 12;
                chest.Velocity.Y = Math.Max(chest.Velocity.Y, 2.5);
                hips.Velocity.Y = Math.Max(hips.Velocity.Y, 2.5);
                state.Grounded = false;
                state.BodyMode = BodyModeIndex.Default;
                state.Animation = AnimationIndex.Fall;
                state.AnimationFrame++;
                previousJump = input.Jump;
                return;
            }

            bool resting = input.Posture == VirtualPosture.Sit || input.Posture == VirtualPosture.Sleep;
            bool crawl = input.Y > 0 || resting;
            if (input.X != 0 && wasGrounded)
            {
                double mainTarget = input.X * (crawl ? 2.5 : 4.2) * owner.Appearance.RunSpeedFactor;
                double hipsTarget = input.X * (crawl ? 2.5 : 4.0) * owner.Appearance.RunSpeedFactor;
                chest.Velocity.X = MathUtil.MoveTowards(chest.Velocity.X, mainTarget, 1.2);
                hips.Velocity.X = MathUtil.MoveTowards(hips.Velocity.X, hipsTarget, 1.2);
            }
            else if (input.X != 0)
            {
                chest.Velocity.X += input.X * 0.18 * owner.Appearance.RunSpeedFactor;
                hips.Velocity.X += input.X * 0.153 * owner.Appearance.RunSpeedFactor;
            }

            if (input.X == 0 && wasGrounded)
            {
                chest.Velocity.X *= 0.72;
                hips.Velocity.X *= 0.72;
                state.Stillness = MathUtil.Clamp01(state.Stillness + 0.035);
            }
            else
            {
                state.Stillness = MathUtil.Clamp01(state.Stillness - 0.12);
            }

            if (wallContact && input.Y < 0 && !wasGrounded)
            {
                state.BodyMode = BodyModeIndex.WallClimb;
                state.Animation = AnimationIndex.WallClimb;
                chest.Velocity.Y = MathUtil.MoveTowards(chest.Velocity.Y, -2.1, 0.9);
                hips.Velocity.Y = MathUtil.MoveTowards(hips.Velocity.Y, -1.8, 0.8);
                chest.Velocity.X *= 0.5;
                hips.Velocity.X *= 0.5;
            }
            else if (wasGrounded)
            {
                state.BodyMode = crawl ? BodyModeIndex.Crawl : BodyModeIndex.Stand;
            }
            else
            {
                state.BodyMode = BodyModeIndex.Default;
            }

            if (input.Jump && !previousJump && wasGrounded && preJumpCounter == 0)
            {
                preJumpCounter = 4;
                state.Animation = AnimationIndex.PreJump;
            }

            bool launchedThisTick = false;
            if (preJumpCounter > 0)
            {
                preJumpCounter--;
                owner.BodyConnection.Distance = MathUtil.Lerp(owner.BodyConnection.Distance, 12.0, 0.55);
                chest.Velocity.Y += 0.28;
                if (preJumpCounter == 0)
                {
                    // Player.Jump's normal standing branch starts at y=4/3
                    // (Rain World y-up) and then applies a held-button boost.
                    chest.Velocity.Y = -4.0;
                    hips.Velocity.Y = -3.0;
                    jumpBoost = 8.0;
                    state.AerobicLevel = MathUtil.Clamp01(state.AerobicLevel + 0.75 / 9.0);
                    state.Animation = AnimationIndex.Jump;
                    state.Grounded = false;
                    state.BodyMode = BodyModeIndex.Default;
                    launchedThisTick = true;
                }
            }
            else
            {
                double targetDistance = crawl || landingCounter > 0 ? 13.5 : SimulationConstants.BodyConnectionDistance;
                owner.BodyConnection.Distance = MathUtil.Lerp(owner.BodyConnection.Distance, targetDistance, 0.25);
            }

            if (!launchedThisTick && !wasGrounded && input.Jump && jumpBoost > 0.0)
            {
                jumpBoost = Math.Max(0.0, jumpBoost - 1.5);
                double boost = (jumpBoost + 1.0) * 0.3;
                chest.Velocity.Y -= boost;
                hips.Velocity.Y -= boost;
            }
            else if (!input.Jump && preJumpCounter == 0)
            {
                jumpBoost = 0.0;
            }

            if (!launchedThisTick)
            {
                StabilizePosture(input, wasGrounded, crawl, input.Posture);
            }

            if (preJumpCounter == 0 && !launchedThisTick)
            {
                if (wasGrounded)
                {
                    if (input.Posture == VirtualPosture.Sleep)
                    {
                        state.Animation = AnimationIndex.Sleep;
                    }
                    else if (input.Posture == VirtualPosture.Sit)
                    {
                        state.Animation = AnimationIndex.Sit;
                    }
                    else if (landingCounter > 0)
                    {
                        state.Animation = AnimationIndex.Land;
                    }
                    else if (crawl)
                    {
                        state.Animation = AnimationIndex.DownOnFours;
                    }
                    else if (input.X != 0)
                    {
                        state.Animation = AnimationIndex.StandUp;
                    }
                    else if (state.Animation != AnimationIndex.Sit && state.Animation != AnimationIndex.Sleep)
                    {
                        state.Animation = AnimationIndex.None;
                    }
                }
                else if (state.BodyMode != BodyModeIndex.WallClimb)
                {
                    state.Animation = chest.Velocity.Y < 0.0 ? AnimationIndex.Jump : AnimationIndex.Fall;
                }
            }

            double speed = Math.Abs((chest.Velocity.X + hips.Velocity.X) * 0.5);
            state.RunCycle += speed * (crawl ? 0.07 : 0.11);
            if (wasGrounded && !launchedThisTick)
            {
                if (input.X == 0 || resting)
                {
                    state.AnimationFrame = 0;
                }
                else
                {
                    state.AnimationFrame++;
                    int lastFrame = crawl ? 10 : 6;
                    if (state.AnimationFrame > lastFrame) state.AnimationFrame = 0;
                    if (state.AnimationFrame == 0 && state.AerobicLevel < 0.7)
                        state.AerobicLevel = MathUtil.Clamp01(state.AerobicLevel + 0.05 / 9.0);
                }
            }
            else
            {
                state.AnimationFrame++;
            }
            previousJump = input.Jump;
        }

        private void StabilizePosture(VirtualInput input, bool grounded, bool crawl, VirtualPosture posture)
        {
            if (!grounded)
            {
                return;
            }

            BodyChunk chest = owner.BodyChunks[0];
            BodyChunk hips = owner.BodyChunks[1];

            if (!crawl && posture == VirtualPosture.None)
            {
                // Player.UpdateBodyMode's Stand branch applies +1.5 to body chunk 0
                // and -4.5 to body chunk 1 in Rain World's y-up coordinates.
                // The desktop collision world uses y-down screen coordinates.
                chest.Velocity.Y -= 1.5;
                hips.Velocity.Y += 4.5;
            }
            else
            {
                double desiredVertical = posture == VirtualPosture.Sleep
                    ? -1.5
                    : (posture == VirtualPosture.Sit ? -6.0 : (crawl ? -3.5 : -13.0 + owner.State.LandingCompression * 6.0));
                double desiredHorizontal = owner.State.Facing *
                    (posture == VirtualPosture.Sleep ? 11.0 : (posture == VirtualPosture.Sit ? 6.0 : (crawl ? 10.0 : 2.5)));
                Vec2 desiredChest = hips.Position + new Vec2(desiredHorizontal, desiredVertical);
                Vec2 correction = Vec2.ClampMagnitude(desiredChest - chest.Position, 2.4);
                chest.Velocity += correction * 0.18;
                hips.Velocity -= correction * 0.05;
            }

            double edgeDistance = worldDistanceToEdge(owner.State.Facing);
            if (edgeDistance < 14.0 && input.X == owner.State.Facing)
            {
                chest.Velocity.X *= 0.72;
                hips.Velocity.X *= 0.72;
            }
        }

        private double worldDistanceToEdge(int direction)
        {
            return owner.World == null
                ? 1000.0
                : owner.World.DistanceToEdge(owner.Center, direction, owner.PrimarySupportingSurfaceId);
        }
    }
}
