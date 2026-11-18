// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Screens;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// Shared layout for freeplay's menu screens: a dimmed background with a centred column of content.
    /// </summary>
    public abstract partial class FreeplayScreen : OsuScreen
    {
        protected const float CONTENT_WIDTH = 640;

        private const float transition_duration = 250;

        protected readonly Container Content;

        protected FreeplayScreen()
        {
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                    Alpha = 0.6f,
                },
                Content = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Vertical = 40, Horizontal = HORIZONTAL_OVERFLOW_PADDING },
                },
            };
        }

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

        protected static RoundedButton Button(string text, Action action, float width = 320) => new RoundedButton
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Width = width,
            Height = 50,
            Text = text,
            Action = action,
        };

        protected static string FormatDuration(double milliseconds)
        {
            var time = TimeSpan.FromMilliseconds(milliseconds);
            return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time:mm\\:ss}" : $"{time:m\\:ss}";
        }
    }
}
