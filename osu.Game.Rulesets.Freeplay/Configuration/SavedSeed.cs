// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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
        public SeedMap? Map => map ??= SeedMap.FromCode(Code);
    }
}
