// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Framework.Testing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// freeplay~'s own messages and volume display over the whole game (FIX: in place of osu!lazer's notifications, volume meters and
    /// on-screen toasts, which are hidden). Added to the game once.
    /// </summary>
    public partial class FreeplayOverlays : CompositeDrawable
    {
        /// <summary>
        /// The one shown, for messages from anywhere (e.g. files dropped on the window, a screenshot saved).
        /// </summary>
        public static FreeplayOverlays? Current { get; private set; }

        private const double message_time = 3500;

        private FillFlowContainer messages = null!;
        private Container volume = null!;
        private VolumeBar[] bars = null!;

        private readonly BindableDouble[] volumes = { new BindableDouble(), new BindableDouble(), new BindableDouble(), new BindableDouble() };

        /// <summary>
        /// osu!'s volume overlay, unseen: it still takes Alt+wheel, Alt+arrows and Ctrl+F4, and this shows what it does.
        /// </summary>
        private VolumeOverlay? lazerVolume;

        private OsuGame? game;

        private double nextVolumeLookup;

        public FreeplayOverlays()
        {
            RelativeSizeAxes = Axes.Both;
            Depth = float.MinValue;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio, OsuConfigManager config)
        {
            // In osu!'s order: Alt+wheel changes the one selected, Alt+left/right or the mouse picks another (FIX: only the overall
            // volume could be changed, as picking was in osu!'s hidden overlay).
            bars = new[]
            {
                new VolumeBar("Общая", 0, this),
                new VolumeBar("Музыка", 1, this),
                new VolumeBar("Эффекты", 2, this),
                new VolumeBar("Хитсаунды", 3, this),
            };

            InternalChildren = new Drawable[]
            {
                messages = new FillFlowContainer
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 8),
                    Margin = new MarginPadding { Bottom = 90 },
                },
                volume = new Container
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    AutoSizeAxes = Axes.Both,
                    Margin = new MarginPadding { Right = 40 },
                    Alpha = 0,
                    Masking = true,
                    CornerRadius = 10,
                    BorderThickness = 2,
                    BorderColour = FreeplayColours.TitleGradient,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayColours.BACKGROUND, Alpha = 0.92f },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 6),
                            Padding = new MarginPadding(14),
                            Children = new Drawable[]
                            {
                                new FillFlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, 4),
                                    Children = bars,
                                },
                                new OsuSpriteText
                                {
                                    Text = "Alt+колесо · Alt+←/→ или мышь — выбрать",
                                    Font = OsuFont.GetFont(size: 13),
                                    Colour = FreeplayColours.TEXT_DIM,
                                    Margin = new MarginPadding { Left = 4, Top = 2 },
                                },
                            }
                        },
                    }
                },
            };

            volumes[0].BindTo(audio.Volume);
            volumes[1].BindTo(audio.VolumeTrack);
            volumes[2].BindTo(audio.VolumeSample);
            volumes[3].BindTo(config.GetBindable<double>(OsuSetting.HitsoundVolume));
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Current = this;

            for (int i = 0; i < volumes.Length; i++)
            {
                var bar = bars[i];
                volumes[i].BindValueChanged(v => bar.Value = v.NewValue, true);
            }
        }

        protected override void Update()
        {
            base.Update();

            // (Looked for once a second until found, rather than through the whole game on every frame.)
            if (lazerVolume == null && Time.Current >= nextVolumeLookup)
            {
                nextVolumeLookup = Time.Current + 1000;
                lazerVolume = game?.ChildrenOfType<VolumeOverlay>().FirstOrDefault();
            }

            // Shown while osu!'s overlay would be (it opens on Alt+wheel and on any volume change, and closes a moment after).
            bool shown = lazerVolume?.State.Value == Visibility.Visible;

            if (shown != volume.Alpha > 0.5f)
                volume.FadeTo(shown ? 1 : 0, shown ? 100 : 300);

            if (!shown)
                return;

            int selected = lazerVolume!.SelectedMeterIndex;

            for (int i = 0; i < bars.Length; i++)
                bars[i].Selected = i == selected;
        }

        private void select(int index) => lazerVolume?.SelectMeter(index);

        /// <summary>
        /// Shows <paramref name="text"/> for a few seconds at the bottom of the screen (from any thread).
        /// </summary>
        public static void Show(string text) => Current?.Schedule(() => Current.add(text));

        private void add(string text)
        {
            while (messages.Count >= 3)
                messages.Remove(messages.First(), true);

            var message = new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 10,
                BorderThickness = 2,
                BorderColour = FreeplayColours.TitleGradient,
                Alpha = 0,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayColours.BACKGROUND, Alpha = 0.92f },
                    new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 18, weight: FontWeight.SemiBold))
                    {
                        AutoSizeAxes = Axes.Both,
                        MaximumSize = new Vector2(700, float.MaxValue),
                        Padding = new MarginPadding { Horizontal = 20, Vertical = 12 },
                        Text = text,
                        Colour = FreeplayColours.WHITE,
                    },
                }
            };

            messages.Add(message);
            message.FadeIn(200).Delay(message_time).FadeOut(400).Expire();
        }

        protected override void Dispose(bool isDisposing)
        {
            if (Current == this)
                Current = null;

            base.Dispose(isDisposing);
        }

        private partial class VolumeBar : Container
        {
            private readonly Box fill;
            private readonly Box highlight;
            private readonly OsuSpriteText percent;
            private readonly int index;
            private readonly FreeplayOverlays owner;

            public double Value
            {
                set
                {
                    fill.ResizeWidthTo((float)Math.Clamp(value, 0, 1), 150, Easing.OutQuint);
                    percent.Text = $"{value:P0}";
                }
            }

            public bool Selected
            {
                set => highlight.Alpha = value ? 0.18f : 0;
            }

            public VolumeBar(string label, int index, FreeplayOverlays owner)
            {
                this.index = index;
                this.owner = owner;

                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = 6;

                Children = new Drawable[]
                {
                    highlight = new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayColours.PINK, Alpha = 0 },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 4),
                        Padding = new MarginPadding { Horizontal = 6, Vertical = 5 },
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(8, 0),
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText { Text = label, Font = OsuFont.GetFont(size: 15, weight: FontWeight.SemiBold), Colour = FreeplayColours.TEXT_DIM },
                                    percent = new OsuSpriteText { Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold), Colour = FreeplayColours.PINK },
                                }
                            },
                            new Container
                            {
                                Size = new Vector2(200, 8),
                                Masking = true,
                                CornerRadius = 4,
                                Children = new Drawable[]
                                {
                                    new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.5f },
                                    fill = new Box { RelativeSizeAxes = Axes.Both, Width = 0, Colour = ColourInfo.GradientHorizontal(FreeplayColours.BLUE, FreeplayColours.PINK) },
                                }
                            },
                        }
                    },
                };
            }

            // The mouse over a bar picks it, as over osu!'s meters.
            protected override bool OnHover(HoverEvent e)
            {
                owner.select(index);
                return true;
            }
        }

        /// <summary>
        /// Adds freeplay~'s overlays to <paramref name="game"/> once, and hides osu!lazer's volume meters and on-screen toasts.
        /// </summary>
        public static void AddTo(OsuGame game)
        {
            if (Current != null && Current.Parent != null)
                return;

            game.Add(new FreeplayOverlays { game = game });

            foreach (var lazerVolume in game.ChildrenOfType<VolumeOverlay>())
                lazerVolume.Colour = Color4.Transparent;

            foreach (var toasts in game.ChildrenOfType<OnScreenDisplay>())
                toasts.Colour = Color4.Transparent;

            Game.Graphics.ScreenshotManager.ShowSaved = file => Show($"Скриншот сохранён: {file}");
        }
    }
}
