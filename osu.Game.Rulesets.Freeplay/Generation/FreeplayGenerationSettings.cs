// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Rulesets.Freeplay.Generation
{
    /// <summary>
    /// User-facing settings which control how hit objects are generated.
    /// </summary>
    public class FreeplayGenerationSettings
    {
        /// <summary>
        /// The settings used by the next generated beatmap.
        /// </summary>
        public static FreeplayGenerationSettings Current { get; set; } = new FreeplayGenerationSettings();

        public PatternStyle Style { get; set; } = PatternStyle.Mixed;

        public RhythmSource RhythmSource { get; set; } = RhythmSource.Auto;

        /// <summary>
        /// The approximate star rating to aim for.
        /// </summary>
        public double StarRating { get; set; } = 4;

        // Null means "derive from star rating".
        public float? CircleSize { get; set; }
        public float? ApproachRate { get; set; }
        public float? OverallDifficulty { get; set; }
        public float? DrainRate { get; set; }

        /// <summary>
        /// A fixed seed, for repeating an attempt. Null generates a new pattern each time.
        /// </summary>
        public int? Seed { get; set; }

        public FreeplayGenerationSettings Clone() => (FreeplayGenerationSettings)MemberwiseClone();
    }

    public enum PatternStyle
    {
        /// <summary>
        /// Switches between the other styles every few measures.
        /// </summary>
        Mixed,
        Jumps,
        Streams,
        Flow,
        Sliders,
    }

    public enum RhythmSource
    {
        /// <summary>
        /// Uses the source beatmap's objects when it has enough of them, otherwise the timing grid.
        /// </summary>
        Auto,

        /// <summary>
        /// Places objects at the timings of the source beatmap's objects (only positions and types are regenerated).
        /// </summary>
        BeatmapObjects,

        /// <summary>
        /// Places objects on the beats of the source beatmap's timing points.
        /// </summary>
        TimingGrid,
    }
}
