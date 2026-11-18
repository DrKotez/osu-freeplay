// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using JetBrains.Annotations;
using osu.Framework.Extensions;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Game.Database;
using osu.Game.IO;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Freeplay.Stable
{
    /// <summary>
    /// An osu!stable skin read directly from its folder in stable's Skins folder.
    /// </summary>
    public class StableSkin : LegacySkin
    {
        /// <summary>
        /// Skin folders by <see cref="SkinInfo.ID"/>, as skins are instantiated by the game from a <see cref="SkinInfo"/> alone.
        /// </summary>
        private static readonly ConcurrentDictionary<Guid, string> folders = new ConcurrentDictionary<Guid, string>();

        [UsedImplicitly(ImplicitUseKindFlags.InstantiatedWithFixedConstructorSignature)]
        public StableSkin(SkinInfo skin, IStorageResourceProvider resources)
            : base(skin, resources, createStore(skin))
        {
        }

        private static IResourceStore<byte[]> createStore(SkinInfo skin)
        {
            if (!folders.TryGetValue(skin.ID, out string? folder))
                throw new InvalidOperationException($"No folder is registered for skin {skin.Name}.");

            return new StorageBackedResourceStore(new NativeStorage(folder));
        }

        /// <summary>
        /// Lists the skin folders in osu!stable's Skins folder.
        /// </summary>
        public static IReadOnlyList<string> List(string skinsPath)
        {
            if (!Directory.Exists(skinsPath))
                return Array.Empty<string>();

            return Directory.GetDirectories(skinsPath).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Creates a <see cref="SkinInfo"/> for a stable skin folder, which can be set as the game's current skin.
        /// </summary>
        public static Live<SkinInfo> CreateInfo(string skinsPath, string name)
        {
            // A stable ID per folder, so the same skin is recognised across restarts.
            var id = new Guid(MD5.HashData(Encoding.UTF8.GetBytes(name)));

            folders[id] = Path.Combine(skinsPath, name);

            return new SkinInfo
            {
                ID = id,
                Name = name,
                Creator = "osu!stable",
                // Type and assembly name only, matching how the game stores instantiation info.
                InstantiationInfo = string.Join(',', typeof(StableSkin).AssemblyQualifiedName!.Split(',').Take(2)),
            }.ToLiveUnmanaged();
        }
    }
}
