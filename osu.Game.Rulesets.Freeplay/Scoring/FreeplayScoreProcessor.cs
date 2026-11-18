// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Osu.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Freeplay.Scoring
{
    /// <summary>
    /// Mirrors <c>OsuScoreProcessor</c>, but is owned by the <see cref="FreeplayRuleset"/>.
    /// </summary>
    public partial class FreeplayScoreProcessor : ScoreProcessor
    {
        public FreeplayScoreProcessor(FreeplayRuleset ruleset)
            : base(ruleset)
        {
        }

        public override ScoreRank RankFromScore(double accuracy, IReadOnlyDictionary<HitResult, int> results)
        {
            ScoreRank rank = base.RankFromScore(accuracy, results);

            if ((rank == ScoreRank.S || rank == ScoreRank.X) && results.GetValueOrDefault(HitResult.Miss) > 0)
                rank = ScoreRank.A;

            return rank;
        }

        protected override HitEvent CreateHitEvent(JudgementResult result)
            => base.CreateHitEvent(result).With((result as OsuHitCircleJudgementResult)?.CursorPositionAtHit);
    }
}
