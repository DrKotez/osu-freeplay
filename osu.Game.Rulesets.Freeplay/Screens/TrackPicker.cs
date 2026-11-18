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
        /// One difficulty is chosen per set. The one with the most objects is preferred, as it offers the most detailed rhythm for the generator to thin out.
        /// </remarks>
        public (StableSet set, StableDifficulty difficulty)? Next()
        {
            var sets = candidates();

            if (sets.Count == 0)
                return null;

            int noRepeat = Math.Min(settings.NoRepeatCount, sets.Count - 1);

            while (recentSets.Count > noRepeat)
                recentSets.Dequeue();

            var fresh = sets.Where(s => !recentSets.Contains(s.set.Folder)).ToList();
            var chosen = fresh[rng.Next(fresh.Count)];

            recentSets.Enqueue(chosen.set.Folder);

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
