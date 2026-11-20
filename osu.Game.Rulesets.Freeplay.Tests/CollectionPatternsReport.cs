// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Freeplay.Stable;
using osu.Game.Rulesets.Objects.Types;
using osuTK;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    /// <summary>
    /// What the maps of an osu!stable collection are made of: figures gone round again (a note lands back where one a few before was), the turns
    /// between jumps, the streams. Draws examples of each figure to a picture. For finding patterns worth adding to <see cref="Generation.PatternLibrary"/>.
    /// </summary>
    [TestFixture]
    public class CollectionPatternsReport
    {
        private const string collection_name = "fosu";

        [Test]
        [Explicit]
        public void Report()
        {
            string stable = StableLibrary.DEFAULT_PATH;
            var collection = StableCollection.Read(Path.Combine(stable, "collection.db")).FirstOrDefault(c => c.Name.Equals(collection_name, StringComparison.OrdinalIgnoreCase));

            if (collection == null)
                Assert.Ignore($"No collection {collection_name}.");

            var files = findFiles(stable, collection.BeatmapHashes.ToHashSet(StringComparer.OrdinalIgnoreCase));
            TestContext.Out.WriteLine($"{collection.Name}: {collection.BeatmapHashes.Count} maps, {files.Count} found");

            var loops = new List<Loop>();
            var turns = new Dictionary<string, int>();
            var streams = new Dictionary<string, int>();
            int jumpsTotal = 0;
            int[,] grid = new int[3, 3];
            int gridTotal = 0, nearCorner = 0;
            var lengths = new Dictionary<int, int>();

            foreach (string file in files)
            {
                Beatmap beatmap;
                using (var stream = new LineBufferedReader(File.OpenRead(file)))
                    beatmap = Decoder.GetDecoder<Beatmap>(stream).Decode(stream);

                if (beatmap.BeatmapInfo.Ruleset.OnlineID != 0)
                    continue;

                var notes = beatmap.HitObjects.Where(h => h is IHasPosition && (h is IHasPath || h is not IHasDuration))
                                   .Select(h => new Note(h.StartTime, ((IHasPosition)h).Position, h is IHasPath,
                                       beatmap.ControlPointInfo.TimingPointAt(h.StartTime).BeatLength))
                                   .ToList();

                var mapLoops = findLoops(notes);
                loops.AddRange(mapLoops.Select(l => l with { Map = Path.GetFileNameWithoutExtension(file) }));

                int mapJumps = countTurns(notes, turns);
                jumpsTotal += mapJumps;
                countStreams(notes, streams);

                foreach (var n in notes)
                {
                    grid[Math.Clamp((int)(n.Position.Y / 128), 0, 2), Math.Clamp((int)(n.Position.X / 171), 0, 2)]++;
                    gridTotal++;
                    if (new[] { new Vector2(0, 0), new Vector2(512, 0), new Vector2(0, 384), new Vector2(512, 384) }.Any(c => Vector2.Distance(c, n.Position) < 100))
                        nearCorner++;
                }

                for (int i = 1; i < notes.Count; i++)
                {
                    double gap = notes[i].Time - notes[i - 1].Time;
                    if (notes[i - 1].Slider || gap < notes[i].BeatLength * 0.45 || gap > notes[i].BeatLength * 1.05)
                        continue;
                    int band = (int)(Vector2.Distance(notes[i].Position, notes[i - 1].Position) / 50) * 50;
                    lengths[band] = lengths.GetValueOrDefault(band) + 1;
                }

                var bpm = 60000 / beatmap.ControlPointInfo.TimingPoints.OrderByDescending(t => beatmap.ControlPointInfo.TimingPoints.Count(x => x.BeatLength == t.BeatLength)).First().BeatLength;
                TestContext.Out.WriteLine($"  {beatmap.Metadata.Artist} - {beatmap.Metadata.Title} [{beatmap.BeatmapInfo.DifficultyName}] by {beatmap.Metadata.Author.Username}"
                                          + $" · {bpm:0} bpm · {notes.Count} notes ({notes.Count(n => n.Slider)} sliders) · {mapJumps} jumps · loops {string.Join(" ", mapLoops.GroupBy(l => l.Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}×{g.Count()}"))}");
            }

            TestContext.Out.WriteLine();
            TestContext.Out.WriteLine("Figures gone round (a note back on one 2-8 before, the same rhythm), by kind:");

            foreach (var group in loops.GroupBy(l => l.Name).OrderByDescending(g => g.Count()))
                TestContext.Out.WriteLine($"  {group.Key,-28} {group.Count(),4} times, in {group.Select(l => l.Map).Distinct().Count()} maps, {group.Average(l => l.Rounds):0.0} rounds, jump {group.Average(l => l.Jump):0} px");

            TestContext.Out.WriteLine();
            TestContext.Out.WriteLine($"Turns between {jumpsTotal} jumps (angle the cursor turns, the same rhythm, jumps over 90 px):");
            foreach (var (name, count) in turns.OrderBy(t => t.Key))
                TestContext.Out.WriteLine($"  {name,-20} {count,6} {(double)count / Math.Max(1, jumpsTotal):P0}");

            TestContext.Out.WriteLine();
            TestContext.Out.WriteLine("Streams (1/4, 5+ notes) by shape:");
            foreach (var (name, count) in streams.OrderByDescending(t => t.Value))
                TestContext.Out.WriteLine($"  {name,-28} {count,6}");

            TestContext.Out.WriteLine();
            TestContext.Out.WriteLine("Where the notes are (the playfield in 3×3, % of notes):");
            for (int row = 0; row < 3; row++)
                TestContext.Out.WriteLine("  " + string.Join("  ", Enumerable.Range(0, 3).Select(col => $"{(double)grid[row, col] / Math.Max(1, gridTotal),5:P0}")));
            TestContext.Out.WriteLine($"  within 100 px of a corner: {(double)nearCorner / Math.Max(1, gridTotal):P0} (an even spread would be {4 * Math.PI * 100 * 100 / 4 / (512 * 384):P0})");

            TestContext.Out.WriteLine();
            TestContext.Out.WriteLine("Jump lengths (the same rhythm, 1/2 beat or slower):");
            foreach (var (band, count) in lengths.OrderBy(l => l.Key))
                TestContext.Out.WriteLine($"  {band,3}-{band + 50} px {count,6}");

            draw(loops,Path.Combine(FreeplayGenerationTest.RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "bin", $"collection-{collection_name}.png"));
        }

        /// <summary>
        /// The figures added from the collection as the generator places them: a row a figure, the stars mode then jumps, a few stretches each.
        /// </summary>
        [Test]
        [Explicit]
        public void DrawAddedFigures()
        {
            string source = Path.Combine(FreeplayGenerationTest.RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "Resources", "Beatmaps", "801165.osu");
            var rows = new List<(MetronomeShapes? shape, Generation.JumpPatterns? pattern)>
            {
                (MetronomeShapes.Bowtie, null), (MetronomeShapes.Weave, null), (MetronomeShapes.Hourglass, null), (MetronomeShapes.FrameStar, null), (MetronomeShapes.SharpZigzag, null),
                (null, Generation.JumpPatterns.Bowties), (null, Generation.JumpPatterns.Weaves), (null, Generation.JumpPatterns.Hourglasses), (null, Generation.JumpPatterns.FrameStars),
                (null, Generation.JumpPatterns.SharpZigzags),
            };

            const float scale = 0.5f;
            const int panel_width = (int)(512 * scale) + 10, panel_height = (int)(384 * scale) + 10, per_row = 4;
            using var image = new Image<Rgba32>(panel_width * per_row, panel_height * rows.Count, new Rgba32(24, 24, 32));

            for (int row = 0; row < rows.Count; row++)
            {
                var (shape, pattern) = rows[row];
                var settings = shape != null
                    ? new Generation.FreeplayGenerationSettings { Style = Generation.PatternStyle.Metronome, StarRating = 4, MetronomeShapes = shape.Value, RotateShapes = false, ShapeMeasures = 8, Seed = 3 }
                    : new Generation.FreeplayGenerationSettings { Style = Generation.PatternStyle.Jumps, StarRating = 4.5, SliderAmount = 0, PatternIq = 1, Patterns = pattern!.Value, MinedPatternShare = 0, AllowStreams = false, Seed = 3 };

                var notes = FreeplayGenerationTest.generate(source, settings, out _).HitObjects.OfType<osu.Game.Rulesets.Osu.Objects.OsuHitObject>().ToList();

                for (int col = 0; col < per_row; col++)
                {
                    var origin = new Vector2(col * panel_width + 5, row * panel_height + 5);
                    var slice = notes.Skip(notes.Count / 5 * (col + 1)).Take(shape != null ? 12 : 10).ToList();

                    for (int x = 0; x < 512 * scale; x++)
                    {
                        image[(int)origin.X + x, (int)origin.Y] = new Rgba32(60, 60, 80);
                        image[(int)origin.X + x, (int)(origin.Y + 384 * scale)] = new Rgba32(60, 60, 80);
                    }

                    for (int i = 0; i < slice.Count; i++)
                    {
                        if (i > 0)
                            line(image, origin + slice[i - 1].Position * scale, origin + slice[i].Position * scale, new Rgba32(110, 110, 150));
                    }

                    for (int i = 0; i < slice.Count; i++)
                    {
                        byte shade = (byte)(90 + 165 * i / Math.Max(1, slice.Count - 1));
                        disc(image, origin + slice[i].Position * scale, i == 0 ? 6 : 4, new Rgba32(255, shade, (byte)(255 - shade / 2)));
                    }
                }
            }

            string output = Path.Combine(FreeplayGenerationTest.RepositoryRoot, "osu.Game.Rulesets.Freeplay.Tests", "bin", "added-figures.png");
            using (var stream = File.Create(output))
                image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
            TestContext.Out.WriteLine(output);
        }

        private record Note(double Time, Vector2 Position, bool Slider, double BeatLength);

        /// <param name="Points">The figure's points in the order gone round, centred.</param>
        private record Loop(string Name, Vector2[] Points, int Rounds, float Jump, string Map = "");

        /// <summary>
        /// Runs where each note lands back on the one k before (k of 2 to 8, the smallest that fits), in a steady rhythm, at least one and a half rounds.
        /// </summary>
        private static List<Loop> findLoops(List<Note> notes)
        {
            var result = new List<Loop>();

            for (int i = 0; i < notes.Count;)
            {
                int found = 0, run = 0;

                for (int k = 2; k <= 8 && found == 0; k++)
                {
                    int length = 0;

                    while (i + length + k < notes.Count
                           && Vector2.Distance(notes[i + length].Position, notes[i + length + k].Position) < 12
                           && steady(notes, i + length, i + length + k)
                           && distinct(notes, i + length, k))
                        length++;

                    // Gone round at least half a round more than once: a figure, not a note back by chance.
                    if (length >= Math.Max(2, k / 2 + 1))
                    {
                        found = k;
                        run = length + k;
                    }
                }

                if (found == 0)
                {
                    i++;
                    continue;
                }

                var points = notes.Skip(i).Take(found).Select(n => n.Position).ToArray();
                var centre = points.Aggregate(Vector2.Zero, (a, b) => a + b) / points.Length;
                float jump = Enumerable.Range(0, found).Average(j => Vector2.Distance(points[j], points[(j + 1) % found]));

                if (jump > 40)
                    result.Add(new Loop(name(points), points.Select(p => p - centre).ToArray(), run / found, jump));

                i += run;
            }

            return result;
        }

        /// <summary>
        /// The same gap between every note from <paramref name="from"/> to <paramref name="to"/>.
        /// </summary>
        private static bool steady(List<Note> notes, int from, int to)
        {
            double first = notes[from + 1].Time - notes[from].Time;

            for (int j = from + 1; j < to; j++)
            {
                if (Math.Abs(notes[j + 1].Time - notes[j].Time - first) > 3)
                    return false;
            }

            return first > 0 && first <= notes[from].BeatLength * 1.01;
        }

        /// <summary>
        /// The <paramref name="k"/> notes from <paramref name="from"/> on k different spots (no stack within one round).
        /// </summary>
        private static bool distinct(List<Note> notes, int from, int k)
        {
            for (int a = 0; a < k; a++)
            {
                for (int b = a + 1; b < k; b++)
                {
                    if (Vector2.Distance(notes[from + a].Position, notes[from + b].Position) < 20)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// What a figure gone round in the order of <paramref name="points"/> is: its count of points, and how it goes round them (a polygon
        /// steps to the next point round the middle, a star skips; a crossing goes over itself) and how even its jumps are.
        /// </summary>
        private static string name(Vector2[] points)
        {
            int k = points.Length;

            if (k == 2)
                return "2 back and forth";

            var centre = points.Aggregate(Vector2.Zero, (a, b) => a + b) / k;
            var angles = points.Select(p => MathF.Atan2(p.Y - centre.Y, p.X - centre.X)).ToArray();

            // Where each point is round the middle, and how many places round each jump goes.
            var rank = angles.Select((a, i) => (a, i)).OrderBy(x => x.a).Select((x, r) => (x.i, r)).ToDictionary(x => x.i, x => x.r);
            var steps = Enumerable.Range(0, k).Select(i => ((rank[(i + 1) % k] - rank[i]) % k + k) % k).ToArray();
            int step = steps[0];
            bool regularOrder = steps.All(s => s == step);

            var jumps = Enumerable.Range(0, k).Select(i => Vector2.Distance(points[i], points[(i + 1) % k])).ToArray();
            float spread = jumps.Max() / Math.Max(1, jumps.Min());
            string even = spread < 1.25f ? "even" : spread < 1.8f ? "uneven" : "long+short";

            var radii = points.Select(p => (p - centre).Length).ToArray();
            bool round = radii.Max() / Math.Max(1, radii.Min()) < 1.35f;

            string how;
            if (!regularOrder)
                how = "jumbled";
            else if (step == 1 || step == k - 1)
                how = "polygon";
            else
                how = $"star /{Math.Min(step, k - step)}";

            return $"{k} {how}{(round ? "" : " flat")} {even}";
        }

        /// <summary>
        /// The turn between consecutive jumps (over 90 px, the same gap), in bands.
        /// </summary>
        private static int countTurns(List<Note> notes, Dictionary<string, int> turns)
        {
            int count = 0;

            for (int i = 2; i < notes.Count; i++)
            {
                var a = notes[i - 1].Position - notes[i - 2].Position;
                var b = notes[i].Position - notes[i - 1].Position;

                if (a.Length < 90 || b.Length < 90 || notes[i - 1].Slider || notes[i - 2].Slider || !steady(notes, i - 2, i))
                    continue;

                float turn = MathF.Abs(MathF.Atan2(a.X * b.Y - a.Y * b.X, Vector2.Dot(a, b))) * 180 / MathF.PI;
                int band = (int)(turn / 20) * 20;
                string key = $"{band,3}-{band + 20}°";
                turns[key] = turns.GetValueOrDefault(key) + 1;
                count++;
            }

            return count;
        }

        /// <summary>
        /// Streams of five notes and more at a quarter beat or closer, by how they bend.
        /// </summary>
        private static void countStreams(List<Note> notes, Dictionary<string, int> streams)
        {
            for (int i = 0; i < notes.Count;)
            {
                int end = i;
                while (end + 1 < notes.Count && notes[end + 1].Time - notes[end].Time <= notes[end].BeatLength / 4 * 1.05 && !notes[end].Slider)
                    end++;

                int length = end - i + 1;

                if (length >= 5)
                {
                    var turnsInStream = new List<float>();
                    for (int j = i + 2; j <= end; j++)
                    {
                        var a = notes[j - 1].Position - notes[j - 2].Position;
                        var b = notes[j].Position - notes[j - 1].Position;
                        if (a.Length > 2 && b.Length > 2)
                            turnsInStream.Add(MathF.Atan2(a.X * b.Y - a.Y * b.X, Vector2.Dot(a, b)));
                    }

                    float spacing = Enumerable.Range(i + 1, length - 1).Average(j => Vector2.Distance(notes[j].Position, notes[j - 1].Position));
                    string kind;

                    if (spacing < 8)
                        kind = "stacked";
                    else if (turnsInStream.Count == 0 || turnsInStream.All(t => MathF.Abs(t) < 0.12f))
                        kind = "straight";
                    else if (turnsInStream.Any(t => MathF.Abs(t) > 1.2f))
                        kind = turnsInStream.Count(t => MathF.Abs(t) > 1.2f) >= 2 ? "sharp corners (square/triangle)" : "one sharp corner";
                    else if (turnsInStream.Zip(turnsInStream.Skip(1), (a, b) => MathF.Sign(a) != MathF.Sign(b) && MathF.Abs(a) > 0.2f && MathF.Abs(b) > 0.2f).Count(x => x) > turnsInStream.Count / 2)
                        kind = "zigzag";
                    else if (turnsInStream.All(t => t >= -0.05f) || turnsInStream.All(t => t <= 0.05f))
                        kind = Math.Abs(turnsInStream.Sum()) > MathF.PI * 1.5f ? "circle/spiral" : "arc";
                    else
                        kind = "S-curve/wave";

                    string size = length <= 8 ? "5-8" : length <= 16 ? "9-16" : "17+";
                    string key = $"{kind}, {size}";
                    streams[key] = streams.GetValueOrDefault(key) + 1;
                }

                i = end + 1;
            }
        }

        private static List<string> findFiles(string stable, HashSet<string> hashes)
        {
            var found = new List<string>();
            string songs = Path.Combine(stable, "Songs");

            // The game's own index knows most maps' hashes; the rest are hashed here.
            string index = Path.Combine(FreeplayGenerationTest.RepositoryRoot, "dist", "freeplay~", "freeplay", "stable-index.json");

            if (File.Exists(index))
            {
                var sets = JsonConvert.DeserializeObject<StableIndex>(File.ReadAllText(index))?.Sets ?? new List<StableSet>();

                foreach (var set in sets)
                {
                    foreach (var difficulty in set.Difficulties.Where(d => hashes.Contains(d.MD5)))
                    {
                        string path = Path.Combine(songs, set.Folder, difficulty.File);
                        if (File.Exists(path))
                        {
                            found.Add(path);
                            hashes.Remove(difficulty.MD5);
                        }
                    }
                }
            }

            if (hashes.Count > 0)
            {
                foreach (string path in Directory.EnumerateFiles(songs, "*.osu", SearchOption.AllDirectories))
                {
                    string md5 = Convert.ToHexString(MD5.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

                    if (hashes.Remove(md5))
                        found.Add(path);

                    if (hashes.Count == 0)
                        break;
                }
            }

            if (hashes.Count > 0)
                TestContext.Out.WriteLine($"Not found: {hashes.Count} maps");

            return found;
        }

        /// <summary>
        /// Up to six examples of each kind of figure, a row a kind, the most common first: points numbered by brightness, the way gone round in lines.
        /// </summary>
        private static void draw(List<Loop> loops, string output)
        {
            const int cell = 150, per_row = 6;
            var kinds = loops.GroupBy(l => l.Name).OrderByDescending(g => g.Count()).Take(20).ToList();

            using var image = new Image<Rgba32>(cell * per_row + 220, Math.Max(1, kinds.Count) * cell, new Rgba32(24, 24, 32));

            for (int row = 0; row < kinds.Count; row++)
            {
                var examples = kinds[row].GroupBy(l => l.Map).Select(g => g.First()).Concat(kinds[row]).Distinct().Take(per_row).ToList();

                for (int col = 0; col < examples.Count; col++)
                {
                    var loop = examples[col];
                    var origin = new Vector2(220 + col * cell + cell / 2f, row * cell + cell / 2f);
                    float size = loop.Points.Max(p => p.Length);
                    float scale = (cell / 2f - 14) / Math.Max(1, size);

                    for (int i = 0; i < loop.Points.Length; i++)
                        line(image, origin + loop.Points[i] * scale, origin + loop.Points[(i + 1) % loop.Points.Length] * scale, new Rgba32(110, 110, 150));

                    for (int i = 0; i < loop.Points.Length; i++)
                    {
                        byte shade = (byte)(90 + 165 * i / Math.Max(1, loop.Points.Length - 1));
                        disc(image, origin + loop.Points[i] * scale, i == 0 ? 7 : 5, new Rgba32(255, shade, (byte)(255 - shade / 2)));
                    }
                }

                // The kind's count as a bar on the left (no font to write with).
                int bar = Math.Min(200, kinds[row].Count() * 2);
                for (int x = 10; x < 10 + bar; x++)
                {
                    for (int y = row * cell + cell / 2 - 6; y < row * cell + cell / 2 + 6; y++)
                        image[x, y] = new Rgba32(90, 160, 255);
                }
            }

            using (var stream = File.Create(output))
                image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());

            TestContext.Out.WriteLine();
            TestContext.Out.WriteLine($"Drew the figures, a row a kind in the order above: {output}");
        }

        private static void disc(Image<Rgba32> image, Vector2 centre, int radius, Rgba32 colour)
        {
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    int px = (int)centre.X + x, py = (int)centre.Y + y;
                    if (x * x + y * y <= radius * radius && px >= 0 && py >= 0 && px < image.Width && py < image.Height)
                        image[px, py] = colour;
                }
            }
        }

        private static void line(Image<Rgba32> image, Vector2 from, Vector2 to, Rgba32 colour)
        {
            int steps = (int)Math.Max(1, (to - from).Length);

            for (int s = 0; s <= steps; s++)
            {
                var p = from + (to - from) * s / steps;
                if (p.X >= 0 && p.Y >= 0 && p.X < image.Width && p.Y < image.Height)
                    image[(int)p.X, (int)p.Y] = colour;
            }
        }
    }
}
