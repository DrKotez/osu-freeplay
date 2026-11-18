// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Game.Rulesets.Freeplay.Generation;

namespace osu.Game.Rulesets.Freeplay.Configuration
{
    /// <summary>
    /// The settings a mode shows on its settings page. Every mode also has a star rating, a BPM range and CS/AR/OD/HP.
    /// </summary>
    [Flags]
    public enum ModeOptions
    {
        None = 0,

        /// <summary>
        /// Object density (rests between patterns).
        /// </summary>
        Density = 1,

        JumpSpacing = 1 << 1,
        StreamSpacing = 1 << 2,

        /// <summary>
        /// Whether 1/4 rhythms appear at all.
        /// </summary>
        AllowStreams = 1 << 3,

        Sliders = 1 << 4,
        Spinners = 1 << 5,

        /// <summary>
        /// Which shapes jumps use, whether they rotate, and how often patterns come from collections.
        /// </summary>
        JumpShapes = 1 << 6,

        Seed = 1 << 7,
    }

    /// <summary>
    /// A way to play, chosen from the menu. Each mode keeps its own settings, starting from its defaults.
    /// </summary>
    public class FreeplayMode
    {
        public static readonly FreeplayMode[] ALL =
        {
            new FreeplayMode("aim", "Аим", "Квадраты, звёзды, кресты и прочие прыжки. Без стримов.",
                ModeOptions.Density | ModeOptions.JumpSpacing | ModeOptions.Sliders | ModeOptions.JumpShapes | ModeOptions.Seed, s =>
                {
                    s.Style = PatternStyle.Jumps;
                    s.AllowStreams = false;
                    s.JumpSpacing = 1.1;
                    s.SliderAmount = 0.4;
                    s.AllowSpinners = false;
                    s.StarRating = 5;
                }),
            new FreeplayMode("streams", "Стримы", "Стримы по 12–40 нот в припевах, прыжки между ними.",
                ModeOptions.Density | ModeOptions.JumpSpacing | ModeOptions.StreamSpacing | ModeOptions.Sliders | ModeOptions.Seed, s =>
                {
                    s.Style = PatternStyle.Streams;
                    s.JumpSpacing = 0.9;
                    s.SliderAmount = 0.3;
                    s.AllowSpinners = false;
                    s.StarRating = 5;
                    s.MinBpm = 150;
                }),
            new FreeplayMode("deathstreams", "Дезстримы", "Непрерывные стримы на 100+ нот плотно друг к другу.",
                ModeOptions.StreamSpacing | ModeOptions.JumpSpacing | ModeOptions.Seed, s =>
                {
                    s.Style = PatternStyle.Deathstreams;
                    s.SliderAmount = 0;
                    s.AllowSpinners = false;
                    s.StarRating = 6;
                    s.MinBpm = 150;
                    s.MaxBpm = 220;
                }),
            new FreeplayMode("mixed", "Микс", "Всё подряд: прыжки, стримы, флоу и слайдеры под музыку.",
                ModeOptions.Density | ModeOptions.JumpSpacing | ModeOptions.StreamSpacing | ModeOptions.AllowStreams | ModeOptions.Sliders | ModeOptions.Spinners
                | ModeOptions.JumpShapes | ModeOptions.Seed, s =>
                {
                    s.Style = PatternStyle.Mixed;
                    s.StarRating = 5;
                }),
            new FreeplayMode("remix", "Ремикс карты", "Ритм, слайдеры и сложность маппера, но каждое комбо стоит по-новому. Звёзды выбирают сложность маппера.",
                ModeOptions.Seed, s =>
                {
                    s.RhythmSource = RhythmSource.Remix;
                    s.StarRating = 5;
                }),
            new FreeplayMode("chill", "Чилл", "Плавный флоу, слайдеры и больше передышек.",
                ModeOptions.Density | ModeOptions.JumpSpacing | ModeOptions.Sliders | ModeOptions.Spinners | ModeOptions.Seed, s =>
                {
                    s.Style = PatternStyle.Flow;
                    s.JumpSpacing = 0.85;
                    s.Density = 0.85;
                    s.SliderAmount = 1.2;
                    s.StarRating = 3.5;
                }),
            new FreeplayMode("sliders", "Слайдеры", "Много слайдеров: волны, дуги, повторы.",
                ModeOptions.Density | ModeOptions.JumpSpacing | ModeOptions.Sliders | ModeOptions.Spinners | ModeOptions.Seed, s =>
                {
                    s.Style = PatternStyle.Sliders;
                    s.SliderAmount = 1.6;
                    s.StarRating = 4;
                }),
        };

        public static FreeplayMode? Find(string? id) => ALL.FirstOrDefault(m => m.Id == id);

        /// <summary>
        /// A stable identifier, used as the key for this mode's saved settings.
        /// </summary>
        public readonly string Id;

        public readonly string Name;
        public readonly string Description;

        public readonly ModeOptions Options;

        private readonly Action<FreeplayGenerationSettings> applyDefaults;

        private FreeplayMode(string id, string name, string description, ModeOptions options, Action<FreeplayGenerationSettings> applyDefaults)
        {
            Id = id;
            Name = name;
            Description = description;
            Options = options;
            this.applyDefaults = applyDefaults;
        }

        public FreeplayGenerationSettings CreateDefaults()
        {
            var settings = new FreeplayGenerationSettings();
            applyDefaults(settings);
            return settings;
        }

        public bool Has(ModeOptions option) => Options.HasFlag(option);
    }
}
