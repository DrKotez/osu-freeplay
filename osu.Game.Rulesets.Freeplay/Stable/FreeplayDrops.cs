// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Game.Rulesets.Freeplay.Configuration;

namespace osu.Game.Rulesets.Freeplay.Stable
{
    /// <summary>
    /// Files dropped on the game's window: a seed file goes to the player's seeds, and a beatmap set downloaded from the osu! website (.osz)
    /// into osu!stable's Songs folder (where freeplay~ takes its songs from), so a seed's missing song can be played.
    /// </summary>
    public static class FreeplayDrops
    {
        /// <summary>
        /// Raised (on whichever thread) with what was done, e.g. to show it and refresh the seeds page.
        /// </summary>
        public static event Action<string>? Handled;

        /// <summary>
        /// Handles <paramref name="path"/> if it's a seed or a beatmap set, returning whether it did.
        /// </summary>
        public static bool Handle(string path, FreeplayStorage storage, StableLibrary library)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();

            switch (extension)
            {
                case SeedMap.FILE_EXTENSION:
                case ".txt":
                    string text;

                    try
                    {
                        text = File.ReadAllText(path);
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"Freeplay couldn't read a dropped seed: {e.Message}");
                        return extension == SeedMap.FILE_EXTENSION;
                    }

                    if (SeedMap.FromCode(text) is not SeedMap seed)
                    {
                        if (extension == SeedMap.FILE_EXTENSION)
                            Handled?.Invoke("В этом файле нет сида (или он повреждён).");

                        return extension == SeedMap.FILE_EXTENSION;
                    }

                    var saved = storage.AddSeed(seed, Path.GetFileNameWithoutExtension(path));
                    Handled?.Invoke($"Сид «{saved.Name}» в «Сидах».");
                    return true;

                case ".osz":
                    Task.Run(async () =>
                    {
                        try
                        {
                            library.ImportOsz(path);
                            await library.ScanAsync().ConfigureAwait(false);
                            Handled?.Invoke($"Песня «{Path.GetFileNameWithoutExtension(path)}» добавлена в Songs osu!stable.");
                        }
                        catch (Exception e)
                        {
                            Logger.Log($"Freeplay couldn't add {path}: {e.Message}");
                            Handled?.Invoke($"Не получилось добавить {Path.GetFileName(path)}: {e.Message}");
                        }
                    });
                    return true;
            }

            return false;
        }
    }
}
