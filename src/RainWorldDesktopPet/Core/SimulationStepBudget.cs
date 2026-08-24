using System;

namespace RainWorldDesktopPet.Core
{
    public sealed class SimulationStepBudget
    {
        private int rotation;

        public void Assign(int slugcatCount, int[] limits)
        {
            if (slugcatCount < 0) throw new ArgumentOutOfRangeException("slugcatCount");
            if (limits == null) throw new ArgumentNullException("limits");
            if (limits.Length < slugcatCount) throw new ArgumentException(
                "The step limit buffer is smaller than the Slugcat count.", "limits");
            if (slugcatCount == 0) return;

            // Preserve the original three-step recovery for small counts, but
            // cap a crowded frame at ten total simulation steps. Every
            // Slugcat receives one step before rotating catch-up slots.
            int totalBudget = Math.Min(slugcatCount * 3, Math.Max(slugcatCount, 10));
            for (int i = 0; i < slugcatCount; i++) limits[i] = 1;

            int remaining = totalBudget - slugcatCount;
            int index = rotation % slugcatCount;
            while (remaining > 0)
            {
                if (limits[index] < 3)
                {
                    limits[index]++;
                    remaining--;
                }
                index = (index + 1) % slugcatCount;
            }
            rotation = (rotation + 1) % slugcatCount;
        }
    }
}
