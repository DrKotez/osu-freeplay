// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    [TestFixture]
    public class FeedbackTest
    {
        private string directory = null!;
        private FreeplayStorage storage = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"freeplay-feedback-{Guid.NewGuid():N}");
            storage = new FreeplayStorage(new NativeStorage(directory));
        }

        [TearDown]
        public void TearDown()
        {
            FreeplayGenerationSettings.Current = new FreeplayGenerationSettings();

            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        [Test]
        public void TestFeedbackSavesMapsAndExports()
        {
            string path = FreeplayGenerationTest.sourceBeatmaps().First();
            var generated = FreeplayGenerationTest.generate(path, new FreeplayGenerationSettings { StarRating = 5, Style = PatternStyle.Jumps, Seed = 1 }, out _);

            var entry = new FeedbackEntry { Title = "Some: song?", Rating = FeedbackRating.TooEasy, Flags = { FeedbackFlag.BoringRhythm } };

            storage.SaveFeedback(entry, generated, path);
            Assert.That(entry.Folder, Is.Not.Null);
            Assert.That(storage.CountFeedback(), Is.EqualTo(1));

            string folder = Path.Combine(directory, "freeplay", FreeplayStorage.FEEDBACK_DIRECTORY, entry.Folder!);
            Assert.That(File.Exists(Path.Combine(folder, "feedback.json")));
            Assert.That(File.Exists(Path.Combine(folder, "source.osu")));

            // The generated map opens as an ordinary osu! map with the same objects.
            using (var reader = new LineBufferedReader(File.OpenRead(Path.Combine(folder, "generated.osu"))))
            {
                var decoded = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
                Assert.That(decoded.HitObjects.Count, Is.EqualTo(generated.HitObjects.Count));
                Assert.That(decoded.BeatmapInfo.Ruleset.OnlineID, Is.Zero);
            }

            // Marking more updates the same entry.
            entry.Flags.Add(FeedbackFlag.Monotonous);
            storage.SaveFeedback(entry, generated, path);
            Assert.That(storage.CountFeedback(), Is.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(folder, "feedback.json")), Does.Contain("Monotonous"));

            string? zip = storage.ExportFeedback();
            Assert.That(zip, Is.Not.Null);

            using (var archive = ZipFile.OpenRead(zip!))
                Assert.That(archive.Entries.Select(e => Path.GetFileName(e.FullName)), Is.SupersetOf(new[] { "feedback.json", "generated.osu", "source.osu" }));
        }

        [Test]
        public void TestNoFeedbackExportsNothing() => Assert.That(storage.ExportFeedback(), Is.Null);
    }
}
