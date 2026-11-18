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
using osu.Game.Rulesets.Freeplay.Screens.Controls;
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
        public void TestSwitchingStableSkins()
        {
            AddStep("create two skins", () =>
            {
                foreach (string name in new[] { "Skin A", "Skin B" })
                {
                    string folder = Path.Combine(library.SkinsPath, name);
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "skin.ini"), $"[General]\nName: {name}\nAuthor: test\n");
                }
            });

            var skins = (Func<osu.Game.Skinning.SkinManager>)(() => Game.Dependencies.Get<osu.Game.Skinning.SkinManager>());

            AddStep("apply A", () => StableSkinSelector.Apply(skins(), library, "Skin A"));
            AddUntilStep("A is current", () => skins().CurrentSkin.Value is StableSkin && skins().CurrentSkin.Value.SkinInfo.PerformRead(s => s.Name) == "Skin A");

            AddStep("apply B", () => StableSkinSelector.Apply(skins(), library, "Skin B"));
            AddUntilStep("B is current", () => skins().CurrentSkin.Value is StableSkin && skins().CurrentSkin.Value.SkinInfo.PerformRead(s => s.Name) == "Skin B");

            AddStep("apply A again", () => StableSkinSelector.Apply(skins(), library, "Skin A"));
            AddUntilStep("A is current again", () => skins().CurrentSkin.Value.SkinInfo.PerformRead(s => s.Name) == "Skin A");

            AddStep("apply built-in", () => StableSkinSelector.Apply(skins(), library, null));
            AddUntilStep("built-in is current", () => skins().CurrentSkin.Value is osu.Game.Skinning.DefaultLegacySkin);

            // And gameplay actually draws with it.
            AddStep("apply B", () => StableSkinSelector.Apply(skins(), library, "Skin B"));
            AddStep("set settings", () => FreeplaySettings.SetCurrent(new FreeplaySettings { Tracks = { MinLengthSeconds = 0 }, Stable = { Skin = "Skin B" } }));

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage, library, mixed));
            dismissNotifications();

            AddUntilStep("track playing", () => Game.ScreenStack.CurrentScreen is FreeplayPlayer);
            AddUntilStep("circles drawn by the stable skin", () => Game.ChildrenOfType<osu.Game.Rulesets.Osu.Skinning.Legacy.LegacyMainCirclePiece>().Any());
            AddAssert("skin is still B", () => skins().CurrentSkin.Value.SkinInfo.PerformRead(s => s.Name), () => Is.EqualTo("Skin B"));
        }

        [Test]
        public void TestMenuCursorTakesOnlyTheSkinsOwnParts()
        {
            var skins = (Func<osu.Game.Skinning.SkinManager>)(() => Game.Dependencies.Get<osu.Game.Skinning.SkinManager>());

            AddStep("create a skin with only a cursor", () =>
            {
                string folder = Path.Combine(library.SkinsPath, "Cursor Only");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "skin.ini"), "[General]\nName: Cursor Only\n");

                // A 1x1 white PNG.
                File.WriteAllBytes(Path.Combine(folder, "cursor.png"), Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg=="));
            });

            // The menu cursor only shows (and updates) once the mouse has been used.
            AddStep("move mouse", () => InputManager.MoveMouseTo(Game.ScreenSpaceDrawQuad.Centre));

            AddStep("apply it", () => StableSkinSelector.Apply(skins(), library, "Cursor Only"));
            AddUntilStep("skin current", () => skins().CurrentSkin.Value.SkinInfo.PerformRead(s => s.Name) == "Cursor Only");

            AddUntilStep("menu cursor shows the skin's cursor and no borrowed middle", () =>
            {
                var cursor = Game.ChildrenOfType<osu.Game.Graphics.Cursor.MenuCursorContainer.Cursor>().FirstOrDefault();

                if (cursor == null || !cursor.UsesSkinCursor)
                    return false;

                var sprites = cursor.ChildrenOfType<osu.Framework.Graphics.Sprites.Sprite>().ToList();
                return sprites.Count(s => s.Texture != null) == 1;
            });
        }

        [Test]
        public void TestImportingStableSettings()
        {
            var settings = new FreeplaySettings();

            AddStep("write a stable config", () => File.WriteAllLines(Path.Combine(library.StablePath, "osu!.player.cfg"), new[]
            {
                "# osu! configuration for player",
                "VolumeUniversal = 20",
                "VolumeMusic = 30",
                "VolumeEffect = 100",
                "Offset = -10",
                "DimLevel = 85",
                "CursorSize = 1.06",
                "ScoreMeter = Error",
                "ScoreMeterScale = 2.01",
                "Skin = Skin B",
                "FrameSync = Unlimited",
                "keyOsuLeft = G",
                "keyOsuRight = H",
                "keyOsuSmoke = L",
                "keyPause = Escape",
            }));

            AddStep("import", () =>
            {
                var stable = StableConfig.Find(library.StablePath)!;
                new StableSettingsImport(Game.Dependencies.Get<osu.Game.Configuration.OsuConfigManager>(), Game.Dependencies.Get<osu.Framework.Configuration.FrameworkConfigManager>(),
                    Game.Dependencies.Get<AudioManager>(), Game.Dependencies.Get<osu.Game.Database.RealmAccess>()).Apply(stable, settings);
            });

            AddAssert("volume", () => Game.Dependencies.Get<AudioManager>().Volume.Value, () => Is.EqualTo(0.2).Within(0.001));
            AddAssert("music volume", () => Game.Dependencies.Get<AudioManager>().VolumeTrack.Value, () => Is.EqualTo(0.3).Within(0.001));
            AddAssert("offset", () => Game.Dependencies.Get<osu.Game.Configuration.OsuConfigManager>().Get<double>(osu.Game.Configuration.OsuSetting.AudioOffset), () => Is.EqualTo(-10));
            AddAssert("dim", () => Game.Dependencies.Get<osu.Game.Configuration.OsuConfigManager>().Get<double>(osu.Game.Configuration.OsuSetting.DimLevel), () => Is.EqualTo(0.85).Within(0.001));
            AddAssert("skin", () => settings.Stable.Skin, () => Is.EqualTo("Skin B"));
            AddAssert("hit error scale", () => settings.Timing.Scale, () => Is.EqualTo(2.01).Within(0.001));

            AddAssert("left key is G", () => Game.Dependencies.Get<osu.Game.Database.RealmAccess>().Run(r => r.All<osu.Game.Input.Bindings.RealmKeyBinding>()
                                                                                                     .Where(b => b.RulesetName == FreeplayRuleset.SHORT_NAME)
                                                                                                     .AsEnumerable()
                                                                                                     .Any(b => b.ActionInt == (int)osu.Game.Rulesets.Osu.OsuAction.LeftButton
                                                                                                               && b.KeyCombination.Equals(new osu.Framework.Input.Bindings.KeyCombination(osu.Framework.Input.Bindings.InputKey.G)))));

            AddAssert("mouse buttons still bound", () => Game.Dependencies.Get<osu.Game.Database.RealmAccess>().Run(r => r.All<osu.Game.Input.Bindings.RealmKeyBinding>()
                                                                                                             .Where(b => b.RulesetName == FreeplayRuleset.SHORT_NAME)
                                                                                                             .AsEnumerable()
                                                                                                             .Any(b => b.ActionInt == (int)osu.Game.Rulesets.Osu.OsuAction.LeftButton
                                                                                                                       && b.KeyCombination.Keys.Contains(osu.Framework.Input.Bindings.InputKey.MouseLeft))));
        }

        [Test]
        public void TestChoosingSkinInSettings()
        {
            var skins = (Func<osu.Game.Skinning.SkinManager>)(() => Game.Dependencies.Get<osu.Game.Skinning.SkinManager>());

            AddStep("create skin", () =>
            {
                string folder = Path.Combine(library.SkinsPath, "Skin B");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "skin.ini"), "[General]\nName: Skin B\n");
            });

            AddStep("set settings", () => FreeplaySettings.SetCurrent(new FreeplaySettings()));
            PushAndConfirm(() => new FreeplaySettingsScreen(freeplayStorage, library));

            FreeplayDropdown<string?>? dropdown = null;

            AddUntilStep("find skin dropdown", () => (dropdown = Game.ChildrenOfType<FreeplayDropdown<string?>>().FirstOrDefault(d => d.ChildrenOfType<osu.Game.Graphics.Sprites.OsuSpriteText>().Any(t => t.Text == "Скин"))) != null);

            AddStep("scroll to it", () => Game.ChildrenOfType<osu.Game.Graphics.Containers.OsuScrollContainer>().First().ScrollIntoView(dropdown!, false));
            AddWaitStep("settle", 3);

            AddStep("open list", () =>
            {
                InputManager.MoveMouseTo(dropdown!.ChildrenOfType<osu.Framework.Graphics.Containers.ClickableContainer>().First());
                InputManager.Click(osuTK.Input.MouseButton.Left);
            });

            AddWaitStep("open", 5);

            AddStep("click Skin B", () =>
            {
                var text = dropdown!.ChildrenOfType<osu.Game.Graphics.Sprites.TruncatingSpriteText>().Last(t => t.Text == "Skin B");
                InputManager.MoveMouseTo(text);
                InputManager.Click(osuTK.Input.MouseButton.Left);
            });

            AddUntilStep("setting saved", () => FreeplaySettings.Current.Stable.Skin, () => Is.EqualTo("Skin B"));
            AddUntilStep("skin applied", () => skins().CurrentSkin.Value.SkinInfo.PerformRead(s => s.Name), () => Is.EqualTo("Skin B"));
        }

        /// <summary>
        /// Applies each skin of the local osu!stable install and checks its textures load.
        /// </summary>
        [Explicit]
        [Test]
        public void TestLocalStableSkinsLoad()
        {
            var skins = (Func<osu.Game.Skinning.SkinManager>)(() => Game.Dependencies.Get<osu.Game.Skinning.SkinManager>());
            StableLibrary? local = null;

            AddStep("open local install", () => local = new StableLibrary(StableLibrary.DEFAULT_PATH, Game.Storage.GetStorageForDirectory("freeplay-local"),
                Game.Dependencies.Get<GameHost>(), Game.Dependencies.Get<AudioManager>(), Game.Dependencies.Get<RulesetStore>()));

            foreach (string name in StableSkin.List(Path.Combine(StableLibrary.DEFAULT_PATH, "Skins")).Take(8))
            {
                AddStep($"apply {name}", () => StableSkinSelector.Apply(skins(), local!, name));
                AddUntilStep($"{name} current", () => skins().CurrentSkin.Value.SkinInfo.PerformRead(s => s.Name) == name);
                AddStep($"{name} textures", () =>
                {
                    var skin = skins().CurrentSkin.Value;
                    Console.WriteLine($"SKIN {name}: hitcircle {skin.GetTexture("hitcircle") != null}, cursor {skin.GetTexture("cursor") != null}, type {skin.GetType().Name}");
                });
            }
        }

        [Test]
        public void TestSettingsHotkeyOpensFreeplaySettings()
        {
            PushAndConfirm(() => new FreeplayMenuScreen());

            AddStep("press Ctrl+O", () =>
            {
                InputManager.PressKey(osuTK.Input.Key.ControlLeft);
                InputManager.Key(osuTK.Input.Key.O);
                InputManager.ReleaseKey(osuTK.Input.Key.ControlLeft);
            });

            AddUntilStep("freeplay settings open", () => Game.ScreenStack.CurrentScreen is FreeplaySettingsScreen);
            AddAssert("osu! settings stay closed", () => Game.ChildrenOfType<osu.Game.Overlays.SettingsOverlay>().All(s => s.State.Value == osu.Framework.Graphics.Containers.Visibility.Hidden));
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
            PushAndConfirm(() => new FreeplayKeyBindingsScreen(freeplayStorage));
            AddAssert("bindings shown", () => Game.ChildrenOfType<FreeplayKeyBindingsScreen>().Single().ChildrenOfType<osu.Game.Graphics.Sprites.OsuSpriteText>().Any(t => t.Text == "Z"));
            AddStep("exit keys", () => Game.ScreenStack.Exit());

            foreach (var mode in FreeplayMode.ALL)
            {
                PushAndConfirm(() => new FreeplayModeSettingsScreen(freeplayStorage, library, mode));
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
        public void TestTimingOverlayStaysWithHudHidden()
        {
            FreeplayPlayer? player = null;

            AddStep("set settings", () => FreeplaySettings.SetCurrent(new FreeplaySettings { Tracks = { MinLengthSeconds = 0 } }));

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage, library, mixed));
            dismissNotifications();

            AddUntilStep("track playing", () => (player = Game.ScreenStack.CurrentScreen as FreeplayPlayer) != null);
            AddUntilStep("timing overlay shown", () => player!.ChildrenOfType<FreeplayTimingOverlay>().SingleOrDefault()?.IsPresent == true);

            AddStep("hide HUD", () => player!.ChildrenOfType<osu.Game.Screens.Play.HUDOverlay>().Single().ShowHud.Value = false);
            AddAssert("timing overlay still shown", () => player!.ChildrenOfType<FreeplayTimingOverlay>().Single().IsPresent);
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

            // The statistics page with a real run in the history.
            PushAndConfirm(() => new FreeplayStatsScreen(freeplayStorage));
            AddAssert("run listed", () => Game.ChildrenOfType<FreeplayStatsScreen>().Single().ChildrenOfType<ListRow>().Any());
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
