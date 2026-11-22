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
            Figure? previousShape = null;
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
                // FIX ("круги рядом, такой паттерн всю карту", "прыжки одной длины"): sizes from 0.65 to 1.4 rather than 0.7 to 1.3.
                float size = (float)(0.65 + rng.NextDouble() * (towardZone ? 0.35 : 0.75));

                // FIX: jumping every half beat throughout, every jump was about as long (140 px at 6* and 188 BPM, "круги рядом" all through).
                // Some figures go on the whole beats only, twice as far (as fast for the cursor, so as hard): big figures across the playfield
                // between the small fast ones, more in the verses, fewer in the chorus.
                slowFigure = !settings.AlwaysHalfBeats && settings.FlowLevel == 0 && !towardZone && step < timingPoint.BeatLength * 0.75
                             // (Only while such jumps still fit: past that, they'd be cut short and the map would miss its stars.)
                             && profile.AimVelocity * step * 2 * settings.JumpSpacing * settings.CalibrationSpacing <= Math.Min(profile.MaxJumpDistance * 1.3, 380)
                             && rng.NextDouble() < (inChorus ? 0.15 : 0.35);

                if (slowFigure)
                {
                    step *= 2;
                    notesPerMeasure = Math.Max(1, notesPerMeasure / 2);
                }

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
                int roundLength = shape.FromMaps ? minedLength : shape.Order.Length;
                int length = Math.Max(1, (int)Math.Round(notesPerMeasure * measures / (double)roundLength)) * roundLength;

                // (Counting whole beats: the half beats between them come along, see isHalf.)
                int end = index, wholes = 0;
                double grid = times[index].step;

                // (A figure on the whole beats only leaves the half beats between them out.)
                var figureTimes = times;
                int from = index, to;

                if (slowFigure)
                {
                    figureTimes = new List<(double time, double step)>();

                    while (end < times.Count && figureTimes.Count < length && times[end].step == grid)
                    {
                        if (onBeat(times[end].time))
                            figureTimes.Add((times[end].time, step));
                        end++;
                    }

                    from = 0;
                    to = figureTimes.Count;
                }
                else
                {
                    while (end < times.Count && wholes < length && times[end].step == grid)
                    {
                        if (!isHalf(times, end))
                            wholes++;
                        end++;
                    }

                    to = end;
                }

                // (A mapper's pattern that can't be found falls back to a star.)
                if (to > from && (!shape.FromMaps || !placeMined(result, figureTimes, from, to, distance, ref last)))
                    place(result, figureTimes, from, to, shape.FromMaps ? PatternLibrary.FigureOf(MetronomeShapes.Star) : shape, distance, ref last);

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
            gallopCarry = 0.5;
            gallopped = false;
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
                bool onBeat = Math.Abs((time - timingPoint.Time) / beat - Math.Round((time - timingPoint.Time) / beat)) < 0.1;

                // Half beats: everywhere, in the chorus only, or (the stars between) spread evenly among the whole beats.
                bool half = settings.AlwaysHalfBeats
                            || (settings.FlowLevel == 2
                                ? !onBeat && gallop(nearestDistance(starts, objects, time) < beat / 8)
                                : profile.MaxObjectsPerSecondIn(kiai, beat) >= 2000 / beat && (settings.FlowLevel == 0 || (settings.FlowLevel == 1 && kiai)));

                if (!half && !onBeat)
                    continue;

                if (original.Breaks.Any(b => time > b.StartTime && time < b.EndTime) || nearestDistance(starts, objects, time) > beat * 4)
                    continue;

                // (Half beats spread among whole ones keep the whole beat's step, so a figure carries on through them.)
                result.Add((time, half && !(settings.FlowLevel == 2 && !settings.AlwaysHalfBeats) ? beat / 2 : beat));
            }

            return result;
        }

        private double gallopCarry = 0.5;

        /// <summary>
        /// Whether the half beat after the last was jumped (the next one never is: three notes at the faster speed, never a run).
        /// </summary>
        private bool gallopped;

        /// <summary>
        /// Whether the next half beat between whole ones is jumped: about the share in <see cref="FreeplayGenerationSettings.HalfShare"/>, on the half beats
        /// where the music has a note (<paramref name="musical"/>, the mapper's), or anywhere once they've been missing a while.
        /// (FIX: in blocks, or the chorus only, the half beats made some parts much harder than the rest: "неадекватный разброс"; spread evenly
        /// over the whole song, every other beat the same, it played as one pattern all through: "один и тот же паттерн всю карту".)
        /// </summary>
        private bool gallop(bool musical)
        {
            gallopCarry = Math.Min(gallopCarry + settings.HalfShare, 3);

            if (gallopped || gallopCarry < 1 || (!musical && gallopCarry < 2.5))
            {
                gallopped = false;
                return false;
            }

            gallopCarry -= 1;
            gallopped = true;
            return true;
        }

        /// <summary>
        /// A half beat spread among whole ones (see <see cref="gallop"/>): not a point of the figure but a note on the way between two of them.
        /// FIX: as a point of its own it had a whole jump in half the time, so the cursor had to move twice as fast there ("inhuman" at 225 BPM).
        /// </summary>
        private bool isHalf(List<(double time, double step)> times, int i)
            => settings.FlowLevel == 2 && !settings.AlwaysHalfBeats && i > 0 && times[i].time - times[i - 1].time < times[i].step * 0.75;

        /// <summary>
        /// Adds a half beat with its place left for <see cref="placeHalf"/>, once the figure's next point is known.
        /// </summary>
        /// <returns>Whether note <paramref name="i"/> was such a half beat.</returns>
        private bool holdHalf(List<OsuHitObject> result, List<(double time, double step)> times, int i, Vector2? last, ref int? pending)
        {
            if (last == null || pending != null || !isHalf(times, i))
                return false;

            result.Add(new HitCircle
            {
                StartTime = times[i].time,
                Position = last.Value,
                Samples = RhythmExtractor.PlainSampleNear(original, times[i].time),
            });

            pending = result.Count - 1;
            return true;
        }

        /// <summary>
        /// The half beat waiting goes halfway between the notes either side of it and well off to one side (<paramref name="bend"/> of the jump),
        /// so the three notes make a sharp turn. (FIX: a little off the line, every half beat read as one straight run cut in two, the same all through.)
        /// </summary>
        private void placeHalf(List<OsuHitObject> result, ref int? pending, Vector2? last, Vector2 next, float bend)
        {
            if (pending is not int index || last is not Vector2 from)
                return;

            Vector2 way = next - from;
            result[index].Position = clamp(from + way * 0.5f + new Vector2(-way.Y, way.X) * bend);
            pending = null;
        }

        /// <summary>
        /// A half beat left at the end of a figure: half a jump on from the last note, towards the middle of the playfield.
        /// </summary>
        private void finishHalf(List<OsuHitObject> result, ref int? pending, Vector2? last, float distance)
        {
            if (pending is not int index || last is not Vector2 from)
                return;

            Vector2 away = OsuPlayfield.BASE_SIZE / 2 - from;
            away = away.LengthSquared > 1 ? Vector2.Normalize(away) : new Vector2(1, 0);
            result[index].Position = clamp(from + away * distance * 0.5f);
            pending = null;
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
            // (A figure on the whole beats may go further than the stars' longest jump: twice the time for it, see slowFigure.)
            double distance = Math.Min(profile.MaxJumpDistance * (slowFigure ? 1.3 : 1), profile.AimVelocity * gap) * settings.JumpSpacing * settings.CalibrationSpacing;
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

        private void place(List<OsuHitObject> result, List<(double time, double step)> times, int start, int end, Figure shape, float distance, ref Vector2? last)
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

            int rounds = Math.Max(1, (int)Math.Ceiling(Enumerable.Range(start, end - start).Count(i => !isHalf(times, i)) / (double)shape.Order.Length));
            float bend = (float)(0.3 + rng.NextDouble() * 0.15) * (rng.NextDouble() < 0.5 ? -1 : 1);
            int? pendingHalf = null;
            int n = -1;

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
                if (holdHalf(result, times, i, last, ref pendingHalf))
                    continue;

                n++;
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

                placeHalf(result, ref pendingHalf, last, position, bend);

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

            finishHalf(result, ref pendingHalf, last, distance);
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
            int? pendingHalf = null;
            int n = -1;

            for (int i = start; i < end; i++)
            {
                if (holdHalf(result, times, i, last, ref pendingHalf))
                    continue;

                n++;
                int round = n / minedLength;
                var offset = offsets[n % minedLength];

                // Every other round mirrored, so the pattern comes back the other way instead of jumping back to its start.
                if (round % 2 == 1)
                    offset = new Vector2(2 * middle - offset.X, offset.Y);

                var position = clamp(origin + offset);
                placeHalf(result, ref pendingHalf, last, position, 0.35f);

                result.Add(new HitCircle
                {
                    StartTime = times[i].time,
                    Position = position,
                    NewCombo = n % minedLength == 0,
                    Samples = RhythmExtractor.PlainSampleNear(original, times[i].time),
                });

                last = position;
            }

            finishHalf(result, ref pendingHalf, last, distance);

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
        private Vector2 pickCentre(Figure shape, float radiusX, float radiusY, float rotation, int way, Vector2? last, float distance)
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

        private Figure pickShape(Figure? previous)
        {
            // The player's choice of shapes; harder ones wait for higher ratings, unless they're all that was chosen.
            bool mined = MinedPatternSet.Current?.Jumps.TryGetValue(patternCategory, out var found) == true && found.Count > 0;
            var chosen = PatternLibrary.Figures.Where(s => settings.MetronomeShapes.HasFlag(s.Kind) && (!s.FromMaps || mined)).ToList();
            if (chosen.Count == 0)
                chosen = PatternLibrary.Figures.Where(s => !s.FromMaps || mined).ToList();

            // The same figure again only up to the player's limit (never, by default).
            int limit = Math.Max(1, settings.MaxSameInARow);
            // (Two shapes side by side, and rays as long as a jump, span too much of the playfield to go into a quarter being practised.)
            bool allowed(Figure s) => (s.Kind != previous?.Kind || sameShapeRun < limit) && !(towardZone && s.Wide);

            var available = chosen.Where(s => allowed(s) && profile.Stars >= s.MinStars).ToList();
            if (available.Count == 0)
                available = chosen.Where(allowed).ToList();
            if (available.Count == 0)
                available = chosen;
            // Choruses (kiai) lean to the lively figures (stars, crossings, spirals), verses to the calmer polygons, so the two sound different.
            // Going into a quarter being practised, compact figures (long jumps for their size, like stars) more often: the big ones don't fit.
            double weight(Figure s) => s.Weight * (inChorus ? (s.Calm ? 0.6 : 1.5) : (s.Calm ? 1.3 : 0.85)) * (towardZone ? Math.Pow(s.Edge, 3) : 1) * fits(s)
                                       // (Big figures on the whole beats lean to the calm ones, squares and polygons, so they play unlike the sharp fast ones.)
                                       * (slowFigure && s.Calm ? 1.8 : 1);

            // A jump longer than a shape can make on the playfield (its edge times the most room it has) goes to one that can make it, mostly:
            // otherwise the shape is squeezed and the map ends up easier than asked (at whole beats, stars stopped around 3.8 for 4.5).
            // (Shapes made for each use, or a mapper's patterns, are sized to fit when placed.)
            double reach(Figure s) => s.Order.Length < 2 ? double.MaxValue : s.Edge * MathF.Sqrt((OsuPlayfield.BASE_SIZE.X / 2 - margin) * (OsuPlayfield.BASE_SIZE.Y / 2 - margin));
            double fits(Figure s) => wantedDistance <= reach(s) ? 1 : 0.15;

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
        /// A figure made up for this use (see <see cref="Figure.Make"/>), or <paramref name="shape"/> itself.
        /// </summary>
        private Figure made(Figure shape) => shape.Make?.Invoke(shape, rng) ?? shape;

        /// <summary>
        /// The other way over a round figure's points: a star's for a polygon, a polygon's for a star (five, seven or eight points), or null.
        /// </summary>
        private static int[]? morphed(Figure shape)
        {
            int count = shape.Vertices.Length;

            if (shape.Order.Length != count || count is not (5 or 7 or 8)
                || !shape.Morphs)
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

        /// <summary>
        /// Whether the shape being placed is in a chorus (kiai).
        /// </summary>
        private bool inChorus;

        /// <summary>
        /// The jump the shape being picked is to make.
        /// </summary>
        private float wantedDistance;

        /// <summary>
        /// Whether the figure being placed goes on the whole beats only, with the half beats left out (see <see cref="Generate"/>).
        /// </summary>
        private bool slowFigure;

        /// <summary>
        /// Whether <paramref name="time"/> is on a beat (not between two).
        /// </summary>
        private bool onBeat(double time)
        {
            var timingPoint = original.ControlPointInfo.TimingPointAt(time);
            double beats = (time - timingPoint.Time) / timingPoint.BeatLength;
            return Math.Abs(beats - Math.Round(beats)) < 0.1;
        }

        /// <summary>
        /// <paramref name="shape"/> gone round from any of its points, not always the same one (it matters for the uneven ones: a spiral,
        /// a rectangle, a zigzag, two shapes side by side).
        /// </summary>
        private Figure withStart(Figure shape)
        {
            if (shape.Order.Length < 2 || shape.FromMaps)
                return shape;

            int offset = rng.Next(shape.Order.Length);
            return shape with { Order = shape.Order.Skip(offset).Concat(shape.Order.Take(offset)).ToArray() };
        }
    }
}
