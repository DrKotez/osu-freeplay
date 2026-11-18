// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Generation
{
    /// <summary>
    /// Builds the relative positions of objects in a jump pattern, where consecutive objects are a given distance apart.
    /// </summary>
    public static class PatternShapes
    {
        /// <summary>
        /// The natural number of objects for one pass of a shape, used when choosing how long a pattern runs.
        /// </summary>
        public static int CycleLength(JumpPatterns pattern, int polygonSides)
        {
            switch (pattern)
            {
                case JumpPatterns.Squares:
                    return 4;

                case JumpPatterns.Triangles:
                    return 3;

                case JumpPatterns.Stars:
                    return 5;

                case JumpPatterns.Polygons:
                    return polygonSides;

                default:
                    return 4;
            }
        }

        /// <param name="pattern">A single pattern flag.</param>
        /// <param name="count">How many offsets to create.</param>
        /// <param name="spacing">The distance between consecutive objects.</param>
        /// <param name="angle">The base rotation of the shape.</param>
        /// <param name="rotationPerCycle">How far a repeating shape rotates after each pass (radians).</param>
        /// <param name="polygonSides">The number of sides used for <see cref="JumpPatterns.Polygons"/>.</param>
        /// <param name="rng">Used by shapes which include randomness.</param>
        public static List<Vector2> Create(JumpPatterns pattern, int count, float spacing, float angle, float rotationPerCycle, int polygonSides, Random rng)
        {
            switch (pattern)
            {
                case JumpPatterns.Squares:
                    return polygon(4, 1, count, spacing, angle, rotationPerCycle);

                case JumpPatterns.Triangles:
                    return polygon(3, 1, count, spacing, angle, rotationPerCycle);

                case JumpPatterns.Stars:
                    // Visiting every second vertex of a pentagon draws a pentagram.
                    return polygon(5, 2, count, spacing, angle, rotationPerCycle);

                case JumpPatterns.Polygons:
                    return polygon(polygonSides, 1, count, spacing, angle, rotationPerCycle);

                case JumpPatterns.PingPong:
                    return pingPong(count, spacing, angle, rotationPerCycle);

                case JumpPatterns.Zigzags:
                    return zigzag(count, spacing, angle);

                case JumpPatterns.Spirals:
                    return spiral(count, spacing, angle, rotationPerCycle >= 0 ? 1 : -1);

                case JumpPatterns.Crosses:
                    return cross(count, spacing, angle, rotationPerCycle);

                case JumpPatterns.Stacks:
                    return stacks(count, spacing, angle, rng);

                default:
                    return randomJumps(count, spacing, angle, rng);
            }
        }

        /// <summary>
        /// Visits the vertices of a regular polygon, <paramref name="step"/> vertices at a time.
        /// </summary>
        private static List<Vector2> polygon(int sides, int step, int count, float spacing, float angle, float rotationPerCycle)
        {
            // Chord between vertices `step` apart on a circle of radius R is 2R·sin(π·step/n).
            float radius = spacing / (2 * MathF.Sin(MathF.PI * step / sides));

            var result = new List<Vector2>(count);

            for (int i = 0; i < count; i++)
            {
                int cycle = i / sides;
                int vertex = i * step % sides;

                float vertexAngle = angle + cycle * rotationPerCycle + MathF.Tau * vertex / sides;
                result.Add(polar(vertexAngle, radius));
            }

            return result;
        }

        /// <summary>
        /// Alternates between two sides, stepping sideways each time like a ladder.
        /// </summary>
        private static List<Vector2> pingPong(int count, float spacing, float angle, float rotationPerCycle)
        {
            var result = new List<Vector2>(count);

            // Each jump crosses the ladder and steps a little along it, so consecutive objects stay `spacing` apart.
            float step = spacing * 0.25f;
            float width = MathF.Sqrt(spacing * spacing - step * step);

            // Only a slight lean, so that one side never folds back onto itself.
            Vector2 across = polar(angle, width / 2);
            Vector2 along = polar(angle + MathF.PI / 2 + Math.Clamp(rotationPerCycle, -0.3f, 0.3f), step);

            for (int i = 0; i < count; i++)
                result.Add(along * i + (i % 2 == 0 ? -across : across));

            return result;
        }

        /// <summary>
        /// Alternates either side of a line which advances in one direction.
        /// </summary>
        private static List<Vector2> zigzag(int count, float spacing, float angle)
        {
            var result = new List<Vector2>(count);

            float advance = spacing * 0.35f;
            float width = MathF.Sqrt(spacing * spacing - advance * advance);

            Vector2 forward = polar(angle, advance);
            Vector2 lateral = polar(angle + MathF.PI / 2, width / 2);

            for (int i = 0; i < count; i++)
                result.Add(forward * i + (i % 2 == 0 ? -lateral : lateral));

            return result;
        }

        /// <summary>
        /// Winds outward from the centre, each object a fixed distance from the last.
        /// </summary>
        private static List<Vector2> spiral(int count, float spacing, float angle, int turnDirection)
        {
            var result = new List<Vector2>(count);

            float radius = spacing * 0.6f;
            float theta = angle;

            for (int i = 0; i < count; i++)
            {
                result.Add(polar(theta, radius));

                // Step along the circle by the angle whose chord is `spacing`, then widen.
                float step = 2 * MathF.Asin(Math.Min(1, spacing / (2 * radius)));
                theta += step * turnDirection;
                radius += spacing * 0.18f;
            }

            return result;
        }

        /// <summary>
        /// Crosses between the corners of a square diagonally, drawing an X (bowtie) shape.
        /// </summary>
        private static List<Vector2> cross(int count, float spacing, float angle, float rotationPerCycle)
        {
            var result = new List<Vector2>(count);

            // Diagonals (the long jumps) are `spacing`; the sides between them are shorter.
            float radius = spacing / 2;
            int[] order = { 0, 2, 1, 3 };

            for (int i = 0; i < count; i++)
            {
                int cycle = i / 4;
                float vertexAngle = angle + cycle * rotationPerCycle + MathF.Tau * order[i % 4] / 4;
                result.Add(polar(vertexAngle, radius));
            }

            return result;
        }

        /// <summary>
        /// Groups of two or three objects on the same spot, with jumps between groups.
        /// </summary>
        private static List<Vector2> stacks(int count, float spacing, float angle, Random rng)
        {
            var result = new List<Vector2>(count);

            Vector2 position = Vector2.Zero;
            float direction = angle;
            int groupSize = rng.Next(2, 4);

            for (int i = 0; i < count; i++)
            {
                if (i > 0 && i % groupSize == 0)
                {
                    direction += (float)(1.6 + rng.NextDouble() * 1.2) * (rng.NextDouble() < 0.5 ? -1 : 1);
                    position += polar(direction, spacing * 1.2f);
                }

                result.Add(position);
            }

            return result;
        }

        /// <summary>
        /// Sharp back-and-forth jumps with irregular angles.
        /// </summary>
        private static List<Vector2> randomJumps(int count, float spacing, float angle, Random rng)
        {
            var result = new List<Vector2>(count);

            Vector2 position = Vector2.Zero;
            float direction = angle;

            for (int i = 0; i < count; i++)
            {
                result.Add(position);

                direction += (float)(1.9 + rng.NextDouble() * 0.9) * (rng.NextDouble() < 0.5 ? -1 : 1);
                position += polar(direction, spacing * (float)(0.85 + rng.NextDouble() * 0.3));
            }

            return result;
        }

        private static Vector2 polar(float angle, float length) => new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length;
    }
}
