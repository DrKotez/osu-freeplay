// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
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

        public int MaxCombo => Tracks.Count == 0 ? 0 : Tracks.Max(t => t.MaxCombo);

        public int Judged => Great + Ok + Meh + Misses;

        /// <summary>
        /// osu!-style accuracy across every judged object in the run.
        /// </summary>
        public double Accuracy => Judged == 0 ? 1 : (Great * 300 + Ok * 100 + Meh * 50) / (Judged * 300.0);
    }

    public class TrackRecord
    {
        public string Artist { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;

        /// <summary>
        /// The seed the track's objects were generated with.
        /// </summary>
        public int Seed { get; set; }

        public double PlayedMs { get; set; }

        public bool Completed { get; set; }

        public int Great { get; set; }
        public int Ok { get; set; }
        public int Meh { get; set; }
        public int Misses { get; set; }

        public int MaxCombo { get; set; }

        public long Score { get; set; }
    }

    public enum RunEndReason
    {
        Failed,
        Quit,
        TimeLimit,
        OutOfTracks,
    }
}
