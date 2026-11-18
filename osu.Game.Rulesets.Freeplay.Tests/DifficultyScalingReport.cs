// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Freeplay.Beatmaps;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Tests.Beatmaps;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    /// <summary>
    /// Prints how generated maps change with the star rating, to tune difficulty scaling by numbers rather than by guessing.
    /// </summary>
    [TestFixture]
    public class DifficultyScalingReport
    {
        private static readonly string[] maps = { "100374.osu", "1124896.osu", "1341554.osu", "1450162.osu", "2593923.osu", "801165.osu" };

        [Explicit]
        [TestCase(PatternStyle.Mixed)]
        [TestCase(PatternStyle.Jumps)]
        [TestCase(PatternStyle.Streams)]
        public void Report(PatternStyle style)
        {
            TestContext.Out.WriteLine($"style {style}");
            TestContext.Out.WriteLine("target | raw★  calib★ | obj/min | jump px | px/ms | 1/4 % | longest burst");

            foreach (double stars in new[] { 2.0, 3, 4, 5, 6, 7, 8, 9 })
            {
                var rows = maps.Select(m => measure(m, style, stars)).ToList();

                TestContext.Out.WriteLine($"{stars,5:0.0}★ | {rows.Average(r => r.Raw),5:0.00} {rows.Average(r => r.Calibrated),6:0.00} | {rows.Average(r => r.PerMinute),7:0} | {rows.Average(r => r.Jump),7:0} | {rows.Average(r => r.Velocity),5:0.00} | {rows.Average(r => r.QuarterShare) * 100,5:0} | {rows.Average(r => r.LongestBurst),5:0}"
                                          + $"   (calib range {rows.Min(r => r.Calibrated):0.00}-{rows.Max(r => r.Calibrated):0.00})");
            }
        }

        /// <summary>
        /// Jump distances in the aim mode's default settings, split by what comes before the jump, to find where jumps get small.
        /// </summary>
        [Explicit]
        [TestCase(0.0)]
        [TestCase(0.5)]
        [TestCase(1.0)]
        public void AimJumpDistances(double iq)
        {
            var aim = Configuration.FreeplayMode.Find("aim")!;

            foreach (string file in maps)
            {
                string path = Path.Combine(FreeplayGenerationTest.RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "Resources", "Beatmaps", file);

                var settings = aim.CreateDefaults();
                settings.StarRating = 5;
                settings.PatternIq = iq;
                settings.Seed = 5;

                double stars = generate(path, settings, out var result);

                var objects = result.HitObjects.Cast<OsuHitObject>().ToList();
                var afterCircle = new List<float>();
                var afterSlider = new List<float>();

                for (int i = 1; i < objects.Count; i++)
                {
                    double beatLength = result.ControlPointInfo.TimingPointAt(objects[i].StartTime).BeatLength;
                    double gapBeats = (objects[i].StartTime - objects[i - 1].StartTime) / beatLength;

                    if (gapBeats < 0.4 || gapBeats > 0.6 || objects[i] is Spinner || objects[i - 1] is Spinner)
                        continue;

                    if (objects[i - 1] is Slider slider)
                        afterSlider.Add(Vector2.Distance(slider.EndPosition, objects[i].Position));
                    else
                        afterCircle.Add(Vector2.Distance(objects[i - 1].Position, objects[i].Position));
                }

                string describe(List<float> d) => d.Count == 0 ? "-" : $"n={d.Count} p10 {percentile(d, 0.1):0} median {percentile(d, 0.5):0} p90 {percentile(d, 0.9):0}";

                TestContext.Out.WriteLine($"IQ {iq} {file} {stars:0.00}★ 1/2 jumps after circles: {describe(afterCircle)} | after sliders: {describe(afterSlider)}");
            }
        }

        private static float percentile(List<float> values, double p)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[(int)Math.Clamp(Math.Round(p * (sorted.Count - 1)), 0, sorted.Count - 1)];
        }

        [Explicit]
        [Test]
        public void GapHistogram()
        {
            foreach (string file in maps)
            {
                string path = Path.Combine(FreeplayGenerationTest.RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "Resources", "Beatmaps", file);

                IBeatmap source;
                using (var stream = new LineBufferedReader(File.OpenRead(path)))
                    source = Decoder.GetDecoder<Beatmap>(stream).Decode(stream);

                var settings = new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Jumps, AllowStreams = false, Seed = 5, CalibrateStarRating = true };
                var profile = new DifficultyProfile(settings);
                var points = new SectionPlanner(source, settings, profile, 5).Plan(RhythmExtractor.Extract(source, settings.RhythmSource));

                generate(path, settings, out var result);

                TestContext.Out.WriteLine($"{file}: bpm {60000 / source.ControlPointInfo.TimingPointAt(source.HitObjects[0].StartTime).BeatLength:0} source {histogram(source.HitObjects.Select(h => (h.StartTime, h.GetEndTime())).ToList(), source)}");
                TestContext.Out.WriteLine($"   planned {histogram(points.Select(p => (p.Time, p.Time)).ToList(), source)}");
                TestContext.Out.WriteLine($"   output  {histogram(result.HitObjects.Select(h => (h.StartTime, h.GetEndTime())).ToList(), source)}  sliders {result.HitObjects.Count(h => h is Slider)}");
            }
        }

        private static string histogram(List<(double start, double end)> times, IBeatmap beatmap)
        {
            var buckets = new SortedDictionary<string, int>();

            for (int i = 1; i < times.Count; i++)
            {
                // Silence between objects: from the end of one to the start of the next.
                double beats = (times[i].start - times[i - 1].end) / beatmap.ControlPointInfo.TimingPointAt(times[i].start).BeatLength;
                string key = beats < 0.3 ? "1/4" : beats < 0.6 ? "1/2" : beats < 0.8 ? "3/4" : beats < 1.2 ? "1/1" : beats < 2.2 ? "2/1" : "long";
                buckets[key] = buckets.GetValueOrDefault(key) + 1;
            }

            return string.Join(" ", buckets.Select(b => $"{b.Key}:{b.Value}"));
        }

        private static Metrics measure(string file, PatternStyle style, double stars)
        {
            string path = Path.Combine(FreeplayGenerationTest.RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "Resources", "Beatmaps", file);

            var raw = generate(path, new FreeplayGenerationSettings { StarRating = stars, Style = style, Seed = 5, CalibrateStarRating = false }, out var beatmap);
            var calibrated = generate(path, new FreeplayGenerationSettings { StarRating = stars, Style = style, Seed = 5, CalibrateStarRating = true }, out _);

            var objects = beatmap.HitObjects.Cast<OsuHitObject>().ToList();

            var jumps = new List<float>();
            var velocities = new List<double>();
            int quarter = 0, burst = 0, longestBurst = 0;

            for (int i = 1; i < objects.Count; i++)
            {
                var previous = objects[i - 1];
                var current = objects[i];

                double beatLength = beatmap.ControlPointInfo.TimingPointAt(current.StartTime).BeatLength;
                double gap = current.StartTime - previous.GetEndTime();
                double gapBeats = (current.StartTime - previous.StartTime) / beatLength;

                if (gapBeats < 0.375)
                {
                    quarter++;
                    longestBurst = Math.Max(longestBurst, ++burst + 1);
                    continue;
                }

                burst = 0;

                if (previous is Spinner || current is Spinner || gap > 1000)
                    continue;

                Vector2 from = previous is Slider s ? s.EndPosition : previous.Position;
                float distance = Vector2.Distance(from, current.Position);

                jumps.Add(distance);
                velocities.Add(distance / Math.Max(1, gap));
            }

            double minutes = Math.Max(1, objects[^1].StartTime - objects[0].StartTime) / 60000;

            return new Metrics
            {
                Raw = raw,
                Calibrated = calibrated,
                PerMinute = objects.Count / minutes,
                Jump = jumps.Count > 0 ? jumps.Average() : 0,
                Velocity = velocities.Count > 0 ? velocities.Average() : 0,
                QuarterShare = (double)quarter / Math.Max(1, objects.Count - 1),
                LongestBurst = longestBurst,
            };
        }

        private static double generate(string path, FreeplayGenerationSettings settings, out IBeatmap result)
        {
            IBeatmap source;

            using (var stream = new LineBufferedReader(File.OpenRead(path)))
                source = Decoder.GetDecoder<Beatmap>(stream).Decode(stream);

            FreeplayGenerationSettings.Current = settings;

            result = new TestWorkingBeatmap(source).GetPlayableBeatmap(new FreeplayRuleset().RulesetInfo);
            return ((FreeplayBeatmap)result).StarRating;
        }

        private struct Metrics
        {
            public double Raw, Calibrated, PerMinute, Jump, Velocity, QuarterShare, LongestBurst;
        }
    }
}
