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

            AddHint("Моды из osu!, работают так же. Включаются для всех режимов, пока не выключишь. Звёзды режима считаются без модов, кроме Double Time: под него карта генерируется сразу.");

            foreach (var (flag, name, description) in FreeplayMods.ALL)
            {
                var mod = flag;

                var enabledBindable = Bind(Settings.Mods.HasFlag(mod), enabled =>
                {
                    var before = Settings.Mods;
                    Settings.Mods = enabled ? FreeplayMods.With(before, mod) : before & ~mod;

                    // Something osu! doesn't allow together was turned off: show it.
                    if (enabled && Settings.Mods != (before | mod))
                        Schedule(RebuildForm);
                });

                AddCheckbox(name, enabledBindable, description);

                if (mod == FreeplayModFlags.DoubleTime)
                {
                    // As osu!lazer's Double Time: any speed from 1.01× to 2×.
                    var speed = AddSlider("Скорость Double Time", Settings.DoubleTimeSpeed, FreeplayMods.MIN_SPEED, FreeplayMods.MAX_SPEED, 0.01, v => Settings.DoubleTimeSpeed = v,
                        v => $"{v:0.00}×");

                    ShowWhen(speed, enabledBindable, on => on);

                    var chance = AddSlider("Как часто Double Time", Settings.DoubleTimeChance, 0.05, 1, 0.05, v => Settings.DoubleTimeChance = v,
                        v => v >= 1 ? "на каждом треке" : $"на {v:P0} треков", "Меньше 100% — DT включается на случайных треках, остальные играются как обычно.");

                    ShowWhen(chance, enabledBindable, on => on);
                }
            }
        }
    }
}
