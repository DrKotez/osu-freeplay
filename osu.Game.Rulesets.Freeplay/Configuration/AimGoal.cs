// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.ComponentModel;
using System.Linq;
using osu.Game.Rulesets.Freeplay.Generation;

namespace osu.Game.Rulesets.Freeplay.Configuration
{
    /// <summary>
    /// What the player wants out of an aim run. Picking one sets every fine-tuning value of the mode,
    /// relative to the player's own level (<see cref="FreeplaySettings.PlayerLevel"/>), so nothing else needs touching.
    /// </summary>
    public enum AimGoal
    {
        [Description("Разминка")]
        WarmUp,

        [Description("Фарм аима")]
        Farm,

        [Description("Широкие прыжки")]
        Wide,

        [Description("Сложные углы")]
        Angles,

        [Description("Анти-джампы")]
        AntiJumps,

        [Description("Выносливость")]
        Endurance,

        [Description("Сюрприз")]
        Surprise,
    }

    public enum FailLevel
    {
        [Description("Выкл")]
        Off,

        [Description("Обычное")]
        Normal,

        [Description("Жёсткое")]
        Hard,

        /// <summary>
        /// Uses <see cref="FreeplaySettings.Fail"/> as set in fine tuning.
        /// </summary>
        [Description("Своё")]
        Custom,
    }

    public static class AimGoals
    {
        /// <summary>
        /// How many tracks a warm-up takes to reach the player's level.
        /// </summary>
        public const int WARM_UP_TRACKS = 4;

        public static readonly AimGoal[] ALL = Enum.GetValues<AimGoal>();

        /// <summary>
        /// The goals <see cref="AimGoal.Surprise"/> picks from.
        /// </summary>
        private static readonly AimGoal[] surprises = { AimGoal.Farm, AimGoal.Wide, AimGoal.Angles, AimGoal.AntiJumps, AimGoal.Endurance };

        /// <summary>
        /// A few words for the goal's card.
        /// </summary>
        public static string Tagline(AimGoal goal) => goal switch
        {
            AimGoal.WarmUp => "от лёгкого к своему уровню",
            AimGoal.Farm => "как у Sotarks",
            AimGoal.Wide => "как у gwb, кроссскрин",
            AimGoal.Angles => "как у Grandsenpai",
            AimGoal.AntiJumps => "быстрые — широко",
            AimGoal.Endurance => "длинно и плотно",
            AimGoal.Surprise => "цель на каждый трек",
            _ => string.Empty,
        };

        public static string Describe(AimGoal goal) => goal switch
        {
            AimGoal.WarmUp => $"Начинает на 1.5★ ниже уровня и за {WARM_UP_TRACKS} трека доходит до него. Плавно и ровно.",
            AimGoal.Farm => "Как у Sotarks: пинг-понг, треугольники, широкие ровные прыжки.",
            AimGoal.Wide => "Как у gwb: кроссскрин, большие дистанции, меньше нот.",
            AimGoal.Angles => "Как у Grandsenpai: острые и прямые углы, квадраты, кресты, неровный спейсинг.",
            AimGoal.AntiJumps => "Быстрые ноты — широкий прыжок, медленные — короткий.",
            AimGoal.Endurance => "Плотно и почти без пауз, длинные треки.",
            AimGoal.Surprise => "Каждый трек — случайная цель из остальных.",
            _ => string.Empty,
        };

        /// <summary>
        /// The goal's star range, relative to the player's level.
        /// </summary>
        public static (double low, double high) StarOffsets(AimGoal goal) => goal switch
        {
            AimGoal.WarmUp => (-1.5, 0),
            AimGoal.Farm => (-0.5, 0),
            AimGoal.Wide => (-0.8, -0.3),
            AimGoal.Angles => (-0.5, 0),
            AimGoal.AntiJumps => (-0.5, 0),
            AimGoal.Endurance => (-0.8, -0.3),
            AimGoal.Surprise => (-0.5, 0),
            _ => (0, 0),
        };

        /// <summary>
        /// Sets everything <paramref name="goal"/> decides. Tracks, BPM, CS/AR/OD/HP, the seed and the fail level stay as the player set them.
        /// </summary>
        public static void Apply(FreeplayGenerationSettings s, AimGoal goal, double level)
        {
            s.Goal = goal;
            s.GoalEdited = false;
            s.UseRanges = true;
            s.RotatePatterns = true;
            s.AntiJumps = false;
            s.MinedPatternShare = 0.4;
            s.MinedPatternShareMax = 0.4;
            s.MinTrackSeconds = null;

            ApplyStars(s, level);

            const JumpPatterns flow = JumpPatterns.PingPong | JumpPatterns.Triangles | JumpPatterns.Zigzags | JumpPatterns.RandomJumps;

            switch (goal)
            {
                case AimGoal.WarmUp:
                    set(s, density: (0.8, 0.9), spacing: (0.95, 1.1), iq: (0.1, 0.3), sliders: 0.3, flow | JumpPatterns.Polygons);
                    break;

                case AimGoal.Farm:
                case AimGoal.Surprise:
                    set(s, density: (0.9, 1.0), spacing: (1.1, 1.25), iq: (0.05, 0.3), sliders: 0.2, flow);
                    s.MinedPatternShare = 0.5;
                    s.MinedPatternShareMax = 0.5;
                    break;

                case AimGoal.Wide:
                    set(s, density: (0.75, 0.9), spacing: (1.35, 1.6), iq: (0.2, 0.5), sliders: 0.2,
                        JumpPatterns.PingPong | JumpPatterns.Triangles | JumpPatterns.Squares | JumpPatterns.Crosses | JumpPatterns.RandomJumps);
                    break;

                case AimGoal.Angles:
                    set(s, density: (0.9, 1.0), spacing: (1.0, 1.15), iq: (0.7, 0.95), sliders: 0.2,
                        JumpPatterns.All & ~JumpPatterns.PingPong & ~JumpPatterns.Stacks);
                    s.MinedPatternShare = 0.3;
                    s.MinedPatternShareMax = 0.3;
                    break;

                case AimGoal.AntiJumps:
                    set(s, density: (0.9, 1.0), spacing: (1.05, 1.2), iq: (0.3, 0.6), sliders: 0.2, flow | JumpPatterns.Squares);
                    s.AntiJumps = true;
                    break;

                case AimGoal.Endurance:
                    set(s, density: (1.05, 1.2), spacing: (1.0, 1.1), iq: (0.2, 0.5), sliders: 0.15, flow | JumpPatterns.Squares | JumpPatterns.Polygons);
                    s.MinTrackSeconds = 180;
                    break;
            }
        }

        /// <summary>
        /// Moves the goal's star range to <paramref name="level"/>.
        /// </summary>
        public static void ApplyStars(FreeplayGenerationSettings s, double level)
        {
            if (s.Goal is not AimGoal goal)
                return;

            var (low, high) = StarOffsets(goal);
            s.StarRating = Math.Clamp(Math.Round(level + low, 1), 1, 10);
            s.StarRatingMax = Math.Clamp(Math.Round(level + high, 1), 1, 10);
        }

        /// <summary>
        /// The settings for track number <paramref name="trackIndex"/> (from zero) of a run, before ranges are rolled:
        /// a surprise picks a goal, a warm-up climbs to the player's level.
        /// </summary>
        public static FreeplayGenerationSettings ForTrack(FreeplayGenerationSettings s, int trackIndex, double level, Random rng)
        {
            if (s.Goal is not AimGoal goal || s.GoalEdited)
                return s;

            switch (goal)
            {
                case AimGoal.Surprise:
                {
                    var picked = s.Clone();
                    var pick = surprises[rng.Next(surprises.Length)];
                    Apply(picked, pick, level);

                    // Shown as "Сюрприз: Широкие прыжки", and still a surprise next track.
                    picked.SurpriseOf = pick;
                    picked.Goal = AimGoal.Surprise;
                    return picked;
                }

                case AimGoal.WarmUp:
                {
                    var warm = s.Clone();
                    double progress = Math.Min(1, (double)trackIndex / WARM_UP_TRACKS);
                    double stars = Math.Round(level - 1.5 * (1 - progress), 1);

                    // Climbing exactly, then a little either side of the level once warm.
                    warm.StarRating = progress < 1 ? stars : Math.Round(level - 0.3, 1);
                    warm.StarRatingMax = progress < 1 ? stars : level;

                    // Spacing opens up as the player warms up.
                    warm.JumpSpacing = Math.Round(0.95 + 0.15 * progress, 2);
                    warm.JumpSpacingMax = warm.JumpSpacing;
                    return warm;
                }

                default:
                    return s;
            }
        }

        public static string Name(FreeplayGenerationSettings s)
        {
            if (s.Goal is not AimGoal goal)
                return "Своя";

            string name = goal.GetDescriptionText();
            return s.GoalEdited ? $"Своя (на основе «{name}»)" : name;
        }

        public static string GetDescriptionText(this Enum value)
            => value.GetType().GetField(value.ToString())?.GetCustomAttributes(typeof(DescriptionAttribute), false).OfType<DescriptionAttribute>().FirstOrDefault()?.Description ?? value.ToString();

        private static void set(FreeplayGenerationSettings s, (double, double) density, (double, double) spacing, (double, double) iq, double sliders, JumpPatterns patterns)
        {
            (s.Density, double densityMax) = density;
            s.DensityMax = densityMax;
            (s.JumpSpacing, double spacingMax) = spacing;
            s.JumpSpacingMax = spacingMax;
            (s.PatternIq, double iqMax) = iq;
            s.PatternIqMax = iqMax;
            s.SliderAmount = sliders;
            s.SliderAmountMax = sliders;
            s.Patterns = patterns;
        }

        /// <summary>
        /// The fail conditions a fail level stands for. <see cref="FailLevel.Custom"/> is the player's own, from fine tuning.
        /// </summary>
        public static FailSettings FailFor(FailLevel level, FailSettings custom) => level switch
        {
            FailLevel.Off => new FailSettings { Health = false, CarryHealth = false, HealthRefill = 1, TimeLimitMinutes = custom.TimeLimitMinutes },
            FailLevel.Normal => new FailSettings { Health = true, CarryHealth = true, HealthRefill = 0.3, TimeLimitMinutes = custom.TimeLimitMinutes },
            FailLevel.Hard => new FailSettings { Health = true, CarryHealth = true, HealthRefill = 0.1, MinAccuracy = 0.9, TimeLimitMinutes = custom.TimeLimitMinutes },
            _ => custom,
        };

        public static string DescribeFail(FailSettings fail)
        {
            var parts = new System.Collections.Generic.List<string>();

            if (fail.Health) parts.Add(fail.CarryHealth ? $"HP переносится, +{fail.HealthRefill:P0} за трек" : "HP");
            if (fail.MissLimit > 0) parts.Add(fail.MissLimit == 1 ? "sudden death" : $"{fail.MissLimit} промахов");
            if (fail.MinAccuracy > 0) parts.Add($"точность < {fail.MinAccuracy:P0}");
            if (fail.TimeLimitMinutes > 0) parts.Add($"лимит {fail.TimeLimitMinutes:0} мин");

            return parts.Count > 0 ? string.Join(", ", parts) : "нет";
        }
    }
}
