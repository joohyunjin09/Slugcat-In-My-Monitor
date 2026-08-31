using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using RainWorldDesktopPet.AI;
using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.UI
{
    internal sealed class RadialCommandHitSnapshot
    {
        internal static readonly RadialCommandHitSnapshot Empty =
            new RadialCommandHitSnapshot(null, Vec2.Zero, 0.0, 0.0, false);

        internal RadialCommandHitSnapshot(GameLoop target, Vec2 center,
            double innerRadius, double outerRadius, bool interactive)
        {
            Target = target;
            Center = center;
            InnerRadius = innerRadius;
            OuterRadius = outerRadius;
            Interactive = interactive;
        }

        internal readonly GameLoop Target;
        internal readonly Vec2 Center;
        internal readonly double InnerRadius;
        internal readonly double OuterRadius;
        internal readonly bool Interactive;

        internal bool TryHit(Vec2 point, out DesktopPetCommand command)
        {
            command = DesktopPetCommand.Move;
            if (!Interactive || Target == null) return false;
            Vec2 offset = point - Center;
            double distance = offset.Length;
            if (distance < InnerRadius || distance > OuterRadius) return false;
            command = RadialCommandMenu.CommandAtAngle(
                Math.Atan2(offset.Y, offset.X) * 180.0 / Math.PI);
            return true;
        }

        internal bool Contains(Vec2 point)
        {
            if (!Interactive || Target == null) return false;
            Vec2 offset = point - Center;
            return offset.Length <= OuterRadius;
        }
    }

    internal sealed class RadialCommandMenu : IDisposable
    {
        private const double InnerRadius = 47.0;
        private const double OuterRadius = 124.0;
        private const double HoverExpansion = 8.0;
        private const double RenderMargin = 12.0;
        private const double OpeningSpeed = 2.0;
        private const double ClosingSpeed = 2.0;
        private const double HoverInSpeed = 1.0 / 0.23;
        private const double HoverOutSpeed = 1.0 / 0.23;
        private const int PixelScale = 2;
        private const int PixelCanvasSize = 144;
        private const double InitialScale = 0.38;
        private const double InteractiveThreshold = 0.48;
        private static readonly DesktopPetCommand[] Commands =
        {
            DesktopPetCommand.Stop,
            DesktopPetCommand.Move,
            DesktopPetCommand.FollowMouse
        };

        private readonly double[] hoverAmounts = new double[Commands.Length];
        private readonly Font labelFont;
        private readonly Bitmap pixelCanvas;
        private readonly System.Drawing.Graphics pixelGraphics;
        private GameLoop target;
        private Vec2 anchor;
        private Vec2 center;
        private double openness;
        private bool closing;
        private int hoveredIndex = -1;
        private long lastUpdateTimestamp;
        private bool disposed;

        internal RadialCommandMenu()
        {
            string family = UiLocalization.Current == UiLanguage.English
                ? "RAIN WORLD MENU" : "굴림체";
            float size = UiLocalization.Current == UiLanguage.English
                ? 13.0f : 13.5f;
            labelFont = CreateLabelFont(family, size);
            pixelCanvas = new Bitmap(PixelCanvasSize, PixelCanvasSize,
                PixelFormat.Format32bppPArgb);
            pixelGraphics = System.Drawing.Graphics.FromImage(pixelCanvas);
        }

        internal bool IsVisible { get { return target != null && openness > 0.0; } }
        internal GameLoop Target { get { return target; } }

        internal void Open(GameLoop newTarget, Vec2 slugcatCenter,
            Rectangle workArea)
        {
            if (newTarget == null) throw new ArgumentNullException("newTarget");
            target = newTarget;
            anchor = slugcatCenter;
            center = slugcatCenter;
            openness = 0.0;
            closing = false;
            hoveredIndex = -1;
            Array.Clear(hoverAmounts, 0, hoverAmounts.Length);
            lastUpdateTimestamp = Stopwatch.GetTimestamp();
            UpdateCenter(workArea);
        }

        internal void Close()
        {
            if (target != null) closing = true;
        }

        internal void CloseImmediately()
        {
            target = null;
            openness = 0.0;
            closing = false;
            hoveredIndex = -1;
            Array.Clear(hoverAmounts, 0, hoverAmounts.Length);
            lastUpdateTimestamp = 0;
        }

        internal void Update(Vec2 slugcatCenter, Rectangle workArea,
            Vec2 pointer)
        {
            if (target == null) return;
            long now = Stopwatch.GetTimestamp();
            double elapsed = lastUpdateTimestamp == 0 ? 1.0 / 60.0 :
                (now - lastUpdateTimestamp) / (double)Stopwatch.Frequency;
            lastUpdateTimestamp = now;
            elapsed = MathUtil.Clamp(elapsed, 0.0, 0.05);

            anchor = slugcatCenter;
            openness = MathUtil.Clamp01(openness + elapsed *
                (closing ? -ClosingSpeed : OpeningSpeed));
            UpdateCenter(workArea);

            double eased = EasedOpenness;
            double scale = InitialScale + eased * (1.0 - InitialScale);
            double hitInner = InnerRadius * scale;
            double hitOuter = (OuterRadius + HoverExpansion) * scale;
            hoveredIndex = !closing && openness >= InteractiveThreshold
                ? CommandIndexAtPoint(pointer, center, hitInner, hitOuter) : -1;
            for (int i = 0; i < hoverAmounts.Length; i++)
            {
                double direction = i == hoveredIndex ? HoverInSpeed : -HoverOutSpeed;
                hoverAmounts[i] = MathUtil.Clamp01(
                    hoverAmounts[i] + elapsed * direction);
            }

            if (closing && openness <= 0.0) CloseImmediately();
        }

        private void UpdateCenter(Rectangle workArea)
        {
            double margin = OuterRadius + HoverExpansion + RenderMargin;
            double minX = workArea.Left + margin;
            double maxX = workArea.Right - margin;
            double minY = workArea.Top + margin;
            double maxY = workArea.Bottom - margin;
            Vec2 destination = new Vec2(
                maxX >= minX ? MathUtil.Clamp(anchor.X, minX, maxX) :
                    workArea.Left + workArea.Width * 0.5,
                maxY >= minY ? MathUtil.Clamp(anchor.Y, minY, maxY) :
                    workArea.Top + workArea.Height * 0.5);
            center = Vec2.Lerp(anchor, destination, EasedOpenness);
        }

        internal Rectangle GetRenderBounds()
        {
            double reach = OuterRadius + HoverExpansion + RenderMargin;
            int left = (int)Math.Floor(center.X - reach);
            int top = (int)Math.Floor(center.Y - reach);
            int size = (int)Math.Ceiling(reach * 2.0);
            return new Rectangle(left, top, size, size);
        }

        internal RadialCommandHitSnapshot CreateHitSnapshot()
        {
            if (target == null || closing || openness < InteractiveThreshold)
                return RadialCommandHitSnapshot.Empty;
            double scale = InitialScale + EasedOpenness * (1.0 - InitialScale);
            return new RadialCommandHitSnapshot(target, center,
                InnerRadius * scale,
                (OuterRadius + HoverExpansion) * scale, true);
        }

        internal void Render(System.Drawing.Graphics graphics,
            Rectangle surfaceBounds)
        {
            if (graphics == null) throw new ArgumentNullException("graphics");
            if (!IsVisible) return;

            RenderCore(graphics, surfaceBounds, center, EasedOpenness,
                InitialScale + EasedOpenness * (1.0 - InitialScale),
                hoverAmounts, target.Command);
        }

        internal void RenderPreview(System.Drawing.Graphics graphics,
            Rectangle surfaceBounds, Vec2 previewCenter,
            DesktopPetCommand activeCommand, int hoveredCommandIndex)
        {
            double[] previewHover = new double[Commands.Length];
            if (hoveredCommandIndex >= 0 && hoveredCommandIndex < previewHover.Length)
                previewHover[hoveredCommandIndex] = 1.0;
            RenderCore(graphics, surfaceBounds, previewCenter, 1.0, 1.0,
                previewHover, activeCommand);
        }

        private void RenderCore(System.Drawing.Graphics graphics,
            Rectangle surfaceBounds, Vec2 renderCenter, double eased,
            double scale, double[] renderHoverAmounts,
            DesktopPetCommand activeCommand)
        {
            GraphicsState targetState = graphics.Save();
            try
            {
                pixelGraphics.CompositingMode = CompositingMode.SourceCopy;
                pixelGraphics.Clear(Color.Transparent);
                pixelGraphics.CompositingMode = CompositingMode.SourceOver;
                pixelGraphics.CompositingQuality = CompositingQuality.HighSpeed;
                pixelGraphics.SmoothingMode = SmoothingMode.None;
                pixelGraphics.PixelOffsetMode = PixelOffsetMode.None;
                pixelGraphics.InterpolationMode = InterpolationMode.NearestNeighbor;

                const float centerX = PixelCanvasSize * 0.5f;
                const float centerY = PixelCanvasSize * 0.5f;
                float inner = (float)(InnerRadius * scale / PixelScale);

                for (int i = 0; i < Commands.Length; i++)
                {
                    double hover = SmootherStep(renderHoverAmounts[i]);
                    float outer = (float)((OuterRadius + HoverExpansion * hover) *
                        scale / PixelScale);
                    float startAngle = -88.0f + i * 120.0f;
                    const float sweepAngle = 116.0f;
                    bool active = activeCommand == Commands[i];
                    int alpha = ScaleAlpha(142 + (active ? 12 : 0) +
                        (int)Math.Round(68.0 * hover), eased);
                    int shade = 42 + (active ? 6 : 0) +
                        (int)Math.Round(42.0 * hover);

                    using (GraphicsPath segment = CreateRingSegment(centerX,
                        centerY, inner, outer, startAngle, sweepAngle))
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(alpha,
                        shade, shade + 2, shade + 3)))
                        pixelGraphics.FillPath(fill, segment);

                    double middleAngle = (startAngle + sweepAngle * 0.5) *
                        Math.PI / 180.0;
                    if (active)
                    {
                        double markerRadius = inner + 4.0;
                        int markerX = (int)Math.Round(centerX +
                            Math.Cos(middleAngle) * markerRadius) - 1;
                        int markerY = (int)Math.Round(centerY +
                            Math.Sin(middleAngle) * markerRadius) - 1;
                        using (SolidBrush marker = new SolidBrush(Color.FromArgb(
                            ScaleAlpha(206, eased), 220, 224, 221)))
                            pixelGraphics.FillRectangle(marker, markerX, markerY, 2, 2);
                    }
                }

                graphics.CompositingQuality = CompositingQuality.HighSpeed;
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                int diameter = PixelCanvasSize * PixelScale;
                int left = (int)Math.Round(renderCenter.X - surfaceBounds.Left -
                    diameter * 0.5);
                int top = (int)Math.Round(renderCenter.Y - surfaceBounds.Top -
                    diameter * 0.5);
                graphics.DrawImage(pixelCanvas,
                    new Rectangle(left, top, diameter, diameter),
                    0, 0, PixelCanvasSize, PixelCanvasSize,
                    GraphicsUnit.Pixel);

                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (int i = 0; i < Commands.Length; i++)
                {
                    double hover = SmootherStep(renderHoverAmounts[i]);
                    bool active = activeCommand == Commands[i];
                    double outer = (OuterRadius + HoverExpansion * hover) * scale;
                    double labelRadius = (InnerRadius * scale + outer) * 0.5;
                    double middleAngle = (-88.0 + i * 120.0 + 58.0) *
                        Math.PI / 180.0;
                    float labelX = (float)(renderCenter.X - surfaceBounds.Left +
                        Math.Cos(middleAngle) * labelRadius);
                    float labelY = (float)(renderCenter.Y - surfaceBounds.Top +
                        Math.Sin(middleAngle) * labelRadius);
                    string label = LabelFor(Commands[i]);
                    SizeF measured = graphics.MeasureString(label, labelFont,
                        PointF.Empty, StringFormat.GenericTypographic);
                    int textAlpha = ScaleAlpha(196 + (active ? 12 : 0) +
                        (int)Math.Round(47.0 * hover), eased);
                    using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(
                        textAlpha, 232, 235, 232)))
                        graphics.DrawString(label, labelFont, textBrush,
                            labelX - measured.Width * 0.5f,
                            labelY - measured.Height * 0.5f,
                            StringFormat.GenericTypographic);
                }
            }
            finally
            {
                graphics.Restore(targetState);
            }
        }

        private double EasedOpenness
        {
            get
            {
                return SmootherStep(openness);
            }
        }

        internal static double OpeningDurationSeconds
        { get { return 1.0 / OpeningSpeed; } }

        internal static double ClosingDurationSeconds
        { get { return 1.0 / ClosingSpeed; } }

        internal static double HoverInDurationSeconds
        { get { return 1.0 / HoverInSpeed; } }

        internal static double HoverOutDurationSeconds
        { get { return 1.0 / HoverOutSpeed; } }

        internal static int RenderPixelScale
        { get { return PixelScale; } }

        private static GraphicsPath CreateRingSegment(float centerX, float centerY,
            float innerRadius, float outerRadius, float startAngle, float sweepAngle)
        {
            GraphicsPath path = new GraphicsPath();
            RectangleF outer = new RectangleF(centerX - outerRadius,
                centerY - outerRadius, outerRadius * 2.0f, outerRadius * 2.0f);
            RectangleF inner = new RectangleF(centerX - innerRadius,
                centerY - innerRadius, innerRadius * 2.0f, innerRadius * 2.0f);
            path.AddArc(outer, startAngle, sweepAngle);
            path.AddArc(inner, startAngle + sweepAngle, -sweepAngle);
            path.CloseFigure();
            return path;
        }

        private static int CommandIndexAtPoint(Vec2 point, Vec2 menuCenter,
            double innerRadius, double outerRadius)
        {
            Vec2 offset = point - menuCenter;
            double distance = offset.Length;
            if (distance < innerRadius || distance > outerRadius) return -1;
            DesktopPetCommand command = CommandAtAngle(
                Math.Atan2(offset.Y, offset.X) * 180.0 / Math.PI);
            for (int i = 0; i < Commands.Length; i++)
                if (Commands[i] == command) return i;
            return -1;
        }

        internal static DesktopPetCommand CommandAtAngle(double angleDegrees)
        {
            double normalized = angleDegrees + 90.0;
            while (normalized < 0.0) normalized += 360.0;
            while (normalized >= 360.0) normalized -= 360.0;
            int index = Math.Min(Commands.Length - 1,
                (int)Math.Floor(normalized / 120.0));
            return Commands[index];
        }

        private static string LabelFor(DesktopPetCommand command)
        {
            switch (command)
            {
                case DesktopPetCommand.Stop:
                    return UiLocalization.Text("멈춰", "Stop");
                case DesktopPetCommand.FollowMouse:
                    return UiLocalization.Text("날 따라와", "Follow me");
                default:
                    return UiLocalization.Text("움직여", "Move");
            }
        }

        private static int ScaleAlpha(int alpha, double opacity)
        {
            return Math.Max(0, Math.Min(255,
                (int)Math.Round(alpha * MathUtil.Clamp01(opacity))));
        }

        internal static double SmootherStep(double value)
        {
            value = MathUtil.Clamp01(value);
            return value * value * value *
                (value * (value * 6.0 - 15.0) + 10.0);
        }

        private static Font CreateLabelFont(string preferredFamily, float size)
        {
            Font font = new Font(preferredFamily, size, FontStyle.Regular,
                GraphicsUnit.Pixel);
            if (string.Equals(font.FontFamily.Name, preferredFamily,
                StringComparison.OrdinalIgnoreCase)) return font;

            font.Dispose();
            return new Font(FontFamily.GenericMonospace, size,
                FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            pixelGraphics.Dispose();
            pixelCanvas.Dispose();
            labelFont.Dispose();
        }
    }
}
