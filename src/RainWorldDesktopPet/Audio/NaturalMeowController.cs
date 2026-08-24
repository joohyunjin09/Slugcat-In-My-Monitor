using System;
using RainWorldDesktopPet.AI;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Graphics;
using RainWorldDesktopPet.Workshop;

namespace RainWorldDesktopPet.Audio
{
    public sealed class NaturalMeowController : IDisposable
    {
        private const double MinimumCooldownSeconds = 22.0;
        private readonly PushToMeowLibrary library;
        private readonly WorkshopAudioPlayer audio;
        private readonly WorkshopLog log;
        private readonly Random random;
        private double nextCandidateTime;
        private double playbackStartTime = double.NegativeInfinity;
        private double playbackEndTime = double.NegativeInfinity;
        private double lastMeowEndTime = double.NegativeInfinity;
        private DesktopBehavior previousBehavior;
        private bool previousMouseAttention;
        private bool previousGrabbed;
        private bool shortMeow;
        private string currentSlugcatId;
        private MeowSoundVariation currentVariation;

        public NaturalMeowController(PushToMeowLibrary library, WorkshopLog log, int seed)
        {
            this.library = library;
            this.log = log ?? new WorkshopLog(false);
            random = new Random(seed);
            audio = new WorkshopAudioPlayer(this.log);
            nextCandidateTime = 25.0 + random.NextDouble() * 90.0;
        }

        public bool IsAvailable { get { return library != null && library.IsAvailable; } }
        public bool IsMeowing(double now) { return currentVariation != null && now < playbackEndTime; }
        public double PlaybackEndTime { get { return playbackEndTime; } }
        public string CurrentAsset { get { return currentVariation == null ? null : currentVariation.FilePath; } }

        public void Step(double now, string slugcatId, DesktopBehavior behavior, double stillness,
            double mouseDistance, bool mouseAttention, bool grabbed, bool conscious)
        {
            if (!IsAvailable) return;
            if (currentVariation != null && now >= playbackEndTime)
            {
                audio.Stop();
                currentVariation = null;
                lastMeowEndTime = now;
                ScheduleNormal(now, behavior, stillness);
            }

            bool interactionEnded = (previousMouseAttention && !mouseAttention) ||
                                    (previousGrabbed && !grabbed);
            bool woke = previousBehavior == DesktopBehavior.Sleep && behavior != DesktopBehavior.Sleep;
            bool specialFinished = IsSpecial(previousBehavior) && !IsSpecial(behavior);
            if (interactionEnded || woke || specialFinished)
            {
                double contextual = now + 3.0 + random.NextDouble() * (interactionEnded ? 12.0 : 20.0);
                double earliest = lastMeowEndTime + MinimumCooldownSeconds;
                nextCandidateTime = Math.Min(nextCandidateTime, Math.Max(contextual, earliest));
            }

            previousBehavior = behavior;
            previousMouseAttention = mouseAttention;
            previousGrabbed = grabbed;
            if (currentVariation != null || !conscious || grabbed || now < nextCandidateTime ||
                now < lastMeowEndTime + MinimumCooldownSeconds) return;

            double chance = 0.38;
            if (stillness > 100.0) chance += 0.25;
            if (mouseDistance < 130.0) chance += 0.20;
            if (behavior == DesktopBehavior.Idle || behavior == DesktopBehavior.Sit) chance += 0.14;
            if (behavior == DesktopBehavior.Sleep) chance -= 0.32;
            if (behavior == DesktopBehavior.Walk || behavior == DesktopBehavior.Explore) chance -= 0.10;
            chance = MathUtil.Clamp(chance, 0.05, 0.92);
            if (random.NextDouble() <= chance)
            {
                bool preferLong = interactionEnded || woke || (stillness > 180.0 && random.NextDouble() < 0.45);
                Start(now, slugcatId, !preferLong && random.NextDouble() < 0.76);
            }
            else
            {
                nextCandidateTime = now + 8.0 + random.NextDouble() * 28.0;
            }
        }

        private void Start(double now, string slugcatId, bool isShort)
        {
            MeowSoundVariation variation = library.Choose(slugcatId, isShort);
            if (variation == null || !audio.TryPlay(variation))
            {
                nextCandidateTime = now + 30.0 + random.NextDouble() * 60.0;
                return;
            }
            currentVariation = variation;
            currentSlugcatId = slugcatId;
            shortMeow = isShort;
            playbackStartTime = now;
            playbackEndTime = now + Math.Max(0.08,
                variation.DurationSeconds / Math.Max(0.1f, variation.PlaybackPitch));
            nextCandidateTime = playbackEndTime + MinimumCooldownSeconds;
            log.Info("PushToMeow", "Playing " + slugcatId + " " + (isShort ? "short" : "long") +
                " variation " + variation.AssetName + " (" +
                (playbackEndTime - playbackStartTime).ToString("0.000") + "s, pitch " +
                variation.PlaybackPitch.ToString("0.00") + ", volume " +
                variation.PlaybackVolume.ToString("0.00") + ")");
        }

        private void ScheduleNormal(double now, DesktopBehavior behavior, double stillness)
        {
            double minimum = 35.0;
            double range = 135.0;
            if (behavior == DesktopBehavior.Idle || behavior == DesktopBehavior.Sit || stillness > 120.0)
            {
                minimum = 24.0;
                range = 95.0;
            }
            else if (behavior == DesktopBehavior.Sleep)
            {
                minimum = 70.0;
                range = 150.0;
            }
            nextCandidateTime = now + minimum + random.NextDouble() * range;
        }

        public void ApplyPose(SlugcatPose pose, double now)
        {
            if (pose == null || !IsMeowing(now)) return;
            double duration = Math.Max(0.001, playbackEndTime - playbackStartTime);
            double progress = MathUtil.Clamp01((now - playbackStartTime) / duration);
            pose.IsMeowing = true;
            pose.MeowProgress = progress;
            pose.MeowDurationSeconds = duration;
            pose.MeowIsShort = shortMeow;
            pose.MeowAsset = currentVariation.AssetName;

            // Push To Meow's DoMeowAnim waits 33 ms, looks almost straight up and
            // calls Player.Blink(9/11). Keep that pose for the decoded clip length
            // so long Workshop clips cannot outlive their desktop animation.
            if (now - playbackStartTime >= 0.033)
            {
                pose.LookDirection = new Vec2(0.0, -1.0);
                pose.Blink = true;
            }

            // The DLL gives Spearmaster's physical tail alternating up/down velocity.
            // The desktop renderer translates that to a damped, segment-weighted wiggle.
            if (string.Equals(currentSlugcatId, "Spear", StringComparison.OrdinalIgnoreCase) &&
                pose.Tail != null)
            {
                double envelope = 1.0 - progress;
                for (int index = 0; index < pose.Tail.Length; index++)
                {
                    double weight = (index + 1.0) / pose.Tail.Length;
                    double phase = progress * Math.PI * 5.0 + index * 0.55;
                    pose.Tail[index] += new Vec2(Math.Sin(phase) * 2.2 * weight * envelope,
                        Math.Cos(phase) * 1.2 * weight * envelope);
                }
            }
        }

        private static bool IsSpecial(DesktopBehavior behavior)
        {
            return behavior == DesktopBehavior.Jump || behavior == DesktopBehavior.ClimbWindow ||
                   behavior == DesktopBehavior.DropDown || behavior == DesktopBehavior.BalanceNearEdge;
        }

        public void Dispose()
        {
            audio.Dispose();
        }
    }
}
