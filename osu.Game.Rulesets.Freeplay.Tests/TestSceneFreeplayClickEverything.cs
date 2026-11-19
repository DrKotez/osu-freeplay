// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Audio;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Screens;
using osu.Game.Rulesets.Freeplay.Stable;
using osu.Game.Screens;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    /// <summary>
    /// Opens every freeplay~ page and clicks everything on it (and pulls every slider to both ends), one thing per frame,
    /// the way a curious player would: nothing may crash, and the page must stay open and usable.
    /// </summary>
    public partial class TestSceneFreeplayClickEverything : OsuGameTestScene
    {
        /// <summary>
        /// What isn't clicked: buttons which leave the page, open something outside the game, or end the run.
        /// </summary>
        private static readonly string[] skipped = { "Настроить клавиши", "Экспорт", "Выход", "В меню", "Назад" };

        private FreeplayStorage freeplayStorage = null!;
        private StableLibrary library = null!;

        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("create osu!stable folder", () =>
            {
                string stablePath = Path.Combine(Path.GetTempPath(), $"freeplay-stable-{Guid.NewGuid()}");
                string archive = Path.Combine(FreeplayGenerationTest.RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "Resources", "Archives", "quick.osz");

                ZipFile.ExtractToDirectory(archive, Path.Combine(stablePath, "Songs", "241526 Soleily - Renatus"));
                Directory.CreateDirectory(Path.Combine(stablePath, "Skins", "Skin A"));
                File.WriteAllText(Path.Combine(stablePath, "Skins", "Skin A", "skin.ini"), "[General]\nName: Skin A\n");
                File.WriteAllLines(Path.Combine(stablePath, "osu!.player.cfg"), new[] { "VolumeUniversal = 40", "CursorSize = 1.1", "Skin = Skin A" });

                library = new StableLibrary(stablePath, Game.Storage.GetStorageForDirectory("freeplay"), Game.Dependencies.Get<GameHost>(), Game.Dependencies.Get<AudioManager>(),
                    Game.Dependencies.Get<RulesetStore>());
                library.ScanAsync().WaitSafely();
            });

            AddStep("create storage", () => freeplayStorage = new FreeplayStorage(Game.Storage));
            AddStep("fresh settings", () => FreeplaySettings.SetCurrent(new FreeplaySettings()));
        }

        [Test]
        public void TestSettings() => clickThrough(() => new FreeplaySettingsScreen(freeplayStorage, library));

        [Test]
        public void TestKeyBindings() => clickThrough(() => new FreeplayKeyBindingsScreen(freeplayStorage));

        [Test]
        public void TestStatistics() => clickThrough(() => new FreeplayStatsScreen(freeplayStorage));

        [Test]
        public void TestModeSettings([Values("aim")] string mode) => clickThrough(() => new FreeplayModeSettingsScreen(freeplayStorage, library, FreeplayMode.Find(mode)!));

        [Test]
        public void TestModeSettingsWithFineTuning()
        {
            AddStep("open fine tuning", () =>
            {
                var settings = FreeplaySettings.Current.GetMode(FreeplayMode.Find("aim")!);
                settings.GoalEdited = true;
            });

            clickThrough(() => new FreeplayModeSettingsScreen(freeplayStorage, library, FreeplayMode.Find("aim")!));
        }

        [Test]
        public void TestOsuHotkeysDoNothing()
        {
            PushAndConfirm(() => new FreeplaySettingsScreen(freeplayStorage, library));

            foreach (var keys in new[] { new[] { osuTK.Input.Key.F8 }, new[] { osuTK.Input.Key.F6 }, new[] { osuTK.Input.Key.F9 }, new[] { osuTK.Input.Key.ControlLeft, osuTK.Input.Key.T },
                         new[] { osuTK.Input.Key.ControlLeft, osuTK.Input.Key.B }, new[] { osuTK.Input.Key.ControlLeft, osuTK.Input.Key.N }, new[] { osuTK.Input.Key.ControlLeft, osuTK.Input.Key.P } })
            {
                AddStep($"press {string.Join("+", keys)}", () =>
                {
                    foreach (var k in keys) InputManager.PressKey(k);
                    foreach (var k in keys.Reverse()) InputManager.ReleaseKey(k);
                });
            }

            AddWaitStep("wait", 5);
            AddAssert("no osu! overlay open", () => Game.ChildrenOfType<OverlayContainer>()
                                                        .All(o => o.State.Value == Visibility.Hidden));
            AddAssert("no toolbar", () => Game.Toolbar.State.Value, () => Is.EqualTo(Visibility.Hidden));
        }

        private void clickThrough(Func<OsuScreen> create)
        {
            OsuScreen screen = null!;
            int next = 0;
            var log = new List<string>();

            AddStep("create", () => screen = create());
            PushAndConfirm(() => screen);

            AddUntilStep("click everything", () =>
            {
                // Looked up afresh every time, as clicks rebuild pages.
                var targets = clickables(screen);

                if (next >= targets.Count)
                    return true;

                var target = targets[next++];
                log.Add(describe(target));
                target.TriggerClick();
                return false;
            });

            AddStep("pull every slider to both ends", () =>
            {
                foreach (var slider in screen.ChildrenOfType<SliderBar<double>>().ToList())
                    pull(slider.Current);
                foreach (var slider in screen.ChildrenOfType<SliderBar<float>>().ToList())
                    pull(slider.Current);
                foreach (var slider in screen.ChildrenOfType<SliderBar<int>>().ToList())
                    pull(slider.Current);
            });

            AddWaitStep("let it settle", 5);
            AddAssert("page still open", () => Game.ScreenStack.CurrentScreen, () => Is.SameAs(screen));
            AddAssert("settings still save", () =>
            {
                freeplayStorage.SaveSettings(FreeplaySettings.Current);
                return true;
            });
            AddStep("leave", () => screen.Exit());
            AddUntilStep("left", () => Game.ScreenStack.CurrentScreen != screen);
            AddStep("what was clicked", () => TestContext.Out.WriteLine(string.Join("\n", log)));
        }

        private static void pull<T>(osu.Framework.Bindables.Bindable<T> current)
            where T : struct, System.Numerics.INumber<T>, System.Numerics.IMinMaxValue<T>
        {
            if (current.Disabled || current is not osu.Framework.Bindables.BindableNumber<T> number)
                return;

            T original = number.Value;
            number.Value = number.MinValue;
            number.Value = number.MaxValue;
            number.Value = original;
        }

        private static List<Drawable> clickables(OsuScreen screen) =>
            screen.ChildrenOfType<Drawable>()
                  .Where(d => d.IsPresent && handlesClick(d.GetType()) && !isSkipped(d))
                  .ToList();

        private static bool handlesClick(Type type)
        {
            var method = type.GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic, new[] { typeof(osu.Framework.Input.Events.ClickEvent) });
            return method != null && method.DeclaringType != typeof(Drawable) && method.DeclaringType?.Namespace?.StartsWith("osu.Framework", StringComparison.Ordinal) != true
                   || typeof(osu.Framework.Graphics.Containers.ClickableContainer).IsAssignableFrom(type);
        }

        private static bool isSkipped(Drawable d)
        {
            string text = describe(d);
            return skipped.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        private static string describe(Drawable d) => $"{d.GetType().Name}: " + string.Join(" ", d.ChildrenOfType<OsuSpriteText>().Select(t => t.Text.ToString()).Take(3));
    }
}
