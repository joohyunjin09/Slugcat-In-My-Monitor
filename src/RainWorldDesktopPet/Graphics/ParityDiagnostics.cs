using System;
using System.IO;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;

namespace RainWorldDesktopPet.Graphics
{
    // Read-only render-chain guard. It never corrects physics or animation;
    // anomalous tick deltas are only recorded for comparison with the DLL.
    public sealed class ParityDiagnostics
    {
        private readonly string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlugcatInMyMonitor", "parity.log");
        private long lastLoggedTick = -100;
        private bool hasCrawlFaceSample;
        private double lastCrawlFaceScaleX;
        private int lastCrawlFacing;
        private int lastCrawlBodyAxisSign;

        public string LogPath { get { return logPath; } }

        public void Observe(SlugcatPose pose)
        {
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
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.AppendAllText(logPath, string.Format(
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
            catch (Exception)
            {
            }
        }

        private static bool Finite(Vec2 value)
        {
            return !double.IsNaN(value.X) && !double.IsNaN(value.Y) &&
                   !double.IsInfinity(value.X) && !double.IsInfinity(value.Y);
        }
    }
}
