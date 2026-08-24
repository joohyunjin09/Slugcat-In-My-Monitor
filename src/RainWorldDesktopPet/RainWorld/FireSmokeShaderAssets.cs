using System;
using System.Drawing;
using System.IO;

namespace RainWorldDesktopPet.RainWorld
{
    /// <summary>
    /// The exact two global textures bound by RainWorld.LoadResources to the
    /// Futile/FireSmoke shader: Resources/Palettes/noise and noise2.
    /// </summary>
    public sealed class FireSmokeShaderAssets : IDisposable
    {
        private const int UnityFilterPoint = 0;
        private const int UnityWrapRepeat = 0;

        private readonly float[] noise;
        private readonly float[] noise2;
        private readonly int noiseWidth;
        private readonly int noiseHeight;
        private readonly int noise2Width;
        private readonly int noise2Height;
        private readonly int noiseFilter;
        private readonly int noise2Filter;
        private readonly int noiseWrapU;
        private readonly int noiseWrapV;
        private readonly int noise2WrapU;
        private readonly int noise2WrapV;

        private FireSmokeShaderAssets(UnityTexture2DInfo first, Bitmap firstBitmap,
            UnityTexture2DInfo second, Bitmap secondBitmap)
        {
            noiseWidth = firstBitmap.Width;
            noiseHeight = firstBitmap.Height;
            noise2Width = secondBitmap.Width;
            noise2Height = secondBitmap.Height;
            noiseFilter = first.FilterMode;
            noise2Filter = second.FilterMode;
            noiseWrapU = first.WrapU;
            noiseWrapV = first.WrapV;
            noise2WrapU = second.WrapU;
            noise2WrapV = second.WrapV;
            noise = ReadRedChannel(firstBitmap);
            noise2 = ReadRedChannel(secondBitmap);
            firstBitmap.Dispose();
            secondBitmap.Dispose();
        }

        public string Status { get; private set; }
        public int NoiseWidth { get { return noiseWidth; } }
        public int NoiseHeight { get { return noiseHeight; } }
        public int Noise2Width { get { return noise2Width; } }
        public int Noise2Height { get { return noise2Height; } }
        public bool UsesPointFiltering { get { return noiseFilter == UnityFilterPoint; } }
        public bool UsesRepeatWrap { get { return noiseWrapU == UnityWrapRepeat && noiseWrapV == UnityWrapRepeat; } }
        public bool UsesPointFiltering2 { get { return noise2Filter == UnityFilterPoint; } }
        public bool UsesRepeatWrap2 { get { return noise2WrapU == UnityWrapRepeat && noise2WrapV == UnityWrapRepeat; } }

        public float[] CopyNoisePixels()
        {
            return (float[])noise.Clone();
        }

        public float[] CopyNoise2Pixels()
        {
            return (float[])noise2.Clone();
        }

        public static FireSmokeShaderAssets TryLoad(RainWorldInstallation installation,
            out string status)
        {
            try
            {
                if (installation == null || !File.Exists(installation.ResourcesAssetsPath))
                    throw new FileNotFoundException("Rain World resources.assets was not found.");
                using (UnitySerializedFileReader reader =
                    new UnitySerializedFileReader(installation.ResourcesAssetsPath))
                {
                    UnityTexture2DInfo first = reader.ReadTexture2D("noise");
                    UnityTexture2DInfo second = reader.ReadTexture2D("noise2");
                    Bitmap firstBitmap = DxtDecoder.DecodeTexture(first,
                        reader.ReadTextureData(first));
                    Bitmap secondBitmap = DxtDecoder.DecodeTexture(second,
                        reader.ReadTextureData(second));
                    FireSmokeShaderAssets result = new FireSmokeShaderAssets(first,
                        firstBitmap, second, secondBitmap);
                    result.Status = "Loaded original FireSmoke noise/noise2 textures from " +
                        installation.ResourcesAssetsPath + ".";
                    status = result.Status;
                    return result;
                }
            }
            catch (Exception exception)
            {
                status = "Original FireSmoke assets unavailable: " + exception.Message;
                return null;
            }
        }

        public double SampleNoise(double u, double v)
        {
            return Sample(noise, noiseWidth, noiseHeight, noiseFilter, noiseWrapU,
                noiseWrapV, u, v);
        }

        public double SampleNoise2(double u, double v)
        {
            return Sample(noise2, noise2Width, noise2Height, noise2Filter,
                noise2WrapU, noise2WrapV, u, v);
        }

        private static float[] ReadRedChannel(Bitmap bitmap)
        {
            float[] result = new float[bitmap.Width * bitmap.Height];
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                    // DxtDecoder makes a top-left GDI bitmap. Unity shader UVs
                    // are bottom-left, so restore that orientation for tex2D.
                    result[y * bitmap.Width + x] = bitmap.GetPixel(x,
                        bitmap.Height - 1 - y).R / 255.0f;
            }
            return result;
        }

        private static double Sample(float[] pixels, int width, int height, int filter,
            int wrapU, int wrapV, double u, double v)
        {
            if (filter == UnityFilterPoint)
            {
                return pixels[Resolve((int)Math.Floor(u * width), width, wrapU) +
                    Resolve((int)Math.Floor(v * height), height, wrapV) * width];
            }

            double x = u * width - 0.5;
            double y = v * height - 0.5;
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            double tx = x - x0;
            double ty = y - y0;
            int x1 = x0 + 1;
            int y1 = y0 + 1;
            double a = pixels[Resolve(x0, width, wrapU) +
                Resolve(y0, height, wrapV) * width];
            double b = pixels[Resolve(x1, width, wrapU) +
                Resolve(y0, height, wrapV) * width];
            double c = pixels[Resolve(x0, width, wrapU) +
                Resolve(y1, height, wrapV) * width];
            double d = pixels[Resolve(x1, width, wrapU) +
                Resolve(y1, height, wrapV) * width];
            return (a + (b - a) * tx) + ((c + (d - c) * tx) -
                (a + (b - a) * tx)) * ty;
        }

        private static int Resolve(int value, int size, int wrapMode)
        {
            if (wrapMode != UnityWrapRepeat)
                return Math.Max(0, Math.Min(size - 1, value));
            value %= size;
            return value < 0 ? value + size : value;
        }

        public void Dispose()
        {
            // Texture data is managed and intentionally retained for sampling
            // throughout the renderer lifetime; no native Unity object is used.
        }
    }
}
