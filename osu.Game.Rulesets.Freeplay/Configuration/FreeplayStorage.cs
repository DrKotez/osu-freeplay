// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
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
