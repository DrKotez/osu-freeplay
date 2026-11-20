// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Logging;
using osu.Framework.Lists;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Mods;
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
        private const int max_calibration_passes = 10;

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

            if (settings.FixedMap is SeedMap fixedMap)
                playSeed(beatmap, fixedMap);
            else if (settings.RhythmSource == RhythmSource.Remix)
                remix(original, beatmap, settings, seed, cancellationToken);
            else
                generate(original, beatmap, settings, seed, cancellationToken);

            // As generated, before mods change anything: what a seed code carries.
            SeedMap.Snap(beatmap.HitObjects);
            beatmap.PackedObjects = SeedMap.Pack(beatmap.HitObjects);
            beatmap.GeneratedDifficulty = beatmap.Difficulty.Clone();

            return beatmap;
        }

        /// <summary>
        /// A seed's map, exactly as it was played (on this song's timing).
        /// </summary>
        private static void playSeed(FreeplayBeatmap beatmap, SeedMap seed)
        {
            beatmap.Seed = seed.Seed;
            beatmap.Difficulty = seed.CreateDifficulty();
            beatmap.StackLeniency = seed.StackLeniency;
            beatmap.HitObjects = seed.Unpack();
            beatmap.StarRating = seed.StarRating;
            keepBreaksBetweenNotes(beatmap);

            Logger.Log($"Freeplay played a seed: {beatmap.HitObjects.Count} objects ({seed.Song.Name}, {seed.StarRating:0.00}*)");
        }

        private static void generate(IBeatmap original, FreeplayBeatmap beatmap, FreeplayGenerationSettings settings, int seed, CancellationToken cancellationToken)
        {
            beatmap.StackLeniency = 0.7f;

            double spacingMultiplier = 1;
            double fillBoost = 0;
            int flowLevel = 0;
            bool searchingHalves = false;
            double halfShare = 0, halvesLow = 0, halvesHigh = 1;
            double bestError = double.MaxValue;
            (List<OsuHitObject> objects, BeatmapDifficulty difficulty, double stars) best = (new List<OsuHitObject>(), createDifficulty(new DifficultyProfile(settings)), 0);

            for (int pass = 0; pass < (settings.CalibrateStarRating ? max_calibration_passes : 1); pass++)
            {
                var passSettings = settings.Clone();
                passSettings.CalibrationSpacing = spacingMultiplier;
                passSettings.FillBoost = fillBoost;
                passSettings.FlowLevel = flowLevel;
                passSettings.HalfShare = halfShare;

                var profile = new DifficultyProfile(passSettings);
                var difficulty = createDifficulty(profile);

                List<OsuHitObject> objects;
                double flooredJumpShare;

                if (passSettings.Style == PatternStyle.Metronome)
                {
                    var metronome = new MetronomeGenerator(original, profile, passSettings, seed);
                    objects = metronome.Generate();
                    flooredJumpShare = metronome.FlooredJumpShare;
                }
                else
                {
                    var points = new SectionPlanner(original, passSettings, profile, seed).Plan(RhythmExtractor.Extract(original, passSettings.RhythmSource));
                    var generator = new PatternGenerator(original.ControlPointInfo, profile, passSettings, seed);
                    objects = generator.Generate(points, cancellationToken);
                    flooredJumpShare = generator.FlooredJumpShare;
                }

                if (objects.Count == 0)
                {
                    best = (objects, difficulty, 0);
                    break;
                }

                double stars = CalculateStarRating(beatmap, objects, difficulty, settings.GeneratedForSpeed);

                if (!settings.CalibrateStarRating)
                {
                    best = (objects, difficulty, stars);
                    break;
                }
                double error = Math.Abs(stars - settings.StarRating);

                if (error < bestError)
                {
                    bestError = error;
                    best = (objects, difficulty, stars);
                }

                if (error <= calibration_tolerance)
                    break;

                // The stars mode between half beats (too hard) and whole beats (too easy): the share of the music at half beats is halved
                // towards the stars asked, with the jumps left as they are.
                if (searchingHalves)
                {
                    if (stars < settings.StarRating)
                        halvesLow = halfShare;
                    else
                        halvesHigh = halfShare;

                    halfShare = (halvesLow + halvesHigh) / 2;
                    continue;
                }

                // Aim jumping every half beat that is still too hard with jumps about as small as they go: fewer half beats
                // (only in the chorus, then nowhere), rather than leaving notes out here and there, which plays like pauses.
                if (stars > settings.StarRating + calibration_tolerance && flowLevel < 2 && profile.FlowJumps && !(settings.Style == PatternStyle.Metronome && settings.AlwaysHalfBeats)
                    && (spacingMultiplier <= (settings.Style == PatternStyle.Metronome ? 0.75 : 0.5) / (settings.GeneratedForSpeed * settings.GeneratedForSpeed) || flooredJumpShare > (settings.Style == PatternStyle.Metronome ? 0.4 : 0.5)))
                {
                    flowLevel++;
                    // Fewer notes: the jumps can grow back some.
                    spacingMultiplier = Math.Min(1, spacingMultiplier * 1.5);

                    // FIX: the stars mode doesn't go straight to whole beats throughout (often far too easy); it searches the share of half beats.
                    if (settings.Style == PatternStyle.Metronome && flowLevel == 2)
                    {
                        searchingHalves = true;
                        halfShare = 0.5;
                        spacingMultiplier = 1;
                    }

                    continue;
                }

                // Star rating grows roughly linearly with aim spacing, so scale towards the target.
                spacingMultiplier *= Math.Clamp(Math.Pow(settings.StarRating / Math.Max(0.1, stars), 1.3), 0.6, 1.7);
                // (Sped up by Double Time, the same stars come from shorter jumps.)
                spacingMultiplier = Math.Clamp(spacingMultiplier, 0.4 / (settings.GeneratedForSpeed * settings.GeneratedForSpeed), 2.5);

                // Jumps can only get so big on the playfield, so too easy maps also get more 1/2 notes
                // (a whole star moves it by a quarter). Too hard maps lose notes only once jumps can't shrink further,
                // as removing notes is what makes a map feel undermapped, or once most jumps are at their minimum.
                double fillChange = Math.Clamp((settings.StarRating - stars) * 0.25, -0.3, 0.3);

                if (fillChange > 0 || flooredJumpShare > 0.35 || spacingMultiplier <= 0.55)
                    fillBoost = Math.Clamp(fillBoost + fillChange, -0.8, 0.6);
            }

            beatmap.Difficulty = best.difficulty;
            beatmap.HitObjects = best.objects;
            beatmap.StarRating = best.stars;
            keepBreaksBetweenNotes(beatmap);

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
            beatmap.StarRating = CalculateStarRating(beatmap, objects, difficulty, settings.GeneratedForSpeed);
            keepBreaksBetweenNotes(beatmap);

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
        /// The source's breaks, apart from those with no note after them (where generation stopped earlier than the mapper did):
        /// osu! expects notes after every break (e.g. Singletap looks for the first one).
        /// </summary>
        private static void keepBreaksBetweenNotes(FreeplayBeatmap beatmap)
        {
            double last = beatmap.HitObjects.Count == 0 ? double.MinValue : beatmap.HitObjects.Max(h => h.StartTime);
            var breaks = new SortedList<BreakPeriod>(Comparer<BreakPeriod>.Default);
            breaks.AddRange(beatmap.Breaks.Where(b => b.EndTime <= last));
            beatmap.Breaks = breaks;
        }

        /// <summary>
        /// The real osu! star rating of a set of objects, using the osu! ruleset's difficulty calculator.
        /// </summary>
        /// <remarks>Also used for a played map with mods (e.g. Hard Rock) already applied to its objects and difficulty.</remarks>
        public static double CalculateStarRating(FreeplayBeatmap template, List<OsuHitObject> objects, BeatmapDifficulty difficulty, double speed = 1)
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

                var calculator = new OsuDifficultyCalculator(osuRuleset.RulesetInfo, new FlatWorkingBeatmap(candidate));

                // At the speed it's played (Double Time).
                return (speed > 1 ? calculator.Calculate(new Mod[] { Configuration.FreeplayMods.CreateDoubleTime(speed) }) : calculator.Calculate()).StarRating;
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
