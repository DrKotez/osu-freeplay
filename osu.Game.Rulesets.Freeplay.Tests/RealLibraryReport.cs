// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Freeplay.Stable;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Tests.Beatmaps;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    /// <summary>
    /// Generates aim maps from songs in the local osu!stable library (with patterns mined from the player's aim collection, as the game does)
    /// and measures how well they follow the music and how varied they are. Run by hand: it needs a real osu!stable install.
    /// </summary>
    [TestFixture]
    public class RealLibraryReport
    {
        private const int songs = 40;
        private const double level = 5.8;

        private static (List<(string folder, StableDifficulty difficulty)> songs, MinedPatternSet patterns)? loaded;

        /// <summary>
        /// The patterns mined from the local aim collection, as the game mines them.
        /// </summary>
        internal static MinedPatternSet LocalPatterns() => (loaded ??= load(Path.Combine(StableLibrary.DEFAULT_PATH, "Songs"))).patterns;

        [Explicit]
        [TestCase(AimGoal.Angles)]
        [TestCase(AimGoal.Farm)]
        [TestCase(AimGoal.Wide)]
        [TestCase(AimGoal.Surprise)]
        public void Report(AimGoal goal)
        {
            string songsPath = Path.Combine(StableLibrary.DEFAULT_PATH, "Songs");

            if (!Directory.Exists(songsPath))
                Assert.Ignore("No osu!stable install found.");

            var (picked, patterns) = loaded ??= load(songsPath);
            MinedPatternSet.Current = patterns;

            var rows = new ConcurrentBag<Row>();
            var rng = new Random(1);
            var seeds = picked.Select(_ => rng.Next()).ToList();

            Parallel.For(0, picked.Count, i =>
            {
                try
                {
                    var (folder, difficulty) = picked[i];
                    var settings = new FreeplayGenerationSettings { Style = PatternStyle.Jumps, Seed = seeds[i] };
                    AimGoals.Apply(settings, goal, level);

                    var trackRng = new Random(seeds[i]);
                    var track = AimGoals.ForTrack(settings, 4, level, trackRng).Roll(trackRng);
                    track.CalibrateStarRating = true;

                    rows.Add(measure(Path.Combine(songsPath, folder, difficulty.File), track));
                }
                catch (Exception e)
                {
                    TestContext.Out.WriteLine($"failed {picked[i].folder}: {e.Message}");
                }
            });

            var all = rows.ToList();
            TestContext.Out.WriteLine($"goal {goal}, {all.Count} songs at level {level}, {patterns.Jumps.Values.Sum(j => j.Count)} mined jump patterns");
            TestContext.Out.WriteLine($"stars: target {all.Average(r => r.Target):0.00}, actual {all.Average(r => r.Stars):0.00} (within 0.4: {all.Count(r => Math.Abs(r.Stars - r.Target) < 0.4) / (double)all.Count:P0})");
            TestContext.Out.WriteLine($"off the music: {all.Average(r => r.OffMusic):P1} (worst {all.Max(r => r.OffMusic):P1})");
            TestContext.Out.WriteLine($"2-beat silences over 3+ mapped notes, per minute: {all.Average(r => r.SilencesPerMinute):0.00} (worst {all.Max(r => r.SilencesPerMinute):0.00})");
            TestContext.Out.WriteLine($"spacing spread {all.Average(r => r.Variety.SpacingSpread):0.00}, angle entropy {all.Average(r => r.Variety.AngleEntropy):0.00}, same measures {all.Average(r => r.Variety.SameMeasures):P0}, rhythm entropy {all.Average(r => r.Variety.RhythmEntropy):0.00}");
            var contrasted = all.Where(r => r.Contrast != null).ToList();
            TestContext.Out.WriteLine($"kiai density vs the rest, relative to the mapper: {contrasted.Average(r => r.Contrast!.Value):0.00} (over {contrasted.Count} songs with a kiai; above 1 = more contrast than the mapper)");
            TestContext.Out.WriteLine($"1/4 share {all.Average(r => r.QuarterShare):P0}, sliders {all.Average(r => r.SliderShare):P0}");

            foreach (var r in all.OrderBy(r => r.SilencesPerMinute).TakeLast(3))
                TestContext.Out.WriteLine($"  most silent: {r.Name} {r.SilencesPerMinute:0.0}/min, stars {r.Target:0.0} -> {r.Stars:0.00}");

            foreach (var r in all.OrderBy(r => r.Variety.SameMeasures).TakeLast(3))
                TestContext.Out.WriteLine($"  most repetitive: {r.Name} same {r.Variety.SameMeasures:P0} spread {r.Variety.SpacingSpread:0.00}");
        }

        /// <summary>
        /// Prints where one song's generated map goes silent while the mapper keeps mapping.
        /// </summary>
        [Explicit]
        [TestCase("NabYss", AimGoal.Wide)]
        [TestCase("scarlet", AimGoal.Wide)]
        [TestCase("Solar Strike", AimGoal.Wide)]
        public void Silences(string song, AimGoal goal)
        {
            string songsPath = Path.Combine(StableLibrary.DEFAULT_PATH, "Songs");
            var (picked, patterns) = loaded ??= load(songsPath);
            MinedPatternSet.Current = patterns;

            var (folder, difficulty) = picked.First(p => p.difficulty.File.Contains(song, StringComparison.OrdinalIgnoreCase));

            var settings = new FreeplayGenerationSettings { Style = PatternStyle.Jumps, Seed = 1 };
            AimGoals.Apply(settings, goal, level);
            settings.UseRanges = false;

            var generated = FreeplayGenerationTest.generate(Path.Combine(songsPath, folder, difficulty.File), settings, out var source, calibrate: true);
            var objects = generated.HitObjects.Where(h => h is not Spinner).ToList();

            TestContext.Out.WriteLine($"{folder}: {objects.Count} objects from {source.HitObjects.Count}, stars {((Beatmaps.FreeplayBeatmap)generated).StarRating:0.00}");

            for (int i = 1; i < objects.Count; i++)
            {
                double beat = generated.ControlPointInfo.TimingPointAt(objects[i].StartTime).BeatLength;
                double from = objects[i - 1].GetEndTime(), to = objects[i].StartTime;
                var inside = source.HitObjects.Where(h => h.StartTime > from + 5 && h.StartTime < to - 5).ToList();

                if (to - from >= beat * 2 - 5 && inside.Count >= 3)
                {
                    TestContext.Out.WriteLine($"  {from:0}-{to:0} ({(to - from) / beat:0.0} beats) after {objects[i - 1].GetType().Name}: "
                                              + string.Join(" ", inside.Select(h => $"{(h.StartTime - from) / beat:0.00}{(h is IHasDuration d && d.Duration > 0 ? "s" : "")}")));
                }
            }
        }

        private record Row(string Name, double Target, double Stars, double OffMusic, double SilencesPerMinute, VarietyReport.Variety Variety, double QuarterShare, double SliderShare, double? Contrast);

        /// <summary>
        /// How much denser the kiai is than the rest (objects per second), or null without both.
        /// </summary>
        private static double? kiaiRatio(IBeatmap map, IBeatmap timing)
        {
            var objects = map.HitObjects.Where(h => h is not Spinner).ToList();
            bool kiai(double t) => timing.ControlPointInfo.EffectPointAt(t + 1).KiaiMode;

            var inKiai = objects.Where(h => kiai(h.StartTime)).ToList();
            var outside = objects.Where(h => !kiai(h.StartTime)).ToList();

            if (inKiai.Count < 20 || outside.Count < 20)
                return null;

            double rate(List<HitObject> list) => list.Count / Math.Max(1, list.Max(h => h.StartTime) - list.Min(h => h.StartTime));
            return rate(inKiai) / rate(outside);
        }

        private static Row measure(string path, FreeplayGenerationSettings settings)
        {
            var generated = FreeplayGenerationTest.generate(path, settings, out var source, calibrate: true);
            var objects = generated.HitObjects.Where(h => h is not Spinner).ToList();

            bool heard(double time) => source.HitObjects.Any(h => Math.Abs(h.StartTime - time) < 6 || (time > h.StartTime && time < h.GetEndTime() + 6));

            int silences = 0, quarters = 0;

            for (int i = 1; i < objects.Count; i++)
            {
                double beat = generated.ControlPointInfo.TimingPointAt(objects[i].StartTime).BeatLength;
                double from = objects[i - 1].GetEndTime(), to = objects[i].StartTime;

                if (to - from >= beat * 2 - 5 && source.HitObjects.Count(h => h.StartTime > from + 5 && h.StartTime < to - 5) >= 3)
                    silences++;

                if ((objects[i].StartTime - objects[i - 1].StartTime) / beat < 0.375)
                    quarters++;
            }

            double minutes = Math.Max(0.5, (objects[^1].StartTime - objects[0].StartTime) / 60000);

            return new Row(Path.GetFileNameWithoutExtension(path), settings.StarRating, ((Beatmaps.FreeplayBeatmap)generated).StarRating,
                objects.Count(h => !heard(h.StartTime)) / (double)objects.Count, silences / minutes,
                VarietyReport.MeasureGenerated(generated), quarters / (double)objects.Count, objects.Count(h => h is Slider) / (double)objects.Count,
                kiaiRatio(generated, source) / kiaiRatio(source, source));
        }

        /// <summary>
        /// Picks songs at random (one difficulty each, the most detailed, as the game does), and mines the aim collection.
        /// </summary>
        private static (List<(string, StableDifficulty)>, MinedPatternSet) load(string songsPath)
        {
            var folders = Directory.GetDirectories(songsPath);
            var difficulties = new ConcurrentBag<(string folder, StableDifficulty difficulty)>();

            Parallel.ForEach(folders, folder =>
            {
                foreach (string file in Directory.EnumerateFiles(folder, "*.osu"))
                {
                    try
                    {
                        if (StableHeaderReader.Read(file, Path.GetFileName(file)) is StableDifficulty difficulty)
                            difficulties.Add((Path.GetFileName(folder), difficulty));
                    }
                    catch
                    {
                        // Broken files are skipped, as the game does.
                    }
                }
            });

            // In a fixed order: the scan is parallel, and the songs picked must be the same every run to compare runs.
            var all = difficulties.OrderBy(d => d.folder, StringComparer.Ordinal).ThenBy(d => d.difficulty.File, StringComparer.Ordinal).ToList();

            var rng = new Random(42);
            var picked = all.GroupBy(d => d.folder)
                            .Select(g => g.MaxBy(d => d.difficulty.ObjectCount))
                            .Where(d => d.difficulty.Bpm >= 150 && d.difficulty.LengthMs >= 60_000 && d.difficulty.ObjectCount >= 200)
                            .OrderBy(_ => rng.Next())
                            .Take(songs)
                            .ToList();

            var patterns = new MinedPatternSet();
            var byHash = all.GroupBy(d => d.difficulty.MD5).ToDictionary(g => g.Key, g => g.First());

            string collectionName = new PatternSourceSettings().Aim!;
            var collection = StableCollection.Read(Path.Combine(StableLibrary.DEFAULT_PATH, "collection.db"))
                                             .FirstOrDefault(c => string.Equals(c.Name.Trim(), collectionName.Trim(), StringComparison.OrdinalIgnoreCase));

            foreach (string hash in collection?.BeatmapHashes.Take(1000) ?? Enumerable.Empty<string>())
            {
                if (!byHash.TryGetValue(hash, out var entry))
                    continue;

                try
                {
                    using (var stream = new LineBufferedReader(File.OpenRead(Path.Combine(songsPath, entry.folder, entry.difficulty.File))))
                    {
                        var beatmap = Decoder.GetDecoder<Beatmap>(stream).Decode(stream);
                        patterns.Add(PatternCategory.Aim, new TestWorkingBeatmap(beatmap).GetPlayableBeatmap(new OsuRuleset().RulesetInfo));
                    }
                }
                catch
                {
                    // As in the game, a map that can't be read just isn't mined.
                }
            }

            return (picked, patterns);
        }
    }
}
