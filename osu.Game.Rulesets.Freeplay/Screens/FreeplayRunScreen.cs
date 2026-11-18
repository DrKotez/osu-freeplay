// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// Runs a marathon: plays random tracks back to back until a fail condition is met, then shows a summary.
    /// </summary>
    public partial class FreeplayRunScreen : FreeplayScreen
    {
        public override string Title => "Забег freeplay";

        /// <summary>
        /// Give up after this many tracks in a row fail to load, rather than spinning forever.
        /// </summary>
        private const int max_consecutive_load_failures = 5;

        private const double delay_between_tracks = 1200;

        private readonly FreeplayStorage freeplayStorage;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        private FreeplaySettings settings = null!;
        private TrackPicker picker = null!;

        private RunRecord run = null!;
        private double health;
        private int consecutiveLoadFailures;

        private TrackOutcome? pendingOutcome;
        private FreeplayPlayer? currentPlayer;
        private bool ended;

        private FillFlowContainer flow = null!;

        public FreeplayRunScreen(FreeplayStorage freeplayStorage)
        {
            this.freeplayStorage = freeplayStorage;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Content.Add(new OsuScrollContainer
            {
                RelativeSizeAxes = Axes.Y,
                Width = CONTENT_WIDTH,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Child = flow = Column(8),
            });
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);
            startRun();
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            if (ended)
                return;

            // The player only reports once it's been shown. Without a report, either the beatmap couldn't be loaded,
            // or the loading screen was exited by the user.
            var outcome = pendingOutcome ?? (currentPlayer?.LoadState >= LoadState.Ready && !currentPlayer.LoadedBeatmapSuccessfully ? TrackOutcome.LoadFailed : TrackOutcome.Quit);

            pendingOutcome = null;
            currentPlayer = null;

            switch (outcome)
            {
                case TrackOutcome.Completed:
                    showMessage($"Трек {run.Tracks.Count} пройден", "Следующий трек...");
                    Scheduler.AddDelayed(playNextTrack, delay_between_tracks);
                    break;

                case TrackOutcome.LoadFailed:
                    if (++consecutiveLoadFailures >= max_consecutive_load_failures)
                        endRun(RunEndReason.OutOfTracks);
                    else
                        Scheduler.AddDelayed(playNextTrack, 200);
                    break;

                case TrackOutcome.Failed:
                    endRun(RunEndReason.Failed);
                    break;

                case TrackOutcome.TimeLimit:
                    endRun(RunEndReason.TimeLimit);
                    break;

                case TrackOutcome.Quit:
                    endRun(RunEndReason.Quit);
                    break;
            }
        }

        private void startRun()
        {
            settings = FreeplaySettings.Current;
            picker = new TrackPicker(realm, settings.Tracks);

            run = new RunRecord
            {
                StarRating = settings.Generation.StarRating,
                Style = settings.Generation.Style,
            };

            health = 1;
            consecutiveLoadFailures = 0;
            ended = false;

            if (picker.CountAvailable() == 0)
            {
                showMessage("Нет подходящих треков", settings.Tracks.Source == TrackSource.Collection
                    ? "Выберите коллекцию в настройках или ослабьте фильтры."
                    : "Импортируйте карты или ослабьте фильтры в настройках.");
                addMenuButtons(allowRetry: false);
                ended = true;
                return;
            }

            playNextTrack();
        }

        private void playNextTrack()
        {
            if (!this.IsCurrentScreen() || ended)
                return;

            var beatmapInfo = picker.Next();

            if (beatmapInfo == null)
            {
                endRun(RunEndReason.OutOfTracks);
                return;
            }

            var ruleset = rulesets.GetRuleset(FreeplayRuleset.SHORT_NAME);

            if (ruleset == null)
                throw new InvalidOperationException("The freeplay ruleset isn't available.");

            Ruleset.Value = ruleset;
            Mods.Value = Array.Empty<Mod>();
            // Refetch, as the same track may come up again (e.g. in a small collection) and a reused working beatmap may already have been consumed.
            Beatmap.Value = beatmaps.GetWorkingBeatmap(beatmapInfo, refetch: true);

            showMessage($"Трек {run.Tracks.Count + 1}", $"{beatmapInfo.Metadata.Artist} — {beatmapInfo.Metadata.Title}");

            var context = new FreeplayRunContext
            {
                Run = run,
                Fail = settings.Fail,
                StartingHealth = health,
                ElapsedBeforeMs = run.SurvivedMs,
                TimeLimitMs = settings.Fail.TimeLimitMinutes * 60_000,
                Report = onTrackReported,
                // Called on the player's update. This screen is hidden while suspended, so its own scheduler wouldn't run.
                ReturnToRun = () => this.MakeCurrent(),
            };

            this.Push(new PlayerLoader(() => currentPlayer = new FreeplayPlayer(context)));
        }

        private void onTrackReported(TrackRecord? record, TrackOutcome outcome, double finalHealth)
        {
            pendingOutcome = outcome;

            if (record == null)
                return;

            consecutiveLoadFailures = 0;
            run.Tracks.Add(record);

            health = Math.Min(1, finalHealth + settings.Fail.HealthRefill);
        }

        private void endRun(RunEndReason reason)
        {
            ended = true;
            run.EndReason = reason;

            if (run.Tracks.Count > 0)
                freeplayStorage.AddRun(run);

            showSummary();
        }

        #region Display

        private void showMessage(string heading, string detail)
        {
            flow.Clear();
            flow.Add(Text(heading, 40, FontWeight.Bold, Typeface.TorusAlternate).With(t => t.Colour = colours.Pink));
            flow.Add(Text(detail, 22));
        }

        private void showSummary()
        {
            showMessage(DescribeEnd(run.EndReason), $"{run.StarRating:0.0}★ · {run.Style.GetDescription()}");

            flow.AddRange(new[]
            {
                stat("Время в игре", FormatDuration(run.SurvivedMs)),
                stat("Треков пройдено", $"{run.TracksCompleted}"),
                stat("Точность", $"{run.Accuracy:P2}"),
                stat("Макс. комбо", $"{run.MaxCombo}x"),
                stat("300 / 100 / 50 / промахи", $"{run.Great} / {run.Ok} / {run.Meh} / {run.Misses}"),
            });

            if (run.HealthGraph.Count > 1)
            {
                flow.Add(Text("Здоровье по ходу забега", 20, FontWeight.Bold).With(t => t.Margin = new MarginPadding { Top = 16 }));
                flow.Add(new HealthGraph(run.HealthGraph, colours));
            }

            if (run.Tracks.Count > 0)
            {
                flow.Add(Text("Треки", 20, FontWeight.Bold).With(t => t.Margin = new MarginPadding { Top = 16 }));

                foreach (var (track, index) in run.Tracks.Select((t, i) => (t, i)))
                {
                    flow.Add(Text($"{index + 1}. {track.Artist} — {track.Title} [{track.Difficulty}]", 16, FontWeight.SemiBold));
                    flow.Add(Text($"{(track.Completed ? "пройден" : "не пройден")} · {FormatDuration(track.PlayedMs)} · {track.MaxCombo}x · промахов: {track.Misses} · seed {track.Seed}", 14)
                        .With(t => t.Colour = colours.Gray9));
                }
            }

            addMenuButtons(allowRetry: true);
        }

        private void addMenuButtons(bool allowRetry)
        {
            flow.Add(new Container { Height = 20 });

            if (allowRetry)
                flow.Add(Button("Новый забег", startRun));

            flow.Add(Button("В меню", () => this.Exit()));
        }

        private static Drawable stat(string label, string value) => new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children = new Drawable[]
            {
                Text(label, 20),
                Text(value, 20, FontWeight.Bold).With(t =>
                {
                    t.Anchor = Anchor.TopRight;
                    t.Origin = Anchor.TopRight;
                }),
            }
        };

        public static string DescribeEnd(RunEndReason reason)
        {
            switch (reason)
            {
                case RunEndReason.Failed:
                    return "Поражение";

                case RunEndReason.TimeLimit:
                    return "Время вышло";

                case RunEndReason.OutOfTracks:
                    return "Треки закончились";

                default:
                    return "Забег прерван";
            }
        }

        #endregion

        /// <summary>
        /// A bar per health sample, coloured from red (low) to green (high).
        /// </summary>
        private partial class HealthGraph : FillFlowContainer
        {
            private const int max_bars = 240;

            public HealthGraph(IReadOnlyList<float> samples, OsuColour colours)
            {
                RelativeSizeAxes = Axes.X;
                Height = 80;
                Direction = FillDirection.Horizontal;

                int bucketSize = Math.Max(1, (int)Math.Ceiling(samples.Count / (double)max_bars));
                var buckets = samples.Chunk(bucketSize).Select(c => c.Average()).ToList();

                foreach (float value in buckets)
                {
                    Add(new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Width = 1f / buckets.Count,
                        Padding = new MarginPadding { Horizontal = buckets.Count > 100 ? 0 : 1 },
                        Child = new Box
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            RelativeSizeAxes = Axes.Both,
                            Height = Math.Max(0.02f, value),
                            Colour = value < 0.3f ? colours.Red : value < 0.6f ? colours.Yellow : colours.Green,
                        }
                    });
                }
            }
        }
    }
}
