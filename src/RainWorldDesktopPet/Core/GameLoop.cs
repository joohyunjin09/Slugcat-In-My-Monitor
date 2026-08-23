using System;
using System.Diagnostics;
using System.Drawing;
using RainWorldDesktopPet.AI;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Graphics;
using RainWorldDesktopPet.Physics;
using RainWorldDesktopPet.RainWorld;

namespace RainWorldDesktopPet.Core
{
    public sealed class GameLoop
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly FixedTimeStep fixedTimeStep = new FixedTimeStep(SimulationConstants.LogicStepSeconds);
        private readonly MouseTracker mouse = new MouseTracker();
        private double lastTime;
        private double surfaceRefreshAccumulator;
        private long simulationTick;
        private readonly ParityDiagnostics parityDiagnostics = new ParityDiagnostics();

        public GameLoop(IntPtr overlayHandle, RainWorldInstallation installation, SlugcatVariant variant)
        {
            World = new DesktopCollisionWorld(new WindowEnumerator());
            World.Refresh(overlayHandle);
            Point cursor = System.Windows.Forms.Cursor.Position;
            MonitorInfo monitor = MonitorManager.FindNearest(cursor);
            double spawnX = MathUtil.Clamp(cursor.X, monitor.WorkArea.Left + 70.0, monitor.WorkArea.Right - 70.0);
            Vec2 spawn = new Vec2(spawnX, monitor.WorkArea.Bottom - SimulationConstants.HipsChunkRadius - 2.0);
            Slugcat = new Slugcat(spawn, variant);
            AI = new DesktopPetAI(Environment.TickCount);
            AI.Attention.SetTarget(AttentionKind.Mouse, Vec2.FromPoint(cursor));
            Graphics = new SlugcatGraphics(Slugcat);
            mouse.Sample(SimulationConstants.LogicStepSeconds);

            RainWorldAssetLoader assetLoader = new RainWorldAssetLoader(installation);
            RainWorldAtlasSet atlas = assetLoader.TryLoadLoosePlayerAtlas();
            AssetStatus = assetLoader.Status;
            Renderer = new SpriteRenderer(atlas);
        }

        public readonly DesktopCollisionWorld World;
        public readonly Slugcat Slugcat;
        public readonly DesktopPetAI AI;
        public readonly SlugcatGraphics Graphics;
        public readonly SpriteRenderer Renderer;
        public string AssetStatus { get; private set; }
        public bool DebugEnabled { get; set; }
        public bool Paused { get; set; }
        public SlugcatAppearance Appearance { get { return Slugcat.Appearance; } }
        public double Interpolation { get { return fixedTimeStep.Alpha; } }
        public long SimulationTick { get { return simulationTick; } }

        public void Advance(IntPtr overlayHandle)
        {
            double now = clock.Elapsed.TotalSeconds;
            double elapsed = lastTime <= 0.0 ? SimulationConstants.LogicStepSeconds : now - lastTime;
            lastTime = now;
            mouse.Sample(elapsed);
            if (Paused)
            {
                fixedTimeStep.Reset();
                return;
            }

            surfaceRefreshAccumulator += elapsed;
            if (surfaceRefreshAccumulator >= SimulationConstants.WindowRefreshSeconds)
            {
                surfaceRefreshAccumulator %= SimulationConstants.WindowRefreshSeconds;
                World.Refresh(overlayHandle);
                Vec2 surfaceDelta = Slugcat.ApplyMovingSurfaceDelta(World);
                Graphics.ApplyMovingSurfaceDelta(surfaceDelta);
            }

            fixedTimeStep.AddElapsed(elapsed);
            int steps = 0;
            while (steps < 3 && fixedTimeStep.ConsumeStep())
            {
                VirtualInput input = Slugcat.IsGrabbed ? VirtualInput.Neutral : AI.Step(Slugcat, World, mouse);
                Slugcat.Step(input, World, mouse.Position, mouse.Velocity);
                Graphics.Step(AI.Attention, World);
                simulationTick++;
                steps++;
            }
            // MainLoopProcess.RawUpdate zeroes myTimeStacker after the third
            // catch-up Update, preventing a stalled desktop from spiralling.
            if (steps == 3) fixedTimeStep.Reset();
        }

        public SlugcatPose BuildPose()
        {
            SlugcatPose pose = Graphics.BuildPose(Interpolation, AI.Attention, simulationTick);
            parityDiagnostics.Observe(pose);
            return pose;
        }

        public bool HitTest(Vec2 screenPoint)
        {
            return Slugcat.HitTest(screenPoint) || Vec2.Distance(screenPoint, Graphics.Head.Position) < 17.0;
        }

        public bool BeginGrab(Vec2 screenPoint)
        {
            if (Slugcat.Grab(screenPoint)) return true;
            if (Vec2.Distance(screenPoint, Graphics.Head.Position) < 17.0)
            {
                return Slugcat.Grab(Slugcat.BodyChunks[0].Position);
            }
            return false;
        }

        public void EndGrab()
        {
            Slugcat.Release(mouse.Velocity);
        }

        public void SetVariant(SlugcatVariant variant)
        {
            Slugcat.SetVariant(variant);
        }
    }
}
