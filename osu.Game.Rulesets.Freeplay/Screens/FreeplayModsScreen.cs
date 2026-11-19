// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Freeplay.Configuration;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// The osu! mods runs are played with, for every mode.
    /// </summary>
    public partial class FreeplayModsScreen : FreeplayFormScreen
    {
        public override string Title => "Моды freeplay";

        protected override string PageTitle => "Моды";

        public FreeplayModsScreen(FreeplayStorage freeplayStorage)
            : base(freeplayStorage)
        {
        }

        protected override void CreateForm()
        {
            AddHeading("Моды");

            AddHint("Моды из osu!, работают так же. Включаются для всех режимов, пока не выключишь. Звёзды режима считаются без модов.");

            foreach (var (flag, name, description) in FreeplayMods.ALL)
            {
                var mod = flag;

                AddCheckbox(name, Settings.Mods.HasFlag(mod), enabled =>
                {
                    var before = Settings.Mods;
                    Settings.Mods = enabled ? FreeplayMods.With(before, mod) : before & ~mod;

                    // Something osu! doesn't allow together was turned off: show it.
                    if (enabled && Settings.Mods != (before | mod))
                        Schedule(RebuildForm);
                }, description);
            }
        }
    }
}
