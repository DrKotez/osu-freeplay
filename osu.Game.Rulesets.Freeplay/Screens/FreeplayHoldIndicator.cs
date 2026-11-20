// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;
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
        private readonly IBindable<double> progress;
        private readonly Box fill;

        public FreeplayHoldIndicator(IBindable<double> progress)
        {
            this.progress = progress.GetBoundCopy();

            Anchor = Anchor.TopCentre;
            Origin = Anchor.TopCentre;
            AutoSizeAxes = Axes.Both;
            Margin = new MarginPadding { Top = 60 };
            Alpha = 0;

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

        protected override void LoadComplete()
        {
            base.LoadComplete();

            progress.BindValueChanged(p =>
            {
                fill.Width = (float)p.NewValue;
                this.FadeTo(p.NewValue > 0.02 ? 1 : 0, 100);
            }, true);
        }
    }
}
