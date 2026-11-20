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
        private VolumeBar master = null!, music = null!, effects = null!;

        private readonly BindableDouble masterVolume = new BindableDouble(), musicVolume = new BindableDouble(), effectsVolume = new BindableDouble();

        public FreeplayOverlays()
        {
            RelativeSizeAxes = Axes.Both;
            Depth = float.MinValue;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
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
                            Spacing = new Vector2(0, 10),
                            Padding = new MarginPadding(18),
                            Children = new Drawable[]
                            {
                                master = new VolumeBar("Громкость"),
                                music = new VolumeBar("Музыка"),
                                effects = new VolumeBar("Эффекты"),
                            }
                        },
                    }
                },
            };

            masterVolume.BindTo(audio.Volume);
            musicVolume.BindTo(audio.VolumeTrack);
            effectsVolume.BindTo(audio.VolumeSample);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Current = this;

            bool loaded = false;

            void showVolume()
            {
                master.Value = masterVolume.Value;
                music.Value = musicVolume.Value;
                effects.Value = effectsVolume.Value;

                // (Not for the values set as the game starts.)
                if (!loaded)
                    return;

                volume.FadeIn(100).Delay(1500).FadeOut(400);
            }

            masterVolume.BindValueChanged(_ => showVolume());
            musicVolume.BindValueChanged(_ => showVolume());
            effectsVolume.BindValueChanged(_ => showVolume());
            showVolume();
            loaded = true;
        }

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

        private partial class VolumeBar : FillFlowContainer
        {
            private readonly Box fill;
            private readonly OsuSpriteText percent;

            public double Value
            {
                set
                {
                    fill.ResizeWidthTo((float)Math.Clamp(value, 0, 1), 150, Easing.OutQuint);
                    percent.Text = $"{value:P0}";
                }
            }

            public VolumeBar(string label)
            {
                AutoSizeAxes = Axes.Both;
                Direction = FillDirection.Vertical;
                Spacing = new Vector2(0, 4);

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
                };
            }
        }

        /// <summary>
        /// Adds freeplay~'s overlays to <paramref name="game"/> once, and hides osu!lazer's volume meters and on-screen toasts.
        /// </summary>
        public static void AddTo(OsuGame game)
        {
            if (Current != null && Current.Parent != null)
                return;

            game.Add(new FreeplayOverlays());

            foreach (var lazerVolume in game.ChildrenOfType<VolumeOverlay>())
                lazerVolume.Colour = Color4.Transparent;

            foreach (var toasts in game.ChildrenOfType<OnScreenDisplay>())
                toasts.Colour = Color4.Transparent;

            Game.Graphics.ScreenshotManager.ShowSaved = file => Show($"Скриншот сохранён: {file}");
        }
    }
}
