// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Freeplay.Configuration;
using osu.Game.Rulesets.Freeplay.Screens.Controls;
using osuTK.Graphics;
using Vector2 = osuTK.Vector2;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// A page of settings laid out like osu!stable's options: the page's sections listed on the left (click one to jump to it),
    /// and the sections themselves on the right. Saved when the page is left.
    /// </summary>
    public abstract partial class FreeplayFormScreen : FreeplayScreen
    {
        private const float sidebar_width = 240;
        private const float page_width = 720;

        protected readonly FreeplayStorage FreeplayStorage;
        protected readonly FreeplaySettings Settings;

        /// <summary>
        /// Where controls are added: the current section (the last <see cref="AddHeading"/>), or the top of the page before any.
        /// </summary>
        protected FillFlowContainer Flow { get; private set; } = null!;

        private FillFlowContainer sections = null!;
        private FillFlowContainer sidebar = null!;
        private OsuScrollContainer scroll = null!;

        private readonly List<(Drawable panel, FillFlowContainer flow, SectionLink link)> links = new List<(Drawable, FillFlowContainer, SectionLink)>();

        protected FreeplayFormScreen(FreeplayStorage freeplayStorage)
        {
            FreeplayStorage = freeplayStorage;
            Settings = FreeplaySettings.Current;
        }

        /// <summary>
        /// The page's name, shown above its sections.
        /// </summary>
        protected abstract string PageTitle { get; }

        [BackgroundDependencyLoader]
        private void load()
        {
            Content.Add(new Container
            {
                RelativeSizeAxes = Axes.Y,
                Width = sidebar_width + page_width + 30,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Children = new Drawable[]
                {
                    sidebar = new FillFlowContainer
                    {
                        Width = sidebar_width,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 2),
                    },
                    scroll = new OsuScrollContainer
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = page_width,
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Child = sections = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 14),
                            Padding = new MarginPadding { Bottom = 60, Right = 12 },
                        },
                    },
                }
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Here rather than in load(), so that pages' own [Resolved] dependencies (injected after this class's load) are available.
            RebuildForm();
        }

        /// <summary>
        /// Builds (or rebuilds, e.g. after a reset) the page.
        /// </summary>
        protected void RebuildForm()
        {
            sections.Clear();
            sidebar.Clear();
            links.Clear();

            sidebar.Add(Heading(PageTitle, 38).With(h => h.Margin = new MarginPadding { Bottom = 14 }));

            Flow = createSection();
            sections.Add(Flow);

            CreateForm();

            // Sections with nothing in them (e.g. an option a mode doesn't have) aren't worth a link.
            foreach (var (panel, _, link) in links.Where(l => l.flow.Count <= 1))
            {
                panel.Hide();
                link.Hide();
            }
        }

        protected abstract void CreateForm();

        protected override void Update()
        {
            base.Update();

            // Highlight the section being read.
            SectionLink? active = null;

            foreach (var (panel, _, link) in links)
            {
                if (panel.IsPresent && scroll.GetChildPosInContent(panel) <= scroll.Current + 60)
                    active = link;
            }

            active ??= links.FirstOrDefault().link;

            foreach (var (_, _, link) in links)
                link.Active = link == active;
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            FreeplayStorage.SaveSettings(Settings);
            return base.OnExiting(e);
        }

        private static FillFlowContainer createSection() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 2),
        };

        /// <summary>
        /// Starts a new section, with a link to it on the left.
        /// </summary>
        protected Drawable AddHeading(string text)
        {
            var section = createSection();

            var panel = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = FreeplayStyle.CORNER_RADIUS + 2,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayStyle.PANEL, Alpha = 0.85f },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = 4,
                        Colour = ColourInfo.GradientVertical(FreeplayColours.BLUE, FreeplayColours.PINK),
                    },
                    section.With(s => s.Padding = new MarginPadding { Left = 8, Vertical = 10 }),
                }
            };

            section.Add(new OsuSpriteText
            {
                Text = text,
                Font = OsuFont.GetFont(Typeface.TorusAlternate, 24, FontWeight.Bold),
                Colour = FreeplayColours.PINK,
                Margin = new MarginPadding { Left = FreeplayStyle.ROW_PADDING, Bottom = 4 },
            });

            sections.Add(panel);
            Flow = section;

            var link = new SectionLink(text, () => scroll.ScrollIntoView(panel));
            sidebar.Add(link);
            links.Add((panel, section, link));
            return panel;
        }

        /// <summary>
        /// Scrolls the page so <paramref name="drawable"/> (e.g. a section from <see cref="AddHeading"/>) is in view, once it has been laid out.
        /// </summary>
        protected void ScrollTo(Drawable drawable) => ScheduleAfterChildren(() => scroll.ScrollIntoView(drawable));

        protected void AddHint(string text) => Flow.Add(FreeplayStyle.Hint(text).With(h => h.Padding = new MarginPadding { Horizontal = FreeplayStyle.ROW_PADDING, Vertical = 4 }));

        protected Drawable AddSlider(string caption, double value, double min, double max, double step, Action<double> apply, Func<double, string>? format = null, string? hint = null,
                                     bool percentage = false)
        {
            format ??= percentage ? v => $"{v:P0}" : v => $"{v}";
            return AddControl(new FreeplaySlider<double>(caption, BindDouble(value, min, max, step, apply), format, hint, (float)step));
        }

        protected Drawable AddIntSlider(string caption, int value, int min, int max, Action<int> apply, Func<int, string>? format = null, string? hint = null)
            => AddControl(new FreeplaySlider<int>(caption, BindInt(value, min, max, apply), format ?? (v => $"{v}"), hint, 1));

        protected Drawable AddCheckbox(string caption, bool value, Action<bool> apply, string? hint = null) => AddCheckbox(caption, Bind(value, apply), hint);

        protected Drawable AddCheckbox(string caption, Bindable<bool> current, string? hint = null) => AddControl(new FreeplayCheckbox(caption, current, hint));

        protected Drawable AddDropdown<T>(string caption, Bindable<T> current, IEnumerable<T> items, Func<T, string>? describe = null, string? hint = null)
            => AddControl(new FreeplayDropdown<T>(caption, current, items, describe, hint));

        protected Drawable AddEnumDropdown<T>(string caption, Bindable<T> current, string? hint = null)
            where T : struct, Enum
            => AddDropdown(caption, current, Enum.GetValues<T>(), v => v.GetDescription(), hint);

        protected Drawable AddTextBox(string caption, Bindable<string> current, string placeholder, string? hint = null) => AddControl(new FreeplayTextBox(caption, current, placeholder, hint));

        protected Drawable AddButton(string text, Action action, bool blue = false)
            => AddControl(new FreeplayButton(text, action, blue ? FreeplayColours.DEEP_BLUE : FreeplayColours.DEEP_PINK, page_width - 60)
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Margin = new MarginPadding { Vertical = 6 },
            });

        protected T AddControl<T>(T control)
            where T : Drawable
        {
            Flow.Add(control);
            return control;
        }

        /// <summary>
        /// A checkbox and slider for one difficulty attribute, where unchecked means "derive from star rating".
        /// </summary>
        protected void AddDifficultyOverride(string caption, float? value, float min, float max, Action<float?> apply)
        {
            var enabled = new BindableBool(value != null);
            var number = new BindableFloat(value ?? 5) { MinValue = min, MaxValue = max, Precision = 0.1f };

            enabled.BindValueChanged(e => apply(e.NewValue ? number.Value : null));

            number.BindValueChanged(n =>
            {
                if (enabled.Value)
                    apply(n.NewValue);
            });

            AddCheckbox($"{caption}: своё значение", enabled, "Выключено — подбирается по звёздам.");

            var slider = AddControl(new FreeplaySlider<float>(caption, number, v => $"{v:0.0}", keyboardStep: 0.1f));

            ShowWhen(slider, enabled, e => e);
        }

        /// <summary>
        /// Called after any change to a control created while this is set (e.g. to notice fine tuning being changed).
        /// </summary>
        protected Action? ChangeHook { get; set; }

        /// <summary>
        /// Fail conditions the player sets themselves.
        /// </summary>
        protected void AddFailControls(FailSettings fail)
        {
            AddCheckbox("Поражение при нулевом HP", fail.Health, v => fail.Health = v);

            var carryBindable = Bind(fail.CarryHealth, v => fail.CarryHealth = v);
            AddCheckbox("Переносить HP между треками", carryBindable, "Иначе каждый трек начинается с полным здоровьем.");

            var refill = AddSlider("Восстановление HP после трека", fail.HealthRefill, 0, 1, 0.05, v => fail.HealthRefill = v, percentage: true);
            ShowWhen(refill, carryBindable, c => c);

            AddIntSlider("Лимит промахов за забег", fail.MissLimit, 0, 200, v => fail.MissLimit = v, v => v == 0 ? "выкл" : v == 1 ? "sudden death" : $"{v}", "1 — sudden death.");
            AddSlider("Минимальная точность", fail.MinAccuracy, 0, 0.99, 0.01, v => fail.MinAccuracy = v, v => v == 0 ? "выкл" : $"{v:P0}");
            AddSlider("Лимит времени забега", fail.TimeLimitMinutes, 0, 120, 1, v => fail.TimeLimitMinutes = v, v => v == 0 ? "выкл" : $"{v:0} мин",
                "Забег завершится успешно, когда время выйдет.");
        }

        /// <summary>
        /// Shows a dependent control only while its controlling setting makes it relevant.
        /// </summary>
        protected static void ShowWhen<T>(Drawable drawable, IBindable<T> source, Func<T, bool> condition)
            => source.BindValueChanged(v => drawable.Alpha = condition(v.NewValue) ? 1 : 0, true);

        protected Bindable<T> Bind<T>(T value, Action<T> apply)
        {
            var bindable = new Bindable<T>(value);
            var hook = ChangeHook;
            bindable.BindValueChanged(v => { apply(v.NewValue); hook?.Invoke(); });
            return bindable;
        }

        protected BindableDouble BindDouble(double value, double min, double max, double precision, Action<double> apply)
        {
            var bindable = new BindableDouble(value) { MinValue = min, MaxValue = max, Precision = precision };
            var hook = ChangeHook;
            bindable.BindValueChanged(v => { apply(v.NewValue); hook?.Invoke(); });
            return bindable;
        }

        protected BindableInt BindInt(int value, int min, int max, Action<int> apply)
        {
            var bindable = new BindableInt(value) { MinValue = min, MaxValue = max };
            var hook = ChangeHook;
            bindable.BindValueChanged(v => { apply(v.NewValue); hook?.Invoke(); });
            return bindable;
        }

        /// <summary>
        /// Collection names match ignoring case and surrounding spaces, like <see cref="Stable.StableLibrary.FindCollection"/>.
        /// </summary>
        protected static bool SameCollection(string? a, string? b)
        {
            if (a == null || b == null)
                return a == b;

            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        protected record CollectionOption(string? Name, string Label)
        {
            public override string ToString() => Label;
        }

        /// <summary>
        /// A section's name on the left of the page, like osu!stable's options sidebar.
        /// </summary>
        private partial class SectionLink : ClickableContainer
        {
            private readonly Box bar;
            private readonly OsuSpriteText text;

            private bool active;

            public bool Active
            {
                set
                {
                    if (active == value)
                        return;

                    active = value;
                    bar.ResizeWidthTo(value ? 4 : 0, 200, Easing.OutQuint);
                    text.FadeColour(value ? FreeplayColours.PINK : (Color4)FreeplayColours.TEXT_DIM, 200);
                    text.MoveToX(value ? 14 : 8, 200, Easing.OutQuint);
                }
            }

            public SectionLink(string title, Action action)
            {
                RelativeSizeAxes = Axes.X;
                Height = 34;
                Action = action;

                Children = new Drawable[]
                {
                    bar = new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = 0,
                        Colour = FreeplayColours.PINK,
                    },
                    text = new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 8,
                        Text = title,
                        Font = OsuFont.GetFont(size: 18, weight: FontWeight.SemiBold),
                        Colour = FreeplayColours.TEXT_DIM,
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!active)
                    text.FadeColour(FreeplayColours.WHITE, 150);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (!active)
                    text.FadeColour(FreeplayColours.TEXT_DIM, 300);
                base.OnHoverLost(e);
            }
        }
    }
}
