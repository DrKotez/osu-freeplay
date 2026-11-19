// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Freeplay.Configuration;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Freeplay.Screens.Controls
{
    /// <summary>
    /// The aim analysis of a run or several: why notes were missed, in which quarter of the playfield and on which jumps,
    /// and a picture of where the hits landed on the circle.
    /// </summary>
    public partial class AimAnalysisPanel : TitledPanel
    {
        private const float scatter_size = 156;
        private const int max_dots = 800;

        public AimAnalysisPanel(string title, AimAnalysis analysis)
            : base(title)
        {
            int misses = analysis.AimMisses + analysis.TimingMisses + analysis.NoClickMisses;

            if (analysis.Notes == 0)
            {
                Body.Add(text("Пока нечего разбирать.", 18, FreeplayColours.TEXT_DIM));
                return;
            }

            Body.Add(text(misses == 0
                ? $"Ни одного мисса на {analysis.Notes} нотах."
                : $"Миссов {misses}: мимо {analysis.AimMisses} · по тайму {analysis.TimingMisses} · не нажал {analysis.NoClickMisses}", 20, FreeplayColours.WHITE, FontWeight.SemiBold));

            if (analysis.AimMisses > 0)
            {
                Body.Add(text($"Мимо: недовёл {share(analysis.Undershoot, analysis.AimMisses)} · перевёл {share(analysis.Overshoot, analysis.AimMisses)} · "
                              + $"мисрид {share(analysis.Misreads, analysis.AimMisses)}"
                              + (analysis.AimMisses - analysis.Undershoot - analysis.Overshoot - analysis.Misreads is int other and > 0 ? $" · без направления {other}" : string.Empty),
                    18, FreeplayColours.TEXT_DIM));
            }

            if (analysis.Hits > 0)
            {
                Body.Add(text($"Попадания: в центр {share(analysis.CentreHits, analysis.Hits)} · между {share(analysis.MiddleHits, analysis.Hits)} · по краю {share(analysis.EdgeHits, analysis.Hits)}",
                    18, FreeplayColours.TEXT_DIM));
            }

            var weakest = analysis.AimMisses > 0 ? Enum.GetValues<PlayfieldQuarter>().MaxBy(analysis.QuarterMissRate) : (PlayfieldQuarter?)null;

            Body.Add(new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(24, 0),
                Margin = new MarginPadding { Vertical = 10 },
                Children = new Drawable[]
                {
                    labelled("Мимо по четвертям поля", quarters(analysis, weakest)),
                    labelled("Мимо по направлению прыжка", directions(analysis)),
                    labelled("Куда попадаешь", scatter(analysis)),
                }
            });

            var found = analysis.WeakestQuarter();

            Body.Add(text(found is PlayfieldQuarter quarter
                ? $"Слабее всего {QuarterName(quarter)}. В настройках «Звёздочки» можно чаще ставить туда ноты («Слабая зона»)."
                : $"Слабую зону видно от 30 миссов мимо (пока {analysis.AimMisses}).", 16, FreeplayColours.TEXT_DIM));
        }

        public static string QuarterName(PlayfieldQuarter quarter) => quarter switch
        {
            PlayfieldQuarter.TopLeft => "левый верх",
            PlayfieldQuarter.TopRight => "правый верх",
            PlayfieldQuarter.BottomLeft => "левый низ",
            _ => "правый низ",
        };

        private static string share(int part, int whole) => whole == 0 ? "—" : $"{(double)part / whole:P0}";

        private static Drawable quarters(AimAnalysis analysis, PlayfieldQuarter? weakest)
        {
            var grid = new FillFlowContainer
            {
                Size = new Vector2(2 * 118 + 6, 2 * 62 + 6),
                Direction = FillDirection.Full,
                Spacing = new Vector2(6),
            };

            foreach (var quarter in Enum.GetValues<PlayfieldQuarter>())
                grid.Add(cell($"{analysis.QuarterMissRate(quarter):P1}", $"{analysis.QuarterMisses[(int)quarter]} из {analysis.QuarterNotes[(int)quarter]}", quarter == weakest, 118));

            return grid;
        }

        private static Drawable directions(AimAnalysis analysis)
        {
            // Laid out like arrows: up on top, left and right at the sides, down at the bottom.
            Drawable at(JumpDirection direction, string arrow, Vector2 position) => cell($"{arrow} {analysis.DirectionMissRate(direction):P1}",
                $"{analysis.DirectionMisses[(int)direction]} из {analysis.DirectionNotes[(int)direction]}", false, 96, 54).With(c => c.Position = position);

            return new Container
            {
                Size = new Vector2(3 * 96 + 8, 3 * 54 + 8),
                Children = new[]
                {
                    at(JumpDirection.Up, "↑", new Vector2(100, 0)),
                    at(JumpDirection.Left, "←", new Vector2(0, 58)),
                    at(JumpDirection.Right, "→", new Vector2(200, 58)),
                    at(JumpDirection.Down, "↓", new Vector2(100, 116)),
                }
            };
        }

        private static Drawable scatter(AimAnalysis analysis)
        {
            var dots = new Container { RelativeSizeAxes = Axes.Both };
            int pairs = analysis.Scatter.Count / 2;

            // The latest ones, when there are a lot.
            for (int i = Math.Max(0, pairs - max_dots); i < pairs; i++)
            {
                var offset = new Vector2(analysis.Scatter[i * 2], analysis.Scatter[i * 2 + 1]);

                if (offset.Length > 1.15f)
                    offset = offset.Normalized() * 1.15f;

                dots.Add(new Circle
                {
                    Size = new Vector2(4),
                    Origin = Anchor.Centre,
                    Position = new Vector2(scatter_size / 2) + offset * (scatter_size / 2 - 6),
                    Colour = FreeplayColours.BLUE,
                    Alpha = 0.55f,
                });
            }

            Drawable ring(float share, float alpha) => new CircularContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2((scatter_size - 12) * share),
                Masking = true,
                BorderThickness = 2,
                BorderColour = FreeplayColours.PINK.Opacity(alpha),
                Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
            };

            return new Container
            {
                Size = new Vector2(scatter_size),
                Children = new[]
                {
                    ring(1, 0.9f),
                    ring(AimAnalysis.EDGE, 0.35f),
                    ring(AimAnalysis.CENTRE, 0.35f),
                    dots,
                }
            };
        }

        private static Drawable cell(string value, string detail, bool highlight, float width, float height = 62) => new Container
        {
            Size = new Vector2(width, height),
            Masking = true,
            CornerRadius = FreeplayStyle.CORNER_RADIUS,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = highlight ? FreeplayColours.DEEP_PINK : Color4.Black, Alpha = highlight ? 0.8f : 0.35f },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Children = new Drawable[]
                    {
                        text(value, 20, FreeplayColours.WHITE, FontWeight.Bold).With(t =>
                        {
                            t.Anchor = Anchor.TopCentre;
                            t.Origin = Anchor.TopCentre;
                        }),
                        text(detail, 14, highlight ? FreeplayColours.WHITE : FreeplayColours.TEXT_DIM).With(t =>
                        {
                            t.Anchor = Anchor.TopCentre;
                            t.Origin = Anchor.TopCentre;
                        }),
                    }
                },
            }
        };

        private static Drawable labelled(string label, Drawable content) => new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 6),
            Children = new[] { text(label, 15, FreeplayColours.PINK, FontWeight.SemiBold), content },
        };

        private static OsuSpriteText text(string value, float size, Color4 colour, FontWeight weight = FontWeight.Regular) => new OsuSpriteText
        {
            Text = value,
            Font = OsuFont.GetFont(size: size, weight: weight),
            Colour = colour,
        };
    }
}
