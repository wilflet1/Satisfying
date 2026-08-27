using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
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
        const string StampPath = "Assets/_Project/Scripts/Unity/Core/BuildStamp.cs";

        [MenuItem("Satisfying/Build/Windows 64", priority = 20)]
        public static void BuildWindows() { Build(BuildTarget.StandaloneWindows64, "Windows/Satisfying.exe", false); }

        [MenuItem("Satisfying/Build/macOS", priority = 21)]
        public static void BuildMac() { Build(BuildTarget.StandaloneOSX, "macOS/Satisfying.app", false); }

        [MenuItem("Satisfying/Build/Linux 64", priority = 22)]
        public static void BuildLinux() { Build(BuildTarget.StandaloneLinux64, "Linux/Satisfying", false); }

        [MenuItem("Satisfying/Build/Android APK", priority = 24)]
        public static void BuildAndroid()
        {
            ConfigureAndroid();
            Build(BuildTarget.Android, "Android/Satisfying.apk", false);
        }

        [MenuItem("Satisfying/Build/Android APK (development)", priority = 25)]
        public static void BuildAndroidDevelopment()
        {
            ConfigureAndroid();
            Build(BuildTarget.Android, "Android/Satisfying-dev.apk", true);
        }

        /// <summary>
        /// The handful of player settings an installable APK actually needs. Set here rather than
        /// left to whoever last opened the inspector, so the build is the same on any machine.
        /// </summary>
        static void ConfigureAndroid()
        {
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.satisfying.duel");

            // ARM64 with IL2CPP: Play requires 64 bit, and it is what every phone since about 2017
            // actually runs. Mono would be a smaller build that no store would take.
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // The netcode is raw UDP, so the INTERNET permission is not optional. Unity usually infers
            // it, but "usually" is how you ship a build that cannot open a socket.
            PlayerSettings.Android.forceInternetPermission = true;

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // A duel is landscape. Portrait would put both thumbs over the middle of the screen.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // An APK to sideload, not a bundle to upload.
            EditorUserBuildSettings.buildAppBundle = false;
        }

        /// <summary>
        /// Says what machine a built binary is for, by reading the two bytes in the ELF header that
        /// answer it.
        ///
        /// Worth printing because the failure it prevents is slow and remote: a server binary for the
        /// wrong architecture uploads perfectly happily and then says "cannot execute binary file" on
        /// a machine you are talking to over SSH. It also settles the question this project cannot
        /// otherwise answer from a Windows editor - Unity ships linuxarm64 server variations, but
        /// PlayerSettings.SetArchitecture accepts any index and quietly changes nothing, so what comes
        /// out of here is x86-64 whatever you ask for. If you want the ARM build, set Target
        /// Architecture in Build Settings by hand and check this line says AArch64.
        /// </summary>
        static void ReportElfMachine(string soPath)
        {
            try
            {
                if (!File.Exists(soPath)) { Debug.LogWarning("[satisfying] no UnityPlayer.so to check"); return; }

                byte[] header = new byte[20];
                using (FileStream stream = File.OpenRead(soPath)) stream.Read(header, 0, header.Length);

                if (header[0] != 0x7F || header[1] != (byte)'E' || header[2] != (byte)'L' || header[3] != (byte)'F')
                {
                    Debug.LogWarning("[satisfying] " + soPath + " is not an ELF binary");
                    return;
                }

                int machine = header[18] | (header[19] << 8);
                string name = machine == 0x3E ? "x86-64" : machine == 0xB7 ? "AArch64 (ARM64)" : "unknown";
                Debug.Log("[satisfying] server binary is for " + name + " (ELF machine 0x"
                          + machine.ToString("X2") + ")");
            }
            catch (Exception e) { Debug.LogWarning("[satisfying] could not read the ELF header: " + e.Message); }
        }

        [MenuItem("Satisfying/Build/Linux dedicated server", priority = 23)]
        public static void BuildLinuxServer()
        {
            string built = Build(BuildTarget.StandaloneLinux64, "LinuxServer/SatisfyingServer", false,
                                 StandaloneBuildSubtarget.Server);
            if (built != null) ReportElfMachine(Path.Combine(Path.GetDirectoryName(built), "UnityPlayer.so"));
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

        [MenuItem("Satisfying/Build/Refresh the build stamp", priority = 30)]
        public static void WriteBuildStamp()
        {
            string commit = Git("rev-parse --short HEAD");
            string branch = Git("rev-parse --abbrev-ref HEAD");
            if (string.IsNullOrEmpty(commit)) commit = "no git";
            // Not counting the stamp itself, which this method is about to rewrite: every build
            // leaves it modified, so counting it would mean the second build in a row - and every
            // build after that - claimed to be from a dirty tree whether or not anything had changed.
            if (!string.IsNullOrEmpty(Git("status --porcelain -- . \":(exclude)" + StampPath + "\""))) commit += "+dirty";

            string source =
                "namespace Satisfying.Game\n" +
                "{\n" +
                "    // GENERATED by BuildScript.WriteBuildStamp immediately before each build.\n" +
                "    // Editing this by hand is pointless: the next build overwrites it.\n" +
                "    public static class BuildStamp\n" +
                "    {\n" +
                "        public const string Commit = \"" + Escape(commit) + "\";\n" +
                "        public const string Built = \"" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC\";\n" +
                "        public const string Branch = \"" + Escape(branch) + "\";\n" +
                "\n" +
                "        public static string Describe(int protocolVersion)\n" +
                "        {\n" +
                "            string where = string.IsNullOrEmpty(Branch) ? Commit : Commit + \" (\" + Branch + \")\";\n" +
                "            return \"build \" + where + \"   \" + Built + \"   protocol v\" + protocolVersion;\n" +
                "        }\n" +
                "    }\n" +
                "}\n";

            string path = StampPath;
            string existing = File.Exists(path) ? File.ReadAllText(path) : null;
            if (existing == source) return;          // no churn when nothing moved

            File.WriteAllText(path, source);
            AssetDatabase.ImportAsset(path);
            Debug.Log("[satisfying] build stamp: " + commit + " on " + branch);
        }

        static string Escape(string text)
        {
            return text == null ? "" : text.Replace("\\", "/").Replace("\"", "'").Trim();
        }

        /// <summary>Asks git, quietly. No repository is a perfectly normal state for a zip download.</summary>
        static string Git(string arguments)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("git", arguments);
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WorkingDirectory = Directory.GetCurrentDirectory();

                using (Process process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(4000);
                    return output.Trim();
                }
            }
            catch (Exception)
            {
                return null;
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
            WriteBuildStamp();

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
                    case "android": BuildAndroid(); return;
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
