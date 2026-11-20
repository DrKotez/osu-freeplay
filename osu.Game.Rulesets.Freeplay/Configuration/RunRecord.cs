// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Extensions;
using osu.Game.Rulesets.Freeplay.Generation;

namespace osu.Game.Rulesets.Freeplay.Configuration
{
    /// <summary>
    /// The result of a single freeplay run (a marathon of tracks until failure or exit).
    /// </summary>
    public class RunRecord
    {
        public DateTimeOffset Date { get; set; } = DateTimeOffset.Now;

        public RunEndReason EndReason { get; set; }

        public double StarRating { get; set; }

        public PatternStyle Style { get; set; }

        /// <summary>
        /// The name of the preset the run was played with, or null for custom settings.
        /// </summary>
        public string? Preset { get; set; }

        [JsonIgnore]
        public string StyleName => Preset ?? Style.GetDescription();

        /// <summary>
        /// The osu! mods the run was played with, e.g. "Hard Rock · No Fail", or null.
        /// </summary>
        public string? Mods { get; set; }

        public List<TrackRecord> Tracks { get; set; } = new List<TrackRecord>();

        /// <summary>
        /// Health sampled once per second across the whole run, for the summary graph.
        /// </summary>
        public List<float> HealthGraph { get; set; } = new List<float>();

        public double SurvivedMs => Tracks.Sum(t => t.PlayedMs);

        public int TracksCompleted => Tracks.Count(t => t.Completed);

        public int Great => Tracks.Sum(t => t.Great);
        public int Ok => Tracks.Sum(t => t.Ok);
        public int Meh => Tracks.Sum(t => t.Meh);
        public int Misses => Tracks.Sum(t => t.Misses);

        /// <summary>
        /// Where the clicks of the whole run went, or null if no track was analysed.
        /// </summary>
        [JsonIgnore]
        public AimAnalysis? Aim => AimAnalysis.Combine(Tracks.Select(t => t.Aim));

        public int MaxCombo => Tracks.Count == 0 ? 0 : Tracks.Max(t => t.MaxCombo);

        public int Judged => Great + Ok + Meh + Misses;

        /// <summary>
        /// osu!-style accuracy across every judged object in the run.
        /// </summary>
        public double Accuracy => Judged == 0 ? 1 : (Great * 300 + Ok * 100 + Meh * 50) / (Judged * 300.0);

        /// <summary>
        /// The unstable rate across the run, weighted by how many objects each track had, or null if no track had one.
        /// </summary>
        [JsonIgnore]
        public double? UnstableRate
        {
            get
            {
                var rated = Tracks.Where(t => t.UnstableRate != null && t.Great + t.Ok + t.Meh > 0).ToList();

                if (rated.Count == 0)
                    return null;

                return rated.Sum(t => t.UnstableRate!.Value * (t.Great + t.Ok + t.Meh)) / rated.Sum(t => t.Great + t.Ok + t.Meh);
            }
        }
    }

    public class TrackRecord
    {
        /// <summary>
        /// The generated map, kept while the run is shown so feedback can save it. Not saved with the history.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public osu.Game.Beatmaps.IBeatmap? Generated { get; set; }

        public string Artist { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;

        /// <summary>
        /// The seed the track's objects were generated with.
        /// </summary>
        public int Seed { get; set; }

        /// <summary>
        /// The real osu! star rating of what was played.
        /// </summary>
        public double StarRating { get; set; }

        /// <summary>
        /// The pp an SS of the map gives with the run's mods, for a pp target (0 otherwise).
        /// </summary>
        public double Performance { get; set; }

        public double Bpm { get; set; }

        /// <summary>
        /// How evenly the track was hit (osu!'s unstable rate), or null if too little was hit to tell.
        /// </summary>
        public double? UnstableRate { get; set; }

        public double PlayedMs { get; set; }

        public bool Completed { get; set; }

        public int Great { get; set; }
        public int Ok { get; set; }
        public int Meh { get; set; }
        public int Misses { get; set; }

        public int MaxCombo { get; set; }

        public long Score { get; set; }

        /// <summary>
        /// Where the clicks went (in modes which analyse them), or null.
        /// </summary>
        public AimAnalysis? Aim { get; set; }
    }

    public enum RunEndReason
    {
        Failed,
        Quit,
        TimeLimit,
        OutOfTracks,

        /// <summary>
        /// A speed-up run reached the fastest track there is.
        /// </summary>
        RampDone,

        /// <summary>
        /// A seed's map was played to the end.
        /// </summary>
        SeedDone,
    }
}
