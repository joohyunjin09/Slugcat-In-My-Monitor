using System;

namespace RainWorldDesktopPet.Core
{
    public sealed class FixedTimeStep
    {
        private readonly double stepSeconds;
        private double accumulator;

        public FixedTimeStep(double stepSeconds)
        {
            if (stepSeconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException("stepSeconds");
            }

            this.stepSeconds = stepSeconds;
        }

        public double StepSeconds { get { return stepSeconds; } }
        public double AccumulatorSeconds { get { return accumulator; } }
        public double Alpha { get { return MathUtil.Clamp01(accumulator / stepSeconds); } }

        public void AddElapsed(double seconds)
        {
            accumulator += Math.Max(0.0, seconds);
        }

        public bool ConsumeStep()
        {
            if (accumulator + 0.0000001 < stepSeconds)
            {
                return false;
            }

            accumulator -= stepSeconds;
            return true;
        }

        public void Reset()
        {
            accumulator = 0.0;
        }

        public void ClampAccumulator(double maximumSeconds)
        {
            if (maximumSeconds < 0.0)
                throw new ArgumentOutOfRangeException("maximumSeconds");
            accumulator = Math.Min(accumulator, maximumSeconds);
        }
    }
}
