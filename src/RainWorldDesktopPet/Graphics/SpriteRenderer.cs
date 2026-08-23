using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using RainWorldDesktopPet.AI;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Physics;
using RainWorldDesktopPet.RainWorld;

namespace RainWorldDesktopPet.Graphics
{
    public sealed class OriginalFaceState
    {
        public string HeadElement;
        public Vec2 HeadPosition;
        public double HeadRotation;
        public double HeadScaleX;
        public string FaceElement;
        public Vec2 FacePosition;
        public double FaceRotation;
        public double FaceScaleX;
        public string Reason;
    }

    public sealed class SpriteRenderer : IDisposable
    {
        private static readonly Color OutlineColor = Color.FromArgb(255, 28, 39, 51);
        private static readonly Color EyeColor = Color.FromArgb(255, 23, 32, 42);
        private readonly RainWorldAtlasSet atlas;
        private readonly Font debugFont = new Font("Consolas", 9.0f, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Dictionary<int, ImageAttributes> tintAttributes = new Dictionary<int, ImageAttributes>();
        private readonly Dictionary<int, SolidBrush> bodyBrushes = new Dictionary<int, SolidBrush>();
        private readonly PointF[] destinationPoints = new PointF[3];
        private readonly Vec2[] tailMeshVertices = new Vec2[15];
        private readonly PointF[] tailMeshPoints = new PointF[15];
        private readonly PointF[] tailTrianglePoints = new PointF[3];
        private readonly PointF[] tailRasterDestinationPoints = new PointF[3];
        private readonly Bitmap tailRaster;
        private readonly System.Drawing.Graphics tailRasterGraphics;
        private const int TailRasterSize = 128;
        public const int OriginalTailMeshVertexCount = 15;
        public const int OriginalTailMeshTriangleCount = 13;
        private static readonly int[,] TailTriangles =
        {
            { 0, 1, 2 }, { 1, 2, 3 }, { 4, 5, 6 }, { 5, 6, 7 },
            { 8, 9, 10 }, { 9, 10, 11 }, { 12, 13, 14 },
            { 2, 3, 4 }, { 3, 4, 5 }, { 6, 7, 8 }, { 7, 8, 9 },
            { 10, 11, 12 }, { 11, 12, 13 }
        };
        private static readonly int[] TailLeftEdge =
        {
            0, 2, 4, 6, 8, 10, 12, 14
        };
        private static readonly int[] TailRightEdge =
        {
            1, 3, 5, 7, 9, 11, 13, 14
        };
        private SlugcatPose activePose;
        private RenderSpace activeRenderSpace;

        public SpriteRenderer(RainWorldAtlasSet atlas)
        {
            this.atlas = atlas;
            tailRaster = new Bitmap(TailRasterSize, TailRasterSize,
                PixelFormat.Format32bppPArgb);
            tailRasterGraphics = System.Drawing.Graphics.FromImage(tailRaster);
            tailRasterGraphics.SmoothingMode = SmoothingMode.None;
            tailRasterGraphics.PixelOffsetMode = PixelOffsetMode.Half;
            tailRasterGraphics.CompositingMode = CompositingMode.SourceCopy;
            tailRasterGraphics.CompositingQuality = CompositingQuality.HighSpeed;
        }

        public bool UsesLocalAtlas { get { return atlas != null; } }

        public void Render(
            System.Drawing.Graphics graphics,
            SlugcatPose pose,
            Vec2 windowOrigin,
            bool debug,
            DesktopCollisionWorld world,
            Slugcat slugcat,
            DesktopPetAI ai,
            string assetStatus,
            SlugcatAppearance appearance)
        {
            Render(graphics, pose, new RenderSpace(new Rectangle((int)windowOrigin.X,
                (int)windowOrigin.Y, 1, 1)), debug, world, slugcat, ai, assetStatus, appearance);
        }

        public void Render(
            System.Drawing.Graphics graphics,
            SlugcatPose pose,
            RenderSpace renderSpace,
            bool debug,
            DesktopCollisionWorld world,
            Slugcat slugcat,
            DesktopPetAI ai,
            string assetStatus,
            SlugcatAppearance appearance)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            pose.SpritePlacements.Clear();
            pose.OverlayBounds = renderSpace.VirtualDesktopBounds;
            activePose = pose;
            activeRenderSpace = renderSpace;
            GraphicsState state = graphics.Save();
            try
            {
                double scale = pose.CharacterRenderScale;
                graphics.Transform = new Matrix((float)scale, 0.0f, 0.0f, (float)scale,
                    (float)-renderSpace.WorldOrigin.X,
                    (float)-renderSpace.WorldOrigin.Y);

                if (atlas != null)
                {
                    // Keep the procedural tail and legs behind the torso for
                    // the desktop view. Broad custom parts otherwise appear
                    // to pass across the front of the Slugcat.
                    DrawTail(graphics, pose, pose.VisualTailColor); // 2
                    DrawAtlasLegs(graphics, pose); // 4
                    DrawAtlasTorso(graphics, pose); // 0 Body, 1 Hips
                    DrawExtraGraphics(graphics, pose, ExtraGraphicsLayer.AfterTailBeforeHead);
                    DrawAtlasHeadPart(graphics, pose, pose.VisualHeadColor, false); // 3
                    DrawAtlasArm(graphics, pose, 0, pose.VisualArmColor); // 5
                    DrawAtlasArm(graphics, pose, 1, pose.VisualArmColor); // 6
                    DrawExtraGraphics(graphics, pose, ExtraGraphicsLayer.BehindFace);
                    DrawAtlasHeadPart(graphics, pose, pose.VisualHeadColor, true); // 9
                    DrawExtraGraphics(graphics, pose, ExtraGraphicsLayer.InFront);
                }
                else
                {
                    DrawTail(graphics, pose, pose.VisualTailColor);
                    DrawLimbs(graphics, pose, 0, pose.VisualArmColor);
                    DrawProceduralBody(graphics, pose);
                    DrawLimbs(graphics, pose, 1, pose.VisualArmColor);
                    DrawHead(graphics, pose, false, pose.VisualHeadColor);
                }

                if (debug)
                {
                    graphics.Transform = new Matrix(1.0f, 0.0f, 0.0f, 1.0f,
                        (float)-renderSpace.WorldOrigin.X, (float)-renderSpace.WorldOrigin.Y);
                    DrawDebugWorld(graphics, pose, world, slugcat, ai);
                }
            }
            finally
            {
                graphics.Restore(state);
                activePose = null;
                activeRenderSpace = null;
            }

            if (debug)
            {
                StringBuilder builder = new StringBuilder(3400);
                builder.AppendFormat("sim {0:0.#} Hz tick {1} step {2:0.000000}s time {3:0.000}s steps/frame {4}\n",
                    pose.LogicTicksPerSecond, pose.SimulationTick, pose.LogicStepSeconds,
                    pose.SimulationTimeSeconds, pose.SimulationStepsLastFrame);
                builder.AppendFormat("accumulator {0:0.000000}s timeStacker {1:0.000} | render {2:0.0} FPS monitor {3:0.#} Hz\n",
                    pose.AccumulatorSeconds, pose.TimeStacker, pose.RenderFramesPerSecond, pose.MonitorRefreshRate);
                builder.AppendFormat("AI {0} input {1} | body {2} animation {3} frame {4} facing/flip {5}\n",
                    ai.Behavior, slugcat.LastInput, pose.BodyMode, pose.Animation,
                    pose.AnimationFrame, pose.Facing);
                builder.AppendFormat("skin {0} originalID={1} profile={2} sprites base={3} extra={4}\n",
                    pose.CurrentSkin, pose.OriginalSlugcatId, pose.VisualProfileName,
                    pose.BaseSpriteCount, pose.ExtraSpriteCount);
                builder.AppendFormat("face {0} tail={1} extensions={2}\n",
                    pose.SelectedFaceElement, pose.TailProfileName, pose.GraphicsExtensions);
                for (int i = 0; i < pose.ExtraParts.Length; i++)
                {
                    ExtraGraphicsPartPose extra = pose.ExtraParts[i];
                    builder.AppendFormat("  extra[{0}] #{1} {2}/{3} last={4} pos={5} render={6} rot={7:0.##} layer={8} visible={9}\n",
                        i, extra.OriginalSpriteIndex, extra.ExtensionName, extra.Element,
                        extra.LastPosition, extra.CurrentPosition, extra.RenderPosition,
                        extra.Rotation, extra.Layer, extra.Visible);
                }
                VirtualInput[] inputHistory = slugcat.Movement.InputHistory;
                builder.AppendFormat("input history now/1/2/3: {0} | {1} | {2} | {3}\n",
                    inputHistory[0], inputHistory[1], inputHistory[2], inputHistory[3]);
                builder.AppendFormat("physics gravity {0:0.###}/tick air {1:0.###} maxFall none connection {2:0.###} world x{3:0.00} snapshot {4}\n",
                    SimulationConstants.GravityPerTick, SimulationConstants.AirFriction,
                    slugcat.BodyConnection.Distance, SimulationConstants.DesktopWorldScale,
                    world.CurrentSnapshot.Version);
                builder.AppendFormat("monitor {0} id={1} bounds={2} work={3} taskbar={4}/{5} floorY={6:0.###}\n",
                    pose.CurrentMonitorName, pose.CurrentMonitorId, pose.CurrentMonitorBounds,
                    pose.CurrentMonitorWorkArea, pose.CurrentTaskbarEdge,
                    pose.CurrentTaskbarBounds, pose.CurrentMonitorFloorY);
                builder.AppendFormat("current surface {0}/{1} left={2:0.###} right={3:0.###} top={4:0.###}\n",
                    pose.CurrentSurfaceId, pose.CurrentSurfaceKind, pose.CurrentSurfaceLeft,
                    pose.CurrentSurfaceRight, pose.CurrentSurfaceTop);
                for (int i = 0; i < slugcat.BodyChunks.Length; i++)
                {
                    BodyChunk chunk = slugcat.BodyChunks[i];
                    builder.AppendFormat("chunk{0} pos {1} last {2} render {3} vel {4} contact F/L/R={5}/{6}/{7} surface={8} wall={9}\n",
                        i, chunk.Position, chunk.LastPosition, pose.ChunkRender[i], chunk.Velocity,
                        chunk.ContactFloor, chunk.ContactLeft, chunk.ContactRight,
                        chunk.SupportingSurfaceId, chunk.WallSurfaceId);
                    AppendSurfaceDebug(builder, world, chunk);
                }
                builder.AppendFormat("head {0}->{1}->{2} target {3} vel {4} originalLook {5} finalLook {6} dir {7}\n",
                    pose.HeadLast, pose.HeadCurrent, pose.Head, pose.HeadTarget,
                    pose.HeadVelocity, pose.OriginalLookDirection, pose.LookDirection,
                    pose.HeadDirection);
                builder.AppendFormat("face animation={0} body={1} facing/flip={2} blink={3} head={4} at {5} rot={6:0.###} scaleX={7:0.###}\n",
                    pose.Animation, pose.BodyMode, pose.Facing, pose.Blink, pose.HeadElement,
                    pose.HeadSpritePosition, pose.HeadRotation, pose.HeadScaleX);
                builder.AppendFormat("face element={0} at {1} rot={2:0.###} scaleX={3:0.###} reason={4}\n",
                    pose.SelectedFaceElement, pose.FacePosition, pose.FaceRotation,
                    pose.FaceScaleX, pose.FaceSelectionReason);
                builder.AppendFormat("mouse pos={0} headDistance={1:0.###} radius={2:0.###} lastRelevantClick={3:0.###} since={4:0.###} timeout={5:0.###} active={6}\n",
                    pose.MousePosition, pose.MouseDistanceToHead, pose.MouseAttentionRadius,
                    pose.LastRelevantMouseClickTime, pose.TimeSinceRelevantMouseClick,
                    pose.MouseAttentionTimeout, pose.MouseAttentionActive);
                builder.AppendFormat("air input x prev={0}/{1} y/jump={2}/{3} vx before={4:0.###},{5:0.###} after={6:0.###},{7:0.###}\n",
                    pose.InputX, pose.PreviousInputX, pose.InputY, pose.InputJump,
                    pose.AirHorizontalVelocityBefore[0], pose.AirHorizontalVelocityBefore[1],
                    pose.AirHorizontalVelocityAfter[0], pose.AirHorizontalVelocityAfter[1]);
                builder.AppendFormat("air gravity={0:0.###} contribution c0={1} c1={2} body={3} animation={4} airborne={5} rising={6} falling={7} counter={8:0.###} branch={9}\n",
                    SimulationConstants.GravityPerTick, pose.AirMovementContribution[0],
                    pose.AirMovementContribution[1], pose.BodyMode, pose.Animation, pose.IsAirborne,
                    pose.IsRising, pose.IsFalling, pose.AirborneCounter,
                    pose.AirControlBranch);
                builder.AppendFormat("impact seq={0} chunk={1} pre={2} post={3} direction={4} normal={5} speed={6:0.###} surface={7}/{8} first={9} triggered={10}\n",
                    pose.TerrainImpactSequence, pose.ImpactBodyChunk, pose.PreImpactVelocity,
                    pose.PostImpactVelocity, pose.ImpactDirection,
                    pose.ImpactCollisionNormal, pose.ImpactSpeed, pose.ImpactSurfaceId,
                    pose.ImpactSurfaceKind, pose.ImpactFirstContact,
                    pose.TerrainImpactTriggered);
                builder.AppendFormat("impact safety result={0} originallyLethal={1} originalStun={2} applied={3} override={4} deadline={5} max={6} ticks/{7:0.0}s death={8}\n",
                    pose.DesktopImpactResult, pose.ImpactWasOriginallyLethal,
                    pose.CalculatedImpactStun, pose.AppliedImpactStun,
                    pose.ImpactSafetyOverrideApplied, pose.ImpactStunDeadlineTick,
                    SimulationConstants.MaxImpactStunTicks,
                    SimulationConstants.MaxImpactStunDurationSeconds,
                    pose.ImpactCausedDeath);
                builder.AppendFormat("stun active={0} counter={1} initial={2} conscious={3} dead={4} body={5} animation={6} standing={7} face={8}\n",
                    pose.IsStunned, pose.StunCounter, pose.InitialStunValue,
                    pose.Conscious, pose.Dead, pose.BodyMode, pose.Animation,
                    pose.Standing, pose.SelectedFaceElement);
                for (int i = 0; i < 2; i++)
                {
                    builder.AppendFormat("hand{0} {1}->{2}->{3} shoulder {4} dir {5} rot {6:0.###} scaleY {7:0.###} target {8} mode {9} grip {10}; foot {11}->{12}->{13} target {14}\n",
                        i, pose.HandLast[i], pose.HandCurrent[i], pose.Hands[i],
                        pose.ArmShoulders[i], pose.ArmDirections[i], pose.ArmRotations[i],
                        pose.ArmScaleY[i], pose.HandTargets[i], pose.ArmModes[i],
                        pose.ArmGripSurfaceIds[i], pose.FootLast[i], pose.FootCurrent[i],
                        pose.Feet[i], pose.FootTargets[i]);
                }
                Vec2 tailPrevious = pose.TailRoot;
                builder.AppendFormat("tail root={0} tip={1} mode={2} meshVertices={3}\n",
                    pose.TailRoot, pose.TailTip, pose.TailRenderMode,
                    pose.TailMeshVertexCount);
                for (int i = 0; i < pose.Tail.Length; i++)
                {
                    builder.AppendFormat("tail{0} last={1} current={2} render={3} radius={4:0.###} distance={5:0.###} tangent={6} perp={7}\n",
                        i, pose.TailLast[i], pose.TailCurrent[i], pose.Tail[i],
                        pose.TailRadii[i], Vec2.Distance(tailPrevious, pose.Tail[i]),
                        pose.TailTangents[i], pose.TailPerpendiculars[i]);
                    tailPrevious = pose.Tail[i];
                }
                builder.AppendFormat("tail mesh L/R root={0}/{1} joint0={2}/{3} joint1={4}/{5} joint2={6}/{7} tip={8} | graphics {9} overlay {10}\n",
                    pose.TailMeshVertices[0], pose.TailMeshVertices[1],
                    pose.TailMeshVertices[4], pose.TailMeshVertices[5],
                    pose.TailMeshVertices[8], pose.TailMeshVertices[9],
                    pose.TailMeshVertices[12], pose.TailMeshVertices[13],
                    pose.TailMeshVertices[14], pose.GraphicsBounds, pose.OverlayBounds);
                builder.AppendFormat("renderScale {0:0.00} | attention final={1}/{2} original={3}/{4}\n{5}",
                    pose.CharacterRenderScale, ai.Attention.Kind, ai.Attention.Target,
                    ai.OriginalAttentionKind, ai.OriginalAttentionTarget, assetStatus);
                string text = builder.ToString();
                using (Brush shadow = new SolidBrush(Color.FromArgb(210, 0, 0, 0)))
                using (Brush foreground = new SolidBrush(Color.FromArgb(255, 235, 255, 235)))
                {
                    graphics.DrawString(text, debugFont, shadow, new PointF(9.0f, 9.0f));
                    graphics.DrawString(text, debugFont, foreground, new PointF(8.0f, 8.0f));
                }
            }
        }

        private static void AppendSurfaceDebug(StringBuilder builder, DesktopCollisionWorld world, BodyChunk chunk)
        {
            long id = chunk.SupportingSurfaceId != 0 ? chunk.SupportingSurfaceId : chunk.WallSurfaceId;
            if (id == 0) return;
            DesktopSurfaceKind kind = chunk.SupportingSurfaceId != 0
                ? chunk.SupportingSurfaceKind
                : chunk.WallSurfaceKind;
            DesktopSurface surface;
            if (!world.TryGetSurface(id, kind, out surface))
            {
                builder.AppendFormat("  surface {0}/{1} MISSING\n", id, kind);
                return;
            }
            builder.AppendFormat("  surface {0}/{1} LTRB={2},{3},{4},{5} prev={6} current={7} velocity={8} missed={9}\n",
                surface.Id, surface.Kind, surface.Left, surface.Top, surface.Right, surface.Bottom,
                surface.PreviousWindowBounds, surface.CurrentWindowBounds,
                surface.MovementVelocity, surface.MissingRefreshes);
        }

        private void DrawTail(System.Drawing.Graphics graphics, SlugcatPose pose, Color bodyColor)
        {
            if (pose.Tail == null || pose.Tail.Length != SimulationConstants.TailSegmentCount)
                return;
            // TailSegment is simulation-only. Atlas and procedural body paths
            // both submit this one continuous PlayerGraphics-equivalent mesh;
            // there is no segmented line/sprite fallback.
            DrawOriginalTailMesh(graphics, pose, bodyColor);
        }

        private void DrawOriginalTailMesh(System.Drawing.Graphics graphics, SlugcatPose pose, Color bodyColor)
        {
            PopulateOriginalTailMeshVertices(pose, tailMeshVertices);
            for (int i = 0; i < tailMeshVertices.Length; i++)
                tailMeshPoints[i] = tailMeshVertices[i].ToPointF();

            // Rain World draws this TriangleMesh into its 1:1 internal render
            // target with MSAA disabled, then point-filters that target to the
            // display. Rasterize the DLL's 13 triangles at simulation-pixel
            // resolution first so the tail shares the atlas' pixel grid.
            float minX = tailMeshPoints[0].X;
            float minY = tailMeshPoints[0].Y;
            float maxX = minX;
            float maxY = minY;
            for (int i = 1; i < tailMeshPoints.Length; i++)
            {
                minX = Math.Min(minX, tailMeshPoints[i].X);
                minY = Math.Min(minY, tailMeshPoints[i].Y);
                maxX = Math.Max(maxX, tailMeshPoints[i].X);
                maxY = Math.Max(maxY, tailMeshPoints[i].Y);
            }

            int rasterLeft = (int)Math.Floor(minX) - 2;
            int rasterTop = (int)Math.Floor(minY) - 2;
            int rasterWidth = (int)Math.Ceiling(maxX) + 2 - rasterLeft;
            int rasterHeight = (int)Math.Ceiling(maxY) + 2 - rasterTop;
            if (rasterWidth <= TailRasterSize && rasterHeight <= TailRasterSize)
            {
                tailRasterGraphics.Clear(Color.Transparent);
                for (int i = 0; i < TailTriangles.GetLength(0); i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        PointF point = tailMeshPoints[TailTriangles[i, j]];
                        tailTrianglePoints[j] = new PointF(
                            point.X - rasterLeft, point.Y - rasterTop);
                    }
                    tailRasterGraphics.FillPolygon(GetBodyBrush(bodyColor),
                        tailTrianglePoints, FillMode.Winding);
                }

                tailRasterDestinationPoints[0] = new PointF(rasterLeft, rasterTop);
                tailRasterDestinationPoints[1] = new PointF(
                    rasterLeft + rasterWidth, rasterTop);
                tailRasterDestinationPoints[2] = new PointF(
                    rasterLeft, rasterTop + rasterHeight);
                graphics.DrawImage(tailRaster, tailRasterDestinationPoints,
                    new RectangleF(0.0f, 0.0f, rasterWidth, rasterHeight),
                    GraphicsUnit.Pixel);
            }
            else
            {
                GraphicsState state = graphics.Save();
                try
                {
                    graphics.SmoothingMode = SmoothingMode.None;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    for (int i = 0; i < TailTriangles.GetLength(0); i++)
                    {
                        for (int j = 0; j < 3; j++)
                            tailTrianglePoints[j] = tailMeshPoints[TailTriangles[i, j]];
                        graphics.FillPolygon(GetBodyBrush(bodyColor),
                            tailTrianglePoints, FillMode.Winding);
                    }
                }
                finally
                {
                    graphics.Restore(state);
                }
            }
            Array.Copy(tailMeshVertices, pose.TailMeshVertices,
                OriginalTailMeshVertexCount);
            pose.TailRenderMode = "OriginalTriangleMesh";
            pose.TailMeshVertexCount = tailMeshVertices.Length;
        }

        public static Vec2[] BuildOriginalTailMeshVertices(SlugcatPose pose)
        {
            if (pose == null) throw new ArgumentNullException("pose");
            if (pose.Tail == null || pose.Tail.Length < 4 ||
                pose.TailRadii == null || pose.TailRadii.Length < 4)
                throw new ArgumentException("The original PlayerGraphics tail requires four segments.", "pose");

            Vec2[] vertices = new Vec2[OriginalTailMeshVertexCount];
            PopulateOriginalTailMeshVertices(pose, vertices);
            return vertices;
        }

        private static void PopulateOriginalTailMeshVertices(SlugcatPose pose, Vec2[] vertices)
        {
            Vec2 previous = (pose.Hips * 3.0 + pose.Chest) / 4.0;
            pose.TailRoot = previous;
            double previousRadius = pose.TailRootRadius;
            for (int i = 0; i < 4; i++)
            {
                Vec2 current = pose.Tail[i];
                Vec2 direction = (current - previous).Normalized;
                Vec2 perpendicular = direction.Perpendicular;
                pose.TailCrossSectionCenters[i] = previous;
                pose.TailTangents[i] = direction;
                pose.TailPerpendiculars[i] = perpendicular;
                double halfAdvance = i == 0 ? 0.0 : Vec2.Distance(current, previous) / 5.0;
                Vec2 previousWidth = perpendicular * previousRadius;
                vertices[i * 4] = previous - previousWidth + direction * halfAdvance;
                vertices[i * 4 + 1] = previous + previousWidth + direction * halfAdvance;
                if (i < 3)
                {
                    Vec2 currentWidth = perpendicular * pose.TailRadii[i];
                    vertices[i * 4 + 2] = current - currentWidth - direction * halfAdvance;
                    vertices[i * 4 + 3] = current + currentWidth - direction * halfAdvance;
                }
                else
                {
                    vertices[14] = current;
                }
                previousRadius = pose.TailRadii[i];
                previous = current;
            }
            pose.TailTip = pose.Tail[3];
            Array.Copy(vertices, pose.TailMeshVertices,
                OriginalTailMeshVertexCount);
        }

        private SolidBrush GetBodyBrush(Color color)
        {
            SolidBrush brush;
            if (bodyBrushes.TryGetValue(color.ToArgb(), out brush)) return brush;
            brush = new SolidBrush(color);
            bodyBrushes[color.ToArgb()] = brush;
            return brush;
        }

        private static void DrawLimbs(System.Drawing.Graphics graphics, SlugcatPose pose, int layer, Color bodyColor)
        {
            int sideIndex = layer == 0 ? 0 : 1;
            DrawLimb(graphics, pose.Chest, pose.Elbows[sideIndex], pose.Hands[sideIndex], 5.0f, bodyColor);
            DrawLimb(graphics, pose.Hips, pose.Knees[sideIndex], pose.Feet[sideIndex], 5.5f, bodyColor);
        }

        private static void DrawLimb(System.Drawing.Graphics graphics, Vec2 start, Vec2 joint, Vec2 end, float width, Color bodyColor)
        {
            using (Pen outline = CreateRoundPen(OutlineColor, width + 4.0f))
            using (Pen fill = CreateRoundPen(bodyColor, width))
            {
                PointF[] points = { start.ToPointF(), joint.ToPointF(), end.ToPointF() };
                graphics.DrawLines(outline, points);
                graphics.DrawLines(fill, points);
            }
            FillCircle(graphics, end, width * 0.65, bodyColor);
        }

        private static void DrawProceduralBody(System.Drawing.Graphics graphics, SlugcatPose pose)
        {
            float bodyWidth = (float)((18.0 - pose.LandingCompression * 2.5) * pose.VisualBodyScale);
            using (Pen outline = CreateRoundPen(OutlineColor, bodyWidth + 6.0f))
            using (Pen body = CreateRoundPen(pose.VisualBodyColor, bodyWidth))
            {
                graphics.DrawLine(outline, pose.Chest.ToPointF(), pose.Hips.ToPointF());
                graphics.DrawLine(body, pose.Chest.ToPointF(), pose.Hips.ToPointF());
            }
            FillCircle(graphics, pose.Chest, 10.3, OutlineColor);
            FillCircle(graphics, pose.Hips, 10.0, OutlineColor);
            FillCircle(graphics, pose.Chest, 7.4 * pose.VisualBodyScale, pose.VisualBodyColor);
            FillCircle(graphics, pose.Hips, 7.1 * pose.VisualHipsScale, Shade(pose.VisualHipsColor));
        }

        private void DrawAtlasTorso(System.Drawing.Graphics graphics, SlugcatPose pose)
        {
            double bodyAngle = AimScreen(pose.Hips, pose.Chest);
            double verticality = MathUtil.InverseLerp(0.3, 0.5, Math.Abs(pose.BodyUp.Y));
            double bodyWidth = pose.VisualBodyScale + MathUtil.Lerp(-0.05, 0.05, pose.Breath) * verticality;
            double hipsWidth = pose.VisualHipsScale + 0.05 * pose.Breath;

            Vec2 bodyPosition = pose.Chest + new Vec2(0.0, -0.5 * pose.Breath * (1.0 - verticality));
            DrawElement(graphics, pose.BodyElement, bodyPosition, bodyAngle, bodyWidth, 1.0,
                0.5, 0.7894737, pose.VisualBodyColor);
            Vec2 hipsPosition = (pose.Hips * 2.0 + pose.Chest) / 3.0;
            Vec2 tailTarget = pose.Tail.Length > 0 ? pose.Tail[0] : pose.Hips + (pose.Hips - pose.Chest);
            double hipsAngle = AimScreen(pose.Chest, tailTarget);
            DrawElement(graphics, pose.HipsElement, hipsPosition, hipsAngle, hipsWidth, 1.0,
                0.5, 0.5, pose.VisualHipsColor);
        }

        private void DrawAtlasLegs(System.Drawing.Graphics graphics, SlugcatPose pose)
        {
            string legsName;
            if (pose.BodyMode == BodyModeIndex.Stand)
                legsName = "LegsA" + PositiveModulo(pose.AnimationFrame, 7);
            else if (pose.BodyMode == BodyModeIndex.Crawl)
                legsName = "LegsACrawling" + PositiveModulo(pose.AnimationFrame / 2, 6);
            else if (pose.BodyMode == BodyModeIndex.WallClimb)
                legsName = "LegsAWall";
            else
                legsName = "LegsAAir0";
            double legsAngle = AimScreen(pose.LegsDirection, Vec2.Zero);
            double legsScaleX = pose.BodyMode == BodyModeIndex.Stand || pose.BodyMode == BodyModeIndex.Crawl
                ? pose.Facing
                : 1.0;
            DrawElement(graphics, legsName, pose.Legs, legsAngle, legsScaleX, 1.0,
                0.5, 0.25, pose.VisualLegsColor);
        }

        private void DrawAtlasArm(System.Drawing.Graphics graphics, SlugcatPose pose, int index, Color bodyColor)
        {
            Vec2 hand = pose.Hands[index];
            Vec2 shoulder = ComputeArmShoulder(pose, index);
            pose.ArmShoulders[index] = shoulder;
            if (!pose.ArmVisible[index]) return;
            int frame = MathUtil.Clamp((int)Math.Round(Vec2.Distance(hand, shoulder) / 2.0), 0, 12);
            double angle = ComputeArmRotation(pose, index);
            double scaleY = ComputeArmScaleY(pose, index);
            DrawElement(graphics, "PlayerArm" + frame, hand, angle, 1.0, scaleY,
                0.9, 0.5, bodyColor);
        }

        private void DrawExtraGraphics(System.Drawing.Graphics graphics, SlugcatPose pose,
            ExtraGraphicsLayer layer)
        {
            if (pose.ExtraParts == null) return;
            for (int i = 0; i < pose.ExtraParts.Length; i++)
            {
                ExtraGraphicsPartPose part = pose.ExtraParts[i];
                if (part == null || !part.Visible || part.Layer != layer) continue;
                DrawElement(graphics, part.Element, part.SpritePosition, part.Rotation,
                    part.ScaleX, part.ScaleY, part.AnchorX, part.AnchorY, part.Tint);
            }
        }

        private void DrawAtlasHeadPart(System.Drawing.Graphics graphics, SlugcatPose pose, Color bodyColor, bool faceOnly)
        {
            OriginalFaceState state = ResolveOriginalFaceState(pose);

            if (!faceOnly)
            {
                DrawElement(graphics, state.HeadElement, state.HeadPosition,
                    state.HeadRotation, state.HeadScaleX,
                    1.0, 0.5, 0.5, bodyColor);
                return;
            }

            pose.HeadElement = state.HeadElement;
            pose.HeadSpritePosition = state.HeadPosition;
            pose.HeadRotation = state.HeadRotation;
            pose.HeadScaleX = state.HeadScaleX;
            pose.SelectedFaceElement = state.FaceElement;
            pose.FacePosition = state.FacePosition;
            pose.FaceRotation = state.FaceRotation;
            pose.FaceScaleX = state.FaceScaleX;
            pose.FaceSelectionReason = state.Reason;
                DrawElement(graphics, state.FaceElement, state.FacePosition,
                    state.FaceRotation, state.FaceScaleX, 1.0, 0.5, 0.5, pose.VisualEyeColor);
        }

        private void DrawHead(System.Drawing.Graphics graphics, SlugcatPose pose, bool useAtlas, Color bodyColor)
        {
            double angle = SelectHeadAngle(pose);
            if (useAtlas)
            {
                OriginalFaceState state = ResolveOriginalFaceState(pose);
                DrawElement(graphics, state.HeadElement, state.HeadPosition,
                    state.HeadRotation, state.HeadScaleX,
                    1.0, 0.5, 0.5, bodyColor);
                DrawElement(graphics, state.FaceElement, state.FacePosition,
                    state.FaceRotation, state.FaceScaleX,
                    1.0, 0.5, 0.5, pose.VisualEyeColor);
                return;
            }

            Vec2 right = pose.BodyRight;
            Vec2 up = pose.BodyUp;
            PointF[] leftEar =
            {
                (pose.Head - right * 7.0 + up * 2.0).ToPointF(),
                (pose.Head - right * 10.5 + up * 13.5).ToPointF(),
                (pose.Head - right * 1.8 + up * 8.0).ToPointF()
            };
            PointF[] rightEar =
            {
                (pose.Head + right * 7.0 + up * 2.0).ToPointF(),
                (pose.Head + right * 10.5 + up * 13.5).ToPointF(),
                (pose.Head + right * 1.8 + up * 8.0).ToPointF()
            };
            using (Brush outline = new SolidBrush(OutlineColor))
            using (Brush body = new SolidBrush(bodyColor))
            {
                graphics.FillPolygon(outline, leftEar);
                graphics.FillPolygon(outline, rightEar);
                FillCircle(graphics, pose.Head, 11.8, OutlineColor);
                graphics.FillPolygon(body, leftEar);
                graphics.FillPolygon(body, rightEar);
                FillCircle(graphics, pose.Head, 8.9, bodyColor);
            }

            Vec2 eyeCenter = pose.Head + pose.LookDirection * 1.8 + up * 0.5;
            FillCircle(graphics, eyeCenter - right * 3.2, 1.15, pose.VisualEyeColor);
            FillCircle(graphics, eyeCenter + right * 3.2, 1.15, pose.VisualEyeColor);
        }

        public static int SelectFaceFrame(SlugcatPose pose)
        {
            if (pose.Animation == AnimationIndex.Sleep) return 1;
            if (pose.BodyMode == BodyModeIndex.Crawl ||
                (pose.BodyMode == BodyModeIndex.Stand && pose.InputX != 0)) return 4;

            Vec2 lookOffset = pose.LookDirection * 3.0;
            Vec2 faceAxis = pose.Head - pose.Hips;
            faceAxis.X *= 1.0 - MathUtil.Clamp(lookOffset.Length / 3.0, 0.0, 1.0);
            faceAxis = faceAxis.Normalized;
            return MathUtil.Clamp(
                (int)Math.Round(Math.Abs(AimScreen(Vec2.Zero, faceAxis) / 22.5)), 0, 8);
        }

        public static double SelectHeadAngle(SlugcatPose pose)
        {
            if (pose.Animation == AnimationIndex.Sleep) return 45.0 * pose.Facing;
            Vec2 bodyMiddle = (pose.Chest + pose.Hips) * 0.5;
            return AimScreen(bodyMiddle, pose.Head);
        }

        public static double SelectFaceScaleX(SlugcatPose pose)
        {
            double headAngle = SelectHeadAngle(pose);
            double headFacing = headAngle < 0.0 ? -1.0 : 1.0;
            if (pose.BodyMode == BodyModeIndex.Crawl)
            {
                double bodyDirectionX = pose.Chest.X - pose.Hips.X;
                return Math.Abs(bodyDirectionX) > 0.5
                    ? (bodyDirectionX < 0.0 ? -1.0 : 1.0)
                    : (pose.Facing < 0 ? -1.0 : 1.0);
            }
            if (pose.Animation == AnimationIndex.Sleep)
                return BodyAxisSign(pose);
            if (pose.BodyMode == BodyModeIndex.Stand && pose.InputX != 0)
                return headFacing;
            Vec2 look = pose.LookDirection * 3.0;
            return Math.Abs(look.X) < 0.1 ? headFacing : (look.X < 0.0 ? -1.0 : 1.0);
        }

        public static OriginalFaceState ResolveOriginalFaceState(SlugcatPose pose)
        {
            if (pose == null) throw new ArgumentNullException("pose");
            OriginalFaceState result = new OriginalFaceState();
            double rawHeadAngle = AimScreen((pose.Chest + pose.Hips) * 0.5, pose.Head);
            int headFrame = MathUtil.Clamp(
                (int)Math.Round(Math.Abs(rawHeadAngle / 360.0 * 34.0)), 0, 17);
            double headScaleX = rawHeadAngle < 0.0 ? -1.0 : 1.0;
            Vec2 headPosition = pose.Head;
            Vec2 faceLook = pose.LookDirection * 3.0;
            int faceFrame;
            double faceRotation = 0.0;
            double faceScaleX;
            string faceElement;
            string reason;

            if (!pose.Conscious)
            {
                faceLook = Vec2.Zero;
                headFrame = 0;
                faceElement = pose.Dead ? "FaceDead" : "FaceStunned";
                faceRotation = rawHeadAngle;
                faceScaleX = headScaleX;
                reason = pose.Dead ? "Dead" : "Stunned";
            }
            else if (pose.Animation == AnimationIndex.Sleep)
            {
                double bodyAxis = BodyAxisSign(pose);
                headFrame = 4;
                rawHeadAngle = 45.0 * bodyAxis;
                headScaleX = rawHeadAngle < 0.0 ? -1.0 : 1.0;
                headPosition += new Vec2(bodyAxis * 2.0, -1.0);
                faceFrame = 1;
                faceElement = FaceFamily(pose) + faceFrame;
                faceScaleX = bodyAxis;
                faceLook = new Vec2(-4.0 * bodyAxis, 2.0);
                reason = "Sleep";
            }
            else if (pose.BodyMode == BodyModeIndex.ZeroG)
            {
                headFrame = 0;
                faceElement = FaceFamily(pose) + "0";
                faceScaleX = SelectDefaultFaceScaleX(pose, rawHeadAngle);
                faceRotation = rawHeadAngle;
                reason = "ZeroG";
            }
            else if (pose.BodyMode == BodyModeIndex.Crawl ||
                     (pose.BodyMode == BodyModeIndex.Stand && pose.InputX != 0))
            {
                bool crawl = pose.BodyMode == BodyModeIndex.Crawl;
                headFrame = crawl ? 7 : 6;
                faceFrame = 4;
                faceElement = FaceFamily(pose) + faceFrame;
                faceLook.X = 0.0;
                faceScaleX = crawl ? BodyAxisSign(pose) : headScaleX;
                reason = crawl ? "Crawl" : "StandMovement";
            }
            else
            {
                faceFrame = SelectFaceFrame(pose);
                faceElement = FaceFamily(pose) + faceFrame;
                faceScaleX = SelectDefaultFaceScaleX(pose, rawHeadAngle);
                if (pose.IsAirborne)
                    reason = pose.IsRising ? "AirborneRising" : "AirborneFalling";
                else if (pose.BodyMode == BodyModeIndex.WallClimb)
                    reason = "WallClimb";
                else if (pose.BodyMode == BodyModeIndex.ClimbingOnBeam)
                    reason = "Beam";
                else if (pose.Animation == AnimationIndex.LedgeCrawl)
                    reason = "Ledge";
                else
                    reason = "Original";
            }

            if (pose.MouseAttentionActive) reason += "+MouseAttention";
            SlugcatVisualProfile profile = SlugcatVisualProfiles.Get(pose.CurrentSkin);
            result.HeadElement = profile.HeadFamily + headFrame;
            result.HeadPosition = headPosition;
            result.HeadRotation = rawHeadAngle;
            result.HeadScaleX = headScaleX * pose.VisualHeadScale;
            result.FaceElement = faceElement;
            result.FacePosition = headPosition + faceLook + new Vec2(0.0, 2.0);
            result.FaceRotation = faceRotation;
            result.FaceScaleX = faceScaleX;
            result.Reason = reason;
            return result;
        }

        private static double SelectDefaultFaceScaleX(SlugcatPose pose, double headAngle)
        {
            Vec2 look = pose.LookDirection * 3.0;
            if (Math.Abs(look.X) < 0.1) return headAngle < 0.0 ? -1.0 : 1.0;
            return look.X < 0.0 ? -1.0 : 1.0;
        }

        private static string FaceFamily(SlugcatPose pose)
        {
            return SlugcatVisualProfiles.Get(pose.CurrentSkin).ResolveFaceFamily(
                pose.Blink, SelectFaceScaleX(pose));
        }

        private static double BodyAxisSign(SlugcatPose pose)
        {
            double bodyDirectionX = pose.Chest.X - pose.Hips.X;
            if (Math.Abs(bodyDirectionX) > 0.5)
                return bodyDirectionX < 0.0 ? -1.0 : 1.0;
            return pose.Facing < 0 ? -1.0 : 1.0;
        }

        public static Vec2 ComputeArmShoulder(SlugcatPose pose, int index)
        {
            double bodyAngle = AimScreen(pose.Hips, pose.Chest);
            double shoulderSpread = 4.5 / (pose.ArmRetractCounters[index] + 1.0);
            shoulderSpread *= Math.Abs(Math.Cos(bodyAngle / 360.0 * Math.PI * 2.0));
            shoulderSpread *= pose.ArmShoulderScale;
            Vec2 shoulderOffset = new Vec2((-1.0 + 2.0 * index) * shoulderSpread, 3.5);
            return pose.Chest + RotateScreen(shoulderOffset, bodyAngle);
        }

        // PlayerGraphics.DrawSprites recomputes this directly from the
        // interpolated hand and shoulder every draw. There is intentionally no
        // retained angle, wrap interpolation, clamp or stabilizer.
        public static double ComputeArmRotation(SlugcatPose pose, int index)
        {
            return AimScreen(pose.Hands[index], ComputeArmShoulder(pose, index)) + 90.0;
        }

        public static double ComputeArmScaleY(SlugcatPose pose, int index)
        {
            if (pose.BodyMode == BodyModeIndex.Crawl)
                return pose.Chest.X < pose.Hips.X ? -1.0 : 1.0;
            if (pose.BodyMode == BodyModeIndex.WallClimb)
                return pose.Facing == -1 ? -1.0 : 1.0;
            return SignedDistanceToLine(pose.Hands[index], pose.Chest, pose.Hips) < 0.0
                ? -1.0
                : 1.0;
        }

        private void DrawElement(System.Drawing.Graphics graphics, string name, Vec2 position, double angle, double scaleX, double scaleY, double anchorX, double anchorY, Color tint)
        {
            // Futile accepts zero-scale sprites (for example the outside row of
            // Spearmaster's tinyStar speckles); GDI+ rejects a singular matrix.
            if (Math.Abs(scaleX) < 0.000001 || Math.Abs(scaleY) < 0.000001) return;
            AtlasSprite sprite;
            if (!atlas.TryGet(name, out sprite)) return;
            AtlasElement element = sprite.Element;
            GraphicsState state = graphics.Save();
            try
            {
                graphics.TranslateTransform((float)position.X, (float)position.Y);
                graphics.RotateTransform((float)angle);
                graphics.ScaleTransform((float)scaleX, (float)scaleY);
                RectangleF destination = element.GetLocalRectangle(anchorX, anchorY);
                RectangleF source = new RectangleF(element.Frame.X, element.Frame.Y, element.Frame.Width, element.Frame.Height);
                destinationPoints[0] = new PointF(destination.Left, destination.Top);
                destinationPoints[1] = new PointF(destination.Right, destination.Top);
                destinationPoints[2] = new PointF(destination.Left, destination.Bottom);
                ImageAttributes attributes = GetTintAttributes(tint);
                graphics.DrawImage(sprite.Atlas.Image, destinationPoints, source, GraphicsUnit.Pixel, attributes, null, 0);

                if (activePose != null && activeRenderSpace != null)
                {
                    activePose.SpritePlacements.Add(new SpritePlacement
                    {
                        Name = name,
                        PhysicsSource = position,
                        InterpolatedPosition = position,
                        Anchor = new Vec2(anchorX, anchorY),
                        LocalRectangle = destination,
                        OverlayPosition = activeRenderSpace.WorldToOverlay(activePose.ToRenderedWorld(position)),
                        FinalScreenPosition = activePose.ToRenderedWorld(position)
                    });
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private ImageAttributes GetTintAttributes(Color tint)
        {
            ImageAttributes attributes;
            if (tintAttributes.TryGetValue(tint.ToArgb(), out attributes)) return attributes;

            float red = tint.R / 255.0f;
            float green = tint.G / 255.0f;
            float blue = tint.B / 255.0f;
            ColorMatrix matrix = new ColorMatrix(new float[][]
            {
                new float[] { red, 0, 0, 0, 0 },
                new float[] { 0, green, 0, 0, 0 },
                new float[] { 0, 0, blue, 0, 0 },
                new float[] { 0, 0, 0, tint.A / 255.0f, 0 },
                new float[] { 0, 0, 0, 0, 1 }
            });
            attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            tintAttributes[tint.ToArgb()] = attributes;
            return attributes;
        }

        private static Color Shade(Color color)
        {
            return Color.FromArgb(color.A,
                MathUtil.Clamp((int)Math.Round(color.R * 0.9), 0, 255),
                MathUtil.Clamp((int)Math.Round(color.G * 0.93), 0, 255),
                MathUtil.Clamp((int)Math.Round(color.B * 0.96), 0, 255));
        }

        private static double AimScreen(Vec2 from, Vec2 to)
        {
            double angle = Math.Atan2(to.Y - from.Y, to.X - from.X) * 180.0 / Math.PI + 90.0;
            while (angle > 180.0) angle -= 360.0;
            while (angle < -180.0) angle += 360.0;
            return angle;
        }

        private static Vec2 RotateScreen(Vec2 value, double degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            return new Vec2(value.X * cosine - value.Y * sine,
                value.X * sine + value.Y * cosine);
        }

        private static double SignedDistanceToLine(Vec2 point, Vec2 lineA, Vec2 lineB)
        {
            Vec2 axis = lineB - lineA;
            Vec2 relative = point - lineA;
            return axis.X * relative.Y - axis.Y * relative.X;
        }

        private static int PositiveModulo(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private static void DrawDebugWorld(System.Drawing.Graphics graphics, SlugcatPose pose, DesktopCollisionWorld world, Slugcat slugcat, DesktopPetAI ai)
        {
            Vec2 renderedChest = pose.ToRenderedWorld(pose.Chest);
            using (Pen surfacePen = new Pen(Color.FromArgb(190, 63, 220, 130), 1.0f))
            using (Pen connectionPen = new Pen(Color.FromArgb(220, 255, 205, 60), 1.4f))
            using (Pen targetPen = new Pen(Color.FromArgb(220, 80, 185, 255), 1.0f))
            using (Pen rawAttentionPen = new Pen(Color.FromArgb(230, 255, 115, 210), 1.0f))
            {
                IList<DesktopSurface> surfaces = world.Surfaces;
                for (int i = 0; i < surfaces.Count; i++)
                {
                    DesktopSurface surface = surfaces[i];
                    Rectangle bounds = surface.Bounds;
                    if (bounds.Right < renderedChest.X - 616.0 || bounds.Left > renderedChest.X + 616.0 ||
                        bounds.Bottom < renderedChest.Y - 418.0 || bounds.Top > renderedChest.Y + 418.0)
                    {
                        continue;
                    }
                    if (surface.IsHorizontal)
                    {
                        graphics.DrawLine(surfacePen, bounds.Left, bounds.Top, bounds.Right, bounds.Top);
                    }
                    else
                    {
                        int wallX = (surface.Kind == DesktopSurfaceKind.WindowRightWall ||
                            surface.Kind == DesktopSurfaceKind.MonitorRightBoundary)
                            ? bounds.Right
                            : bounds.Left;
                        graphics.DrawLine(surfacePen, wallX, bounds.Top, wallX, bounds.Bottom);
                    }
                }

                Vec2 renderedHipsBody = pose.ToRenderedWorld(pose.Hips);
                Vec2 renderedHead = pose.ToRenderedWorld(pose.Head);
                Vec2 renderedSmoothedAttention = pose.ToRenderedWorld(ai.Attention.Smoothed);
                Vec2 renderedAttention = pose.ToRenderedWorld(ai.Attention.Target);
                graphics.DrawLine(connectionPen, renderedChest.ToPointF(), renderedHipsBody.ToPointF());
                graphics.DrawLine(targetPen, renderedHead.ToPointF(), renderedSmoothedAttention.ToPointF());
                DrawCross(graphics, targetPen, renderedSmoothedAttention, 4.0);
                graphics.DrawLine(rawAttentionPen, renderedSmoothedAttention.ToPointF(), renderedAttention.ToPointF());
                DrawCross(graphics, rawAttentionPen, renderedAttention, 5.0);
                for (int i = 0; i < 2; i++)
                {
                    Vec2 shoulder = pose.ToRenderedWorld(pose.ArmShoulders[i]);
                    Vec2 hand = pose.ToRenderedWorld(pose.Hands[i]);
                    Vec2 target = pose.ToRenderedWorld(pose.HandTargets[i]);
                    Vec2 connection = pose.ToRenderedWorld(pose.ArmConnections[i]);
                    graphics.DrawLine(connectionPen, shoulder.ToPointF(), hand.ToPointF());
                    graphics.DrawLine(targetPen, hand.ToPointF(), target.ToPointF());
                    DrawCross(graphics, targetPen, target, 3.0);
                    DrawCross(graphics, connectionPen, connection, 2.5);
                }
                Vec2 renderedHips = pose.ToRenderedWorld(pose.Hips);
                Vec2 renderedLegs = pose.ToRenderedWorld(pose.Legs);
                Vec2 renderedLegTarget = pose.ToRenderedWorld(pose.FootTargets[0]);
                graphics.DrawLine(connectionPen, renderedHips.ToPointF(), renderedLegs.ToPointF());
                graphics.DrawLine(targetPen, renderedLegs.ToPointF(), renderedLegTarget.ToPointF());
                DrawCross(graphics, targetPen, renderedLegTarget, 3.0);
                for (int i = 0; i < pose.ExtraParts.Length; i++)
                {
                    ExtraGraphicsPartPose part = pose.ExtraParts[i];
                    if (part == null || !part.Visible ||
                        part.ExtensionName != "AxolotlGills") continue;
                    Vec2 connection = pose.ToRenderedWorld(part.ConnectionPosition);
                    Vec2 control = pose.ToRenderedWorld(part.CurrentPosition);
                    Vec2 target = pose.ToRenderedWorld(part.TargetPosition);
                    graphics.DrawLine(connectionPen, connection.ToPointF(), control.ToPointF());
                    graphics.DrawLine(targetPen, control.ToPointF(), target.ToPointF());
                    DrawCross(graphics, connectionPen, connection, 2.5);
                    DrawCross(graphics, targetPen, target, 2.5);
                }
            }

            for (int i = 0; i < slugcat.BodyChunks.Length; i++)
            {
                BodyChunk chunk = slugcat.BodyChunks[i];
                Vec2 center = pose.ToRenderedWorld(chunk.Position);
                Vec2 velocityEnd = pose.ToRenderedWorld(chunk.Position + chunk.Velocity * 3.0);
                double radius = DesktopWorldTransform.ToDesktopLength(chunk.Radius);
                using (Pen pen = new Pen(Color.FromArgb(230, 255, 90, 90), 1.0f))
                {
                    graphics.DrawEllipse(pen, (float)(center.X - radius), (float)(center.Y - radius),
                        (float)(radius * 2.0), (float)(radius * 2.0));
                    graphics.DrawLine(pen, center.ToPointF(), velocityEnd.ToPointF());
                }
            }

            using (Pen controlPen = new Pen(Color.FromArgb(230, 255, 145, 55), 1.0f))
            using (Pen interpolatedPen = new Pen(Color.FromArgb(230, 235, 90, 255), 1.0f))
            using (Pen tangentPen = new Pen(Color.FromArgb(220, 255, 220, 80), 1.0f))
            using (Pen perpendicularPen = new Pen(Color.FromArgb(220, 80, 225, 255), 1.0f))
            using (Pen wirePen = new Pen(Color.FromArgb(125, 255, 255, 255), 0.8f))
            using (Pen leftEdgePen = new Pen(Color.FromArgb(235, 70, 155, 255), 1.4f))
            using (Pen rightEdgePen = new Pen(Color.FromArgb(235, 80, 255, 145), 1.4f))
            {
                for (int i = 0; i < pose.Tail.Length; i++)
                {
                    Vec2 control = pose.ToRenderedWorld(pose.TailCurrent[i]);
                    Vec2 center = pose.ToRenderedWorld(pose.Tail[i]);
                    double radius = DesktopWorldTransform.ToDesktopLength(pose.TailRadii[i]);
                    DrawCross(graphics, controlPen, control, 3.0);
                    graphics.DrawEllipse(interpolatedPen,
                        (float)(center.X - radius), (float)(center.Y - radius),
                        (float)(radius * 2.0), (float)(radius * 2.0));

                    Vec2 section = pose.ToRenderedWorld(pose.TailCrossSectionCenters[i]);
                    Vec2 tangentEnd = pose.ToRenderedWorld(
                        pose.TailCrossSectionCenters[i] + pose.TailTangents[i] * 8.0);
                    double sectionRadius = i == 0 ? pose.TailRootRadius : pose.TailRadii[i - 1];
                    Vec2 perpendicularA = pose.ToRenderedWorld(
                        pose.TailCrossSectionCenters[i] -
                        pose.TailPerpendiculars[i] * sectionRadius);
                    Vec2 perpendicularB = pose.ToRenderedWorld(
                        pose.TailCrossSectionCenters[i] +
                        pose.TailPerpendiculars[i] * sectionRadius);
                    graphics.DrawLine(tangentPen, section.ToPointF(), tangentEnd.ToPointF());
                    graphics.DrawLine(perpendicularPen,
                        perpendicularA.ToPointF(), perpendicularB.ToPointF());
                }

                if (pose.TailMeshVertices != null &&
                    pose.TailMeshVertices.Length == OriginalTailMeshVertexCount)
                {
                    PointF[] triangle = new PointF[3];
                    for (int i = 0; i < TailTriangles.GetLength(0); i++)
                    {
                        for (int j = 0; j < 3; j++)
                            triangle[j] = pose.ToRenderedWorld(
                                pose.TailMeshVertices[TailTriangles[i, j]]).ToPointF();
                        graphics.DrawPolygon(wirePen, triangle);
                    }

                    PointF[] left = new PointF[TailLeftEdge.Length];
                    PointF[] right = new PointF[TailRightEdge.Length];
                    for (int i = 0; i < TailLeftEdge.Length; i++)
                        left[i] = pose.ToRenderedWorld(
                            pose.TailMeshVertices[TailLeftEdge[i]]).ToPointF();
                    for (int i = 0; i < TailRightEdge.Length; i++)
                        right[i] = pose.ToRenderedWorld(
                            pose.TailMeshVertices[TailRightEdge[i]]).ToPointF();
                    graphics.DrawLines(leftEdgePen, left);
                    graphics.DrawLines(rightEdgePen, right);
                    DrawCross(graphics, leftEdgePen,
                        pose.ToRenderedWorld(pose.TailRoot), 4.5);
                    DrawCross(graphics, rightEdgePen,
                        pose.ToRenderedWorld(pose.TailTip), 4.5);
                }
            }
        }

        private static Pen CreateRoundPen(Color color, float width)
        {
            Pen pen = new Pen(color, width);
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            return pen;
        }

        private static void FillCircle(System.Drawing.Graphics graphics, Vec2 center, double radius, Color color)
        {
            using (Brush brush = new SolidBrush(color))
            {
                graphics.FillEllipse(brush, (float)(center.X - radius), (float)(center.Y - radius), (float)(radius * 2.0), (float)(radius * 2.0));
            }
        }

        private static void DrawCross(System.Drawing.Graphics graphics, Pen pen, Vec2 point, double radius)
        {
            graphics.DrawLine(pen, (float)(point.X - radius), (float)point.Y, (float)(point.X + radius), (float)point.Y);
            graphics.DrawLine(pen, (float)point.X, (float)(point.Y - radius), (float)point.X, (float)(point.Y + radius));
        }

        public void Dispose()
        {
            tailRasterGraphics.Dispose();
            tailRaster.Dispose();
            debugFont.Dispose();
            foreach (KeyValuePair<int, ImageAttributes> item in tintAttributes)
            {
                item.Value.Dispose();
            }
            tintAttributes.Clear();
            foreach (KeyValuePair<int, SolidBrush> item in bodyBrushes)
            {
                item.Value.Dispose();
            }
            bodyBrushes.Clear();
            if (atlas != null) atlas.Dispose();
        }
    }
}
