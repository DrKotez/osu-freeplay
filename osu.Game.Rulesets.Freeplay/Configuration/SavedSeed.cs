// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace osu.Game.Rulesets.Freeplay.Configuration
{
    /// <summary>
    /// A seed in the player's own list, under the name they gave it.
    /// </summary>
    public class SavedSeed
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The seed code (see <see cref="SeedMap.ToCode"/>).
        /// </summary>
        public string Code { get; set; } = string.Empty;

        public DateTimeOffset Added { get; set; } = DateTimeOffset.Now;

        private SeedMap? map;

        [JsonIgnore]
        public SeedMap? Map => map ??= read(Code);

        /// <summary>
        /// Codes already read (OPTIMISATION: the seeds page is rebuilt on every button, and each rebuild used to unpack every seed again).
        /// </summary>
        private static readonly Dictionary<string, SeedMap?> read_codes = new Dictionary<string, SeedMap?>();

        private static SeedMap? read(string code)
        {
            lock (read_codes)
            {
                if (read_codes.TryGetValue(code, out var known))
                    return known;

                if (read_codes.Count > 500)
                    read_codes.Clear();

                return read_codes[code] = SeedMap.FromCode(code);
            }
        }
    }
}
