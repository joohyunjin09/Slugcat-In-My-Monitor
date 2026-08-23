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
        private readonly MouseAttentionState mouseAttention = new MouseAttentionState();
        private double lastTime;
        private double surfaceRefreshAccumulator;
        private long simulationTick;
        private readonly ParityDiagnostics parityDiagnostics = new ParityDiagnostics();
        private readonly Stopwatch renderMetricClock = Stopwatch.StartNew();
        private int renderFramesInSample;
        private int simulationStepsLastFrame;
        private double renderFramesPerSecond;
        private double monitorRefreshRate;
        private readonly RainWorldAtlasSet atlas;

        public GameLoop(IntPtr overlayHandle, RainWorldInstallation installation, SlugcatVariant variant)
            : this(overlayHandle, installation, variant, SlugcatSkin.Default)
        {
        }

        public GameLoop(IntPtr overlayHandle, RainWorldInstallation installation,
            SlugcatVariant variant, SlugcatSkin skin)
        {
            World = new DesktopCollisionWorld(new WindowEnumerator());
            World.Refresh(overlayHandle);
            Point cursor = System.Windows.Forms.Cursor.Position;
            MonitorInfo monitor = MonitorManager.FindNearest(cursor);
            double spawnMargin = DesktopWorldTransform.ToDesktopLength(70.0);
            double spawnX = MathUtil.Clamp(cursor.X, monitor.WorkArea.Left + spawnMargin,
                monitor.WorkArea.Right - spawnMargin);
            Vec2 spawn = DesktopWorldTransform.ToSimulation(new Vec2(spawnX,
                monitor.WorkArea.Bottom - DesktopWorldTransform.ToDesktopLength(
                    SimulationConstants.HipsChunkRadius + 2.0)));
            Slugcat = new Slugcat(spawn, variant);
            AI = new DesktopPetAI(Environment.TickCount);
            AI.Attention.SetTarget(AttentionKind.RandomPoint,
                spawn + new Vec2(Slugcat.State.Facing * 60.0, -20.0));
            RainWorldAssetLoader assetLoader = new RainWorldAssetLoader(installation);
            atlas = assetLoader.TryLoadPlayerAtlas();
            AssetStatus = assetLoader.Status;
            SlugcatVisualProfile requested = SlugcatVisualProfiles.Get(skin);
            string missing;
            if (!requested.IsAvailable(atlas, out missing))
            {
                requested = SlugcatVisualProfiles.Default;
                AssetStatus += " Requested skin is unavailable (missing " + missing + "); Default is active.";
            }
            Graphics = new SlugcatGraphics(Slugcat, requested, atlas);
            mouse.Sample(SimulationConstants.LogicStepSeconds);
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
        public SlugcatSkin Skin { get { return Graphics.VisualProfile.Skin; } }
        public double Interpolation { get { return fixedTimeStep.Alpha; } }
        public long SimulationTick { get { return simulationTick; } }
        public double RenderFramesPerSecond { get { return renderFramesPerSecond; } }
        public double MonitorRefreshRate { get { return monitorRefreshRate; } }
        public MouseAttentionState MouseAttention { get { return mouseAttention; } }

        public void RecordRenderFrame(double displayRefreshRate)
        {
            monitorRefreshRate = displayRefreshRate;
            renderFramesInSample++;
            double seconds = renderMetricClock.Elapsed.TotalSeconds;
            if (seconds < 0.5) return;
            renderFramesPerSecond = renderFramesInSample / seconds;
            renderFramesInSample = 0;
            renderMetricClock.Restart();
        }

        public void Advance(IntPtr overlayHandle)
        {
            double now = clock.Elapsed.TotalSeconds;
            double elapsed = lastTime <= 0.0 ? SimulationConstants.LogicStepSeconds : now - lastTime;
            lastTime = now;
            mouse.Sample(elapsed);
            if (Paused)
            {
                mouse.ConsumeClick();
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
                if (!Slugcat.State.Conscious || Slugcat.State.Dead ||
                    Slugcat.State.StunCounter > 0)
                {
                    mouse.ConsumeClick();
                    mouseAttention.Suppress(now, mouse.Position, Graphics.Head.Position);
                }
                else
                {
                    mouseAttention.Update(now, mouse.Position, mouse.ConsumeClick(), Graphics.Head.Position);
                }
                VirtualInput input = Slugcat.IsGrabbed
                    ? VirtualInput.Neutral
                    : AI.Step(Slugcat, World, mouse, mouseAttention);
                Slugcat.Step(input, World, mouse.Position, mouse.Velocity);
                if (!Slugcat.State.Conscious || Slugcat.State.Dead ||
                    Slugcat.State.StunCounter > 0)
                    mouseAttention.Suppress(now, mouse.Position, Graphics.Head.Position);
                parityDiagnostics.ObserveSurfaceState(Slugcat, World, input, simulationTick);
                Graphics.Step(AI.Attention, AI.OriginalAttentionTarget,
                    AI.MouseAttentionActive && Slugcat.State.Conscious &&
                        !Slugcat.State.Dead && Slugcat.State.StunCounter < 1,
                    World);
                simulationTick++;
                steps++;
            }
            // MainLoopProcess.RawUpdate zeroes myTimeStacker after the third
            // catch-up Update, preventing a stalled desktop from spiralling.
            if (steps == 3) fixedTimeStep.Reset();
            simulationStepsLastFrame = steps;
        }

        public SlugcatPose BuildPose()
        {
            SlugcatPose pose = Graphics.BuildPose(Interpolation, AI.Attention, simulationTick);
            pose.LogicTicksPerSecond = SimulationConstants.LogicTicksPerSecond;
            pose.LogicStepSeconds = fixedTimeStep.StepSeconds;
            pose.AccumulatorSeconds = fixedTimeStep.AccumulatorSeconds;
            pose.SimulationTimeSeconds = simulationTick * SimulationConstants.LogicStepSeconds;
            pose.SimulationStepsLastFrame = simulationStepsLastFrame;
            pose.RenderFramesPerSecond = renderFramesPerSecond;
            pose.MonitorRefreshRate = monitorRefreshRate;
            pose.MousePosition = mouseAttention.MousePosition;
            pose.MouseDistanceToHead = mouseAttention.DistanceToHead;
            pose.MouseAttentionRadius = mouseAttention.Radius;
            pose.LastRelevantMouseClickTime = mouseAttention.LastRelevantClickTime;
            pose.TimeSinceRelevantMouseClick = mouseAttention.TimeSinceRelevantClick;
            pose.MouseAttentionTimeout = mouseAttention.TimeoutSeconds;
            pose.MouseAttentionActive = mouseAttention.IsActive;
            MonitorInfo currentMonitor = World.FindMonitor(Slugcat.Center);
            pose.CurrentMonitorName = currentMonitor.Name;
            pose.CurrentMonitorId = currentMonitor.TerrainId;
            pose.CurrentMonitorBounds = currentMonitor.Bounds;
            pose.CurrentMonitorWorkArea = currentMonitor.WorkArea;
            pose.CurrentTaskbarBounds = currentMonitor.TaskbarBounds;
            pose.CurrentTaskbarEdge = currentMonitor.TaskbarEdge;
            pose.CurrentMonitorFloorY = DesktopWorldTransform.ToSimulationLength(
                currentMonitor.FloorY);

            BodyChunk surfaceChunk = Slugcat.BodyChunks[1].SupportingSurfaceId != 0
                ? Slugcat.BodyChunks[1]
                : Slugcat.BodyChunks[0];
            if (surfaceChunk.SupportingSurfaceId != 0)
            {
                pose.CurrentSurfaceId = surfaceChunk.SupportingSurfaceId;
                pose.CurrentSurfaceKind = surfaceChunk.SupportingSurfaceKind;
            }
            else
            {
                surfaceChunk = Slugcat.BodyChunks[0].WallSurfaceId != 0
                    ? Slugcat.BodyChunks[0]
                    : Slugcat.BodyChunks[1];
                pose.CurrentSurfaceId = surfaceChunk.WallSurfaceId;
                pose.CurrentSurfaceKind = surfaceChunk.WallSurfaceKind;
            }
            DesktopSurface currentSurface;
            if (pose.CurrentSurfaceId != 0 && World.TryGetSurface(
                pose.CurrentSurfaceId, pose.CurrentSurfaceKind, out currentSurface))
            {
                pose.CurrentSurfaceLeft = currentSurface.Left;
                pose.CurrentSurfaceRight = currentSurface.Right;
                pose.CurrentSurfaceTop = currentSurface.Top;
            }
            else
            {
                pose.CurrentSurfaceId = 0;
                pose.CurrentSurfaceKind = DesktopSurfaceKind.ScreenEdge;
                pose.CurrentSurfaceLeft = 0.0;
                pose.CurrentSurfaceRight = 0.0;
                pose.CurrentSurfaceTop = 0.0;
            }
            parityDiagnostics.Observe(pose);
            return pose;
        }

        public bool HitTest(Vec2 screenPoint)
        {
            Vec2 simulationPoint = DesktopWorldTransform.ToSimulation(screenPoint);
            return Slugcat.HitTest(simulationPoint) ||
                Vec2.Distance(simulationPoint, Graphics.Head.Position) < 17.0;
        }

        public bool BeginGrab(Vec2 screenPoint)
        {
            Vec2 simulationPoint = DesktopWorldTransform.ToSimulation(screenPoint);
            if (Slugcat.Grab(simulationPoint)) return true;
            if (Vec2.Distance(simulationPoint, Graphics.Head.Position) < 17.0)
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

        public bool CanUseSkin(SlugcatSkin skin, out string reason)
        {
            string missing;
            bool available = SlugcatVisualProfiles.Get(skin).IsAvailable(atlas, out missing);
            reason = available ? null : "Missing local Downpour asset: " + missing;
            return available;
        }

        public bool SetSkin(SlugcatSkin skin)
        {
            string reason;
            if (!CanUseSkin(skin, out reason)) return false;
            Graphics.SetVisualProfile(SlugcatVisualProfiles.Get(skin), atlas);
            return true;
        }
    }
}
