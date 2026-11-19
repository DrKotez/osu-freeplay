// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Screens.Play;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// The skip button as osu!stable has it: the skin's own "play-skip" in the bottom right corner,
    /// or a small freeplay~ button there when the skin has none, in place of osu!'s wide skip bar.
    /// </summary>
    public partial class FreeplaySkipOverlay : SkipOverlay
    {
        public FreeplaySkipOverlay(double startTime)
            : base(startTime)
        {
            RelativeSizeAxes = Axes.Both;
            Size = Vector2.One;
            Position = new Vector2(0.5f);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // osu!'s countdown bar along the bottom isn't in osu!stable.
            RemainingTimeBox.Alpha = 0;
        }

        protected override OsuClickableContainer CreateButton(IBindable<bool> inSkipPeriod) => new SkipButton
        {
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            Enabled = { BindTarget = inSkipPeriod },
        };

        internal partial class SkipButton : OsuClickableContainer
        {
            public SkipButton()
            {
                // Before the base class's load, which sizes its content from this.
                AutoSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load(ISkinSource skin)
            {
                var texture = skin.GetTexture("play-skip") ?? skin.GetTexture("play-skip-0");

                Child = texture != null
                    ? new Sprite { Texture = texture }
                    : new Container
                    {
                        Size = new Vector2(190, 56),
                        Margin = new MarginPadding(24),
                        Masking = true,
                        CornerRadius = 8,
                        BorderThickness = 2,
                        BorderColour = FreeplayColours.TitleGradient,
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayColours.BACKGROUND, Alpha = 0.85f },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Text = "Пропустить »",
                                Font = OsuFont.GetFont(size: 22, weight: FontWeight.Bold),
                                Colour = FreeplayColours.PINK,
                            },
                        }
                    };

                Enabled.BindValueChanged(e => this.FadeTo(e.NewValue ? 1 : 0, 200), true);
            }
        }
    }
}
