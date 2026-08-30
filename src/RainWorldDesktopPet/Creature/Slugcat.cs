using System;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Physics;
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
        private readonly List<AbilityEffect> effects = new List<AbilityEffect>();
        private readonly List<DesktopSpear> spears = new List<DesktopSpear>();
        private readonly IList<AbilityEffect> effectView;
        private readonly IList<DesktopSpear> spearView;
        private readonly Random spearImpactRandom = new Random(0x5BEA7);
        private ISlugcatAbilityController abilityController;
        private double sizeMovementScale = 1.0;
        private bool pupAppearance;

        public Slugcat(Vec2 spawnPosition)
            : this(spawnPosition, SlugcatId.White)
        {
        }

        public Slugcat(Vec2 spawnPosition, SlugcatId selectedSlugcat)
        {
            effectView = effects.AsReadOnly();
            spearView = spears.AsReadOnly();
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
            : this(spawnPosition, SlugcatId.White)
        {
            Appearance = SlugcatAppearance.For(variant);
            Appearance.SetPupScale(BodyProportionScale);
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
        public IList<AbilityEffect> AbilityEffects { get { return effectView; } }
        public IList<DesktopSpear> Spears { get { return spearView; } }
        public TerrainImpactData LastTerrainImpact { get { return lastTerrainImpact; } }
        public long LastTerrainImpactTick { get; private set; }
        public long TerrainImpactSequence { get; private set; }
        public long ImpactStunDeadlineTick { get { return impactStunDeadlineTick; } }
        public double SizeMovementScale { get { return sizeMovementScale; } }
        public bool PupAppearance { get { return pupAppearance; } }
        public double BodyProportionScale
        {
            // Player.setPupStatus keeps the original 9/8 BodyChunk radii.
            // The pup-specific connection and tail rules are applied separately.
            get { return 1.0; }
        }
        public double EffectiveBodyConnectionDistance
        {
            get
            {
                return pupAppearance
                    ? SlugpupAppearanceSettings.BodyConnectionDistance
                    : SimulationConstants.BodyConnectionDistance;
            }
        }

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
                        BodyChunks[i].Integrate(SimulationConstants.GravityPerTick,
                            SimulationConstants.AirFriction, sizeMovementScale);
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
                        SimulationConstants.AirFriction, sizeMovementScale);
                }
            }

            // PhysicalObject.Update advances and resolves every BodyChunk before
            // updating BodyChunkConnections. Keep that one-pass ordering here.
            for (int i = 0; grabbedChunk < 0 && i < BodyChunks.Length; i++)
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
            for (int i = 0; grabbedChunk < 0 && i < BodyChunks.Length; i++)
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
                            originalCalculatedStun, originallyLethal);
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
                }
                Movement.TerrainImpact(impact);
                abilityController.TerrainImpact(impact);
                impact.FinalStunCounter = State.StunCounter;
                lastTerrainImpact.CopyFrom(impact);
                LastTerrainImpactTick = physicsTick;
                TerrainImpactSequence++;
            }
        }

        private int ApplyNonLethalTerrainImpactStun(int originalCalculatedStun,
            bool suppressLethalImpactStunSound)
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
            if (applied > 0) Stun(applied, suppressLethalImpactStunSound);
            return applied;
        }

        public void Stun(int ticks)
        {
            Stun(ticks, false);
        }

        private void Stun(int ticks, bool suppressInitialSound)
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
            if (beginsStun && !suppressInitialSound)
            {
                EmitSound("UI_Slugcat_Stunned_Init", Center, 1.0, 1.0, 10);
            }
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
                new Vec2(0.0, EffectiveBodyConnectionDistance);
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
            State.Standing = true;
            State.StunCounter = 0;
            State.InitialStunValue = 0;
            State.Conscious = true;
            if (abilityController != null) abilityController.Reset();
            Movement.Reset();
            effects.Clear();
            spears.Clear();
        }

        public bool HitTest(Vec2 point)
        {
            return PickChunk(point, 18.0) >= 0;
        }

        public void SetSizeScale(double value)
        {
            if (value <= 0.0 || double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException("value");

            sizeMovementScale = value;
            ApplyCollisionRadii(false);
        }

        public void SetPupAppearance(bool enabled)
        {
            if (pupAppearance == enabled)
            {
                if (Appearance != null) Appearance.SetPupScale(BodyProportionScale);
                ApplyCollisionRadii(true);
                return;
            }

            pupAppearance = enabled;
            if (Appearance != null) Appearance.SetPupScale(BodyProportionScale);
            ApplyCollisionRadii(true);

            double targetDistance = EffectiveBodyConnectionDistance;
            if (enabled) BodyConnection.SetMaximumDistance(targetDistance);
            else BodyConnection.ClearMaximumDistance();
            BodyConnection.Distance = targetDistance;

            BodyChunk chest = BodyChunks[0];
            BodyChunk hips = BodyChunks[1];
            Vec2 axis = chest.Position - hips.Position;
            if (axis.LengthSquared < 0.000001) axis = Vec2.Up;
            chest.Position = hips.Position + axis.Normalized * targetDistance;

            // Geometry switches are settings changes, not animation frames. Keep
            // both interpolation snapshots on the same new pose so changing to a
            // pup cannot produce a one-frame adult->pup body lag or vertical bob.
            for (int i = 0; i < BodyChunks.Length; i++)
                BodyChunks[i].LastPosition = BodyChunks[i].Position;
        }

        private void ApplyCollisionRadii(bool preserveGroundContact)
        {
            double scale = sizeMovementScale;
            double mainRadius = SimulationConstants.MainChunkRadius * scale;
            double hipsRadius = SimulationConstants.HipsChunkRadius * scale;
            BodyChunk hips = BodyChunks[1];
            double oldHipsRadius = hips.Radius;
            bool grounded = preserveGroundContact &&
                (State.Grounded || hips.ContactFloor || hips.PreviousContactFloor);

            if (grounded && Math.Abs(oldHipsRadius - hipsRadius) > 0.000001)
            {
                // Windows simulation Y points down. Move the chunk centre by the
                // lost/gained radius so its collision bottom stays on the exact
                // same floor while the explicit desktop size changes.
                Vec2 floorCompensation = new Vec2(0.0, oldHipsRadius - hipsRadius);
                for (int i = 0; i < BodyChunks.Length; i++)
                {
                    BodyChunks[i].Position += floorCompensation;
                    BodyChunks[i].LastPosition += floorCompensation;
                }
            }

            BodyChunks[0].SetRadius(mainRadius);
            BodyChunks[1].SetRadius(hipsRadius);
        }

        public void SetSelectedSlugcat(SlugcatId id)
        {
            SlugcatProfile profile = SlugcatProfiles.Get(id);
            switch (id)
            {
                case SlugcatId.Yellow:
                    Appearance = SlugcatAppearance.For(SlugcatVariant.Monk);
                    break;
                case SlugcatId.Red:
                    Appearance = SlugcatAppearance.For(SlugcatVariant.Hunter);
                    break;
                case SlugcatId.Gourmand:
                    Appearance = SlugcatAppearance.For(SlugcatVariant.Gourmand);
                    break;
                default:
                    Appearance = SlugcatAppearance.For(SlugcatVariant.Survivor);
                    break;
            }
            Appearance.SetPupScale(BodyProportionScale);
            SetSelectedProfile(profile);
        }

        public void SetVariant(SlugcatVariant variant)
        {
            Appearance = SlugcatAppearance.For(variant);
            Appearance.SetPupScale(BodyProportionScale);
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
        }

        public void StartSoundLoop(string id, string loopKey, Vec2 position,
            double volume, double pitch)
        {
        }

        public void StopSoundLoop(string id, string loopKey, Vec2 position)
        {
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
                effects[i].Step(world);
                if (!effects[i].IsAlive) effects.RemoveAt(i);
            }
            for (int i = spears.Count - 1; i >= 0; i--)
            {
                if (spears[i].Step(world))
                {
                    for (int spark = 0; spark < spears[i].ImpactSparkCount; spark++)
                    {
                        Vec2 angle = RandomUnit(spearImpactRandom);
                        Vec2 at = spears[i].Chunk.Position +
                            spears[i].ThrowDirection * (spears[i].Chunk.Radius - 1.0);
                        Vec2 velocity = angle * (spearImpactRandom.NextDouble() * 10.0) -
                            spears[i].ThrowDirection * 10.0;
                        AddEffect(AbilityEffect.CreateSpark(at, velocity, 2, 4,
                            spearImpactRandom));
                    }
                }
                if (spears[i].IsExpired) spears.RemoveAt(i);
            }
        }

        private static Vec2 RandomUnit(Random random)
        {
            double angle = random.NextDouble() * Math.PI * 2.0;
            return new Vec2(Math.Cos(angle), Math.Sin(angle));
        }

    }
}
