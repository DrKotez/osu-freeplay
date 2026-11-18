// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace osu.Game.Rulesets.Freeplay.Stable
{
    /// <summary>
    /// A cached listing of osu!stable's Songs folder, so the library doesn't need to be re-read on every start.
    /// </summary>
    public class StableIndex
    {
        public List<StableSet> Sets { get; set; } = new List<StableSet>();
    }

    /// <summary>
    /// One folder in osu!stable's Songs folder.
    /// </summary>
    public class StableSet
    {
        /// <summary>
        /// The folder name, relative to the Songs folder.
        /// </summary>
        public string Folder { get; set; } = string.Empty;

        /// <summary>
        /// The folder's last write time when it was indexed, used to detect changes.
        /// </summary>
        public long LastWriteTicks { get; set; }

        public List<StableDifficulty> Difficulties { get; set; } = new List<StableDifficulty>();
    }

    /// <summary>
    /// The header information of one osu!standard .osu file.
    /// </summary>
    public class StableDifficulty
    {
        public string File { get; set; } = string.Empty;
        public string MD5 { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
        public string TitleUnicode { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string ArtistUnicode { get; set; } = string.Empty;
        public string Creator { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;

        public string AudioFile { get; set; } = string.Empty;
        public string BackgroundFile { get; set; } = string.Empty;

        public double LengthMs { get; set; }
        public double Bpm { get; set; }
        public int ObjectCount { get; set; }
    }

    /// <summary>
    /// Reads just the parts of a .osu file needed to list and filter it, which is much faster than fully decoding it.
    /// </summary>
    public static class StableHeaderReader
    {
        /// <returns>The difficulty, or null if the file isn't an osu!standard beatmap with objects.</returns>
        public static StableDifficulty? Read(string fullPath, string fileName)
        {
            byte[] bytes = File.ReadAllBytes(fullPath);

            var difficulty = new StableDifficulty
            {
                File = fileName,
                MD5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(),
            };

            string section = string.Empty;
            int mode = 0;

            var timingPoints = new List<(double time, double beatLength)>();
            double firstObject = double.MaxValue, lastObject = double.MinValue;

            foreach (string rawLine in Encoding.UTF8.GetString(bytes).Split('\n'))
            {
                string line = rawLine.Trim();

                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (line[0] == '[' && line[^1] == ']')
                {
                    section = line;
                    continue;
                }

                switch (section)
                {
                    case "[General]":
                    case "[Metadata]":
                        readPair(difficulty, line, ref mode);
                        break;

                    case "[Events]":
                        // Background events look like: 0,0,"bg.jpg",0,0
                        if (difficulty.BackgroundFile.Length == 0 && line.StartsWith("0,0,", StringComparison.Ordinal))
                            difficulty.BackgroundFile = line.Split(',')[2].Trim('"');
                        break;

                    case "[TimingPoints]":
                    {
                        string[] parts = line.Split(',');

                        if (parts.Length >= 2 && tryParse(parts[0], out double time) && tryParse(parts[1], out double beatLength)
                            && beatLength > 0 && (parts.Length < 7 || parts[6] == "1"))
                            timingPoints.Add((time, beatLength));
                        break;
                    }

                    case "[HitObjects]":
                    {
                        string[] parts = line.Split(',');

                        if (parts.Length >= 3 && tryParse(parts[2], out double time))
                        {
                            difficulty.ObjectCount++;
                            firstObject = Math.Min(firstObject, time);
                            lastObject = Math.Max(lastObject, time);
                        }

                        break;
                    }
                }
            }

            if (mode != 0 || difficulty.ObjectCount == 0 || timingPoints.Count == 0 || difficulty.AudioFile.Length == 0)
                return null;

            difficulty.LengthMs = lastObject - firstObject;
            difficulty.Bpm = 60000 / dominantBeatLength(timingPoints, lastObject);

            return difficulty;
        }

        private static void readPair(StableDifficulty difficulty, string line, ref int mode)
        {
            int separator = line.IndexOf(':');

            if (separator < 0)
                return;

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();

            switch (key)
            {
                case "AudioFilename": difficulty.AudioFile = value; break;
                case "Mode": int.TryParse(value, out mode); break;
                case "Title": difficulty.Title = value; break;
                case "TitleUnicode": difficulty.TitleUnicode = value; break;
                case "Artist": difficulty.Artist = value; break;
                case "ArtistUnicode": difficulty.ArtistUnicode = value; break;
                case "Creator": difficulty.Creator = value; break;
                case "Version": difficulty.Version = value; break;
            }
        }

        /// <summary>
        /// The beat length that lasts longest, as the BPM shown for a track.
        /// </summary>
        private static double dominantBeatLength(List<(double time, double beatLength)> timingPoints, double endTime)
        {
            return timingPoints.Select((t, i) => (t.beatLength, duration: (i + 1 < timingPoints.Count ? timingPoints[i + 1].time : endTime) - t.time))
                               .GroupBy(t => Math.Round(t.beatLength, 3))
                               .MaxBy(g => g.Sum(t => t.duration))!
                               .Key;
        }

        private static bool tryParse(string value, out double result) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
