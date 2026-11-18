// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// freeplay~'s palette, based on the transgender pride flag.
    /// </summary>
    public static class FreeplayColours
    {
        public static readonly Color4 BLUE = Color4Extensions.FromHex("5bcefa");
        public static readonly Color4 PINK = Color4Extensions.FromHex("f5a9b8");
        public static readonly Color4 WHITE = Color4Extensions.FromHex("ffffff");

        /// <summary>
        /// Deeper tones of the flag colours, readable behind white text.
        /// </summary>
        public static readonly Color4 DEEP_BLUE = Color4Extensions.FromHex("3a9fd1");

        public static readonly Color4 DEEP_PINK = Color4Extensions.FromHex("e2789a");

        public static readonly Color4 BACKGROUND = Color4Extensions.FromHex("17121f");

        public static readonly Color4 TEXT_DIM = Color4Extensions.FromHex("c9bfd6");

        /// <summary>
        /// The flag's stripes from top to bottom.
        /// </summary>
        public static readonly Color4[] FLAG = { BLUE, PINK, WHITE, PINK, BLUE };

        public static ColourInfo TitleGradient => ColourInfo.GradientHorizontal(BLUE, PINK);
    }
}
