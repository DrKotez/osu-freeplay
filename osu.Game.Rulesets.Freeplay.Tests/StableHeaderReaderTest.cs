// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Extensions;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Freeplay.Stable;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    [TestFixture]
    public class StableHeaderReaderTest
    {
        [Test]
        public void TestHeaderMatchesFullDecode()
        {
            // The loose test beatmaps have their metadata stripped, so use one from the test archive.
            string archive = Path.Combine(FreeplayGenerationTest.RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "Resources", "Archives", "quick.osz");
            string folder = Path.Combine(Path.GetTempPath(), $"freeplay-header-{Guid.NewGuid()}");
            System.IO.Compression.ZipFile.ExtractToDirectory(archive, folder);

            string path = Directory.GetFiles(folder, "*[Insane].osu").Single();

            var header = StableHeaderReader.Read(path, Path.GetFileName(path))!;
            Assert.That(header, Is.Not.Null);

            Beatmap decoded;
            using (var stream = new LineBufferedReader(File.OpenRead(path)))
                decoded = Decoder.GetDecoder<Beatmap>(stream).Decode(stream);

            Assert.That(header.Title, Is.EqualTo(decoded.Metadata.Title));
            Assert.That(header.Artist, Is.EqualTo(decoded.Metadata.Artist));
            Assert.That(header.Version, Is.EqualTo(decoded.BeatmapInfo.DifficultyName));
            Assert.That(header.AudioFile, Is.EqualTo(decoded.Metadata.AudioFile));
            Assert.That(header.ObjectCount, Is.EqualTo(decoded.HitObjects.Count));
            Assert.That(header.Bpm, Is.EqualTo(60000 / decoded.GetMostCommonBeatLength()).Within(0.5));

            using (var stream = File.OpenRead(path))
                Assert.That(header.MD5, Is.EqualTo(stream.ComputeMD5Hash()));
        }

        /// <summary>
        /// Reads every .osu file in the local osu!stable install, to check timing and that nothing throws.
        /// </summary>
        [Explicit]
        [Test]
        public void ScanLocalStableInstall()
        {
            string songs = Path.Combine(StableLibrary.DEFAULT_PATH, "Songs");

            if (!Directory.Exists(songs))
                Assert.Ignore("No osu!stable install found.");

            var stopwatch = Stopwatch.StartNew();

            var files = Directory.GetDirectories(songs).SelectMany(d => Directory.EnumerateFiles(d, "*.osu")).ToList();
            TestContext.Out.WriteLine($"listed {files.Count} files in {stopwatch.Elapsed.TotalSeconds:0.0}s");

            var read = new ConcurrentBag<StableDifficulty>();
            int failed = 0;

            Parallel.ForEach(files, file =>
            {
                try
                {
                    var difficulty = StableHeaderReader.Read(file, Path.GetFileName(file));
                    if (difficulty != null)
                        read.Add(difficulty);
                }
                catch
                {
                    System.Threading.Interlocked.Increment(ref failed);
                }
            });

            TestContext.Out.WriteLine($"read {read.Count} osu! difficulties ({failed} failed) in {stopwatch.Elapsed.TotalSeconds:0.0}s");
            TestContext.Out.WriteLine($"no background: {read.Count(d => d.BackgroundFile.Length == 0)}, BPM range {read.Min(d => d.Bpm):0}-{read.Max(d => d.Bpm):0}");

            Assert.That(read, Is.Not.Empty);
        }
    }
}
