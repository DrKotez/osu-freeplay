// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;

namespace osu.Game.Rulesets.Freeplay.Configuration
{
    /// <summary>
    /// The osu! mods a run can be played with. Each is osu!'s own mod, unchanged.
    /// </summary>
    [Flags]
    public enum FreeplayModFlags
    {
        None = 0,
        SingleTap = 1,
        NoFail = 2,
        Relax = 4,
        HardRock = 8,
        Notelock = 16,
        Hidden = 32,
        DoubleTime = 64,
    }

    public static class FreeplayMods
    {
        /// <summary>
        /// osu!'s usual Double Time speed.
        /// </summary>
        public const double DEFAULT_SPEED = 1.5;

        /// <summary>
        /// The speeds osu!lazer allows Double Time.
        /// </summary>
        public const double MIN_SPEED = 1.01, MAX_SPEED = 2;

        public static OsuModDoubleTime CreateDoubleTime(double speed)
        {
            var mod = new OsuModDoubleTime();
            mod.SpeedChange.Value = Math.Clamp(speed, MIN_SPEED, MAX_SPEED);
            return mod;
        }

        /// <summary>
        /// Each mod with its name and what it does, in the order shown.
        /// </summary>
        public static readonly (FreeplayModFlags flag, string name, string description)[] ALL =
        {
            (FreeplayModFlags.SingleTap, "Singletap", "Жмёшь одной кнопкой: той, которой нажал первым. Вторая кнопка не засчитывается. После перерыва в карте можно выбрать заново."),
            (FreeplayModFlags.NoFail, "No Fail", "Проиграть нельзя: ни по HP, ни по миссам, ни по точности. Время забега, если оно задано, всё равно кончается."),
            (FreeplayModFlags.Relax, "Relax", "Нажимать не нужно, только целиться. Как в osu!. Не вместе с Singletap."),
            (FreeplayModFlags.HardRock, "Hard Rock", "Как в osu!: круги меньше, AR, OD и HP выше, карта отражена сверху вниз. Карта выходит сложнее выбранных звёзд."),
            (FreeplayModFlags.Hidden, "Hidden", "Как в osu!: подхода нет, круги исчезают ещё до того, как их нажать."),
            (FreeplayModFlags.DoubleTime, "Double Time", "Как в osu!: всё быстрее, скорость настраивается (1.5× — обычный DT). Карта генерируется сразу под DT: звёзды те, что выбраны. AR и OD, если ты их задал сам, остаются такими же и с DT (AR 10 — это AR 10, а не 11); если нет — растут от DT, как в osu!."),
            (FreeplayModFlags.Notelock, "Notelock", "Как в osu!stable: следующую ноту нельзя нажать, пока не прошла предыдущая, и остальное поведение стейбла (мод Classic из osu!lazer)."),
        };

        /// <summary>
        /// Fresh instances of osu!'s mods for <paramref name="flags"/>.
        /// </summary>
        public static Mod[] Create(FreeplayModFlags flags, double doubleTimeSpeed = DEFAULT_SPEED)
        {
            var mods = new List<Mod>();

            if (flags.HasFlag(FreeplayModFlags.SingleTap) && !flags.HasFlag(FreeplayModFlags.Relax))
                mods.Add(new OsuModSingleTap());
            if (flags.HasFlag(FreeplayModFlags.NoFail))
                mods.Add(new OsuModNoFail());
            if (flags.HasFlag(FreeplayModFlags.Relax))
                mods.Add(new OsuModRelax());
            if (flags.HasFlag(FreeplayModFlags.HardRock))
                mods.Add(new OsuModHardRock());
            if (flags.HasFlag(FreeplayModFlags.Hidden))
                mods.Add(new OsuModHidden());
            if (flags.HasFlag(FreeplayModFlags.DoubleTime))
                mods.Add(CreateDoubleTime(doubleTimeSpeed));
            if (flags.HasFlag(FreeplayModFlags.Notelock))
                mods.Add(new OsuModClassic());

            return mods.ToArray();
        }

        /// <summary>
        /// <paramref name="flags"/> with <paramref name="flag"/> turned on, and whatever osu! doesn't allow with it turned off.
        /// </summary>
        public static FreeplayModFlags With(FreeplayModFlags flags, FreeplayModFlags flag)
        {
            var added = Create(flag).Single();
            var result = flags | flag;

            foreach (var (other, _, _) in ALL)
            {
                if (other == flag || !result.HasFlag(other))
                    continue;

                var existing = Create(other).Single();

                if (added.IncompatibleMods.Any(t => t.IsInstanceOfType(existing)) || existing.IncompatibleMods.Any(t => t.IsInstanceOfType(added)))
                    result &= ~other;
            }

            return result;
        }

        /// <summary>
        /// osu!'s acronyms of the mods that are on (NL for Notelock), e.g. "HD HR", or null with none.
        /// </summary>
        public static string? Short(FreeplayModFlags flags, double doubleTimeSpeed = DEFAULT_SPEED)
        {
            var acronyms = ALL.Where(m => flags.HasFlag(m.flag)).Select(m => m.flag switch
            {
                FreeplayModFlags.Notelock => "NL",
                FreeplayModFlags.DoubleTime => SpeedName("DT", doubleTimeSpeed),
                _ => Create(m.flag).Single().Acronym,
            }).ToList();
            return acronyms.Count == 0 ? null : string.Join(" ", acronyms);
        }

        /// <summary>
        /// The names of the mods that are on, e.g. "Hard Rock · No Fail", or null with none.
        /// </summary>
        /// <summary>
        /// A rate mod's name, with its speed when it isn't the usual one: "DT", "DT 1.30×".
        /// </summary>
        public static string SpeedName(string name, double speed) => Math.Abs(speed - DEFAULT_SPEED) < 0.001 ? name : FormattableString.Invariant($"{name} {speed:0.00}×");

        public static string? Describe(FreeplayModFlags flags, double doubleTimeSpeed = DEFAULT_SPEED)
        {
            var names = ALL.Where(m => flags.HasFlag(m.flag)).Select(m => m.flag == FreeplayModFlags.DoubleTime ? SpeedName("Double Time", doubleTimeSpeed) : m.name).ToList();
            return names.Count == 0 ? null : string.Join(" · ", names);
        }
    }
}
