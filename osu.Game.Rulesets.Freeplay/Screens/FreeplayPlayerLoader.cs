// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Freeplay.Screens.Controls;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// The loading screen before each track: freeplay's own card with the song, its BPM and the run's mode,
    /// in place of osu!'s song details. osu!'s side panel (background dim, offset) is kept.
    /// </summary>
    public partial class FreeplayPlayerLoader : PlayerLoader
    {
        private const float card_width = 760;
        private const float card_height = 210;

        private readonly double bpm;
        private readonly string detail;

        private Container card = null!;
        private OsuSpriteText status = null!;

        /// <param name="createPlayer">Creates the player.</param>
        /// <param name="bpm">The track's main BPM.</param>
        /// <param name="detail">The run's mode and star rating, e.g. "Аим · 5.0★".</param>
        public FreeplayPlayerLoader(Func<Player> createPlayer, double bpm, string detail)
            : base(createPlayer)
        {
            this.bpm = bpm;
            this.detail = detail;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var metadata = Beatmap.Value.Metadata;
            var info = Beatmap.Value.BeatmapInfo;

            string title = metadata.TitleUnicode.Length > 0 ? metadata.TitleUnicode : metadata.Title;
            string artist = metadata.ArtistUnicode.Length > 0 ? metadata.ArtistUnicode : metadata.Artist;

            AddInternal(new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 14),
                Children = new Drawable[]
                {
                    card = new Container
                    {
                        Size = new Vector2(card_width, card_height),
                        Masking = true,
                        CornerRadius = 10,
                        BorderThickness = 3,
                        BorderColour = FreeplayColours.TitleGradient,
                        Alpha = 0,
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = FreeplayColours.BACKGROUND },
                            new Sprite
                            {
                                RelativeSizeAxes = Axes.Both,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                FillMode = FillMode.Fill,
                                Texture = Beatmap.Value.GetBackground(),
                            },
                            // Darker on the left, where the text is.
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientHorizontal(Color4.Black.Opacity(0.85f), Color4.Black.Opacity(0.35f)),
                            },
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                Direction = FillDirection.Vertical,
                                Padding = new MarginPadding { Horizontal = 26, Top = 20 },
                                Spacing = new Vector2(0, 2),
                                Children = new Drawable[]
                                {
                                    line(title, 40, FontWeight.Bold, FreeplayColours.WHITE, Typeface.TorusAlternate),
                                    line(artist, 22, FontWeight.SemiBold, FreeplayColours.TEXT_DIM),
                                    line($"[{info.DifficultyName}] · {metadata.Author.Username}", 16, FontWeight.Regular, FreeplayColours.PINK),
                                }
                            },
                            new FillFlowContainer
                            {
                                Anchor = Anchor.BottomLeft,
                                Origin = Anchor.BottomLeft,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(10, 0),
                                Margin = new MarginPadding { Left = 26, Bottom = 20 },
                                Children = new Drawable[]
                                {
                                    chip(bpm > 0 ? $"{bpm:0} BPM" : "BPM ?", true),
                                    chip(detail, false),
                                    chip(FormatLength(info.Length), false),
                                }
                            },
                            new Box
                            {
                                Anchor = Anchor.BottomLeft,
                                Origin = Anchor.BottomLeft,
                                RelativeSizeAxes = Axes.X,
                                Height = 4,
                                Colour = FreeplayColours.TitleGradient,
                            },
                        }
                    },
                    status = new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Font = OsuFont.GetFont(size: 18, weight: FontWeight.SemiBold),
                        Colour = FreeplayColours.TEXT_DIM,
                        Text = "генерирую карту…",
                    },
                }
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // freeplay's card replaces osu!'s song details (which also carry a loading spinner; the status line under the card takes over).
            MetadataInfo.Colour = Color4.Transparent;

            // osu!'s side panel (dim, blur, offset) duplicates freeplay's own settings, so it's hidden. Hidden, it also can't hold the loader up by being hovered.
            PlayerSettings.Alpha = 0;

            card.ScaleTo(0.92f).FadeIn(400, Easing.OutQuint).ScaleTo(1, 600, Easing.OutQuint);
            status.Loop(t => t.FadeTo(0.4f, 600).Then().FadeTo(1, 600));
        }

        protected override void Update()
        {
            base.Update();

            if (CurrentPlayer?.LoadState >= LoadState.Ready && status.Text != "готово ♡")
            {
                status.ClearTransforms();
                status.FadeIn(200);
                status.Text = "готово ♡";
                status.Colour = FreeplayColours.PINK;
            }
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            base.OnSuspending(e);
            card.FadeOut(300, Easing.OutQuint).ScaleTo(1.05f, 300, Easing.OutQuint);
            status.FadeOut(200);
        }

        private static TruncatingSpriteText line(string text, float size, FontWeight weight, Color4 colour, Typeface typeface = Typeface.Torus) => new TruncatingSpriteText
        {
            RelativeSizeAxes = Axes.X,
            Text = text,
            Font = OsuFont.GetFont(typeface, size, weight),
            Colour = colour,
            Shadow = true,
        };

        private static Drawable chip(string text, bool highlight) => new Container
        {
            AutoSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = FreeplayStyle.CORNER_RADIUS,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = highlight ? (ColourInfo)ColourInfo.GradientHorizontal(FreeplayColours.DEEP_BLUE, FreeplayColours.DEEP_PINK) : Color4.Black.Opacity(0.55f),
                },
                new OsuSpriteText
                {
                    Margin = new MarginPadding { Horizontal = 12, Vertical = 5 },
                    Text = text,
                    Font = OsuFont.GetFont(Typeface.TorusAlternate, highlight ? 24 : 18, FontWeight.Bold),
                    Colour = FreeplayColours.WHITE,
                    Shadow = true,
                },
            }
        };

        public static string FormatLength(double milliseconds)
        {
            var time = TimeSpan.FromMilliseconds(milliseconds);
            return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time:mm\\:ss}" : $"{time:m\\:ss}";
        }
    }
}
