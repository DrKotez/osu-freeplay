// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using osu.Framework;
using osu.Framework.Development;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game;
using SDL;

namespace osu.Desktop
{
    public static class Program
    {
        /// <summary>
        /// A file which, next to the executable, makes freeplay~ store its data in its own folder.
        /// </summary>
        public const string PORTABLE_MARKER = @"portable.ini";

        // freeplay~ keeps its own data, separate from any osu!lazer install.
#if DEBUG
        private const string base_game_name = @"freeplay-development";
#else
        private const string base_game_name = @"freeplay";
#endif

        [STAThread]
        public static void Main(string[] args)
        {
            if (OperatingSystem.IsWindows())
            {
                var windowsVersion = Environment.OSVersion.Version;

                // We are limited by realm, which only supports Windows 8.1 and higher.
                if (windowsVersion.Major < 6 || (windowsVersion.Major == 6 && windowsVersion.Minor <= 2))
                {
                    unsafe
                    {
                        SDL3.SDL_ShowSimpleMessageBox(SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR,
                            "Your operating system is too old to run freeplay~"u8,
                            "freeplay~ requires at least Windows 8.1 to run."u8, null);
                        return;
                    }
                }
            }

            // This is a safe default. Localised usages should specify lower values as required.
            AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromMilliseconds(1000));

            string gameName = base_game_name;

            foreach (string arg in args)
            {
                string[] split = arg.Split('=');

                if (split[0] != "--debug-client-id")
                    continue;

                if (!DebugUtils.IsDebugBuild)
                    throw new InvalidOperationException("Cannot use this argument in a non-debug build.");

                if (split.Length < 2 || !int.TryParse(split[1], out int clientID))
                    throw new ArgumentException("Provided client ID must be an integer.");

                gameName = $"{base_game_name}-{clientID}";
            }

            var hostOptions = new HostOptions
            {
                IPCPipeName = OsuGame.IPC_PIPE_NAME,
                FriendlyGameName = OsuGameBase.GAME_NAME,
                // A portable copy (with portable.ini beside the executable) keeps everything in its own folder, so it can be zipped and passed on.
                PortableInstallation = File.Exists(Path.Combine(AppContext.BaseDirectory, PORTABLE_MARKER)),
            };

            using (DesktopGameHost host = Host.GetSuitableDesktopHost(gameName, hostOptions))
            {
                // Multiple instances are allowed in debug, e.g. for testing multiplayer locally.
                if (!host.IsPrimaryInstance && !DebugUtils.IsDebugBuild)
                {
                    Logger.Log(@"freeplay~ is already running.", LoggingTarget.Runtime, LogLevel.Error);
                    return;
                }

                host.Run(new OsuGameDesktop(args)
                {
                    EnableWebSocketServer = Environment.GetEnvironmentVariable("OSU_WEBSOCKET_SERVER") == "1",
                });
            }
        }
    }
}
