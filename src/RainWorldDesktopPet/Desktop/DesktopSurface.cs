using System;
using System.Drawing;
using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Desktop
{
    public enum DesktopSurfaceKind
    {
        WorkAreaFloor,
        WindowTop,
        WindowLeftWall,
        WindowRightWall,
        ScreenEdge
    }

    public sealed class DesktopSurface
    {
        public DesktopSurface(long id, DesktopSurfaceKind kind, Rectangle bounds, string label)
        {
            Id = id;
            Kind = kind;
            Bounds = bounds;
            Label = label ?? string.Empty;
            MovementDelta = Vec2.Zero;
        }

        public readonly long Id;
        public readonly DesktopSurfaceKind Kind;
        public readonly Rectangle Bounds;
        public readonly string Label;
        public Vec2 MovementDelta;

        public bool IsHorizontal
        {
            get
            {
                return Kind == DesktopSurfaceKind.WorkAreaFloor || Kind == DesktopSurfaceKind.WindowTop;
            }
        }

        public double Top { get { return Bounds.Top; } }
        public double Left { get { return Bounds.Left; } }
        public double Right { get { return Bounds.Right; } }
        public double Bottom { get { return Bounds.Bottom; } }
    }
}
