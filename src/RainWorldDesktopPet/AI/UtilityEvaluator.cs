using System;
using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.AI
{
    public sealed class UtilityContext
    {
        public bool Grounded;
        public bool WallContact;
        public bool ObstacleAhead;
        public int ObstacleDirection;
        public bool ExplorationJumpAvailable;
        public bool SpecialTraversalAvailable;
        public bool OnWindow;
        public double MouseDistance;
        public double MouseSpeed;
        public double Fatigue;
        public double Curiosity;
        public double EdgeDistance;
        public double BehaviorAgeSeconds;
        public double Stillness;
        public int SaferDirection;
        // Mouse locomotion is an explicit interaction.  Passive cursor
        // proximity must not make every autonomous Slugcat converge on the
        // same screen coordinate.
        public bool MouseAttentionActive;
        public bool JumpReady;
        public bool DropReady;
        public bool RestReady = true;
        public bool TransitionAvailable;
        // A neutral personality keeps standalone utility probes and callers
        // that do not own an AbstractCreature equivalent backward-compatible.
        public double PersonalityEnergy = 0.5;
        public double PersonalityNervous = 0.5;
        public double PersonalityAggression = 0.5;
        public double PersonalityBravery = 0.5;
        public double PersonalityDominance = 0.5;
    }

    public static class UtilityEvaluator
    {
        public static double Score(DesktopBehavior behavior, UtilityContext context, double variation)
        {
            double score;
            switch (behavior)
            {
                case DesktopBehavior.Idle:
                    score = 0.22 + context.Stillness * 0.16 +
                        (1.0 - context.PersonalityEnergy) * 0.24;
                    break;
                case DesktopBehavior.Walk:
                    score = context.Grounded ? 0.24 + context.Curiosity * 0.22 +
                        context.PersonalityEnergy * 0.16 : 0.02;
                    break;
                case DesktopBehavior.Explore:
                    score = context.Grounded ? 0.12 + context.Curiosity * 0.54 +
                        context.PersonalityBravery * 0.26 +
                        context.PersonalityDominance * 0.12 : 0.01;
                    break;
                case DesktopBehavior.Sit:
                    score = context.Grounded && context.RestReady
                        ? Math.Max(0.0, context.Fatigue - 0.42) * 0.85 +
                            context.Stillness * 0.12 +
                            (1.0 - context.PersonalityEnergy) * 0.10
                        : 0.0;
                    break;
                case DesktopBehavior.Sleep:
                    score = context.Grounded && context.RestReady
                        ? Math.Max(0.0, context.Fatigue -
                            MathUtil.Lerp(0.86, 0.72, 1.0 - context.PersonalityEnergy)) *
                            1.9
                        : 0.0;
                    break;
                case DesktopBehavior.LookAround:
                    score = 0.12 + context.Curiosity * 0.30 + context.Stillness * 0.12 +
                        context.PersonalityNervous * 0.18;
                    break;
                case DesktopBehavior.FollowMouse:
                    score = context.MouseAttentionActive &&
                        context.MouseDistance > 90.0 && context.MouseDistance < 650.0
                        ? 0.12 + context.Curiosity * 0.40 + context.PersonalityAggression *
                            0.18 + MathUtil.InverseLerp(650.0, 140.0, context.MouseDistance) * 0.25
                        : 0.0;
                    break;
                case DesktopBehavior.AvoidMouse:
                    score = context.MouseAttentionActive && context.MouseDistance < 105.0
                        ? 0.62 + context.PersonalityNervous * 0.30 +
                            MathUtil.InverseLerp(105.0, 20.0, context.MouseDistance) * 0.5 + MathUtil.Clamp01(context.MouseSpeed / 1400.0) * 0.25
                        : 0.0;
                    break;
                case DesktopBehavior.Jump:
                    score = context.Grounded && context.JumpReady &&
                            context.Curiosity > 0.5 &&
                            (context.TransitionAvailable || context.ObstacleAhead ||
                             context.ExplorationJumpAvailable)
                        ? 0.52 + context.Curiosity * 0.34 + context.PersonalityBravery *
                            0.34 + context.PersonalityEnergy * 0.16 +
                            (context.ObstacleAhead ? 0.14 : 0.0) +
                            (context.ExplorationJumpAvailable ? 0.10 : 0.0) +
                            (context.SpecialTraversalAvailable ? 0.16 : 0.0)
                        : 0.0;
                    break;
                case DesktopBehavior.ClimbWindow:
                    score = context.WallContact && !context.Grounded ? 0.88 : 0.0;
                    break;
                case DesktopBehavior.DropDown:
                    score = context.Grounded && context.OnWindow && context.DropReady &&
                            context.EdgeDistance < 24.0 && context.Curiosity > 0.72
                        ? 0.74 + context.PersonalityBravery * 0.38 +
                            context.PersonalityDominance * 0.16
                        : 0.0;
                    break;
                case DesktopBehavior.BalanceNearEdge:
                    score = context.Grounded && context.EdgeDistance < 34.0 ? 0.78 : 0.0;
                    break;
                case DesktopBehavior.ObserveWindow:
                    score = context.OnWindow ? 0.26 + context.Curiosity * 0.42 : 0.0;
                    break;
                default:
                    score = 0.0;
                    break;
            }

            return Math.Max(0.0, score + variation);
        }
    }
}
