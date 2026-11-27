// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NUnit.Framework;
using osu.Framework.Extensions;
using osu.Game.Rulesets.Freeplay.Beatmaps;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    /// <summary>
    /// Every pattern of <see cref="PatternLibrary"/>, so a new one is checked as soon as it's added: on the playfield, real jumps, the stars asked for.
    /// </summary>
    [TestFixture]
    public class PatternLibraryTest
    {
        private static string source => Path.Combine(FreeplayGenerationTest.RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "Resources", "Beatmaps", "801165.osu");

        private static IEnumerable<MetronomeShapes> figures => PatternLibrary.Figures.Select(f => f.Kind);

        private static IEnumerable<JumpPatterns> jumpShapes => PatternLibrary.Jumps.Select(j => j.Flag);

        [Test]
        public void TestEveryChoiceHasAPattern()
        {
            var shapes = Enum.GetValues<MetronomeShapes>().Where(s => s != MetronomeShapes.None && s != MetronomeShapes.All).ToList();
            var jumps = Enum.GetValues<JumpPatterns>().Where(s => s != JumpPatterns.None && s != JumpPatterns.All).ToList();

            Assert.That(figures, Is.EquivalentTo(shapes), "a figure for every checkbox, once");
            Assert.That(jumpShapes, Is.EquivalentTo(jumps), "a jump shape for every checkbox, once");
            Assert.That(shapes.Aggregate(MetronomeShapes.None, (a, b) => a | b), Is.EqualTo(MetronomeShapes.All), "All has them all");
            Assert.That(jumps.Aggregate(JumpPatterns.None, (a, b) => a | b), Is.EqualTo(JumpPatterns.All), "All has them all");
            Assert.That(shapes.Cast<Enum>().Concat(jumps.Cast<Enum>()).All(s => !string.IsNullOrWhiteSpace(s.GetDescription()) && s.GetDescription() != s.ToString()), "every one has a name");
        }

        [TestCaseSource(nameof(figures))]
        public void TestStarsFigure(MetronomeShapes kind)
        {
            var map = (FreeplayBeatmap)FreeplayGenerationTest.generate(source,
                new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Metronome, MetronomeShapes = kind, Seed = 6 }, out _, calibrate: true);
            var objects = map.HitObjects.OfType<HitCircle>().ToList();
            var jumps = objects.Zip(objects.Skip(1), (a, b) => Vector2.Distance(a.Position, b.Position)).ToList();

            TestContext.WriteLine($"{kind}: {objects.Count} notes, {map.StarRating:0.00}*, shortest {jumps.Min():0} px, average {jumps.Average():0} px");

            Assert.That(objects.Count, Is.GreaterThan(100));
            onPlayfield(objects);
            Assert.That(map.StarRating, Is.EqualTo(5).Within(0.7));
            Assert.That(jumps.Count(j => j < 25), Is.LessThan(jumps.Count * 0.03), "real jumps");
        }

        [TestCaseSource(nameof(jumpShapes))]
        public void TestJumpShape(JumpPatterns flag)
        {
            var shape = PatternLibrary.JumpOf(flag)!;

            // The shape itself: about the spacing asked between notes (a stack's notes are on one spot, a cross's sides shorter than its diagonals).
            var offsets = shape.Create(16, 100, 0.3f, 0.2f, 6, new Random(1));
            var steps = offsets.Zip(offsets.Skip(1), (a, b) => Vector2.Distance(a, b)).Where(d => d > 1).ToList();

            Assert.That(offsets, Has.Count.EqualTo(16));
            Assert.That(steps.Average(), Is.InRange(60, 140), "jumps about as long as asked");
            Assert.That(shape.Cycle(6), Is.GreaterThan(1));
            Assert.That(shape.Iq, Is.InRange(0, 1));

            // And in a map, on its own.
            var map = FreeplayGenerationTest.generate(source, new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Jumps, Patterns = flag, MinedPatternShare = 0, Seed = 4 }, out _);
            onPlayfield(map.HitObjects.OfType<OsuHitObject>());
        }

        [Test]
        public void TestStreamShapes()
        {
            foreach (var shape in PatternLibrary.Streams)
            {
                Assert.That(shape.Name, Is.Not.Empty);

                for (int i = 0; i < 40; i++)
                {
                    float turn = shape.Turn(i, 16, 0.2f);
                    Assert.That(float.IsFinite(turn) && Math.Abs(turn) <= MathF.PI, $"{shape.Name} turns {turn} at {i}");
                }
            }

            var map = FreeplayGenerationTest.generate(source, new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Deathstreams, Seed = 2 }, out _);
            onPlayfield(map.HitObjects.OfType<OsuHitObject>());
        }

        [Test]
        public void TestFigureAsJumpShape()
        {
            // Any stars figure can be a jump shape: a crossed square goes round as one, its jumps the spacing on average, its second pass turned.
            var shape = JumpShape.FromFigure(JumpPatterns.Crosses, PatternLibrary.FigureOf(MetronomeShapes.CrossedSquare), 0.8, 1);
            var offsets = shape.Create(8, 120, 0, 0.5f, 0, new Random(1));
            var pass = offsets.Take(4).ToList();

            Assert.That(shape.Cycle(0), Is.EqualTo(4));
            Assert.That(Enumerable.Range(0, 4).Average(i => Vector2.Distance(pass[i], pass[(i + 1) % 4])), Is.EqualTo(120).Within(0.5));
            Assert.That(Vector2.Distance(offsets[4], offsets[0]), Is.GreaterThan(20), "the second pass turned");
            Assert.That(offsets[4].Length, Is.EqualTo(offsets[0].Length).Within(0.01), "the same size");
        }

        [Test]
        public void TestNewPatternsComeOnForSavedChoices()
        {
            var serializer = new JsonSerializerSettings { Converters = { new StringEnumConverter() }, ObjectCreationHandling = ObjectCreationHandling.Replace };

            // Saved before settings knew which there were: the choice stays as it was, and the figures added since (from fosu) come on.
            var old = JsonConvert.DeserializeObject<FreeplayGenerationSettings>("{\"Patterns\":\"Squares, Stars\",\"MetronomeShapes\":\"Star\"}", serializer)!;
            Assert.That(old.Patterns, Is.EqualTo(JumpPatterns.Squares | JumpPatterns.Stars | JumpPatterns.Bowties | JumpPatterns.Weaves | JumpPatterns.Hourglasses
                                                 | JumpPatterns.FrameStars | JumpPatterns.SharpZigzags));
            Assert.That(old.MetronomeShapes, Is.EqualTo(MetronomeShapes.Star | MetronomeShapes.Bowtie | MetronomeShapes.Weave | MetronomeShapes.Hourglass
                                                        | MetronomeShapes.FrameStar | MetronomeShapes.SharpZigzag));

            // Saved when there were fewer: the ones the player never saw come on, the ones they turned off stay off.
            var fewer = JsonConvert.DeserializeObject<FreeplayGenerationSettings>(
                "{\"Patterns\":\"Squares\",\"KnownPatterns\":\"Squares, Triangles\",\"MetronomeShapes\":\"Star\",\"KnownMetronomeShapes\":\"Star, Square\"}", serializer)!;
            Assert.That(fewer.Patterns, Is.EqualTo(JumpPatterns.All & ~JumpPatterns.Triangles));
            Assert.That(fewer.MetronomeShapes, Is.EqualTo(MetronomeShapes.All & ~MetronomeShapes.Square));

            // Saved and loaded again: as chosen.
            var chosen = new FreeplayGenerationSettings { Patterns = JumpPatterns.Crosses, MetronomeShapes = MetronomeShapes.Hexagon };
            var copy = Configuration.ModePreset.Copy(chosen);
            Assert.That((copy.Patterns, copy.MetronomeShapes), Is.EqualTo((JumpPatterns.Crosses, MetronomeShapes.Hexagon)));
        }

        [Test]
        public void TestLibraryShapesKeepToAim()
        {
            // The shapes the styles don't list (the fosu ones): in aim at their weight, none in flow, unless they're all that was chosen.
            FreeplayGenerationTest.generate(source, new FreeplayGenerationSettings { StarRating = 5 }, out var original);
            var added = PatternLibrary.Jumps.Where(j => j.Weight > 0).Select(j => j.Flag).ToHashSet();

            List<JumpPatterns> placed(PatternStyle style, JumpPatterns patterns)
            {
                var settings = new FreeplayGenerationSettings { StarRating = 5, Style = style, Patterns = patterns, MinedPatternShare = 0, AllowStreams = false };
                var profile = new DifficultyProfile(settings);
                var result = new List<JumpPatterns>();

                foreach (int seed in new[] { 1, 2, 3 })
                {
                    var points = new SectionPlanner(original, settings, profile, seed).Plan(RhythmExtractor.Extract(original, settings.RhythmSource));
                    var generator = new PatternGenerator(original.ControlPointInfo, profile, settings, seed);
                    generator.Generate(points, default);
                    result.AddRange(generator.PlacedShapes.OfType<JumpPatterns>());
                }

                return result;
            }

            var aim = placed(PatternStyle.Jumps, JumpPatterns.All);
            var flow = placed(PatternStyle.Flow, JumpPatterns.All);
            var onlyBowties = placed(PatternStyle.Flow, JumpPatterns.Bowties);

            TestContext.WriteLine($"aim {aim.Count(added.Contains)}/{aim.Count}, flow {flow.Count(added.Contains)}/{flow.Count}, flow with bowties only {onlyBowties.Count(s => s == JumpPatterns.Bowties)}/{onlyBowties.Count}");

            Assert.That(aim.Count(added.Contains), Is.GreaterThan(0));
            Assert.That(flow, Is.Not.Empty);
            Assert.That(flow.Count(added.Contains), Is.EqualTo(0), "flow keeps its own shapes");
            Assert.That(onlyBowties, Has.Some.EqualTo(JumpPatterns.Bowties), "chosen alone, they come anyway");
        }

        [Test]
        public void TestLeftoverStarsToDropped()
        {
            var serializer = new JsonSerializerSettings { Converters = { new StringEnumConverter() }, ObjectCreationHandling = ObjectCreationHandling.Replace };

            // Saved when "to" only counted with ranges on: ranges off, it's dropped; ranges on, it stays.
            var off = JsonConvert.DeserializeObject<FreeplayGenerationSettings>("{\"UseRanges\":false,\"StarRating\":7.1,\"StarRatingMax\":6.6}", serializer)!;
            var on = JsonConvert.DeserializeObject<FreeplayGenerationSettings>("{\"UseRanges\":true,\"StarRating\":5,\"StarRatingMax\":6}", serializer)!;
            Assert.That(off.StarRatingMax, Is.Null);
            Assert.That(on.StarRatingMax, Is.EqualTo(6));

            // Saved since: as chosen.
            var chosen = new FreeplayGenerationSettings { UseRanges = false, StarRating = 5, StarRatingMax = 6 };
            Assert.That(Configuration.ModePreset.Copy(chosen).StarRatingMax, Is.EqualTo(6));
        }

        [TestCase(4.0)]
        [TestCase(5.5)]
        [TestCase(6.5)]
        public void TestSquares(double stars)
        {
            // The square mode: every jump along 0, 45 or 90 degrees (a few between figures may miss, where none fits), on the playfield,
            // real jumps, the stars asked for; squares, diamonds and ladders most of the time, the rest now and then.
            var kinds = new List<SquarePatterns>();
            int jumps = 0, straight = 0, stacked = 0;

            foreach (int seed in new[] { 1, 2, 3 })
            {
                var map = (FreeplayBeatmap)FreeplayGenerationTest.generate(source,
                    new FreeplayGenerationSettings { StarRating = stars, Style = PatternStyle.Metronome, Squares = true, Seed = seed }, out var original, calibrate: true);
                var objects = map.HitObjects.OfType<HitCircle>().ToList();

                Assert.That(map.HitObjects, Has.All.InstanceOf<HitCircle>(), "circles only");
                onPlayfield(objects);

                // The stars asked for, or as near as the stars mode gets on this song (its slowest BPM caps how hard it can be).
                var starsMode = (FreeplayBeatmap)FreeplayGenerationTest.generate(source,
                    new FreeplayGenerationSettings { StarRating = stars, Style = PatternStyle.Metronome, Seed = seed }, out _, calibrate: true);
                TestContext.WriteLine($"{stars}* seed {seed}: squares {map.StarRating:0.00}*, stars mode {starsMode.StarRating:0.00}*");
                Assert.That(map.StarRating, Is.EqualTo(Math.Min(stars, starsMode.StarRating)).Within(0.5), $"seed {seed}");

                for (int i = 1; i < objects.Count; i++)
                {
                    var d = objects[i].Position - objects[i - 1].Position;

                    if (d.Length < 25)
                    {
                        TestContext.WriteLine($"STACK at {objects[i].StartTime} ({objects[i].StartTime - objects[i - 1].StartTime:0} ms): {objects[i - 1].Position} -> {objects[i].Position}, nc {objects[i].NewCombo}");
                        stacked++;
                        continue;
                    }

                    jumps++;
                    double angle = Math.Atan2(d.Y, d.X) * 180 / Math.PI;
                    double off = Math.Abs(angle / 45 - Math.Round(angle / 45)) * 45;

                    if (off < 2)
                        straight++;
                }

                var settings = new FreeplayGenerationSettings { StarRating = stars, Style = PatternStyle.Metronome, Squares = true, Seed = seed };
                var generator = new MetronomeGenerator(original, new DifficultyProfile(settings), settings, seed);
                generator.Generate();
                kinds.AddRange(generator.PlacedSquares);
            }

            TestContext.WriteLine($"{stars}*: {straight}/{jumps} jumps at 45/90 degrees; {string.Join(", ", kinds.GroupBy(k => k).Select(g => $"{g.Key} {g.Count()}"))}");

            Assert.That(straight, Is.GreaterThan(jumps * 0.95));
            Assert.That(stacked, Is.Zero, "real jumps: no note on (or nearly on) the last");
            int main = kinds.Count(k => k is SquarePatterns.Square or SquarePatterns.Diamond or SquarePatterns.Ladder);
            Assert.That(main, Is.GreaterThan(kinds.Count * 0.75), "mostly squares, diamonds and ladders");
            Assert.That(kinds.Distinct().Count(), Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void TestSquarePatternsTurnOff()
        {
            FreeplayGenerationTest.generate(source, new FreeplayGenerationSettings { StarRating = 5 }, out var original);
            var settings = new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Metronome, Squares = true, SquarePatterns = SquarePatterns.Diamond | SquarePatterns.Grid, Seed = 4 };
            var generator = new MetronomeGenerator(original, new DifficultyProfile(settings), settings, 4);
            generator.Generate();

            Assert.That(generator.PlacedSquares, Has.All.Matches<SquarePatterns>(k => k is SquarePatterns.Diamond or SquarePatterns.Grid));
            Assert.That(generator.PlacedSquares, Has.Some.EqualTo(SquarePatterns.Grid), "a rare one chosen with one other comes");
        }

        [Test]
        public void TestApproachRateRange()
        {
            // Ticked: each track its own AR within the range (either way round), on its own without "Разброс настроек"; unticked, the AR set.
            var ranged = new FreeplayGenerationSettings { ApproachRate = 10, ApproachRateMax = 8.5f, ApproachRateRange = true };
            var rng = new Random(1);
            var picked = Enumerable.Range(0, 200).Select(_ => ranged.Roll(rng).ApproachRate!.Value).ToList();

            Assert.That(picked, Has.All.InRange(8.5f - 0.001f, 10 + 0.001f));
            Assert.That(picked.Distinct().Count(), Is.GreaterThan(8), "different on different tracks");
            Assert.That(picked.Min(), Is.LessThan(8.8f));
            Assert.That(picked.Max(), Is.GreaterThan(9.7f));

            var fixedAr = new FreeplayGenerationSettings { ApproachRate = 9, ApproachRateMax = 10, ApproachRateRange = false };
            Assert.That(fixedAr.Roll(rng).ApproachRate, Is.EqualTo(9));

            var fromStars = new FreeplayGenerationSettings { ApproachRateMax = 10, ApproachRateRange = true };
            Assert.That(fromStars.Roll(rng).ApproachRate, Is.Null, "no own AR: from the stars");

            // And the map has it.
            var map = FreeplayGenerationTest.generate(source, new FreeplayGenerationSettings { StarRating = 5, ApproachRate = 7.3f, Seed = 2 }, out _);
            Assert.That(map.Difficulty.ApproachRate, Is.EqualTo(7.3f).Within(0.01));
        }

        private static void onPlayfield(IEnumerable<OsuHitObject> objects)
        {
            foreach (var o in objects)
            {
                Assert.That(o.Position.X, Is.InRange(0, 512), $"at {o.StartTime}");
                Assert.That(o.Position.Y, Is.InRange(0, 384), $"at {o.StartTime}");
            }
        }
    }
}
