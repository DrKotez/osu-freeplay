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
        /// Whether the shape being placed goes into <see cref="FreeplayGenerationSettings.WeakQuarter"/>, and how many notes are there so far.
        /// </summary>
        private bool towardZone;

        /// <summary>
        /// How many figures of the last one's kind came in a row.
        /// </summary>
        private int sameShapeRun;

        private readonly List<MetronomeShapes> placedShapes = new List<MetronomeShapes>();

        /// <summary>
        /// The figures placed, in order.
        /// </summary>
        public IReadOnlyList<MetronomeShapes> PlacedShapes => placedShapes;

        private readonly List<bool> placedInChorus = new List<bool>();

        /// <summary>
        /// For each of <see cref="PlacedShapes"/>, whether it was in a chorus (kiai).
        /// </summary>
        public IReadOnlyList<bool> PlacedInChorus => placedInChorus;

        private readonly List<string> placedTwists = new List<string>();

        /// <summary>
        /// How each placed figure changed from round to round (for tests).
        /// </summary>
        public IReadOnlyList<string> PlacedTwists => placedTwists;

        private int zoneNotes;

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
                inChorus = original.ControlPointInfo.EffectPointAt(startTime + 1).KiaiMode;

                // Choruses change figure more often; verses hold one longer.
                int measures = settings.ShapeMeasures > 0 ? settings.ShapeMeasures : rng.NextDouble() switch
                {
                    < 0.4 => 1,
                    < 0.85 => 2,
                    _ => 4,
                };

                if (settings.ShapeMeasures == 0 && inChorus && measures == 4)
                    measures = 2;

                // Practising a quarter: shapes go there until it has its share of the notes.
                towardZone = settings.WeakQuarter != null && zoneNotes < settings.WeakZoneShare * Math.Max(1, result.Count);

                // Each shape bigger or smaller than the last (around the size the stars ask for), wider in the chorus. Known before the shape is
                // picked: a jump too long for a shape's size on the playfield goes to one that can make it (see pickShape).
                // (Going into a quarter being practised, from the smaller half of the sizes, so more of the figure fits there.)
                float size = (float)(0.7 + rng.NextDouble() * (towardZone ? 0.3 : 0.6));
                float distance = jumpDistance(step) * size * (original.ControlPointInfo.EffectPointAt(startTime + 1).KiaiMode ? 1.08f : 1);
                wantedDistance = distance;

                var shape = pickShape(previousShape);
                sameShapeRun = shape.Kind == previousShape?.Kind ? sameShapeRun + 1 : 1;
                placedShapes.Add(shape.Kind);
                placedInChorus.Add(inChorus);
                previousShape = shape;
                minedLength = rng.Next(4, 9);

                // Whole rounds only: a shape cut off a jump or two into its next round plays like stray jumps before the next one.
                // (It carries on across the music's rests, rather than starting a new shape after each.)
                int roundLength = shape.Kind == MetronomeShapes.MapperPatterns ? minedLength : shape.Order.Length;
                int length = Math.Max(1, (int)Math.Round(notesPerMeasure * measures / (double)roundLength)) * roundLength;

                int end = index;
                while (end < times.Count && end - index < length && times[end].step == step)
                    end++;

                // (A mapper's pattern that can't be found falls back to a star.)
                if (shape.Kind != MetronomeShapes.MapperPatterns || !placeMined(result, times, index, end, distance, ref last))
                    place(result, times, index, end, shape.Kind == MetronomeShapes.MapperPatterns ? shapes[0] : shape, distance, ref last);

                zoneNotes = result.Count(h => settings.WeakQuarter == Configuration.AimAnalysis.QuarterOf(h.Position));
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
                            || (profile.MaxObjectsPerSecondIn(kiai, beat) >= 2000 / beat
                                && (settings.FlowLevel == 0 || (settings.FlowLevel == 1 && kiai) || (settings.FlowLevel == 2 && halfBlock(time, timingPoint))));
                bool onBeat = Math.Abs((time - timingPoint.Time) / beat - Math.Round((time - timingPoint.Time) / beat)) < 0.1;

                if (!half && !onBeat)
                    continue;

                if (original.Breaks.Any(b => time > b.StartTime && time < b.EndTime) || nearestDistance(starts, objects, time) > beat * 4)
                    continue;

                result.Add((time, half ? beat / 2 : beat));
            }

            return result;
        }

        /// <summary>
        /// Whether the two-measure block holding <paramref name="time"/> is one of the <see cref="FreeplayGenerationSettings.HalfShare"/> still
        /// jumped every half beat (FIX: between half beats, too hard even with short jumps, and whole beats, too easy even across the playfield,
        /// the stars asked could fall in a gap: a 4.5* map came out 3.8*). Choruses come first.
        /// </summary>
        private bool halfBlock(double time, osu.Game.Beatmaps.ControlPoints.TimingControlPoint timingPoint)
        {
            if (settings.HalfShare <= 0)
                return false;

            int block = (int)Math.Floor((time - timingPoint.Time) / (timingPoint.BeatLength * timingPoint.TimeSignature.Numerator * 2));
            double order = (uint)(block * 2654435761u) / (double)uint.MaxValue;

            // A chorus's blocks get half beats before the verses'.
            if (original.ControlPointInfo.EffectPointAt(time + 1).KiaiMode)
                order *= 0.5;

            return order < settings.HalfShare;
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

            // A chorus moves more: often spinning, growing or pulsing.
            if (settings.RotateShapes && inChorus && rng.NextDouble() < 0.4)
                twist = rng.Next(3) switch { 0 => Twist.Spin, 1 => Twist.Grow, _ => Twist.Pulse };

            // A quickly turning shape: a good part of a turn each round.
            if (twist == Twist.Spin)
                drift = (float)(rng.NextDouble() * 0.25 + 0.35) * (rng.NextDouble() < 0.5 ? -1 : 1);

            // FIX: mirroring a shape of two points (back and forth) swaps them, so the next round's first note landed on the last one.
            if (twist == Twist.Mirror && shape.Order.Length < 3)
                twist = Twist.None;

            // Over the same points the other way in every other round (only for the round figures this works on).
            int[]? morphOrder = twist == Twist.Morph ? morphed(shape) : null;

            if (twist == Twist.Morph && morphOrder == null)
                twist = Twist.None;

            placedTwists.Add(twist.ToString());

            int rounds = Math.Max(1, (int)Math.Ceiling((end - start) / (double)shape.Order.Length));

            // FIX: a shape that pulses or grows past its size is placed so its biggest round fits, and only grows as far as the playfield
            // allows (it used to run into the edges, where its points were squashed together).
            float peak = twist switch
            {
                Twist.Pulse => 1.12f,
                Twist.Grow => 1.15f,
                _ => 1,
            };

            peak = Math.Max(1, Math.Min(peak, Math.Min(maxX / radiusX, maxY / radiusY)));

            Vector2 centre = pickCentre(shape, radiusX * peak, radiusY * peak, rotation, way, last, distance);
            Vector2 travel = twist == Twist.Travel ? new Vector2(MathF.Cos(rotation * 3), MathF.Sin(rotation * 3)) * distance * 0.25f : Vector2.Zero;

            for (int i = start; i < end; i++)
            {
                int n = i - start;
                int round = n / shape.Order.Length;

                int turn = twist == Twist.Reverse && round % 2 == 1 ? -way : way;
                float scale = twist switch
                {
                    Twist.Pulse => round % 2 == 0 ? peak : 0.8f,
                    // From a little smaller than asked to a little bigger, about the asked size on average.
                    Twist.Grow => rounds < 2 ? 1 : 0.8f + (peak - 0.8f) * round / (rounds - 1),
                    _ => 1,
                };

                int side = twist == Twist.Mirror && round % 2 == 1 ? -1 : 1;

                var order = morphOrder != null && round % 2 == 1 ? morphOrder : shape.Order;
                var vertex = shape.Vertices[order[n % order.Length]];
                Vector2 point = rotate(new Vector2(vertex.X * side, vertex.Y * turn), rotation + round * drift);

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

                var position = clamp(centre + new Vector2(point.X * radiusX, point.Y * radiusY) * scale);

                // FIX: where a round's change (mirrored, reversed, started elsewhere) or the playfield's edge brings a point onto the last note,
                // it goes to the opposite side of the shape instead, so every note is a real jump.
                if (last is Vector2 previous && Vector2.Distance(position, previous) < Math.Max(20, distance * 0.25f))
                {
                    position = clamp(centre - new Vector2(point.X * radiusX, point.Y * radiusY) * scale);

                    // (A point in the middle has no opposite side: a jump's length away from the last note, towards the middle of the playfield.)
                    if (Vector2.Distance(position, previous) < Math.Max(20, distance * 0.25f))
                    {
                        Vector2 away = OsuPlayfield.BASE_SIZE / 2 - previous;
                        away = away.LengthSquared > 1 ? Vector2.Normalize(away) : new Vector2(1, 0);
                        position = clamp(previous + away * distance);
                    }
                }

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
        /// Where mapper patterns come from: the stars mode's own collection if one was chosen, otherwise the aim one.
        /// </summary>
        private static PatternCategory patternCategory
            => MinedPatternSet.Current?.Jumps.TryGetValue(PatternCategory.Stars, out var stars) == true && stars.Count > 0 ? PatternCategory.Stars : PatternCategory.Aim;

        /// <summary>
        /// A real jump pattern from the aim collection (the kind aim maps are made of: wide angles, zigzags across the screen), laid on the grid:
        /// sized to the wanted jump, turned and mirrored, and played round after round, mirrored every other round so it goes back and forth.
        /// </summary>
        /// <returns>Whether a pattern could be placed.</returns>
        private bool placeMined(List<OsuHitObject> result, List<(double time, double step)> times, int start, int end, float distance, ref Vector2? last)
        {
            var pattern = MinedPatternSet.Current?.PickJumps(patternCategory, minedLength, Enumerable.Repeat(0.5, minedLength - 1).ToList(), rng, 0.5, null, recentMined);

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

            Vector2 random() => new Vector2(spread(margin - min.X, margin - min.X + Math.Max(0, room.X - (max.X - min.X)), left),
                spread(margin - min.Y, margin - min.Y + Math.Max(0, room.Y - (max.Y - min.Y)), top));

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

            /// <summary>The shape grows from round to round (smaller than asked to bigger), so its jumps build up.</summary>
            Grow,

            /// <summary>Every other round is mirrored left to right: the same shape, gone round from the other side.</summary>
            Mirror,

            /// <summary>The shape turns quickly, a good part of a turn each round.</summary>
            Spin,

            /// <summary>Every other round goes over the same points the other way: a pentagon, then the star on its corners, and back.</summary>
            Morph,
        }

        /// <summary>
        /// Somewhere the whole shape fits, with its first point about a jump away from the last note.
        /// </summary>
        private Vector2 pickCentre(Shape shape, float radiusX, float radiusY, float rotation, int way, Vector2? last, float distance)
        {
            float minX = margin + radiusX, maxX = OsuPlayfield.BASE_SIZE.X - margin - radiusX;
            float minY = margin + radiusY, maxY = OsuPlayfield.BASE_SIZE.Y - margin - radiusY;

            Vector2 random() => new Vector2(minX > maxX ? OsuPlayfield.BASE_SIZE.X / 2 : spread(minX, maxX, left),
                minY > maxY ? OsuPlayfield.BASE_SIZE.Y / 2 : spread(minY, maxY, top));

            if (last == null)
                return random();

            var first = shape.Vertices[shape.Order[0]];
            Vector2 point = rotate(new Vector2(first.X, first.Y * way), rotation);
            Vector2 firstOffset = new Vector2(point.X * radiusX, point.Y * radiusY);

            return Enumerable.Range(0, 24).Select(_ => random()).MinBy(c => Math.Abs(Vector2.Distance(c + firstOffset, last.Value) - distance));
        }

        /// <summary>
        /// How far into the playfield a shape going to the practised quarter may sit (as a share of where it could be).
        /// </summary>
        private const float zone_reach = 0.1f;

        private bool left => settings.WeakQuarter is Configuration.PlayfieldQuarter.TopLeft or Configuration.PlayfieldQuarter.BottomLeft;

        private bool top => settings.WeakQuarter is Configuration.PlayfieldQuarter.TopLeft or Configuration.PlayfieldQuarter.TopRight;

        /// <summary>
        /// Somewhere between <paramref name="low"/> and <paramref name="high"/>: anywhere, or near the practised quarter's side (<paramref name="lowSide"/> or the other).
        /// </summary>
        private float spread(float low, float high, bool lowSide)
        {
            float r = (float)rng.NextDouble();

            if (!towardZone)
                return low + r * (high - low);

            float reach = (high - low) * zone_reach;
            return lowSide ? low + r * reach : high - r * reach;
        }

        private static Vector2 rotate(Vector2 v, float angle)
        {
            float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
            return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        }

        private Vector2 clamp(Vector2 position) => Vector2.Clamp(position, new Vector2(margin), OsuPlayfield.BASE_SIZE - new Vector2(margin));

        private Shape pickShape(Shape? previous)
        {
            // The player's choice of shapes; harder ones wait for higher ratings, unless they're all that was chosen.
            bool mined = MinedPatternSet.Current?.Jumps.TryGetValue(patternCategory, out var found) == true && found.Count > 0;
            var chosen = shapes.Where(s => settings.MetronomeShapes.HasFlag(s.Kind) && (s.Kind != MetronomeShapes.MapperPatterns || mined)).ToList();
            if (chosen.Count == 0)
                chosen = shapes.Where(s => s.Kind != MetronomeShapes.MapperPatterns || mined).ToList();

            // The same figure again only up to the player's limit (never, by default).
            int limit = Math.Max(1, settings.MaxSameInARow);
            // (Two shapes side by side, and rays as long as a jump, span too much of the playfield to go into a quarter being practised.)
            bool allowed(Shape s) => (s.Kind != previous?.Kind || sameShapeRun < limit) && !(towardZone && s.Kind is MetronomeShapes.TwinShapes or MetronomeShapes.Spokes);

            var available = chosen.Where(s => allowed(s) && profile.Stars >= s.MinStars).ToList();
            if (available.Count == 0)
                available = chosen.Where(allowed).ToList();
            if (available.Count == 0)
                available = chosen;
            // Choruses (kiai) lean to the lively figures (stars, crossings, spirals), verses to the calmer polygons, so the two sound different.
            // Going into a quarter being practised, compact figures (long jumps for their size, like stars) more often: the big ones don't fit.
            double weight(Shape s) => s.Weight * (inChorus ? (calm(s.Kind) ? 0.6 : 1.5) : (calm(s.Kind) ? 1.3 : 0.85)) * (towardZone ? Math.Pow(s.Edge, 3) : 1) * fits(s);

            // A jump longer than a shape can make on the playfield (its edge times the most room it has) goes to one that can make it, mostly:
            // otherwise the shape is squeezed and the map ends up easier than asked (at whole beats, stars stopped around 3.8 for 4.5).
            // (Shapes made for each use, or a mapper's patterns, are sized to fit when placed.)
            double reach(Shape s) => s.Order.Length < 2 ? double.MaxValue : s.Edge * MathF.Sqrt((OsuPlayfield.BASE_SIZE.X / 2 - margin) * (OsuPlayfield.BASE_SIZE.Y / 2 - margin));
            double fits(Shape s) => wantedDistance <= reach(s) ? 1 : 0.15;

            double roll = rng.NextDouble() * available.Sum(weight);

            foreach (var shape in available)
            {
                roll -= weight(shape);

                if (roll <= 0)
                    return withStart(made(shape));
            }

            return withStart(made(available[^1]));
        }

        /// <summary>
        /// The figures made up for each use (a random shape, rays from the middle), or <paramref name="shape"/> itself.
        /// </summary>
        private Shape made(Shape shape) => shape.Kind switch
        {
            MetronomeShapes.RandomShape => randomShape(shape.Weight),
            MetronomeShapes.Spokes => spokes(shape.Weight),
            _ => shape,
        };

        /// <summary>
        /// Out from the middle and back, to each of three to five points in turn: every jump the same length, through one spot.
        /// </summary>
        private Shape spokes(double weight)
        {
            int rays = rng.Next(3, 6);
            var vertices = new[] { Vector2.Zero }.Concat(Enumerable.Range(0, rays).Select(i => new Vector2(MathF.Cos(i * MathF.Tau / rays), MathF.Sin(i * MathF.Tau / rays)))).ToArray();

            // Round the rays in order, or every other one (a star's way) with five.
            int step = rays == 5 && rng.NextDouble() < 0.5 ? 2 : 1;
            var order = Enumerable.Range(0, rays).SelectMany(i => new[] { 0, 1 + i * step % rays }).ToArray();

            return new Shape(MetronomeShapes.Spokes, vertices, order, weight);
        }

        /// <summary>
        /// The other way over a round figure's points: a star's for a polygon, a polygon's for a star (five, seven or eight points), or null.
        /// </summary>
        private static int[]? morphed(Shape shape)
        {
            int count = shape.Vertices.Length;

            if (shape.Order.Length != count || count is not (5 or 7 or 8)
                || shape.Kind is not (MetronomeShapes.Star or MetronomeShapes.Pentagon or MetronomeShapes.SevenPointStar or MetronomeShapes.EightPointStar or MetronomeShapes.UnevenStar))
                return null;

            int step = ((shape.Order[1] - shape.Order[0]) % count + count) % count;
            int other = count switch
            {
                5 => step == 1 || step == 4 ? 2 : 1,
                7 => step == 3 || step == 4 ? 2 : 3,
                _ => step == 3 || step == 5 ? 1 : 3,
            };

            return Enumerable.Range(0, count).Select(i => (shape.Order[0] + i * other) % count).ToArray();
        }

        private static bool calm(MetronomeShapes kind) => kind is MetronomeShapes.Triangle or MetronomeShapes.Square or MetronomeShapes.Pentagon or MetronomeShapes.Hexagon
            or MetronomeShapes.Rectangle or MetronomeShapes.BackAndForth;

        /// <summary>
        /// Whether the shape being placed is in a chorus (kiai).
        /// </summary>
        private bool inChorus;

        /// <summary>
        /// The jump the shape being picked is to make.
        /// </summary>
        private float wantedDistance;

        /// <summary>
        /// <paramref name="shape"/> gone round from any of its points, not always the same one (it matters for the uneven ones: a spiral,
        /// a rectangle, a zigzag, two shapes side by side).
        /// </summary>
        private Shape withStart(Shape shape)
        {
            if (shape.Order.Length < 2 || shape.Kind == MetronomeShapes.MapperPatterns)
                return shape;

            int offset = rng.Next(shape.Order.Length);
            return shape with { Order = shape.Order.Skip(offset).Concat(shape.Order.Take(offset)).ToArray() };
        }

        /// <summary>
        /// A shape: <see cref="Vertices"/> (within a radius of one), visited in <see cref="Order"/> (a star visits every other point).
        /// <see cref="Edge"/> is the average jump between neighbours in the order, for a radius of one.
        /// </summary>
        private sealed record Shape(MetronomeShapes Kind, Vector2[] Vertices, int[] Order, double Weight, double MinStars = 0)
        {
            public float Edge { get; } = Order.Length < 2 ? 1 : Enumerable.Range(0, Order.Length).Average(i => Vector2.Distance(Vertices[Order[(i + 1) % Order.Length]], Vertices[Order[i]]));

            /// <summary>
            /// Points evenly around a circle, visited <paramref name="step"/> apart (2 for a five-point star).
            /// </summary>
            public static Shape Regular(MetronomeShapes kind, int points, int step, double weight, double minStars = 0, Func<int, float>? radius = null)
                => new Shape(kind,
                    Enumerable.Range(0, points).Select(i => new Vector2(MathF.Cos(i * MathF.Tau / points), MathF.Sin(i * MathF.Tau / points)) * (radius?.Invoke(i) ?? 1)).ToArray(),
                    Enumerable.Range(0, points).Select(i => i * step % points).ToArray(), weight, minStars);
        }

        /// <summary>
        /// A shape made up for this once: three to seven points at uneven angles and distances, gone round as a polygon, a star or in a jumbled order
        /// (with no jump between neighbouring points too short to be one). Every one is new, so it can't wear out.
        /// </summary>
        private Shape randomShape(double weight)
        {
            for (int attempt = 0; ; attempt++)
            {
                int count = rng.Next(3, 8);

                var angles = Enumerable.Range(0, count).Select(i => (float)((i + 0.15 + rng.NextDouble() * 0.7) * Math.Tau / count)).ToArray();
                var vertices = angles.Select(a => new Vector2(MathF.Cos(a), MathF.Sin(a)) * (float)(0.55 + rng.NextDouble() * 0.45)).ToArray();

                int[] order = rng.NextDouble() switch
                {
                    < 0.35 => Enumerable.Range(0, count).ToArray(),
                    < 0.7 when count >= 5 => Enumerable.Range(0, count).Select(i => i * 2 % count).ToArray(),
                    _ => Enumerable.Range(0, count).OrderBy(_ => rng.Next()).ToArray(),
                };

                // Real jumps only (and a star over an even count of points would leave half of them out).
                bool allVisited = order.Distinct().Count() == count;
                bool longEnough = Enumerable.Range(0, count).All(i => Vector2.Distance(vertices[order[i]], vertices[order[(i + 1) % count]]) > 0.6f);

                if ((allVisited && longEnough) || attempt > 30)
                    return new Shape(MetronomeShapes.RandomShape, vertices, order, weight);
            }
        }

        private static readonly Shape[] shapes =
        {
            Shape.Regular(MetronomeShapes.Star, 5, 2, 3),
            Shape.Regular(MetronomeShapes.BackAndForth, 2, 1, 0.5),
            Shape.Regular(MetronomeShapes.Triangle, 3, 1, 1.2),
            Shape.Regular(MetronomeShapes.Square, 4, 1, 1.2),
            new Shape(MetronomeShapes.CrossedSquare, Shape.Regular(MetronomeShapes.Square, 4, 1, 0).Vertices, new[] { 0, 2, 1, 3 }, 1),
            Shape.Regular(MetronomeShapes.Pentagon, 5, 1, 0.4),
            Shape.Regular(MetronomeShapes.Hexagon, 6, 1, 0.2),
            Shape.Regular(MetronomeShapes.SevenPointStar, 7, 3, 0.8, minStars: 5),
            new Shape(MetronomeShapes.StarOfDavid, Shape.Regular(MetronomeShapes.Hexagon, 6, 1, 0).Vertices, new[] { 0, 2, 4, 1, 3, 5 }, 0.3, MinStars: 4.5),
            Shape.Regular(MetronomeShapes.EightPointStar, 8, 3, 0.8, minStars: 5),
            // A star whose points are alternately near and far, so its jumps go long, short, long.
            Shape.Regular(MetronomeShapes.UnevenStar, 5, 2, 1, radius: i => i % 2 == 0 ? 1 : 0.55f),
            // Back and forth along a zigzag: across the playfield and back the same way.
            new Shape(MetronomeShapes.Zigzag,
                new[] { new Vector2(-1, -0.45f), new Vector2(-0.5f, 0.45f), new Vector2(0, -0.45f), new Vector2(0.5f, 0.45f), new Vector2(1, -0.45f) },
                new[] { 0, 1, 2, 3, 4, 3, 2, 1 }, 1),
            // Long sides and short ones in turn.
            new Shape(MetronomeShapes.Rectangle, new[] { new Vector2(-1, -0.5f), new Vector2(1, -0.5f), new Vector2(1, 0.5f), new Vector2(-1, 0.5f) }, new[] { 0, 1, 2, 3 }, 0.8),
            // Two small triangles side by side, jumped between in turn: every jump crosses the playfield.
            new Shape(MetronomeShapes.TwinShapes,
                Enumerable.Range(0, 6).Select(i => new Vector2(i < 3 ? -0.6f : 0.6f, 0) + new Vector2(MathF.Cos(i % 3 * MathF.Tau / 3 + MathF.PI / 2), MathF.Sin(i % 3 * MathF.Tau / 3 + MathF.PI / 2)) * 0.4f).ToArray(),
                new[] { 0, 3, 1, 4, 2, 5 }, 1.2),
            // Round and outwards, three eighths of a turn a jump: the jumps grow, then one leap back to the middle.
            new Shape(MetronomeShapes.Spiral,
                Enumerable.Range(0, 8).Select(i => new Vector2(MathF.Cos(i * MathF.PI * 3 / 4), MathF.Sin(i * MathF.PI * 3 / 4)) * (0.35f + 0.65f * i / 7)).ToArray(),
                Enumerable.Range(0, 8).ToArray(), 0.8),
            // Along a figure of eight (never through its middle, so no two notes meet there).
            new Shape(MetronomeShapes.FigureEight,
                Enumerable.Range(0, 8).Select(i => (i + 0.5f) * MathF.Tau / 8).Select(t => new Vector2(MathF.Sin(t), MathF.Sin(t) * MathF.Cos(t) * 1.3f)).ToArray(),
                Enumerable.Range(0, 8).ToArray(), 0.8),
            // Rays from the middle (see spokes); made for each use, this one only stands for them when picking.
            new Shape(MetronomeShapes.Spokes, Array.Empty<Vector2>(), Array.Empty<int>(), 1),
            // Made up for each use (see randomShape); this one only stands for them when picking.
            new Shape(MetronomeShapes.RandomShape, Array.Empty<Vector2>(), Array.Empty<int>(), 2.5),
            // Real jump patterns from the aim collection, laid on the grid (see placeMined).
            new Shape(MetronomeShapes.MapperPatterns, Array.Empty<Vector2>(), Array.Empty<int>(), 3),
        };
    }
}
