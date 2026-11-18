// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Freeplay.Generation;

namespace osu.Game.Rulesets.Freeplay.Configuration
{
    /// <summary>
    /// A ready-made set of generation settings, so a run can be started with one click.
    /// </summary>
    public class FreeplayPreset
    {
        public static readonly FreeplayPreset[] ALL =
        {
            new FreeplayPreset("Аим", "Квадраты, звёзды, кресты и прочие прыжки. Без стримов.", s =>
            {
                s.Style = PatternStyle.Jumps;
                s.AllowStreams = false;
                s.JumpSpacing = 1.1;
                s.SliderAmount = 0.4;
                s.AllowSpinners = false;
            }),
            new FreeplayPreset("Стримы", "Стримы по 12–30 нот в припевах, прыжки между ними.", s =>
            {
                s.Style = PatternStyle.Streams;
                s.JumpSpacing = 0.9;
                s.SliderAmount = 0.3;
                s.AllowSpinners = false;
            }),
            new FreeplayPreset("Дезстримы", "Непрерывные стримы на 100+ нот плотно друг к другу.", s =>
            {
                s.Style = PatternStyle.Deathstreams;
                s.SliderAmount = 0;
                s.AllowSpinners = false;
            }),
            new FreeplayPreset("Микс", "Всё подряд: прыжки, стримы, флоу и слайдеры под музыку.", s =>
            {
                s.Style = PatternStyle.Mixed;
            }),
            new FreeplayPreset("Чилл", "Плавный флоу, слайдеры и больше передышек.", s =>
            {
                s.Style = PatternStyle.Flow;
                s.JumpSpacing = 0.85;
                s.Density = 0.85;
                s.SliderAmount = 1.2;
            }),
            new FreeplayPreset("Слайдеры", "Много слайдеров: волны, дуги, повторы.", s =>
            {
                s.Style = PatternStyle.Sliders;
                s.SliderAmount = 1.6;
            }),
        };

        public readonly string Name;
        public readonly string Description;

        private readonly Action<FreeplayGenerationSettings> apply;

        private FreeplayPreset(string name, string description, Action<FreeplayGenerationSettings> apply)
        {
            Name = name;
            Description = description;
            this.apply = apply;
        }

        /// <summary>
        /// Creates this preset's settings. The star rating, difficulty attributes and seed are kept from <paramref name="custom"/>,
        /// as those are personal preference rather than part of a style.
        /// </summary>
        public FreeplayGenerationSettings CreateSettings(FreeplayGenerationSettings custom)
        {
            var settings = new FreeplayGenerationSettings
            {
                StarRating = custom.StarRating,
                CircleSize = custom.CircleSize,
                ApproachRate = custom.ApproachRate,
                OverallDifficulty = custom.OverallDifficulty,
                DrainRate = custom.DrainRate,
                Seed = custom.Seed,
            };

            apply(settings);
            return settings;
        }
    }
}
