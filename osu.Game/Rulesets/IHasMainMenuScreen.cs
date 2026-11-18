// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Screens;

namespace osu.Game.Rulesets
{
    /// <summary>
    /// A ruleset which provides its own entry screen, shown from a dedicated main menu button.
    /// </summary>
    public interface IHasMainMenuScreen
    {
        OsuScreen CreateMainMenuScreen();
    }
}
