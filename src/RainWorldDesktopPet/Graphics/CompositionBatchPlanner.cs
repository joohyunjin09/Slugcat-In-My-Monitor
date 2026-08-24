using System;
using System.Collections.Generic;
using System.Drawing;

namespace RainWorldDesktopPet.Graphics
{
    public sealed class CompositionBatch
    {
        internal CompositionBatch(Rectangle bounds, int surfaceIndex)
        {
            Bounds = bounds;
            SurfaceIndices = new List<int> { surfaceIndex };
        }

        public Rectangle Bounds { get; internal set; }
        public List<int> SurfaceIndices { get; private set; }
    }

    public static class CompositionBatchPlanner
    {
        public static IList<CompositionBatch> Plan(IList<Rectangle> surfaceBounds,
            int sizeQuantum)
        {
            if (surfaceBounds == null) throw new ArgumentNullException("surfaceBounds");
            if (sizeQuantum < 1) throw new ArgumentOutOfRangeException("sizeQuantum");

            List<CompositionBatch> batches = new List<CompositionBatch>(surfaceBounds.Count);
            for (int i = 0; i < surfaceBounds.Count; i++)
                batches.Add(new CompositionBatch(surfaceBounds[i], i));

            while (TryMergeBestPair(batches, sizeQuantum)) { }
            batches.Sort(delegate(CompositionBatch left, CompositionBatch right)
            {
                return left.SurfaceIndices[0].CompareTo(right.SurfaceIndices[0]);
            });
            return batches;
        }

        private static bool TryMergeBestPair(List<CompositionBatch> batches,
            int sizeQuantum)
        {
            int bestLeft = -1;
            int bestRight = -1;
            Rectangle bestBounds = Rectangle.Empty;
            long bestSaving = 0;

            for (int left = 0; left < batches.Count; left++)
            {
                for (int right = left + 1; right < batches.Count; right++)
                {
                    Rectangle union = Rectangle.Union(batches[left].Bounds,
                        batches[right].Bounds);
                    Rectangle rounded = RoundAroundCenter(union, sizeQuantum);
                    long separateArea = Area(batches[left].Bounds) +
                        Area(batches[right].Bounds);
                    long saving = separateArea - Area(rounded);
                    if (saving <= bestSaving) continue;
                    bestSaving = saving;
                    bestLeft = left;
                    bestRight = right;
                    bestBounds = rounded;
                }
            }

            if (bestLeft < 0) return false;
            CompositionBatch target = batches[bestLeft];
            CompositionBatch source = batches[bestRight];
            target.Bounds = bestBounds;
            target.SurfaceIndices.AddRange(source.SurfaceIndices);
            target.SurfaceIndices.Sort();
            batches.RemoveAt(bestRight);
            return true;
        }

        private static Rectangle RoundAroundCenter(Rectangle bounds, int quantum)
        {
            int width = RoundUp(bounds.Width, quantum);
            int height = RoundUp(bounds.Height, quantum);
            int centerX = bounds.Left + bounds.Width / 2;
            int centerY = bounds.Top + bounds.Height / 2;
            return new Rectangle(centerX - width / 2, centerY - height / 2,
                width, height);
        }

        private static int RoundUp(int value, int quantum)
        {
            return ((Math.Max(1, value) + quantum - 1) / quantum) * quantum;
        }

        private static long Area(Rectangle bounds)
        {
            return (long)Math.Max(0, bounds.Width) * Math.Max(0, bounds.Height);
        }
    }
}
