// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Generation
{
    /// <summary>
    /// The square mode (<see cref="FreeplayGenerationSettings.Squares"/>): square jump practice, as in "Square Jump Practice Maps".
    /// Every jump goes along 0, 45 or 90 degrees: squares upright or on a corner, ladders, and now and then (each can be turned off)
    /// a square creeping across, growing, turning by 45 degrees, or squares side by side over the playfield.
    /// </summary>
    public partial class MetronomeGenerator
    {
        private readonly List<SquarePatterns> placedSquares = new List<SquarePatterns>();

        /// <summary>
        /// The square patterns placed, in order (the square mode only).
        /// </summary>
        public IReadOnlyList<SquarePatterns> PlacedSquares => placedSquares;

        private SquarePatterns previousSquare;
        private int sameSquareRun;

        private static readonly SquarePatterns[] square_kinds = Enum.GetValues<SquarePatterns>().Where(k => k != SquarePatterns.None && k != SquarePatterns.All).ToArray();

        /// <summary>
        /// The three the mode is made of, most of the time; the rest are now and then.
        /// </summary>
        private static double squareWeight(SquarePatterns kind) => kind switch
        {
            SquarePatterns.Square => 3,
            SquarePatterns.Diamond => 2.5,
            SquarePatterns.Ladder => 1.5,
            _ => 0.35,
        };

        private static readonly float sqrt2 = MathF.Sqrt(2);

        /// <summary>
        /// The room a square has: the playfield's height (its shorter side) within the margins.
        /// </summary>
        private float squareRoom => OsuPlayfield.BASE_SIZE.Y - 2 * margin - 8;

        /// <summary>
        /// The side of <paramref name="kind"/> for jumps of <paramref name="distance"/> (a ladder's jumps are a side and a diagonal by turns).
        /// </summary>
        private static float squareSide(SquarePatterns kind, float distance) => kind is SquarePatterns.Ladder or SquarePatterns.Turning ? distance * 2 / (1 + sqrt2) : distance;

        /// <summary>
        /// The longest side <paramref name="kind"/> can have on the playfield.
        /// </summary>
        private float maxSquareSide(SquarePatterns kind) => kind switch
        {
            SquarePatterns.Diamond => squareRoom / sqrt2,
            // (Its diamond reaches a side from the middle, see squarePath.)
            SquarePatterns.Turning => squareRoom / 2,
            SquarePatterns.Growing => squareRoom / 1.25f,
            SquarePatterns.Sliding => squareRoom * 0.8f,
            SquarePatterns.Grid => Math.Min(squareRoom, (OsuPlayfield.BASE_SIZE.X - 2 * margin - 8) / 2),
            // (Two rungs, a side apart.)
            _ => squareRoom,
        };

        private SquarePatterns pickSquare(float distance)
        {
            var chosen = square_kinds.Where(k => settings.SquarePatterns.HasFlag(k)).ToList();
            if (chosen.Count == 0)
                chosen = new List<SquarePatterns> { SquarePatterns.Square, SquarePatterns.Diamond, SquarePatterns.Ladder };

            // (Squares are the mode: the same kind twice is fine, more only as the player allows.)
            int limit = Math.Max(2, settings.MaxSameInARow);

            // A jump too long for a kind on the playfield goes to one that can make it, mostly (or the map ends up easier than asked).
            double weight(SquarePatterns k) => squareWeight(k) * (squareSide(k, distance) <= maxSquareSide(k) ? 1 : 0.1)
                                                             * (k == previousSquare && sameSquareRun >= limit && chosen.Count > 1 ? 0 : 1);

            double roll = rng.NextDouble() * chosen.Sum(weight);

            foreach (var kind in chosen)
            {
                roll -= weight(kind);

                if (roll <= 0)
                    return kind;
            }

            return chosen[^1];
        }

        /// <summary>
        /// The eight ways a jump may go: along the playfield's sides and its diagonals.
        /// </summary>
        private static readonly Vector2[] eight_ways = Enumerable.Range(0, 8).Select(i => new Vector2(MathF.Round(MathF.Cos(i * MathF.PI / 4), 5), MathF.Round(MathF.Sin(i * MathF.PI / 4), 5))).ToArray();

        private void placeSquare(List<OsuHitObject> result, List<(double time, double step)> times, int start, int end, SquarePatterns kind, float distance, ref Vector2? last)
        {
            Vector2 low = new Vector2(margin + 2), high = OsuPlayfield.BASE_SIZE - new Vector2(margin + 2);
            float side = Math.Min(squareSide(kind, distance), maxSquareSide(kind));
            int way = rng.NextDouble() < 0.5 ? 1 : -1;
            // (Half beats in between go off to one side by half the jump: a right angle, their own two jumps at 45 degrees.)
            float bend = rng.NextDouble() < 0.5 ? 0.5f : -0.5f;

            // The points of the figure: every note but the half beats held for later, counted as the loop below holds them
            // (FIX: counted as every note but those isHalf marks, which also marks the beat after a half beat, the path ran out
            // before the notes did and the rest were stacked on its last point).
            int notes = 0;
            bool held = false;

            for (int i = start; i < end; i++)
            {
                if (!held && (last != null || notes > 0) && isHalf(times, i))
                {
                    held = true;
                    continue;
                }

                held = false;
                notes++;
            }

            int roundLength = kind == SquarePatterns.Ladder ? 2 : 4;
            int rounds = Math.Max(1, (notes + roundLength - 1) / roundLength);

            // The whole path, then laid where it starts a jump from the last note along one of the eight ways.
            var path = kind == SquarePatterns.Ladder ? ladderPath(notes, side) : squarePath(kind, notes, rounds, side);

            // (Only half beats here: they go off the last note, see finishSquareHalf.)
            if (path.Count == 0)
                path.Add(Vector2.Zero);

            // (Squares and diamonds may start from any corner: round after round the same.)
            Vector2 origin = squareOrigin(ref path, low, high, last, distance, kind is SquarePatterns.Square or SquarePatterns.Diamond ? 4 : 1);

            int? pendingHalf = null;
            int n = 0;


            for (int i = start; i < end; i++)
            {
                if (holdHalf(result, times, i, last, ref pendingHalf))
                    continue;

                var position = clamp(origin + path[Math.Min(n, path.Count - 1)]);

                placeSquareHalf(result, ref pendingHalf, last, position, ref bend);

                result.Add(new HitCircle
                {
                    StartTime = times[i].time,
                    Position = position,
                    // (A combo per square, or per two rungs.)
                    NewCombo = n % 4 == 0,
                    Samples = RhythmExtractor.PlainSampleNear(original, times[i].time),
                });

                last = position;
                n++;
            }

            finishSquareHalf(result, ref pendingHalf, last, distance);
        }

        /// <summary>
        /// Where a path goes: within the playfield, its first point a jump from the last note along one of the eight ways
        /// (the path mirrored if that fits better, which keeps its angles), otherwise as near to a jump away as fits.
        /// FIX: going only as it was made, one figure in twenty started off at some other angle.
        /// </summary>
        private Vector2 squareOrigin(ref List<Vector2> path, Vector2 low, Vector2 high, Vector2? last, float distance, int starts)
        {
            var given = path;
            // (Round after round the same: from corner k, the k-th corner on. FIX: turned as a whole, a path not of whole rounds
            // had its last corner twice where its end met its start.)
            int period = Math.Max(1, Math.Min(starts, given.Count));
            var startsAt = Enumerable.Range(0, period).OrderBy(_ => rng.Next())
                                     .Select(k => k == 0 ? given : Enumerable.Range(0, given.Count).Select(j => given[(j + k) % period]).ToList()).ToList();
            var mirrors = new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) }.OrderBy(_ => rng.Next()).ToArray();

            (Vector2 lowest, Vector2 highest) bounds(List<Vector2> p)
                => (low - new Vector2(p.Min(q => q.X), p.Min(q => q.Y)), high - new Vector2(p.Max(q => q.X), p.Max(q => q.Y)));

            static bool fits(Vector2 o, (Vector2 lowest, Vector2 highest) b)
                => o.X >= b.lowest.X - 0.5f && o.X <= b.highest.X + 0.5f && o.Y >= b.lowest.Y - 0.5f && o.Y <= b.highest.Y + 0.5f;

            Vector2 anywhere((Vector2 lowest, Vector2 highest) b) => new Vector2(
                b.lowest.X > b.highest.X ? (b.lowest.X + b.highest.X) / 2 : b.lowest.X + (float)rng.NextDouble() * (b.highest.X - b.lowest.X),
                b.lowest.Y > b.highest.Y ? (b.lowest.Y + b.highest.Y) / 2 : b.lowest.Y + (float)rng.NextDouble() * (b.highest.Y - b.lowest.Y));

            if (last is not Vector2 from)
                return anywhere(bounds(path));

            // The jump as long as asked first, then a little shorter or longer, before giving up the angle.
            foreach (float length in new[] { 1, 0.85f, 1.15f, 0.7f, 1.3f, 0.55f, 1.5f, 0.4f })
            {
                foreach (var (startAt, mirror) in startsAt.SelectMany(s => mirrors.Select(m => (s, m))))
                {
                    var mirrored = startAt.Select(p => p * mirror).ToList();
                    var b = bounds(mirrored);

                    foreach (var w in eight_ways.OrderBy(_ => rng.Next()))
                    {
                        var origin = from + w * distance * length - mirrored[0];

                        if (fits(origin, b))
                        {
                            path = mirrored;
                            return origin;
                        }
                    }
                }
            }

            // Too big to fit at all: smaller, so its points aren't pushed together at the playfield's edge.
            Vector2 size = new Vector2(path.Max(p => p.X) - path.Min(p => p.X), path.Max(p => p.Y) - path.Min(p => p.Y));
            float shrink = Math.Min(1, Math.Min((high.X - low.X) / Math.Max(1, size.X), (high.Y - low.Y) / Math.Max(1, size.Y)));

            if (shrink < 1)
                path = path.Select(p => p * shrink).ToList();

            var anyBounds = bounds(path);
            var first = path[0];
            return Enumerable.Range(0, 24).Select(_ => anywhere(anyBounds)).MinBy(o => Math.Abs(Vector2.Distance(o + first, from) - distance));
        }

        /// <summary>
        /// The corners of a square of <paramref name="side"/> around its middle, in order round it, upright or on a corner.
        /// </summary>
        private static Vector2[] corners(float side, bool onCorner, int way, float? reach = null)
        {
            float h = side / 2, r = reach ?? side / sqrt2;
            var points = onCorner
                ? new[] { new Vector2(0, -r), new Vector2(r, 0), new Vector2(0, r), new Vector2(-r, 0) }
                : new[] { new Vector2(-h, -h), new Vector2(h, -h), new Vector2(h, h), new Vector2(-h, h) };

            return way > 0 ? points : points.Reverse().ToArray();
        }

        /// <summary>
        /// Round and round a square, changing from round to round as <paramref name="kind"/> does (around the middle at 0, 0).
        /// </summary>
        private List<Vector2> squarePath(SquarePatterns kind, int notes, int rounds, float side)
        {
            var path = new List<Vector2>();
            int way = rng.NextDouble() < 0.5 ? 1 : -1;
            int corner = rng.Next(4);
            bool onCorner = kind == SquarePatterns.Diamond || (kind == SquarePatterns.Turning && rng.NextDouble() < 0.5);

            Vector2 centre = Vector2.Zero;
            // Creeping: which way off the closing side the square goes each round (see below).
            int creepSide = rng.NextDouble() < 0.5 ? 1 : -1;
            // Side by side: a side along the playfield's sides each round.
            Vector2 step = eight_ways[rng.Next(4) * 2] * side;
            // The middles may spread over the playfield less the square's own size, so the whole path fits.
            float extent = side * (kind == SquarePatterns.Turning ? 2 : kind == SquarePatterns.Growing ? 1.25f : 1);
            Vector2 span = OsuPlayfield.BASE_SIZE - new Vector2(2 * margin + 8 + extent);
            Vector2 lowest = centre, highest = centre;
            Vector2[]? previousSquare = null;
            Vector2? anchor = null;

            bool within(Vector2 c) => Math.Max(highest.X, c.X) - Math.Min(lowest.X, c.X) <= span.X && Math.Max(highest.Y, c.Y) - Math.Min(lowest.Y, c.Y) <= span.Y;

            for (int round = 0; path.Count < notes; round++)
            {
                float scale = kind == SquarePatterns.Growing && rounds > 1 ? 0.75f + 0.5f * round / (rounds - 1) : 1;
                bool thisCorner = kind == SquarePatterns.Turning ? onCorner ^ (round % 2 == 1) : onCorner;
                // Square and diamond by turns: the diamond's corners a side from the middle, so from one to the other is a jump at 45 degrees.
                float? reach = kind == SquarePatterns.Turning ? side : null;
                bool keepStart = false;

                if (previousSquare != null && kind == SquarePatterns.Sliding)
                {
                    // The square moves so the jump on to its next round goes along the diagonal: half its closing side, turned 45 degrees
                    // (FIX: moved any other way, that jump went off at some angle). Back from the edges; where neither way fits, it stays.
                    Vector2 closing = previousSquare[corner] - path[^1];
                    Vector2 across = new Vector2(-closing.Y, closing.X);

                    foreach (int s in new[] { creepSide, -creepSide })
                    {
                        var creep = (across * s - closing) / 2;

                        if (within(centre + creep))
                        {
                            creepSide = s;
                            centre += creep;
                            keepStart = true;
                            break;
                        }
                    }
                }

                if (previousSquare != null && kind == SquarePatterns.Grid)
                {
                    // On along the same side, back, or the other way round.
                    var next = new[] { step, -step, new Vector2(-step.Y, step.X), new Vector2(step.Y, -step.X) }.FirstOrDefault(s => within(centre + s));

                    if (next != Vector2.Zero)
                    {
                        step = next;
                        centre += step;
                    }
                }

                var offsets = corners(side * scale, thisCorner, way, reach);

                // Growing from the corner each round starts at, rather than its middle: on to the next round along a side.
                if (kind == SquarePatterns.Growing)
                {
                    anchor ??= offsets[corner];
                    centre = anchor.Value - offsets[corner];
                    keepStart = true;
                }

                lowest = Vector2.ComponentMin(lowest, centre);
                highest = Vector2.ComponentMax(highest, centre);

                var square = offsets.Select(c => centre + c).ToArray();

                if (previousSquare != null && !keepStart)
                    corner = nextStart(square, path[^1], corner, side * scale * (kind == SquarePatterns.Turning ? 1 / sqrt2 : 1));

                for (int k = 0; k < 4 && path.Count < notes; k++)
                    path.Add(square[(corner + k) % 4]);

                previousSquare = square;
            }

            return path;
        }

        /// <summary>
        /// The corner a round starts from: a jump from <paramref name="previous"/> along one of the eight ways, as near <paramref name="wanted"/> as can be,
        /// going on round the square (<paramref name="preferred"/>) if that one is.
        /// </summary>
        private static int nextStart(Vector2[] square, Vector2 previous, int preferred, float wanted)
        {
            bool alongAWay(int c)
            {
                var d = square[c] - previous;

                if (d.Length < wanted * 0.3f)
                    return false;

                double angle = Math.Atan2(d.Y, d.X) / (Math.PI / 4);
                return Math.Abs(angle - Math.Round(angle)) * 45 < 0.5;
            }

            float off(int c) => Math.Abs(Vector2.Distance(square[c], previous) - wanted);

            if (alongAWay(preferred) && off(preferred) < wanted * 0.5f)
                return preferred;

            var along = Enumerable.Range(0, 4).Where(alongAWay).ToList();

            if (along.Count > 0)
                return along.MinBy(off);

            return Enumerable.Range(0, 4).Where(c => Vector2.Distance(square[c], previous) > wanted * 0.3f).DefaultIfEmpty(preferred).MinBy(off);
        }

        /// <summary>
        /// A ladder: across a rung (a side), then along the diagonal to the start of the next rung (a side further), up and back.
        /// Every jump along a side or at 45 degrees. Sideways or upright.
        /// </summary>
        private List<Vector2> ladderPath(int notes, float side)
        {
            bool upright = rng.NextDouble() < 0.3;
            float across = upright ? OsuPlayfield.BASE_SIZE.X : OsuPlayfield.BASE_SIZE.Y;
            int rungs = Math.Max(2, Math.Min(4, (int)((across - 2 * margin - 8) / side) + 1));
            float xSign = rng.NextDouble() < 0.5 ? 1 : -1, ySign = rng.NextDouble() < 0.5 ? 1 : -1;

            var path = new List<Vector2>();
            int rung = 0, dir = 1;

            while (path.Count < notes)
            {
                for (int k = 0; k < 2 && path.Count < notes; k++)
                {
                    var point = new Vector2(k * side * xSign, rung * side * ySign);
                    path.Add(upright ? new Vector2(point.Y, point.X) : point);
                }

                if (rung + dir < 0 || rung + dir >= rungs)
                    dir = -dir;
                rung += dir;
            }

            return path;
        }

        /// <summary>
        /// The half beat waiting goes off the jump by half of it to one side (a right angle, two jumps at 45 degrees), the other side
        /// where that's off the playfield, or halfway along the jump where both are (FIX: pushed back in at the edge, its angles were lost).
        /// </summary>
        private void placeSquareHalf(List<OsuHitObject> result, ref int? pending, Vector2? last, Vector2 next, ref float bend)
        {
            if (pending is not int index || last is not Vector2 from)
                return;

            Vector2 way = next - from;
            Vector2 at(float b) => from + way * 0.5f + new Vector2(-way.Y, way.X) * b;
            bool inside(Vector2 p) => p == clamp(p);

            float chosen = new[] { bend, -bend }.FirstOrDefault(b => inside(at(b)));
            if (chosen != 0)
                bend = chosen;

            result[index].Position = clamp(at(chosen));
            pending = null;
        }

        /// <summary>
        /// A half beat left at the end of a figure: half a jump on from the last note, along the one of the eight ways nearest the middle of the playfield.
        /// </summary>
        private void finishSquareHalf(List<OsuHitObject> result, ref int? pending, Vector2? last, float distance)
        {
            if (pending is not int index || last is not Vector2 from)
                return;

            Vector2 toMiddle = OsuPlayfield.BASE_SIZE / 2 - from;
            var way = toMiddle.LengthSquared < 1 ? eight_ways[0] : eight_ways.MaxBy(w => Vector2.Dot(w, toMiddle));
            result[index].Position = clamp(from + way * distance * 0.5f);
            pending = null;
        }
    }
}
