// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Generation
{
    /// <summary>
    /// A section's own way of being mapped, so that sections differ from each other: each ingredient is only sometimes there.
    /// </summary>
    public class SectionRecipe
    {
        /// <summary>
        /// Follow the mapper's spacing (wide where they went wide, tight where tight), within limits.
        /// </summary>
        public bool MapperSpacing;

        /// <summary>
        /// Clap and finish hitsounds get a wider jump and a sharp turn.
        /// </summary>
        public bool Accents;

        public SectionCharacter Character;

        /// <summary>
        /// A part of the playfield the section keeps to, or null for all of it.
        /// </summary>
        public Vector2? Territory;

        /// <summary>
        /// Which way motifs turn: -1 or 1, or 0 for either.
        /// </summary>
        public int Side;

        /// <summary>
        /// Jumps open up through the section (into a chorus).
        /// </summary>
        public bool Crescendo;

        /// <summary>
        /// Patterns from the example collections come only from this mapper.
        /// </summary>
        public string? Flavor;

        /// <summary>
        /// When the music repeats a phrase, its pattern comes back mirrored or turned.
        /// </summary>
        public bool Repeats;

        /// <summary>
        /// How much of the section's patterns come from the example collections.
        /// </summary>
        public double MinedShare;

        /// <summary>
        /// How many ingredients two recipes don't share, for keeping neighbouring sections apart.
        /// </summary>
        public int DifferenceFrom(SectionRecipe other)
        {
            int difference = 0;

            if (MapperSpacing != other.MapperSpacing) difference++;
            if (Accents != other.Accents) difference++;
            if (Character != other.Character) difference += 2;
            if (Territory != other.Territory) difference++;
            if (Side != other.Side) difference++;
            if (Repeats != other.Repeats) difference++;
            if (Flavor != other.Flavor) difference++;
            if (Math.Abs(MinedShare - other.MinedShare) > 0.25) difference++;

            return difference;
        }

        private static SectionCharacter pickCharacter(Random rng, double iq)
        {
            var characters = Enum.GetValues<SectionCharacter>().Where(c => c != SectionCharacter.None).ToArray();
            var weights = characters.Select(c => 0.1 + Math.Exp(-Math.Pow(SectionCharacters.Iq(c) - iq, 2) / (2 * 0.25 * 0.25))).ToArray();

            double roll = rng.NextDouble() * weights.Sum();

            for (int i = 0; i < characters.Length; i++)
            {
                roll -= weights[i];

                if (roll <= 0)
                    return characters[i];
            }

            return characters[^1];
        }

        private static readonly Vector2[] territories =
        {
            new Vector2(140, 192), new Vector2(372, 192), new Vector2(256, 120), new Vector2(256, 264),
            new Vector2(150, 120), new Vector2(362, 264), new Vector2(362, 120), new Vector2(150, 264),
        };

        /// <summary>
        /// Rolls a recipe from <paramref name="palette"/>. A few candidates are rolled and the one least like the recent sections wins,
        /// so the generator doesn't settle into one way of mapping by chance.
        /// </summary>
        public static SectionRecipe Roll(TrackPalette palette, Random rng, IReadOnlyList<SectionRecipe> recent, IReadOnlyList<string> flavors, double minedShare, double iq = 0.5)
        {
            SectionRecipe? best = null;
            int bestScore = -1;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                var candidate = rollOne(palette, rng, flavors, minedShare, iq);

                // The same character twice in a row is what makes a map feel like one long section.
                if (recent.Count > 0 && candidate.Character != SectionCharacter.None && candidate.Character == recent[^1].Character)
                    continue;

                int score = recent.Count == 0 ? 0 : recent.TakeLast(2).Sum(r => candidate.DifferenceFrom(r));

                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best ?? rollOne(palette, rng, flavors, minedShare, iq);
        }

        private static SectionRecipe rollOne(TrackPalette palette, Random rng, IReadOnlyList<string> flavors, double minedShare, double iq)
        {

            return new SectionRecipe
            {
                MapperSpacing = rng.NextDouble() < palette.MapperSpacing,
                Accents = rng.NextDouble() < palette.Accents,
                Character = rng.NextDouble() < palette.Character ? pickCharacter(rng, iq) : SectionCharacter.None,
                Territory = rng.NextDouble() < palette.Territory ? territories[rng.Next(territories.Length)] : null,
                Side = rng.NextDouble() < palette.Flow ? (rng.NextDouble() < 0.5 ? -1 : 1) : 0,
                Crescendo = rng.NextDouble() < palette.Crescendo,
                Flavor = flavors.Count > 1 && rng.NextDouble() < palette.Flavor ? flavors[rng.Next(flavors.Count)] : null,
                Repeats = rng.NextDouble() < palette.Repeats,
                MinedShare = Math.Clamp(minedShare * (0.4 + rng.NextDouble() * 1.3), 0, 0.9),
            };
        }
    }

    public static class SectionCharacters
    {
        /// <summary>
        /// How tricky each character is, on the scale of <see cref="FreeplayGenerationSettings.PatternIq"/>: a map's IQ makes its own kind likelier.
        /// </summary>
        public static double Iq(SectionCharacter character) => character switch
        {
            SectionCharacter.PingPong => 0.05,
            SectionCharacter.WideTriangles => 0.25,
            SectionCharacter.Flow => 0.3,
            SectionCharacter.Stacks => 0.5,
            SectionCharacter.AntiJumps => 0.6,
            _ => 0.85,
        };
    }

    public enum SectionCharacter
    {
        None,
        WideTriangles,
        PingPong,
        SquaresAndCrosses,
        Flow,
        AntiJumps,
        Stacks,
    }

    /// <summary>
    /// How often a track's sections use each ingredient (see <see cref="SectionRecipe"/>), rolled per track so tracks differ too:
    /// one leans on accents, another on repeats. Saved with the track's settings, so a seed still repeats the same map.
    /// </summary>
    public class TrackPalette
    {
        public double MapperSpacing { get; set; } = 0.25;
        public double Accents { get; set; } = 0.5;
        public double Character { get; set; } = 0.3;
        public double Territory { get; set; } = 0.35;
        public double Flow { get; set; } = 0.5;
        public double Crescendo { get; set; } = 0.5;
        public double Flavor { get; set; } = 0.4;
        public double Repeats { get; set; } = 0.3;

        /// <summary>
        /// How likely the track is to keep some of its quiet sections calmer than the mapper's rhythm, so its choruses stand out (see <see cref="Section.Calm"/>).
        /// </summary>
        public double Contrast { get; set; } = 0.45;

        /// <summary>
        /// How many rare moments (a huge jump into a chorus, a stack on a finish, a jump back onto a note) the track may have.
        /// </summary>
        public int Spices { get; set; } = 2;

        /// <summary>
        /// The ingredients this track leans on, in words.
        /// </summary>
        public List<string> Featured { get; set; } = new List<string>();

        public static TrackPalette Roll(Random rng)
        {
            var palette = new TrackPalette();

            var ingredients = new (string name, Func<double> get, Action<double> set)[]
            {
                ("задумка маппера", () => palette.MapperSpacing, v => palette.MapperSpacing = v),
                ("акценты", () => palette.Accents, v => palette.Accents = v),
                ("характеры", () => palette.Character, v => palette.Character = v),
                ("территории", () => palette.Territory, v => palette.Territory = v),
                ("флоу", () => palette.Flow, v => palette.Flow = v),
                ("нарастания", () => palette.Crescendo, v => palette.Crescendo = v),
                ("почерки мапперов", () => palette.Flavor, v => palette.Flavor = v),
                ("повторы", () => palette.Repeats, v => palette.Repeats = v),
                ("контраст куплет/припев", () => palette.Contrast, v => palette.Contrast = v),
            };

            foreach (var (_, get, set) in ingredients)
                set(Math.Clamp(get() * (0.4 + rng.NextDouble() * 1.2), 0, 0.85));

            // One or two the track is known for.
            foreach (var (name, get, set) in ingredients.OrderBy(_ => rng.Next()).Take(rng.Next(1, 3)))
            {
                set(Math.Clamp(Math.Max(get() * 1.8, 0.5), 0, 0.85));
                palette.Featured.Add(name);
            }

            palette.Spices = rng.Next(1, 4);
            return palette;
        }

        public string Describe() => Featured.Count == 0 ? string.Empty : string.Join(" · ", Featured);
    }
}
