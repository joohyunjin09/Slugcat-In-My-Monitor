using System;
using System.Collections.Generic;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;

namespace RainWorldDesktopPet.Physics
{
    // Desktop counterpart of Rain World's Rope: it keeps a polyline between
    // body and tongue, introducing an exposed window corner when a straight
    // segment crosses terrain. Rendering samples this path by arc length.
    public sealed class DesktopRope
    {
        private readonly List<Vec2> path = new List<Vec2>(4);
        private readonly List<Vec2> bends = new List<Vec2>(4);

        public DesktopRope(Vec2 a, Vec2 b)
        {
            path.Add(a);
            path.Add(b);
            TotalLength = Vec2.Distance(a, b);
        }

        public IList<Vec2> Path { get { return path.AsReadOnly(); } }
        public double TotalLength { get; private set; }
        public Vec2 AConnect { get { return path.Count > 1 ? path[1] : path[0]; } }
        public Vec2 BConnect { get { return path.Count > 1 ? path[path.Count - 2] : path[0]; } }

        public void Update(DesktopCollisionWorld world, Vec2 a, Vec2 b,
            long attachedSurfaceId)
        {
            for (int i = bends.Count - 1; i >= 0; i--)
            {
                Vec2 before = i == 0 ? a : bends[i - 1];
                Vec2 after = i == bends.Count - 1 ? b : bends[i + 1];
                if (!CrossesAnySurface(world, before, after, attachedSurfaceId))
                    bends.RemoveAt(i);
            }

            path.Clear();
            path.Add(a);
            for (int i = 0; i < bends.Count; i++) path.Add(bends[i]);
            path.Add(b);

            int insertions = 0;
            int segment = 0;
            while (segment < path.Count - 1 && insertions < 50)
            {
                Vec2 corner;
                if (TryFindCorner(world, path[segment], path[segment + 1],
                    attachedSurfaceId, b, out corner) &&
                    Vec2.Distance(corner, path[segment]) > 0.01 &&
                    Vec2.Distance(corner, path[segment + 1]) > 0.01)
                {
                    path.Insert(segment + 1, corner);
                    bends.Insert(segment, corner);
                    insertions++;
                    continue;
                }
                segment++;
            }

            TotalLength = 0.0;
            for (int i = 1; i < path.Count; i++)
                TotalLength += Vec2.Distance(path[i - 1], path[i]);
        }

        private static bool TryFindCorner(DesktopCollisionWorld world, Vec2 a, Vec2 b,
            long attachedSurfaceId, Vec2 attachment, out Vec2 bestCorner)
        {
            bestCorner = Vec2.Zero;
            double bestLength = double.MaxValue;
            bool found = false;
            for (int i = 0; i < world.Surfaces.Count; i++)
            {
                DesktopSurface surface = world.Surfaces[i];
                if (surface.Id == attachedSurfaceId &&
                    DistanceToSurface(attachment, surface) < 6.0) continue;
                if (!CrossesSurface(a, b, surface)) continue;
                Vec2 first = surface.IsHorizontal
                    ? new Vec2(surface.Left, surface.Top)
                    : new Vec2(surface.WallX, surface.Top);
                Vec2 second = surface.IsHorizontal
                    ? new Vec2(surface.Right, surface.Top)
                    : new Vec2(surface.WallX, surface.Bottom);
                EvaluateCorner(a, b, first, ref bestCorner, ref bestLength);
                EvaluateCorner(a, b, second, ref bestCorner, ref bestLength);
                found = true;
            }
            return found;
        }

        private static bool CrossesAnySurface(DesktopCollisionWorld world, Vec2 a, Vec2 b,
            long attachedSurfaceId)
        {
            for (int i = 0; i < world.Surfaces.Count; i++)
            {
                DesktopSurface surface = world.Surfaces[i];
                if (surface.Id == attachedSurfaceId) continue;
                if (CrossesSurface(a, b, surface)) return true;
            }
            return false;
        }

        public Vec2[] Sample(int segmentCount)
        {
            if (segmentCount < 2) throw new ArgumentOutOfRangeException("segmentCount");
            Vec2[] result = new Vec2[segmentCount];
            if (TotalLength < 0.0001)
            {
                for (int i = 0; i < result.Length; i++) result[i] = path[0];
                return result;
            }
            int edge = 1;
            double passed = 0.0;
            for (int i = 0; i < result.Length; i++)
            {
                double wanted = TotalLength * i / (result.Length - 1.0);
                while (edge < path.Count - 1 &&
                    passed + Vec2.Distance(path[edge - 1], path[edge]) < wanted)
                {
                    passed += Vec2.Distance(path[edge - 1], path[edge]);
                    edge++;
                }
                double length = Vec2.Distance(path[edge - 1], path[edge]);
                double local = length < 0.0001 ? 0.0 : (wanted - passed) / length;
                result[i] = Vec2.Lerp(path[edge - 1], path[edge], local);
            }
            return result;
        }

        private static void EvaluateCorner(Vec2 a, Vec2 b, Vec2 corner,
            ref Vec2 best, ref double bestLength)
        {
            double length = Vec2.Distance(a, corner) + Vec2.Distance(corner, b);
            if (length >= bestLength) return;
            best = corner;
            bestLength = length;
        }

        private static bool CrossesSurface(Vec2 a, Vec2 b, DesktopSurface surface)
        {
            Vec2 delta = b - a;
            if (surface.IsHorizontal)
            {
                if (Math.Abs(delta.Y) < 0.0001) return false;
                double t = (surface.Top - a.Y) / delta.Y;
                if (t <= 0.01 || t >= 0.99) return false;
                double x = a.X + delta.X * t;
                return x > surface.Left && x < surface.Right;
            }
            if (Math.Abs(delta.X) < 0.0001) return false;
            double verticalT = (surface.WallX - a.X) / delta.X;
            if (verticalT <= 0.01 || verticalT >= 0.99) return false;
            double y = a.Y + delta.Y * verticalT;
            return y > surface.Top && y < surface.Bottom;
        }

        private static double DistanceToSurface(Vec2 point, DesktopSurface surface)
        {
            if (surface.IsHorizontal)
                return Math.Abs(point.Y - surface.Top) +
                    Math.Max(0.0, Math.Max(surface.Left - point.X, point.X - surface.Right));
            return Math.Abs(point.X - surface.WallX) +
                Math.Max(0.0, Math.Max(surface.Top - point.Y, point.Y - surface.Bottom));
        }
    }
}
