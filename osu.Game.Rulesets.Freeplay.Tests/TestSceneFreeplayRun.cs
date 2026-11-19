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
            AddStep("short breaks between tracks", () => FreeplayRunScreen.AutoContinueDelay = 1500);
            // The test tracks are short: prepare the next one soon enough for it to be ready.
            AddStep("prepare the next track early", () => FreeplayRunScreen.PrepareDelay = 1000);
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
        public void TestAltScrollChangesVolume()
        {
            double before = 0;
            AudioManager audio() => Game.Dependencies.Get<AudioManager>();

            PushAndConfirm(() => new FreeplayMenuScreen());

            AddStep("set volume", () => audio().Volume.Value = 0.5);
            AddStep("alt + scroll up", () =>
            {
                before = audio().Volume.Value;
                InputManager.MoveMouseTo(Game.ScreenSpaceDrawQuad.Centre);
                InputManager.PressKey(osuTK.Input.Key.AltLeft);
                InputManager.ScrollVerticalBy(3);
                InputManager.ReleaseKey(osuTK.Input.Key.AltLeft);
            });
            AddUntilStep("volume went up", () => audio().Volume.Value, () => Is.GreaterThan(before + 0.01));
        }

        [Test]
        public void TestStableImportButtonKeepsVolumeWorking()
        {
            AudioManager audio() => Game.Dependencies.Get<AudioManager>();
            double before = 0;

            AddStep("write a stable config", () => File.WriteAllLines(Path.Combine(library.StablePath, "osu!.player.cfg"), new[] { "VolumeUniversal = 40", "VolumeMusic = 50", "VolumeEffect = 60" }));
            AddStep("set settings", () => FreeplaySettings.SetCurrent(new FreeplaySettings()));

            PushAndConfirm(() => new FreeplaySettingsScreen(freeplayStorage, library));

            AddStep("press import", () => Game.ChildrenOfType<FreeplayButton>().First(b => b.ChildrenOfType<osu.Game.Graphics.Sprites.OsuSpriteText>().Any(t => t.Text.ToString().Contains("osu!stable сейчас"))).TriggerClick());
            AddAssert("volume taken", () => audio().Volume.Value, () => Is.EqualTo(0.4).Within(0.001));

            AddStep("alt + scroll up", () =>
            {
                before = audio().Volume.Value;
                InputManager.MoveMouseTo(Game.ScreenSpaceDrawQuad.Centre);
                InputManager.PressKey(osuTK.Input.Key.AltLeft);
                InputManager.ScrollVerticalBy(3);
                InputManager.ReleaseKey(osuTK.Input.Key.AltLeft);
            });
            AddUntilStep("volume went up", () => audio().Volume.Value, () => Is.GreaterThan(before + 0.01));
            AddAssert("framework config agrees", () => Game.Dependencies.Get<osu.Framework.Configuration.FrameworkConfigManager>().Get<double>(osu.Framework.Configuration.FrameworkSetting.VolumeUniversal), () => Is.EqualTo(audio().Volume.Value).Within(0.001));
            AddAssert("slider agrees", () => Game.ChildrenOfType<osu.Framework.Graphics.UserInterface.SliderBar<double>>().Any(s => Math.Abs(s.Current.Value - audio().Volume.Value) < 0.001));
            AddAssert("mixer agrees", () => audio().AggregateVolume.Value, () => Is.EqualTo(audio().Volume.Value).Within(0.001));

            AddStep("leave settings", () => Game.ScreenStack.CurrentScreen.Exit());
            AddUntilStep("settings closed", () => Game.ScreenStack.CurrentScreen is not FreeplaySettingsScreen);
            AddStep("turn the volume meter down", () => Game.ChildrenOfType<osu.Game.Overlays.Volume.VolumeMeter>().First().Bindable.Value = 0.3);
            AddAssert("volume follows", () => audio().Volume.Value, () => Is.EqualTo(0.3).Within(0.001));
            AddAssert("still saved", () => Game.Dependencies.Get<osu.Framework.Configuration.FrameworkConfigManager>().Get<double>(osu.Framework.Configuration.FrameworkSetting.VolumeUniversal), () => Is.EqualTo(audio().Volume.Value).Within(0.001));

            AddStep("change cursor size", () => Game.Dependencies.Get<osu.Game.Configuration.OsuConfigManager>().GetBindable<float>(osu.Game.Configuration.OsuSetting.GameplayCursorSize).Value = 0.7f);
            AddAssert("cursor size still saved", () => Game.Dependencies.Get<osu.Game.Configuration.OsuConfigManager>().Get<float>(osu.Game.Configuration.OsuSetting.GameplayCursorSize), () => Is.EqualTo(0.7f).Within(0.001));
        }

        [Test]
        public void TestStableImportKeepsLocalChanges()
        {
            var settings = new FreeplaySettings();
            var config = (Func<osu.Game.Configuration.OsuConfigManager>)(() => Game.Dependencies.Get<osu.Game.Configuration.OsuConfigManager>());

            void import(string cursorSize)
            {
                string file = Path.Combine(Path.GetTempPath(), $"freeplay-stable-{Guid.NewGuid():N}.cfg");
                File.WriteAllText(file, $"CursorSize = {cursorSize}\n");

                new StableSettingsImport(config(), Game.Dependencies.Get<osu.Framework.Configuration.FrameworkConfigManager>(),
                    Game.Dependencies.Get<AudioManager>(), Game.Dependencies.Get<osu.Game.Database.RealmAccess>()).Apply(StableConfig.Read(file), settings);

                File.Delete(file);
            }

            AddStep("first import", () => import("1.2"));
            AddAssert("taken", () => config().Get<float>(osu.Game.Configuration.OsuSetting.GameplayCursorSize), () => Is.EqualTo(1.2f).Within(0.001));

            AddStep("change it here", () => config().SetValue(osu.Game.Configuration.OsuSetting.GameplayCursorSize, 0.8f));
            AddStep("start again, stable unchanged", () => import("1.2"));
            AddAssert("own change kept", () => config().Get<float>(osu.Game.Configuration.OsuSetting.GameplayCursorSize), () => Is.EqualTo(0.8f).Within(0.001));

            AddStep("change it in stable", () => import("1.5"));
            AddAssert("stable's change taken", () => config().Get<float>(osu.Game.Configuration.OsuSetting.GameplayCursorSize), () => Is.EqualTo(1.5f).Within(0.001));
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
        public void TestModsButtonBesidePlayOpensMods()
        {
            Screens.Controls.FreeplayButton button(string text) => Game.ChildrenOfType<Screens.Controls.FreeplayButton>().First(b => b.Text.StartsWith(text, StringComparison.Ordinal));

            PushAndConfirm(() => new FreeplayMenuScreen());
            AddStep("choose how to play", () => Game.ChildrenOfType<FreeplayMenuScreen>().Single().ChildrenOfType<MenuBar>().First(b => b.ChildrenOfType<osu.Game.Graphics.Sprites.OsuSpriteText>()
                                                                                                                                              .Any(t => t.Text == "Играть")).TriggerClick());
            AddUntilStep("left of play", () => button("Моды").ScreenSpaceDrawQuad.Centre.X < button("Играть").ScreenSpaceDrawQuad.TopLeft.X
                                               && Math.Abs(button("Моды").ScreenSpaceDrawQuad.Centre.Y - button("Играть").ScreenSpaceDrawQuad.Centre.Y) < 1);
            AddStep("click it", () =>
            {
                InputManager.MoveMouseTo(button("Моды"));
                InputManager.Click(osuTK.Input.MouseButton.Left);
            });
            AddUntilStep("mods page open", () => Game.ScreenStack.CurrentScreen is FreeplayModsScreen);
            AddStep("turn on hidden and go back", () =>
            {
                FreeplaySettings.Current.Mods = FreeplayModFlags.Hidden;
                Game.ScreenStack.CurrentScreen.Exit();
            });
            AddUntilStep("mods shown on the button", () => button("Моды").Text, () => Is.EqualTo("Моды: HD"));
        }

        [Test]
        public void TestAimSettingsPickGoalsAndNoticeFineTuning()
        {
            var aim = FreeplayMode.Find("aim")!;
            FreeplayGenerationSettings aimSettings() => FreeplaySettings.Current.GetMode(aim);
            T? find<T>(System.Func<T, bool> match) where T : osu.Framework.Graphics.Drawable
                => Game.ScreenStack.CurrentScreen is FreeplayModeSettingsScreen page ? page.ChildrenOfType<T>().FirstOrDefault(match) : null;
            bool hasText(osu.Framework.Graphics.Drawable d, string text) => d.ChildrenOfType<osu.Game.Graphics.Sprites.OsuSpriteText>().Any(t => t.Text == text);

            AddStep("set settings", () => FreeplaySettings.SetCurrent(new FreeplaySettings { PlayerLevel = 5 }));
            PushAndConfirm(() => new FreeplayModeSettingsScreen(freeplayStorage, library, aim));

            AddStep("click wide jumps", () =>
            {
                InputManager.MoveMouseTo(find<FreeplayModeSettingsScreen.GoalCard>(c => c.Goal == AimGoal.Wide)!);
                InputManager.Click(osuTK.Input.MouseButton.Left);
            });
            AddAssert("goal picked", () => aimSettings().Goal, () => Is.EqualTo(AimGoal.Wide));
            AddAssert("stars from level", () => aimSettings().StarRating, () => Is.EqualTo(4.2).Within(1e-9));
            AddAssert("fine tuning hidden", () => find<FreeplayCheckbox>(c => hasText(c, "Анти-джампы")), () => Is.Null);

            AddStep("open fine tuning", () => find<FreeplayButton>(b => hasText(b, "Показать тонкую настройку"))!.TriggerClick());
            AddUntilStep("fine tuning shown", () => find<FreeplayCheckbox>(c => hasText(c, "Анти-джампы")), () => Is.Not.Null);
            AddAssert("not edited yet", () => aimSettings().GoalEdited, () => Is.False);

            AddStep("toggle anti-jumps", () => find<FreeplayCheckbox>(c => hasText(c, "Анти-джампы"))!.TriggerClick());
            AddAssert("anti-jumps on", () => aimSettings().AntiJumps);
            AddAssert("goal marked edited", () => aimSettings().GoalEdited);
            AddAssert("shown as own", () => find<osu.Game.Graphics.Sprites.OsuSpriteText>(t => t.Text.ToString().StartsWith("Своя", System.StringComparison.Ordinal)), () => Is.Not.Null);

            AddStep("restore goal", () => find<FreeplayButton>(b => hasText(b, "Вернуть как в цели «Широкие прыжки»"))!.TriggerClick());
            AddAssert("goal restored", () => aimSettings().GoalEdited || aimSettings().AntiJumps, () => Is.False);
        }

        [Test]
        public void TestCompletedTrackContinuesToNext()
        {
            FreeplayPlayer? firstPlayer = null;

            AddStep("set settings", () => FreeplaySettings.SetCurrent(customFail(new FreeplaySettings
            {
                Tracks = { MinLengthSeconds = 0 },
                Fail = { Health = false },
            })));

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage, library, mixed));
            dismissNotifications();

            AddUntilStep("first track playing", () => (firstPlayer = Game.ScreenStack.CurrentScreen as FreeplayPlayer) != null);
            waitUntil("first track completed", () => firstPlayer!.GameplayState.HasPassed);

            // The run screen is only shown briefly between tracks, so go straight to checking for the next track.
            waitUntil("second track playing", () => Game.ScreenStack.CurrentScreen is FreeplayPlayer player && player != firstPlayer);
            AddAssert("nothing saved mid-run", () => freeplayStorage.LoadHistory(), () => Is.Empty);
        }

        [Test]
        public void TestMarkingATrackSavesItAndWaits()
        {
            FreeplayPlayer? firstPlayer = null;
            FeedbackPanel.Chip? chip() => Game.ChildrenOfType<FeedbackPanel.Chip>().FirstOrDefault(c => c.Text == "Скучный ритм");

            // Long enough to find the marks before the break ends by itself.
            AddStep("normal breaks", () => FreeplayRunScreen.AutoContinueDelay = 30_000);

            AddStep("set settings", () => FreeplaySettings.SetCurrent(customFail(new FreeplaySettings
            {
                Tracks = { MinLengthSeconds = 0 },
                Fail = { Health = false },
            })));

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage, library, mixed));
            dismissNotifications();

            AddUntilStep("first track playing", () => (firstPlayer = Game.ScreenStack.CurrentScreen as FreeplayPlayer) != null);
            waitUntil("feedback shown", () => chip() != null);

            AddStep("mark boring rhythm", () => chip()!.TriggerClick());
            AddAssert("saved", () => freeplayStorage.CountFeedback(), () => Is.EqualTo(1));

            AddStep("write something", () =>
            {
                Game.ChildrenOfType<FeedbackPanel.Chip>().First(c => c.Text == "Другое…").TriggerClick();
                Game.ChildrenOfType<FeedbackPanel>().First().Comment.Current.Value = "слишком много треугольников";
            });
            AddAssert("comment saved", () => Directory.GetFiles(Game.Storage.GetFullPath("freeplay/feedback"), "feedback.json", SearchOption.AllDirectories)
                                                      .Any(f => File.ReadAllText(f).Contains("слишком много треугольников")));

            Stopwatch waited = null!;
            AddStep("start waiting", () => waited = Stopwatch.StartNew());
            AddUntilStep("wait past the usual break", () => waited.ElapsedMilliseconds > 3000);
            AddAssert("still between tracks", () => Game.ScreenStack.CurrentScreen is FreeplayRunScreen);
            AddStep("leave the comment box", () => InputManager.Key(osuTK.Input.Key.Escape));

            // The key the player hits notes with goes on too (Z by default).
            AddStep("press the note key", () => InputManager.Key(osuTK.Input.Key.Z));
            AddUntilStep("next track loading", () => Game.ScreenStack.CurrentScreen is not FreeplayRunScreen);
        }

        [Test]
        public void TestEscapeBetweenTracksPauses()
        {
            FreeplayPlayer? firstPlayer = null;
            FreeplayRunScreen runScreen = null!;

            AddStep("normal breaks", () => FreeplayRunScreen.AutoContinueDelay = 30_000);
            AddStep("set settings", () => FreeplaySettings.SetCurrent(customFail(new FreeplaySettings
            {
                Tracks = { MinLengthSeconds = 0 },
                Fail = { Health = false },
            })));

            PushAndConfirm(() => runScreen = new FreeplayRunScreen(freeplayStorage, library, mixed));
            dismissNotifications();

            AddUntilStep("first track playing", () => (firstPlayer = Game.ScreenStack.CurrentScreen as FreeplayPlayer) != null);
            waitUntil("between tracks", () => Game.ScreenStack.CurrentScreen is FreeplayRunScreen && Game.ChildrenOfType<FeedbackPanel>().Any());

            AddStep("press escape", () => InputManager.Key(osuTK.Input.Key.Escape));
            AddAssert("paused, still in the run", () => runScreen.Paused && Game.ScreenStack.CurrentScreen == runScreen);
            AddStep("press the note key", () => InputManager.Key(osuTK.Input.Key.Z));
            AddAssert("no track while paused", () => Game.ScreenStack.CurrentScreen, () => Is.SameAs(runScreen));

            AddStep("escape again", () => InputManager.Key(osuTK.Input.Key.Escape));
            AddAssert("resumed", () => runScreen.Paused, () => Is.False);

            AddStep("pause", () => InputManager.Key(osuTK.Input.Key.Escape));
            AddStep("leave to the menu", () => Game.ChildrenOfType<Screens.Controls.FreeplayButton>().First(b => b.ChildrenOfType<Game.Graphics.Sprites.OsuSpriteText>()
                                                                                                                 .Any(t => t.Text == "Выйти в меню")).TriggerClick());
            AddUntilStep("left the run", () => Game.ScreenStack.CurrentScreen is not FreeplayRunScreen);
            AddUntilStep("run recorded", () => freeplayStorage.LoadHistory().SingleOrDefault()?.EndReason == RunEndReason.Quit);
        }

        [Test]
        public void TestEscapeOnLoaderPausesAndPlaysTheSameTrack()
        {
            FreeplayRunScreen runScreen = null!;
            string? title = null;

            AddStep("set settings", () => FreeplaySettings.SetCurrent(new FreeplaySettings { Tracks = { MinLengthSeconds = 0 } }));

            PushAndConfirm(() => runScreen = new FreeplayRunScreen(freeplayStorage, library, mixed));

            AddUntilStep("loader shown", () => Game.ScreenStack.CurrentScreen is FreeplayPlayerLoader);
            AddStep("leave the loader", () =>
            {
                title = Game.Beatmap.Value.BeatmapInfo.DifficultyName;
                Game.ScreenStack.CurrentScreen.Exit();
            });

            AddUntilStep("paused in the run", () => Game.ScreenStack.CurrentScreen == runScreen && runScreen.Paused);
            AddAssert("nothing recorded", () => freeplayStorage.LoadHistory(), () => Is.Empty);

            AddStep("resume", () => InputManager.Key(osuTK.Input.Key.Escape));
            AddUntilStep("same track loading", () => Game.ScreenStack.CurrentScreen is FreeplayPlayerLoader && Game.Beatmap.Value.BeatmapInfo.DifficultyName == title);
        }

        [Test]
        public void TestRunPlaysWithOsuMods()
        {
            AddStep("set settings", () => FreeplaySettings.SetCurrent(new FreeplaySettings
            {
                Tracks = { MinLengthSeconds = 0 },
                Mods = FreeplayModFlags.HardRock | FreeplayModFlags.NoFail | FreeplayModFlags.SingleTap | FreeplayModFlags.Notelock | FreeplayModFlags.Hidden,
            }));

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage, library, FreeplayMode.Find("stars")!));
            dismissNotifications();

            AddUntilStep("track playing", () => Game.ScreenStack.CurrentScreen is FreeplayPlayer);
            AddAssert("osu!'s own mods", () => ((FreeplayPlayer)Game.ScreenStack.CurrentScreen).GameplayState.Mods.Select(m => m.GetType()).ToHashSet().SetEquals(new[]
            {
                typeof(osu.Game.Rulesets.Osu.Mods.OsuModHardRock), typeof(osu.Game.Rulesets.Osu.Mods.OsuModNoFail),
                typeof(osu.Game.Rulesets.Osu.Mods.OsuModSingleTap), typeof(osu.Game.Rulesets.Osu.Mods.OsuModClassic), typeof(osu.Game.Rulesets.Osu.Mods.OsuModHidden),
            }));
            AddAssert("hard rock's circles", () => ((FreeplayPlayer)Game.ScreenStack.CurrentScreen).GameplayState.Beatmap.Difficulty.CircleSize > 4.5f);

            AddStep("quit", () => Game.ScreenStack.CurrentScreen.Exit());
            AddUntilStep("mods recorded", () => freeplayStorage.LoadHistory().SingleOrDefault()?.Mods, () => Is.EqualTo("Singletap · No Fail · Hard Rock · Hidden · Notelock"));
        }

        [Test]
        public void TestRelaxAndSingletapExcludeEachOther()
        {
            AddAssert("relax turns singletap off", () => FreeplayMods.With(FreeplayModFlags.SingleTap | FreeplayModFlags.HardRock, FreeplayModFlags.Relax),
                () => Is.EqualTo(FreeplayModFlags.Relax | FreeplayModFlags.HardRock));
            AddAssert("singletap turns relax off", () => FreeplayMods.With(FreeplayModFlags.Relax, FreeplayModFlags.SingleTap), () => Is.EqualTo(FreeplayModFlags.SingleTap));
        }

        [Test]
        public void TestStarsModePlays()
        {
            AddStep("set settings", () => FreeplaySettings.SetCurrent(customFail(new FreeplaySettings
            {
                Tracks = { MinLengthSeconds = 0 },
                Fail = { Health = false },
            })));

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage, library, FreeplayMode.Find("stars")!));
            dismissNotifications();

            AddUntilStep("track playing", () => Game.ScreenStack.CurrentScreen is FreeplayPlayer);
            AddAssert("only circles on the grid", () =>
            {
                var objects = ((FreeplayPlayer)Game.ScreenStack.CurrentScreen).GameplayState.Beatmap.HitObjects;
                return objects.Count > 10 && objects.All(h => h is osu.Game.Rulesets.Osu.Objects.HitCircle);
            });

            // Nobody clicks here: once a few notes have gone by, the analysis has them as not clicked.
            AddUntilStep("a few notes missed", () => ((FreeplayPlayer)Game.ScreenStack.CurrentScreen).GameplayState.ScoreProcessor.HitEvents.Count >= 3);
            AddStep("quit", () => Game.ScreenStack.CurrentScreen.Exit());
            AddUntilStep("run summary", () => Game.ChildrenOfType<Screens.Controls.AimAnalysisPanel>().Any());
            AddAssert("misses analysed", () =>
            {
                var aim = freeplayStorage.LoadHistory().Single().Tracks.Single().Aim!;
                return aim.Notes >= 3 && aim.NoClickMisses == aim.Notes;
            });
        }

        [Test]
        public void TestStarsRampEndsWhenNothingIsFaster()
        {
            AddStep("set settings", () =>
            {
                var settings = customFail(new FreeplaySettings
                {
                    Tracks = { MinLengthSeconds = 0 },
                    Fail = { Health = false },
                });

                settings.GetMode(FreeplayMode.Find("stars")!).Ramp = true;
                FreeplaySettings.SetCurrent(settings);
            });

            PushAndConfirm(() => new FreeplayRunScreen(freeplayStorage, library, FreeplayMode.Find("stars")!));
            dismissNotifications();

            AddUntilStep("track playing", () => Game.ScreenStack.CurrentScreen is FreeplayPlayer);

            // The library has one song: after it, there's nothing faster.
            waitUntil("ramp done", () => Game.ChildrenOfType<osu.Game.Graphics.Sprites.OsuSpriteText>().Any(t => t.Text.ToString().Contains("Разгон пройден")));
            AddAssert("shows how far it got", () => Game.ChildrenOfType<osu.Game.Graphics.Sprites.OsuSpriteText>().Any(t => t.Text.ToString().StartsWith("Дошёл до", StringComparison.Ordinal)));
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
            AddStep("set settings", () => FreeplaySettings.SetCurrent(customFail(new FreeplaySettings
            {
                Tracks = { MinLengthSeconds = 0 },
                Fail = { Health = false, MissLimit = 2 },
            })));

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

        /// <summary>
        /// Makes every mode use the settings' own fail conditions rather than its fail level.
        /// </summary>
        private static FreeplaySettings customFail(FreeplaySettings settings)
        {
            foreach (var mode in FreeplayMode.ALL)
                settings.GetMode(mode).FailLevel = FailLevel.Custom;

            return settings;
        }

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
