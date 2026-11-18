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
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    [TestFixture]
    public class FreeplayGenerationTest
    {
        private static readonly string[] source_directories =
        {
            "osu.Game.Rulesets.Osu.Tests/Resources/Testing/Beatmaps",
            "osu.Game.Rulesets.Mania.Tests/Resources/Testing/Beatmaps",
        };

        private static IEnumerable<string> sourceBeatmaps()
        {
            string root = findRepositoryRoot();
            return source_directories.SelectMany(d => Directory.GetFiles(Path.Combine(root, d), "*.osu")).Order();
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

                    assertPlayable(beatmap, new DifficultyProfile(settings), $"{Path.GetFileName(path)} {stars}* {style}");
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

        private static void assertPlayable(IBeatmap beatmap, DifficultyProfile profile, string context)
        {
            var objects = beatmap.HitObjects.Cast<OsuHitObject>().ToList();

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

        private static IBeatmap generate(string path, FreeplayGenerationSettings settings, out IBeatmap source)
        {
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
