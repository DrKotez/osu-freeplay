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
        private readonly Generation.FreeplayGenerationSettings mode;
        private readonly Random rng = new Random();

        private readonly Queue<string> recentSets = new Queue<string>();

        /// <summary>
        /// From this generated star rating, faster songs are preferred.
        /// </summary>
        private const double fast_song_stars = 6;

        /// <summary>
        /// From this generated star rating, songs far too slow to reach it (a 90 BPM ballad at 6★ ends up a 4★ map) are left out.
        /// </summary>
        private const double slow_song_stars = 4.5;

        /// <summary>
        /// How often a fast song is picked when one is preferred, so slower songs still turn up.
        /// </summary>
        private const double fast_song_preference = 0.8;

        /// <summary>
        /// The BPM a song needs to reach <paramref name="stars"/> with fair patterns (1/2 jumps across the screen top out around 6-7★ at 140 BPM).
        /// </summary>
        public static double MinimumBpmFor(double stars) => 100 + 11 * stars;

        /// <summary>
        /// About the stars <paramref name="difficulty"/>'s song needs for the pp asked (see <see cref="Generation.FreeplayGenerationSettings.UsePerformance"/>), at the speed picked for.
        /// </summary>
        public double StarsFor(StableDifficulty difficulty)
            => Generation.PerformanceTarget.StarsFor(mode.TargetPerformance, difficulty.LengthMs / 1000 / speed, mode.Style);

        /// <param name="playerSpeed">The player's Double Time speed (1 without it): tracks are picked by their BPM and length at it.</param>
        public TrackPicker(StableLibrary library, TrackSelectionSettings settings, Generation.FreeplayGenerationSettings mode, double playerSpeed = 1)
        {
            this.library = library;
            this.settings = settings;
            this.mode = mode;
            this.playerSpeed = playerSpeed;
        }

        private readonly double playerSpeed;

        /// <summary>
        /// Counts the sets the current settings allow.
        /// </summary>
        public int CountAvailable()
        {
            // (The library may have changed since the last pick.)
            cachedCandidates = null;
            chooseSpeed();
            return candidates().Count;
        }

        /// <summary>
        /// The speed the last track picked is to be played at: 1, or Double Time's when no track fit the BPM range at its own speed
        /// (see <see cref="Generation.FreeplayGenerationSettings.AutoDoubleTime"/>).
        /// </summary>
        public double LastSpeed { get; private set; } = 1;

        /// <summary>
        /// The speed tracks are being picked for (their BPM and length counted at it).
        /// </summary>
        private double speed = 1;

        private void chooseSpeed()
        {
            speed = playerSpeed;

            // The game's Double Time only without the player's.
            if (playerSpeed > 1 || !mode.AutoDoubleTime || (mode.MinBpm <= 0 && mode.MaxBpm <= 0) || candidates().Count > 0)
                return;

            speed = Generation.FreeplayGenerationSettings.AUTO_DOUBLE_TIME_SPEED;

            if (candidates().Count == 0)
                speed = 1;
        }

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
            // Modes that need one tempo throughout check maps indexed before that was recorded as they come up.
            cachedCandidates = null;
            chooseSpeed();
            LastSpeed = speed;

            for (int attempt = 0; attempt < 50; attempt++)
            {
                var next = pick(closestToStars, generatedStars);

                if (next == null || !singleBpmOnly || next.Value.difficulty.SingleBpm == true)
                    return next;

                var (set, difficulty) = next.Value;

                difficulty.SingleBpm ??= StableHeaderReader.Read(System.IO.Path.Combine(library.SongsPath, set.Folder, difficulty.File), difficulty.File)?.SingleBpm ?? false;

                if (difficulty.SingleBpm == true)
                    return next;

                dropDifficulty(set, difficulty);
            }

            return null;
        }

        /// <summary>
        /// For a run that speeds up: one of the slowest tracks to start, then one a little faster than <paramref name="afterBpm"/> (the next few up, at random).
        /// Null when there's nothing faster.
        /// </summary>
        public (StableSet set, StableDifficulty difficulty)? NextFaster(double? afterBpm)
        {
            // (Not a BPM range: no Double Time of the game's, only the player's.)
            speed = LastSpeed = playerSpeed;
            cachedCandidates = null;

            for (int attempt = 0; attempt < 50; attempt++)
            {
                var options = candidates().Select(c => (c.set, difficulty: c.difficulties.MaxBy(d => d.ObjectCount)!))
                                          .Where(c => c.difficulty.SingleBpm != false && !recentSets.Contains(c.set.Folder))
                                          .OrderBy(c => c.difficulty.Bpm)
                                          .ToList();

                if (afterBpm != null)
                    options = options.Where(c => c.difficulty.Bpm >= afterBpm + 2).ToList();
                else
                {
                    // The start: slow, but not so slow the chosen stars can't be reached on it.
                    // (Shapes only grow so big, so stars need the speed: the full minimum, not the 80% other modes start from.)
                    var fastEnough = options.Where(c => c.difficulty.Bpm * speed >= MinimumBpmFor(mode.StarRating)).ToList();

                    if (fastEnough.Count >= 3)
                        options = fastEnough;
                }

                if (options.Count == 0)
                    return null;

                // The slowest fifth to start; after that the next few up, so the speed climbs a few BPM at a time.
                var near = afterBpm == null ? options.Take(Math.Max(3, options.Count / 5)).ToList() : options.TakeWhile((c, i) => i < 5 || c.difficulty.Bpm <= options[0].difficulty.Bpm + 5).Take(12).ToList();
                var (set, difficulty) = near[rng.Next(near.Count)];

                difficulty.SingleBpm ??= StableHeaderReader.Read(System.IO.Path.Combine(library.SongsPath, set.Folder, difficulty.File), difficulty.File)?.SingleBpm ?? false;

                if (difficulty.SingleBpm != true)
                {
                    dropDifficulty(set, difficulty);
                    continue;
                }

                recentSets.Enqueue(set.Folder);
                return (set, difficulty);
            }

            return null;
        }

        /// <summary>
        /// Whether the mode only plays maps with one BPM throughout (its jumps follow the tempo exactly).
        /// </summary>
        private bool singleBpmOnly => mode.Style == Generation.PatternStyle.Metronome;

        private (StableSet set, StableDifficulty difficulty)? pick(double? closestToStars, double? generatedStars)
        {
            var sets = candidates();

            if (sets.Count == 0)
                return null;

            int noRepeat = Math.Min(settings.NoRepeatCount, sets.Count - 1);

            while (recentSets.Count > noRepeat)
                recentSets.Dequeue();

            var fresh = sets.Where(s => !recentSets.Contains(s.set.Folder)).ToList();

            // A pp target: only tracks whose length takes stars within the limits for it (a short track needs more stars than a long one),
            // and fast enough for those stars.
            if (mode.UsePerformance && closestToStars == null)
            {
                double low = Math.Min(mode.StarRating, mode.StarRatingMax ?? mode.StarRating), high = Math.Max(mode.StarRating, mode.StarRatingMax ?? mode.StarRating);

                var fitting = fresh.Where(s =>
                {
                    var d = s.difficulties.MaxBy(x => x.ObjectCount)!;
                    double stars = StarsFor(d);
                    return stars >= low - 0.3 && stars <= high + 0.3 && (stars < slow_song_stars || d.Bpm * speed >= MinimumBpmFor(stars) * 0.8);
                }).ToList();

                if (fitting.Count > 0)
                    fresh = fitting;
                else if (fresh.Count > 0)
                {
                    // None fits: the closest ones.
                    double middle = (low + high) / 2;
                    fresh = fresh.OrderBy(s => Math.Abs(StarsFor(s.difficulties.MaxBy(x => x.ObjectCount)!) - middle)).Take(Math.Max(3, fresh.Count / 10)).ToList();
                }
            }

            if (generatedStars >= slow_song_stars)
            {
                double tooSlow = MinimumBpmFor(generatedStars.Value) * 0.8;
                var fastEnough = fresh.Where(s => s.difficulties.Max(d => d.Bpm * speed) >= tooSlow).ToList();

                if (fastEnough.Count >= 10)
                    fresh = fastEnough;
            }

            if (generatedStars >= fast_song_stars && rng.NextDouble() < fast_song_preference)
            {
                double minBpm = MinimumBpmFor(generatedStars.Value);
                var fast = fresh.Where(s => s.difficulties.Max(d => d.Bpm * speed) >= minBpm).ToList();

                // Only when the filters leave enough fast songs to keep runs varied.
                if (fast.Count >= 20)
                    fresh = fast;
            }

            // Not the same artist twice in a row, nor three songs at the same tempo.
            var unlike = fresh.Where(s => !recentPicks.TakeLast(1).Any(r => sameArtist(r.artist, s.difficulties[0].Artist))
                                          && !(recentPicks.Count >= 2 && recentPicks.TakeLast(2).All(r => Math.Abs(r.bpm - s.difficulties.Max(d => d.Bpm * speed)) < 5))).ToList();

            if (unlike.Count >= 5)
                fresh = unlike;

            var chosen = fresh[rng.Next(fresh.Count)];

            recentSets.Enqueue(chosen.set.Folder);
            recentPicks.Add((chosen.difficulties[0].Artist, chosen.difficulties.Max(d => d.Bpm * speed)));

            if (recentPicks.Count > 4)
                recentPicks.RemoveAt(0);

            if (closestToStars != null)
                return (chosen.set, chosen.difficulties.MinBy(d => Math.Abs(library.GetStarRating(chosen.set, d) - closestToStars.Value))!);

            // The rhythm usually comes from the most detailed difficulty; sometimes from the next one, whose rhythm is often simpler and different.
            var byObjects = chosen.difficulties.OrderByDescending(d => d.ObjectCount).ToList();

            if (byObjects.Count >= 2 && byObjects[1].ObjectCount >= byObjects[0].ObjectCount * 0.6 && rng.NextDouble() < second_difficulty_chance)
                return (chosen.set, byObjects[1]);

            return (chosen.set, byObjects[0]);
        }

        private const double second_difficulty_chance = 0.3;

        private readonly List<(string artist, double bpm)> recentPicks = new List<(string, double)>();

        private static bool sameArtist(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        private List<(StableSet set, List<StableDifficulty> difficulties)>? cachedCandidates;
        private double cachedSpeed;

        /// <summary>
        /// The sets the settings allow, worked out once per pick (OPTIMISATION: a pick used to filter the whole library
        /// up to fifty times over, on the update thread).
        /// </summary>
        private List<(StableSet set, List<StableDifficulty> difficulties)> candidates()
        {
            if (cachedCandidates == null || cachedSpeed != speed)
            {
                cachedCandidates = buildCandidates();
                cachedSpeed = speed;
            }

            return cachedCandidates;
        }

        /// <summary>
        /// Leaves out a difficulty found to change tempo (in modes that need one tempo), so it isn't picked again this time.
        /// </summary>
        private void dropDifficulty(StableSet set, StableDifficulty difficulty)
        {
            if (cachedCandidates == null)
                return;

            int index = cachedCandidates.FindIndex(c => c.set == set);

            if (index < 0)
                return;

            cachedCandidates[index].difficulties.Remove(difficulty);

            if (cachedCandidates[index].difficulties.Count == 0)
                cachedCandidates.RemoveAt(index);
        }

        private List<(StableSet set, List<StableDifficulty> difficulties)> buildCandidates()
        {
            HashSet<string>? hashes = null;

            // A mode may pick its own source; otherwise the general settings decide.
            string? collection = mode.TrackCollection switch
            {
                null => settings.Source == TrackSource.Collection ? settings.CollectionName ?? string.Empty : null,
                Generation.FreeplayGenerationSettings.WHOLE_LIBRARY => null,
                _ => mode.TrackCollection,
            };

            if (collection != null)
            {
                hashes = library.GetCollectionHashes(collection);

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
            double lengthSeconds = difficulty.LengthMs / 1000 / speed;

            double minLength = mode.MinTrackSeconds ?? settings.MinLengthSeconds;

            if (minLength > 0 && lengthSeconds < minLength)
                return false;

            // A mode's own minimum (e.g. endurance's long tracks) wins over a general maximum below it.
            double maxLength = mode.MinTrackSeconds != null && settings.MaxLengthSeconds < mode.MinTrackSeconds ? 0 : settings.MaxLengthSeconds;

            if (maxLength > 0 && lengthSeconds > maxLength)
                return false;

            if (singleBpmOnly && difficulty.SingleBpm == false)
                return false;

            if (mode.MinBpm > 0 && difficulty.Bpm * speed < mode.MinBpm)
                return false;

            if (mode.MaxBpm > 0 && difficulty.Bpm * speed > mode.MaxBpm)
                return false;

            return true;
        }
    }
}
