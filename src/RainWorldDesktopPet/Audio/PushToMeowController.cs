using System;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Graphics;

namespace RainWorldDesktopPet.Audio
{
    public sealed class PushToMeowController
    {
        private readonly IPushToMeowSource source;
        private readonly Random random;
        private long nextMeowTick;

        public PushToMeowController(IPushToMeowSource source, int seed)
        {
            this.source = source;
            random = new Random(seed);
            nextMeowTick = SecondsToTicks(12.0 + random.NextDouble() * 18.0);
        }

        public long NextMeowTick { get { return nextMeowTick; } }

        public void Step(Slugcat slugcat, SlugcatGraphics graphics,
            double fullnessRatio, long simulationTick)
        {
            if (slugcat == null || graphics == null || simulationTick < nextMeowTick)
                return;
            if (source == null || !source.PushToMeowAvailable || source.Muted)
            {
                nextMeowTick = simulationTick + SecondsToTicks(5.0);
                return;
            }
            if (slugcat.State.Dead || !slugcat.State.Conscious ||
                slugcat.State.StunCounter > 0 || slugcat.IsGrabbed ||
                slugcat.State.Animation == AnimationIndex.Sleep)
            {
                nextMeowTick = simulationTick + SecondsToTicks(5.0);
                return;
            }

            bool shortMeow = random.Next(2) == 0;
            PushToMeowSound sound;
            if (!source.TryResolveMeow(slugcat.SelectedSlugcat.Id,
                slugcat.PupAppearance, shortMeow, out sound))
            {
                nextMeowTick = simulationTick + SecondsToTicks(5.0);
                return;
            }

            // Push To Meow calls DoMeowAnim before PlayMeowSound. Schedule the
            // same delayed face/tail state before dispatching the audio event.
            graphics.TriggerMeowAnimation(shortMeow);
            slugcat.EmitSound(sound.SoundId, slugcat.Center,
                sound.Volume, sound.Pitch, 10);
            nextMeowTick = simulationTick + SecondsToTicks(
                CalculateIntervalSeconds(fullnessRatio, random.NextDouble()));
        }

        // Push To Meow's hungry SlugNPC branch uses a fullness-dependent
        // 15..105 second window. Desktop pets meow even when fed, so retain
        // that shape while narrowing it to a natural 24..85 second range.
        public static double CalculateIntervalSeconds(double fullnessRatio,
            double randomUnit)
        {
            fullnessRatio = MathUtil.Clamp01(fullnessRatio);
            randomUnit = MathUtil.Clamp01(randomUnit);
            return MathUtil.Lerp(24.0, 65.0, fullnessRatio) + randomUnit * 20.0;
        }

        private static long SecondsToTicks(double seconds)
        {
            return (long)Math.Ceiling(seconds * SimulationConstants.LogicTicksPerSecond);
        }
    }
}
