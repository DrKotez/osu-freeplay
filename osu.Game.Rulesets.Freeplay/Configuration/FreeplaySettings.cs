// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// The star rating the player is comfortable at. Goals (see <see cref="AimGoal"/>) set their stars relative to it.
        /// </summary>
        public double PlayerLevel { get; set; } = 5.5;

        public TrackSelectionSettings Tracks { get; set; } = new TrackSelectionSettings();

        public FailSettings Fail { get; set; } = new FailSettings();

        public TimingDisplaySettings Timing { get; set; } = new TimingDisplaySettings();

        public StableSettings Stable { get; set; } = new StableSettings();

        public PatternSourceSettings PatternSources { get; set; } = new PatternSourceSettings();

        /// <summary>
        /// The osu! mods every run is played with.
        /// </summary>
        public FreeplayModFlags Mods { get; set; }

        /// <summary>
        /// Double Time's speed (osu!lazer lets it be set from 1.01× to 2×).
        /// </summary>
        public double DoubleTimeSpeed { get; set; } = FreeplayMods.DEFAULT_SPEED;


        /// <summary>
        /// Each mode's saved presets (by mode id).
        /// </summary>
        public Dictionary<string, List<ModePreset>> Presets { get; set; } = new Dictionary<string, List<ModePreset>>();

        /// <summary>
        /// <paramref name="mode"/>'s presets, in the order saved.
        /// </summary>
        public List<ModePreset> PresetsOf(FreeplayMode mode)
        {
            if (!Presets.TryGetValue(mode.Id, out var presets))
                Presets[mode.Id] = presets = new List<ModePreset>();

            return presets;
        }

        /// <summary>
        /// Saves <paramref name="mode"/>'s current settings as <paramref name="name"/> (replacing a preset of that name).
        /// </summary>
        public ModePreset SavePreset(FreeplayMode mode, string name)
        {
            var presets = PresetsOf(mode);
            var preset = new ModePreset { Name = name.Trim(), Settings = ModePreset.Copy(GetMode(mode)) };

            int existing = presets.FindIndex(p => p.Name == preset.Name);

            if (existing >= 0)
                presets[existing] = preset;
            else
                presets.Add(preset);

            return preset;
        }

        /// <summary>
        /// Makes <paramref name="preset"/>'s settings <paramref name="mode"/>'s current ones.
        /// </summary>
        public FreeplayGenerationSettings ApplyPreset(FreeplayMode mode, ModePreset preset)
        {
            Modes[mode.Id] = ModePreset.Copy(preset.Settings);
            return GetMode(mode);
        }

        public void DeletePreset(FreeplayMode mode, ModePreset preset) => PresetsOf(mode).Remove(preset);

        /// <summary>
        /// The preset <paramref name="mode"/>'s settings are exactly now, or null if they're not any (or were changed since).
        /// </summary>
        public ModePreset? ActivePreset(FreeplayMode mode)
        {
            if (!Presets.TryGetValue(mode.Id, out var presets) || presets.Count == 0)
                return null;

            string current = ModePreset.Saved(GetMode(mode));
            return presets.FirstOrDefault(p => ModePreset.Saved(p.Settings) == current);
        }

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

            // New settings, and saves from before goals, start from the default goal.
            if (mode.HasGoals && settings.Goal == null)
                AimGoals.Apply(settings, AimGoal.Angles, PlayerLevel);

            return settings;
        }

        /// <summary>
        /// Resets <paramref name="mode"/> to its defaults.
        /// </summary>
        public FreeplayGenerationSettings ResetMode(FreeplayMode mode)
        {
            Modes.Remove(mode.Id);
            return GetMode(mode);
        }
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

        /// <summary>
        /// Take the player's osu!stable settings (volume, offset, dim, cursor, skin, keys...) each time freeplay~ starts.
        /// </summary>
        public bool SyncWithStable { get; set; } = true;

        /// <summary>
        /// The osu!stable values last taken, by key, so a later start only takes what changed in stable since
        /// (and keeps what the player changed here, like the cursor size or volume).
        /// </summary>
        public Dictionary<string, string> Imported { get; set; } = new Dictionary<string, string>();
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

        /// <summary>
        /// Jump patterns for the stars mode. Null takes the aim ones.
        /// </summary>
        public string? Stars { get; set; }
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
        /// The fail conditions of the run being played, for gameplay processors that can't be handed them directly.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public static FailSettings? Active { get; set; }

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
