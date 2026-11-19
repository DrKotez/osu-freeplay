// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Freeplay.Configuration;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Screens.Controls
{
    /// <summary>
    /// The seed of a map just played, to share: copied as a code, saved as a file, or kept in the player's own seeds.
    /// </summary>
    public partial class SeedSharePanel : TitledPanel
    {
        private readonly SeedMap seed;
        private readonly FreeplayStorage storage;
        private readonly Action? onUsed;
        private readonly OsuTextFlowContainer status;

        [Resolved]
        private Clipboard clipboard { get; set; } = null!;

        /// <param name="seed">The map's seed.</param>
        /// <param name="storage">Where files and the player's seeds go.</param>
        /// <param name="onUsed">Called when a button is pressed (e.g. so the next track waits).</param>
        public SeedSharePanel(SeedMap seed, FreeplayStorage storage, Action? onUsed = null)
            : base("Сид карты")
        {
            this.seed = seed;
            this.storage = storage;
            this.onUsed = onUsed;

            string code = seed.ToCode();

            Body.Add(new OsuSpriteText
            {
                Text = $"{seed.Song.Artist} — {seed.Song.Title} · {seed.Description} · код {code.Length} символов",
                Font = OsuFont.GetFont(size: 16, weight: FontWeight.SemiBold),
                Colour = FreeplayColours.TEXT_DIM,
            });

            Body.Add(new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(10, 0),
                Margin = new MarginPadding { Vertical = 8 },
                Children = new Drawable[]
                {
                    new FreeplayButton("Скопировать сид", copy, FreeplayColours.DEEP_PINK, 230, 44),
                    new FreeplayButton("Сохранить файлом", saveFile, FreeplayColours.DEEP_BLUE, 230, 44),
                    new FreeplayButton("В мои сиды", addToMine, new osuTK.Graphics.Color4(150, 115, 210, 255), 230, 44),
                }
            });

            Body.Add(status = new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 15))
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Colour = FreeplayColours.TEXT_DIM,
                Text = "Сид — вся карта целиком: у друга будет ровно она, если у него есть эта песня. Код длинный, поэтому если не влезает в сообщение, отправь файлом.",
            });
        }

        private void copy()
        {
            clipboard.SetText(seed.ToCode());
            status.Text = "Скопировано. Вставь другу, а он — в «Сиды» в меню.";
            onUsed?.Invoke();
        }

        private void saveFile()
        {
            string path = storage.SaveSeedFile(seed);
            storage.PresentSeedFile(path);
            status.Text = $"Сохранено: {System.IO.Path.GetFileName(path)}. Друг перетаскивает файл в окно игры.";
            onUsed?.Invoke();
        }

        private void addToMine()
        {
            var saved = storage.AddSeed(seed);
            status.Text = $"Есть в «Сидах» как «{saved.Name}».";
            onUsed?.Invoke();
        }
    }
}
