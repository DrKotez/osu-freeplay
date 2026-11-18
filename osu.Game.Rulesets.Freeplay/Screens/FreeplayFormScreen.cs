// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Rulesets.Freeplay.Configuration;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// A scrolling page of settings, saved when the page is left.
    /// </summary>
    public abstract partial class FreeplayFormScreen : FreeplayScreen
    {
        protected readonly FreeplayStorage FreeplayStorage;
        protected readonly FreeplaySettings Settings;

        [Cached]
        protected readonly OverlayColourProvider ColourProvider = new OverlayColourProvider(OverlayColourScheme.Pink);

        protected FillFlowContainer Flow { get; private set; } = null!;

        protected FreeplayFormScreen(FreeplayStorage freeplayStorage)
        {
            FreeplayStorage = freeplayStorage;
            Settings = FreeplaySettings.Current;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Content.Add(new OsuScrollContainer
            {
                RelativeSizeAxes = Axes.Y,
                Width = CONTENT_WIDTH,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Child = Flow = Column(8),
            });

            CreateForm();
        }

        protected abstract void CreateForm();

        public override bool OnExiting(ScreenExitEvent e)
        {
            FreeplayStorage.SaveSettings(Settings);
            return base.OnExiting(e);
        }

        protected void AddHeading(string text) => Flow.Add(Text(text, 28, FontWeight.Bold).With(t => t.Margin = new MarginPadding { Top = Flow.Count > 0 ? 20 : 0, Bottom = 4 }));

        protected void AddHint(string text) => Flow.Add(new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 15))
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Colour = ColourProvider.Content2,
            Text = text,
        });

        protected void AddSlider(string caption, double value, double min, double max, double step, Action<double> apply, Func<double, string>? format = null, string? hint = null,
                                 bool percentage = false)
        {
            var slider = new FormSliderBar<double>
            {
                Caption = caption,
                HintText = hint ?? string.Empty,
                Current = BindDouble(value, min, max, step, apply),
                KeyboardStep = (float)step,
                DisplayAsPercentage = percentage,
            };

            if (format != null)
                slider.LabelFormat = v => format(v);

            Flow.Add(slider);
        }

        protected void AddCheckbox(string caption, bool value, Action<bool> apply, string? hint = null)
        {
            Flow.Add(new FormCheckBox
            {
                Caption = caption,
                HintText = hint ?? string.Empty,
                Current = Bind(value, apply),
            });
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

            Flow.Add(new FormCheckBox
            {
                Caption = $"{caption}: своё значение",
                HintText = "Выключено — подбирается по звёздам.",
                Current = enabled,
            });

            var slider = new FormSliderBar<float>
            {
                Caption = caption,
                Current = number,
                KeyboardStep = 0.1f,
            };

            Flow.Add(slider);

            ShowWhen(slider, enabled, e => e);
        }

        /// <summary>
        /// Shows a dependent control only while its controlling setting makes it relevant.
        /// </summary>
        protected static void ShowWhen<T>(Drawable drawable, IBindable<T> source, Func<T, bool> condition)
            => source.BindValueChanged(v => drawable.Alpha = condition(v.NewValue) ? 1 : 0, true);

        protected static Bindable<T> Bind<T>(T value, Action<T> apply)
        {
            var bindable = new Bindable<T>(value);
            bindable.BindValueChanged(v => apply(v.NewValue));
            return bindable;
        }

        protected static BindableDouble BindDouble(double value, double min, double max, double precision, Action<double> apply)
        {
            var bindable = new BindableDouble(value) { MinValue = min, MaxValue = max, Precision = precision };
            bindable.BindValueChanged(v => apply(v.NewValue));
            return bindable;
        }

        protected static BindableInt BindInt(int value, int min, int max, Action<int> apply)
        {
            var bindable = new BindableInt(value) { MinValue = min, MaxValue = max };
            bindable.BindValueChanged(v => apply(v.NewValue));
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
    }
}
