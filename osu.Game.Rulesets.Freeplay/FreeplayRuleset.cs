// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Freeplay.Beatmaps;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Scoring;
using osu.Game.Rulesets.Freeplay.Screens;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Configuration;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Replays;
using osu.Game.Rulesets.Osu.Scoring;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Replays.Types;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Freeplay
{
    /// <summary>
    /// A ruleset which plays like osu!, but generates a fresh set of hit objects for every attempt.
    /// </summary>
    /// <remarks>
    /// Gameplay, drawables and skinning are reused from <see cref="OsuRuleset"/>.
    /// This intentionally does not derive from <see cref="OsuRuleset"/>, as that would also make it an <see cref="ILegacyRuleset"/> sharing online ID 0.
    /// </remarks>
    public class FreeplayRuleset : Ruleset, IHasMainMenuScreen
    {
        public const string SHORT_NAME = "freeplay";

        private readonly OsuRuleset osuRuleset = new OsuRuleset();

        public override string Description => "freeplay~";

        public override string ShortName => SHORT_NAME;

        public override string PlayingVerb => "Surviving the unknown";

        public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

        public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null) => new DrawableOsuRuleset(this, beatmap, mods);

        public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap) => new FreeplayBeatmapConverter(beatmap, this);

        public override IBeatmapProcessor CreateBeatmapProcessor(IBeatmap beatmap) => new OsuBeatmapProcessor(beatmap);

        public override ScoreProcessor CreateScoreProcessor() => new FreeplayScoreProcessor(this);

        public override HealthProcessor CreateHealthProcessor(double drainStartTime) => new FreeplayHealthProcessor(drainStartTime, FreeplaySettings.Current.Fail.Health);

        public OsuScreen CreateMainMenuScreen() => new FreeplayMenuScreen();

        public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => new OsuDifficultyCalculator(RulesetInfo, beatmap);

        public override PerformanceCalculator CreatePerformanceCalculator() => new OsuPerformanceCalculator();

        public override IEnumerable<Mod> GetModsFor(ModType type) => osuRuleset.GetModsFor(type);

        public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0) => osuRuleset.GetDefaultKeyBindings(variant);

        public override ISkin? CreateSkinTransformer(ISkin skin, IBeatmap beatmap) => osuRuleset.CreateSkinTransformer(skin, beatmap);

        public override IConvertibleReplayFrame CreateConvertibleReplayFrame() => new OsuReplayFrame();

        // Must be an OsuRulesetConfigManager, as osu! drawables resolve it directly.
        public override IRulesetConfigManager CreateConfig(SettingsStore? settings) => new OsuRulesetConfigManager(settings, RulesetInfo);

        public override RulesetSettingsSubsection CreateSettings() => new OsuSettingsSubsection(this);

        public override IEnumerable<HitResult> GetValidHitResults() => osuRuleset.GetValidHitResults();

        public override LocalisableString GetDisplayNameForHitResult(HitResult result) => osuRuleset.GetDisplayNameForHitResult(result);

        public override StatisticItem[] CreateStatisticsForScore(ScoreInfo score, IBeatmap playableBeatmap) => osuRuleset.CreateStatisticsForScore(score, playableBeatmap);

        public override Drawable CreateIcon() => new SpriteIcon { Icon = FontAwesome.Solid.Infinity };
    }
}
