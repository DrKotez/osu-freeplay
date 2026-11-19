// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using NUnit.Framework;

namespace osu.Game.Rulesets.Freeplay.Tests
{
    /// <summary>
    /// The game tests start a whole game each, and the work a previous one left running (scans, pattern mining, loading) shares the
    /// thread pool with the next game's loading. Near the pool's minimum, new threads are only added slowly, so a game could wait
    /// past the framework's ten seconds to reach its intro, failing whichever test came next. More threads up front avoids the wait.
    /// </summary>
    [SetUpFixture]
    public class TestThreadPoolSetup
    {
        [OneTimeSetUp]
        public void RaiseMinimumThreads()
        {
            ThreadPool.GetMinThreads(out int workers, out int completion);
            ThreadPool.SetMinThreads(Math.Max(workers, 64), completion);
        }
    }
}
