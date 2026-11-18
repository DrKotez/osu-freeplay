// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Screens.Controls;
using osu.Game.Rulesets.Osu;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// freeplay~'s own key bindings page: only the keys freeplay uses, each changed by clicking it and pressing a key.
    /// </summary>
    public partial class FreeplayKeyBindingsScreen : FreeplayFormScreen
    {
        public override string Title => "Клавиши freeplay";

        protected override string PageTitle => "Клавиши";

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        public FreeplayKeyBindingsScreen(FreeplayStorage freeplayStorage)
            : base(freeplayStorage)
        {
        }

        protected override void CreateForm()
        {
            AddHint("Нажми на клавишу и затем на новую. Esc — отмена. Клики мышью для нот работают всегда, отдельно от этих клавиш.");

            AddHeading("Ноты");

            rulesetRow("Левая кнопка", OsuAction.LeftButton);
            rulesetRow("Правая кнопка", OsuAction.RightButton);
            rulesetRow("Дым (рисовать курсором)", OsuAction.Smoke);

            AddHeading("Во время игры");

            globalRow("Пауза", GlobalAction.PauseGameplay);
            globalRow("Быстрый рестарт (удерживать)", GlobalAction.QuickRetry);
            globalRow("Быстрый выход (удерживать)", GlobalAction.QuickExit);
            globalRow("Пропустить вступление", GlobalAction.SkipCutscene);
            globalRow("Спрятать / показать интерфейс", GlobalAction.ToggleInGameInterface);
            globalRow("Показать интерфейс, пока удерживаешь", GlobalAction.HoldForHUD);

            AddHeading("Громкость");

            globalRow("Громче", GlobalAction.IncreaseVolume);
            globalRow("Тише", GlobalAction.DecreaseVolume);
            globalRow("Без звука", GlobalAction.ToggleMute);

            AddHeading("Сброс");

            AddHint("Вернуть все клавиши на этой странице к стандартным (Z, X, Esc, ` и т.д.).");
            AddButton("Сбросить клавиши", resetAll, blue: true);
        }

        private void rulesetRow(string caption, OsuAction action)
            => AddControl(new BindingRow(caption, () => findRuleset(action), (int)action, FreeplayRuleset.SHORT_NAME, write));

        private void globalRow(string caption, GlobalAction action)
            => AddControl(new BindingRow(caption, () => findGlobal(action), (int)action, null, write));

        /// <summary>
        /// The keyboard binding of a ruleset action (mouse buttons are bound alongside and left alone).
        /// </summary>
        private (Guid id, KeyCombination combination)? findRuleset(OsuAction action) => realm.Run(r => r.All<RealmKeyBinding>()
                                                                                                        .Where(b => b.RulesetName == FreeplayRuleset.SHORT_NAME && b.Variant == 0)
                                                                                                        .AsEnumerable()
                                                                                                        .Where(b => b.ActionInt == (int)action && !isMouse(b.KeyCombination))
                                                                                                        .Select(b => ((Guid, KeyCombination)?)(b.ID, b.KeyCombination))
                                                                                                        .FirstOrDefault());

        private (Guid id, KeyCombination combination)? findGlobal(GlobalAction action) => realm.Run(r => r.All<RealmKeyBinding>()
                                                                                                         .Where(b => b.RulesetName == null)
                                                                                                         .AsEnumerable()
                                                                                                         .Where(b => b.ActionInt == (int)action)
                                                                                                         .Select(b => ((Guid, KeyCombination)?)(b.ID, b.KeyCombination))
                                                                                                         .FirstOrDefault());

        private static bool isMouse(KeyCombination combination) => combination.Keys.Any(k => k >= InputKey.FirstMouseButton);

        private void write(Guid? id, int action, string? ruleset, KeyCombination combination) => realm.Write(r =>
        {
            var existing = id == null ? null : r.Find<RealmKeyBinding>(id.Value);

            if (existing != null)
                existing.KeyCombinationString = combination.ToString();
            else
                r.Add(new RealmKeyBinding(action, combination, ruleset, ruleset == null ? null : 0));
        });

        private void resetAll()
        {
            var rulesetDefaults = new FreeplayRuleset().GetDefaultKeyBindings().Where(b => !isMouse(b.KeyCombination)).ToList();

            var globalActions = new[]
            {
                GlobalAction.PauseGameplay, GlobalAction.QuickRetry, GlobalAction.QuickExit, GlobalAction.SkipCutscene,
                GlobalAction.ToggleInGameInterface, GlobalAction.HoldForHUD, GlobalAction.IncreaseVolume, GlobalAction.DecreaseVolume, GlobalAction.ToggleMute,
            };

            var globalDefaults = Enum.GetValues<GlobalActionCategory>()
                                     .SelectMany(GlobalActionContainer.GetDefaultBindingsFor)
                                     .Where(b => globalActions.Contains((GlobalAction)b.Action))
                                     .GroupBy(b => (GlobalAction)b.Action)
                                     .Select(g => g.First())
                                     .ToList();

            foreach (var binding in rulesetDefaults)
            {
                var current = findRuleset((OsuAction)binding.Action);
                write(current?.id, (int)(OsuAction)binding.Action, FreeplayRuleset.SHORT_NAME, binding.KeyCombination);
            }

            foreach (var binding in globalDefaults)
            {
                var current = findGlobal((GlobalAction)binding.Action);
                write(current?.id, (int)(GlobalAction)binding.Action, null, binding.KeyCombination);
            }

            RebuildForm();
        }

        /// <summary>
        /// One action and its key. Clicking the key starts listening for a new one.
        /// </summary>
        private partial class BindingRow : CompositeDrawable
        {
            private readonly Func<(Guid id, KeyCombination combination)?> find;
            private readonly int action;
            private readonly string? ruleset;
            private readonly Action<Guid?, int, string?, KeyCombination> write;

            [Resolved]
            private ReadableKeyCombinationProvider keyCombinationProvider { get; set; } = null!;

            private KeyButton button = null!;

            public BindingRow(string caption, Func<(Guid id, KeyCombination combination)?> find, int action, string? ruleset, Action<Guid?, int, string?, KeyCombination> write)
            {
                this.find = find;
                this.action = action;
                this.ruleset = ruleset;
                this.write = write;

                RelativeSizeAxes = Axes.X;
                Height = 44;
                Padding = new MarginPadding { Horizontal = FreeplayStyle.ROW_PADDING };

                InternalChildren = new Drawable[]
                {
                    FreeplayStyle.Label(caption).With(l =>
                    {
                        l.Anchor = Anchor.CentreLeft;
                        l.Origin = Anchor.CentreLeft;
                    }),
                    button = new KeyButton(onBound)
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                refresh();
            }

            private void refresh()
            {
                var current = find();
                button.Text = current == null ? "не назначено" : keyCombinationProvider.GetReadableString(current.Value.combination);
            }

            private void onBound(KeyCombination combination)
            {
                write(find()?.id, action, ruleset, combination);
                refresh();
            }
        }

        /// <summary>
        /// Shows a key; when clicked, takes focus and binds the next key pressed.
        /// </summary>
        private partial class KeyButton : ClickableContainer
        {
            private readonly Action<KeyCombination> bound;
            private readonly Box background;
            private readonly OsuSpriteText text;

            private string label = string.Empty;
            private bool listening;

            public string Text
            {
                set
                {
                    label = value;
                    if (!listening)
                        text.Text = value;
                }
            }

            public override bool AcceptsFocus => true;

            public KeyButton(Action<KeyCombination> bound)
            {
                this.bound = bound;

                Size = new Vector2(220, 32);
                Masking = true;
                CornerRadius = FreeplayStyle.CORNER_RADIUS;
                BorderThickness = 2;
                BorderColour = FreeplayColours.DEEP_PINK;

                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayStyle.PANEL },
                    text = new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = OsuFont.GetFont(size: 16, weight: FontWeight.Bold),
                        Colour = FreeplayColours.WHITE,
                    },
                };
            }

            protected override bool OnClick(ClickEvent e)
            {
                if (!listening)
                    GetContainingFocusManager()?.ChangeFocus(this);
                return true;
            }

            protected override void OnFocus(FocusEvent e)
            {
                listening = true;
                text.Text = "нажми клавишу…";
                text.Colour = FreeplayColours.PINK;
                background.FadeColour(FreeplayStyle.PANEL_HOVER, 150);
                base.OnFocus(e);
            }

            protected override void OnFocusLost(FocusLostEvent e)
            {
                listening = false;
                text.Text = label;
                text.Colour = FreeplayColours.WHITE;
                background.FadeColour(FreeplayStyle.PANEL, 150);
                base.OnFocusLost(e);
            }

            protected override bool OnKeyDown(KeyDownEvent e)
            {
                if (!listening)
                    return false;

                if (e.Key == Key.Escape)
                {
                    GetContainingFocusManager()?.ChangeFocus(null);
                    return true;
                }

                // Wait for the key itself when only a modifier is held, so Shift+Tab can be bound.
                if (e.Key is Key.ShiftLeft or Key.ShiftRight or Key.ControlLeft or Key.ControlRight or Key.AltLeft or Key.AltRight or Key.WinLeft or Key.WinRight)
                    return true;

                bound(KeyCombination.FromInputState(e.CurrentState));
                GetContainingFocusManager()?.ChangeFocus(null);
                return true;
            }
        }
    }
}
