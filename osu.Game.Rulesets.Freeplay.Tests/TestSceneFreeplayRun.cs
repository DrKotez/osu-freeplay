// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Extensions;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Screens;
using osu.Game.Tests.Resources;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    /// <summary>
    /// Runs freeplay inside a full game instance, using a ~10 second test beatmap with a virtual track.
    /// </summary>
    public partial class TestSceneFreeplayRun : OsuGameTestScene
    {
        private FreeplayStorage freeplayStorage = null!;

        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("import beatmap", () => Game.BeatmapManager.Import(TestResources.GetQuickTestBeatmapForImport()).WaitSafely());
            AddStep("create storage", () => freeplayStorage = new FreeplayStorage(Game.Storage));
        }

        [Test]
        public void TestMenuScreensLoad()
        {
            PushAndConfirm(() => new FreeplayMenuScreen());
            PushAndConfirm(() => new FreeplaySettingsScreen(freeplayStorage));
            AddStep("exit settings", () => Game.ScreenStack.Exit());
            AddUntilStep("settings saved", () => Game.Storage.Exists("freeplay/settings.json"));
            PushAndConfirm(() => new FreeplayStatsScreen(freeplayStorage));
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

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage));
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

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage));
            dismissNotifications();

            AddUntilStep("track playing", () => (firstPlayer = Game.ScreenStack.CurrentScreen as FreeplayPlayer) != null);
            AddStep("retry", () => firstPlayer!.Restart());

            AddUntilStep("new player playing", () => Game.ScreenStack.CurrentScreen is FreeplayPlayer player && player != firstPlayer);
            AddAssert("nothing recorded", () => freeplayStorage.LoadHistory(), () => Is.Empty);
        }

        [Test]
        public void TestQuittingEndsRun()
        {
            AddStep("set settings", () => FreeplaySettings.SetCurrent(new FreeplaySettings
            {
                Tracks = { MinLengthSeconds = 0 },
            }));

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage));
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

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage));
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
