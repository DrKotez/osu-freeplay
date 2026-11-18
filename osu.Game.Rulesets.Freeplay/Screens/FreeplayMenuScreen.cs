// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Input.Bindings;
using osu.Game.Overlays;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Stable;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// freeplay~'s main menu, laid out like osu!stable's: a pulsing circle with menu bars sliding out beside it,
    /// and a list of presets (like song select) to start playing from.
    /// </summary>
    public partial class FreeplayMenuScreen : FreeplayScreen, IKeyBindingHandler<GlobalAction>
    {
        public override string Title => "freeplay~";

        // No back button or Esc-to-quit on the root screen; the exit bar is explicit.
        public override bool AllowUserExit => false;

        // The menu shows a song's background behind it.
        protected override float BackgroundDim => 0.45f;

        private const float cookie_size = 340;
        private const double transition = 500;

        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Pink);

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        private FreeplayStorage freeplayStorage = null!;
        private StableLibrary library = null!;
        private FreeplayMenuMusic music = null!;

        private FreeplayCookie cookie = null!;
        private FillFlowContainer topBars = null!;
        private Container presetList = null!;
        private Container backBar = null!;
        private readonly List<PresetPanel> modePanels = new List<PresetPanel>();
        private OsuTextFlowContainer modeDescription = null!;
        private FreeplayMode selectedMode = FreeplayMode.ALL[0];
        private OsuTextFlowContainer libraryStatus = null!;
        private Sprite background = null!;

        private readonly BindableDouble starRating = new BindableDouble { MinValue = 1, MaxValue = 10, Precision = 0.1 };
        private readonly Random rng = new Random();

        private bool choosingPreset;

        [BackgroundDependencyLoader]
        private void load(Storage storage, GameHost host, AudioManager audio, RulesetStore rulesets)
        {
            freeplayStorage = new FreeplayStorage(storage);
            FreeplaySettings.SetCurrent(freeplayStorage.LoadSettings());

            library = new StableLibrary(StableLibrary.FindInstall(FreeplaySettings.Current.Stable.Path), storage.GetStorageForDirectory("freeplay"), host, audio, rulesets);
            StableSkinSelector.Apply(skins, library, FreeplaySettings.Current.Stable.Skin);

            selectedMode = FreeplayMode.Find(FreeplaySettings.Current.SelectedMode) ?? FreeplayMode.ALL[0];
            starRating.Value = FreeplaySettings.Current.GetMode(selectedMode).StarRating;
            starRating.BindValueChanged(s =>
            {
                FreeplaySettings.Current.GetMode(selectedMode).StarRating = s.NewValue;
                updateSummary();
            });

            AddInternal(music = new FreeplayMenuMusic());

            // Behind the dim, so a song's background shows through like on osu!stable's menu.
            AddInternal(background = new Sprite
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                FillMode = FillMode.Fill,
                Depth = float.MaxValue,
            });

            cookie = new FreeplayCookie(cookie_size, () => music.Amplitude)
            {
                Anchor = Anchor.Centre,
                X = -140,
                Action = () => setChoosingPreset(true),
            };

            topBars = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.CentreLeft,
                X = -140 + cookie_size / 2 - 30,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 10),
                Depth = 1,
                Children = new Drawable[]
                {
                    new MenuBar("Играть", FreeplayColours.DEEP_PINK, () => setChoosingPreset(true)),
                    new MenuBar("Настройки", FreeplayColours.DEEP_BLUE, () => this.Push(new FreeplaySettingsScreen(freeplayStorage, library))),
                    new MenuBar("Статистика", FreeplayColours.DEEP_PINK, () => this.Push(new FreeplayStatsScreen(freeplayStorage))),
                    new MenuBar("Выход", new Color4(90, 70, 110, 255), () => this.Exit()),
                }
            };

            var panels = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 8),
                Padding = new MarginPadding { Left = 30, Right = 10, Vertical = 10 },
            };

            for (int i = 0; i < FreeplayMode.ALL.Length; i++)
            {
                var mode = FreeplayMode.ALL[i];
                var panel = new PresetPanel(mode.Name, string.Empty, i % 2 == 0 ? FreeplayColours.PINK : FreeplayColours.BLUE, () => onModeClicked(mode));

                modePanels.Add(panel);
                panels.Add(panel);
            }

            presetList = new Container
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                RelativeSizeAxes = Axes.Y,
                Width = 600,
                Alpha = 0,
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 6),
                        Padding = new MarginPadding { Left = 30, Right = 10 },
                        Children = new Drawable[]
                        {
                            Heading("Выбери, как играть", 36),
                            new FormSliderBar<double>
                            {
                                Caption = "Сложность режима",
                                Current = starRating,
                                KeyboardStep = 0.1f,
                                LabelFormat = v => $"{v:0.0}★",
                            },
                            modeDescription = new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 15))
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Colour = FreeplayColours.TEXT_DIM,
                            },
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(10, 0),
                                Children = new Drawable[]
                                {
                                    Button("Играть", () => play(selectedMode), 250),
                                    Button("Настройки режима", () => this.Push(new FreeplayModeSettingsScreen(freeplayStorage, selectedMode)), 250, blue: true),
                                }
                            },
                        }
                    },
                    new OsuScrollContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Top = 250 },
                        Child = panels,
                    },
                }
            };

            Content.AddRange(new Drawable[]
            {
                topBars,
                cookie,
                presetList,
                backBar = new Container
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    AutoSizeAxes = Axes.Both,
                    Alpha = 0,
                    Child = new MenuBar("← Назад", new Color4(90, 70, 110, 255), () => setChoosingPreset(false)) { Scale = new Vector2(0.7f) },
                },
                libraryStatus = new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 15))
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    TextAnchor = Anchor.BottomRight,
                    AutoSizeAxes = Axes.Y,
                    Width = 520,
                    Colour = FreeplayColours.TEXT_DIM,
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Text = music.Title == null ? string.Empty : $"♪ {music.Title}",
                    Font = OsuFont.GetFont(size: 18, weight: FontWeight.SemiBold),
                    Colour = FreeplayColours.WHITE,
                    Alpha = 0.8f,
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Text = "freeplay~ ♡ каждая попытка — новая карта",
                    Font = OsuFont.GetFont(size: 18, weight: FontWeight.SemiBold),
                    Colour = FreeplayColours.TitleGradient,
                },
            });

            if (FreeplayIntro.ShouldPlay)
                AddInternal(new FreeplayIntro());

            selectMode(selectedMode);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            music.Play();
            changeBackground();

            // Scanning reports progress from a background thread.
            library.ScanProgress.BindValueChanged(_ => Schedule(updateLibraryStatus), true);
            library.ScanAsync().ContinueWith(_ =>
            {
                minePatterns();
                Schedule(changeBackground);
            });
        }

        private void setChoosingPreset(bool choosing)
        {
            choosingPreset = choosing;

            if (choosing)
            {
                cookie.MoveToX(-DrawWidth / 2 + cookie_size * 0.45f, transition, Easing.OutQuint).ScaleTo(0.7f, transition, Easing.OutQuint);
                topBars.FadeOut(transition / 2).MoveToX(-200, transition, Easing.OutQuint);
                presetList.FadeIn(transition).MoveToX(0, transition, Easing.OutQuint);
                backBar.FadeIn(transition);
            }
            else
            {
                cookie.MoveToX(-140, transition, Easing.OutQuint).ScaleTo(1, transition, Easing.OutQuint);
                topBars.FadeIn(transition).MoveToX(-140 + cookie_size / 2 - 30, transition, Easing.OutQuint);
                presetList.FadeOut(transition / 2).MoveToX(100, transition, Easing.OutQuint);
                backBar.FadeOut(transition / 2);
            }
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (!choosingPreset)
                return false;

            switch (e.Action)
            {
                case GlobalAction.Back:
                    setChoosingPreset(false);
                    return true;

                case GlobalAction.Select:
                    play(selectedMode);
                    return true;

                case GlobalAction.SelectNext:
                case GlobalAction.SelectPrevious:
                    int index = Array.IndexOf(FreeplayMode.ALL, selectedMode) + (e.Action == GlobalAction.SelectNext ? 1 : -1);
                    selectMode(FreeplayMode.ALL[(index + FreeplayMode.ALL.Length) % FreeplayMode.ALL.Length]);
                    return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        private void changeBackground()
        {
            var texture = library.GetRandomBackground(rng);

            if (texture == null)
                return;

            background.Texture = texture;
            background.FadeInFromZero(800);
        }

        private void updateLibraryStatus()
        {
            if (!library.Exists)
                libraryStatus.Text = $"Папка osu!stable не найдена: {library.StablePath}. Укажите её в настройках.";
            else if (library.IsScanning)
                libraryStatus.Text = $"Сканирую osu!stable... {library.ScanProgress.Value:P0}";
            else
                libraryStatus.Text = $"{library.Sets.Count} песен · {library.Collections.Count} коллекций"
                                     + (library.Patterns == null ? " · собираю паттерны..." : $" · {library.Patterns.Count} паттернов из коллекций");
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            // Appear immediately, so the (unused) osu! main menu underneath doesn't show while fading in.
            FinishTransforms();
            Alpha = 1;
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            if (e.Last is FreeplayRunScreen)
            {
                music.Play();
                changeBackground();
            }

            // The mode's settings page may have changed its star rating or reset it...
            selectMode(selectedMode);

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

            // Gameplay brings its own music; settings and statistics keep the menu's.
            if (e.Next is FreeplayRunScreen)
                music.Stop();

            freeplayStorage.SaveSettings(FreeplaySettings.Current);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            music.Stop();
            freeplayStorage.SaveSettings(FreeplaySettings.Current);
            return base.OnExiting(e);
        }

        private void onModeClicked(FreeplayMode mode)
        {
            // Like osu!stable's song select: the first click selects, clicking the selection plays.
            if (mode == selectedMode)
                play(mode);
            else
                selectMode(mode);
        }

        private void selectMode(FreeplayMode mode)
        {
            selectedMode = mode;
            FreeplaySettings.Current.SelectedMode = mode.Id;

            for (int i = 0; i < modePanels.Count; i++)
                modePanels[i].Selected = FreeplayMode.ALL[i] == mode;


            starRating.Value = FreeplaySettings.Current.GetMode(mode).StarRating;

            updateSummary();
        }

        private void play(FreeplayMode mode) => this.Push(new FreeplayRunScreen(freeplayStorage, library, mode));

        private void updateSummary()
        {
            var settings = FreeplaySettings.Current;

            var fail = new List<string>();

            if (settings.Fail.Health) fail.Add("HP");
            if (settings.Fail.MissLimit > 0) fail.Add(settings.Fail.MissLimit == 1 ? "sudden death" : $"{settings.Fail.MissLimit} промахов");
            if (settings.Fail.MinAccuracy > 0) fail.Add($"точность < {settings.Fail.MinAccuracy:P0}");
            if (settings.Fail.TimeLimitMinutes > 0) fail.Add($"лимит {settings.Fail.TimeLimitMinutes:0} мин");

            for (int i = 0; i < modePanels.Count; i++)
            {
                var mode = settings.GetMode(FreeplayMode.ALL[i]);
                string stars = mode.UseRanges && mode.StarRatingMax != null && Math.Abs(mode.StarRatingMax.Value - mode.StarRating) > 0.05
                    ? $"{Math.Min(mode.StarRating, mode.StarRatingMax.Value):0.0}–{Math.Max(mode.StarRating, mode.StarRatingMax.Value):0.0}★"
                    : $"{mode.StarRating:0.0}★";

                modePanels[i].Detail = $"{stars} · {DescribeBpm(mode.MinBpm, mode.MaxBpm)}" + (mode.UseRanges ? " · разброс" : string.Empty);
            }

            modeDescription.Text = $"{selectedMode.Description}\nПоражение (общее для всех режимов): {(fail.Count > 0 ? string.Join(", ", fail) : "нет")}";
        }

        public static string DescribeBpm(double min, double max)
        {
            if (min > 0 && max > 0)
                return $"{min:0}–{max:0} BPM";

            if (min > 0)
                return $"от {min:0} BPM";

            if (max > 0)
                return $"до {max:0} BPM";

            return "любой BPM";
        }
    }
}
