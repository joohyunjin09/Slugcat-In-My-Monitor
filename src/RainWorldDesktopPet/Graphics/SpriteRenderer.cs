using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using RainWorldDesktopPet.AI;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Physics;
using RainWorldDesktopPet.RainWorld;

namespace RainWorldDesktopPet.Graphics
{
    public sealed class SpriteRenderer : IDisposable
    {
        private static readonly Color OutlineColor = Color.FromArgb(255, 28, 39, 51);
        private static readonly Color EyeColor = Color.FromArgb(255, 23, 32, 42);
        private readonly RainWorldAtlasSet atlas;
        private readonly Font debugFont = new Font("Consolas", 9.0f, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Dictionary<int, ImageAttributes> tintAttributes = new Dictionary<int, ImageAttributes>();
        private readonly Dictionary<int, SolidBrush> bodyBrushes = new Dictionary<int, SolidBrush>();
        private readonly PointF[] destinationPoints = new PointF[3];
        private readonly PointF[] tailMeshPoints = new PointF[15];
        private static readonly int[,] TailTriangles =
        {
            { 0, 1, 2 }, { 1, 2, 3 }, { 4, 5, 6 }, { 5, 6, 7 },
            { 8, 9, 10 }, { 9, 10, 11 }, { 12, 13, 14 },
            { 2, 3, 4 }, { 3, 4, 5 }, { 6, 7, 8 }, { 7, 8, 9 },
            { 10, 11, 12 }, { 11, 12, 13 }
        };
        private SlugcatPose activePose;
        private RenderSpace activeRenderSpace;

        public SpriteRenderer(RainWorldAtlasSet atlas)
        {
            this.atlas = atlas;
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
                Vec2 origin = pose.CharacterOrigin;
                graphics.Transform = new Matrix((float)scale, 0.0f, 0.0f, (float)scale,
                    (float)(-renderSpace.WorldOrigin.X + (1.0 - scale) * origin.X),
                    (float)(-renderSpace.WorldOrigin.Y + (1.0 - scale) * origin.Y));

                if (atlas != null)
                {
                    // PlayerGraphics.AddToContainer keeps sprites 0..6 in this
                    // exact Futile order, then FaceA (9) above both arms.
                    DrawAtlasTorso(graphics, pose, appearance); // 0 Body, 1 Hips
                    DrawTail(graphics, pose, appearance.BodyColor, true); // 2
                    DrawAtlasHeadPart(graphics, pose, appearance.BodyColor, false); // 3
                    DrawAtlasLegs(graphics, pose, appearance); // 4
                    DrawAtlasArm(graphics, pose, 0, appearance.BodyColor); // 5
                    DrawAtlasArm(graphics, pose, 1, appearance.BodyColor); // 6
                    DrawAtlasHeadPart(graphics, pose, appearance.BodyColor, true); // 9
                }
                else
                {
                    DrawTail(graphics, pose, appearance.BodyColor, false);
                    DrawLimbs(graphics, pose, 0, appearance.BodyColor);
                    DrawProceduralBody(graphics, pose, appearance);
                    DrawLimbs(graphics, pose, 1, appearance.BodyColor);
                    DrawHead(graphics, pose, false, appearance.BodyColor);
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
                string text = string.Format(
                    "tick {0}  timeStacker {1:0.000}  40 Hz  renderScale {26:0.00}\nAI {2} | animation {3} | body {4} | facing/flip {5}\ninput {6}  vel {7}\nchunk0 last {8} current {9} render {10}\nchunk1 last {11} current {12} render {13}\nhead last {14} current {15} render {16}\ntail0 last {17} current {18} render {19}\ngraphics {20} overlay {21}\nattention {22} target {23} | look {24}\nL shoulder {27} hand {28}->{29}->{30} target {31} mode {32} len {33:0} conn {47}->{48}->{49}\nR shoulder {34} hand {35}->{36}->{37} target {38} mode {39} len {40:0} conn {50}->{51}->{52}\nlegs hip {41} particle {42} target {43}\nface {44} scaleX {45:0} headDir {46}\n{25}",
                    pose.SimulationTick,
                    pose.TimeStacker,
                    ai.Behavior,
                    pose.Animation,
                    pose.BodyMode,
                    pose.Facing,
                    slugcat.LastInput,
                    (slugcat.BodyChunks[0].Velocity + slugcat.BodyChunks[1].Velocity) * 0.5,
                    pose.ChunkLast[0], pose.ChunkCurrent[0], pose.ChunkRender[0],
                    pose.ChunkLast[1], pose.ChunkCurrent[1], pose.ChunkRender[1],
                    pose.HeadLast, pose.HeadCurrent, pose.Head,
                    pose.TailLast[0], pose.TailCurrent[0], pose.Tail[0],
                    pose.GraphicsBounds, pose.OverlayBounds,
                    ai.Attention.Kind, ai.Attention.Target, pose.LookDirection,
                    assetStatus,
                    pose.CharacterRenderScale,
                    pose.ArmShoulders[0], pose.HandLast[0], pose.HandCurrent[0], pose.Hands[0],
                    pose.HandTargets[0], pose.ArmModes[0], pose.ArmMaxLengths[0],
                    pose.ArmShoulders[1], pose.HandLast[1], pose.HandCurrent[1], pose.Hands[1],
                    pose.HandTargets[1], pose.ArmModes[1], pose.ArmMaxLengths[1],
                    pose.Hips, pose.Legs, pose.FootTargets[0],
                    pose.SelectedFaceElement, pose.FaceScaleX, pose.HeadDirection,
                    pose.ArmConnectionLast[0], pose.ArmConnectionCurrent[0], pose.ArmConnections[0],
                    pose.ArmConnectionLast[1], pose.ArmConnectionCurrent[1], pose.ArmConnections[1]);
                using (Brush shadow = new SolidBrush(Color.FromArgb(210, 0, 0, 0)))
                using (Brush foreground = new SolidBrush(Color.FromArgb(255, 235, 255, 235)))
                {
                    graphics.DrawString(text, debugFont, shadow, new PointF(9.0f, 9.0f));
                    graphics.DrawString(text, debugFont, foreground, new PointF(8.0f, 8.0f));
                }
            }
        }

        private void DrawTail(System.Drawing.Graphics graphics, SlugcatPose pose, Color bodyColor, bool originalAtlasStyle)
        {
            if (originalAtlasStyle && pose.Tail.Length == SimulationConstants.TailSegmentCount)
            {
                DrawOriginalTailMesh(graphics, pose, bodyColor);
                return;
            }

            Vec2 previous = pose.Hips;
            for (int i = 0; i < pose.Tail.Length; i++)
            {
                float width = (float)(pose.TailRadii[i] * 2.0);
                if (originalAtlasStyle)
                {
                    using (Pen fill = CreateRoundPen(bodyColor, width))
                    {
                        graphics.DrawLine(fill, previous.ToPointF(), pose.Tail[i].ToPointF());
                    }
                }
                else
                {
                    using (Pen outline = CreateRoundPen(OutlineColor, width + 4.0f))
                    using (Pen fill = CreateRoundPen(bodyColor, width))
                    {
                        graphics.DrawLine(outline, previous.ToPointF(), pose.Tail[i].ToPointF());
                        graphics.DrawLine(fill, previous.ToPointF(), pose.Tail[i].ToPointF());
                    }
                }
                previous = pose.Tail[i];
            }
        }

        private void DrawOriginalTailMesh(System.Drawing.Graphics graphics, SlugcatPose pose, Color bodyColor)
        {
            Vec2 root = (pose.Hips * 3.0 + pose.Chest) / 4.0;
            double previousRadius = 6.0;
            Vec2 previous = root;
            for (int i = 0; i < 4; i++)
            {
                Vec2 current = pose.Tail[i];
                Vec2 direction = (current - previous).Normalized;
                if (direction.LengthSquared < 0.001) direction = new Vec2(-pose.Facing, 0.0);
                Vec2 perpendicular = direction.Perpendicular;
                double halfAdvance = i == 0 ? 0.0 : Vec2.Distance(current, previous) / 5.0;
                Vec2 rootWidth = perpendicular * previousRadius;
                tailMeshPoints[i * 4] = (previous - rootWidth + direction * halfAdvance).ToPointF();
                tailMeshPoints[i * 4 + 1] = (previous + rootWidth + direction * halfAdvance).ToPointF();
                if (i < 3)
                {
                    Vec2 endWidth = perpendicular * pose.TailRadii[i];
                    tailMeshPoints[i * 4 + 2] = (current - endWidth - direction * halfAdvance).ToPointF();
                    tailMeshPoints[i * 4 + 3] = (current + endWidth - direction * halfAdvance).ToPointF();
                }
                else
                {
                    tailMeshPoints[14] = current.ToPointF();
                }
                previousRadius = pose.TailRadii[i];
                previous = current;
            }
            PointF[] triangle = new PointF[3];
            for (int i = 0; i < TailTriangles.GetLength(0); i++)
            {
                triangle[0] = tailMeshPoints[TailTriangles[i, 0]];
                triangle[1] = tailMeshPoints[TailTriangles[i, 1]];
                triangle[2] = tailMeshPoints[TailTriangles[i, 2]];
                graphics.FillPolygon(GetBodyBrush(bodyColor), triangle, FillMode.Winding);
            }
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

        private static void DrawProceduralBody(System.Drawing.Graphics graphics, SlugcatPose pose, SlugcatAppearance appearance)
        {
            float bodyWidth = (float)((18.0 - pose.LandingCompression * 2.5) * appearance.BodyWidthScale);
            using (Pen outline = CreateRoundPen(OutlineColor, bodyWidth + 6.0f))
            using (Pen body = CreateRoundPen(appearance.BodyColor, bodyWidth))
            {
                graphics.DrawLine(outline, pose.Chest.ToPointF(), pose.Hips.ToPointF());
                graphics.DrawLine(body, pose.Chest.ToPointF(), pose.Hips.ToPointF());
            }
            FillCircle(graphics, pose.Chest, 10.3, OutlineColor);
            FillCircle(graphics, pose.Hips, 10.0, OutlineColor);
            FillCircle(graphics, pose.Chest, 7.4 * appearance.BodyWidthScale, appearance.BodyColor);
            FillCircle(graphics, pose.Hips, 7.1 * appearance.HipsWidthScale, Shade(appearance.BodyColor));
        }

        private void DrawAtlasTorso(System.Drawing.Graphics graphics, SlugcatPose pose, SlugcatAppearance appearance)
        {
            double bodyAngle = AimScreen(pose.Hips, pose.Chest);
            double verticality = MathUtil.InverseLerp(0.3, 0.5, Math.Abs(pose.BodyUp.Y));
            double bodyWidth = appearance.BodyWidthScale + MathUtil.Lerp(-0.05, 0.05, pose.Breath) * verticality;
            double hipsWidth = appearance.HipsWidthScale + 0.05 * pose.Breath;

            Vec2 bodyPosition = pose.Chest + new Vec2(0.0, -0.5 * pose.Breath * (1.0 - verticality));
            DrawElement(graphics, "BodyA", bodyPosition, bodyAngle, bodyWidth, 1.0,
                0.5, 0.7894737, appearance.BodyColor);
            Vec2 hipsPosition = (pose.Hips * 2.0 + pose.Chest) / 3.0;
            Vec2 tailTarget = pose.Tail.Length > 0 ? pose.Tail[0] : pose.Hips + (pose.Hips - pose.Chest);
            double hipsAngle = AimScreen(pose.Chest, tailTarget);
            DrawElement(graphics, "HipsA", hipsPosition, hipsAngle, hipsWidth, 1.0,
                0.5, 0.5, appearance.BodyColor);
        }

        private void DrawAtlasLegs(System.Drawing.Graphics graphics, SlugcatPose pose, SlugcatAppearance appearance)
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
                0.5, 0.25, appearance.BodyColor);
        }

        private void DrawAtlasArm(System.Drawing.Graphics graphics, SlugcatPose pose, int index, Color bodyColor)
        {
            Vec2 hand = pose.Hands[index];
            Vec2 shoulder = ComputeArmShoulder(pose, index);
            pose.ArmShoulders[index] = shoulder;
            if (!pose.ArmVisible[index]) return;
            int frame = MathUtil.Clamp((int)Math.Round(Vec2.Distance(hand, shoulder) / 2.0), 0, 12);
            double angle = AimScreen(hand, shoulder) + 90.0;
            double scaleY;
            if (pose.BodyMode == BodyModeIndex.Crawl)
                scaleY = pose.Chest.X < pose.Hips.X ? -1.0 : 1.0;
            else if (pose.BodyMode == BodyModeIndex.WallClimb)
                scaleY = pose.Facing == -1 ? -1.0 : 1.0;
            else
                scaleY = SignedDistanceToLine(hand, pose.Chest, pose.Hips) < 0.0 ? -1.0 : 1.0;
            DrawElement(graphics, "PlayerArm" + frame, hand, angle, 1.0, scaleY,
                0.9, 0.5, bodyColor);
        }

        private void DrawAtlasHeadPart(System.Drawing.Graphics graphics, SlugcatPose pose, Color bodyColor, bool faceOnly)
        {
            double angle = SelectHeadAngle(pose);
            int headFrame = MathUtil.Clamp((int)Math.Round(Math.Abs(angle / 360.0 * 34.0)), 0, 17);
            double headFacing = angle < 0.0 ? -1.0 : 1.0;
            int faceFrame;
            if (pose.Animation == AnimationIndex.Sleep)
            {
                headFrame = 4;
                faceFrame = 1;
            }
            else if (pose.BodyMode == BodyModeIndex.Crawl ||
                     (pose.BodyMode == BodyModeIndex.Stand && pose.Animation == AnimationIndex.StandUp))
            {
                headFrame = pose.BodyMode == BodyModeIndex.Crawl ? 7 : 6;
                faceFrame = 4;
            }
            else
            {
                faceFrame = SelectFaceFrame(pose);
            }

            if (!faceOnly)
            {
                DrawElement(graphics, "HeadA" + headFrame, pose.Head, angle, headFacing,
                    1.0, 0.5, 0.5, bodyColor);
                return;
            }

            Vec2 faceLook = pose.LookDirection * 3.0;
            double faceFacing;
            if (pose.BodyMode == BodyModeIndex.Crawl)
            {
                faceLook.X = 0.0;
                faceFacing = SelectFaceScaleX(pose);
            }
            else if (pose.BodyMode == BodyModeIndex.Stand && pose.Animation == AnimationIndex.StandUp)
            {
                faceLook.X = 0.0;
                faceFacing = headFacing;
            }
            else if (pose.Animation == AnimationIndex.Sleep)
            {
                faceFacing = pose.Chest.X < pose.Hips.X ? -1.0 : 1.0;
            }
            else
            {
                faceFacing = SelectFaceScaleX(pose);
            }
            pose.SelectedFaceElement = "FaceA" + faceFrame;
            pose.FaceScaleX = faceFacing;
            DrawElement(graphics, pose.SelectedFaceElement,
                pose.Head + faceLook + new Vec2(0.0, 2.0),
                0.0, faceFacing, 1.0, 0.5, 0.5, EyeColor);
        }

        private void DrawHead(System.Drawing.Graphics graphics, SlugcatPose pose, bool useAtlas, Color bodyColor)
        {
            double angle = SelectHeadAngle(pose);
            if (useAtlas)
            {
                int headFrame = MathUtil.Clamp((int)Math.Round(Math.Abs(angle / 360.0 * 34.0)), 0, 17);
                double headFacing = angle < 0.0 ? -1.0 : 1.0;
                int faceFrame;
                if (pose.Animation == AnimationIndex.Sleep)
                {
                    headFrame = 4;
                    faceFrame = 1;
                }
                else if (pose.BodyMode == BodyModeIndex.Crawl ||
                         (pose.BodyMode == BodyModeIndex.Stand && pose.Animation == AnimationIndex.StandUp))
                {
                    faceFrame = 4;
                }
                else
                {
                    faceFrame = SelectFaceFrame(pose);
                }
                double faceFacing = Math.Abs(pose.LookDirection.X) < 0.1
                    ? headFacing
                    : (pose.LookDirection.X < 0.0 ? -1.0 : 1.0);
                DrawElement(graphics, "HeadA" + headFrame, pose.Head, angle, headFacing,
                    1.0, 0.5, 0.5, bodyColor);
                DrawElement(graphics, "FaceA" + faceFrame,
                    pose.Head + pose.LookDirection * 3.0 + new Vec2(0.0, 2.0),
                    0.0, faceFacing, 1.0, 0.5, 0.5, EyeColor);
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
            FillCircle(graphics, eyeCenter - right * 3.2, 1.15, EyeColor);
            FillCircle(graphics, eyeCenter + right * 3.2, 1.15, EyeColor);
        }

        public static int SelectFaceFrame(SlugcatPose pose)
        {
            if (pose.Animation == AnimationIndex.Sleep) return 1;
            if (pose.BodyMode == BodyModeIndex.Crawl ||
                (pose.BodyMode == BodyModeIndex.Stand && pose.Animation == AnimationIndex.StandUp)) return 4;

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
                return pose.Chest.X < pose.Hips.X ? -1.0 : 1.0;
            if (pose.BodyMode == BodyModeIndex.Stand && pose.Animation == AnimationIndex.StandUp)
                return headFacing;
            Vec2 look = pose.LookDirection * 3.0;
            return Math.Abs(look.X) < 0.1 ? headFacing : (look.X < 0.0 ? -1.0 : 1.0);
        }

        public static Vec2 ComputeArmShoulder(SlugcatPose pose, int index)
        {
            double bodyAngle = AimScreen(pose.Hips, pose.Chest);
            double shoulderSpread = 4.5 / (pose.ArmRetractCounters[index] + 1.0);
            shoulderSpread *= Math.Abs(Math.Cos(bodyAngle / 360.0 * Math.PI * 2.0));
            Vec2 shoulderOffset = new Vec2((-1.0 + 2.0 * index) * shoulderSpread, 3.5);
            return pose.Chest + RotateScreen(shoulderOffset, bodyAngle);
        }

        private void DrawElement(System.Drawing.Graphics graphics, string name, Vec2 position, double angle, double scaleX, double scaleY, double anchorX, double anchorY, Color tint)
        {
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
            using (Pen surfacePen = new Pen(Color.FromArgb(190, 63, 220, 130), 1.0f))
            using (Pen connectionPen = new Pen(Color.FromArgb(220, 255, 205, 60), 1.4f))
            using (Pen targetPen = new Pen(Color.FromArgb(220, 80, 185, 255), 1.0f))
            using (Pen rawAttentionPen = new Pen(Color.FromArgb(230, 255, 115, 210), 1.0f))
            {
                IList<DesktopSurface> surfaces = world.Surfaces;
                for (int i = 0; i < surfaces.Count; i++)
                {
                    DesktopSurface surface = surfaces[i];
                    if (surface.Right < pose.Chest.X - 280.0 || surface.Left > pose.Chest.X + 280.0 ||
                        surface.Bottom < pose.Chest.Y - 190.0 || surface.Top > pose.Chest.Y + 190.0)
                    {
                        continue;
                    }
                    if (surface.IsHorizontal)
                    {
                        graphics.DrawLine(surfacePen, (float)surface.Left, (float)surface.Top, (float)surface.Right, (float)surface.Top);
                    }
                    else
                    {
                        graphics.DrawLine(surfacePen, (float)surface.Left, (float)surface.Top, (float)surface.Left, (float)surface.Bottom);
                    }
                }

                graphics.DrawLine(connectionPen, pose.Chest.ToPointF(), pose.Hips.ToPointF());
                graphics.DrawLine(targetPen, pose.Head.ToPointF(), ai.Attention.Smoothed.ToPointF());
                DrawCross(graphics, targetPen, ai.Attention.Smoothed, 4.0);
                graphics.DrawLine(rawAttentionPen, ai.Attention.Smoothed.ToPointF(), ai.Attention.Target.ToPointF());
                DrawCross(graphics, rawAttentionPen, ai.Attention.Target, 5.0);
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
            }

            for (int i = 0; i < slugcat.BodyChunks.Length; i++)
            {
                BodyChunk chunk = slugcat.BodyChunks[i];
                using (Pen pen = new Pen(Color.FromArgb(230, 255, 90, 90), 1.0f))
                {
                    graphics.DrawEllipse(pen, (float)(chunk.Position.X - chunk.Radius), (float)(chunk.Position.Y - chunk.Radius),
                        (float)(chunk.Radius * 2.0), (float)(chunk.Radius * 2.0));
                    graphics.DrawLine(pen, chunk.Position.ToPointF(), (chunk.Position + chunk.Velocity * 3.0).ToPointF());
                }
            }

            for (int i = 0; i < pose.Tail.Length; i++)
            {
                using (Pen pen = new Pen(Color.FromArgb(220, 235, 90, 255), 1.0f))
                {
                    graphics.DrawEllipse(pen, (float)(pose.Tail[i].X - pose.TailRadii[i]), (float)(pose.Tail[i].Y - pose.TailRadii[i]),
                        (float)(pose.TailRadii[i] * 2.0), (float)(pose.TailRadii[i] * 2.0));
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
