using System;
using System.Drawing;
using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Desktop
{
    public enum DesktopSurfaceKind
    {
        WorkAreaFloor,
        MonitorFloor,
        TaskbarTop,
        WindowTop,
        WindowLeftWall,
        WindowRightWall,
        MonitorLeftBoundary,
        MonitorRightBoundary,
        ScreenEdge
    }

    public sealed class DesktopSurface
    {
        public DesktopSurface(long id, DesktopSurfaceKind kind, Rectangle bounds, string label)
            : this(id, kind, bounds, label, bounds, bounds, 0)
        {
        }

        public DesktopSurface(long id, DesktopSurfaceKind kind, Rectangle bounds, string label,
            Rectangle previousWindowBounds, Rectangle currentWindowBounds, int missingRefreshes)
        {
            Id = id;
            Kind = kind;
            Bounds = bounds;
            Label = label ?? string.Empty;
            PreviousWindowBounds = previousWindowBounds;
            CurrentWindowBounds = currentWindowBounds;
            MissingRefreshes = missingRefreshes;
            MovementDelta = Vec2.Zero;
        }

        public readonly long Id;
        public readonly DesktopSurfaceKind Kind;
        public readonly Rectangle Bounds;
        public readonly string Label;
        public readonly Rectangle PreviousWindowBounds;
        public readonly Rectangle CurrentWindowBounds;
        public readonly int MissingRefreshes;
        public Vec2 MovementDelta { get; internal set; }

        public bool IsHorizontal
        {
            get
            {
                return Kind == DesktopSurfaceKind.WorkAreaFloor ||
                    Kind == DesktopSurfaceKind.MonitorFloor ||
                    Kind == DesktopSurfaceKind.TaskbarTop ||
                    Kind == DesktopSurfaceKind.WindowTop;
            }
        }

        public bool BlocksPositiveX
        {
            get
            {
                return Kind == DesktopSurfaceKind.WindowLeftWall ||
                    Kind == DesktopSurfaceKind.MonitorRightBoundary;
            }
        }

        public bool BlocksNegativeX
        {
            get
            {
                return Kind == DesktopSurfaceKind.WindowRightWall ||
                    Kind == DesktopSurfaceKind.MonitorLeftBoundary;
            }
        }

        public double WallX
        {
            get
            {
                return (Kind == DesktopSurfaceKind.WindowRightWall ||
                    Kind == DesktopSurfaceKind.MonitorRightBoundary)
                    ? Right
                    : Left;
            }
        }

        // Collision-facing bounds are Rain World units. Bounds itself remains
        // the exact desktop rectangle for diagnostics and overlay drawing.
        public double Top { get { return DesktopWorldTransform.ToSimulationLength(Bounds.Top); } }
        public double Left { get { return DesktopWorldTransform.ToSimulationLength(Bounds.Left); } }
        public double Right { get { return DesktopWorldTransform.ToSimulationLength(Bounds.Right); } }
        public double Bottom { get { return DesktopWorldTransform.ToSimulationLength(Bounds.Bottom); } }
        public Vec2 MovementVelocity
        {
            get { return MovementDelta / SimulationConstants.WindowRefreshSeconds; }
        }
    }
}
