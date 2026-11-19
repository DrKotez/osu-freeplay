// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
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
                double nowOff = 0, nowSilent = 0, nowSame = 0, nowStars = 0, nowPauses = 0, nowRun = 0;
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
                }

                var then = decode(played);
                var (thenOff, thenSilent) = rhythm(then, sourceMap);

                string marks = string.Join(",", entry.Flags.Select(f => f.ToString())) + (entry.Rating is FeedbackRating r ? $" {r}" : string.Empty);

                TestContext.Out.WriteLine($"{trim(entry.Title, 30),-30} | {trim(marks, 24),-24} | {thenOff,8:P0} → {nowOff,-6:P0} | {thenSilent,9:0.0} → {nowSilent,-8:0.0} | "
                                          + $"{VarietyReport.MeasureGenerated(then).SameMeasures,10:P0} → {nowSame,-8:P0} | {entry.ActualStars:0.00} → {nowStars:0.00} | pauses/min {RealLibraryReport.Pauses(then).perMinute:0} → {nowPauses:0}, run {RealLibraryReport.Pauses(then).averageRun:0.0} → {nowRun:0.0}");
            }
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
