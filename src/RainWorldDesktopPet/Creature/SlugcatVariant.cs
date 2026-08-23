using System.Drawing;

namespace RainWorldDesktopPet.Creature
{
    public enum SlugcatVariant
    {
        Survivor,
        Monk,
        Hunter,
        Gourmand
    }

    public sealed class SlugcatAppearance
    {
        private SlugcatAppearance(
            SlugcatVariant variant,
            Color bodyColor,
            double runSpeedFactor,
            double bodyWeightFactor,
            double bodyWidthScale,
            double hipsWidthScale)
        {
            Variant = variant;
            BodyColor = bodyColor;
            RunSpeedFactor = runSpeedFactor;
            BodyWeightFactor = bodyWeightFactor;
            BodyWidthScale = bodyWidthScale;
            HipsWidthScale = hipsWidthScale;
        }

        public readonly SlugcatVariant Variant;
        public readonly Color BodyColor;
        public readonly double RunSpeedFactor;
        public readonly double BodyWeightFactor;
        public readonly double BodyWidthScale;
        public readonly double HipsWidthScale;

        public static SlugcatAppearance For(SlugcatVariant variant)
        {
            // PlayerGraphics.DefaultSlugcatColor and SlugcatStats::.ctor in the
            // locally installed Assembly-CSharp.dll are the source of these values.
            switch (variant)
            {
                case SlugcatVariant.Monk:
                    return new SlugcatAppearance(variant, Color.FromArgb(255, 255, 255, 115), 1.0, 0.95, 1.0, 1.0);
                case SlugcatVariant.Hunter:
                    return new SlugcatAppearance(variant, Color.FromArgb(255, 255, 115, 115), 1.2, 1.12, 1.0, 1.0);
                case SlugcatVariant.Gourmand:
                    return new SlugcatAppearance(variant, Color.FromArgb(255, 240, 193, 151), 1.0, 1.35, 1.4, 1.6);
                default:
                    return new SlugcatAppearance(SlugcatVariant.Survivor, Color.White, 1.0, 1.0, 1.0, 1.0);
            }
        }
    }
}
