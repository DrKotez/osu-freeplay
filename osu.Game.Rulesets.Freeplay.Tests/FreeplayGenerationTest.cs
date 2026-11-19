// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Freeplay.Beatmaps;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Replays;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
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
            Assert.That(bursts.Max(), Is.LessThanOrEqualTo(13), "aim bursts should never become streams");

            // Some triples are played as stacks: three notes on one spot.
            var circles = on.HitObjects.OfType<HitCircle>().ToList();
            int stackedTriples = Enumerable.Range(0, Math.Max(0, circles.Count - 2)).Count(i =>
                circles[i + 2].StartTime - circles[i].StartTime < on.ControlPointInfo.TimingPointAt(circles[i].StartTime).BeatLength * 0.6
                && Vector2.Distance(circles[i].Position, circles[i + 1].Position) < 3 && Vector2.Distance(circles[i + 1].Position, circles[i + 2].Position) < 3);

            Assert.That(stackedTriples, Is.GreaterThan(0), "no stacked triples");

            // And every triple (three 1/4 notes on their own) is one.
            var objects = on.HitObjects.OfType<OsuHitObject>().ToList();
            bool quick(int i) => i > 0 && i < objects.Count && objects[i] is HitCircle && objects[i - 1] is HitCircle
                                 && (objects[i].StartTime - objects[i - 1].StartTime) / on.ControlPointInfo.TimingPointAt(objects[i].StartTime).BeatLength < 0.375;

            for (int i = 0; i + 2 < objects.Count; i++)
            {
                if (!quick(i + 1) || !quick(i + 2) || quick(i) || quick(i + 3))
                    continue;

                Assert.That(Vector2.Distance(objects[i].Position, objects[i + 2].Position), Is.LessThan(3), $"triple at {objects[i].StartTime} isn't stacked");
            }
        }

        [Test]
        public void TestPaletteKeepsSeedsRepeatable()
        {
            string path = sourceBeatmaps().First(p => p.EndsWith("801165.osu", StringComparison.Ordinal));
            var palette = TrackPalette.Roll(new Random(5));

            FreeplayGenerationSettings settings() => new FreeplayGenerationSettings { StarRating = 5.5, Style = PatternStyle.Jumps, Seed = 9, Palette = palette };

            var first = generate(path, settings(), out _, calibrate: true);
            var second = generate(path, settings(), out _, calibrate: true);

            Assert.That(second.HitObjects.Select(h => (h.StartTime, ((OsuHitObject)h).Position)), Is.EqualTo(first.HitObjects.Select(h => (h.StartTime, ((OsuHitObject)h).Position))));
        }

        [Test]
        public void TestNeighbouringSectionsDiffer()
        {
            var rng = new Random(3);
            var palette = new TrackPalette { Character = 0.85 };
            var recent = new List<SectionRecipe>();

            for (int i = 0; i < 200; i++)
                recent.Add(SectionRecipe.Roll(palette, rng, recent, new[] { "a", "b" }, 0.4));

            for (int i = 1; i < recent.Count; i++)
            {
                if (recent[i].Character != SectionCharacter.None)
                    Assert.That(recent[i].Character, Is.Not.EqualTo(recent[i - 1].Character), $"section {i} repeats its character");
            }

            Assert.That(recent.Select(r => r.Character).Distinct().Count(), Is.GreaterThan(4));
        }

        /// <summary>
        /// Aim follows the music: notes go where the source has a sound (an object, or inside one of its sliders) or on the half-beat grid
        /// (aim keeps on jumping through the mapper's rests), and the music isn't left silent where the mapper kept mapping.
        /// </summary>
        [TestCaseSource(nameof(realBeatmaps))]
        public void TestAimFollowsTheMapper(string path)
        {
            var generated = generate(path, new FreeplayGenerationSettings { StarRating = 5.5, Style = PatternStyle.Jumps, AllowStreams = false, Seed = 11 }, out var source, calibrate: true);

            var sourceObjects = source.HitObjects;
            bool heard(double time) => sourceObjects.Any(h => Math.Abs(h.StartTime - time) < 6 || (time > h.StartTime && time < h.GetEndTime() + 6));

            var objects = generated.HitObjects.Where(h => h is not Spinner).ToList();
            bool onGrid(double time) => RhythmExtractor.BeatStrength(generated.ControlPointInfo, time) >= 2;
            double offMusic = objects.Count(h => !heard(h.StartTime) && !onGrid(h.StartTime)) / (double)objects.Count;

            int silences = 0;

            for (int i = 1; i < objects.Count; i++)
            {
                double beat = generated.ControlPointInfo.TimingPointAt(objects[i].StartTime).BeatLength;
                double from = objects[i - 1].GetEndTime(), to = objects[i].StartTime;

                if (to - from >= beat * 2 - 5 && sourceObjects.Count(h => h.StartTime > from + 5 && h.StartTime < to - 5) >= 3)
                {
                    silences++;
                    TestContext.WriteLine($"  silence {from:0}-{to:0} after {objects[i - 1].GetType().Name}, source inside: {string.Join(" ", sourceObjects.Where(h => h.StartTime > from + 5 && h.StartTime < to - 5).Select(h => $"{h.StartTime:0}{(h.GetEndTime() > h.StartTime ? "s" : "")}"))}");
                }
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
            TestContext.Out.WriteLine($"anti: 1/2 {anti.quick:0}px, 1/1 {anti.slow:0}px");

            // (Aim jumps every half beat now, so the few 1/1 gaps left are mostly around sliders and shapes, which keep part of their size.)
            Assert.That(anti.quick, Is.GreaterThan(anti.slow * 1.35), "1/2 jumps should be much wider than 1/1 ones");
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

        /// <summary>
        /// The stars mode jumps on the tempo's grid and nothing else: every gap is the same half beat (or beat), apart from the music's own rests,
        /// in shapes held for a while, and at about the wanted star rating.
        /// </summary>
        [TestCaseSource(nameof(realBeatmaps))]
        public void TestMetronomeJumpsOnTheGrid(string path)
        {
            var generated = generate(path, new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Metronome, Seed = 5 }, out var source, calibrate: true);
            var objects = generated.HitObjects.OfType<HitCircle>().ToList();

            Assert.That(objects.Count, Is.EqualTo(generated.HitObjects.Count), "only circles");
            Assert.That(objects.Count, Is.GreaterThan(100));

            double beat = generated.ControlPointInfo.TimingPointAt(objects[0].StartTime).BeatLength;
            var gaps = objects.Zip(objects.Skip(1), (a, b) => (b.StartTime - a.StartTime) / beat).ToList();

            int onGrid = gaps.Count(g => Math.Abs(g - 0.5) < 0.02 || Math.Abs(g - 1) < 0.02);
            TestContext.WriteLine($"{Path.GetFileName(path)}: {objects.Count} jumps, {onGrid / (double)gaps.Count:P1} on the grid, {((Beatmaps.FreeplayBeatmap)generated).StarRating:0.00}*");

            Assert.That(gaps.All(g => g > 0.49), "nothing faster than a half beat");
            Assert.That(onGrid, Is.GreaterThan(gaps.Count * 0.97), "jumps follow the tempo");
            // Songs too slow for the rating (the track picker leaves them out) can't get there with jumps that fit on the screen.
            if (60000 / beat >= Screens.TrackPicker.MinimumBpmFor(5) * 0.8)
                Assert.That(((Beatmaps.FreeplayBeatmap)generated).StarRating, Is.EqualTo(5).Within(0.6));

            // Shapes come back round, but change: some jumps land near where one landed a few before, not all of them.
            int repeats = Enumerable.Range(7, objects.Count - 7).Count(i => Enumerable.Range(2, 6).Any(k => Vector2.Distance(objects[i].Position, objects[i - k].Position) < 30));
            Assert.That(repeats, Is.GreaterThan(objects.Count / 5), "shapes held for a while");
            Assert.That(repeats, Is.LessThan(objects.Count * 0.95), "shapes change");
        }

        /// <summary>
        /// A seed code carries the whole map: played from the code on the same song, every object comes back as it was generated.
        /// </summary>
        [TestCase(PatternStyle.Metronome, 5.0)]
        [TestCase(PatternStyle.Jumps, 5.5)]
        [TestCase(PatternStyle.Mixed, 4.0)]
        public void TestSeedCodeGivesTheSameMap(PatternStyle style, double stars)
        {
            string path = realBeatmaps().First(p => p.EndsWith("1124896.osu", StringComparison.Ordinal));

            var played = (FreeplayBeatmap)generate(path, new FreeplayGenerationSettings { StarRating = stars, Style = style, Seed = 11 }, out _, calibrate: true);

            var seed = new Configuration.SeedMap
            {
                Song = { MD5 = "abc", SetId = 1124896, Artist = "a", Title = "t", Version = "v" },
                Mode = "test",
                StarRating = played.StarRating,
                Seed = played.Seed,
                StackLeniency = played.StackLeniency,
                Objects = played.PackedObjects!,
            };
            seed.SetDifficulty(played.Difficulty);

            string code = seed.ToCode();
            TestContext.WriteLine($"{style}: {played.HitObjects.Count} objects → {code.Length} characters");

            // Pasted from a chat, split across lines.
            var read = Configuration.SeedMap.FromCode("вот: " + string.Join("\n", code.Chunk(500).Select(c => new string(c))));
            Assert.That(read, Is.Not.Null);
            Assert.That(read!.Song.SetId, Is.EqualTo(1124896));

            var again = (FreeplayBeatmap)generate(path, new FreeplayGenerationSettings { StarRating = 1, Style = PatternStyle.Jumps, FixedMap = read }, out _);

            Assert.That(again.HitObjects.Count, Is.EqualTo(played.HitObjects.Count));
            Assert.That(again.Difficulty.CircleSize, Is.EqualTo(played.Difficulty.CircleSize));

            for (int i = 0; i < played.HitObjects.Count; i++)
            {
                var a = (OsuHitObject)played.HitObjects[i];
                var b = (OsuHitObject)again.HitObjects[i];

                Assert.That(b.GetType(), Is.EqualTo(a.GetType()));
                Assert.That(b.StartTime, Is.EqualTo(a.StartTime).Within(0.001));
                Assert.That(b.StackedPosition.X, Is.EqualTo(a.StackedPosition.X).Within(0.001));
                Assert.That(b.StackedPosition.Y, Is.EqualTo(a.StackedPosition.Y).Within(0.001));
                Assert.That(b.NewCombo, Is.EqualTo(a.NewCombo));
                Assert.That(b.Samples.Select(s => (s.Name, s.Bank, s.Volume)), Is.EqualTo(a.Samples.Select(s => (s.Name, s.Bank, s.Volume))));

                if (a is Slider sa)
                {
                    var sb = (Slider)b;
                    Assert.That(sb.EndTime, Is.EqualTo(sa.EndTime).Within(0.001));
                    Assert.That(sb.RepeatCount, Is.EqualTo(sa.RepeatCount));
                    Assert.That(Vector2.Distance(sb.EndPosition, sa.EndPosition), Is.LessThan(0.001f));
                }

                if (a is Spinner spa)
                    Assert.That(((Spinner)b).EndTime, Is.EqualTo(spa.EndTime).Within(0.001));
            }
        }

        /// <summary>
        /// "Одна фигура подряд — не больше N": no aim shape or stars figure comes more than N times in a row; without a limit, aim shapes do repeat.
        /// </summary>
        [Test]
        public void TestSameShapeLimit()
        {
            string path = realBeatmaps().First(p => p.EndsWith("1124896.osu", StringComparison.Ordinal));
            generate(path, new FreeplayGenerationSettings { StarRating = 5 }, out var source);

            int longestRun<T>(IReadOnlyList<T?> shapes)
            {
                int longest = 0, run = 0;

                for (int i = 0; i < shapes.Count; i++)
                {
                    run = shapes[i] != null && i > 0 && EqualityComparer<T?>.Default.Equals(shapes[i], shapes[i - 1]) ? run + 1 : shapes[i] == null ? 0 : 1;
                    longest = Math.Max(longest, run);
                }

                return longest;
            }

            IReadOnlyList<JumpPatterns?> aim(int limit, int seed)
            {
                var settings = new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Jumps, Patterns = JumpPatterns.Squares | JumpPatterns.Triangles, MaxSameInARow = limit };
                var profile = new DifficultyProfile(settings);
                var points = new SectionPlanner(source, settings, profile, seed).Plan(RhythmExtractor.Extract(source, settings.RhythmSource));
                var generator = new PatternGenerator(source.ControlPointInfo, profile, settings, seed);
                generator.Generate(points, default);
                return generator.PlacedShapes;
            }

            IReadOnlyList<MetronomeShapes> stars(int limit, int seed)
            {
                var settings = new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Metronome, MetronomeShapes = MetronomeShapes.Star | MetronomeShapes.Square, MaxSameInARow = limit };
                var generator = new MetronomeGenerator(source, new DifficultyProfile(settings), settings, seed);
                generator.Generate();
                return generator.PlacedShapes;
            }

            foreach (int seed in new[] { 1, 2, 3 })
            {
                var free = aim(0, seed);
                TestContext.WriteLine($"aim, seed {seed}: no limit {longestRun(free)} in a row ({free.Count(s => s != null)} shapes), limit 1: {longestRun(aim(1, seed))}, limit 2: {longestRun(aim(2, seed))}");

                Assert.That(longestRun(aim(1, seed)), Is.LessThanOrEqualTo(1));
                Assert.That(longestRun(aim(2, seed)), Is.LessThanOrEqualTo(2));

                Assert.That(longestRun(stars(1, seed).Cast<MetronomeShapes?>().ToList()), Is.LessThanOrEqualTo(1), "stars as always");
                Assert.That(longestRun(stars(3, seed).Cast<MetronomeShapes?>().ToList()), Is.LessThanOrEqualTo(3));
            }

            Assert.That(new[] { 1, 2, 3 }.Max(seed => longestRun(aim(0, seed))), Is.GreaterThan(2), "without a limit, the same shape does come several times");
            Assert.That(new[] { 1, 2, 3 }.Max(seed => longestRun(stars(3, seed).Cast<MetronomeShapes?>().ToList())), Is.GreaterThan(1), "stars may now repeat a figure");
        }

        /// <summary>
        /// AR and OD go up to 11 (osu!lazer's extended limits), and Hard Rock doesn't bring them back down to 10.
        /// </summary>
        [Test]
        public void TestApproachRateAndOverallDifficultyUpTo11()
        {
            string path = realBeatmaps().First(p => p.EndsWith("1124896.osu", StringComparison.Ordinal));
            var settings = new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Metronome, ApproachRate = 11, OverallDifficulty = 11, Seed = 1 };

            var map = generate(path, settings, out var source);
            var circle = (HitCircle)map.HitObjects.First();

            Assert.That(map.Difficulty.ApproachRate, Is.EqualTo(11));
            Assert.That(circle.TimePreempt, Is.EqualTo(300).Within(1), "AR 11 shows a note 300 ms ahead");
            Assert.That(circle.HitWindows.WindowFor(HitResult.Great), Is.LessThan(15), "OD 11: a 300 within ±14 ms");

            FreeplayGenerationSettings.Current = settings;
            var withHardRock = new TestWorkingBeatmap(source).GetPlayableBeatmap(new FreeplayRuleset().RulesetInfo, new Mod[] { new OsuModHardRock() });

            Assert.That(withHardRock.Difficulty.ApproachRate, Is.EqualTo(11));
            Assert.That(withHardRock.Difficulty.OverallDifficulty, Is.EqualTo(11));
        }

        /// <summary>
        /// Practising a quarter puts about the asked share of the notes there, with the same star rating.
        /// </summary>
        [TestCase(WeakZone.TopLeft, 0.4)]
        [TestCase(WeakZone.BottomRight, 0.4)]
        [TestCase(WeakZone.TopRight, 0.5)]
        public void TestMetronomeWeakZone(WeakZone zone, double share)
        {
            string path = realBeatmaps().First(p => p.EndsWith("1124896.osu", StringComparison.Ordinal));
            var quarter = (Configuration.PlayfieldQuarter)(zone - WeakZone.TopLeft);

            double shareIn(Beatmaps.FreeplayBeatmap map) =>
                map.HitObjects.OfType<HitCircle>().Count(h => Configuration.AimAnalysis.QuarterOf(h.Position) == quarter) / (double)map.HitObjects.Count;

            var plain = (Beatmaps.FreeplayBeatmap)generate(path, new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Metronome, Seed = 3 }, out _, calibrate: true);
            var practised = (Beatmaps.FreeplayBeatmap)generate(path, new FreeplayGenerationSettings
            {
                StarRating = 5, Style = PatternStyle.Metronome, Seed = 3, WeakZone = zone, WeakZoneShare = share,
            }, out _, calibrate: true);

            TestContext.WriteLine($"{zone}: {shareIn(plain):P1} → {shareIn(practised):P1} (asked {share:P0}), {plain.StarRating:0.00}* → {practised.StarRating:0.00}*");

            Assert.That(shareIn(practised), Is.EqualTo(share).Within(0.08));
            Assert.That(practised.StarRating, Is.EqualTo(plain.StarRating).Within(0.4), "the same difficulty");
        }

        /// <summary>
        /// The aim analysis tells apart short and long of a note, a misread, a timing miss and no click, and where hits landed.
        /// </summary>
        [Test]
        public void TestAimAnalysis()
        {
            var difficulty = new BeatmapDifficulty { CircleSize = 4, OverallDifficulty = 8, ApproachRate = 9 };
            var circles = new List<HitCircle>();

            // Jumps to the right, one a second, along the middle line.
            for (int i = 0; i < 7; i++)
            {
                var circle = new HitCircle { StartTime = 1000 + i * 1000, Position = new Vector2(60 + i * 60, 192) };
                circle.ApplyDefaults(new ControlPointInfo(), difficulty);
                circles.Add(circle);
            }

            float r = (float)circles[0].Radius;
            var frames = new List<ReplayFrame>();
            var events = new List<HitEvent>();

            void press(double time, Vector2 position)
            {
                frames.Add(new OsuReplayFrame(time, position, Osu.OsuAction.LeftButton));
                frames.Add(new OsuReplayFrame(time + 30, position));
            }

            // 0: hit in the centre; 1: hit on the edge.
            press(1000, circles[0].Position);
            events.Add(new HitEvent(0, 1, HitResult.Great, circles[0], null, circles[0].Position));
            press(2000, circles[1].Position + new Vector2(0, r * 0.8f));
            events.Add(new HitEvent(0, 1, HitResult.Ok, circles[1], circles[0], circles[1].Position + new Vector2(0, r * 0.8f)));
            // 2: short of it; 3: past it; 4: far away (misread); 5: in the circle, but late; 6: no click.
            press(3000, circles[2].Position - new Vector2(r * 1.5f, 0));
            press(4000, circles[3].Position + new Vector2(r * 1.5f, 0));
            press(5000, circles[4].Position + new Vector2(0, r * 5));
            press(6000 + circles[5].HitWindows.WindowFor(HitResult.Meh) * 1.5, circles[5].Position);

            for (int i = 2; i < 7; i++)
                events.Add(new HitEvent(0, 1, HitResult.Miss, circles[i], circles[i - 1], null));

            var analysis = Configuration.AimAnalysis.Analyse(circles, events, frames);

            Assert.That(analysis.Notes, Is.EqualTo(7));
            Assert.That(analysis.CentreHits, Is.EqualTo(1));
            Assert.That(analysis.EdgeHits, Is.EqualTo(1));
            Assert.That(analysis.AimMisses, Is.EqualTo(3));
            Assert.That(analysis.Undershoot, Is.EqualTo(1));
            Assert.That(analysis.Overshoot, Is.EqualTo(1));
            Assert.That(analysis.Misreads, Is.EqualTo(1));
            Assert.That(analysis.TimingMisses, Is.EqualTo(1));
            Assert.That(analysis.NoClickMisses, Is.EqualTo(1));
            Assert.That(analysis.DirectionMisses[(int)Configuration.JumpDirection.Right], Is.EqualTo(3));
            Assert.That(analysis.QuarterMisses.Sum(), Is.EqualTo(3));
        }

        /// <summary>
        /// The stars mode's settings: only the chosen shape (a star comes back every five jumps), and every half beat when asked, however hard.
        /// </summary>
        [Test]
        public void TestMetronomeSettings()
        {
            string path = realBeatmaps().First(p => p.EndsWith("1124896.osu", StringComparison.Ordinal));

            var generated = generate(path, new FreeplayGenerationSettings
            {
                StarRating = 3.5,
                Style = PatternStyle.Metronome,
                MetronomeShapes = MetronomeShapes.Star,
                RotateShapes = false,
                ShapeMeasures = 8,
                AlwaysHalfBeats = true,
                Seed = 2,
            }, out _, calibrate: true);

            var objects = generated.HitObjects.OfType<HitCircle>().ToList();
            double beat = generated.ControlPointInfo.TimingPointAt(objects[0].StartTime).BeatLength;
            var gaps = objects.Zip(objects.Skip(1), (a, b) => (b.StartTime - a.StartTime) / beat).ToList();

            Assert.That(gaps.Count(g => Math.Abs(g - 0.5) < 0.02), Is.GreaterThan(gaps.Count * 0.97), "every half beat");

            // Within a shape (eight measures here), a jump lands where the one five before did; only the first few after a change don't.
            int checkedJumps = 0, same = 0;

            for (int i = 5; i < objects.Count; i++)
            {
                if (objects[i].StartTime - objects[i - 5].StartTime > beat * 2.6)
                    continue;

                checkedJumps++;
                if (Vector2.Distance(objects[i].Position, objects[i - 5].Position) < 1)
                    same++;
            }

            Assert.That(checkedJumps, Is.GreaterThan(50));
            Assert.That(same, Is.GreaterThan(checkedJumps * 0.8), "only stars, not rotating");
        }

        [Test]
        public void TestIqChangesAngles()
        {
            string path = sourceBeatmaps().First(p => p.EndsWith("2593923.osu", StringComparison.Ordinal));

            // Over a few seeds, as one map's share of turns is noisy.
            double rightAngles(double iq) => new[] { 8, 9, 10 }.Average(seed => rightAnglesWith(iq, seed));

            double rightAnglesWith(double iq, int seed)
            {
                var objects = generate(path, new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Jumps, AllowStreams = false, PatternIq = iq, Seed = seed }, out _)
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

        /// <summary>
        /// The stars mode's mapper patterns come from its own collection when there is one, laid on the grid in whole rounds.
        /// </summary>
        [Test]
        public void TestMetronomeTakesMapperPatternsFromItsCollection()
        {
            var patterns = new MinedPatternSet();

            using (var stream = new LineBufferedReader(File.OpenRead(sourceBeatmaps().First(p => p.EndsWith("801165.osu", StringComparison.Ordinal)))))
                patterns.Add(PatternCategory.Stars, new TestWorkingBeatmap(Decoder.GetDecoder<Beatmap>(stream).Decode(stream)).GetPlayableBeatmap(new OsuRuleset().RulesetInfo));

            Assert.That(patterns.Jumps.ContainsKey(PatternCategory.Aim), Is.False, "only the stars collection");

            MinedPatternSet.Current = patterns;

            try
            {
                var generated = generate(sourceBeatmaps().First(p => p.EndsWith("1124896.osu", StringComparison.Ordinal)), new FreeplayGenerationSettings
                {
                    StarRating = 5,
                    Style = PatternStyle.Metronome,
                    MetronomeShapes = MetronomeShapes.MapperPatterns,
                    Seed = 4,
                }, out _, calibrate: true);

                var objects = generated.HitObjects.OfType<HitCircle>().ToList();
                double beat = generated.ControlPointInfo.TimingPointAt(objects[0].StartTime).BeatLength;
                var gaps = objects.Zip(objects.Skip(1), (a, b) => (b.StartTime - a.StartTime) / beat).ToList();

                Assert.That(gaps.Count(g => Math.Abs(g - 0.5) < 0.02 || Math.Abs(g - 1) < 0.02), Is.GreaterThan(gaps.Count * 0.97), "on the grid");

                // Rounds of a mapper's pattern are four to eight jumps; no one or two left over between them.
                var combos = objects.Select((h, i) => (h, i)).Where(x => x.h.NewCombo).Select(x => x.i).ToList();
                var rounds = combos.Zip(combos.Skip(1), (a, b) => b - a).ToList();
                Assert.That(rounds.Count(r => r < 4), Is.LessThan(rounds.Count * 0.05), "whole rounds");

                // (Eight-jump rounds only come from mapper patterns: no built-in shape has eight points.)
                Assert.That(rounds, Has.Some.EqualTo(8), "mapper patterns used");
            }
            finally
            {
                MinedPatternSet.Current = null;
            }
        }

        /// <summary>
        /// Made-up shapes in the stars mode: real jumps on the grid, and a different shape each time.
        /// </summary>
        [Test]
        public void TestMetronomeRandomShapesDiffer()
        {
            var generated = generate(sourceBeatmaps().First(p => p.EndsWith("1124896.osu", StringComparison.Ordinal)), new FreeplayGenerationSettings
            {
                StarRating = 5,
                Style = PatternStyle.Metronome,
                MetronomeShapes = MetronomeShapes.RandomShape,
                RotateShapes = false,
                Seed = 6,
            }, out _, calibrate: true);

            var objects = generated.HitObjects.OfType<HitCircle>().ToList();
            double beat = generated.ControlPointInfo.TimingPointAt(objects[0].StartTime).BeatLength;

            var jumps = objects.Zip(objects.Skip(1), (a, b) => (gap: (b.StartTime - a.StartTime) / beat, distance: Vector2.Distance(a.Position, b.Position))).ToList();
            Assert.That(jumps.Count(j => j.gap < 1.05 && j.distance < 40), Is.LessThan(jumps.Count * 0.03), "no jumps too short to be jumps");

            // Combo lengths are the shapes' sizes (three to seven points, doubled for triangles): with made-up shapes, several sizes come up.
            var combos = objects.Select((h, i) => (h, i)).Where(x => x.h.NewCombo).Select(x => x.i).ToList();
            var sizes = combos.Zip(combos.Skip(1), (a, b) => b - a).Distinct().ToList();
            Assert.That(sizes.Count, Is.GreaterThanOrEqualTo(3), "different shapes");
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

            // From 3★ the generator may go up to a third over the limit rather than leave the music silent for a beat (see PatternGenerator.planRhythm).
            double maxInWindow = profile.MaxObjectsPerSecond * DifficultyProfile.DENSITY_WINDOW / 1000 * (profile.Stars >= 3 ? 1.3 : 1);

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
