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

    public enum SlugcatGraphicsExtensionKind
    {
        None,
        ArtificerScar,
        SpearmasterSpeckles,
        RivuletGills
    }

    public class SlugcatGraphicsProfile
    {
        internal SlugcatGraphicsProfile(SlugcatId id, string displayName,
            string originalSlugcatId, Color bodyColor, Color eyeColor,
            string bodyElement, string hipsElement, string headFamily,
            double bodyScale, double hipsScale, double headScale,
            double armShoulderScale, SlugcatTailProfile tail,
            int baseSpriteCount, int extraSpriteCount,
            SlugcatGraphicsExtensionKind extensionKind, string[] extensionNames,
            string[] requiredElements)
        {
            Id = id;
            DisplayName = displayName;
            OriginalSlugcatId = originalSlugcatId;
            BodyColor = bodyColor;
            EyeColor = eyeColor;
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
            ExtensionKind = extensionKind;
            ExtensionNames = extensionNames ?? new string[0];
            RequiredElements = requiredElements ?? new string[0];
        }

        public readonly SlugcatId Id;
        public readonly string DisplayName;
        public readonly string OriginalSlugcatId;
        public readonly Color BodyColor;
        public readonly Color EyeColor;
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
        public readonly SlugcatGraphicsExtensionKind ExtensionKind;
        public readonly string[] ExtensionNames;
        public readonly string[] RequiredElements;

        public string ResolveFaceFamily(bool blink, double faceScaleX)
        {
            if (Id == SlugcatId.Saint || blink) return "FaceB";
            if (Id == SlugcatId.Artificer) return faceScaleX < 0.0 ? "FaceD" : "FaceC";
            return "FaceA";
        }

        public bool IsAvailable(RainWorldAtlasSet atlas, out string missingElement)
        {
            missingElement = null;
            if (Id == SlugcatId.White || Id == SlugcatId.Yellow ||
                Id == SlugcatId.Red || Id == SlugcatId.Gourmand) return true;
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
                if (!atlas.TryGet(HeadFamily + i, out ignored))
                {
                    missingElement = HeadFamily + i;
                    return false;
                }
            }
            for (int i = 0; i < 13; i++)
            {
                if (!atlas.TryGet("PlayerArm" + i, out ignored))
                {
                    missingElement = "PlayerArm" + i;
                    return false;
                }
            }
            for (int i = 0; i < 7; i++)
            {
                if (!atlas.TryGet("LegsA" + i, out ignored))
                {
                    missingElement = "LegsA" + i;
                    return false;
                }
            }
            for (int i = 0; i < 6; i++)
            {
                if (!atlas.TryGet("LegsACrawling" + i, out ignored))
                {
                    missingElement = "LegsACrawling" + i;
                    return false;
                }
            }
            string[] faceFamilies = Id == SlugcatId.Artificer
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

    public static class SlugcatGraphicsProfiles
    {
        private static readonly SlugcatTailProfile DefaultTail = new SlugcatTailProfile(
            "DefaultTail", new double[] { 6.0, 4.0, 2.5, 1.0 },
            new double[] { 4.0, 7.0, 7.0, 7.0 }, 6.0);
        private static readonly SlugcatTailProfile SpearmasterTail = new SlugcatTailProfile(
            "SpearmasterTail", new double[] { 8.0, 6.0, 4.0, 2.0 },
            new double[] { 4.0, 7.0, 7.0, 7.0 }, 6.0);

        public static readonly SlugcatGraphicsProfile White = new SlugcatGraphicsProfile(
            SlugcatId.White, "White", "White", Color.White, Color.FromArgb(16, 16, 16),
            "BodyA", "HipsA", "HeadA", 1.0, 1.0, 1.0, 1.0, DefaultTail, 12, 0,
            SlugcatGraphicsExtensionKind.None, new string[0], new string[0]);

        public static readonly SlugcatGraphicsProfile Yellow = new SlugcatGraphicsProfile(
            SlugcatId.Yellow, "Yellow", "Yellow", Color.FromArgb(255, 255, 115),
            Color.FromArgb(16, 16, 16), "BodyA", "HipsA", "HeadA", 1.0, 1.0,
            1.0, 1.0, DefaultTail, 12, 0, SlugcatGraphicsExtensionKind.None,
            new string[0], new string[0]);

        public static readonly SlugcatGraphicsProfile Red = new SlugcatGraphicsProfile(
            SlugcatId.Red, "Red", "Red", Color.FromArgb(255, 115, 115),
            Color.FromArgb(16, 16, 16), "BodyA", "HipsA", "HeadA", 1.0, 1.0,
            1.0, 1.0, DefaultTail, 12, 0, SlugcatGraphicsExtensionKind.None,
            new string[0], new string[0]);

        // Gourmand uses the same atlas family as White, but PlayerGraphics
        // applies the original 1.4/1.6 torso/hips silhouette.
        public static readonly SlugcatGraphicsProfile Gourmand = new SlugcatGraphicsProfile(
            SlugcatId.Gourmand, "Gourmand", "Gourmand", Color.FromArgb(240, 193, 151),
            Color.FromArgb(16, 16, 16), "BodyA", "HipsA", "HeadA", 1.4, 1.6, 1.0, 1.0,
            DefaultTail, 12, 0, SlugcatGraphicsExtensionKind.None, new string[0], new string[0]);

        public static readonly SlugcatGraphicsProfile Artificer = new SlugcatGraphicsProfile(
            SlugcatId.Artificer, "Artificer", "Artificer", Color.FromArgb(112, 35, 60), Color.White,
            "BodyA", "HipsA", "HeadA", 1.0, 1.0, 1.0, 1.0, DefaultTail, 12, 1,
            SlugcatGraphicsExtensionKind.ArtificerScar, new string[] { "ArtificerScar" },
            new string[] { "BodyA", "HipsA", "HeadA0", "FaceB0", "FaceC0", "FaceD0", "FaceStunned", "FaceDead", "MushroomA" });

        public static readonly SlugcatGraphicsProfile SpearMaster = new SlugcatGraphicsProfile(
            SlugcatId.SpearMaster, "SpearMaster", "Spear", Color.FromArgb(79, 46, 105), Color.White,
            "BodyA", "HipsA", "HeadA", 0.76, 0.76, 0.85, 0.6, SpearmasterTail, 12, 19,
            SlugcatGraphicsExtensionKind.SpearmasterSpeckles,
            new string[] { "TailSpeckles", "CosmeticPearl(inactive)" },
            new string[] { "BodyA", "HipsA", "HeadA0", "FaceA0", "FaceB0", "FaceStunned", "FaceDead", "tinyStar", "BioSpear1", "BioSpear2", "BioSpear3", "JetFishEyeA", "BodyPearl" });

        public static readonly SlugcatGraphicsProfile Rivulet = new SlugcatGraphicsProfile(
            SlugcatId.Rivulet, "Rivulet", "Rivulet", Color.FromArgb(145, 204, 240), Color.FromArgb(16, 16, 16),
            "BodyA", "HipsA", "HeadA", 1.0, 1.0, 1.0, 1.0, DefaultTail, 12, 12,
            SlugcatGraphicsExtensionKind.RivuletGills, new string[] { "AxolotlGills" },
            new string[] { "BodyA", "HipsA", "HeadA0", "FaceA0", "FaceB0", "FaceStunned", "FaceDead", "LizardScaleA3", "LizardScaleB3" });

        public static readonly SlugcatGraphicsProfile Saint = new SlugcatGraphicsProfile(
            SlugcatId.Saint, "Saint", "Saint", Color.FromArgb(170, 241, 86), Color.FromArgb(16, 16, 16),
            "BodyA", "HipsA", "HeadB", 1.0, 1.0, 1.0, 1.0, DefaultTail, 12, 0,
            SlugcatGraphicsExtensionKind.None, new string[] { "Tongue", "Ascension(inactive)" },
            new string[] { "BodyA", "HipsA", "HeadB0", "FaceB0", "FaceStunned", "FaceDead" });

        private static readonly IList<SlugcatGraphicsProfile> all = Array.AsReadOnly(
            new SlugcatGraphicsProfile[] {
            White, Yellow, Red, Gourmand, Artificer, SpearMaster, Rivulet, Saint
        });

        public static IList<SlugcatGraphicsProfile> All { get { return all; } }

        public static SlugcatGraphicsProfile Get(SlugcatId id)
        {
            for (int i = 0; i < all.Count; i++) if (all[i].Id == id) return all[i];
            return White;
        }
    }

    // Compatibility facade for callers that used the earlier graphics-only
    // selector. Runtime selection itself is now SlugcatId.
    public sealed class SlugcatVisualProfile : SlugcatGraphicsProfile
    {
        internal SlugcatVisualProfile(SlugcatSkin skin, SlugcatGraphicsProfile source,
            bool usesVariantBodyColor, bool usesVariantBodyProportions)
            : base(source.Id, source.DisplayName, source.OriginalSlugcatId,
                source.BodyColor, source.EyeColor, source.BodyElement,
                source.HipsElement, source.HeadFamily, source.BodyScale,
                source.HipsScale, source.HeadScale, source.ArmShoulderScale,
                source.Tail, source.BaseSpriteCount, source.ExtraSpriteCount,
                source.ExtensionKind, source.ExtensionNames, source.RequiredElements)
        {
            Skin = skin;
            UsesVariantBodyColor = usesVariantBodyColor;
            UsesVariantBodyProportions = usesVariantBodyProportions;
        }

        public readonly SlugcatSkin Skin;
        public readonly bool UsesVariantBodyColor;
        public readonly bool UsesVariantBodyProportions;

        public Color ResolveBodyColor(SlugcatAppearance appearance)
        {
            return UsesVariantBodyColor && appearance != null
                ? appearance.BodyColor : BodyColor;
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
            // PlayerGraphics' RenderAsPup branch does not use the 17->12 body
            // connection ratio as a sprite-width multiplier. Keep the authored
            // BodyA profile width here; pup-specific scaleX is a separate
            // DrawSprites branch in the original game.
            return UsesVariantBodyProportions && appearance != null
                ? appearance.BodyWidthScale : BodyScale;
        }

        public double ResolveHipsScale(SlugcatAppearance appearance)
        {
            // As with BodyA, the original HipsA pup width is not obtained by
            // uniformly multiplying the adult sprite by 12/17.
            return UsesVariantBodyProportions && appearance != null
                ? appearance.HipsWidthScale : HipsScale;
        }
    }

    public static class SlugcatVisualProfiles
    {
        public static readonly SlugcatVisualProfile Default = new SlugcatVisualProfile(
            SlugcatSkin.Default, SlugcatGraphicsProfiles.White, true, true);
        public static readonly SlugcatVisualProfile Artificer = new SlugcatVisualProfile(
            SlugcatSkin.Artificer, SlugcatGraphicsProfiles.Artificer, false, false);
        public static readonly SlugcatVisualProfile Spearmaster = new SlugcatVisualProfile(
            SlugcatSkin.Spearmaster, SlugcatGraphicsProfiles.SpearMaster, false, false);
        public static readonly SlugcatVisualProfile Rivulet = new SlugcatVisualProfile(
            SlugcatSkin.Rivulet, SlugcatGraphicsProfiles.Rivulet, false, false);
        public static readonly SlugcatVisualProfile Saint = new SlugcatVisualProfile(
            SlugcatSkin.Saint, SlugcatGraphicsProfiles.Saint, false, false);

        private static readonly SlugcatVisualProfile[] all =
        {
            Default, Artificer, Spearmaster, Rivulet, Saint
        };

        public static IList<SlugcatVisualProfile> All { get { return all; } }

        public static SlugcatVisualProfile Get(SlugcatSkin skin)
        {
            for (int i = 0; i < all.Length; i++) if (all[i].Skin == skin) return all[i];
            return Default;
        }

        internal static SlugcatVisualProfile FromGraphics(SlugcatGraphicsProfile profile)
        {
            if (profile == null) return Default;
            for (int i = 0; i < all.Length; i++)
                if (all[i].Id == profile.Id && all[i].OriginalSlugcatId == profile.OriginalSlugcatId)
                    return all[i];
            return Default;
        }
    }
}
