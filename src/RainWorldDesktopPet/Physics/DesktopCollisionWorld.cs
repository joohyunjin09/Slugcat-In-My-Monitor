using System;
using System.Collections.Generic;
using System.Drawing;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Graphics;

namespace RainWorldDesktopPet.Physics
{
    public sealed class DesktopCollisionWorld
    {
        private sealed class CachedWindow
        {
            public long Id;
            public IntPtr Handle;
            public Rectangle PreviousBounds;
            public Rectangle CurrentBounds;
            public string Label;
            public int ZOrder;
            public int MissingRefreshes;
            public bool ObservedThisRefresh;
        }

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
        private readonly Dictionary<long, CachedWindow> cachedWindows = new Dictionary<long, CachedWindow>();
        private readonly Dictionary<long, Vec2> latestDeltas = new Dictionary<long, Vec2>();
        private readonly Dictionary<long, Vec2> latestLeftWallDeltas = new Dictionary<long, Vec2>();
        private readonly Dictionary<long, Vec2> latestRightWallDeltas = new Dictionary<long, Vec2>();
        private Rectangle virtualBounds;
        private IList<MonitorInfo> monitors;
        private long snapshotVersion;
        private DesktopCollisionSnapshot currentSnapshot;

        public DesktopCollisionWorld(WindowEnumerator windowEnumerator)
        {
            this.windowEnumerator = windowEnumerator;
            virtualBounds = MonitorManager.GetVirtualBounds();
            monitors = MonitorManager.GetMonitors();
            currentSnapshot = new DesktopCollisionSnapshot(0, surfaces, monitors);
        }

        public IList<DesktopSurface> Surfaces { get { return currentSnapshot.Surfaces; } }
        public DesktopCollisionSnapshot CurrentSnapshot { get { return currentSnapshot; } }
        public Rectangle VirtualBounds { get { return virtualBounds; } }

        public void Refresh(IntPtr overlayHandle)
        {
            IList<DesktopWindowSnapshot> windows = windowEnumerator.Enumerate(overlayHandle);
            RefreshFromSnapshots(windows, windowEnumerator.LastEnumerationSucceeded, true);
        }

        public void RefreshFromSnapshots(IList<DesktopWindowSnapshot> windows)
        {
            RefreshFromSnapshots(windows, true, false, null);
        }

        public void RefreshFromSnapshots(IList<DesktopWindowSnapshot> windows,
            IList<MonitorInfo> monitorTopology)
        {
            RefreshFromSnapshots(windows, true, false, monitorTopology);
        }

        public void RefreshFromSnapshots(IList<DesktopWindowSnapshot> windows,
            bool enumerationSucceeded, bool validateRealHandles)
        {
            RefreshFromSnapshots(windows, enumerationSucceeded, validateRealHandles, null);
        }

        private void RefreshFromSnapshots(IList<DesktopWindowSnapshot> windows,
            bool enumerationSucceeded, bool validateRealHandles,
            IList<MonitorInfo> monitorTopology)
        {
            surfaces.Clear();
            latestDeltas.Clear();
            latestLeftWallDeltas.Clear();
            latestRightWallDeltas.Clear();
            monitors = monitorTopology == null
                ? MonitorManager.GetMonitors()
                : new List<MonitorInfo>(monitorTopology);
            virtualBounds = monitorTopology == null
                ? MonitorManager.GetVirtualBounds()
                : UnionMonitorBounds(monitors);
            AddMonitorTerrain();

            foreach (KeyValuePair<long, CachedWindow> pair in cachedWindows)
                pair.Value.ObservedThisRefresh = false;

            for (int i = 0; i < windows.Count; i++)
            {
                DesktopWindowSnapshot window = windows[i];
                long id = window.Handle.ToInt64();
                CachedWindow cached;
                if (!cachedWindows.TryGetValue(id, out cached))
                {
                    cached = new CachedWindow();
                    cached.Id = id;
                    cached.Handle = window.Handle;
                    cached.PreviousBounds = window.Bounds;
                    cached.CurrentBounds = window.Bounds;
                    cachedWindows.Add(id, cached);
                }
                else
                {
                    cached.PreviousBounds = cached.CurrentBounds;
                    cached.CurrentBounds = window.Bounds;
                }
                cached.Label = string.IsNullOrEmpty(window.Title) ? window.ClassName : window.Title;
                cached.ZOrder = i;
                cached.MissingRefreshes = 0;
                cached.ObservedThisRefresh = true;
            }

            if (enumerationSucceeded)
            {
                List<long> expired = new List<long>();
                foreach (KeyValuePair<long, CachedWindow> pair in cachedWindows)
                {
                    CachedWindow cached = pair.Value;
                    if (cached.ObservedThisRefresh) continue;
                    bool alive = !validateRealHandles || windowEnumerator.IsWindowAlive(cached.Handle);
                    cached.MissingRefreshes++;
                    if (!alive || cached.MissingRefreshes > SimulationConstants.MissingWindowRefreshGrace)
                        expired.Add(pair.Key);
                }
                for (int i = 0; i < expired.Count; i++) cachedWindows.Remove(expired[i]);
            }

            List<CachedWindow> orderedWindows = new List<CachedWindow>(cachedWindows.Values);
            orderedWindows.Sort(delegate(CachedWindow left, CachedWindow right)
            {
                return left.ZOrder.CompareTo(right.ZOrder);
            });
            List<Rectangle> higherZBounds = new List<Rectangle>(orderedWindows.Count);
            for (int i = 0; i < orderedWindows.Count; i++)
            {
                CachedWindow window = orderedWindows[i];
                long id = window.Id;
                Vec2 topDelta = Vec2.Zero;
                Vec2 leftDelta = Vec2.Zero;
                Vec2 rightDelta = Vec2.Zero;
                if (window.ObservedThisRefresh)
                {
                    bool resized = window.CurrentBounds.Width != window.PreviousBounds.Width ||
                                   window.CurrentBounds.Height != window.PreviousBounds.Height;
                    topDelta = new Vec2(
                        resized ? 0.0 : window.CurrentBounds.Left - window.PreviousBounds.Left,
                        window.CurrentBounds.Top - window.PreviousBounds.Top);
                    leftDelta = new Vec2(window.CurrentBounds.Left - window.PreviousBounds.Left,
                        window.CurrentBounds.Top - window.PreviousBounds.Top);
                    rightDelta = new Vec2(window.CurrentBounds.Right - window.PreviousBounds.Right,
                        window.CurrentBounds.Top - window.PreviousBounds.Top);
                }

                latestDeltas[id] = DesktopWorldTransform.ToSimulationDelta(topDelta);
                latestLeftWallDeltas[id] = DesktopWorldTransform.ToSimulationDelta(leftDelta);
                latestRightWallDeltas[id] = DesktopWorldTransform.ToSimulationDelta(rightDelta);
                AddVisibleHorizontalSurfaces(id, DesktopSurfaceKind.WindowTop, window.CurrentBounds,
                    window.CurrentBounds.Top, window.Label, topDelta, higherZBounds,
                    window.PreviousBounds, window.CurrentBounds, window.MissingRefreshes);
                AddVisibleVerticalSurfaces(id, DesktopSurfaceKind.WindowLeftWall, window.CurrentBounds,
                    window.CurrentBounds.Left, window.Label, leftDelta, higherZBounds,
                    window.PreviousBounds, window.CurrentBounds, window.MissingRefreshes);
                AddVisibleVerticalSurfaces(id, DesktopSurfaceKind.WindowRightWall, window.CurrentBounds,
                    window.CurrentBounds.Right - 1, window.Label, rightDelta, higherZBounds,
                    window.PreviousBounds, window.CurrentBounds, window.MissingRefreshes);
                higherZBounds.Add(window.CurrentBounds);
            }
            currentSnapshot = new DesktopCollisionSnapshot(++snapshotVersion, surfaces, monitors);
        }

        private void AddMonitorTerrain()
        {
            for (int i = 0; i < monitors.Count; i++)
            {
                MonitorInfo monitor = monitors[i];
                if (monitor.TaskbarEdge == DesktopTaskbarEdge.Bottom &&
                    !monitor.TaskbarBounds.IsEmpty)
                {
                    surfaces.Add(new DesktopSurface(monitor.TerrainId,
                        DesktopSurfaceKind.TaskbarTop,
                        Rectangle.FromLTRB(monitor.Bounds.Left, monitor.FloorY,
                            monitor.Bounds.Right, monitor.FloorY + 1),
                        monitor.Name + " taskbar"));
                }

                // The explicit monitor floor is always present, including when
                // it coincides with a bottom taskbar top. It is terrain in the
                // shared snapshot, not a teleport or off-screen recovery rule.
                surfaces.Add(new DesktopSurface(monitor.TerrainId,
                    DesktopSurfaceKind.MonitorFloor,
                    Rectangle.FromLTRB(monitor.Bounds.Left, monitor.FloorY,
                        monitor.Bounds.Right, monitor.FloorY + 1),
                    monitor.Name + " desktop floor"));
                AddMonitorBoundary(i, true);
                AddMonitorBoundary(i, false);
            }
        }

        private void AddMonitorBoundary(int monitorIndex, bool left)
        {
            MonitorInfo monitor = monitors[monitorIndex];
            int x = left ? monitor.Bounds.Left : monitor.Bounds.Right;
            List<SurfaceSpan> spans = new List<SurfaceSpan>();
            spans.Add(new SurfaceSpan(monitor.Bounds.Top, monitor.FloorY));
            for (int i = 0; i < monitors.Count; i++)
            {
                if (i == monitorIndex) continue;
                Rectangle other = monitors[i].Bounds;
                bool coversExterior = left
                    ? other.Left < x && other.Right >= x
                    : other.Left <= x && other.Right > x;
                if (coversExterior) SubtractSpan(spans, other.Top, other.Bottom);
            }

            for (int i = 0; i < spans.Count; i++)
            {
                if (spans[i].End <= spans[i].Start) continue;
                Rectangle bounds = left
                    ? Rectangle.FromLTRB(x, spans[i].Start, x + 1, spans[i].End)
                    : Rectangle.FromLTRB(x - 1, spans[i].Start, x, spans[i].End);
                surfaces.Add(new DesktopSurface(monitor.TerrainId,
                    left ? DesktopSurfaceKind.MonitorLeftBoundary :
                        DesktopSurfaceKind.MonitorRightBoundary,
                    bounds, monitor.Name + (left ? " left boundary" : " right boundary")));
            }
        }

        private static Rectangle UnionMonitorBounds(IList<MonitorInfo> topology)
        {
            if (topology == null || topology.Count == 0) return Rectangle.Empty;
            Rectangle bounds = topology[0].Bounds;
            for (int i = 1; i < topology.Count; i++) bounds = Rectangle.Union(bounds, topology[i].Bounds);
            return bounds;
        }

        private void AddVisibleHorizontalSurfaces(long id, DesktopSurfaceKind kind, Rectangle bounds,
            int y, string label, Vec2 delta, IList<Rectangle> occluders,
            Rectangle previousBounds, Rectangle currentBounds, int missingRefreshes)
        {
            List<SurfaceSpan> spans = new List<SurfaceSpan>(4);
            // Split by monitor work areas before applying window z-order. A
            // maximized/top-snapped window at the monitor ceiling otherwise
            // creates a floor whose standing position is outside the screen.
            for (int i = 0; i < monitors.Count; i++)
            {
                Rectangle work = monitors[i].WorkArea;
                if (y < work.Top + DesktopWorldTransform.ToDesktopLength(
                    SimulationConstants.VisibleWindowTopClearance) || y > work.Bottom)
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
                    Rectangle.FromLTRB(spans[i].Start, y, spans[i].End, y + 1), label,
                    previousBounds, currentBounds, missingRefreshes);
                surface.MovementDelta = DesktopWorldTransform.ToSimulationDelta(delta);
                surfaces.Add(surface);
            }
        }

        private void AddVisibleVerticalSurfaces(long id, DesktopSurfaceKind kind, Rectangle bounds,
            int x, string label, Vec2 delta, IList<Rectangle> occluders,
            Rectangle previousBounds, Rectangle currentBounds, int missingRefreshes)
        {
            List<SurfaceSpan> spans = new List<SurfaceSpan>(4);
            // A climber is located outside the window. Keep only wall portions
            // for which that exterior body center belongs to a real monitor,
            // and clip away the same unsafe work-area ceiling used by tops.
            double desktopRadius = DesktopWorldTransform.ToDesktopLength(
                SimulationConstants.MainChunkRadius);
            double exteriorX = kind == DesktopSurfaceKind.WindowLeftWall
                ? x - desktopRadius
                : x + 1.0 + desktopRadius;
            for (int i = 0; i < monitors.Count; i++)
            {
                Rectangle work = monitors[i].WorkArea;
                if (exteriorX < work.Left || exteriorX >= work.Right) continue;
                int start = Math.Max(bounds.Top,
                    work.Top + (int)Math.Ceiling(DesktopWorldTransform.ToDesktopLength(
                        SimulationConstants.VisibleWindowTopClearance)));
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
                DesktopSurface surface = new DesktopSurface(id, kind, segment, label,
                    previousBounds, currentBounds, missingRefreshes);
                surface.MovementDelta = DesktopWorldTransform.ToSimulationDelta(delta);
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
            Resolve(chunk, currentSnapshot, ignoredHorizontalSurfaceId,
                SimulationConstants.SurfaceFriction);
        }

        public void Resolve(BodyChunk chunk, DesktopCollisionSnapshot snapshot,
            long ignoredHorizontalSurfaceId = 0)
        {
            Resolve(chunk, snapshot, ignoredHorizontalSurfaceId,
                SimulationConstants.SurfaceFriction);
        }

        public void Resolve(BodyChunk chunk, DesktopCollisionSnapshot snapshot,
            long ignoredHorizontalSurfaceId, double surfaceFriction)
        {
            Resolve(chunk, snapshot, ignoredHorizontalSurfaceId, surfaceFriction,
                SimulationConstants.Bounce);
        }

        public void Resolve(BodyChunk chunk, DesktopCollisionSnapshot snapshot,
            long ignoredHorizontalSurfaceId, double surfaceFriction, double bounce)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            ResolveHorizontal(chunk, snapshot.Surfaces, ignoredHorizontalSurfaceId,
                surfaceFriction, bounce);
            ResolveVertical(chunk, snapshot.Surfaces, surfaceFriction, bounce);
            chunk.CollisionSnapshotVersion = snapshot.Version;
        }

        // BodyChunkConnections run after the original collision pass and can
        // move a chunk a few units back through a monitor corner. Re-project
        // only that shallow, post-constraint penetration against the explicit
        // monitor boundary/floor surfaces from the same frozen snapshot. This
        // is contact resolution, not an off-screen recovery teleport, and it
        // deliberately does not synthesize a second TerrainImpact event.
        public void ResolveMonitorTerrainAfterConstraints(BodyChunk chunk,
            DesktopCollisionSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");

            IList<DesktopSurface> tickSurfaces = snapshot.Surfaces;
            for (int i = 0; i < tickSurfaces.Count; i++)
            {
                DesktopSurface surface = tickSurfaces[i];
                bool leftBoundary = surface.Kind == DesktopSurfaceKind.MonitorLeftBoundary;
                bool rightBoundary = surface.Kind == DesktopSurfaceKind.MonitorRightBoundary;
                if (!leftBoundary && !rightBoundary) continue;
                if (chunk.Position.Y < surface.Top - chunk.Radius ||
                    chunk.Position.Y > surface.Bottom + chunk.Radius)
                    continue;

                double wall = surface.WallX;
                double resolvedX = leftBoundary
                    ? wall + chunk.Radius
                    : wall - chunk.Radius;
                bool shallowPenetration = leftBoundary
                    ? chunk.Position.X < resolvedX && chunk.Position.X >= wall - chunk.Radius
                    : chunk.Position.X > resolvedX && chunk.Position.X <= wall + chunk.Radius;
                if (!shallowPenetration) continue;

                chunk.Position.X = resolvedX;
                if ((leftBoundary && chunk.Velocity.X < 0.0) ||
                    (rightBoundary && chunk.Velocity.X > 0.0))
                    chunk.Velocity.X = 0.0;
                chunk.ContactLeft |= leftBoundary;
                chunk.ContactRight |= rightBoundary;
                chunk.WallSurfaceId = surface.Id;
                chunk.WallSurfaceKind = surface.Kind;
            }

            DesktopSurface bestFloor = null;
            double bestTop = double.MaxValue;
            double collisionRadius = Math.Max(0.0, chunk.Radius - 1.0);
            for (int i = 0; i < tickSurfaces.Count; i++)
            {
                DesktopSurface surface = tickSurfaces[i];
                if (surface.Kind != DesktopSurfaceKind.MonitorFloor &&
                    surface.Kind != DesktopSurfaceKind.TaskbarTop)
                    continue;
                double penetration = chunk.Position.Y + chunk.Radius - surface.Top;
                if (penetration <= 0.0 || penetration > chunk.Radius * 1.5)
                    continue;
                if (chunk.Position.X < surface.Left - collisionRadius ||
                    chunk.Position.X > surface.Right + collisionRadius)
                    continue;
                if (surface.Top >= bestTop) continue;
                bestFloor = surface;
                bestTop = surface.Top;
            }

            if (bestFloor == null) return;
            chunk.Position.Y = bestFloor.Top - chunk.Radius;
            if (chunk.Velocity.Y > 0.0) chunk.Velocity.Y = 0.0;
            chunk.ContactFloor = true;
            chunk.SupportingSurfaceId = bestFloor.Id;
            chunk.SupportingSurfaceKind = bestFloor.Kind;
        }

        private static void ResolveHorizontal(BodyChunk chunk,
            IList<DesktopSurface> tickSurfaces, long ignoredSurfaceId,
            double surfaceFriction, double bounce)
        {
            DesktopSurface best = null;
            double bestTop = double.MaxValue;
            double oldBottom = chunk.LastPosition.Y + chunk.Radius;
            double newBottom = chunk.Position.Y + chunk.Radius;

            for (int i = 0; i < tickSurfaces.Count; i++)
            {
                DesktopSurface surface = tickSurfaces[i];
                if (ignoredSurfaceId != 0 && surface.Id == ignoredSurfaceId &&
                    surface.Kind == DesktopSurfaceKind.WindowTop)
                {
                    continue;
                }
                if (!surface.IsHorizontal)
                {
                    continue;
                }

                bool crossed = oldBottom <= surface.Top + 2.0 && newBottom >= surface.Top;
                bool shallowPenetration = newBottom > surface.Top && newBottom < surface.Top + chunk.Radius * 1.5 && chunk.Position.Y < surface.Top;
                bool retainedSupportPenetration = chunk.PreviousContactFloor &&
                    chunk.PreviousSupportingSurfaceId == surface.Id &&
                    chunk.PreviousSupportingSurfaceKind == surface.Kind &&
                    newBottom >= surface.Top;
                double sampleX = chunk.Position.X;
                if (crossed && Math.Abs(newBottom - oldBottom) > 0.000001)
                {
                    double impactTime = MathUtil.Clamp((surface.Top - oldBottom) /
                        (newBottom - oldBottom), 0.0, 1.0);
                    sampleX = MathUtil.Lerp(chunk.LastPosition.X, chunk.Position.X, impactTime);
                }
                double collisionRadius = Math.Max(0.0, chunk.Radius - 1.0);
                bool overlapsAtImpact = sampleX >= surface.Left - collisionRadius &&
                                        sampleX <= surface.Right + collisionRadius;
                if (chunk.Velocity.Y >= -0.01 && overlapsAtImpact &&
                    (crossed || shallowPenetration || retainedSupportPenetration) &&
                    surface.Top < bestTop)
                {
                    best = surface;
                    bestTop = surface.Top;
                }
            }

            if (best == null)
            {
                return;
            }

            Vec2 preImpactVelocity = chunk.Velocity;
            double impactSpeed = Math.Max(0.0, preImpactVelocity.Y);
            // BodyChunk passes lastContactPoint.y > -1 for a floor impact.
            // Terrain identity is deliberately irrelevant: moving from one
            // coplanar floor tile/surface to another does not create a new
            // directional first contact in Rain World.
            bool firstContact = !chunk.PreviousContactFloor;
            TerrainImpactData impact = chunk.RecordTerrainCollision(best,
                preImpactVelocity, new Vec2(0.0, -1.0), new Vec2(0.0, -1.0),
                impactSpeed, firstContact);
            impact.TerrainImpactTriggered = impactSpeed > SimulationConstants.ImpactThreshold;

            chunk.Position.Y = best.Top - chunk.Radius;
            chunk.FloorImpactSpeed = Math.Max(chunk.FloorImpactSpeed, impactSpeed);
            double rebound = impactSpeed * bounce;
            double stopThreshold = 1.0 + 9.0 * (1.0 - bounce);
            if (rebound < SimulationConstants.GravityPerTick || rebound < stopThreshold)
            {
                chunk.Velocity.Y = 0.0;
            }
            else
            {
                chunk.Velocity.Y = -rebound;
            }

            chunk.Velocity.X *= MathUtil.Clamp(surfaceFriction * 2.0, 0.0, 1.0);
            chunk.ContactFloor = true;
            chunk.SupportingSurfaceId = best.Id;
            chunk.SupportingSurfaceKind = best.Kind;
            impact.PostImpactVelocity = chunk.Velocity;
        }

        private static void ResolveVertical(BodyChunk chunk,
            IList<DesktopSurface> tickSurfaces, double surfaceFriction, double bounce)
        {
            DesktopSurface best = null;
            bool positiveMotion = false;
            double bestTime = double.MaxValue;
            double bestWall = 0.0;
            for (int i = 0; i < tickSurfaces.Count; i++)
            {
                DesktopSurface surface = tickSurfaces[i];
                bool movingPositive = surface.BlocksPositiveX && chunk.Velocity.X > 0.0;
                bool movingNegative = surface.BlocksNegativeX && chunk.Velocity.X < 0.0;
                bool restingPositive = surface.BlocksPositiveX && Math.Abs(chunk.Velocity.X) <= 0.01;
                bool restingNegative = surface.BlocksNegativeX && Math.Abs(chunk.Velocity.X) <= 0.01;
                if (!movingPositive && !movingNegative && !restingPositive && !restingNegative) continue;

                bool positive = movingPositive || restingPositive;
                double wall = surface.WallX;
                double oldEdge = chunk.LastPosition.X + (positive ? chunk.Radius : -chunk.Radius);
                double newEdge = chunk.Position.X + (positive ? chunk.Radius : -chunk.Radius);
                bool crossed = positive
                    ? oldEdge <= wall && newEdge >= wall
                    : oldEdge >= wall && newEdge <= wall;
                bool resting = Math.Abs(newEdge - wall) <= 1.5;
                if (!crossed && !resting) continue;
                double impactTime = crossed && Math.Abs(newEdge - oldEdge) > 0.000001
                    ? MathUtil.Clamp((wall - oldEdge) / (newEdge - oldEdge), 0.0, 1.0)
                    : 1.0;
                double sampleY = MathUtil.Lerp(chunk.LastPosition.Y,
                    chunk.Position.Y, impactTime);
                if (sampleY < surface.Top + 3.0 || sampleY > surface.Bottom) continue;
                if (impactTime >= bestTime) continue;
                best = surface;
                bestTime = impactTime;
                bestWall = wall;
                positiveMotion = positive;
            }

            if (best == null) return;
            Vec2 preImpactVelocity = chunk.Velocity;
            double impactSpeed = Math.Abs(preImpactVelocity.X);
            // Mirrors lastContactPoint.x < 1 / > -1 in BodyChunk. Switching
            // terrain identity while retaining the same contact direction is
            // not a fresh TerrainImpact contact.
            bool firstContact = positiveMotion
                ? !chunk.PreviousContactRight
                : !chunk.PreviousContactLeft;
            Vec2 impactDirection = new Vec2(positiveMotion ? 1.0 : -1.0, 0.0);
            Vec2 collisionNormal = new Vec2(positiveMotion ? -1.0 : 1.0, 0.0);
            TerrainImpactData impact = chunk.RecordTerrainCollision(best,
                preImpactVelocity, impactDirection, collisionNormal, impactSpeed, firstContact);
            impact.TerrainImpactTriggered = impactSpeed > SimulationConstants.ImpactThreshold;

            chunk.Position.X = bestWall + (positiveMotion ? -chunk.Radius : chunk.Radius);
            chunk.Velocity.X = (positiveMotion ? -1.0 : 1.0) *
                impactSpeed * bounce;
            double stopThreshold = 1.0 + 9.0 * (1.0 - bounce);
            if (Math.Abs(chunk.Velocity.X) < stopThreshold) chunk.Velocity.X = 0.0;
            chunk.Velocity.Y *= MathUtil.Clamp(surfaceFriction * 2.0, 0.0, 1.0);
            chunk.ContactRight = positiveMotion;
            chunk.ContactLeft = !positiveMotion;
            chunk.WallSurfaceId = best.Id;
            chunk.WallSurfaceKind = best.Kind;
            impact.PostImpactVelocity = chunk.Velocity;
        }

        // GenericBodyPart.PushOutOfTerrain adapted to the exposed desktop
        // top/wall representation. It uses the same frozen terrain view as
        // BodyChunks and only resolves an actually swept circular contact.
        public void PushOutOfTerrain(BodyPart part, Vec2 basePoint)
        {
            IList<DesktopSurface> tickSurfaces = currentSnapshot.Surfaces;
            for (int i = 0; i < tickSurfaces.Count; i++)
            {
                DesktopSurface surface = tickSurfaces[i];
                if (surface.IsHorizontal)
                {
                    double oldBottom = part.LastPosition.Y + part.Radius;
                    double newBottom = part.Position.Y + part.Radius;
                    bool crossed = oldBottom <= surface.Top + 0.5 && newBottom >= surface.Top;
                    if (!crossed || part.Velocity.Y < 0.0) continue;
                    double t = Math.Abs(newBottom - oldBottom) < 0.000001
                        ? 1.0
                        : MathUtil.Clamp((surface.Top - oldBottom) / (newBottom - oldBottom), 0.0, 1.0);
                    double x = MathUtil.Lerp(part.LastPosition.X, part.Position.X, t);
                    if (x < surface.Left - part.Radius || x > surface.Right + part.Radius) continue;
                    part.Position.Y = surface.Top - part.Radius;
                    part.Velocity.Y = 0.0;
                    part.Velocity.X *= part.SurfaceFriction;
                    continue;
                }

                if (!surface.BlocksPositiveX && !surface.BlocksNegativeX) continue;
                double wall = surface.WallX;
                bool fromLeft = surface.BlocksPositiveX;
                bool crossedWall = fromLeft
                    ? part.LastPosition.X + part.Radius <= wall && part.Position.X + part.Radius >= wall
                    : part.LastPosition.X - part.Radius >= wall && part.Position.X - part.Radius <= wall;
                if (!crossedWall) continue;
                double oldEdge = part.LastPosition.X + (fromLeft ? part.Radius : -part.Radius);
                double newEdge = part.Position.X + (fromLeft ? part.Radius : -part.Radius);
                double tWall = Math.Abs(newEdge - oldEdge) < 0.000001
                    ? 1.0
                    : MathUtil.Clamp((wall - oldEdge) / (newEdge - oldEdge), 0.0, 1.0);
                double y = MathUtil.Lerp(part.LastPosition.Y, part.Position.Y, tWall);
                if (y < surface.Top - part.Radius || y > surface.Bottom + part.Radius) continue;
                part.Position.X = wall + (fromLeft ? -part.Radius : part.Radius);
                part.Velocity.X = 0.0;
                part.Velocity.Y *= part.SurfaceFriction;
            }
        }

        public bool TryGetSurface(long surfaceId, DesktopSurfaceKind kind, out DesktopSurface found)
        {
            for (int i = 0; i < surfaces.Count; i++)
            {
                DesktopSurface surface = surfaces[i];
                if (surface.Id == surfaceId && surface.Kind == kind)
                {
                    found = surface;
                    return true;
                }
            }
            found = null;
            return false;
        }

        public bool ContainsSurface(long surfaceId, DesktopSurfaceKind kind, Vec2 point, double tolerance)
        {
            for (int i = 0; i < surfaces.Count; i++)
            {
                DesktopSurface surface = surfaces[i];
                if (surface.Id != surfaceId || surface.Kind != kind) continue;
                if (surface.IsHorizontal)
                {
                    if (point.X >= surface.Left - tolerance && point.X <= surface.Right + tolerance &&
                        Math.Abs(point.Y - surface.Top) <= tolerance) return true;
                }
                else if (point.Y >= surface.Top - tolerance && point.Y <= surface.Bottom + tolerance &&
                         Math.Abs(point.X - surface.Left) <= tolerance) return true;
            }
            return false;
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
            DesktopSurfaceKind ignoredKind;
            return TryGetWall(x, y, direction, maximumDistance,
                out wallX, out surfaceId, out ignoredKind);
        }

        public bool TryGetWall(double x, double y, int direction, double maximumDistance,
            out double wallX, out long surfaceId, out DesktopSurfaceKind surfaceKind)
        {
            wallX = 0.0;
            surfaceId = 0;
            surfaceKind = DesktopSurfaceKind.ScreenEdge;
            double bestDistance = maximumDistance + 1.0;
            for (int i = 0; i < surfaces.Count; i++)
            {
                DesktopSurface surface = surfaces[i];
                bool matchingSide = direction > 0
                    ? surface.BlocksPositiveX
                    : surface.BlocksNegativeX;
                if (!matchingSide || y < surface.Top || y > surface.Bottom) continue;

                double candidateX = surface.WallX;
                double distance = (candidateX - x) * direction;
                if (distance < -2.0 || distance > maximumDistance || distance >= bestDistance) continue;
                bestDistance = Math.Max(0.0, distance);
                wallX = candidateX;
                surfaceId = surface.Id;
                surfaceKind = surface.Kind;
            }
            return surfaceId != 0;
        }

        public MonitorInfo FindMonitor(Vec2 simulationPoint)
        {
            Vec2 desktop = DesktopWorldTransform.ToDesktop(simulationPoint);
            Point point = new Point((int)Math.Round(desktop.X), (int)Math.Round(desktop.Y));
            return MonitorManager.FindNearest(currentSnapshot.Monitors, point);
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
