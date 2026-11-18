// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Generation
{
    public enum PatternCategory
    {
        Aim,
        Streams,
        Tech,
    }

    /// <summary>
    /// A run of consecutive circles taken from a real beatmap, as positions relative to its first circle.
    /// </summary>
    public class MinedPattern
    {
        public required List<Vector2> Offsets { get; init; }

        /// <summary>
        /// The gaps between consecutive circles, in beats.
        /// </summary>
        public required List<double> GapBeats { get; init; }

        /// <summary>
        /// The average distance between consecutive circles, used to rescale the pattern to a target spacing.
        /// </summary>
        public float AverageSpacing => Offsets.Count < 2 ? 0 : Offsets.Zip(Offsets.Skip(1), (a, b) => Vector2.Distance(a, b)).Average();
    }

    /// <summary>
    /// Patterns taken from the beatmaps of the collections chosen as examples for each category.
    /// </summary>
    public class MinedPatternSet
    {
        /// <summary>
        /// The patterns used by the next generated beatmap, or null to only use built-in shapes.
        /// </summary>
        public static MinedPatternSet? Current { get; set; }

        /// <summary>
        /// Jump patterns (circle runs within a mapper's combo), by category.
        /// </summary>
        public readonly Dictionary<PatternCategory, List<MinedPattern>> Jumps = new Dictionary<PatternCategory, List<MinedPattern>>();

        /// <summary>
        /// Streams (runs of 1/4 circles), from any category.
        /// </summary>
        public readonly List<MinedPattern> Streams = new List<MinedPattern>();

        public int Count => Jumps.Values.Sum(j => j.Count) + Streams.Count;

        private const int min_jump_pattern = 3;
        private const int max_jump_pattern = 16;
        private const int min_stream = 6;

        /// <summary>
        /// Splits a beatmap's objects into jump patterns (circle runs inside each combo) and streams.
        /// </summary>
        public void Add(PatternCategory category, IBeatmap beatmap)
        {
            if (!Jumps.TryGetValue(category, out var jumps))
                Jumps[category] = jumps = new List<MinedPattern>();

            var run = new List<OsuHitObject>();
            var objects = beatmap.HitObjects.OfType<OsuHitObject>().ToList();

            for (int i = 0; i <= objects.Count; i++)
            {
                var current = i < objects.Count ? objects[i] : null;
                var previous = run.LastOrDefault();

                bool continues = current is HitCircle && previous != null && !current.NewCombo
                                 && current.StartTime - previous.StartTime <= beatLength(beatmap, current.StartTime) * 1.5;

                if (!continues)
                {
                    flush(beatmap, run, jumps);
                    run.Clear();
                }

                if (current is HitCircle)
                    run.Add(current);
            }

            addStreams(beatmap, objects);
        }

        private void flush(IBeatmap beatmap, List<OsuHitObject> run, List<MinedPattern> jumps)
        {
            if (run.Count < min_jump_pattern)
                return;

            var pattern = create(beatmap, run.Take(max_jump_pattern).ToList());

            // Streams are collected separately; jump patterns are the ones with actual movement.
            if (pattern.GapBeats.All(g => g < 0.3) || pattern.AverageSpacing < 20)
                return;

            jumps.Add(pattern);
        }

        private void addStreams(IBeatmap beatmap, List<OsuHitObject> objects)
        {
            var run = new List<OsuHitObject>();

            foreach (var obj in objects.Append(null))
            {
                var previous = run.LastOrDefault();

                if (obj is HitCircle && previous != null && (obj.StartTime - previous.StartTime) / beatLength(beatmap, obj.StartTime) < 0.3)
                    run.Add(obj);
                else
                {
                    if (run.Count >= min_stream)
                        Streams.Add(create(beatmap, run));

                    run.Clear();

                    if (obj is HitCircle)
                        run.Add(obj);
                }
            }
        }

        private static MinedPattern create(IBeatmap beatmap, List<OsuHitObject> run)
        {
            var origin = run[0].Position;

            return new MinedPattern
            {
                Offsets = run.Select(h => h.Position - origin).ToList(),
                GapBeats = run.Zip(run.Skip(1), (a, b) => (b.StartTime - a.StartTime) / beatLength(beatmap, b.StartTime)).ToList(),
            };
        }

        private static double beatLength(IBeatmap beatmap, double time) => Math.Max(1, beatmap.ControlPointInfo.TimingPointAt(time).BeatLength);

        /// <summary>
        /// Picks a jump pattern with at least <paramref name="count"/> circles, preferring ones whose rhythm matches <paramref name="gapBeats"/>.
        /// </summary>
        public MinedPattern? PickJumps(PatternCategory category, int count, IReadOnlyList<double> gapBeats, Random rng)
        {
            if (!Jumps.TryGetValue(category, out var patterns) || patterns.Count == 0)
            {
                // Borrow from another category rather than giving up entirely.
                patterns = Jumps.Values.SelectMany(p => p).ToList();
                if (patterns.Count == 0)
                    return null;
            }

            var longEnough = patterns.Where(p => p.Offsets.Count >= count).ToList();

            if (longEnough.Count == 0)
                return null;

            // Rhythm matching on 1/4 beat steps keeps patterns that were mapped to similar rhythms, where possible.
            var matching = longEnough.Where(p => rhythmMatches(p, gapBeats)).ToList();
            var pool = matching.Count >= 3 ? matching : longEnough;

            return pool[rng.Next(pool.Count)];
        }

        public MinedPattern? PickStream(int minLength, Random rng)
        {
            if (Streams.Count == 0)
                return null;

            var longEnough = Streams.Where(s => s.Offsets.Count >= minLength).ToList();
            var pool = longEnough.Count > 0 ? longEnough : Streams;

            return pool[rng.Next(pool.Count)];
        }

        private static bool rhythmMatches(MinedPattern pattern, IReadOnlyList<double> gapBeats)
        {
            for (int i = 0; i < gapBeats.Count && i < pattern.GapBeats.Count; i++)
            {
                if (Math.Abs(Math.Round(pattern.GapBeats[i] * 4) - Math.Round(gapBeats[i] * 4)) > 0)
                    return false;
            }

            return true;
        }
    }
}
