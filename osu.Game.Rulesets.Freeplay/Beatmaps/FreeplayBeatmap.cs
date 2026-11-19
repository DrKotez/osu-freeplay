// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Osu.Beatmaps;

namespace osu.Game.Rulesets.Freeplay.Beatmaps
{
    public class FreeplayBeatmap : OsuBeatmap
    {
        /// <summary>
        /// The settings this beatmap was generated with.
        /// </summary>
        public FreeplayGenerationSettings Settings { get; set; } = new FreeplayGenerationSettings();

        /// <summary>
        /// The seed this beatmap was generated with. Generating again with the same seed and settings gives the same objects.
        /// </summary>
        public int Seed { get; set; }

        /// <summary>
        /// The osu! star rating of the generated objects (zero if it wasn't calculated).
        /// </summary>
        public double StarRating { get; set; }

        /// <summary>
        /// The objects as generated (before any mod changed them), packed for a seed code (see <see cref="Configuration.SeedMap"/>).
        /// </summary>
        public byte[]? PackedObjects { get; set; }

        /// <summary>
        /// The difficulty as generated (before any mod changed it).
        /// </summary>
        public osu.Game.Beatmaps.BeatmapDifficulty? GeneratedDifficulty { get; set; }
    }
}
