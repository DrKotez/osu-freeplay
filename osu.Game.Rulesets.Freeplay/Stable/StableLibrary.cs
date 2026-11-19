// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.Freeplay.Stable
{
    /// <summary>
    /// Plays beatmaps straight from osu!stable's Songs folder, without importing them, like stable itself does.
    /// </summary>
    public class StableLibrary
    {
        /// <summary>
        /// osu!stable's default install location.
        /// </summary>
        public static readonly string DEFAULT_PATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!");

        private const string index_file = "stable-index.json";

        /// <summary>
        /// Finds this computer's osu!stable install: <paramref name="chosen"/> if it exists, else the usual folder,
        /// else wherever osu!stable registered itself (it handles osu:// links). This way a copy of freeplay~ passed to
        /// someone else finds their maps and skins rather than looking for the original owner's folders.
        /// </summary>
        /// <returns>The first folder with a Songs folder, or <paramref name="chosen"/> (or the usual folder) if none has one.</returns>
        public static string FindInstall(string? chosen)
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(chosen))
                candidates.Add(chosen.Trim());

            candidates.Add(DEFAULT_PATH);

            string? registered = registeredInstall();
            if (registered != null)
                candidates.Add(registered);

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(Path.Combine(candidate, "Songs")))
                    return candidate;
            }

            return candidates[0];
        }

        private static string? registeredInstall()
        {
            if (!OperatingSystem.IsWindows())
                return null;

            try
            {
                // e.g. "C:\Games\osu!\osu!.exe" "%1"
                using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"osu\Shell\Open\Command");
                string? command = key?.GetValue(string.Empty)?.ToString();

                if (string.IsNullOrEmpty(command))
                    return null;

                string executable = command.StartsWith('"') ? command.Split('"')[1] : command.Split(' ')[0];
                return Path.GetDirectoryName(executable);
            }
            catch (Exception e)
            {
                Logger.Log($"Couldn't read osu!stable's install location from the registry: {e.Message}");
                return null;
            }
        }

        public readonly string StablePath;
        public string SongsPath => songsPath ?? Path.Combine(StablePath, "Songs");

        private readonly string? songsPath;
        public string SkinsPath => Path.Combine(StablePath, "Skins");

        /// <summary>
        /// Whether the osu!stable folder exists.
        /// </summary>
        public bool Exists => Directory.Exists(SongsPath);

        /// <summary>
        /// The indexed sets. Replaced (not mutated) when a scan completes, so it is safe to read from any thread.
        /// </summary>
        public IReadOnlyList<StableSet> Sets { get; private set; } = Array.Empty<StableSet>();

        public IReadOnlyList<StableCollection> Collections { get; private set; } = Array.Empty<StableCollection>();

        /// <summary>
        /// Progress of the current scan, from 0 to 1. 1 when idle.
        /// </summary>
        public readonly BindableDouble ScanProgress = new BindableDouble(1);

        public bool IsScanning { get; private set; }

        private readonly Storage freeplayStorage;
        private readonly AudioManager audio;
        private readonly RulesetStore rulesets;

        private readonly ITrackStore tracks;
        private readonly LargeTextureStore textures;
        private readonly IResourceStore<byte[]> files;

        /// <param name="songsPath">Where songs are, if not in the install's own Songs folder (osu!stable's BeatmapDirectory setting).</param>
        public StableLibrary(string stablePath, Storage freeplayStorage, GameHost host, AudioManager audio, RulesetStore rulesets, string? songsPath = null)
        {
            StablePath = stablePath;
            this.songsPath = songsPath;

            this.freeplayStorage = freeplayStorage;
            this.audio = audio;
            this.rulesets = rulesets;

            // One shared store over the whole Songs folder, addressed by "folder/file".
            files = new StorageBackedResourceStore(new NativeStorage(SongsPath, host));
            tracks = audio.GetTrackStore(files);
            textures = new LargeTextureStore(host.Renderer, host.CreateTextureLoaderStore(files));

            loadIndex();
            Collections = StableCollection.Read(Path.Combine(StablePath, "collection.db"));
        }

        /// <summary>
        /// Rescans the Songs folder, re-reading only folders that changed since the last scan.
        /// </summary>
        public Task ScanAsync(CancellationToken cancellationToken = default) => Task.Run(() => scan(cancellationToken), cancellationToken);

        public WorkingBeatmap GetWorkingBeatmap(StableSet set, StableDifficulty difficulty)
            => new StableWorkingBeatmap(set, difficulty, files, tracks, textures, audio, rulesets.GetRuleset(0)!);

        /// <summary>
        /// A background image from a random song, for menus.
        /// </summary>
        public Texture? GetRandomBackground(Random rng)
        {
            var withBackgrounds = Sets.Where(s => s.Difficulties.Any(d => d.BackgroundFile.Length > 0)).ToList();

            for (int attempt = 0; attempt < 5 && withBackgrounds.Count > 0; attempt++)
            {
                var set = withBackgrounds[rng.Next(withBackgrounds.Count)];
                var difficulty = set.Difficulties.First(d => d.BackgroundFile.Length > 0);

                try
                {
                    var texture = textures.Get($"{set.Folder}/{difficulty.BackgroundFile}");
                    if (texture != null)
                        return texture;
                }
                catch (Exception e)
                {
                    Logger.Log($"Couldn't load menu background from {set.Folder}: {e.Message}");
                }
            }

            return null;
        }

        private readonly ConcurrentDictionary<string, double> starRatings = new ConcurrentDictionary<string, double>();

        /// <summary>
        /// The osu! star rating of a difficulty, calculated on first request and cached for the session.
        /// </summary>
        public double GetStarRating(StableSet set, StableDifficulty difficulty)
        {
            return starRatings.GetOrAdd(difficulty.MD5, _ =>
            {
                try
                {
                    var ruleset = rulesets.GetRuleset(0)!.CreateInstance();
                    return ruleset.CreateDifficultyCalculator(GetWorkingBeatmap(set, difficulty)).Calculate().StarRating;
                }
                catch (Exception e)
                {
                    Logger.Log($"Couldn't calculate star rating for {difficulty.File}: {e.Message}");
                    return 0;
                }
            });
        }

        /// <summary>
        /// Patterns taken from the example collections, once <see cref="MinePatternsAsync"/> has run.
        /// </summary>
        public Generation.MinedPatternSet? Patterns { get; private set; }

        /// <summary>
        /// Collects example patterns from the given collections, unless they were already collected from the same ones.
        /// </summary>
        public Task MinePatternsAsync(Configuration.PatternSourceSettings sources, CancellationToken cancellationToken = default)
        {
            // Collections match ignoring case and surrounding spaces (see FindCollection), so the same choice spelled differently isn't mined twice.
            static string normalise(string? name) => name?.Trim().ToLowerInvariant() ?? string.Empty;

            string key = $"{normalise(sources.Aim)}|{normalise(sources.Streams)}|{normalise(sources.Tech)}|{normalise(sources.Stars)}";

            lock (miningLock)
            {
                // Asked again (e.g. the menu resuming) while the same collections are being or have been mined: reuse that.
                if (miningTask != null && miningKey == key)
                    return miningTask;

                var copy = new Configuration.PatternSourceSettings { Aim = sources.Aim, Streams = sources.Streams, Tech = sources.Tech, Stars = sources.Stars };

                miningKey = key;
                return miningTask = Task.Run(() => Patterns = PatternMiner.Mine(this, copy, rulesets.GetRuleset(0)!, cancellationToken), cancellationToken);
            }
        }

        private readonly object miningLock = new object();
        private Task? miningTask;
        private string? miningKey;

        public HashSet<string>? GetCollectionHashes(string? name)
        {
            var collection = FindCollection(name);
            return collection == null ? null : new HashSet<string>(collection.BeatmapHashes);
        }

        /// <summary>
        /// Finds a collection by name, ignoring case and surrounding spaces (stable collection names often have stray leading spaces).
        /// </summary>
        public StableCollection? FindCollection(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return Collections.FirstOrDefault(c => string.Equals(c.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private void scan(CancellationToken cancellationToken)
        {
            if (!Exists || IsScanning)
                return;

            IsScanning = true;
            ScanProgress.Value = 0;

            try
            {
                var previous = Sets.ToDictionary(s => s.Folder);
                var folders = Directory.GetDirectories(SongsPath);

                var result = new ConcurrentBag<StableSet>();
                int done = 0;

                Parallel.ForEach(folders, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, folder =>
                {
                    string name = Path.GetFileName(folder);
                    long lastWrite = Directory.GetLastWriteTimeUtc(folder).Ticks;

                    if (previous.TryGetValue(name, out var cached) && cached.LastWriteTicks == lastWrite)
                        result.Add(cached);
                    else
                    {
                        var set = readSet(folder, name, lastWrite);
                        if (set != null)
                            result.Add(set);
                    }

                    int count = Interlocked.Increment(ref done);
                    if (count % 50 == 0)
                        ScanProgress.Value = (double)count / folders.Length;
                });

                Sets = result.OrderBy(s => s.Folder, StringComparer.OrdinalIgnoreCase).ToList();
                Collections = StableCollection.Read(Path.Combine(StablePath, "collection.db"));

                saveIndex();
                Logger.Log($"Freeplay indexed {Sets.Count} osu!stable sets ({Sets.Sum(s => s.Difficulties.Count)} difficulties).");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to scan the osu!stable Songs folder.");
            }
            finally
            {
                IsScanning = false;
                ScanProgress.Value = 1;
            }
        }

        private static StableSet? readSet(string folder, string name, long lastWrite)
        {
            var set = new StableSet { Folder = name, LastWriteTicks = lastWrite };

            try
            {
                foreach (string file in Directory.EnumerateFiles(folder, "*.osu"))
                {
                    try
                    {
                        var difficulty = StableHeaderReader.Read(file, Path.GetFileName(file));
                        if (difficulty != null)
                            set.Difficulties.Add(difficulty);
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"Skipping unreadable beatmap {file}: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Log($"Skipping unreadable folder {folder}: {e.Message}");
            }

            return set.Difficulties.Count > 0 ? set : null;
        }

        private void loadIndex()
        {
            try
            {
                if (!freeplayStorage.Exists(index_file))
                    return;

                using (var stream = freeplayStorage.GetStream(index_file))
                using (var reader = new StreamReader(stream))
                    Sets = JsonConvert.DeserializeObject<StableIndex>(reader.ReadToEnd())?.Sets ?? new List<StableSet>();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to read the osu!stable index, it will be rebuilt.");
            }
        }

        private void saveIndex()
        {
            using (var stream = freeplayStorage.CreateFileSafely(index_file))
            using (var writer = new StreamWriter(stream))
                writer.Write(JsonConvert.SerializeObject(new StableIndex { Sets = Sets.ToList() }));
        }
    }
}
