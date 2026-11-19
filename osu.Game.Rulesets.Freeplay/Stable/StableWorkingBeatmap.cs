// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Extensions;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Models;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Freeplay.Stable
{
    /// <summary>
    /// A beatmap read directly from a folder in osu!stable's Songs folder.
    /// </summary>
    public class StableWorkingBeatmap : WorkingBeatmap
    {
        private readonly StableSet set;
        private readonly StableDifficulty difficulty;
        private readonly IResourceStore<byte[]> files;
        private readonly ITrackStore tracks;
        private readonly LargeTextureStore textures;


        private Task<IBeatmap>? pregeneration;
        private IRulesetInfo? pregeneratedRuleset;

        private string? pregeneratedMods;

        /// <summary>
        /// Starts generating this track for <paramref name="ruleset"/> with <paramref name="settings"/> in the background after <paramref name="delayMs"/>, on a low priority thread
        /// so the track being played doesn't stutter. The player's request for the playable beatmap then takes it (once).
        /// </summary>
        public void Pregenerate(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, Generation.FreeplayGenerationSettings settings, double delayMs, CancellationToken cancellationToken)
        {
            var source = new TaskCompletionSource<IBeatmap>();
            pregeneratedRuleset = ruleset;
            pregeneratedMods = string.Join(",", mods.Select(m => m.Acronym).Order());
            pregeneration = source.Task;

            var thread = new Thread(() =>
            {
                try
                {
                    // First the track being played loads in peace.
                    if (cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(delayMs)))
                        throw new OperationCanceledException(cancellationToken);

                    using (Generation.FreeplayGenerationSettings.UseForThisThread(settings))
                        source.SetResult(base.GetPlayableBeatmap(ruleset, mods, cancellationToken));
                }
                catch (OperationCanceledException)
                {
                    // A run that ended or restarted no longer needs it. Cancelled rather than failed, so nothing is left unobserved.
                    source.TrySetCanceled();
                }
                catch (Exception e)
                {
                    // The player then generates it as usual.
                    Logger.Log($"Freeplay couldn't generate the next track in advance: {e.Message}");
                    source.TrySetCanceled();
                }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.Lowest,
                Name = "freeplay pregeneration",
            };

            thread.Start();
        }

        public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken token)
        {
            var prepared = pregeneration;

            if (prepared != null && string.Join(",", mods.Select(m => m.Acronym).Order()) == pregeneratedMods && ruleset.ShortName == pregeneratedRuleset?.ShortName)
            {
                pregeneration = null;

                try
                {
                    // Still generating: waiting for it is quicker than starting over.
                    prepared.Wait(token);
                    Logger.Log("Freeplay played a track generated in advance.");
                    return prepared.GetResultSafely();
                }
                catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    Logger.Log($"Freeplay couldn't use the track generated in advance ({e.Message}), generating it again.");
                }
            }

            return base.GetPlayableBeatmap(ruleset, mods, token);
        }

        public StableWorkingBeatmap(StableSet set, StableDifficulty difficulty, IResourceStore<byte[]> files, ITrackStore tracks, LargeTextureStore textures, AudioManager audio,
                                    RulesetInfo ruleset)
            : base(createBeatmapInfo(set, difficulty, ruleset), audio)
        {
            this.set = set;
            this.difficulty = difficulty;
            this.files = files;
            this.tracks = tracks;
            this.textures = textures;
        }

        private static BeatmapInfo createBeatmapInfo(StableSet set, StableDifficulty difficulty, RulesetInfo ruleset)
        {
            var metadata = new BeatmapMetadata
            {
                Title = difficulty.Title,
                TitleUnicode = difficulty.TitleUnicode,
                Artist = difficulty.Artist,
                ArtistUnicode = difficulty.ArtistUnicode,
                Author = { Username = difficulty.Creator },
                AudioFile = difficulty.AudioFile,
                BackgroundFile = difficulty.BackgroundFile,
            };

            var beatmapInfo = new BeatmapInfo(ruleset, new BeatmapDifficulty(), metadata)
            {
                DifficultyName = difficulty.Version,
                MD5Hash = difficulty.MD5,
                Length = difficulty.LengthMs,
                BPM = difficulty.Bpm,
                TotalObjectCount = difficulty.ObjectCount,
            };

            var setInfo = new BeatmapSetInfo { Beatmaps = { beatmapInfo } };
            beatmapInfo.BeatmapSet = setInfo;

            // osu! decides whether two beatmaps share a track or background by comparing file hashes. Without any files,
            // every stable beatmap would look the same, and the first track would keep playing for every following map.
            addFile(setInfo, set, difficulty.AudioFile);
            addFile(setInfo, set, difficulty.BackgroundFile);

            return beatmapInfo;
        }

        private static void addFile(BeatmapSetInfo setInfo, StableSet set, string file)
        {
            if (string.IsNullOrEmpty(file) || setInfo.Files.Any(f => f.Filename == file))
                return;

            // Identified by location rather than content, so nothing needs to be read.
            string hash = $"{set.Folder}/{file}".ToLowerInvariant().ComputeSHA2Hash();
            setInfo.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = hash }, file));
        }

        private string pathFor(string file) => $"{set.Folder}/{file}";

        protected override IBeatmap GetBeatmap()
        {
            try
            {
                using (var stream = files.GetStream(pathFor(difficulty.File)))
                {
                    if (stream == null)
                    {
                        Logger.Log($"Beatmap file {pathFor(difficulty.File)} is missing.", level: LogLevel.Error);
                        return null!;
                    }

                    using (var reader = new LineBufferedReader(stream))
                    {
                        var beatmap = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);

                        beatmap.BeatmapInfo.MD5Hash = difficulty.MD5;
                        beatmap.BeatmapInfo.BeatmapSet = BeatmapInfo.BeatmapSet;
                        beatmap.BeatmapInfo.UpdateStatisticsFromBeatmap(beatmap);

                        return beatmap;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Beatmap {pathFor(difficulty.File)} failed to load");
                return null!;
            }
        }

        public override Texture GetBackground()
        {
            if (string.IsNullOrEmpty(difficulty.BackgroundFile))
                return null!;

            try
            {
                return textures.Get(pathFor(difficulty.BackgroundFile));
            }
            catch (Exception e)
            {
                Logger.Error(e, "Background failed to load");
                return null!;
            }
        }

        protected override Track GetBeatmapTrack()
        {
            try
            {
                return tracks.Get(pathFor(difficulty.AudioFile));
            }
            catch (Exception e)
            {
                Logger.Error(e, "Track failed to load");
                return null!;
            }
        }

        // Beatmap hitsounds and skin elements aren't used; the selected osu!stable skin is.
        protected override ISkin GetSkin() => null!;

        public override Stream GetStream(string storagePath) => files.GetStream(pathFor(storagePath));
    }
}
