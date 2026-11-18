// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Rulesets.Freeplay.Stable;
using osu.Game.Rulesets.Osu;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    [TestFixture]
    public class StableWorkingBeatmapTest
    {
        /// <summary>
        /// osu! keeps the previous track playing when it thinks the next beatmap has the same audio,
        /// so stable beatmaps must only look alike when they really share a file.
        /// </summary>
        [Test]
        public void TestAudioOnlySharedWithinSameFolder()
        {
            var first = create("1 Artist - Song", "audio.mp3", "bg.jpg");
            var sameSet = create("1 Artist - Song", "audio.mp3", "bg.jpg");
            var otherSet = create("2 Other - Song", "audio.mp3", "bg.jpg");
            var otherAudio = create("1 Artist - Song", "other.mp3", "bg2.jpg");

            Assert.That(first.BeatmapInfo.AudioEquals(sameSet.BeatmapInfo), Is.True);
            Assert.That(first.BeatmapInfo.AudioEquals(otherSet.BeatmapInfo), Is.False);
            Assert.That(first.BeatmapInfo.AudioEquals(otherAudio.BeatmapInfo), Is.False);

            Assert.That(first.BeatmapInfo.BackgroundEquals(sameSet.BeatmapInfo), Is.True);
            Assert.That(first.BeatmapInfo.BackgroundEquals(otherSet.BeatmapInfo), Is.False);
        }

        private static StableWorkingBeatmap create(string folder, string audio, string background)
        {
            var set = new StableSet { Folder = folder };
            var difficulty = new StableDifficulty { File = "map.osu", AudioFile = audio, BackgroundFile = background };

            return new StableWorkingBeatmap(set, difficulty, null!, null!, null!, null!, new OsuRuleset().RulesetInfo);
        }
    }
}
