// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.ComponentModel;
using osu.Game.Rulesets.Freeplay.Generation;

namespace osu.Game.Rulesets.Freeplay.Configuration
{
    /// <summary>
    /// Everything the player can configure for a freeplay run. Persisted as JSON by <see cref="FreeplayStorage"/>.
    /// </summary>
    public class FreeplaySettings
    {
        /// <summary>
        /// The settings used by gameplay processors (e.g. health) that can't be handed settings directly.
        /// </summary>
        public static FreeplaySettings Current { get; private set; } = new FreeplaySettings();

        public FreeplayGenerationSettings Generation { get; set; } = new FreeplayGenerationSettings();

        public TrackSelectionSettings Tracks { get; set; } = new TrackSelectionSettings();

        public FailSettings Fail { get; set; } = new FailSettings();

        public static void SetCurrent(FreeplaySettings settings)
        {
            Current = settings;
            FreeplayGenerationSettings.Current = settings.Generation;
        }
    }

    public class TrackSelectionSettings
    {
        public TrackSource Source { get; set; } = TrackSource.Library;

        /// <summary>
        /// The <see cref="Collections.BeatmapCollection"/> used when <see cref="Source"/> is <see cref="TrackSource.Collection"/>.
        /// </summary>
        public Guid? CollectionID { get; set; }

        // Zero means no limit.
        public double MinLengthSeconds { get; set; } = 60;
        public double MaxLengthSeconds { get; set; }
        public double MinBpm { get; set; }
        public double MaxBpm { get; set; }

        /// <summary>
        /// Avoid replaying a track until this many others have been played (limited by library size).
        /// </summary>
        public int NoRepeatCount { get; set; } = 20;
    }

    public enum TrackSource
    {
        [Description("Вся библиотека")]
        Library,
        [Description("Коллекция")]
        Collection,
    }

    public class FailSettings
    {
        /// <summary>
        /// Fail when health reaches zero.
        /// </summary>
        public bool Health { get; set; } = true;

        /// <summary>
        /// Health carries over between tracks (with <see cref="HealthRefill"/> added) instead of resetting.
        /// </summary>
        public bool CarryHealth { get; set; } = true;

        public double HealthRefill { get; set; } = 0.3;

        /// <summary>
        /// Fail after this many misses across the whole run. Zero disables. One is sudden death.
        /// </summary>
        public int MissLimit { get; set; }

        /// <summary>
        /// Fail when accuracy drops below this (0-1) after the first objects. Zero disables.
        /// </summary>
        public double MinAccuracy { get; set; }

        /// <summary>
        /// End the run (as a success) after this many minutes. Zero disables.
        /// </summary>
        public double TimeLimitMinutes { get; set; }
    }
}
