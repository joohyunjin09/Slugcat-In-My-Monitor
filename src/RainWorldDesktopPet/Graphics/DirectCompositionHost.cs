using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RainWorldDesktopPet.Graphics
{
    public sealed class DirectCompositionHost : IDisposable
    {
        private const int MaximumSurfaces = 8;
        private readonly CompositionSurface[] surfaces = new CompositionSurface[MaximumSurfaces];
        private IntPtr nativeRenderer;
        private Rectangle desktopBounds;
        private bool disposed;

        public DirectCompositionHost(IntPtr windowHandle, Rectangle desktopBounds)
        {
            this.desktopBounds = desktopBounds;
            int result = NativeMethods.Create(windowHandle, out nativeRenderer);
            ThrowIfFailed(result, "Could not initialize DirectComposition");
        }

        public void SetDesktopBounds(Rectangle bounds)
        {
            desktopBounds = bounds;
        }

        public CompositionSurface PrepareSurface(int slot, Rectangle bounds)
        {
            if (slot < 0 || slot >= MaximumSurfaces) throw new ArgumentOutOfRangeException("slot");
            CompositionSurface surface = surfaces[slot];
            Size currentSize = surface == null ? Size.Empty :
                new Size(surface.Width, surface.Height);
            Size reusableSize = SelectReusableSurfaceSize(currentSize, bounds.Size);
            int width = reusableSize.Width;
            int height = reusableSize.Height;
            if (surface == null || surface.Width < width || surface.Height < height)
            {
                if (surface != null) surface.Dispose();
                surface = new CompositionSurface(width, height);
                surfaces[slot] = surface;
            }
            int centerX = bounds.Left + bounds.Width / 2;
            int centerY = bounds.Top + bounds.Height / 2;
            surface.Bounds = new Rectangle(centerX - surface.Width / 2,
                centerY - surface.Height / 2, surface.Width, surface.Height);
            return surface;
        }

        public static Size SelectReusableSurfaceSize(Size current, Size required)
        {
            if (required.Width < 1 || required.Height < 1)
                throw new ArgumentOutOfRangeException("required");
            return new Size(Math.Max(current.Width, required.Width),
                Math.Max(current.Height, required.Height));
        }

        public void ResetSurfaces()
        {
            if (disposed) return;
            int result = NativeMethods.Commit(nativeRenderer, 0);
            ThrowIfFailed(result, "Could not reset DirectComposition surfaces");
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i] != null) surfaces[i].Dispose();
                surfaces[i] = null;
            }
        }

        public void Present(int slot)
        {
            CompositionSurface surface = surfaces[slot];
            if (surface == null) throw new InvalidOperationException("The composition surface was not prepared.");
            surface.Graphics.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
            BitmapData data = surface.Bitmap.LockBits(new Rectangle(0, 0, surface.Width, surface.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int result = NativeMethods.Present(nativeRenderer, slot, data.Scan0,
                    (uint)surface.Width, (uint)surface.Height, (uint)Math.Abs(data.Stride),
                    surface.Bounds.X - desktopBounds.X, surface.Bounds.Y - desktopBounds.Y);
                ThrowIfFailed(result, "Could not present a DirectComposition surface");
            }
            finally
            {
                surface.Bitmap.UnlockBits(data);
            }
        }

        public void Commit(int activeSurfaceCount)
        {
            uint activeMask = activeSurfaceCount <= 0 ? 0u : (1u << activeSurfaceCount) - 1u;
            int result = NativeMethods.Commit(nativeRenderer, activeMask);
            ThrowIfFailed(result, "Could not commit the DirectComposition frame");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (nativeRenderer != IntPtr.Zero)
            {
                NativeMethods.Destroy(nativeRenderer);
                nativeRenderer = IntPtr.Zero;
            }
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i] != null) surfaces[i].Dispose();
                surfaces[i] = null;
            }
        }

        private static void ThrowIfFailed(int result, string operation)
        {
            if (result >= 0) return;
            Exception exception = Marshal.GetExceptionForHR(result);
            throw new InvalidOperationException(operation + " (HRESULT 0x" +
                result.ToString("X8") + ").", exception);
        }

        public sealed class CompositionSurface : IDisposable
        {
            private readonly Bitmap bitmap;
            private readonly System.Drawing.Graphics graphics;

            internal CompositionSurface(int width, int height)
            {
                bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                graphics = System.Drawing.Graphics.FromImage(bitmap);
            }

            public int Width { get { return bitmap.Width; } }
            public int Height { get { return bitmap.Height; } }
            public Rectangle Bounds { get; internal set; }
            public System.Drawing.Graphics Graphics { get { return graphics; } }
            internal Bitmap Bitmap { get { return bitmap; } }

            public void Dispose()
            {
                graphics.Dispose();
                bitmap.Dispose();
            }
        }

        private static class NativeMethods
        {
            private const string Library = "SlugcatInMyMonitor.DirectComposition.dll";

            [DllImport(Library, EntryPoint = "SlugcatDCompCreate", CallingConvention = CallingConvention.StdCall)]
            internal static extern int Create(IntPtr windowHandle, out IntPtr renderer);

            [DllImport(Library, EntryPoint = "SlugcatDCompPresent", CallingConvention = CallingConvention.StdCall)]
            internal static extern int Present(IntPtr renderer, int slot, IntPtr pixels,
                uint width, uint height, uint stride, float x, float y);

            [DllImport(Library, EntryPoint = "SlugcatDCompCommit", CallingConvention = CallingConvention.StdCall)]
            internal static extern int Commit(IntPtr renderer, uint activeMask);

            [DllImport(Library, EntryPoint = "SlugcatDCompDestroy", CallingConvention = CallingConvention.StdCall)]
            internal static extern void Destroy(IntPtr renderer);
        }
    }
}
