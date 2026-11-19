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
    /// Turns <see cref="RhythmPoint"/>s into playable osu! hit objects, according to a <see cref="DifficultyProfile"/> and <see cref="FreeplayGenerationSettings"/>.
    /// </summary>
    /// <remarks>
    /// Generation runs in three passes:
    /// <list type="number">
    /// <item>Rhythm: choose which points become objects, and whether each is a circle, slider or spinner.</item>
    /// <item>Placement: group consecutive circles into jump patterns (squares, stars...) and streams (arcs, S-curves...), and position everything.</item>
    /// <item>Combos.</item>
    /// </list>
    /// </remarks>
    public class PatternGenerator
    {
        private static readonly Vector2 playfield_centre = OsuPlayfield.BASE_SIZE / 2;

        /// <summary>
        /// Gaps between circles longer than this (in beats) end a jump pattern.
        /// </summary>
        private const double max_phrase_gap_beats = 1.5;

        /// <summary>
        /// Gaps shorter than this (in beats) are treated as streams rather than jumps.
        /// </summary>
        private const double burst_gap_beats = 0.375;

        /// <summary>
        /// How often stream segments replay a mapper's stream (when available) rather than a built-in curve.
        /// </summary>
        private const double mined_pattern_chance = 0.6;

        /// <summary>
        /// Of the patterns which don't come from the example collections, how many are geometric shapes rather than random motifs.
        /// Smarter maps use more shapes.
        /// </summary>
        private double shapeShare => 0.25 + 0.35 * iq;

        /// <summary>
        /// <see cref="FreeplayGenerationSettings.PatternIq"/>, clamped.
        /// </summary>
        private double iq => Math.Clamp(settings.PatternIq, 0, 1);

        /// <summary>
        /// How tricky each shape is, on the same 0-1 scale as <see cref="FreeplayGenerationSettings.PatternIq"/>.
        /// </summary>
        private static double shapeIq(JumpPatterns pattern)
        {
            switch (pattern)
            {
                case JumpPatterns.PingPong:
                    return 0.05;

                case JumpPatterns.Zigzags:
                    return 0.2;

                case JumpPatterns.Triangles:
                    return 0.3;

                case JumpPatterns.Stacks:
                    return 0.45;

                case JumpPatterns.RandomJumps:
                    return 0.55;

                case JumpPatterns.Spirals:
                    return 0.6;

                case JumpPatterns.Polygons:
                    return 0.65;

                case JumpPatterns.Squares:
                    return 0.8;

                case JumpPatterns.Crosses:
                    return 0.85;

                default:
                    return 0.95;
            }
        }

        private readonly ControlPointInfo controlPoints;
        private readonly DifficultyProfile profile;
        private readonly FreeplayGenerationSettings settings;
        private readonly Random rng;
        private readonly float margin;

        public PatternGenerator(ControlPointInfo controlPoints, DifficultyProfile profile, FreeplayGenerationSettings settings, int seed)
        {
            this.controlPoints = controlPoints;
            this.profile = profile;
            this.settings = settings;
            rng = new Random(seed);

            float scale = LegacyRulesetExtensions.CalculateScaleFromCircleSize(profile.CircleSize, true);
            margin = OsuHitObject.OBJECT_RADIUS * scale;
        }

        public List<OsuHitObject> Generate(IReadOnlyList<RhythmPoint> points, CancellationToken cancellationToken = default)
        {
            var planned = planRhythm(points, cancellationToken);

            placeObjects(planned);
            applyCombos(planned);

            return planned.Select(p => p.Result!).ToList();
        }

        #region Rhythm

        private List<PlannedObject> planRhythm(IReadOnlyList<RhythmPoint> points, CancellationToken cancellationToken)
        {
            var result = new List<PlannedObject>();

            var recentStartTimes = new Queue<double>();
            var tunings = new Dictionary<SectionType, StyleTuning>();
            Section? lastSection = null;

            int burstGaps = 0;

            for (int i = 0; i < points.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var point = points[i];
                var timingPoint = controlPoints.TimingPointAt(point.Time);
                double beatLength = timingPoint.BeatLength;

                var section = point.Section;

                if (!tunings.TryGetValue(section.Type, out var tuning))
                    tunings[section.Type] = tuning = StyleTuning.For(section.Type, profile, settings);

                bool styleChanged = lastSection != null && lastSection != section && lastSection.Type != section.Type;

                var last = result.LastOrDefault();

                bool accepted = tryAccept(point, last, beatLength, tuning, burstGaps, recentStartTimes, out bool isBurstGap);

                // Too dense for the star rating, but leaving it out would leave a beat or more silent while the music plays: keep it anyway.
                // (Within a third over the limit, and not on easy maps, whose rests are the point.)
                if (!accepted && last != null && profile.Stars >= 3 && point.Time - last.EndTime >= beatLength * 0.95
                    && recentStartTimes.Count(t => point.Time - t < DifficultyProfile.DENSITY_WINDOW) < profile.MaxObjectsPerSecond * DifficultyProfile.DENSITY_WINDOW / 1000 * 1.3
                    && tryAccept(point, last, beatLength, tuning, burstGaps, new Queue<double>(), out isBurstGap))
                    accepted = true;

                if (!accepted)
                {
                    // When two points are too close to both keep, keep the one on the stronger beat, so thinning doesn't push patterns off the beat.
                    if (last == null || last.Kind != ObjectKind.Circle || point.ContinuesStream || beatStrength(point.Time) <= beatStrength(last.Time))
                        continue;

                    var withoutLast = new Queue<double>(recentStartTimes.Take(recentStartTimes.Count - 1));
                    var beforeLast = result.Count > 1 ? result[^2] : null;

                    if (!tryAccept(point, beforeLast, beatLength, tuning, 0, withoutLast, out isBurstGap))
                        continue;

                    result.RemoveAt(result.Count - 1);
                    recentStartTimes = withoutLast;
                    burstGaps = 0;
                }

                double available = timeUntilNextPlayable(points, i, beatLength, tuning) - point.Time;

                var planned = new PlannedObject
                {
                    Point = point,
                    Time = point.Time,
                    EndTime = point.Time,
                    BeatLength = beatLength,
                    Timing = timingPoint,
                    IsBurstGap = isBurstGap,
                    Section = section,
                    Tuning = tuning,
                    StyleChanged = styleChanged,
                };

                lastSection = section;

                if (settings.AllowSpinners && point.IsSpinner && point.HoldEndTime - point.Time >= Math.Max(1500, beatLength * 3))
                {
                    planned.Kind = ObjectKind.Spinner;
                    planned.EndTime = point.HoldEndTime!.Value;
                }
                else if (shouldCreateSlider(point, available, beatLength, tuning, out double duration))
                {
                    planned.Kind = ObjectKind.Slider;
                    planned.EndTime = point.Time + duration;
                    planSlider(planned, duration, tuning);
                }

                burstGaps = isBurstGap ? burstGaps + 1 : 0;
                recentStartTimes.Enqueue(point.Time);

                result.Add(planned);
            }

            return result;
        }

        /// <summary>
        /// How strong a beat <paramref name="time"/> falls on: 4 for a downbeat, 3 a beat, 2 a half beat, 1 a quarter, 0 otherwise.
        /// </summary>
        private int beatStrength(double time)
        {
            var timingPoint = controlPoints.TimingPointAt(time);
            double beats = (time - timingPoint.Time) / timingPoint.BeatLength;

            // Allow for rounding in mapped timings.
            double tolerance = 8 / timingPoint.BeatLength;

            if (Math.Abs(beats - Math.Round(beats)) < tolerance)
                return (long)Math.Round(beats) % Math.Max(1, timingPoint.TimeSignature.Numerator) == 0 ? 4 : 3;

            if (Math.Abs(beats * 2 - Math.Round(beats * 2)) < tolerance * 2)
                return 2;

            if (Math.Abs(beats * 4 - Math.Round(beats * 4)) < tolerance * 4)
                return 1;

            return 0;
        }

        /// <summary>
        /// Decides whether a point is kept, enforcing minimum gaps, density and stream length limits.
        /// </summary>
        private bool tryAccept(RhythmPoint point, PlannedObject? last, double beatLength, StyleTuning tuning, int burstGaps, Queue<double> recentStartTimes, out bool isBurstGap)
        {
            isBurstGap = false;

            if (last == null)
                return true;

            // Leave some recovery time after held objects.
            double gapFromEnd = point.Time - last.EndTime;
            double requiredAfterEnd = last.Kind == ObjectKind.Spinner ? beatLength
                : last.Kind == ObjectKind.Slider ? Math.Max(profile.MinGapMs, beatLength * (profile.Stars < 3.5 && !last.Point.KickSlider ? 0.5 : 0.25))
                : 0;

            if (gapFromEnd < requiredAfterEnd - 1)
                return false;

            // Generated streams are already spaced for the star rating, and are meant to be long.
            if (point.ContinuesStream && last.Point.Section == point.Section)
            {
                isBurstGap = true;
                return point.Time - last.Time >= profile.MinGapMs - 1;
            }

            while (recentStartTimes.Count > 0 && point.Time - recentStartTimes.Peek() >= DifficultyProfile.DENSITY_WINDOW)
                recentStartTimes.Dequeue();

            if (recentStartTimes.Count >= profile.MaxObjectsPerSecondIn(point.Section?.Kiai == true, beatLength) * DifficultyProfile.DENSITY_WINDOW / 1000)
                return false;

            double gap = point.Time - last.Time;
            double gapBeats = gap / beatLength;

            isBurstGap = gapBeats < burst_gap_beats;

            if (isBurstGap)
                return gap >= profile.MinGapMs && burstGaps < tuning.MaxBurstGaps;

            return gapBeats >= tuning.MinGapBeats * 0.95;
        }

        /// <summary>
        /// Estimates when the next object could be placed, which bounds how long a generated slider may be.
        /// </summary>
        private double timeUntilNextPlayable(IReadOnlyList<RhythmPoint> points, int index, double beatLength, StyleTuning tuning)
        {
            double minGap = Math.Max(profile.MinGapMs, beatLength * tuning.MinGapBeats);

            for (int j = index + 1; j < points.Count; j++)
            {
                if (points[j].Time - points[index].Time >= minGap * 0.95)
                    return points[j].Time;
            }

            return points[index].Time + beatLength * 4;
        }

        private bool shouldCreateSlider(RhythmPoint point, double available, double beatLength, StyleTuning tuning, out double duration)
        {
            duration = 0;

            // A burst the player isn't asked to tap: a short slider covering it, if sliders are allowed at all.
            if (point.KickSlider)
            {
                duration = point.HoldEndTime!.Value - point.Time;
                return settings.SliderAmount > 0 && available >= duration;
            }

            if (profile.Stars < 1.5 && point.HoldEndTime == null)
                return false;

            double holdDuration = (point.HoldEndTime ?? point.Time) - point.Time;

            if (holdDuration >= beatLength * 0.45 && rng.NextDouble() < tuning.KeepHoldChance)
            {
                duration = Math.Min(holdDuration, beatLength * 4);

                // Aim jumps to the next note from the slider's end: a 1/4 after it would be a full jump in a quarter beat. Shortened to leave half a beat, or a circle.
                if (settings.Style == PatternStyle.Jumps && available - duration < beatLength * 0.5 - 1)
                {
                    duration = Math.Floor((available - beatLength * 0.5 + 1) / (beatLength / 2)) * (beatLength / 2);

                    if (duration < beatLength * 0.45)
                        return false;
                }

                return true;
            }

            // Generate a slider into a gap, leaving at least half a beat before the next object.
            int maxHalfBeats = (int)Math.Floor((available - beatLength * 0.5) / (beatLength / 2));

            if (maxHalfBeats < 1 || rng.NextDouble() >= tuning.SliderChance)
                return false;

            duration = rng.Next(1, Math.Min(maxHalfBeats, 4) + 1) * beatLength / 2;
            return true;
        }

        private void planSlider(PlannedObject planned, double duration, StyleTuning tuning)
        {
            int spans = 1;

            if (duration >= planned.BeatLength * 0.95 && rng.NextDouble() < tuning.RepeatChance)
                spans = duration >= planned.BeatLength * 1.45 && rng.NextDouble() < 0.3 ? 3 : 2;

            // Matches Slider.Velocity with a slider velocity multiplier of 1.
            double velocity = 100 * profile.SliderMultiplier / planned.BeatLength;
            double spanLength = velocity * duration / spans;

            while (spanLength > 260 && spans < 6)
                spanLength = velocity * duration / ++spans;

            planned.Spans = spans;
            planned.SpanLength = spanLength;
        }

        #endregion

        #region Placement

        private Vector2 cursor = playfield_centre;
        private float direction;
        private PlannedObject? previous;

        private float kiaiSpacing = 1;

        /// <summary>
        /// The character the pattern being placed follows (the section's, for most of its patterns).
        /// </summary>
        private SectionCharacter patternCharacter;

        /// <summary>
        /// The next jump is the huge entrance into a chorus (a rare moment).
        /// </summary>
        private bool bigEntrance;

        #region Variety

        private TrackPalette palette = new TrackPalette();
        private readonly Dictionary<Section, SectionRecipe> recipes = new Dictionary<Section, SectionRecipe>();
        private readonly List<SectionRecipe> recentRecipes = new List<SectionRecipe>();
        private readonly Dictionary<Section, Section?> nextSections = new Dictionary<Section, Section?>();
        private readonly Dictionary<Section, (double start, double end, float median)> sectionSpans = new Dictionary<Section, (double, double, float)>();
        private IReadOnlyList<string> flavors = Array.Empty<string>();
        private int spicesLeft;

        /// <summary>
        /// The recipe of the section being placed.
        /// </summary>
        private SectionRecipe recipe = new SectionRecipe();

        /// <summary>
        /// Jump patterns placed so far, by rhythm, so a phrase the music repeats can be answered with the same pattern.
        /// </summary>
        private readonly Queue<MinedPattern> recentMined = new Queue<MinedPattern>();

        private readonly List<(string rhythm, List<Vector2> offsets)> phraseMemory = new List<(string, List<Vector2>)>();

        private void prepareVariety(List<PlannedObject> planned)
        {
            palette = settings.Palette ?? new TrackPalette();
            spicesLeft = palette.Spices;
            flavors = MinedPatternSet.Current?.Creators(PatternCategory.Aim) ?? (IReadOnlyList<string>)Array.Empty<string>();

            Section? last = null;

            foreach (var p in planned)
            {
                if (p.Section == last)
                    continue;

                if (last != null)
                    nextSections[last] = p.Section;

                last = p.Section;
            }

            // The mapper's typical jump in each section, to measure their spacing against.
            foreach (var group in planned.GroupBy(p => p.Section))
            {
                var list = group.ToList();
                var distances = list.Zip(list.Skip(1), (a, b) => a.Point.SourcePosition is Vector2 pa && b.Point.SourcePosition is Vector2 pb ? Vector2.Distance(pa, pb) : -1)
                                    .Where(d => d > 10).OrderBy(d => d).ToList();

                sectionSpans[group.Key] = (list[0].Time, list[^1].Time, distances.Count >= 4 ? distances[distances.Count / 2] : 0);
            }
        }

        private SectionRecipe recipeFor(Section section)
        {
            if (recipes.TryGetValue(section, out var existing))
                return existing;

            // Only aim-like sections get recipes; streams keep their own shapes.
            var rolled = section.Type is SectionType.Streams or SectionType.Deathstream
                ? new SectionRecipe { MinedShare = settings.MinedPatternShare }
                : SectionRecipe.Roll(palette, rng, recentRecipes, flavors, Math.Clamp(settings.MinedPatternShare, 0, 1), iq);

            // Opening up only means something into a chorus.
            if (rolled.Crescendo && !(nextSections.GetValueOrDefault(section)?.Kiai == true && !section.Kiai))
                rolled.Crescendo = false;

            recipes[section] = rolled;
            recentRecipes.Add(rolled);
            return rolled;
        }

        /// <summary>
        /// How much wider than usual the jump to <paramref name="current"/> is, from the section's recipe and the music.
        /// </summary>
        private float noteSpacing(PlannedObject current, PlannedObject? from)
        {
            float factor = 1;

            if (recipe.MapperSpacing && from?.Point.SourcePosition is Vector2 a && current.Point.SourcePosition is Vector2 b
                && sectionSpans.TryGetValue(current.Section, out var span) && span.median > 0)
            {
                // Carefully: only part of the mapper's contrast, and never extreme.
                float ratio = Math.Clamp(Vector2.Distance(a, b) / span.median, 0.6f, 1.5f);
                factor *= 1 + (ratio - 1) * 0.6f;
            }

            if (recipe.Accents && isAccent(current.Point))
                factor *= 1.3f;

            if (bigEntrance)
            {
                bigEntrance = false;
                factor *= 1.6f;
            }

            if (recipe.Crescendo && sectionSpans.TryGetValue(current.Section, out var crescendo) && crescendo.end > crescendo.start)
                factor *= 0.88f + 0.27f * (float)((current.Time - crescendo.start) / (crescendo.end - crescendo.start));

            return factor;
        }

        /// <summary>
        /// The sharp turn of an accent: doubling back on simple maps, a right angle on smart ones.
        /// </summary>
        private float accentTurn() => (rng.NextDouble() < iq ? MathF.PI / 2 + randomRange(-0.2f, 0.2f) : randomRange(2.3f, 3.0f)) * (rng.NextDouble() < 0.5 ? -1 : 1);

        private static bool isAccent(RhythmPoint point) => point.Samples.Any(s => s.Name is HitSampleInfo.HIT_CLAP or HitSampleInfo.HIT_FINISH);

        private static bool isFinish(RhythmPoint point) => point.Samples.Any(s => s.Name == HitSampleInfo.HIT_FINISH);

        /// <summary>
        /// Turns <paramref name="angle"/> a little towards the section's part of the playfield, when the cursor is away from it.
        /// </summary>
        private float steerToTerritory(float angle)
        {
            if (recipe.Territory is not Vector2 territory)
                return angle;

            Vector2 toTerritory = territory - cursor;

            if (toTerritory.Length < 110)
                return angle;

            float difference = MathF.IEEERemainder(MathF.Atan2(toTerritory.Y, toTerritory.X) - angle, MathF.Tau);
            return angle + Math.Clamp(difference, -0.45f, 0.45f);
        }

        /// <summary>
        /// Whether to spend one of the track's rare moments, with <paramref name="chance"/>.
        /// </summary>
        private bool spice(double chance)
        {
            if (spicesLeft <= 0 || rng.NextDouble() >= chance)
                return false;

            spicesLeft--;
            return true;
        }

        private static string rhythmKey(List<PlannedObject> planned, int start, int count)
            => string.Join(",", Enumerable.Range(1, count - 1).Select(i => Math.Round((planned[start + i].Time - planned[start + i - 1].Time) / planned[start + i].BeatLength * 4)));

        #endregion

        private void placeObjects(List<PlannedObject> planned)
        {
            direction = randomAngle();
            prepareVariety(planned);

            int i = 0;
            Section? section = null;

            while (i < planned.Count)
            {
                var current = planned[i];

                // Choruses (the mapper's kiai) open up, the rest closes in a little, so a map has a shape rather than one spacing throughout.
                // A calm section (see Section.Calm) rests the hand with smaller jumps.
                kiaiSpacing = current.Section.Kiai ? 1.1f : current.Section.Calm ? 0.82f : 0.94f;
                recipe = recipeFor(current.Section);

                // Only jump patterns pick their own size; sliders and streams jump as usual.
                patternScale = 1;

                // Into a chorus: once in a while, one huge jump onto its first note.
                if (current.Section != section)
                {
                    if (section != null && current.Section.Kiai && !section.Kiai && current.Kind == ObjectKind.Circle && spice(0.5))
                        bigEntrance = true;

                    section = current.Section;
                }

                switch (current.Kind)
                {
                    case ObjectKind.Spinner:
                        placeSpinner(current);
                        i++;
                        continue;

                    case ObjectKind.Slider:
                        placeSlider(current);
                        i++;
                        continue;
                }

                int streamEnd = findStreamEnd(planned, i);

                if (streamEnd > i)
                {
                    placeStream(planned, i, streamEnd);
                    i = streamEnd + 1;
                    continue;
                }

                i += placeJumpPattern(planned, i, findPhraseEnd(planned, i));
            }
        }

        /// <summary>
        /// Finds the last circle of a stream starting at <paramref name="start"/> (or <paramref name="start"/> itself if none starts there).
        /// </summary>
        private static int findStreamEnd(List<PlannedObject> planned, int start)
        {
            int end = start;

            while (end + 1 < planned.Count && planned[end + 1].Kind == ObjectKind.Circle && planned[end + 1].IsBurstGap)
                end++;

            return end;
        }

        /// <summary>
        /// Finds the last circle which can share a jump pattern with the circle at <paramref name="start"/>.
        /// </summary>
        private static int findPhraseEnd(List<PlannedObject> planned, int start)
        {
            int end = start;

            while (end + 1 < planned.Count)
            {
                var next = planned[end + 1];

                if (next.Kind != ObjectKind.Circle || next.IsBurstGap || next.StyleChanged || next.Section != planned[start].Section)
                    break;

                // Leave the head of a following stream to the stream.
                if (end + 2 < planned.Count && planned[end + 2].IsBurstGap)
                    break;

                if (next.Time - planned[end].Time > next.BeatLength * max_phrase_gap_beats)
                    break;

                end++;
            }

            return end;
        }

        /// <summary>
        /// Places circles from <paramref name="start"/> as a single jump pattern, up to <paramref name="end"/>.
        /// </summary>
        /// <returns>The number of circles placed.</returns>
        private int placeJumpPattern(List<PlannedObject> planned, int start, int end)
        {
            // Where the rhythm is even, patterns differ in size instead: some tighter, some wider, around the same average.
            patternScale = planned[start].Section.FlowStep > 0 ? (float)Math.Clamp(1 + (rng.NextDouble() + rng.NextDouble() - 1) * 0.32, 0.75, 1.25) : 1;

            int placed = placeJumpPatternUnrecorded(planned, start, end);

            // Remembered by rhythm, so the music repeating this phrase can bring the pattern back.
            if (placed >= 4 && planned[start].Result is OsuHitObject head)
            {
                var offsets = Enumerable.Range(start, placed).Select(i => planned[i].Result!.Position - head.Position).ToList();
                phraseMemory.Add((rhythmKey(planned, start, placed), offsets));

                if (phraseMemory.Count > 16)
                    phraseMemory.RemoveAt(0);
            }

            return placed;
        }

        /// <summary>
        /// The shape placed last (null after a motif or a mapper's pattern) and how many times in a row, for <see cref="FreeplayGenerationSettings.MaxSameInARow"/>.
        /// </summary>
        private readonly List<JumpPatterns?> placedShapes = new List<JumpPatterns?>();

        /// <summary>
        /// The shapes placed, in order, with null for a motif or a mapper's pattern between them.
        /// </summary>
        public IReadOnlyList<JumpPatterns?> PlacedShapes => placedShapes;

        private JumpPatterns? lastShape;

        private int lastShapeRun;

        /// <summary>
        /// Whether <see cref="lastShape"/> came as many times in a row as the player allows (never with only one shape on).
        /// </summary>
        private bool shapeLimitReached => settings.MaxSameInARow > 0 && lastShape != null && lastShapeRun >= settings.MaxSameInARow
                                          && System.Numerics.BitOperations.PopCount((uint)settings.Patterns) > 1;

        private int placeJumpPatternUnrecorded(List<PlannedObject> planned, int start, int end)
        {
            var first = planned[start];
            var tuning = first.Tuning;

            if (recipe.Repeats && !shapeLimitReached)
            {
                int repeated = placeRepeat(planned, start, end);
                if (repeated > 0)
                    return repeated;
            }

            // A section's character decides most of its shapes (or the kind of motif) instead of chance; the rest stay mixed,
            // so the section has a flavour without being one shape throughout.
            var character = patternCharacter = rng.NextDouble() < 0.6 ? recipe.Character : SectionCharacter.None;

            JumpPatterns? forcedShape = character switch
            {
                SectionCharacter.SquaresAndCrosses => rng.NextDouble() < 0.5 ? JumpPatterns.Squares : JumpPatterns.Crosses,
                SectionCharacter.Stacks => JumpPatterns.Stacks,
                SectionCharacter.PingPong => JumpPatterns.PingPong,
                _ => null,
            };

            if ((forcedShape is JumpPatterns forced && !settings.Patterns.HasFlag(forced)) || (forcedShape == lastShape && shapeLimitReached))
                forcedShape = null;

            if (character is SectionCharacter.WideTriangles or SectionCharacter.Flow)
            {
                int placed = placeMotif(planned, start, end);
                if (placed > 0)
                {
                    lastShape = null;
                    placedShapes.Add(null);
                    return placed;
                }
            }
            else if (forcedShape == null)
            {
                // Mix patterns from real beatmaps with ones made up on the spot, so no two runs look alike.
                double mined = MinedPatternSet.Current != null ? recipe.MinedShare : 0;
                double roll = rng.NextDouble();

                if (roll < mined)
                {
                    int placed = placeMinedPattern(planned, start, end);
                    if (placed > 0)
                    {
                        lastShape = null;
                        placedShapes.Add(null);
                        return placed;
                    }
                }
                else if (roll < mined + (1 - mined) * (1 - shapeShare))
                {
                    int placed = placeMotif(planned, start, end);
                    if (placed > 0)
                    {
                        lastShape = null;
                        placedShapes.Add(null);
                        return placed;
                    }
                }
            }

            var pattern = forcedShape ?? choosePattern(tuning);
            lastShapeRun = pattern == lastShape ? lastShapeRun + 1 : 1;
            lastShape = pattern;
            placedShapes.Add(pattern);
            int sides = rng.Next(5, 9);
            int cycle = PatternShapes.CycleLength(pattern, sides);

            // Run one or two passes of a closed shape; open shapes run for one or two measures' worth.
            int wanted = pattern is JumpPatterns.PingPong or JumpPatterns.Zigzags or JumpPatterns.RandomJumps
                ? rng.Next(4, 9)
                : cycle * (rng.NextDouble() < 0.5 ? 1 : 2);

            int count = Math.Min(wanted, end - start + 1);

            if (count < 3)
            {
                for (int i = 0; i < count; i++)
                    placeSingleCircle(planned[start + i]);
                return count;
            }

            double averageGap = 0;
            for (int i = 1; i < count; i++)
                averageGap += planned[start + i].Time - planned[start + i - 1].Time;
            averageGap /= count - 1;

            float spacing = jumpDistance(averageGap, tuning);

            // A second pass of a closed shape turns by half a vertex, so it interleaves with the first
            // (two squares form an eight-pointed star, two triangles a hexagram).
            float rotation = settings.RotatePatterns ? (MathF.PI / cycle + randomRange(-0.05f, 0.05f)) * (rng.NextDouble() < 0.5 ? -1 : 1) : 0;

            var offsets = PatternShapes.Create(pattern, count, spacing, randomAngle(), rotation, sides, rng);

            // Big closed shapes don't fit on the playfield and would be shrunk, losing the difficulty; play a motif instead.
            if (fitScale(offsets) < 0.75)
            {
                int placed = placeMotif(planned, start, end);
                if (placed > 0)
                    return placed;
            }

            var positions = fitToPlayfield(offsets, first);

            first.IsPatternStart = true;

            for (int i = 0; i < count; i++)
            {
                var current = planned[start + i];
                current.Result = createCircle(current, positions[i]);
                moveCursor(current, positions[i]);
            }

            return count;
        }

        /// <summary>
        /// Places circles using a pattern taken from a real beatmap, rescaled to the target spacing and randomly rotated and mirrored.
        /// </summary>
        /// <returns>The number of circles placed, or zero if no suitable pattern was found.</returns>
        /// <summary>
        /// When this phrase's rhythm was heard before, sometimes brings that pattern back, mirrored or turned, the way mappers answer a repeated phrase.
        /// </summary>
        /// <returns>The number of circles placed, or zero if nothing was repeated.</returns>
        private int placeRepeat(List<PlannedObject> planned, int start, int end)
        {
            if (phraseMemory.Count == 0 || rng.NextDouble() >= 0.6)
                return 0;

            // Even half beats all sound alike: repeating on them would bring the same pattern back over and over, not where the music repeats.
            int span = Math.Min(end - start + 1, 12);

            if (span >= 4 && rhythmKey(planned, start, span).Split(',').Distinct().Count() == 1 && rng.NextDouble() >= 0.2)
                return 0;

            for (int count = Math.Min(end - start + 1, 12); count >= 4; count--)
            {
                string key = rhythmKey(planned, start, count);
                var match = phraseMemory.LastOrDefault(m => m.rhythm == key && m.offsets.Count == count);

                if (match.offsets == null)
                    continue;

                var first = planned[start];

                // Sized for this phrase, a little either way of the original.
                double averageGap = Enumerable.Range(1, count - 1).Average(i => planned[start + i].Time - planned[start + i - 1].Time);
                float wanted = jumpDistance(averageGap, first.Tuning);
                float original = match.offsets.Zip(match.offsets.Skip(1), (a, b) => Vector2.Distance(a, b)).Average();
                float scale = original < 1 ? 1 : Math.Clamp(wanted / original, 0.8f, 1.25f);

                bool mirror = rng.NextDouble() < 0.5;
                float turn = rng.Next(4) * MathF.PI / 2;

                var offsets = match.offsets.Select(o => rotate(new Vector2(mirror ? -o.X : o.X, o.Y) * scale, turn)).ToList();
                var positions = fitToPlayfield(offsets, first);

                first.IsPatternStart = true;

                for (int i = 0; i < count; i++)
                {
                    var current = planned[start + i];
                    current.Result = createCircle(current, positions[i]);
                    moveCursor(current, positions[i]);
                }

                return count;
            }

            return 0;
        }

        private int placeMinedPattern(List<PlannedObject> planned, int start, int end)
        {
            var first = planned[start];

            int count = Math.Min(end - start + 1, rng.Next(4, 13));

            if (count < 3)
                return 0;

            var gaps = new List<double>();
            for (int i = 1; i < count; i++)
                gaps.Add((planned[start + i].Time - planned[start + i - 1].Time) / planned[start + i].BeatLength);

            var pattern = MinedPatternSet.Current!.PickJumps(categoryFor(first.Section.Type), count, gaps, rng, iq, recipe.Flavor, recentMined);

            if (pattern == null)
                return 0;

            var offsets = pattern.Offsets.Take(count).ToList();

            double averageGap = Enumerable.Range(1, count - 1).Average(i => planned[start + i].Time - planned[start + i - 1].Time);
            float spacing = jumpDistance(averageGap, first.Tuning);

            float sourceSpacing = offsets.Zip(offsets.Skip(1), (a, b) => Vector2.Distance(a, b)).Average();
            float scale = sourceSpacing < 1 ? 1 : Math.Clamp(spacing / sourceSpacing, 0.5f, 1.9f);

            float angle = randomAngle();
            bool mirror = rng.NextDouble() < 0.5;

            offsets = offsets.Select(o => rotate(new Vector2(mirror ? -o.X : o.X, o.Y) * scale, angle)).ToList();

            // Sometimes nudge the mapper's pattern, so the same pattern doesn't come back identical.
            if (rng.NextDouble() < 0.35)
            {
                float jitter = spacing * 0.15f;
                offsets = offsets.Select((o, i) => i == 0 ? o : o + polar(randomAngle(), randomRange(0, jitter))).ToList();
            }

            var positions = fitToPlayfield(offsets, first);

            first.IsPatternStart = true;

            for (int i = 0; i < count; i++)
            {
                var current = planned[start + i];
                current.Result = createCircle(current, positions[i]);
                moveCursor(current, positions[i]);
            }

            return count;
        }

        /// <summary>
        /// Places circles as a random motif: a short sequence of turns and step lengths, made up for this pattern and repeated,
        /// so it reads as a deliberate shape while being new every time. Each jump is sized from its own gap, so longer
        /// rests get longer jumps, and the walk bends away from edges instead of being shrunk to fit.
        /// </summary>
        /// <returns>The number of circles placed, or zero if the phrase is too short.</returns>
        private int placeMotif(List<PlannedObject> planned, int start, int end)
        {
            var first = planned[start];
            var tuning = first.Tuning;

            int count = Math.Min(end - start + 1, rng.Next(4, 13));

            if (count < 3)
                return 0;

            // Simple maps repeat a short motif (back and forth, the same triangle); smart ones use longer, changing ones.
            // (Never a single repeated step: that is the same back and forth, or the same polygon, for the whole pattern.)
            int motifLength = Math.Min(5, rng.Next(2 + (int)Math.Round(iq * 1.5), 4 + (int)Math.Round(iq * 1.5)));

            var turns = new float[motifLength];
            var steps = new float[motifLength];

            float stepVariation = (float)(0.12 + 0.23 * iq);

            // Simple motifs keep turning the same way, which flows; smart ones switch sides unpredictably.
            // A section may have its own way round (clockwise or not), which its motifs mostly keep.
            int side = recipe.Side != 0 ? recipe.Side : rng.NextDouble() < 0.5 ? -1 : 1;
            // (Smart maps still switch often: keeping one way round makes their sharp motifs into the same polygon over and over.)
            double switchSides = recipe.Side != 0 ? iq * 0.6 : iq * 0.8;

            for (int i = 0; i < motifLength; i++)
            {
                if (rng.NextDouble() < switchSides)
                    side = rng.NextDouble() < 0.5 ? -1 : 1;

                turns[i] = patternCharacter switch
                {
                    SectionCharacter.WideTriangles => randomRange(1.95f, 2.3f),
                    SectionCharacter.Flow => randomRange(0.3f, 0.85f),
                    _ => motifTurn(tuning),
                } * side;

                steps[i] = randomRange(1 - stepVariation, 1 + stepVariation);
            }

            // A repeated motif slowly rotates, the way mappers rotate a repeated pattern; smarter maps rotate more.
            float drift = settings.RotatePatterns ? randomRange(-1, 1) * (float)(0.22 + 0.25 * iq) : 0;

            placeSingleCircle(first);
            first.IsPatternStart = true;

            for (int i = 1; i < count; i++)
            {
                var current = planned[start + i];
                int step = (i - 1) % motifLength;

                var before = planned[start + i - 1];
                float distance = Math.Min(480, jumpDistance(current.Time - before.Time, tuning) * steps[step] * noteSpacing(current, before));

                float turn = turns[step] + (step == motifLength - 1 ? drift : 0);

                // An accent breaks the motif with a sharp turn.
                if (recipe.Accents && isAccent(current.Point))
                    turn = accentTurn();

                Vector2 position;

                // Rare moments: a stop on a finish (stacked on the last note), or a jump back onto the note before last.
                if (isFinish(current.Point) && current.Time - before.Time >= current.BeatLength * 0.45 && spice(0.2))
                    position = cursor;
                else if (i >= 2 && planned[start + i - 2].Result is OsuHitObject twoBack && Vector2.Distance(twoBack.Position, cursor) > distance * 0.6f && spice(0.04))
                    position = twoBack.Position;
                else
                {
                    direction = steerToTerritory(direction + turn);
                    direction = findAngle(direction, a => inBounds(cursor + polar(a, distance)));
                    position = clampToPlayfield(cursor + polar(direction, distance));
                }

                current.Result = createCircle(current, position);
                moveCursor(current, position);
            }

            return count;
        }

        /// <summary>
        /// The size of a turn in a motif (always positive; the caller picks the side). Sharp styles mostly double back like jump maps,
        /// smooth styles mostly carry on in the same direction like flow maps.
        /// </summary>
        private float motifTurn(StyleTuning tuning)
        {
            double roll = rng.NextDouble();

            switch (tuning.TurnMode)
            {
                case TurnMode.Sharp:
                    // Simple: doubling back or wide triangles, the comfortable angles of aim slop.
                    if (rng.NextDouble() >= iq)
                        return roll < 0.42 ? randomRange(2.6f, 3.1f) : roll < 0.8 ? randomRange(2.0f, 2.6f) : randomRange(1.25f, 2.0f);

                    // Smart: right angles (squares), star points, and awkward in-between snaps.
                    return roll < 0.4 ? MathF.PI / 2 + randomRange(-0.15f, 0.15f)
                        : roll < 0.7 ? 2.51f + randomRange(-0.1f, 0.1f)
                        : randomRange(0.6f, 1.4f);

                case TurnMode.Smooth:
                    return roll < 0.7 ? randomRange(0.2f, 0.9f) : randomRange(0.9f, 1.7f);

                default:
                    return roll < 0.4 ? randomRange(2.0f, 3.0f) : roll < 0.75 ? randomRange(1.0f, 2.0f) : randomRange(0.2f, 1.0f);
            }
        }

        /// <summary>
        /// How much a shape would be shrunk to fit on the playfield (1 if it fits as it is).
        /// </summary>
        private float fitScale(List<Vector2> offsets)
        {
            Vector2 size = new Vector2(offsets.Max(o => o.X) - offsets.Min(o => o.X), offsets.Max(o => o.Y) - offsets.Min(o => o.Y));
            Vector2 available = OsuPlayfield.BASE_SIZE - new Vector2(margin * 2);

            return Math.Min(1, Math.Min(available.X / Math.Max(1, size.X), available.Y / Math.Max(1, size.Y)));
        }

        private static PatternCategory categoryFor(SectionType type)
        {
            switch (type)
            {
                case SectionType.Jumps:
                    return PatternCategory.Aim;

                case SectionType.Streams:
                case SectionType.Deathstream:
                    return PatternCategory.Streams;

                default:
                    return PatternCategory.Tech;
            }
        }

        private static Vector2 rotate(Vector2 v, float angle)
        {
            float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
            return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        }

        private JumpPatterns choosePattern(StyleTuning tuning)
        {
            // Shapes far from the map's IQ become rare, but none disappear completely.
            var available = tuning.PatternWeights.Where(w => settings.Patterns.HasFlag(w.Key) && !(shapeLimitReached && w.Key == lastShape))
                                  .Select(w => new KeyValuePair<JumpPatterns, double>(w.Key, w.Value * (0.08 + Math.Exp(-Math.Pow(shapeIq(w.Key) - iq, 2) / (2 * 0.22 * 0.22)))))
                                  .ToList();

            // With every shape disabled, fall back to unstructured jumps.
            if (available.Count == 0)
                return JumpPatterns.RandomJumps;

            double roll = rng.NextDouble() * available.Sum(w => w.Value);

            foreach (var (pattern, weight) in available)
            {
                roll -= weight;
                if (roll <= 0)
                    return pattern;
            }

            return available[^1].Key;
        }

        /// <summary>
        /// Positions a shape so that it lies within the playfield and its first object is roughly a jump away from the cursor.
        /// Shapes which can't fit are scaled down.
        /// </summary>
        private List<Vector2> fitToPlayfield(List<Vector2> offsets, PlannedObject first)
        {
            Vector2 min = new Vector2(offsets.Min(o => o.X), offsets.Min(o => o.Y));
            Vector2 max = new Vector2(offsets.Max(o => o.X), offsets.Max(o => o.Y));

            Vector2 available = OsuPlayfield.BASE_SIZE - new Vector2(margin * 2);
            Vector2 size = max - min;

            float scale = Math.Min(1, Math.Min(available.X / Math.Max(1, size.X), available.Y / Math.Max(1, size.Y)));

            if (scale < 1)
            {
                offsets = offsets.Select(o => o * scale).ToList();
                min *= scale;
                max *= scale;
            }

            // The range of shape origins which keep every object in bounds.
            Vector2 originMin = new Vector2(margin) - min;
            Vector2 originMax = OsuPlayfield.BASE_SIZE - new Vector2(margin) - max;

            Vector2 origin;

            if (previous == null || previous.Kind == ObjectKind.Spinner)
                origin = playfield_centre - (min + max) / 2;
            else
            {
                float lead = Math.Min(480, jumpDistance(jumpGap(first.Time), first.Tuning) * noteSpacing(first, previous));

                origin = Vector2.Zero;
                float bestError = float.MaxValue;

                for (int attempt = 0; attempt < 12; attempt++)
                {
                    Vector2 target = cursor + polar(steerToTerritory(direction + nextTurn(first.Tuning)) + attempt * 0.5f, lead);
                    Vector2 candidate = Vector2.Clamp(target - offsets[0], originMin, originMax);

                    float error = Vector2.Distance(candidate + offsets[0], target);

                    if (error < bestError)
                    {
                        bestError = error;
                        origin = candidate;
                    }

                    if (error < 1)
                        break;
                }
            }

            return offsets.Select(o => o + origin).ToList();
        }

        private void placeStream(List<PlannedObject> planned, int start, int end)
        {
            var head = planned[start];
            var tuning = head.Tuning;

            // The head is jumped to like any other object; the rest follows a shape.
            placeSingleCircle(head);
            head.IsPatternStart = true;

            int length = end - start + 1;

            float spacing = streamSpacing(tuning);

            // Short bursts outside stream sections are spaced out at high star ratings (spaced triples and bursts),
            // which is how hard maps on slower songs get their difficulty.
            if (length <= 5 && head.Section.Type is not (SectionType.Streams or SectionType.Deathstream))
                spacing *= 1 + (float)Math.Clamp((profile.Stars - 5.5) / 2, 0, 1.5);

            // A triple is always a stack: three notes on one spot. Longer aim bursts get their own look:
            // almost stacked, as usual, or spaced a little more.
            if (length <= 13 && head.Section.Type is not (SectionType.Streams or SectionType.Deathstream))
            {
                double look = rng.NextDouble();

                if (head.Point.StackBurst || length == 3)
                    spacing = 0;
                // A double is a stack, exactly as a triple: two notes on one spot.
                else if (length == 2)
                    spacing = 0;
                else
                    spacing = look < 0.25 ? margin * 0.25f : look < 0.65 ? spacing : spacing * 1.25f;
            }

            // Head somewhere with room for the stream, rather than running along an edge.
            float reach = Math.Min(spacing * (length - 1), 220);
            direction = findAngle(direction, a => inBounds(cursor + polar(a, reach)));

            // Long streams change shape every so often, so a deathstream winds around instead of repeating one curve.
            StreamShape shape = default;
            float curvature = 0;
            int segmentStart = 0, segmentLength = 0;

            // When a mapper's stream drives a segment, its turns and step lengths are replayed.
            List<float>? minedTurns = null;
            List<float>? minedSteps = null;

            for (int i = start + 1; i <= end; i++)
            {
                int index = i - start;

                if (index - segmentStart >= segmentLength)
                {
                    shape = (StreamShape)rng.Next(Enum.GetValues<StreamShape>().Length);
                    curvature = randomRange(0.12f, 0.3f) * (rng.NextDouble() < 0.5 ? -1 : 1);
                    segmentStart = index;
                    segmentLength = length <= 24 ? length : rng.Next(8, 17);

                    minedTurns = null;
                    minedSteps = null;

                    var mined = rng.NextDouble() < mined_pattern_chance ? MinedPatternSet.Current?.PickStream(Math.Min(segmentLength + 1, 16), rng) : null;

                    if (mined != null)
                        (minedTurns, minedSteps) = streamMotion(mined, mirror: rng.NextDouble() < 0.5);
                }

                int segmentIndex = index - segmentStart;
                float stepSpacing = spacing;

                if (minedTurns != null && minedSteps != null)
                {
                    direction += minedTurns[segmentIndex % minedTurns.Count];
                    stepSpacing = spacing * minedSteps[segmentIndex % minedSteps.Count];
                }
                else
                    switch (shape)
                {
                    case StreamShape.Arc:
                        direction += curvature;
                        break;

                    case StreamShape.SCurve:
                        direction += segmentIndex < segmentLength / 2 ? curvature : -curvature;
                        break;

                    case StreamShape.Square:
                        // Straight runs with a right-angle turn every four notes.
                        if (segmentIndex % 4 == 3)
                            direction += MathF.PI / 2 * MathF.Sign(curvature);
                        break;

                    case StreamShape.Triangle:
                        if (segmentIndex % 5 == 4)
                            direction += MathF.Tau / 3 * MathF.Sign(curvature);
                        break;

                    case StreamShape.Zigzag:
                        direction += (segmentIndex % 2 == 0 ? 1 : -1) * 0.9f;
                        break;

                    case StreamShape.Wave:
                        direction += MathF.Cos(segmentIndex * 0.7f) * 0.35f * MathF.Sign(curvature);
                        break;

                    case StreamShape.Spiral:
                        // Tightening curve.
                        direction += curvature * (0.6f + segmentIndex * 0.08f);
                        break;

                    case StreamShape.Straight:
                        break;
                }

                // Turn back towards the centre near edges, so long streams don't scrape along them.
                direction += steerFromEdges(stepSpacing);
                direction = findAngle(direction, a => inBounds(cursor + polar(a, stepSpacing)));

                Vector2 position = clampToPlayfield(cursor + polar(direction, stepSpacing));

                planned[i].Result = createCircle(planned[i], position);
                moveCursor(planned[i], position, updateDirection: minedTurns != null || shape != StreamShape.Zigzag);
            }
        }

        /// <summary>
        /// Distance between stream notes. Streams overlap: notes sit well under a circle's diameter apart, closer at low star ratings.
        /// </summary>
        private float streamSpacing(StyleTuning tuning)
        {
            float overlap = (float)Math.Clamp(0.55 + 0.06 * profile.Stars, 0.6, 1.1);
            return margin * overlap * (float)(settings.StreamSpacing * tuning.StreamSpacingMultiplier);
        }

        /// <summary>
        /// Converts a mapper's stream into per-note turns (radians) and step lengths (relative to its average step).
        /// </summary>
        private static (List<float> turns, List<float> steps) streamMotion(MinedPattern stream, bool mirror)
        {
            var steps = stream.Offsets.Zip(stream.Offsets.Skip(1), (a, b) => b - a).ToList();
            float average = Math.Max(1, steps.Average(s => s.Length));

            var turns = new List<float> { 0 };

            for (int i = 1; i < steps.Count; i++)
            {
                float turn = MathF.Atan2(steps[i].Y, steps[i].X) - MathF.Atan2(steps[i - 1].Y, steps[i - 1].X);
                turn = MathF.IEEERemainder(turn, MathF.Tau);
                turns.Add(mirror ? -turn : turn);
            }

            return (turns, steps.Select(s => Math.Clamp(s.Length / average, 0.6f, 1.5f)).ToList());
        }

        /// <summary>
        /// A turn towards the centre of the playfield, growing as the cursor heads towards a nearby edge.
        /// </summary>
        private float steerFromEdges(float spacing)
        {
            const float comfort_zone = 70;

            Vector2 ahead = cursor + polar(direction, spacing * 3);

            float distanceToEdge = Math.Min(
                Math.Min(ahead.X - margin, OsuPlayfield.BASE_SIZE.X - margin - ahead.X),
                Math.Min(ahead.Y - margin, OsuPlayfield.BASE_SIZE.Y - margin - ahead.Y));

            if (distanceToEdge >= comfort_zone)
                return 0;

            Vector2 toCentre = playfield_centre - cursor;
            float angleToCentre = MathF.Atan2(toCentre.Y, toCentre.X);
            float difference = MathF.IEEERemainder(angleToCentre - direction, MathF.Tau);

            float strength = Math.Clamp(1 - distanceToEdge / comfort_zone, 0, 1) * 0.35f;
            return Math.Clamp(difference, -strength, strength);
        }

        private void placeSingleCircle(PlannedObject planned)
        {
            Vector2 position = nextPosition(planned);
            planned.Result = createCircle(planned, position);
            moveCursor(planned, position);
        }

        private void placeSlider(PlannedObject planned)
        {
            Vector2 position = nextPosition(planned);

            var (path, endOffset, travelAngle) = createPath(position, planned.SpanLength, planned.Tuning);

            bool endsAtTail = planned.Spans % 2 == 1;

            var samples = samplesFor(planned.Point);

            planned.Result = new Slider
            {
                StartTime = planned.Time,
                Position = position,
                Path = path,
                RepeatCount = planned.Spans - 1,
                Samples = samples,
                NodeSamples = Enumerable.Range(0, planned.Spans + 1).Select(_ => (IList<HitSampleInfo>)samples.ToList()).ToList(),
            };

            cursor = endsAtTail ? position + endOffset : position;
            direction = endsAtTail ? travelAngle : travelAngle + MathF.PI;
            previous = planned;
        }

        private void placeSpinner(PlannedObject planned)
        {
            planned.Result = new Spinner
            {
                StartTime = planned.Time,
                EndTime = planned.EndTime,
                Position = playfield_centre,
                Samples = samplesFor(planned.Point),
            };

            cursor = playfield_centre;
            direction = randomAngle();
            previous = planned;
        }

        private HitCircle createCircle(PlannedObject planned, Vector2 position) => new HitCircle
        {
            StartTime = planned.Time,
            Position = position,
            Samples = samplesFor(planned.Point),
        };

        private void moveCursor(PlannedObject planned, Vector2 position, bool updateDirection = true)
        {
            Vector2 movement = position - cursor;

            if (updateDirection && movement.LengthSquared > 1)
                direction = MathF.Atan2(movement.Y, movement.X);

            cursor = position;
            previous = planned;
        }

        private (SliderPath path, Vector2 endOffset, float angle) createPath(Vector2 start, double length, StyleTuning tuning)
        {
            float len = (float)length;

            if (len >= 120 && rng.NextDouble() < tuning.WaveChance)
            {
                var wave = createWavePath(start, length, tuning);

                // Bezier curves don't end exactly where their last control point is, so check where it really ends.
                if (inBounds(start + wave.endOffset))
                    return wave;
            }

            bool curved = len >= 40 && rng.NextDouble() < tuning.CurveChance;

            // Bend is the sagitta as a fraction of the chord. Arc length ≈ chord * (1 + 8k²/3).
            float bend = curved ? (float)(0.15 + rng.NextDouble() * 0.2) * (rng.NextDouble() < 0.5 ? -1 : 1) : 0;
            float chord = len / (1 + 8 * bend * bend / 3);

            float baseAngle = direction + randomRange(-tuning.SliderTurn, tuning.SliderTurn);
            float angle = findAngle(baseAngle, a => inBounds(start + polar(a, chord)));

            Vector2 along = polar(angle, 1);
            Vector2 perpendicular = new Vector2(-along.Y, along.X);
            Vector2 end = along * chord;

            PathControlPoint[] pathPoints = curved
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

            return (new SliderPath(pathPoints, length), end, angle);
        }

        /// <summary>
        /// A long slider which snakes side to side along its direction.
        /// </summary>
        private (SliderPath path, Vector2 endOffset, float angle) createWavePath(Vector2 start, double length, StyleTuning tuning)
        {
            int waves = rng.Next(2, 4);
            float amplitude = randomRange(0.12f, 0.2f);

            // The curve is longer than its chord; ExpectedDistance trims any excess.
            float chord = (float)length / (1 + 4 * amplitude);

            float angle = findAngle(direction + randomRange(-tuning.SliderTurn, tuning.SliderTurn), a => inBounds(start + polar(a, chord)));

            Vector2 along = polar(angle, 1);
            Vector2 perpendicular = new Vector2(-along.Y, along.X);

            var pathPoints = new List<PathControlPoint> { new PathControlPoint(Vector2.Zero, PathType.BEZIER) };

            for (int i = 1; i <= waves * 2; i++)
            {
                float progress = (float)i / (waves * 2);
                float side = i == waves * 2 ? 0 : i % 2 == 1 ? 1 : -1;

                pathPoints.Add(new PathControlPoint(along * chord * progress + perpendicular * chord * amplitude * side));
            }

            var path = new SliderPath(pathPoints.ToArray(), length);
            return (path, path.PositionAt(1), angle);
        }

        /// <summary>
        /// Positions a lone object a jump away from the cursor.
        /// </summary>
        private Vector2 nextPosition(PlannedObject planned)
        {
            if (previous == null || previous.Kind == ObjectKind.Spinner)
                return playfield_centre;

            double gap = jumpGap(planned.Time);

            float distance = planned.IsBurstGap
                ? streamSpacing(planned.Tuning)
                : Math.Min(480, jumpDistance(gap, planned.Tuning) * noteSpacing(planned, previous));

            float turn = planned.IsBurstGap ? randomRange(-0.35f, 0.35f) : nextTurn(planned.Tuning);

            if (!planned.IsBurstGap && recipe.Accents && isAccent(planned.Point))
                turn = accentTurn();

            direction = findAngle(planned.IsBurstGap ? direction + turn : steerToTerritory(direction + turn), a => inBounds(cursor + polar(a, distance)));

            return clampToPlayfield(cursor + polar(direction, distance));
        }

        /// <summary>
        /// The distance to jump across a gap (in milliseconds), based on the star rating, style and spacing setting.
        /// </summary>
        private float jumpDistance(double gap, StyleTuning tuning)
        {
            bool antiShort = false;

            if ((settings.AntiJumps || recipe.Character == SectionCharacter.AntiJumps) && previous != null && gap > 0 && gap <= 1000)
            {
                // Anti-jumps: quick notes get a full beat's jump, slow ones a short hop.
                double beat = previous.BeatLength;
                antiShort = gap >= beat * 0.75;
                gap = antiShort ? beat * 0.35 : beat * 1.15;
            }

            double distance = Math.Min(profile.MaxJumpDistance, profile.AimVelocity * gap) * tuning.DistanceMultiplier * settings.JumpSpacing * settings.CalibrationSpacing * kiaiSpacing * patternScale;

            if (gap > 1000)
            {
                // Long rests don't need to be followed by huge jumps.
                distance = Math.Min(distance, 200);
            }
            else
            {
                // Calmer styles have proportionally smaller minimums; the player's own spacing setting still applies.
                double floor = profile.MinJumpDistance * Math.Min(1, tuning.DistanceMultiplier) * settings.JumpSpacing;

                jumpCount++;

                // The short hops of anti-jumps are meant to be short.
                if (antiShort)
                    floor *= 0.45;

                // Aim jumping every half beat hits harder per jump: smaller ones are still real jumps there, and let it reach lower star ratings.
                if (profile.FlowJumps && previous != null && gap <= previous.BeatLength * 0.6)
                    floor *= 0.55;

                if (distance < floor)
                {
                    distance = floor;
                    flooredJumps++;
                }
            }

            return (float)Math.Clamp(distance * randomRange(0.92f, 1.08f), 30, 480);
        }

        private int jumpCount, flooredJumps;

        /// <summary>
        /// The current jump pattern's size relative to the rest (see <see cref="placeJumpPattern"/>).
        /// </summary>
        private float patternScale = 1;

        /// <summary>
        /// How many jumps were raised to <see cref="DifficultyProfile.MinJumpDistance"/>. When most were, shrinking jumps further does nothing,
        /// and a map that's still too hard needs fewer notes instead.
        /// </summary>
        public double FlooredJumpShare => jumpCount == 0 ? 0 : (double)flooredJumps / jumpCount;

        /// <summary>
        /// The time a jump to <paramref name="time"/> has, from the end of the previous object.
        /// A short slider (like a kick slider) is jumped from as if it were a circle, or the jump after it ends up tiny.
        /// </summary>
        private double jumpGap(double time)
        {
            if (previous == null)
                return 0;

            if (previous.Kind == ObjectKind.Slider && previous.EndTime - previous.Time < previous.BeatLength * 0.6)
                return time - previous.Time;

            return time - previous.EndTime;
        }

        private float nextTurn(StyleTuning tuning)
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

        private static IList<HitSampleInfo> samplesFor(RhythmPoint point)
            => point.Samples.Count > 0 ? point.Samples.ToList() : new List<HitSampleInfo> { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) };

        #endregion

        #region Combos

        private static void applyCombos(List<PlannedObject> planned)
        {
            int comboLength = 0;
            long comboMeasure = long.MinValue;
            bool afterSpinner = false;

            foreach (var current in planned)
            {
                var timingPoint = current.Timing;
                double measureLength = timingPoint.BeatLength * timingPoint.TimeSignature.Numerator;
                long measure = (long)Math.Floor((current.Time - timingPoint.Time) / measureLength) + (long)timingPoint.Time * 1000;

                bool newCombo = comboLength == 0
                                || afterSpinner
                                || current.Kind == ObjectKind.Spinner
                                || current.StyleChanged
                                || (current.IsPatternStart && comboLength >= 3)
                                || (comboLength >= 6 && measure != comboMeasure)
                                || comboLength >= 12;

                if (newCombo)
                {
                    current.Result!.NewCombo = true;
                    comboLength = 0;
                    comboMeasure = measure;
                }

                comboLength++;
                afterSpinner = current.Kind == ObjectKind.Spinner;
            }
        }

        #endregion

        #region Helpers

        private bool inBounds(Vector2 position)
            => position.X >= margin && position.Y >= margin && position.X <= OsuPlayfield.BASE_SIZE.X - margin && position.Y <= OsuPlayfield.BASE_SIZE.Y - margin;

        private Vector2 clampToPlayfield(Vector2 position) => Vector2.Clamp(position, new Vector2(margin), OsuPlayfield.BASE_SIZE - new Vector2(margin));

        private static Vector2 polar(float angle, float length) => new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length;

        private float randomRange(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        private float randomAngle() => randomRange(0, MathF.Tau);

        #endregion

        private enum ObjectKind
        {
            Circle,
            Slider,
            Spinner,
        }

        private enum StreamShape
        {
            Arc,
            SCurve,
            Square,
            Triangle,
            Zigzag,
            Wave,
            Spiral,
            Straight,
        }

        private enum TurnMode
        {
            Smooth,
            Sharp,
            Varied,
        }

        private class PlannedObject
        {
            public RhythmPoint Point = null!;
            public double Time;
            public double EndTime;
            public double BeatLength;
            public TimingControlPoint Timing = null!;
            public ObjectKind Kind;
            public int Spans;
            public double SpanLength;

            /// <summary>
            /// Whether the gap from the previous object is a 1/4-style stream gap.
            /// </summary>
            public bool IsBurstGap;

            public Section Section = null!;
            public StyleTuning Tuning = null!;
            public bool StyleChanged;
            public bool IsPatternStart;

            public OsuHitObject? Result;
        }

        private class StyleTuning
        {
            public double MinGapBeats;
            public int MaxBurstGaps;
            public double DistanceMultiplier;
            public double StreamSpacingMultiplier = 1;
            public TurnMode TurnMode;
            public float SliderTurn;
            public double SliderChance;
            public double KeepHoldChance;
            public double RepeatChance;
            public double CurveChance;
            public double WaveChance;
            public Dictionary<JumpPatterns, double> PatternWeights = new Dictionary<JumpPatterns, double>();

            public static StyleTuning For(SectionType type, DifficultyProfile profile, FreeplayGenerationSettings settings)
            {
                var tuning = create(type, profile);

                if (!settings.AllowStreams)
                    tuning.MaxBurstGaps = 0;

                // Aim with bursts turned on: triples and bursts of up to 13 notes, never long streams.
                if (type == SectionType.Jumps && SectionPlanner.BurstsAllowed(type, profile, settings))
                    tuning.MaxBurstGaps = Math.Min(profile.MaxBurstGaps, 12);

                tuning.SliderChance = Math.Min(1, tuning.SliderChance * settings.SliderAmount);
                tuning.KeepHoldChance = Math.Min(1, tuning.KeepHoldChance * settings.SliderAmount);

                return tuning;
            }

            private static StyleTuning create(SectionType type, DifficultyProfile profile)
            {
                switch (type)
                {
                    case SectionType.Jumps:
                        return new StyleTuning
                        {
                            // Pure aim: no 1/4 rhythms at all.
                            MinGapBeats = Math.Max(profile.MinGapBeats, 0.5),
                            MaxBurstGaps = 0,
                            DistanceMultiplier = 1.2,
                            TurnMode = TurnMode.Sharp,
                            SliderTurn = 1.2f,
                            SliderChance = 0.05,
                            KeepHoldChance = 0.3,
                            RepeatChance = 0.05,
                            CurveChance = 0.3,
                            PatternWeights =
                            {
                                [JumpPatterns.Squares] = 3,
                                [JumpPatterns.Stars] = 3,
                                [JumpPatterns.Triangles] = 2,
                                [JumpPatterns.PingPong] = 2,
                                [JumpPatterns.Zigzags] = 2,
                                [JumpPatterns.Polygons] = 1,
                                [JumpPatterns.RandomJumps] = 1,
                                [JumpPatterns.Crosses] = 2,
                                [JumpPatterns.Spirals] = 1.5,
                                [JumpPatterns.Stacks] = 1,
                            },
                        };

                    case SectionType.Streams:
                    case SectionType.Deathstream:
                        // Notes come from SectionPlanner's generated streams, so only stream heads are ever jumped to.
                        return new StyleTuning
                        {
                            MinGapBeats = profile.MinGapBeats,
                            MaxBurstGaps = int.MaxValue,
                            DistanceMultiplier = 1,
                            StreamSpacingMultiplier = type == SectionType.Deathstream ? 0.7 : 1,
                            TurnMode = TurnMode.Smooth,
                            SliderTurn = 0.6f,
                            SliderChance = 0,
                            KeepHoldChance = 0,
                            RepeatChance = 0,
                            CurveChance = 0,
                            PatternWeights =
                            {
                                [JumpPatterns.PingPong] = 2,
                                [JumpPatterns.Triangles] = 1,
                                [JumpPatterns.RandomJumps] = 1,
                            },
                        };

                    case SectionType.Flow:
                        return new StyleTuning
                        {
                            MinGapBeats = profile.MinGapBeats,
                            MaxBurstGaps = profile.MaxBurstGaps,
                            DistanceMultiplier = 0.85,
                            TurnMode = TurnMode.Smooth,
                            SliderTurn = 0.5f,
                            SliderChance = 0.25,
                            KeepHoldChance = 0.9,
                            RepeatChance = 0.12,
                            CurveChance = 0.7,
                            WaveChance = 0.15,
                            PatternWeights =
                            {
                                [JumpPatterns.Polygons] = 3,
                                [JumpPatterns.Spirals] = 2,
                                [JumpPatterns.Squares] = 1,
                                [JumpPatterns.Triangles] = 1,
                                [JumpPatterns.Zigzags] = 1,
                            },
                        };

                    default:
                        return new StyleTuning
                        {
                            MinGapBeats = profile.MinGapBeats,
                            MaxBurstGaps = profile.MaxBurstGaps,
                            DistanceMultiplier = 0.75,
                            TurnMode = TurnMode.Varied,
                            SliderTurn = 1.0f,
                            SliderChance = 0.6,
                            KeepHoldChance = 1,
                            RepeatChance = 0.35,
                            CurveChance = 0.6,
                            WaveChance = 0.3,
                            PatternWeights =
                            {
                                [JumpPatterns.RandomJumps] = 2,
                                [JumpPatterns.PingPong] = 1,
                                [JumpPatterns.Triangles] = 1,
                                [JumpPatterns.Stacks] = 1,
                            },
                        };
                }
            }
        }
    }
}
