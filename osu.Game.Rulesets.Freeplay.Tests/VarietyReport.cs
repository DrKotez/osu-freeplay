// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    /// <summary>
    /// Measures how varied generated aim maps are, so "more varied" is a number rather than a feeling.
    /// </summary>
    [TestFixture]
    public class VarietyReport
    {
        private static readonly string[] maps = { "100374.osu", "1124896.osu", "1341554.osu", "1450162.osu", "2593923.osu", "801165.osu" };

        public record Variety(double SpacingSpread, double AngleEntropy, double SameMeasures, double RhythmEntropy, double Stars);

        [Explicit]
        [TestCase(AimGoal.Angles)]
        [TestCase(AimGoal.Farm)]
        [TestCase(AimGoal.Wide)]
        public void Report(AimGoal goal)
        {
            TestContext.Out.WriteLine($"goal {goal}");
            TestContext.Out.WriteLine("map          | spacing spread | angle entropy | same measures | rhythm entropy | stars");

            var rows = new List<Variety>();

            foreach (string map in maps)
            {
                var v = Measure(map, goal, seed: 3);
                rows.Add(v);
                TestContext.Out.WriteLine($"{map,-12} | {v.SpacingSpread,14:0.00} | {v.AngleEntropy,13:0.00} | {v.SameMeasures,13:P0} | {v.RhythmEntropy,14:0.00} | {v.Stars:0.00}");
            }

            TestContext.Out.WriteLine($"{"average",-12} | {rows.Average(r => r.SpacingSpread),14:0.00} | {rows.Average(r => r.AngleEntropy),13:0.00} | {rows.Average(r => r.SameMeasures),13:P0} | {rows.Average(r => r.RhythmEntropy),14:0.00} | {rows.Average(r => r.Stars):0.00}");
        }

        /// <summary>
        /// <list type="bullet">
        /// <item>Spacing spread: how much 1/2 jump distances vary (coefficient of variation).</item>
        /// <item>Angle entropy: how evenly turn angles spread over 30° buckets (0 = always the same turn, 1 = all turns equally common).</item>
        /// <item>Same measures: how often a measure looks like the one before it (same spacing band and same most common turn).</item>
        /// <item>Rhythm entropy: how varied the gaps between objects are (0 = one gap throughout).</item>
        /// </list>
        /// </summary>
        public static Variety Measure(string map, AimGoal goal, int seed)
        {
            string path = Path.Combine(FreeplayGenerationTest.RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "Resources", "Beatmaps", map);

            var settings = new FreeplayGenerationSettings { Style = PatternStyle.Jumps, Seed = seed };
            AimGoals.Apply(settings, goal, 5.5);
            settings.UseRanges = false;
            settings.AllowSpinners = false;
            settings.StarRating = 5.5;
            settings.Palette = TrackPalette.Roll(new Random(seed));

            return MeasureGenerated(FreeplayGenerationTest.generate(path, settings, out _, calibrate: true));
        }

        /// <param name="wholeBeats">Also count jumps a whole beat apart (the stars mode, which goes to beats where half beats are too hard).</param>
        public static Variety MeasureGenerated(osu.Game.Beatmaps.IBeatmap beatmap, bool wholeBeats = false)
        {
            var objects = beatmap.HitObjects.OfType<OsuHitObject>().Where(h => h is not Spinner).ToList();

            var distances = new List<double>();
            var turns = new List<int>();
            var gaps = new List<int>();
            var measures = new Dictionary<int, (List<double> d, List<int> t)>();

            for (int i = 1; i < objects.Count; i++)
            {
                var a = objects[i - 1];
                var b = objects[i];
                double beat = beatmap.ControlPointInfo.TimingPointAt(b.StartTime).BeatLength;
                double gap = (b.StartTime - a.GetEndTime()) / beat;

                gaps.Add((int)Math.Round((b.StartTime - a.StartTime) / beat * 4));

                if (a is not HitCircle || b is not HitCircle || gap < 0.4 || gap > (wholeBeats ? 1.1 : 0.6))
                    continue;

                double distance = Vector2.Distance(a.StackedPosition, b.StackedPosition);
                distances.Add(distance);

                int measure = (int)(b.StartTime / (beat * 4));

                if (!measures.TryGetValue(measure, out var m))
                    measures[measure] = m = (new List<double>(), new List<int>());

                m.d.Add(distance);

                if (i >= 2 && objects[i - 2] is HitCircle c && distance > 20 && Vector2.Distance(c.StackedPosition, a.StackedPosition) > 20)
                {
                    Vector2 first = a.StackedPosition - c.StackedPosition, second = b.StackedPosition - a.StackedPosition;
                    double turn = Math.Abs(Math.IEEERemainder(Math.Atan2(second.Y, second.X) - Math.Atan2(first.Y, first.X), Math.Tau));
                    int bucket = Math.Min(5, (int)(turn / (Math.PI / 6)));
                    turns.Add(bucket);
                    m.t.Add(bucket);
                }
            }

            double mean = distances.Average();
            double spread = Math.Sqrt(distances.Average(d => (d - mean) * (d - mean))) / mean;

            var ordered = measures.OrderBy(m => m.Key).Select(m => m.Value).Where(m => m.d.Count >= 3 && m.t.Count >= 2).ToList();
            int same = 0;

            for (int i = 1; i < ordered.Count; i++)
            {
                bool sameSpacing = Math.Abs(ordered[i].d.Average() - ordered[i - 1].d.Average()) < 25;
                bool sameTurn = mostCommon(ordered[i].t) == mostCommon(ordered[i - 1].t);

                if (sameSpacing && sameTurn)
                    same++;
            }

            return new Variety(spread, entropy(turns, 6), ordered.Count < 2 ? 0 : same / (double)(ordered.Count - 1), entropy(gaps.Select(g => Math.Min(g, 8)).ToList(), 9),
                (beatmap as Beatmaps.FreeplayBeatmap)?.StarRating ?? 0);
        }

        private static int mostCommon(List<int> values) => values.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;

        private static double entropy(List<int> values, int buckets)
        {
            if (values.Count == 0)
                return 0;

            double h = values.GroupBy(v => v).Select(g => g.Count() / (double)values.Count).Sum(p => -p * Math.Log(p));
            return h / Math.Log(buckets);
        }
    }
}
