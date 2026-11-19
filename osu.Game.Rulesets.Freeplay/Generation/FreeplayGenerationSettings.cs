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
        public static FreeplayGenerationSettings Current
        {
            get => threadSettings ?? current;
            set => current = value;
        }

        private static FreeplayGenerationSettings current = new FreeplayGenerationSettings();

        [ThreadStatic]
        private static FreeplayGenerationSettings? threadSettings;

        /// <summary>
        /// Makes <see cref="Current"/> <paramref name="settings"/> on this thread only (for generating a track in the background while another plays), until disposed.
        /// </summary>
        public static IDisposable UseForThisThread(FreeplayGenerationSettings settings)
        {
            threadSettings = settings;
            return new osu.Framework.Allocation.InvokeOnDisposal(() => threadSettings = null);
        }

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
        /// <see cref="PatternStyle.Metronome"/>: the shapes it jumps around.
        /// </summary>
        public MetronomeShapes MetronomeShapes { get; set; } = MetronomeShapes.All;

        /// <summary>
        /// <see cref="PatternStyle.Metronome"/>: how many measures a shape is held, or 0 for a mix (mostly two, sometimes one or four).
        /// </summary>
        public int ShapeMeasures { get; set; }

        /// <summary>
        /// <see cref="PatternStyle.Metronome"/>: whether shapes change from one round to the next (turning, reversing, pulsing, moving).
        /// </summary>
        public bool RotateShapes { get; set; } = true;

        /// <summary>
        /// <see cref="PatternStyle.Metronome"/>: jump every half beat even where the stars only allow beats (the map then comes out harder than asked).
        /// </summary>
        public bool AlwaysHalfBeats { get; set; }

        /// <summary>
        /// <see cref="PatternStyle.Metronome"/>: a run that speeds up: each track a little faster than the last, with the stars rising along with the BPM.
        /// </summary>
        public bool Ramp { get; set; }

        /// <summary>
        /// <see cref="PatternStyle.Metronome"/>: a quarter of the playfield that gets more of the notes, to practise it.
        /// </summary>
        public WeakZone WeakZone { get; set; }

        /// <summary>
        /// With <see cref="WeakZone"/>: the share of notes in that quarter (a quarter of them without it).
        /// </summary>
        public double WeakZoneShare { get; set; } = 0.4;

        /// <summary>
        /// With <see cref="WeakZone.Auto"/>: the quarter missed most in the latest runs, found when the run starts (null if not enough misses yet).
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public Configuration.PlayfieldQuarter? AutoWeakQuarter { get; set; }

        /// <summary>
        /// The quarter which gets more notes, or null.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public Configuration.PlayfieldQuarter? WeakQuarter => WeakZone switch
        {
            WeakZone.Off => null,
            WeakZone.Auto => AutoWeakQuarter,
            _ => (Configuration.PlayfieldQuarter)(WeakZone - WeakZone.TopLeft),
        };

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
        /// Where 1/4 bursts in the music aren't played, cover them with short "kick" sliders instead of leaving their extra notes out.
        /// </summary>
        public bool KickSliders { get; set; }

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
        /// How hard patterns are to read and move through, independent of star rating (0-1).
        /// Low is "aim slop": back-and-forth and wide triangles, even spacing, repetitive motifs.
        /// High is tricky: squares, stars and crosses, right and acute angles, uneven spacing, changing motifs.
        /// </summary>
        public double PatternIq { get; set; } = 0.5;

        /// <summary>
        /// Where this mode's tracks come from: null follows the general settings, <see cref="WHOLE_LIBRARY"/> is every song,
        /// anything else is the name of an osu!stable collection.
        /// </summary>
        public string? TrackCollection { get; set; }

        public const string WHOLE_LIBRARY = "*";

        /// <summary>
        /// The goal these settings were made from (see <see cref="Configuration.AimGoals.Apply"/>), or null for a mode without goals.
        /// </summary>
        public Configuration.AimGoal? Goal { get; set; }

        /// <summary>
        /// Whether the player changed fine tuning after picking <see cref="Goal"/>. Then the goal no longer changes anything per track.
        /// </summary>
        public bool GoalEdited { get; set; }

        /// <summary>
        /// The goal a surprise track picked. Only set on a track's own copy of the settings.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public Configuration.AimGoal? SurpriseOf { get; set; }

        public Configuration.FailLevel FailLevel { get; set; } = Configuration.FailLevel.Normal;

        /// <summary>
        /// Jumps get wider the less time they have, and shorter the more: the reverse of normal spacing.
        /// </summary>
        public bool AntiJumps { get; set; }

        /// <summary>
        /// Tracks shorter than this aren't played. Null follows the general track settings.
        /// </summary>
        public double? MinTrackSeconds { get; set; }

        /// <summary>
        /// Added to how often 1/2 notes are filled into gaps. Set by star rating calibration, not by the player.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public double FillBoost { get; set; }

        /// <summary>
        /// Scales jumps on top of <see cref="JumpSpacing"/>. Set by star rating calibration, not by the player,
        /// so it can shrink jumps without going below the minimum the player's own spacing implies.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public double CalibrationSpacing { get; set; } = 1;

        /// <summary>
        /// How much of aim jumps every half beat: 0 everywhere, 1 only in kiai (beats elsewhere), 2 nowhere (beats throughout).
        /// Raised by star rating calibration when the half beats are too hard even with the smallest jumps.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public int FlowLevel { get; set; }

        /// <summary>
        /// When on, each track picks its own value between each setting and its "Max" counterpart below,
        /// so a run varies (e.g. density 70-100%, 4.5-5.5★).
        /// </summary>
        /// <summary>
        /// How this track mixes its ways of mapping (see <see cref="TrackPalette"/>). Rolled per track; null uses the defaults.
        /// </summary>
        public TrackPalette? Palette { get; set; }

        public bool UseRanges { get; set; }

        // The upper ends of the ranges. Null means "same as the setting" (no range).
        public double? StarRatingMax { get; set; }
        public double? DensityMax { get; set; }
        public double? JumpSpacingMax { get; set; }
        public double? StreamSpacingMax { get; set; }
        public double? SliderAmountMax { get; set; }
        public double? PatternIqMax { get; set; }
        public double? MinedPatternShareMax { get; set; }

        /// <summary>
        /// The settings for one track: with <see cref="UseRanges"/>, each ranged setting is picked at random within its range.
        /// </summary>
        public FreeplayGenerationSettings Roll(Random rng, double? starQuantile = null)
        {
            var rolled = Clone();
            rolled.Palette = TrackPalette.Roll(rng);

            if (!UseRanges)
                return rolled;

            rolled.StarRating = pick(StarRating, StarRatingMax, rng, 0.1, starQuantile);
            rolled.Density = pick(Density, DensityMax, rng, 0.05);
            rolled.JumpSpacing = pick(JumpSpacing, JumpSpacingMax, rng, 0.05);
            rolled.StreamSpacing = pick(StreamSpacing, StreamSpacingMax, rng, 0.05);
            rolled.SliderAmount = pick(SliderAmount, SliderAmountMax, rng, 0.05);
            rolled.PatternIq = pick(PatternIq, PatternIqMax, rng, 0.05);
            rolled.MinedPatternShare = pick(MinedPatternShare, MinedPatternShareMax, rng, 0.05);

            return rolled;
        }

        private static double pick(double min, double? max, Random rng, double step, double? quantile = null)
        {
            if (max == null)
                return min;

            double low = Math.Min(min, max.Value), high = Math.Max(min, max.Value);
            return Math.Round((low + (quantile ?? rng.NextDouble()) * (high - low)) / step) * step;
        }

        public FreeplayGenerationSettings Clone() => (FreeplayGenerationSettings)MemberwiseClone();
    }

    [Flags]
    public enum MetronomeShapes
    {
        None = 0,

        [Description("Пятиконечная звезда")]
        Star = 1,

        [Description("Семиконечная звезда")]
        SevenPointStar = 1 << 1,

        [Description("Звезда Давида")]
        StarOfDavid = 1 << 2,

        [Description("Треугольник")]
        Triangle = 1 << 3,

        [Description("Квадрат")]
        Square = 1 << 4,

        [Description("Перекрещенный квадрат")]
        CrossedSquare = 1 << 5,

        [Description("Пятиугольник")]
        Pentagon = 1 << 6,

        [Description("Шестиугольник")]
        Hexagon = 1 << 7,

        [Description("Пинг-понг")]
        BackAndForth = 1 << 8,

        [Description("Паттерны из карт (аим-коллекция)")]
        MapperPatterns = 1 << 9,

        [Description("Восьмиконечная звезда")]
        EightPointStar = 1 << 10,

        [Description("Неровная звезда (длинные и короткие прыжки)")]
        UnevenStar = 1 << 11,

        [Description("Зигзаг туда-обратно")]
        Zigzag = 1 << 12,

        [Description("Прямоугольник")]
        Rectangle = 1 << 13,

        [Description("Случайная фигура (каждый раз новая)")]
        RandomShape = 1 << 14,

        All = Star | SevenPointStar | StarOfDavid | Triangle | Square | CrossedSquare | Pentagon | Hexagon | BackAndForth | MapperPatterns
              | EightPointStar | UnevenStar | Zigzag | Rectangle | RandomShape,
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

        /// <summary>
        /// Jumps on every half beat (or beat) from start to end, in shapes held for a few measures: stars, polygons, squares, back and forth.
        /// Rhythm comes from the tempo alone, not the mapper.
        /// </summary>
        [Description("Звёздочка")]
        Metronome,
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

namespace osu.Game.Rulesets.Freeplay.Generation
{
    /// <summary>
    /// Which quarter of the playfield <see cref="FreeplayGenerationSettings.WeakZone"/> practises.
    /// </summary>
    public enum WeakZone
    {
        Off,

        /// <summary>
        /// The one missed most in the latest runs.
        /// </summary>
        Auto,

        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }
}
