// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Freeplay.Generation
{
    /// <summary>
    /// Difficulty as the pp an SS would give (see <see cref="FreeplayGenerationSettings.UsePerformance"/>): roughly what stars a track of some length
    /// needs for it, before it's generated (the generator then finds the stars exactly with osu!'s own pp).
    /// </summary>
    /// <remarks>
    /// osu!'s pp grows with the cube of the stars, and a little with the number of notes (its length bonus); accuracy adds about the same at any stars. How much pp a star gives
    /// and how many notes a second a map has are learned from the tracks generated.
    /// </remarks>
    public static class PerformanceTarget
    {
        private static readonly object learning = new object();

        /// <summary>
        /// pp of an SS over stars³ and the length bonus, by style (the stars mode jumps only; aim has more of everything).
        /// </summary>
        private static double perStarAim = DEFAULT_PER_STAR_AIM, perStarStars = DEFAULT_PER_STAR_STARS;

        /// <summary>
        /// Notes a second, over the stars.
        /// </summary>
        private static double notesAim = DEFAULT_NOTES_AIM, notesStars = DEFAULT_NOTES_STARS;

        public const double DEFAULT_PER_STAR_AIM = 1.33, DEFAULT_PER_STAR_STARS = 1.35;
        public const double DEFAULT_NOTES_AIM = 0.75, DEFAULT_NOTES_STARS = 0.8;

        /// <summary>
        /// About what accuracy adds to an SS, whatever the stars.
        /// </summary>
        public const double ACCURACY_PP = 25;

        /// <summary>
        /// osu!'s length bonus for <paramref name="objects"/> notes.
        /// </summary>
        public static double LengthBonus(double objects) => 0.95 + 0.35 * Math.Min(1, objects / 2000) + (objects > 2000 ? Math.Log10(objects / 2000) * 0.5 : 0);

        /// <summary>
        /// About the pp of an SS of a map of <paramref name="stars"/> lasting <paramref name="seconds"/> (as played).
        /// </summary>
        public static double Estimate(double stars, double seconds, PatternStyle style)
        {
            bool metronome = style == PatternStyle.Metronome;
            double objects = Math.Max(1, seconds) * stars * (metronome ? notesStars : notesAim);
            return ACCURACY_PP + (metronome ? perStarStars : perStarAim) * stars * stars * stars * LengthBonus(objects);
        }

        /// <summary>
        /// About the stars a map lasting <paramref name="seconds"/> (as played) needs for an SS to give <paramref name="pp"/>.
        /// </summary>
        public static double StarsFor(double pp, double seconds, PatternStyle style)
        {
            double low = 0.5, high = 15;

            for (int i = 0; i < 30; i++)
            {
                double mid = (low + high) / 2;

                if (Estimate(mid, seconds, style) < pp)
                    low = mid;
                else
                    high = mid;
            }

            return (low + high) / 2;
        }

        /// <summary>
        /// Learns from a generated map: its stars, how long it lasts (as played), its notes and the pp of an SS.
        /// </summary>
        public static void Learn(double stars, double seconds, int objects, double pp, PatternStyle style)
        {
            if (stars < 1 || seconds < 10 || objects < 20 || pp <= 0)
                return;

            bool metronome = style == PatternStyle.Metronome;
            double perStar = Math.Max(0.1, pp - ACCURACY_PP) / (stars * stars * stars * LengthBonus(objects));
            double notes = objects / seconds / stars;

            lock (learning)
            {
                if (metronome)
                {
                    perStarStars = perStarStars * 0.7 + perStar * 0.3;
                    notesStars = notesStars * 0.7 + notes * 0.3;
                }
                else
                {
                    perStarAim = perStarAim * 0.7 + perStar * 0.3;
                    notesAim = notesAim * 0.7 + notes * 0.3;
                }
            }
        }
    }
}
