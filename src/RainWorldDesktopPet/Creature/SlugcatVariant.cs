using System;
using System.Collections.Generic;
using System.Drawing;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Graphics;

namespace RainWorldDesktopPet.Creature
{
    // Kept as a source-compatible entry point for the base-game variants.
    // Downpour selection uses SlugcatId so graphics, stats and mechanics cannot
    // be selected independently.
    public enum SlugcatVariant
    {
        Survivor,
        Monk,
        Hunter,
        Gourmand
    }

    public static class SlugpupAppearanceSettings
    {
        // Player.setPupStatus changes the BodyChunkConnection from 17 to 12.
        // It does not rescale either BodyChunk radius; PlayerGraphics applies
        // the separate half-length rule only when constructing the tail.
        public const double BodyConnectionDistance = 12.0;
    }

    public sealed class SlugcatAppearance
    {
        private double pupScale = 1.0;

        private SlugcatAppearance(SlugcatVariant variant, Color bodyColor,
            double runSpeedFactor, double bodyWeightFactor,
            double bodyWidthScale, double hipsWidthScale)
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
        public double PupScale { get { return pupScale; } }
        public bool RenderAsPup { get { return pupScale < 0.999999; } }

        public void SetPupScale(double value)
        {
            if (value <= 0.0 || double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException("value");
            pupScale = value;
        }

        public static SlugcatAppearance For(SlugcatVariant variant)
        {
            switch (variant)
            {
                case SlugcatVariant.Monk:
                    return new SlugcatAppearance(variant, Color.FromArgb(255, 255, 255, 115),
                        1.0, 0.95, 1.0, 1.0);
                case SlugcatVariant.Hunter:
                    return new SlugcatAppearance(variant, Color.FromArgb(255, 255, 115, 115),
                        1.2, 1.12, 1.0, 1.0);
                case SlugcatVariant.Gourmand:
                    return new SlugcatAppearance(variant, Color.FromArgb(255, 240, 193, 151),
                        1.0, 1.35, 1.4, 1.6);
                default:
                    return new SlugcatAppearance(SlugcatVariant.Survivor, Color.White,
                        1.0, 1.0, 1.0, 1.0);
            }
        }
    }

    // The only public character selector. Keep this list in the same order as
    // SlugcatProfiles.All and every character-selection UI.
    public enum SlugcatId
    {
        White,
        Yellow,
        Red,
        Gourmand,
        Artificer,
        SpearMaster,
        Rivulet,
        Saint
    }

    public sealed class SlugcatMovementProfile
    {
        public SlugcatMovementProfile(double runSpeed, double bodyWeight,
            double throwingSkill, double poleClimbSpeed, double corridorClimbSpeed,
            double standingJumpChest, double standingJumpHips,
            double airRunSpeed, double crawlSpeed)
        {
            RunSpeedFactor = runSpeed;
            BodyWeightFactor = bodyWeight;
            ThrowingSkill = throwingSkill;
            PoleClimbSpeedFactor = poleClimbSpeed;
            CorridorClimbSpeedFactor = corridorClimbSpeed;
            StandingJumpChest = standingJumpChest;
            StandingJumpHips = standingJumpHips;
            AirRunSpeed = airRunSpeed;
            CrawlSpeed = crawlSpeed;
        }

        public readonly double RunSpeedFactor;
        public readonly double BodyWeightFactor;
        public readonly double ThrowingSkill;
        public readonly double PoleClimbSpeedFactor;
        public readonly double CorridorClimbSpeedFactor;
        public readonly double StandingJumpChest;
        public readonly double StandingJumpHips;
        public readonly double AirRunSpeed;
        public readonly double CrawlSpeed;
    }

    public sealed class SlugcatAbilityProfile
    {
        public SlugcatAbilityProfile(string name, Func<Slugcat, ISlugcatAbilityController> factory)
        {
            Name = name;
            Factory = factory;
        }

        public readonly string Name;
        internal readonly Func<Slugcat, ISlugcatAbilityController> Factory;
    }

    public sealed class SlugcatAudioProfile
    {
        public SlugcatAudioProfile(string footstepA, string footstepB,
            string jump, string impactLight, string impactMedium, string impactHard)
        {
            FootstepA = footstepA;
            FootstepB = footstepB;
            Jump = jump;
            ImpactLight = impactLight;
            ImpactMedium = impactMedium;
            ImpactHard = impactHard;
        }

        public readonly string FootstepA;
        public readonly string FootstepB;
        public readonly string Jump;
        public readonly string ImpactLight;
        public readonly string ImpactMedium;
        public readonly string ImpactHard;
    }

    public sealed class SlugcatProfile
    {
        internal SlugcatProfile(SlugcatId id, string displayName,
            SlugcatGraphicsProfile graphics, SlugcatMovementProfile movement,
            SlugcatAbilityProfile abilities, SlugcatAudioProfile audio)
        {
            Id = id;
            DisplayName = displayName;
            Graphics = graphics;
            Movement = movement;
            Abilities = abilities;
            Audio = audio;
        }

        public readonly SlugcatId Id;
        public readonly string DisplayName;
        public readonly SlugcatGraphicsProfile Graphics;
        public readonly SlugcatMovementProfile Movement;
        public readonly SlugcatAbilityProfile Abilities;
        public readonly SlugcatAudioProfile Audio;
        public SlugcatTailProfile Tail { get { return Graphics.Tail; } }

        internal ISlugcatAbilityController CreateController(Slugcat owner)
        {
            return Abilities.Factory(owner);
        }
    }

    public static class SlugcatProfiles
    {
        private static readonly SlugcatAudioProfile SharedAudio = new SlugcatAudioProfile(
            "Slugcat_Step_A", "Slugcat_Step_B", "Slugcat_Normal_Jump",
            "Slugcat_Terrain_Impact_Light", "Slugcat_Terrain_Impact_Medium",
            "Slugcat_Terrain_Impact_Hard");

        public static readonly SlugcatProfile White = Build(SlugcatId.White,
            "White", SlugcatGraphicsProfiles.White,
            new SlugcatMovementProfile(1.0, 1.0, 1.0, 1.0, 1.0, 4.0, 3.0, 4.0, 2.5),
            "None", delegate(Slugcat s) { return new DefaultAbilityController(s); });

        public static readonly SlugcatProfile Yellow = Build(SlugcatId.Yellow,
            "Yellow", SlugcatGraphicsProfiles.Yellow,
            new SlugcatMovementProfile(1.0, 0.95, 0.0, 0.8, 1.0,
                4.0, 3.0, 4.0, 2.5),
            "None", delegate(Slugcat s) { return new DefaultAbilityController(s); });

        public static readonly SlugcatProfile Red = Build(SlugcatId.Red,
            "Red", SlugcatGraphicsProfiles.Red,
            new SlugcatMovementProfile(1.2, 1.12, 2.0, 1.25, 1.2,
                4.0, 3.0, 4.0, 2.5),
            "None", delegate(Slugcat s) { return new DefaultAbilityController(s); });

        public static readonly SlugcatProfile Gourmand = Build(SlugcatId.Gourmand,
            "Gourmand", SlugcatGraphicsProfiles.Gourmand,
            new SlugcatMovementProfile(1.0, 1.35, 2.0, 0.8, 0.86, 4.0, 3.0, 4.0, 2.5),
            "Roll / exhaustion / crafting", delegate(Slugcat s) { return new GourmandAbilityController(s); });

        public static readonly SlugcatProfile Artificer = Build(SlugcatId.Artificer,
            "Artificer", SlugcatGraphicsProfiles.Artificer,
            new SlugcatMovementProfile(1.2, 1.12, 2.0, 1.25, 1.2, 4.0, 3.0, 4.0, 2.5),
            "Explosive jump", delegate(Slugcat s) { return new ArtificerAbilityController(s); });

        public static readonly SlugcatProfile SpearMaster = Build(SlugcatId.SpearMaster,
            "SpearMaster", SlugcatGraphicsProfiles.SpearMaster,
            new SlugcatMovementProfile(1.2, 0.85, 2.0, 1.25, 1.2, 4.0, 3.0, 4.0, 2.5),
            "Needle spear", delegate(Slugcat s) { return new SpearmasterAbilityController(s); });

        public static readonly SlugcatProfile Rivulet = Build(SlugcatId.Rivulet,
            "Rivulet", SlugcatGraphicsProfiles.Rivulet,
            new SlugcatMovementProfile(1.75, 0.95, 1.0, 1.8, 1.6, 6.0, 5.0, 4.0, 2.5),
            "Agile movement", delegate(Slugcat s) { return new DefaultAbilityController(s); });

        public static readonly SlugcatProfile Saint = Build(SlugcatId.Saint,
            "Saint", SlugcatGraphicsProfiles.Saint,
            new SlugcatMovementProfile(1.0, 1.0, 0.0, 1.0, 1.0, 4.0, 3.0, 4.0, 2.5),
            "Tongue / rope", delegate(Slugcat s) { return new SaintAbilityController(s); });

        private static readonly IList<SlugcatProfile> all = Array.AsReadOnly(
            new SlugcatProfile[] {
            White, Yellow, Red, Gourmand, Artificer, SpearMaster, Rivulet, Saint
        });

        public static IList<SlugcatProfile> All { get { return all; } }

        public static SlugcatProfile Get(SlugcatId id)
        {
            for (int i = 0; i < all.Count; i++) if (all[i].Id == id) return all[i];
            return White;
        }

        internal static SlugcatProfile Get(SlugcatVariant variant)
        {
            switch (variant)
            {
                case SlugcatVariant.Monk: return Yellow;
                case SlugcatVariant.Hunter: return Red;
                case SlugcatVariant.Gourmand: return Gourmand;
                default: return White;
            }
        }

        public static bool TryParse(string value, out SlugcatId id)
        {
            if (string.Equals(value, "default", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "survivor", StringComparison.OrdinalIgnoreCase))
            {
                id = SlugcatId.White;
                return true;
            }
            if (string.Equals(value, "monk", StringComparison.OrdinalIgnoreCase))
            {
                id = SlugcatId.Yellow;
                return true;
            }
            if (string.Equals(value, "hunter", StringComparison.OrdinalIgnoreCase))
            {
                id = SlugcatId.Red;
                return true;
            }
            return Enum.TryParse(value, true, out id) &&
                Enum.IsDefined(typeof(SlugcatId), id);
        }

        public static string SelectionLabel(SlugcatId id)
        {
            switch (id)
            {
                case SlugcatId.White: return UiLocalization.Text("Survivor — 생존자", "Survivor");
                case SlugcatId.Yellow: return UiLocalization.Text("Monk — 수도승", "Monk");
                case SlugcatId.Red: return UiLocalization.Text("Hunter — 사냥꾼", "Hunter");
                case SlugcatId.Gourmand: return UiLocalization.Text("Gourmand — 대식가", "Gourmand");
                case SlugcatId.Artificer: return UiLocalization.Text("Artificer — 기술병", "Artificer");
                case SlugcatId.SpearMaster: return UiLocalization.Text("SpearMaster — 창술가", "SpearMaster");
                case SlugcatId.Rivulet: return UiLocalization.Text("Rivulet — 물살이", "Rivulet");
                case SlugcatId.Saint: return UiLocalization.Text("Saint — 성자", "Saint");
                default: return Get(id).DisplayName;
            }
        }

        private static SlugcatProfile Build(SlugcatId id, string name,
            SlugcatGraphicsProfile graphics, SlugcatMovementProfile movement,
            string abilityName, Func<Slugcat, ISlugcatAbilityController> factory)
        {
            return new SlugcatProfile(id, name, graphics, movement,
                new SlugcatAbilityProfile(abilityName, factory), SharedAudio);
        }
    }
}
