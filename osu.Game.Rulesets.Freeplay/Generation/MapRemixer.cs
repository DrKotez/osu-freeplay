// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Generation
{
    /// <summary>
    /// Keeps a mapper's objects (timing, circles, sliders, spinners) but moves each combo somewhere new:
    /// every combo is rotated, possibly mirrored, and placed at the same distance from the previous combo as the mapper had it.
    /// Patterns inside a combo keep the mapper's shape, so nothing becomes awkward to play, just unfamiliar.
    /// </summary>
    public class MapRemixer
    {
        private const int attempts_per_combo = 24;

        private static readonly Vector2 playfield_centre = OsuPlayfield.BASE_SIZE / 2;

        private readonly Random rng;
        private readonly float margin;

        public MapRemixer(int seed, float circleRadius)
        {
            rng = new Random(seed);
            margin = circleRadius;
        }

        public void Remix(IReadOnlyList<OsuHitObject> objects)
        {
            var combos = splitCombos(objects);

            Vector2 originalPreviousEnd = playfield_centre;
            Vector2 previousEnd = playfield_centre;
            float previousDirection = randomAngle();

            foreach (var combo in combos)
            {
                if (combo.All(h => h is Spinner))
                    continue;

                var first = combo.First(h => h is not Spinner);
                var originalEnd = endPosition(combo.Last(h => h is not Spinner));

                float entryDistance = Vector2.Distance(first.Position, originalPreviousEnd);

                var transform = findTransform(combo, first.Position, previousEnd, previousDirection, entryDistance);

                foreach (var hitObject in combo)
                    apply(hitObject, transform);

                var last = combo.Last(h => h is not Spinner);
                Vector2 newEnd = endPosition(last);

                Vector2 travel = newEnd - previousEnd;
                if (travel.LengthSquared > 1)
                    previousDirection = MathF.Atan2(travel.Y, travel.X);

                previousEnd = newEnd;
                originalPreviousEnd = originalEnd;
            }
        }

        private Transform findTransform(List<OsuHitObject> combo, Vector2 origin, Vector2 previousEnd, float previousDirection, float entryDistance)
        {
            var shape = combo.Where(h => h is not Spinner).SelectMany(samplePoints).Select(p => p - origin).ToList();

            for (int attempt = 0; attempt < attempts_per_combo; attempt++)
            {
                // Vary how sharply the next combo turns away from the last, from flowing to doubling back.
                float turn = (float)(rng.NextDouble() * 2 - 1) * MathF.PI;

                var candidate = new Transform
                {
                    Origin = origin,
                    Angle = randomAngle(),
                    Mirror = rng.NextDouble() < 0.5,
                    Target = previousEnd + new Vector2(MathF.Cos(previousDirection + turn), MathF.Sin(previousDirection + turn)) * entryDistance,
                };

                if (shape.All(p => inBounds(candidate.Target + candidate.Rotate(p))))
                    return candidate;
            }

            // Nothing fitted: leave the combo where the mapper put it, which is always valid.
            return new Transform { Origin = origin, Target = origin };
        }

        private static void apply(OsuHitObject hitObject, Transform transform)
        {
            if (hitObject is Spinner)
                return;

            hitObject.Position = transform.Target + transform.Rotate(hitObject.Position - transform.Origin);

            if (hitObject is Slider slider)
            {
                var controlPoints = slider.Path.ControlPoints.Select(c => new PathControlPoint(transform.Rotate(c.Position), c.Type)).ToArray();
                slider.Path = new SliderPath(controlPoints, slider.Path.ExpectedDistance.Value);
            }
        }

        /// <summary>
        /// Points which must stay inside the playfield: object heads, and points along slider bodies.
        /// </summary>
        private static IEnumerable<Vector2> samplePoints(OsuHitObject hitObject)
        {
            yield return hitObject.Position;

            if (hitObject is Slider slider)
            {
                for (int i = 1; i <= 10; i++)
                    yield return slider.Position + slider.Path.PositionAt(i / 10.0);
            }
        }

        private static Vector2 endPosition(OsuHitObject hitObject)
            => hitObject is Slider slider ? slider.Position + slider.Path.PositionAt(slider.RepeatCount % 2 == 0 ? 1 : 0) : hitObject.Position;

        private static List<List<OsuHitObject>> splitCombos(IReadOnlyList<OsuHitObject> objects)
        {
            var combos = new List<List<OsuHitObject>>();

            foreach (var hitObject in objects)
            {
                if (combos.Count == 0 || hitObject.NewCombo || hitObject is Spinner || combos[^1].LastOrDefault() is Spinner)
                    combos.Add(new List<OsuHitObject>());

                combos[^1].Add(hitObject);
            }

            return combos;
        }

        private bool inBounds(Vector2 position)
            => position.X >= margin && position.Y >= margin && position.X <= OsuPlayfield.BASE_SIZE.X - margin && position.Y <= OsuPlayfield.BASE_SIZE.Y - margin;

        private float randomAngle() => (float)(rng.NextDouble() * MathF.Tau);

        private struct Transform
        {
            public Vector2 Origin;
            public Vector2 Target;
            public float Angle;
            public bool Mirror;

            public Vector2 Rotate(Vector2 offset)
            {
                if (Mirror)
                    offset.X = -offset.X;

                float cos = MathF.Cos(Angle), sin = MathF.Sin(Angle);
                return new Vector2(offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos);
            }
        }
    }
}
