// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    public partial class FreeplaySettingsScreen : FreeplayScreen
    {
        public override string Title => "Настройки freeplay";

        private readonly FreeplayStorage freeplayStorage;
        private readonly FreeplaySettings settings;

        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Pink);

        private FillFlowContainer flow = null!;

        public FreeplaySettingsScreen(FreeplayStorage freeplayStorage)
        {
            this.freeplayStorage = freeplayStorage;
            settings = FreeplaySettings.Current;
        }

        [BackgroundDependencyLoader]
        private void load(RealmAccess realm)
        {
            Content.Add(new OsuScrollContainer
            {
                RelativeSizeAxes = Axes.Y,
                Width = CONTENT_WIDTH,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Child = flow = Column(8),
            });

            var generation = settings.Generation;
            var tracks = settings.Tracks;
            var fail = settings.Fail;

            heading("Генерация");

            flow.Add(new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 15))
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Colour = colourProvider.Content2,
                Text = "Генерация и паттерны работают для кнопки «Свои настройки» в меню. Пресеты берут отсюда только звёзды, CS/AR/OD/HP и seed. Треки и условия поражения общие для всех.",
            });

            flow.Add(new FormSliderBar<double>
            {
                Caption = "Сложность (звёзды)",
                Current = bindDouble(generation.StarRating, 1, 10, 0.1, v => generation.StarRating = v),
                KeyboardStep = 0.1f,
                LabelFormat = v => $"{v:0.0}★",
            });

            flow.Add(new FormEnumDropdown<PatternStyle>
            {
                Caption = "Стиль паттернов",
                HintText = "Прыжки — чистый аим без троек и стримов. Стримы — стримы по 12+ нот в напряжённых частях трека. Дезстримы — непрерывные стримы на 100+ нот. Флоу — плавные многоугольники и слайдеры. Слайдеры — много слайдеров. Микс — подбирает стиль под каждую часть трека.",
                Current = bind(generation.Style, v => generation.Style = v),
            });

            flow.Add(new FormSliderBar<double>
            {
                Caption = "Плотность объектов",
                HintText = "Меньше — больше передышек между паттернами.",
                Current = bindDouble(generation.Density, 0.5, 1.5, 0.05, v => generation.Density = v),
                KeyboardStep = 0.05f,
                DisplayAsPercentage = true,
            });

            flow.Add(new FormEnumDropdown<RhythmSource>
            {
                Caption = "Источник ритма",
                HintText = "«По объектам карты» берёт тайминги из сложности маппера, «по сетке BPM» — только из тайминга трека.",
                Current = bind(generation.RhythmSource, v => generation.RhythmSource = v),
            });

            heading("Паттерны");

            flow.Add(new FormSliderBar<double>
            {
                Caption = "Дистанция прыжков",
                Current = bindDouble(generation.JumpSpacing, 0.5, 1.8, 0.05, v => generation.JumpSpacing = v),
                KeyboardStep = 0.05f,
                DisplayAsPercentage = true,
            });

            flow.Add(new FormCheckBox
            {
                Caption = "Стримы, тройки и бёрсты",
                HintText = "Выключено — никаких 1/4, только прыжки и слайдеры.",
                Current = bind(generation.AllowStreams, v => generation.AllowStreams = v),
            });

            flow.Add(new FormSliderBar<double>
            {
                Caption = "Расстояние между нотами в стримах",
                Current = bindDouble(generation.StreamSpacing, 0.5, 1.8, 0.05, v => generation.StreamSpacing = v),
                KeyboardStep = 0.05f,
                DisplayAsPercentage = true,
            });

            flow.Add(new FormSliderBar<double>
            {
                Caption = "Количество слайдеров",
                HintText = "0% — без слайдеров.",
                Current = bindDouble(generation.SliderAmount, 0, 2, 0.05, v => generation.SliderAmount = v),
                KeyboardStep = 0.05f,
                DisplayAsPercentage = true,
            });

            flow.Add(new FormCheckBox
            {
                Caption = "Спиннеры",
                Current = bind(generation.AllowSpinners, v => generation.AllowSpinners = v),
            });

            flow.Add(new FormCheckBox
            {
                Caption = "Вращать повторяющиеся фигуры",
                HintText = "Второй круг квадрата поворачивается на 45° (выходит восьмиконечная звезда), треугольника — на 60° (гексаграмма).",
                Current = bind(generation.RotatePatterns, v => generation.RotatePatterns = v),
            });

            flow.Add(Text("Фигуры для прыжков", 20, FontWeight.SemiBold).With(t => t.Margin = new MarginPadding { Top = 8 }));

            foreach (var pattern in Enum.GetValues<JumpPatterns>().Where(p => p != JumpPatterns.None && p != JumpPatterns.All))
            {
                flow.Add(new FormCheckBox
                {
                    Caption = pattern.GetDescription(),
                    Current = bind(generation.Patterns.HasFlag(pattern), enabled => generation.Patterns = enabled ? generation.Patterns | pattern : generation.Patterns & ~pattern),
                });
            }

            flow.Add(Text("Если выключить все фигуры, будут обычные рваные прыжки.", 14).With(t => t.Colour = colourProvider.Content2));

            heading("Параметры карты");

            difficultyOverride("CS (размер кругов)",generation.CircleSize, 2, 7, v => generation.CircleSize = v);
            difficultyOverride("AR (скорость появления)", generation.ApproachRate, 1, 10, v => generation.ApproachRate = v);
            difficultyOverride("OD (точность)", generation.OverallDifficulty, 1, 10, v => generation.OverallDifficulty = v);
            difficultyOverride("HP (дрейн здоровья)", generation.DrainRate, 1, 10, v => generation.DrainRate = v);

            flow.Add(new FormTextBox
            {
                Caption = "Фиксированный seed",
                HintText = "Пусто — новая карта каждый раз. Seed каждого трека виден в итогах забега.",
                PlaceholderText = "случайный",
                Current = bind(generation.Seed?.ToString() ?? string.Empty, v => generation.Seed = int.TryParse(v, out int seed) ? seed : null),
            });

            heading("Треки");

            var collections = realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().Select(c => new CollectionOption(c.ID, c.Name)).ToList());
            var noCollection = new CollectionOption(null, collections.Count == 0 ? "нет коллекций" : "не выбрана");
            collections.Insert(0, noCollection);

            var sourceBindable = bind(tracks.Source, v => tracks.Source = v);

            flow.Add(new FormEnumDropdown<TrackSource>
            {
                Caption = "Откуда брать треки",
                Current = sourceBindable,
            });

            var collectionBindable = bind(collections.FirstOrDefault(c => c.ID == tracks.CollectionID) ?? noCollection, v => tracks.CollectionID = v.ID);

            var collectionDropdown = new FormDropdown<CollectionOption>
            {
                Caption = "Коллекция",
                Items = collections,
                Current = collectionBindable,
            };

            flow.Add(collectionDropdown);

            showWhen(collectionDropdown, sourceBindable, s => s == TrackSource.Collection);

            flow.Add(new FormSliderBar<double>
            {
                Caption = "Мин. длина трека",
                Current = bindDouble(tracks.MinLengthSeconds, 0, 600, 10, v => tracks.MinLengthSeconds = v),
                KeyboardStep = 10,
                LabelFormat = v => v == 0 ? "без ограничения" : FormatDuration(v * 1000),
            });

            flow.Add(new FormSliderBar<double>
            {
                Caption = "Макс. длина трека",
                Current = bindDouble(tracks.MaxLengthSeconds, 0, 900, 10, v => tracks.MaxLengthSeconds = v),
                KeyboardStep = 10,
                LabelFormat = v => v == 0 ? "без ограничения" : FormatDuration(v * 1000),
            });

            flow.Add(new FormSliderBar<double>
            {
                Caption = "Мин. BPM",
                Current = bindDouble(tracks.MinBpm, 0, 400, 5, v => tracks.MinBpm = v),
                KeyboardStep = 5,
                LabelFormat = v => v == 0 ? "без ограничения" : $"{v:0}",
            });

            flow.Add(new FormSliderBar<double>
            {
                Caption = "Макс. BPM",
                Current = bindDouble(tracks.MaxBpm, 0, 400, 5, v => tracks.MaxBpm = v),
                KeyboardStep = 5,
                LabelFormat = v => v == 0 ? "без ограничения" : $"{v:0}",
            });

            flow.Add(new FormSliderBar<int>
            {
                Caption = "Не повторять последние N треков",
                Current = bindInt(tracks.NoRepeatCount, 0, 200, v => tracks.NoRepeatCount = v),
            });

            heading("Условия поражения");

            flow.Add(new FormCheckBox
            {
                Caption = "Поражение при нулевом HP",
                Current = bind(fail.Health, v => fail.Health = v),
            });

            var carryBindable = bind(fail.CarryHealth, v => fail.CarryHealth = v);

            flow.Add(new FormCheckBox
            {
                Caption = "Переносить HP между треками",
                HintText = "Иначе каждый трек начинается с полным здоровьем.",
                Current = carryBindable,
            });

            var refillSlider = new FormSliderBar<double>
            {
                Caption = "Восстановление HP после трека",
                Current = bindDouble(fail.HealthRefill, 0, 1, 0.05, v => fail.HealthRefill = v),
                KeyboardStep = 0.05f,
                DisplayAsPercentage = true,
            };

            flow.Add(refillSlider);

            showWhen(refillSlider, carryBindable, c => c);

            flow.Add(new FormSliderBar<int>
            {
                Caption = "Лимит промахов за забег",
                HintText = "1 — sudden death.",
                Current = bindInt(fail.MissLimit, 0, 200, v => fail.MissLimit = v),
                LabelFormat = v => v == 0 ? "выкл" : v == 1 ? "sudden death" : $"{v}",
            });

            flow.Add(new FormSliderBar<double>
            {
                Caption = "Минимальная точность",
                Current = bindDouble(fail.MinAccuracy, 0, 0.99, 0.01, v => fail.MinAccuracy = v),
                KeyboardStep = 0.01f,
                LabelFormat = v => v == 0 ? "выкл" : $"{v:P0}",
            });

            flow.Add(new FormSliderBar<double>
            {
                Caption = "Лимит времени забега",
                HintText = "Забег завершится успешно, когда время выйдет.",
                Current = bindDouble(fail.TimeLimitMinutes, 0, 120, 1, v => fail.TimeLimitMinutes = v),
                KeyboardStep = 1,
                LabelFormat = v => v == 0 ? "выкл" : $"{v:0} мин",
            });
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            freeplayStorage.SaveSettings(settings);
            FreeplaySettings.SetCurrent(settings);

            return base.OnExiting(e);
        }

        private void heading(string text) => flow.Add(Text(text, 28, FontWeight.Bold).With(t => t.Margin = new MarginPadding { Top = flow.Count > 0 ? 20 : 0, Bottom = 4 }));

        /// <summary>
        /// A checkbox and slider for one difficulty attribute, where unchecked means "derive from star rating".
        /// </summary>
        private void difficultyOverride(string caption, float? value, float min, float max, Action<float?> apply)
        {
            var enabled = new BindableBool(value != null);
            var number = new BindableFloat(value ?? 5) { MinValue = min, MaxValue = max, Precision = 0.1f };

            enabled.BindValueChanged(e => apply(e.NewValue ? number.Value : null));

            number.BindValueChanged(n =>
            {
                if (enabled.Value)
                    apply(n.NewValue);
            });

            flow.Add(new FormCheckBox
            {
                Caption = $"{caption}: своё значение",
                HintText = "Выключено — подбирается по звёздам.",
                Current = enabled,
            });

            var slider = new FormSliderBar<float>
            {
                Caption = caption,
                Current = number,
                KeyboardStep = 0.1f,
            };

            flow.Add(slider);

            showWhen(slider, enabled, e => e);
        }

        /// <summary>
        /// Shows a dependent control only while its controlling setting makes it relevant.
        /// </summary>
        private static void showWhen<T>(Drawable drawable, IBindable<T> source, Func<T, bool> condition)
            => source.BindValueChanged(v => drawable.Alpha = condition(v.NewValue) ? 1 : 0, true);

        private static Bindable<T> bind<T>(T value, Action<T> apply)
        {
            var bindable = new Bindable<T>(value);
            bindable.BindValueChanged(v => apply(v.NewValue));
            return bindable;
        }

        private static BindableDouble bindDouble(double value, double min, double max, double precision, Action<double> apply)
        {
            var bindable = new BindableDouble(value) { MinValue = min, MaxValue = max, Precision = precision };
            bindable.BindValueChanged(v => apply(v.NewValue));
            return bindable;
        }

        private static BindableInt bindInt(int value, int min, int max, Action<int> apply)
        {
            var bindable = new BindableInt(value) { MinValue = min, MaxValue = max };
            bindable.BindValueChanged(v => apply(v.NewValue));
            return bindable;
        }

        private record CollectionOption(Guid? ID, string Name)
        {
            public override string ToString() => Name;
        }
    }
}
