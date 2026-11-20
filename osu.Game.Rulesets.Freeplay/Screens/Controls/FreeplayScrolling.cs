// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Freeplay.Screens.Controls
{
    /// <summary>
    /// A scrolling page with a thin bar in the flag colours (FIX: in place of osu!lazer's grey and green one).
    /// </summary>
    public partial class FreeplayScrollContainer : OsuScrollContainer
    {
        protected override ScrollbarContainer CreateScrollbar(Direction direction) => new FreeplayScrollbar(direction);

        private partial class FreeplayScrollbar : ScrollbarContainer
        {
            private const float width = 6;

            private readonly Box box;

            protected override float MinimumDimSize => width * 4;

            public FreeplayScrollbar(Direction direction)
                : base(direction)
            {
                CornerRadius = width / 2;
                Masking = true;
                Size = new Vector2(width);
                Margin = new MarginPadding(3);
                Alpha = 0.6f;

                Child = box = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(FreeplayColours.BLUE, FreeplayColours.PINK),
                };
            }

            public override void ResizeTo(float val, int duration = 0, Easing easing = Easing.None)
            {
                this.ResizeTo(new Vector2(width) { [(int)ScrollDirection] = val }, duration, easing);
            }

            protected override bool OnHover(HoverEvent e)
            {
                this.FadeTo(1, 100);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) => this.FadeTo(0.6f, 100);

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                if (!base.OnMouseDown(e))
                    return false;

                box.FadeColour(FreeplayColours.WHITE, 100);
                return true;
            }

            protected override void OnMouseUp(MouseUpEvent e)
            {
                box.FadeColour(ColourInfo.GradientVertical(FreeplayColours.BLUE, FreeplayColours.PINK), 100);
                base.OnMouseUp(e);
            }
        }
    }

    /// <summary>
    /// A text field in freeplay~'s colours (FIX: osu!lazer's text box set its own yellow border, grey background and purple selection on
    /// load, over the colours given in the constructor).
    /// </summary>
    public partial class FreeplayTextField : OsuTextBox
    {
        protected override Color4 SelectionColour => FreeplayColours.DEEP_PINK.Opacity(0.6f);

        protected override void LoadComplete()
        {
            base.LoadComplete();

            BackgroundUnfocused = FreeplayStyle.PANEL;
            BackgroundFocused = FreeplayStyle.PANEL_HOVER;
            BackgroundCommit = FreeplayColours.PINK.Opacity(0.5f);
            BorderColour = FreeplayColours.PINK;
            Placeholder.Colour = FreeplayColours.TEXT_DIM.Opacity(0.6f);
        }
    }
}
