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
                    EffectCount = slugcat.AbilityEffects.Count
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
            run("Character switching clears previous ability objects",
                CharacterSwitchClearsAbilityState);
            run("Artificer replay matches explosive-jump chunk assignments",
                ArtificerExplosiveJumpReplay);
            run("Artificer down input follows the original parry counter branch",
                ArtificerParryReplay);
            run("Gourmand AI does not fabricate a fall-roll diagonal",
                GourmandAiDoesNotForceFallRoll);
            run("SlugNPC-inspired AI keeps per-slugcat personalities",
                AutonomousAiPersonalitiesDiffer);
            run("SlugNPC-inspired AI diversifies equivalent platform routes",
                AutonomousAiRoutesDiversify);
            run("Autonomous exploration chooses interior destinations before reversing",
                AutonomousExplorationAvoidsScreenEdgeOscillation);
            run("Artificer and Saint AI use traversal abilities on long routes",
                AutonomousSpecialistsUseTraversalAbilities);
            run("Artificer effects retain original smoke and light lifecycles",
                ArtificerEffectLifecycleReplay);
            run("Spearmaster extraction creates the needle on original progress tick",
                SpearmasterExtractionReplay);
            run("Spearmaster tail marker stays fixed while BioSpear emerges",
                SpearmasterTailGrowthReplay);
            run("Spearmaster neutral gate freezes creation while Pickup stays held",
                SpearmasterNeutralGateReplay);
            run("Spearmaster throw uses ThrowObject velocity and needle gravity",
                SpearmasterThrowReplay);
            run("Spearmaster umbilical endpoints follow scaled tail and spear",
                SpearmasterUmbilicalEndpointsFollowScaledVisuals);
            run("Spearmaster thrown spear fades then expires after fifteen seconds",
                SpearmasterThrownSpearExpiryReplay);
            run("Grounded free spear keeps the original diagonal resting spread",
                SpearGroundRestDirectionReplay);
            run("Thrown floor contact enters diagonal spear rest",
                ThrownSpearFloorRestReplay);
            run("Spearmaster AI holds without a target and traverses explicit action states",
                SpearmasterAiActionStateReplay);
            run("Spearmaster AI throws without mouse attention after its cooldown",
                SpearmasterAutonomousThrowReplay);
            run("Rivulet replay uses stats-driven ground jump and shared air control",
                RivuletMovementReplay);
            run("Movement launch momentum produces character-specific trajectories",
                MovementMomentumTrajectories);
            run("Saint replay shoots, attaches and jump-releases through Tongue states",
                SaintTongueReplay);
            run("Saint tongue reach follows the selected Slugcat size",
                SaintTongueReachScalesWithSize);
            run("Gourmand falling diagonal replay gates roll through original counters",
                GourmandRollReplay);
            run("Gourmand exhaustion uses aerobicLevel recovery and slowMovementStun",
                GourmandExhaustionReplay);
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
            artificer.SetSelectedSlugcat(SlugcatId.Yellow);
            Equal(0, artificer.AbilityEffects.Count, "effects after switch");
            Equal(0, artificer.Spears.Count, "spears after switch");
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
            Equal(19, replay[0].EffectCount, "light + eight smoke + ten sparks");
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
        }

        private static void GourmandAiDoesNotForceFallRoll()
        {
            DesktopCollisionWorld world = CreateAirWorld();
            Slugcat slugcat = CreateAirSlugcat(SlugcatId.Gourmand);
            slugcat.BodyChunks[0].Velocity.Y = 12.0;
            slugcat.BodyChunks[1].Velocity.Y = 12.0;
            DesktopPetAI ai = new DesktopPetAI(4401);
            MouseTracker mouse = new MouseTracker();
            VirtualInput input = ai.Step(slugcat, world, mouse, null);
            True(!(input.X != 0 && input.Y > 0),
                "falling alone does not synthesize Player.downDiagonal for Gourmand");
        }

        private static void AutonomousAiPersonalitiesDiffer()
        {
            DesktopPetAI first = new DesktopPetAI(1001, 1);
            DesktopPetAI second = new DesktopPetAI(1002, 2);
            True(Math.Abs(first.PersonalityEnergy - second.PersonalityEnergy) > 0.000001 ||
                Math.Abs(first.PersonalityNervous - second.PersonalityNervous) > 0.000001 ||
                Math.Abs(first.PersonalityAggression - second.PersonalityAggression) > 0.000001 ||
                Math.Abs(first.PersonalityBravery - second.PersonalityBravery) > 0.000001 ||
                Math.Abs(first.PersonalityDominance - second.PersonalityDominance) > 0.000001,
                "distinct SlugNPC seeds retain distinct behavior personalities");
            UtilityContext cautious = new UtilityContext
            {
                Grounded = true,
                TransitionAvailable = true,
                JumpReady = true,
                Curiosity = 0.75,
                PersonalityEnergy = 0.15,
                PersonalityBravery = 0.05,
                PersonalityDominance = 0.05
            };
            UtilityContext bold = new UtilityContext
            {
                Grounded = true,
                TransitionAvailable = true,
                JumpReady = true,
                Curiosity = 0.75,
                PersonalityEnergy = 0.95,
                PersonalityBravery = 0.95,
                PersonalityDominance = 0.95
            };
            True(UtilityEvaluator.Score(DesktopBehavior.Jump, bold, 0.0) >
                UtilityEvaluator.Score(DesktopBehavior.Jump, cautious, 0.0),
                "SlugNPC bravery and energy alter route-transition utility");

            UtilityContext exhausted = new UtilityContext
            {
                Grounded = true,
                Fatigue = 1.0,
                PersonalityEnergy = 0.1,
                RestReady = false
            };
            Near(0.0, UtilityEvaluator.Score(DesktopBehavior.Sleep, exhausted, 0.0),
                0.000001, "rest cooldown blocks repeated sleep selection");
            exhausted.RestReady = true;
            True(UtilityEvaluator.Score(DesktopBehavior.Sleep, exhausted, 0.0) > 0.0,
                "rest becomes available after its cooldown");
        }

        private static void AutonomousAiRoutesDiversify()
        {
            MonitorInfo monitor = new MonitorInfo("ROUTES",
                new Rectangle(0, 0, 1200, 1000),
                new Rectangle(0, 0, 1200, 1000), true);
            DesktopWindowSnapshot[] windows =
            {
                new DesktopWindowSnapshot
                {
                    Handle = new IntPtr(7201),
                    Bounds = new Rectangle(280, 840, 230, 80),
                    Title = "Left route", ClassName = "Replay"
                },
                new DesktopWindowSnapshot
                {
                    Handle = new IntPtr(7202),
                    Bounds = new Rectangle(690, 840, 230, 80),
                    Title = "Right route", ClassName = "Replay"
                }
            };
            DesktopCollisionWorld world = CreateWorld(monitor, windows);
            double floorY = FindFloorY(world);
            DesktopSurface floor = null;
            for (int i = 0; i < world.Surfaces.Count; i++)
            {
                if (!world.Surfaces[i].IsHorizontal ||
                    Math.Abs(world.Surfaces[i].Top - floorY) > 0.000001) continue;
                floor = world.Surfaces[i];
                break;
            }
            True(floor != null, "route replay has a source floor");
            HashSet<long> selectedTargets = new HashSet<long>();
            for (int seed = 1001; seed <= 1008; seed++)
            {
                Slugcat slugcat = new Slugcat(new Vec2(
                    DesktopWorldTransform.ToSimulationLength(600.0),
                    floorY - SimulationConstants.HipsChunkRadius - 0.5), SlugcatId.White);
                slugcat.BodyChunks[1].SupportingSurfaceId = floor.Id;
                slugcat.BodyChunks[1].SupportingSurfaceKind = floor.Kind;
                DesktopPetAI ai = new DesktopPetAI(seed, seed - 1000);
                PlatformTransitionPlan plan = ai.PlanPlatformTransition(slugcat, world);
                True(plan.IsValid, "each route replay AI finds a viable platform");
                selectedTargets.Add(plan.TargetSurfaceId);
            }
            True(selectedTargets.Count >= 2,
                "stable personality preferences split equivalent platform routes");
        }

        private static void AutonomousExplorationAvoidsScreenEdgeOscillation()
        {
            MonitorInfo monitor = new MonitorInfo("EXPLORE",
                new Rectangle(0, 0, 1200, 1000),
                new Rectangle(0, 0, 1200, 1000), true);
            DesktopCollisionWorld world = CreateWorld(monitor,
                new DesktopWindowSnapshot[0]);
            double floorY = FindFloorY(world);
            DesktopSurface floor = null;
            for (int i = 0; i < world.Surfaces.Count; i++)
            {
                DesktopSurface candidate = world.Surfaces[i];
                if (candidate.Kind == DesktopSurfaceKind.MonitorFloor)
                {
                    floor = candidate;
                    break;
                }
            }
            True(floor != null, "exploration replay has a floor");

            Slugcat slugcat = new Slugcat(new Vec2(
                (floor.Left + floor.Right) * 0.5,
                floorY - SimulationConstants.HipsChunkRadius - 0.5), SlugcatId.White);
            slugcat.State.Grounded = true;
            slugcat.BodyChunks[1].SupportingSurfaceId = floor.Id;
            slugcat.BodyChunks[1].SupportingSurfaceKind = floor.Kind;
            DesktopPetAI ai = new DesktopPetAI(5817);
            MouseTracker mouse = new MouseTracker();
            int previousDirection = 0;
            int interiorTurns = 0;

            for (int tick = 0; tick < 800; tick++)
            {
                Vec2 before = slugcat.Center;
                VirtualInput input = ai.Step(slugcat, world, mouse, null);
                bool exploring = ai.Behavior == DesktopBehavior.Walk ||
                    ai.Behavior == DesktopBehavior.Explore;
                if (!exploring || input.X == 0)
                {
                    previousDirection = 0;
                }
                else
                {
                    if (previousDirection != 0 && input.X != previousDirection)
                    {
                        double nearestEdge = Math.Min(before.X - floor.Left,
                            floor.Right - before.X);
                        True(nearestEdge > 34.0,
                            "exploration changes direction at an interior destination, not a screen edge");
                        interiorTurns++;
                    }
                    previousDirection = input.X;
                }
                slugcat.Step(input, world, mouse.Position, mouse.Velocity);
            }
            True(interiorTurns > 0,
                "seeded exploration reaches at least one independently chosen interior destination");
        }

        private static void AutonomousSpecialistsUseTraversalAbilities()
        {
            MonitorInfo monitor = new MonitorInfo("SPECIALISTS",
                new Rectangle(0, 0, 1200, 1000),
                new Rectangle(0, 0, 1200, 1000), true);
            DesktopWindowSnapshot platform = new DesktopWindowSnapshot
            {
                Handle = new IntPtr(8341),
                Bounds = new Rectangle(690, 840, 230, 80),
                Title = "Traversal target",
                ClassName = "Replay"
            };
            DesktopCollisionWorld world = CreateWorld(monitor,
                new[] { platform });
            double floorY = FindFloorY(world);
            DesktopSurface floor = null;
            for (int i = 0; i < world.Surfaces.Count; i++)
            {
                if (world.Surfaces[i].Kind == DesktopSurfaceKind.MonitorFloor)
                {
                    floor = world.Surfaces[i];
                    break;
                }
            }
            True(floor != null, "specialist replay has a source floor");

            Slugcat artificer = CreateTraversalSlugcat(SlugcatId.Artificer,
                floor, floorY);
            DesktopPetAI artificerAi = new DesktopPetAI(6021);
            MouseTracker mouse = new MouseTracker();
            bool artificerUsedAbility = false;
            for (int tick = 0; tick < 180; tick++)
            {
                VirtualInput input = artificerAi.Step(artificer, world, mouse, null);
                artificer.Step(input, world, mouse.Position, mouse.Velocity);
                ArtificerAbilityController ability =
                    (ArtificerAbilityController)artificer.AbilityController;
                if (ability.ExplosiveJumpCounter > 0)
                {
                    artificerUsedAbility = true;
                    break;
                }
            }
            True(artificerUsedAbility,
                "Artificer AI uses the original explosive jump for a long route");

            Slugcat saint = CreateTraversalSlugcat(SlugcatId.Saint, floor, floorY);
            DesktopPetAI saintAi = new DesktopPetAI(6021);
            bool saintUsedAbility = false;
            for (int tick = 0; tick < 180; tick++)
            {
                VirtualInput input = saintAi.Step(saint, world, mouse, null);
                saint.Step(input, world, mouse.Position, mouse.Velocity);
                SaintAbilityController ability =
                    (SaintAbilityController)saint.AbilityController;
                if (ability.Mode != SaintTongueMode.Retracted)
                {
                    saintUsedAbility = true;
                    break;
                }
            }
            True(saintUsedAbility,
                "Saint AI shoots the original tongue for a long route");
        }

        private static Slugcat CreateTraversalSlugcat(SlugcatId id,
            DesktopSurface floor, double floorY)
        {
            Slugcat slugcat = new Slugcat(new Vec2(
                DesktopWorldTransform.ToSimulationLength(600.0),
                floorY - SimulationConstants.HipsChunkRadius - 0.5), id);
            slugcat.State.Grounded = true;
            for (int i = 0; i < slugcat.BodyChunks.Length; i++)
                slugcat.BodyChunks[i].ContactFloor = true;
            slugcat.BodyChunks[1].SupportingSurfaceId = floor.Id;
            slugcat.BodyChunks[1].SupportingSurfaceKind = floor.Kind;
            return slugcat;
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

            bool foundZeroLifetimeSpark = false;
            Random sparkRandom = new Random(923);
            for (int i = 0; i < 64; i++)
            {
                AbilityEffect spark = AbilityEffect.CreateSpark(Vec2.Zero, Vec2.Right,
                    4, 18, sparkRandom);
                if (spark.InitialLifetime != 0) continue;
                foundZeroLifetimeSpark = true;
                spark.Step();
                True(!spark.IsAlive,
                    "zero-lifetime original Spark expires on its first update");
                break;
            }
            True(foundZeroLifetimeSpark,
                "original Random.Range(0, 4) branch produces a zero-lifetime Spark");

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
            AbilitySoundSink sounds = new AbilitySoundSink();
            slugcat.SetAudioSink(sounds, "spearmaster-extraction");
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
            Equal(9, replay[79].EffectCount, "four drips plus five sparks");
            Equal(2, sounds.Events.Count,
                "extraction emits exactly one pull and one completion sound");
            Equal("SM_Spear_Pull", sounds.Events[0].Id,
                "pull sound starts after spear progress crosses .1");
            Equal("SM_Spear_Grab", sounds.Events[1].Id,
                "grab sound plays when the needle is fully extracted");
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
            Color umbilicalStart = SpriteRenderer.ResolveOriginalUmbilicalColor(
                0, spear.Umbilical.Length, 1.0, 1.0);
            Color umbilicalEnd = SpriteRenderer.ResolveOriginalUmbilicalColor(
                spear.Umbilical.Length - 1, spear.Umbilical.Length, 1.0, 1.0);
            True(umbilicalStart.R > umbilicalStart.G &&
                umbilicalEnd.G > umbilicalStart.G,
                "Spear.Umbilical uses the original red-to-thread palette gradient");
            Near(48.0, spear.Chunk.Velocity.X, 0.000001,
                "Spearmaster horizontal skill multiplies 40 by 1.2");
            Near(-1.05045, spear.Chunk.Velocity.Y, 0.00001,
                "horizontal spear inherits half player y then -1.5");
            double beforeY = spear.Chunk.Velocity.Y;
            double beforePositionY = spear.Chunk.Position.Y;
            IList<AbilityReplayTick> flightReplay = AbilityInputReplay.Run(slugcat,
                world, new[] { VirtualInput.Neutral });
            Near((beforeY + 0.45) * 0.999, spear.Chunk.Velocity.Y, 0.00001,
                "Spear.Update cancels half of PhysicalObject .9 gravity");
            Near(beforePositionY + spear.Chunk.Velocity.Y, spear.Chunk.Position.Y,
                0.00001, "needle integrates the post-friction velocity");
            spear.DisconnectNeedle();
            True(!spear.NeedleHasConnection && spear.HasUmbilical,
                "Spear_NeedleDisconnect leaves the original breaking umbilical alive");
            for (int tick = 0; tick < 8; tick++)
                slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            True(spear.HasUmbilical,
                "umbilical survives several frames after the needle disconnects");
            for (int tick = 0; tick < 430; tick++)
                slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            True(!spear.HasUmbilical,
                "all original 150..200 tick umbilical segments eventually expire");
            for (int i = 0; i < 5; i++) graphics.Step(attention, world);
            Equal(0, ability.ThrowFollowTicks,
                "throwing hand follows the released spear for exactly five graphics ticks");
        }

        private static void SpearmasterThrownSpearExpiryReplay()
        {
            DesktopCollisionWorld world = CreateAirWorld();
            Slugcat slugcat = CreateAirSlugcat(SlugcatId.SpearMaster);
            List<VirtualInput> extraction = new List<VirtualInput>();
            for (int i = 0; i < 80; i++)
                extraction.Add(new VirtualInput(0, 0, false, true));
            AbilityInputReplay.Run(slugcat, world, extraction);
            SpearmasterAbilityController ability =
                (SpearmasterAbilityController)slugcat.AbilityController;
            DesktopSpear spear = ability.HeldSpear;
            slugcat.Step(new VirtualInput(0, 0, false, false, true,
                VirtualPosture.None, false), world, Vec2.Zero, Vec2.Zero);
            Equal(600, spear.DespawnAfterTicks,
                "Spearmaster throw schedules the fifteen-second lifespan");

            for (int tick = 0; tick < 580; tick++)
                slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            Near(1.0, spear.Opacity, 0.000001,
                "needle remains opaque before its final half-second fade");
            for (int tick = 0; tick < 19; tick++)
                slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            True(spear.Opacity > 0.0 && spear.Opacity < 1.0,
                "needle fades naturally during its final half second");
            True(slugcat.Spears.Contains(spear),
                "needle exists until the full fifteen-second lifespan elapses");
            slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            True(!slugcat.Spears.Contains(spear),
                "needle is removed on its fifteen-second expiry tick");
        }

        private static void SpearGroundRestDirectionReplay()
        {
            Vec2 upwardLean = DesktopSpear.CalculateOriginalGroundRestDirection(0.0);
            Vec2 downwardLean = DesktopSpear.CalculateOriginalGroundRestDirection(1.0);
            True(upwardLean.X > 0.6 && upwardLean.Y > 0.6,
                "original -50 degree ground rest points diagonally into the floor");
            True(downwardLean.X < -0.6 && downwardLean.Y > 0.6,
                "original +50 degree ground rest keeps the opposite floor-facing diagonal");
        }

        private static void ThrownSpearFloorRestReplay()
        {
            DesktopCollisionWorld world;
            CreateFloorSlugcat(SlugcatId.White, out world);
            double floor = FindFloorY(world);
            DesktopSpear spear = new DesktopSpear(new Vec2(250.0, floor - 6.0));
            spear.Throw(new Vec2(20.0, 5.0), Vec2.Right);
            spear.Step(world);

            Equal((int)DesktopSpearMode.Free, (int)spear.Mode,
                "unaligned thrown floor contact remains in original Free mode");
            True(Math.Abs(spear.Rotation.Y) > 0.1,
                "ground-rest spear cannot retain a horizontal airborne rotation");
            Vec2 restDirection = spear.Rotation;
            spear.Step(world);
            Near(restDirection.X, spear.LastRotation.X, 0.000001,
                "stationary free spear snapshots its settled rotation");
            Near(restDirection.Y, spear.LastRotation.Y, 0.000001,
                "settled rotation no longer interpolates from the airborne spin");
            Near(restDirection.X, spear.Rotation.X, 0.000001,
                "free ground-rest direction remains stable");
            Near(restDirection.Y, spear.Rotation.Y, 0.000001,
                "free ground-rest direction remains stable");
            True(!spear.IsSpinning, "ground-rest Free spear clears spinning state");
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
                "white BioSpear retains TailSpeckles spearProg movement");
            Near(1.0, selected.ScaleY, 0.000001,
                "selected tinyStar retains its base scale during extraction");

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

        private static void SpearmasterAutonomousThrowReplay()
        {
            DesktopCollisionWorld world;
            Slugcat slugcat = CreateFloorSlugcat(SlugcatId.SpearMaster, out world);
            DesktopPetAI ai = new DesktopPetAI(7319);
            MouseTracker mouse = new MouseTracker();
            SpearmasterAbilityController ability =
                (SpearmasterAbilityController)slugcat.AbilityController;
            bool threwWithoutMouseAttention = false;

            for (int tick = 0; tick < 900; tick++)
            {
                VirtualInput input = ai.Step(slugcat, world, mouse, null);
                if (input.Throw)
                {
                    True(!ai.MouseAttentionActive,
                        "autonomous throw does not require mouse attention");
                    threwWithoutMouseAttention = true;
                }
                slugcat.Step(input, world, mouse.Position, mouse.Velocity);
                if (threwWithoutMouseAttention && ability.ThrownSpear != null)
                    break;
            }

            True(threwWithoutMouseAttention,
                "targetless Spearmaster emits a throw after its own cooldown");
            True(ability.ThrownSpear != null,
                "autonomous throw releases the held needle");
        }

        private static void SpearmasterUmbilicalEndpointsFollowScaledVisuals()
        {
            double size = SlugcatSizeSettings.SmallMultiplier;
            DesktopCollisionWorld world = CreateAirWorld();
            DesktopSpear spear = new DesktopSpear(new Vec2(120.0, 90.0), 0);
            Vec2 tailAnchor = new Vec2(80.0, 105.0);
            spear.SetConnectionAnchor(tailAnchor);
            spear.SetConnectionScale(size);
            spear.Throw(new Vec2(12.0, 0.0), Vec2.Right);
            spear.Step(world);
            Vec2 movedTailAnchor = tailAnchor + new Vec2(6.0, -4.0);
            spear.SetConnectionAnchor(movedTailAnchor);
            spear.SetConnectionScale(size);
            True(spear.HasUmbilical, "thrown needle creates an umbilical");
            Near(0.0, Vec2.Distance(movedTailAnchor, spear.Umbilical[0]),
                0.000001, "tail endpoint follows the current tail hole");
            int last = spear.Umbilical.Length - 1;
            Vec2 expectedPhysicalSpearEnd = spear.Chunk.Position -
                spear.Rotation * (25.0 * size);
            Near(0.0, Vec2.Distance(expectedPhysicalSpearEnd,
                spear.Umbilical[last]), 0.000001,
                "physical spear endpoint follows scaled spear length");

            SlugcatPose pose = new SlugcatPose();
            pose.CharacterOrigin = new Vec2(100.0, 100.0);
            pose.CharacterRenderScale = SimulationConstants.CharacterRenderScale * size;
            pose.Tail = new Vec2[]
            {
                new Vec2(96.0, 103.0), new Vec2(93.0, 107.0),
                new Vec2(89.0, 111.0), new Vec2(85.0, 114.0)
            };
            Vec2 spearWorldCenter = new Vec2(180.0, 110.0);
            Vec2 renderCenter = pose.ToCharacterRenderSpaceForWorld(spearWorldCenter);
            Vec2 previous = new Vec2(90.0, 108.0);
            Vec2 next = new Vec2(120.0, 109.0);
            SpriteRenderer.ResolveUmbilicalRenderEndpoints(pose, renderCenter,
                Vec2.Right, 1, 4, ref previous, ref next);
            Near(0.0, Vec2.Distance(pose.ToRenderedWorld(pose.Tail[2]),
                pose.ToRenderedWorld(previous)), 0.000001,
                "rendered tail endpoint is welded to scaled tail hole");

            previous = new Vec2(145.0, 110.0);
            next = new Vec2(170.0, 110.0);
            SpriteRenderer.ResolveUmbilicalRenderEndpoints(pose, renderCenter,
                Vec2.Right, 3, 4, ref previous, ref next);
            Vec2 renderedSpearCenter = pose.ToRenderedStaticWorld(spearWorldCenter);
            Vec2 expectedRenderedSpearEnd = renderedSpearCenter -
                Vec2.Right * (25.0 * pose.CharacterRenderScale);
            Near(0.0, Vec2.Distance(expectedRenderedSpearEnd,
                pose.ToRenderedWorld(next)), 0.000001,
                "rendered spear endpoint is welded to scaled spear sprite");
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
            True(ability.Rope.Length == 20, "PlayerGraphics uses twenty rope segments");
            saint.BodyChunks[0].ContactRight = true;
            True(!ability.CanJumpReleaseAttachedTongue,
                "wall contact blocks attached-tongue jump release until fully airborne");
            saint.BodyChunks[0].ContactRight = false;
            saint.State.Grounded = false;
            saint.State.CanJump = 0;
            saint.State.BodyMode = BodyModeIndex.Default;
            True(ability.CanJumpReleaseAttachedTongue,
                "fully airborne attached Saint accepts a fresh jump release");

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
            saint.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            True(ability.Mode == SaintTongueMode.Retracted,
                "following Tongue.Update completes retraction");
        }

        private static void SaintTongueReachScalesWithSize()
        {
            double[] scales =
            {
                SlugcatSizeSettings.SmallMultiplier,
                SlugcatSizeSettings.NormalMultiplier,
                SlugcatSizeSettings.LargeMultiplier
            };
            double[] normalizedReach = new double[scales.Length];
            for (int index = 0; index < scales.Length; index++)
            {
                DesktopCollisionWorld world = CreateAirWorld();
                Slugcat saint = CreateAirSlugcat(SlugcatId.Saint);
                saint.SetSizeScale(scales[index]);
                saint.State.Grounded = false;
                SaintAbilityController ability =
                    (SaintAbilityController)saint.AbilityController;
                Near(200.0 * scales[index], ability.MaximumTongueLength,
                    0.000001, "maximum tongue length " + index);
                saint.Step(new VirtualInput(0, -1, true, false), world,
                    Vec2.Zero, Vec2.Zero);
                Near(140.0 * scales[index], ability.RequestedRopeLength,
                    0.000001, "initial requested rope length " + index);
                double maximum = Vec2.Distance(saint.BodyChunks[0].Position,
                    ability.TonguePosition);
                for (int tick = 0; tick < 5; tick++)
                {
                    saint.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
                    maximum = Math.Max(maximum, Vec2.Distance(
                        saint.BodyChunks[0].Position, ability.TonguePosition));
                }
                normalizedReach[index] = maximum / scales[index];
            }
            True(normalizedReach[0] > 0.0, "Small tongue produces positive reach");
            Near(normalizedReach[2], normalizedReach[0], 5.0,
                "Small tongue follows Large spatial ratio");
            True(normalizedReach[1] > 0.0, "Normal tongue produces positive reach");
            Near(normalizedReach[2], normalizedReach[1], 5.0,
                "Normal tongue follows Large spatial ratio");
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

        private static bool HasEffect(Slugcat slugcat, AbilityEffectKind kind)
        {
            for (int i = 0; i < slugcat.AbilityEffects.Count; i++)
                if (slugcat.AbilityEffects[i].Kind == kind) return true;
            return false;
        }

        private sealed class AbilitySoundSink : ISoundEventSink
        {
            internal readonly List<SoundEvent> Events = new List<SoundEvent>();
            public string Status { get { return "test"; } }
            public void Play(SoundEvent sound) { Events.Add(sound); }
            public void StartLoop(SoundEvent sound, string loopKey) { }
            public void StopLoop(string sourceId, string loopKey) { }
            public void StopSource(string sourceId) { }
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
