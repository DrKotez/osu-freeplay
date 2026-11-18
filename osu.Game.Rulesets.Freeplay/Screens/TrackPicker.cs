// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Rulesets.Freeplay.Configuration;
using Realms;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// Picks random tracks from the library or a collection, according to <see cref="TrackSelectionSettings"/>.
    /// </summary>
    public class TrackPicker
    {
        private readonly RealmAccess realm;
        private readonly TrackSelectionSettings settings;
        private readonly Random rng = new Random();

        private readonly Queue<Guid> recentSets = new Queue<Guid>();

        public TrackPicker(RealmAccess realm, TrackSelectionSettings settings)
        {
            this.realm = realm;
            this.settings = settings;
        }

        /// <summary>
        /// Counts the beatmap sets the current settings allow.
        /// </summary>
        public int CountAvailable() => realm.Run(r => candidateSets(r).Count);

        /// <summary>
        /// Picks the next track, or null if nothing matches the settings.
        /// </summary>
        /// <remarks>
        /// One difficulty is chosen per set. The hardest osu! difficulty is preferred, as it offers the most detailed rhythm for the generator to thin out.
        /// </remarks>
        public BeatmapInfo? Next()
        {
            return realm.Run(r =>
            {
                var sets = candidateSets(r);

                if (sets.Count == 0)
                    return null;

                int noRepeat = Math.Min(settings.NoRepeatCount, sets.Count - 1);

                while (recentSets.Count > noRepeat)
                    recentSets.Dequeue();

                var fresh = sets.Where(s => !recentSets.Contains(s.Key)).ToList();
                var chosen = fresh[rng.Next(fresh.Count)];

                recentSets.Enqueue(chosen.Key);

                return chosen.OrderByDescending(b => b.Ruleset.OnlineID == 0)
                             .ThenByDescending(b => b.StarRating)
                             .First()
                             .Detach();
            });
        }

        private List<IGrouping<Guid, BeatmapInfo>> candidateSets(Realm r)
        {
            // Protected sets are bundled with the game (e.g. the intro track) and have no hit objects to take rhythm from.
            IEnumerable<BeatmapInfo> beatmaps = r.All<BeatmapInfo>().Filter("BeatmapSet.DeletePending == false && BeatmapSet.Protected == false && Hidden == false && TotalObjectCount != 0");

            if (settings.Source == TrackSource.Collection)
            {
                var collection = settings.CollectionID == null ? null : r.Find<BeatmapCollection>(settings.CollectionID.Value);

                if (collection == null)
                    return new List<IGrouping<Guid, BeatmapInfo>>();

                var hashes = collection.BeatmapMD5Hashes.ToHashSet();
                beatmaps = beatmaps.Where(b => hashes.Contains(b.MD5Hash));
            }

            beatmaps = beatmaps.Where(b => b.BeatmapSet != null && matchesFilters(b));

            return beatmaps.GroupBy(b => b.BeatmapSet!.ID).ToList();
        }

        private bool matchesFilters(BeatmapInfo beatmap)
        {
            double lengthSeconds = beatmap.Length / 1000;

            if (settings.MinLengthSeconds > 0 && lengthSeconds < settings.MinLengthSeconds)
                return false;

            if (settings.MaxLengthSeconds > 0 && lengthSeconds > settings.MaxLengthSeconds)
                return false;

            if (settings.MinBpm > 0 && beatmap.BPM < settings.MinBpm)
                return false;

            if (settings.MaxBpm > 0 && beatmap.BPM > settings.MaxBpm)
                return false;

            return true;
        }
    }
}
