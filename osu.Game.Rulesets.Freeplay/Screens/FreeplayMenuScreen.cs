// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Freeplay.Configuration;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// The freeplay entry screen: play, settings and statistics.
    /// </summary>
    public partial class FreeplayMenuScreen : FreeplayScreen
    {
        public override string Title => "osu!freeplay";

        private FreeplayStorage freeplayStorage = null!;
        private OsuSpriteText summary = null!;

        [BackgroundDependencyLoader]
        private void load(Storage storage, OsuColour colours)
        {
            freeplayStorage = new FreeplayStorage(storage);
            FreeplaySettings.SetCurrent(freeplayStorage.LoadSettings());

            Content.Add(new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(14),
                Children = new Drawable[]
                {
                    centred(Text("osu!freeplay", 72, FontWeight.Bold, Typeface.TorusAlternate).With(t => t.Colour = colours.Pink)),
                    centred(Text("каждая попытка — новая карта", 22)),
                    centred(summary = Text(string.Empty, 18).With(t => t.Colour = colours.Gray9)),
                    new Container { Height = 20 },
                    Button("Играть", () => this.Push(new FreeplayRunScreen(freeplayStorage))),
                    Button("Настройки", () => this.Push(new FreeplaySettingsScreen(freeplayStorage))),
                    Button("Статистика", () => this.Push(new FreeplayStatsScreen(freeplayStorage))),
                }
            });

            updateSummary();
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);
            updateSummary();
        }

        private void updateSummary()
        {
            var settings = FreeplaySettings.Current;

            var fail = new List<string>();

            if (settings.Fail.Health) fail.Add("HP");
            if (settings.Fail.MissLimit > 0) fail.Add(settings.Fail.MissLimit == 1 ? "sudden death" : $"{settings.Fail.MissLimit} промахов");
            if (settings.Fail.MinAccuracy > 0) fail.Add($"точность < {settings.Fail.MinAccuracy:P0}");
            if (settings.Fail.TimeLimitMinutes > 0) fail.Add($"лимит {settings.Fail.TimeLimitMinutes:0} мин");

            string source = settings.Tracks.Source == TrackSource.Library ? "вся библиотека" : "коллекция";

            summary.Text = $"{settings.Generation.StarRating:0.0}★ · {settings.Generation.Style.GetDescription()} · {source} · поражение: {(fail.Count > 0 ? string.Join(", ", fail) : "нет")}";
        }

        private static Drawable centred(Drawable drawable)
        {
            drawable.Anchor = Anchor.TopCentre;
            drawable.Origin = Anchor.TopCentre;
            return drawable;
        }
    }
}
