using System;
using System.Collections.Generic;
using System.Drawing;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;

namespace RainWorldDesktopPet.Graphics
{
    public sealed class SlugcatPose
    {
        public long SimulationTick;
        public double TimeStacker;
        public Vec2[] ChunkLast = new Vec2[2];
        public Vec2[] ChunkCurrent = new Vec2[2];
        public Vec2[] ChunkRender = new Vec2[2];
        public Vec2[] DrawLast = new Vec2[2];
        public Vec2[] DrawCurrent = new Vec2[2];
        public Vec2 Chest;
        public Vec2 Hips;
        public Vec2 HeadLast;
        public Vec2 HeadCurrent;
        public Vec2 Head;
        public Vec2 BodyUp;
        public Vec2 BodyRight;
        public Vec2 LookDirection;
        public Vec2[] Hands = new Vec2[2];
        public Vec2[] HandLast = new Vec2[2];
        public Vec2[] HandCurrent = new Vec2[2];
        public Vec2[] HandTargets = new Vec2[2];
        public Vec2[] ArmConnections = new Vec2[2];
        public Vec2[] ArmConnectionLast = new Vec2[2];
        public Vec2[] ArmConnectionCurrent = new Vec2[2];
        public Vec2[] ArmShoulders = new Vec2[2];
        public double[] ArmMaxLengths = new double[2];
        public int[] ArmRetractCounters = new int[2];
        public LimbMode[] ArmModes = new LimbMode[2];
        public bool[] ArmVisible = new bool[2];
        public Vec2[] Elbows = new Vec2[2];
        public Vec2[] Feet = new Vec2[2];
        public Vec2[] FootTargets = new Vec2[2];
        public Vec2[] Knees = new Vec2[2];
        public Vec2 LegsLast;
        public Vec2 LegsCurrent;
        public Vec2 Legs;
        public Vec2 LegsDirection;
        public Vec2[] TailLast;
        public Vec2[] TailCurrent;
        public Vec2[] Tail;
        public double[] TailRadii;
        public int Facing;
        public AnimationIndex Animation;
        public BodyModeIndex BodyMode;
        public int AnimationFrame;
        public double Breath;
        public double LandingCompression;
        public Vec2 CharacterOrigin;
        public double CharacterRenderScale = SimulationConstants.CharacterRenderScale;
        public string SelectedFaceElement = string.Empty;
        public double FaceScaleX;
        public Vec2 HeadDirection;
        public RectangleF GraphicsBounds;
        public Rectangle OverlayBounds;
        public readonly List<SpritePlacement> SpritePlacements = new List<SpritePlacement>();

        public void UpdateGraphicsBounds()
        {
            Vec2 chest = ToRenderedWorld(Chest);
            Vec2 hips = ToRenderedWorld(Hips);
            Vec2 head = ToRenderedWorld(Head);
            Vec2 renderedLegs = ToRenderedWorld(Legs);
            double left = Math.Min(chest.X, Math.Min(hips.X, Math.Min(head.X, renderedLegs.X)));
            double top = Math.Min(chest.Y, Math.Min(hips.Y, Math.Min(head.Y, renderedLegs.Y)));
            double right = Math.Max(chest.X, Math.Max(hips.X, Math.Max(head.X, renderedLegs.X)));
            double bottom = Math.Max(chest.Y, Math.Max(hips.Y, Math.Max(head.Y, renderedLegs.Y)));
            IncludeRendered(Hands, this, ref left, ref top, ref right, ref bottom);
            IncludeRendered(Tail, this, ref left, ref top, ref right, ref bottom);
            double spriteReach = 24.0 * CharacterRenderScale;
            GraphicsBounds = RectangleF.FromLTRB((float)(left - spriteReach), (float)(top - spriteReach),
                (float)(right + spriteReach), (float)(bottom + spriteReach));
        }

        public Vec2 ToRenderedWorld(Vec2 point)
        {
            return CharacterOrigin + (point - CharacterOrigin) * CharacterRenderScale;
        }

        private static void IncludeRendered(Vec2[] points, SlugcatPose pose,
            ref double left, ref double top, ref double right, ref double bottom)
        {
            if (points == null) return;
            for (int i = 0; i < points.Length; i++)
            {
                Vec2 point = pose.ToRenderedWorld(points[i]);
                left = Math.Min(left, point.X);
                top = Math.Min(top, point.Y);
                right = Math.Max(right, point.X);
                bottom = Math.Max(bottom, point.Y);
            }
        }
    }

    public sealed class SpritePlacement
    {
        public string Name;
        public Vec2 PhysicsSource;
        public Vec2 InterpolatedPosition;
        public Vec2 Anchor;
        public RectangleF LocalRectangle;
        public Vec2 OverlayPosition;
        public Vec2 FinalScreenPosition;
    }
}
