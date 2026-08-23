using System;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Physics;

namespace RainWorldDesktopPet.Creature
{
    public sealed class Slugcat
    {
        private int grabbedChunk = -1;

        public Slugcat(Vec2 spawnPosition)
            : this(spawnPosition, SlugcatVariant.Survivor)
        {
        }

        public Slugcat(Vec2 spawnPosition, SlugcatVariant variant)
        {
            BodyChunks = new BodyChunk[2];
            BodyChunks[0] = new BodyChunk(0, spawnPosition + new Vec2(0.0, -SimulationConstants.BodyConnectionDistance),
                SimulationConstants.MainChunkRadius, SimulationConstants.MainChunkMass);
            BodyChunks[1] = new BodyChunk(1, spawnPosition,
                SimulationConstants.HipsChunkRadius, SimulationConstants.HipsChunkMass);
            BodyConnection = new BodyChunkConnection(
                BodyChunks[0], BodyChunks[1], SimulationConstants.BodyConnectionDistance,
                BodyChunkConnectionType.Normal, SimulationConstants.BodyConnectionElasticity,
                SimulationConstants.BodyConnectionSymmetry);
            State = new SlugcatState();
            Movement = new SlugcatMovement(this);
            SetVariant(variant);
        }

        public readonly BodyChunk[] BodyChunks;
        public readonly BodyChunkConnection BodyConnection;
        public readonly SlugcatState State;
        public readonly SlugcatMovement Movement;
        public DesktopCollisionWorld World { get; private set; }
        public VirtualInput LastInput { get; private set; }
        public bool IsGrabbed { get { return grabbedChunk >= 0; } }
        public SlugcatAppearance Appearance { get; private set; }

        public Vec2 Center { get { return (BodyChunks[0].Position + BodyChunks[1].Position) * 0.5; } }

        public long PrimarySupportingSurfaceId
        {
            get
            {
                return BodyChunks[1].SupportingSurfaceId != 0
                    ? BodyChunks[1].SupportingSurfaceId
                    : BodyChunks[0].SupportingSurfaceId;
            }
        }

        public void Step(VirtualInput input, DesktopCollisionWorld world, Vec2 mousePosition, Vec2 mouseVelocity)
        {
            World = world;
            LastInput = input;
            for (int i = 0; i < BodyChunks.Length; i++)
            {
                BodyChunks[i].BeginTick();
            }

            if (grabbedChunk >= 0)
            {
                for (int i = 0; i < BodyChunks.Length; i++)
                {
                    if (i == grabbedChunk)
                    {
                        BodyChunks[i].Position = Vec2.Lerp(BodyChunks[i].Position, mousePosition, 0.55);
                        BodyChunks[i].Velocity = Vec2.ClampMagnitude(mouseVelocity / SimulationConstants.LogicTicksPerSecond, 30.0);
                    }
                    else
                    {
                        BodyChunks[i].Integrate(SimulationConstants.GravityPerTick, SimulationConstants.AirFriction);
                    }
                }
            }
            else
            {
                for (int i = 0; i < BodyChunks.Length; i++)
                {
                    double gravity = State.BodyMode == BodyModeIndex.WallClimb ? SimulationConstants.GravityPerTick * 0.15 : SimulationConstants.GravityPerTick;
                    BodyChunks[i].Integrate(gravity, SimulationConstants.AirFriction);
                }
            }

            // PhysicalObject.Update advances and resolves every BodyChunk before
            // updating BodyChunkConnections. Keep that one-pass ordering here.
            for (int i = 0; i < BodyChunks.Length; i++)
            {
                world.Resolve(BodyChunks[i], Movement.IgnoredSurfaceId);
            }

            for (int iteration = 0; iteration < SimulationConstants.ConstraintIterations; iteration++)
            {
                BodyConnection.Solve();
            }

            // Player.Update runs PhysicalObject/BodyChunk collision and connection
            // before MovementUpdate. Input forces therefore affect the next tick.
            if (grabbedChunk < 0)
            {
                Movement.ApplyInput(input, world);
            }
        }

        public Vec2 ApplyMovingSurfaceDelta(DesktopCollisionWorld world)
        {
            if (State.BodyMode == BodyModeIndex.WallClimb)
            {
                BodyChunk wallChunk = BodyChunks[0].WallSurfaceId != 0 ? BodyChunks[0] : BodyChunks[1];
                if (wallChunk.WallSurfaceId != 0)
                {
                    Vec2 wallDelta = world.GetSurfaceMovement(wallChunk.WallSurfaceId, wallChunk.WallSurfaceKind);
                    for (int i = 0; i < BodyChunks.Length; i++)
                    {
                        BodyChunks[i].Position += wallDelta;
                        BodyChunks[i].LastPosition += wallDelta;
                    }
                    return wallDelta;
                }
            }

            Vec2 primaryDelta = world.GetSurfaceMovement(PrimarySupportingSurfaceId);
            for (int i = 0; i < BodyChunks.Length; i++)
            {
                long id = BodyChunks[i].SupportingSurfaceId;
                if (id != 0)
                {
                    Vec2 delta = world.GetSurfaceMovement(id);
                    BodyChunks[i].Position += delta;
                    BodyChunks[i].LastPosition += delta;
                }
            }
            return primaryDelta;
        }

        public int PickChunk(Vec2 point, double extraRadius)
        {
            int best = -1;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < BodyChunks.Length; i++)
            {
                double distance = Vec2.Distance(point, BodyChunks[i].Position);
                if (distance <= BodyChunks[i].Radius + extraRadius && distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best;
        }

        public bool Grab(Vec2 point)
        {
            grabbedChunk = PickChunk(point, 14.0);
            return grabbedChunk >= 0;
        }

        public void Release(Vec2 mouseVelocity)
        {
            if (grabbedChunk >= 0)
            {
                BodyChunks[grabbedChunk].Velocity = Vec2.ClampMagnitude(mouseVelocity / SimulationConstants.LogicTicksPerSecond * 1.08, 35.0);
            }

            grabbedChunk = -1;
        }

        public bool HitTest(Vec2 point)
        {
            return PickChunk(point, 18.0) >= 0;
        }

        public void SetVariant(SlugcatVariant variant)
        {
            Appearance = SlugcatAppearance.For(variant);
            BodyChunks[0].SetMass(SimulationConstants.MainChunkMass * Appearance.BodyWeightFactor);
            BodyChunks[1].SetMass(SimulationConstants.HipsChunkMass * Appearance.BodyWeightFactor);
        }
    }
}
