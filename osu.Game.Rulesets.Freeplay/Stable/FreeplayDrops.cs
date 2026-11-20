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
                        // (A seed is at most a few hundred kilobytes: a huge file isn't one, and isn't read whole.)
                        if (new FileInfo(path).Length > 4_000_000)
                        {
                            Handled?.Invoke($"{Path.GetFileName(path)}: слишком большой для сида.");
                            return true;
                        }

                        text = File.ReadAllText(path);
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"Freeplay couldn't read a dropped seed: {e.Message}");
                        return true;
                    }

                    if (SeedMap.FromCode(text) is not SeedMap seed)
                    {
                        Handled?.Invoke($"{Path.GetFileName(path)}: в файле нет сида (или он повреждён).");

                        return true;
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

            if (extension == ".osk")
            {
                // A skin: into osu!stable's Skins folder, where freeplay~'s skin list (Настройки) finds it.
                try
                {
                    string folder = Path.Combine(library.SkinsPath, string.Join("_", Path.GetFileNameWithoutExtension(path).Split(Path.GetInvalidFileNameChars())));
                    Directory.CreateDirectory(folder);
                    System.IO.Compression.ZipFile.ExtractToDirectory(path, folder, true);
                    Handled?.Invoke($"Скин «{Path.GetFileNameWithoutExtension(path)}» добавлен в Skins osu!stable. Выбери его в настройках.");
                }
                catch (Exception e)
                {
                    Logger.Log($"Freeplay couldn't add {path}: {e.Message}");
                    Handled?.Invoke($"Не получилось добавить {Path.GetFileName(path)}: {e.Message}");
                }

                return true;
            }

            // FIX (no lazer UI): anything else used to go to osu!lazer's own import (its notifications, its skin and replay database).
            Handled?.Invoke($"{Path.GetFileName(path)}: сюда можно перетащить сид (.fpseed), песню (.osz) или скин (.osk).");
            return true;
        }
    }
}
