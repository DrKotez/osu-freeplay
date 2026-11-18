// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.Freeplay.Stable
{
    /// <summary>
    /// The settings of an osu!stable player, read from their <c>osu!.&lt;name&gt;.cfg</c>.
    /// </summary>
    public class StableConfig
    {
        public readonly string FilePath;

        private readonly Dictionary<string, string> values;

        private StableConfig(string filePath, Dictionary<string, string> values)
        {
            FilePath = filePath;
            this.values = values;
        }

        /// <summary>
        /// Reads the config of whoever plays osu!stable on this computer: the one named after the Windows user if there is one,
        /// otherwise the most recently used.
        /// </summary>
        public static StableConfig? Find(string stablePath)
        {
            try
            {
                if (!Directory.Exists(stablePath))
                    return null;

                // osu!.cfg is stable's own (shared) file; player settings live in osu!.<name>.cfg.
                var candidates = Directory.GetFiles(stablePath, "osu!.*.cfg").Where(f => !Path.GetFileName(f).Equals("osu!.cfg", StringComparison.OrdinalIgnoreCase)).ToList();

                if (candidates.Count == 0)
                    return null;

                string ownName = $"osu!.{Environment.UserName}.cfg";
                string file = candidates.FirstOrDefault(f => Path.GetFileName(f).Equals(ownName, StringComparison.OrdinalIgnoreCase))
                              ?? candidates.OrderByDescending(File.GetLastWriteTimeUtc).First();

                return Read(file);
            }
            catch (Exception e)
            {
                Logger.Log($"Couldn't read osu!stable's settings: {e.Message}");
                return null;
            }
        }

        public static StableConfig Read(string file)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in File.ReadLines(file))
            {
                if (line.StartsWith('#'))
                    continue;

                int separator = line.IndexOf('=');

                if (separator <= 0)
                    continue;

                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            return new StableConfig(file, values);
        }

        public string? GetString(string key) => values.TryGetValue(key, out string? value) && value.Length > 0 ? value : null;

        public double? GetDouble(string key) => double.TryParse(GetString(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;

        public bool? GetBool(string key)
        {
            string? value = GetString(key);
            return value == null ? null : value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
