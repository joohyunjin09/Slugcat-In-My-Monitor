using System;
using System.Collections.Generic;
using System.Drawing;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Physics;

namespace RainWorldDesktopPet.Graphics
{
    public sealed class SlugcatPose
    {
        public long SimulationTick;
        public double TimeStacker;
        public double LogicTicksPerSecond;
        public double LogicStepSeconds;
        public double AccumulatorSeconds;
        public double SimulationTimeSeconds;
        public int SimulationStepsLastFrame;
        public double RenderFramesPerSecond;
        public double MonitorRefreshRate;
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
        public Vec2 HeadTarget;
        public Vec2 HeadVelocity;
        public Vec2 BodyUp;
        public Vec2 BodyRight;
        public Vec2 LookDirection;
        public Vec2 OriginalLookDirection;
        public Vec2[] Hands = new Vec2[2];
        public Vec2[] HandLast = new Vec2[2];
        public Vec2[] HandCurrent = new Vec2[2];
        public Vec2[] HandTargets = new Vec2[2];
        public Vec2[] ArmConnections = new Vec2[2];
        public Vec2[] ArmConnectionLast = new Vec2[2];
        public Vec2[] ArmConnectionCurrent = new Vec2[2];
        public Vec2[] ArmShoulders = new Vec2[2];
        public Vec2[] ArmDirections = new Vec2[2];
        public double[] ArmRotations = new double[2];
        public double[] ArmScaleY = new double[2];
        public double[] ArmMaxLengths = new double[2];
        public int[] ArmRetractCounters = new int[2];
        public LimbMode[] ArmModes = new LimbMode[2];
        public long[] ArmGripSurfaceIds = new long[2];
        public bool[] ArmVisible = new bool[2];
        public Vec2[] Elbows = new Vec2[2];
        public Vec2[] Feet = new Vec2[2];
        public Vec2[] FootLast = new Vec2[2];
        public Vec2[] FootCurrent = new Vec2[2];
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
        public int InputX;
        public int PreviousInputX;
        public int InputY;
        public bool InputJump;
        public bool Conscious = true;
        public bool Dead;
        public bool Blink;
        public bool IsAirborne;
        public bool IsRising;
        public bool IsFalling;
        public double AirborneCounter;
        public Vec2[] AirMovementContribution = new Vec2[2];
        public double[] AirHorizontalVelocityBefore = new double[2];
        public double[] AirHorizontalVelocityAfter = new double[2];
        public string AirControlBranch = string.Empty;
        public string CurrentMonitorName = string.Empty;
        public long CurrentMonitorId;
        public Rectangle CurrentMonitorBounds;
        public Rectangle CurrentMonitorWorkArea;
        public Rectangle CurrentTaskbarBounds;
        public DesktopTaskbarEdge CurrentTaskbarEdge;
        public double CurrentMonitorFloorY;
        public long CurrentSurfaceId;
        public DesktopSurfaceKind CurrentSurfaceKind = DesktopSurfaceKind.ScreenEdge;
        public double CurrentSurfaceLeft;
        public double CurrentSurfaceRight;
        public double CurrentSurfaceTop;
        public long TerrainImpactSequence;
        public int ImpactBodyChunk;
        public Vec2 PreImpactVelocity;
        public Vec2 PostImpactVelocity;
        public Vec2 ImpactDirection;
        public Vec2 ImpactCollisionNormal;
        public double ImpactSpeed;
        public long ImpactSurfaceId;
        public DesktopSurfaceKind ImpactSurfaceKind = DesktopSurfaceKind.ScreenEdge;
        public bool ImpactFirstContact;
        public bool TerrainImpactTriggered;
        public int CalculatedImpactStun;
        public int AppliedImpactStun;
        public bool ImpactWasOriginallyLethal;
        public bool ImpactSafetyOverrideApplied;
        public DesktopPetImpactResult DesktopImpactResult;
        public long ImpactStunDeadlineTick;
        public bool ImpactCausedDeath;
        public bool IsStunned;
        public int StunCounter;
        public int InitialStunValue;
        public bool Standing;
        public double Breath;
        public double LandingCompression;
        public Vec2 CharacterOrigin;
        public double CharacterRenderScale = SimulationConstants.CharacterRenderScale;
        public SlugcatId SelectedSlugcat;
        public SlugcatSkin CurrentSkin;
        public string OriginalSlugcatId = "White";
        public string VisualProfileName = "Default";
        public string MovementProfileDebug = string.Empty;
        public string AbilityDebug = string.Empty;
        public string AudioProfileDebug = string.Empty;
        public int BaseSpriteCount = 12;
        public int ExtraSpriteCount;
        public string GraphicsExtensions = "none";
        public string TailProfileName = "DefaultTail";
        public double TailRootRadius = 6.0;
        public Color VisualBodyColor = Color.White;
        public Color VisualEyeColor = Color.FromArgb(16, 16, 16);
        public Color VisualHeadColor = Color.White;
        public Color VisualArmColor = Color.White;
        public Color VisualHipsColor = Color.White;
        public Color VisualLegsColor = Color.White;
        public Color VisualTailColor = Color.White;
        public string BodyElement = "BodyA";
        public string HipsElement = "HipsA";
        public double VisualBodyScale = 1.0;
        public double VisualHipsScale = 1.0;
        public double VisualHeadScale = 1.0;
        public double ArmShoulderScale = 1.0;
        public ExtraGraphicsPartPose[] ExtraParts = new ExtraGraphicsPartPose[0];
        public string SelectedFaceElement = string.Empty;
        public string HeadElement = string.Empty;
        public Vec2 HeadSpritePosition;
        public double HeadRotation;
        public double HeadScaleX;
        public Vec2 FacePosition;
        public double FaceRotation;
        public double FaceScaleX;
        public string FaceSelectionReason = string.Empty;
        public Vec2 MousePosition;
        public double MouseDistanceToHead;
        public double MouseAttentionRadius;
        public double LastRelevantMouseClickTime;
        public double TimeSinceRelevantMouseClick;
        public double MouseAttentionTimeout;
        public bool MouseAttentionActive;
        public string TailRenderMode = string.Empty;
        public int TailMeshVertexCount;
        public Vec2 TailRoot;
        public Vec2 TailTip;
        public Vec2[] TailCrossSectionCenters = new Vec2[4];
        public Vec2[] TailTangents = new Vec2[4];
        public Vec2[] TailPerpendiculars = new Vec2[4];
        public Vec2[] TailMeshVertices = new Vec2[15];
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
            if (ExtraParts != null)
            {
                for (int i = 0; i < ExtraParts.Length; i++)
                {
                    ExtraGraphicsPartPose part = ExtraParts[i];
                    if (part == null || !part.Visible) continue;
                    Vec2 rendered = ToRenderedWorld(part.SpritePosition);
                    left = Math.Min(left, rendered.X);
                    top = Math.Min(top, rendered.Y);
                    right = Math.Max(right, rendered.X);
                    bottom = Math.Max(bottom, rendered.Y);
                }
            }
            double spriteReach = 24.0 * CharacterRenderScale;
            GraphicsBounds = RectangleF.FromLTRB((float)(left - spriteReach), (float)(top - spriteReach),
                (float)(right + spriteReach), (float)(bottom + spriteReach));
        }

        public Vec2 ToRenderedWorld(Vec2 point)
        {
            return DesktopWorldTransform.ToDesktop(point);
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
