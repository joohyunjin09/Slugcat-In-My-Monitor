using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using RainWorldDesktopPet.AI;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Physics;
using RainWorldDesktopPet.RainWorld;
using RainWorldDesktopPet.Graphics;

namespace RainWorldDesktopPet.Tests
{
    internal static class Program
    {
        private static int failures;

        private static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--preview")
            {
                RenderPreview(args[1], args.Length >= 3 ? ReadVariant(args[2]) : SlugcatVariant.Survivor,
                    args.Length >= 4 ? args[3] : "walk");
                return 0;
            }

            Run("FixedTimeStep uses 40 Hz independently of render rate", FixedStepUsesFortyHertz);
            Run("BodyChunkConnection projects to its target distance", ConnectionProjectsDistance);
            Run("Desktop floor collision prevents tunneling", DesktopFloorCollision);
            Run("AI produces VirtualInput without moving physics directly", AiDoesNotMoveCreature);
            Run("Futile atlas metadata parses frame geometry", AtlasMetadataParses);
            Run("Rain World locator validates an explicit installation", LocatorValidatesExplicitPath);
            Run("Required autonomous behavior states are present", RequiredBehaviorsExist);
            Run("Jump and DropDown utility states are reachable", UtilityActionsAreReachable);
            Run("Wall contact reaches ClimbWindow through VirtualInput", WallContactReachesClimbMovement);
            Run("WallClimb hands use alternating wall targets", WallClimbHandsTargetTheWall);
            Run("Sleep curl pulls both hands to the original target", SleepCurlHandsShareOriginalTarget);
            Run("Moving window walls carry a climbing Slugcat", MovingWindowWallCarriesClimber);
            Run("Occluded windows do not create hidden surfaces", OccludedWindowsAreClipped);
            Run("Monitor-ceiling window tops cannot hide the Slugcat", MonitorCeilingWindowTopIsRejected);
            Run("PlayerGraphics face frame uses the body-head axis", OriginalFaceFrameSelection);
            Run("Original slugcat variants match local DLL constants", OriginalVariantValues);
            Run("PlayerGraphics tail uses the original four-segment layout", OriginalTailLayout);
            Run("Sit and sleep flow through VirtualInput into movement", RestPosturesUseVirtualInput);
            Run("Original Stand forces keep the upper body upright", StandForcesKeepUpperBodyUpright);
            Run("Idle and rest poses do not cycle walking frames", IdleAndRestFramesStayStill);
            Run("Jump launch is not overwritten by Stand forces", JumpLaunchClearsGroundedForces);
            Run("DropDown requests window-surface pass-through", DropDownRequestsSurfacePassThrough);
            Run("GenericBodyPart uses original ConnectToPoint equation", OriginalConnectToPointEquation);
            Run("All graphics parts share one timeStacker", SharedGraphicsInterpolation);
            Run("Futile trim and anchor restore sprite-local coordinates", FutileTrimAnchorCoordinates);
            Run("Negative virtual-desktop coordinates convert once", NegativeVirtualDesktopCoordinates);
            Run("240 Hz rendering preserves the 40 Hz simulation count", TwoFortyHertzRenderCadence);
            Run("Ten-second idle/walk/turn/jump graphics stay connected", LongGraphicsScenarioStaysConnected);
            Run("Graphics bounds include procedural extremities", GraphicsBoundsIncludeExtremities);
            Run("Unused Stand and Walk hands retract like SlugcatHand", UnusedHandsRetract);
            Run("Crawl hands use original velocity-relative targets", CrawlHandsUseOriginalTargets);
            Run("SlugcatHand connection constraint prevents arm separation", ArmConstraintPreventsSeparation);
            Run("Crawl face follows persistent body facing, not attention", CrawlFaceUsesBodyFacing);
            Run("Arm shoulders rotate from the interpolated body axis", ArmShouldersFollowBodyAxis);
            Run("CharacterRenderScale uniformly enlarges visual coordinates", UniformCharacterRenderScale);
            Run("Expanded arm/leg/face debug overlay renders without mutation", ExpandedDebugOverlayRenders);

            RainWorldInstallation localInstallation = new RainWorldLocator().Locate(null);
            if (localInstallation == null)
                Console.WriteLine("SKIP  Local embedded original atlas (Rain World installation not found)");
            else
            Run("Local embedded original atlas loads without DMS", delegate { EmbeddedOriginalAtlasLoads(localInstallation); });

            Console.WriteLine(failures == 0
                ? "All RainWorldDesktopPet tests passed."
                : failures + " RainWorldDesktopPet test(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        private static void RenderPreview(string outputPath, SlugcatVariant variant, string scenario)
        {
            RainWorldInstallation installation = new RainWorldLocator().Locate(null);
            if (installation == null) throw new InvalidOperationException("Rain World installation was not found.");
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Slugcat slugcat = new Slugcat(new Vec2(work.Left + work.Width * 0.5, work.Bottom - 9.0), variant);
            DesktopPetAI ai = new DesktopPetAI(22);
            double attentionX = scenario == "crawl-right" ? -120.0 : 120.0;
            ai.Attention.SetTarget(AttentionKind.Mouse, slugcat.Center + new Vec2(attentionX, -55.0));
            SlugcatGraphics proceduralGraphics = new SlugcatGraphics(slugcat);
            for (int i = 0; i < 90; i++)
            {
                VirtualInput input;
                if (scenario == "idle") input = VirtualInput.Neutral;
                else if (scenario == "crawl-right") input = new VirtualInput(1, 1, false, false);
                else if (scenario == "crawl-left") input = new VirtualInput(-1, 1, false, false);
                else if (scenario == "jump") input = new VirtualInput(1, 0, i >= 35 && i < 44, false);
                else input = i > 20 && i < 68 ? new VirtualInput(1, 0, i == 45, false) : VirtualInput.Neutral;
                slugcat.Step(input, world, ai.Attention.Target, Vec2.Zero);
                ai.Attention.Step();
                proceduralGraphics.Step(ai.Attention, world);
            }

            RainWorldAssetLoader loader = new RainWorldAssetLoader(installation);
            RainWorldAtlasSet set = loader.TryLoadLoosePlayerAtlas();
            using (SpriteRenderer renderer = new SpriteRenderer(set))
            using (Bitmap bitmap = new Bitmap(560, 420, PixelFormat.Format32bppPArgb))
            using (System.Drawing.Graphics drawing = System.Drawing.Graphics.FromImage(bitmap))
            {
                drawing.Clear(Color.Transparent);
                SlugcatPose pose = proceduralGraphics.BuildPose(1.0, ai.Attention);
                Vec2 origin = (pose.Chest + pose.Hips) * 0.5 - new Vec2(280.0, 220.0);
                renderer.Render(drawing, pose, origin, false, world, slugcat, ai, loader.Status, slugcat.Appearance);
                string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                bitmap.Save(outputPath, ImageFormat.Png);
            }
            Console.WriteLine("Preview written to " + Path.GetFullPath(outputPath));
            Console.WriteLine(loader.Status);
            Console.WriteLine("Scenario " + scenario);
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS  " + name);
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine("FAIL  " + name);
                Console.WriteLine("      " + exception.Message);
            }
        }

        private static void FixedStepUsesFortyHertz()
        {
            FixedTimeStep step = new FixedTimeStep(SimulationConstants.LogicStepSeconds);
            step.AddElapsed(0.1);
            int count = 0;
            while (step.ConsumeStep()) count++;
            Equal(4, count, "0.1 seconds must contain four 40 Hz ticks");
        }

        private static void ConnectionProjectsDistance()
        {
            BodyChunk first = new BodyChunk(0, new Vec2(0.0, 0.0), 9.0, 0.7);
            BodyChunk second = new BodyChunk(1, new Vec2(100.0, 0.0), 8.0, 0.3);
            BodyChunkConnection connection = new BodyChunkConnection(first, second, 17.0,
                BodyChunkConnectionType.Normal, 1.0, 0.5);
            connection.Solve();
            Near(17.0, Vec2.Distance(first.Position, second.Position), 0.0001, "connection distance");
            Near(41.5, first.Velocity.X, 0.0001, "first chunk connection velocity correction");
            Near(-41.5, second.Velocity.X, 0.0001, "second chunk connection velocity correction");
        }

        private static void DesktopFloorCollision()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            BodyChunk chunk = new BodyChunk(0, new Vec2(work.Left + work.Width * 0.5, work.Bottom - 8.0), 9.0, 1.0);
            chunk.LastPosition = new Vec2(chunk.Position.X, work.Bottom - 13.0);
            chunk.Velocity = new Vec2(0.0, 5.0);
            world.Resolve(chunk);
            True(chunk.ContactFloor, "chunk should contact the work-area floor");
            Near(work.Bottom - chunk.Radius, chunk.Position.Y, 0.01, "resolved floor height");
        }

        private static void AiDoesNotMoveCreature()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(300.0, 300.0));
            DesktopPetAI ai = new DesktopPetAI(1234);
            MouseTracker mouse = new MouseTracker();
            mouse.Sample(0.025);
            Vec2 before = slugcat.Center;
            VirtualInput input = ai.Step(slugcat, world, mouse);
            Vec2 after = slugcat.Center;
            Near(0.0, Vec2.Distance(before, after), 0.000001, "AI must not set position");
            True(input.X >= -1 && input.X <= 1, "virtual horizontal input range");
        }

        private static void AtlasMetadataParses()
        {
            string root = Path.Combine(Path.GetTempPath(), "slugcat-atlas-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string png = Path.Combine(root, "player.png");
            string txt = Path.Combine(root, "player.txt");
            try
            {
                using (Bitmap bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb)) bitmap.Save(png, ImageFormat.Png);
                File.WriteAllText(txt,
                    "{\"frames\":{\"BodyA.png\":{\"frame\":{\"x\":1,\"y\":2,\"w\":10,\"h\":12}," +
                    "\"rotated\":false,\"trimmed\":true,\"spriteSourceSize\":{\"x\":3,\"y\":4,\"w\":10,\"h\":12}," +
                    "\"sourceSize\":{\"w\":18,\"h\":20}}}}");
                using (RainWorldAtlas atlas = RainWorldAtlasLoader.Load(png, txt))
                {
                    AtlasElement element;
                    True(atlas.TryGet("BodyA", out element), "extensionless lookup");
                    Equal(10, element.Frame.Width, "frame width");
                    Equal(20, element.SourceSize.Height, "source height");
                }
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void LocatorValidatesExplicitPath()
        {
            string root = Path.Combine(Path.GetTempPath(), "slugcat-locator-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "RainWorld_Data", "Managed"));
                Directory.CreateDirectory(Path.Combine(root, "RainWorld_Data", "StreamingAssets"));
                using (File.Create(Path.Combine(root, "RainWorld.exe"))) { }
                using (File.Create(Path.Combine(root, "RainWorld_Data", "Managed", "Assembly-CSharp.dll"))) { }
                RainWorldLocator locator = new RainWorldLocator(Path.Combine(root, "test-settings", "rain-world-path.txt"));
                True(locator.IsValid(root), "fake layout should validate");
                RainWorldInstallation installation = locator.Locate(root);
                True(installation != null && string.Equals(installation.RootPath, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase),
                    "explicit path should win");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void RequiredBehaviorsExist()
        {
            string[] names =
            {
                "Idle", "Walk", "Explore", "Sit", "Sleep", "LookAround", "FollowMouse", "AvoidMouse",
                "Jump", "ClimbWindow", "DropDown", "BalanceNearEdge", "ObserveWindow"
            };
            HashSet<string> actual = new HashSet<string>(Enum.GetNames(typeof(DesktopBehavior)), StringComparer.Ordinal);
            for (int i = 0; i < names.Length; i++) True(actual.Contains(names[i]), "missing behavior " + names[i]);
        }

        private static void UtilityActionsAreReachable()
        {
            UtilityContext jump = new UtilityContext
            {
                Grounded = true,
                Curiosity = 1.0,
                JumpReady = true,
                EdgeDistance = 200.0
            };
            double worstJump = UtilityEvaluator.Score(DesktopBehavior.Jump, jump, -0.06);
            double stickyExplore = UtilityEvaluator.Score(DesktopBehavior.Explore, jump, 0.08) + 0.07;
            True(worstJump > stickyExplore,
                "ready Jump must beat a hysteretic Explore score even at adverse random variation");

            UtilityContext drop = new UtilityContext
            {
                Grounded = true,
                OnWindow = true,
                Curiosity = 0.9,
                DropReady = true,
                EdgeDistance = 10.0
            };
            double worstDrop = UtilityEvaluator.Score(DesktopBehavior.DropDown, drop, -0.06);
            double stickyBalance = UtilityEvaluator.Score(DesktopBehavior.BalanceNearEdge, drop, 0.08) + 0.07;
            True(worstDrop > stickyBalance,
                "ready DropDown must beat a hysteretic BalanceNearEdge score");

            jump.JumpReady = false;
            drop.DropReady = false;
            Near(0.0, UtilityEvaluator.Score(DesktopBehavior.Jump, jump, 0.0), 0.000001,
                "Jump cooldown gate");
            Near(0.0, UtilityEvaluator.Score(DesktopBehavior.DropDown, drop, 0.0), 0.000001,
                "DropDown cooldown gate");
        }

        private static void WallContactReachesClimbMovement()
        {
            Point cursor = System.Windows.Forms.Cursor.Position;
            Slugcat slugcat = new Slugcat(new Vec2(cursor.X + 500.0, cursor.Y + 500.0));
            slugcat.BodyChunks[0].ContactRight = true;
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            MouseTracker mouse = new MouseTracker();
            mouse.Sample(SimulationConstants.LogicStepSeconds);
            DesktopPetAI ai = new DesktopPetAI(991);

            VirtualInput input = ai.Step(slugcat, world, mouse);
            True(ai.Behavior == DesktopBehavior.ClimbWindow, "wall-contact rising edge should select ClimbWindow");
            Equal(1, input.X, "climb input should press into a right-side wall");
            Equal(-1, input.Y, "climb input should press upward");

            slugcat.Movement.ApplyInput(input, world);
            True(slugcat.State.BodyMode == BodyModeIndex.WallClimb,
                "movement must interpret climb VirtualInput without direct AI movement");
            True(slugcat.BodyChunks[0].Velocity.Y < 0.0 && slugcat.BodyChunks[1].Velocity.Y < 0.0,
                "wall climb should produce upward screen-space velocity");
        }

        private static void OriginalFaceFrameSelection()
        {
            SlugcatPose pose = new SlugcatPose();
            pose.Hips = Vec2.Zero;
            pose.Head = new Vec2(0.0, -12.0);
            pose.LookDirection = new Vec2(1.0, 0.0);
            pose.BodyMode = BodyModeIndex.Stand;
            pose.Animation = AnimationIndex.None;
            Equal(0, SpriteRenderer.SelectFaceFrame(pose), "upright face frame");

            pose.Head = new Vec2(12.0, 0.0);
            pose.LookDirection = Vec2.Zero;
            Equal(4, SpriteRenderer.SelectFaceFrame(pose), "horizontal face frame");

            pose.Animation = AnimationIndex.Sleep;
            Equal(1, SpriteRenderer.SelectFaceFrame(pose), "sleep curl face frame");
            pose.Facing = 1;
            Near(45.0, SpriteRenderer.SelectHeadAngle(pose), 0.000001, "right-facing sleep head angle");
            pose.Facing = -1;
            Near(-45.0, SpriteRenderer.SelectHeadAngle(pose), 0.000001, "left-facing sleep head angle");
        }

        private static void WallClimbHandsTargetTheWall()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(400.0, 400.0));
            slugcat.State.BodyMode = BodyModeIndex.WallClimb;
            slugcat.State.Facing = 1;
            slugcat.BodyChunks[0].ContactRight = true;
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(50.0, -20.0));

            graphics.Step(attention, world);
            True(graphics.Arms[0].TargetPosition.X > slugcat.BodyChunks[0].Position.X,
                "both wall-climb hands should target the contacted wall side");
            True(graphics.Arms[1].TargetPosition.X > slugcat.BodyChunks[0].Position.X,
                "both wall-climb hands should target the contacted wall side");
            Near(slugcat.BodyChunks[0].Position.Y - 3.0, graphics.Arms[0].TargetPosition.Y, 0.000001,
                "upper wall hand offset");
            Near(slugcat.BodyChunks[0].Position.Y + 7.0, graphics.Arms[1].TargetPosition.Y, 0.000001,
                "lower wall hand offset");
        }

        private static void SleepCurlHandsShareOriginalTarget()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(400.0, 400.0));
            slugcat.State.Animation = AnimationIndex.Sleep;
            slugcat.State.Facing = -1;
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center);

            graphics.Step(attention, world);

            Vec2 expected = slugcat.Center + new Vec2(-10.0, 20.0);
            Near(0.0, Vec2.Distance(expected, graphics.Arms[0].TargetPosition), 0.000001,
                "left sleep hand target");
            Near(0.0, Vec2.Distance(expected, graphics.Arms[1].TargetPosition), 0.000001,
                "right sleep hand target");
        }

        private static void MovingWindowWallCarriesClimber()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            List<DesktopWindowSnapshot> snapshots = new List<DesktopWindowSnapshot>();
            snapshots.Add(new DesktopWindowSnapshot
            {
                Handle = new IntPtr(4321),
                Bounds = Rectangle.FromLTRB(100, 100, 300, 300),
                Title = "test",
                ClassName = "test"
            });
            world.RefreshFromSnapshots(snapshots);

            snapshots[0].Bounds = Rectangle.FromLTRB(120, 130, 320, 330);
            world.RefreshFromSnapshots(snapshots);
            Vec2 wallDelta = world.GetSurfaceMovement(4321, DesktopSurfaceKind.WindowLeftWall);
            Near(20.0, wallDelta.X, 0.000001, "translated left-wall x delta");
            Near(30.0, wallDelta.Y, 0.000001, "translated left-wall y delta");

            Slugcat slugcat = new Slugcat(new Vec2(80.0, 180.0));
            slugcat.State.BodyMode = BodyModeIndex.WallClimb;
            slugcat.BodyChunks[0].WallSurfaceId = 4321;
            slugcat.BodyChunks[0].WallSurfaceKind = DesktopSurfaceKind.WindowLeftWall;
            Vec2 chestBefore = slugcat.BodyChunks[0].Position;
            Vec2 hipsBefore = slugcat.BodyChunks[1].Position;
            Vec2 applied = slugcat.ApplyMovingSurfaceDelta(world);
            Near(20.0, applied.X, 0.000001, "applied wall x delta");
            Near(30.0, applied.Y, 0.000001, "applied wall y delta");
            Near(0.0, Vec2.Distance(chestBefore + applied, slugcat.BodyChunks[0].Position), 0.000001,
                "climbing chest follows wall");
            Near(0.0, Vec2.Distance(hipsBefore + applied, slugcat.BodyChunks[1].Position), 0.000001,
                "climbing hips follows wall");

            snapshots[0].Bounds = Rectangle.FromLTRB(140, 130, 320, 330);
            world.RefreshFromSnapshots(snapshots);
            Near(0.0, world.GetSurfaceMovement(4321, DesktopSurfaceKind.WindowTop).X, 0.000001,
                "left-edge resize must not translate the top platform");
            Near(20.0, world.GetSurfaceMovement(4321, DesktopSurfaceKind.WindowLeftWall).X, 0.000001,
                "left-edge resize moves the left wall");
            Near(0.0, world.GetSurfaceMovement(4321, DesktopSurfaceKind.WindowRightWall).X, 0.000001,
                "left-edge resize leaves the right wall fixed");
        }

        private static void MonitorCeilingWindowTopIsRejected()
        {
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            int inset = Math.Max(20, Math.Min(100, work.Width / 4));
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            List<DesktopWindowSnapshot> snapshots = new List<DesktopWindowSnapshot>();
            snapshots.Add(new DesktopWindowSnapshot
            {
                Handle = new IntPtr(8765),
                Bounds = Rectangle.FromLTRB(work.Left + inset, work.Top,
                    Math.Min(work.Right - inset, work.Left + inset + 300),
                    Math.Min(work.Bottom, work.Top + 300)),
                Title = "top-snapped",
                ClassName = "test"
            });
            world.RefreshFromSnapshots(snapshots);

            int wallSegments = 0;
            for (int i = 0; i < world.Surfaces.Count; i++)
            {
                DesktopSurface surface = world.Surfaces[i];
                True(surface.Id != 8765 || surface.Kind != DesktopSurfaceKind.WindowTop,
                    "a top-border window must not create an off-screen standing surface");
                if (surface.Id == 8765 &&
                    (surface.Kind == DesktopSurfaceKind.WindowLeftWall ||
                     surface.Kind == DesktopSurfaceKind.WindowRightWall))
                {
                    wallSegments++;
                    True(surface.Top >= work.Top + SimulationConstants.VisibleWindowTopClearance,
                        "a top-border wall must be clipped to the visible climbing band");
                }
            }
            True(wallSegments > 0, "the visible part of an inset window wall should remain climbable");
        }

        private static void OccludedWindowsAreClipped()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            List<DesktopWindowSnapshot> snapshots = new List<DesktopWindowSnapshot>();
            snapshots.Add(new DesktopWindowSnapshot
            {
                Handle = new IntPtr(7001),
                Bounds = Rectangle.FromLTRB(0, 0, 500, 500),
                Title = "front",
                ClassName = "front"
            });
            snapshots.Add(new DesktopWindowSnapshot
            {
                Handle = new IntPtr(7002),
                Bounds = Rectangle.FromLTRB(100, 100, 300, 300),
                Title = "covered",
                ClassName = "covered"
            });
            world.RefreshFromSnapshots(snapshots);
            IList<DesktopSurface> surfaces = world.Surfaces;
            for (int i = 0; i < surfaces.Count; i++)
                True(surfaces[i].Id != 7002, "fully occluded window surface should be removed");

            snapshots.Clear();
            snapshots.Add(new DesktopWindowSnapshot
            {
                Handle = new IntPtr(7003),
                Bounds = Rectangle.FromLTRB(150, 50, 250, 150),
                Title = "partial front",
                ClassName = "partial front"
            });
            snapshots.Add(new DesktopWindowSnapshot
            {
                Handle = new IntPtr(7004),
                Bounds = Rectangle.FromLTRB(100, 100, 300, 300),
                Title = "partial back",
                ClassName = "partial back"
            });
            world.RefreshFromSnapshots(snapshots);
            int visibleTopSegments = 0;
            surfaces = world.Surfaces;
            for (int i = 0; i < surfaces.Count; i++)
            {
                if (surfaces[i].Id == 7004 && surfaces[i].Kind == DesktopSurfaceKind.WindowTop)
                {
                    visibleTopSegments++;
                    True(surfaces[i].Right <= 150 || surfaces[i].Left >= 250,
                        "partially covered top segments must exclude the occluder interval");
                }
            }
            Equal(2, visibleTopSegments, "visible top segment count after clipping");
        }

        private static void OriginalVariantValues()
        {
            SlugcatAppearance survivor = SlugcatAppearance.For(SlugcatVariant.Survivor);
            SlugcatAppearance monk = SlugcatAppearance.For(SlugcatVariant.Monk);
            SlugcatAppearance hunter = SlugcatAppearance.For(SlugcatVariant.Hunter);
            SlugcatAppearance gourmand = SlugcatAppearance.For(SlugcatVariant.Gourmand);

            Equal(Color.FromArgb(255, 255, 255, 255).ToArgb(), survivor.BodyColor.ToArgb(), "Survivor color");
            Equal(Color.FromArgb(255, 255, 255, 115).ToArgb(), monk.BodyColor.ToArgb(), "Monk color");
            Equal(Color.FromArgb(255, 255, 115, 115).ToArgb(), hunter.BodyColor.ToArgb(), "Hunter color");
            Equal(Color.FromArgb(255, 240, 193, 151).ToArgb(), gourmand.BodyColor.ToArgb(), "Gourmand color");
            Near(1.2, hunter.RunSpeedFactor, 0.000001, "Hunter run-speed factor");
            Near(1.35, gourmand.BodyWeightFactor, 0.000001, "Gourmand body-weight factor");
            Near(1.4, gourmand.BodyWidthScale, 0.000001, "Gourmand body scaleX");
            Near(1.6, gourmand.HipsWidthScale, 0.000001, "Gourmand hips scaleX");

            Slugcat creature = new Slugcat(Vec2.Zero, SlugcatVariant.Gourmand);
            Near(0.35 * 1.35, creature.BodyChunks[0].Mass, 0.000001, "Gourmand main chunk mass");
            Near(0.35 * 1.35, creature.BodyChunks[1].Mass, 0.000001, "Gourmand hips chunk mass");
        }

        private static void OriginalTailLayout()
        {
            ProceduralTail tail = new ProceduralTail(Vec2.Zero);
            Equal(4, tail.Segments.Length, "tail segment count");
            double[] radii = { 6.0, 4.0, 2.5, 1.0 };
            double[] lengths = { 4.0, 7.0, 7.0, 7.0 };
            for (int i = 0; i < tail.Segments.Length; i++)
            {
                Near(radii[i], tail.Segments[i].Radius, 0.000001, "tail radius " + i);
                Near(lengths[i], tail.Segments[i].Length, 0.000001, "tail connection radius " + i);
            }
        }

        private static void RestPosturesUseVirtualInput()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(300.0, 300.0));
            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.Movement.ApplyInput(
                new VirtualInput(0, 0, false, false, VirtualPosture.Sleep), world);
            True(slugcat.State.Animation == AnimationIndex.Sleep, "sleep posture must be interpreted by movement");

            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.Movement.ApplyInput(
                new VirtualInput(0, 0, false, false, VirtualPosture.Sit), world);
            True(slugcat.State.Animation == AnimationIndex.Sit, "sit posture must be interpreted by movement");
        }

        private static void StandForcesKeepUpperBodyUpright()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Slugcat slugcat = new Slugcat(new Vec2(
                work.Left + work.Width * 0.5,
                work.Bottom - SimulationConstants.HipsChunkRadius - 1.0));

            for (int i = 0; i < 40; i++)
            {
                slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            }

            True(slugcat.State.BodyMode == BodyModeIndex.Stand, "grounded neutral posture should be Stand");
            True(slugcat.BodyChunks[0].Position.Y < slugcat.BodyChunks[1].Position.Y - 10.0,
                "the main body chunk must remain above the hips");
            Near(SimulationConstants.BodyConnectionDistance,
                Vec2.Distance(slugcat.BodyChunks[0].Position, slugcat.BodyChunks[1].Position),
                0.01,
                "standing body connection distance");
        }

        private static void IdleAndRestFramesStayStill()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(300.0, 300.0));
            slugcat.State.AnimationFrame = 5;
            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.Movement.ApplyInput(VirtualInput.Neutral, world);
            Equal(0, slugcat.State.AnimationFrame, "idle Stand frame");

            slugcat.State.AnimationFrame = 5;
            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.Movement.ApplyInput(
                new VirtualInput(0, 0, false, false, VirtualPosture.Sleep), world);
            Equal(0, slugcat.State.AnimationFrame, "sleep frame");
        }

        private static void JumpLaunchClearsGroundedForces()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Slugcat slugcat = new Slugcat(new Vec2(
                work.Left + work.Width * 0.5,
                work.Bottom - SimulationConstants.HipsChunkRadius - 1.0));

            for (int i = 0; i < 8; i++) slugcat.Step(VirtualInput.Neutral, world, Vec2.Zero, Vec2.Zero);
            for (int i = 0; i < 8 && slugcat.State.Animation != AnimationIndex.Jump; i++)
                slugcat.Step(new VirtualInput(0, 0, true, false), world, Vec2.Zero, Vec2.Zero);

            True(slugcat.State.Animation == AnimationIndex.Jump, "jump should launch after pre-jump compression");
            True(slugcat.State.BodyMode == BodyModeIndex.Default && !slugcat.State.Grounded,
                "launch tick must be airborne");
            True(slugcat.BodyChunks[0].Velocity.Y < 0.0 && slugcat.BodyChunks[1].Velocity.Y < 0.0,
                "both chunks must have upward screen-space velocity at launch");
        }

        private static void DropDownRequestsSurfacePassThrough()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(300.0, 300.0));
            slugcat.BodyChunks[1].ContactFloor = true;
            slugcat.BodyChunks[1].SupportingSurfaceId = 1234;
            slugcat.Movement.ApplyInput(
                new VirtualInput(0, 1, false, false, VirtualPosture.None, true), world);

            Equal(1234, (int)slugcat.Movement.IgnoredSurfaceId, "ignored window surface id");
            True(!slugcat.State.Grounded && slugcat.State.BodyMode == BodyModeIndex.Default,
                "drop-through should leave the grounded state");
            True(slugcat.BodyChunks[0].Velocity.Y > 0.0 && slugcat.BodyChunks[1].Velocity.Y > 0.0,
                "drop-through should push both chunks downward");
        }

        private static void EmbeddedOriginalAtlasLoads(RainWorldInstallation installation)
        {
            RainWorldAssetLoader loader = new RainWorldAssetLoader(installation);
            using (RainWorldAtlasSet set = loader.TryLoadPlayerAtlas())
            {
                True(set != null, loader.Status);
                AtlasSprite body;
                True(set.TryGet("BodyA", out body), "BodyA from embedded rainWorld atlas");
                True(body.Atlas.ImagePath.EndsWith("#rainWorld", StringComparison.OrdinalIgnoreCase),
                    "BodyA must resolve to the original embedded atlas, not DMS");
                Equal(464, body.Atlas.Image.Width, "base atlas width");
                Equal(512, body.Atlas.Image.Height, "base atlas height");
                Equal(358, body.Element.Frame.X, "BodyA frame x");
                Equal(50, body.Element.Frame.Y, "BodyA frame y");
                Equal(14, body.Element.Frame.Width, "BodyA frame width");
                Equal(19, body.Element.Frame.Height, "BodyA frame height");
            }
        }

        private static void OriginalConnectToPointEquation()
        {
            BodyPart part = new BodyPart(Vec2.Zero, 4.0, 0.8, 0.99);
            part.ConnectToPoint(new Vec2(10.0, 0.0), 3.0, false, 0.2,
                new Vec2(2.0, 0.0), 0.7, 0.1);
            Near(7.0, part.Position.X, 0.000001, "constraint position correction");
            Near(4.16, part.Velocity.X, 0.000001, "host-relative adapted velocity");
        }

        private static void SharedGraphicsInterpolation()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(420.0, 360.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(80.0, -20.0));
            slugcat.BodyChunks[0].Position += new Vec2(12.0, -4.0);
            slugcat.BodyChunks[1].Position += new Vec2(8.0, 2.0);
            graphics.Step(attention, world);
            SlugcatPose pose = graphics.BuildPose(0.25, attention, 17);

            Near(0.0, Vec2.Distance(Vec2.Lerp(pose.DrawLast[0], pose.DrawCurrent[0], 0.25), pose.Chest),
                0.000001, "upper draw interpolation");
            Near(0.0, Vec2.Distance(Vec2.Lerp(pose.HeadLast, pose.HeadCurrent, 0.25), pose.Head),
                0.000001, "head interpolation");
            Near(0.0, Vec2.Distance(Vec2.Lerp(pose.LegsLast, pose.LegsCurrent, 0.25), pose.Legs),
                0.000001, "legs interpolation");
            Near(0.0, Vec2.Distance(Vec2.Lerp(pose.TailLast[0], pose.TailCurrent[0], 0.25), pose.Tail[0]),
                0.000001, "tail interpolation");
            Near(0.25, pose.TimeStacker, 0.000001, "reported timeStacker");
            Equal(17, (int)pose.SimulationTick, "reported simulation tick");
        }

        private static void FutileTrimAnchorCoordinates()
        {
            AtlasElement element = new AtlasElement
            {
                Frame = new Rectangle(1, 2, 10, 12),
                SpriteSource = new Rectangle(3, 4, 10, 12),
                SourceSize = new Size(18, 20)
            };
            RectangleF local = element.GetLocalRectangle(0.5, 0.7894737);
            Near(-6.0, local.X, 0.00001, "trimmed local x");
            Near(-0.210526, local.Y, 0.0001, "trimmed local y");
            Near(10.0, local.Width, 0.00001, "trimmed width");
        }

        private static void NegativeVirtualDesktopCoordinates()
        {
            RenderSpace space = new RenderSpace(Rectangle.FromLTRB(-1920, -200, 2560, 1440));
            Vec2 world = new Vec2(-1800.5, -50.25);
            Vec2 overlay = space.WorldToOverlay(world);
            Near(119.5, overlay.X, 0.000001, "negative screen world-to-overlay x");
            Near(149.75, overlay.Y, 0.000001, "negative screen world-to-overlay y");
            Near(0.0, Vec2.Distance(world, space.OverlayToWorld(overlay)), 0.000001,
                "coordinate conversion round trip");
        }

        private static void TwoFortyHertzRenderCadence()
        {
            FixedTimeStep step = new FixedTimeStep(SimulationConstants.LogicStepSeconds);
            int updates = 0;
            for (int frame = 0; frame < 240; frame++)
            {
                step.AddElapsed(1.0 / 240.0);
                while (step.ConsumeStep()) updates++;
            }
            Equal(40, updates, "one second at 240 render frames");
        }

        private static void LongGraphicsScenarioStaysConnected()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Rectangle work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Slugcat slugcat = new Slugcat(new Vec2(work.Left + work.Width * 0.5,
                work.Bottom - SimulationConstants.HipsChunkRadius - 1.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(90.0, -35.0));
            for (int tick = 0; tick < 520; tick++)
            {
                int direction = tick < 120 ? 0 : (tick < 240 ? 1 : (tick < 360 ? -1 : 1));
                bool jump = tick >= 400 && tick < 410;
                slugcat.Step(new VirtualInput(direction, 0, jump, false), world, Vec2.Zero, Vec2.Zero);
                attention.Step();
                graphics.Step(attention, world);
                SlugcatPose pose = graphics.BuildPose((tick % 6) / 6.0, attention, tick);
                True(Vec2.Distance(pose.Head, pose.Chest) < 35.0, "head separation at tick " + tick);
                True(Vec2.Distance(pose.Tail[0], pose.Hips) < 25.0, "tail-root separation at tick " + tick);
                for (int i = 0; i < pose.Hands.Length; i++)
                    True(Vec2.Distance(pose.Hands[i], pose.Chest) < 42.0, "hand separation at tick " + tick);
            }
        }

        private static void GraphicsBoundsIncludeExtremities()
        {
            SlugcatPose pose = new SlugcatPose();
            pose.Chest = new Vec2(0.0, 0.0);
            pose.Hips = new Vec2(0.0, 10.0);
            pose.Head = new Vec2(0.0, -10.0);
            pose.Legs = new Vec2(0.0, 20.0);
            pose.Hands[0] = new Vec2(-30.0, 0.0);
            pose.Hands[1] = new Vec2(30.0, 0.0);
            pose.Tail = new[] { new Vec2(0.0, 12.0), new Vec2(15.0, 18.0), new Vec2(30.0, 22.0) };
            pose.UpdateGraphicsBounds();
            True(pose.GraphicsBounds.Left < -30.0 && pose.GraphicsBounds.Right > 30.0,
                "hand and tail x extents");
            True(pose.GraphicsBounds.Top < -10.0 && pose.GraphicsBounds.Bottom > 22.0,
                "head and tail y extents");
        }

        private static void UnusedHandsRetract()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(400.0, 400.0));
            slugcat.State.BodyMode = BodyModeIndex.Stand;
            slugcat.State.Animation = AnimationIndex.None;
            AttentionSystem attention = new AttentionSystem();
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(30.0, -20.0));
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            for (int i = 0; i < 35; i++) graphics.Step(attention, world);
            True(graphics.Arms[0].Mode == LimbMode.Retracted, "left idle hand must retract");
            True(graphics.Arms[1].Mode == LimbMode.Retracted, "right idle hand must retract");
            Near(0.0, Vec2.Distance(slugcat.BodyChunks[0].Position, graphics.Arms[0].End.Position),
                0.000001, "retracted left hand position");
        }

        private static void CrawlHandsUseOriginalTargets()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(400.0, 400.0));
            slugcat.State.BodyMode = BodyModeIndex.Crawl;
            slugcat.State.Animation = AnimationIndex.DownOnFours;
            slugcat.BodyChunks[0].Velocity = new Vec2(2.0, 0.0);
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            graphics.Step(attention, world);
            Vec2 connection = slugcat.BodyChunks[0].Position;
            Near(connection.X + 14.0, graphics.Arms[0].TargetPosition.X, 0.000001,
                "left DownOnFours target x");
            Near(connection.X + 26.0, graphics.Arms[1].TargetPosition.X, 0.000001,
                "right DownOnFours target x");
            Near(connection.Y, graphics.Arms[0].TargetPosition.Y, 0.000001,
                "crawl target y at horizontal velocity");
            True(graphics.Arms[0].Mode == LimbMode.HuntAbsolutePosition &&
                 graphics.Arms[1].Mode == LimbMode.HuntAbsolutePosition,
                "crawl arms use absolute hunt mode");
        }

        private static void ArmConstraintPreventsSeparation()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(440.0, 380.0));
            slugcat.State.BodyMode = BodyModeIndex.Crawl;
            slugcat.State.Animation = AnimationIndex.DownOnFours;
            SlugcatGraphics graphics = new SlugcatGraphics(slugcat);
            AttentionSystem attention = new AttentionSystem();
            for (int tick = 0; tick < 120; tick++)
            {
                slugcat.BodyChunks[0].Velocity = new Vec2(tick < 60 ? 8.0 : -8.0, tick % 9 == 0 ? 4.0 : 0.0);
                graphics.Step(attention, world);
                for (int hand = 0; hand < 2; hand++)
                {
                    True(Vec2.Distance(graphics.Arms[hand].End.Position,
                        slugcat.BodyChunks[0].Position) <= 20.0001,
                        "arm length at tick " + tick + " hand " + hand);
                }
            }
        }

        private static void CrawlFaceUsesBodyFacing()
        {
            SlugcatPose pose = new SlugcatPose();
            pose.BodyMode = BodyModeIndex.Crawl;
            pose.Animation = AnimationIndex.DownOnFours;
            pose.Facing = 1;
            pose.Hips = new Vec2(0.0, 0.0);
            pose.Chest = new Vec2(8.0, 0.0);
            pose.Head = new Vec2(10.0, 0.0);
            pose.LookDirection = new Vec2(-1.0, 0.0);
            Near(1.0, SpriteRenderer.SelectFaceScaleX(pose), 0.000001,
                "right crawl body must ignore left attention for face flip");
            pose.Chest = new Vec2(-8.0, 0.0);
            pose.LookDirection = new Vec2(1.0, 0.0);
            Near(-1.0, SpriteRenderer.SelectFaceScaleX(pose), 0.000001,
                "left crawl body must ignore right attention for face flip");
            pose.Chest = new Vec2(0.1, 0.0);
            pose.Facing = -1;
            Near(-1.0, SpriteRenderer.SelectFaceScaleX(pose), 0.000001,
                "near-vertical crawl uses persistent facing hysteresis");
        }

        private static void ArmShouldersFollowBodyAxis()
        {
            SlugcatPose pose = new SlugcatPose();
            pose.Chest = Vec2.Zero;
            pose.Hips = new Vec2(0.0, 17.0);
            pose.ArmRetractCounters[0] = 0;
            pose.ArmRetractCounters[1] = 0;
            Vec2 left = SpriteRenderer.ComputeArmShoulder(pose, 0);
            Vec2 right = SpriteRenderer.ComputeArmShoulder(pose, 1);
            Near(9.0, Vec2.Distance(left, right), 0.00001, "upright shoulder separation");
            Near(left.Y, right.Y, 0.00001, "upright shoulder axis");

            pose.Hips = new Vec2(-17.0, 0.0);
            left = SpriteRenderer.ComputeArmShoulder(pose, 0);
            right = SpriteRenderer.ComputeArmShoulder(pose, 1);
            Near(0.0, Vec2.Distance(left, right), 0.00001,
                "original cosine shoulder compression for horizontal torso");
        }

        private static void UniformCharacterRenderScale()
        {
            SlugcatPose pose = new SlugcatPose();
            pose.CharacterOrigin = new Vec2(10.0, 10.0);
            pose.CharacterRenderScale = 2.20;
            Vec2 point = pose.ToRenderedWorld(new Vec2(20.0, 5.0));
            Near(32.0, point.X, 0.000001, "uniform scaled x");
            Near(-1.0, point.Y, 0.000001, "uniform scaled y");
            Near(2.20, SimulationConstants.CharacterRenderScale, 0.000001,
                "configured visual-only scale");
        }

        private static void ExpandedDebugOverlayRenders()
        {
            DesktopCollisionWorld world = new DesktopCollisionWorld(new WindowEnumerator());
            world.Refresh(IntPtr.Zero);
            Slugcat slugcat = new Slugcat(new Vec2(320.0, 260.0));
            DesktopPetAI ai = new DesktopPetAI(77);
            AttentionSystem attention = ai.Attention;
            attention.SetTarget(AttentionKind.RandomPoint, slugcat.Center + new Vec2(50.0, -20.0));
            SlugcatGraphics procedural = new SlugcatGraphics(slugcat);
            slugcat.State.BodyMode = BodyModeIndex.Crawl;
            slugcat.State.Animation = AnimationIndex.DownOnFours;
            procedural.Step(attention, world);
            SlugcatPose pose = procedural.BuildPose(0.5, attention, 12);
            Vec2 before = slugcat.Center;
            using (SpriteRenderer renderer = new SpriteRenderer(null))
            using (Bitmap bitmap = new Bitmap(640, 480, PixelFormat.Format32bppPArgb))
            using (System.Drawing.Graphics drawing = System.Drawing.Graphics.FromImage(bitmap))
            {
                renderer.Render(drawing, pose, new RenderSpace(new Rectangle(0, 0, 640, 480)),
                    true, world, slugcat, ai, "debug-test", slugcat.Appearance);
            }
            Near(0.0, Vec2.Distance(before, slugcat.Center), 0.000001,
                "debug rendering must not mutate player physics");
        }

        private static SlugcatVariant ReadVariant(string value)
        {
            if (string.Equals(value, "yellow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "monk", StringComparison.OrdinalIgnoreCase)) return SlugcatVariant.Monk;
            if (string.Equals(value, "red", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "hunter", StringComparison.OrdinalIgnoreCase)) return SlugcatVariant.Hunter;
            if (string.Equals(value, "gourmand", StringComparison.OrdinalIgnoreCase)) return SlugcatVariant.Gourmand;
            return SlugcatVariant.Survivor;
        }

        private static void True(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static void Equal(int expected, int actual, string message)
        {
            if (expected != actual) throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
        }

        private static void Near(double expected, double actual, double tolerance, string message)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
        }
    }
}
