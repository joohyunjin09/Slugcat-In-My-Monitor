using System;
using System.Drawing;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.RainWorld;

namespace RainWorldDesktopPet.Graphics
{
    public enum ExtraGraphicsLayer
    {
        AfterTailBeforeHead,
        BehindFace,
        InFront
    }

    public sealed class ExtraGraphicsPartPose
    {
        public int OriginalSpriteIndex;
        public string ExtensionName = string.Empty;
        public string Element = string.Empty;
        public Vec2 LastPosition;
        public Vec2 CurrentPosition;
        public Vec2 RenderPosition;
        public Vec2 SpritePosition;
        public Vec2 ConnectionPosition;
        public Vec2 TargetPosition;
        public double Rotation;
        public double ScaleX = 1.0;
        public double ScaleY = 1.0;
        public double AnchorX = 0.5;
        public double AnchorY = 0.5;
        public Color Tint = Color.White;
        public ExtraGraphicsLayer Layer;
        public bool Visible = true;
    }

    public interface ISlugcatGraphicsExtension
    {
        string Name { get; }
        int SpriteCount { get; }
        void Step(Slugcat slugcat, Vec2 lookDirection);
        int BuildPose(SlugcatPose pose, int outputIndex, double timeStacker);
        void Translate(Vec2 delta);
    }

    internal static class SlugcatGraphicsExtensionFactory
    {
        public static ISlugcatGraphicsExtension[] Create(SlugcatGraphicsProfile profile,
            Slugcat slugcat, RainWorldAtlasSet atlas)
        {
            switch (profile.ExtensionKind)
            {
                case SlugcatGraphicsExtensionKind.RivuletGills:
                    return new ISlugcatGraphicsExtension[] { new RivuletGillsExtension(slugcat, atlas) };
                case SlugcatGraphicsExtensionKind.ArtificerScar:
                    return new ISlugcatGraphicsExtension[] { new ArtificerScarExtension() };
                case SlugcatGraphicsExtensionKind.SpearmasterSpeckles:
                    return new ISlugcatGraphicsExtension[] { new SpearmasterTailSpecklesExtension(slugcat) };
                default:
                    return new ISlugcatGraphicsExtension[0];
            }
        }
    }

    public sealed class RivuletGillsExtension : ISlugcatGraphicsExtension
    {
        private const double Rigor = 0.5873646;
        private const double Width = 0.65 + (1.2 - 0.65) * (0.1542603 * 1.310689);
        private static readonly double[] LengthFactors = { 0.9722961, 0.6056554, 0.7223744 };
        private static readonly double[] BackwardFactors = { 0.3644831, 0.9129724, 0.4567381 };
        private static readonly double[] SourceY = { 0.03570603, 0.02899241, 0.02639332 };
        private readonly BodyPart[] scales = new BodyPart[6];
        private readonly double[] lengths = new double[6];
        private readonly double[] backwards = new double[6];
        private readonly Vec2[] connections = new Vec2[6];
        private readonly Vec2[] targets = new Vec2[6];
        private readonly double graphicHeight;

        public RivuletGillsExtension(Slugcat slugcat, RainWorldAtlasSet atlas)
        {
            double sourceHeight = 1.0;
            AtlasSprite sprite;
            if (atlas != null && atlas.TryGet("LizardScaleA3", out sprite))
                sourceHeight = sprite.Element.SourceSize.Height;
            graphicHeight = Math.Max(1.0, sourceHeight);
            Vec2 root = slugcat.BodyChunks[0].Position;
            for (int i = 0; i < scales.Length; i++)
            {
                int row = i % 3;
                lengths[i] = MathUtil.Lerp(2.5, 15.0, 1.310689 * LengthFactors[row]);
                backwards[i] = 0.1759363 * BackwardFactors[row];
                Vec2 sideRoot = root + new Vec2(i < 3 ? -5.0 : 5.0, 0.0);
                Vec2 initial = sideRoot + RotateOriginal(Vec2.Up, row * 30.0 - 45.0) * lengths[i];
                scales[i] = new BodyPart(initial, 0.0, 0.8, 1.0);
                connections[i] = sideRoot;
                targets[i] = initial;
            }
        }

        public string Name { get { return "AxolotlGills"; } }
        public int SpriteCount { get { return 12; } }
        public BodyPart[] ScaleObjects { get { return scales; } }
        public double[] Lengths { get { return lengths; } }

        public void Step(Slugcat slugcat, Vec2 lookDirection)
        {
            Vec2 upper = slugcat.BodyChunks[0].Position;
            Vec2 lower = slugcat.BodyChunks[1].Position;
            double lookAngle = VecToOriginalDegrees(lookDirection);
            for (int i = 0; i < scales.Length; i++)
            {
                int row = i % 3;
                Vec2 root = upper + new Vec2(i < 3 ? -5.0 : 5.0, 0.0);
                Vec2 outward = RotateOriginal(Vec2.Up, row * 30.0 - 45.0 + 90.0);
                Vec2 baseDirection = RotateOriginal(Vec2.Up, row * 30.0 - 45.0);
                Vec2 direction = Vec2.Lerp(baseDirection, (upper - lower).Normalized,
                    Math.Abs(lookAngle));
                if (SourceY[row] < 0.2)
                {
                    direction -= outward * (Math.Pow(MathUtil.InverseLerp(0.2, 0.0,
                        SourceY[row]), 2.0) * 2.0);
                }
                direction = Vec2.Lerp(direction, baseDirection,
                    Math.Pow(backwards[i], 1.0)).Normalized;
                Vec2 target = root + direction * lengths[i];
                BodyPart scale = scales[i];
                double distance = Vec2.Distance(scale.Position, target);
                double halfLength = lengths[i] / 2.0;
                if (distance >= halfLength)
                {
                    Vec2 correction = (target - scale.Position).Normalized * (distance - halfLength);
                    scale.Position += correction;
                    scale.Velocity += correction;
                }
                scale.Velocity += Vec2.ClampMagnitude(target - scale.Position, 10.0) /
                    MathUtil.Lerp(5.0, 1.5, Rigor);
                scale.Velocity *= MathUtil.Lerp(1.0, 0.8, Rigor);
                scale.ConnectToPoint(root, lengths[i], true, 0.0, Vec2.Zero, 0.0, 0.0);

                // BodyPart.Update is empty in the local DLL. AxolotlScale owns
                // the single snapshot/integration pass and uses 0.9 in air.
                scale.Velocity *= 0.9;
                scale.LastPosition = scale.Position;
                scale.Position += scale.Velocity;
                connections[i] = root;
                targets[i] = target;
            }
        }

        public int BuildPose(SlugcatPose pose, int outputIndex, double timeStacker)
        {
            for (int i = 0; i < scales.Length; i++)
            {
                Vec2 root = pose.FacePosition + new Vec2(i < 3 ? -5.0 : 5.0, 0.0);
                Vec2 render = scales[i].RenderPosition(timeStacker);
                double rotation = AimScreen(root, render) + (i < 3 ? 0.0 : 180.0);
                double flip = i < 3 ? -1.0 : 1.0;
                Fill(pose.ExtraParts[outputIndex + i], 12 + i, "LizardScaleA3",
                    scales[i], render, root, targets[i], rotation, flip * Width,
                    lengths[i] / graphicHeight, Color.FromArgb(145, 204, 240));
                Fill(pose.ExtraParts[outputIndex + 6 + i], 18 + i, "LizardScaleB3",
                    scales[i], render, root, targets[i], rotation, flip * Width,
                    lengths[i] / graphicHeight, Color.FromArgb(223, 45, 234));
            }
            return outputIndex + 12;
        }

        private static void Fill(ExtraGraphicsPartPose part, int spriteIndex, string element,
            BodyPart scale, Vec2 render, Vec2 connection, Vec2 target, double rotation,
            double scaleX, double scaleY, Color tint)
        {
            part.OriginalSpriteIndex = spriteIndex;
            part.ExtensionName = "AxolotlGills";
            part.Element = element;
            part.LastPosition = scale.LastPosition;
            part.CurrentPosition = scale.Position;
            part.RenderPosition = render;
            part.SpritePosition = connection;
            part.ConnectionPosition = connection;
            part.TargetPosition = target;
            part.Rotation = rotation;
            part.ScaleX = scaleX;
            part.ScaleY = scaleY;
            part.AnchorX = 0.5;
            part.AnchorY = 0.1;
            part.Tint = tint;
            part.Layer = ExtraGraphicsLayer.InFront;
            part.Visible = true;
        }

        public void Translate(Vec2 delta)
        {
            for (int i = 0; i < scales.Length; i++)
            {
                scales[i].Translate(delta);
                connections[i] += delta;
                targets[i] += delta;
            }
        }

        private static Vec2 RotateOriginal(Vec2 value, double degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            return new Vec2(value.X * cosine - value.Y * sine,
                value.X * sine + value.Y * cosine);
        }

        private static double VecToOriginalDegrees(Vec2 value)
        {
            return Math.Atan2(value.X, -value.Y) * 180.0 / Math.PI;
        }

        private static double AimScreen(Vec2 from, Vec2 to)
        {
            return Math.Atan2(to.Y - from.Y, to.X - from.X) * 180.0 / Math.PI + 90.0;
        }
    }

    public sealed class ArtificerScarExtension : ISlugcatGraphicsExtension
    {
        public string Name { get { return "ArtificerScar"; } }
        public int SpriteCount { get { return 1; } }
        public void Step(Slugcat slugcat, Vec2 lookDirection) { }

        public int BuildPose(SlugcatPose pose, int outputIndex, double timeStacker)
        {
            ExtraGraphicsPartPose part = pose.ExtraParts[outputIndex++];
            int frame = ReadTrailingFrame(pose.SelectedFaceElement);
            part.OriginalSpriteIndex = 12;
            part.ExtensionName = Name;
            part.Element = "MushroomA";
            part.LastPosition = pose.FacePosition;
            part.CurrentPosition = pose.FacePosition;
            part.RenderPosition = pose.FacePosition + new Vec2(3.0, -3.0);
            part.SpritePosition = part.RenderPosition;
            part.ConnectionPosition = pose.FacePosition;
            part.TargetPosition = part.RenderPosition;
            part.Rotation = pose.FaceRotation;
            part.ScaleX = 1.0;
            if (pose.SelectedFaceElement.StartsWith("FaceC", StringComparison.Ordinal))
            {
                part.ScaleX = 1.0 - frame / 8.0;
                part.RenderPosition.X = pose.FacePosition.X + 3.0 + 4.0 * (frame / 8.0);
                part.SpritePosition.X = part.RenderPosition.X;
            }
            else if (pose.SelectedFaceElement.StartsWith("FaceD", StringComparison.Ordinal))
            {
                part.RenderPosition.X = pose.FacePosition.X + 3.0 * (1.0 - frame / 8.0);
                part.SpritePosition.X = part.RenderPosition.X;
            }
            part.ScaleY = 1.0;
            part.AnchorX = 0.5;
            part.AnchorY = 0.5;
            part.Tint = Color.FromArgb(69, 40, 60);
            part.Layer = ExtraGraphicsLayer.BehindFace;
            part.Visible = true;
            return outputIndex;
        }

        public void Translate(Vec2 delta) { }

        private static int ReadTrailingFrame(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            char value = name[name.Length - 1];
            return value >= '0' && value <= '8' ? value - '0' : 0;
        }
    }

    public sealed class SpearmasterTailSpecklesExtension : ISlugcatGraphicsExtension
    {
        private readonly Slugcat slugcat;

        public SpearmasterTailSpecklesExtension(Slugcat slugcat)
        {
            this.slugcat = slugcat;
        }

        public string Name { get { return "TailSpeckles+CosmeticPearl"; } }
        public int SpriteCount { get { return 19; } }
        public void Step(Slugcat slugcat, Vec2 lookDirection) { }

        public int BuildPose(SlugcatPose pose, int outputIndex, double timeStacker)
        {
            int first = outputIndex;
            SpearmasterAbilityController ability =
                slugcat.AbilityController as SpearmasterAbilityController;
            double spearProgress = ability == null ? 0.0 : ability.SpearProgress;
            int selectedRow = ability == null ? 0 : ability.SpearRow;
            int selectedLine = ability == null ? 0 : ability.SpearLine;
            SpineSample selectedSpine = new SpineSample();
            Vec2 selectedPosition = Vec2.Zero;
            for (int row = 0; row < 5; row++)
            {
                double fraction = row / 4.0;
                double along = MathUtil.Lerp(0.4, 0.95, Math.Pow(fraction, 0.8));
                SpineSample spine = SampleSpine(pose, along);
                double depth = 0.8 * Math.Sqrt(fraction);
                double growthTint = (0.8 - depth) * spearProgress;
                Color tint = LerpColor(pose.VisualBodyColor,
                    LerpColor(Color.White, pose.VisualBodyColor, 0.3),
                    0.2 + depth + growthTint);
                for (int line = 0; line < 3; line++)
                {
                    double around = (line + (row % 2 == 0 ? 0.5 : 0.0)) / 2.0;
                    around = -1.0 + 2.0 * around;
                    if (around < -1.0) around += 2.0;
                    else if (around > 1.0) around -= 2.0;
                    around = Math.Sign(around) * Math.Pow(Math.Abs(around), 0.6);
                    ExtraGraphicsPartPose part = pose.ExtraParts[outputIndex++];
                    part.OriginalSpriteIndex = 12 + row * 3 + line;
                    part.ExtensionName = "TailSpeckles";
                    part.Element = "tinyStar";
                    part.LastPosition = spine.Position;
                    part.CurrentPosition = spine.Position;
                    part.RenderPosition = spine.Position + spine.Perpendicular * ((spine.Radius + 0.5) * around);
                    part.SpritePosition = part.RenderPosition;
                    part.ConnectionPosition = spine.Position;
                    part.TargetPosition = part.RenderPosition;
                    part.Rotation = VecToDegrees(spine.Direction);
                    part.ScaleX = LerpMap(Math.Abs(around), 0.4, 1.0, 1.0, 0.0);
                    part.ScaleY = 1.0;
                    if (ability != null && spearProgress > 0.0)
                    {
                        if (row == selectedRow && line == selectedLine)
                        {
                            part.ScaleX *= 1.0 + spearProgress * 2.0;
                            part.ScaleY *= 1.0 + spearProgress * 2.0;
                            selectedSpine = spine;
                            selectedPosition = part.RenderPosition;
                        }
                        else if ((row == selectedRow + 1 && line == selectedLine) ||
                            (row == selectedRow - 1 && line == selectedLine) ||
                            (row == selectedRow && line == selectedLine + 1) ||
                            (row == selectedRow && line == selectedLine - 1))
                        {
                            part.ScaleX *= 1.0 + spearProgress;
                            part.ScaleY *= 1.0 + spearProgress;
                        }
                    }
                    part.AnchorX = 0.5;
                    part.AnchorY = 0.5;
                    part.Tint = tint;
                    part.Layer = ExtraGraphicsLayer.AfterTailBeforeHead;
                    part.Visible = true;
                }
            }

            ExtraGraphicsPartPose spear = pose.ExtraParts[outputIndex++];
            ResetHidden(spear, 27, "TailSpeckles", "BioSpear1");
            if (ability != null && spearProgress > 0.0)
            {
                spear.Element = "BioSpear" + (ability.SpearType % 3 + 1);
                spear.LastPosition = selectedPosition;
                spear.CurrentPosition = selectedPosition;
                spear.RenderPosition = selectedPosition;
                spear.SpritePosition = selectedPosition;
                spear.ConnectionPosition = selectedSpine.Position;
                Vec2 direction = selectedSpine.Perpendicular;
                // Original checks y-up > .35; desktop simulation is y-down.
                if (direction.Normalized.Y < -0.35) direction *= -1.0;
                spear.Rotation = VecToDegrees(direction);
                spear.ScaleX = 1.0;
                spear.ScaleY = -spearProgress * 0.5;
                spear.AnchorX = 0.5;
                spear.AnchorY = 0.0;
                spear.Visible = true;
            }
            for (int i = 0; i < 3; i++)
            {
                ExtraGraphicsPartPose pearl = pose.ExtraParts[outputIndex++];
                ResetHidden(pearl, 28 + i, "CosmeticPearl(inactive)",
                    i == 0 ? "JetFishEyeA" : (i == 1 ? "Futile_White" : "BodyPearl"));
            }
            if (outputIndex - first != SpriteCount)
                throw new InvalidOperationException("Spearmaster extra sprite allocation drifted from the DLL mapping.");
            return outputIndex;
        }

        public void Translate(Vec2 delta) { }

        private static void ResetHidden(ExtraGraphicsPartPose part, int index,
            string extension, string element)
        {
            part.OriginalSpriteIndex = index;
            part.ExtensionName = extension;
            part.Element = element;
            part.LastPosition = Vec2.Zero;
            part.CurrentPosition = Vec2.Zero;
            part.RenderPosition = Vec2.Zero;
            part.SpritePosition = Vec2.Zero;
            part.ConnectionPosition = Vec2.Zero;
            part.TargetPosition = Vec2.Zero;
            part.Rotation = 0.0;
            part.ScaleX = 0.0;
            part.ScaleY = 0.0;
            part.Tint = Color.White;
            part.Layer = ExtraGraphicsLayer.AfterTailBeforeHead;
            part.Visible = false;
        }

        private static SpineSample SampleSpine(SlugcatPose pose, double fraction)
        {
            double scaled = MathUtil.Clamp01(fraction) * pose.Tail.Length;
            int previousIndex = Math.Max(0, (int)Math.Floor(scaled - 1.0));
            int currentIndex = Math.Min(pose.Tail.Length - 1, (int)Math.Floor(scaled));
            Vec2 previous = pose.Tail[previousIndex];
            Vec2 current = pose.Tail[currentIndex];
            Vec2 next = pose.Tail[Math.Min(currentIndex + 1, pose.Tail.Length - 1)];
            double local = MathUtil.InverseLerp(previousIndex + 1.0,
                currentIndex + 1.0, scaled);
            Vec2 direction = Vec2.Lerp(current - previous, next - current, local).Normalized;
            if (direction.LengthSquared < 0.001) direction = Vec2.Right;
            SpineSample sample = new SpineSample();
            sample.Position = Vec2.Lerp(previous, current, local);
            sample.Direction = direction;
            // Custom.PerpendicularVector(-y,x) converted from y-up to desktop
            // y-down is the negative of Vec2.Perpendicular.
            sample.Perpendicular = -direction.Perpendicular;
            sample.Radius = MathUtil.Lerp(pose.TailRadii[previousIndex],
                pose.TailRadii[currentIndex], local);
            return sample;
        }

        private static double VecToDegrees(Vec2 value)
        {
            return Math.Atan2(value.X, -value.Y) * 180.0 / Math.PI;
        }

        private static double LerpMap(double value, double fromA, double toA,
            double fromB, double toB)
        {
            return MathUtil.Lerp(fromB, toB, MathUtil.InverseLerp(fromA, toA, value));
        }

        private static Color LerpColor(Color from, Color to, double amount)
        {
            amount = MathUtil.Clamp01(amount);
            return Color.FromArgb(
                (int)Math.Round(MathUtil.Lerp(from.A, to.A, amount)),
                (int)Math.Round(MathUtil.Lerp(from.R, to.R, amount)),
                (int)Math.Round(MathUtil.Lerp(from.G, to.G, amount)),
                (int)Math.Round(MathUtil.Lerp(from.B, to.B, amount)));
        }

        private struct SpineSample
        {
            public Vec2 Position;
            public Vec2 Direction;
            public Vec2 Perpendicular;
            public double Radius;
        }
    }
}
