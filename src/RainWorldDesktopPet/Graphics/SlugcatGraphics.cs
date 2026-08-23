using System;
using RainWorldDesktopPet.AI;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Physics;

namespace RainWorldDesktopPet.Graphics
{
    public sealed class SlugcatGraphics
    {
        private readonly Slugcat slugcat;
        private readonly Limb[] arms;
        private readonly BodyPart head;
        private readonly BodyPart legs;
        private readonly ProceduralTail tail;
        private readonly SlugcatPose renderPose;
        private readonly Vec2[,] drawPositions = new Vec2[2, 2];
        private Vec2 lookDirection;
        private Vec2 lastLookDirection;
        private Vec2 legsDirection = Vec2.Down;
        private Vec2 lastLegsDirection = Vec2.Down;
        private Vec2 legsTargetPosition;
        private double breath;
        private double lastBreath;

        public SlugcatGraphics(Slugcat slugcat)
        {
            this.slugcat = slugcat;
            Vec2 chest = slugcat.BodyChunks[0].Position;
            Vec2 hips = slugcat.BodyChunks[1].Position;
            drawPositions[0, 0] = drawPositions[0, 1] = chest;
            drawPositions[1, 0] = drawPositions[1, 1] = hips;
            head = new BodyPart(chest + new Vec2(0.0, -7.0), 4.0, 0.8, 0.99);
            legs = new BodyPart(hips + new Vec2(0.0, 5.0), 1.0, 0.8, 0.99);
            legsTargetPosition = hips;
            arms = new Limb[2];
            arms[0] = new Limb(LimbKind.Arm, -1, chest + new Vec2(-4.0, 8.0), 20.0);
            arms[1] = new Limb(LimbKind.Arm, 1, chest + new Vec2(4.0, 8.0), 20.0);
            tail = new ProceduralTail(hips);
            renderPose = new SlugcatPose();
            int count = tail.Segments.Length;
            renderPose.Tail = new Vec2[count];
            renderPose.TailLast = new Vec2[count];
            renderPose.TailCurrent = new Vec2[count];
            renderPose.TailRadii = new double[count];
        }

        public ProceduralTail Tail { get { return tail; } }
        public Limb[] Arms { get { return arms; } }
        public BodyPart Legs { get { return legs; } }
        public BodyPart Head { get { return head; } }

        // Called after Player/PhysicalObject update, matching GraphicsModule.Update.
        public void Step(AttentionSystem attention, DesktopCollisionWorld world)
        {
            lastBreath = breath;
            breath += slugcat.State.Animation == AnimationIndex.Sleep
                ? 0.0125
                : 1.0 / MathUtil.Lerp(60.0, 15.0, Math.Pow(slugcat.State.AerobicLevel, 1.5));
            lastLookDirection = lookDirection;
            lookDirection = (attention.Smoothed - head.Position).Normalized;
            lastLegsDirection = legsDirection;

            for (int i = 0; i < 2; i++)
            {
                drawPositions[i, 1] = drawPositions[i, 0];
                drawPositions[i, 0] = slugcat.BodyChunks[i].Position;
            }
            ApplyOriginalBodyModeOffsets();

            Vec2 upper = drawPositions[0, 0];
            Vec2 lower = drawPositions[1, 0];
            Vec2 bodyUp = (upper - lower).Normalized;
            if (bodyUp.LengthSquared < 0.1) bodyUp = Vec2.Up;

            if (slugcat.State.BodyMode == BodyModeIndex.Stand)
            {
                if (slugcat.LastInput.X == 0) head.Velocity -= lookDirection * 0.5;
                upper -= lookDirection * 2.0;
                drawPositions[0, 0] = upper;
            }
            else
            {
                head.Velocity += lookDirection;
            }

            tail.Step(upper, lower, slugcat.BodyChunks[1].Velocity,
                slugcat.State.Facing, slugcat.State.BodyMode, world);

            head.Update();
            Vec2 neckDirection = bodyUp * 3.0;
            if (slugcat.State.BodyMode == BodyModeIndex.Crawl) neckDirection.X *= 2.5;
            Vec2 headTarget = Vec2.Lerp(upper, lower, 0.2) + neckDirection;
            head.ConnectToPoint(headTarget, 3.0, false, 0.2,
                slugcat.BodyChunks[0].Velocity, 0.7, 0.1);

            legs.Update();
            bool grounded = slugcat.BodyChunks[1].ContactFloor;
            Vec2 legsTarget = grounded
                ? slugcat.BodyChunks[1].Position + new Vec2(legsDirection.X * 8.0, -1.0)
                : slugcat.BodyChunks[1].Position + new Vec2(legsDirection.X * 8.0, 2.0);
            legsTargetPosition = legsTarget;
            legs.ConnectToPoint(legsTarget, grounded ? 5.0 : 4.0, false, 0.25,
                new Vec2(slugcat.BodyChunks[1].Velocity.X, 10.0), 0.5, 0.1);
            if (grounded)
            {
                if (slugcat.BodyChunks[1].ContactLeft) legsDirection.X += 1.0;
                if (slugcat.BodyChunks[1].ContactRight) legsDirection.X -= 1.0;
                legsDirection.Y += 1.0;
            }
            else
            {
                legsDirection += slugcat.BodyChunks[1].Velocity * 0.01;
                legsDirection.Y += 0.05;
            }
            legsDirection = legsDirection.Normalized;

            for (int i = 0; i < 2; i++)
            {
                arms[i].Step(slugcat, slugcat.BodyChunks[0].Position,
                    slugcat.BodyChunks[1].Position, slugcat.BodyChunks[0].Velocity, world);
            }
            if (slugcat.State.Animation == AnimationIndex.Sleep)
            {
                Vec2 center = (upper + lower) * 0.5;
                head.Position = Vec2.Lerp(head.Position,
                    center + new Vec2(slugcat.State.Facing * 5.0, 3.0), 0.35);
                tail.CurlAround(lower, slugcat.State.Facing, 1.0);
            }
        }

        private void ApplyOriginalBodyModeOffsets()
        {
            int frame = slugcat.State.AnimationFrame;
            int facing = slugcat.State.Facing;
            Vec2 upper = drawPositions[0, 0];
            Vec2 lower = drawPositions[1, 0];
            if (slugcat.State.BodyMode == BodyModeIndex.Stand)
            {
                double cycle = frame / 6.0 * Math.PI * 2.0;
                upper.X += facing * 6.0 * MathUtil.Clamp(Math.Abs(slugcat.BodyChunks[1].Velocity.X) - 0.2, 0.0, 1.0);
                upper.Y -= Math.Cos(cycle) * 2.0;
                lower.X -= facing * (1.5 - frame / 6.0);
                lower.Y -= 2.0 + Math.Sin(cycle) * 4.0;
            }
            else if (slugcat.State.BodyMode == BodyModeIndex.Crawl)
            {
                double sin = Math.Sin(frame / 21.0 * Math.PI * 2.0);
                double cos = Math.Cos(frame / 14.0 * Math.PI * 2.0);
                upper.X += cos * facing * 2.0;
                upper.Y += -sin * 1.5 - 3.0;
                head.Velocity.Y -= sin * 0.5 + 0.5;
                head.Velocity.X += upper.X < lower.X ? -1.0 : 1.0;
                lower.X += -3.0 * sin * facing;
                lower.Y += cos * 1.5 - 7.0;
            }
            else if (slugcat.State.BodyMode == BodyModeIndex.WallClimb)
            {
                legsDirection.Y += 1.0;
                upper.Y -= 2.0;
                upper.X -= facing * (slugcat.BodyChunks[1].ContactFloor ? 3.0 : 5.0);
                head.Velocity.Y += facing * 5.0;
            }
            if (slugcat.State.Animation == AnimationIndex.Sleep)
            {
                Vec2 middle = (upper + lower) * 0.5;
                upper = Vec2.Lerp(upper, middle, 0.35);
                lower = Vec2.Lerp(lower, middle, 0.35);
            }
            drawPositions[0, 0] = upper;
            drawPositions[1, 0] = lower;
        }

        public SlugcatPose BuildPose(double interpolation, AttentionSystem attention)
        {
            return BuildPose(interpolation, attention, 0);
        }

        public SlugcatPose BuildPose(double interpolation, AttentionSystem attention, long simulationTick)
        {
            double timeStacker = MathUtil.Clamp01(interpolation);
            SlugcatPose pose = renderPose;
            pose.SimulationTick = simulationTick;
            pose.TimeStacker = timeStacker;
            for (int i = 0; i < 2; i++)
            {
                pose.ChunkLast[i] = slugcat.BodyChunks[i].LastPosition;
                pose.ChunkCurrent[i] = slugcat.BodyChunks[i].Position;
                pose.ChunkRender[i] = slugcat.BodyChunks[i].RenderPosition(timeStacker);
                pose.DrawLast[i] = drawPositions[i, 1];
                pose.DrawCurrent[i] = drawPositions[i, 0];
            }
            pose.Chest = Vec2.Lerp(drawPositions[0, 1], drawPositions[0, 0], timeStacker);
            pose.Hips = Vec2.Lerp(drawPositions[1, 1], drawPositions[1, 0], timeStacker);
            pose.BodyUp = (pose.Chest - pose.Hips).Normalized;
            if (pose.BodyUp.LengthSquared < 0.1) pose.BodyUp = Vec2.Up;
            pose.BodyRight = pose.BodyUp.Perpendicular;
            pose.HeadLast = head.LastPosition;
            pose.HeadCurrent = head.Position;
            pose.Head = head.RenderPosition(timeStacker);
            pose.LookDirection = Vec2.Lerp(lastLookDirection, lookDirection, timeStacker);
            pose.HeadDirection = (pose.Head - Vec2.Lerp(pose.Hips, pose.Chest, 0.5)).Normalized;
            pose.LegsLast = legs.LastPosition;
            pose.LegsCurrent = legs.Position;
            pose.Legs = legs.RenderPosition(timeStacker);
            pose.LegsDirection = Vec2.Lerp(lastLegsDirection, legsDirection, timeStacker).Normalized;
            pose.Facing = slugcat.State.Facing;
            pose.Animation = slugcat.State.Animation;
            pose.BodyMode = slugcat.State.BodyMode;
            pose.AnimationFrame = slugcat.State.AnimationFrame;
            pose.Breath = 0.5 + 0.5 * Math.Sin(MathUtil.Lerp(lastBreath, breath, timeStacker) * Math.PI * 2.0);
            pose.LandingCompression = slugcat.State.LandingCompression;
            for (int i = 0; i < 2; i++)
            {
                pose.HandLast[i] = arms[i].End.LastPosition;
                pose.HandCurrent[i] = arms[i].End.Position;
                pose.Hands[i] = arms[i].RenderPosition(timeStacker);
                pose.HandTargets[i] = arms[i].TargetPosition;
                pose.ArmConnectionLast[i] = arms[i].LastConnectionPosition;
                pose.ArmConnectionCurrent[i] = arms[i].ConnectionPosition;
                pose.ArmConnections[i] = Vec2.Lerp(arms[i].LastConnectionPosition,
                    arms[i].ConnectionPosition, timeStacker);
                pose.ArmMaxLengths[i] = arms[i].Length;
                pose.ArmRetractCounters[i] = arms[i].RetractCounter;
                pose.ArmModes[i] = arms[i].Mode;
                pose.ArmVisible[i] = arms[i].Mode != LimbMode.Retracted;
                pose.Elbows[i] = arms[i].ComputeJoint(pose.Chest, pose.Hands[i], timeStacker);
                pose.Feet[i] = pose.Legs + pose.BodyRight * (i == 0 ? -2.0 : 2.0);
                pose.FootTargets[i] = legsTargetPosition;
                pose.Knees[i] = Vec2.Lerp(pose.Hips, pose.Feet[i], 0.5);
            }
            TailSegment[] segments = tail.Segments;
            for (int i = 0; i < segments.Length; i++)
            {
                pose.TailLast[i] = segments[i].LastPosition;
                pose.TailCurrent[i] = segments[i].Position;
                pose.Tail[i] = segments[i].RenderPosition(timeStacker);
                double stretched = MathUtil.Lerp(segments[i].LastStretched, segments[i].Stretched, timeStacker);
                pose.TailRadii[i] = segments[i].Radius * stretched;
            }
            pose.CharacterOrigin = (pose.Chest + pose.Hips) * 0.5;
            pose.CharacterRenderScale = SimulationConstants.CharacterRenderScale;
            pose.FaceScaleX = SpriteRenderer.SelectFaceScaleX(pose);
            pose.SelectedFaceElement = "FaceA" + SpriteRenderer.SelectFaceFrame(pose);
            for (int i = 0; i < 2; i++)
                pose.ArmShoulders[i] = SpriteRenderer.ComputeArmShoulder(pose, i);
            pose.UpdateGraphicsBounds();
            return pose;
        }

        public void ApplyMovingSurfaceDelta(Vec2 delta)
        {
            if (delta.LengthSquared < 0.000001) return;
            head.Translate(delta);
            legs.Translate(delta);
            for (int i = 0; i < 2; i++) arms[i].Translate(delta);
            tail.Translate(delta);
            for (int i = 0; i < 2; i++)
            {
                drawPositions[i, 0] += delta;
                drawPositions[i, 1] += delta;
            }
        }
    }
}
