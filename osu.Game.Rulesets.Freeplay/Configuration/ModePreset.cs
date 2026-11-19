// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using osu.Game.Rulesets.Freeplay.Generation;

namespace osu.Game.Rulesets.Freeplay.Configuration
{
    /// <summary>
    /// All of a mode's settings, saved under a name to come back to.
    /// </summary>
    public class ModePreset
    {
        public string Name { get; set; } = string.Empty;

        public FreeplayGenerationSettings Settings { get; set; } = new FreeplayGenerationSettings();

        private static readonly JsonSerializerSettings serializer_settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        /// <summary>
        /// A copy of <paramref name="settings"/> sharing nothing with it.
        /// </summary>
        public static FreeplayGenerationSettings Copy(FreeplayGenerationSettings settings)
            => JsonConvert.DeserializeObject<FreeplayGenerationSettings>(Saved(settings), serializer_settings)!;

        /// <summary>
        /// <paramref name="settings"/> as they're saved, to tell whether two are the same.
        /// </summary>
        public static string Saved(FreeplayGenerationSettings settings) => JsonConvert.SerializeObject(settings, serializer_settings);
    }
}
