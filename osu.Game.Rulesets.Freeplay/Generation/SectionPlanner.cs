// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.Freeplay.Generation
{
    public enum SectionType
    {
        Jumps,
        Streams,
        Deathstream,
        Flow,
        Sliders,
    }

    /// <summary>
    /// A run of measures generated with one kind of pattern.
    /// </summary>
    public class Section
    {
        public double StartTime;
        public double EndTime;
        public SectionType Type;

        /// <summary>
        /// Whether this section is in a kiai (chorus) part of the track.
        /// </summary>
        public bool Kiai;

        /// <summary>
        /// Whether this section is kept calmer than the mapper's rhythm (on the beats, fewer off-beats), so the chorus after it stands out.
        /// </summary>
        public bool Calm;
    }

    /// <summary>
    /// Splits a track into sections by measure and decides what each section plays like, so that intense parts of the music
    /// (kiai, dense mapping) get intense patterns. Stream sections get their own notes on the track's 1/4 grid,
    /// as the source beatmap rarely maps long enough streams.
    /// </summary>
    public class SectionPlanner
    {
        /// <summary>
        /// Source objects per beat above which a part of the track is considered intense.
        /// </summary>
        private const double intense_density = 1.6;

        private readonly IBeatmap original;
        private readonly FreeplayGenerationSettings settings;
        private readonly DifficultyProfile profile;
        private readonly Random rng;
        private readonly Random calmRng;

        /// <summary>
        /// Whether this track contrasts its verses with its choruses (see <see cref="TrackPalette.Contrast"/>).
        /// </summary>
        private readonly bool contrast;

        public SectionPlanner(IBeatmap original, FreeplayGenerationSettings settings, DifficultyProfile profile, int seed)
        {
            this.original = original;
            this.settings = settings;
            this.profile = profile;

            // Offset from the generator's seed, so the two don't draw identical sequences.
            rng = new Random(unchecked(seed * 31 + 7));

            // Its own sequence, so calm sections don't change how the rest of the map rolls.
            calmRng = new Random(unchecked(seed * 17 + 3));
            contrast = calmRng.NextDouble() < (settings.Palette?.Contrast ?? 0);
        }

        /// <summary>
        /// Assigns each source point a section, and replaces the points of stream sections with generated streams.
        /// </summary>
        public List<RhythmPoint> Plan(List<RhythmPoint> sourcePoints)
        {
            if (sourcePoints.Count == 0)
                return sourcePoints;

            double startTime = sourcePoints[0].Time;
            double endTime = original.HitObjects.Max(h => h.GetEndTime());

            var measures = measureStarts(startTime, endTime);
            var result = new List<RhythmPoint>();

            SectionType? previousType = null;
            int index = 0;

            while (index < measures.Count)
            {
                double sectionStart = measures[index];
                bool kiai = isKiai(sectionStart);

                bool intense = isIntense(sectionStart, measureEnd(measures, index + 4, endTime), kiai);
                var type = chooseType(intense, previousType);

                int length = type switch
                {
                    SectionType.Deathstream => 8,
                    SectionType.Streams => rng.NextDouble() < 0.5 ? 2 : 4,
                    _ => 4,
                };

                // Sections end where the kiai starts or stops, so choruses get their own patterns.
                int end = index + 1;
                while (end < measures.Count && end - index < length && isKiai(measures[end]) == kiai)
                    end++;

                var section = new Section
                {
                    StartTime = sectionStart,
                    EndTime = measureEnd(measures, end, endTime),
                    Type = type,
                    Kiai = kiai,
                    // Not every quiet section, so it doesn't become a rule.
                    Calm = contrast && !intense && (type is SectionType.Jumps or SectionType.Flow) && calmRng.NextDouble() < 0.65,
                };

                var points = section.Type is SectionType.Streams or SectionType.Deathstream
                    ? generateStreams(section)
                    : null;

                // Streams that couldn't be generated (e.g. too fast for the star rating) fall back to the source rhythm.
                if (points == null || points.Count == 0)
                {
                    if (section.Type is SectionType.Streams or SectionType.Deathstream)
                        section.Type = SectionType.Jumps;

                    var sectionPoints = sourcePoints.Where(p => p.Time >= section.StartTime && p.Time < section.EndTime).ToList();

                    if (!BurstsAllowed(section.Type, profile, settings))
                        sectionPoints = settings.KickSliders ? coverBursts(sectionPoints) : dropBursts(sectionPoints);
                    else if (section.Type == SectionType.Jumps)
                        sectionPoints = reshapeDoubles(sectionPoints);

                    sectionPoints = thinToDensity(sectionPoints, section);
                    points = fillGaps(sectionPoints, section);
                }

                foreach (var point in points)
                    point.Section = section;

                result.AddRange(points);

                previousType = section.Type;
                index = end;
            }

            // Points before the first measure (e.g. a pickup before the first timing point) join the first section.
            var first = result.FirstOrDefault()?.Section;

            if (first != null)
            {
                var early = sourcePoints.Where(p => p.Time < first.StartTime).ToList();

                foreach (var point in early)
                    point.Section = first;

                result.InsertRange(0, early);
            }

            return result;
        }

        private SectionType chooseType(bool intense, SectionType? previous)
        {
            var type = rollType(intense);

            // Avoid repeating a section type, except for single-type styles.
            if (type == previous && settings.Style == PatternStyle.Mixed)
                type = rollType(intense);

            // Long generated streams are too much below ~3.5★; short bursts from the source rhythm still appear from 3★.
            bool streamsPlayable = settings.AllowStreams && profile.Stars >= 3.5;

            if (!streamsPlayable && type is SectionType.Streams or SectionType.Deathstream)
                type = settings.Style == PatternStyle.Jumps || intense ? SectionType.Jumps : SectionType.Flow;

            return type;
        }

        private SectionType rollType(bool intense)
        {
            double roll = rng.NextDouble();

            switch (settings.Style)
            {
                case PatternStyle.Jumps:
                    return SectionType.Jumps;

                case PatternStyle.Flow:
                    return SectionType.Flow;

                case PatternStyle.Sliders:
                    return SectionType.Sliders;

                case PatternStyle.Streams:
                    if (intense)
                        return roll < 0.8 ? SectionType.Streams : SectionType.Jumps;

                    return roll < 0.45 ? SectionType.Streams : roll < 0.75 ? SectionType.Jumps : SectionType.Flow;

                case PatternStyle.Deathstreams:
                    if (intense)
                        return roll < 0.8 ? SectionType.Deathstream : SectionType.Streams;

                    return roll < 0.35 ? SectionType.Deathstream : roll < 0.75 ? SectionType.Streams : SectionType.Flow;

                default:
                    if (intense)
                    {
                        if (profile.Stars >= 5.5 && roll < 0.08)
                            return SectionType.Deathstream;

                        return roll < 0.45 ? SectionType.Jumps : roll < 0.75 ? SectionType.Streams : SectionType.Sliders;
                    }

                    return roll < 0.45 ? SectionType.Flow : roll < 0.75 ? SectionType.Sliders : SectionType.Jumps;
            }
        }

        /// <summary>
        /// Places notes on the 1/4 grid (or 1/2 for tracks too fast for the star rating).
        /// Stream sections alternate streams with one-beat rests; deathstreams run the whole section.
        /// </summary>
        private List<RhythmPoint>? generateStreams(Section section)
        {
            var timingPoint = original.ControlPointInfo.TimingPointAt(section.StartTime);
            double beatLength = timingPoint.BeatLength;

            int divisor = beatLength / 4 >= profile.MinGapMs - 1 ? 4 : beatLength / 2 >= profile.MinGapMs - 1 ? 2 : 0;

            if (divisor == 0 || beatLength <= 0)
                return null;

            double step = beatLength / divisor;

            // Start on a beat.
            double time = timingPoint.Time + Math.Ceiling((section.StartTime - timingPoint.Time) / beatLength - 0.001) * beatLength;

            var result = new List<RhythmPoint>();

            // Streams grow with the star rating: 8-12 notes around 4★, up to ~40 at 9★.
            int minLength = profile.Stars < 4.5 ? 8 : 12;
            int maxLength = Math.Max(minLength, (int)(8 + 5 * (profile.Stars - 3)));

            while (time < section.EndTime - step)
            {
                int length = section.Type == SectionType.Deathstream ? int.MaxValue : rng.Next(minLength, maxLength + 1);

                for (int note = 0; note < length && time < section.EndTime - step / 2; note++)
                {
                    if (!inBreak(time))
                    {
                        result.Add(new RhythmPoint
                        {
                            Time = time,
                            Samples = RhythmExtractor.PlainSampleNear(original, time),
                            ContinuesStream = note > 0,
                        });
                    }

                    time += step;
                }

                // Rest for a beat, then start the next stream on a beat.
                double lastNote = time - step;
                time = timingPoint.Time + Math.Ceiling((lastNote + beatLength - timingPoint.Time) / beatLength - 0.001) * beatLength;
            }

            return result;
        }

        /// <summary>
        /// Whether 1/4 bursts from the music are played as bursts in sections of <paramref name="type"/>.
        /// Jump sections only have them in the aim style when the player turns them on; elsewhere they follow the star rating.
        /// </summary>
        public static bool BurstsAllowed(SectionType type, DifficultyProfile profile, FreeplayGenerationSettings settings)
        {
            if (!settings.AllowStreams || profile.MaxBurstGaps == 0)
                return false;

            if (type == SectionType.Jumps)
                return settings.Style == PatternStyle.Jumps;

            return true;
        }

        /// <summary>
        /// Turns runs of 1/4 notes into kick sliders: each pair of burst notes becomes one short slider from the first to the second,
        /// and a leftover note stays a note. A triple becomes a kick slider and a circle; a longer run, kick sliders every half beat.
        /// Without this, dropping the burst leaves a hole exactly where the music is busiest.
        /// </summary>
        private List<RhythmPoint> coverBursts(List<RhythmPoint> points)
        {
            var result = new List<RhythmPoint>(points.Count);

            for (int i = 0; i < points.Count; i++)
            {
                var current = points[i];
                result.Add(current);

                if (current.HoldEndTime != null || current.IsSpinner || i + 1 >= points.Count)
                    continue;

                var next = points[i + 1];
                double beatLength = original.ControlPointInfo.TimingPointAt(current.Time).BeatLength;

                if (next.HoldEndTime != null || next.IsSpinner || (next.Time - current.Time) / beatLength >= 0.375)
                    continue;

                current.HoldEndTime = next.Time;
                current.KickSlider = true;

                // The second note is covered by the slider.
                i++;
            }

            return result;
        }

        /// <summary>
        /// Lone 1/4 doubles are the least fun burst: some lose their second note, some become a triple stacked on one spot, the rest stay.
        /// </summary>
        private List<RhythmPoint> reshapeDoubles(List<RhythmPoint> points)
        {
            var result = new List<RhythmPoint>(points.Count);

            for (int i = 0; i < points.Count; i++)
            {
                var current = points[i];
                result.Add(current);

                if (i + 1 >= points.Count || current.HoldEndTime != null || current.IsSpinner)
                    continue;

                var next = points[i + 1];
                double quarter = original.ControlPointInfo.TimingPointAt(current.Time).BeatLength / 4;

                bool quick(double from, double to) => to - from < quarter * 1.5;

                // A double: two notes a 1/4 apart, with no quick note before or after.
                if (next.HoldEndTime != null || !quick(current.Time, next.Time)
                                             || (i > 0 && quick(points[i - 1].Time, current.Time))
                                             || (i + 2 < points.Count && quick(next.Time, points[i + 2].Time)))
                    continue;

                double roll = rng.NextDouble();

                if (roll < 0.45)
                {
                    // Just the first note.
                    i++;
                    continue;
                }

                if (roll < 0.7)
                {
                    double third = next.Time + (next.Time - current.Time);
                    bool room = i + 2 >= points.Count || points[i + 2].Time - third >= quarter * 1.9;

                    if (room && !inBreak(third))
                    {
                        current.StackBurst = next.StackBurst = true;
                        result.Add(next);
                        result.Add(new RhythmPoint { Time = third, Samples = RhythmExtractor.PlainSampleNear(original, third), StackBurst = true });
                        i++;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Leaves the extra notes of 1/4 bursts out: of each run, the notes a half beat apart stay (a triple becomes two notes on the 1/2).
        /// </summary>
        private List<RhythmPoint> dropBursts(List<RhythmPoint> points)
        {
            var result = new List<RhythmPoint>(points.Count);

            foreach (var point in points)
            {
                var last = result.LastOrDefault();

                if (last != null && last.HoldEndTime == null && !last.IsSpinner)
                {
                    double beatLength = original.ControlPointInfo.TimingPointAt(point.Time).BeatLength;

                    if ((point.Time - last.Time) / beatLength < 0.375)
                        continue;
                }

                result.Add(point);
            }

            return result;
        }

        /// <summary>
        /// Brings each measure down to the density the star rating allows by leaving out its weakest notes (quarters, then off-beats),
        /// spread across the measure, rather than whatever comes after the limit is reached, which leaves silences while the music plays.
        /// </summary>
        private List<RhythmPoint> thinToDensity(List<RhythmPoint> points, Section section)
        {
            if (points.Count < 4)
                return points;

            double calmShare = 0.6 + calmRng.NextDouble() * 0.2;

            var result = new List<RhythmPoint>(points.Count);

            var measures = points.GroupBy(p => measureIndexOf(p.Time)).Select(g => g.ToList()).ToList();

            for (int m = 0; m < measures.Count; m++)
            {
                var measure = measures[m];

                // The neighbours across the measure's edges, so a removal there can't leave a silence either.
                var before = result.Count > 0 ? result[^1] : null;
                var after = m + 1 < measures.Count ? measures[m + 1][0] : null;

                var timingPoint = original.ControlPointInfo.TimingPointAt(measure[0].Time);
                double measureSeconds = timingPoint.BeatLength * timingPoint.TimeSignature.Numerator / 1000;

                // A little under the limit, so the generator's own density check doesn't drop more on top.
                int allowed = Math.Max(1, (int)Math.Floor(profile.MaxObjectsPerSecondIn(section.Kiai) * measureSeconds * 0.95));

                // A calm section keeps about two thirds of what the mapper has (off-beats and quarters go first, the beats stay).
                if (section.Calm)
                    allowed = Math.Min(allowed, Math.Max(timingPoint.TimeSignature.Numerator, (int)Math.Ceiling(measure.Count * calmShare)));

                for (int strength = 0; strength <= 2 && measure.Count > allowed; strength++)
                {
                    var weak = measure.Where(p => p.HoldEndTime == null && !p.IsSpinner && !p.StackBurst && RhythmExtractor.BeatStrength(original.ControlPointInfo, p.Time) == strength).ToList();

                    // Every other one, so what's left stays even rather than bunched.
                    for (int i = 0; i < weak.Count && measure.Count > allowed; i += 2)
                        removeUnlessSilent(measure, weak[i], timingPoint.BeatLength, before, after);

                    for (int i = 1; i < weak.Count && measure.Count > allowed; i += 2)
                        removeUnlessSilent(measure, weak[i], timingPoint.BeatLength, before, after);
                }

                result.AddRange(measure);
            }

            return result;
        }

        /// <summary>
        /// Removes <paramref name="point"/>, unless that would leave the music silent for a beat and a half.
        /// </summary>
        private static void removeUnlessSilent(List<RhythmPoint> measure, RhythmPoint point, double beatLength, RhythmPoint? before, RhythmPoint? after)
        {
            int index = measure.IndexOf(point);

            if (index < 0)
                return;

            var previous = index > 0 ? measure[index - 1] : before;
            var next = index < measure.Count - 1 ? measure[index + 1] : after;

            // At the very start or end of the section there's nothing to judge against: it stays as it was.
            if (previous != null && next != null && next.Time - (previous.HoldEndTime ?? previous.Time) >= beatLength * 1.5)
                return;

            measure.RemoveAt(index);
        }

        private int measureIndexOf(double time)
        {
            var timingPoint = original.ControlPointInfo.TimingPointAt(time);
            double measureLength = timingPoint.BeatLength * timingPoint.TimeSignature.Numerator;

            // Unique per timing point: measures are counted from each one.
            return measureLength <= 0 ? 0 : (int)Math.Floor((time - timingPoint.Time + 2) / measureLength) + (int)timingPoint.Time * 1000;
        }

        /// <summary>
        /// Adds notes where the music has a sound the kept rhythm doesn't: inside the mapper's sliders (at their ends and repeats, or on the beat),
        /// which mostly become circles in aim and would otherwise go silent. Plain gaps are the mapper's own rhythm and stay as they are,
        /// unless the source is too sparse to follow (a low difficulty, or the timing grid), where the half-beat grid is filled as before.
        /// </summary>
        private List<RhythmPoint> fillGaps(List<RhythmPoint> points, Section section)
        {
            double sectionBeats = Math.Max(1, (section.EndTime - section.StartTime) / original.ControlPointInfo.TimingPointAt(section.StartTime).BeatLength);
            bool sparse = points.Count / sectionBeats < 0.7;

            double chance = profile.FillChance * section.Type switch
            {
                SectionType.Jumps => 1,
                SectionType.Flow => 0.6,
                _ => 0.35,
            };

            // Calm parts of the track stay calmer, so choruses stand out.
            if (!section.Kiai)
                chance *= 0.9;

            if (chance <= 0 || points.Count < 2)
                return points;

            var result = new List<RhythmPoint>(points.Count * 2);

            for (int i = 0; i < points.Count; i++)
            {
                var current = points[i];
                result.Add(current);

                if (i + 1 >= points.Count)
                    break;

                double beatLength = original.ControlPointInfo.TimingPointAt(current.Time).BeatLength;
                double gapBeats = (points[i + 1].Time - current.Time) / beatLength;

                if (current.IsSpinner || beatLength <= 0)
                    continue;

                // Longer gaps are rests the music asks for, unless a held object (a mapper's slider) fills them.
                // Most of those become circles in aim, leaving its whole length silent.
                double longestGap = current.HoldEndTime != null ? 4.05 : 2.05;

                if (gapBeats < 0.95 || gapBeats > longestGap)
                    continue;

                // Fills inside a held object only survive if it ends up a circle: if it becomes a slider,
                // the generator rejects them for overlapping it.
                double free = current.Time + beatLength / 2 - 1;
                double latest = points[i + 1].Time - beatLength / 2 + 1;
                bool twoBeats = gapBeats > 1.5;

                if (current.HoldEndTime != null)
                {
                    // Inside a slider: its end and repeats are sounds the mapper put there; past them, the beats.
                    double span = current.SpanDuration ?? double.MaxValue;
                    var times = new List<double>();

                    if (span < beatLength * 1.05)
                    {
                        for (double t = current.Time + span; t <= current.HoldEndTime.Value + 1; t += span)
                            times.Add(t);
                    }

                    for (double t = current.Time + beatLength; t < points[i + 1].Time - 1; t += beatLength)
                    {
                        if (!times.Any(x => Math.Abs(x - t) < beatLength / 4))
                            times.Add(t);
                    }

                    foreach (double time in times.Order())
                    {
                        if (time >= free && time <= latest)
                            tryFill(result, time, Math.Min(1, chance * 1.2));
                    }

                    continue;
                }

                // Plain gaps are the mapper's rhythm: filling them on the grid puts notes where the music has none.
                if (!sparse)
                    continue;

                // Half-beat positions inside the gap. In a two-beat gap the beat between sounds the most empty, so it's the likeliest.
                for (int k = 1; k * 0.5 < gapBeats - 0.25; k++)
                {
                    double time = current.Time + k * beatLength / 2;

                    if (time < free || time > latest)
                        continue;

                    bool onBeat = k % 2 == 0;
                    double positionChance = !twoBeats ? chance : onBeat ? Math.Min(1, chance * 1.3) : chance * 0.5;

                    tryFill(result, time, positionChance);
                }
            }

            return result;
        }

        private bool tryFill(List<RhythmPoint> result, double time, double chance)
        {
            if (inBreak(time) || rng.NextDouble() >= chance)
                return false;

            result.Add(new RhythmPoint
            {
                Time = time,
                Samples = RhythmExtractor.PlainSampleNear(original, time),
            });

            return true;
        }

        private List<double> measureStarts(double startTime, double endTime)
        {
            var result = new List<double>();
            var timingPoints = original.ControlPointInfo.TimingPoints;

            for (int i = 0; i < timingPoints.Count; i++)
            {
                var timingPoint = timingPoints[i];
                double segmentEnd = i + 1 < timingPoints.Count ? timingPoints[i + 1].Time : endTime;
                double measureLength = timingPoint.BeatLength * timingPoint.TimeSignature.Numerator;

                if (measureLength <= 0)
                    continue;

                for (double time = timingPoint.Time; time < segmentEnd && time < endTime; time += measureLength)
                {
                    if (time + measureLength > startTime)
                        result.Add(time);
                }
            }

            return result;
        }

        private static double measureEnd(List<double> measures, int index, double endTime) => index < measures.Count ? measures[index] : endTime;

        private bool isKiai(double time) => original.ControlPointInfo.EffectPointAt(time + 1).KiaiMode;

        private bool isIntense(double start, double end, bool kiai)
        {
            if (kiai)
                return true;

            double beatLength = original.ControlPointInfo.TimingPointAt(start).BeatLength;
            double beats = Math.Max(1, (end - start) / beatLength);

            int objects = original.HitObjects.Count(h => h.StartTime >= start && h.StartTime < end);

            return objects / beats >= intense_density;
        }

        private bool inBreak(double time) => original.Breaks.Any(b => time > b.StartTime && time < b.EndTime);
    }
}
