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
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Freeplay.Stable;
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
        private readonly StableLibrary library;
        private readonly FreeplayMode mode;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        private FreeplaySettings settings = null!;
        private FreeplayGenerationSettings generation = null!;
        private TrackPicker picker = null!;

        private RunRecord run = null!;
        private double health;
        private int consecutiveLoadFailures;

        private TrackOutcome? pendingOutcome;
        private FreeplayPlayer? currentPlayer;
        private bool ended;

        private FillFlowContainer flow = null!;

        /// <param name="freeplayStorage">Where the run is recorded.</param>
        /// <param name="library">Where tracks are picked from.</param>
        /// <param name="mode">The mode to play, with its saved settings.</param>
        public FreeplayRunScreen(FreeplayStorage freeplayStorage, StableLibrary library, FreeplayMode mode)
        {
            this.freeplayStorage = freeplayStorage;
            this.library = library;
            this.mode = mode;
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

        public override bool OnExiting(ScreenExitEvent e)
        {
            // Silence the last track, so the menu's own music plays.
            Beatmap.Value.Track.Stop();
            Beatmap.SetDefault();

            return base.OnExiting(e);
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
            // A copy, so a calibration or anything else changed during the run never leaks into the saved settings.
            generation = settings.GetMode(mode).Clone();

            // Read by the beatmap converter when each track is generated.
            FreeplayGenerationSettings.Current = generation;
            MinedPatternSet.Current = library.Patterns;
            picker = new TrackPicker(library, settings.Tracks, generation);

            resetRun();

            if (picker.CountAvailable() == 0)
            {
                showMessage("Нет подходящих треков", !library.Exists
                    ? $"Не найдена папка osu!stable: {library.StablePath}. Укажите её в настройках."
                    : settings.Tracks.Source == TrackSource.Collection
                        ? "Выберите коллекцию в настройках или ослабьте фильтры."
                        : "Подождите, пока библиотека просканируется, или ослабьте фильтры в настройках.");
                addMenuButtons(allowRetry: false);
                ended = true;
                return;
            }

            playNextTrack();
        }

        private void resetRun()
        {
            run = new RunRecord
            {
                StarRating = generation.StarRating,
                Style = generation.Style,
                Preset = mode.Name,
            };

            health = 1;
            consecutiveLoadFailures = 0;
            pendingOutcome = null;
            ended = false;
        }

        private void playNextTrack()
        {
            if (!this.IsCurrentScreen() || ended)
                return;

            // A remix plays the mapper's own difficulty, so pick the one closest to the requested star rating.
            // With ranges on, each track gets its own values. Read by the beatmap converter when the track is generated.
            var track = generation.Roll(rollRng);
            FreeplayGenerationSettings.Current = track;

            bool remix = track.RhythmSource == RhythmSource.Remix;
            var next = picker.Next(closestToStars: remix ? track.StarRating : null, generatedStars: remix ? null : track.StarRating);

            if (next == null)
            {
                endRun(RunEndReason.OutOfTracks);
                return;
            }

            var (set, difficulty) = next.Value;

            var ruleset = rulesets.GetRuleset(FreeplayRuleset.SHORT_NAME);

            if (ruleset == null)
                throw new InvalidOperationException("The freeplay ruleset isn't available.");

            Ruleset.Value = ruleset;
            Mods.Value = Array.Empty<Mod>();
            Beatmap.Value = library.GetWorkingBeatmap(set, difficulty);

            showMessage($"Трек {run.Tracks.Count + 1}", $"{difficulty.Artist} — {difficulty.Title} [{difficulty.Version}] · {difficulty.Bpm:0} BPM");

            // The context is created per player, so that a retry (which creates a new player from the same loader) picks up the reset run.
            this.Push(new FreeplayPlayerLoader(() => currentPlayer = new FreeplayPlayer(createContext()), difficulty.Bpm,
                remix ? $"{mode.Name} · сложность маппера ближе всего к {track.StarRating:0.0}★" : $"{mode.Name} · {describeTrack(track)}"));
        }

        private readonly Random rollRng = new Random();

        /// <summary>
        /// The track's star rating, and with ranges on, the other values it rolled.
        /// </summary>
        private string describeTrack(FreeplayGenerationSettings track)
        {
            string text = $"{track.StarRating:0.0}★";

            if (!track.UseRanges)
                return text;

            if (mode.Has(ModeOptions.Density))
                text += $" · плотность {track.Density:P0}";

            if (mode.Has(ModeOptions.JumpSpacing))
                text += $" · прыжки {track.JumpSpacing:P0}";

            if (mode.Has(ModeOptions.Iq))
                text += $" · IQ {track.PatternIq * 100:0}";

            return text;
        }

        private FreeplayRunContext createContext() => new FreeplayRunContext
        {
            Run = run,
            Fail = settings.Fail,
            StartingHealth = health,
            ElapsedBeforeMs = run.SurvivedMs,
            TimeLimitMs = settings.Fail.TimeLimitMinutes * 60_000,
            Report = onTrackReported,
            // Called on the player's update. This screen is hidden while suspended, so its own scheduler wouldn't run.
            ReturnToRun = () => this.MakeCurrent(),
            RestartRun = resetRun,
        };

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
            flow.Add(Heading(heading, 44));
            flow.Add(Text(detail, 22));
        }

        private void showSummary()
        {
            showMessage(DescribeEnd(run.EndReason), $"{run.StarRating:0.0}★ · {run.StyleName}");

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
                    flow.Add(Text($"{track.StarRating:0.00}★ · {track.Bpm:0} BPM · {(track.Completed ? "пройден" : "не пройден")} · {FormatDuration(track.PlayedMs)} · {track.MaxCombo}x · промахов: {track.Misses} · seed {track.Seed}", 14)
                        .With(t => t.Colour = FreeplayColours.TEXT_DIM));
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
