// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Freeplay.Beatmaps
{
    /// <summary>
    /// Discards the source beatmap's objects and generates new ones synced to its music,
    /// using <see cref="FreeplayGenerationSettings.Current"/>.
    /// </summary>
    public class FreeplayBeatmapConverter : BeatmapConverter<OsuHitObject>
    {
        /// <summary>
        /// Regenerations allowed while adjusting spacing towards the target star rating.
        /// </summary>
        private const int max_calibration_passes = 4;

        /// <summary>
        /// How close (in stars) is close enough to stop calibrating.
        /// </summary>
        private const double calibration_tolerance = 0.25;

        public FreeplayBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
            : base(beatmap, ruleset)
        {
        }

        // Any beatmap with timing can be used, regardless of its original ruleset.
        public override bool CanConvert() => true;

        protected override Beatmap<OsuHitObject> ConvertBeatmap(IBeatmap original, CancellationToken cancellationToken)
        {
            var settings = FreeplayGenerationSettings.Current.Clone();
            int seed = settings.Seed ?? Random.Shared.Next();

            var beatmap = (FreeplayBeatmap)base.ConvertBeatmap(original, cancellationToken);

            beatmap.Settings = settings;
            beatmap.Seed = seed;

            if (settings.RhythmSource == RhythmSource.Remix)
                remix(original, beatmap, settings, seed, cancellationToken);
            else
                generate(original, beatmap, settings, seed, cancellationToken);

            return beatmap;
        }

        private static void generate(IBeatmap original, FreeplayBeatmap beatmap, FreeplayGenerationSettings settings, int seed, CancellationToken cancellationToken)
        {
            beatmap.StackLeniency = 0.7f;

            double spacingMultiplier = 1;
            double bestError = double.MaxValue;
            (List<OsuHitObject> objects, BeatmapDifficulty difficulty, double stars) best = (new List<OsuHitObject>(), createDifficulty(new DifficultyProfile(settings)), 0);

            for (int pass = 0; pass < (settings.CalibrateStarRating ? max_calibration_passes : 1); pass++)
            {
                var passSettings = settings.Clone();
                passSettings.JumpSpacing = settings.JumpSpacing * spacingMultiplier;

                var profile = new DifficultyProfile(passSettings);
                var difficulty = createDifficulty(profile);

                var points = new SectionPlanner(original, passSettings, profile, seed).Plan(RhythmExtractor.Extract(original, passSettings.RhythmSource));
                var objects = new PatternGenerator(original.ControlPointInfo, profile, passSettings, seed).Generate(points, cancellationToken);

                if (!settings.CalibrateStarRating || objects.Count == 0)
                {
                    best = (objects, difficulty, 0);
                    break;
                }

                double stars = calculateStarRating(beatmap, objects, difficulty);
                double error = Math.Abs(stars - settings.StarRating);

                if (error < bestError)
                {
                    bestError = error;
                    best = (objects, difficulty, stars);
                }

                if (error <= calibration_tolerance)
                    break;

                // Star rating grows roughly linearly with aim spacing, so scale towards the target.
                spacingMultiplier *= Math.Clamp(Math.Pow(settings.StarRating / Math.Max(0.1, stars), 1.3), 0.6, 1.7);
                spacingMultiplier = Math.Clamp(spacingMultiplier, 0.4, 2.5);
            }

            beatmap.Difficulty = best.difficulty;
            beatmap.HitObjects = best.objects;
            beatmap.StarRating = best.stars;

            Logger.Log($"Freeplay generated {beatmap.HitObjects.Count} objects ({original.HitObjects.Count} source objects, seed {seed}, target {settings.StarRating:0.0}*, actual {best.stars:0.00}*, {settings.Style})");
        }

        private static void remix(IBeatmap original, FreeplayBeatmap beatmap, FreeplayGenerationSettings settings, int seed, CancellationToken cancellationToken)
        {
            var converted = new OsuBeatmapConverter(original, new OsuRuleset()).Convert(cancellationToken);

            // The mapper's difficulty settings fit their objects; only replace what the player chose to override.
            var difficulty = original.Difficulty.Clone();
            if (settings.CircleSize != null) difficulty.CircleSize = settings.CircleSize.Value;
            if (settings.ApproachRate != null) difficulty.ApproachRate = settings.ApproachRate.Value;
            if (settings.OverallDifficulty != null) difficulty.OverallDifficulty = settings.OverallDifficulty.Value;
            if (settings.DrainRate != null) difficulty.DrainRate = settings.DrainRate.Value;

            var objects = converted.HitObjects.Cast<OsuHitObject>().ToList();

            float radius = OsuHitObject.OBJECT_RADIUS * LegacyRulesetExtensions.CalculateScaleFromCircleSize(difficulty.CircleSize, true);
            new MapRemixer(seed, radius).Remix(objects);

            beatmap.Difficulty = difficulty;
            beatmap.StackLeniency = original.StackLeniency;
            beatmap.HitObjects = objects;
            beatmap.StarRating = calculateStarRating(beatmap, objects, difficulty);

            Logger.Log($"Freeplay remixed {objects.Count} objects (seed {seed}, {beatmap.StarRating:0.00}*)");
        }

        private static BeatmapDifficulty createDifficulty(DifficultyProfile profile) => new BeatmapDifficulty
        {
            CircleSize = profile.CircleSize,
            ApproachRate = profile.ApproachRate,
            OverallDifficulty = profile.OverallDifficulty,
            DrainRate = profile.DrainRate,
            SliderMultiplier = profile.SliderMultiplier,
            SliderTickRate = 1,
        };

        /// <summary>
        /// The real osu! star rating of a set of objects, using the osu! ruleset's difficulty calculator.
        /// </summary>
        private static double calculateStarRating(FreeplayBeatmap template, List<OsuHitObject> objects, BeatmapDifficulty difficulty)
        {
            try
            {
                var osuRuleset = new OsuRuleset();

                var candidate = new OsuBeatmap
                {
                    BeatmapInfo = template.BeatmapInfo.Clone(),
                    ControlPointInfo = template.ControlPointInfo,
                    Breaks = template.Breaks,
                    StackLeniency = template.StackLeniency,
                    HitObjects = objects,
                };

                candidate.BeatmapInfo.Ruleset = osuRuleset.RulesetInfo;
                candidate.Difficulty = difficulty.Clone();

                return new OsuDifficultyCalculator(osuRuleset.RulesetInfo, new FlatWorkingBeatmap(candidate)).Calculate().StarRating;
            }
            catch (Exception e)
            {
                Logger.Error(e, "Freeplay couldn't calculate a star rating");
                return 0;
            }
        }

        protected override Beatmap<OsuHitObject> CreateBeatmap() => new FreeplayBeatmap();
    }
}
