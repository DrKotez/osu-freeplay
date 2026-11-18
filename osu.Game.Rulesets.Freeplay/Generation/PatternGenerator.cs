// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Audio;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Generation
{
    /// <summary>
    /// Turns <see cref="RhythmPoint"/>s into playable osu! hit objects, according to a <see cref="DifficultyProfile"/> and <see cref="PatternStyle"/>.
    /// </summary>
    public class PatternGenerator
    {
        private static readonly Vector2 playfield_centre = OsuPlayfield.BASE_SIZE / 2;

        private static readonly PatternStyle[] mixable_styles = { PatternStyle.Jumps, PatternStyle.Streams, PatternStyle.Flow, PatternStyle.Sliders };

        private readonly ControlPointInfo controlPoints;
        private readonly DifficultyProfile profile;
        private readonly PatternStyle requestedStyle;
        private readonly Random rng;
        private readonly float margin;

        private PatternStyle style;
        private StyleTuning tuning = null!;
        private double styleEndTime = double.MinValue;
        private bool styleChanged;

        private Vector2 cursor = playfield_centre;
        private float direction;
        private double lastStartTime = double.MinValue;
        private double lastEndTime = double.MinValue;
        private bool lastWasSpinner;
        private int burstGaps;

        private int comboLength;
        private long comboMeasure = long.MinValue;

        public PatternGenerator(ControlPointInfo controlPoints, DifficultyProfile profile, PatternStyle style, int seed)
        {
            this.controlPoints = controlPoints;
            this.profile = profile;
            requestedStyle = style;
            rng = new Random(seed);

            float scale = LegacyRulesetExtensions.CalculateScaleFromCircleSize(profile.CircleSize, true);
            margin = OsuHitObject.OBJECT_RADIUS * scale;

            direction = randomAngle();
        }

        public List<OsuHitObject> Generate(IReadOnlyList<RhythmPoint> points, CancellationToken cancellationToken = default)
        {
            var result = new List<OsuHitObject>();

            for (int i = 0; i < points.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var point = points[i];
                var timingPoint = controlPoints.TimingPointAt(point.Time);
                double beatLength = timingPoint.BeatLength;

                updateStyle(point.Time, beatLength);

                if (!tryAccept(point, beatLength, out bool isBurstGap))
                    continue;

                double available = timeUntilNextPlayable(points, i, beatLength) - point.Time;

                OsuHitObject hitObject;

                // Slider end times can't be read back until defaults are applied, so they are tracked here.
                double endTime = point.Time;

                if (point.IsSpinner && point.HoldEndTime - point.Time >= Math.Max(1500, beatLength * 3))
                {
                    hitObject = createSpinner(point);
                    endTime = point.HoldEndTime!.Value;
                }
                else if (shouldCreateSlider(point, available, beatLength, out double duration))
                {
                    hitObject = createSlider(point, duration, beatLength, isBurstGap);
                    endTime = point.Time + duration;
                }
                else
                    hitObject = createCircle(point, isBurstGap);

                applyCombo(hitObject, timingPoint);

                lastStartTime = point.Time;
                lastEndTime = endTime;
                lastWasSpinner = hitObject is Spinner;
                burstGaps = isBurstGap ? burstGaps + 1 : 0;

                result.Add(hitObject);
            }

            return result;
        }

        #region Rhythm

        private void updateStyle(double time, double beatLength)
        {
            if (requestedStyle != PatternStyle.Mixed)
            {
                if (tuning == null)
                    setStyle(requestedStyle);
                return;
            }

            if (time < styleEndTime)
                return;

            var candidates = mixable_styles.Where(s => s != style || tuning == null).ToArray();
            setStyle(candidates[rng.Next(candidates.Length)]);

            // Two or four measures.
            styleEndTime = time + beatLength * 4 * (rng.NextDouble() < 0.5 ? 2 : 4);
            styleChanged = true;
        }

        private void setStyle(PatternStyle newStyle)
        {
            style = newStyle;
            tuning = StyleTuning.For(newStyle, profile);
        }

        /// <summary>
        /// Decides whether a point is kept, enforcing minimum gaps and stream length limits.
        /// </summary>
        private bool tryAccept(RhythmPoint point, double beatLength, out bool isBurstGap)
        {
            isBurstGap = false;

            if (lastStartTime == double.MinValue)
                return true;

            // Leave some recovery time after held objects.
            double gapFromEnd = point.Time - lastEndTime;
            double requiredAfterEnd = lastWasSpinner ? beatLength : lastEndTime > lastStartTime ? Math.Max(profile.MinGapMs, beatLength * (profile.Stars < 3.5 ? 0.5 : 0.25)) : 0;

            if (gapFromEnd < requiredAfterEnd - 1)
                return false;

            double gap = point.Time - lastStartTime;
            double gapBeats = gap / beatLength;

            isBurstGap = gapBeats < 0.375;

            if (isBurstGap)
                return gap >= profile.MinGapMs && burstGaps < tuning.MaxBurstGaps;

            return gapBeats >= tuning.MinGapBeats * 0.95;
        }

        /// <summary>
        /// Estimates when the next object could be placed, which bounds how long a generated slider may be.
        /// </summary>
        private double timeUntilNextPlayable(IReadOnlyList<RhythmPoint> points, int index, double beatLength)
        {
            double minGap = Math.Max(profile.MinGapMs, beatLength * tuning.MinGapBeats);

            for (int j = index + 1; j < points.Count; j++)
            {
                if (points[j].Time - points[index].Time >= minGap * 0.95)
                    return points[j].Time;
            }

            return points[index].Time + beatLength * 4;
        }

        #endregion

        #region Objects

        private bool shouldCreateSlider(RhythmPoint point, double available, double beatLength, out double duration)
        {
            duration = 0;

            if (profile.Stars < 1.5 && point.HoldEndTime == null)
                return false;

            double holdDuration = (point.HoldEndTime ?? point.Time) - point.Time;

            if (holdDuration >= beatLength * 0.45 && rng.NextDouble() < tuning.KeepHoldChance)
            {
                duration = Math.Min(holdDuration, beatLength * 4);
                return true;
            }

            // Generate a slider into a gap, leaving at least half a beat before the next object.
            int maxHalfBeats = (int)Math.Floor((available - beatLength * 0.5) / (beatLength / 2));

            if (maxHalfBeats < 1 || rng.NextDouble() >= tuning.SliderChance)
                return false;

            duration = rng.Next(1, Math.Min(maxHalfBeats, 4) + 1) * beatLength / 2;
            return true;
        }

        private HitCircle createCircle(RhythmPoint point, bool isBurstGap)
        {
            Vector2 position = nextPosition(point.Time, isBurstGap);

            cursor = position;

            return new HitCircle
            {
                StartTime = point.Time,
                Position = position,
                Samples = samplesFor(point),
            };
        }

        private Slider createSlider(RhythmPoint point, double duration, double beatLength, bool isBurstGap)
        {
            Vector2 position = nextPosition(point.Time, isBurstGap);

            int spans = 1;

            if (duration >= beatLength * 0.95 && rng.NextDouble() < tuning.RepeatChance)
                spans = duration >= beatLength * 1.45 && rng.NextDouble() < 0.3 ? 3 : 2;

            // Matches Slider.Velocity with a slider velocity multiplier of 1.
            double velocity = 100 * profile.SliderMultiplier / beatLength;
            double spanLength = velocity * duration / spans;

            while (spanLength > 260 && spans < 6)
                spanLength = velocity * duration / ++spans;

            var (path, endOffset, travelAngle) = createPath(position, spanLength);

            bool endsAtTail = spans % 2 == 1;
            cursor = endsAtTail ? position + endOffset : position;
            direction = endsAtTail ? travelAngle : travelAngle + MathF.PI;

            var samples = samplesFor(point);

            return new Slider
            {
                StartTime = point.Time,
                Position = position,
                Path = path,
                RepeatCount = spans - 1,
                Samples = samples,
                NodeSamples = Enumerable.Range(0, spans + 1).Select(_ => (IList<HitSampleInfo>)samples.ToList()).ToList(),
            };
        }

        private Spinner createSpinner(RhythmPoint point)
        {
            cursor = playfield_centre;
            direction = randomAngle();

            return new Spinner
            {
                StartTime = point.Time,
                EndTime = point.HoldEndTime!.Value,
                Position = playfield_centre,
                NewCombo = true,
                Samples = samplesFor(point),
            };
        }

        private (SliderPath path, Vector2 endOffset, float angle) createPath(Vector2 start, double length)
        {
            float len = (float)length;
            bool curved = len >= 40 && rng.NextDouble() < tuning.CurveChance;

            // Bend is the sagitta as a fraction of the chord. Arc length ≈ chord * (1 + 8k²/3).
            float bend = curved ? (float)(0.15 + rng.NextDouble() * 0.2) * (rng.NextDouble() < 0.5 ? -1 : 1) : 0;
            float chord = len / (1 + 8 * bend * bend / 3);

            float baseAngle = direction + randomRange(-tuning.SliderTurn, tuning.SliderTurn);
            float angle = findAngle(baseAngle, a => inBounds(start + polar(a, chord)));

            Vector2 along = polar(angle, 1);
            Vector2 perpendicular = new Vector2(-along.Y, along.X);
            Vector2 end = along * chord;

            PathControlPoint[] controlPoints = curved
                ? new[]
                {
                    new PathControlPoint(Vector2.Zero, PathType.PERFECT_CURVE),
                    new PathControlPoint(along * chord / 2 + perpendicular * chord * bend),
                    new PathControlPoint(end),
                }
                : new[]
                {
                    new PathControlPoint(Vector2.Zero, PathType.LINEAR),
                    new PathControlPoint(end),
                };

            return (new SliderPath(controlPoints, length), end, angle);
        }

        private Vector2 nextPosition(double time, bool isBurstGap)
        {
            if (lastStartTime == double.MinValue || lastWasSpinner)
                return playfield_centre;

            double gap = time - lastEndTime;

            double distance = isBurstGap
                ? profile.StreamSpacing * randomRange(0.9f, 1.1f)
                : Math.Min(profile.MaxJumpDistance, profile.AimVelocity * gap) * randomRange(0.85f, 1.15f);

            distance *= tuning.DistanceMultiplier;

            // Long rests don't need to be followed by huge jumps.
            if (gap > controlPoints.TimingPointAt(time).BeatLength * 2)
                distance = Math.Min(distance, 200);

            float turn = isBurstGap ? randomRange(-tuning.StreamTurn, tuning.StreamTurn) : nextTurn();

            float dist = (float)distance;
            direction = findAngle(direction + turn, a => inBounds(cursor + polar(a, dist)));

            Vector2 position = cursor + polar(direction, dist);

            return Vector2.Clamp(position, new Vector2(margin), OsuPlayfield.BASE_SIZE - new Vector2(margin));
        }

        private float nextTurn()
        {
            switch (tuning.TurnMode)
            {
                case TurnMode.Sharp:
                    return randomRange(1.9f, 2.6f) * (rng.NextDouble() < 0.5 ? -1 : 1);

                case TurnMode.Smooth:
                    return randomRange(-0.7f, 0.7f);

                default:
                    return randomRange(-1.6f, 1.6f);
            }
        }

        /// <summary>
        /// Searches outward from <paramref name="preferred"/> for an angle satisfying <paramref name="valid"/>,
        /// falling back to aiming at the playfield centre.
        /// </summary>
        private float findAngle(float preferred, Func<float, bool> valid)
        {
            for (int i = 0; i < 18; i++)
            {
                float offset = (i + 1) / 2 * 0.35f * (i % 2 == 0 ? 1 : -1);

                if (valid(preferred + offset))
                    return preferred + offset;
            }

            Vector2 toCentre = playfield_centre - cursor;
            return MathF.Atan2(toCentre.Y, toCentre.X);
        }

        private void applyCombo(OsuHitObject hitObject, TimingControlPoint timingPoint)
        {
            double measureLength = timingPoint.BeatLength * timingPoint.TimeSignature.Numerator;
            long measure = (long)Math.Floor((hitObject.StartTime - timingPoint.Time) / measureLength) + (long)timingPoint.Time * 1000;

            bool newCombo = comboLength == 0
                            || lastWasSpinner
                            || hitObject is Spinner
                            || styleChanged
                            || (comboLength >= 4 && measure != comboMeasure)
                            || comboLength >= 10;

            if (newCombo)
            {
                hitObject.NewCombo = true;
                comboLength = 0;
                comboMeasure = measure;
                styleChanged = false;
            }

            comboLength++;
        }

        private static IList<HitSampleInfo> samplesFor(RhythmPoint point)
            => point.Samples.Count > 0 ? point.Samples.ToList() : new List<HitSampleInfo> { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) };

        #endregion

        #region Helpers

        private bool inBounds(Vector2 position)
            => position.X >= margin && position.Y >= margin && position.X <= OsuPlayfield.BASE_SIZE.X - margin && position.Y <= OsuPlayfield.BASE_SIZE.Y - margin;

        private static Vector2 polar(float angle, float length) => new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length;

        private float randomRange(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        private float randomAngle() => randomRange(0, MathF.Tau);

        #endregion

        private enum TurnMode
        {
            Smooth,
            Sharp,
            Varied,
        }

        private class StyleTuning
        {
            public double MinGapBeats;
            public int MaxBurstGaps;
            public double DistanceMultiplier;
            public TurnMode TurnMode;
            public float StreamTurn;
            public float SliderTurn;
            public double SliderChance;
            public double KeepHoldChance;
            public double RepeatChance;
            public double CurveChance;

            public static StyleTuning For(PatternStyle style, DifficultyProfile profile)
            {
                switch (style)
                {
                    case PatternStyle.Jumps:
                        return new StyleTuning
                        {
                            MinGapBeats = Math.Max(profile.MinGapBeats, 0.5),
                            MaxBurstGaps = profile.MaxBurstGaps / 2,
                            DistanceMultiplier = 1.25,
                            TurnMode = TurnMode.Sharp,
                            StreamTurn = 0.3f,
                            SliderTurn = 1.2f,
                            SliderChance = 0.1,
                            KeepHoldChance = 0.5,
                            RepeatChance = 0.05,
                            CurveChance = 0.3,
                        };

                    case PatternStyle.Streams:
                        return new StyleTuning
                        {
                            MinGapBeats = profile.MinGapBeats,
                            MaxBurstGaps = profile.MaxBurstGaps * 2,
                            DistanceMultiplier = 0.9,
                            TurnMode = TurnMode.Smooth,
                            StreamTurn = 0.35f,
                            SliderTurn = 0.6f,
                            SliderChance = 0.1,
                            KeepHoldChance = 0.8,
                            RepeatChance = 0.1,
                            CurveChance = 0.5,
                        };

                    case PatternStyle.Flow:
                        return new StyleTuning
                        {
                            MinGapBeats = profile.MinGapBeats,
                            MaxBurstGaps = profile.MaxBurstGaps,
                            DistanceMultiplier = 0.85,
                            TurnMode = TurnMode.Smooth,
                            StreamTurn = 0.45f,
                            SliderTurn = 0.5f,
                            SliderChance = 0.25,
                            KeepHoldChance = 0.9,
                            RepeatChance = 0.12,
                            CurveChance = 0.7,
                        };

                    default:
                        return new StyleTuning
                        {
                            MinGapBeats = profile.MinGapBeats,
                            MaxBurstGaps = profile.MaxBurstGaps,
                            DistanceMultiplier = 0.75,
                            TurnMode = TurnMode.Varied,
                            StreamTurn = 0.4f,
                            SliderTurn = 1.0f,
                            SliderChance = 0.6,
                            KeepHoldChance = 1,
                            RepeatChance = 0.35,
                            CurveChance = 0.6,
                        };
                }
            }
        }
    }
}
