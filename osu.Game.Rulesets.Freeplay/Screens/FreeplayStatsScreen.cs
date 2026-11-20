// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Screens.Controls;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// Totals, records, a breakdown by mode and recent history across all freeplay runs.
    /// </summary>
    public partial class FreeplayStatsScreen : FreeplayScreen
    {
        public override string Title => "Статистика freeplay";

        private const int recent_runs_shown = 50;

        private const float page_width = 900;

        private readonly FreeplayStorage freeplayStorage;

        public FreeplayStatsScreen(FreeplayStorage freeplayStorage)
        {
            this.freeplayStorage = freeplayStorage;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var history = freeplayStorage.LoadHistory();
            var flow = Column(14);

            Content.Add(new Controls.FreeplayScrollContainer
            {
                RelativeSizeAxes = Axes.Y,
                Width = page_width,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Child = flow.With(f => f.Padding = new MarginPadding { Bottom = 40, Right = 12 }),
            });

            flow.Add(Heading("Статистика", 52));

            flow.Add(createFeedbackPanel());

            if (history.Count == 0)
            {
                flow.Add(Text("Пока ни одного забега. Нажми «Играть» в меню.", 20).With(t => t.Colour = FreeplayColours.TEXT_DIM));
                return;
            }

            int judged = history.Sum(r => r.Judged);
            double accuracy = judged == 0 ? 1 : history.Sum(r => r.Accuracy * r.Judged) / judged;
            double? unstableRate = weightedUnstableRate(history);

            flow.Add(new StatTiles(new Drawable[]
            {
                new StatTile("забегов", $"{history.Count}", FreeplayColours.PINK),
                new StatTile("всего в игре", FormatDuration(history.Sum(r => r.SurvivedMs)), FreeplayColours.BLUE),
                new StatTile("треков пройдено", $"{history.Sum(r => r.TracksCompleted)}"),
                new StatTile("лучшее комбо", $"{history.Max(r => r.MaxCombo)}x"),
                new StatTile("средняя точность", $"{accuracy:P2}"),
                new StatTile("средний UR", unstableRate is double ur ? $"{ur:0.0}" : "—"),
            }));

            // The stars mode's aim analysis, over its latest runs.
            var (aim, analysedRuns) = AimAnalysis.Latest(history);

            if (aim != null)
                flow.Add(new AimAnalysisPanel($"Разбор миссов «Звёздочки» (последние забеги: {analysedRuns})", aim));

            var records = new TitledPanel("Рекорды");

            var longest = history.MaxBy(r => r.SurvivedMs)!;
            var mostTracks = history.MaxBy(r => r.TracksCompleted)!;
            var mostAccurate = history.Where(r => r.Judged >= 100).MaxBy(r => r.Accuracy);
            var steadiest = history.Where(r => r.UnstableRate != null).MinBy(r => r.UnstableRate);

            records.Body.Add(record("Самый долгий забег", FormatDuration(longest.SurvivedMs), longest));
            records.Body.Add(record("Больше всего треков", $"{mostTracks.TracksCompleted}", mostTracks));

            if (mostAccurate != null)
                records.Body.Add(record("Самая высокая точность (от 100 нот)", $"{mostAccurate.Accuracy:P2}", mostAccurate));

            if (steadiest != null)
                records.Body.Add(record("Самый ровный UR", $"{steadiest.UnstableRate:0.0}", steadiest));

            flow.Add(records);

            var modes = new TitledPanel("По режимам");

            foreach (var group in history.GroupBy(r => r.StyleName).OrderByDescending(g => g.Sum(r => r.SurvivedMs)))
            {
                var runs = group.ToList();
                int modeJudged = runs.Sum(r => r.Judged);
                double modeAccuracy = modeJudged == 0 ? 1 : runs.Sum(r => r.Accuracy * r.Judged) / modeJudged;

                modes.Body.Add(new ListRow(
                    group.Key,
                    $"{runs.Count} забегов · всего {FormatDuration(runs.Sum(r => r.SurvivedMs))} · треков {runs.Sum(r => r.TracksCompleted)} · точность {modeAccuracy:P2}"
                    + (weightedUnstableRate(runs) is double modeUr ? $" · UR {modeUr:0.0}" : string.Empty),
                    $"лучший {FormatDuration(runs.Max(r => r.SurvivedMs))}",
                    FreeplayColours.PINK));
            }

            flow.Add(modes);

            var recent = new TitledPanel("Последние забеги");

            foreach (var run in history.AsEnumerable().Reverse().Take(recent_runs_shown))
            {
                recent.Body.Add(new ListRow(
                    $"{run.Date.LocalDateTime:dd.MM HH:mm} · {run.StyleName} · {run.StarRating:0.0}★" + (run.Mods != null ? $" · {run.Mods}" : string.Empty),
                    $"{FreeplayRunScreen.DescribeEnd(run.EndReason)} · {run.TracksCompleted} тр. · {run.Accuracy:P2} · {run.MaxCombo}x"
                    + (run.UnstableRate is double runUr ? $" · UR {runUr:0.0}" : string.Empty),
                    FormatDuration(run.SurvivedMs),
                    run.EndReason == RunEndReason.Failed ? FreeplayColours.DEEP_PINK : FreeplayColours.BLUE));
            }

            flow.Add(recent);
        }

        [Resolved]
        private GameHost host { get; set; } = null!;

        /// <summary>
        /// The marks left on tracks, and a button packing them up to send.
        /// </summary>
        private Drawable createFeedbackPanel()
        {
            var panel = new TitledPanel("Фидбек по картам");
            int count = freeplayStorage.CountFeedback();

            var status = Text(count == 0
                ? "Пока пусто. Отмечай треки между ними («Скучный ритм», «Кайф»...), и карты сохранятся сюда."
                : $"Отмечено треков: {count}. Экспорт собирает их в один zip (карты, настройки, отметки, без музыки) — его можно скинуть.", 17);

            panel.Body.Add(status.With(t => t.Colour = FreeplayColours.TEXT_DIM));

            if (count > 0)
            {
                panel.Body.Add(Button("Экспорт фидбека", () =>
                {
                    string? zip = freeplayStorage.ExportFeedback();

                    if (zip == null)
                        return;

                    status.Text = $"Готово: {System.IO.Path.GetFileName(zip)} (папка открыта).";
                    host.PresentFileExternally(zip);
                }, 320).With(b => b.Margin = new MarginPadding { Top = 8 }));
            }

            return panel;
        }

        private static Drawable record(string label, string value, RunRecord run)
            => new ListRow(label, $"{run.Date.LocalDateTime:dd.MM.yyyy} · {run.StyleName} · {run.StarRating:0.0}★", value, FreeplayColours.BLUE);

        private static double? weightedUnstableRate(IReadOnlyCollection<RunRecord> runs)
        {
            var tracks = runs.SelectMany(r => r.Tracks).Where(t => t.UnstableRate != null && t.Great + t.Ok + t.Meh > 0).ToList();

            if (tracks.Count == 0)
                return null;

            return tracks.Sum(t => t.UnstableRate!.Value * (t.Great + t.Ok + t.Meh)) / tracks.Sum(t => t.Great + t.Ok + t.Meh);
        }
    }
}
