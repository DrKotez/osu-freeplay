// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Generation;
using osu.Game.Rulesets.Freeplay.Screens.Controls;
using osuTK;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// One mode's own settings. A mode with goals (aim) is set up by picking a goal, with everything else hidden under fine tuning;
    /// other modes show only what makes sense for them.
    /// </summary>
    public partial class FreeplayModeSettingsScreen : FreeplayFormScreen
    {
        public override string Title => $"Режим «{mode.Name}»";

        protected override string PageTitle => mode.Name;

        private readonly FreeplayMode mode;

        private readonly Stable.StableLibrary library;

        private bool fineTuningOpen;
        private bool scrollToFineTuning;

        private OsuSpriteText? goalName;
        private OsuTextFlowContainer? starsHint;
        private BindableDouble? starsLow, starsHigh;

        /// <summary>
        /// Set while the page itself changes fine tuning values (e.g. following the level), which isn't the player editing them.
        /// </summary>
        private bool syncing;

        public FreeplayModeSettingsScreen(FreeplayStorage freeplayStorage, Stable.StableLibrary library, FreeplayMode mode)
            : base(freeplayStorage)
        {
            this.mode = mode;
            this.library = library;
        }

        protected override void CreateForm()
        {
            var s = Settings.GetMode(mode);
            starsLow = starsHigh = null;
            goalName = null;
            starsHint = null;

            if (!mode.HasGoals)
            {
                AddHint(mode.Description);
                createFineTuning(s);
                createTracks(s);
                createMap(s);
                createFail(s);
                createSeedAndReset(s);
                return;
            }

            createGoals(s);
            createTracks(s);
            createMap(s);
            createFail(s);

            var fineTuning = AddHeading("Тонкая настройка");

            AddHint(fineTuningOpen
                ? "Здесь значения выбранной цели. Если что-то поменять, цель станет «Своя» и перестанет подстраивать треки сама (разминка, сюрприз)."
                : "Все ползунки генерации: звёзды, плотность, дистанции, IQ, фигуры, слайдеры. Заполнены значениями цели.");

            AddButton(fineTuningOpen ? "Скрыть тонкую настройку" : "Показать тонкую настройку", () =>
            {
                fineTuningOpen = !fineTuningOpen;
                scrollToFineTuning = fineTuningOpen;
                RebuildForm();
            }, blue: true);

            if (!fineTuningOpen)
                return;

            if (s.Goal is AimGoal goal)
            {
                AddButton($"Вернуть как в цели «{goal.GetDescription()}»", () =>
                {
                    AimGoals.Apply(s, goal, Settings.PlayerLevel);
                    RebuildForm();
                });
            }

            if (scrollToFineTuning)
            {
                scrollToFineTuning = false;
                ScrollTo(fineTuning);
            }

            ChangeHook = () => markEdited(s);
            createFineTuning(s);
            ChangeHook = null;

            createSeedAndReset(s);
        }

        #region Simple

        private void createGoals(FreeplayGenerationSettings s)
        {
            AddHeading("Цель");

            goalName = AddControl(FreeplayStyle.Label(string.Empty, 20, FontWeight.Bold).With(t =>
            {
                t.Colour = FreeplayColours.WHITE;
                t.Margin = new MarginPadding { Left = FreeplayStyle.ROW_PADDING, Bottom = 4 };
            }));
            updateGoalName(s);

            var cards = AddControl(new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Spacing = new Vector2(8),
                Padding = new MarginPadding { Horizontal = FreeplayStyle.ROW_PADDING, Vertical = 4 },
            });

            foreach (var goal in AimGoals.ALL)
            {
                var g = goal;
                cards.Add(new GoalCard(g, s.Goal == g, () =>
                {
                    AimGoals.Apply(s, g, Settings.PlayerLevel);
                    RebuildForm();
                }));
            }

            if (s.Goal is AimGoal selected)
                AddHint(AimGoals.Describe(selected));

            AddControl(new FreeplaySlider<double>("Мой уровень", BindDouble(Settings.PlayerLevel, 1, 10, 0.1, v =>
            {
                Settings.PlayerLevel = v;
                AimGoals.ApplyStars(s, v);
                syncStars(s);
            }), v => $"{v:0.0}★", "Звёзды, на которых тебе комфортно. Цель ставит звёзды от него: разминка ниже, фарм около."));

            starsHint = AddControl(FreeplayStyle.Hint(string.Empty).With(h => h.Padding = new MarginPadding { Horizontal = FreeplayStyle.ROW_PADDING, Vertical = 4 }));
            updateStarsHint(s);

            AddCheckbox("Бёрсты и тройки", s.AllowStreams, v => s.AllowStreams = v,
                "Включено — где в музыке быстрые ноты, будут тройки и бёрсты до 5 нот (стримов не будет). Выключено — лишние ноты быстрых мест пропускаются.");
        }

        private void createTracks(FreeplayGenerationSettings s)
        {
            AddHeading("Треки");

            var trackOptions = new List<string?> { null, FreeplayGenerationSettings.WHOLE_LIBRARY };
            trackOptions.AddRange(library.Collections.Select(c => c.Name));

            string? currentTracks = s.TrackCollection is null or FreeplayGenerationSettings.WHOLE_LIBRARY
                ? s.TrackCollection
                : library.Collections.FirstOrDefault(c => SameCollection(c.Name, s.TrackCollection))?.Name ?? s.TrackCollection;

            if (currentTracks != null && !trackOptions.Contains(currentTracks))
                trackOptions.Add(currentTracks);

            AddDropdown("Откуда брать треки", Bind(currentTracks, v => s.TrackCollection = v), trackOptions, v => DescribeTracks(v, Settings, library),
                "Своя коллекция для этого режима или вся библиотека. «Как в общих настройках» — то, что выбрано в Настройки → Треки.");

            AddSlider("Мин. BPM", s.MinBpm, 0, 400, 5, v => s.MinBpm = v, bpmLabel);
            AddSlider("Макс. BPM", s.MaxBpm, 0, 400, 5, v => s.MaxBpm = v, bpmLabel);
        }

        private void createMap(FreeplayGenerationSettings s)
        {
            AddHeading("CS / AR / OD / HP");

            AddDifficultyOverride("CS (размер кругов)", s.CircleSize, 2, 7, v => s.CircleSize = v);
            AddDifficultyOverride("AR (скорость появления)", s.ApproachRate, 1, 10, v => s.ApproachRate = v);
            AddDifficultyOverride("OD (точность)", s.OverallDifficulty, 1, 10, v => s.OverallDifficulty = v);
            AddDifficultyOverride("HP (дрейн здоровья)", s.DrainRate, 1, 10, v => s.DrainRate = v);
        }

        private void createFail(FreeplayGenerationSettings s)
        {
            AddHeading("Поражение");

            AddEnumDropdown("Поражение", Bind(s.FailLevel, v =>
            {
                s.FailLevel = v;
                // Custom shows its own controls below.
                Schedule(RebuildForm);
            }));

            AddHint(describeFailLevel(s.FailLevel));

            if (s.FailLevel == FailLevel.Custom)
                AddFailControls(Settings.Fail);
        }

        private string describeFailLevel(FailLevel level) => level switch
        {
            FailLevel.Off => "Без поражения: забег идёт, пока не выйдешь сам.",
            FailLevel.Normal => "HP переносится между треками, после каждого трека +30%. Кончилось HP — конец забега.",
            FailLevel.Hard => "HP переносится, после трека всего +10%, а точность ниже 90% — сразу конец забега.",
            _ => $"Свои условия: {AimGoals.DescribeFail(Settings.Fail)}.",
        };

        private void markEdited(FreeplayGenerationSettings s)
        {
            if (syncing || s.Goal == null || s.GoalEdited)
                return;

            s.GoalEdited = true;
            updateGoalName(s);
        }

        private void updateGoalName(FreeplayGenerationSettings s)
        {
            if (goalName != null)
                goalName.Text = AimGoals.Name(s);
        }

        private void syncStars(FreeplayGenerationSettings s)
        {
            syncing = true;

            if (starsLow != null)
                starsLow.Value = s.StarRating;
            if (starsHigh != null && s.StarRatingMax != null)
                starsHigh.Value = s.StarRatingMax.Value;

            syncing = false;
            updateStarsHint(s);
        }

        private void updateStarsHint(FreeplayGenerationSettings s)
        {
            if (starsHint == null)
                return;

            double low = s.StarRating, high = s.StarRatingMax ?? s.StarRating;

            starsHint.Text = s.Goal == AimGoal.WarmUp && !s.GoalEdited
                ? $"Звёзды: с {low:0.0}★ до {high:0.0}★ за {AimGoals.WARM_UP_TRACKS} трека, дальше около {high:0.0}★."
                : $"Звёзды: {Math.Min(low, high):0.0}–{Math.Max(low, high):0.0}★, на каждый трек свои.";
        }

        #endregion

        #region Fine tuning

        private void createFineTuning(FreeplayGenerationSettings s)
        {
            bool remix = s.RhythmSource == RhythmSource.Remix;

            var ranges = Bind(s.UseRanges, v => s.UseRanges = v);

            AddHeading("Сложность");

            if (!remix)
            {
                AddCheckbox("Разброс настроек", ranges,
                    "Каждый трек берёт свои значения в диапазоне «от — до»: например, плотность 70–100% или 4.5–5.5★. Так забег не однообразный.");
            }

            (starsLow, starsHigh) = ranged(ranges, "Звёзды", s.StarRating, s.StarRatingMax, 1, 10, 0.1, v =>
            {
                s.StarRating = v;
                updateStarsHint(s);
            }, v =>
            {
                s.StarRatingMax = v;
                updateStarsHint(s);
            }, v => $"{v:0.0}★",
                hint: remix
                    ? "Берётся сложность маппера, чья реальная сложность ближе всего к этим звёздам."
                    : "Карта подгоняется под эти звёзды: ритм, расстояния и стримы. С 6★ чаще попадаются быстрые песни, на медленных столько не набрать честно.");

            if (mode.Options.HasFlag(ModeOptions.Density) || mode.Options.HasFlag(ModeOptions.JumpSpacing) || mode.Options.HasFlag(ModeOptions.StreamSpacing))
                AddHeading("Паттерны");

            if (mode.Has(ModeOptions.Density))
                ranged(ranges, "Плотность", s.Density, s.DensityMax, 0.5, 1.5, 0.05, v => s.Density = v, v => s.DensityMax = v, percent, hint: "Меньше — больше передышек и меньше нот на 1/2.");

            if (mode.Has(ModeOptions.JumpSpacing))
            {
                ranged(ranges, s.Style == PatternStyle.Deathstreams || s.Style == PatternStyle.Streams ? "Прыжки между стримами" : "Дистанция прыжков",
                    s.JumpSpacing, s.JumpSpacingMax, 0.5, 1.8, 0.05, v => s.JumpSpacing = v, v => s.JumpSpacingMax = v, percent,
                    hint: "Калибровка звёзд всё равно подстраивает расстояния, это сдвигает их в свою сторону.");
            }

            if (mode.HasGoals)
            {
                AddCheckbox("Анти-джампы", s.AntiJumps, v => s.AntiJumps = v,
                    "Быстрые ноты — широкий прыжок, медленные — короткий. Наоборот от обычного.");
            }

            if (mode.Has(ModeOptions.StreamSpacing))
                ranged(ranges, "Расстояние в стримах", s.StreamSpacing, s.StreamSpacingMax, 0.5, 1.8, 0.05, v => s.StreamSpacing = v, v => s.StreamSpacingMax = v, percent, hint: "100% — ноты налезают друг на друга, как в обычных стримах.");

            if (mode.Has(ModeOptions.Iq))
            {
                ranged(ranges, "IQ карты", s.PatternIq, s.PatternIqMax, 0, 1, 0.05, v => s.PatternIq = v, v => s.PatternIqMax = v, iqLabel,
                    hint: "Не звёзды, а то, насколько паттерны мудрёные. Низкий — aim slop: туда-обратно, широкие треугольники, ровные расстояния, повторяющиеся мотивы. "
                          + "Высокий — квадраты, звёзды, кресты, прямые и острые углы, неровные расстояния, мотивы постоянно меняются.");
            }

            if (mode.Has(ModeOptions.AllowStreams))
            {
                // With goals, bursts are on the main part of the page.
                if (!mode.HasGoals)
                {
                    AddCheckbox(s.Style == PatternStyle.Jumps ? "Бёрсты и тройки" : "Стримы, тройки и бёрсты", s.AllowStreams, v => s.AllowStreams = v,
                        "Выключено — никаких 1/4: лишние ноты быстрых мест пропускаются.");
                }

                AddCheckbox("Кик-слайдеры вместо пропущенных бёрстов", s.KickSliders, v => s.KickSliders = v,
                    "Когда бёрсты выключены: быстрые места закрываются короткими слайдерами, а не просто теряют ноты.");
            }

            if (mode.Has(ModeOptions.Sliders))
                ranged(ranges, "Количество слайдеров", s.SliderAmount, s.SliderAmountMax, 0, 2, 0.05, v => s.SliderAmount = v, v => s.SliderAmountMax = v, percent, hint: "0% — без слайдеров.");

            if (mode.Has(ModeOptions.Spinners))
                AddCheckbox("Спиннеры", s.AllowSpinners, v => s.AllowSpinners = v);

            if (mode.HasGoals)
            {
                AddSlider("Мин. длина трека", s.MinTrackSeconds ?? 0, 0, 600, 10, v => s.MinTrackSeconds = v == 0 ? null : v,
                    v => v == 0 ? "как в общих настройках" : FormatDuration(v * 1000));
            }

            if (mode.Has(ModeOptions.JumpShapes))
            {
                AddHeading("Откуда паттерны");

                ranged(ranges, "Из коллекций", s.MinedPatternShare, s.MinedPatternShareMax, 0, 1, 0.05, v => s.MinedPatternShare = v, v => s.MinedPatternShareMax = v, v => $"{v:P0} из коллекций · {1 - v:P0} рандом",
                    hint: "Остальное выдумывается на ходу: случайные мотивы (каждый раз новая форма) и немного фигур. Коллекции выбираются в общих настройках.");

                AddCheckbox("Вращать повторяющиеся фигуры", s.RotatePatterns, v => s.RotatePatterns = v,
                    "Второй круг квадрата поворачивается на 45° (выходит восьмиконечная звезда), треугольника — на 60°.");

                AddControl(FreeplayStyle.Label("Фигуры", 19, FontWeight.Bold).With(t => t.Margin = new MarginPadding { Left = FreeplayStyle.ROW_PADDING, Top = 10, Bottom = 2 }));

                foreach (var pattern in Enum.GetValues<JumpPatterns>().Where(p => p != JumpPatterns.None && p != JumpPatterns.All))
                {
                    var flag = pattern;
                    AddCheckbox(flag.GetDescription(), s.Patterns.HasFlag(flag), enabled => s.Patterns = enabled ? s.Patterns | flag : s.Patterns & ~flag);
                }

                AddHint("Если выключить все фигуры, вместо них будут рандомные мотивы.");
            }

            if (mode.Has(ModeOptions.Metronome))
            {
                AddHeading("Фигуры");

                AddHint("Звёздочка прыгает по кругу одной фигуры, потом переходит к следующей. Выключенные фигуры не выпадают.");

                foreach (var shape in Enum.GetValues<MetronomeShapes>().Where(p => p != MetronomeShapes.None && p != MetronomeShapes.All))
                {
                    var flag = shape;
                    AddCheckbox(flag.GetDescription(), s.MetronomeShapes.HasFlag(flag), enabled => s.MetronomeShapes = enabled ? s.MetronomeShapes | flag : s.MetronomeShapes & ~flag);
                }

                AddHint("Если выключить все, будут все. Семи- и восьмиконечная звезда появляются с 5★, звезда Давида — с 4.5★, если выбраны не только они.");

                AddDropdown("Сколько держится фигура", Bind(s.ShapeMeasures, v => s.ShapeMeasures = v), new[] { 0, 1, 2, 4, 8, 16 },
                    v => v == 0 ? "по-разному (чаще 2 такта)" : $"{v} {(v == 1 ? "такт" : v == 2 || v == 4 ? "такта" : "тактов")}");

                AddCheckbox("Фигуры меняются от круга к кругу", s.RotateShapes, v => s.RotateShapes = v,
                    "Вращаются, как крутящаяся звезда, идут в обратную сторону через круг, пульсируют (больше-меньше) или переезжают по полю. Выключено — каждый круг одинаковый.");

                AddCheckbox("Разгон", s.Ramp, v => s.Ramp = v,
                    "Забег начинается с медленной песни, каждый следующий трек чуть быстрее (на 2–15 BPM). Звёзды растут вместе с BPM, фигуры остаются того же размера. Звёзды выше — стартовые. В конце видно, до какого BPM дошёл.");

                AddCheckbox("Всегда полдоли", s.AlwaysHalfBeats, v => s.AlwaysHalfBeats = v,
                    "Прыжки каждые полдоли, даже если для твоих звёзд это слишком быстро. Тогда карта выйдет сложнее, чем выбрано: прыжки станут короче, но звёзд всё равно будет больше.");

                AddHeading("Слабая зона");

                var found = Configuration.AimAnalysis.Latest(FreeplayStorage.LoadHistory()).analysis;
                var foundQuarter = found?.WeakestQuarter();

                AddHint("Игра разбирает каждый мисс «Звёздочки»: куда ты нажал, недовёл или перевёл, в какой четверти поля. Разбор виден в итогах забега и в статистике. "
                        + (foundQuarter is Configuration.PlayfieldQuarter weak
                            ? $"Сейчас слабее всего {AimAnalysisPanel.QuarterName(weak)} (последние {Configuration.AimAnalysis.LATEST_RUNS} забегов)."
                            : $"Чтобы найти слабую зону, нужно хотя бы 30 миссов мимо (пока {found?.AimMisses ?? 0})."));

                AddDropdown("Чаще ставить ноты", Bind(s.WeakZone, v => s.WeakZone = v), Enum.GetValues<WeakZone>(), v => v switch
                {
                    WeakZone.Off => "везде поровну",
                    WeakZone.Auto => foundQuarter is Configuration.PlayfieldQuarter q ? $"в слабую зону (сейчас {AimAnalysisPanel.QuarterName(q)})" : "в слабую зону (пока не найдена)",
                    _ => AimAnalysisPanel.QuarterName((Configuration.PlayfieldQuarter)(v - WeakZone.TopLeft)),
                }, "Фигуры чаще встают в эту четверть поля. Размер прыжков и звёзды не меняются.");

                AddControl(new FreeplaySlider<double>("Нот в этой четверти", BindDouble(s.WeakZoneShare, 0.3, 0.55, 0.05, v => s.WeakZoneShare = v), v => $"{v:P0}",
                    "Без слабой зоны в каждую четверть попадает около 25% нот. Больше половины не бывает: фигуры крупные и в угол целиком не помещаются.", 0.05f));
            }
        }

        private void createSeedAndReset(FreeplayGenerationSettings s)
        {
            if (mode.Has(ModeOptions.Seed))
            {
                AddHeading("Seed");

                AddTextBox("Фиксированный seed", Bind(s.Seed?.ToString() ?? string.Empty, v => s.Seed = int.TryParse(v, out int seed) ? seed : null), "случайный",
                    "Пусто — новая карта каждый раз. Seed каждого трека виден в итогах забега.");
            }

            AddHeading("Сброс");

            AddHint("Вернуть все настройки этого режима к стандартным. Другие режимы и общие настройки не меняются.");
            AddButton("Сбросить режим к стандартным", () =>
            {
                Settings.ResetMode(mode);
                RebuildForm();
            }, blue: true);
        }

        /// <summary>
        /// A setting which can be a range: its own slider, plus an upper end shown while ranges are on.
        /// </summary>
        private (BindableDouble lower, BindableDouble upper) ranged(IBindable<bool> ranges, string caption, double value, double? max, double min, double maxValue, double step,
                                                                    Action<double> apply, Action<double?> applyMax, Func<double, string> format, string? hint = null)
        {
            var lowerValue = BindDouble(value, min, maxValue, step, apply);
            AddControl(new FreeplaySlider<double>(caption, lowerValue, format, hint, (float)step));

            var upperValue = BindDouble(max ?? value, min, maxValue, step, v => applyMax(v));
            var upper = AddControl(new FreeplaySlider<double>($"{caption}: до", upperValue, format, "Верхняя граница разброса. Нижняя — ползунок выше.", (float)step));

            ShowWhen(upper, ranges, r => r);

            // Turning ranges on starts from no range at all, rather than a leftover upper end.
            if (max == null)
                ranges.BindValueChanged(r => { if (r.NewValue) applyMax(upperValue.Value); });

            return (lowerValue, upperValue);
        }

        #endregion

        private static string percent(double v) => $"{v:P0}";

        /// <summary>
        /// Describes a mode's track source (see <see cref="FreeplayGenerationSettings.TrackCollection"/>).
        /// </summary>
        public static string DescribeTracks(string? source, FreeplaySettings settings, Stable.StableLibrary library)
        {
            switch (source)
            {
                case null:
                    string general = settings.Tracks.Source == TrackSource.Collection && settings.Tracks.CollectionName != null
                        ? $"коллекция «{settings.Tracks.CollectionName.Trim()}»"
                        : "вся библиотека";
                    return $"как в общих настройках ({general})";

                case FreeplayGenerationSettings.WHOLE_LIBRARY:
                    return $"вся библиотека ({library.Sets.Count} песен)";

                default:
                    var collection = library.FindCollection(source);
                    return collection == null ? $"коллекция «{source.Trim()}» (не найдена)" : $"коллекция «{collection.Name.Trim()}» ({collection.BeatmapHashes.Count})";
            }
        }

        private static string bpmLabel(double v) => v == 0 ? "без ограничения" : $"{v:0}";

        private static string iqLabel(double v)
        {
            string kind = v < 0.2 ? "aim slop" : v < 0.4 ? "простые" : v < 0.6 ? "средние" : v < 0.8 ? "мудрёные" : "квадраты и звёзды";
            return $"{v * 100:0} · {kind}";
        }

        /// <summary>
        /// One goal to pick, like a difficulty in osu!stable's song select.
        /// </summary>
        public partial class GoalCard : ClickableContainer
        {
            public readonly AimGoal Goal;

            private readonly Box background;
            private readonly bool selected;

            public GoalCard(AimGoal goal, bool selected, Action action)
            {
                Goal = goal;
                this.selected = selected;

                Size = new Vector2(218, 62);
                Action = action;
                Masking = true;
                CornerRadius = FreeplayStyle.CORNER_RADIUS + 2;
                BorderThickness = selected ? 3 : 0;
                BorderColour = FreeplayColours.PINK;

                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = selected ? FreeplayStyle.TRACK : FreeplayStyle.PANEL_HOVER,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 12,
                        Spacing = new Vector2(0, 2),
                        Children = new Drawable[]
                        {
                            FreeplayStyle.Label(goal.GetDescription(), 18, FontWeight.Bold).With(t => t.Colour = selected ? FreeplayColours.PINK : FreeplayColours.WHITE),
                            FreeplayStyle.Label(AimGoals.Tagline(goal), 14, FontWeight.Regular).With(t => t.Colour = FreeplayColours.TEXT_DIM),
                        }
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!selected)
                    background.FadeColour(FreeplayStyle.TRACK, 150);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (!selected)
                    background.FadeColour(FreeplayStyle.PANEL_HOVER, 300);
                base.OnHoverLost(e);
            }
        }
    }
}
