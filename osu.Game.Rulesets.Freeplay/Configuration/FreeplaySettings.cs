// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.ComponentModel;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Freeplay.Stable;

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

        /// <summary>
        /// Each mode's own settings, by <see cref="FreeplayMode.Id"/>. Use <see cref="GetMode"/>, which fills in defaults.
        /// </summary>
        public Dictionary<string, FreeplayGenerationSettings> Modes { get; set; } = new Dictionary<string, FreeplayGenerationSettings>();

        /// <summary>
        /// The mode last chosen in the menu.
        /// </summary>
        public string SelectedMode { get; set; } = FreeplayMode.ALL[0].Id;

        public TrackSelectionSettings Tracks { get; set; } = new TrackSelectionSettings();

        public FailSettings Fail { get; set; } = new FailSettings();

        public TimingDisplaySettings Timing { get; set; } = new TimingDisplaySettings();

        public StableSettings Stable { get; set; } = new StableSettings();

        public PatternSourceSettings PatternSources { get; set; } = new PatternSourceSettings();

        public static void SetCurrent(FreeplaySettings settings) => Current = settings;

        /// <summary>
        /// The settings of <paramref name="mode"/>, created from its defaults the first time.
        /// </summary>
        public FreeplayGenerationSettings GetMode(FreeplayMode mode)
        {
            if (!Modes.TryGetValue(mode.Id, out var settings))
                Modes[mode.Id] = settings = mode.CreateDefaults();

            // What the mode is can't be changed by its settings (and older saves may lack it).
            var defaults = mode.CreateDefaults();
            settings.Style = defaults.Style;
            settings.RhythmSource = defaults.RhythmSource;

            return settings;
        }

        /// <summary>
        /// Resets <paramref name="mode"/> to its defaults.
        /// </summary>
        public FreeplayGenerationSettings ResetMode(FreeplayMode mode) => Modes[mode.Id] = mode.CreateDefaults();
    }

    /// <summary>
    /// The hit error bar and unstable rate freeplay shows over the playfield (see <see cref="Screens.FreeplayTimingOverlay"/>).
    /// </summary>
    public class TimingDisplaySettings
    {
        public bool ShowHitErrorBar { get; set; } = true;

        public bool ShowUnstableRate { get; set; } = true;

        public double Scale { get; set; } = 1.5;

        public double Opacity { get; set; } = 1;
    }

    public class StableSettings
    {
        /// <summary>
        /// The osu!stable install folder, containing Songs, Skins and collection.db. Empty finds it automatically
        /// (see <see cref="StableLibrary.FindInstall"/>), which is what lets a copy of freeplay~ work on another computer.
        /// </summary>
        public string? Path { get; set; }

        /// <summary>
        /// The folder name of the osu!stable skin to use, or null for the built-in skin.
        /// </summary>
        public string? Skin { get; set; }
    }

    /// <summary>
    /// osu!stable collections whose beatmaps are used as examples of patterns, by kind.
    /// Null (or a missing collection) uses the generator's built-in shapes.
    /// </summary>
    public class PatternSourceSettings
    {
        public string? Aim { get; set; } = "! #asd - Zentoro - Consi";
        public string? Streams { get; set; } = "BPM 170-179";
        public string? Tech { get; set; } = "techno";
    }

    public class TrackSelectionSettings
    {
        public TrackSource Source { get; set; } = TrackSource.Library;

        /// <summary>
        /// The name of the osu!stable collection used when <see cref="Source"/> is <see cref="TrackSource.Collection"/>.
        /// </summary>
        public string? CollectionName { get; set; }

        // Zero means no limit.
        public double MinLengthSeconds { get; set; } = 60;
        public double MaxLengthSeconds { get; set; }

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
