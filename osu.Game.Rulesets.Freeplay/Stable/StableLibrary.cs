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

        public readonly string StablePath;
        public string SongsPath => Path.Combine(StablePath, "Songs");
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

        public StableLibrary(string stablePath, Storage freeplayStorage, GameHost host, AudioManager audio, RulesetStore rulesets)
        {
            StablePath = stablePath;

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

        public HashSet<string>? GetCollectionHashes(string? name)
        {
            var collection = Collections.FirstOrDefault(c => c.Name == name);
            return collection == null ? null : new HashSet<string>(collection.BeatmapHashes);
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
