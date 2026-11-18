// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Freeplay.Screens.Controls
{
    /// <summary>
    /// A big number with its label underneath, for summaries.
    /// </summary>
    public partial class StatTile : CompositeDrawable
    {
        public StatTile(string label, string value, Color4? accent = null, float width = 170)
        {
            Size = new Vector2(width, 84);
            Masking = true;
            CornerRadius = FreeplayStyle.CORNER_RADIUS + 2;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayStyle.PANEL, Alpha = 0.9f },
                new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = 3,
                    Colour = accent.HasValue ? (ColourInfo)accent.Value : FreeplayColours.TitleGradient,
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = value,
                            Font = OsuFont.GetFont(Typeface.TorusAlternate, 30, FontWeight.Bold),
                            Colour = accent ?? FreeplayColours.WHITE,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = label,
                            Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                            Colour = FreeplayColours.TEXT_DIM,
                        },
                    }
                },
            };
        }
    }

    /// <summary>
    /// A row of <see cref="StatTile"/>s which wraps onto more lines when it runs out of room.
    /// </summary>
    public partial class StatTiles : FillFlowContainer
    {
        public StatTiles(IEnumerable<Drawable> tiles)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Spacing = new Vector2(10);
            AddRange(tiles);
        }
    }

    /// <summary>
    /// A titled panel, like the sections of the settings pages.
    /// </summary>
    public partial class TitledPanel : CompositeDrawable
    {
        public readonly FillFlowContainer Body;

        public TitledPanel(string title)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Masking = true;
            CornerRadius = FreeplayStyle.CORNER_RADIUS + 2;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayStyle.PANEL, Alpha = 0.85f },
                new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = 4,
                    Colour = ColourInfo.GradientVertical(FreeplayColours.BLUE, FreeplayColours.PINK),
                },
                Body = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 2),
                    Padding = new MarginPadding { Left = 18, Right = 12, Vertical = 12 },
                    Child = new OsuSpriteText
                    {
                        Text = title,
                        Font = OsuFont.GetFont(Typeface.TorusAlternate, 24, FontWeight.Bold),
                        Colour = FreeplayColours.PINK,
                        Margin = new MarginPadding { Bottom = 6 },
                    },
                },
            };
        }
    }

    /// <summary>
    /// One line of a list (a track of a run, a run in the history): a main line, a detail line under it, and a value on the right.
    /// Lights up under the cursor.
    /// </summary>
    public partial class ListRow : CompositeDrawable
    {
        private readonly Box hover;

        public ListRow(string main, string detail, string right, Color4 marker)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Masking = true;
            CornerRadius = FreeplayStyle.CORNER_RADIUS;

            InternalChildren = new Drawable[]
            {
                hover = new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayStyle.PANEL_HOVER, Alpha = 0 },
                new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = 3,
                    Colour = marker,
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Left = 14, Right = 10, Vertical = 6 },
                    Children = new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Width = 0.78f,
                            Direction = FillDirection.Vertical,
                            Children = new Drawable[]
                            {
                                FreeplayStyle.TruncatingLabel(main, 16, FontWeight.SemiBold),
                                FreeplayStyle.TruncatingLabel(detail, 13.5f).With(t => t.Colour = FreeplayColours.TEXT_DIM),
                            }
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Text = right,
                            Font = OsuFont.GetFont(Typeface.TorusAlternate, 18, FontWeight.Bold),
                            Colour = marker,
                        },
                    }
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            hover.FadeTo(0.7f, 150);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hover.FadeOut(300);
            base.OnHoverLost(e);
        }
    }
}
