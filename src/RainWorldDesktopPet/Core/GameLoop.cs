using System;
using System.Collections.Generic;
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
    public sealed class GameLoop : IDisposable
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly FixedTimeStep fixedTimeStep = new FixedTimeStep(SimulationConstants.LogicStepSeconds);
        private readonly MouseTracker mouse = new MouseTracker();
        private readonly MouseAttentionState mouseAttention = new MouseAttentionState();
        private readonly bool managesWorldRefresh;
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
        private bool disposed;
        private int offscreenTicks;
        private Vec2 lastVisibleCenter;
        private bool hasVisibleCenter;

        public GameLoop(IntPtr overlayHandle, RainWorldInstallation installation, SlugcatVariant variant)
            : this(overlayHandle, installation, variant, SlugcatSkin.Default)
        {
        }

        public GameLoop(IntPtr overlayHandle, RainWorldInstallation installation,
            SlugcatVariant variant, SlugcatSkin skin)
            : this(overlayHandle, installation, variant, skin, 0)
        {
        }

        public GameLoop(IntPtr overlayHandle, RainWorldInstallation installation,
            SlugcatVariant variant, SlugcatSkin skin, int spawnIndex)
            : this(overlayHandle, installation, variant, skin, spawnIndex, null)
        {
        }

        internal GameLoop(IntPtr overlayHandle, RainWorldInstallation installation,
            SlugcatVariant variant, SlugcatSkin skin, int spawnIndex,
            DesktopCollisionWorld sharedWorld)
        {
            Installation = installation;
            managesWorldRefresh = sharedWorld == null;
            World = sharedWorld ?? new DesktopCollisionWorld(new WindowEnumerator());
            if (managesWorldRefresh) World.Refresh(overlayHandle);
            Point cursor = System.Windows.Forms.Cursor.Position;
            MonitorInfo monitor = MonitorManager.FindNearest(cursor);
            double spawnMargin = DesktopWorldTransform.ToDesktopLength(70.0);
            double spawnX = MathUtil.Clamp(cursor.X, monitor.WorkArea.Left + spawnMargin,
                monitor.WorkArea.Right - spawnMargin);
            if (spawnIndex > 0)
            {
                int step = (spawnIndex + 1) / 2;
                int direction = spawnIndex % 2 == 1 ? 1 : -1;
                spawnX = MathUtil.Clamp(spawnX + direction * step *
                    DesktopWorldTransform.ToDesktopLength(48.0),
                    monitor.WorkArea.Left + spawnMargin,
                    monitor.WorkArea.Right - spawnMargin);
            }
            Vec2 spawn = DesktopWorldTransform.ToSimulation(new Vec2(spawnX,
                monitor.WorkArea.Bottom - DesktopWorldTransform.ToDesktopLength(
                    SimulationConstants.HipsChunkRadius + 2.0)));
            Slugcat = new Slugcat(spawn, variant);
            lastVisibleCenter = Slugcat.Center;
            hasVisibleCenter = true;
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
        public readonly RainWorldInstallation Installation;
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
        public int OffscreenRecoveryCount { get; private set; }

        public bool TryGetAtlasSprite(string name, bool original, out AtlasSprite sprite)
        {
            sprite = null;
            if (atlas == null) return false;
            return original ? atlas.TryGetBase(name, out sprite) : atlas.TryGet(name, out sprite);
        }

        public bool SetPartAtlas(string part, string imagePath, string metadataPath, out string reason)
        {
            reason = null;
            if (atlas == null)
            {
                reason = "The original Rain World atlas is unavailable.";
                return false;
            }
            try
            {
                RainWorldAtlas replacement = RainWorldAtlasLoader.Load(imagePath, metadataPath);
                atlas.SetPartOverride(part, replacement);
                return true;
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        public void ClearPartAtlas(string part)
        {
            if (atlas != null) atlas.ClearPartOverride(part);
        }

        public Color GetPartColor(string part) { return Graphics.GetPartColor(part); }
        public void SetPartColor(string part, Color color) { Graphics.SetPartColor(part, color); }
        public void ClearPartColors() { Graphics.ClearPartColors(); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Renderer.Dispose();
        }

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

            if (managesWorldRefresh)
            {
                surfaceRefreshAccumulator += elapsed;
                if (surfaceRefreshAccumulator >= SimulationConstants.WindowRefreshSeconds)
                {
                    surfaceRefreshAccumulator %= SimulationConstants.WindowRefreshSeconds;
                    World.Refresh(overlayHandle);
                    ApplyMovingSurfaceDelta();
                }
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
                RecoverFromDesktopEscape();
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

        public void ApplyMovingSurfaceDelta()
        {
            if (Paused) return;
            Vec2 surfaceDelta = Slugcat.ApplyMovingSurfaceDelta(World);
            Graphics.ApplyMovingSurfaceDelta(surfaceDelta);
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

        private void RecoverFromDesktopEscape()
        {
            if (Slugcat.IsGrabbed)
            {
                offscreenTicks = 0;
                return;
            }

            IList<MonitorInfo> monitors = World.CurrentSnapshot.Monitors;
            if (DesktopRecovery.IsNearAnyMonitor(Slugcat.Center, monitors))
            {
                lastVisibleCenter = Slugcat.Center;
                hasVisibleCenter = true;
                offscreenTicks = 0;
                return;
            }

            // A throw through the top of a monitor is a temporary ceiling
            // excursion, not a lost pet. Keep simulating the original upward
            // momentum and gravity so the Slugcat naturally falls back into
            // the same monitor. Horizontal and lower-screen escapes still use
            // the normal timed/hard recovery below.
            if (DesktopRecovery.IsAboveMonitorCeiling(Slugcat.Center, monitors))
            {
                offscreenTicks = 0;
                return;
            }

            offscreenTicks++;
            bool hardEscape = DesktopRecovery.IsFarOutsideVirtualDesktop(
                Slugcat.Center, World.VirtualBounds);
            if (!hardEscape && offscreenTicks < DesktopRecovery.OffscreenGraceTicks) return;

            Vec2 preferred = hasVisibleCenter ? lastVisibleCenter : Slugcat.Center;
            Vec2 safeHips = DesktopRecovery.FindSafeHipsPosition(preferred, monitors,
                SimulationConstants.HipsChunkRadius);
            Vec2 delta = safeHips - Slugcat.BodyChunks[1].Position;
            Slugcat.Reposition(safeHips);
            Graphics.ApplyMovingSurfaceDelta(delta);
            AI.Attention.SetTarget(AttentionKind.RandomPoint,
                Slugcat.Center + new Vec2(Slugcat.State.Facing * 60.0, -20.0));
            lastVisibleCenter = Slugcat.Center;
            hasVisibleCenter = true;
            offscreenTicks = 0;
            OffscreenRecoveryCount++;
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
