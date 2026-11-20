// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Configuration;
using osu.Framework.Development;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
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

        // (The menu has its own "Выход" and "← Назад".)
        protected override bool ShowBackBar => false;

        // No back button or Esc-to-quit on the root screen; the exit bar is explicit.
        public override bool AllowUserExit => false;

        // The menu shows a song's background behind it.
        protected override float BackgroundDim => 0.45f;

        private const float cookie_size = 340;
        private const double transition = 500;

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

        private Controls.FreeplayButton modsButton = null!;


        private static FreeplayMenuScreen? dropOwner;

        /// <summary>
        /// Gameplay brings its own music, wherever it was started from (the menu, or the seeds page over it); the menu's comes back after.
        /// </summary>
        private void onRunning(bool running)
        {
            if (running)
                music.Stop();
            else
                music.Play(fade: false);
        }

        // FIX (no lazer UI): freeplay~'s own message, not an osu!lazer notification.
        private void onDropped(string what) => FreeplayOverlays.Show(what);

        private OsuSpriteText clock = null!;

        /// <summary>
        /// When the game was started.
        /// </summary>
        private static readonly DateTime started = System.Diagnostics.Process.GetCurrentProcess().StartTime;

        protected override void Update()
        {
            base.Update();

            float top = presetHead.DrawHeight + 14;

            if (Math.Abs(presetScroll.Padding.Top - top) > 0.5f)
                presetScroll.Padding = new MarginPadding { Top = top };

            // OPTIMISATION: the text changes once a second, so it's only built then (not on every frame).
            var now = DateTime.Now;
            long second = now.Ticks / TimeSpan.TicksPerSecond;

            if (second == lastClockSecond)
                return;

            lastClockSecond = second;
            var running = now - started;
            clock.Text = $"в игре {(int)running.TotalHours}:{running.Minutes:00}:{running.Seconds:00} · на часах {now:HH:mm}";
        }

        private long lastClockSecond = -1;

        private OsuTextFlowContainer modeDescription = null!;
        private FreeplayMode selectedMode = FreeplayMode.AVAILABLE[0];
        private OsuTextFlowContainer libraryStatus = null!;
        private Sprite background = null!;

        private readonly BindableDouble starRating = new BindableDouble { MinValue = 1, MaxValue = 10, Precision = 0.1 };
        private readonly BindableDouble starRatingMax = new BindableDouble { MinValue = 1, MaxValue = 10, Precision = 0.1 };
        private readonly BindableBool usePerformance = new BindableBool();
        private readonly BindableDouble performance = new BindableDouble { MinValue = 30, MaxValue = 1000, Precision = 5 };
        private Drawable performanceCheckbox = null!, performanceSlider = null!;

        /// <summary>
        /// While the difficulty controls are set from a mode's settings (not by the player).
        /// </summary>
        private bool syncingDifficulty;

        /// <summary>
        /// The difficulty and buttons above the list of modes, and the list (which starts below them, however tall they are).
        /// </summary>
        private FillFlowContainer presetHead = null!;

        private Controls.FreeplayScrollContainer presetScroll = null!;
        private readonly Random rng = new Random();

        private bool choosingPreset;

        [BackgroundDependencyLoader]
        private void load(Storage storage, GameHost host, AudioManager audio, RulesetStore rulesets, OsuConfigManager config, FrameworkConfigManager frameworkConfig, RealmAccess realm)
        {
            freeplayStorage = new FreeplayStorage(storage);
            FreeplaySettings.SetCurrent(freeplayStorage.LoadSettings());

            string stablePath = StableLibrary.FindInstall(FreeplaySettings.Current.Stable.Path);
            stableConfig = StableConfig.Find(stablePath);

            // Taking stable's settings changes things like volume and window mode, which must happen on the update thread (see LoadComplete).
            settingsImport = new StableSettingsImport(config, frameworkConfig, audio, realm);

            library = new StableLibrary(stablePath, storage.GetStorageForDirectory("freeplay"), host, audio, rulesets,
                stableConfig == null ? null : StableSettingsImport.SongsPath(stableConfig, stablePath));

            // Seeds and beatmap sets dropped on the window (see FreeplayDrops).
            OsuGame.FileDropHandler = path => FreeplayDrops.Handle(path, freeplayStorage, library);

            // FIX (no lazer UI): the hint under "Интерфейс в игре" explains hiding it, so osu!lazer's notification about it isn't shown.
            osu.Game.Screens.Play.HUDOverlay.ShowHiddenInterfaceNotice = false;
            dropOwner = this;
            FreeplayDrops.Handled += onDropped;
            FreeplayRunScreen.Running += onRunning;

            selectedMode = FreeplayMode.AVAILABLE.FirstOrDefault(m => m.Id == FreeplaySettings.Current.SelectedMode) ?? FreeplayMode.AVAILABLE[0];

            // The stars from and to: the range each track's stars come from, or the limits of a pp target.
            starRating.BindValueChanged(s =>
            {
                if (syncingDifficulty)
                    return;

                var modeSettings = FreeplaySettings.Current.GetMode(selectedMode);
                modeSettings.StarRating = s.NewValue;

                if (s.NewValue > starRatingMax.Value)
                    starRatingMax.Value = s.NewValue;
                else
                    modeSettings.StarRatingMax ??= starRatingMax.Value;

                updateSummary();
            });

            starRatingMax.BindValueChanged(s =>
            {
                if (syncingDifficulty)
                    return;

                var settings = FreeplaySettings.Current;
                var modeSettings = settings.GetMode(selectedMode);

                // A mode with goals takes the top of the range as the player's level, and its goal sets the stars around it.
                if (selectedMode.HasGoals && !modeSettings.GoalEdited && modeSettings.Goal != null)
                {
                    settings.PlayerLevel = s.NewValue;
                    AimGoals.ApplyStars(modeSettings, s.NewValue);
                    modeSettings.StarRatingMax = s.NewValue;
                    syncDifficulty(selectedMode);
                }
                else
                {
                    modeSettings.StarRatingMax = s.NewValue;

                    if (s.NewValue < starRating.Value)
                        starRating.Value = s.NewValue;
                }

                updateSummary();
            });

            usePerformance.BindValueChanged(p =>
            {
                performanceSlider.FadeTo(p.NewValue ? 1 : 0, 150);

                if (syncingDifficulty)
                    return;

                FreeplaySettings.Current.GetMode(selectedMode).UsePerformance = p.NewValue;
                updateSummary();
            });

            performance.BindValueChanged(p =>
            {
                if (syncingDifficulty)
                    return;

                FreeplaySettings.Current.GetMode(selectedMode).TargetPerformance = p.NewValue;
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

            for (int i = 0; i < FreeplayMode.AVAILABLE.Length; i++)
            {
                var mode = FreeplayMode.AVAILABLE[i];
                var panel = new PresetPanel(mode.Name, string.Empty, i % 2 == 0 ? FreeplayColours.PINK : FreeplayColours.BLUE, () => onModeClicked(mode));

                modePanels.Add(panel);
                panels.Add(panel);
            }

            // Not a mode: the player's own seeds, shared maps played as they are.
            panels.Add(new PresetPanel("Сиды", "карты по коду или файлу от друзей · свои сиды", new Color4(190, 160, 240, 255), () => this.Push(new FreeplaySeedsScreen(freeplayStorage, library)))
            {
                Margin = new MarginPadding { Top = 10 },
            });

            presetList = new Container
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                RelativeSizeAxes = Axes.Y,
                Width = 600,
                Alpha = 0,
                Children = new Drawable[]
                {
                    presetHead = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 6),
                        Padding = new MarginPadding { Left = 30, Right = 10 },
                        Children = new Drawable[]
                        {
                            Heading("Выбери, как играть", 36),
                            new Controls.FreeplaySlider<double>("Звёзды от", starRating, v => $"{v:0.0}★", keyboardStep: 0.1f),
                            new Controls.FreeplaySlider<double>("Звёзды до", starRatingMax, v => $"{v:0.0}★", keyboardStep: 0.1f),
                            performanceCheckbox = new Controls.FreeplayCheckbox("По PP", usePerformance,
                                "Сложность — сколько PP дало бы SS карты. Короткий трек выйдет сложнее длинного, а звёзды выше остаются пределом."),
                            performanceSlider = new Controls.FreeplaySlider<double>("PP за SS", performance, v => $"{v:0}pp", keyboardStep: 5),
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
                                // The mods button sticks out to the left of the column, so "Играть" stays where it was.
                                Margin = new MarginPadding { Left = -260 },
                                Children = new Drawable[]
                                {
                                    // Not a mode: the mods page applies to every mode.
                                    modsButton = new Controls.FreeplayButton("Моды", () => this.Push(new FreeplayModsScreen(freeplayStorage)), new Color4(150, 115, 210, 255), 250, 50)
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                    },
                                    Button("Играть", () => play(selectedMode), 250),
                                    Button("Настройки режима", () => this.Push(new FreeplayModeSettingsScreen(freeplayStorage, library, selectedMode)), 250, blue: true),
                                }
                            },
                        }
                    },
                    presetScroll = new Controls.FreeplayScrollContainer
                    {
                        // As wide as the buttons above, with the mods button sticking out to the left.
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        RelativeSizeAxes = Axes.Y,
                        Width = 600 + 260,
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
                new FillFlowContainer
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = "freeplay~ ♡ каждая попытка — новая карта",
                            Font = OsuFont.GetFont(size: 18, weight: FontWeight.SemiBold),
                            Colour = FreeplayColours.TitleGradient,
                        },
                        // As osu!stable shows: how long the game has been running, and the time of day.
                        clock = new OsuSpriteText
                        {
                            Font = OsuFont.GetFont(size: 16, weight: FontWeight.SemiBold),
                            Colour = FreeplayColours.TEXT_DIM,
                        },
                    }
                },
            });

            if (FreeplayIntro.ShouldPlay)
                AddInternal(new FreeplayIntro());

            selectMode(selectedMode);
        }

        private StableConfig? stableConfig;
        private StableSettingsImport settingsImport = null!;

        [Resolved(canBeNull: true)]
        private OsuGame? game { get; set; }

        private OsuSpriteText skinToast = null!;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // FIX (no lazer UI): freeplay~'s own messages and volume display, in place of osu!lazer's.
            if (game != null)
                FreeplayOverlays.AddTo(game);

            // Whoever plays gets their own osu!stable settings: volume, offset, skin, keys and so on.
            // (Not in tests, which must not pick up the settings of the computer they run on.)
            if (stableConfig != null && FreeplaySettings.Current.Stable.SyncWithStable && !DebugUtils.IsNUnitRunning)
            {
                settingsImport.Apply(stableConfig, FreeplaySettings.Current);
                freeplayStorage.SaveSettings(FreeplaySettings.Current);
            }

            StableSkinSelector.Apply(skins, library, FreeplaySettings.Current.Stable.Skin);

            music.Play();

            // osu!'s hotkeys for its settings and skins go to freeplay's instead: Ctrl+O opens these settings,
            // and the skin hotkeys move through osu!stable's skins.
            if (game != null)
            {
                game.OpenSettingsHandler = openSettings;
                game.CycleSkinHandler = cycleSkin;
            }

            AddInternal(skinToast = new OsuSpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Y = 40,
                Font = OsuFont.GetFont(Typeface.TorusAlternate, 26, FontWeight.Bold),
                Colour = FreeplayColours.TitleGradient,
                Shadow = true,
                Alpha = 0,
            });
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
                    int index = Array.IndexOf(FreeplayMode.AVAILABLE, selectedMode) + (e.Action == GlobalAction.SelectNext ? 1 : -1);
                    selectMode(FreeplayMode.AVAILABLE[(index + FreeplayMode.AVAILABLE.Length) % FreeplayMode.AVAILABLE.Length]);
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

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            FreeplayDrops.Handled -= onDropped;
            FreeplayRunScreen.Running -= onRunning;

            if (OsuGame.FileDropHandler != null && ReferenceEquals(this, dropOwner))
                OsuGame.FileDropHandler = null;

            if (game != null)
            {
                game.OpenSettingsHandler = null;
                game.CycleSkinHandler = null;
            }
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            music.Stop();
            freeplayStorage.SaveSettings(FreeplaySettings.Current);
            return base.OnExiting(e);
        }

        private bool openSettings()
        {
            // Only from the menu itself; elsewhere (gameplay, other pages) the hotkey does nothing.
            if (this.IsCurrentScreen())
                this.Push(new FreeplaySettingsScreen(freeplayStorage, library));

            return true;
        }

        /// <summary>
        /// Switches to the next (or previous, or a random for 0) osu!stable skin, and remembers it.
        /// </summary>
        private bool cycleSkin(int step)
        {
            var options = new List<string?> { null };
            options.AddRange(StableSkin.List(library.SkinsPath));

            int current = Math.Max(0, options.IndexOf(FreeplaySettings.Current.Stable.Skin));
            int next = step == 0 && options.Count > 1
                ? (current + rng.Next(1, options.Count)) % options.Count
                : ((current + step) % options.Count + options.Count) % options.Count;

            string? skin = options[next];

            FreeplaySettings.Current.Stable.Skin = skin;
            StableSkinSelector.Apply(skins, library, skin);
            freeplayStorage.SaveSettings(FreeplaySettings.Current);

            skinToast.Text = $"Скин: {skin ?? "встроенный"}";
            skinToast.FinishTransforms();
            skinToast.FadeIn(150).Delay(1500).FadeOut(500);

            return true;
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
                modePanels[i].Selected = FreeplayMode.AVAILABLE[i] == mode;


            syncDifficulty(mode);

            updateSummary();
        }

        private void play(FreeplayMode mode) => this.Push(new FreeplayRunScreen(freeplayStorage, library, mode));

        private void updateSummary()
        {
            var settings = FreeplaySettings.Current;
            var selected = settings.GetMode(selectedMode);

            for (int i = 0; i < modePanels.Count; i++)
            {
                var mode = settings.GetMode(FreeplayMode.AVAILABLE[i]);
                string stars = mode.StarRatingMax != null && Math.Abs(mode.StarRatingMax.Value - mode.StarRating) > 0.05
                    ? $"{Math.Min(mode.StarRating, mode.StarRatingMax.Value):0.0}–{Math.Max(mode.StarRating, mode.StarRatingMax.Value):0.0}★"
                    : $"{mode.StarRating:0.0}★";

                if (mode.UsePerformance && canTargetPerformance(FreeplayMode.AVAILABLE[i]))
                    stars = $"{mode.TargetPerformance:0}pp ({stars})";

                string goal = FreeplayMode.AVAILABLE[i].HasGoals ? $"{AimGoals.Name(mode)} · " : string.Empty;
                string ramp = mode.Style == Generation.PatternStyle.Metronome && mode.Ramp ? " · разгон" : string.Empty;
                string preset = settings.ActivePreset(FreeplayMode.AVAILABLE[i]) is ModePreset active ? $"«{active.Name}» · " : string.Empty;
                modePanels[i].Detail = $"{preset}{goal}{stars}{ramp} · {DescribeBpm(mode.MinBpm, mode.MaxBpm)}";
            }

            modsButton.Text = FreeplayMods.Short(settings.Mods, settings.DoubleTimeSpeed) is string on ? $"Моды: {on}" : "Моды";

            string description = selectedMode.HasGoals && selected.Goal is AimGoal g && !selected.GoalEdited ? AimGoals.Describe(g) : selectedMode.Description;
            var fail = AimGoals.FailFor(selected.FailLevel, settings.Fail);

            modeDescription.Text = $"{description}\nТреки: {FreeplayModeSettingsScreen.DescribeTracks(selected.TrackCollection, settings, library)}"
                                   + $"\nПоражение: {selected.FailLevel.GetDescription().ToLowerInvariant()} ({AimGoals.DescribeFail(fail)})";
        }

        /// <summary>
        /// Sets the difficulty controls to <paramref name="mode"/>'s settings.
        /// </summary>
        private void syncDifficulty(FreeplayMode mode)
        {
            var s = FreeplaySettings.Current.GetMode(mode);

            syncingDifficulty = true;
            starRating.Value = Math.Min(s.StarRating, s.StarRatingMax ?? s.StarRating);
            starRatingMax.Value = Math.Max(s.StarRating, s.StarRatingMax ?? s.StarRating);
            usePerformance.Value = s.UsePerformance && canTargetPerformance(mode);
            performance.Value = s.TargetPerformance;
            syncingDifficulty = false;

            // (A remix plays the mapper's difficulty: it has no pp to aim for.)
            performanceCheckbox.Alpha = canTargetPerformance(mode) ? 1 : 0;
            performanceSlider.Alpha = usePerformance.Value ? 1 : 0;
        }

        private static bool canTargetPerformance(FreeplayMode mode) => FreeplaySettings.Current.GetMode(mode).RhythmSource != Generation.RhythmSource.Remix;

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
