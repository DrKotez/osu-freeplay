// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using osuTK;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    /// <summary>
    /// Generates the tracks a player marked again, with the generator as it is now, and compares them with the maps they played.
    /// Reads the feedback folder of the local portable copy (or <c>FREEPLAY_FEEDBACK</c>). Run by hand.
    /// </summary>
    [TestFixture]
    public class FeedbackReplayReport
    {
        [Explicit]
        [Test]
        public void Report()
        {
            string folder = Environment.GetEnvironmentVariable("FREEPLAY_FEEDBACK")
                            ?? Path.Combine(FreeplayGenerationTest.RepositoryRoot, "dist", "freeplay~", "freeplay", FreeplayStorage.FEEDBACK_DIRECTORY);

            if (!Directory.Exists(folder))
                Assert.Ignore($"No feedback at {folder}.");

            if (Directory.Exists(Path.Combine(osu.Game.Rulesets.Freeplay.Stable.StableLibrary.DEFAULT_PATH, "Songs")))
                MinedPatternSet.Current = RealLibraryReport.LocalPatterns();

            var serializer = new JsonSerializerSettings { Converters = { new StringEnumConverter() } };

            TestContext.Out.WriteLine("track                          | marks                    | off music  old→new | silences/min old→new | same measures old→new | stars old→new");

            foreach (string entryFolder in Directory.GetDirectories(folder).Order())
            {
                string source = Path.Combine(entryFolder, "source.osu");
                string played = Path.Combine(entryFolder, "generated.osu");

                if (!File.Exists(source) || !File.Exists(played))
                    continue;

                var entry = JsonConvert.DeserializeObject<FeedbackEntry>(File.ReadAllText(Path.Combine(entryFolder, "feedback.json")), serializer)!;

                IBeatmap sourceMap = null!;
                double nowOff = 0, nowSilent = 0, nowSame = 0, nowStars = 0, nowPauses = 0, nowRun = 0, nowDoubles = 0, nowLoose = 0;
                const int seeds = 5;

                // Several seeds, as one map says little about how varied the generator is.
                for (int k = 0; k < seeds; k++)
                {
                    var settings = (entry.Settings ?? new FreeplayGenerationSettings()).Clone();
                    settings.Palette = Environment.GetEnvironmentVariable("FREEPLAY_NO_RECIPES") != null
                        ? new TrackPalette { MapperSpacing = 0, Accents = 0, Character = 0, Territory = 0, Flow = 0, Crescendo = 0, Flavor = 0, Repeats = 0, Contrast = 0, Spices = 0 }
                        : TrackPalette.Roll(new Random(entry.Seed + k));
                    settings.Seed = entry.Seed + k;

                    var now = FreeplayGenerationTest.generate(source, settings, out sourceMap, calibrate: true);
                    var (off, silent) = rhythm(now, sourceMap);

                    nowOff += off / seeds;
                    nowSilent += silent / seeds;
                    nowSame += VarietyReport.MeasureGenerated(now).SameMeasures / seeds;
                    nowStars += ((Beatmaps.FreeplayBeatmap)now).StarRating / seeds;
                    nowPauses += RealLibraryReport.Pauses(now).perMinute / seeds;
                    nowRun += RealLibraryReport.Pauses(now).averageRun / seeds;
                    var (doubles, loose) = doublesIn(now);
                    nowDoubles += doubles / seeds;
                    nowLoose += loose / seeds;
                }

                var then = decode(played);
                var (thenOff, thenSilent) = rhythm(then, sourceMap);

                string marks = string.Join(",", entry.Flags.Select(f => f.ToString())) + (entry.Rating is FeedbackRating r ? $" {r}" : string.Empty);

                TestContext.Out.WriteLine($"{trim(entry.Title, 30),-30} | {trim(marks, 24),-24} | {thenOff,8:P0} → {nowOff,-6:P0} | {thenSilent,9:0.0} → {nowSilent,-8:0.0} | "
                                          + $"{VarietyReport.MeasureGenerated(then).SameMeasures,10:P0} → {nowSame,-8:P0} | {entry.ActualStars:0.00} → {nowStars:0.00} | pauses/min {RealLibraryReport.Pauses(then).perMinute:0} → {nowPauses:0}, run {RealLibraryReport.Pauses(then).averageRun:0.0} → {nowRun:0.0} | doubles/min {doublesIn(then).perMinute:0.0} → {nowDoubles:0.0} (not stacked {doublesIn(then).loose:0.0} → {nowLoose:0.0})");
            }
        }

        /// <summary>
        /// The stars mode's marked tracks then and now: stars, how fast the cursor must move (the 90th percentile, px/ms) and how evenly
        /// hard the track is (the spread of that speed across sixteen parts of it: lower is steadier).
        /// </summary>
        [Explicit]
        [Test]
        public void StarsReport()
        {
            string folder = Environment.GetEnvironmentVariable("FREEPLAY_FEEDBACK")
                            ?? Path.Combine(FreeplayGenerationTest.RepositoryRoot, "dist", "freeplay~", "freeplay", FreeplayStorage.FEEDBACK_DIRECTORY);

            if (!Directory.Exists(folder))
                Assert.Ignore($"No feedback at {folder}.");

            if (Directory.Exists(Path.Combine(osu.Game.Rulesets.Freeplay.Stable.StableLibrary.DEFAULT_PATH, "Songs")))
                MinedPatternSet.Current = RealLibraryReport.LocalPatterns();

            var serializer = new JsonSerializerSettings { Converters = { new StringEnumConverter() } };

            foreach (string entryFolder in Directory.GetDirectories(folder).Order())
            {
                string source = Path.Combine(entryFolder, "source.osu");
                string played = Path.Combine(entryFolder, "generated.osu");

                if (!File.Exists(source) || !File.Exists(played))
                    continue;

                var entry = JsonConvert.DeserializeObject<FeedbackEntry>(File.ReadAllText(Path.Combine(entryFolder, "feedback.json")), serializer)!;

                if (entry.Settings?.Style != PatternStyle.Metronome)
                    continue;

                var settings = entry.Settings.Clone();
                settings.Seed = entry.Seed;

                // Sped up by Double Time: older entries only have the BPM as played, over the map's own.
                var then = decode(played);
                double speed = entry.Speed > 1 ? entry.Speed : Math.Round(entry.Bpm / (60000 / then.ControlPointInfo.TimingPoints[0].BeatLength), 2);
                if (speed < 1.01)
                    speed = 1;
                settings.GeneratedForSpeed = speed;

                var now = FreeplayGenerationTest.generate(source, settings, out _, calibrate: true);

                var (thenSpeed, thenSpread, thenRate) = speeds(then, speed);
                var (nowSpeed, nowSpread, nowRate) = speeds(now, speed);

                TestContext.Out.WriteLine($"{trim(entry.Title, 28),-28} x{speed:0.00} {entry.Rating?.ToString() ?? "",-9} {trim(entry.Comment ?? "", 24),-24} | stars {entry.TargetStars:0.0}: {entry.ActualStars:0.00} → {((Beatmaps.FreeplayBeatmap)now).StarRating:0.00}"
                                          + $" | speed p90 {thenSpeed:0.00} → {nowSpeed:0.00} px/ms | spread {thenSpread:0.00} → {nowSpread:0.00} | jumps/s {thenRate:0.0} → {nowRate:0.0}");
            }
        }

        private static (double p90, double spread, double rate) speeds(IBeatmap map, double clockRate)
        {
            var o = map.HitObjects.OfType<OsuHitObject>().Where(h => h is not Spinner).OrderBy(h => h.StartTime).ToList();
            var speed = o.Zip(o.Skip(1), (a, b) => (time: b.StartTime, v: Vector2.Distance(a.StackedEndPosition, b.StackedPosition) / Math.Max(1, b.StartTime - a.GetEndTime()) * clockRate)).ToList();

            if (speed.Count < 32)
                return (0, 0, 0);

            var sorted = speed.Select(s => s.v).Order().ToList();
            double start = speed[0].time, length = speed[^1].time - start;
            var parts = Enumerable.Range(0, 16).Select(p => speed.Where(s => s.time >= start + length * p / 16 && s.time < start + length * (p + 1) / 16).Select(s => s.v).DefaultIfEmpty(0).Average()).ToList();
            double mean = parts.Average();

            return (sorted[(int)(sorted.Count * 0.9)], Math.Sqrt(parts.Average(p => (p - mean) * (p - mean))) / Math.Max(0.001, mean), o.Count / Math.Max(1, length / 1000) * clockRate);
        }

        /// <summary>
        /// Lone 1/4 doubles per minute, and how many of those per minute aren't on one spot.
        /// </summary>
        private static (double perMinute, double loose) doublesIn(IBeatmap map)
        {
            var o = map.HitObjects.OfType<OsuHitObject>().Where(h => h is not Spinner).OrderBy(h => h.StartTime).ToList();
            int doubles = 0, loose = 0;

            for (int i = 1; i < o.Count; i++)
            {
                double beat = map.ControlPointInfo.TimingPointAt(o[i].StartTime).BeatLength;
                bool quick(int a, int b) => a >= 0 && b < o.Count && (o[b].StartTime - o[a].GetEndTime()) / beat < 0.375;

                if (!quick(i - 1, i) || quick(i - 2, i - 1) || quick(i, i + 1))
                    continue;

                doubles++;
                if (osuTK.Vector2.Distance(o[i].Position, o[i - 1].Position) > 2)
                    loose++;
            }

            double minutes = Math.Max(0.5, (o[^1].StartTime - o[0].StartTime) / 60000);
            return (doubles / minutes, loose / minutes);
        }

        private static string trim(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

        private static IBeatmap decode(string path)
        {
            using (var stream = new LineBufferedReader(File.OpenRead(path)))
                return new osu.Game.Tests.Beatmaps.TestWorkingBeatmap(Decoder.GetDecoder<Beatmap>(stream).Decode(stream)).GetPlayableBeatmap(new osu.Game.Rulesets.Osu.OsuRuleset().RulesetInfo);
        }

        private static (double offMusic, double silencesPerMinute) rhythm(IBeatmap generated, IBeatmap source)
        {
            var objects = generated.HitObjects.Where(h => h is not Spinner && h is not osu.Game.Rulesets.Objects.Types.IHasDuration { Duration: > 3000 }).OrderBy(h => h.StartTime).ToList();

            bool heard(double time) => source.HitObjects.Any(h => Math.Abs(h.StartTime - time) < 6 || (time > h.StartTime && time < h.GetEndTime() + 6));

            int silences = 0;

            for (int i = 1; i < objects.Count; i++)
            {
                double beat = generated.ControlPointInfo.TimingPointAt(objects[i].StartTime).BeatLength;
                double from = objects[i - 1].GetEndTime(), to = objects[i].StartTime;

                if (to - from >= beat * 2 - 5 && source.HitObjects.Count(h => h.StartTime > from + 5 && h.StartTime < to - 5) >= 3)
                    silences++;
            }

            double minutes = Math.Max(0.5, (objects[^1].StartTime - objects[0].StartTime) / 60000);
            return (objects.Count(h => !heard(h.StartTime)) / (double)objects.Count, silences / minutes);
        }
    }
}
