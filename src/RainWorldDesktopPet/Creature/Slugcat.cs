using System;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Physics;
using RainWorldDesktopPet.Audio;
using System.Collections.Generic;

namespace RainWorldDesktopPet.Creature
{
    public sealed class Slugcat
    {
        private int grabbedChunk = -1;
        private long physicsTick;
        private readonly TerrainImpactData lastTerrainImpact = new TerrainImpactData();
        private bool impactStunEpisodeActive;
        private long impactStunDeadlineTick = -1;
        private readonly List<SoundEvent> soundEvents = new List<SoundEvent>();
        private readonly List<AbilityEffect> effects = new List<AbilityEffect>();
        private readonly List<DesktopSpear> spears = new List<DesktopSpear>();
        private ISlugcatAbilityController abilityController;

        public Slugcat(Vec2 spawnPosition)
            : this(spawnPosition, SlugcatId.Default)
        {
        }

        public Slugcat(Vec2 spawnPosition, SlugcatId selectedSlugcat)
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
            SetSelectedSlugcat(selectedSlugcat);
        }

        public Slugcat(Vec2 spawnPosition, SlugcatVariant variant)
            : this(spawnPosition, SlugcatId.Default)
        {
            Appearance = SlugcatAppearance.For(variant);
            SetSelectedProfile(SlugcatProfiles.Get(variant));
        }

        public readonly BodyChunk[] BodyChunks;
        public readonly BodyChunkConnection BodyConnection;
        public readonly SlugcatState State;
        public readonly SlugcatMovement Movement;
        public DesktopCollisionWorld World { get; private set; }
        public VirtualInput LastInput { get; private set; }
        public bool IsGrabbed { get { return grabbedChunk >= 0; } }
        public SlugcatProfile SelectedSlugcat { get; private set; }
        public SlugcatAppearance Appearance { get; private set; }
        public ISlugcatAbilityController AbilityController { get { return abilityController; } }
        public IList<AbilityEffect> AbilityEffects { get { return effects.AsReadOnly(); } }
        public IList<DesktopSpear> Spears { get { return spears.AsReadOnly(); } }
        public TerrainImpactData LastTerrainImpact { get { return lastTerrainImpact; } }
        public long LastTerrainImpactTick { get; private set; }
        public long TerrainImpactSequence { get; private set; }
        public long ImpactStunDeadlineTick { get { return impactStunDeadlineTick; } }

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

        public DesktopSurfaceKind PrimarySupportingSurfaceKind
        {
            get
            {
                return BodyChunks[1].SupportingSurfaceId != 0
                    ? BodyChunks[1].SupportingSurfaceKind
                    : BodyChunks[0].SupportingSurfaceKind;
            }
        }

        public void Step(VirtualInput input, DesktopCollisionWorld world, Vec2 mousePosition, Vec2 mouseVelocity)
        {
            World = world;
            DesktopCollisionSnapshot tickSnapshot = world.CurrentSnapshot;
            input.ResolveEdges(LastInput);
            LastInput = input;
            StepTransientObjects(world);
            bool consciousForSurfaceFriction = State.Conscious;
            if (State.ImpactBlinkTicks > 0) State.ImpactBlinkTicks--;
            if (State.Dead)
            {
                State.Animation = AnimationIndex.Dead;
                State.BodyMode = BodyModeIndex.Dead;
                State.Standing = false;
            }
            else if (State.StunCounter > 0)
            {
                // Player.Update sets this before Creature.Update decrements stun.
                State.Animation = AnimationIndex.None;
                State.BodyMode = BodyModeIndex.Stunned;
                State.Standing = false;
            }
            if (State.StunCounter > 0) State.StunCounter--;
            State.Conscious = !State.Dead && State.StunCounter < 10;
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
                    // Player keeps base.gravity=.9 in WallClimb; that mode adds
                    // its own contact/slide forces after BodyChunk.Update.
                    BodyChunks[i].Integrate(SimulationConstants.GravityPerTick,
                        SimulationConstants.AirFriction);
                }
            }

            // PhysicalObject.Update advances and resolves every BodyChunk before
            // updating BodyChunkConnections. Keep that one-pass ordering here.
            for (int i = 0; i < BodyChunks.Length; i++)
            {
                world.Resolve(BodyChunks[i], tickSnapshot, Movement.IgnoredSurfaceId,
                    consciousForSurfaceFriction
                        ? SimulationConstants.SurfaceFriction
                        : SimulationConstants.UnconsciousSurfaceFriction);
                ProcessTerrainImpacts(BodyChunks[i]);
            }

            for (int iteration = 0; iteration < SimulationConstants.ConstraintIterations; iteration++)
            {
                BodyConnection.Solve();
            }

            // The original update order leaves connections after BodyChunk
            // collision. Preserve that order, then close only shallow monitor
            // corner penetrations created by the connection itself so a chunk
            // cannot begin the next swept pass outside desktop terrain.
            for (int i = 0; i < BodyChunks.Length; i++)
                world.ResolveMonitorTerrainAfterConstraints(BodyChunks[i], tickSnapshot);

            // Player.Update runs PhysicalObject/BodyChunk collision and connection
            // before MovementUpdate. Input forces therefore affect the next tick.
            if (grabbedChunk < 0 && !State.Dead && State.StunCounter < 1)
            {
                abilityController.UpdateBeforeMovement(ref input, world);
                Movement.ApplyInput(input, world);
                abilityController.UpdateAfterMovement(input, world);
            }
            else if (grabbedChunk < 0)
            {
                VirtualInput disabledAbilityInput = input;
                abilityController.UpdateBeforeMovement(ref disabledAbilityInput, world);
                Movement.ApplyDisabledInput(input);
                abilityController.UpdateAfterMovement(disabledAbilityInput, world);
            }
            State.Conscious = !State.Dead && State.StunCounter < 10;
            State.Standing = !State.Dead && State.StunCounter < 1 &&
                State.Grounded && State.BodyMode == BodyModeIndex.Stand;
            // A recovered episode is closed only after the full Player update.
            // A collision on the exact recovery tick therefore cannot reset a
            // fresh three-second horizon before the pet becomes conscious.
            if (impactStunEpisodeActive && State.StunCounter < 1)
            {
                impactStunEpisodeActive = false;
                impactStunDeadlineTick = -1;
            }
            physicsTick++;
        }

        private void ProcessTerrainImpacts(BodyChunk chunk)
        {
            for (int i = 0; i < chunk.TerrainImpactCount; i++)
            {
                TerrainImpactData impact = chunk.TerrainImpacts[i];
                if (!impact.TerrainImpactTriggered) continue;

                // Player.TerrainImpact blinks for any component impact > 12,
                // whether or not this is the first contact tick.
                if (impact.ImpactSpeed > 12.0)
                {
                    int blink = MathUtil.Clamp((int)impact.ImpactSpeed, 12, 60) / 2;
                    State.ImpactBlinkTicks = Math.Max(State.ImpactBlinkTicks, blink);
                }

                if (impact.FirstContact)
                {
                    bool gourmand = SelectedSlugcat.Id == SlugcatId.Gourmand;
                    double deathSpeed = gourmand ? 80.0 : 60.0;
                    double stunSpeed = gourmand ? 40.0 : 35.0;
                    bool originallyLethal = impact.ImpactDirection.Y < 0.0 &&
                        impact.ImpactSpeed > deathSpeed;
                    if (originallyLethal || impact.ImpactSpeed > stunSpeed)
                    {
                        int originalCalculatedStun = CalculateOriginalImpactStun(
                            impact.ImpactSpeed, stunSpeed, deathSpeed);
                        impact.CalculatedStun = originalCalculatedStun;
                        impact.WasOriginallyLethal = originallyLethal;
                        impact.AppliedStun = ApplyNonLethalTerrainImpactStun(
                            originalCalculatedStun);
                        impact.SafetyOverrideApplied = originallyLethal ||
                            impact.AppliedStun < originalCalculatedStun;
                        impact.DesktopResult = impact.AppliedStun >=
                            SimulationConstants.MaxImpactStunTicks
                            ? DesktopPetImpactResult.MaximumStun
                            : (impact.AppliedStun > 0
                                ? DesktopPetImpactResult.Stun
                                : DesktopPetImpactResult.None);
                        impact.ImpactStunDeadlineTick = impactStunDeadlineTick;
                    }
                    EmitImpactSound(impact);
                }
                abilityController.TerrainImpact(impact);
                impact.FinalStunCounter = State.StunCounter;
                lastTerrainImpact.CopyFrom(impact);
                LastTerrainImpactTick = physicsTick;
                TerrainImpactSequence++;
            }
        }

        private int ApplyNonLethalTerrainImpactStun(int originalCalculatedStun)
        {
            if (!impactStunEpisodeActive)
            {
                impactStunEpisodeActive = true;
                impactStunDeadlineTick = physicsTick +
                    SimulationConstants.MaxImpactStunTicks;
            }

            long remainingLong = Math.Max(0L, impactStunDeadlineTick - physicsTick);
            int remaining = remainingLong > int.MaxValue
                ? int.MaxValue
                : (int)remainingLong;
            int applied = Math.Min(originalCalculatedStun,
                Math.Min(SimulationConstants.MaxImpactStunTicks, remaining));
            if (applied > 0) Stun(applied);
            return applied;
        }

        public void Stun(int ticks)
        {
            bool beginsStun = ticks > 10 && State.StunCounter <= 10;
            if (ticks > State.StunCounter)
            {
                State.StunCounter = ticks;
                if (ticks > 10) State.InitialStunValue = ticks;
            }
            // Player.Stun drops standing/feet state, then Creature.Stun only
            // raises the counter. Player.Update selects Stunned/None at the
            // beginning of the following tick; do not move that transition
            // into the TerrainImpact callback itself.
            if (ticks > 5)
            {
                State.Standing = false;
            }
            State.Conscious = !State.Dead && State.StunCounter < 10;
            if (beginsStun) EmitSound("Slugcat_Stunned_Init", Center, 1.0, 1.0, 10);
        }

        public void Die()
        {
            State.Dead = true;
            State.Conscious = false;
            State.Standing = false;
            State.Animation = AnimationIndex.Dead;
            State.BodyMode = BodyModeIndex.Dead;
        }

        public static int CalculateOriginalImpactStun(double speed,
            double stunThreshold, double deathThreshold)
        {
            double amount = MathUtil.InverseLerp(stunThreshold, deathThreshold, speed);
            return (int)MathUtil.Lerp(40.0, 140.0, Math.Pow(amount, 2.5));
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
            if (primaryDelta.LengthSquared > 0.000001)
            {
                // A connected player is one physical object. Carry both chunks
                // by the selected supporting HWND transform so the window move
                // cannot split the body before the next connection solve.
                for (int i = 0; i < BodyChunks.Length; i++)
                {
                    BodyChunks[i].Position += primaryDelta;
                    BodyChunks[i].LastPosition += primaryDelta;
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

        public void Reposition(Vec2 hipsPosition)
        {
            grabbedChunk = -1;
            BodyChunks[1].Position = hipsPosition;
            BodyChunks[0].Position = hipsPosition -
                new Vec2(0.0, SimulationConstants.BodyConnectionDistance);
            for (int i = 0; i < BodyChunks.Length; i++)
            {
                BodyChunk chunk = BodyChunks[i];
                chunk.LastPosition = chunk.Position;
                chunk.Velocity = Vec2.Zero;
                chunk.ContactFloor = false;
                chunk.ContactLeft = false;
                chunk.ContactRight = false;
                chunk.SupportingSurfaceId = 0;
                chunk.WallSurfaceId = 0;
                chunk.PreviousContactFloor = false;
                chunk.PreviousContactLeft = false;
                chunk.PreviousContactRight = false;
                chunk.PreviousSupportingSurfaceId = 0;
                chunk.PreviousWallSurfaceId = 0;
            }
            State.Animation = AnimationIndex.None;
            State.BodyMode = BodyModeIndex.Default;
            State.Grounded = false;
            State.Standing = false;
            State.StunCounter = 0;
            State.InitialStunValue = 0;
            State.Conscious = true;
            if (abilityController != null) abilityController.Reset();
            effects.Clear();
            spears.Clear();
        }

        public bool HitTest(Vec2 point)
        {
            return PickChunk(point, 18.0) >= 0;
        }

        public void SetSelectedSlugcat(SlugcatId id)
        {
            SlugcatProfile profile = SlugcatProfiles.Get(id);
            Appearance = SlugcatAppearance.For(id == SlugcatId.Gourmand
                ? SlugcatVariant.Gourmand : SlugcatVariant.Survivor);
            SetSelectedProfile(profile);
        }

        public void SetVariant(SlugcatVariant variant)
        {
            Appearance = SlugcatAppearance.For(variant);
            SetSelectedProfile(SlugcatProfiles.Get(variant));
        }

        private void SetSelectedProfile(SlugcatProfile profile)
        {
            if (abilityController != null) abilityController.Reset();
            effects.Clear();
            spears.Clear();
            SelectedSlugcat = profile;
            BodyChunks[0].SetMass(SimulationConstants.MainChunkMass * profile.Movement.BodyWeightFactor);
            BodyChunks[1].SetMass(SimulationConstants.HipsChunkMass * profile.Movement.BodyWeightFactor);
            abilityController = profile.CreateController(this);
        }

        public void EmitSound(string id, Vec2 position, double volume, double pitch,
            int cooldownTicks)
        {
            soundEvents.Add(new SoundEvent(id, position, volume, pitch, cooldownTicks));
        }

        public SoundEvent[] DrainSoundEvents()
        {
            SoundEvent[] result = soundEvents.ToArray();
            soundEvents.Clear();
            return result;
        }

        public void AddEffect(AbilityEffect effect)
        {
            if (effect != null) effects.Add(effect);
        }

        public void AddSpear(DesktopSpear spear)
        {
            if (spear != null && !spears.Contains(spear)) spears.Add(spear);
        }

        private void StepTransientObjects(DesktopCollisionWorld world)
        {
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                effects[i].Step();
                if (effects[i].Lifetime <= 0) effects.RemoveAt(i);
            }
            for (int i = 0; i < spears.Count; i++)
            {
                if (spears[i].Step(world))
                {
                    string sound = spears[i].LastImpactSound ?? "Spear_Bounce_Off_Wall";
                    EmitSound(sound, spears[i].Chunk.Position, 1.0, 1.0, 4);
                }
            }
        }

        private void EmitImpactSound(TerrainImpactData impact)
        {
            string id;
            double volume;
            if (impact.ImpactSpeed > 25.0)
            {
                id = SelectedSlugcat.Audio.ImpactHard;
                volume = 1.0;
            }
            else if (impact.ImpactSpeed > 12.0)
            {
                id = SelectedSlugcat.Audio.ImpactMedium;
                volume = 0.75;
            }
            else
            {
                id = SelectedSlugcat.Audio.ImpactLight;
                volume = 0.45;
            }
            int chunkIndex = MathUtil.Clamp(impact.BodyChunkIndex, 0, BodyChunks.Length - 1);
            EmitSound(id, BodyChunks[chunkIndex].Position, volume,
                MathUtil.Lerp(0.5, 2.0, MathUtil.InverseLerp(0.0, 60.0, impact.ImpactSpeed)), 3);
        }
    }
}
