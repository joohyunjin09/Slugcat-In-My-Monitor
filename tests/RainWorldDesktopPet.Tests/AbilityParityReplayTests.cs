using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using RainWorldDesktopPet.Audio;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
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
            run("Artificer replay matches explosive-jump chunk assignments",
                ArtificerExplosiveJumpReplay);
            run("Artificer down input follows the original parry counter branch",
                ArtificerParryReplay);
            run("Spearmaster extraction creates the needle on original progress tick",
                SpearmasterExtractionReplay);
            run("Spearmaster throw uses ThrowObject velocity and needle gravity",
                SpearmasterThrowReplay);
            run("Rivulet replay uses stats-driven ground jump and shared air control",
                RivuletMovementReplay);
            run("Saint replay shoots, attaches and jump-releases through Tongue states",
                SaintTongueReplay);
            run("Gourmand falling diagonal replay gates roll through original counters",
                GourmandRollReplay);
            run("Gourmand exhaustion uses aerobicLevel recovery and slowMovementStun",
                GourmandExhaustionReplay);
            run("Local sounds.txt maps ability SoundIDs and PLAYALL metadata",
                LocalAbilitySoundCatalog);
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

        private static void SpearmasterExtractionReplay()
        {
            DesktopCollisionWorld world = CreateAirWorld();
            Slugcat slugcat = CreateAirSlugcat(SlugcatId.Spearmaster);
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
            True(ContainsSound(replay[23].Sounds, "SM_Spear_Pull"),
                "pull sound starts at the first >=.1 branch");
            True(ContainsSound(replay[79].Sounds, "SM_Spear_Grab"),
                "grab sound matches creation tick");
            Equal(9, replay[79].EffectCount, "four drips plus five sparks");
        }

        private static void SpearmasterThrowReplay()
        {
            DesktopCollisionWorld world = CreateAirWorld();
            Slugcat slugcat = CreateAirSlugcat(SlugcatId.Spearmaster);
            List<VirtualInput> extraction = new List<VirtualInput>();
            for (int i = 0; i < 80; i++) extraction.Add(new VirtualInput(0, 0, false, true));
            AbilityInputReplay.Run(slugcat, world, extraction);
            SpearmasterAbilityController ability =
                (SpearmasterAbilityController)slugcat.AbilityController;
            DesktopSpear spear = ability.HeldSpear;
            slugcat.BodyChunks[0].Velocity = Vec2.Zero;
            slugcat.BodyChunks[1].Velocity = Vec2.Zero;
            AbilityInputReplay.Run(slugcat, world,
                new[] { new VirtualInput(0, 0, false, false, true,
                    VirtualPosture.None, false) });
            True(spear.Mode == DesktopSpearMode.Thrown, "throw releases held grasp");
            Near(48.0, spear.Chunk.Velocity.X, 0.000001,
                "Spearmaster horizontal skill multiplies 40 by 1.2");
            Near(-1.05045, spear.Chunk.Velocity.Y, 0.00001,
                "horizontal spear inherits half player y then -1.5");
            double beforeY = spear.Chunk.Velocity.Y;
            double beforePositionY = spear.Chunk.Position.Y;
            AbilityInputReplay.Run(slugcat, world, new[] { VirtualInput.Neutral });
            Near((beforeY + 1.35) * 0.999, spear.Chunk.Velocity.Y, 0.00001,
                "PhysicalObject .9 plus thrown Spear .45 gravity");
            Near(beforePositionY + spear.Chunk.Velocity.Y, spear.Chunk.Position.Y,
                0.00001, "needle integrates the post-friction velocity");
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
            True(ability.Mode == SaintTongueMode.Retracted,
                "jump release enters Retracting then Tongue.Update retracts on the same tick");
            Near(-8.0, release[1].ChestVelocity.Y, 0.000001, "release chest velocity");
            Near(-7.0, release[1].HipsVelocity.Y, 0.000001, "release hips velocity");
            True(ContainsSound(release[1].Sounds, "Tube_Worm_Detach_Tongue_Terrain"),
                "detach sound");
            True(ContainsSound(release[1].Sounds, "Slugcat_Normal_Jump"),
                "release jump sound");
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

        private static void Near(double expected, double actual,
            double tolerance, string message)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException(message + ": expected " +
                    expected + ", got " + actual);
        }
    }
}
