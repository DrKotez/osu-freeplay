// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Replays;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Configuration
{
    /// <summary>
    /// A quarter of the playfield. Top is the top of the screen.
    /// </summary>
    public enum PlayfieldQuarter
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    /// <summary>
    /// Which way a jump went (the larger of its sideways and up-down parts).
    /// </summary>
    public enum JumpDirection
    {
        Right,
        Left,
        Up,
        Down,
    }

    /// <summary>
    /// Where the player's clicks went, worked out from every press of a track: why notes were missed, in which quarter
    /// of the playfield and on which jumps, and how close to the centre the hits were.
    /// </summary>
    public class AimAnalysis
    {
        /// <summary>
        /// Clicks further than this many circle radii from the note are a misread (the player went for something else).
        /// </summary>
        public const float MISREAD_RADII = 3;

        /// <summary>
        /// Hits within this share of the radius are in the centre; past <see cref="EDGE"/> they're on the edge.
        /// </summary>
        public const float CENTRE = 1 / 3f, EDGE = 2 / 3f;

        /// <summary>
        /// How many hit positions a track keeps for the scatter picture.
        /// </summary>
        private const int max_scatter_per_track = 400;

        /// <summary>
        /// Circles and slider heads.
        /// </summary>
        public int Notes { get; set; }

        /// <summary>
        /// Clicked, but outside the circle ("мимо"): the misses this is about.
        /// </summary>
        public int AimMisses { get; set; }

        /// <summary>
        /// Of <see cref="AimMisses"/>: short of the note along the jump.
        /// </summary>
        public int Undershoot { get; set; }

        /// <summary>
        /// Of <see cref="AimMisses"/>: past the note along the jump.
        /// </summary>
        public int Overshoot { get; set; }

        /// <summary>
        /// Of <see cref="AimMisses"/>: very far from the note, or nearer another one coming up.
        /// </summary>
        public int Misreads { get; set; }

        /// <summary>
        /// Clicked in the circle, but at the wrong time.
        /// </summary>
        public int TimingMisses { get; set; }

        /// <summary>
        /// Not clicked at all.
        /// </summary>
        public int NoClickMisses { get; set; }

        public int[] QuarterNotes { get; set; } = new int[4];
        public int[] QuarterMisses { get; set; } = new int[4];
        public int[] DirectionNotes { get; set; } = new int[4];
        public int[] DirectionMisses { get; set; } = new int[4];

        public int CentreHits { get; set; }
        public int MiddleHits { get; set; }
        public int EdgeHits { get; set; }

        /// <summary>
        /// Where hits landed from the note's centre, in radii, as x, y pairs.
        /// </summary>
        public List<float> Scatter { get; set; } = new List<float>();

        [JsonIgnore]
        public int Hits => CentreHits + MiddleHits + EdgeHits;

        /// <summary>
        /// The share of a quarter's notes missed by aim.
        /// </summary>
        public double QuarterMissRate(PlayfieldQuarter quarter) => rate(QuarterMisses[(int)quarter], QuarterNotes[(int)quarter]);

        public double DirectionMissRate(JumpDirection direction) => rate(DirectionMisses[(int)direction], DirectionNotes[(int)direction]);

        private static double rate(int misses, int notes) => notes == 0 ? 0 : (double)misses / notes;

        /// <summary>
        /// The quarter missed most often (by share of its notes), or null with too few misses to tell.
        /// </summary>
        public PlayfieldQuarter? WeakestQuarter(int minimumMisses = 30)
        {
            if (AimMisses < minimumMisses)
                return null;

            return Enum.GetValues<PlayfieldQuarter>().MaxBy(QuarterMissRate);
        }

        public void Add(AimAnalysis other)
        {
            Notes += other.Notes;
            AimMisses += other.AimMisses;
            Undershoot += other.Undershoot;
            Overshoot += other.Overshoot;
            Misreads += other.Misreads;
            TimingMisses += other.TimingMisses;
            NoClickMisses += other.NoClickMisses;
            CentreHits += other.CentreHits;
            MiddleHits += other.MiddleHits;
            EdgeHits += other.EdgeHits;

            for (int i = 0; i < 4; i++)
            {
                QuarterNotes[i] += other.QuarterNotes[i];
                QuarterMisses[i] += other.QuarterMisses[i];
                DirectionNotes[i] += other.DirectionNotes[i];
                DirectionMisses[i] += other.DirectionMisses[i];
            }

            Scatter.AddRange(other.Scatter);
        }

        /// <summary>
        /// Everything of <paramref name="runs"/> (most recent last) taken together, or null if none was analysed.
        /// </summary>
        public static AimAnalysis? Combine(IEnumerable<AimAnalysis?> analyses)
        {
            AimAnalysis? total = null;

            foreach (var analysis in analyses.OfType<AimAnalysis>())
                (total ??= new AimAnalysis()).Add(analysis);

            return total;
        }

        /// <summary>
        /// How many of the latest analysed runs are taken together, for the statistics and <see cref="Generation.WeakZone.Auto"/>.
        /// </summary>
        public const int LATEST_RUNS = 10;

        /// <summary>
        /// The latest analysed runs of <paramref name="history"/> (oldest first) taken together, and how many there were.
        /// </summary>
        public static (AimAnalysis? analysis, int runs) Latest(IEnumerable<RunRecord> history)
        {
            var runs = history.Where(r => r.Tracks.Any(t => t.Aim != null)).TakeLast(LATEST_RUNS).ToList();
            return (Combine(runs.Select(r => r.Aim)), runs.Count);
        }

        public static PlayfieldQuarter QuarterOf(Vector2 position) =>
            (position.X < OsuPlayfield.BASE_SIZE.X / 2, position.Y < OsuPlayfield.BASE_SIZE.Y / 2) switch
            {
                (true, true) => PlayfieldQuarter.TopLeft,
                (false, true) => PlayfieldQuarter.TopRight,
                (true, false) => PlayfieldQuarter.BottomLeft,
                _ => PlayfieldQuarter.BottomRight,
            };

        public static JumpDirection DirectionOf(Vector2 jump) =>
            Math.Abs(jump.X) >= Math.Abs(jump.Y)
                ? jump.X >= 0 ? JumpDirection.Right : JumpDirection.Left
                : jump.Y >= 0 ? JumpDirection.Down : JumpDirection.Up;

        /// <summary>
        /// Works out a track from its judgements and the presses in its replay (all in playfield coordinates).
        /// </summary>
        /// <param name="singleTap">Only one key counts (Singletap): presses of the other, which osu! ignores, aren't clicks.</param>
        public static AimAnalysis Analyse(IEnumerable<HitObject> beatmapObjects, IReadOnlyList<HitEvent> hitEvents, IEnumerable<ReplayFrame> frames, bool singleTap = false)
        {
            var analysis = new AimAnalysis();

            var objects = beatmapObjects.OfType<OsuHitObject>().OrderBy(h => h.StartTime).ToList();
            var presses = Presses(frames, singleTap);
            var used = new bool[presses.Count];

            // Circles and slider heads (which are circles too), with what came before each.
            var notes = hitEvents.Where(e => e.HitObject is HitCircle && e.Result != HitResult.None && e.Result != HitResult.IgnoreMiss)
                                 .OrderBy(e => e.HitObject.StartTime)
                                 .ToList();

            // The presses that hit something first, so a miss is never pinned on a press that hit the note before.
            foreach (var hit in notes.Where(e => e.Result.IsHit()))
            {
                double time = hit.HitObject.StartTime + hit.TimeOffset;
                int nearest = nearestPress(presses, used, time, 4);

                if (nearest >= 0)
                    used[nearest] = true;
            }

            foreach (var e in notes)
            {
                var circle = (HitCircle)e.HitObject;
                Vector2 centre = circle.StackedPosition;
                float radius = (float)circle.Radius;
                Vector2? from = previousPosition(objects, circle);

                analysis.Notes++;
                var quarter = QuarterOf(centre);
                analysis.QuarterNotes[(int)quarter]++;

                JumpDirection? direction = null;

                if (from is Vector2 start && Vector2.Distance(start, centre) > radius)
                {
                    direction = DirectionOf(centre - start);
                    analysis.DirectionNotes[(int)direction.Value]++;
                }

                if (e.Result.IsHit())
                {
                    if (e.Position is Vector2 position)
                    {
                        Vector2 offset = (position - centre) / radius;
                        float length = offset.Length;

                        if (length < CENTRE)
                            analysis.CentreHits++;
                        else if (length < EDGE)
                            analysis.MiddleHits++;
                        else
                            analysis.EdgeHits++;

                        if (analysis.Scatter.Count < max_scatter_per_track * 2)
                        {
                            analysis.Scatter.Add(MathF.Round(offset.X, 2));
                            analysis.Scatter.Add(MathF.Round(offset.Y, 2));
                        }
                    }

                    continue;
                }

                double window = circle.HitWindows.WindowFor(HitResult.Meh);
                int press = nearestPress(presses, used, circle.StartTime, window);

                if (press < 0)
                {
                    // A click well outside the window is a timing miss too (too early or late), rather than none.
                    if (nearestPress(presses, used, circle.StartTime, window * 2) >= 0)
                        analysis.TimingMisses++;
                    else
                        analysis.NoClickMisses++;
                    continue;
                }

                used[press] = true;
                var (pressTime, pressPosition) = presses[press];
                float distance = Vector2.Distance(pressPosition, centre);

                if (distance <= radius)
                {
                    analysis.TimingMisses++;
                    continue;
                }

                analysis.AimMisses++;
                analysis.QuarterMisses[(int)quarter]++;

                if (direction != null)
                    analysis.DirectionMisses[(int)direction.Value]++;

                // Nearer a note coming up than the one it was for: the player went for that one.
                bool nearerAnother = objects.Any(o => o.StartTime > circle.StartTime && o.StartTime - pressTime < o.TimePreempt && o != circle
                                                      && Vector2.Distance(pressPosition, o.StackedPosition) < distance);

                if (distance > radius * MISREAD_RADII || nearerAnother)
                    analysis.Misreads++;
                else if (from is Vector2 origin && Vector2.Distance(origin, centre) > radius)
                {
                    Vector2 along = Vector2.Normalize(centre - origin);

                    if (Vector2.Dot(pressPosition - centre, along) > 0)
                        analysis.Overshoot++;
                    else
                        analysis.Undershoot++;
                }
            }

            return analysis;
        }

        /// <summary>
        /// When each button went down, and where the cursor was.
        /// </summary>
        public static List<(double time, Vector2 position)> Presses(IEnumerable<ReplayFrame> frames, bool singleTap = false)
        {
            var presses = new List<(double, Vector2)>();
            var held = new HashSet<OsuAction>();

            // FIX: with Singletap, the other key's presses (blocked by osu!) used to count as clicks wide of the notes.
            OsuAction? only = null;

            foreach (var frame in frames.OfType<OsuReplayFrame>())
            {
                var pressed = frame.Actions.Where(a => (a == OsuAction.LeftButton || a == OsuAction.RightButton) && !held.Contains(a)).ToList();

                if (singleTap && pressed.Count > 0)
                {
                    only ??= pressed[0];
                    pressed.RemoveAll(a => a != only);
                }

                if (pressed.Count > 0)
                    presses.Add((frame.Time, frame.Position));

                held.Clear();
                held.UnionWith(frame.Actions);
            }

            return presses;
        }

        private static int nearestPress(List<(double time, Vector2 position)> presses, bool[] used, double time, double window)
        {
            int best = -1;
            double bestGap = double.MaxValue;

            for (int i = 0; i < presses.Count; i++)
            {
                if (used[i])
                    continue;

                double gap = Math.Abs(presses[i].time - time);

                if (gap <= window && gap < bestGap)
                {
                    best = i;
                    bestGap = gap;
                }
            }

            return best;
        }

        /// <summary>
        /// Where the cursor comes from to <paramref name="circle"/>: the end of the object before it, if that's recent enough to be part of the same movement.
        /// </summary>
        private static Vector2? previousPosition(List<OsuHitObject> objects, HitCircle circle)
        {
            // A slider's head starts where the slider does, so the slider itself isn't what came before.
            var previous = objects.LastOrDefault(o => o.StartTime < circle.StartTime - 1 && !(o is Slider slider && slider.HeadCircle == circle));

            if (previous == null || circle.StartTime - previous.GetEndTime() > 2000)
                return null;

            return previous.StackedEndPosition;
        }
    }
}
