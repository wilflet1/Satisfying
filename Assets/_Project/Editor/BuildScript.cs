using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Satisfying.Editor
{
    /// <summary>
    /// Headless-friendly builds plus the two-instance playtest loop, which is the only way to feel
    /// netcode: build a player once, then run it against the editor and duel yourself.
    /// </summary>
    public static class BuildScript
    {
        const string BuildRoot = "Builds";

        [MenuItem("Satisfying/Build/Windows 64", priority = 20)]
        public static void BuildWindows() { Build(BuildTarget.StandaloneWindows64, "Windows/Satisfying.exe", false); }

        [MenuItem("Satisfying/Build/macOS", priority = 21)]
        public static void BuildMac() { Build(BuildTarget.StandaloneOSX, "macOS/Satisfying.app", false); }

        [MenuItem("Satisfying/Build/Linux 64", priority = 22)]
        public static void BuildLinux() { Build(BuildTarget.StandaloneLinux64, "Linux/Satisfying", false); }

        [MenuItem("Satisfying/Build/Linux dedicated server", priority = 23)]
        public static void BuildLinuxServer()
        {
            Build(BuildTarget.StandaloneLinux64, "LinuxServer/SatisfyingServer", false, StandaloneBuildSubtarget.Server);
        }

        [MenuItem("Satisfying/Playtest/Build a duel client", priority = 40)]
        public static string BuildPlaytestClient()
        {
            BuildTarget target = CurrentStandaloneTarget();
            string path = target == BuildTarget.StandaloneWindows64
                ? "Playtest/Satisfying.exe"
                : (target == BuildTarget.StandaloneOSX ? "Playtest/Satisfying.app" : "Playtest/Satisfying");
            return Build(target, path, true);
        }

        [MenuItem("Satisfying/Playtest/Launch a second player (joins 127.0.0.1)", priority = 41)]
        public static void LaunchSecondPlayer()
        {
            string path = BuildPlaytestClient();
            if (string.IsNullOrEmpty(path)) return;

            string executable = path;
            if (path.EndsWith(".app")) executable = Path.Combine(path, "Contents/MacOS/Satisfying");

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.GetFullPath(executable),
                    Arguments = "-connect 127.0.0.1 -name challenger -screen-fullscreen 0 -screen-width 1280 -screen-height 720",
                    UseShellExecute = true
                });
                Debug.Log("[satisfying] second player launched - host a duel in the editor and it will join you.");
            }
            catch (Exception e)
            {
                Debug.LogError("[satisfying] could not launch the playtest client: " + e.Message);
            }
        }

        static BuildTarget CurrentStandaloneTarget()
        {
#if UNITY_EDITOR_WIN
            return BuildTarget.StandaloneWindows64;
#elif UNITY_EDITOR_OSX
            return BuildTarget.StandaloneOSX;
#else
            return BuildTarget.StandaloneLinux64;
#endif
        }

        static string Build(BuildTarget target, string relativePath, bool development,
                            StandaloneBuildSubtarget subtarget = StandaloneBuildSubtarget.Player)
        {
            ProjectSetup.Run();

            string path = Path.Combine(BuildRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

            BuildPlayerOptions options = new BuildPlayerOptions();
            options.scenes = new[] { ProjectSetup.ScenePath };
            options.locationPathName = path;
            options.target = target;
            options.subtarget = (int)subtarget;      // Server strips the renderer and the audio stack
            options.options = development ? BuildOptions.Development : BuildOptions.None;

            var report = BuildPipeline.BuildPlayer(options);
            if (report != null && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.LogError("[satisfying] build failed: " + report.summary.result);
                return null;
            }

            Debug.Log("[satisfying] built " + target + " -> " + Path.GetFullPath(path));
            return path;
        }

        /// <summary>
        /// Entry point for batch mode:
        /// Unity -batchmode -nographics -projectPath . -executeMethod Satisfying.Editor.BuildScript.BuildFromCommandLine -quit
        /// </summary>
        public static void BuildFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            BuildTarget target = CurrentStandaloneTarget();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-target") continue;
                switch (args[i + 1].ToLowerInvariant())
                {
                    case "linuxserver": BuildLinuxServer(); return;
                    case "windows": target = BuildTarget.StandaloneWindows64; break;
                    case "mac": target = BuildTarget.StandaloneOSX; break;
                    case "linux": target = BuildTarget.StandaloneLinux64; break;
                }
            }

            string folder = target == BuildTarget.StandaloneWindows64
                ? "Windows/Satisfying.exe"
                : (target == BuildTarget.StandaloneOSX ? "macOS/Satisfying.app" : "Linux/Satisfying");
            Build(target, folder, false);
        }
    }
}
