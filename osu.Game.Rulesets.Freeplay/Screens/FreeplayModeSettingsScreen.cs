// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// One mode's own settings. Only what makes sense for the mode is shown.
    /// </summary>
    public partial class FreeplayModeSettingsScreen : FreeplayFormScreen
    {
        public override string Title => $"Режим «{mode.Name}»";

        private readonly FreeplayMode mode;

        public FreeplayModeSettingsScreen(FreeplayStorage freeplayStorage, FreeplayMode mode)
            : base(freeplayStorage)
        {
            this.mode = mode;
        }

        protected override void CreateForm()
        {
            var s = Settings.GetMode(mode);
            bool remix = s.RhythmSource == RhythmSource.Remix;

            Flow.Add(Heading(mode.Name, 44));
            AddHint(mode.Description);

            var ranges = Bind(s.UseRanges, v => s.UseRanges = v);

            if (!remix)
            {
                Flow.Add(new FormCheckBox
                {
                    Caption = "Разброс настроек",
                    HintText = "Каждый трек берёт свои значения в диапазоне «от — до»: например, плотность 70–100% или 4.5–5.5★. Так забег не однообразный.",
                    Current = ranges,
                });
            }

            AddHeading("Сложность");

            ranged(ranges, "Звёзды", s.StarRating, s.StarRatingMax, 1, 10, 0.1, v => s.StarRating = v, v => s.StarRatingMax = v, v => $"{v:0.0}★",
                hint: remix
                    ? "Берётся сложность маппера, чья реальная сложность ближе всего к этим звёздам."
                    : "Карта подгоняется под эти звёзды: ритм, расстояния и стримы. С 6★ чаще попадаются быстрые песни, на медленных столько не набрать честно.");

            AddHeading("BPM треков");

            AddHint("Играются только песни в этом диапазоне. 0 — без ограничения.");

            AddSlider("Мин. BPM", s.MinBpm, 0, 400, 5, v => s.MinBpm = v, bpmLabel);
            AddSlider("Макс. BPM", s.MaxBpm, 0, 400, 5, v => s.MaxBpm = v, bpmLabel);

            if (mode.Options.HasFlag(ModeOptions.Density) || mode.Options.HasFlag(ModeOptions.JumpSpacing) || mode.Options.HasFlag(ModeOptions.StreamSpacing))
                AddHeading("Паттерны");

            if (mode.Has(ModeOptions.Density))
                ranged(ranges, "Плотность", s.Density, s.DensityMax, 0.5, 1.5, 0.05, v => s.Density = v, v => s.DensityMax = v, percent, hint: "Меньше — больше передышек и меньше нот на 1/2.");

            if (mode.Has(ModeOptions.JumpSpacing))
            {
                ranged(ranges, s.Style == PatternStyle.Deathstreams || s.Style == PatternStyle.Streams ? "Прыжки между стримами" : "Дистанция прыжков",
                    s.JumpSpacing, s.JumpSpacingMax, 0.5, 1.8, 0.05, v => s.JumpSpacing = v, v => s.JumpSpacingMax = v, percent,
                    hint: "Калибровка звёзд всё равно подстраивает расстояния, это сдвигает их в свою сторону.");
            }

            if (mode.Has(ModeOptions.StreamSpacing))
                ranged(ranges, "Расстояние в стримах", s.StreamSpacing, s.StreamSpacingMax, 0.5, 1.8, 0.05, v => s.StreamSpacing = v, v => s.StreamSpacingMax = v, percent, hint: "100% — ноты налезают друг на друга, как в обычных стримах.");

            if (mode.Has(ModeOptions.Iq))
            {
                ranged(ranges, "IQ карты", s.PatternIq, s.PatternIqMax, 0, 1, 0.05, v => s.PatternIq = v, v => s.PatternIqMax = v, iqLabel,
                    hint: "Не звёзды, а то, насколько паттерны мудрёные. Низкий — aim slop: туда-обратно, широкие треугольники, ровные расстояния, повторяющиеся мотивы. "
                          + "Высокий — квадраты, звёзды, кресты, прямые и острые углы, неровные расстояния, мотивы постоянно меняются.");
            }

            if (mode.Has(ModeOptions.AllowStreams))
            {
                if (s.Style == PatternStyle.Jumps)
                {
                    AddCheckbox("Бёрсты и тройки", s.AllowStreams, v => s.AllowStreams = v,
                        "Включено — где в музыке быстрые ноты, будут тройки и бёрсты до 5 нот (стримов не будет). "
                        + "Выключено — такие места закрываются короткими кик-слайдерами, чтобы звук не терялся.");
                }
                else
                    AddCheckbox("Стримы, тройки и бёрсты", s.AllowStreams, v => s.AllowStreams = v, "Выключено — никаких 1/4, только прыжки и слайдеры (бёрсты в музыке закрываются кик-слайдерами).");
            }

            if (mode.Has(ModeOptions.Sliders))
                ranged(ranges, "Количество слайдеров", s.SliderAmount, s.SliderAmountMax, 0, 2, 0.05, v => s.SliderAmount = v, v => s.SliderAmountMax = v, percent, hint: "0% — без слайдеров.");

            if (mode.Has(ModeOptions.Spinners))
                AddCheckbox("Спиннеры", s.AllowSpinners, v => s.AllowSpinners = v);

            if (mode.Has(ModeOptions.JumpShapes))
            {
                AddHeading("Откуда паттерны");

                ranged(ranges, "Из коллекций", s.MinedPatternShare, s.MinedPatternShareMax, 0, 1, 0.05, v => s.MinedPatternShare = v, v => s.MinedPatternShareMax = v, v => $"{v:P0} из коллекций · {1 - v:P0} рандом",
                    hint: "Остальное выдумывается на ходу: случайные мотивы (каждый раз новая форма) и немного фигур. Коллекции выбираются в общих настройках.");

                AddCheckbox("Вращать повторяющиеся фигуры", s.RotatePatterns, v => s.RotatePatterns = v,
                    "Второй круг квадрата поворачивается на 45° (выходит восьмиконечная звезда), треугольника — на 60°.");

                Flow.Add(Text("Фигуры", 20, FontWeight.SemiBold).With(t => t.Margin = new MarginPadding { Top = 8 }));

                foreach (var pattern in Enum.GetValues<JumpPatterns>().Where(p => p != JumpPatterns.None && p != JumpPatterns.All))
                {
                    var flag = pattern;
                    AddCheckbox(flag.GetDescription(), s.Patterns.HasFlag(flag), enabled => s.Patterns = enabled ? s.Patterns | flag : s.Patterns & ~flag);
                }

                AddHint("Если выключить все фигуры, вместо них будут рандомные мотивы.");
            }

            AddHeading("Параметры карты");

            AddDifficultyOverride("CS (размер кругов)", s.CircleSize, 2, 7, v => s.CircleSize = v);
            AddDifficultyOverride("AR (скорость появления)", s.ApproachRate, 1, 10, v => s.ApproachRate = v);
            AddDifficultyOverride("OD (точность)", s.OverallDifficulty, 1, 10, v => s.OverallDifficulty = v);
            AddDifficultyOverride("HP (дрейн здоровья)", s.DrainRate, 1, 10, v => s.DrainRate = v);

            if (mode.Has(ModeOptions.Seed))
            {
                Flow.Add(new FormTextBox
                {
                    Caption = "Фиксированный seed",
                    HintText = "Пусто — новая карта каждый раз. Seed каждого трека виден в итогах забега.",
                    PlaceholderText = "случайный",
                    Current = Bind(s.Seed?.ToString() ?? string.Empty, v => s.Seed = int.TryParse(v, out int seed) ? seed : null),
                });
            }

            Flow.Add(new osu.Framework.Graphics.Containers.Container { Height = 12 });
            Flow.Add(Button("Сбросить режим к стандартным", () =>
            {
                Settings.ResetMode(mode);
                Flow.Clear();
                CreateForm();
            }, CONTENT_WIDTH, blue: true));
        }

        /// <summary>
        /// A setting which can be a range: its own slider, plus an upper end shown while ranges are on.
        /// </summary>
        private void ranged(IBindable<bool> ranges, string caption, double value, double? max, double min, double maxValue, double step, Action<double> apply, Action<double?> applyMax,
                            Func<double, string> format, string? hint = null)
        {
            AddSlider(caption, value, min, maxValue, step, apply, format, hint);

            var upper = new FormSliderBar<double>
            {
                Caption = $"{caption}: до",
                HintText = "Верхняя граница разброса. Нижняя — ползунок выше.",
                Current = BindDouble(max ?? value, min, maxValue, step, v => applyMax(v)),
                KeyboardStep = (float)step,
                LabelFormat = v => format(v),
            };

            Flow.Add(upper);

            ShowWhen(upper, ranges, r => r);

            // Turning ranges on starts from no range at all, rather than a leftover upper end.
            if (max == null)
                ranges.BindValueChanged(r => { if (r.NewValue) applyMax(upper.Current.Value); });
        }

        private static string percent(double v) => $"{v:P0}";

        private static string bpmLabel(double v) => v == 0 ? "без ограничения" : $"{v:0}";

        private static string iqLabel(double v)
        {
            string kind = v < 0.2 ? "aim slop" : v < 0.4 ? "простые" : v < 0.6 ? "средние" : v < 0.8 ? "мудрёные" : "квадраты и звёзды";
            return $"{v * 100:0} · {kind}";
        }
    }
}
