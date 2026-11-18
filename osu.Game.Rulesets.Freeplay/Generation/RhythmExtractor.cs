// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.Freeplay.Generation
{
    /// <summary>
    /// A moment in the music where an object may be placed.
    /// </summary>
    public class RhythmPoint
    {
        public double Time;

        /// <summary>
        /// If the source had a held object here, the time it ends.
        /// </summary>
        public double? HoldEndTime;

        public bool IsSpinner;

        public IList<HitSampleInfo> Samples = Array.Empty<HitSampleInfo>();
    }

    /// <summary>
    /// Extracts candidate object timings from a source beatmap.
    /// The result is intentionally dense; <see cref="PatternGenerator"/> thins it down to the target difficulty.
    /// </summary>
    public static class RhythmExtractor
    {
        /// <summary>
        /// Below this many source objects per second, the source rhythm is considered too sparse to follow in <see cref="RhythmSource.Auto"/>.
        /// </summary>
        private const double min_objects_per_second = 0.75;

        private const double chord_merge_window = 5;

        public static List<RhythmPoint> Extract(IBeatmap original, RhythmSource source)
        {
            var objects = original.HitObjects;

            if (objects.Count == 0)
                return new List<RhythmPoint>();

            if (source == RhythmSource.Auto)
            {
                double length = Math.Max(1, objects[^1].GetEndTime() - objects[0].StartTime);
                source = objects.Count / (length / 1000) >= min_objects_per_second ? RhythmSource.BeatmapObjects : RhythmSource.TimingGrid;
            }

            var points = source == RhythmSource.BeatmapObjects ? fromObjects(original) : fromTimingGrid(original);

            return removeBreaks(original, mergeChords(points));
        }

        private static List<RhythmPoint> fromObjects(IBeatmap original)
        {
            return original.HitObjects.Select(h =>
            {
                double endTime = h.GetEndTime();

                return new RhythmPoint
                {
                    Time = h.StartTime,
                    HoldEndTime = endTime > h.StartTime ? endTime : null,
                    IsSpinner = h is IHasDuration && h is not IHasPath && h is not IHasColumn,
                    Samples = h.Samples,
                };
            }).OrderBy(p => p.Time).ToList();
        }

        private static List<RhythmPoint> fromTimingGrid(IBeatmap original)
        {
            var result = new List<RhythmPoint>();
            var timingPoints = original.ControlPointInfo.TimingPoints;

            double startTime = original.HitObjects[0].StartTime;
            double endTime = original.HitObjects.Max(h => h.GetEndTime());

            for (int i = 0; i < timingPoints.Count; i++)
            {
                var timingPoint = timingPoints[i];
                double sectionEnd = i + 1 < timingPoints.Count ? timingPoints[i + 1].Time : endTime;

                // Half-beats are offered everywhere; the generator decides whether to use them.
                double step = timingPoint.BeatLength / 2;

                if (step <= 0)
                    continue;

                for (double time = timingPoint.Time; time < sectionEnd; time += step)
                {
                    if (time < startTime - 1)
                        continue;

                    result.Add(new RhythmPoint { Time = time, Samples = plainSampleNear(original, time) });
                }
            }

            return result;
        }

        private static List<RhythmPoint> mergeChords(List<RhythmPoint> points)
        {
            var result = new List<RhythmPoint>(points.Count);

            foreach (var point in points)
            {
                var last = result.LastOrDefault();

                if (last != null && point.Time - last.Time < chord_merge_window)
                {
                    if (point.HoldEndTime > (last.HoldEndTime ?? 0))
                        last.HoldEndTime = point.HoldEndTime;
                    continue;
                }

                result.Add(point);
            }

            return result;
        }

        private static List<RhythmPoint> removeBreaks(IBeatmap original, List<RhythmPoint> points)
            => points.Where(p => !original.Breaks.Any(b => p.Time > b.StartTime && p.Time < b.EndTime)).ToList();

        /// <summary>
        /// A plain hit sound using the bank and volume of the nearest source object, as grid points don't correspond to any mapped hitsound.
        /// </summary>
        private static IList<HitSampleInfo> plainSampleNear(IBeatmap original, double time)
        {
            var nearest = nearestObject(original, time).Samples.FirstOrDefault();

            return nearest == null
                ? new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) }
                : new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, nearest.Bank, volume: nearest.Volume) };
        }

        private static HitObject nearestObject(IBeatmap original, double time)
        {
            var objects = original.HitObjects;

            int lo = 0, hi = objects.Count - 1;

            while (lo < hi)
            {
                int mid = (lo + hi) / 2;

                if (objects[mid].StartTime < time)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return objects[lo];
        }
    }
}
