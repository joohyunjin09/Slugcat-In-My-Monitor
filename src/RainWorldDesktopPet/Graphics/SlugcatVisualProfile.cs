using System;
using System.Collections.Generic;
using System.Drawing;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.RainWorld;

namespace RainWorldDesktopPet.Graphics
{
    public enum SlugcatSkin
    {
        Default,
        Artificer,
        Spearmaster,
        Rivulet,
        Saint
    }

    public sealed class SlugcatTailProfile
    {
        public SlugcatTailProfile(string name, double[] radii, double[] lengths, double rootRadius)
        {
            if (radii == null || lengths == null || radii.Length != lengths.Length || radii.Length != 4)
                throw new ArgumentException("PlayerGraphics tails require four matching radius and length values.");
            Name = name;
            Radii = (double[])radii.Clone();
            Lengths = (double[])lengths.Clone();
            RootRadius = rootRadius;
        }

        public readonly string Name;
        public readonly double[] Radii;
        public readonly double[] Lengths;
        public readonly double RootRadius;
    }

    public sealed class SlugcatVisualProfile
    {
        internal SlugcatVisualProfile(
            SlugcatSkin skin,
            string displayName,
            string originalSlugcatId,
            Color bodyColor,
            Color eyeColor,
            bool usesVariantBodyColor,
            bool usesVariantBodyProportions,
            string bodyElement,
            string hipsElement,
            string headFamily,
            double bodyScale,
            double hipsScale,
            double headScale,
            double armShoulderScale,
            SlugcatTailProfile tail,
            int baseSpriteCount,
            int extraSpriteCount,
            string[] extensionNames,
            string[] requiredElements)
        {
            Skin = skin;
            DisplayName = displayName;
            OriginalSlugcatId = originalSlugcatId;
            BodyColor = bodyColor;
            EyeColor = eyeColor;
            UsesVariantBodyColor = usesVariantBodyColor;
            UsesVariantBodyProportions = usesVariantBodyProportions;
            BodyElement = bodyElement;
            HipsElement = hipsElement;
            HeadFamily = headFamily;
            BodyScale = bodyScale;
            HipsScale = hipsScale;
            HeadScale = headScale;
            ArmShoulderScale = armShoulderScale;
            Tail = tail;
            BaseSpriteCount = baseSpriteCount;
            ExtraSpriteCount = extraSpriteCount;
            ExtensionNames = extensionNames ?? new string[0];
            RequiredElements = requiredElements ?? new string[0];
        }

        public readonly SlugcatSkin Skin;
        public readonly string DisplayName;
        public readonly string OriginalSlugcatId;
        public readonly Color BodyColor;
        public readonly Color EyeColor;
        public readonly bool UsesVariantBodyColor;
        public readonly bool UsesVariantBodyProportions;
        public readonly string BodyElement;
        public readonly string HipsElement;
        public readonly string HeadFamily;
        public readonly double BodyScale;
        public readonly double HipsScale;
        public readonly double HeadScale;
        public readonly double ArmShoulderScale;
        public readonly SlugcatTailProfile Tail;
        public readonly int BaseSpriteCount;
        public readonly int ExtraSpriteCount;
        public readonly string[] ExtensionNames;
        public readonly string[] RequiredElements;

        public Color ResolveBodyColor(SlugcatAppearance appearance)
        {
            return UsesVariantBodyColor && appearance != null ? appearance.BodyColor : BodyColor;
        }

        public string ResolveOriginalSlugcatId(SlugcatAppearance appearance)
        {
            if (Skin != SlugcatSkin.Default || appearance == null) return OriginalSlugcatId;
            switch (appearance.Variant)
            {
                case SlugcatVariant.Monk: return "Yellow";
                case SlugcatVariant.Hunter: return "Red";
                case SlugcatVariant.Gourmand: return "Gourmand";
                default: return "White";
            }
        }

        public double ResolveBodyScale(SlugcatAppearance appearance)
        {
            return UsesVariantBodyProportions && appearance != null ? appearance.BodyWidthScale : BodyScale;
        }

        public double ResolveHipsScale(SlugcatAppearance appearance)
        {
            return UsesVariantBodyProportions && appearance != null ? appearance.HipsWidthScale : HipsScale;
        }

        public string ResolveFaceFamily(bool blink, double faceScaleX)
        {
            if (Skin == SlugcatSkin.Saint || blink) return "FaceB";
            if (Skin == SlugcatSkin.Artificer) return faceScaleX < 0.0 ? "FaceD" : "FaceC";
            return "FaceA";
        }

        public bool IsAvailable(RainWorldAtlasSet atlas, out string missingElement)
        {
            missingElement = null;
            if (Skin == SlugcatSkin.Default) return true;
            if (atlas == null)
            {
                missingElement = "local Downpour atlas";
                return false;
            }

            AtlasSprite ignored;
            string[] common = { BodyElement, HipsElement, "FaceStunned", "FaceDead",
                "LegsAAir0", "LegsAWall" };
            for (int i = 0; i < common.Length; i++)
            {
                if (!atlas.TryGet(common[i], out ignored))
                {
                    missingElement = common[i];
                    return false;
                }
            }
            for (int i = 0; i < 18; i++)
            {
                string name = HeadFamily + i;
                if (!atlas.TryGet(name, out ignored))
                {
                    missingElement = name;
                    return false;
                }
            }
            for (int i = 0; i < 13; i++)
            {
                string name = "PlayerArm" + i;
                if (!atlas.TryGet(name, out ignored))
                {
                    missingElement = name;
                    return false;
                }
            }
            for (int i = 0; i < 7; i++)
            {
                string name = "LegsA" + i;
                if (!atlas.TryGet(name, out ignored))
                {
                    missingElement = name;
                    return false;
                }
            }
            for (int i = 0; i < 6; i++)
            {
                string name = "LegsACrawling" + i;
                if (!atlas.TryGet(name, out ignored))
                {
                    missingElement = name;
                    return false;
                }
            }
            string[] faceFamilies = Skin == SlugcatSkin.Artificer
                ? new string[] { "FaceB", "FaceC", "FaceD" }
                : new string[] { "FaceA", "FaceB" };
            for (int family = 0; family < faceFamilies.Length; family++)
            {
                for (int frame = 0; frame < 9; frame++)
                {
                    string name = faceFamilies[family] + frame;
                    if (!atlas.TryGet(name, out ignored))
                    {
                        missingElement = name;
                        return false;
                    }
                }
            }
            for (int i = 0; i < RequiredElements.Length; i++)
            {
                if (!atlas.TryGet(RequiredElements[i], out ignored))
                {
                    missingElement = RequiredElements[i];
                    return false;
                }
            }
            return true;
        }
    }

    public static class SlugcatVisualProfiles
    {
        private static readonly SlugcatTailProfile DefaultTail = new SlugcatTailProfile(
            "DefaultTail", new double[] { 6.0, 4.0, 2.5, 1.0 },
            new double[] { 4.0, 7.0, 7.0, 7.0 }, 6.0);
        private static readonly SlugcatTailProfile SpearmasterTail = new SlugcatTailProfile(
            "SpearmasterTail", new double[] { 8.0, 6.0, 4.0, 2.0 },
            new double[] { 4.0, 7.0, 7.0, 7.0 }, 8.0);

        public static readonly SlugcatVisualProfile Default = new SlugcatVisualProfile(
            SlugcatSkin.Default, "Default", "White", Color.White, Color.FromArgb(16, 16, 16),
            true, true, "BodyA", "HipsA", "HeadA", 1.0, 1.0, 1.0, 1.0,
            DefaultTail, 12, 0, new string[0], new string[0]);

        public static readonly SlugcatVisualProfile Artificer = new SlugcatVisualProfile(
            SlugcatSkin.Artificer, "Artificer", "Artificer", Color.FromArgb(112, 35, 60), Color.White,
            false, false, "BodyA", "HipsA", "HeadA", 1.0, 1.0, 1.0, 1.0,
            DefaultTail, 12, 1, new string[] { "ArtificerScar" },
            new string[] { "BodyA", "HipsA", "HeadA0", "FaceB0", "FaceC0", "FaceD0", "FaceStunned", "FaceDead", "MushroomA" });

        public static readonly SlugcatVisualProfile Spearmaster = new SlugcatVisualProfile(
            SlugcatSkin.Spearmaster, "Spearmaster", "Spear", Color.FromArgb(79, 46, 105), Color.White,
            false, false, "BodyA", "HipsA", "HeadA", 0.76, 0.76, 0.85, 0.6,
            SpearmasterTail, 12, 19, new string[] { "TailSpeckles", "CosmeticPearl(inactive)" },
            new string[] { "BodyA", "HipsA", "HeadA0", "FaceA0", "FaceB0", "FaceStunned", "FaceDead", "tinyStar", "BioSpear1", "JetFishEyeA", "BodyPearl" });

        public static readonly SlugcatVisualProfile Rivulet = new SlugcatVisualProfile(
            SlugcatSkin.Rivulet, "Rivulet", "Rivulet", Color.FromArgb(145, 204, 240), Color.FromArgb(16, 16, 16),
            false, false, "BodyA", "HipsA", "HeadA", 1.0, 1.0, 1.0, 1.0,
            DefaultTail, 12, 12, new string[] { "AxolotlGills" },
            new string[] { "BodyA", "HipsA", "HeadA0", "FaceA0", "FaceB0", "FaceStunned", "FaceDead", "LizardScaleA3", "LizardScaleB3" });

        public static readonly SlugcatVisualProfile Saint = new SlugcatVisualProfile(
            SlugcatSkin.Saint, "Saint", "Saint", Color.FromArgb(170, 241, 86), Color.FromArgb(16, 16, 16),
            false, false, "BodyA", "HipsA", "HeadB", 1.0, 1.0, 1.0, 1.0,
            DefaultTail, 12, 0, new string[] { "Tongue(inactive: gameplay state absent)", "Ascension(inactive)" },
            new string[] { "BodyA", "HipsA", "HeadB0", "FaceB0", "FaceStunned", "FaceDead" });

        private static readonly SlugcatVisualProfile[] all =
        {
            Default, Artificer, Spearmaster, Rivulet, Saint
        };

        public static IList<SlugcatVisualProfile> All { get { return all; } }

        public static SlugcatVisualProfile Get(SlugcatSkin skin)
        {
            for (int i = 0; i < all.Length; i++)
                if (all[i].Skin == skin) return all[i];
            return Default;
        }
    }
}
