// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Rulesets.Freeplay.Beatmaps;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Ranking;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// Plays a single track of a run. Doesn't show results or save scores; instead reports back to the <see cref="FreeplayRunScreen"/>.
    /// </summary>
    public partial class FreeplayPlayer : Player
    {
        /// <summary>
        /// Accuracy isn't meaningful until a few objects have been judged.
        /// </summary>
        private const int min_judged_for_accuracy_fail = 20;

        private const double completion_delay = 1000;

        // Every track is freshly generated, so there are never other scores to compare against.
        [Cached(typeof(IGameplayLeaderboardProvider))]
        private readonly EmptyGameplayLeaderboardProvider leaderboardProvider = new EmptyGameplayLeaderboardProvider();

        private readonly FreeplayRunContext context;

        private TrackOutcome outcome = TrackOutcome.Quit;
        private bool reported;
        private bool restarting;

        private int judged;
        private int misses;
        private double firstObjectTime;

        private FreeplayBreakSkip? breakSkip;
        private double nextHealthSample;

        public FreeplayPlayer(FreeplayRunContext context)
            : base(new PlayerConfiguration
            {
                ShowResults = false,
            })
        {
            this.context = context;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // osu!'s "hold for menu" button and break panel aren't in osu!stable. The button stays (transparent), as it also handles Esc.
            HUDOverlay.HoldToQuit.Colour = osuTK.Graphics.Color4.Transparent;
            BreakOverlay.Colour = osuTK.Graphics.Color4.Transparent;

            // Over the playfield rather than in the HUD, so it stays visible with the interface hidden.
            if (LoadedBeatmapSuccessfully && (FreeplaySettings.Current.Timing.ShowHitErrorBar || FreeplaySettings.Current.Timing.ShowUnstableRate))
                DrawableRuleset.Overlays.Add(new FreeplayTimingOverlay(FreeplaySettings.Current.Timing));

            // Retrying restarts the whole run (on the same track, with a fresh pattern), rather than letting a failed track be replayed.
            var prepareLoader = PrepareLoaderForRestart;

            PrepareLoaderForRestart = quickRestart =>
            {
                restarting = true;
                prepareLoader?.Invoke(quickRestart);
            };

            if (!LoadedBeatmapSuccessfully)
                return;

            firstObjectTime = GameplayState.Beatmap.HitObjects.First().StartTime;

            // Breaks are skipped like the intro, with the skip button or Space.
            AddInternal(breakSkip = new FreeplayBreakSkip(GameplayState.Beatmap.Breaks, GameplayState.Beatmap.HitObjects.OfType<Osu.Objects.OsuHitObject>().OrderBy(h => h.StartTime).ToList(),
                () => GameplayClockContainer.CurrentTime, Seek));
            nextHealthSample = firstObjectTime;

            if (context.Fail.CarryHealth)
                HealthProcessor.Health.Value = context.StartingHealth;

            HealthProcessor.FailConditions += meetsFailCondition;

            ScoreProcessor.HasCompleted.BindValueChanged(completed =>
            {
                if (completed.NewValue && !GameplayState.HasFailed)
                    Scheduler.AddDelayed(() => report(TrackOutcome.Completed), completion_delay);
            });
        }

        protected override void Update()
        {
            base.Update();

            if (!LoadedBeatmapSuccessfully || reported || GameplayState.HasFailed)
                return;

            double time = GameplayClockContainer.CurrentTime;

            while (time >= nextHealthSample)
            {
                context.Run.HealthGraph.Add((float)HealthProcessor.Health.Value);
                nextHealthSample += 1000;
            }

            if (context.TimeLimitMs > 0 && context.ElapsedBeforeMs + playedMs() >= context.TimeLimitMs)
                report(TrackOutcome.TimeLimit);
        }

        private bool meetsFailCondition(HealthProcessor healthProcessor, JudgementResult result)
        {
            if (result.Type.AffectsAccuracy() && result.Type.IsScorable() && !result.Type.IsBonus())
                judged++;

            if (result.Type == HitResult.Miss)
                misses++;

            if (context.Fail.MissLimit > 0 && context.Run.Misses + misses >= context.Fail.MissLimit)
                return true;

            if (context.Fail.MinAccuracy > 0 && context.Run.Judged + judged >= min_judged_for_accuracy_fail && ScoreProcessor.Accuracy.Value < context.Fail.MinAccuracy)
                return true;

            return false;
        }

        protected override void PerformFail()
        {
            base.PerformFail();

            // Let the fail animation play out, then hand back to the run instead of showing the retry overlay.
            Scheduler.AddDelayed(() => report(TrackOutcome.Failed), 1500);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            if (restarting && !reported)
            {
                // The loader will create a new player, which picks up the fresh run.
                reported = true;
                context.RestartRun();
            }

            // Covers quitting from the pause menu, and failing to load the beatmap.
            if (!reported)
                report(LoadedBeatmapSuccessfully ? TrackOutcome.Quit : TrackOutcome.LoadFailed, exit: false);

            return base.OnExiting(e);
        }

        private void report(TrackOutcome trackOutcome, bool exit = true)
        {
            if (reported)
                return;

            reported = true;
            outcome = trackOutcome;

            Logger.Log($"Freeplay track ended: {outcome} (current screen: {this.IsCurrentScreen()}, exit: {exit})");

            context.Report(createRecord(), outcome, HealthProcessor?.Health.Value ?? 0);

            if (exit && this.IsCurrentScreen())
                context.ReturnToRun();
        }

        private TrackRecord? createRecord()
        {
            if (!LoadedBeatmapSuccessfully)
                return null;

            ScoreProcessor.PopulateScore(Score.ScoreInfo);

            var statistics = Score.ScoreInfo.Statistics;
            var metadata = Beatmap.Value.BeatmapInfo.Metadata;

            return new TrackRecord
            {
                Artist = metadata.ArtistUnicode.Length > 0 ? metadata.ArtistUnicode : metadata.Artist,
                Title = metadata.TitleUnicode.Length > 0 ? metadata.TitleUnicode : metadata.Title,
                Difficulty = Beatmap.Value.BeatmapInfo.DifficultyName,
                Seed = (GameplayState.Beatmap as FreeplayBeatmap)?.Seed ?? 0,
                StarRating = playedStarRating(),
                Bpm = Beatmap.Value.BeatmapInfo.BPM,
                UnstableRate = ScoreProcessor.HitEvents.CalculateUnstableRate()?.Result,
                PlayedMs = playedMs(),
                Completed = outcome == TrackOutcome.Completed,
                Great = statistics.GetValueOrDefault(HitResult.Great),
                Ok = statistics.GetValueOrDefault(HitResult.Ok),
                Meh = statistics.GetValueOrDefault(HitResult.Meh),
                Misses = statistics.GetValueOrDefault(HitResult.Miss),
                MaxCombo = ScoreProcessor.HighestCombo.Value,
                Score = ScoreProcessor.TotalScore.Value,
                Generated = GameplayState.Beatmap,
                // Only the stars mode shows it.
                Aim = context.Run.Style == PatternStyle.Metronome ? AimAnalysis.Analyse(GameplayState.Beatmap.HitObjects, ScoreProcessor.HitEvents, Score.Replay.Frames) : null,
            };
        }

        /// <summary>
        /// The star rating of what was played: the generated map's, or with Hard Rock, the harder map it made.
        /// </summary>
        private double playedStarRating()
        {
            if (GameplayState.Beatmap is not FreeplayBeatmap map)
                return 0;

            if (!GameplayState.Mods.Any(m => m is Osu.Mods.OsuModHardRock))
                return map.StarRating;

            return FreeplayBeatmapConverter.CalculateStarRating(map, map.HitObjects.OfType<Osu.Objects.OsuHitObject>().ToList(), map.Difficulty);
        }

        // (Skipped breaks weren't played.)
        private double playedMs() => Math.Max(0, GameplayClockContainer.CurrentTime - firstObjectTime - (breakSkip?.SkippedMs ?? 0));

        protected override SkipOverlay CreateSkipOverlay(double startTime) => new FreeplaySkipOverlay(startTime);

        protected override ResultsScreen CreateResults(ScoreInfo score) => throw new InvalidOperationException("Freeplay doesn't show results per track.");
    }

    public enum TrackOutcome
    {
        Completed,
        Failed,
        Quit,
        TimeLimit,
        LoadFailed,
    }

    /// <summary>
    /// What a <see cref="FreeplayPlayer"/> needs to know about the run it's part of.
    /// </summary>
    public class FreeplayRunContext
    {
        public required RunRecord Run { get; init; }

        public required FailSettings Fail { get; init; }

        public double StartingHealth { get; init; } = 1;

        public double ElapsedBeforeMs { get; init; }

        public double TimeLimitMs { get; init; }

        /// <summary>
        /// Called once per track with its record (null if it never loaded), how it ended, and the final health.
        /// </summary>
        public required Action<TrackRecord?, TrackOutcome, double> Report { get; init; }

        /// <summary>
        /// Leaves gameplay and returns to the run screen.
        /// </summary>
        public required Action ReturnToRun { get; init; }

        /// <summary>
        /// Discards the current run and starts a new one, as the player has requested a retry.
        /// </summary>
        public required Action RestartRun { get; init; }
    }
}
