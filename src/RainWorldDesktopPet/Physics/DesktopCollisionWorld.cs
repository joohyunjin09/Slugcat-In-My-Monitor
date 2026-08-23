using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;

namespace RainWorldDesktopPet.Physics
{
    public sealed class DesktopCollisionWorld
    {
        private struct SurfaceSpan
        {
            public SurfaceSpan(int start, int end)
            {
                Start = start;
                End = end;
            }

            public int Start;
            public int End;
        }

        private readonly WindowEnumerator windowEnumerator;
        private readonly List<DesktopSurface> surfaces = new List<DesktopSurface>(128);
        private readonly ReadOnlyCollection<DesktopSurface> readOnlySurfaces;
        private readonly Dictionary<long, Rectangle> previousWindowBounds = new Dictionary<long, Rectangle>();
        private readonly Dictionary<long, Vec2> latestDeltas = new Dictionary<long, Vec2>();
        private readonly Dictionary<long, Vec2> latestLeftWallDeltas = new Dictionary<long, Vec2>();
        private readonly Dictionary<long, Vec2> latestRightWallDeltas = new Dictionary<long, Vec2>();
        private Rectangle virtualBounds;
        private IList<MonitorInfo> monitors;

        public DesktopCollisionWorld(WindowEnumerator windowEnumerator)
        {
            this.windowEnumerator = windowEnumerator;
            readOnlySurfaces = surfaces.AsReadOnly();
            virtualBounds = MonitorManager.GetVirtualBounds();
            monitors = MonitorManager.GetMonitors();
        }

        public IList<DesktopSurface> Surfaces { get { return readOnlySurfaces; } }
        public Rectangle VirtualBounds { get { return virtualBounds; } }

        public void Refresh(IntPtr overlayHandle)
        {
            RefreshFromSnapshots(windowEnumerator.Enumerate(overlayHandle));
        }

        public void RefreshFromSnapshots(IList<DesktopWindowSnapshot> windows)
        {
            surfaces.Clear();
            latestDeltas.Clear();
            latestLeftWallDeltas.Clear();
            latestRightWallDeltas.Clear();
            virtualBounds = MonitorManager.GetVirtualBounds();

            monitors = MonitorManager.GetMonitors();
            for (int i = 0; i < monitors.Count; i++)
            {
                Rectangle work = monitors[i].WorkArea;
                long id = -1000L - i;
                surfaces.Add(new DesktopSurface(id, DesktopSurfaceKind.WorkAreaFloor,
                    Rectangle.FromLTRB(work.Left, work.Bottom, work.Right, work.Bottom + 1), monitors[i].Name));
            }

            Dictionary<long, Rectangle> currentBounds = new Dictionary<long, Rectangle>();
            List<Rectangle> higherZBounds = new List<Rectangle>(windows.Count);
            for (int i = 0; i < windows.Count; i++)
            {
                DesktopWindowSnapshot window = windows[i];
                long id = window.Handle.ToInt64();
                currentBounds[id] = window.Bounds;
                Vec2 topDelta = Vec2.Zero;
                Vec2 leftDelta = Vec2.Zero;
                Vec2 rightDelta = Vec2.Zero;
                Rectangle previous;
                if (previousWindowBounds.TryGetValue(id, out previous))
                {
                    bool resized = window.Bounds.Width != previous.Width || window.Bounds.Height != previous.Height;
                    topDelta = new Vec2(
                        resized ? 0.0 : window.Bounds.Left - previous.Left,
                        window.Bounds.Top - previous.Top);
                    leftDelta = new Vec2(window.Bounds.Left - previous.Left, window.Bounds.Top - previous.Top);
                    rightDelta = new Vec2(window.Bounds.Right - previous.Right, window.Bounds.Top - previous.Top);
                }

                latestDeltas[id] = topDelta;
                latestLeftWallDeltas[id] = leftDelta;
                latestRightWallDeltas[id] = rightDelta;
                string label = string.IsNullOrEmpty(window.Title) ? window.ClassName : window.Title;
                AddVisibleHorizontalSurfaces(id, DesktopSurfaceKind.WindowTop, window.Bounds,
                    window.Bounds.Top, label, topDelta, higherZBounds);
                AddVisibleVerticalSurfaces(id, DesktopSurfaceKind.WindowLeftWall, window.Bounds,
                    window.Bounds.Left, label, leftDelta, higherZBounds);
                AddVisibleVerticalSurfaces(id, DesktopSurfaceKind.WindowRightWall, window.Bounds,
                    window.Bounds.Right - 1, label, rightDelta, higherZBounds);
                higherZBounds.Add(window.Bounds);
            }

            previousWindowBounds.Clear();
            foreach (KeyValuePair<long, Rectangle> item in currentBounds)
            {
                previousWindowBounds[item.Key] = item.Value;
            }
        }

        private void AddVisibleHorizontalSurfaces(long id, DesktopSurfaceKind kind, Rectangle bounds,
            int y, string label, Vec2 delta, IList<Rectangle> occluders)
        {
            List<SurfaceSpan> spans = new List<SurfaceSpan>(4);
            // Split by monitor work areas before applying window z-order. A
            // maximized/top-snapped window at the monitor ceiling otherwise
            // creates a floor whose standing position is outside the screen.
            for (int i = 0; i < monitors.Count; i++)
            {
                Rectangle work = monitors[i].WorkArea;
                if (y < work.Top + SimulationConstants.VisibleWindowTopClearance || y > work.Bottom)
                    continue;
                int start = Math.Max(bounds.Left, work.Left);
                int end = Math.Min(bounds.Right, work.Right);
                if (end > start) spans.Add(new SurfaceSpan(start, end));
            }
            if (spans.Count == 0) return;
            for (int i = 0; i < occluders.Count; i++)
            {
                Rectangle occluder = occluders[i];
                if (occluder.Top <= y && occluder.Bottom > y)
                    SubtractSpan(spans, occluder.Left, occluder.Right);
                if (spans.Count == 0) return;
            }

            for (int i = 0; i < spans.Count; i++)
            {
                if (spans[i].End <= spans[i].Start) continue;
                DesktopSurface surface = new DesktopSurface(id, kind,
                    Rectangle.FromLTRB(spans[i].Start, y, spans[i].End, y + 1), label);
                surface.MovementDelta = delta;
                surfaces.Add(surface);
            }
        }

        private void AddVisibleVerticalSurfaces(long id, DesktopSurfaceKind kind, Rectangle bounds,
            int x, string label, Vec2 delta, IList<Rectangle> occluders)
        {
            List<SurfaceSpan> spans = new List<SurfaceSpan>(4);
            // A climber is located outside the window. Keep only wall portions
            // for which that exterior body center belongs to a real monitor,
            // and clip away the same unsafe work-area ceiling used by tops.
            double exteriorX = kind == DesktopSurfaceKind.WindowLeftWall
                ? x - SimulationConstants.MainChunkRadius
                : x + 1.0 + SimulationConstants.MainChunkRadius;
            for (int i = 0; i < monitors.Count; i++)
            {
                Rectangle work = monitors[i].WorkArea;
                if (exteriorX < work.Left || exteriorX >= work.Right) continue;
                int start = Math.Max(bounds.Top,
                    work.Top + (int)Math.Ceiling(SimulationConstants.VisibleWindowTopClearance));
                int end = Math.Min(bounds.Bottom, work.Bottom);
                if (end > start) spans.Add(new SurfaceSpan(start, end));
            }
            if (spans.Count == 0) return;
            for (int i = 0; i < occluders.Count; i++)
            {
                Rectangle occluder = occluders[i];
                if (occluder.Left <= x && occluder.Right > x)
                    SubtractSpan(spans, occluder.Top, occluder.Bottom);
                if (spans.Count == 0) return;
            }

            for (int i = 0; i < spans.Count; i++)
            {
                if (spans[i].End <= spans[i].Start) continue;
                Rectangle segment = Rectangle.FromLTRB(x, spans[i].Start, x + 1, spans[i].End);
                DesktopSurface surface = new DesktopSurface(id, kind, segment, label);
                surface.MovementDelta = delta;
                surfaces.Add(surface);
            }
        }

        private static void SubtractSpan(List<SurfaceSpan> spans, int coveredStart, int coveredEnd)
        {
            for (int i = spans.Count - 1; i >= 0; i--)
            {
                SurfaceSpan span = spans[i];
                int overlapStart = Math.Max(span.Start, coveredStart);
                int overlapEnd = Math.Min(span.End, coveredEnd);
                if (overlapStart >= overlapEnd) continue;

                spans.RemoveAt(i);
                if (span.Start < overlapStart) spans.Add(new SurfaceSpan(span.Start, overlapStart));
                if (overlapEnd < span.End) spans.Add(new SurfaceSpan(overlapEnd, span.End));
            }
        }

        public Vec2 GetSurfaceMovement(long surfaceId)
        {
            return GetSurfaceMovement(surfaceId, DesktopSurfaceKind.WindowTop);
        }

        public Vec2 GetSurfaceMovement(long surfaceId, DesktopSurfaceKind kind)
        {
            Vec2 value;
            if (kind == DesktopSurfaceKind.WindowLeftWall)
                return latestLeftWallDeltas.TryGetValue(surfaceId, out value) ? value : Vec2.Zero;
            if (kind == DesktopSurfaceKind.WindowRightWall)
                return latestRightWallDeltas.TryGetValue(surfaceId, out value) ? value : Vec2.Zero;
            return latestDeltas.TryGetValue(surfaceId, out value) ? value : Vec2.Zero;
        }

        public void Resolve(BodyChunk chunk, long ignoredHorizontalSurfaceId = 0)
        {
            ResolveHorizontal(chunk, ignoredHorizontalSurfaceId);
            ResolveVertical(chunk);
            RecoverAgainstMonitorBounds(chunk);
        }

        private void ResolveHorizontal(BodyChunk chunk, long ignoredSurfaceId)
        {
            DesktopSurface best = null;
            double bestTop = double.MaxValue;
            double oldBottom = chunk.LastPosition.Y + chunk.Radius;
            double newBottom = chunk.Position.Y + chunk.Radius;

            for (int i = 0; i < surfaces.Count; i++)
            {
                DesktopSurface surface = surfaces[i];
                if (ignoredSurfaceId != 0 && surface.Id == ignoredSurfaceId &&
                    surface.Kind == DesktopSurfaceKind.WindowTop)
                {
                    continue;
                }
                if (!surface.IsHorizontal || chunk.Position.X < surface.Left - chunk.Radius * 0.35 || chunk.Position.X > surface.Right + chunk.Radius * 0.35)
                {
                    continue;
                }

                bool crossed = oldBottom <= surface.Top + 2.0 && newBottom >= surface.Top;
                bool shallowPenetration = newBottom > surface.Top && newBottom < surface.Top + chunk.Radius * 1.5 && chunk.Position.Y < surface.Top;
                if (chunk.Velocity.Y >= -0.01 && (crossed || shallowPenetration) && surface.Top < bestTop)
                {
                    best = surface;
                    bestTop = surface.Top;
                }
            }

            if (best == null)
            {
                return;
            }

            chunk.Position.Y = best.Top - chunk.Radius;
            chunk.FloorImpactSpeed = Math.Max(chunk.FloorImpactSpeed, Math.Max(0.0, chunk.Velocity.Y));
            double rebound = Math.Abs(chunk.Velocity.Y) * SimulationConstants.Bounce;
            double stopThreshold = 1.0 + 9.0 * (1.0 - SimulationConstants.Bounce);
            if (rebound < SimulationConstants.GravityPerTick || rebound < stopThreshold)
            {
                chunk.Velocity.Y = 0.0;
            }
            else
            {
                chunk.Velocity.Y = -rebound;
            }

            chunk.Velocity.X *= MathUtil.Clamp(SimulationConstants.SurfaceFriction * 2.0, 0.0, 1.0);
            chunk.ContactFloor = true;
            chunk.SupportingSurfaceId = best.Id;
        }

        private void ResolveVertical(BodyChunk chunk)
        {
            for (int i = 0; i < surfaces.Count; i++)
            {
                DesktopSurface surface = surfaces[i];
                if (surface.Kind != DesktopSurfaceKind.WindowLeftWall && surface.Kind != DesktopSurfaceKind.WindowRightWall)
                {
                    continue;
                }

                if (chunk.Position.Y < surface.Top + 3.0 || chunk.Position.Y > surface.Bottom)
                {
                    continue;
                }

                if (surface.Kind == DesktopSurfaceKind.WindowLeftWall && chunk.Velocity.X > 0.0)
                {
                    double wall = surface.Left;
                    bool crossed = chunk.LastPosition.X + chunk.Radius <= wall && chunk.Position.X + chunk.Radius >= wall;
                    bool restingContact = chunk.Velocity.X >= -0.01 &&
                        Math.Abs(chunk.Position.X + chunk.Radius - wall) <= 1.5;
                    if (crossed || restingContact)
                    {
                        chunk.Position.X = wall - chunk.Radius;
                        if (chunk.Velocity.X > 0.0) chunk.Velocity.X *= -0.15;
                        chunk.ContactRight = true;
                        chunk.WallSurfaceId = surface.Id;
                        chunk.WallSurfaceKind = surface.Kind;
                    }
                }
                else if (surface.Kind == DesktopSurfaceKind.WindowLeftWall && Math.Abs(chunk.Velocity.X) <= 0.01)
                {
                    double wall = surface.Left;
                    if (Math.Abs(chunk.Position.X + chunk.Radius - wall) <= 1.5)
                    {
                        chunk.Position.X = wall - chunk.Radius;
                        chunk.ContactRight = true;
                        chunk.WallSurfaceId = surface.Id;
                        chunk.WallSurfaceKind = surface.Kind;
                    }
                }
                else if (surface.Kind == DesktopSurfaceKind.WindowRightWall && chunk.Velocity.X < 0.0)
                {
                    double wall = surface.Right;
                    bool crossed = chunk.LastPosition.X - chunk.Radius >= wall && chunk.Position.X - chunk.Radius <= wall;
                    bool restingContact = chunk.Velocity.X <= 0.01 &&
                        Math.Abs(chunk.Position.X - chunk.Radius - wall) <= 1.5;
                    if (crossed || restingContact)
                    {
                        chunk.Position.X = wall + chunk.Radius;
                        if (chunk.Velocity.X < 0.0) chunk.Velocity.X *= -0.15;
                        chunk.ContactLeft = true;
                        chunk.WallSurfaceId = surface.Id;
                        chunk.WallSurfaceKind = surface.Kind;
                    }
                }
                else if (surface.Kind == DesktopSurfaceKind.WindowRightWall && Math.Abs(chunk.Velocity.X) <= 0.01)
                {
                    double wall = surface.Right;
                    if (Math.Abs(chunk.Position.X - chunk.Radius - wall) <= 1.5)
                    {
                        chunk.Position.X = wall + chunk.Radius;
                        chunk.ContactLeft = true;
                        chunk.WallSurfaceId = surface.Id;
                        chunk.WallSurfaceKind = surface.Kind;
                    }
                }
            }
        }

        private void RecoverAgainstMonitorBounds(BodyChunk chunk)
        {
            Point point = new Point((int)Math.Round(chunk.Position.X), (int)Math.Round(chunk.Position.Y));
            for (int i = 0; i < monitors.Count; i++)
            {
                if (monitors[i].Bounds.Contains(point)) return;
            }

            // A jump above a shorter, vertically offset monitor can still lie
            // inside the virtual screen's bounding rectangle. Preserve the
            // recovery margin for the monitor whose horizontal band owns X.
            for (int i = 0; i < monitors.Count; i++)
            {
                Rectangle bounds = monitors[i].Bounds;
                if (point.X >= bounds.Left && point.X < bounds.Right &&
                    chunk.Position.Y >= bounds.Top - SimulationConstants.RecoveryMargin &&
                    chunk.Position.Y <= bounds.Bottom + SimulationConstants.RecoveryMargin)
                {
                    return;
                }
            }

            bool inVirtualGap = virtualBounds.Contains(point);
            bool outsideVerticalMargin = chunk.Position.Y < virtualBounds.Top - SimulationConstants.RecoveryMargin ||
                                         chunk.Position.Y > virtualBounds.Bottom + SimulationConstants.RecoveryMargin;
            bool outsideHorizontalBounds = chunk.Position.X < virtualBounds.Left || chunk.Position.X > virtualBounds.Right;
            if (!inVirtualGap && !outsideVerticalMargin && !outsideHorizontalBounds)
            {
                return;
            }

            MonitorInfo monitor = MonitorManager.FindNearest(point);
            Rectangle safe = monitor.WorkArea;
            double oldX = chunk.Position.X;
            double oldY = chunk.Position.Y;
            chunk.Position.X = MathUtil.Clamp(chunk.Position.X,
                safe.Left + chunk.Radius, safe.Right - chunk.Radius);
            chunk.Position.Y = MathUtil.Clamp(chunk.Position.Y,
                safe.Top + chunk.Radius, safe.Bottom - chunk.Radius);
            chunk.LastPosition = chunk.Position;

            if (chunk.Position.X > oldX)
            {
                chunk.Velocity.X = Math.Abs(chunk.Velocity.X) * 0.25;
                chunk.ContactLeft = true;
            }
            else if (chunk.Position.X < oldX)
            {
                chunk.Velocity.X = -Math.Abs(chunk.Velocity.X) * 0.25;
                chunk.ContactRight = true;
            }
            if (Math.Abs(chunk.Position.Y - oldY) > 0.001) chunk.Velocity.Y = 0.0;
        }

        public bool TryGetFloor(double x, double y, double maximumDrop, out double floorY, out long surfaceId)
        {
            floorY = double.MaxValue;
            surfaceId = 0;
            for (int i = 0; i < surfaces.Count; i++)
            {
                DesktopSurface surface = surfaces[i];
                if (!surface.IsHorizontal || x < surface.Left || x > surface.Right || surface.Top < y - 2.0 || surface.Top > y + maximumDrop)
                {
                    continue;
                }

                if (surface.Top < floorY)
                {
                    floorY = surface.Top;
                    surfaceId = surface.Id;
                }
            }

            return floorY < double.MaxValue;
        }

        public bool TryGetWall(double x, double y, int direction, double maximumDistance,
            out double wallX, out long surfaceId)
        {
            wallX = 0.0;
            surfaceId = 0;
            double bestDistance = maximumDistance + 1.0;
            for (int i = 0; i < surfaces.Count; i++)
            {
                DesktopSurface surface = surfaces[i];
                bool matchingSide = direction > 0
                    ? surface.Kind == DesktopSurfaceKind.WindowLeftWall
                    : surface.Kind == DesktopSurfaceKind.WindowRightWall;
                if (!matchingSide || y < surface.Top || y > surface.Bottom) continue;

                double candidateX = direction > 0 ? surface.Left : surface.Right;
                double distance = (candidateX - x) * direction;
                if (distance < -2.0 || distance > maximumDistance || distance >= bestDistance) continue;
                bestDistance = Math.Max(0.0, distance);
                wallX = candidateX;
                surfaceId = surface.Id;
            }
            return surfaceId != 0;
        }

        public double DistanceToEdge(Vec2 position, int direction, long preferredSurfaceId)
        {
            double best = 1000.0;
            for (int i = 0; i < surfaces.Count; i++)
            {
                DesktopSurface surface = surfaces[i];
                if (!surface.IsHorizontal || (preferredSurfaceId != 0 && surface.Id != preferredSurfaceId))
                {
                    continue;
                }

                if (position.X >= surface.Left - 4.0 && position.X <= surface.Right + 4.0 && Math.Abs(position.Y - surface.Top) < 60.0)
                {
                    double distance = direction < 0 ? position.X - surface.Left : surface.Right - position.X;
                    if (distance >= 0.0 && distance < best)
                    {
                        best = distance;
                    }
                }
            }

            return best;
        }
    }
}
