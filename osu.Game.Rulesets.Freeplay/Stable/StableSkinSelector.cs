// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using osu.Framework.Logging;
using osu.Game.Database;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Freeplay.Stable
{
    public static class StableSkinSelector
    {
        /// <summary>
        /// Makes an osu!stable skin folder the game's current skin, or the built-in classic skin if <paramref name="name"/> is null or missing.
        /// </summary>
        public static void Apply(SkinManager skins, StableLibrary library, string? name)
        {
            if (skins.CurrentSkinInfo.Disabled)
                return;

            if (name != null && Directory.Exists(Path.Combine(library.SkinsPath, name)))
            {
                if (skins.CurrentSkinInfo.Value.PerformRead(s => s.Name) != name)
                    skins.CurrentSkinInfo.Value = StableSkin.CreateInfo(library.SkinsPath, name);

                return;
            }

            if (name != null)
                Logger.Log($"osu!stable skin \"{name}\" wasn't found, using the built-in skin.");

            skins.CurrentSkinInfo.Value = DefaultLegacySkin.CreateInfo().ToLiveUnmanaged();
        }
    }
}
