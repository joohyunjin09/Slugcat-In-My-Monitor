using System;

namespace RainWorldDesktopPet.Core
{
    public enum SlugcatSize
    {
        Small,
        Normal,
        Large
    }

    public static class SlugcatSizeSettings
    {
        // Rain World v1.11.8 renders gameplay at an internal height of 768.
        // Full HD therefore displays an original sprite pixel at 1080/768.
        public const double LargeRenderScale = SimulationConstants.CharacterRenderScale;
        public const double SmallRenderScale = 1080.0 / 768.0;
        public const double NormalRenderScale =
            (LargeRenderScale + SmallRenderScale) * 0.5;

        public const double LargeMultiplier = 1.0;
        public const double NormalMultiplier = NormalRenderScale / LargeRenderScale;
        public const double SmallMultiplier = SmallRenderScale / LargeRenderScale;

        public static double Multiplier(SlugcatSize size)
        {
            switch (size)
            {
                case SlugcatSize.Small: return SmallMultiplier;
                case SlugcatSize.Normal: return NormalMultiplier;
                case SlugcatSize.Large: return LargeMultiplier;
                default: throw new ArgumentOutOfRangeException("size");
            }
        }
    }
}
