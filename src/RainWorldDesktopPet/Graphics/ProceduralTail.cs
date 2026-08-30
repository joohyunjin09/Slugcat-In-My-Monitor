using System;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Physics;

namespace RainWorldDesktopPet.Graphics
{
    public sealed class ProceduralTail
    {
        // PlayerGraphics creates pup TailSegments with the normal segment
        // radius but half the normal connection length. Do not treat the
        // 17 -> 12 body connection ratio as a uniform tail scale.
        private const double SlugpupTailLengthScale = 0.5;

        private readonly TailSegment[] segments;
        private Vec2 lastHips;
        private double geometryScaleMarker = 1.0;
        private double tailLengthScale = 1.0;

        public ProceduralTail(Vec2 hips)
            : this(hips, SlugcatGraphicsProfiles.White.Tail)
        {
        }

        public ProceduralTail(Vec2 hips, SlugcatTailProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            int count = profile.Radii.Length;
            segments = new TailSegment[count];
            Vec2 position = hips;
            lastHips = hips;
            for (int i = 0; i < count; i++)
            {
                double radius = profile.Radii[i];
                double length = profile.Lengths[i];
                position += new Vec2(-length, 2.0);
                segments[i] = new TailSegment(position, radius, length, i == 0 ? 1.0 : 0.5);
            }
        }

        public TailSegment[] Segments { get { return segments; } }

        // Compatibility marker for the settings bridge. The original pup
        // geometry is not uniformly scaled by this value; it only tells us
        // whether the bridge is requesting adult or pup proportions.
        public double GeometryScale { get { return geometryScaleMarker; } }
        public double TailLengthScale { get { return tailLengthScale; } }

        public void SetGeometryScale(double value, Vec2 hips)
        {
            if (value <= 0.0 || double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException("value");

            SetPupGeometry(value < 0.999999, hips);
            geometryScaleMarker = value;
            lastHips = hips;
        }

        public void SetPupGeometry(bool enabled, Vec2 hips)
        {
            double targetLengthScale = enabled ? SlugpupTailLengthScale : 1.0;
            if (Math.Abs(targetLengthScale - tailLengthScale) < 0.000001)
            {
                lastHips = hips;
                return;
            }

            double ratio = targetLengthScale / tailLengthScale;
            Vec2 currentConnection = hips;
            Vec2 lastConnection = hips;
            for (int i = 0; i < segments.Length; i++)
            {
                TailSegment segment = segments[i];

                Vec2 currentDelta = segment.Position - currentConnection;
                segment.Position = currentConnection + currentDelta * ratio;

                Vec2 lastDelta = segment.LastPosition - lastConnection;
                segment.LastPosition = lastConnection + lastDelta * ratio;

                segment.Velocity *= ratio;
                segment.SetLengthScale(targetLengthScale);

                currentConnection = segment.Position;
                lastConnection = segment.LastPosition;
            }

            tailLengthScale = targetLengthScale;
            lastHips = hips;
        }

        public void Step(
            Vec2 chest,
            Vec2 hips,
            Vec2 physicalHips,
            Vec2 hipsVelocity,
            int facing,
            BodyModeIndex bodyMode,
            DesktopCollisionWorld world,
            double movementScale)
        {
            if (movementScale <= 0.0 || double.IsNaN(movementScale) ||
                double.IsInfinity(movementScale))
                throw new ArgumentOutOfRangeException("movementScale");

            // TailSegment runs in canonical Rain World distances. Horizontal
            // BodyChunk travel is size-compensated before graphics update, so
            // reconstruct the missing canonical frame displacement inside the
            // tail simulation. Leave the root in the authored hips frame and
            // blend the correction toward the tip: the root also drives the
            // hips sprite angle, so translating it would create a waist kink.
            // Rendering applies movementScale spatially again.
            double extraCanonicalX = (physicalHips.X - lastHips.X) *
                (1.0 / movementScale - 1.0);
            if (Math.Abs(extraCanonicalX) > 0.000001)
            {
                for (int i = 0; i < segments.Length; i++)
                {
                    double distalWeight = segments.Length == 1 ? 0.0 :
                        (double)i / (segments.Length - 1);
                    segments[i].Position.X -= extraCanonicalX * distalWeight;
                }
            }
            lastHips = physicalHips;
            // PlayerGraphics.Update starts this factor at one, lowers it while
            // running, and sets it to zero while airborne. It controls both
            // damping and how strongly gravity pulls the tail down.
            double terrainFactor = 1.0;
            bool fastStanding = bodyMode == BodyModeIndex.Stand && Math.Abs(hipsVelocity.X) > 2.0;
            if (bodyMode == BodyModeIndex.Stand)
            {
                terrainFactor = 1.0 - MathUtil.Clamp((Math.Abs(hipsVelocity.X) - 1.0) * 0.5, 0.0, 1.0);
            }
            else if (bodyMode == BodyModeIndex.Default)
            {
                terrainFactor = 0.0;
            }

            Vec2 forceOrigin = chest;
            if (fastStanding)
            {
                forceOrigin = hips + new Vec2(
                    facing * 16.0 *
                        MathUtil.Clamp(Math.Abs(hipsVelocity.X) - 0.2, 0.0, 1.0),
                    4.0);
            }

            Vec2 nextForceOrigin = hips;
            double outwardForce = 28.0;
            for (int i = 0; i < segments.Length; i++)
            {
                TailSegment segment = segments[i];
                TailSegment connectedSegment = i == 0 ? null : segments[i - 1];
                Vec2 connectedPoint = connectedSegment == null ? hips : connectedSegment.Position;

                segment.BeginUpdate();
                segment.ConstrainTo(connectedPoint, connectedSegment);
                segment.ApplyEnvironment(
                    MathUtil.Lerp(0.75, 0.95, terrainFactor),
                    MathUtil.Lerp(0.1, 0.5, terrainFactor));
                terrainFactor = (terrainFactor * 10.0 + 1.0) / 11.0;

                Vec2 fromHips = segment.Position - hips;
                double maximumDistance = 9.0 * (i + 1);
                if (fromHips.Length > maximumDistance)
                {
                    segment.Position = hips + fromHips.Normalized * maximumDistance;
                }

                ResolveSurface(segment, world);

                Vec2 away = segment.Position - forceOrigin;
                double distance = Math.Max(0.001, away.Length);
                segment.Velocity += away / distance * (outwardForce / distance);
                outwardForce *= 0.5;
                forceOrigin = nextForceOrigin;
                nextForceOrigin = segment.Position;
            }

            // Later segment constraints can push an earlier segment after its
            // own hips-distance check. Re-apply the same PlayerGraphics bound
            // after the chain pass so the enlarged desktop X travel cannot
            // leave a visually detached root between fixed ticks.
            for (int i = 0; i < segments.Length; i++)
            {
                Vec2 fromHips = segments[i].Position - hips;
                double maximumDistance = 9.0 * (i + 1);
                if (fromHips.Length > maximumDistance)
                    segments[i].Position = hips + fromHips.Normalized * maximumDistance;
            }
        }

        private static void ResolveSurface(TailSegment segment, DesktopCollisionWorld world)
        {
            double floor;
            long surfaceId;
            if (!world.TryGetFloor(segment.Position.X, segment.LastPosition.Y,
                segment.Radius + 14.0, out floor, out surfaceId)) return;

            if (segment.Position.Y + segment.Radius > floor && segment.LastPosition.Y <= floor)
            {
                segment.Position.Y = floor - segment.Radius;
                segment.Velocity.Y = Math.Min(0.0, segment.Velocity.Y) * 0.2;
                segment.Velocity.X *= 0.85;
            }
        }

        public void CurlAround(Vec2 hips, int direction, double amount)
        {
            amount = MathUtil.Clamp01(amount);
            for (int i = 0; i < segments.Length; i++)
            {
                double fraction = segments.Length == 1 ? 0.0 : (double)i / (segments.Length - 1);
                double curveX = (Math.Sin(fraction * Math.PI) * 25.0 - fraction * 10.0) * -direction;
                double curveY = MathUtil.Lerp(-5.0, 15.0, fraction);
                segments[i].Velocity *= 1.0 - 0.2 * amount;
                segments[i].Position = Vec2.Lerp(
                    segments[i].Position,
                    hips + new Vec2(curveX, curveY),
                    0.1 * amount);
            }
        }

        public void Translate(Vec2 delta)
        {
            lastHips += delta;
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i].Position += delta;
                segments[i].LastPosition += delta;
            }
        }
    }
}
