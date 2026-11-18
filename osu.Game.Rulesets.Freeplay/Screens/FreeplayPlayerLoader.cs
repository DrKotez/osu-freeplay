// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// The loading screen before each track, with the track's BPM and the run's mode shown above the usual song details.
    /// </summary>
    public partial class FreeplayPlayerLoader : PlayerLoader
    {
        private readonly double bpm;
        private readonly string detail;

        /// <param name="createPlayer">Creates the player.</param>
        /// <param name="bpm">The track's main BPM.</param>
        /// <param name="detail">A line under the BPM, e.g. the mode and star rating.</param>
        public FreeplayPlayerLoader(Func<Player> createPlayer, double bpm, string detail)
            : base(createPlayer)
        {
            this.bpm = bpm;
            this.detail = detail;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            AddInternal(new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Both,
                Margin = new MarginPadding { Top = 60 },
                Masking = true,
                CornerRadius = 12,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.Black,
                        Alpha = 0.6f,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Horizontal = 28, Vertical = 12 },
                        Spacing = new Vector2(0, 2),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = bpm > 0 ? $"{bpm:0} BPM" : "BPM неизвестен",
                                Font = OsuFont.GetFont(Typeface.TorusAlternate, 44, FontWeight.Bold),
                                Colour = FreeplayColours.TitleGradient,
                                Shadow = true,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = detail,
                                Font = OsuFont.GetFont(size: 20, weight: FontWeight.SemiBold),
                                Colour = FreeplayColours.WHITE,
                            },
                        }
                    },
                }
            });
        }
    }
}
