// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Configuration;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Screens.Controls;
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

        protected override string PageTitle => "Настройки";

        private readonly StableLibrary library;

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [Resolved]
        private FrameworkConfigManager frameworkConfig { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        [Resolved]
        private Database.RealmAccess realm { get; set; } = null!;

        private List<string>? lastImport;

        public FreeplaySettingsScreen(FreeplayStorage freeplayStorage, StableLibrary library)
            : base(freeplayStorage)
        {
            this.library = library;
        }

        protected override void CreateForm()
        {
            var tracks = Settings.Tracks;
            var timing = Settings.Timing;

            AddHint("Здесь то, что общее для всех режимов. Цель, звёзды, BPM, CS/AR/OD/HP и поражение у каждого режима свои: выбери режим в меню «Играть» и нажми «Настройки режима».");

            AddHeading("osu!stable");

            AddTextBox("Папка osu!stable", Bind(Settings.Stable.Path ?? string.Empty, v => Settings.Stable.Path = string.IsNullOrWhiteSpace(v) ? null : v.Trim()), "автоматически",
                $"Здесь лежат Songs, Skins и collection.db. Карты читаются прямо отсюда, ничего не копируется. Пусто — найти автоматически (сейчас: {library.StablePath}). После смены папки перезайди в игру.");

            AddHint(library.Exists
                ? $"Найдено: {library.Sets.Count} песен, {library.Sets.Sum(s => s.Difficulties.Count)} сложностей osu!, {library.Collections.Count} коллекций."
                : "Папка Songs не найдена.");

            var skinOptions = new List<string?> { null };
            skinOptions.AddRange(StableSkin.List(library.SkinsPath));

            AddDropdown("Скин", Bind(skinOptions.Contains(Settings.Stable.Skin) ? Settings.Stable.Skin : null, v =>
            {
                Settings.Stable.Skin = v;
                StableSkinSelector.Apply(skins, library, v);
            }), skinOptions, v => v ?? "Встроенный (классический)", "Скины берутся из папки Skins в osu!stable.");

            var stableConfig = StableConfig.Find(library.StablePath);

            AddCheckbox("Брать настройки из osu!stable", Settings.Stable.SyncWithStable, v => Settings.Stable.SyncWithStable = v,
                "При каждом запуске: громкость, оффсет, затемнение, курсор, FPS, полный экран, интерфейс, шкала попаданий, скин и клавиши — как в твоём osu!stable. "
                + "Что поменяешь здесь, при следующем запуске снова станет как в stable.");

            AddHint(stableConfig == null ? "Настройки osu!stable не найдены (нет файла osu!.<имя>.cfg)." : $"Файл настроек: {System.IO.Path.GetFileName(stableConfig.FilePath)}");

            if (stableConfig != null)
            {
                AddButton("Взять настройки из osu!stable сейчас", () =>
                {
                    var taken = new StableSettingsImport(config, frameworkConfig, audio, realm).Apply(stableConfig, Settings, force: true);
                    StableSkinSelector.Apply(skins, library, Settings.Stable.Skin);
                    FreeplayStorage.SaveSettings(Settings);
                    RebuildForm();
                    lastImport = taken;
                }, blue: true);

                if (lastImport != null)
                    AddHint($"Взято: {string.Join(", ", lastImport)}.");
            }

            AddHeading("Шкала попаданий и UR");

            AddHint("Внизу экрана во время игры. Видна всегда, даже со скрытым интерфейсом (Shift+Tab).");

            AddCheckbox("Шкала попаданий", timing.ShowHitErrorBar, v => timing.ShowHitErrorBar = v, "Где ты попадаешь относительно ноты: левее — раньше, правее — позже.");
            AddCheckbox("Unstable rate (UR)", timing.ShowUnstableRate, v => timing.ShowUnstableRate = v, "Насколько ровно ты попадаешь. Меньше — стабильнее.");
            AddSlider("Размер", timing.Scale, 0.5, 4, 0.1, v => timing.Scale = v, v => $"{v:P0}");
            AddSlider("Прозрачность", timing.Opacity, 0.2, 1, 0.05, v => timing.Opacity = v, v => $"{v:P0}");

            AddHeading("Образцы паттернов");

            AddHint("Генератор берёт куски настоящих карт из этих коллекций, поворачивает и отражает их и подгоняет под ритм трека. Сколько паттернов брать отсюда, а сколько выдумывать на рандоме, настраивается в каждом режиме.");

            var sources = Settings.PatternSources;

            patternSource("Аим", sources.Aim, v => sources.Aim = v);
            patternSource("Стримы", sources.Streams, v => sources.Streams = v);
            patternSource("Тех, флоу и слайдеры", sources.Tech, v => sources.Tech = v);
            patternSource("Звёздочка", sources.Stars, v => sources.Stars = v, "как для аима");
            AddHint("Своя коллекция для «Звёздочки»: например, аимслоп-карты. Из неё берутся паттерны фигуры «Паттерны из карт».");

            AddHeading("Треки");

            var sourceBindable = Bind(tracks.Source, v => tracks.Source = v);
            AddEnumDropdown("Откуда брать треки", sourceBindable);

            var collections = new List<string?> { null };
            collections.AddRange(library.Collections.Select(c => c.Name));

            string? currentCollection = library.Collections.FirstOrDefault(c => SameCollection(c.Name, tracks.CollectionName))?.Name;

            var collectionDropdown = AddDropdown("Коллекция", Bind(currentCollection, v => tracks.CollectionName = v), collections,
                v => v == null ? (library.Collections.Count == 0 ? "нет коллекций" : "не выбрана") : $"{v.Trim()} ({library.FindCollection(v)?.BeatmapHashes.Count ?? 0})");

            ShowWhen(collectionDropdown, sourceBindable, s => s == TrackSource.Collection);

            AddSlider("Мин. длина трека", tracks.MinLengthSeconds, 0, 600, 10, v => tracks.MinLengthSeconds = v, v => v == 0 ? "без ограничения" : FormatDuration(v * 1000));
            AddSlider("Макс. длина трека", tracks.MaxLengthSeconds, 0, 900, 10, v => tracks.MaxLengthSeconds = v, v => v == 0 ? "без ограничения" : FormatDuration(v * 1000));
            AddIntSlider("Не повторять последние N треков", tracks.NoRepeatCount, 0, 200, v => tracks.NoRepeatCount = v);

            AddHeading("Звук");

            AddControl(new FreeplaySlider<double>("Общая громкость", audio.Volume, percent, keyboardStep: 0.05f));
            AddControl(new FreeplaySlider<double>("Музыка", audio.VolumeTrack, percent, keyboardStep: 0.05f));
            AddControl(new FreeplaySlider<double>("Хитсаунды и эффекты", audio.VolumeSample, percent, keyboardStep: 0.05f));
            AddControl(new FreeplaySlider<double>("Оффсет", (BindableNumber<double>)config.GetBindable<double>(OsuSetting.AudioOffset), v => $"{v:+0;-0;0} мс",
                "Плюс — ноты позже, минус — раньше. Подстраивай, если попадания стабильно уходят в одну сторону на шкале.", 1));

            AddHeading("Экран");

            var windowModes = host.Window?.SupportedWindowModes.ToList() ?? Enum.GetValues<WindowMode>().ToList();
            AddDropdown("Режим окна", frameworkConfig.GetBindable<WindowMode>(FrameworkSetting.WindowMode), windowModes, describeWindowMode);

            AddDropdown("Ограничение FPS", frameworkConfig.GetBindable<FrameSync>(FrameworkSetting.FrameSync), Enum.GetValues<FrameSync>(), describeFrameSync,
                "Для osu! лучше без вертикальной синхронизации: меньше задержка.");

            AddCheckbox("Показывать FPS", config.GetBindable<bool>(OsuSetting.ShowFpsDisplay));

            AddHeading("Геймплей");

            AddHint("Планшет работает как в osu!stable: область, поворот и сглаживание берутся из OpenTabletDriver (или другого драйвера), здесь их настраивать не нужно.");

            AddControl(new FreeplaySlider<double>("Затемнение фона", (BindableNumber<double>)config.GetBindable<double>(OsuSetting.DimLevel), percent, keyboardStep: 0.05f));
            AddControl(new FreeplaySlider<float>("Размер курсора", (BindableNumber<float>)config.GetBindable<float>(OsuSetting.GameplayCursorSize), v => $"{v:0.00}×", keyboardStep: 0.05f));
            AddCheckbox("Курсор меньше на мелких кругах", config.GetBindable<bool>(OsuSetting.AutoCursorSize), "Размер курсора подстраивается под CS карты.");
            AddCheckbox("Вспышки при попадании", config.GetBindable<bool>(OsuSetting.HitLighting));
            AddDropdown("Интерфейс в игре", config.GetBindable<HUDVisibilityMode>(OsuSetting.HUDVisibilityMode), Enum.GetValues<HUDVisibilityMode>(), describeHud,
                "Shift+Tab в игре прячет и показывает интерфейс. Шкала попаданий freeplay видна всегда.");

            AddHeading("Клавиши");

            AddHint("Клавиши для нот (по умолчанию Z и X), паузы, рестарта, громкости и интерфейса.");
            AddButton("Настроить клавиши", () => this.Push(new FreeplayKeyBindingsScreen(FreeplayStorage)), blue: true);
        }

        private static string percent(double v) => $"{v:P0}";

        private static string describeWindowMode(WindowMode mode)
        {
            switch (mode)
            {
                case WindowMode.Windowed:
                    return "В окне";

                case WindowMode.Borderless:
                    return "Окно без рамки на весь экран";

                case WindowMode.Fullscreen:
                    return "Полный экран";

                default:
                    return mode.ToString();
            }
        }

        private static string describeFrameSync(FrameSync sync)
        {
            switch (sync.ToString())
            {
                case "VSync":
                    return "Вертикальная синхронизация";

                case "VSyncVRR":
                    return "Синхронизация для G-Sync / FreeSync";

                case "Limit2x":
                    return "2× частоты экрана";

                case "Limit4x":
                    return "4× частоты экрана";

                case "Limit8x":
                    return "8× частоты экрана";

                case "Unlimited":
                    return "Без ограничения";

                default:
                    return sync.ToString();
            }
        }

        private static string describeHud(HUDVisibilityMode mode)
        {
            switch (mode)
            {
                case HUDVisibilityMode.Never:
                    return "Никогда не показывать";

                case HUDVisibilityMode.HideDuringGameplay:
                    return "Прятать во время игры";

                default:
                    return "Показывать всегда";
            }
        }

        private void patternSource(string caption, string? current, System.Action<string?> apply, string none = "нет (только рандом и фигуры)")
        {
            var options = new List<string?> { null };
            options.AddRange(library.Collections.Select(c => c.Name));

            // A collection that no longer exists still shows, so the choice isn't silently lost.
            string? selected = library.Collections.FirstOrDefault(c => SameCollection(c.Name, current))?.Name;

            if (selected == null && current != null)
                options.Add(selected = current);

            AddDropdown(caption, Bind(selected, apply), options, v => v == null
                ? none
                : library.FindCollection(v) is StableCollection c ? $"{c.Name.Trim()} ({c.BeatmapHashes.Count})" : $"{v.Trim()} (не найдена)");
        }
    }
}
