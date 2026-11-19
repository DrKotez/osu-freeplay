// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Game.Screens;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// What's behind freeplay~'s screens: its own dark colour, so osu!'s menu backgrounds never show through.
    /// One instance is shared by consecutive freeplay screens (backgrounds of the same type are equal).
    /// </summary>
    public partial class FreeplayBackgroundScreen : BackgroundScreen
    {
        public FreeplayBackgroundScreen()
        {
            InternalChild = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = FreeplayColours.BACKGROUND,
            };
        }
    }
}
