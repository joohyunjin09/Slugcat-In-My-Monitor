using System;
using System.Drawing;
using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Graphics
{
    internal struct FoodLayerPalette
    {
        public FoodLayerPalette(Color baseColor, Color primaryColor, Color detailColor)
        {
            BaseColor = baseColor;
            PrimaryColor = primaryColor;
            DetailColor = detailColor;
        }

        public readonly Color BaseColor;
        public readonly Color PrimaryColor;
        public readonly Color DetailColor;
    }

    // Rain World's edible sprites are mostly masks. Their final appearance is
    // supplied by ApplyPalette rather than baked into the atlas texture. The
    // desktop has no RoomPalette, so use one stable, dark neutral reference
    // palette while preserving the original per-item color equations.
    internal static class FoodRenderPalette
    {
        internal const double ReferenceDarkness = 0.4;
        internal const double NormalEggHueMinimum = -0.15;
        internal const double NormalEggHueMaximum = 0.1;

        private static readonly Color DesktopBlack =
            Color.FromArgb(255, 24, 20, 29);
        private static readonly Color DesktopFog =
            Color.FromArgb(255, 80, 76, 88);
        private static readonly FoodLayerPalette DangleFruitPalette =
            new FoodLayerPalette(DesktopBlack,
                LerpColor(Color.FromArgb(255, 0, 0, 255), DesktopBlack,
                    ReferenceDarkness), Color.Transparent);

        internal static FoodLayerPalette DangleFruit
        {
            get { return DangleFruitPalette; }
        }

        // EggBug's constructor seeds UnityEngine.Random from EntityID, then
        // evaluates ClampedRandomVariation(0.5, 0.5, 2). For the input range
        // used by RandomDeviation, its S-curve reduces to u / (3 - u).
        // Keeping this distribution avoids the unrelated green/yellow eggs
        // produced when the entire hue wheel is sampled uniformly.
        internal static double CreateNormalEggHue(Random random)
        {
            if (random == null) throw new ArgumentNullException("random");
            double magnitudeSample = random.NextDouble();
            double signedDeviation = magnitudeSample / (3.0 - magnitudeSample);
            if (random.NextDouble() >= 0.5) signedDeviation = -signedDeviation;
            double variation = MathUtil.Clamp01(0.5 + signedDeviation);
            return MathUtil.Lerp(NormalEggHueMinimum, NormalEggHueMaximum,
                variation);
        }

        internal static FoodLayerPalette EggBugEgg(double hue)
        {
            Color liquid = HslToRgb(hue + 1.5, 1.0, 0.5);
            Color eyeBase = HslToRgb(hue + 1.0, 1.0, 0.5);
            Color paletteAccent = LerpColor(eyeBase, DesktopFog, 0.3);
            Color darkBase = LerpColor(DesktopBlack, paletteAccent,
                0.1 * (1.0 - ReferenceDarkness));
            double darkness = Math.Pow(InverseLerp(0.5, 1.0,
                ReferenceDarkness), 2.0);
            return new FoodLayerPalette(
                darkBase,
                LerpColor(liquid, darkBase, darkness),
                LerpColor(eyeBase, darkBase, 0.5 + 0.5 * darkness));
        }

        private static double InverseLerp(double from, double to, double value)
        {
            if (Math.Abs(to - from) < 0.000001) return 0.0;
            return MathUtil.Clamp01((value - from) / (to - from));
        }

        private static Color LerpColor(Color from, Color to, double amount)
        {
            amount = MathUtil.Clamp01(amount);
            return Color.FromArgb(
                MathUtil.Clamp((int)Math.Round(MathUtil.Lerp(from.A, to.A, amount)), 0, 255),
                MathUtil.Clamp((int)Math.Round(MathUtil.Lerp(from.R, to.R, amount)), 0, 255),
                MathUtil.Clamp((int)Math.Round(MathUtil.Lerp(from.G, to.G, amount)), 0, 255),
                MathUtil.Clamp((int)Math.Round(MathUtil.Lerp(from.B, to.B, amount)), 0, 255));
        }

        private static Color HslToRgb(double hue, double saturation, double lightness)
        {
            hue -= Math.Floor(hue);
            saturation = MathUtil.Clamp01(saturation);
            lightness = MathUtil.Clamp01(lightness);
            double chroma = (1.0 - Math.Abs(2.0 * lightness - 1.0)) * saturation;
            double sector = hue * 6.0;
            double secondary = chroma * (1.0 - Math.Abs(sector % 2.0 - 1.0));
            double red = 0.0;
            double green = 0.0;
            double blue = 0.0;
            if (sector < 1.0) { red = chroma; green = secondary; }
            else if (sector < 2.0) { red = secondary; green = chroma; }
            else if (sector < 3.0) { green = chroma; blue = secondary; }
            else if (sector < 4.0) { green = secondary; blue = chroma; }
            else if (sector < 5.0) { red = secondary; blue = chroma; }
            else { red = chroma; blue = secondary; }
            double match = lightness - chroma * 0.5;
            return Color.FromArgb(255,
                MathUtil.Clamp((int)Math.Round((red + match) * 255.0), 0, 255),
                MathUtil.Clamp((int)Math.Round((green + match) * 255.0), 0, 255),
                MathUtil.Clamp((int)Math.Round((blue + match) * 255.0), 0, 255));
        }
    }
}
