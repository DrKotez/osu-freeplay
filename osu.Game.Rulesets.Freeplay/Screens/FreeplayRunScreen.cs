// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using osu.Game.Rulesets.Freeplay.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Freeplay.Screens.Controls;
using osu.Game.Rulesets.Freeplay.Stable;
using osu.Game.Rulesets.Mods;
using osuTK;
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
        private Database.RealmAccess realm { get; set; } = null!;

        private FreeplaySettings settings = null!;
        private FreeplayGenerationSettings generation = null!;

        /// <summary>
        /// The fail conditions of this run: the mode's fail level, or the player's own with <see cref="FailLevel.Custom"/>.
        /// </summary>
        private FailSettings fail = null!;

        private TrackPicker picker = null!;

        /// <summary>
        /// The osu! mods this run plays with (chosen when it starts).
        /// </summary>
        private FreeplayModFlags runMods;

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
                Width = summary_width,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Child = flow = Column(8),
            });

            AddInternal(pauseMenu = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0,
                Children = new Drawable[]
                {
                    // Over the page: clicks on it don't reach the buttons underneath.
                    new PauseBackdrop { RelativeSizeAxes = Axes.Both, Colour = FreeplayColours.BACKGROUND, Alpha = 0.92f },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 14),
                        Children = new Drawable[]
                        {
                            Heading("Пауза", 64).With(h =>
                            {
                                h.Anchor = Anchor.TopCentre;
                                h.Origin = Anchor.TopCentre;
                            }),
                            Text("Забег стоит · Esc — продолжить", 20, FontWeight.SemiBold).With(t =>
                            {
                                t.Anchor = Anchor.TopCentre;
                                t.Origin = Anchor.TopCentre;
                                t.Colour = FreeplayColours.TEXT_DIM;
                                t.Margin = new MarginPadding { Bottom = 10 };
                            }),
                            centred(Button("Продолжить", resume, 320)),
                            centred(Button("Закончить забег", () => quitFromPause(toMenu: false), 320, blue: true)),
                            centred(Button("Выйти в меню", () => quitFromPause(toMenu: true), 320, blue: true)),
                        }
                    },
                }
            });
        }

        private static Drawable centred(Drawable d)
        {
            d.Anchor = Anchor.TopCentre;
            d.Origin = Anchor.TopCentre;
            return d;
        }

        #region Pause

        private Container pauseMenu = null!;

        /// <summary>
        /// Whether the run is paused between tracks (Esc there pauses rather than leaving).
        /// </summary>
        public bool Paused { get; private set; }

        /// <summary>
        /// What "Продолжить" does besides closing the menu: load the track the player backed out of, or nothing (the countdown carries on).
        /// </summary>
        private Action? resumeAction;

        private double? pausedCountdownLeft;

        public override bool OnBackButton()
        {
            // A finished run is just its summary: Esc leaves it.
            if (ended)
                return false;

            if (Paused)
                resume();
            else
                pause(null);

            return true;
        }

        private void pause(Action? onResume)
        {
            if (Paused)
                return;

            Paused = true;
            resumeAction = onResume;
            pausedCountdownLeft = autoContinueAt is double at ? Math.Max(0, at - Time.Current) : null;
            autoContinueAt = null;
            GetContainingFocusManager()?.ChangeFocus(null);
            pauseMenu.FadeIn(150, Easing.OutQuint);
        }

        private void resume()
        {
            if (!Paused)
                return;

            Paused = false;
            pauseMenu.FadeOut(150, Easing.OutQuint);

            var action = resumeAction;
            resumeAction = null;

            if (action != null)
            {
                pausedCountdownLeft = null;
                action();
                return;
            }

            // At least a moment to get ready again.
            if (pausedCountdownLeft is double left)
                autoContinueAt = Time.Current + Math.Max(left, 3000);

            pausedCountdownLeft = null;
            updateCountdown();
        }

        private void quitFromPause(bool toMenu)
        {
            if (!Paused)
                return;

            Paused = false;
            resumeAction = null;
            pausedCountdownLeft = null;
            pauseMenu.FadeOut(150, Easing.OutQuint);

            waitingForNextTrack = false;
            endRun(RunEndReason.Quit);

            if (toMenu)
                this.Exit();
        }

        private partial class PauseBackdrop : Box
        {
            protected override bool OnMouseDown(MouseDownEvent e) => true;
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnHover(HoverEvent e) => true;
        }

        #endregion

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);
            startRun();
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            pendingCancellation?.Cancel();

            // Silence the last track, so the menu's own music plays.
            Beatmap.Value.Track.Stop();
            Beatmap.SetDefault();
            FailSettings.Active = null;

            return base.OnExiting(e);
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            if (ended)
                return;

            // The player only reports once it's been shown. Without a report, either the beatmap couldn't be loaded,
            // or the loading screen was exited by the user.
            bool reported = pendingOutcome != null;
            var outcome = pendingOutcome ?? (currentPlayer?.LoadState >= LoadState.Ready && !currentPlayer.LoadedBeatmapSuccessfully ? TrackOutcome.LoadFailed : TrackOutcome.Quit);

            pendingOutcome = null;
            currentPlayer = null;

            if (newRunRequested)
            {
                // "Новый забег" from the pause menu: this run is kept in the history as quit, and another starts.
                newRunRequested = false;

                if (run.Tracks.Count > 0)
                {
                    run.EndReason = RunEndReason.Quit;
                    freeplayStorage.AddRun(run);
                }

                // (Once this screen is current again.)
                Schedule(startRun);
                return;
            }

            if (!reported && outcome == TrackOutcome.Quit && playing?.difficulty is StableDifficulty left)
            {
                // Esc on the loading screen, before the track began: the run pauses, and "Продолжить" loads the same track again.
                pendingCancellation?.Cancel();
                pending = playing;
                showMessage("Пауза", $"Следующий трек: {left.Artist} — {left.Title} [{left.Version}]");
                pause(playNextTrack);
                return;
            }

            switch (outcome)
            {
                case TrackOutcome.Completed:
                    showBetweenTracks();
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
            fail = AimGoals.FailFor(generation.FailLevel, settings.Fail);
            runMods = settings.Mods;
            // Hard Rock turns the map upside down after it's generated, so the quarter to practise is generated upside down too.
            generation.WeakZoneUpsideDown = runMods.HasFlag(FreeplayModFlags.HardRock);

            // The quarter to practise, from the latest runs' misses.
            if (generation.WeakZone == WeakZone.Auto)
                generation.AutoWeakQuarter = AimAnalysis.Latest(freeplayStorage.LoadHistory()).analysis?.WeakestQuarter();
            FailSettings.Active = fail;

            // Read by the beatmap converter when each track is generated.
            FreeplayGenerationSettings.Current = generation;
            MinedPatternSet.Current = library.Patterns;
            picker = new TrackPicker(library, settings.Tracks, generation);
            rampBpm = null;
            rampStart = null;

            resetRun();

            if (picker.CountAvailable() == 0)
            {
                showMessage("Нет подходящих треков", !library.Exists
                    ? $"Не найдена папка osu!stable: {library.StablePath}. Укажите её в настройках."
                    : $"Треки: {FreeplayModeSettingsScreen.DescribeTracks(generation.TrackCollection, settings, library)}. "
                      + "Выбери другую коллекцию или расширь BPM в настройках режима, длину треков — в общих настройках. Если библиотека ещё сканируется, подожди немного.");
                addMenuButtons(allowRetry: false);
                ended = true;
                return;
            }

            playNextTrack();
        }

        private bool newRunRequested;

        /// <summary>
        /// "Трек заново": the run starts over on the track being played, with the same map when its seed is known.
        /// </summary>
        private void restartTrack(int? seed)
        {
            var track = current;
            var loaded = playing;

            resetRun();

            if (track is not var (set, difficulty, settings))
                return;

            if (seed != null)
            {
                settings = settings.Clone();
                settings.Seed = seed;
            }

            FreeplayGenerationSettings.Current = settings;
            current = (set, difficulty, settings);
            playing = loaded == null ? null : loaded with { settings = settings };

            // (The next track, prepared for the old run, was dropped with it.)
            prepareNext(1);
        }

        private void resetRun()
        {
            // A track prepared for the old run doesn't belong to the new one.
            pendingCancellation?.Cancel();
            pending = null;
            playing = null;

            run = new RunRecord
            {
                StarRating = generation.StarRating,
                Style = generation.Style,
                Preset = mode.HasGoals ? $"{mode.Name} · {AimGoals.Name(generation)}" : mode.Name,
                Mods = FreeplayMods.Describe(runMods),
            };

            health = 1;
            consecutiveLoadFailures = 0;
            lastFeedback = null;
            current = null;
            waitingForNextTrack = false;
            autoContinueAt = null;
            pendingOutcome = null;
            ended = false;
        }

        private void playNextTrack()
        {
            if (!this.IsCurrentScreen() || ended)
                return;

            // Prepared while the last track played (see prepareNext), or picked now.
            var next = pending ?? pickTrack(run.Tracks.Count);
            pending = null;
            playing = next;

            if (next.end is RunEndReason reason)
            {
                endRun(reason);
                return;
            }

            var (set, difficulty, track, working) = (next.set!, next.difficulty!, next.settings!, next.working!);
            bool remix = track.RhythmSource == RhythmSource.Remix;

            FreeplayGenerationSettings.Current = track;
            current = (set, difficulty, track);

            var ruleset = rulesets.GetRuleset(FreeplayRuleset.SHORT_NAME);

            if (ruleset == null)
                throw new InvalidOperationException("The freeplay ruleset isn't available.");

            Ruleset.Value = ruleset;
            Mods.Value = FreeplayMods.Create(runMods);
            Beatmap.Value = working;

            showMessage($"Трек {run.Tracks.Count + 1}", $"{difficulty.Artist} — {difficulty.Title} [{difficulty.Version}] · {difficulty.Bpm:0} BPM");

            // The context is created per player, so that a retry (which creates a new player from the same loader) picks up the reset run.
            this.Push(new FreeplayPlayerLoader(() => currentPlayer = new FreeplayPlayer(createContext()), difficulty.Bpm,
                (remix ? $"{mode.Name} · сложность маппера ближе всего к {track.StarRating:0.0}★" : $"{mode.Name} · {describeTrack(track)}") + (FreeplayMods.Describe(runMods) is string modNames ? $" · {modNames}" : string.Empty)));

            // The next one is chosen now and generated in the background once this one is under way, so it's ready when this one ends.
            // (Not by this screen's scheduler: it doesn't run while the player is on top.)
            prepareNext(run.Tracks.Count + 1);
        }

        /// <summary>
        /// How long after a track starts loading the next one is prepared (long enough for this one to load first).
        /// </summary>
        public static double PrepareDelay = 8000;

        /// <summary>
        /// The next track: chosen and (if done in time) generated while the last one played.
        /// </summary>
        private PickedTrack? pending;

        private CancellationTokenSource? pendingCancellation;

        /// <summary>
        /// The track whose loading screen is up (loaded again if the player backs out of it).
        /// </summary>
        private PickedTrack? playing;

        private record PickedTrack(StableSet? set, StableDifficulty? difficulty, FreeplayGenerationSettings? settings, StableWorkingBeatmap? working, RunEndReason? end);

        private void prepareNext(int index)
        {
            // Only for the track that's playing now: a run that ended or restarted doesn't need it.
            if (ended || pending != null || run.Tracks.Count + 1 != index)
                return;

            var next = pickTrack(index);
            pending = next;

            if (next.working == null)
                return;

            pendingCancellation?.Cancel();
            pendingCancellation = new CancellationTokenSource();
            next.working.Pregenerate(rulesets.GetRuleset(FreeplayRuleset.SHORT_NAME)!, FreeplayMods.Create(runMods), next.settings!, PrepareDelay, pendingCancellation.Token);
        }

        /// <summary>
        /// Chooses track number <paramref name="index"/> (from zero) of the run and its settings.
        /// </summary>
        private PickedTrack pickTrack(int index)
        {
            // A remix plays the mapper's own difficulty, so pick the one closest to the requested star rating.
            // With ranges on, each track gets its own values. Read by the beatmap converter when the track is generated.
            // Tracks alternate between the easier and harder half of the star range, so a run has ups and downs.
            double quantile = index % 2 == 0 ? rollRng.NextDouble() * 0.55 : 0.45 + rollRng.NextDouble() * 0.55;
            var track = AimGoals.ForTrack(generation, index, settings.PlayerLevel, rollRng).Roll(rollRng, quantile);

            bool remix = track.RhythmSource == RhythmSource.Remix;
            bool ramp = track.Style == PatternStyle.Metronome && track.Ramp;

            var next = ramp
                ? picker.NextFaster(rampBpm)
                : picker.Next(closestToStars: remix ? track.StarRating : null, generatedStars: remix ? null : track.StarRating);

            if (next == null)
                return new PickedTrack(null, null, null, null, ramp && rampBpm != null ? RunEndReason.RampDone : RunEndReason.OutOfTracks);

            var (set, difficulty) = next.Value;

            if (ramp)
            {
                // The stars climb with the speed, so the shapes keep about their size as they get faster (as with stars maps from 260 to 320).
                rampStart ??= (difficulty.Bpm, track.StarRating);
                track.StarRating = Math.Clamp(Math.Round(rampStart.Value.stars * difficulty.Bpm / rampStart.Value.bpm, 2), 1, 12);
                track.StarRatingMax = track.StarRating;
                rampBpm = difficulty.Bpm;
            }

            return new PickedTrack(set, difficulty, track, (StableWorkingBeatmap)library.GetWorkingBeatmap(set, difficulty), null);
        }

        private readonly Random rollRng = new Random();

        /// <summary>
        /// A speed-up run (see <see cref="FreeplayGenerationSettings.Ramp"/>): the last track's BPM, and where the run started.
        /// </summary>
        private double? rampBpm;

        private (double bpm, double stars)? rampStart;

        /// <summary>
        /// The track being played: where it's from, and the settings it was generated with.
        /// </summary>
        private (StableSet set, StableDifficulty difficulty, FreeplayGenerationSettings settings)? current;

        /// <summary>
        /// Feedback for the last track played (saved once the player marks something).
        /// </summary>
        private FeedbackEntry? lastFeedback;

        private bool waitingForNextTrack;


        private FeedbackEntry createFeedback(TrackRecord record, TrackOutcome outcome, (StableSet set, StableDifficulty difficulty, FreeplayGenerationSettings settings) track)
        {
            var settingsUsed = (record.Generated as FreeplayBeatmap)?.Settings ?? track.settings;
            int judged = record.Great + record.Ok + record.Meh + record.Misses;

            return new FeedbackEntry
            {
                Mode = mode.Name,
                Goal = describeGoal(track.settings),
                Artist = record.Artist,
                Title = record.Title,
                Difficulty = record.Difficulty,
                SetFolder = track.set.Folder,
                DifficultyFile = track.difficulty.File,
                Bpm = record.Bpm,
                Seed = record.Seed,
                TargetStars = track.settings.StarRating,
                ActualStars = record.StarRating,
                Outcome = outcome.ToString(),
                Accuracy = judged == 0 ? 1 : (record.Great * 300 + record.Ok * 100 + record.Meh * 50) / (judged * 300.0),
                Misses = record.Misses,
                UnstableRate = record.UnstableRate,
                PlayedSeconds = Math.Round(record.PlayedMs / 1000),
                Settings = settingsUsed,
            };
        }

        private void saveFeedback()
        {
            if (lastFeedback == null)
                return;

            var record = run.Tracks.LastOrDefault();
            string source = System.IO.Path.Combine(library.SongsPath, lastFeedback.SetFolder, lastFeedback.DifficultyFile);
            freeplayStorage.SaveFeedback(lastFeedback, record?.Generated, source);
        }

        /// <summary>
        /// Between tracks: a moment to mark the last one. Marking something waits for the player to continue.
        /// </summary>
        /// <summary>
        /// How long the break between tracks lasts when the player doesn't touch anything.
        /// </summary>
        public static double AutoContinueDelay { get; set; } = 30_000;

        private double? autoContinueAt;
        private OsuSpriteText? countdownText;

        private void showBetweenTracks()
        {
            showMessage($"Трек {run.Tracks.Count} пройден", string.Empty);

            waitingForNextTrack = true;

            if (lastFeedback != null)
            {
                flow.Add(feedbackPanel = new FeedbackPanel(lastFeedback, () =>
                {
                    saveFeedback();

                    // The player is marking or writing something: wait for them as long as they like.
                    autoContinueAt = null;
                    updateCountdown();
                }));
            }

            autoContinueAt = Time.Current + (lastFeedback == null ? delay_between_tracks : AutoContinueDelay);

            flow.Add(countdownText = Text(string.Empty, 20, FontWeight.SemiBold).With(t =>
            {
                t.Anchor = Anchor.TopCentre;
                t.Origin = Anchor.TopCentre;
                t.Colour = FreeplayColours.TEXT_DIM;
            }));

            flow.Add(Button("Дальше", continueRun, 280).With(b =>
            {
                b.Anchor = Anchor.TopCentre;
                b.Origin = Anchor.TopCentre;
            }));

            updateCountdown();
        }

        protected override void Update()
        {
            base.Update();

            if (!waitingForNextTrack || autoContinueAt == null)
                return;

            if (Time.Current >= autoContinueAt)
                continueRun();
            else
                updateCountdown();
        }

        private void updateCountdown()
        {
            if (countdownText == null)
                return;

            countdownText.Text = autoContinueAt is double at
                ? $"Следующий трек через {Math.Ceiling(Math.Max(0, at - Time.Current) / 1000):0} с · Enter или клавиша нот — сразу"
                : "Ждём тебя · Enter, клавиша нот или «Дальше»";
        }

        private void continueRun()
        {
            if (!waitingForNextTrack || Paused)
                return;

            waitingForNextTrack = false;
            autoContinueAt = null;
            countdownText = null;
            playNextTrack();
        }

        /// <summary>
        /// Whether the player is writing in the feedback box (where note keys are letters).
        /// </summary>
        private bool typing => feedbackPanel?.Comment.HasFocus == true;

        private FeedbackPanel? feedbackPanel;

        /// <summary>
        /// Whether <paramref name="key"/> is one the player hits notes with (from their key bindings; mouse buttons aren't, as they also click the marks).
        /// </summary>
        private bool isNoteKey(osuTK.Input.Key key)
        {
            var input = osu.Framework.Input.Bindings.KeyCombination.FromKey(key);

            return realm.Run(r => r.All<Game.Input.Bindings.RealmKeyBinding>()
                                   .Where(b => b.RulesetName == FreeplayRuleset.SHORT_NAME && b.Variant == 0)
                                   .AsEnumerable()
                                   .Any(b => (b.ActionInt == (int)Osu.OsuAction.LeftButton || b.ActionInt == (int)Osu.OsuAction.RightButton)
                                             && b.KeyCombination.Keys.Length == 1 && b.KeyCombination.Keys[0] == input));
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (Paused)
                return base.OnKeyDown(e);

            if (waitingForNextTrack && !e.Repeat && (e.Key == osuTK.Input.Key.Enter || e.Key == osuTK.Input.Key.KeypadEnter || (isNoteKey(e.Key) && !typing)))
            {
                continueRun();
                return true;
            }

            return base.OnKeyDown(e);
        }

        private string describeGoal(FreeplayGenerationSettings track)
        {
            if (!mode.HasGoals)
                return string.Empty;

            return track.SurpriseOf is AimGoal pick ? $"Сюрприз: {pick.GetDescription()}" : AimGoals.Name(track);
        }

        /// <summary>
        /// The track's star rating, and with ranges on, the other values it rolled.
        /// </summary>
        private string describeTrack(FreeplayGenerationSettings track)
        {
            string goal = describeGoal(track);
            string text = (goal.Length > 0 ? $"{goal} · " : string.Empty) + $"{track.StarRating:0.0}★";

            if (track.Palette?.Describe() is string character && character.Length > 0)
                text += $" · {character}";

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
            Fail = fail,
            StartingHealth = health,
            ElapsedBeforeMs = run.SurvivedMs,
            TimeLimitMs = fail.TimeLimitMinutes * 60_000,
            Report = onTrackReported,
            // Called on the player's update. This screen is hidden while suspended, so its own scheduler wouldn't run.
            ReturnToRun = () => this.MakeCurrent(),
            RestartRun = restartTrack,
            NewRun = () => newRunRequested = true,
        };

        private void onTrackReported(TrackRecord? record, TrackOutcome outcome, double finalHealth)
        {
            pendingOutcome = outcome;

            if (record == null)
                return;

            consecutiveLoadFailures = 0;

            // Only the last track's map is kept, for its feedback.
            if (run.Tracks.Count > 0)
                run.Tracks[^1].Generated = null;

            run.Tracks.Add(record);
            lastFeedback = current == null ? null : createFeedback(record, outcome, current.Value);

            health = Math.Min(1, finalHealth + fail.HealthRefill);
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

        private const float summary_width = 900;

        private void showMessage(string heading, string detail)
        {
            flow.Clear();
            flow.Add(Heading(heading, 52).With(h =>
            {
                h.Anchor = Anchor.TopCentre;
                h.Origin = Anchor.TopCentre;
            }));
            flow.Add(Text(detail, 22, FontWeight.SemiBold).With(t =>
            {
                t.Anchor = Anchor.TopCentre;
                t.Origin = Anchor.TopCentre;
                t.Colour = FreeplayColours.TEXT_DIM;
            }));
        }

        private void showSummary()
        {
            showMessage(DescribeEnd(run.EndReason), $"{run.StyleName} · {run.StarRating:0.0}★{(run.Mods != null ? $" · {run.Mods}" : string.Empty)} · {run.Date.LocalDateTime:dd.MM HH:mm}");

            if (lastFeedback != null && run.Tracks.Count > 0)
            {
                var last = run.Tracks[^1];
                flow.Add(new TitledPanel($"Последний трек: {last.Artist} — {last.Title}").With(p => p.Body.Add(new FeedbackPanel(lastFeedback, saveFeedback))));
            }

            flow.Add(new Container { Height = 10 });

            flow.Add(new StatTiles(new Drawable[]
            {
                new StatTile("время в игре", FormatDuration(run.SurvivedMs), FreeplayColours.BLUE),
                new StatTile("треков пройдено", $"{run.TracksCompleted}", FreeplayColours.PINK),
                new StatTile("точность", $"{run.Accuracy:P2}"),
                new StatTile("макс. комбо", $"{run.MaxCombo}x"),
                new StatTile("UR", run.UnstableRate is double runUr ? $"{runUr:0.0}" : "—"),
                new StatTile("промахи", $"{run.Misses}", FreeplayColours.DEEP_PINK),
            }));

            // How far a speed-up run got: the fastest track finished.
            if (rampStart is var (startBpm, startStars) && run.Tracks.Where(t => t.Completed).MaxBy(t => t.Bpm) is TrackRecord fastest)
            {
                flow.Add(new TitledPanel("Разгон").With(p => p.Body.Add(Text(
                    $"Дошёл до {fastest.Bpm:0} BPM · {fastest.StarRating:0.00}★ (начал с {startBpm:0} BPM · {startStars:0.0}★)", 20, FontWeight.SemiBold))));
            }

            flow.Add(new TitledPanel("Попадания").With(p => p.Body.Add(Text($"300: {run.Great}    100: {run.Ok}    50: {run.Meh}    промахи: {run.Misses}", 18, FontWeight.SemiBold))));

            if (run.Aim is AimAnalysis aim)
                flow.Add(new AimAnalysisPanel("Разбор миссов", aim));

            if (run.HealthGraph.Count > 1)
                flow.Add(new TitledPanel("Здоровье по ходу забега").With(p => p.Body.Add(new HealthGraph(run.HealthGraph))));

            if (run.Tracks.Count > 0)
            {
                var tracks = new TitledPanel($"Треки ({run.Tracks.Count})");

                foreach (var (track, index) in run.Tracks.Select((t, i) => (t, i)))
                {
                    string ur = track.UnstableRate is double value ? $" · UR {value:0.0}" : string.Empty;

                    tracks.Body.Add(new ListRow(
                        $"{index + 1}. {track.Artist} — {track.Title} [{track.Difficulty}]",
                        $"{track.StarRating:0.00}★ · {track.Bpm:0} BPM · {FormatDuration(track.PlayedMs)} · {track.MaxCombo}x · промахов: {track.Misses}{ur} · seed {track.Seed}",
                        track.Completed ? "пройден" : "не пройден",
                        track.Completed ? FreeplayColours.BLUE : FreeplayColours.DEEP_PINK));
                }

                flow.Add(tracks);
            }

            addMenuButtons(allowRetry: true);
        }

        private void addMenuButtons(bool allowRetry)
        {
            var buttons = new FillFlowContainer
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(12, 0),
                Margin = new MarginPadding { Top = 16, Bottom = 30 },
            };

            if (allowRetry)
                buttons.Add(Button("Новый забег", startRun, 280));

            buttons.Add(Button("В меню", () => this.Exit(), 280, blue: true));

            flow.Add(buttons);
        }

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

                case RunEndReason.RampDone:
                    return "Разгон пройден: быстрее треков нет";

                default:
                    return "Забег прерван";
            }
        }

        #endregion

        /// <summary>
        /// A bar per health sample: deep pink when low, pink, then blue when healthy.
        /// </summary>
        private partial class HealthGraph : FillFlowContainer
        {
            private const int max_bars = 240;

            public HealthGraph(IReadOnlyList<float> samples)
            {
                RelativeSizeAxes = Axes.X;
                Height = 90;
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
                            Colour = value < 0.3f ? FreeplayColours.DEEP_PINK : value < 0.6f ? FreeplayColours.PINK : FreeplayColours.BLUE,
                        }
                    });
                }
            }
        }
    }
}
