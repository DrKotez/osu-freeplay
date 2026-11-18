// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using osu.Game.Rulesets.Freeplay.Generation;

namespace osu.Game.Rulesets.Freeplay.Configuration
{
    /// <summary>
    /// What the player thought of one generated track, with everything needed to look at the same map again:
    /// saved by <see cref="FreeplayStorage.SaveFeedback"/> next to the generated .osu and the mapper's original.
    /// </summary>
    public class FeedbackEntry
    {
        public DateTimeOffset Date { get; set; } = DateTimeOffset.Now;

        public string Mode { get; set; } = string.Empty;
        public string Goal { get; set; } = string.Empty;

        public string Artist { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;

        /// <summary>
        /// The song's folder in osu!stable's Songs, and the difficulty the rhythm was taken from.
        /// </summary>
        public string SetFolder { get; set; } = string.Empty;

        public string DifficultyFile { get; set; } = string.Empty;

        public double Bpm { get; set; }
        public int Seed { get; set; }
        public double TargetStars { get; set; }
        public double ActualStars { get; set; }

        public string Outcome { get; set; } = string.Empty;
        public double Accuracy { get; set; }
        public int Misses { get; set; }
        public double? UnstableRate { get; set; }
        public double PlayedSeconds { get; set; }

        public FeedbackRating? Rating { get; set; }

        public List<FeedbackFlag> Flags { get; set; } = new List<FeedbackFlag>();

        /// <summary>
        /// Anything else the player wrote about the track.
        /// </summary>
        public string Comment { get; set; } = string.Empty;

        /// <summary>
        /// The settings the track was generated with (after ranges were rolled).
        /// </summary>
        public FreeplayGenerationSettings? Settings { get; set; }

        /// <summary>
        /// The entry's folder under <c>freeplay/feedback</c>, once saved.
        /// </summary>
        public string? Folder { get; set; }

        public bool IsEmpty => Rating == null && Flags.Count == 0 && string.IsNullOrWhiteSpace(Comment);
    }

    public enum FeedbackRating
    {
        [Description("Легко")]
        TooEasy,

        [Description("В самый раз")]
        JustRight,

        [Description("Сложно")]
        TooHard,
    }

    /// <remarks>Saved by name: new marks go anywhere, but existing names stay.</remarks>
    public enum FeedbackFlag
    {
        [Description("Скучный ритм")]
        BoringRhythm,

        [Description("Ноты мимо музыки")]
        OffMusic,

        [Description("Тишина, а музыка играет")]
        OddPauses,

        [Description("Однотипно")]
        Monotonous,

        [Description("Слишком широко")]
        TooWide,

        [Description("Слишком тесно")]
        TooCramped,

        [Description("Плохой спейсинг")]
        BadSpacing,

        [Description("Неудобный флоу")]
        BadFlow,

        [Description("Нечитаемо")]
        Unreadable,

        [Description("Много слайдеров")]
        TooManySliders,

        [Description("Неудобные слайдеры")]
        AwkwardSliders,

        [Description("Кайф")]
        Great,
    }
}
