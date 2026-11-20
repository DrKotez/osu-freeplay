// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Generation
{
    /// <summary>
    /// Every pattern the generator makes up itself, in one place: the stars mode's figures (<see cref="Figures"/>), the jump shapes of the other styles
    /// (<see cref="Jumps"/>) and the shapes streams wind in (<see cref="Streams"/>). (A mapper's own patterns come from <see cref="MinedPatternSet"/>.)
    /// </summary>
    /// <remarks>
    /// <para>To add a figure to the stars mode: a value with its name (<c>[Description]</c>) in <see cref="MetronomeShapes"/>, then one line here.
    /// A figure that is just points gone round in some order needs no code: <see cref="Figure.Regular"/> for points round a circle, or its points
    /// (within a radius of one) and their order.</para>
    /// <para>To add a jump shape: a value in <see cref="JumpPatterns"/>, then one line here, with a <see cref="JumpShape.Weight"/> for the styles
    /// that don't list it. Any stars mode figure can be one too (<see cref="JumpShape.FromFigure"/>).</para>
    /// <para>The settings' checkboxes come from the enums, and a new value is turned on for players who saved their choice before it existed
    /// (see <see cref="FreeplayGenerationSettings"/>). <c>PatternLibraryTest</c> checks every pattern here: on the playfield, the jumps it's asked for.</para>
    /// </remarks>
    public static class PatternLibrary
    {
        #region The stars mode

        /// <summary>
        /// The stars mode's figures, gone round in turn (see <see cref="MetronomeGenerator"/>). The order is the generator's: changing it changes the maps.
        /// </summary>
        public static IReadOnlyList<Figure> Figures { get; } = new[]
        {
            Figure.Regular(MetronomeShapes.Star, 5, 2, 3) with { Morphs = true },
            Figure.Regular(MetronomeShapes.BackAndForth, 2, 1, 0.5) with { Calm = true },
            Figure.Regular(MetronomeShapes.Triangle, 3, 1, 1.2) with { Calm = true },
            Figure.Regular(MetronomeShapes.Square, 4, 1, 1.2) with { Calm = true },
            new Figure(MetronomeShapes.CrossedSquare, Figure.Regular(MetronomeShapes.Square, 4, 1, 0).Vertices, new[] { 0, 2, 1, 3 }, 1),
            Figure.Regular(MetronomeShapes.Pentagon, 5, 1, 0.4) with { Calm = true, Morphs = true },
            Figure.Regular(MetronomeShapes.Hexagon, 6, 1, 0.2) with { Calm = true },
            Figure.Regular(MetronomeShapes.SevenPointStar, 7, 3, 0.8, minStars: 5) with { Morphs = true },
            new Figure(MetronomeShapes.StarOfDavid, Figure.Regular(MetronomeShapes.Hexagon, 6, 1, 0).Vertices, new[] { 0, 2, 4, 1, 3, 5 }, 0.3, MinStars: 4.5),
            Figure.Regular(MetronomeShapes.EightPointStar, 8, 3, 0.8, minStars: 5) with { Morphs = true },
            // A star whose points are alternately near and far, so its jumps go long, short, long.
            Figure.Regular(MetronomeShapes.UnevenStar, 5, 2, 1, radius: i => i % 2 == 0 ? 1 : 0.55f) with { Morphs = true },
            // Back and forth along a zigzag: across the playfield and back the same way.
            new Figure(MetronomeShapes.Zigzag,
                new[] { new Vector2(-1, -0.45f), new Vector2(-0.5f, 0.45f), new Vector2(0, -0.45f), new Vector2(0.5f, 0.45f), new Vector2(1, -0.45f) },
                new[] { 0, 1, 2, 3, 4, 3, 2, 1 }, 1),
            // Long sides and short ones in turn.
            new Figure(MetronomeShapes.Rectangle, new[] { new Vector2(-1, -0.5f), new Vector2(1, -0.5f), new Vector2(1, 0.5f), new Vector2(-1, 0.5f) }, new[] { 0, 1, 2, 3 }, 0.8)
                { Calm = true },
            // Two small triangles side by side, jumped between in turn: every jump crosses the playfield.
            new Figure(MetronomeShapes.TwinShapes,
                Enumerable.Range(0, 6).Select(i => new Vector2(i < 3 ? -0.6f : 0.6f, 0) + new Vector2(MathF.Cos(i % 3 * MathF.Tau / 3 + MathF.PI / 2), MathF.Sin(i % 3 * MathF.Tau / 3 + MathF.PI / 2)) * 0.4f)
                          .ToArray(),
                new[] { 0, 3, 1, 4, 2, 5 }, 1.2) { Wide = true },
            // Round and outwards, three eighths of a turn a jump: the jumps grow, then one leap back to the middle.
            new Figure(MetronomeShapes.Spiral,
                Enumerable.Range(0, 8).Select(i => new Vector2(MathF.Cos(i * MathF.PI * 3 / 4), MathF.Sin(i * MathF.PI * 3 / 4)) * (0.35f + 0.65f * i / 7)).ToArray(),
                Enumerable.Range(0, 8).ToArray(), 0.8),
            // Along a figure of eight (never through its middle, so no two notes meet there).
            new Figure(MetronomeShapes.FigureEight,
                Enumerable.Range(0, 8).Select(i => (i + 0.5f) * MathF.Tau / 8).Select(t => new Vector2(MathF.Sin(t), MathF.Sin(t) * MathF.Cos(t) * 1.3f)).ToArray(),
                Enumerable.Range(0, 8).ToArray(), 0.8),
            new Figure(MetronomeShapes.Spokes, Array.Empty<Vector2>(), Array.Empty<int>(), 1) { Wide = true, Make = spokes },
            new Figure(MetronomeShapes.RandomShape, Array.Empty<Vector2>(), Array.Empty<int>(), 2.5) { Make = randomShape },
            // Real jump patterns from the aim collection, laid on the grid (see MetronomeGenerator.placeMined).
            new Figure(MetronomeShapes.MapperPatterns, Array.Empty<Vector2>(), Array.Empty<int>(), 3) { FromMaps = true },
        };

        /// <summary>
        /// The stars mode's figure of <paramref name="kind"/>.
        /// </summary>
        public static Figure FigureOf(MetronomeShapes kind) => Figures.First(f => f.Kind == kind);

        /// <summary>
        /// Out from the middle and back, to each of three to five points in turn: every jump the same length, through one spot.
        /// </summary>
        private static Figure spokes(Figure figure, Random rng)
        {
            int rays = rng.Next(3, 6);
            var vertices = new[] { Vector2.Zero }.Concat(Enumerable.Range(0, rays).Select(i => new Vector2(MathF.Cos(i * MathF.Tau / rays), MathF.Sin(i * MathF.Tau / rays)))).ToArray();

            // Round the rays in order, or every other one (a star's way) with five.
            int step = rays == 5 && rng.NextDouble() < 0.5 ? 2 : 1;
            var order = Enumerable.Range(0, rays).SelectMany(i => new[] { 0, 1 + i * step % rays }).ToArray();

            return new Figure(MetronomeShapes.Spokes, vertices, order, figure.Weight) { Wide = figure.Wide };
        }

        /// <summary>
        /// A shape made up for this once: three to seven points at uneven angles and distances, gone round as a polygon, a star or in a jumbled order
        /// (with no jump between neighbouring points too short to be one). Every one is new, so it can't wear out.
        /// </summary>
        private static Figure randomShape(Figure figure, Random rng)
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
                    return new Figure(MetronomeShapes.RandomShape, vertices, order, figure.Weight);
            }
        }

        #endregion

        #region Jump shapes

        /// <summary>
        /// The jump shapes of the other styles (see <see cref="PatternGenerator"/>).
        /// </summary>
        public static IReadOnlyList<JumpShape> Jumps { get; } = new[]
        {
            new JumpShape(JumpPatterns.Squares, 0.8, (c, s, a, r, sides, rng) => PatternShapes.Polygon(4, 1, c, s, a, r)) { Cycle = _ => 4 },
            new JumpShape(JumpPatterns.Triangles, 0.3, (c, s, a, r, sides, rng) => PatternShapes.Polygon(3, 1, c, s, a, r)) { Cycle = _ => 3 },
            // Visiting every second vertex of a pentagon draws a pentagram.
            new JumpShape(JumpPatterns.Stars, 0.95, (c, s, a, r, sides, rng) => PatternShapes.Polygon(5, 2, c, s, a, r)) { Cycle = _ => 5 },
            new JumpShape(JumpPatterns.Polygons, 0.65, (c, s, a, r, sides, rng) => PatternShapes.Polygon(sides, 1, c, s, a, r)) { Cycle = sides => sides },
            new JumpShape(JumpPatterns.PingPong, 0.05, (c, s, a, r, sides, rng) => PatternShapes.PingPong(c, s, a, r)) { Open = true },
            new JumpShape(JumpPatterns.Zigzags, 0.2, (c, s, a, r, sides, rng) => PatternShapes.Zigzag(c, s, a)) { Open = true },
            new JumpShape(JumpPatterns.RandomJumps, 0.55, (c, s, a, r, sides, rng) => PatternShapes.RandomJumps(c, s, a, rng)) { Open = true },
            new JumpShape(JumpPatterns.Spirals, 0.6, (c, s, a, r, sides, rng) => PatternShapes.Spiral(c, s, a, r >= 0 ? 1 : -1)),
            new JumpShape(JumpPatterns.Crosses, 0.85, (c, s, a, r, sides, rng) => PatternShapes.Cross(c, s, a, r)),
            new JumpShape(JumpPatterns.Stacks, 0.45, (c, s, a, r, sides, rng) => PatternShapes.Stacks(c, s, a, rng)),
        };

        /// <summary>
        /// The jump shape of <paramref name="flag"/>, or null.
        /// </summary>
        public static JumpShape? JumpOf(JumpPatterns flag) => Jumps.FirstOrDefault(j => j.Flag == flag);

        #endregion

        #region Streams

        /// <summary>
        /// The shapes a stream winds in, a stretch at a time: how its direction turns at each note (see <see cref="StreamShape.Turn"/>).
        /// The order is the generator's: changing it changes the maps.
        /// </summary>
        public static IReadOnlyList<StreamShape> Streams { get; } = new[]
        {
            new StreamShape("Дуга", (i, length, curvature) => curvature),
            new StreamShape("S-кривая", (i, length, curvature) => i < length / 2 ? curvature : -curvature),
            // Straight runs with a right-angle turn every four notes.
            new StreamShape("Квадрат", (i, length, curvature) => i % 4 == 3 ? MathF.PI / 2 * MathF.Sign(curvature) : 0),
            new StreamShape("Треугольник", (i, length, curvature) => i % 5 == 4 ? MathF.Tau / 3 * MathF.Sign(curvature) : 0),
            new StreamShape("Зигзаг", (i, length, curvature) => (i % 2 == 0 ? 1 : -1) * 0.9f) { Zigzags = true },
            new StreamShape("Волна", (i, length, curvature) => MathF.Cos(i * 0.7f) * 0.35f * MathF.Sign(curvature)),
            // Tightening curve.
            new StreamShape("Спираль", (i, length, curvature) => curvature * (0.6f + i * 0.08f)),
            new StreamShape("Прямая", (i, length, curvature) => 0),
        };

        #endregion
    }

    /// <summary>
    /// A stars mode figure: <see cref="Vertices"/> (within a radius of one), visited in <see cref="Order"/> (a star visits every other point).
    /// <see cref="Edge"/> is the average jump between neighbours in the order, for a radius of one.
    /// </summary>
    /// <param name="Weight">How often it comes, against the others.</param>
    /// <param name="MinStars">Below these stars it comes only when it's all the player chose.</param>
    public sealed record Figure(MetronomeShapes Kind, Vector2[] Vertices, int[] Order, double Weight, double MinStars = 0)
    {
        public float Edge { get; } = Order.Length < 2 ? 1 : Enumerable.Range(0, Order.Length).Average(i => Vector2.Distance(Vertices[Order[(i + 1) % Order.Length]], Vertices[Order[i]]));

        /// <summary>
        /// A calm figure (a polygon, back and forth): more in verses, fewer in choruses.
        /// </summary>
        public bool Calm { get; init; }

        /// <summary>
        /// A round figure that can turn into its other way over the same points as it goes: a star into a polygon and back.
        /// </summary>
        public bool Morphs { get; init; }

        /// <summary>
        /// Spans too much of the playfield to go into a quarter being practised.
        /// </summary>
        public bool Wide { get; init; }

        /// <summary>
        /// Made up anew for each use (from this one and the generator's random); this one only stands for them when picking.
        /// </summary>
        public Func<Figure, Random, Figure>? Make { get; init; }

        /// <summary>
        /// Stands for the patterns of real maps (see <see cref="MinedPatternSet"/>), not a figure of its own.
        /// </summary>
        public bool FromMaps { get; init; }

        /// <summary>
        /// Points evenly around a circle, visited <paramref name="step"/> apart (2 for a five-point star).
        /// </summary>
        public static Figure Regular(MetronomeShapes kind, int points, int step, double weight, double minStars = 0, Func<int, float>? radius = null)
            => new Figure(kind,
                Enumerable.Range(0, points).Select(i => new Vector2(MathF.Cos(i * MathF.Tau / points), MathF.Sin(i * MathF.Tau / points)) * (radius?.Invoke(i) ?? 1)).ToArray(),
                Enumerable.Range(0, points).Select(i => i * step % points).ToArray(), weight, minStars);
    }

    /// <summary>
    /// Makes the offsets of a jump shape: <c>count</c> objects, <c>spacing</c> apart, turned by <c>angle</c>, each pass turned <c>rotationPerCycle</c> more,
    /// <c>sides</c> for the shapes of any number of sides.
    /// </summary>
    public delegate List<Vector2> JumpShapeMaker(int count, float spacing, float angle, float rotationPerCycle, int sides, Random rng);

    /// <summary>
    /// A jump shape of the styles other than the stars mode.
    /// </summary>
    /// <param name="Iq">How tricky it is, on the scale of <see cref="FreeplayGenerationSettings.PatternIq"/>: shapes far from the map's are rare.</param>
    public sealed record JumpShape(JumpPatterns Flag, double Iq, JumpShapeMaker Create)
    {
        /// <summary>
        /// How many objects make one pass, for <c>sides</c> sides (where it has any).
        /// </summary>
        public Func<int, int> Cycle { get; init; } = _ => 4;

        /// <summary>
        /// An open shape (it never comes back round): it runs four to eight objects instead of a pass or two.
        /// </summary>
        public bool Open { get; init; }

        /// <summary>
        /// How often it comes in a style that doesn't list it (they list the shapes made before this library, see PatternGenerator.StyleTuning).
        /// </summary>
        public double Weight { get; init; }

        /// <summary>
        /// A stars mode figure as a jump shape: its jumps <c>spacing</c> long on average, each pass turned a little more.
        /// </summary>
        public static JumpShape FromFigure(JumpPatterns flag, Figure figure, double iq, double weight)
            => new JumpShape(flag, iq, (count, spacing, angle, rotation, sides, rng) => PatternShapes.Closed(figure.Vertices, figure.Order, count, spacing, angle, rotation))
            {
                Cycle = _ => figure.Order.Length,
                Weight = weight,
            };
    }

    /// <summary>
    /// A shape a stream winds in.
    /// </summary>
    /// <param name="Turn">How far the stream turns (radians) at a note: from its place in the stretch, the stretch's length and how much it curves
    /// (a random 0.12–0.3 either way).</param>
    public sealed record StreamShape(string Name, Func<int, int, float, float> Turn)
    {
        /// <summary>
        /// Goes left and right of its line: the cursor's way isn't each note's.
        /// </summary>
        public bool Zigzags { get; init; }
    }
}
