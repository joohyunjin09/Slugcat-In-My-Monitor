using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Physics;

namespace RainWorldDesktopPet.Graphics
{
    // Read-only render-chain guard. It never corrects physics or animation;
    // anomalous tick deltas are only recorded for comparison with the DLL.
    public sealed class ParityDiagnostics
    {
        private sealed class PendingLog
        {
            public string Path;
            public string Text;
        }

        private static readonly object logSync = new object();
        private static readonly Queue<PendingLog> pendingLogs = new Queue<PendingLog>();
        private static readonly AutoResetEvent logSignal = new AutoResetEvent(false);

        static ParityDiagnostics()
        {
            Thread writer = new Thread(LogWriterMain);
            writer.IsBackground = true;
            writer.Name = "Slugcat parity diagnostics";
            writer.Start();
        }

        private readonly string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlugcatInMyMonitor", "parity.log");
        private readonly string surfaceLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlugcatInMyMonitor", "surface-loss.log");
        private readonly string terrainEscapeLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlugcatInMyMonitor", "terrain-escape.log");
        private readonly string airControlLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlugcatInMyMonitor", "air-control.log");
        private readonly string impactLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlugcatInMyMonitor", "terrain-impact.log");
        private long lastLoggedTick = -100;
        private bool hasCrawlFaceSample;
        private double lastCrawlFaceScaleX;
        private int lastCrawlFacing;
        private int lastCrawlBodyAxisSign;
        private readonly double[] lastArmRotation = new double[2];
        private readonly double[] lastArmLength = new double[2];
        private readonly bool[] lastArmVisible = new bool[2];
        private bool hasArmSample;
        private readonly bool[] terrainEscapeActive = new bool[2];
        private long lastAirControlLogTick = -1;
        private long lastImpactSequence;

        public string LogPath { get { return logPath; } }
        public string SurfaceLogPath { get { return surfaceLogPath; } }

        public void ObserveSurfaceState(Slugcat slugcat, DesktopCollisionWorld world,
            VirtualInput input, long tick)
        {
            for (int i = 0; i < slugcat.BodyChunks.Length; i++)
            {
                BodyChunk chunk = slugcat.BodyChunks[i];
                if (chunk.PreviousSupportingSurfaceId != 0 && chunk.SupportingSurfaceId == 0)
                    LogSurfaceLoss(chunk, world, input, tick, true);
                if (chunk.PreviousWallSurfaceId != 0 && chunk.WallSurfaceId == 0)
                    LogSurfaceLoss(chunk, world, input, tick, false);
                ObserveTerrainEscape(chunk, world, tick);
            }
        }

        private void LogSurfaceLoss(BodyChunk chunk, DesktopCollisionWorld world,
            VirtualInput input, long tick, bool floor)
        {
            long id = floor ? chunk.PreviousSupportingSurfaceId : chunk.PreviousWallSurfaceId;
            DesktopSurfaceKind kind = floor
                ? chunk.PreviousSupportingSurfaceKind
                : chunk.PreviousWallSurfaceKind;
            DesktopSurface surface;
            bool stillExists = world.TryGetSurface(id, kind, out surface);
            string reason;
            if (input.DropThrough) reason = "drop-through input";
            else if (input.Jump) reason = "jump input";
            else if (!stillExists) reason = "surface removed or enumeration grace expired";
            else if (floor && (chunk.Position.X < surface.Left - chunk.Radius ||
                               chunk.Position.X > surface.Right + chunk.Radius)) reason = "left surface span";
            else reason = "collision no longer reported contact";

            bool expectedRelease = input.DropThrough || input.Jump;
            if (stillExists && floor)
            {
                bool notPenetrating = chunk.Position.Y + chunk.Radius <= surface.Top + 0.5;
                bool clearlyAboveAndRising = chunk.Position.Y + chunk.Radius < surface.Top - 0.5 &&
                    chunk.Velocity.Y < 0.0;
                bool leftSpan = chunk.Position.X < surface.Left - chunk.Radius ||
                                chunk.Position.X > surface.Right + chunk.Radius;
                expectedRelease |= notPenetrating || clearlyAboveAndRising || leftSpan;
            }
            else if (stillExists && !floor)
            {
                bool movingAway = surface.BlocksPositiveX
                    ? chunk.Velocity.X < 0.0
                    : chunk.Velocity.X > 0.0;
                expectedRelease |= movingAway;
            }
            if (expectedRelease) return;

            Rectangle previous = stillExists ? surface.PreviousWindowBounds : Rectangle.Empty;
            Rectangle current = stillExists ? surface.CurrentWindowBounds : Rectangle.Empty;
            Vec2 movement = stillExists ? surface.MovementVelocity : Vec2.Zero;
            QueueLog(surfaceLogPath, string.Format(
                    "[UNEXPECTED SURFACE LOSS] {0:u} tick={1} snapshot={20} chunk={2} lost={3}/{4} reason={5}; pos={6} last={7} vel={8}; " +
                    "prevContact F/L/R={9}/{10}/{11} current={12}/{13}/{14}; input={15}; " +
                    "surfaceExists={16} previousRect={17} currentRect={18} surfaceVelocity={19}{21}",
                    DateTime.Now, tick, chunk.Index, id, kind, reason,
                    chunk.Position, chunk.LastPosition, chunk.Velocity,
                    chunk.PreviousContactFloor, chunk.PreviousContactLeft, chunk.PreviousContactRight,
                    chunk.ContactFloor, chunk.ContactLeft, chunk.ContactRight, input,
                    stillExists, previous, current, movement,
                    chunk.CollisionSnapshotVersion, Environment.NewLine));
        }

        public void Observe(SlugcatPose pose)
        {
            ObserveAirControl(pose);
            ObserveTerrainImpact(pose);
            ObserveArmRotations(pose);
            string reason = null;
            if (Vec2.Distance(pose.ChunkLast[0], pose.ChunkCurrent[0]) > 45.0 ||
                Vec2.Distance(pose.ChunkLast[1], pose.ChunkCurrent[1]) > 45.0)
                reason = "BodyChunk tick displacement exceeded 45 px";
            else if (Vec2.Distance(pose.Head, pose.Chest) > 35.0)
                reason = "head separated from upper draw position";
            else if (pose.Tail.Length > 0 && Vec2.Distance(pose.Tail[0], pose.Hips) > 24.0)
                reason = "tail root separated from hips";
            else if (Vec2.Distance(pose.HandCurrent[0], pose.ArmConnectionCurrent[0]) > pose.ArmMaxLengths[0] + 0.5 ||
                     Vec2.Distance(pose.HandCurrent[1], pose.ArmConnectionCurrent[1]) > pose.ArmMaxLengths[1] + 0.5)
                reason = "SlugcatHand exceeded connection radius";
            else if (Vec2.Distance(pose.HandLast[0], pose.HandCurrent[0]) > 24.0 ||
                     Vec2.Distance(pose.HandLast[1], pose.HandCurrent[1]) > 24.0)
                reason = "SlugcatHand tick displacement exceeded 24 px";
            else if (!Finite(pose.Head) || !Finite(pose.Chest) || !Finite(pose.Hips))
                reason = "non-finite graphics coordinate";

            int crawlBodyAxisSign = Math.Abs(pose.Chest.X - pose.Hips.X) > 0.5
                ? Math.Sign(pose.Chest.X - pose.Hips.X)
                : 0;
            if (pose.BodyMode == BodyModeIndex.Crawl)
            {
                bool unexplainedFaceFlip = hasCrawlFaceSample &&
                    pose.Facing == lastCrawlFacing &&
                    crawlBodyAxisSign != 0 &&
                    crawlBodyAxisSign == lastCrawlBodyAxisSign &&
                    Math.Sign(pose.FaceScaleX) != Math.Sign(lastCrawlFaceScaleX);
                if (reason == null && unexplainedFaceFlip)
                    reason = "crawl face scaleX flipped without a facing/body-axis reversal";

                hasCrawlFaceSample = true;
                lastCrawlFaceScaleX = pose.FaceScaleX;
                lastCrawlFacing = pose.Facing;
                lastCrawlBodyAxisSign = crawlBodyAxisSign;
            }
            else
            {
                hasCrawlFaceSample = false;
            }

            if (reason == null || pose.SimulationTick - lastLoggedTick < 40) return;
            lastLoggedTick = pose.SimulationTick;
            QueueLog(logPath, string.Format(
                    "{0:u} tick={1} t={2:0.000} {3}; animation={4} body={5} facing={6} look={7} headDir={8} face={9} scaleX={10:0.###}; " +
                    "chunk0 {11}->{12}->{13}; head {14}->{15}->{16}; tail0 {17}->{18}->{19}; " +
                    "leftArm mode={20} hand={21}->{22} target={23} connection={24}; " +
                    "rightArm mode={25} hand={26}->{27} target={28} connection={29}{30}",
                    DateTime.Now, pose.SimulationTick, pose.TimeStacker, reason,
                    pose.Animation, pose.BodyMode, pose.Facing, pose.LookDirection,
                    pose.HeadDirection, pose.SelectedFaceElement, pose.FaceScaleX,
                    pose.ChunkLast[0], pose.ChunkCurrent[0], pose.ChunkRender[0],
                    pose.HeadLast, pose.HeadCurrent, pose.Head,
                    pose.TailLast[0], pose.TailCurrent[0], pose.Tail[0],
                    pose.ArmModes[0], pose.HandLast[0], pose.HandCurrent[0],
                    pose.HandTargets[0], pose.ArmConnectionCurrent[0],
                    pose.ArmModes[1], pose.HandLast[1], pose.HandCurrent[1],
                    pose.HandTargets[1], pose.ArmConnectionCurrent[1], Environment.NewLine));
        }

        private void ObserveTerrainEscape(BodyChunk chunk, DesktopCollisionWorld world, long tick)
        {
            MonitorInfo monitor = world.FindMonitor(chunk.Position);
            double floor = DesktopWorldTransform.ToSimulationLength(monitor.FloorY);
            bool escaped = chunk.Position.Y - chunk.Radius > floor + 1.0;
            if (!escaped)
            {
                terrainEscapeActive[chunk.Index] = false;
                return;
            }
            if (terrainEscapeActive[chunk.Index]) return;
            terrainEscapeActive[chunk.Index] = true;

            StringBuilder available = new StringBuilder();
            for (int i = 0; i < world.Surfaces.Count; i++)
            {
                DesktopSurface surface = world.Surfaces[i];
                if (surface.Bounds.Right < monitor.Bounds.Left ||
                    surface.Bounds.Left > monitor.Bounds.Right ||
                    surface.Bounds.Bottom < monitor.Bounds.Top ||
                    surface.Bounds.Top > monitor.Bounds.Bottom) continue;
                available.AppendFormat("  {0}/{1} {2} LTRB={3},{4},{5},{6}\n",
                    surface.Id, surface.Kind, surface.Label, surface.Left,
                    surface.Top, surface.Right, surface.Bottom);
            }
            QueueLog(terrainEscapeLogPath, string.Format(
                    "[DESKTOP TERRAIN ESCAPE] {0:u}\nTick: {1}\nMonitor: {2}/{3}\nMonitor Bounds: {4}\nWorkArea: {5}\nFloorY: {6:0.###}\nChunk: {7}\nChunk Position: {8}\nLast Position: {9}\nVelocity: {10}\nLast Surface: {11}/{12}\nCollision Snapshot: {13}\nAvailable Surfaces:\n{14}\n",
                    DateTime.Now, tick, monitor.Name, monitor.TerrainId, monitor.Bounds,
                    monitor.WorkArea, floor, chunk.Index, chunk.Position,
                    chunk.LastPosition, chunk.Velocity, chunk.PreviousSupportingSurfaceId,
                    chunk.PreviousSupportingSurfaceKind, chunk.CollisionSnapshotVersion,
                    available));
        }

        private void ObserveAirControl(SlugcatPose pose)
        {
            if (!pose.IsAirborne || pose.InputX == pose.PreviousInputX ||
                pose.SimulationTick == lastAirControlLogTick) return;
            lastAirControlLogTick = pose.SimulationTick;
            QueueLog(airControlLogPath, string.Format(
                    "[AIR CONTROL] {0:u}\nTick: {1}\nInput X: {2}\nPrevious Input X: {3}\nVelocity Before: {4:0.###}, {5:0.###}\nVelocity After: {6:0.###}, {7:0.###}\nAnimation: {8}\nBodyMode: {9}\nOriginal-equivalent branch: {10}\n\n",
                    DateTime.Now, pose.SimulationTick, pose.InputX, pose.PreviousInputX,
                    pose.AirHorizontalVelocityBefore[0], pose.AirHorizontalVelocityBefore[1],
                    pose.AirHorizontalVelocityAfter[0], pose.AirHorizontalVelocityAfter[1],
                    pose.Animation, pose.BodyMode, pose.AirControlBranch));
        }

        private void ObserveTerrainImpact(SlugcatPose pose)
        {
            if (pose.TerrainImpactSequence == 0 ||
                pose.TerrainImpactSequence == lastImpactSequence) return;
            lastImpactSequence = pose.TerrainImpactSequence;
            QueueLog(impactLogPath, string.Format(
                    "[TERRAIN IMPACT] {0:u}\nTick: {1}\nChunk: {2}\nSurface: {3}/{4}\nPreImpact Velocity: {5}\nPostImpact Velocity: {6}\nCollision Normal: {7}\nImpact Direction: {8}\nImpact Speed: {9:0.###}\nFirst Contact: {10}\nOriginal Calculated Stun: {11}\nApplied Impact Stun: {12}\nDesktop Result: {13}\nOriginally Lethal: {14}\nSafety Override: {15}\nImpact Stun Deadline Tick: {16}\nFinal Stun Counter: {17}\nDead: {18}\nFace Element: {19}\n\n",
                    DateTime.Now, pose.SimulationTick, pose.ImpactBodyChunk,
                    pose.ImpactSurfaceId, pose.ImpactSurfaceKind, pose.PreImpactVelocity,
                    pose.PostImpactVelocity, pose.ImpactCollisionNormal,
                    pose.ImpactDirection, pose.ImpactSpeed, pose.ImpactFirstContact,
                    pose.CalculatedImpactStun, pose.AppliedImpactStun,
                    pose.DesktopImpactResult, pose.ImpactWasOriginallyLethal,
                    pose.ImpactSafetyOverrideApplied, pose.ImpactStunDeadlineTick,
                    pose.StunCounter, pose.ImpactCausedDeath || pose.Dead,
                    pose.SelectedFaceElement));
        }

        private void ObserveArmRotations(SlugcatPose pose)
        {
            for (int i = 0; i < 2; i++)
            {
                double rotation = pose.ArmRotations[i];
                double armLength = Vec2.Distance(pose.Hands[i], pose.ArmShoulders[i]);
                if (hasArmSample && lastArmVisible[i] && pose.ArmVisible[i] &&
                    lastArmLength[i] >= 6.0 && armLength >= 6.0)
                {
                    double delta = ShortestAngleDelta(lastArmRotation[i], rotation);
                    if (Math.Abs(delta) > 120.0)
                    {
                        QueueLog(logPath, string.Format(
                                "[ARM ROTATION SPIKE] {0:u} tick={1} arm={2} previous={3:0.###} current={4:0.###} shortestDelta={5:0.###}; " +
                                "hand={6} shoulder={7} direction={8} distance={9:0.###} target={10} connection={11} mode={12} retract={13} body={14} animation={15} scaleY={16:0.###}{17}",
                                DateTime.Now, pose.SimulationTick, i, lastArmRotation[i], rotation, delta,
                                pose.Hands[i], pose.ArmShoulders[i], pose.ArmDirections[i],
                                Vec2.Distance(pose.Hands[i], pose.ArmShoulders[i]), pose.HandTargets[i],
                                pose.ArmConnections[i], pose.ArmModes[i], pose.ArmRetractCounters[i],
                                pose.BodyMode, pose.Animation, pose.ArmScaleY[i], Environment.NewLine));
                    }
                }
                lastArmRotation[i] = rotation;
                lastArmLength[i] = armLength;
                lastArmVisible[i] = pose.ArmVisible[i];
            }
            hasArmSample = true;
        }

        private static double ShortestAngleDelta(double from, double to)
        {
            double delta = to - from;
            while (delta > 180.0) delta -= 360.0;
            while (delta < -180.0) delta += 360.0;
            return delta;
        }

        private static bool Finite(Vec2 value)
        {
            return !double.IsNaN(value.X) && !double.IsNaN(value.Y) &&
                   !double.IsInfinity(value.X) && !double.IsInfinity(value.Y);
        }

        private static void QueueLog(string path, string value)
        {
            lock (logSync)
            {
                while (pendingLogs.Count >= 128) pendingLogs.Dequeue();
                pendingLogs.Enqueue(new PendingLog { Path = path, Text = value });
            }
            logSignal.Set();
        }

        private static void LogWriterMain()
        {
            while (true)
            {
                PendingLog entry = null;
                lock (logSync)
                    if (pendingLogs.Count > 0) entry = pendingLogs.Dequeue();
                if (entry == null)
                {
                    logSignal.WaitOne(1000);
                    continue;
                }
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.Path));
                    File.AppendAllText(entry.Path, entry.Text);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
