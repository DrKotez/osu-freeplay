// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Osu.Scoring;

namespace osu.Game.Rulesets.Freeplay.Scoring
{
    /// <summary>
    /// osu! health, where running out of health can be disabled as a fail condition.
    /// </summary>
    public partial class FreeplayHealthProcessor : OsuHealthProcessor
    {
        private readonly bool failOnZeroHealth;

        public FreeplayHealthProcessor(double drainStartTime, bool failOnZeroHealth)
            : base(drainStartTime)
        {
            this.failOnZeroHealth = failOnZeroHealth;
        }

        protected override bool CheckDefaultFailCondition(JudgementResult result) => failOnZeroHealth && base.CheckDefaultFailCondition(result);
    }
}
