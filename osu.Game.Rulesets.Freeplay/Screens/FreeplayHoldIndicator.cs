// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Graphics.Containers;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// How far a held Esc is from leaving the track (FIX: osu!'s own "hold for menu" button is unseen, so holding Esc used to leave with no
    /// warning). Shown only while held.
    /// </summary>
    public partial class FreeplayHoldIndicator : CompositeDrawable
    {
        private readonly HoldToConfirmContainer source;
        private readonly Box fill;

        public FreeplayHoldIndicator(HoldToConfirmContainer source)
        {
            this.source = source;

            Anchor = Anchor.TopCentre;
            Origin = Anchor.TopCentre;
            AutoSizeAxes = Axes.Both;
            Margin = new MarginPadding { Top = 60 };
            Alpha = 0;
            // (Updated while unseen, to notice the hold starting.)
            AlwaysPresent = true;

            InternalChild = new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 10,
                BorderThickness = 2,
                BorderColour = FreeplayColours.TitleGradient,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayColours.BACKGROUND, Alpha = 0.9f },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 6),
                        Padding = new MarginPadding { Horizontal = 18, Vertical = 10 },
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "Держи, чтобы выйти из трека",
                                Font = OsuFont.GetFont(size: 16, weight: FontWeight.SemiBold),
                                Colour = FreeplayColours.WHITE,
                            },
                            new Container
                            {
                                Size = new Vector2(220, 6),
                                Masking = true,
                                CornerRadius = 3,
                                Children = new Drawable[]
                                {
                                    new Box { RelativeSizeAxes = Axes.Both, Colour = osuTK.Graphics.Color4.Black, Alpha = 0.6f },
                                    fill = new Box { RelativeSizeAxes = Axes.Both, Width = 0, Colour = ColourInfo.GradientHorizontal(FreeplayColours.BLUE, FreeplayColours.PINK) },
                                }
                            },
                        }
                    },
                }
            };
        }
        private bool shown;

        // Read every frame rather than bound: osu!'s button unbinds its progress when the hold completes, which would leave this stuck.
        // (Shown from a little way in, so a quick tap of Esc, which only pauses, doesn't flash it.)
        protected override void Update()
        {
            base.Update();

            double value = source.Progress.Value;
            fill.Width = (float)value;

            bool show = value > 0.15;

            if (show != shown)
            {
                shown = show;
                this.FadeTo(show ? 1 : 0, 100);
            }
        }
    }
}
