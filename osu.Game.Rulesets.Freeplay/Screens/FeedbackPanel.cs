// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Screens.Controls;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// "How was that track?": a rating and a few marks, saved with the track so its map can be looked at later.
    /// </summary>
    public partial class FeedbackPanel : FillFlowContainer
    {
        private readonly FeedbackEntry entry;
        private readonly Action changed;

        private readonly List<(FeedbackRating rating, Chip chip)> ratings = new List<(FeedbackRating, Chip)>();

        /// <param name="entry">The track's feedback, changed in place.</param>
        /// <param name="changed">Called after each change, to save it.</param>
        public FeedbackPanel(FeedbackEntry entry, Action changed)
        {
            this.entry = entry;
            this.changed = changed;

            AutoSizeAxes = Axes.Both;
            Anchor = Anchor.TopCentre;
            Origin = Anchor.TopCentre;
            Direction = FillDirection.Vertical;
            Spacing = new Vector2(0, 8);
            Margin = new MarginPadding { Vertical = 10 };

            var ratingRow = row();
            var flagRow = row();

            foreach (var rating in Enum.GetValues<FeedbackRating>())
            {
                var r = rating;
                var chip = new Chip(r.GetDescription(), entry.Rating == r, () => setRating(r));
                ratings.Add((r, chip));
                ratingRow.Add(chip);
            }

            foreach (var flag in Enum.GetValues<FeedbackFlag>())
            {
                var f = flag;
                Chip chip = null!;
                chip = new Chip(f.GetDescription(), entry.Flags.Contains(f), () =>
                {
                    if (!entry.Flags.Remove(f))
                        entry.Flags.Add(f);

                    chip.Selected = entry.Flags.Contains(f);
                    changed();
                });
                flagRow.Add(chip);
            }

            Children = new Drawable[]
            {
                FreeplayStyle.Label("Как трек? Отметки сохраняются вместе с картой.", 16).With(t =>
                {
                    t.Anchor = Anchor.TopCentre;
                    t.Origin = Anchor.TopCentre;
                    t.Colour = FreeplayColours.TEXT_DIM;
                }),
                ratingRow,
                flagRow,
            };
        }

        private void setRating(FeedbackRating rating)
        {
            entry.Rating = entry.Rating == rating ? null : rating;

            foreach (var (r, chip) in ratings)
                chip.Selected = entry.Rating == r;

            changed();
        }

        private static FillFlowContainer row() => new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(8, 0),
        };

        public partial class Chip : ClickableContainer
        {
            public readonly string Text;

            private readonly Box background;
            private readonly OsuSpriteText label;

            private bool selected;

            public bool Selected
            {
                get => selected;
                set
                {
                    selected = value;

                    Color4 back = value ? FreeplayColours.PINK : FreeplayStyle.PANEL_HOVER;
                    Color4 text = value ? Color4.White : FreeplayColours.TEXT_DIM;

                    background.FadeColour(back, IsLoaded ? 150 : 0);
                    label.FadeColour(text, IsLoaded ? 150 : 0);
                }
            }

            public Chip(string text, bool selected, Action action)
            {
                Text = text;
                Action = action;
                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = FreeplayStyle.CORNER_RADIUS + 2;

                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both },
                    label = new OsuSpriteText
                    {
                        Text = text,
                        Font = OsuFont.GetFont(size: 17, weight: FontWeight.SemiBold),
                        Margin = new MarginPadding { Horizontal = 14, Vertical = 7 },
                    },
                };

                Selected = selected;
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!selected)
                    background.FadeColour(FreeplayStyle.TRACK, 100);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (!selected)
                    background.FadeColour(FreeplayStyle.PANEL_HOVER, 200);
                base.OnHoverLost(e);
            }
        }
    }
}
