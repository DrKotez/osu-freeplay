// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Freeplay.Beatmaps
{
    /// <summary>
    /// Discards the source beatmap's objects and generates new ones synced to its music,
    /// using <see cref="FreeplayGenerationSettings.Current"/>.
    /// </summary>
    public class FreeplayBeatmapConverter : BeatmapConverter<OsuHitObject>
    {
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
            var profile = new DifficultyProfile(settings);

            beatmap.Settings = settings;
            beatmap.Seed = seed;

            beatmap.Difficulty = new BeatmapDifficulty
            {
                CircleSize = profile.CircleSize,
                ApproachRate = profile.ApproachRate,
                OverallDifficulty = profile.OverallDifficulty,
                DrainRate = profile.DrainRate,
                SliderMultiplier = profile.SliderMultiplier,
                SliderTickRate = 1,
            };

            beatmap.StackLeniency = 0.7f;

            var points = new SectionPlanner(original, settings, profile, seed).Plan(RhythmExtractor.Extract(original, settings.RhythmSource));
            beatmap.HitObjects = new PatternGenerator(original.ControlPointInfo, profile, settings, seed).Generate(points, cancellationToken);

            Logger.Log($"Freeplay generated {beatmap.HitObjects.Count} objects from {points.Count} rhythm points ({original.HitObjects.Count} source objects, seed {seed}, {settings.StarRating:0.0}*, {settings.Style})");

            return beatmap;
        }

        protected override Beatmap<OsuHitObject> CreateBeatmap() => new FreeplayBeatmap();
    }
}
