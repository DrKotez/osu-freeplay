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

                // A shape for a measure or a few (two most often), not past a change of grid.
                var timingPoint = original.ControlPointInfo.TimingPointAt(startTime);
                int notesPerMeasure = Math.Max(1, (int)Math.Round(timingPoint.BeatLength * timingPoint.TimeSignature.Numerator / step));
                int measures = settings.ShapeMeasures > 0 ? settings.ShapeMeasures : rng.NextDouble() switch
                {
                    < 0.3 => 1,
                    < 0.75 => 2,
                    _ => 4,
                };

                var shape = pickShape(previousShape);
                previousShape = shape;
                minedLength = rng.Next(4, 9);

                // Whole rounds only: a shape cut off a jump or two into its next round plays like stray jumps before the next one.
                // (It carries on across the music's rests, rather than starting a new shape after each.)
                int roundLength = shape.Kind == MetronomeShapes.MapperPatterns ? minedLength : shape.Order.Length;
                int length = Math.Max(1, (int)Math.Round(notesPerMeasure * measures / (double)roundLength)) * roundLength;

                int end = index;
                while (end < times.Count && end - index < length && times[end].step == step)
                    end++;

                // Each shape bigger or smaller than the last (around the size the stars ask for), wider in the chorus.
                float size = (float)(0.7 + rng.NextDouble() * 0.6);
                float distance = jumpDistance(step) * size * (original.ControlPointInfo.EffectPointAt(startTime + 1).KiaiMode ? 1.08f : 1);
                // (A mapper's pattern that can't be found falls back to a star.)
                if (shape.Kind != MetronomeShapes.MapperPatterns || !placeMined(result, times, index, end, distance, ref last))
                    place(result, times, index, end, shape.Kind == MetronomeShapes.MapperPatterns ? shapes[0] : shape, distance, ref last);

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
            // A little smaller than in aim, but not cramped: below this the beats with bigger shapes play better than tiny half-beat jumps.
            double floor = profile.MinJumpDistance * settings.JumpSpacing * (profile.FlowJumps ? 0.6 : 1);

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
            // The radii that give the wanted jump between neighbours. Shapes may stretch sideways to use the playfield's width,
            // so big jumps still fit (a circle would stop at its height).
            float maxX = OsuPlayfield.BASE_SIZE.X / 2 - margin - 4, maxY = OsuPlayfield.BASE_SIZE.Y / 2 - margin - 4;
            float wanted = distance / shape.Edge;
            float radiusY = Math.Min(maxY, wanted);
            float radiusX = Math.Min(maxX, wanted * wanted / Math.Max(1, radiusY));

            // A narrower shape is sometimes kept so: an upright or squashed star is a different pattern to play.
            if (radiusX > radiusY && rng.NextDouble() < 0.3)
                radiusX = radiusY;

            float rotation = (float)(rng.NextDouble() * Math.Tau);
            int way = rng.NextDouble() < 0.5 ? 1 : -1;

            // Half the shapes slowly turn from one round to the next, like a rotating star.
            float drift = !settings.RotateShapes || rng.NextDouble() < 0.5 ? 0 : (float)(rng.NextDouble() * 0.15 + 0.08) * (rng.NextDouble() < 0.5 ? -1 : 1);

            // How the shape changes from round to round, so holding it isn't one loop over and over.
            var twist = settings.RotateShapes ? (Twist)rng.Next(Enum.GetValues<Twist>().Length) : Twist.None;

            Vector2 centre = pickCentre(shape, radiusX, radiusY, rotation, way, last, distance);
            Vector2 travel = twist == Twist.Travel ? new Vector2(MathF.Cos(rotation * 3), MathF.Sin(rotation * 3)) * distance * 0.25f : Vector2.Zero;

            for (int i = start; i < end; i++)
            {
                int n = i - start;
                int round = n / shape.Order.Length;

                int turn = twist == Twist.Reverse && round % 2 == 1 ? -way : way;
                float scale = twist == Twist.Pulse ? (round % 2 == 0 ? 1.12f : 0.8f) : 1;

                float angle = rotation + round * drift + turn * shape.Order[n % shape.Order.Length] * MathF.Tau / shape.Points;

                // A travelling shape walks across the playfield, turning back at the edges.
                if (twist == Twist.Travel && n % shape.Order.Length == 0 && n > 0)
                {
                    var moved = centre + travel;

                    if (moved.X - radiusX < margin || moved.X + radiusX > OsuPlayfield.BASE_SIZE.X - margin)
                        travel.X = -travel.X;
                    if (moved.Y - radiusY < margin || moved.Y + radiusY > OsuPlayfield.BASE_SIZE.Y - margin)
                        travel.Y = -travel.Y;

                    centre += travel;
                }

                var position = clamp(centre + new Vector2(MathF.Cos(angle) * radiusX, MathF.Sin(angle) * radiusY) * scale);

                result.Add(new HitCircle
                {
                    StartTime = times[i].time,
                    Position = position,
                    // A combo per round (two for back and forth, whose rounds are a jump each way).
                    NewCombo = n % (shape.Order.Length < 4 ? shape.Order.Length * 2 : shape.Order.Length) == 0,
                    Samples = RhythmExtractor.PlainSampleNear(original, times[i].time),
                });

                last = position;
            }
        }

        /// <summary>
        /// How many jumps a round of a mapper's pattern has (see <see cref="placeMined"/>).
        /// </summary>
        private int minedLength = 6;

        private readonly Queue<MinedPattern> recentMined = new Queue<MinedPattern>();

        /// <summary>
        /// A real jump pattern from the aim collection (the kind aim maps are made of: wide angles, zigzags across the screen), laid on the grid:
        /// sized to the wanted jump, turned and mirrored, and played round after round, mirrored every other round so it goes back and forth.
        /// </summary>
        /// <returns>Whether a pattern could be placed.</returns>
        private bool placeMined(List<OsuHitObject> result, List<(double time, double step)> times, int start, int end, float distance, ref Vector2? last)
        {
            var pattern = MinedPatternSet.Current?.PickJumps(PatternCategory.Aim, minedLength, Enumerable.Repeat(0.5, minedLength - 1).ToList(), rng, 0.5, null, recentMined);

            if (pattern == null || pattern.Offsets.Count < minedLength)
                return false;

            var offsets = pattern.Offsets.Take(minedLength).Select(o => o - pattern.Offsets[0]).ToList();
            float spacing = offsets.Zip(offsets.Skip(1), (a, b) => Vector2.Distance(a, b)).Average();

            if (spacing < 1)
                return false;

            float angle = (float)(rng.NextDouble() * Math.Tau);
            bool mirror = rng.NextDouble() < 0.5;
            float cos = MathF.Cos(angle), sin = MathF.Sin(angle);

            offsets = offsets.Select(o => new Vector2(mirror ? -o.X : o.X, o.Y) * (distance / spacing))
                             .Select(o => new Vector2(o.X * cos - o.Y * sin, o.X * sin + o.Y * cos)).ToList();

            // Shrunk if it doesn't fit, then placed so it fits with its first jump about the right size.
            Vector2 min = new Vector2(offsets.Min(o => o.X), offsets.Min(o => o.Y)), max = new Vector2(offsets.Max(o => o.X), offsets.Max(o => o.Y));
            Vector2 room = OsuPlayfield.BASE_SIZE - new Vector2(margin * 2);
            float fit = Math.Min(1, Math.Min(room.X / Math.Max(1, max.X - min.X), room.Y / Math.Max(1, max.Y - min.Y)));
            offsets = offsets.Select(o => o * fit).ToList();
            min *= fit;
            max *= fit;

            Vector2 random() => new Vector2(margin - min.X + (float)rng.NextDouble() * Math.Max(0, room.X - (max.X - min.X)),
                margin - min.Y + (float)rng.NextDouble() * Math.Max(0, room.Y - (max.Y - min.Y)));

            Vector2 origin = last is Vector2 from
                ? Enumerable.Range(0, 24).Select(_ => random()).MinBy(c => Math.Abs(Vector2.Distance(c, from) - distance))
                : random();

            float middle = (min.X + max.X) / 2;

            for (int i = start; i < end; i++)
            {
                int n = i - start;
                int round = n / minedLength;
                var offset = offsets[n % minedLength];

                // Every other round mirrored, so the pattern comes back the other way instead of jumping back to its start.
                if (round % 2 == 1)
                    offset = new Vector2(2 * middle - offset.X, offset.Y);

                var position = clamp(origin + offset);

                result.Add(new HitCircle
                {
                    StartTime = times[i].time,
                    Position = position,
                    NewCombo = n % minedLength == 0,
                    Samples = RhythmExtractor.PlainSampleNear(original, times[i].time),
                });

                last = position;
            }

            return true;
        }

        /// <summary>
        /// How a held shape changes between rounds.
        /// </summary>
        private enum Twist
        {
            /// <summary>The same round each time (apart from rotation).</summary>
            None,

            /// <summary>Every other round goes the other way round.</summary>
            Reverse,

            /// <summary>Rounds alternate between bigger and smaller.</summary>
            Pulse,

            /// <summary>The whole shape moves a little each round.</summary>
            Travel,
        }

        /// <summary>
        /// Somewhere the whole shape fits, with its first point about a jump away from the last note.
        /// </summary>
        private Vector2 pickCentre(Shape shape, float radiusX, float radiusY, float rotation, int way, Vector2? last, float distance)
        {
            float minX = margin + radiusX, maxX = OsuPlayfield.BASE_SIZE.X - margin - radiusX;
            float minY = margin + radiusY, maxY = OsuPlayfield.BASE_SIZE.Y - margin - radiusY;

            Vector2 random() => new Vector2(minX > maxX ? OsuPlayfield.BASE_SIZE.X / 2 : minX + (float)rng.NextDouble() * (maxX - minX),
                minY > maxY ? OsuPlayfield.BASE_SIZE.Y / 2 : minY + (float)rng.NextDouble() * (maxY - minY));

            if (last == null)
                return random();

            float firstAngle = rotation + way * shape.Order[0] * MathF.Tau / shape.Points;
            Vector2 firstOffset = new Vector2(MathF.Cos(firstAngle) * radiusX, MathF.Sin(firstAngle) * radiusY);

            return Enumerable.Range(0, 24).Select(_ => random()).MinBy(c => Math.Abs(Vector2.Distance(c + firstOffset, last.Value) - distance));
        }

        private Vector2 clamp(Vector2 position) => Vector2.Clamp(position, new Vector2(margin), OsuPlayfield.BASE_SIZE - new Vector2(margin));

        private Shape pickShape(Shape? previous)
        {
            // The player's choice of shapes; harder ones wait for higher ratings, unless they're all that was chosen.
            bool mined = MinedPatternSet.Current?.Jumps.TryGetValue(PatternCategory.Aim, out var aim) == true && aim.Count > 0;
            var chosen = shapes.Where(s => settings.MetronomeShapes.HasFlag(s.Kind) && (s.Kind != MetronomeShapes.MapperPatterns || mined)).ToList();
            if (chosen.Count == 0)
                chosen = shapes.Where(s => s.Kind != MetronomeShapes.MapperPatterns || mined).ToList();

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
            public float Edge { get; } = Order.Length == 0 ? 1 : (float)Enumerable.Range(0, Order.Length).Average(i =>
                2 * Math.Sin(Math.Abs(Order[(i + 1) % Order.Length] - Order[i]) * Math.PI / Points));
        }

        private static readonly Shape[] shapes =
        {
            new Shape(MetronomeShapes.Star, 5, new[] { 0, 2, 4, 1, 3 }, 3),
            new Shape(MetronomeShapes.BackAndForth, 2, new[] { 0, 1 }, 0.5),
            new Shape(MetronomeShapes.Triangle, 3, new[] { 0, 1, 2 }, 1.2),
            new Shape(MetronomeShapes.Square, 4, new[] { 0, 1, 2, 3 }, 1.2),
            new Shape(MetronomeShapes.CrossedSquare, 4, new[] { 0, 2, 1, 3 }, 1),
            new Shape(MetronomeShapes.Pentagon, 5, new[] { 0, 1, 2, 3, 4 }, 0.4),
            new Shape(MetronomeShapes.Hexagon, 6, new[] { 0, 1, 2, 3, 4, 5 }, 0.2),
            new Shape(MetronomeShapes.SevenPointStar, 7, new[] { 0, 3, 6, 2, 5, 1, 4 }, 0.8, MinStars: 5),
            new Shape(MetronomeShapes.StarOfDavid, 6, new[] { 0, 2, 4, 1, 3, 5 }, 0.3, MinStars: 4.5),
            // Real jump patterns from the aim collection, laid on the grid (see placeMined).
            new Shape(MetronomeShapes.MapperPatterns, 0, Array.Empty<int>(), 3),
        };
    }
}
