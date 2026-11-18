// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Rulesets.Osu;
using Newtonsoft.Json.Converters;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Rulesets.Freeplay.Configuration
{
    /// <summary>
    /// Reads and writes freeplay settings and run history as JSON files in the game's storage, under <c>freeplay/</c>.
    /// </summary>
    public class FreeplayStorage
    {
        private const string settings_file = "settings.json";
        private const string history_file = "history.json";

        private static readonly JsonSerializerSettings serializer_settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new StringEnumConverter() },
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        private readonly Storage storage;

        public FreeplayStorage(Storage gameStorage)
        {
            storage = gameStorage.GetStorageForDirectory("freeplay");
        }

        public FreeplaySettings LoadSettings() => read<FreeplaySettings>(settings_file) ?? new FreeplaySettings();

        public void SaveSettings(FreeplaySettings settings) => write(settings_file, settings);

        public List<RunRecord> LoadHistory() => read<List<RunRecord>>(history_file) ?? new List<RunRecord>();

        public void AddRun(RunRecord run)
        {
            var history = LoadHistory();
            history.Add(run);
            write(history_file, history);
        }

        public const string FEEDBACK_DIRECTORY = "feedback";

        /// <summary>
        /// Saves (or updates) <paramref name="entry"/>. The first save also writes the generated map and copies the mapper's original,
        /// so the map can be looked at again on another computer, without the song or the player's collections.
        /// </summary>
        public void SaveFeedback(FeedbackEntry entry, IBeatmap? generated, string? sourcePath)
        {
            try
            {
                bool first = entry.Folder == null;

                if (first)
                {
                    string name = $"{entry.Date.LocalDateTime:yyyyMMdd-HHmmss}-{sanitise(entry.Title)}";
                    string folder = name;

                    for (int i = 2; storage.ExistsDirectory(Path.Combine(FEEDBACK_DIRECTORY, folder)); i++)
                        folder = $"{name}-{i}";

                    entry.Folder = folder;
                }

                string directory = Path.Combine(FEEDBACK_DIRECTORY, entry.Folder!);
                Directory.CreateDirectory(storage.GetFullPath(directory));
                write(Path.Combine(directory, "feedback.json"), entry);

                if (!first)
                    return;

                if (generated != null)
                {
                    using (var stream = storage.CreateFileSafely(Path.Combine(directory, "generated.osu")))
                    using (var writer = new StreamWriter(stream))
                        EncodeGenerated(generated, writer);
                }

                if (sourcePath != null && File.Exists(sourcePath))
                    File.Copy(sourcePath, storage.GetFullPath(Path.Combine(directory, "source.osu"), true), true);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to save freeplay feedback.");
            }
        }

        public int CountFeedback() => storage.ExistsDirectory(FEEDBACK_DIRECTORY) ? storage.GetDirectories(FEEDBACK_DIRECTORY).Count() : 0;

        /// <summary>
        /// Packs all feedback into one zip, to send to whoever looks at the maps.
        /// </summary>
        /// <returns>The zip's full path, or null if there's no feedback.</returns>
        public string? ExportFeedback()
        {
            if (CountFeedback() == 0)
                return null;

            string zip = storage.GetFullPath(Path.Combine("exports", $"freeplay-feedback-{DateTime.Now:yyyyMMdd-HHmmss}.zip"), true);
            ZipFile.CreateFromDirectory(storage.GetFullPath(FEEDBACK_DIRECTORY), zip, CompressionLevel.Optimal, includeBaseDirectory: true);
            return zip;
        }

        /// <summary>
        /// Writes a generated map as an ordinary osu! .osu file (it's played as freeplay, which the format doesn't know).
        /// </summary>
        public static void EncodeGenerated(IBeatmap generated, TextWriter writer)
        {
            var copy = generated.Clone();
            copy.BeatmapInfo = generated.BeatmapInfo.Clone();
            copy.BeatmapInfo.Ruleset = new OsuRuleset().RulesetInfo;
            copy.BeatmapInfo.DifficultyName = $"freeplay {generated.BeatmapInfo.DifficultyName}";

            new LegacyBeatmapEncoder(copy, null, null).Encode(writer);
        }

        private static string sanitise(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return clean.Length > 40 ? clean[..40] : clean;
        }

        private T? read<T>(string filename) where T : class
        {
            if (!storage.Exists(filename))
                return null;

            try
            {
                using (var stream = storage.GetStream(filename))
                using (var reader = new StreamReader(stream))
                    return JsonConvert.DeserializeObject<T>(reader.ReadToEnd(), serializer_settings);
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Failed to read freeplay {filename}, using defaults.");
                return null;
            }
        }

        private void write<T>(string filename, T value)
        {
            try
            {
                using (var stream = storage.CreateFileSafely(filename))
                using (var writer = new StreamWriter(stream))
                    writer.Write(JsonConvert.SerializeObject(value, serializer_settings));
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Failed to save freeplay {filename}.");
            }
        }
    }
}
