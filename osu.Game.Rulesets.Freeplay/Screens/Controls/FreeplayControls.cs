// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osuTK.Graphics;
using Vector2 = osuTK.Vector2;

namespace osu.Game.Rulesets.Freeplay.Screens.Controls
{
    /// <summary>
    /// The look shared by freeplay's controls: flat dark panels with flag-coloured accents, like osu!stable's options.
    /// </summary>
    public static class FreeplayStyle
    {
        public const float ROW_PADDING = 10;
        public const float CORNER_RADIUS = 4;

        public static readonly Color4 PANEL = Color4Extensions.FromHex("231b2e");
        public static readonly Color4 PANEL_HOVER = Color4Extensions.FromHex("2e2440");
        public static readonly Color4 TRACK = Color4Extensions.FromHex("3d3250");

        public static OsuSpriteText Label(string text, float size = 17, FontWeight weight = FontWeight.SemiBold) => new OsuSpriteText
        {
            Text = text,
            Font = OsuFont.GetFont(size: size, weight: weight),
            Colour = FreeplayColours.WHITE,
        };

        /// <summary>
        /// A label which ends in "…" rather than overflowing its width.
        /// </summary>
        public static TruncatingSpriteText TruncatingLabel(string text, float size, FontWeight weight = FontWeight.Regular) => new TruncatingSpriteText
        {
            Text = text,
            Font = OsuFont.GetFont(size: size, weight: weight),
            Colour = FreeplayColours.WHITE,
            RelativeSizeAxes = Axes.X,
        };

        public static OsuTextFlowContainer Hint(string text) => new OsuTextFlowContainer(t =>
        {
            t.Font = OsuFont.GetFont(size: 13.5f);
            t.Colour = FreeplayColours.TEXT_DIM;
        })
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Text = text,
        };
    }

    /// <summary>
    /// A setting's row: a label (with an optional hint underneath) and the control itself.
    /// The row lights up on hover, so it's clear which setting the cursor is on.
    /// </summary>
    public abstract partial class SettingRow : CompositeDrawable
    {
        protected readonly FillFlowContainer Body;
        private readonly Box hover;

        protected SettingRow(string caption, string? hint)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            InternalChildren = new Drawable[]
            {
                hover = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = FreeplayStyle.PANEL_HOVER,
                    Alpha = 0,
                },
                Body = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 6),
                    Padding = new MarginPadding { Horizontal = FreeplayStyle.ROW_PADDING, Vertical = 8 },
                },
            };

            Caption = caption;
            HintText = hint;
        }

        protected readonly string Caption;
        protected readonly string? HintText;

        /// <summary>
        /// Adds the hint under whatever the row put first.
        /// </summary>
        protected void AddHint()
        {
            if (!string.IsNullOrEmpty(HintText))
                Body.Add(FreeplayStyle.Hint(HintText));
        }

        protected override bool OnHover(HoverEvent e)
        {
            hover.FadeTo(0.6f, 150);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hover.FadeOut(300);
            base.OnHoverLost(e);
        }
    }

    /// <summary>
    /// A square tick box beside its label. Clicking anywhere on the row toggles it.
    /// </summary>
    public partial class FreeplayCheckbox : SettingRow
    {
        private const float box_size = 22;

        private readonly Bindable<bool> current;
        // As an object: a drawable unbinds every bindable field it has when disposed, which would cut a global setting (volume, cursor size) off from the game and its config.
        private readonly object source;
        private readonly Box fill;
        private readonly SpriteIcon tick;

        public FreeplayCheckbox(string caption, Bindable<bool> current, string? hint = null)
            : base(caption, hint)
        {
            // A bound copy, unbound with this drawable, so a global setting doesn't keep a closed page alive.
            // Bindings between bindables are weak, so the source is also held here, or it could be collected along with what it saves.
            source = current;
            this.current = current.GetBoundCopy();
            current = this.current;

            Body.Add(new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Children = new Drawable[]
                {
                    new Container
                    {
                        Size = new Vector2(box_size),
                        Masking = true,
                        CornerRadius = FreeplayStyle.CORNER_RADIUS,
                        BorderThickness = 2,
                        BorderColour = FreeplayColours.PINK,
                        Children = new Drawable[]
                        {
                            fill = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientVertical(FreeplayColours.BLUE, FreeplayColours.PINK),
                            },
                            tick = new SpriteIcon
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Size = new Vector2(12),
                                Icon = FontAwesome.Solid.Check,
                                Colour = FreeplayColours.WHITE,
                            },
                        }
                    },
                    FreeplayStyle.Label(caption).With(l =>
                    {
                        l.Anchor = Anchor.CentreLeft;
                        l.Origin = Anchor.CentreLeft;
                        l.X = box_size + 12;
                    }),
                }
            });

            AddHint();

            current.BindValueChanged(v =>
            {
                fill.FadeTo(v.NewValue ? 1 : 0, 120);
                tick.FadeTo(v.NewValue ? 1 : 0, 120);
                tick.ScaleTo(v.NewValue ? 1 : 0.5f, 200, Easing.OutBack);
            }, true);
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (!current.Disabled)
                current.Value = !current.Value;
            return true;
        }
    }

    /// <summary>
    /// A labelled slider with its value shown on the right of the label.
    /// </summary>
    public partial class FreeplaySlider<T> : SettingRow
        where T : struct, INumber<T>, IMinMaxValue<T>
    {
        /// <summary>
        /// Held so it isn't collected: the bar's binding to it is weak, and it may be all that saves the value.
        /// </summary>
        // As an object: a drawable unbinds every bindable field it has when disposed, which would cut a global setting (volume, cursor size) off from the game and its config.
        private readonly object source;

        public FreeplaySlider(string caption, BindableNumber<T> current, Func<T, string> format, string? hint = null, float keyboardStep = 0)
            : base(caption, hint)
        {
            source = current;

            OsuSpriteText value;

            Body.Add(new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Children = new Drawable[]
                {
                    FreeplayStyle.Label(caption),
                    value = FreeplayStyle.Label(string.Empty, weight: FontWeight.Bold).With(v =>
                    {
                        v.Anchor = Anchor.TopRight;
                        v.Origin = Anchor.TopRight;
                        v.Colour = FreeplayColours.PINK;
                    }),
                }
            });

            Bar bar;

            Body.Add(bar = new Bar
            {
                RelativeSizeAxes = Axes.X,
                Height = 20,
                Current = current,
                KeyboardStep = keyboardStep,
            });

            AddHint();

            // Through the bar's own copy, which is unbound with it, so a global setting doesn't keep a closed page alive.
            bar.Current.BindValueChanged(v => value.Text = format(v.NewValue), true);
        }

        private partial class Bar : SliderBar<T>
        {
            private const float nub_size = 16;

            private readonly Box filled;
            private readonly Container nub;

            public Bar()
            {
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 4,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Masking = true,
                        CornerRadius = 2,
                        Padding = new MarginPadding { Horizontal = nub_size / 2 },
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayStyle.TRACK },
                            filled = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientHorizontal(FreeplayColours.BLUE, FreeplayColours.PINK),
                            },
                        }
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Horizontal = nub_size / 2 },
                        Child = nub = new Container
                        {
                            RelativePositionAxes = Axes.X,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.Centre,
                            Size = new Vector2(nub_size),
                            Masking = true,
                            CornerRadius = nub_size / 2,
                            BorderThickness = 3,
                            BorderColour = FreeplayColours.PINK,
                            Child = new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayColours.WHITE },
                        },
                    },
                };
            }

            protected override void UpdateValue(float value)
            {
                filled.ResizeWidthTo(value, 150, Easing.OutQuint);
                nub.MoveToX(value, 150, Easing.OutQuint);
            }

            protected override bool OnHover(HoverEvent e)
            {
                nub.ScaleTo(1.2f, 150, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                nub.ScaleTo(1, 300, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }

    /// <summary>
    /// A choice from a list. The list opens under the row (pushing the rows below down) rather than floating over them.
    /// </summary>
    public partial class FreeplayDropdown<T> : SettingRow
    {
        private const float max_list_height = 260;
        private const float item_height = 30;
        private const int search_threshold = 12;

        private SearchField? search;

        private readonly Bindable<T> current;
        private readonly IReadOnlyList<T> items;
        // As an object: a drawable unbinds every bindable field it has when disposed, which would cut a global setting off from the game and its config.
        private readonly object source;
        private readonly Func<T, string> describe;

        private readonly TruncatingSpriteText selected;
        private readonly SpriteIcon chevron;
        private readonly Container list;
        private readonly FillFlowContainer listItems;

        private bool open;

        public FreeplayDropdown(string caption, Bindable<T> current, IEnumerable<T> items, Func<T, string>? describe = null, string? hint = null)
            : base(caption, hint)
        {
            // Bindings between bindables are weak, so the source is held too, or it could be collected along with what it saves.
            source = current;
            this.current = current.GetBoundCopy();
            current = this.current;
            this.items = items.ToList();
            this.describe = describe ?? (v => v?.ToString() ?? string.Empty);

            Body.Add(FreeplayStyle.Label(caption));

            Body.Add(new ClickableContainer
            {
                RelativeSizeAxes = Axes.X,
                Height = 34,
                Masking = true,
                CornerRadius = FreeplayStyle.CORNER_RADIUS,
                BorderThickness = 2,
                BorderColour = FreeplayColours.DEEP_PINK,
                Action = toggle,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayStyle.PANEL },
                    selected = FreeplayStyle.TruncatingLabel(string.Empty, 16).With(t =>
                    {
                        t.Anchor = Anchor.CentreLeft;
                        t.Origin = Anchor.CentreLeft;
                        t.X = 10;
                        t.Width = 0.9f;
                    }),
                    chevron = new SpriteIcon
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.Centre,
                        X = -16,
                        Size = new Vector2(12),
                        Icon = FontAwesome.Solid.ChevronDown,
                        Colour = FreeplayColours.PINK,
                    },
                }
            });

            // Long lists (skins, collections) can be searched.
            if (this.items.Count > search_threshold)
            {
                Body.Add(search = new SearchField
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 32,
                    PlaceholderText = "поиск…",
                    Alpha = 0,
                });

                search.Current.BindValueChanged(_ => filter());
                search.OnCommit += (_, _) => listItems.OfType<Item>().FirstOrDefault(i => i.Alpha > 0)?.TriggerClick();
            }

            Body.Add(list = new Container
            {
                RelativeSizeAxes = Axes.X,
                Masking = true,
                CornerRadius = FreeplayStyle.CORNER_RADIUS,
                Alpha = 0,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayStyle.PANEL },
                    new OsuScrollContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Child = listItems = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                        },
                    },
                }
            });

            foreach (var item in this.items)
                listItems.Add(new Item(this.describe(item), () => choose(item), () => EqualityComparer<T>.Default.Equals(current.Value, item)));

            AddHint();

            current.BindValueChanged(v => selected.Text = this.describe(v.NewValue), true);
        }

        private void toggle()
        {
            open = !open;

            if (open)
            {
                foreach (var item in listItems.OfType<Item>())
                    item.UpdateSelection();
            }

            if (search != null)
            {
                search.FadeTo(open ? 1 : 0, 150);

                if (open)
                {
                    search.Text = string.Empty;
                    Schedule(() => GetContainingFocusManager()?.ChangeFocus(search));
                }
            }

            list.FadeTo(open ? 1 : 0, 150);
            resizeList();
            chevron.RotateTo(open ? 180 : 0, 200, Easing.OutQuint);
        }

        private void filter()
        {
            string term = search?.Text.Trim() ?? string.Empty;

            foreach (var item in listItems.OfType<Item>())
                item.Alpha = term.Length == 0 || item.Label.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            resizeList();
        }

        private void resizeList()
        {
            int shown = listItems.OfType<Item>().Count(i => i.Alpha > 0);
            float height = Math.Min(max_list_height, Math.Max(1, shown) * item_height);

            list.ResizeHeightTo(open ? height : 0, 200, Easing.OutQuint);
        }

        private void choose(T item)
        {
            current.Value = item;
            toggle();
        }

        private partial class Item : ClickableContainer
        {
            private readonly Func<bool> isSelected;
            private readonly Box background;
            private readonly TruncatingSpriteText text;

            public readonly string Label;

            public Item(string label, Action action, Func<bool> isSelected)
            {
                this.isSelected = isSelected;
                Label = label;

                RelativeSizeAxes = Axes.X;
                Height = item_height;
                Action = action;

                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = FreeplayStyle.PANEL_HOVER,
                        Alpha = 0,
                    },
                    text = FreeplayStyle.TruncatingLabel(label, 15).With(t =>
                    {
                        t.Anchor = Anchor.CentreLeft;
                        t.Origin = Anchor.CentreLeft;
                        t.X = 12;
                        t.Width = 0.95f;
                    }),
                };
            }

            public void UpdateSelection()
            {
                bool selected = isSelected();
                text.Colour = selected ? FreeplayColours.PINK : FreeplayColours.WHITE;
                text.Font = OsuFont.GetFont(size: 15, weight: selected ? FontWeight.Bold : FontWeight.Regular);
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeIn(100);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeOut(200);
                base.OnHoverLost(e);
            }
        }
    }

    /// <summary>
    /// The search field of a long dropdown.
    /// </summary>
    public partial class SearchField : OsuTextBox
    {
        public SearchField()
        {
            BackgroundUnfocused = FreeplayStyle.PANEL;
            BackgroundFocused = FreeplayStyle.PANEL_HOVER;
            BorderColour = FreeplayColours.PINK;
        }
    }

    /// <summary>
    /// A labelled text field.
    /// </summary>
    public partial class FreeplayTextBox : SettingRow
    {
        public FreeplayTextBox(string caption, Bindable<string> current, string placeholder, string? hint = null)
            : base(caption, hint)
        {
            Body.Add(FreeplayStyle.Label(caption));

            Body.Add(new Field
            {
                RelativeSizeAxes = Axes.X,
                Height = 34,
                PlaceholderText = placeholder,
                Current = current,
                CommitOnFocusLost = true,
            });

            AddHint();
        }

        private partial class Field : OsuTextBox
        {
            public Field()
            {
                BackgroundUnfocused = FreeplayStyle.PANEL;
                BackgroundFocused = FreeplayStyle.PANEL_HOVER;
                BorderColour = FreeplayColours.DEEP_PINK;
            }
        }
    }

    /// <summary>
    /// A flat button in a flag colour that bounces a little when pressed, like osu!stable's.
    /// </summary>
    public partial class FreeplayButton : ClickableContainer
    {
        private readonly Box flash;
        private readonly Container content;

        public FreeplayButton(string text, Action action, Color4 colour, float width = 320, float height = 44)
        {
            Size = new Vector2(width, height);
            Action = action;

            Child = content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                CornerRadius = FreeplayStyle.CORNER_RADIUS,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientVertical(colour.Lighten(0.15f), colour.Darken(0.15f)),
                    },
                    flash = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.White,
                        Alpha = 0,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = text,
                        Font = OsuFont.GetFont(Typeface.TorusAlternate, 19, FontWeight.Bold),
                        Shadow = true,
                    },
                }
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            flash.FadeTo(0.12f, 150);
            content.ScaleTo(1.03f, 200, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            flash.FadeOut(300);
            content.ScaleTo(1, 300, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            content.ScaleTo(0.95f, 400, Easing.OutQuint);
            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            content.ScaleTo(IsHovered ? 1.03f : 1, 500, Easing.OutElastic);
            base.OnMouseUp(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            flash.FadeTo(0.4f).FadeOut(400);
            return base.OnClick(e);
        }
    }
}
