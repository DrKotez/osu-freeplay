// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    /// <summary>
    /// A fingerprint of the maps generated for every style, to check a change that shouldn't touch them leaves them as they were.
    /// Writes one line a map to the file in FREEPLAY_GOLDEN (compare two runs' files).
    /// </summary>
    [TestFixture]
    public class GoldenMapsReport
    {
        [Test]
        [Explicit]
        public void GoldenMaps()
        {
            string output = Environment.GetEnvironmentVariable("FREEPLAY_GOLDEN") ?? Path.Combine(Path.GetTempPath(), "freeplay-golden.txt");
            var lines = new List<string>();

            foreach (string name in new[] { "801165", "20544", "1124896", "2593923" })
            {
                string path = Path.Combine(FreeplayGenerationTest.RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "Resources", "Beatmaps", $"{name}.osu");

                foreach (var style in Enum.GetValues<PatternStyle>())
                {
                    foreach (int seed in new[] { 1, 2, 3 })
                    {
                        foreach (double stars in new[] { 3.5, 5.5 })
                        {
                            var map = FreeplayGenerationTest.generate(path, new FreeplayGenerationSettings { Style = style, StarRating = stars, Seed = seed }, out _, calibrate: style == PatternStyle.Metronome && seed == 1);

                            lines.Add($"{name} {style} seed {seed} {stars}* : {fingerprint(map)}");
                        }
                    }
                }
            }

            // Each figure on its own.
            string single = Path.Combine(FreeplayGenerationTest.RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "Resources", "Beatmaps", "801165.osu");

            foreach (var shape in Enum.GetValues<MetronomeShapes>().Where(s => s != MetronomeShapes.None && s != MetronomeShapes.All))
                lines.Add($"stars {shape} : {fingerprint(FreeplayGenerationTest.generate(single, new FreeplayGenerationSettings { Style = PatternStyle.Metronome, StarRating = 5, MetronomeShapes = shape, Seed = 4 }, out _))}");

            foreach (var pattern in Enum.GetValues<JumpPatterns>().Where(s => s != JumpPatterns.None && s != JumpPatterns.All))
            {
                foreach (var style in new[] { PatternStyle.Jumps, PatternStyle.Flow, PatternStyle.Mixed })
                    lines.Add($"jumps {pattern} {style} : {fingerprint(FreeplayGenerationTest.generate(single, new FreeplayGenerationSettings { Style = style, StarRating = 5, Patterns = pattern, MinedPatternShare = 0, Seed = 4 }, out _))}");
            }

            File.WriteAllLines(output, lines);
            TestContext.Out.WriteLine($"{lines.Count} maps to {output}");
        }

        private static string fingerprint(osu.Game.Beatmaps.IBeatmap map)
        {
            var text = new StringBuilder();

            foreach (var o in map.HitObjects)
            {
                text.Append(o.StartTime.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                if (o is IHasPosition p)
                    text.Append(p.X.ToString("0.###", CultureInfo.InvariantCulture)).Append(',').Append(p.Y.ToString("0.###", CultureInfo.InvariantCulture));
                text.Append(o.GetType().Name[0]).Append(';');
            }

            return $"{map.HitObjects.Count} objects {Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16]}";
        }
    }
}
