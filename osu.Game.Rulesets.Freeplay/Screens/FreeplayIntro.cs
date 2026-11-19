// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Video;
using osu.Framework.Input.Events;
using osu.Framework.IO.Stores;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// freeplay~'s intro media, embedded from <c>Resources/Intro</c> when present (it isn't part of the repository).
    /// </summary>
    public static class FreeplayMedia
    {
        public const string INTRO_VIDEO = "intro.mp4";
        public const string MENU_MUSIC = "menu-loop.mp3";

        public static IResourceStore<byte[]> CreateStore() => new NamespacedResourceStore<byte[]>(new DllResourceStore(typeof(FreeplayRuleset).Assembly), "Resources/Intro");
    }

    /// <summary>
    /// Loops the menu music while freeplay's menus are open.
    /// </summary>
    public partial class FreeplayMenuMusic : Component
    {
        private const double fade_duration = 600;

        private Track? track;

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            track = audio.GetTrackStore(FreeplayMedia.CreateStore()).Get(FreeplayMedia.MENU_MUSIC);

            if (track != null)
                track.Looping = true;
        }

        public void Play()
        {
            if (track == null)
                return;

            if (!track.IsRunning)
            {
                track.Volume.Value = 0;
                track.Start();
            }

            this.TransformBindableTo(track.Volume, 1, fade_duration);
        }

        /// <summary>
        /// The current loudness (0-1), for pulsing visuals to the music.
        /// </summary>
        public float Amplitude => track?.CurrentAmplitudes.Average ?? 0;

        public string? Title => track == null ? null : "jfpLoop";

        public void Stop()
        {
            if (track == null)
                return;

            // Stop right away: this lives on the menu screen, which is hidden once gameplay starts,
            // and hidden drawables don't update, so a fade-out would never finish.
            ClearTransforms();
            track.Stop();
        }

        protected override void Dispose(bool isDisposing)
        {
            track?.Dispose();
            base.Dispose(isDisposing);
        }
    }

    /// <summary>
    /// Plays the intro video over the whole screen once per launch. Clicking or pressing a key skips it.
    /// </summary>
    public partial class FreeplayIntro : CompositeDrawable
    {
        /// <summary>
        /// The intro plays once per launch, not every time the menu is shown.
        /// </summary>
        private static bool played;

        public static bool ShouldPlay => !played;

        /// <summary>
        /// Input can't skip the intro this early, so a click left over from launching doesn't swallow it.
        /// </summary>
        private const double min_play_time = 1500;

        /// <summary>
        /// The intro gives up after this long, in case the video never reports its end.
        /// </summary>
        private const double max_play_time = 20000;

        private Video? video;
        private bool finished;
        private double startTime = double.NaN;

        private double elapsed => double.IsNaN(startTime) ? 0 : Time.Current - startTime;

        public FreeplayIntro()
        {
            RelativeSizeAxes = Axes.Both;
            AlwaysPresent = true;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            played = true;

            // Not in tests: the video is the player's own (not in the repository), and its decoder outliving a test's game
            // held up the next game's loading, failing whichever test came after.
            var stream = osu.Framework.Development.DebugUtils.IsNUnitRunning ? null : FreeplayMedia.CreateStore().GetStream(FreeplayMedia.INTRO_VIDEO);

            if (stream == null)
            {
                finished = true;
                Expire();
                return;
            }

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black },
                video = new Video(stream, startAtCurrentTime: true)
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    FillMode = FillMode.Fill,
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            if (video == null || finished)
                return;

            if (double.IsNaN(startTime))
                startTime = Time.Current;

            // The video is timed from when it's first shown (not from game start, which made it end instantly), and plays to its end.
            bool ended = video.State == VideoDecoder.DecoderState.EndOfStream && video.PlaybackPosition >= video.Duration - 50;

            if (ended || video.State == VideoDecoder.DecoderState.Faulted || elapsed > max_play_time)
                finish();
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (elapsed >= min_play_time)
                finish();
            return true;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (elapsed >= min_play_time)
                finish();
            return true;
        }

        public event Action? Finished;

        private void finish()
        {
            if (finished)
                return;

            finished = true;
            Finished?.Invoke();

            this.FadeOut(300).Expire();
        }
    }
}
