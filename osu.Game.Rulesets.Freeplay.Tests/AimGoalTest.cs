// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    [TestFixture]
    public class AimGoalTest
    {
        private static FreeplayMode aim => FreeplayMode.Find("aim")!;

        [Test]
        public void TestAimStartsFromAGoal()
        {
            var settings = new FreeplaySettings { PlayerLevel = 6 };
            var s = settings.GetMode(aim);

            Assert.That(s.Goal, Is.EqualTo(AimGoal.Farm));
            Assert.That(s.GoalEdited, Is.False);
            Assert.That(s.StarRating, Is.EqualTo(5.7).Within(1e-9));
            Assert.That(s.StarRatingMax, Is.EqualTo(6.2).Within(1e-9));
            Assert.That(s.UseRanges, Is.True);
        }

        [TestCaseSource(typeof(AimGoals), nameof(AimGoals.ALL))]
        public void TestGoalsKeepThePlayersOwnSettings(AimGoal goal)
        {
            var s = new FreeplayGenerationSettings { MinBpm = 170, MaxBpm = 230, ApproachRate = 9.3f, CircleSize = 4, TrackCollection = "farm", Seed = 3, FailLevel = FailLevel.Hard };

            AimGoals.Apply(s, goal, 5.5);

            Assert.That(s.Goal, Is.EqualTo(goal));
            Assert.That((s.MinBpm, s.MaxBpm, s.ApproachRate, s.CircleSize, s.TrackCollection, s.Seed, s.FailLevel),
                Is.EqualTo((170.0, 230.0, (float?)9.3f, (float?)4f, "farm", (int?)3, FailLevel.Hard)));
            Assert.That(s.StarRatingMax, Is.GreaterThanOrEqualTo(s.StarRating));
            Assert.That(s.StarRatingMax, Is.LessThanOrEqualTo(5.5 + 0.3 + 1e-9));
        }

        [Test]
        public void TestLevelMovesStars()
        {
            var s = new FreeplayGenerationSettings();
            AimGoals.Apply(s, AimGoal.Endurance, 5);
            AimGoals.ApplyStars(s, 6.5);

            Assert.That(s.StarRating, Is.EqualTo(5.7).Within(1e-9));
            Assert.That(s.StarRatingMax, Is.EqualTo(6.2).Within(1e-9));
        }

        [Test]
        public void TestWarmUpClimbsToLevel()
        {
            var s = new FreeplayGenerationSettings();
            AimGoals.Apply(s, AimGoal.WarmUp, 6);
            var rng = new Random(1);

            var stars = Enumerable.Range(0, 8).Select(i => AimGoals.ForTrack(s, i, 6, rng).Roll(rng).StarRating).ToList();

            Assert.That(stars[0], Is.EqualTo(4.5).Within(1e-9));
            Assert.That(stars.Take(AimGoals.WARM_UP_TRACKS + 1), Is.Ordered);
            Assert.That(stars.Skip(AimGoals.WARM_UP_TRACKS).All(v => v >= 5.7 - 1e-9 && v <= 6 + 1e-9));
        }

        [Test]
        public void TestSurprisePicksOtherGoals()
        {
            var s = new FreeplayGenerationSettings();
            AimGoals.Apply(s, AimGoal.Surprise, 5.5);
            var rng = new Random(2);

            var picks = Enumerable.Range(0, 40).Select(i => AimGoals.ForTrack(s, i, 5.5, rng)).ToList();

            Assert.That(picks.All(p => p.SurpriseOf is AimGoal g && g != AimGoal.Surprise && g != AimGoal.WarmUp));
            Assert.That(picks.Select(p => p.SurpriseOf).Distinct().Count(), Is.GreaterThan(2));
            Assert.That(s.SurpriseOf, Is.Null, "the saved settings stay a surprise");
        }

        [Test]
        public void TestEditedGoalStopsChangingTracks()
        {
            var s = new FreeplayGenerationSettings();
            AimGoals.Apply(s, AimGoal.WarmUp, 6);
            s.GoalEdited = true;

            Assert.That(AimGoals.ForTrack(s, 0, 6, new Random(1)), Is.SameAs(s));
            Assert.That(AimGoals.Name(s), Does.StartWith("Своя"));
        }

        [Test]
        public void TestFailLevels()
        {
            var custom = new FailSettings { Health = false, MissLimit = 1, TimeLimitMinutes = 30 };

            Assert.That(AimGoals.FailFor(FailLevel.Off, custom).Health, Is.False);
            Assert.That(AimGoals.FailFor(FailLevel.Normal, custom).Health, Is.True);
            Assert.That(AimGoals.FailFor(FailLevel.Hard, custom).MinAccuracy, Is.GreaterThan(0));
            Assert.That(AimGoals.FailFor(FailLevel.Hard, custom).HealthRefill, Is.LessThan(AimGoals.FailFor(FailLevel.Normal, custom).HealthRefill));
            Assert.That(AimGoals.FailFor(FailLevel.Custom, custom), Is.SameAs(custom));
            Assert.That(AimGoals.FailFor(FailLevel.Normal, custom).MissLimit, Is.Zero, "presets don't take the custom conditions");
        }
    }
}
