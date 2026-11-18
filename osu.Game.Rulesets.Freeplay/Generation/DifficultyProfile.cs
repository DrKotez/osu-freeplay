// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Freeplay.Generation
{
    /// <summary>
    /// Generation limits derived from a target star rating.
    /// These keep generated patterns humanly playable and are tuned by feel, not computed exactly.
    /// </summary>
    public class DifficultyProfile
    {
        public readonly double Stars;

        /// <summary>
        /// The smallest allowed gap between two objects, in beats.
        /// </summary>
        public readonly double MinGapBeats;

        /// <summary>
        /// The smallest allowed gap between two objects, in milliseconds, regardless of BPM.
        /// </summary>
        public readonly double MinGapMs;

        /// <summary>
        /// The maximum number of consecutive 1/4-beat gaps (i.e. stream length - 1).
        /// </summary>
        public readonly int MaxBurstGaps;

        /// <summary>
        /// Cursor speed (osu!pixels per millisecond) used to size jumps.
        /// </summary>
        /// <summary>
        /// The maximum average object rate over <see cref="DENSITY_WINDOW"/>. Keeps sections from becoming walls of objects.
        /// </summary>
        public readonly double MaxObjectsPerSecond;

        public const double DENSITY_WINDOW = 2000;

        public readonly double AimVelocity;

        public readonly double MaxJumpDistance;

        public readonly double StreamSpacing;

        public readonly double SliderMultiplier;

        public readonly float CircleSize;
        public readonly float ApproachRate;
        public readonly float OverallDifficulty;
        public readonly float DrainRate;

        public DifficultyProfile(FreeplayGenerationSettings settings)
        {
            Stars = Math.Clamp(settings.StarRating, 0.5, 12);
            double s = Stars;

            if (s < 2)
                MinGapBeats = 1;
            else if (s < 4.5)
                MinGapBeats = 0.5;
            else
                MinGapBeats = 0.25;

            // Allow short 1/4 bursts at mid difficulty, even though MinGapBeats is 1/2 there.
            MaxBurstGaps = s < 3 ? 0 : (int)Math.Round(Math.Pow(s - 2.5, 1.6) * 1.5);

            double maxStreamBpm = 120 + 25 * s;
            MinGapMs = 15000 / maxStreamBpm;

            MaxObjectsPerSecond = (1.5 + 0.9 * s) * Math.Clamp(settings.Density, 0.25, 2);

            AimVelocity = 0.15 + 0.2 * s;
            MaxJumpDistance = Math.Min(380, 120 + 40 * s);
            StreamSpacing = 15 + 9 * s;
            SliderMultiplier = Math.Clamp(1.0 + 0.15 * s, 1, 2.6);

            CircleSize = settings.CircleSize ?? (float)Math.Clamp(3.5 + 0.1 * s, 3.5, 4.5);
            ApproachRate = settings.ApproachRate ?? (float)Math.Clamp(4.5 + 0.85 * s, 4, 10);
            OverallDifficulty = settings.OverallDifficulty ?? (float)Math.Clamp(4 + 0.8 * s, 3, 10);
            DrainRate = settings.DrainRate ?? (float)Math.Clamp(3 + 0.6 * s, 2, 8);
        }
    }
}
