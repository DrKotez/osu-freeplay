// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps.Timing;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Freeplay.Screens
{
    /// <summary>
    /// Skips the rest of a break (a stretch the map marks as a break, with no notes), as osu!stable does: the skip button
    /// in the corner, or the skip key (Space). Gameplay resumes a little before the next note appears.
    /// </summary>
    public partial class FreeplayBreakSkip : CompositeDrawable, IKeyBindingHandler<GlobalAction>
    {
        /// <summary>
        /// How long before the next note starts to appear the skip lands.
        /// </summary>
        public const double LEAD_TIME = 1000;

        private readonly IReadOnlyList<BreakPeriod> breaks;
        private readonly IReadOnlyList<OsuHitObject> objects;
        private readonly Func<double> currentTime;

        /// <summary>
        /// Whether gameplay is paused: no skipping then (FIX: a jump while paused froze gameplay on resuming, as the frame-stable seek
        /// it needs only lasts one frame, which a paused game doesn't process).
        /// </summary>
        public Func<bool> IsPaused { get; init; } = () => false;
        private readonly Action<double> seek;
        private readonly FreeplaySkipOverlay.SkipButton button;

        /// <summary>
        /// How much break time was skipped so far.
        /// </summary>
        public double SkippedMs { get; private set; }

        /// <param name="breaks">The map's breaks.</param>
        /// <param name="objects">The map's objects, in order.</param>
        /// <param name="currentTime">The gameplay time.</param>
        /// <param name="seek">Moves gameplay to a time.</param>
        public FreeplayBreakSkip(IReadOnlyList<BreakPeriod> breaks, IReadOnlyList<OsuHitObject> objects, Func<double> currentTime, Action<double> seek)
        {
            this.breaks = breaks;
            this.objects = objects;
            this.currentTime = currentTime;
            this.seek = seek;

            RelativeSizeAxes = Axes.Both;
            AlwaysPresent = true;

            InternalChild = button = new FreeplaySkipOverlay.SkipButton
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Action = Skip,
            };
        }

        /// <summary>
        /// Where a skip now would land, or null outside a break (or too near its end to be worth it).
        /// </summary>
        public double? Target
        {
            get
            {
                double now = currentTime();

                if (IsPaused())
                    return null;

                if (!breaks.Any(b => now >= b.StartTime && now < b.EndTime))
                    return null;

                var next = objects.FirstOrDefault(h => h.StartTime > now);

                if (next == null)
                    return null;

                double target = next.StartTime - next.TimePreempt - LEAD_TIME;
                return target - now > 500 ? target : null;
            }
        }

        protected override void Update()
        {
            base.Update();
            button.Enabled.Value = Target != null;
        }

        public void Skip()
        {
            if (Target is not double target)
                return;

            SkippedMs += target - currentTime();
            seek(target);
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Repeat || e.Action != GlobalAction.SkipCutscene || Target == null)
                return false;

            Skip();
            return true;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }
    }
}
