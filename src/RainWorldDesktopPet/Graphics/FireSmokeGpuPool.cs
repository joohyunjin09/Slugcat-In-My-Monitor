using System;
using RainWorldDesktopPet.RainWorld;

namespace RainWorldDesktopPet.Graphics
{
    internal sealed class FireSmokeGpuLease : IDisposable
    {
        private bool disposed;

        internal FireSmokeGpuLease(FireSmokeGpuRenderer renderer, bool ownsAssets)
        {
            Renderer = renderer;
            OwnsAssets = ownsAssets;
        }

        internal FireSmokeGpuRenderer Renderer { get; private set; }
        internal bool OwnsAssets { get; private set; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            FireSmokeGpuPool.Release();
            Renderer = null;
        }
    }

    internal static class FireSmokeGpuPool
    {
        private static readonly object Sync = new object();
        private static FireSmokeGpuRenderer renderer;
        private static FireSmokeShaderAssets ownedAssets;
        private static int leaseCount;

        internal static FireSmokeGpuLease TryAcquire(FireSmokeShaderAssets assets,
            out string status)
        {
            if (assets == null)
            {
                status = "Original FireSmoke GPU renderer unavailable: original textures were not loaded.";
                return null;
            }

            lock (Sync)
            {
                if (renderer == null)
                {
                    renderer = FireSmokeGpuRenderer.TryCreate(assets, out status);
                    if (renderer == null) return null;
                    ownedAssets = assets;
                }
                else
                {
                    status = "Original FireSmoke GPU renderer shared across active Slugcats.";
                }

                leaseCount++;
                return new FireSmokeGpuLease(renderer,
                    ReferenceEquals(assets, ownedAssets));
            }
        }

        internal static void Release()
        {
            lock (Sync)
            {
                if (leaseCount <= 0) return;
                leaseCount--;
                if (leaseCount != 0) return;

                if (renderer != null) renderer.Dispose();
                if (ownedAssets != null) ownedAssets.Dispose();
                renderer = null;
                ownedAssets = null;
            }
        }
    }
}
