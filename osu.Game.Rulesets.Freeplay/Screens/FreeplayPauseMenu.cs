// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Freeplay.Screens.Controls;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// freeplay~'s pause menu during a track (FIX: in place of osu!lazer's look). It mirrors osu!'s pause overlay, which stays underneath unseen:
    /// the same buttons, in the same order, doing the same; that one keeps handling the keys (Esc to resume, arrows and Enter).
    /// </summary>
    public partial class FreeplayPauseMenu : CompositeDrawable
    {
        private readonly GameplayMenuOverlay overlay;
        private readonly FillFlowContainer buttons;

        public FreeplayPauseMenu(GameplayMenuOverlay overlay)
        {
            this.overlay = overlay;

            RelativeSizeAxes = Axes.Both;
            Alpha = 0;

            InternalChildren = new Drawable[]
            {
                new Backdrop { RelativeSizeAxes = Axes.Both, Colour = FreeplayColours.BACKGROUND, Alpha = 0.88f },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 14),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "Пауза",
                            Font = OsuFont.GetFont(Typeface.TorusAlternate, 64, FontWeight.Bold),
                            Colour = FreeplayColours.TitleGradient,
                            Margin = new MarginPadding { Bottom = 10 },
                        },
                        buttons = new FillFlowContainer
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 12),
                        },
                    }
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            overlay.State.BindValueChanged(state =>
            {
                if (state.NewValue == Visibility.Visible)
                {
                    rebuild();
                    this.FadeIn(150, Easing.OutQuint);
                }
                else
                    this.FadeOut(150, Easing.OutQuint);
            }, true);
        }

        /// <summary>
        /// osu!'s buttons as they are now (the player renames and adds some), in their order on screen.
        /// </summary>
        private void rebuild()
        {
            buttons.Clear();

            var ordered = overlay.Buttons.OrderBy(b => b.Parent is FillFlowContainer<DialogButton> flow ? flow.GetLayoutPosition(b) : 0).ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var button = ordered[i];

                Color4 colour = i == 0 ? FreeplayColours.DEEP_PINK : i == ordered.Count - 1 ? new Color4(90, 70, 110, 255) : FreeplayColours.DEEP_BLUE;

                var ours = new MirroredButton(button.Text.ToString(), () => button.TriggerClick(), colour, button)
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                };

                buttons.Add(ours);
            }
        }

        protected override void Update()
        {
            base.Update();

            // FIX: the arrows pick a button in osu!'s unseen menu, and Enter presses it: the one picked is shown here.
            foreach (var button in buttons.OfType<MirroredButton>())
                button.Highlighted = button.Mirrored.State == SelectionState.Selected;
        }

        /// <summary>
        /// One of osu!'s buttons, drawn freeplay~'s way. The mouse over it picks osu!'s too, so Enter presses what's under the mouse.
        /// </summary>
        private partial class MirroredButton : FreeplayButton
        {
            public readonly DialogButton Mirrored;

            public MirroredButton(string text, System.Action action, Color4 colour, DialogButton mirrored)
                : base(text, action, colour, 340, 54)
            {
                Mirrored = mirrored;
            }

            protected override bool OnHover(HoverEvent e)
            {
                Mirrored.State = SelectionState.Selected;
                return base.OnHover(e);
            }
        }

        /// <summary>
        /// Keeps clicks from reaching osu!'s unseen buttons underneath.
        /// </summary>
        private partial class Backdrop : Box
        {
            protected override bool OnMouseDown(MouseDownEvent e) => true;
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnHover(HoverEvent e) => true;
        }
    }
}
