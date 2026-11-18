// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// The big circle at the centre of the menu, in the spirit of osu!stable's "cookie". It pulses to the music.
    /// </summary>
    public partial class FreeplayCookie : CompositeDrawable
    {
        private readonly Func<float> amplitude;
        private readonly Container pulse;

        public Action? Action;

        public FreeplayCookie(float size, Func<float> amplitude)
        {
            this.amplitude = amplitude;

            Size = new Vector2(size);
            Origin = Anchor.Centre;

            InternalChild = pulse = new CircularContainer
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                BorderThickness = size * 0.045f,
                BorderColour = FreeplayColours.WHITE,
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = FreeplayColours.PINK.Opacity(0.5f),
                    Radius = 40,
                },
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientVertical(FreeplayColours.BLUE, FreeplayColours.PINK),
                    },
                    // A white band through the middle, like the centre stripe of the flag.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Height = 0.22f,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = FreeplayColours.WHITE,
                        Alpha = 0.85f,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = "freeplay~",
                        Font = OsuFont.GetFont(Typeface.TorusAlternate, size * 0.17f, FontWeight.Bold),
                        Colour = FreeplayColours.DEEP_PINK,
                    },
                }
            };
        }

        protected override void Update()
        {
            base.Update();

            // Ease towards the music's loudness, so the pulse follows the beat without jittering.
            float target = 1 + Math.Clamp(amplitude(), 0, 1) * 0.08f + (IsHovered ? 0.04f : 0);
            float current = pulse.Scale.X;
            float eased = current + (target - current) * (float)Math.Min(1, Time.Elapsed / 60);

            pulse.Scale = new Vector2(eased);
        }

        protected override bool OnHover(HoverEvent e) => true;

        protected override bool OnClick(ClickEvent e)
        {
            Action?.Invoke();
            pulse.ScaleTo(0.92f).ScaleTo(1, 400, Easing.OutElastic);
            return true;
        }
    }

    /// <summary>
    /// A slanted menu bar, like osu!stable's main menu buttons.
    /// </summary>
    public partial class MenuBar : CompositeDrawable
    {
        public const float WIDTH = 380;
        public const float HEIGHT = 62;

        private readonly Action action;
        private readonly Container content;
        private readonly Box flash;

        public MenuBar(string text, Color4 colour, Action action)
        {
            this.action = action;

            Size = new Vector2(WIDTH, HEIGHT);

            InternalChild = content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Shear = new Vector2(-0.2f, 0),
                Masking = true,
                CornerRadius = 6,
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Colour = Color4.Black.Opacity(0.35f),
                    Radius = 8,
                },
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientHorizontal(colour, colour.Darken(0.6f)),
                    },
                    flash = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.White,
                        Alpha = 0,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Shear = new Vector2(0.2f, 0),
                        X = 30,
                        Text = text,
                        Font = OsuFont.GetFont(Typeface.TorusAlternate, 30, FontWeight.Bold),
                        Shadow = true,
                    },
                }
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            content.MoveToX(18, 200, Easing.OutQuint);
            flash.FadeTo(0.12f, 200);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            content.MoveToX(0, 300, Easing.OutQuint);
            flash.FadeOut(300);
        }

        protected override bool OnClick(ClickEvent e)
        {
            flash.FadeTo(0.5f).FadeOut(400);
            action();
            return true;
        }
    }

    /// <summary>
    /// A selectable panel like osu!stable's song select entries: a title, a line of detail and a coloured edge.
    /// </summary>
    public partial class PresetPanel : CompositeDrawable
    {
        public const float HEIGHT = 74;

        private readonly Action action;
        private readonly Container content;
        private readonly Box highlight;
        private readonly OsuTextFlowContainer detailText;

        public string Detail
        {
            set => detailText.Text = value;
        }

        private bool selected;

        /// <summary>
        /// Whether this panel is the chosen one. Clicking a selected panel confirms it, like on osu!stable's song select.
        /// </summary>
        public bool Selected
        {
            get => selected;
            set
            {
                selected = value;
                updateState();
            }
        }

        private bool hovered;

        public PresetPanel(string title, string detail, Color4 colour, Action action)
        {
            this.action = action;

            RelativeSizeAxes = Axes.X;
            Height = HEIGHT;

            InternalChild = content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 8,
                BorderThickness = 2,
                BorderColour = colour.Opacity(0.7f),
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.Black,
                        Alpha = 0.65f,
                    },
                    highlight = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientHorizontal(colour.Opacity(0.45f), colour.Opacity(0)),
                        Alpha = 0.4f,
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = 8,
                        Colour = colour,
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Padding = new MarginPadding { Left = 24, Right = 14 },
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(2),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = title,
                                Font = OsuFont.GetFont(Typeface.TorusAlternate, 26, FontWeight.Bold),
                                Shadow = true,
                            },
                            detailText = new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 15))
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Colour = FreeplayColours.TEXT_DIM,
                                Text = detail,
                            },
                        }
                    },
                }
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            hovered = true;
            updateState();
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hovered = false;
            updateState();
        }

        private void updateState()
        {
            content.MoveToX(selected ? -40 : hovered ? -24 : 0, 300, Easing.OutQuint);
            highlight.FadeTo(selected || hovered ? 1 : 0.4f, 200);
            content.BorderThickness = selected ? 4 : 2;
        }

        protected override bool OnClick(ClickEvent e)
        {
            action();
            return true;
        }
    }
}
