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
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Tests.Beatmaps;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    [TestFixture]
    public class FreeplayGenerationTest
    {
        /// <summary>
        /// The repository root, for reading test beatmaps from source.
        /// </summary>
        public static string RepositoryRoot => findRepositoryRoot();

        /// <summary>
        /// Test beatmaps that are real, dense maps (not format edge cases or low difficulties, where the grid is filled on purpose).
        /// </summary>
        private static IEnumerable<string> realBeatmaps() => sourceBeatmaps().Where(p => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), @"^\d+\.osu$") && !p.EndsWith("20544.osu", StringComparison.Ordinal));

        internal static IEnumerable<string> sourceBeatmaps()
        {
            return Directory.GetFiles(Path.Combine(RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "Resources", "Beatmaps"), "*.osu").Order();
        }

        [TearDown]
        public void TearDown() => FreeplayGenerationSettings.Current = new FreeplayGenerationSettings();

        [TestCaseSource(nameof(sourceBeatmaps))]
        public void TestGeneratedObjectsArePlayable(string path)
        {
            foreach (double stars in new[] { 1.0, 3, 5, 7 })
            {
                foreach (var style in Enum.GetValues<PatternStyle>())
                {
                    var settings = new FreeplayGenerationSettings { StarRating = stars, Style = style, Seed = 1234 };
                    var beatmap = generate(path, settings, out var source);

                    if (source.HitObjects.Count >= 10)
                        Assert.That(beatmap.HitObjects, Is.Not.Empty, $"{stars}* {style}: no objects generated");

                    assertPlayable(beatmap, new DifficultyProfile(settings), $"{Path.GetFileName(path)} {stars}* {style}", checkDensity: style is PatternStyle.Jumps or PatternStyle.Flow or PatternStyle.Sliders);
                }
            }
        }

        [TestCaseSource(nameof(sourceBeatmaps))]
        public void TestSameSeedGivesSameObjects(string path)
        {
            var settings = new FreeplayGenerationSettings { Style = PatternStyle.Mixed, Seed = 42 };

            var first = generate(path, settings, out _);
            var second = generate(path, settings, out _);

            Assert.That(second.HitObjects.Select(describe), Is.EqualTo(first.HitObjects.Select(describe)));
        }

        [Test]
        public void TestDifferentSeedsGiveDifferentObjects()
        {
            string path = sourceBeatmaps().First(p => p.EndsWith("diffcalc-test.osu", StringComparison.Ordinal));

            var first = generate(path, new FreeplayGenerationSettings { Seed = 1 }, out _);
            var second = generate(path, new FreeplayGenerationSettings { Seed = 2 }, out _);

            Assert.That(second.HitObjects.Select(describe), Is.Not.EqualTo(first.HitObjects.Select(describe)));
        }

        [Test]
        public void TestHigherStarsGiveDenserObjects()
        {
            string path = sourceBeatmaps().First(p => p.EndsWith("diffcalc-test.osu", StringComparison.Ordinal));

            int easy = generate(path, new FreeplayGenerationSettings { StarRating = 1, Seed = 7 }, out _).HitObjects.Count;
            int hard = generate(path, new FreeplayGenerationSettings { StarRating = 7, Seed = 7 }, out var source).HitObjects.Count;

            TestContext.Out.WriteLine($"source: {source.HitObjects.Count}, 1*: {easy}, 7*: {hard}");
            Assert.That(hard, Is.GreaterThan(easy));
        }

        [TestCaseSource(nameof(sourceBeatmaps))]
        public void TestJumpsStyleHasNoStreams(string path)
        {
            var beatmap = generate(path, new FreeplayGenerationSettings { StarRating = 7, Style = PatternStyle.Jumps, AllowStreams = false, Seed = 3 }, out _);
            Assert.That(shortestGapBeats(beatmap), Is.GreaterThanOrEqualTo(0.375), "1/4 gap generated in jumps style");
        }

        [Test]
        public void TestAimBurstsAreOptional()
        {
            // A map with plenty of 1/4 in its rhythm.
            string path = sourceBeatmaps().First(p => p.EndsWith("801165.osu", StringComparison.Ordinal));

            var off = generate(path, new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Jumps, AllowStreams = false, Seed = 4 }, out _);
            var on = generate(path, new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Jumps, AllowStreams = true, Seed = 4 }, out _);

            Assert.That(shortestGapBeats(off), Is.GreaterThanOrEqualTo(0.375), "bursts without the setting");

            int kickSliders(IBeatmap b) => b.HitObjects.OfType<Slider>().Count(s => s.Duration / b.ControlPointInfo.TimingPointAt(s.StartTime).BeatLength < 0.3);
            Assert.That(kickSliders(off), Is.LessThan(3), "bursts are left out by default, not turned into kick sliders");

            var kicks = generate(path, new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Jumps, AllowStreams = false, KickSliders = true, Seed = 4 }, out _);
            Assert.That(kickSliders(kicks), Is.GreaterThan(10), "with kick sliders on, bursts in the music should be covered by them");

            var bursts = streamLengths(on).Where(l => l > 1).ToList();
            Assert.That(bursts, Is.Not.Empty, "no bursts with the setting on");
            Assert.That(bursts.Max(), Is.LessThanOrEqualTo(5), "aim bursts should never become streams");
        }

        /// <summary>
        /// Aim follows the mapper: notes go where the source has a sound (an object, or inside one of its sliders),
        /// and the music isn't left silent where the mapper kept mapping.
        /// </summary>
        [TestCaseSource(nameof(realBeatmaps))]
        public void TestAimFollowsTheMapper(string path)
        {
            var generated = generate(path, new FreeplayGenerationSettings { StarRating = 5.5, Style = PatternStyle.Jumps, AllowStreams = false, Seed = 11 }, out var source, calibrate: true);

            var sourceObjects = source.HitObjects;
            bool heard(double time) => sourceObjects.Any(h => Math.Abs(h.StartTime - time) < 6 || (time > h.StartTime && time < h.GetEndTime() + 6));

            var objects = generated.HitObjects.Where(h => h is not Spinner).ToList();
            double offMusic = objects.Count(h => !heard(h.StartTime)) / (double)objects.Count;

            int silences = 0;

            for (int i = 1; i < objects.Count; i++)
            {
                double beat = generated.ControlPointInfo.TimingPointAt(objects[i].StartTime).BeatLength;
                double from = objects[i - 1].GetEndTime(), to = objects[i].StartTime;

                if (to - from >= beat * 2 - 5 && sourceObjects.Count(h => h.StartTime > from + 5 && h.StartTime < to - 5) >= 3)
                    silences++;
            }

            TestContext.WriteLine($"{Path.GetFileName(path)}: {objects.Count} objects, {offMusic:P1} off the music, {silences} silences over 3+ mapped notes");

            Assert.That(offMusic, Is.LessThan(0.03), "notes where the mapper has no sound");
            Assert.That(silences, Is.LessThanOrEqualTo(3), "two-beat silences while the mapper keeps mapping");
        }

        [Test]
        public void TestAntiJumpsReverseSpacing()
        {
            string path = sourceBeatmaps().First(p => p.EndsWith("801165.osu", StringComparison.Ordinal));

            (double quick, double slow) spacing(bool anti)
            {
                var beatmap = generate(path, new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Jumps, AllowStreams = false, Seed = 5, AntiJumps = anti }, out _);
                var quick = new List<double>();
                var slow = new List<double>();

                var objects = beatmap.HitObjects.OfType<OsuHitObject>().Where(h => h is not Spinner).ToList();

                for (int i = 1; i < objects.Count; i++)
                {
                    if (objects[i - 1] is not HitCircle || objects[i] is not HitCircle)
                        continue;

                    double beat = beatmap.ControlPointInfo.TimingPointAt(objects[i].StartTime).BeatLength;
                    double gap = (objects[i].StartTime - objects[i - 1].StartTime) / beat;
                    double distance = Vector2.Distance(objects[i].StackedPosition, objects[i - 1].StackedPosition);

                    if (gap > 0.4 && gap < 0.6)
                        quick.Add(distance);
                    else if (gap > 0.9 && gap < 1.1)
                        slow.Add(distance);
                }

                Assert.That(quick, Is.Not.Empty);
                Assert.That(slow, Is.Not.Empty);
                return (quick.Average(), slow.Average());
            }

            var anti = spacing(true);

            Assert.That(anti.quick, Is.GreaterThan(anti.slow * 1.5), "1/2 jumps should be much wider than 1/1 ones");
        }

        [Test]
        public void TestRangesRollWithinBounds()
        {
            var settings = new FreeplayGenerationSettings { UseRanges = true, StarRating = 4.5, StarRatingMax = 5.5, Density = 1, DensityMax = 0.7, PatternIq = 0.3 };
            var rng = new Random(1);

            var rolls = Enumerable.Range(0, 200).Select(_ => settings.Roll(rng)).ToList();

            Assert.That(rolls.All(r => r.StarRating >= 4.5 - 1e-9 && r.StarRating <= 5.5 + 1e-9));
            Assert.That(rolls.All(r => r.Density >= 0.7 - 1e-9 && r.Density <= 1 + 1e-9), "a range given upside down still works");
            Assert.That(rolls.Select(r => r.StarRating).Distinct().Count(), Is.GreaterThan(5), "values should vary");
            Assert.That(rolls.All(r => r.PatternIq == 0.3), "settings without an upper end stay fixed");

            settings.UseRanges = false;
            Assert.That(settings.Roll(rng).StarRating, Is.EqualTo(4.5));
        }

        [Test]
        public void TestIqChangesAngles()
        {
            string path = sourceBeatmaps().First(p => p.EndsWith("2593923.osu", StringComparison.Ordinal));

            double rightAngles(double iq)
            {
                var objects = generate(path, new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Jumps, AllowStreams = false, PatternIq = iq, Seed = 8 }, out _)
                              .HitObjects.OfType<HitCircle>().ToList();

                int near = 0, total = 0;

                for (int i = 2; i < objects.Count; i++)
                {
                    Vector2 a = objects[i - 1].Position - objects[i - 2].Position;
                    Vector2 b = objects[i].Position - objects[i - 1].Position;

                    if (a.Length < 30 || b.Length < 30)
                        continue;

                    double turn = Math.Acos(Math.Clamp(Vector2.Dot(a, b) / (a.Length * b.Length), -1, 1));
                    total++;

                    if (turn > 1.1 && turn < 2.0)
                        near++;
                }

                return (double)near / total;
            }

            double slop = rightAngles(0), smart = rightAngles(1);
            TestContext.Out.WriteLine($"share of 63-115° turns: IQ 0 {slop:P0}, IQ 100 {smart:P0}");

            Assert.That(smart, Is.GreaterThan(slop + 0.1));
        }

        [TestCaseSource(nameof(sourceBeatmaps))]
        public void TestDisablingStreams(string path)
        {
            var beatmap = generate(path, new FreeplayGenerationSettings { StarRating = 7, Style = PatternStyle.Streams, AllowStreams = false, Seed = 3 }, out _);
            Assert.That(shortestGapBeats(beatmap), Is.GreaterThanOrEqualTo(0.375), "1/4 gap generated with streams disabled");
        }

        [TestCaseSource(nameof(sourceBeatmaps))]
        public void TestDisablingSlidersAndSpinners(string path)
        {
            var beatmap = generate(path, new FreeplayGenerationSettings { Style = PatternStyle.Sliders, SliderAmount = 0, AllowSpinners = false, Seed = 3 }, out _);

            Assert.That(beatmap.HitObjects.OfType<Slider>(), Is.Empty);
            Assert.That(beatmap.HitObjects.OfType<Spinner>(), Is.Empty);
        }

        [TestCase("2593923.osu")]
        [TestCase("diffcalc-test.osu")]
        public void TestRemixKeepsMapperObjects(string file)
        {
            string path = sourceBeatmaps().First(p => p.EndsWith(file, StringComparison.Ordinal));
            var settings = new FreeplayGenerationSettings { RhythmSource = RhythmSource.Remix, Seed = 4 };

            var remixed = generate(path, settings, out var source);
            var original = new TestWorkingBeatmap(source).GetPlayableBeatmap(new OsuRuleset().RulesetInfo);

            Assert.That(remixed.HitObjects.Select(h => (h.StartTime, h.GetType())), Is.EqualTo(original.HitObjects.Select(h => (h.StartTime, h.GetType()))));
            Assert.That(remixed.HitObjects.Cast<OsuHitObject>().Select(h => h.Position), Is.Not.EqualTo(original.HitObjects.Cast<OsuHitObject>().Select(h => h.Position)));

            // Combos that fit nowhere else keep the mapper's placement, and mappers sometimes run slider ends slightly off the playfield.
            const float mapper_tolerance = 16;

            foreach (var obj in remixed.HitObjects.Cast<OsuHitObject>())
            {
                var end = obj is Slider slider ? slider.EndPosition : obj.Position;

                foreach (var position in new[] { obj.Position, end })
                {
                    Assert.That(position.X, Is.InRange(-mapper_tolerance, OsuPlayfield.BASE_SIZE.X + mapper_tolerance), $"object at {obj.StartTime}");
                    Assert.That(position.Y, Is.InRange(-mapper_tolerance, OsuPlayfield.BASE_SIZE.Y + mapper_tolerance), $"object at {obj.StartTime}");
                }
            }
        }

        [Test]
        public void TestStarRatingIsCalibrated()
        {
            string path = sourceBeatmaps().First(p => p.EndsWith("2593923.osu", StringComparison.Ordinal));

            double low = ((FreeplayBeatmap)generate(path, new FreeplayGenerationSettings { StarRating = 3, Style = PatternStyle.Jumps, Seed = 2 }, out _, calibrate: true)).StarRating;
            double high = ((FreeplayBeatmap)generate(path, new FreeplayGenerationSettings { StarRating = 7, Style = PatternStyle.Jumps, Seed = 2 }, out _, calibrate: true)).StarRating;

            TestContext.Out.WriteLine($"3* target: {low:0.00}, 7* target: {high:0.00}");

            Assert.That(low, Is.EqualTo(3).Within(1));
            Assert.That(high, Is.EqualTo(7).Within(1.5));
            Assert.That(high - low, Is.GreaterThan(2.5));
        }

        [Test]
        public void TestMinedPatternsArePlayable()
        {
            var patterns = new MinedPatternSet();

            foreach (string file in new[] { "2593923.osu", "801165.osu", "1124896.osu" })
            {
                using (var stream = new LineBufferedReader(File.OpenRead(sourceBeatmaps().First(p => p.EndsWith(file, StringComparison.Ordinal)))))
                {
                    var source = Decoder.GetDecoder<Beatmap>(stream).Decode(stream);
                    var playable = new TestWorkingBeatmap(source).GetPlayableBeatmap(new OsuRuleset().RulesetInfo);
                    patterns.Add(PatternCategory.Aim, playable);
                    patterns.Add(PatternCategory.Streams, playable);
                }
            }

            TestContext.Out.WriteLine($"mined {patterns.Count} patterns, {patterns.Streams.Count} streams");
            Assert.That(patterns.Jumps[PatternCategory.Aim], Is.Not.Empty);

            MinedPatternSet.Current = patterns;

            try
            {
                foreach (var style in new[] { PatternStyle.Jumps, PatternStyle.Streams, PatternStyle.Mixed })
                {
                    var settings = new FreeplayGenerationSettings { StarRating = 5, Style = style, Seed = 8 };
                    var beatmap = generate(sourceBeatmaps().First(p => p.EndsWith("2593923.osu", StringComparison.Ordinal)), settings, out _);
                    assertPlayable(beatmap, new DifficultyProfile(settings), $"mined {style}", checkDensity: style == PatternStyle.Jumps);
                }
            }
            finally
            {
                MinedPatternSet.Current = null;
            }
        }

        [Test]
        public void TestStreamNotesOverlap()
        {
            string path = sourceBeatmaps().First(p => p.EndsWith("2593923.osu", StringComparison.Ordinal));

            foreach (double stars in new[] { 4.0, 6 })
            {
                var settings = new FreeplayGenerationSettings { StarRating = stars, Style = PatternStyle.Streams, Seed = 6 };
                var beatmap = generate(path, settings, out _);
                var objects = beatmap.HitObjects.Cast<OsuHitObject>().ToList();

                var streamSteps = objects.Zip(objects.Skip(1), (a, b) => (a, b))
                                         .Where(p => (p.b.StartTime - p.a.StartTime) / beatmap.ControlPointInfo.TimingPointAt(p.b.StartTime).BeatLength < 0.3)
                                         .Select(p => Vector2.Distance(p.a.Position, p.b.Position))
                                         .ToList();

                float diameter = 2 * (float)objects[0].Radius;
                TestContext.Out.WriteLine($"{stars}*: average stream step {streamSteps.Average():0.0}px, circle diameter {diameter:0.0}px");

                Assert.That(streamSteps.Average(), Is.LessThan(diameter), $"{stars}*: stream notes don't overlap");
            }
        }

        [TestCase("2593923.osu")]
        [TestCase("801165.osu")]
        public void TestStreamsStyleHasRealStreams(string file)
        {
            string path = sourceBeatmaps().First(p => p.EndsWith(file, StringComparison.Ordinal));
            var beatmap = generate(path, new FreeplayGenerationSettings { StarRating = 4.5, Style = PatternStyle.Streams, Seed = 9 }, out _);

            var streams = streamLengths(beatmap);
            TestContext.Out.WriteLine($"streams: {string.Join(", ", streams.Where(s => s >= 4))}");

            Assert.That(streams.Count(s => s >= 12), Is.GreaterThanOrEqualTo(5), "too few streams of 12+ notes");
        }

        [TestCase("2593923.osu")]
        [TestCase("801165.osu")]
        public void TestDeathstreamsStyleHasDeathstreams(string file)
        {
            string path = sourceBeatmaps().First(p => p.EndsWith(file, StringComparison.Ordinal));
            var settings = new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Deathstreams, Seed = 9 };
            var beatmap = generate(path, settings, out _);

            var streams = streamLengths(beatmap);
            TestContext.Out.WriteLine($"streams: {string.Join(", ", streams.Where(s => s >= 4))}");

            Assert.That(streams.Max(), Is.GreaterThanOrEqualTo(100), "no deathstream of 100+ notes");
            assertPlayable(beatmap, new DifficultyProfile(settings), file, checkDensity: false);
        }

        /// <summary>
        /// The lengths (in notes) of runs of consecutive 1/4 (or faster) gaps.
        /// </summary>
        private static List<int> streamLengths(IBeatmap beatmap)
        {
            var result = new List<int>();
            int current = 1;

            for (int i = 1; i < beatmap.HitObjects.Count; i++)
            {
                double beatLength = beatmap.ControlPointInfo.TimingPointAt(beatmap.HitObjects[i].StartTime).BeatLength;
                bool streamGap = (beatmap.HitObjects[i].StartTime - beatmap.HitObjects[i - 1].StartTime) / beatLength < 0.3;

                if (streamGap)
                    current++;
                else
                {
                    result.Add(current);
                    current = 1;
                }
            }

            result.Add(current);
            return result;
        }

        [Test]
        public void TestEachPatternCanBeUsedAlone()
        {
            string path = sourceBeatmaps().First(p => p.EndsWith("diffcalc-test.osu", StringComparison.Ordinal));

            foreach (var pattern in Enum.GetValues<JumpPatterns>().Where(p => p != JumpPatterns.All))
            {
                var settings = new FreeplayGenerationSettings { Style = PatternStyle.Jumps, Patterns = pattern, Seed = 5 };
                assertPlayable(generate(path, settings, out _), new DifficultyProfile(settings), $"pattern {pattern}");
            }
        }

        /// <summary>
        /// Renders generated objects to images for eyeballing pattern shapes. Each panel shows eight consecutive objects, numbered by colour from dark to light.
        /// </summary>
        [Explicit]
        [TestCase(PatternStyle.Jumps)]
        [TestCase(PatternStyle.Streams)]
        [TestCase(PatternStyle.Flow)]
        [TestCase(PatternStyle.Mixed)]
        [TestCase(PatternStyle.Deathstreams)]
        [TestCase(PatternStyle.Sliders)]
        public void RenderPatterns(PatternStyle style)
        {
            string path = sourceBeatmaps().First(p => p.EndsWith("2593923.osu", StringComparison.Ordinal));
            var objects = generate(path, new FreeplayGenerationSettings { StarRating = 5, Style = style, Seed = 11 }, out _).HitObjects.Cast<OsuHitObject>().ToList();

            const int columns = 4, rows = 4;
            const float panel_scale = 0.6f;

            int panelWidth = (int)(OsuPlayfield.BASE_SIZE.X * panel_scale), panelHeight = (int)(OsuPlayfield.BASE_SIZE.Y * panel_scale);

            using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(panelWidth * columns, panelHeight * rows, new SixLabors.ImageSharp.PixelFormats.Rgba32(20, 20, 28));

            // One panel per combo, as each pattern starts a new combo.
            var combos = new List<List<OsuHitObject>>();

            foreach (var obj in objects)
            {
                if (obj.NewCombo || combos.Count == 0)
                    combos.Add(new List<OsuHitObject>());
                combos[^1].Add(obj);
            }

            for (int panel = 0; panel < Math.Min(combos.Count, columns * rows); panel++)
            {
                var slice = combos[panel];
                var offset = new osuTK.Vector2(panel % columns * panelWidth, panel / columns * panelHeight);

                for (int x = 0; x < panelWidth; x++)
                    image[(int)offset.X + x, (int)offset.Y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(70, 70, 90);
                for (int y = 0; y < panelHeight; y++)
                    image[(int)offset.X, (int)offset.Y + y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(70, 70, 90);

                for (int i = 0; i < slice.Count; i++)
                {
                    var position = offset + slice[i].Position * panel_scale;
                    byte shade = (byte)(80 + 175 * i / Math.Max(1, slice.Count - 1));

                    if (i > 0)
                        drawLine(image, offset + slice[i - 1].Position * panel_scale, position, new SixLabors.ImageSharp.PixelFormats.Rgba32(90, 90, 120));

                    if (slice[i] is Slider slider)
                        drawLine(image, position, offset + slider.EndPosition * panel_scale, new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 170, 60));

                    drawDisc(image, position, slice[i] is Slider ? 7 : 6, new SixLabors.ImageSharp.PixelFormats.Rgba32(255, shade, (byte)(255 - shade / 2)));
                }
            }

            string output = Path.Combine(findRepositoryRoot(), "osu.Game.Rulesets.Freeplay.Tests", "bin", $"patterns-{style}.png");
            using (var stream = File.Create(output))
                image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
            TestContext.Out.WriteLine($"Rendered {objects.Count} objects to {output}");
        }

        private static void drawDisc(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image, osuTK.Vector2 centre, int radius, SixLabors.ImageSharp.PixelFormats.Rgba32 colour)
        {
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    int px = (int)centre.X + x, py = (int)centre.Y + y;

                    if (x * x + y * y <= radius * radius && px >= 0 && py >= 0 && px < image.Width && py < image.Height)
                        image[px, py] = colour;
                }
            }
        }

        private static void drawLine(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image, osuTK.Vector2 from, osuTK.Vector2 to, SixLabors.ImageSharp.PixelFormats.Rgba32 colour)
        {
            int steps = (int)Math.Max(1, (to - from).Length);

            for (int s = 0; s <= steps; s++)
            {
                var p = from + (to - from) * s / steps;

                if (p.X >= 0 && p.Y >= 0 && p.X < image.Width && p.Y < image.Height)
                    image[(int)p.X, (int)p.Y] = colour;
            }
        }

        private static double shortestGapBeats(IBeatmap beatmap)
        {
            var objects = beatmap.HitObjects;
            double shortest = double.MaxValue;

            for (int i = 1; i < objects.Count; i++)
            {
                double beatLength = beatmap.ControlPointInfo.TimingPointAt(objects[i].StartTime).BeatLength;
                shortest = Math.Min(shortest, (objects[i].StartTime - objects[i - 1].StartTime) / beatLength);
            }

            return shortest;
        }

        /// <summary>
        /// Checks objects are in bounds, don't overlap, respect minimum gaps and (if <paramref name="checkDensity"/>) the density limit.
        /// Styles with generated streams are exempt from the density limit by design.
        /// </summary>
        private static void assertPlayable(IBeatmap beatmap, DifficultyProfile profile, string context, bool checkDensity = true)
        {
            var objects = beatmap.HitObjects.Cast<OsuHitObject>().ToList();

            double maxInWindow = profile.MaxObjectsPerSecond * DifficultyProfile.DENSITY_WINDOW / 1000;

            foreach (var obj in checkDensity ? objects : new List<OsuHitObject>())
            {
                int inWindow = objects.Count(o => o.StartTime >= obj.StartTime && o.StartTime - obj.StartTime < DifficultyProfile.DENSITY_WINDOW);
                Assert.That(inWindow, Is.LessThanOrEqualTo(Math.Ceiling(maxInWindow)), $"{context}: too dense after {obj.StartTime}");
            }

            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];

                assertInPlayfield(obj.Position, $"{context}: object {i} at {obj.StartTime}");

                if (obj is Slider slider)
                    assertInPlayfield(slider.EndPosition, $"{context}: slider {i} end at {obj.StartTime}");

                if (i == 0)
                    continue;

                var previous = objects[i - 1];

                Assert.That(obj.StartTime, Is.GreaterThanOrEqualTo(previous.GetEndTime() - 1), $"{context}: object {i} at {obj.StartTime} overlaps previous");
                Assert.That(obj.StartTime - previous.StartTime, Is.GreaterThanOrEqualTo(profile.MinGapMs - 1), $"{context}: object {i} at {obj.StartTime} too close");
            }
        }

        private static void assertInPlayfield(osuTK.Vector2 position, string context)
        {
            Assert.That(position.X, Is.InRange(0, OsuPlayfield.BASE_SIZE.X), context);
            Assert.That(position.Y, Is.InRange(0, OsuPlayfield.BASE_SIZE.Y), context);
        }

        private static string describe(HitObject h) => h is OsuHitObject o ? $"{h.GetType().Name}@{h.StartTime}:{o.Position}" : h.ToString()!;

        /// <summary>
        /// Generates freeplay objects from the beatmap at <paramref name="path"/>. Star rating calibration is off unless <paramref name="calibrate"/> is set,
        /// as it regenerates several times and most tests don't need it.
        /// </summary>
        internal static IBeatmap generate(string path, FreeplayGenerationSettings settings, out IBeatmap source, bool calibrate = false)
        {
            settings.CalibrateStarRating = calibrate;

            using (var stream = new LineBufferedReader(File.OpenRead(path)))
                source = Decoder.GetDecoder<Beatmap>(stream).Decode(stream);

            FreeplayGenerationSettings.Current = settings;

            var result = new TestWorkingBeatmap(source).GetPlayableBeatmap(new FreeplayRuleset().RulesetInfo);
            Assert.That(result, Is.InstanceOf<FreeplayBeatmap>());
            return result;
        }

        private static string findRepositoryRoot()
        {
            // Test case sources are evaluated before a test context exists.
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "osu.sln")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
