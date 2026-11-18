// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play.HUD.HitErrorMeters;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// A hit error bar and unstable rate at the bottom of the screen. Lives with the playfield rather than the HUD,
    /// so it stays visible with the interface hidden, and is sized from freeplay's settings rather than the skin editor.
    /// </summary>
    public partial class FreeplayTimingOverlay : CompositeDrawable
    {
        private readonly TimingDisplaySettings settings;

        [Resolved]
        private ScoreProcessor scoreProcessor { get; set; } = null!;

        private OsuSpriteText? unstableRate;
        private HitEventExtensions.UnstableRateCalculationResult? unstableRateResult;

        public FreeplayTimingOverlay(TimingDisplaySettings settings)
        {
            this.settings = settings;
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            float scale = (float)settings.Scale;

            var content = new FillFlowContainer
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Margin = new MarginPadding { Bottom = 12 },
                Scale = new Vector2(scale),
                Alpha = (float)settings.Opacity,
            };

            if (settings.ShowUnstableRate)
            {
                content.Add(unstableRate = new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold, fixedWidth: true),
                    Colour = FreeplayColours.WHITE,
                    Shadow = true,
                    Text = "UR -",
                });
            }

            if (settings.ShowHitErrorBar)
            {
                // The meter is a vertical bar; turned on its side it sits along the bottom like osu!stable's.
                content.Add(new Container
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    AutoSizeAxes = Axes.Both,
                    Child = new BarHitErrorMeter
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Rotation = 270,
                        LabelStyle = { Value = BarHitErrorMeter.LabelStyles.None },
                    },
                });
            }

            InternalChild = content;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (unstableRate == null)
                return;

            scoreProcessor.NewJudgement += onJudgement;
            scoreProcessor.JudgementReverted += onJudgement;
        }

        private void onJudgement(JudgementResult result)
        {
            if (HitEventExtensions.AffectsUnstableRate(result.HitObject, result.Type))
                Scheduler.AddOnce(updateUnstableRate);
        }

        private void updateUnstableRate()
        {
            unstableRateResult = scoreProcessor.HitEvents.CalculateUnstableRate(unstableRateResult);

            double? value = unstableRateResult?.Result;
            unstableRate!.Text = value == null ? "UR -" : $"UR {Math.Round(value.Value, 2):0.00}";
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (unstableRate != null && scoreProcessor.IsNotNull())
            {
                scoreProcessor.NewJudgement -= onJudgement;
                scoreProcessor.JudgementReverted -= onJudgement;
            }
        }
    }
}
