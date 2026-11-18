// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Extensions;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Freeplay.Screens;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Platform;
using osu.Game.Rulesets.Freeplay.Stable;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    /// <summary>
    /// Runs freeplay inside a full game instance, using a ~10 second test beatmap with a virtual track.
    /// </summary>
    public partial class TestSceneFreeplayRun : OsuGameTestScene
    {
        private FreeplayStorage freeplayStorage = null!;
        private StableLibrary library = null!;

        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("create osu!stable folder", () =>
            {
                // A fake osu!stable install with one song folder, extracted from the test archive.
                string stablePath = Path.Combine(Path.GetTempPath(), $"freeplay-stable-{Guid.NewGuid()}");
                string archive = Path.Combine(FreeplayGenerationTest.RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "Resources", "Archives", "quick.osz");

                ZipFile.ExtractToDirectory(archive, Path.Combine(stablePath, "Songs", "241526 Soleily - Renatus"));
                Directory.CreateDirectory(Path.Combine(stablePath, "Skins"));

                library = new StableLibrary(stablePath, Game.Storage.GetStorageForDirectory("freeplay"), Game.Dependencies.Get<GameHost>(), Game.Dependencies.Get<AudioManager>(),
                    Game.Dependencies.Get<RulesetStore>());
                library.ScanAsync().WaitSafely();
            });

            AddAssert("library has the song", () => library.Sets.Count, () => Is.EqualTo(1));
            AddStep("create storage", () => freeplayStorage = new FreeplayStorage(Game.Storage));
        }

        [Test]
        public void TestMenuScreensLoad()
        {
            PushAndConfirm(() => new FreeplayMenuScreen());
            PushAndConfirm(() => new FreeplaySettingsScreen(freeplayStorage, library));
            AddStep("exit settings", () => Game.ScreenStack.Exit());
            AddUntilStep("settings saved", () => Game.Storage.Exists("freeplay/settings.json"));
            PushAndConfirm(() => new FreeplayStatsScreen(freeplayStorage));
            AddStep("exit stats", () => Game.ScreenStack.Exit());

            foreach (var mode in FreeplayMode.ALL)
            {
                PushAndConfirm(() => new FreeplayModeSettingsScreen(freeplayStorage, mode));
                AddStep("exit mode settings", () => Game.ScreenStack.Exit());
            }
        }

        [Test]
        public void TestCompletedTrackContinuesToNext()
        {
            FreeplayPlayer? firstPlayer = null;

            AddStep("set settings", () => FreeplaySettings.SetCurrent(new FreeplaySettings
            {
                Tracks = { MinLengthSeconds = 0 },
                Fail = { Health = false },
            }));

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage, library, mixed));
            dismissNotifications();

            AddUntilStep("first track playing", () => (firstPlayer = Game.ScreenStack.CurrentScreen as FreeplayPlayer) != null);
            waitUntil("first track completed", () => firstPlayer!.GameplayState.HasPassed);

            // The run screen is only shown briefly between tracks, so go straight to checking for the next track.
            waitUntil("second track playing", () => Game.ScreenStack.CurrentScreen is FreeplayPlayer player && player != firstPlayer);
            AddAssert("nothing saved mid-run", () => freeplayStorage.LoadHistory(), () => Is.Empty);
        }

        [Test]
        public void TestRetryRestartsRun()
        {
            FreeplayPlayer? firstPlayer = null;

            AddStep("set settings", () => FreeplaySettings.SetCurrent(new FreeplaySettings
            {
                Tracks = { MinLengthSeconds = 0 },
            }));

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage, library, mixed));
            dismissNotifications();

            AddUntilStep("track playing", () => (firstPlayer = Game.ScreenStack.CurrentScreen as FreeplayPlayer) != null);
            AddStep("retry", () => firstPlayer!.Restart());

            AddUntilStep("new player playing", () => Game.ScreenStack.CurrentScreen is FreeplayPlayer player && player != firstPlayer);
            AddAssert("nothing recorded", () => freeplayStorage.LoadHistory(), () => Is.Empty);
        }

        [Test]
        public void TestModeUsesItsOwnSettings()
        {
            var deathstreams = FreeplayMode.Find("deathstreams")!;

            AddStep("set settings", () =>
            {
                var settings = new FreeplaySettings { Tracks = { MinLengthSeconds = 0 } };

                var mode = settings.GetMode(deathstreams);
                mode.StarRating = 6;
                mode.MinBpm = 0;
                mode.MaxBpm = 0;

                settings.GetMode(mixed).StarRating = 2;

                FreeplaySettings.SetCurrent(settings);
            });

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage, library, deathstreams));
            dismissNotifications();

            AddUntilStep("track playing", () => Game.ScreenStack.CurrentScreen is FreeplayPlayer);
            AddAssert("mode style used", () => FreeplayGenerationSettings.Current.Style, () => Is.EqualTo(PatternStyle.Deathstreams));
            AddAssert("mode star rating used", () => FreeplayGenerationSettings.Current.StarRating, () => Is.EqualTo(6));
            AddAssert("run plays a copy", () => FreeplayGenerationSettings.Current, () => Is.Not.SameAs(FreeplaySettings.Current.GetMode(deathstreams)));

            AddStep("quit gameplay", () => Game.ScreenStack.CurrentScreen.Exit());
            AddUntilStep("back at run screen", () => Game.ScreenStack.CurrentScreen is FreeplayRunScreen);
            AddStep("exit run", () => Game.ScreenStack.CurrentScreen.Exit());

            AddAssert("other modes untouched", () => FreeplaySettings.Current.GetMode(mixed).StarRating, () => Is.EqualTo(2));
            AddAssert("mode recorded", () => freeplayStorage.LoadHistory().Single().StyleName, () => Is.EqualTo("Дезстримы"));
        }

        [Test]
        public void TestQuittingEndsRun()
        {
            AddStep("set settings", () => FreeplaySettings.SetCurrent(new FreeplaySettings
            {
                Tracks = { MinLengthSeconds = 0 },
            }));

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage, library, mixed));
            dismissNotifications();

            AddUntilStep("track playing", () => Game.ScreenStack.CurrentScreen is FreeplayPlayer);
            AddStep("quit gameplay", () => Game.ScreenStack.CurrentScreen.Exit());

            AddUntilStep("back at run screen", () => Game.ScreenStack.CurrentScreen is FreeplayRunScreen);
            AddUntilStep("quit run recorded", () => freeplayStorage.LoadHistory().SingleOrDefault()?.EndReason == RunEndReason.Quit);
        }

        [Test]
        public void TestMissLimitEndsRun()
        {
            AddStep("set settings", () => FreeplaySettings.SetCurrent(new FreeplaySettings
            {
                Tracks = { MinLengthSeconds = 0 },
                Fail = { Health = false, MissLimit = 2 },
            }));

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage, library, mixed));
            dismissNotifications();

            AddUntilStep("track playing", () => Game.ScreenStack.CurrentScreen is FreeplayPlayer);
            waitUntil("back at run screen", () => Game.ScreenStack.CurrentScreen is FreeplayRunScreen);

            AddUntilStep("failed run recorded", () => freeplayStorage.LoadHistory().SingleOrDefault()?.EndReason == RunEndReason.Failed);
            AddAssert("two misses recorded", () => freeplayStorage.LoadHistory().Single().Misses, () => Is.EqualTo(2));
            AddAssert("summary shown", () => Game.ChildrenOfType<FreeplayRunScreen>().Single().ChildrenOfType<osu.Game.Graphics.Sprites.OsuSpriteText>().Any(t => t.Text == "Поражение"));
        }

        /// <summary>
        /// Like <see cref="TestScene.AddUntilStep"/>, but waits in real time.
        /// Headless test steps run faster than real time, while the test track plays in real time for ~10 seconds.
        /// </summary>
        private static FreeplayMode mixed => FreeplayMode.Find("mixed")!;

        private void waitUntil(string description, Func<bool> condition, double timeoutMs = 45000)
        {
            // Kept short so that no single step reaches the framework's own (accelerated) step timeout.
            const double slice_ms = 500;

            var stopwatch = new Stopwatch();

            AddStep($"{description}: start waiting", () => stopwatch.Restart());

            for (int i = 1; i <= Math.Ceiling(timeoutMs / slice_ms); i++)
            {
                double until = i * slice_ms;
                AddUntilStep($"{description} (≤{until / 1000:0.0}s)", () => condition() || stopwatch.Elapsed.TotalMilliseconds >= until);
            }

            AddAssert(description, condition);
        }

        private void dismissNotifications() => AddRepeatStep("dismiss notifications", DismissAnyNotifications, 5);
    }
}
