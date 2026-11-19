// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Freeplay.Generation
{
    /// <summary>
    /// Generation limits derived from a target star rating.
    /// These keep generated patterns humanly playable and are tuned against measured star ratings
    /// (see DifficultyScalingReport in the tests), roughly:
    /// <list type="bullet">
    /// <item>below 2.5★: notes on beats only, short jumps, sliders;</item>
    /// <item>2.5-4.5★: 1/2 rhythms, short bursts, first streams;</item>
    /// <item>4.5-6.5★: gaps filled with 1/2 jumps, longer streams, 1/4 everywhere it's mapped;</item>
    /// <item>above 6.5★: 1/2 jumps almost everywhere, jumps across the screen, deathstreams.</item>
    /// </list>
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
        /// The maximum average object rate over <see cref="DENSITY_WINDOW"/>. Keeps sections from becoming walls of objects.
        /// </summary>
        public readonly double MaxObjectsPerSecond;

        public const double DENSITY_WINDOW = 2000;

        /// <summary>
        /// <see cref="MaxObjectsPerSecond"/> for a part of the track: a little more in a kiai (chorus) and a little less outside it, from 3★,
        /// so a capped map keeps the mapper's contrast instead of flattening the chorus down to the verse.
        /// </summary>
        public double MaxObjectsPerSecondIn(bool kiai, double beatLength = 0)
        {
            double cap = Stars < 3 ? MaxObjectsPerSecond : MaxObjectsPerSecond * (kiai ? 1.12 : 0.92);

            // Aim jumps every half beat when the stars allow it at all: the rating then comes from the jumps' size rather than
            // from leaving notes out, which leaves pauses all over the map. (Room above it for the mapper's 1/4 doubles.)
            if (FlowJumps && beatLength > 0)
            {
                double halfBeats = 2000 / beatLength;

                if (halfBeats <= (1.2 + 0.95 * Stars) * 1.25)
                    cap = Math.Max(cap, halfBeats * 1.15);
            }

            return cap;
        }

        /// <summary>
        /// Whether this is aim that keeps on jumping every half beat (see <see cref="MaxObjectsPerSecondIn"/>).
        /// </summary>
        public readonly bool FlowJumps;

        /// <summary>
        /// How likely a one-beat gap in the music is filled with an extra 1/2 note (and a two-beat gap with one on the beat).
        /// This is what makes high star ratings dense: source beatmaps rarely map 1/2 everywhere.
        /// </summary>
        public readonly double FillChance;

        /// <summary>
        /// Cursor speed (osu!pixels per millisecond) used to size jumps.
        /// </summary>
        public readonly double AimVelocity;

        /// <summary>
        /// The shortest jump (osu!pixels) between notes which aren't a burst. Without it, calibration on fast songs
        /// shrinks jumps until notes sit next to each other; with it, calibration removes notes instead.
        /// </summary>
        public readonly double MinJumpDistance;

        public readonly double MaxJumpDistance;

        public readonly double SliderMultiplier;

        public readonly float CircleSize;
        public readonly float ApproachRate;
        public readonly float OverallDifficulty;
        public readonly float DrainRate;

        public DifficultyProfile(FreeplayGenerationSettings settings)
        {
            Stars = Math.Clamp(settings.StarRating, 0.5, 12);
            FlowJumps = (settings.Style is PatternStyle.Jumps or PatternStyle.Metronome) && settings.StarRating >= 3;
            double s = Stars;
            double density = Math.Clamp(settings.Density, 0.25, 2);

            if (s < 2.5)
                MinGapBeats = 1;
            else if (s < 4.5)
                MinGapBeats = 0.5;
            else
                MinGapBeats = 0.25;

            // Allow short 1/4 bursts at mid difficulty, even though MinGapBeats is 1/2 there.
            MaxBurstGaps = s < 3 ? 0 : (int)Math.Round(Math.Pow(s - 2.5, 1.6) * 1.5);

            double maxStreamBpm = 120 + 25 * s;
            MinGapMs = 15000 / maxStreamBpm;

            // Enough for 1/2 on fast songs from ~5★ (185 BPM is 6.2 notes a second); calibration keeps the rating with smaller jumps.
            MaxObjectsPerSecond = (1.2 + 0.95 * s) * density;

            // When calibration takes notes away (a dense source on a fast song), the source's own notes thin out too, keeping stronger beats.
            if (settings.FillBoost < 0)
                MaxObjectsPerSecond *= 1 + settings.FillBoost * 0.6;

            // Starts early: from ~3.5★ most empty beats in the music should get a note, or maps feel undermapped.
            FillChance = Math.Clamp(Math.Clamp((s - 2.5) / 3, 0, 0.9) * density + settings.FillBoost, 0, 0.95);

            // Grows faster than linearly: star rating rewards speed, and top difficulties jump much further per millisecond.
            AimVelocity = 0.12 + 0.12 * s + 0.018 * s * s;
            MaxJumpDistance = Math.Min(480, 110 + 45 * s);
            MinJumpDistance = Math.Min(320, 60 + 30 * s);
            SliderMultiplier = Math.Clamp(1.0 + 0.15 * s, 1, 2.6);

            CircleSize = settings.CircleSize ?? (float)Math.Clamp(3.5 + 0.1 * s, 3.5, 4.5);
            ApproachRate = settings.ApproachRate ?? (float)Math.Clamp(4.5 + 0.85 * s, 4, 10);
            OverallDifficulty = settings.OverallDifficulty ?? (float)Math.Clamp(4 + 0.8 * s, 3, 10);
            DrainRate = settings.DrainRate ?? (float)Math.Clamp(3 + 0.6 * s, 2, 8);
        }
    }
}
