// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Audio;
using osu.Framework.Configuration;
using osu.Framework.Input.Bindings;
using osu.Framework.Logging;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Osu;

namespace osu.Game.Rulesets.Freeplay.Stable
{
    /// <summary>
    /// Copies an osu!stable player's own settings (volume, offset, dim, cursor, skin, keys...) into freeplay~,
    /// so whoever plays feels at home without setting anything up.
    /// </summary>
    public class StableSettingsImport
    {
        private readonly OsuConfigManager config;
        private readonly FrameworkConfigManager frameworkConfig;
        private readonly AudioManager audio;
        private readonly RealmAccess realm;

        public StableSettingsImport(OsuConfigManager config, FrameworkConfigManager frameworkConfig, AudioManager audio, RealmAccess realm)
        {
            this.config = config;
            this.frameworkConfig = frameworkConfig;
            this.audio = audio;
            this.realm = realm;
        }

        /// <summary>
        /// Applies what <paramref name="stable"/> sets.
        /// </summary>
        /// <returns>What was taken over, in words, for showing to the player.</returns>
        public List<string> Apply(StableConfig stable, FreeplaySettings settings, bool force = false)
        {
            var taken = new List<string>();
            var imported = settings.Stable.Imported;

            // Only what changed in osu!stable since it was last taken: what the player changed here since stays as they set it.
            bool changed(params string[] keys)
            {
                bool any = false;

                foreach (string key in keys)
                {
                    string? raw = stable.GetString(key);

                    if (raw == null || (!force && imported.TryGetValue(key, out string? last) && last == raw))
                        continue;

                    imported[key] = raw;
                    any = true;
                }

                return any;
            }

            void percent(string key, Action<double> apply, string description)
            {
                if (changed(key) && stable.GetDouble(key) is double value)
                {
                    apply(Math.Clamp(value / 100, 0, 1));
                    taken.Add(description);
                }
            }

            percent("VolumeUniversal", v => audio.Volume.Value = v, "громкость");
            percent("VolumeMusic", v => audio.VolumeTrack.Value = v, "громкость музыки");
            percent("VolumeEffect", v => audio.VolumeSample.Value = v, "громкость эффектов");
            percent("DimLevel", v => config.SetValue(OsuSetting.DimLevel, v), "затемнение фона");

            if (changed("Offset") && stable.GetDouble("Offset") is double offset)
            {
                config.SetValue(OsuSetting.AudioOffset, Math.Clamp(offset, -500, 500));
                taken.Add("оффсет");
            }

            if (changed("CursorSize") && stable.GetDouble("CursorSize") is double cursorSize)
            {
                config.SetValue(OsuSetting.GameplayCursorSize, (float)Math.Clamp(cursorSize, 0.1, 2));
                taken.Add("размер курсора");
            }

            if (changed("AutomaticCursorSizing"))
                setBool(stable, "AutomaticCursorSizing", OsuSetting.AutoCursorSize, taken, null);
            if (changed("HitLighting"))
                setBool(stable, "HitLighting", OsuSetting.HitLighting, taken, "вспышки");
            if (changed("FpsCounter"))
                setBool(stable, "FpsCounter", OsuSetting.ShowFpsDisplay, taken, "счётчик FPS");

            if (changed("ShowInterface") && stable.GetBool("ShowInterface") is bool showInterface)
            {
                config.SetValue(OsuSetting.HUDVisibilityMode, showInterface ? HUDVisibilityMode.Always : HUDVisibilityMode.Never);
                taken.Add("интерфейс в игре");
            }

            if (changed("Fullscreen") && stable.GetBool("Fullscreen") is bool fullscreen)
            {
                frameworkConfig.SetValue(FrameworkSetting.WindowMode, fullscreen ? WindowMode.Fullscreen : WindowMode.Windowed);
                taken.Add("полный экран");
            }

            if (changed("FrameSync") && stable.GetString("FrameSync") is string frameSync && parseFrameSync(frameSync) is FrameSync sync)
            {
                frameworkConfig.SetValue(FrameworkSetting.FrameSync, sync);
                taken.Add("ограничение FPS");
            }

            if (changed("ScoreMeter", "ScoreMeterScale") && stable.GetString("ScoreMeter") is string scoreMeter)
            {
                settings.Timing.ShowHitErrorBar = !scoreMeter.Equals("None", StringComparison.OrdinalIgnoreCase);

                if (stable.GetDouble("ScoreMeterScale") is double scale)
                    settings.Timing.Scale = Math.Clamp(scale, 0.5, 4);

                taken.Add("шкала попаданий");
            }

            if (changed("Skin") && stable.GetString("Skin") is string skin)
            {
                settings.Stable.Skin = skin;
                taken.Add("скин");
            }

            bool keys = false;
            if (changed("keyOsuLeft")) keys |= setRulesetKey(stable.GetString("keyOsuLeft"), OsuAction.LeftButton);
            if (changed("keyOsuRight")) keys |= setRulesetKey(stable.GetString("keyOsuRight"), OsuAction.RightButton);
            if (changed("keyOsuSmoke")) keys |= setRulesetKey(stable.GetString("keyOsuSmoke"), OsuAction.Smoke);
            if (changed("keyPause")) keys |= setGlobalKey(stable.GetString("keyPause"), GlobalAction.PauseGameplay);

            if (keys)
                taken.Add("клавиши");

            Logger.Log($"Freeplay took osu!stable's settings from {stable.FilePath}: {string.Join(", ", taken)}.");
            return taken;
        }

        /// <summary>
        /// Where the player's songs are, if stable is set to a folder other than its own Songs folder.
        /// </summary>
        public static string? SongsPath(StableConfig stable, string stablePath)
        {
            string? directory = stable.GetString("BeatmapDirectory");

            if (directory == null || directory.Equals("Songs", StringComparison.OrdinalIgnoreCase))
                return null;

            string path = Path.IsPathRooted(directory) ? directory : Path.Combine(stablePath, directory);
            return Directory.Exists(path) ? path : null;
        }

        private void setBool(StableConfig stable, string key, OsuSetting setting, List<string> taken, string? description)
        {
            if (stable.GetBool(key) is not bool value)
                return;

            config.SetValue(setting, value);

            if (description != null)
                taken.Add(description);
        }

        private static FrameSync? parseFrameSync(string value)
        {
            if (value.Equals("VSync", StringComparison.OrdinalIgnoreCase))
                return FrameSync.VSync;

            if (value.Contains("Unlimited", StringComparison.OrdinalIgnoreCase))
                return FrameSync.Unlimited;

            // Stable's fixed limits (120, 240, custom) are closest to a multiple of the display's rate.
            if (value.StartsWith("Limit", StringComparison.OrdinalIgnoreCase))
                return Enum.TryParse<FrameSync>("Limit2x", out var limited) ? limited : null;

            return null;
        }

        /// <summary>
        /// Converts a key name from stable's config (.NET's Keys names, e.g. "Z", "D1", "LShiftKey", "Oemcomma").
        /// </summary>
        private static InputKey? parseKey(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["LShiftKey"] = "LShift",
                ["RShiftKey"] = "RShift",
                ["ShiftKey"] = "LShift",
                ["LControlKey"] = "LControl",
                ["RControlKey"] = "RControl",
                ["ControlKey"] = "LControl",
                ["LMenu"] = "LAlt",
                ["RMenu"] = "RAlt",
                ["Menu"] = "LAlt",
                ["Return"] = "Enter",
                ["Back"] = "BackSpace",
                ["Capital"] = "CapsLock",
                ["Oemtilde"] = "Tilde",
                ["Oem3"] = "Tilde",
                ["OemSemicolon"] = "Semicolon",
                ["Oem1"] = "Semicolon",
                ["Oemcomma"] = "Comma",
                ["OemPeriod"] = "Period",
                ["OemQuestion"] = "Slash",
                ["Oem2"] = "Slash",
                ["OemOpenBrackets"] = "BracketLeft",
                ["Oem4"] = "BracketLeft",
                ["Oem6"] = "BracketRight",
                ["OemCloseBrackets"] = "BracketRight",
                ["OemQuotes"] = "Quote",
                ["Oem7"] = "Quote",
                ["OemPipe"] = "BackSlash",
                ["Oem5"] = "BackSlash",
                ["OemMinus"] = "Minus",
                ["Oemplus"] = "Plus",
            };

            if (renames.TryGetValue(name, out string? renamed))
                name = renamed;
            else if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
                name = $"Number{name[1]}";
            else if (name.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase))
                name = $"Keypad{name[6..]}";

            return Enum.TryParse<InputKey>(name, true, out var key) && key != InputKey.None ? key : null;
        }

        private bool setRulesetKey(string? stableKey, OsuAction action)
        {
            if (parseKey(stableKey) is not InputKey key)
                return false;

            writeBinding(FreeplayRuleset.SHORT_NAME, (int)action, new KeyCombination(key), keyboardOnly: true);
            return true;
        }

        private bool setGlobalKey(string? stableKey, GlobalAction action)
        {
            if (parseKey(stableKey) is not InputKey key)
                return false;

            writeBinding(null, (int)action, new KeyCombination(key), keyboardOnly: false);
            return true;
        }

        /// <summary>
        /// Replaces an action's first (keyboard) binding, leaving mouse buttons bound alongside it alone.
        /// </summary>
        private void writeBinding(string? ruleset, int action, KeyCombination combination, bool keyboardOnly) => realm.Write(r =>
        {
            var existing = r.All<RealmKeyBinding>()
                            .Where(b => b.RulesetName == ruleset)
                            .AsEnumerable()
                            .FirstOrDefault(b => b.ActionInt == action && (ruleset == null || b.Variant == 0) && (!keyboardOnly || !b.KeyCombination.Keys.Any(k => k >= InputKey.FirstMouseButton)));

            if (existing != null)
                existing.KeyCombinationString = combination.ToString();
            else
                r.Add(new RealmKeyBinding(action, combination, ruleset, ruleset == null ? null : 0));
        });
    }
}
