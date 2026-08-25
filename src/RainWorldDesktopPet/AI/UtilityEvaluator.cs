using System;
using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.AI
{
    public enum AIMood
    {
        Calm,
        Curious,
        Energetic,
        Lazy,
        Playful,
        Restless,
        Focused
    }

    public sealed class UtilityContext
    {
        public bool Grounded;
        public bool WallContact;
        public bool ObstacleAhead;
        public int ObstacleDirection;
        public bool ExplorationJumpAvailable;
        public bool FreeJumpOpportunity;
        public bool SpecialTraversalAvailable;
        public bool OnWindow;
        public double MouseDistance;
        public double MouseSpeed;
        public double EdgeDistance;
        public double BehaviorAgeSeconds;
        public double Stillness;
        public int SaferDirection;
        public bool MouseAttentionActive;
        public bool JumpReady;
        public bool DropReady;
        public bool RestReady = true;
        public bool TransitionAvailable;

        // Continuously changing needs/state. These are intentionally separate
        // from long-lived personality values so the same Slugcat can behave
        // differently over a long session without rerolling its personality.
        public double Activity = 0.5;
        public double Curiosity = 0.5;
        public double Boredom = 0.3;
        public double Restlessness = 0.4;
        public double Sleepiness = 0.2;
        public double Playfulness = 0.4;
        public double AttentionDrive = 0.5;
        public double Confidence = 0.5;
        public AIMood Mood = AIMood.Calm;

        // Legacy fields are kept for compatibility with existing diagnostics/tests.
        public double Fatigue;
        public double MovementUrge = 0.5;

        // SlugNPCAI-style individual personality values.
        public double PersonalityEnergy = 0.5;
        public double PersonalityNervous = 0.5;
        public double PersonalityAggression = 0.5;
        public double PersonalityBravery = 0.5;
        public double PersonalityDominance = 0.5;

        // Compatibility aliases kept for earlier diagnostics/tests. New code uses
        // the more explicit Trait* names below.
        public double RoamAffinity = 0.5;
        public double JumpAffinity = 0.5;
        public double RestAffinity = 0.5;
        public double ObservationAffinity = 0.5;
        public double DirectionPersistence = 0.5;

        // Character archetype + persistent per-instance modifiers.
        public double TraitActivity = 0.5;
        public double TraitCuriosity = 0.5;
        public double TraitPlayfulness = 0.5;
        public double TraitRestlessness = 0.5;
        public double TraitAttention = 0.5;
        public double TraitConfidence = 0.5;
        public double TraitPatience = 0.5;
        public double TraitImpulsiveness = 0.5;
        public double TraitRest = 0.5;
        public double TraitObservation = 0.5;
        public double TraitDirectionPersistence = 0.5;
        public double TraitMouseInterest = 0.5;
        public double TraitMouseApproach = 0.5;
    }

    public static class UtilityEvaluator
    {
        public static double Score(DesktopBehavior behavior, UtilityContext context, double variation)
        {
            double score;
            switch (behavior)
            {
                case DesktopBehavior.Idle:
                    score = 0.10 + context.Stillness * 0.10 +
                        context.Sleepiness * 0.22 + context.TraitRest * 0.13 +
                        MoodWeight(context.Mood, AIMood.Calm, 0.18) +
                        MoodWeight(context.Mood, AIMood.Lazy, 0.46) -
                        context.Boredom * 0.24 - context.Restlessness * 0.22 -
                        context.Activity * 0.10;
                    break;

                case DesktopBehavior.Walk:
                    score = context.Grounded
                        ? 0.17 + context.Activity * 0.16 + context.Curiosity * 0.13 +
                            context.Boredom * 0.16 + context.TraitActivity * 0.13 +
                            context.TraitDirectionPersistence * 0.10 -
                            MoodWeight(context.Mood, AIMood.Lazy, 0.13) -
                            context.Sleepiness * 0.08
                        : 0.01;
                    break;

                case DesktopBehavior.Run:
                    score = context.Grounded
                        ? 0.05 + context.Activity * 0.27 + context.Restlessness * 0.28 +
                            context.TraitActivity * 0.17 + context.TraitImpulsiveness * 0.13 +
                            MoodWeight(context.Mood, AIMood.Energetic, 0.26) +
                            MoodWeight(context.Mood, AIMood.Restless, 0.20) +
                            MoodWeight(context.Mood, AIMood.Playful, 0.12) -
                            MoodWeight(context.Mood, AIMood.Lazy, 0.50) -
                            MoodWeight(context.Mood, AIMood.Calm, 0.12) -
                            context.Sleepiness * 0.20
                        : 0.0;
                    break;

                case DesktopBehavior.Crawl:
                    score = context.Grounded
                        ? 0.04 + context.Curiosity * 0.08 + context.Sleepiness * 0.17 +
                            context.TraitRest * 0.11 +
                            MoodWeight(context.Mood, AIMood.Calm, 0.12) +
                            MoodWeight(context.Mood, AIMood.Lazy, 0.18) -
                            context.Restlessness * 0.12
                        : 0.0;
                    break;

                case DesktopBehavior.Explore:
                    score = context.Grounded
                        ? 0.09 + context.Curiosity * 0.30 + context.Boredom * 0.25 +
                            context.Confidence * 0.10 + context.TraitCuriosity * 0.18 +
                            context.TraitActivity * 0.08 +
                            MoodWeight(context.Mood, AIMood.Curious, 0.24) +
                            MoodWeight(context.Mood, AIMood.Focused, 0.09) -
                            MoodWeight(context.Mood, AIMood.Lazy, 0.24)
                        : 0.01;
                    break;

                case DesktopBehavior.Sit:
                    score = context.Grounded && context.RestReady
                        ? 0.03 + context.Sleepiness * 0.28 + context.TraitRest * 0.25 +
                            Math.Max(0.0, context.Fatigue - 0.35) * 0.32 +
                            MoodWeight(context.Mood, AIMood.Lazy, 0.62) +
                            MoodWeight(context.Mood, AIMood.Calm, 0.12) -
                            context.Restlessness * 0.17 - context.Boredom * 0.08
                        : 0.0;
                    break;

                case DesktopBehavior.Sleep:
                    score = context.Grounded && context.RestReady
                        ? Math.Max(0.0, context.Sleepiness - 0.52) * 1.25 +
                            context.TraitRest * 0.20 +
                            MoodWeight(context.Mood, AIMood.Lazy, 0.46) -
                            context.Restlessness * 0.18
                        : 0.0;
                    break;

                case DesktopBehavior.LookAround:
                    score = 0.07 + context.Curiosity * 0.18 +
                        context.AttentionDrive * 0.18 + context.TraitObservation * 0.20 +
                        context.TraitAttention * 0.12 +
                        MoodWeight(context.Mood, AIMood.Curious, 0.18) +
                        MoodWeight(context.Mood, AIMood.Focused, 0.20) -
                        context.Restlessness * 0.05;
                    break;

                case DesktopBehavior.FollowMouse:
                    score = context.MouseAttentionActive &&
                        context.MouseDistance > 70.0 && context.MouseDistance < 650.0
                        ? 0.06 + context.Curiosity * 0.20 + context.AttentionDrive * 0.18 +
                            context.TraitMouseInterest * 0.28 + context.TraitMouseApproach * 0.30 +
                            MathUtil.InverseLerp(650.0, 120.0, context.MouseDistance) * 0.16 +
                            MoodWeight(context.Mood, AIMood.Curious, 0.12) -
                            context.Sleepiness * 0.08
                        : 0.0;
                    break;

                case DesktopBehavior.AvoidMouse:
                    score = context.MouseAttentionActive && context.MouseDistance < 125.0
                        ? 0.17 + context.PersonalityNervous * 0.28 +
                            (1.0 - context.Confidence) * 0.22 +
                            (1.0 - context.TraitMouseApproach) * 0.20 +
                            MathUtil.InverseLerp(125.0, 18.0, context.MouseDistance) * 0.34 +
                            MathUtil.Clamp01(context.MouseSpeed / 1400.0) * 0.17
                        : 0.0;
                    break;

                case DesktopBehavior.Jump:
                    score = context.Grounded && context.JumpReady &&
                        (context.TransitionAvailable || context.ObstacleAhead ||
                         context.ExplorationJumpAvailable || context.FreeJumpOpportunity)
                        ? 0.18 + context.Activity * 0.12 + context.Restlessness * 0.14 +
                            context.Playfulness * 0.27 + context.Confidence * 0.10 +
                            context.TraitPlayfulness * 0.20 + context.TraitImpulsiveness * 0.08 +
                            (context.ObstacleAhead ? 0.24 : 0.0) +
                            (context.TransitionAvailable
                                ? 0.20 + context.PersonalityBravery * 0.10 +
                                    context.PersonalityEnergy * 0.06
                                : 0.0) +
                            (context.ExplorationJumpAvailable ? 0.14 : 0.0) +
                            (context.FreeJumpOpportunity ? 0.10 : 0.0) +
                            (context.SpecialTraversalAvailable ? 0.36 : 0.0) +
                            MoodWeight(context.Mood, AIMood.Playful, 0.20) +
                            MoodWeight(context.Mood, AIMood.Energetic, 0.12)
                        : 0.0;
                    break;

                case DesktopBehavior.Play:
                    score = context.Grounded
                        ? 0.02 + context.Playfulness * 0.36 + context.Restlessness * 0.15 +
                            context.Boredom * 0.16 + context.Confidence * 0.09 +
                            context.TraitPlayfulness * 0.24 +
                            MoodWeight(context.Mood, AIMood.Playful, 0.34) -
                            MoodWeight(context.Mood, AIMood.Lazy, 0.55) -
                            context.Sleepiness * 0.13
                        : 0.0;
                    break;

                case DesktopBehavior.TurnAround:
                    score = context.Grounded
                        ? 0.015 + context.Restlessness * 0.17 + context.Boredom * 0.10 +
                            context.Playfulness * 0.13 + context.TraitImpulsiveness * 0.17 +
                            (1.0 - context.TraitDirectionPersistence) * 0.14 +
                            MoodWeight(context.Mood, AIMood.Restless, 0.15) +
                            MoodWeight(context.Mood, AIMood.Playful, 0.10)
                        : 0.0;
                    break;

                case DesktopBehavior.ClimbWindow:
                    score = context.WallContact && !context.Grounded
                        ? 0.78 + context.Confidence * 0.14 : 0.0;
                    break;

                case DesktopBehavior.DropDown:
                    score = context.Grounded && context.OnWindow && context.DropReady &&
                            context.EdgeDistance < 24.0 && context.Confidence > 0.40
                        ? 0.36 + context.Curiosity * 0.16 + context.Boredom * 0.15 +
                            context.Confidence * 0.20
                        : 0.0;
                    break;

                case DesktopBehavior.BalanceNearEdge:
                    score = context.Grounded && context.EdgeDistance < 34.0
                        ? 0.64 + context.TraitObservation * 0.10 : 0.0;
                    break;

                case DesktopBehavior.ObserveWindow:
                    score = context.OnWindow
                        ? 0.06 + context.Curiosity * 0.15 + context.AttentionDrive * 0.14 +
                            context.TraitObservation * 0.31 +
                            MoodWeight(context.Mood, AIMood.Focused, 0.19) +
                            MoodWeight(context.Mood, AIMood.Calm, 0.08) -
                            context.Restlessness * 0.08
                        : 0.0;
                    break;

                default:
                    score = 0.0;
                    break;
            }

            return Math.Max(0.0, score + variation);
        }

        private static double MoodWeight(AIMood current, AIMood wanted, double weight)
        {
            return current == wanted ? weight : 0.0;
        }
    }
}
