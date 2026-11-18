// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Stable;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// Picks random tracks from the osu!stable library or one of its collections, according to <see cref="TrackSelectionSettings"/>.
    /// </summary>
    public class TrackPicker
    {
        private readonly StableLibrary library;
        private readonly TrackSelectionSettings settings;
        private readonly Random rng = new Random();

        private readonly Queue<string> recentSets = new Queue<string>();

        /// <summary>
        /// From this generated star rating, faster songs are preferred.
        /// </summary>
        private const double fast_song_stars = 6;

        /// <summary>
        /// How often a fast song is picked when one is preferred, so slower songs still turn up.
        /// </summary>
        private const double fast_song_preference = 0.8;

        /// <summary>
        /// The BPM a song needs to reach <paramref name="stars"/> with fair patterns (1/2 jumps across the screen top out around 6-7★ at 140 BPM).
        /// </summary>
        public static double MinimumBpmFor(double stars) => 100 + 11 * stars;

        public TrackPicker(StableLibrary library, TrackSelectionSettings settings)
        {
            this.library = library;
            this.settings = settings;
        }

        /// <summary>
        /// Counts the sets the current settings allow.
        /// </summary>
        public int CountAvailable() => candidates().Count;

        /// <summary>
        /// Picks the next track, or null if nothing matches the settings.
        /// </summary>
        /// <remarks>
        /// One difficulty is chosen per set. By default the one with the most objects is chosen, as it offers the most detailed rhythm for the generator to thin out.
        /// </remarks>
        /// <param name="closestToStars">If set, choose the difficulty whose real star rating is closest to this instead (for remixing a mapper's difficulty as-is).</param>
        /// <param name="generatedStars">The star rating tracks will be generated at. High ratings prefer faster songs, as slow songs can't honestly get that hard.</param>
        public (StableSet set, StableDifficulty difficulty)? Next(double? closestToStars = null, double? generatedStars = null)
        {
            var sets = candidates();

            if (sets.Count == 0)
                return null;

            int noRepeat = Math.Min(settings.NoRepeatCount, sets.Count - 1);

            while (recentSets.Count > noRepeat)
                recentSets.Dequeue();

            var fresh = sets.Where(s => !recentSets.Contains(s.set.Folder)).ToList();

            if (generatedStars >= fast_song_stars && rng.NextDouble() < fast_song_preference)
            {
                double minBpm = MinimumBpmFor(generatedStars.Value);
                var fast = fresh.Where(s => s.difficulties.Max(d => d.Bpm) >= minBpm).ToList();

                // Only when the filters leave enough fast songs to keep runs varied.
                if (fast.Count >= 20)
                    fresh = fast;
            }
            var chosen = fresh[rng.Next(fresh.Count)];

            recentSets.Enqueue(chosen.set.Folder);

            if (closestToStars != null)
                return (chosen.set, chosen.difficulties.MinBy(d => Math.Abs(library.GetStarRating(chosen.set, d) - closestToStars.Value))!);

            return (chosen.set, chosen.difficulties.MaxBy(d => d.ObjectCount)!);
        }

        private List<(StableSet set, List<StableDifficulty> difficulties)> candidates()
        {
            HashSet<string>? hashes = null;

            if (settings.Source == TrackSource.Collection)
            {
                hashes = library.GetCollectionHashes(settings.CollectionName);

                if (hashes == null)
                    return new List<(StableSet, List<StableDifficulty>)>();
            }

            var result = new List<(StableSet, List<StableDifficulty>)>();

            foreach (var set in library.Sets)
            {
                var difficulties = set.Difficulties.Where(d => (hashes == null || hashes.Contains(d.MD5)) && matchesFilters(d)).ToList();

                if (difficulties.Count > 0)
                    result.Add((set, difficulties));
            }

            return result;
        }

        private bool matchesFilters(StableDifficulty difficulty)
        {
            double lengthSeconds = difficulty.LengthMs / 1000;

            if (settings.MinLengthSeconds > 0 && lengthSeconds < settings.MinLengthSeconds)
                return false;

            if (settings.MaxLengthSeconds > 0 && lengthSeconds > settings.MaxLengthSeconds)
                return false;

            if (settings.MinBpm > 0 && difficulty.Bpm < settings.MinBpm)
                return false;

            if (settings.MaxBpm > 0 && difficulty.Bpm > settings.MaxBpm)
                return false;

            return true;
        }
    }
}
