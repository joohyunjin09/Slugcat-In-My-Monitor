using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace RainWorldDesktopPet.RainWorld
{
    /// <summary>Decodes the texture formats used by Rain World's embedded player atlases.</summary>
    public static class DxtDecoder
    {
        public const int UnityTextureFormatRgba32 = 4;
        public const int UnityTextureFormatDxt1 = 10;
        public const int UnityTextureFormatDxt5 = 12;

        public static Bitmap DecodeTexture(UnityTexture2DInfo texture, byte[] data)
        {
            if (texture == null) throw new ArgumentNullException("texture");
            if (data == null) throw new ArgumentNullException("data");

            switch (texture.TextureFormat)
            {
                case UnityTextureFormatRgba32:
                    return DecodeRgba32(texture.Width, texture.Height, data);
                case UnityTextureFormatDxt1:
                    return DecodeDxt1(texture.Width, texture.Height, data);
                case UnityTextureFormatDxt5:
                    return DecodeDxt5(texture.Width, texture.Height, data);
                default:
                    throw new NotSupportedException("Unsupported Unity TextureFormat " + texture.TextureFormat +
                        " for embedded atlas " + texture.Name + ". Expected RGBA32 (4), DXT1 (10), or DXT5 (12).");
            }
        }

        public static Bitmap DecodeRgba32(int width, int height, byte[] data)
        {
            ValidateDimensions(width, height);
            int required;
            try { required = checked(width * height * 4); }
            catch (OverflowException) { throw new InvalidDataException("RGBA32 texture is too large."); }
            if (data == null || data.Length < required)
                throw new InvalidDataException("RGBA32 payload is truncated. Expected at least " + required + " bytes.");

            byte[] rgba = new byte[required];
            Buffer.BlockCopy(data, 0, rgba, 0, required);
            return CreateTopLeftBitmap(width, height, rgba);
        }

        public static Bitmap DecodeDxt5(int width, int height, byte[] data)
        {
            ValidateDimensions(width, height);
            int blockWidth = (width + 3) / 4;
            int blockHeight = (height + 3) / 4;
            int required;
            int outputSize;
            try
            {
                required = checked(blockWidth * blockHeight * 16);
                outputSize = checked(width * height * 4);
            }
            catch (OverflowException)
            {
                throw new InvalidDataException("DXT5 texture is too large.");
            }
            if (data == null || data.Length < required)
                throw new InvalidDataException("DXT5 payload is truncated. Expected at least " + required + " bytes.");

            byte[] rgba = new byte[outputSize];
            int source = 0;
            for (int blockY = 0; blockY < blockHeight; blockY++)
            {
                for (int blockX = 0; blockX < blockWidth; blockX++)
                {
                    DecodeBlock(data, source, rgba, width, height, blockX * 4, blockY * 4);
                    source += 16;
                }
            }
            return CreateTopLeftBitmap(width, height, rgba);
        }

        public static Bitmap DecodeDxt1(int width, int height, byte[] data)
        {
            ValidateDimensions(width, height);
            int blockWidth = (width + 3) / 4;
            int blockHeight = (height + 3) / 4;
            int required;
            int outputSize;
            try
            {
                required = checked(blockWidth * blockHeight * 8);
                outputSize = checked(width * height * 4);
            }
            catch (OverflowException)
            {
                throw new InvalidDataException("DXT1 texture is too large.");
            }
            if (data == null || data.Length < required)
                throw new InvalidDataException("DXT1 payload is truncated. Expected at least " + required + " bytes.");

            byte[] rgba = new byte[outputSize];
            int source = 0;
            for (int blockY = 0; blockY < blockHeight; blockY++)
            {
                for (int blockX = 0; blockX < blockWidth; blockX++)
                {
                    DecodeDxt1Block(data, source, rgba, width, height, blockX * 4, blockY * 4);
                    source += 8;
                }
            }
            return CreateTopLeftBitmap(width, height, rgba);
        }

        private static void DecodeDxt1Block(byte[] source, int offset, byte[] rgba,
            int width, int height, int outputX, int outputY)
        {
            ushort color0 = (ushort)(source[offset] | (source[offset + 1] << 8));
            ushort color1 = (ushort)(source[offset + 2] | (source[offset + 3] << 8));
            byte[] colors = new byte[16];
            ExpandRgb565(color0, colors, 0);
            ExpandRgb565(color1, colors, 4);
            if (color0 > color1)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    colors[8 + channel] = (byte)((2 * colors[channel] + colors[4 + channel]) / 3);
                    colors[12 + channel] = (byte)((colors[channel] + 2 * colors[4 + channel]) / 3);
                }
            }
            else
            {
                for (int channel = 0; channel < 3; channel++)
                    colors[8 + channel] = (byte)((colors[channel] + colors[4 + channel]) / 2);
                colors[15] = 0;
            }

            uint indices = (uint)(source[offset + 4] | (source[offset + 5] << 8) |
                (source[offset + 6] << 16) | (source[offset + 7] << 24));
            for (int pixel = 0; pixel < 16; pixel++)
            {
                int x = outputX + pixel % 4;
                int y = outputY + pixel / 4;
                if (x >= width || y >= height) continue;
                int colorIndex = (int)((indices >> (pixel * 2)) & 3);
                int sourceColor = colorIndex * 4;
                int destination = (y * width + x) * 4;
                rgba[destination] = colors[sourceColor];
                rgba[destination + 1] = colors[sourceColor + 1];
                rgba[destination + 2] = colors[sourceColor + 2];
                rgba[destination + 3] = colors[sourceColor + 3];
            }
        }

        private static void DecodeBlock(byte[] source, int offset, byte[] rgba, int width, int height, int outputX, int outputY)
        {
            byte[] alpha = new byte[8];
            alpha[0] = source[offset];
            alpha[1] = source[offset + 1];
            if (alpha[0] > alpha[1])
            {
                alpha[2] = (byte)((6 * alpha[0] + alpha[1]) / 7);
                alpha[3] = (byte)((5 * alpha[0] + 2 * alpha[1]) / 7);
                alpha[4] = (byte)((4 * alpha[0] + 3 * alpha[1]) / 7);
                alpha[5] = (byte)((3 * alpha[0] + 4 * alpha[1]) / 7);
                alpha[6] = (byte)((2 * alpha[0] + 5 * alpha[1]) / 7);
                alpha[7] = (byte)((alpha[0] + 6 * alpha[1]) / 7);
            }
            else
            {
                alpha[2] = (byte)((4 * alpha[0] + alpha[1]) / 5);
                alpha[3] = (byte)((3 * alpha[0] + 2 * alpha[1]) / 5);
                alpha[4] = (byte)((2 * alpha[0] + 3 * alpha[1]) / 5);
                alpha[5] = (byte)((alpha[0] + 4 * alpha[1]) / 5);
                alpha[6] = 0;
                alpha[7] = 255;
            }

            ulong alphaIndices = 0;
            for (int i = 0; i < 6; i++) alphaIndices |= (ulong)source[offset + 2 + i] << (8 * i);

            ushort color0 = (ushort)(source[offset + 8] | (source[offset + 9] << 8));
            ushort color1 = (ushort)(source[offset + 10] | (source[offset + 11] << 8));
            byte[] colors = new byte[16]; // four RGBA entries; alpha is supplied separately
            ExpandRgb565(color0, colors, 0);
            ExpandRgb565(color1, colors, 4);
            for (int channel = 0; channel < 3; channel++)
            {
                colors[8 + channel] = (byte)((2 * colors[channel] + colors[4 + channel]) / 3);
                colors[12 + channel] = (byte)((colors[channel] + 2 * colors[4 + channel]) / 3);
            }

            uint colorIndices = (uint)(source[offset + 12] |
                (source[offset + 13] << 8) |
                (source[offset + 14] << 16) |
                (source[offset + 15] << 24));

            for (int pixel = 0; pixel < 16; pixel++)
            {
                int x = outputX + pixel % 4;
                int y = outputY + pixel / 4;
                if (x >= width || y >= height) continue;
                int colorIndex = (int)((colorIndices >> (pixel * 2)) & 3);
                int alphaIndex = (int)((alphaIndices >> (pixel * 3)) & 7);
                int destination = (y * width + x) * 4;
                int colorOffset = colorIndex * 4;
                rgba[destination] = colors[colorOffset];
                rgba[destination + 1] = colors[colorOffset + 1];
                rgba[destination + 2] = colors[colorOffset + 2];
                rgba[destination + 3] = alpha[alphaIndex];
            }
        }

        private static void ExpandRgb565(ushort value, byte[] destination, int offset)
        {
            int r = (value >> 11) & 31;
            int g = (value >> 5) & 63;
            int b = value & 31;
            destination[offset] = (byte)((r * 255 + 15) / 31);
            destination[offset + 1] = (byte)((g * 255 + 31) / 63);
            destination[offset + 2] = (byte)((b * 255 + 15) / 31);
            destination[offset + 3] = 255;
        }

        private static Bitmap CreateTopLeftBitmap(int width, int height, byte[] unityRgba)
        {
            // Unity Texture2D raw rows use a bottom-left origin. System.Drawing and
            // TexturePacker frame coordinates use top-left, so rows are flipped here.
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData bits = null;
            try
            {
                bits = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    byte[] row = new byte[width * 4];
                    for (int outputY = 0; outputY < height; outputY++)
                    {
                        int sourceY = height - 1 - outputY;
                        int sourceOffset = sourceY * width * 4;
                        for (int x = 0; x < width; x++)
                        {
                            int sourcePixel = sourceOffset + x * 4;
                            int destinationPixel = x * 4;
                            row[destinationPixel] = unityRgba[sourcePixel + 2]; // B
                            row[destinationPixel + 1] = unityRgba[sourcePixel + 1]; // G
                            row[destinationPixel + 2] = unityRgba[sourcePixel]; // R
                            row[destinationPixel + 3] = unityRgba[sourcePixel + 3]; // A
                        }
                        IntPtr rowAddress = IntPtr.Add(bits.Scan0, outputY * bits.Stride);
                        Marshal.Copy(row, 0, rowAddress, row.Length);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(bits);
                    bits = null;
                }
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
            return bitmap;
        }

        private static void ValidateDimensions(int width, int height)
        {
            if (width <= 0 || height <= 0 || width > 65536 || height > 65536)
                throw new InvalidDataException("Invalid texture dimensions: " + width + "x" + height);
        }
    }
}
