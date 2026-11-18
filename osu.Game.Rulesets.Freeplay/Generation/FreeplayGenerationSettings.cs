// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.ComponentModel;

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

        /// <summary>
        /// Scales how many objects may appear per second. Lower values leave more rests.
        /// </summary>
        public double Density { get; set; } = 1;

        /// <summary>
        /// Which shapes jump sections may be built from.
        /// </summary>
        public JumpPatterns Patterns { get; set; } = JumpPatterns.All;

        /// <summary>
        /// Repeated shapes (e.g. squares) rotate a little each time round.
        /// </summary>
        public bool RotatePatterns { get; set; } = true;

        /// <summary>
        /// Scales the distance between jumps.
        /// </summary>
        public double JumpSpacing { get; set; } = 1;

        /// <summary>
        /// Scales the distance between stream notes.
        /// </summary>
        public double StreamSpacing { get; set; } = 1;

        /// <summary>
        /// Whether 1/4 rhythms (bursts, triples and streams) may be generated at all.
        /// </summary>
        public bool AllowStreams { get; set; } = true;

        /// <summary>
        /// Scales how often sliders are generated. Zero disables them.
        /// </summary>
        public double SliderAmount { get; set; } = 1;

        public bool AllowSpinners { get; set; } = true;

        /// <summary>
        /// Whether generated beatmaps are adjusted until their real star rating is close to <see cref="StarRating"/>.
        /// </summary>
        public bool CalibrateStarRating { get; set; } = true;

        // Null means "derive from star rating".
        public float? CircleSize { get; set; }
        public float? ApproachRate { get; set; }
        public float? OverallDifficulty { get; set; }
        public float? DrainRate { get; set; }

        /// <summary>
        /// A fixed seed, for repeating an attempt. Null generates a new pattern each time.
        /// </summary>
        public int? Seed { get; set; }

        /// <summary>
        /// Only songs within this BPM range are played. Zero means no limit.
        /// </summary>
        public double MinBpm { get; set; }

        public double MaxBpm { get; set; }

        /// <summary>
        /// How much of the jump patterns come from the example collections (when available).
        /// The rest are made up at random: motifs, and a smaller share of geometric shapes.
        /// </summary>
        public double MinedPatternShare { get; set; } = 0.4;

        /// <summary>
        /// Added to how often 1/2 notes are filled into gaps. Set by star rating calibration, not by the player.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public double FillBoost { get; set; }

        public FreeplayGenerationSettings Clone() => (FreeplayGenerationSettings)MemberwiseClone();
    }

    [Flags]
    public enum JumpPatterns
    {
        None = 0,

        [Description("Квадраты")]
        Squares = 1,

        [Description("Треугольники")]
        Triangles = 1 << 1,

        [Description("Звёзды")]
        Stars = 1 << 2,

        [Description("Многоугольники")]
        Polygons = 1 << 3,

        [Description("Пинг-понг")]
        PingPong = 1 << 4,

        [Description("Зигзаги")]
        Zigzags = 1 << 5,

        [Description("Рваные прыжки")]
        RandomJumps = 1 << 6,

        [Description("Спирали")]
        Spirals = 1 << 7,

        [Description("Кресты")]
        Crosses = 1 << 8,

        [Description("Стаки")]
        Stacks = 1 << 9,

        All = Squares | Triangles | Stars | Polygons | PingPong | Zigzags | RandomJumps | Spirals | Crosses | Stacks,
    }

    public enum PatternStyle
    {
        /// <summary>
        /// Switches between the other styles every few measures.
        /// </summary>
        [Description("Микс")]
        Mixed,
        [Description("Прыжки")]
        Jumps,
        [Description("Стримы")]
        Streams,
        [Description("Дезстримы")]
        Deathstreams,
        [Description("Флоу")]
        Flow,
        [Description("Слайдеры")]
        Sliders,
    }

    public enum RhythmSource
    {
        /// <summary>
        /// Uses the source beatmap's objects when it has enough of them, otherwise the timing grid.
        /// </summary>
        [Description("Авто")]
        Auto,

        /// <summary>
        /// Places objects at the timings of the source beatmap's objects (only positions and types are regenerated).
        /// </summary>
        [Description("По объектам карты")]
        BeatmapObjects,

        /// <summary>
        /// Places objects on the beats of the source beatmap's timing points.
        /// </summary>
        [Description("По сетке BPM")]
        TimingGrid,

        /// <summary>
        /// Keeps the source beatmap's objects exactly (timing, circles, sliders, spinners), only moving each combo somewhere new.
        /// </summary>
        [Description("Ремикс карты маппера")]
        Remix,
    }
}
