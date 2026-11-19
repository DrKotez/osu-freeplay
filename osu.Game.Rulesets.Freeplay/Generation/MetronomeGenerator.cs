// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Generation
{
    /// <summary>
    /// <see cref="PatternStyle.Metronome"/>: a jump on every half beat (or beat) from the first note to the last, the way
    /// "stars" practice maps jump around one star to a metronome. Here the shape changes every few measures: stars, polygons,
    /// squares, crossed squares, back and forth, each held long enough to settle into, and sized for the star rating.
    /// </summary>
    public class MetronomeGenerator
    {
        private readonly IBeatmap original;
        private readonly DifficultyProfile profile;
        private readonly FreeplayGenerationSettings settings;
        private readonly Random rng;
        private readonly float margin;

        private int jumpCount, flooredJumps;

        /// <summary>
        /// How many jumps were raised to the smallest allowed jump (see <see cref="PatternGenerator.FlooredJumpShare"/>).
        /// </summary>
        public double FlooredJumpShare => jumpCount == 0 ? 0 : (double)flooredJumps / jumpCount;

        public MetronomeGenerator(IBeatmap original, DifficultyProfile profile, FreeplayGenerationSettings settings, int seed)
        {
            this.original = original;
            this.profile = profile;
            this.settings = settings;
            rng = new Random(seed);

            margin = OsuHitObject.OBJECT_RADIUS * LegacyRulesetExtensions.CalculateScaleFromCircleSize(profile.CircleSize, true);
        }

        public List<OsuHitObject> Generate()
        {
            var result = new List<OsuHitObject>();
            var times = jumpTimes();

            if (times.Count == 0)
                return result;

            Vector2? last = null;
            Shape? previousShape = null;
            int index = 0;

            while (index < times.Count)
            {
                var (startTime, step) = times[index];

                // A shape for a few measures (four most often), not past a change of grid.
                var timingPoint = original.ControlPointInfo.TimingPointAt(startTime);
                int notesPerMeasure = Math.Max(1, (int)Math.Round(timingPoint.BeatLength * timingPoint.TimeSignature.Numerator / step));
                int measures = settings.ShapeMeasures > 0 ? settings.ShapeMeasures : rng.NextDouble() switch
                {
                    < 0.2 => 2,
                    < 0.8 => 4,
                    _ => 8,
                };

                int end = index;
                while (end < times.Count && end - index < notesPerMeasure * measures && times[end].step == step && (end == index || times[end].time - times[end - 1].time <= step * 1.5))
                    end++;

                var shape = pickShape(previousShape);
                previousShape = shape;

                // Each shape a little bigger or smaller than the last (around the size the stars ask for), wider in the chorus.
                float size = (float)(0.85 + rng.NextDouble() * 0.3);
                float distance = jumpDistance(step) * size * (original.ControlPointInfo.EffectPointAt(startTime + 1).KiaiMode ? 1.08f : 1);
                place(result, times, index, end, shape, distance, ref last);

                index = end;
            }

            return result;
        }

        /// <summary>
        /// Every half beat (every beat where the stars don't allow halves, see <see cref="FreeplayGenerationSettings.FlowLevel"/>) while the music plays:
        /// from the first note to the last, leaving out breaks and stretches of four beats or more where the mapper has nothing at all.
        /// </summary>
        private List<(double time, double step)> jumpTimes()
        {
            var objects = original.HitObjects.OrderBy(h => h.StartTime).ToList();
            var result = new List<(double, double)>();

            if (objects.Count == 0)
                return result;

            var starts = objects.Select(h => h.StartTime).ToArray();
            double first = starts[0], last = objects.Max(h => h.GetEndTime());

            var timingPoint = original.ControlPointInfo.TimingPointAt(first);
            double beat = timingPoint.BeatLength;

            if (beat <= 0)
                return result;

            for (double time = timingPoint.Time + Math.Ceiling((first - timingPoint.Time - 2) / (beat / 2)) * (beat / 2); time <= last + 2; time += beat / 2)
            {
                bool kiai = original.ControlPointInfo.EffectPointAt(time + 1).KiaiMode;
                bool half = settings.AlwaysHalfBeats
                            || (profile.MaxObjectsPerSecondIn(kiai, beat) >= 2000 / beat && (settings.FlowLevel == 0 || (settings.FlowLevel == 1 && kiai)));
                bool onBeat = Math.Abs((time - timingPoint.Time) / beat - Math.Round((time - timingPoint.Time) / beat)) < 0.1;

                if (!half && !onBeat)
                    continue;

                if (original.Breaks.Any(b => time > b.StartTime && time < b.EndTime) || nearestDistance(starts, objects, time) > beat * 4)
                    continue;

                result.Add((time, half ? beat / 2 : beat));
            }

            return result;
        }

        private static double nearestDistance(double[] starts, List<HitObject> objects, double time)
        {
            int i = Array.BinarySearch(starts, time);
            if (i < 0) i = ~i;

            double best = double.MaxValue;

            for (int k = Math.Max(0, i - 2); k < Math.Min(starts.Length, i + 2); k++)
            {
                // Inside a held object counts as the music playing.
                if (time >= objects[k].StartTime && time <= objects[k].GetEndTime())
                    return 0;

                best = Math.Min(best, Math.Abs(starts[k] - time));
            }

            return best;
        }

        private float jumpDistance(double gap)
        {
            double distance = Math.Min(profile.MaxJumpDistance, profile.AimVelocity * gap) * settings.JumpSpacing * settings.CalibrationSpacing;
            // Jumping on and on hits harder per jump, as in aim (see PatternGenerator.jumpDistance).
            // Smaller still than in aim: keeping the half beats matters more here than big jumps.
            double floor = profile.MinJumpDistance * settings.JumpSpacing * (profile.FlowJumps ? 0.4 : 1);

            jumpCount++;

            if (distance < floor)
            {
                distance = floor;
                flooredJumps++;
            }

            return (float)Math.Clamp(distance, 40, 420);
        }

        private void place(List<OsuHitObject> result, List<(double time, double step)> times, int start, int end, Shape shape, float distance, ref Vector2? last)
        {
            // The radius that gives the wanted jump between neighbours, as large as fits.
            float maxRadius = Math.Min(OsuPlayfield.BASE_SIZE.X, OsuPlayfield.BASE_SIZE.Y) / 2 - margin - 4;
            float radius = Math.Min(maxRadius, distance / shape.Edge);

            float rotation = (float)(rng.NextDouble() * Math.Tau);
            int way = rng.NextDouble() < 0.5 ? 1 : -1;

            // Half the shapes slowly turn from one round to the next, like a rotating star.
            float drift = !settings.RotateShapes || rng.NextDouble() < 0.5 ? 0 : (float)(rng.NextDouble() * 0.15 + 0.08) * (rng.NextDouble() < 0.5 ? -1 : 1);

            Vector2 centre = pickCentre(shape, radius, rotation, way, last, distance);

            for (int i = start; i < end; i++)
            {
                int n = i - start;
                int round = n / shape.Order.Length;
                float angle = rotation + round * drift + way * shape.Order[n % shape.Order.Length] * MathF.Tau / shape.Points;

                var position = clamp(centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);

                result.Add(new HitCircle
                {
                    StartTime = times[i].time,
                    Position = position,
                    NewCombo = n % shape.Order.Length == 0,
                    Samples = RhythmExtractor.PlainSampleNear(original, times[i].time),
                });

                last = position;
            }
        }

        /// <summary>
        /// Somewhere the whole shape fits, with its first point about a jump away from the last note.
        /// </summary>
        private Vector2 pickCentre(Shape shape, float radius, float rotation, int way, Vector2? last, float distance)
        {
            float minX = margin + radius, maxX = OsuPlayfield.BASE_SIZE.X - margin - radius;
            float minY = margin + radius, maxY = OsuPlayfield.BASE_SIZE.Y - margin - radius;

            Vector2 random() => new Vector2(minX > maxX ? OsuPlayfield.BASE_SIZE.X / 2 : minX + (float)rng.NextDouble() * (maxX - minX),
                minY > maxY ? OsuPlayfield.BASE_SIZE.Y / 2 : minY + (float)rng.NextDouble() * (maxY - minY));

            if (last == null)
                return random();

            float firstAngle = rotation + way * shape.Order[0] * MathF.Tau / shape.Points;
            Vector2 firstOffset = new Vector2(MathF.Cos(firstAngle), MathF.Sin(firstAngle)) * radius;

            return Enumerable.Range(0, 24).Select(_ => random()).MinBy(c => Math.Abs(Vector2.Distance(c + firstOffset, last.Value) - distance));
        }

        private Vector2 clamp(Vector2 position) => Vector2.Clamp(position, new Vector2(margin), OsuPlayfield.BASE_SIZE - new Vector2(margin));

        private Shape pickShape(Shape? previous)
        {
            // The player's choice of shapes; harder ones wait for higher ratings, unless they're all that was chosen.
            var chosen = shapes.Where(s => settings.MetronomeShapes.HasFlag(s.Kind)).ToList();
            if (chosen.Count == 0)
                chosen = shapes.ToList();

            var available = chosen.Where(s => s != previous && profile.Stars >= s.MinStars).ToList();
            if (available.Count == 0)
                available = chosen.Where(s => s != previous).ToList();
            if (available.Count == 0)
                available = chosen;
            double roll = rng.NextDouble() * available.Sum(s => s.Weight);

            foreach (var shape in available)
            {
                roll -= shape.Weight;

                if (roll <= 0)
                    return shape;
            }

            return available[^1];
        }

        /// <summary>
        /// A shape: <see cref="Points"/> evenly around a circle, visited in <see cref="Order"/> (a star visits every other point).
        /// <see cref="Edge"/> is the jump between neighbours in the order, for a radius of one.
        /// </summary>
        private sealed record Shape(MetronomeShapes Kind, int Points, int[] Order, double Weight, double MinStars = 0)
        {
            public float Edge { get; } = (float)Enumerable.Range(0, Order.Length).Average(i =>
                2 * Math.Sin(Math.Abs(Order[(i + 1) % Order.Length] - Order[i]) * Math.PI / Points));
        }

        private static readonly Shape[] shapes =
        {
            new Shape(MetronomeShapes.Star, 5, new[] { 0, 2, 4, 1, 3 }, 3),
            new Shape(MetronomeShapes.BackAndForth, 2, new[] { 0, 1 }, 1),
            new Shape(MetronomeShapes.Triangle, 3, new[] { 0, 1, 2 }, 1.2),
            new Shape(MetronomeShapes.Square, 4, new[] { 0, 1, 2, 3 }, 1.2),
            new Shape(MetronomeShapes.CrossedSquare, 4, new[] { 0, 2, 1, 3 }, 1),
            new Shape(MetronomeShapes.Pentagon, 5, new[] { 0, 1, 2, 3, 4 }, 0.8),
            new Shape(MetronomeShapes.Hexagon, 6, new[] { 0, 1, 2, 3, 4, 5 }, 0.6),
            new Shape(MetronomeShapes.SevenPointStar, 7, new[] { 0, 3, 6, 2, 5, 1, 4 }, 0.8, MinStars: 5),
            new Shape(MetronomeShapes.StarOfDavid, 6, new[] { 0, 2, 4, 1, 3, 5 }, 0.6, MinStars: 4.5),
        };
    }
}
