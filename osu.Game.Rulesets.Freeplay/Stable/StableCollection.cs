// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using osu.Framework.Logging;
using osu.Game.IO.Legacy;

namespace osu.Game.Rulesets.Freeplay.Stable
{
    /// <summary>
    /// A collection from osu!stable's collection.db.
    /// </summary>
    public class StableCollection
    {
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// MD5 hashes of the .osu files in the collection.
        /// </summary>
        public List<string> BeatmapHashes { get; init; } = new List<string>();

        /// <summary>
        /// Reads collection.db: a version, a collection count, then for each collection its name, a hash count and the hashes.
        /// </summary>
        public static IReadOnlyList<StableCollection> Read(string path)
        {
            var result = new List<StableCollection>();

            if (!File.Exists(path))
                return result;

            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new SerializationReader(stream))
                {
                    reader.ReadInt32(); // version

                    int collectionCount = reader.ReadInt32();

                    for (int i = 0; i < collectionCount; i++)
                    {
                        var collection = new StableCollection { Name = reader.ReadString() };

                        int beatmapCount = reader.ReadInt32();

                        for (int j = 0; j < beatmapCount; j++)
                            collection.BeatmapHashes.Add(reader.ReadString());

                        result.Add(collection);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to read osu!stable collections.");
            }

            return result;
        }
    }
}
