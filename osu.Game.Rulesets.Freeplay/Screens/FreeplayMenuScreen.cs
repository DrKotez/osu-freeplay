// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Stable;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// The freeplay entry screen: pick a difficulty and a preset to start playing straight away, or go to settings and statistics.
    /// </summary>
    public partial class FreeplayMenuScreen : FreeplayScreen
    {
        public override string Title => "freeplay~";

        // No back button or Esc-to-quit on the root screen; the exit button is explicit.
        public override bool AllowUserExit => false;

        private const float card_width = (CONTENT_WIDTH - 10) / 2;

        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Pink);

        private FreeplayStorage freeplayStorage = null!;
        private StableLibrary library = null!;
        private OsuTextFlowContainer customSummary = null!;
        private OsuTextFlowContainer libraryStatus = null!;

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        private readonly BindableDouble starRating = new BindableDouble { MinValue = 1, MaxValue = 10, Precision = 0.1 };

        [BackgroundDependencyLoader]
        private void load(Storage storage, GameHost host, AudioManager audio, RulesetStore rulesets, OsuColour colours)
        {
            freeplayStorage = new FreeplayStorage(storage);
            FreeplaySettings.SetCurrent(freeplayStorage.LoadSettings());

            library = new StableLibrary(FreeplaySettings.Current.Stable.Path, storage.GetStorageForDirectory("freeplay"), host, audio, rulesets);
            StableSkinSelector.Apply(skins, library, FreeplaySettings.Current.Stable.Skin);

            starRating.Value = FreeplaySettings.Current.Generation.StarRating;
            starRating.BindValueChanged(s => FreeplaySettings.Current.Generation.StarRating = s.NewValue);

            var presets = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Full,
                Spacing = new Vector2(10),
            };

            // Alternate the flag colours across the grid.
            for (int i = 0; i < FreeplayPreset.ALL.Length; i++)
            {
                var preset = FreeplayPreset.ALL[i];
                presets.Add(card(preset.Name, description(preset.Description, colours), () => play(preset), blue: (i / 2 + i) % 2 == 1));
            }

            presets.Add(card("Свои настройки", customSummary = description(string.Empty, colours), () => play(null)));

            Content.Add(new OsuScrollContainer
            {
                RelativeSizeAxes = Axes.Y,
                Width = CONTENT_WIDTH,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(14),
                    Children = new Drawable[]
                    {
                        centred(Heading("freeplay~", 80)),
                        centred(Text("каждая попытка — новая карта ♡", 22).With(t => t.Colour = FreeplayColours.WHITE)),
                        libraryStatus = description(string.Empty, colours).With(d => d.TextAnchor = Anchor.TopCentre),
                        new FormSliderBar<double>
                        {
                            Caption = "Сложность",
                            Current = starRating,
                            KeyboardStep = 0.1f,
                            LabelFormat = v => $"{v:0.0}★",
                        },
                        presets,
                        new FillFlowContainer
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(10),
                            Margin = new MarginPadding { Top = 10 },
                            Children = new Drawable[]
                            {
                                Button("Настройки", () => this.Push(new FreeplaySettingsScreen(freeplayStorage, library)), card_width, blue: true),
                                Button("Статистика", () => this.Push(new FreeplayStatsScreen(freeplayStorage)), card_width, blue: true),
                            }
                        },
                        // This is the root screen, so leaving it closes the game. Only do so on purpose, not via a stray back button press.
                        Button("Выход", () => this.Exit(), card_width / 2).With(b => b.Alpha = 0.8f),
                    }
                }
            });

            updateSummary();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Scanning reports progress from a background thread.
            library.ScanProgress.BindValueChanged(_ => Schedule(updateLibraryStatus), true);
            library.ScanAsync().ContinueWith(_ => minePatterns());
        }

        private void updateLibraryStatus()
        {
            if (!library.Exists)
                libraryStatus.Text = $"Папка osu!stable не найдена: {library.StablePath}. Укажите её в настройках.";
            else if (library.IsScanning)
                libraryStatus.Text = $"Сканирую osu!stable... {library.ScanProgress.Value:P0} (первый раз может занять пару минут)";
            else
                libraryStatus.Text = $"Библиотека osu!stable: {library.Sets.Count} песен, {library.Collections.Count} коллекций"
                                     + (library.Patterns == null ? " · собираю паттерны из коллекций..." : $" · паттернов из коллекций: {library.Patterns.Count}");
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            // The settings screen may have changed the star rating.
            starRating.Value = FreeplaySettings.Current.Generation.StarRating;
            updateSummary();

            // ...or the example collections.
            if (!library.IsScanning)
                minePatterns();
        }

        /// <summary>
        /// Collects example patterns from the chosen collections (a no-op if they haven't changed).
        /// </summary>
        private void minePatterns()
        {
            Schedule(updateLibraryStatus);
            library.MinePatternsAsync(FreeplaySettings.Current.PatternSources).ContinueWith(_ => Schedule(updateLibraryStatus));
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            base.OnSuspending(e);
            freeplayStorage.SaveSettings(FreeplaySettings.Current);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            freeplayStorage.SaveSettings(FreeplaySettings.Current);
            return base.OnExiting(e);
        }

        private void play(FreeplayPreset? preset) => this.Push(new FreeplayRunScreen(freeplayStorage, library, preset));

        private void updateSummary()
        {
            var settings = FreeplaySettings.Current;

            var fail = new List<string>();

            if (settings.Fail.Health) fail.Add("HP");
            if (settings.Fail.MissLimit > 0) fail.Add(settings.Fail.MissLimit == 1 ? "sudden death" : $"{settings.Fail.MissLimit} промахов");
            if (settings.Fail.MinAccuracy > 0) fail.Add($"точность < {settings.Fail.MinAccuracy:P0}");
            if (settings.Fail.TimeLimitMinutes > 0) fail.Add($"лимит {settings.Fail.TimeLimitMinutes:0} мин");

            customSummary.Text = $"{settings.Generation.Style.GetDescription()} · поражение: {(fail.Count > 0 ? string.Join(", ", fail) : "нет")}";
        }

        private static Drawable card(string name, Drawable description, System.Action action, bool blue = false) => new FillFlowContainer
        {
            Width = card_width,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(4),
            Children = new[]
            {
                Button(name, action, card_width, blue),
                description,
            }
        };

        private static OsuTextFlowContainer description(string text, OsuColour colours) => new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 14))
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Colour = FreeplayColours.TEXT_DIM,
            Text = text,
        };

        private static Drawable centred(Drawable drawable)
        {
            drawable.Anchor = Anchor.TopCentre;
            drawable.Origin = Anchor.TopCentre;
            return drawable;
        }
    }
}
