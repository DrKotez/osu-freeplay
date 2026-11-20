// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.Freeplay.Beatmaps;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    /// <summary>
    /// Difficulty as the pp an SS gives (<see cref="FreeplayGenerationSettings.UsePerformance"/>), within the stars from and to.
    /// </summary>
    [TestFixture]
    public class PerformanceTargetTest
    {
        [SetUp]
        public void SetUp() => PerformanceTarget.Reset();

        private static string source =>Path.Combine(FreeplayGenerationTest.RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "Resources", "Beatmaps", "801165.osu");

        private static FreeplayBeatmap generate(string path, double pp, double low = 1, double high = 10)
            => (FreeplayBeatmap)FreeplayGenerationTest.generate(path, new FreeplayGenerationSettings
            {
                Style = PatternStyle.Jumps,
                AllowStreams = false,
                UsePerformance = true,
                TargetPerformance = pp,
                StarRating = low,
                StarRatingMax = high,
                Seed = 3,
            }, out _, calibrate: true);

        [Test]
        public void TestMapGivesThePpAsked()
        {
            var map = generate(source, 200);
            double recalculated = FreeplayBeatmapConverter.CalculatePerformance(map, map.HitObjects.ToList(), map.Difficulty, Array.Empty<osu.Game.Rulesets.Mods.Mod>());

            Assert.That(map.Performance, Is.EqualTo(200).Within(200 * 0.12), $"{map.StarRating:0.00}*");
            Assert.That(recalculated, Is.EqualTo(map.Performance).Within(1));
        }

        [Test]
        public void TestStarsStayWithinTheLimits()
        {
            var map = generate(source, 1000, 3, 4);

            Assert.That(map.StarRating, Is.LessThan(4.4));
            Assert.That(map.Performance, Is.LessThan(1000));
        }

        [Test]
        public void TestShortTrackIsHarderThanLongOne()
        {
            // The same song, cut to its first 40 seconds.
            string[] lines = File.ReadAllLines(source);
            int objectsAt = Array.IndexOf(lines, "[HitObjects]");
            double first = double.Parse(lines[objectsAt + 1].Split(',')[2], System.Globalization.CultureInfo.InvariantCulture);
            var kept = lines.Take(objectsAt + 1).Concat(lines.Skip(objectsAt + 1).Where(l => l.Split(',').Length > 2
                                                                                           && double.Parse(l.Split(',')[2], System.Globalization.CultureInfo.InvariantCulture) < first + 40000));
            string shortPath = Path.Combine(Path.GetTempPath(), $"freeplay-short-{Guid.NewGuid():N}.osu");
            File.WriteAllLines(shortPath, kept);

            try
            {
                var full = generate(source, 250);
                var cut = generate(shortPath, 250);

                Assert.That(cut.HitObjects.Last().StartTime, Is.LessThan(full.HitObjects.Last().StartTime - 30000), "the short copy is shorter");
                Assert.That(cut.StarRating, Is.GreaterThan(full.StarRating + 0.1), $"short {cut.StarRating:0.00}* ({cut.Performance:0}pp), long {full.StarRating:0.00}* ({full.Performance:0}pp)");
            }
            finally
            {
                File.Delete(shortPath);
            }
        }

        [Test]
        public void TestEstimateGrowsWithStarsAndShrinksWithLength()
        {
            Assert.That(PerformanceTarget.StarsFor(300, 30, PatternStyle.Metronome), Is.GreaterThan(PerformanceTarget.StarsFor(300, 180, PatternStyle.Metronome) + 0.2));
            Assert.That(PerformanceTarget.Estimate(6, 120, PatternStyle.Jumps), Is.GreaterThan(PerformanceTarget.Estimate(5, 120, PatternStyle.Jumps)));
        }

        [Test]
        public void TestStarRangeRolledWithoutOtherRanges()
        {
            var settings = new FreeplayGenerationSettings { StarRating = 4, StarRatingMax = 6, UseRanges = false };
            var rolled = Enumerable.Range(0, 50).Select(i => settings.Roll(new Random(i)).StarRating).ToList();

            Assert.That(rolled.Min(), Is.GreaterThanOrEqualTo(4));
            Assert.That(rolled.Max(), Is.LessThanOrEqualTo(6));
            Assert.That(rolled.Distinct().Count(), Is.GreaterThan(3));

            // A pp target keeps them as limits.
            settings.UsePerformance = true;
            var limits = settings.Roll(new Random(1));
            Assert.That((limits.StarRating, limits.StarRatingMax), Is.EqualTo((4.0, (double?)6.0)));
        }

        [Test]
        public void TestPpRangeRolledPerTrack()
        {
            var settings = new FreeplayGenerationSettings { UsePerformance = true, TargetPerformance = 300, TargetPerformanceMax = 200, StarRating = 4, StarRatingMax = 6 };
            var rolled = Enumerable.Range(0, 50).Select(i => settings.Roll(new Random(i))).ToList();

            Assert.That(rolled.All(r => r.TargetPerformance >= 200 && r.TargetPerformance <= 300), "a range given upside down still works");
            Assert.That(rolled.Select(r => r.TargetPerformance).Distinct().Count(), Is.GreaterThan(5));
            Assert.That(rolled.All(r => r.TargetPerformance % 5 == 0));
            Assert.That(rolled.All(r => r.StarRating == 4 && r.StarRatingMax == 6), "the stars stay the limits");

            settings.TargetPerformanceMax = null;
            Assert.That(settings.Roll(new Random(1)).TargetPerformance, Is.EqualTo(300));
        }
    }
}
