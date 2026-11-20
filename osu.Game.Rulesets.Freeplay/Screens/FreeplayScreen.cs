// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Allocation;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Screens;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// Shared layout for freeplay's menu screens: a dimmed background with a centred column of content.
    /// </summary>
    public abstract partial class FreeplayScreen : OsuScreen
    {
        protected const float CONTENT_WIDTH = 640;

        private const float transition_duration = 250;

        private const float flag_stripe_height = 4;

        protected readonly Container Content;

        // freeplay~ has no toolbar.
        public override bool HideOverlaysOnEnter => true;

        protected override BackgroundScreen CreateBackground() => new FreeplayBackgroundScreen();

        /// <summary>
        /// How much the dark backdrop covers whatever is behind the screen.
        /// </summary>
        protected virtual float BackgroundDim => 0.88f;

        /// <summary>
        /// Whether the page has freeplay~'s own "← Назад" in the corner, in place of osu!lazer's (the menu and the run have their own ways back).
        /// </summary>
        protected virtual bool ShowBackBar => true;

        [BackgroundDependencyLoader]
        private void loadBackBar()
        {
            if (!ShowBackBar)
                return;

            // FIX: room at the bottom for the bar, so it doesn't cover a page's last rows or links.
            var padding = Content.Padding;
            padding.Bottom = Math.Max(padding.Bottom, 90);
            Content.Padding = padding;

            AddInternal(new Container
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                AutoSizeAxes = Axes.Both,
                Child = new MenuBar("← Назад", new osuTK.Graphics.Color4(90, 70, 110, 255), () => this.Exit()) { Scale = new osuTK.Vector2(0.7f) },
            });
        }

        protected FreeplayScreen()
        {
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            RelativeSizeAxes = Axes.Both;

            // FIX (no lazer UI): osu!lazer's "назад" button isn't shown; Esc still works through it (and pages have their own way back).
            FooterContent.Value = osu.Game.Screens.Footer.ScreenFooterContent.Legacy(showBackButton: false);

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = FreeplayColours.BACKGROUND,
                    Alpha = BackgroundDim,
                },
                // A soft wash of the flag colours, blue at the top fading to pink at the bottom.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(FreeplayColours.BLUE, FreeplayColours.PINK),
                    Alpha = 0.12f,
                },
                flagStripes(Anchor.TopLeft),
                flagStripes(Anchor.BottomLeft),
                Content = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Vertical = 40, Horizontal = HORIZONTAL_OVERFLOW_PADDING },
                },
            };
        }

        private static Drawable flagStripes(Anchor anchor)
        {
            var stripes = new FillFlowContainer
            {
                Anchor = anchor,
                Origin = anchor,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Alpha = 0.9f,
            };

            foreach (var colour in FreeplayColours.FLAG)
                stripes.Add(new Box { RelativeSizeAxes = Axes.X, Height = flag_stripe_height, Colour = colour });

            return stripes;
        }

        /// <summary>
        /// A large heading filled with the flag gradient.
        /// </summary>
        protected static OsuSpriteText Heading(string text, float size = 72) => Text(text, size, FontWeight.Bold, Typeface.TorusAlternate).With(t => t.Colour = FreeplayColours.TitleGradient);

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);
            this.FadeInFromZero(transition_duration, Easing.OutQuint);
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);
            this.FadeIn(transition_duration, Easing.OutQuint);
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            base.OnSuspending(e);
            this.FadeOut(transition_duration, Easing.OutQuint);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            if (base.OnExiting(e))
                return true;

            this.FadeOut(transition_duration, Easing.OutQuint);
            return false;
        }

        protected static OsuSpriteText Text(string text, float size = 18, FontWeight weight = FontWeight.Regular, Typeface typeface = Typeface.Torus) => new OsuSpriteText
        {
            Text = text,
            Font = OsuFont.GetFont(typeface, size, weight),
        };

        protected static FillFlowContainer Column(float spacing = 10) => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(spacing),
        };

        protected static Controls.FreeplayButton Button(string text, Action action, float width = 320, bool blue = false)
            => new Controls.FreeplayButton(text, action, blue ? FreeplayColours.DEEP_BLUE : FreeplayColours.DEEP_PINK, width, 50)
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
            };

        protected static string FormatDuration(double milliseconds)
        {
            var time = TimeSpan.FromMilliseconds(milliseconds);
            return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time:mm\\:ss}" : $"{time:m\\:ss}";
        }
    }
}
