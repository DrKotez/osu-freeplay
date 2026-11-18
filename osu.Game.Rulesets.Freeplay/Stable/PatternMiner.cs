// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Logging;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;

namespace osu.Game.Rulesets.Freeplay.Stable
{
    /// <summary>
    /// Builds a <see cref="MinedPatternSet"/> from the beatmaps in the osu!stable collections chosen as pattern sources.
    /// </summary>
    public static class PatternMiner
    {
        /// <summary>
        /// Caps the work per collection, as decoding is the slow part.
        /// </summary>
        private const int max_beatmaps_per_collection = 300;

        public static MinedPatternSet Mine(StableLibrary library, PatternSourceSettings sources, RulesetInfo osuRuleset, CancellationToken cancellationToken = default)
        {
            var result = new MinedPatternSet();

            var byHash = new Dictionary<string, (StableSet set, StableDifficulty difficulty)>();

            foreach (var set in library.Sets)
            {
                foreach (var difficulty in set.Difficulties)
                    byHash.TryAdd(difficulty.MD5, (set, difficulty));
            }

            foreach (var (category, collection) in new[]
                     {
                         (PatternCategory.Aim, sources.Aim),
                         (PatternCategory.Streams, sources.Streams),
                         (PatternCategory.Tech, sources.Tech),
                     })
            {
                var hashes = library.GetCollectionHashes(collection);

                if (hashes == null)
                    continue;

                int mined = 0;

                foreach (string hash in hashes.Take(max_beatmaps_per_collection))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!byHash.TryGetValue(hash, out var entry))
                        continue;

                    try
                    {
                        var playable = library.GetWorkingBeatmap(entry.set, entry.difficulty).GetPlayableBeatmap(osuRuleset);
                        result.Add(category, playable);
                        mined++;
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"Skipping pattern source {entry.difficulty.File}: {e.Message}");
                    }
                }

                Logger.Log($"Freeplay mined {category} patterns from {mined} beatmaps in \"{collection}\" ({result.Jumps.GetValueOrDefault(category)?.Count ?? 0} jump patterns).");
            }

            Logger.Log($"Freeplay pattern library: {result.Count} patterns, {result.Streams.Count} streams.");
            return result;
        }
    }
}
