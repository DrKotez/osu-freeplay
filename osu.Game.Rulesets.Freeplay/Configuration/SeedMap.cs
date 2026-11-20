// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Configuration
{
    /// <summary>
    /// The osu!stable difficulty a seed's map was made from, and where to get it.
    /// </summary>
    public class SeedSong
    {
        /// <summary>
        /// The MD5 of the .osu file, which tells the exact difficulty.
        /// </summary>
        public string MD5 { get; set; } = string.Empty;

        /// <summary>
        /// The osu! website's id of the beatmap set (from its folder's name), or null if unknown.
        /// </summary>
        public int? SetId { get; set; }

        public string Artist { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Creator { get; set; } = string.Empty;

        [JsonIgnore]
        public string Name => $"{Artist} — {Title} [{Version}]";

        [JsonIgnore]
        public string? DownloadUrl => SetId is int id ? $"https://osu.ppy.sh/beatmapsets/{id}" : null;
    }

    /// <summary>
    /// A played map, whole: what's needed to play exactly the same map again on another computer (given the song). Its code
    /// ("сид") carries every object, since a map generated from patterns of the player's own collections can't be generated again elsewhere.
    /// </summary>
    public class SeedMap
    {
        /// <summary>
        /// What every seed code starts with.
        /// </summary>
        public const string PREFIX = "freeplay~1:";

        /// <summary>
        /// The extension of a seed saved as a file.
        /// </summary>
        public const string FILE_EXTENSION = ".fpseed";

        public SeedSong Song { get; set; } = new SeedSong();

        /// <summary>
        /// The mode it was made in, e.g. "Звёздочка".
        /// </summary>
        public string Mode { get; set; } = string.Empty;

        public double StarRating { get; set; }
        public double Bpm { get; set; }
        public int Seed { get; set; }

        /// <summary>
        /// The speed the map was made for and is played at: 1, or Double Time's when the game turned it on for the track.
        /// </summary>
        public double Speed { get; set; } = 1;

        public float CircleSize { get; set; }
        public float ApproachRate { get; set; }
        public float OverallDifficulty { get; set; }
        public float DrainRate { get; set; }
        public double SliderMultiplier { get; set; }
        public double SliderTickRate { get; set; }
        public float StackLeniency { get; set; }

        /// <summary>
        /// The objects, packed (see <see cref="Pack"/>).
        /// </summary>
        [JsonIgnore]
        public byte[] Objects { get; set; } = Array.Empty<byte>();

        [JsonIgnore]
        public string Description => $"{Mode} · {StarRating:0.00}★ · {Bpm * Speed:0} BPM" + (Speed > 1 ? FormattableString.Invariant($" (DT {Speed:0.0#}×)") : string.Empty);

        public BeatmapDifficulty CreateDifficulty() => new BeatmapDifficulty
        {
            CircleSize = CircleSize,
            ApproachRate = ApproachRate,
            OverallDifficulty = OverallDifficulty,
            DrainRate = DrainRate,
            SliderMultiplier = SliderMultiplier,
            SliderTickRate = SliderTickRate,
        };

        public void SetDifficulty(BeatmapDifficulty difficulty)
        {
            CircleSize = difficulty.CircleSize;
            ApproachRate = difficulty.ApproachRate;
            OverallDifficulty = difficulty.OverallDifficulty;
            DrainRate = difficulty.DrainRate;
            SliderMultiplier = difficulty.SliderMultiplier;
            SliderTickRate = difficulty.SliderTickRate;
        }

        /// <summary>
        /// The seed of a map just played (as it was generated, before mods), or null if it can't be one.
        /// </summary>
        public static SeedMap? From(Beatmaps.FreeplayBeatmap map, Stable.StableSet set, Stable.StableDifficulty difficulty, string mode)
        {
            if (map.PackedObjects == null || map.GeneratedDifficulty == null)
                return null;

            var seed = new SeedMap
            {
                Song = new SeedSong
                {
                    MD5 = difficulty.MD5,
                    SetId = Stable.StableLibrary.OnlineSetId(set),
                    Artist = difficulty.Artist,
                    Title = difficulty.Title,
                    Version = difficulty.Version,
                    Creator = difficulty.Creator,
                },
                Mode = mode,
                StarRating = Math.Round(map.StarRating, 2),
                Bpm = Math.Round(difficulty.Bpm, 1),
                Seed = map.Seed,
                Speed = map.Settings.GeneratedForSpeed,
                StackLeniency = map.StackLeniency,
                Objects = map.PackedObjects,
            };

            seed.SetDifficulty(map.GeneratedDifficulty);
            return seed;
        }

        #region Code

        /// <summary>
        /// The longest code read, and the most it may unpack to (real maps are a few kilobytes, and a hundred at most).
        /// </summary>
        private const int max_code_length = 2_000_000, max_unpacked_length = 8_000_000;

        /// <summary>
        /// Limits on what a seed's map can hold (FIX: a code from someone else could otherwise ask for a million repeats and hang the game).
        /// </summary>
        private const int max_objects = 30_000, max_repeats = 500, max_control_points = 2_000, max_samples = 5_000;

        private const double max_time = 3 * 60 * 60 * 1000;

        private bool isSane()
        {
            static bool within(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;

            return Song != null && Song.MD5.Length <= 64
                   && within(StarRating, 0, 100) && within(Bpm, 0, 10_000) && within(Speed, 1, Configuration.FreeplayMods.MAX_SPEED)
                   && within(CircleSize, 0, 11) && within(ApproachRate, 0, 11) && within(OverallDifficulty, 0, 11) && within(DrainRate, 0, 11)
                   && within(SliderMultiplier, 0.1, 10) && within(SliderTickRate, 0.1, 16) && within(StackLeniency, 0, 1);
        }

        private static void check(bool condition)
        {
            if (!condition)
                throw new InvalidDataException("Not a map a seed can hold.");
        }

        /// <summary>
        /// The seed code: the prefix, then everything, compressed.
        /// </summary>
        /// <remarks>Worked out once (OPTIMISATION: compressing a map takes a moment, and the share panel and seeds page ask more than once).</remarks>
        public string ToCode() => code ??= createCode();

        private string? code;

        private string createCode()
        {
            using var raw = new MemoryStream();

            using (var deflate = new DeflateStream(raw, CompressionLevel.SmallestSize, true))
            using (var writer = new BinaryWriter(deflate, Encoding.UTF8))
            {
                writer.Write(JsonConvert.SerializeObject(this));
                writer.Write7BitEncodedInt(Objects.Length);
                writer.Write(Objects);
            }

            return PREFIX + Convert.ToBase64String(raw.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// The seed in <paramref name="text"/> (a code, maybe with spaces or line breaks in it from a chat), or null if it isn't one.
        /// </summary>
        public static SeedMap? FromCode(string text)
        {
            string compact = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
            int start = compact.IndexOf(PREFIX, StringComparison.Ordinal);

            if (start < 0)
                return null;

            string body = compact[(start + PREFIX.Length)..].Replace('-', '+').Replace('_', '/');
            body = body.PadRight(body.Length + (4 - body.Length % 4) % 4, '=');

            try
            {
                if (body.Length > max_code_length)
                    return null;

                using var raw = new MemoryStream(Convert.FromBase64String(body));
                using var deflate = new DeflateStream(raw, CompressionMode.Decompress);

                // FIX: a code from someone else is read with limits, so a broken or malicious one can't unpack into gigabytes.
                using var unpacked = new MemoryStream();
                byte[] buffer = new byte[81920];
                int read;

                while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (unpacked.Length + read > max_unpacked_length)
                        return null;

                    unpacked.Write(buffer, 0, read);
                }

                unpacked.Position = 0;
                using var reader = new BinaryReader(unpacked, Encoding.UTF8);

                var seed = JsonConvert.DeserializeObject<SeedMap>(reader.ReadString());

                if (seed == null || !seed.isSane())
                    return null;

                int objectsLength = reader.Read7BitEncodedInt();

                if (objectsLength < 0 || objectsLength > unpacked.Length - unpacked.Position)
                    return null;

                seed.Objects = reader.ReadBytes(objectsLength);

                // Checks the objects can be read, and are within what a map can be (see Unpack).
                seed.Unpack();
                return seed;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Objects

        private enum ObjectType : byte
        {
            Circle,
            Slider,
            Spinner,
        }

        private const byte new_combo = 0x80;

        /// <summary>
        /// Times are kept in sixteenths of a millisecond, positions in sixteenths of a pixel: the same map to within what can be seen or heard.
        /// </summary>
        private const double time_scale = 16;

        private const float position_scale = 16;

        private const int stream_count = 4;

        /// <summary>
        /// Rounds times and positions to what a seed code keeps, so the map played is exactly the one its code gives back
        /// (a hair's difference can otherwise tip osu!'s stacking of near-stacked notes).
        /// </summary>
        public static void Snap(IEnumerable<OsuHitObject> objects)
        {
            foreach (var h in objects)
            {
                double duration = h is Spinner s ? Math.Round((s.EndTime - s.StartTime) * time_scale) / time_scale : 0;

                h.StartTime = Math.Round(h.StartTime * time_scale) / time_scale;
                h.Position = snap(h.Position);

                switch (h)
                {
                    case Spinner spinner:
                        spinner.EndTime = spinner.StartTime + duration;
                        break;

                    case Slider slider:
                        foreach (var point in slider.Path.ControlPoints)
                            point.Position = snap(point.Position);
                        break;
                }
            }

            static Vector2 snap(Vector2 v) => new Vector2(MathF.Round(v.X * position_scale) / position_scale, MathF.Round(v.Y * position_scale) / position_scale);
        }

        public static byte[] Pack(IEnumerable<OsuHitObject> objects)
        {
            var list = objects.ToList();
            var samples = new List<string>();
            var sampleIndices = new Dictionary<string, int>();

            int sampleIndex(IList<HitSampleInfo> set)
            {
                string key = string.Join(";", set.Select(sampleKey));

                if (!sampleIndices.TryGetValue(key, out int index))
                {
                    sampleIndices[key] = index = samples.Count;
                    samples.Add(key);
                }

                return index;
            }

            // One stream per kind of field (kinds, times, positions, sliders): steady rhythms and repeated shapes then make long repeats for the compression.
            var streams = Enumerable.Range(0, stream_count).Select(_ => new MemoryStream()).ToArray();
            var writers = streams.Select(s => new BinaryWriter(s, Encoding.UTF8, true)).ToArray();
            BinaryWriter w = writers[0], times = writers[1], positions = writers[2], sliders = writers[3];

            {
                long lastTime = 0, lastGap = 0;

                void writeTime(double time)
                {
                    long scaled = (long)Math.Round(time * time_scale);
                    // The change in the gap: steady rhythms come out as runs of zeros.
                    times.Write7BitEncodedInt64(zigzag(scaled - lastTime - lastGap));
                    lastGap = scaled - lastTime;
                    lastTime = scaled;
                }

                void writePosition(Vector2 position)
                {
                    positions.Write7BitEncodedInt(zigzag((int)Math.Round(position.X * position_scale)));
                    positions.Write7BitEncodedInt(zigzag((int)Math.Round(position.Y * position_scale)));
                }

                w.Write7BitEncodedInt(list.Count);

                foreach (var h in list)
                {
                    var type = h switch
                    {
                        Slider => ObjectType.Slider,
                        Spinner => ObjectType.Spinner,
                        _ => ObjectType.Circle,
                    };

                    w.Write((byte)((byte)type | (h.NewCombo ? new_combo : 0)));
                    w.Write7BitEncodedInt(h.ComboOffset);
                    writeTime(h.StartTime);
                    writePosition(h.Position);
                    w.Write7BitEncodedInt(sampleIndex(h.Samples));

                    switch (h)
                    {
                        case Slider slider:
                            sliders.Write(slider.SliderVelocityMultiplier);
                            sliders.Write7BitEncodedInt(slider.RepeatCount);
                            sliders.Write(slider.Path.ExpectedDistance.Value != null);
                            if (slider.Path.ExpectedDistance.Value is double distance)
                                sliders.Write(distance);

                            sliders.Write7BitEncodedInt(slider.Path.ControlPoints.Count);

                            foreach (var point in slider.Path.ControlPoints)
                            {
                                sliders.Write(pathType(point.Type));
                                writePosition(point.Position);
                            }

                            sliders.Write7BitEncodedInt(slider.NodeSamples.Count);
                            foreach (var node in slider.NodeSamples)
                                sliders.Write7BitEncodedInt(sampleIndex(node));
                            break;

                        case Spinner spinner:
                            times.Write7BitEncodedInt64((long)Math.Round((spinner.EndTime - spinner.StartTime) * time_scale));
                            break;
                    }
                }
            }

            foreach (var writer in writers)
                writer.Dispose();

            using var result = new MemoryStream();

            using (var header = new BinaryWriter(result, Encoding.UTF8, true))
            {
                header.Write7BitEncodedInt(samples.Count);
                foreach (string key in samples)
                    header.Write(key);

                foreach (var stream in streams)
                {
                    header.Write7BitEncodedInt((int)stream.Length);
                    header.Write(stream.ToArray());
                }
            }

            return result.ToArray();
        }

        public List<OsuHitObject> Unpack()
        {
            using var stream = new MemoryStream(Objects);
            using var r = new BinaryReader(stream, Encoding.UTF8);

            var samples = new List<string>();
            int sampleCount = r.Read7BitEncodedInt();
            check(sampleCount >= 0 && sampleCount <= max_samples);

            for (int i = 0; i < sampleCount; i++)
                samples.Add(r.ReadString());

            var readers = Enumerable.Range(0, stream_count).Select(_ => new BinaryReader(new MemoryStream(r.ReadBytes(r.Read7BitEncodedInt())), Encoding.UTF8)).ToArray();
            BinaryReader meta = readers[0], times = readers[1], positions = readers[2], sliders = readers[3];

            IList<HitSampleInfo> readSamples(BinaryReader from) => parseSamples(samples[from.Read7BitEncodedInt()]);

            long lastTime = 0, lastGap = 0;

            double readTime()
            {
                lastGap += unzigzag(times.Read7BitEncodedInt64());
                lastTime += lastGap;
                return lastTime / time_scale;
            }

            Vector2 readPosition() => new Vector2(unzigzag(positions.Read7BitEncodedInt()) / position_scale, unzigzag(positions.Read7BitEncodedInt()) / position_scale);

            int count = meta.Read7BitEncodedInt();
            check(count >= 0 && count <= max_objects);
            var objects = new List<OsuHitObject>(count);

            for (int i = 0; i < count; i++)
            {
                byte header = meta.ReadByte();
                var type = (ObjectType)(header & ~new_combo);
                bool newCombo = (header & new_combo) != 0;
                int comboOffset = meta.Read7BitEncodedInt();
                double time = readTime();
                Vector2 position = readPosition();
                check(time >= -max_time && time <= max_time && Math.Abs(position.X) <= 10_000 && Math.Abs(position.Y) <= 10_000);
                var hitSamples = readSamples(meta);

                OsuHitObject h;

                switch (type)
                {
                    case ObjectType.Slider:
                        double velocity = sliders.ReadDouble();
                        int repeats = sliders.Read7BitEncodedInt();
                        double? expected = sliders.ReadBoolean() ? sliders.ReadDouble() : null;
                        check(double.IsFinite(velocity) && velocity > 0 && velocity <= 10 && repeats >= 0 && repeats <= max_repeats
                              && (expected == null || (double.IsFinite(expected.Value) && expected >= 0 && expected <= 100_000)));

                        int points = sliders.Read7BitEncodedInt();
                        check(points >= 1 && points <= max_control_points);
                        var controlPoints = new List<PathControlPoint>(points);

                        for (int p = 0; p < points; p++)
                        {
                            var pathType = readPathType(sliders.ReadByte());
                            controlPoints.Add(new PathControlPoint(readPosition(), pathType));
                        }

                        int nodes = sliders.Read7BitEncodedInt();
                        check(nodes == repeats + 2);
                        var nodeSamples = new List<IList<HitSampleInfo>>(nodes);
                        for (int n = 0; n < nodes; n++)
                            nodeSamples.Add(readSamples(sliders));

                        h = new Slider
                        {
                            Path = new SliderPath(controlPoints.ToArray(), expected),
                            RepeatCount = repeats,
                            NodeSamples = nodeSamples,
                            SliderVelocityMultiplier = velocity,
                        };
                        break;

                    case ObjectType.Spinner:
                        double duration = times.Read7BitEncodedInt64() / time_scale;
                        check(duration >= 0 && duration <= max_time);
                        h = new Spinner { EndTime = time + duration };
                        break;

                    default:
                        h = new HitCircle();
                        break;
                }

                h.StartTime = time;

                h.Position = position;
                h.NewCombo = newCombo;
                h.ComboOffset = comboOffset;
                h.Samples = hitSamples;
                objects.Add(h);
            }

            return objects;
        }

        private static long zigzag(long value) => (value << 1) ^ (value >> 63);

        private static int zigzag(int value) => (value << 1) ^ (value >> 31);

        private static long unzigzag(long value) => (value >>> 1) ^ -(value & 1);

        private static int unzigzag(int value) => (value >>> 1) ^ -(value & 1);

        private static byte pathType(PathType? type)
        {
            if (type is not PathType t)
                return 0;

            return t.Type switch
            {
                SplineType.Linear => 1,
                SplineType.PerfectCurve => 2,
                SplineType.Catmull => 4,
                SplineType.BSpline when t.Degree is int degree => (byte)(10 + degree),
                _ => 3,
            };
        }

        private static PathType? readPathType(byte value) => value switch
        {
            0 => null,
            1 => PathType.LINEAR,
            2 => PathType.PERFECT_CURVE,
            4 => PathType.CATMULL,
            >= 10 => PathType.BSpline(value - 10),
            _ => PathType.BEZIER,
        };

        private static string sampleKey(HitSampleInfo sample)
        {
            string legacy = sample is ConvertHitObjectParser.LegacyHitSampleInfo l ? $"L{l.CustomSampleBank}{(l.IsLayered ? "l" : string.Empty)}" : "P";
            return string.Join("|", sample.Name, sample.Bank, sample.Suffix ?? string.Empty, sample.Volume, sample.EditorAutoBank ? 1 : 0, legacy);
        }

        private static IList<HitSampleInfo> parseSamples(string key)
        {
            if (key.Length == 0)
                return new List<HitSampleInfo>();

            return key.Split(';').Select(s =>
            {
                string[] parts = s.Split('|');
                string name = parts[0], bank = parts[1];
                string? suffix = parts[2].Length == 0 ? null : parts[2];
                int volume = int.Parse(parts[3]);
                bool autoBank = parts[4] == "1";
                string legacy = parts[5];

                if (legacy.StartsWith('L'))
                {
                    bool layered = legacy.EndsWith('l');
                    int customBank = int.Parse(legacy[1..].TrimEnd('l'));
                    return (HitSampleInfo)new ConvertHitObjectParser.LegacyHitSampleInfo(name, bank, volume, autoBank, customBank, layered);
                }

                return new HitSampleInfo(name, bank, suffix, volume, autoBank);
            }).ToList();
        }

        #endregion
    }
}
