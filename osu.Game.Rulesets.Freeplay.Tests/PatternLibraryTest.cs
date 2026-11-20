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

            // Saved before settings knew which there were: the choice stays as it was.
            var old = JsonConvert.DeserializeObject<FreeplayGenerationSettings>("{\"Patterns\":\"Squares, Stars\",\"MetronomeShapes\":\"Star\"}", serializer)!;
            Assert.That(old.Patterns, Is.EqualTo(JumpPatterns.Squares | JumpPatterns.Stars));
            Assert.That(old.MetronomeShapes, Is.EqualTo(MetronomeShapes.Star));

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
