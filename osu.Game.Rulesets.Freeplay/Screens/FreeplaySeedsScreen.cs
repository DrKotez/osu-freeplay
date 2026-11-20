// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Screens.Controls;
using osu.Game.Rulesets.Freeplay.Stable;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// The player's own seeds: maps shared as codes or files, named as they like, played on their song.
    /// </summary>
    public partial class FreeplaySeedsScreen : FreeplayFormScreen
    {
        public override string Title => "Сиды freeplay";

        protected override string PageTitle => "Сиды";

        private readonly StableLibrary library;

        [Resolved]
        private Clipboard clipboard { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        private string pasted = string.Empty;
        private string? message;

        public FreeplaySeedsScreen(FreeplayStorage freeplayStorage, StableLibrary library)
            : base(freeplayStorage)
        {
            this.library = library;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            FreeplayDrops.Handled += onDropped;
        }

        private void onDropped(string what) => Schedule(() =>
        {
            message = what;
            RebuildForm();
        });

        protected override void CreateForm()
        {
            AddHeading("Добавить сид");

            AddHint("Сид — карта, которую кто-то сыграл, целиком. Вставь код (кнопка «Скопировать сид» после трека) или перетащи файл .fpseed в окно игры.");

            AddTextBox("Код сида", Bind(pasted, v => pasted = v), "freeplay~1:…");
            AddButton("Добавить код из поля", () => add(pasted));
            AddButton("Вставить из буфера обмена и добавить", () => add(clipboard.GetText() ?? string.Empty), blue: true);

            if (message != null)
                AddHint(message);

            // (A rename still waiting to be saved goes first, so the list read is up to date.)
            saveNow();
            var seeds = FreeplayStorage.LoadSeeds();

            AddHeading($"Мои сиды ({seeds.Count})");

            if (seeds.Count == 0)
            {
                AddHint("Пока пусто. Свои сиды добавляются кнопкой «В мои сиды» после трека.");
                return;
            }

            AddHint("Название можно поменять. Если песни нет, скачай её с сайта osu! и перетащи скачанный .osz в окно игры: она ляжет в Songs osu!stable.");

            foreach (var seed in seeds.AsEnumerable().Reverse())
                addSeed(seeds, seed);
        }

        private void add(string text)
        {
            if (SeedMap.FromCode(text) is not SeedMap seed)
            {
                message = "Это не код сида: он начинается с «freeplay~1:». Если код длинный, его могли обрезать — попроси файл.";
                RebuildForm();
                return;
            }

            var saved = FreeplayStorage.AddSeed(seed);
            pasted = string.Empty;
            message = $"Добавлен: «{saved.Name}».";
            RebuildForm();
        }

        private void addSeed(List<SavedSeed> seeds, SavedSeed saved)
        {
            if (saved.Map is not SeedMap seed)
            {
                AddHint($"«{saved.Name}»: сид повреждён.");
                AddButton($"Удалить «{saved.Name}»", () => remove(seeds, saved), blue: true);
                return;
            }

            bool hasSong = library.FindByMD5(seed.Song.MD5) != null;

            AddTextBox("Название", Bind(saved.Name, v =>
            {
                saved.Name = v;
                saveSoon(seeds);
            }), seed.Song.Title);

            AddHint($"{seed.Song.Name} · {seed.Description} · сид {seed.Seed}"
                    + (hasSong ? string.Empty : library.IsScanning ? " · песню ещё ищу…" : " · НЕТ ПЕСНИ"));

            var row = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(8, 0),
                Margin = new MarginPadding { Left = FreeplayStyle.ROW_PADDING, Bottom = 18, Top = 4 },
            };

            if (hasSong)
                row.Add(new FreeplayButton("Играть", () => this.Push(new FreeplayRunScreen(FreeplayStorage, library, seed, saved.Name)), FreeplayColours.DEEP_PINK, 150, 40));
            else if (seed.Song.DownloadUrl is string url)
                row.Add(new FreeplayButton("Скачать песню", () => host.OpenUrlExternally(url), FreeplayColours.DEEP_PINK, 190, 40));

            row.Add(new FreeplayButton("Скопировать", () =>
            {
                clipboard.SetText(saved.Code);
                message = $"Код «{saved.Name}» скопирован.";
                RebuildForm();
            }, FreeplayColours.DEEP_BLUE, 150, 40));

            row.Add(new FreeplayButton("Файлом", () => FreeplayStorage.PresentSeedFile(FreeplayStorage.SaveSeedFile(seed)), FreeplayColours.DEEP_BLUE, 120, 40));
            row.Add(new FreeplayButton("Удалить", () => remove(seeds, saved), new Color4(90, 70, 110, 255), 120, 40));

            AddControl(row);
        }

        private ScheduledDelegate? pendingSave;
        private List<SavedSeed>? unsaved;

        /// <summary>
        /// Saves a moment after the last change (FIX: renaming used to write the whole file on every letter typed).
        /// </summary>
        private void saveSoon(List<SavedSeed> seeds)
        {
            unsaved = seeds;
            pendingSave?.Cancel();
            pendingSave = Scheduler.AddDelayed(saveNow, 500);
        }

        private void saveNow()
        {
            pendingSave?.Cancel();
            pendingSave = null;

            if (unsaved != null)
                FreeplayStorage.SaveSeeds(unsaved);

            unsaved = null;
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            saveNow();
            return base.OnExiting(e);
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            saveNow();
            base.OnSuspending(e);
        }

        private void remove(List<SavedSeed> seeds, SavedSeed saved)
        {
            seeds.Remove(saved);
            FreeplayStorage.SaveSeeds(seeds);
            message = $"«{saved.Name}» удалён.";
            RebuildForm();
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);
            RebuildForm();
        }

        protected override void Dispose(bool isDisposing)
        {
            FreeplayDrops.Handled -= onDropped;
            base.Dispose(isDisposing);
        }
    }
}
