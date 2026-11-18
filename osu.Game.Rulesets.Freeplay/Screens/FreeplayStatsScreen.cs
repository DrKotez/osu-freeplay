// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.Freeplay.Configuration;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// Totals and recent history across all freeplay runs.
    /// </summary>
    public partial class FreeplayStatsScreen : FreeplayScreen
    {
        public override string Title => "Статистика freeplay";

        private const int recent_runs_shown = 50;

        private readonly FreeplayStorage freeplayStorage;

        public FreeplayStatsScreen(FreeplayStorage freeplayStorage)
        {
            this.freeplayStorage = freeplayStorage;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            var history = freeplayStorage.LoadHistory();
            var flow = Column(6);

            Content.Add(new OsuScrollContainer
            {
                RelativeSizeAxes = Axes.Y,
                Width = CONTENT_WIDTH,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Child = flow,
            });

            flow.Add(Heading("Статистика", 44));

            if (history.Count == 0)
            {
                flow.Add(Text("Пока ни одного забега. Нажмите «Играть» в меню freeplay.", 20));
                return;
            }

            int judged = history.Sum(r => r.Judged);
            double accuracy = judged == 0 ? 1 : history.Sum(r => r.Accuracy * r.Judged) / judged;

            var longest = history.MaxBy(r => r.SurvivedMs)!;
            var mostTracks = history.MaxBy(r => r.TracksCompleted)!;

            flow.AddRange(new[]
            {
                stat("Забегов", $"{history.Count}"),
                stat("Всего в игре", FormatDuration(history.Sum(r => r.SurvivedMs))),
                stat("Треков пройдено", $"{history.Sum(r => r.TracksCompleted)}"),
                stat("Самый долгий забег", $"{FormatDuration(longest.SurvivedMs)} ({longest.StarRating:0.0}★, {longest.StyleName})"),
                stat("Больше всего треков", $"{mostTracks.TracksCompleted} ({mostTracks.StarRating:0.0}★)"),
                stat("Лучшее комбо", $"{history.Max(r => r.MaxCombo)}x"),
                stat("Средняя точность", $"{accuracy:P2}"),
                stat("300 / 100 / 50 / промахи", $"{history.Sum(r => r.Great)} / {history.Sum(r => r.Ok)} / {history.Sum(r => r.Meh)} / {history.Sum(r => r.Misses)}"),
            });

            flow.Add(Text("Последние забеги", 28, FontWeight.Bold).With(t => t.Margin = new MarginPadding { Top = 24 }));

            foreach (var run in history.AsEnumerable().Reverse().Take(recent_runs_shown))
            {
                flow.Add(stat(
                    $"{run.Date.LocalDateTime:dd.MM HH:mm} · {FreeplayRunScreen.DescribeEnd(run.EndReason)}",
                    $"{FormatDuration(run.SurvivedMs)} · {run.TracksCompleted} тр. · {run.Accuracy:P2} · {run.MaxCombo}x · {run.StarRating:0.0}★ {run.StyleName}"));
            }
        }

        private static Drawable stat(string label, string value) => new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children = new Drawable[]
            {
                Text(label, 18),
                Text(value, 18, FontWeight.Bold).With(t =>
                {
                    t.Anchor = Anchor.TopRight;
                    t.Origin = Anchor.TopRight;
                }),
            }
        };
    }
}
