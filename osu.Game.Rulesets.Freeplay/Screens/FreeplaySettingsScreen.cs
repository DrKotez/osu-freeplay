// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Stable;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// Settings shared by every mode: where songs come from and how a run is lost. Each mode's own settings are on its own page.
    /// </summary>
    public partial class FreeplaySettingsScreen : FreeplayFormScreen
    {
        public override string Title => "Настройки freeplay";

        private readonly StableLibrary library;

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        [Resolved]
        private SettingsOverlay? gameSettings { get; set; }

        public FreeplaySettingsScreen(FreeplayStorage freeplayStorage, StableLibrary library)
            : base(freeplayStorage)
        {
            this.library = library;
        }

        protected override void CreateForm()
        {
            var tracks = Settings.Tracks;
            var fail = Settings.Fail;

            Flow.Add(Button("Настройки игры: графика, звук, клавиши, оффсет", () => gameSettings?.ToggleVisibility(), CONTENT_WIDTH, blue: true));

            AddHint("Здесь то, что общее для всех режимов. Звёзды, BPM, паттерны и CS/AR/OD/HP у каждого режима свои: выбери режим в меню «Играть» и нажми «Настройки режима».");

            AddHeading("osu!stable");

            Flow.Add(new FormTextBox
            {
                Caption = "Папка osu!stable",
                HintText = $"Здесь лежат Songs, Skins и collection.db. Карты читаются прямо отсюда, ничего не копируется. Пусто — найти автоматически (сейчас: {library.StablePath}). После смены папки перезайдите в freeplay.",
                PlaceholderText = "автоматически",
                Current = Bind(Settings.Stable.Path ?? string.Empty, v => Settings.Stable.Path = string.IsNullOrWhiteSpace(v) ? null : v.Trim()),
            });

            AddHint(library.Exists
                ? $"Найдено: {library.Sets.Count} песен, {library.Sets.Sum(s => s.Difficulties.Count)} сложностей osu!, {library.Collections.Count} коллекций."
                : "Папка Songs не найдена.");

            var skinOptions = new List<SkinOption> { new SkinOption(null, "Встроенный (классический)") };
            skinOptions.AddRange(StableSkin.List(library.SkinsPath).Select(s => new SkinOption(s, s)));

            Flow.Add(new FormDropdown<SkinOption>
            {
                Caption = "Скин",
                HintText = "Скины берутся из папки Skins в osu!stable.",
                Items = skinOptions,
                Current = Bind(skinOptions.FirstOrDefault(s => s.Name == Settings.Stable.Skin) ?? skinOptions[0], v =>
                {
                    Settings.Stable.Skin = v.Name;
                    StableSkinSelector.Apply(skins, library, v.Name);
                }),
            });

            AddHeading("Шкала попаданий и UR");

            var timing = Settings.Timing;

            AddHint("Внизу экрана во время игры. Видна всегда, даже со скрытым интерфейсом (Shift+Tab).");

            AddCheckbox("Шкала попаданий", timing.ShowHitErrorBar, v => timing.ShowHitErrorBar = v, "Где ты попадаешь относительно ноты: левее — раньше, правее — позже.");
            AddCheckbox("Unstable rate (UR)", timing.ShowUnstableRate, v => timing.ShowUnstableRate = v, "Насколько ровно ты попадаешь. Меньше — стабильнее.");
            AddSlider("Размер", timing.Scale, 0.5, 4, 0.1, v => timing.Scale = v, v => $"{v:P0}");
            AddSlider("Прозрачность", timing.Opacity, 0.2, 1, 0.05, v => timing.Opacity = v, v => $"{v:P0}");

            AddHeading("Образцы паттернов");

            AddHint("Генератор берёт куски настоящих карт из этих коллекций, поворачивает и отражает их и подгоняет под ритм трека. Сколько паттернов брать отсюда, а сколько выдумывать на рандоме, настраивается в каждом режиме.");

            var sourceOptions = new List<CollectionOption> { new CollectionOption(null, "нет (только рандом и фигуры)") };
            sourceOptions.AddRange(library.Collections.Select(c => new CollectionOption(c.Name, $"{c.Name} ({c.BeatmapHashes.Count})")));

            var sources = Settings.PatternSources;

            patternSource("Аим", sourceOptions, sources.Aim, v => sources.Aim = v);
            patternSource("Стримы", sourceOptions, sources.Streams, v => sources.Streams = v);
            patternSource("Тех, флоу и слайдеры", sourceOptions, sources.Tech, v => sources.Tech = v);

            AddHeading("Треки");

            var collections = library.Collections.Select(c => new CollectionOption(c.Name, $"{c.Name} ({c.BeatmapHashes.Count})")).ToList();
            var noCollection = new CollectionOption(null, collections.Count == 0 ? "нет коллекций" : "не выбрана");
            collections.Insert(0, noCollection);

            var sourceBindable = Bind(tracks.Source, v => tracks.Source = v);

            Flow.Add(new FormEnumDropdown<TrackSource>
            {
                Caption = "Откуда брать треки",
                Current = sourceBindable,
            });

            var collectionDropdown = new FormDropdown<CollectionOption>
            {
                Caption = "Коллекция",
                Items = collections,
                Current = Bind(collections.FirstOrDefault(c => SameCollection(c.Name, tracks.CollectionName)) ?? noCollection, v => tracks.CollectionName = v.Name),
            };

            Flow.Add(collectionDropdown);

            ShowWhen(collectionDropdown, sourceBindable, s => s == TrackSource.Collection);

            AddSlider("Мин. длина трека", tracks.MinLengthSeconds, 0, 600, 10, v => tracks.MinLengthSeconds = v, v => v == 0 ? "без ограничения" : FormatDuration(v * 1000));
            AddSlider("Макс. длина трека", tracks.MaxLengthSeconds, 0, 900, 10, v => tracks.MaxLengthSeconds = v, v => v == 0 ? "без ограничения" : FormatDuration(v * 1000));

            Flow.Add(new FormSliderBar<int>
            {
                Caption = "Не повторять последние N треков",
                Current = BindInt(tracks.NoRepeatCount, 0, 200, v => tracks.NoRepeatCount = v),
            });

            AddHeading("Условия поражения");

            AddCheckbox("Поражение при нулевом HP", fail.Health, v => fail.Health = v);

            var carryBindable = Bind(fail.CarryHealth, v => fail.CarryHealth = v);

            Flow.Add(new FormCheckBox
            {
                Caption = "Переносить HP между треками",
                HintText = "Иначе каждый трек начинается с полным здоровьем.",
                Current = carryBindable,
            });

            var refillSlider = new FormSliderBar<double>
            {
                Caption = "Восстановление HP после трека",
                Current = BindDouble(fail.HealthRefill, 0, 1, 0.05, v => fail.HealthRefill = v),
                KeyboardStep = 0.05f,
                DisplayAsPercentage = true,
            };

            Flow.Add(refillSlider);

            ShowWhen(refillSlider, carryBindable, c => c);

            Flow.Add(new FormSliderBar<int>
            {
                Caption = "Лимит промахов за забег",
                HintText = "1 — sudden death.",
                Current = BindInt(fail.MissLimit, 0, 200, v => fail.MissLimit = v),
                LabelFormat = v => v == 0 ? "выкл" : v == 1 ? "sudden death" : $"{v}",
            });

            AddSlider("Минимальная точность", fail.MinAccuracy, 0, 0.99, 0.01, v => fail.MinAccuracy = v, v => v == 0 ? "выкл" : $"{v:P0}");
            AddSlider("Лимит времени забега", fail.TimeLimitMinutes, 0, 120, 1, v => fail.TimeLimitMinutes = v, v => v == 0 ? "выкл" : $"{v:0} мин",
                hint: "Забег завершится успешно, когда время выйдет.");
        }

        private void patternSource(string caption, List<CollectionOption> options, string? current, Action<string?> apply)
        {
            // A collection that no longer exists still shows, so the choice isn't silently lost.
            var selected = options.FirstOrDefault(o => SameCollection(o.Name, current));

            if (selected == null && current != null)
                options.Add(selected = new CollectionOption(current, $"{current} (не найдена)"));

            Flow.Add(new FormDropdown<CollectionOption>
            {
                Caption = caption,
                Items = options,
                Current = Bind(selected ?? options[0], v => apply(v.Name)),
            });
        }

        private record SkinOption(string? Name, string Label)
        {
            public override string ToString() => Label;
        }
    }
}
