// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Extensions;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.Freeplay.Beatmaps;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
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

            // (No osu!lazer "назад" button.)
            FooterContent.Value = osu.Game.Screens.Footer.ScreenFooterContent.Legacy(showBackButton: false);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // osu!'s "hold for menu" button and break panel aren't in osu!stable. The button stays (transparent), as it also handles Esc.
            HUDOverlay.HoldToQuit.Colour = osuTK.Graphics.Color4.Transparent;
            BreakOverlay.Colour = osuTK.Graphics.Color4.Transparent;

            // FIX (no lazer UI): osu!lazer's mod icons, which pop up at the start of a track with mods on.
            HUDOverlay.ModDisplay.Colour = osuTK.Graphics.Color4.Transparent;

            // FIX (no lazer UI): the pause menu is freeplay~'s own; osu!lazer's stays underneath, unseen, for its keys (Esc, arrows, Enter).
            PauseOverlay.Colour = osuTK.Graphics.Color4.Transparent;
            AddInternal(new FreeplayPauseMenu(PauseOverlay));

            // FIX (no lazer UI): osu!'s "hold for menu" is unseen, so holding Esc shows freeplay~'s own progress.
            if (HUDOverlay.HoldToQuit.ChildrenOfType<HoldToConfirmContainer>().FirstOrDefault() is HoldToConfirmContainer hold)
                AddInternal(new FreeplayHoldIndicator(hold));

            // Over the playfield rather than in the HUD, so it stays visible with the interface hidden.
            if (LoadedBeatmapSuccessfully && (FreeplaySettings.Current.Timing.ShowHitErrorBar || FreeplaySettings.Current.Timing.ShowUnstableRate))
                DrawableRuleset.Overlays.Add(new FreeplayTimingOverlay(FreeplaySettings.Current.Timing));

            // The pause menu: the track again (the same map, the run starting over), or a whole new run.
            if (PauseOverlay.Buttons.FirstOrDefault(b => b.Text.ToString() == "Заново") is DialogButton retry)
            {
                retry.Text = "Трек заново";

                PauseOverlay.AddButton("Новый забег", new osuTK.Graphics.Color4(245, 169, 184, 255), () =>
                {
                    context.NewRun();
                    PerformExit();
                });

                // Before "Выйти".
                var flow = (FillFlowContainer<DialogButton>)retry.Parent!;
                string[] order = { "Продолжить", "Трек заново", "Новый забег", "Выйти" };

                foreach (var button in PauseOverlay.Buttons)
                    flow.SetLayoutPosition(button, Array.IndexOf(order, button.Text.ToString()));
            }

            // Retrying restarts the whole run (on the same track and map), rather than letting a failed track be replayed.
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
                () => GameplayClockContainer.CurrentTime, JumpTo)
            {
                IsPaused = () => GameplayClockContainer.IsPaused.Value,
            });
            nextHealthSample = firstObjectTime;

            // OPTIMISATION: with Hard Rock or another speed, the stars are worked out again in full; done now, in the background, rather than
            // on the update thread the moment the track ends (where it stuttered the fail animation).
            playedStars = Task.Run(playedStarRating);

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
                context.RestartRun(LoadedBeatmapSuccessfully ? (GameplayState.Beatmap as FreeplayBeatmap)?.Seed : null);
            }

            // Covers quitting from the pause menu, and failing to load the beatmap.
            // FIX: leaving during the fail animation (before the delayed report) is still a fail, not a quit.
            if (!reported)
                report(!LoadedBeatmapSuccessfully ? TrackOutcome.LoadFailed : GameplayState.HasFailed ? TrackOutcome.Failed : TrackOutcome.Quit, exit: false);

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
                StarRating = playedStars?.IsCompletedSuccessfully == true ? playedStars.GetResultSafely() : playedStarRating(),
                Bpm = Beatmap.Value.BeatmapInfo.BPM * speed,
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
                // FIX: with Relax osu! clicks for the player, so there are no presses to analyse (every miss looked "not clicked").
                Aim = context.Run.Style == PatternStyle.Metronome && !GameplayState.Mods.Any(m => m is Osu.Mods.OsuModRelax)
                    ? AimAnalysis.Analyse(GameplayState.Beatmap.HitObjects, ScoreProcessor.HitEvents, Score.Replay.Frames, singleTap: GameplayState.Mods.Any(m => m is Osu.Mods.OsuModSingleTap))
                    : null,
            };
        }

        /// <summary>
        /// The star rating of what was played: the generated map's, or with Hard Rock, the harder map it made.
        /// </summary>
        private Task<double>? playedStars;

        private double playedStarRating()
        {
            if (GameplayState.Beatmap is not FreeplayBeatmap map)
                return 0;

            // Its stars were worked out without Hard Rock, and at the speed it was generated for; played otherwise, they're worked out again.
            if (!GameplayState.Mods.Any(m => m is Osu.Mods.OsuModHardRock) && Math.Abs(speed - map.Settings.GeneratedForSpeed) < 0.001)
                return map.StarRating;

            return FreeplayBeatmapConverter.CalculateStarRating(map, map.HitObjects.OfType<Osu.Objects.OsuHitObject>().ToList(), map.Difficulty, speed);
        }

        // (Skipped breaks weren't played.)
        private double playedMs() => Math.Max(0, GameplayClockContainer.CurrentTime - firstObjectTime - (breakSkip?.SkippedMs ?? 0)) / speed;

        /// <summary>
        /// How fast the track plays (Double Time's speed, or 1): the time played counts in real time.
        /// </summary>
        private double speed => GameplayState?.Mods.OfType<ModRateAdjust>().FirstOrDefault()?.SpeedChange.Value ?? 1;

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
        /// Discards the current run and starts a new one on the same track, as the player has requested a retry: with the map generated from this seed (the same map), if given.
        /// </summary>
        public required Action<int?> RestartRun { get; init; }

        /// <summary>
        /// Asks for a whole new run (a new track too) once the player has left.
        /// </summary>
        public required Action NewRun { get; init; }
    }
}
