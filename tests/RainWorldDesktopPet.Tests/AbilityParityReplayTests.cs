using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Threading;
using RainWorldDesktopPet.Audio;
using RainWorldDesktopPet.AI;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Graphics;
using RainWorldDesktopPet.Physics;
using RainWorldDesktopPet.RainWorld;

namespace RainWorldDesktopPet.Tests
{
    internal sealed class AbilityReplayTick
    {
        public int Tick;
        public Vec2 ChestPosition;
        public Vec2 HipsPosition;
        public Vec2 ChestVelocity;
        public Vec2 HipsVelocity;
        public AnimationIndex Animation;
        public BodyModeIndex BodyMode;
        public string AbilityState;
        public int SpearCount;
        public int EffectCount;
        public SoundEvent[] Sounds;
    }

    internal static class AbilityInputReplay
    {
        public static IList<AbilityReplayTick> Run(Slugcat slugcat,
            DesktopCollisionWorld world, IList<VirtualInput> inputs)
        {
            List<AbilityReplayTick> result = new List<AbilityReplayTick>(inputs.Count);
            for (int tick = 0; tick < inputs.Count; tick++)
            {
                slugcat.Step(inputs[tick], world, Vec2.Zero, Vec2.Zero);
                result.Add(new AbilityReplayTick
                {
                    Tick = tick,
                    ChestPosition = slugcat.BodyChunks[0].Position,
                    HipsPosition = slugcat.BodyChunks[1].Position,
                    ChestVelocity = slugcat.BodyChunks[0].Velocity,
                    HipsVelocity = slugcat.BodyChunks[1].Velocity,
                    Animation = slugcat.State.Animation,
                    BodyMode = slugcat.State.BodyMode,
                    AbilityState = slugcat.AbilityController.DebugState,
                    SpearCount = slugcat.Spears.Count,
                    EffectCount = slugcat.AbilityEffects.Count,
                    Sounds = slugcat.DrainSoundEvents()
                });
            }
            return result;
        }
    }

    internal static class AbilityParityReplayTests
    {
        public static void Register(Action<string, Action> run)
        {
            run("Exactly eight characters share one ordered profile selector",
                UnifiedEightCharacterProfiles);
            run("Character switching clears previous ability objects and sounds",
                CharacterSwitchClearsAbilityState);
            run("Artificer replay matches explosive-jump chunk assignments",
                ArtificerExplosiveJumpReplay);
            run("Artificer down input follows the original parry counter branch",
                ArtificerParryReplay);
            run("Artificer effects retain original smoke and light lifecycles",
                ArtificerEffectLifecycleReplay);
            run("Spearmaster extraction creates the needle on original progress tick",
                SpearmasterExtractionReplay);
            run("Spearmaster needle grows from the selected tail speckle",
                SpearmasterTailGrowthReplay);
            run("Spearmaster neutral gate freezes creation while Pickup stays held",
                SpearmasterNeutralGateReplay);
            run("Spearmaster throw uses ThrowObject velocity and needle gravity",
                SpearmasterThrowReplay);
            run("Spearmaster AI holds without a target and traverses explicit action states",
                SpearmasterAiActionStateReplay);
            run("Spear wall bounce sound is owned by one contact-state transition",
                SpearBounceTransitionReplay);
            run("Rivulet replay uses stats-driven ground jump and shared air control",
                RivuletMovementReplay);
            run("Movement launch momentum produces character-specific trajectories",
                MovementMomentumTrajectories);
            run("Saint replay shoots, attaches and jump-releases through Tongue states",
                SaintTongueReplay);
            run("Gourmand falling diagonal replay gates roll through original counters",
                GourmandRollReplay);
            run("Gourmand exhaustion uses aerobicLevel recovery and slowMovementStun",
                GourmandExhaustionReplay);
            run("Local sounds.txt maps ability SoundIDs and PLAYALL metadata",
                LocalAbilitySoundCatalog);
            run("Death and game-over SoundIDs are suppressed before playback",
                DeathSoundsAreSuppressed);
            run("Installed UnityFS jump family decodes and queues without blocking",
                LocalUnityFsAudioPlayback);
            run("Sound setting defaults ON, persists, and gates future events",
                SoundSettingPersistenceAndGate);
        }

        private static void UnifiedEightCharacterProfiles()
        {
            SlugcatId[] expected =
            {
                SlugcatId.White,
                SlugcatId.Yellow,
                SlugcatId.Red,
                SlugcatId.Gourmand,
                SlugcatId.Artificer,
                SlugcatId.SpearMaster,
                SlugcatId.Rivulet,
                SlugcatId.Saint
            };
            string[] names =
            {
                "White", "Yellow", "Red", "Gourmand",
                "Artificer", "SpearMaster", "Rivulet", "Saint"
            };

            Equal(8, Enum.GetValues(typeof(SlugcatId)).Length, "SlugcatId count");
            Equal(8, SlugcatProfiles.All.Count, "profile count");
            Equal(8, SlugcatGraphicsProfiles.All.Count, "graphics profile count");
            True(SlugcatProfiles.All.IsReadOnly, "character profile list is immutable");
            True(SlugcatGraphicsProfiles.All.IsReadOnly,
                "graphics profile list is immutable");
            for (int i = 0; i < expected.Length; i++)
            {
                Equal((int)expected[i], (int)SlugcatProfiles.All[i].Id,
                    "profile order " + names[i]);
                Equal(names[i], SlugcatProfiles.All[i].DisplayName,
                    "profile display name " + i);
                Equal((int)expected[i], (int)SlugcatGraphicsProfiles.All[i].Id,
                    "graphics profile order " + names[i]);
                Slugcat selected = new Slugcat(Vec2.Zero, expected[i]);
                Equal((int)expected[i], (int)selected.SelectedSlugcat.Id,
                    "runtime selection " + names[i]);
            }

            AssertParse("white", SlugcatId.White);
            AssertParse("survivor", SlugcatId.White);
            AssertParse("default", SlugcatId.White);
            AssertParse("yellow", SlugcatId.Yellow);
            AssertParse("monk", SlugcatId.Yellow);
            AssertParse("red", SlugcatId.Red);
            AssertParse("hunter", SlugcatId.Red);
            AssertParse("spearmaster", SlugcatId.SpearMaster);
            SlugcatId invalid;
            True(!SlugcatProfiles.TryParse("99", out invalid),
                "undefined numeric character id is rejected");
        }

        private static void CharacterSwitchClearsAbilityState()
        {
            DesktopCollisionWorld world = CreateAirWorld();
            Slugcat artificer = CreateAirSlugcat(SlugcatId.Artificer);
            artificer.Step(new VirtualInput(1, -1, true, true), world,
                Vec2.Zero, Vec2.Zero);
            True(artificer.AbilityEffects.Count > 0, "Artificer effects were created");
            True(artificer.DrainSoundEvents().Length > 0, "Artificer sound was created");
            artificer.EmitSound("Fire_Spear_Explode", artificer.Center, 1.0, 1.0, 1);
            artificer.SetSelectedSlugcat(SlugcatId.Yellow);
            Equal(0, artificer.AbilityEffects.Count, "effects after switch");
            Equal(0, artificer.Spears.Count, "spears after switch");
            Equal(0, artificer.DrainSoundEvents().Length, "queued sounds after switch");
            True(artificer.AbilityController is DefaultAbilityController,
                "Yellow uses the base controller");

            Slugcat spearMaster = CreateAirSlugcat(SlugcatId.SpearMaster);
            List<VirtualInput> extract = new List<VirtualInput>();
            for (int i = 0; i < 80; i++)
                extract.Add(new VirtualInput(0, 0, false, true));
            AbilityInputReplay.Run(spearMaster, world, extract);
            True(spearMaster.Spears.Count > 0, "SpearMaster needle was created");
            spearMaster.SetSelectedSlugcat(SlugcatId.Red);
            Equal(0, spearMaster.Spears.Count, "needle after switch");
            True(spearMaster.AbilityController is DefaultAbilityController,
                "Red uses the base controller");

            Slugcat saint = CreateAirSlugcat(SlugcatId.Saint);
            SaintAbilityController oldTongue =
                (SaintAbilityController)saint.AbilityController;
            saint.Step(new VirtualInput(1, 0, true, false), world,
                Vec2.Zero, Vec2.Zero);
            True(oldTongue.Mode != SaintTongueMode.Retracted,
                "Saint tongue entered an active mode");
            saint.SetSelectedSlugcat(SlugcatId.White);
            True(oldTongue.Mode == SaintTongueMode.Retracted,
                "Saint tongue reset after switch");

            Slugcat rivulet = CreateAirSlugcat(SlugcatId.Rivulet);
            SlugcatGraphics graphics = new SlugcatGraphics(rivulet,
                rivulet.SelectedSlugcat.Graphics, null);
            Equal(1, graphics.Extensions.Length, "Rivulet extension count");
            Equal(12, graphics.Extensions[0].SpriteCount, "Rivulet gill sprite count");
            rivulet.SetSelectedSlugcat(SlugcatId.White);
            graphics.SetGraphicsProfile(rivulet.SelectedSlugcat.Graphics, null);
            Equal(0, graphics.Extensions.Length, "extensions after Rivulet switch");
        }

        private static void AssertParse(string value, SlugcatId expected)
        {
            SlugcatId parsed;
            True(SlugcatProfiles.TryParse(value, out parsed), "parse " + value);
            Equal((int)expected, (int)parsed, "parsed id " + value);
        }

        private static void ArtificerExplosiveJumpReplay()
        {
            DesktopCollisionWorld world = CreateAirWorld();
            Slugcat slugcat = CreateAirSlugcat(SlugcatId.Artificer);
            IList<AbilityReplayTick> replay = AbilityInputReplay.Run(slugcat, world,
                new[] { new VirtualInput(1, -1, true, true) });
            ArtificerAbilityController ability =
                (ArtificerAbilityController)slugcat.AbilityController;
            Near(10.0, replay[0].ChestVelocity.X, 0.000001, "up-right chest x");
            Near(8.0, replay[0].HipsVelocity.X, 0.000001, "up-right hips x");
            Near(-11.0, replay[0].ChestVelocity.Y, 0.000001, "up-right chest y");
            Near(-10.0, replay[0].HipsVelocity.Y, 0.000001, "up-right hips y");
            Equal(1, ability.ExplosiveJumpCounter, "pyro jump counter");
            Equal(150, ability.Cooldown, "pyro cooldown");
            Near(8.0, slugcat.Movement.JumpBoost, 0.000001,
                "up explosive jump stores the original jumpBoost");
            True(replay[0].Animation == AnimationIndex.Flip, "Flip animation");
            True(ContainsSound(replay[0].Sounds, "Fire_Spear_Explode"),
                "original explosion SoundID");
            Equal(19, replay[0].EffectCount, "light + eight smoke + ten sparks");
            Near(slugcat.BodyChunks[0].Position.X, replay[0].Sounds[0].Position.X,
                0.000001, "explosive-jump sound uses firstChunk x");
            Near(slugcat.BodyChunks[0].Position.Y, replay[0].Sounds[0].Position.Y,
                0.000001, "explosive-jump sound uses firstChunk y");
        }

        private static void ArtificerParryReplay()
        {
            DesktopCollisionWorld world = CreateAirWorld();
            Slugcat slugcat = CreateAirSlugcat(SlugcatId.Artificer);
            IList<AbilityReplayTick> replay = AbilityInputReplay.Run(slugcat, world,
                new[] { new VirtualInput(0, 1, true, true) });
            ArtificerAbilityController ability =
                (ArtificerAbilityController)slugcat.AbilityController;
            Equal(2, ability.ExplosiveJumpCounter, "safe parry adds two");
            Equal(40, ability.ParryCooldown, "parry cooldown");
            True(HasEffect(slugcat, AbilityEffectKind.ShockWave),
                "parry shockwave exists");
            True(ContainsSound(replay[0].Sounds, "Fire_Spear_Explode"),
                "parry sound event");
        }

        private static void ArtificerEffectLifecycleReplay()
        {
            DesktopCollisionWorld world = CreateAirWorld();
            Slugcat slugcat = CreateAirSlugcat(SlugcatId.Artificer);
            AbilityInputReplay.Run(slugcat, world,
                new[] { new VirtualInput(1, -1, true, true) });
            int smokeCount = 0;
            int sparkCount = 0;
            int lightCount = 0;
            for (int i = 0; i < slugcat.AbilityEffects.Count; i++)
            {
                AbilityEffect effect = slugcat.AbilityEffects[i];
                if (effect.Kind == AbilityEffectKind.Smoke)
                {
                    smokeCount++;
                    True(effect.LifeTime >= 170.0 && effect.LifeTime <= 400.0,
                        "ExplosionSmoke lifetime is not scaled by size");
                    True(effect.Radius >= 0.6 && effect.Radius <= 1.5,
                        "ExplosionSmoke rad range");
                }
                else if (effect.Kind == AbilityEffectKind.Spark)
                {
                    sparkCount++;
                    Near(1.0, effect.Radius, 0.000001,
                        "Spark triangle half-width");
                }
                else if (effect.Kind == AbilityEffectKind.ExplosionLight)
                {
                    lightCount++;
                    Near(160.0, effect.Radius, 0.000001,
                        "ExplosionLight radius");
                    Near(3.0, effect.LifeTime, 0.000001,
                        "ExplosionLight lifetime");
                    Near(0.0, effect.LastLife, 0.000001,
                        "ExplosionLight initializes lastLife at zero");
                }
                True(effect.Kind != AbilityEffectKind.ShockWave,
                    "explosive jump does not create the parry-only ShockWave");
            }
            Equal(8, smokeCount, "ExplosionSmoke count");
            Equal(10, sparkCount, "Spark count");
            Equal(1, lightCount, "ExplosionLight count");
            for (int i = 0; i < 8; i++)
                True(slugcat.AbilityEffects[i].Kind == AbilityEffectKind.Smoke,
                    "ExplosionSmoke creation order " + i);
            True(slugcat.AbilityEffects[8].Kind == AbilityEffectKind.ExplosionLight,
                "ExplosionLight follows the eight smoke objects");
            for (int i = 9; i < 19; i++)
                True(slugcat.AbilityEffects[i].Kind == AbilityEffectKind.Spark,
                    "Spark creation order " + i);

            AbilityEffect light = AbilityEffect.CreateExplosionLight(Vec2.Zero,
                160.0, 1.0, 3);
            for (int tick = 0; tick < 4; tick++) light.Step();
            True(light.IsAlive,
                "ExplosionLight waits until original lastLife becomes negative");
            light.Step();
            True(!light.IsAlive,
                "ExplosionLight expires on the original fifth update check");

            AbilityEffect wave = AbilityEffect.CreateShockWave(Vec2.Zero,
                200.0, 0.2, 6);
            for (int tick = 0; tick < 7; tick++) wave.Step();
            True(wave.IsAlive && wave.Life > 1.0,
                "ShockWave keeps its final clamped draw while lastLife is one");
            wave.Step();
            True(!wave.IsAlive,
                "ShockWave expires only after lastLife exceeds one");
        }

        private static void SpearmasterExtractionReplay()
        {
            DesktopCollisionWorld world = CreateAirWorld();
            Slugcat slugcat = CreateAirSlugcat(SlugcatId.SpearMaster);
            List<VirtualInput> inputs = new List<VirtualInput>();
            for (int i = 0; i < 80; i++)
                inputs.Add(new VirtualInput(0, 0, false, true));
            IList<AbilityReplayTick> replay = AbilityInputReplay.Run(slugcat, world, inputs);
            SpearmasterAbilityController ability =
                (SpearmasterAbilityController)slugcat.AbilityController;
            Equal(0, replay[78].SpearCount, "no spear before progress exceeds .95");
            Equal(1, replay[79].SpearCount, "spear created on zero-based tick 79");
            True(ability.HeldSpear != null, "new needle enters a free grasp");
            Equal(5, (int)ability.HeldSpear.Chunk.Radius, "original spear radius");
            Near(0.07, ability.HeldSpear.Chunk.Mass, 0.000001, "original spear mass");
            True(ability.HeldSpear.IsSpearmasterNeedle,
                "Spear_makeNeedle marks the realized Spear");
            True(ability.HeldSpear.NeedleHasConnection,
                "new needle remains connected for feeding/umbilical state");
            Near(1.25, ability.HeldSpear.DamageBonus, 0.000001,
                "SpearMaster throwing skill damage bonus");
            True(ContainsSound(replay[23].Sounds, "SM_Spear_Pull"),
                "pull sound starts at the first >=.1 branch");
            True(ContainsSound(replay[79].Sounds, "SM_Spear_Grab"),
                "grab sound matches creation tick");
            Equal(9, replay[79].EffectCount, "four drips plus five sparks");
        }

        private static void SpearmasterThrowReplay()
        {
            DesktopCollisionWorld world = CreateAirWorld();
            Slugcat slugcat = CreateAirSlugcat(SlugcatId.SpearMaster);
            List<VirtualInput> extraction = new List<VirtualInput>();
            for (int i = 0; i < 80; i++) extraction.Add(new VirtualInput(0, 0, false, true));
            AbilityInputReplay.Run(slugcat, world, extraction);
            SpearmasterAbilityController ability =
                (SpearmasterAbilityController)slugcat.AbilityController;
            DesktopSpear spear = ability.HeldSpear;
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint,
                slugcat.BodyChunks[0].Position + new Vec2(50.0, 0.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat,
                SlugcatVisualProfiles.Spearmaster, null);
            graphics.Step(attention, world);
            Near(graphics.Arms[0].End.Position.X, spear.Chunk.Position.X,
                0.000001, "held needle follows grasp zero x");
            Near(graphics.Arms[0].End.Position.Y, spear.Chunk.Position.Y,
                0.000001, "held needle follows grasp zero y");
            True(graphics.Arms[0].Mode == LimbMode.HuntRelativePosition,
                "held needle uses SlugcatHand relative hunt mode");
            slugcat.BodyChunks[0].Velocity = Vec2.Zero;
            slugcat.BodyChunks[1].Velocity = Vec2.Zero;
            IList<AbilityReplayTick> throwReplay = AbilityInputReplay.Run(slugcat, world,
                new[] { new VirtualInput(0, 0, false, false, true,
                    VirtualPosture.None, false) });
            True(spear.Mode == DesktopSpearMode.Thrown, "throw releases held grasp");
            Equal(5, ability.ThrowFollowTicks, "PlayerGraphics throw follow-through ticks");
            Near(8.0, slugcat.BodyChunks[0].Velocity.X, 0.000001,
                "throw adds eight units of chest recoil");
            Near(-4.0, slugcat.BodyChunks[1].Velocity.X, 0.000001,
                "throw subtracts four units from hips");
            True(spear.HasUmbilical && spear.Umbilical.Length >= 10 &&
                spear.Umbilical.Length <= 19,
                "connected needle creates Spear.Umbilical with 10..19 segments");
            True(ContainsSound(throwReplay[0].Sounds, "Slugcat_Throw_Spear"),
                "Spear.Thrown uses the original throw SoundID");
            Near(48.0, spear.Chunk.Velocity.X, 0.000001,
                "Spearmaster horizontal skill multiplies 40 by 1.2");
            Near(-1.05045, spear.Chunk.Velocity.Y, 0.00001,
                "horizontal spear inherits half player y then -1.5");
            double beforeY = spear.Chunk.Velocity.Y;
            double beforePositionY = spear.Chunk.Position.Y;
            IList<AbilityReplayTick> flightReplay = AbilityInputReplay.Run(slugcat,
                world, new[] { VirtualInput.Neutral });
            True(ContainsSound(flightReplay[0].Sounds,
                "Spear_Thrown_Through_Air_LOOP"),
                "thrown needle starts the original flight loop SoundID");
            Near((beforeY + 0.45) * 0.999, spear.Chunk.Velocity.Y, 0.00001,
                "Spear.Update cancels half of PhysicalObject .9 gravity");
            Near(beforePositionY + spear.Chunk.Velocity.Y, spear.Chunk.Position.Y,
                0.00001, "needle integrates the post-friction velocity");
            for (int i = 0; i < 5; i++) graphics.Step(attention, world);
            Equal(0, ability.ThrowFollowTicks,
                "throwing hand follows the released spear for exactly five graphics ticks");
        }

        private static void SpearmasterTailGrowthReplay()
        {
            DesktopCollisionWorld world = CreateAirWorld();
            Slugcat slugcat = CreateAirSlugcat(SlugcatId.SpearMaster);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, new Vec2(50.0, 0.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat,
                SlugcatVisualProfiles.Spearmaster, null);
            VirtualInput pickup = new VirtualInput(0, 0, false, true);
            for (int i = 0; i < 20; i++)
            {
                slugcat.Step(pickup, world, Vec2.Zero, Vec2.Zero);
                graphics.Step(attention, world);
            }

            SpearmasterAbilityController ability =
                (SpearmasterAbilityController)slugcat.AbilityController;
            SlugcatPose pose = graphics.BuildPose(1.0, attention, 1);
            ExtraGraphicsPartPose selected =
                pose.ExtraParts[ability.SpearRow * 3 + ability.SpearLine];
            ExtraGraphicsPartPose growing = pose.ExtraParts[15];
            True(growing.Visible && growing.Element.StartsWith("BioSpear",
                StringComparison.Ordinal), "tail growth uses a BioSpear atlas element");
            Near(selected.RenderPosition.X, growing.RenderPosition.X, 0.000001,
                "growing needle starts at selected speckle x");
            Near(selected.RenderPosition.Y, growing.RenderPosition.Y, 0.000001,
                "growing needle starts at selected speckle y");
            Near(-ability.SpearProgress * 0.5, growing.ScaleY, 0.000001,
                "BioSpear growth scale follows TailSpeckles spearProg");

            for (int i = 20; i < 79; i++)
            {
                slugcat.Step(pickup, world, Vec2.Zero, Vec2.Zero);
                graphics.Step(attention, world);
            }
            Vec2 tailMiddle = graphics.Tail.Segments[2].Position;
            slugcat.Step(pickup, world, Vec2.Zero, Vec2.Zero);
            int drips = 0;
            for (int i = 0; i < slugcat.AbilityEffects.Count; i++)
            {
                AbilityEffect effect = slugcat.AbilityEffects[i];
                if (effect.Kind != AbilityEffectKind.WaterDrip) continue;
                drips++;
                True(Vec2.Distance(tailMiddle, effect.Position) <= 1.500001,
                    "completion WaterDrip originates at PlayerGraphics tail[2]");
            }
            Equal(4, drips, "tail-middle WaterDrip count");
        }

        private static void SpearmasterNeutralGateReplay()
        {
            DesktopCollisionWorld world = CreateAirWorld();
            Slugcat slugcat = CreateAirSlugcat(SlugcatId.SpearMaster);
            List<VirtualInput> pulling = new List<VirtualInput>();
            for (int i = 0; i < 20; i++)
                pulling.Add(new VirtualInput(0, 0, false, true));
            AbilityInputReplay.Run(slugcat, world, pulling);
            SpearmasterAbilityController ability =
                (SpearmasterAbilityController)slugcat.AbilityController;
            double before = ability.SpearProgress;
            AbilityInputReplay.Run(slugcat, world,
                new[] { new VirtualInput(1, 0, false, true) });
            Near(before, ability.SpearProgress, 0.000001,
                "non-neutral input freezes progress while Pickup is held");
            AbilityInputReplay.Run(slugcat, world,
                new[] { VirtualInput.Neutral });
            True(ability.SpearProgress < before,
                "releasing Pickup retracts progress");
        }

        private static void SpearmasterAiActionStateReplay()
        {
            DesktopCollisionWorld world = CreateAirWorld();
            Slugcat slugcat = CreateAirSlugcat(SlugcatId.SpearMaster);
            DesktopPetAI ai = new DesktopPetAI(7319);
            MouseTracker mouse = new MouseTracker();
            HashSet<SpearmasterActionState> visited =
                new HashSet<SpearmasterActionState>();

            for (int tick = 0; tick < 360; tick++)
            {
                VirtualInput input = ai.Step(slugcat, world, mouse, null);
                visited.Add(ai.SpearmasterState);
                slugcat.Step(input, world, mouse.Position, mouse.Velocity);
                if (ai.SpearmasterState == SpearmasterActionState.HoldingSpear &&
                    ((SpearmasterAbilityController)slugcat.AbilityController).HeldSpear != null)
                    break;
            }
            SpearmasterAbilityController ability =
                (SpearmasterAbilityController)slugcat.AbilityController;
            True(ability.HeldSpear != null, "AI completed tail extraction");
            True(visited.Contains(SpearmasterActionState.Idle), "visited Idle");
            True(visited.Contains(SpearmasterActionState.Moving), "visited Moving");
            True(visited.Contains(SpearmasterActionState.PreparingSpear),
                "visited PreparingSpear");
            True(visited.Contains(SpearmasterActionState.PullingSpear),
                "visited PullingSpear");

            for (int tick = 0; tick < 150; tick++)
            {
                VirtualInput input = ai.Step(slugcat, world, mouse, null);
                slugcat.Step(input, world, mouse.Position, mouse.Velocity);
            }
            True(ability.HeldSpear != null,
                "targetless HoldingSpear does not fire on a modulo timer");
            True(ai.SpearmasterState == SpearmasterActionState.HoldingSpear,
                "targetless state remains HoldingSpear");

            slugcat.BodyChunks[0].Position = new Vec2(-85.0, 0.0);
            slugcat.BodyChunks[0].LastPosition = slugcat.BodyChunks[0].Position;
            slugcat.BodyChunks[0].Velocity = Vec2.Zero;
            slugcat.BodyChunks[1].Position = new Vec2(-85.0, 17.0);
            slugcat.BodyChunks[1].LastPosition = slugcat.BodyChunks[1].Position;
            slugcat.BodyChunks[1].Velocity = Vec2.Zero;
            MouseAttentionState mouseAttention = new MouseAttentionState();
            mouseAttention.Update(1.0, Vec2.Zero, true,
                slugcat.BodyChunks[0].Position);
            for (int tick = 0; tick < 160; tick++)
            {
                if (ability.HeldSpear != null)
                {
                    slugcat.BodyChunks[0].Position = new Vec2(-85.0, 0.0);
                    slugcat.BodyChunks[0].LastPosition = slugcat.BodyChunks[0].Position;
                    slugcat.BodyChunks[0].Velocity = Vec2.Zero;
                    slugcat.BodyChunks[1].Position = new Vec2(-85.0, 17.0);
                    slugcat.BodyChunks[1].LastPosition = slugcat.BodyChunks[1].Position;
                    slugcat.BodyChunks[1].Velocity = Vec2.Zero;
                }
                VirtualInput input = ai.Step(slugcat, world, mouse, mouseAttention);
                visited.Add(ai.SpearmasterState);
                slugcat.Step(input, world, mouse.Position, mouse.Velocity);
            }
            True(visited.Contains(SpearmasterActionState.HoldingSpear),
                "visited HoldingSpear");
            True(visited.Contains(SpearmasterActionState.Aiming), "visited Aiming");
            True(visited.Contains(SpearmasterActionState.Throwing), "visited Throwing");
            True(visited.Contains(SpearmasterActionState.Recovering),
                "visited Recovering");
        }

        private static void SpearBounceTransitionReplay()
        {
            MonitorInfo monitor = new MonitorInfo("SPEAR-WALL",
                new Rectangle(0, 0, 1200, 1000),
                new Rectangle(0, 0, 1200, 1000), true);
            DesktopCollisionWorld world = CreateWorld(monitor,
                new DesktopWindowSnapshot[0]);
            DesktopSurface rightWall = null;
            for (int i = 0; i < world.Surfaces.Count; i++)
                if (world.Surfaces[i].Kind == DesktopSurfaceKind.MonitorRightBoundary)
                    rightWall = world.Surfaces[i];
            True(rightWall != null, "monitor right wall exists");
            DesktopSpear spear = new DesktopSpear(new Vec2(
                rightWall.WallX - 300.0,
                (rightWall.Top + rightWall.Bottom) * 0.5), 1);
            spear.Throw(new Vec2(48.0, 0.0), Vec2.Right);
            int transitionCount = 0;
            int bounceSoundCount = 0;
            for (int tick = 0; tick < 80; tick++)
            {
                if (spear.Step(world)) transitionCount++;
                if (spear.LastImpactSound == "Spear_Bounce_Off_Wall")
                    bounceSoundCount++;
            }
            True(spear.Mode == DesktopSpearMode.Free ||
                spear.Mode == DesktopSpearMode.StuckInGround,
                "long-range failed stick becomes Weapon.Mode.Free");
            Equal(1, bounceSoundCount,
                "retained/repeated terrain contact cannot replay bounce sound");
            True(transitionCount <= 2,
                "only bounce and optional eventual ground-rest transitions emit sounds");
        }

        private static void RivuletMovementReplay()
        {
            DesktopCollisionWorld airWorld = CreateAirWorld();
            Slugcat rivulet = CreateAirSlugcat(SlugcatId.Rivulet);
            List<VirtualInput> right = new List<VirtualInput>();
            for (int i = 0; i < 8; i++) right.Add(new VirtualInput(1, 0, false, false));
            AbilityInputReplay.Run(rivulet, airWorld, right);
            Near(4.0, rivulet.BodyChunks[0].Velocity.X, 0.000001,
                "Rivulet does not receive invented air steering");
            Near(1.75, rivulet.SelectedSlugcat.Movement.RunSpeedFactor, 0.000001,
                "runSpeedFac");
            Near(1.8, rivulet.SelectedSlugcat.Movement.PoleClimbSpeedFactor, 0.000001,
                "poleClimbSpeedFac");
            Near(1.6, rivulet.SelectedSlugcat.Movement.CorridorClimbSpeedFactor, 0.000001,
                "corridorClimbSpeedFac");
            Near(0.95, rivulet.SelectedSlugcat.Movement.BodyWeightFactor, 0.000001,
                "bodyWeightFac");

            DesktopCollisionWorld floorWorld;
            Slugcat jumper = CreateFloorSlugcat(SlugcatId.Rivulet, out floorWorld);
            IList<AbilityReplayTick> jump = AbilityInputReplay.Run(jumper, floorWorld,
                new[] { new VirtualInput(0, 0, true, false) });
            Near(-6.0, jump[0].ChestVelocity.Y, 0.000001, "Rivulet standing chest jump");
            Near(-5.0, jump[0].HipsVelocity.Y, 0.000001, "Rivulet standing hips jump");
            Near(8.0, jumper.Movement.JumpBoost, 0.000001,
                "Rivulet Player.Jump uses the shared eight-tick boost");
        }

        private static void MovementMomentumTrajectories()
        {
            MonitorInfo monitor = new MonitorInfo("MOMENTUM",
                new Rectangle(0, 0, 5000, 5000),
                new Rectangle(0, 0, 5000, 5000), true);
            DesktopCollisionWorld world = CreateWorld(monitor,
                new DesktopWindowSnapshot[0]);
            Slugcat normal = LaunchBellySlideJump(SlugcatId.White,
                new Vec2(250.0, 700.0), world);
            Slugcat rivulet = LaunchBellySlideJump(SlugcatId.Rivulet,
                new Vec2(650.0, 700.0), world);
            Near(9.0, normal.BodyChunks[0].Velocity.X, 0.000001,
                "normal BellySlide RocketJump horizontal launch");
            Near(-8.5, normal.BodyChunks[0].Velocity.Y, 0.000001,
                "normal BellySlide RocketJump vertical launch");
            Near(18.0, rivulet.BodyChunks[0].Velocity.X, 0.000001,
                "Rivulet BellySlide RocketJump horizontal launch");
            Near(-10.0, rivulet.BodyChunks[0].Velocity.Y, 0.000001,
                "Rivulet BellySlide RocketJump vertical launch");
            Equal((int)AnimationIndex.RocketJump, (int)normal.State.Animation,
                "BellySlide jump enters RocketJump");

            Vec2 normalStart = normal.Center;
            Vec2 rivuletStart = rivulet.Center;
            double normalTop = normalStart.Y;
            double rivuletTop = rivuletStart.Y;
            for (int tick = 0; tick < 12; tick++)
            {
                normal.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
                rivulet.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
                normalTop = Math.Min(normalTop, normal.Center.Y);
                rivuletTop = Math.Min(rivuletTop, rivulet.Center.Y);
            }
            double normalDistance = normal.Center.X - normalStart.X;
            double rivuletDistance = rivulet.Center.X - rivuletStart.X;
            True(rivuletDistance > normalDistance * 1.7,
                "Rivulet launch preserves its larger horizontal distance");
            True(rivuletStart.Y - rivuletTop > normalStart.Y - normalTop,
                "Rivulet launch reaches the higher original arc");

            Slugcat wall = new Slugcat(new Vec2(1000.0, 700.0), SlugcatId.White);
            wall.BodyChunks[0].ContactRight = true;
            VirtualInput wallJump = new VirtualInput(0, 0, true, false);
            wallJump.ResolveEdges(VirtualInput.Neutral);
            wall.Movement.ApplyInput(wallJump, world);
            Near(-6.0, wall.BodyChunks[0].Velocity.X, 0.000001,
                "wall jump horizontal momentum");
            Near(-8.0, wall.BodyChunks[0].Velocity.Y, 0.000001,
                "wall jump vertical momentum");
        }

        private static Slugcat LaunchBellySlideJump(SlugcatId id, Vec2 at,
            DesktopCollisionWorld world)
        {
            Slugcat slugcat = new Slugcat(at, id);
            slugcat.State.Animation = AnimationIndex.BellySlide;
            slugcat.State.BodyMode = BodyModeIndex.Default;
            slugcat.State.RollDirection = 1;
            slugcat.State.RollCounter = 1;
            slugcat.State.Standing = false;
            VirtualInput jump = new VirtualInput(1, 0, true, false);
            jump.ResolveEdges(VirtualInput.Neutral);
            slugcat.Movement.ApplyInput(jump, world);
            return slugcat;
        }

        private static void SaintTongueReplay()
        {
            MonitorInfo monitor = new MonitorInfo("SAINT",
                new Rectangle(0, 0, 1200, 1200),
                new Rectangle(0, 0, 1200, 1200), true);
            DesktopWindowSnapshot platform = new DesktopWindowSnapshot
            {
                Handle = new IntPtr(8101),
                Bounds = new Rectangle(350, 400, 500, 120),
                Title = "Saint anchor",
                ClassName = "Replay"
            };
            DesktopCollisionWorld world = CreateWorld(monitor,
                new[] { platform });
            Slugcat saint = new Slugcat(new Vec2(272.0, 260.0), SlugcatId.Saint);
            saint.State.Grounded = false;
            IList<AbilityReplayTick> shoot = AbilityInputReplay.Run(saint, world,
                new[]
                {
                    new VirtualInput(0, -1, true, false),
                    VirtualInput.Neutral
                });
            SaintAbilityController ability = (SaintAbilityController)saint.AbilityController;
            True(shoot[0].AbilityState.StartsWith("tongue:ShootingOut"),
                "jump edge shoots through SaintTongueCheck");
            True(ability.Mode == SaintTongueMode.AttachedToTerrain,
                "second tongue update attaches to desktop terrain");
            True(ability.LastElasticityExcess <= 0.000001,
                "newly attached rope is slack and applies no anchor pull");
            True(ContainsSound(shoot[0].Sounds, "Tube_Worm_Shoot_Tongue"),
                "shoot sound tick");
            True(ContainsSound(shoot[1].Sounds, "Tube_Worm_Tongue_Hit_Terrain"),
                "terrain hit sound tick");
            True(ability.Rope.Length == 20, "PlayerGraphics uses twenty rope segments");

            IList<AbilityReplayTick> release = AbilityInputReplay.Run(saint, world,
                new[]
                {
                    VirtualInput.Neutral,
                    new VirtualInput(0, 0, true, false)
                });
            True(ability.LastElasticityTargetLength >=
                ability.LastElasticityRequestLength - 0.000001,
                "attached neutral tick keeps the original >=1.0 rope allowance");
            True(ability.Mode == SaintTongueMode.Retracting,
                "jump release preserves the original Retracting state for the frame");
            Near(-8.0, release[1].ChestVelocity.Y, 0.000001, "release chest velocity");
            Near(-7.0, release[1].HipsVelocity.Y, 0.000001, "release hips velocity");
            True(ContainsSound(release[1].Sounds, "Tube_Worm_Detach_Tongue_Terrain"),
                "detach sound");
            True(ContainsSound(release[1].Sounds, "Slugcat_Normal_Jump"),
                "release jump sound");
            saint.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            True(ability.Mode == SaintTongueMode.Retracted,
                "following Tongue.Update completes retraction");
        }

        private static void SoundSettingPersistenceAndGate()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "slugcat-settings-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                AppSettings settings = AppSettings.Load(path);
                True(settings.SoundEnabled, "missing settings file defaults Sound ON");
                settings.SoundEnabled = false;
                settings.Save();
                True(!AppSettings.Load(path).SoundEnabled,
                    "SoundEnabled survives reload");

                using (RainWorldAudioEngine audio = new RainWorldAudioEngine(null))
                {
                    audio.SetEnabled(false);
                    audio.Play(new SoundEvent("Slugcat_Normal_Jump", Vec2.Zero,
                        1.0, 1.0, 0), Vec2.Zero, 1, 100.0);
                    Equal("sound disabled", audio.LastEvent,
                        "disabled events are consumed without playback");
                    audio.SetEnabled(true);
                    audio.Play(new SoundEvent("Slugcat_Normal_Jump", Vec2.Zero,
                        1.0, 1.0, 0), Vec2.Zero, 2, 100.0);
                    True(audio.LastEvent.StartsWith("Slugcat_Normal_Jump"),
                        "re-enabled audio begins with the next event");
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void GourmandRollReplay()
        {
            DesktopCollisionWorld world;
            Slugcat gourmand = CreateFloorSlugcat(SlugcatId.Gourmand, out world);
            double floor = FindFloorY(world);
            gourmand.BodyChunks[1].Position = new Vec2(250.0, floor - 165.0);
            gourmand.BodyChunks[0].Position = gourmand.BodyChunks[1].Position -
                new Vec2(0.0, SimulationConstants.BodyConnectionDistance);
            gourmand.BodyChunks[0].LastPosition = gourmand.BodyChunks[0].Position;
            gourmand.BodyChunks[1].LastPosition = gourmand.BodyChunks[1].Position;
            gourmand.BodyChunks[0].Velocity = new Vec2(0.0, 15.0);
            gourmand.BodyChunks[1].Velocity = new Vec2(0.0, 15.0);
            gourmand.State.Grounded = false;
            List<VirtualInput> inputs = new List<VirtualInput>();
            for (int i = 0; i < 16; i++)
                inputs.Add(new VirtualInput(1, 1, false, false));
            AbilityInputReplay.Run(gourmand, world, inputs);
            GourmandAbilityController ability =
                (GourmandAbilityController)gourmand.AbilityController;
            True(ability.ConsistentDownDiagonal > 6,
                "downDiagonal history reaches the original gate");
            True(ability.Rolling || gourmand.State.Animation == AnimationIndex.Roll,
                "impact speed and allowRoll start Roll");
            True(ability.RollCounter > 0, "rollCounter advances only after rollDirection");
        }

        private static void GourmandExhaustionReplay()
        {
            DesktopCollisionWorld world = CreateAirWorld();
            Slugcat gourmand = CreateAirSlugcat(SlugcatId.Gourmand);
            gourmand.State.AerobicLevel = 0.95;
            AbilityInputReplay.Run(gourmand, world, new[] { VirtualInput.Neutral });
            GourmandAbilityController ability =
                (GourmandAbilityController)gourmand.AbilityController;
            True(ability.Exhausted, ".95 aerobicLevel sets gourmandExhausted");
            True(gourmand.State.SlowMovementStun >= 5,
                "exhaustion maps through the original 6-to-0 slow stun");
            double exhaustedLevel = gourmand.State.AerobicLevel;
            for (int i = 0; i < 500; i++)
                AbilityInputReplay.Run(gourmand, world, new[] { VirtualInput.Neutral });
            True(gourmand.State.AerobicLevel < exhaustedLevel,
                "idle exhausted denominator recovers aerobicLevel");
            True(!ability.Exhausted && gourmand.State.AerobicLevel < 0.4,
                "recovery below .4 clears gourmandExhausted");
            Equal(0, ability.Crafting.Recipes.Count,
                "missing desktop item types do not create fake recipes");
        }

        private static void LocalAbilitySoundCatalog()
        {
            RainWorldInstallation installation = new RainWorldLocator().Locate(null);
            if (installation == null) return;
            string path = Path.Combine(installation.RootPath, "RainWorld_Data",
                "StreamingAssets", "soundeffects", "sounds.txt");
            IDictionary<string, RainWorldSoundDefinition> catalog =
                RainWorldSoundCatalog.Load(path);
            RainWorldSoundDefinition roll = catalog["Slugcat_Roll_Init"];
            True(roll.PlayAll && roll.Clips.Length == 2,
                "Roll_Init keeps both PLAYALL clips");
            RainWorldSoundDefinition bomb = catalog["Bomb_Explode"];
            True(bomb.PlayAll && bomb.Clips.Length == 2,
                "Bomb_Explode keeps PLAYALL metadata");
            Near(0.8, bomb.Clips[0].MinimumPitch, 0.000001,
                "Bomb clip pitch comes from sounds.txt");
            RainWorldSoundDefinition fire = catalog["Fire_Spear_Explode"];
            Near(0.8, fire.Clips[0].MinimumVolume, 0.000001,
                "Fire spear clip volume comes from sounds.txt");
            Equal(4, catalog["Tube_Worm_Shoot_Tongue"].Clips.Length,
                "tongue shot retains all original variants");
            Equal(2, catalog["SM_Spear_Pull"].Clips.Length,
                "Spearmaster pull retains both variants");
            True(catalog.ContainsKey("UI_Slugcat_Stunned_Init"),
                "stun uses the active UI_Slugcat_Stunned_Init SoundID");
        }

        private static void LocalUnityFsAudioPlayback()
        {
            RainWorldInstallation installation = new RainWorldLocator().Locate(null);
            if (installation == null) return;
            using (RainWorldAudioEngine audio = new RainWorldAudioEngine(installation))
            {
                string resolved;
                int pcmBytes;
                string reason;
                True(audio.TryResolveAndDecodeForDiagnostics("jump2", out resolved,
                    out pcmBytes, out reason), "jump2 family decode: " + reason);
                True(resolved.StartsWith("jump2", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(resolved, "jump2", StringComparison.OrdinalIgnoreCase),
                    "unindexed sounds.txt name resolves to an installed indexed variant");
                True(pcmBytes > 44, "resolved FSB5 clip exposes PCM16 data");

                IDictionary<string, RainWorldSoundDefinition> catalog =
                    RainWorldSoundCatalog.Load(Path.Combine(installation.RootPath, "RainWorld_Data",
                        "StreamingAssets", "soundeffects", "sounds.txt"));
                List<string> failedEvents = new List<string>();
                int tick = 1;
                foreach (string id in catalog.Keys)
                {
                    if (id.StartsWith("Slugcat", StringComparison.OrdinalIgnoreCase) ||
                        id.StartsWith("Spear", StringComparison.OrdinalIgnoreCase) ||
                        id.StartsWith("SM_", StringComparison.OrdinalIgnoreCase) ||
                        id.StartsWith("UI_Slugcat", StringComparison.OrdinalIgnoreCase) ||
                        id.StartsWith("Tube_Worm", StringComparison.OrdinalIgnoreCase) ||
                        id.StartsWith("Rock_", StringComparison.OrdinalIgnoreCase) ||
                        id.StartsWith("Fly_", StringComparison.OrdinalIgnoreCase) ||
                        id.StartsWith("Bomb_", StringComparison.OrdinalIgnoreCase) ||
                        id.StartsWith("Fire_", StringComparison.OrdinalIgnoreCase))
                    {
                        string err = CheckInstalledEvent(audio, id, tick++);
                        if (err != null) failedEvents.Add(err);
                    }
                }
                if (failedEvents.Count > 0)
                {
                    throw new InvalidOperationException("Failed sounds (" + failedEvents.Count + "):\n" +
                        string.Join("\n", failedEvents.ToArray()));
                }
            }
        }

        private static void DeathSoundsAreSuppressed()
        {
            RainWorldInstallation installation = new RainWorldLocator().Locate(null);
            if (installation == null) return;
            using (RainWorldAudioEngine audio = new RainWorldAudioEngine(installation))
            {
                string[] ids =
                {
                    "Slugcat_Terrain_Impact_Death", "UI_Slugcat_Die",
                    "HUD_Game_Over_Prompt"
                };
                for (int i = 0; i < ids.Length; i++)
                {
                    audio.Play(new SoundEvent(ids[i], Vec2.Zero, 1.0, 1.0, 0),
                        Vec2.Zero, i + 1, 100.0);
                    True(audio.LastEvent == "suppressed death sound: " + ids[i],
                        ids[i] + " never enters audio playback");
                }
                audio.Play(new SoundEvent("Slugcat_Terrain_Impact_Hard", Vec2.Zero,
                    1.0, 1.0, 0), Vec2.Zero, 10, 100.0);
                True(audio.LastEvent == "suppressed high-impact sound: Slugcat_Terrain_Impact_Hard",
                    "desktop high-speed impact cannot bypass the bassOnly safety gate");
            }
        }

        private static string CheckInstalledEvent(RainWorldAudioEngine audio, string id,
            long tick)
        {
            Stopwatch enqueue = Stopwatch.StartNew();
            audio.Play(new SoundEvent(id, Vec2.Zero, 0.01, 1.0, 0),
                Vec2.Zero, tick, 100.0);
            enqueue.Stop();
            if (audio.LastEvent.StartsWith("suppressed death sound:",
                StringComparison.OrdinalIgnoreCase) ||
                audio.LastEvent.StartsWith("suppressed high-impact sound:",
                StringComparison.OrdinalIgnoreCase) ||
                audio.LastEvent.StartsWith("silent sound:",
                StringComparison.OrdinalIgnoreCase)) return null;
            Stopwatch deadline = Stopwatch.StartNew();
            while (deadline.ElapsedMilliseconds < 3000 &&
                !audio.LastEvent.StartsWith("playback started: " + id,
                    StringComparison.OrdinalIgnoreCase) &&
                !audio.LastEvent.StartsWith("playback failed",
                    StringComparison.OrdinalIgnoreCase) &&
                !audio.LastEvent.Contains("unavailable"))
                Thread.Sleep(10);
            if (!audio.LastEvent.StartsWith("playback started: " + id,
                StringComparison.OrdinalIgnoreCase))
                return id + " -> " + audio.LastEvent;
            return null;
        }

        private static DesktopCollisionWorld CreateAirWorld()
        {
            MonitorInfo monitor = new MonitorInfo("AIR",
                new Rectangle(-10000, -10000, 20000, 30000),
                new Rectangle(-10000, -10000, 20000, 30000), true);
            return CreateWorld(monitor, new DesktopWindowSnapshot[0]);
        }

        private static DesktopCollisionWorld CreateWorld(MonitorInfo monitor,
            IList<DesktopWindowSnapshot> windows)
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.RefreshFromSnapshots(windows, new[] { monitor });
            return world;
        }

        private static Slugcat CreateAirSlugcat(SlugcatId id)
        {
            Slugcat slugcat = new Slugcat(new Vec2(0.0, 17.0), id);
            slugcat.BodyChunks[0].Position = Vec2.Zero;
            slugcat.BodyChunks[0].LastPosition = Vec2.Zero;
            slugcat.BodyChunks[1].Position = new Vec2(0.0, 17.0);
            slugcat.BodyChunks[1].LastPosition = slugcat.BodyChunks[1].Position;
            slugcat.State.Grounded = false;
            slugcat.State.BodyMode = BodyModeIndex.Default;
            slugcat.State.Animation = AnimationIndex.None;
            return slugcat;
        }

        private static Slugcat CreateFloorSlugcat(SlugcatId id,
            out DesktopCollisionWorld world)
        {
            MonitorInfo monitor = new MonitorInfo("FLOOR",
                new Rectangle(0, 0, 1200, 1000),
                new Rectangle(0, 0, 1200, 1000), true);
            world = CreateWorld(monitor, new DesktopWindowSnapshot[0]);
            double floor = FindFloorY(world);
            Slugcat slugcat = new Slugcat(new Vec2(250.0,
                floor - SimulationConstants.HipsChunkRadius - 0.5), id);
            return slugcat;
        }

        private static double FindFloorY(DesktopCollisionWorld world)
        {
            for (int i = 0; i < world.Surfaces.Count; i++)
                if (world.Surfaces[i].Kind == DesktopSurfaceKind.MonitorFloor)
                    return world.Surfaces[i].Top;
            throw new InvalidOperationException("monitor floor missing");
        }

        private static bool ContainsSound(SoundEvent[] sounds, string id)
        {
            for (int i = 0; i < sounds.Length; i++)
                if (sounds[i].Id == id) return true;
            return false;
        }

        private static bool HasEffect(Slugcat slugcat, AbilityEffectKind kind)
        {
            for (int i = 0; i < slugcat.AbilityEffects.Count; i++)
                if (slugcat.AbilityEffects[i].Kind == kind) return true;
            return false;
        }

        private static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Equal(int expected, int actual, string message)
        {
            if (expected != actual)
                throw new InvalidOperationException(message + ": expected " +
                    expected + ", got " + actual);
        }

        private static void Equal(string expected, string actual, string message)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidOperationException(message + ": expected " +
                    expected + ", got " + actual);
        }

        private static void Near(double expected, double actual,
            double tolerance, string message)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException(message + ": expected " +
                    expected + ", got " + actual);
        }
    }
}
