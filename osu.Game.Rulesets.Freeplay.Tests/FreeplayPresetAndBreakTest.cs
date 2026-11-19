// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Freeplay.Screens;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    [TestFixture]
    public class FreeplayPresetAndBreakTest
    {
        /// <summary>
        /// A preset keeps all of a mode's settings: changed ones come back when it's chosen, and it's saved with the settings.
        /// </summary>
        [Test]
        public void TestPresetsSaveAndRestoreModeSettings()
        {
            var mode = FreeplayMode.Find("stars")!;
            var settings = new FreeplaySettings();

            var stars = settings.GetMode(mode);
            stars.StarRating = 6.3;
            stars.MetronomeShapes = MetronomeShapes.Star | MetronomeShapes.Square;
            stars.WeakZone = WeakZone.BottomLeft;
            settings.SavePreset(mode, " Тренировка ");

            Assert.That(settings.ActivePreset(mode)?.Name, Is.EqualTo("Тренировка"));

            settings.GetMode(mode).StarRating = 4;
            Assert.That(settings.ActivePreset(mode), Is.Null, "changed since");

            string path = Path.Combine(Path.GetTempPath(), $"freeplay-presets-{Guid.NewGuid()}");

            try
            {
                var storage = new FreeplayStorage(new NativeStorage(path));
                storage.SaveSettings(settings);
                settings = storage.LoadSettings();
            }
            finally
            {
                Directory.Delete(path, true);
            }

            var restored = settings.ApplyPreset(mode, settings.PresetsOf(mode).Single());

            Assert.That(restored.StarRating, Is.EqualTo(6.3));
            Assert.That(restored.MetronomeShapes, Is.EqualTo(MetronomeShapes.Star | MetronomeShapes.Square));
            Assert.That(restored.WeakZone, Is.EqualTo(WeakZone.BottomLeft));
            Assert.That(settings.ActivePreset(mode)?.Name, Is.EqualTo("Тренировка"));

            // Changing the mode doesn't change the preset.
            restored.StarRating = 3;
            Assert.That(settings.PresetsOf(mode).Single().Settings.StarRating, Is.EqualTo(6.3));

            // The same name replaces it.
            settings.SavePreset(mode, "Тренировка");
            Assert.That(settings.PresetsOf(mode).Single().Settings.StarRating, Is.EqualTo(3));

            settings.DeletePreset(mode, settings.PresetsOf(mode).Single());
            Assert.That(settings.PresetsOf(mode), Is.Empty);
        }

        /// <summary>
        /// A seed file dropped on the window lands in the player's seeds (once), named after the file; other files are left to osu!.
        /// </summary>
        [Test]
        public void TestDroppedSeedFileIsAdded()
        {
            string path = Path.Combine(Path.GetTempPath(), $"freeplay-drop-{Guid.NewGuid()}");
            Directory.CreateDirectory(path);

            try
            {
                var storage = new FreeplayStorage(new NativeStorage(path));
                var seed = new SeedMap { Song = { MD5 = "abc", Artist = "a", Title = "t" }, Objects = SeedMap.Pack(new[] { new HitCircle { StartTime = 1000 } }) };

                string file = Path.Combine(path, "от друга" + SeedMap.FILE_EXTENSION);
                File.WriteAllText(file, seed.ToCode());

                Assert.That(Stable.FreeplayDrops.Handle(file, storage, null!), Is.True);
                Assert.That(Stable.FreeplayDrops.Handle(file, storage, null!), Is.True);
                Assert.That(storage.LoadSeeds().Single().Name, Is.EqualTo("от друга"));
                Assert.That(storage.LoadSeeds().Single().Map!.Unpack().Single().StartTime, Is.EqualTo(1000));

                string other = Path.Combine(path, "map.osk");
                File.WriteAllText(other, "skin");
                Assert.That(Stable.FreeplayDrops.Handle(other, storage, null!), Is.False);
            }
            finally
            {
                Directory.Delete(path, true);
            }
        }

        /// <summary>
        /// A break is skipped to a second before the next note starts to appear, only from inside the break, and the skipped time is counted.
        /// </summary>
        [Test]
        public void TestBreakSkip()
        {
            var difficulty = new BeatmapDifficulty { ApproachRate = 9 };
            var objects = new List<OsuHitObject>();

            foreach (double time in new double[] { 1000, 2000, 20000, 21000 })
            {
                var circle = new HitCircle { StartTime = time };
                circle.ApplyDefaults(new ControlPointInfo(), difficulty);
                objects.Add(circle);
            }

            double now = 1500;
            double? seekedTo = null;
            var skip = new FreeplayBreakSkip(new[] { new BreakPeriod(2500, 19000) }, objects, () => now, t => seekedTo = now = t);

            Assert.That(skip.Target, Is.Null, "not in a break");

            now = 5000;
            double expected = 20000 - objects[2].TimePreempt - FreeplayBreakSkip.LEAD_TIME;
            Assert.That(skip.Target, Is.EqualTo(expected));

            skip.Skip();
            Assert.That(seekedTo, Is.EqualTo(expected));
            Assert.That(skip.SkippedMs, Is.EqualTo(expected - 5000));
            Assert.That(skip.Target, Is.Null, "nothing left to skip");
        }
    }
}
