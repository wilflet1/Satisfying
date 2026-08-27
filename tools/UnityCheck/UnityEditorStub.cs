// -----------------------------------------------------------------------------------------------
// COMPILE-CHECK STUB - see UnityEngineStub.cs. Covers the UnityEditor surface the project's editor
// scripts use, so the one-click setup and build menu are type-checked too.
//
// A WRONG STUB IS WORSE THAN A MISSING ONE. A missing member is an error here and gets noticed; a
// member stubbed with the wrong type passes here and fails in Unity, which is the one thing this
// file exists to prevent. PlayerSettings.defaultInterfaceOrientation was typed ScreenOrientation
// when it is really UIOrientation - two similarly named enums in different namespaces - and the
// build broke on a machine with a real editor. When adding to this file, copy the signature from
// the actual API rather than from memory of it.
// -----------------------------------------------------------------------------------------------
using System;
using UnityEngine;

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name { get; set; }
        public string path { get; set; }
        public bool isDirty { get; set; }
        public bool IsValid() { return true; }
    }

    public static class SceneManager
    {
        public static Scene GetActiveScene() { return new Scene(); }
    }
}

namespace UnityEngine
{
    public enum FullScreenMode { ExclusiveFullScreen = 0, FullScreenWindow = 1, MaximizedWindow = 2, Windowed = 3 }

    public static class LayerMask
    {
        public static string LayerToName(int layer) { return ""; }
        public static int NameToLayer(string name) { return 0; }
        public static int GetMask(params string[] layerNames) { return 0; }
    }
}

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InitializeOnLoadAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class InitializeOnLoadMethodAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuItemAttribute : Attribute
    {
        public MenuItemAttribute(string itemName) { }
        public MenuItemAttribute(string itemName, bool validate) { }
        public MenuItemAttribute(string itemName, bool validate, int priority) { }
        public int priority;
    }

    public static class EditorApplication
    {
        public delegate void CallbackFunction();
        public static CallbackFunction delayCall;
        public static bool isPlaying { get; set; }
        public static bool isPlayingOrWillChangePlaymode { get { return false; } }
    }

    public static class AssetDatabase
    {
        public static UnityEngine.Object[] LoadAllAssetsAtPath(string path) { return new UnityEngine.Object[1] { new UnityEngine.Object() }; }
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object { return null; }
        public static void SaveAssets() { }
        public static void Refresh() { }
        public static void ImportAsset(string path) { }
        public static void CreateAsset(UnityEngine.Object asset, string path) { }
        public static bool IsValidFolder(string path) { return true; }
        public static string CreateFolder(string parent, string name) { return ""; }
    }

    public class SerializedProperty
    {
        public bool isArray { get { return true; } }
        public int arraySize { get; set; }
        public string stringValue { get; set; }
        public float floatValue { get; set; }
        public int intValue { get; set; }
        public bool boolValue { get; set; }
        public UnityEngine.Object objectReferenceValue { get; set; }
        public SerializedProperty GetArrayElementAtIndex(int index) { return new SerializedProperty(); }
        public void InsertArrayElementAtIndex(int index) { }
        public void DeleteArrayElementAtIndex(int index) { }
    }

    public class SerializedObject
    {
        public SerializedObject(UnityEngine.Object target) { }
        public SerializedProperty FindProperty(string path) { return new SerializedProperty(); }
        public bool ApplyModifiedProperties() { return true; }
        public void ApplyModifiedPropertiesWithoutUndo() { }
        public void Update() { }
    }

    public class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene(string path, bool enabled) { this.path = path; this.enabled = enabled; }
        public string path { get; set; }
        public bool enabled { get; set; }
    }

    public static class EditorBuildSettings
    {
        public static EditorBuildSettingsScene[] scenes { get; set; }
    }

    public static class PlayerSettings
    {
        public static bool runInBackground { get; set; }
        public static string productName { get; set; }
        public static string companyName { get; set; }
        public static int defaultScreenWidth { get; set; }
        public static int defaultScreenHeight { get; set; }
        public static UnityEngine.FullScreenMode fullScreenMode { get; set; }
        public static UIOrientation defaultInterfaceOrientation { get; set; }
        public static bool allowedAutorotateToPortrait { get; set; }
        public static bool allowedAutorotateToPortraitUpsideDown { get; set; }
        public static bool allowedAutorotateToLandscapeLeft { get; set; }
        public static bool allowedAutorotateToLandscapeRight { get; set; }
        public static string applicationIdentifier { get; set; }
        public static void SetApplicationIdentifier(BuildTargetGroup group, string identifier) { }
        public static void SetScriptingBackend(BuildTargetGroup group, ScriptingImplementation backend) { }
        public static ScriptingImplementation GetScriptingBackend(BuildTargetGroup group) { return ScriptingImplementation.IL2CPP; }
        public static void SetArchitecture(BuildTargetGroup group, int architecture) { }
        public static void SetArchitecture(NamedBuildTarget target, int architecture) { }
        public static int GetArchitecture(NamedBuildTarget target) { return 0; }

        public static class Android
        {
            public static AndroidSdkVersions minSdkVersion { get; set; }
            public static AndroidSdkVersions targetSdkVersion { get; set; }
            public static int bundleVersionCode { get; set; }
            public static AndroidArchitecture targetArchitectures { get; set; }
            public static bool forceInternetPermission { get; set; }
            public static bool useCustomKeystore { get; set; }
        }
    }

    public static class EditorUserBuildSettings
    {
        public static BuildTarget activeBuildTarget { get; set; }
        public static StandaloneBuildSubtarget standaloneBuildSubtarget { get; set; }
        public static bool buildAppBundle { get; set; }
        public static AndroidBuildType androidBuildType { get; set; }
        public static bool SwitchActiveBuildTarget(BuildTargetGroup group, BuildTarget target) { return true; }
    }

    public enum AndroidBuildType { Debug = 0, Development = 1, Release = 2 }

    /// <summary>
    /// UnityEditor's own orientation enum. Deliberately NOT UnityEngine.ScreenOrientation - they are
    /// different types with overlapping names, and conflating them is what this stub got wrong.
    /// </summary>
    public enum UIOrientation
    {
        Portrait = 0,
        PortraitUpsideDown = 1,
        LandscapeRight = 2,
        LandscapeLeft = 3,
        AutoRotation = 4
    }

    public enum ScriptingImplementation { Mono2x = 0, IL2CPP = 1 }
    public enum AndroidSdkVersions { AndroidApiLevel23 = 23, AndroidApiLevel24 = 24, AndroidApiLevel29 = 29, AndroidApiLevel33 = 33, AndroidApiLevelAuto = 0 }

    [Flags]
    public enum AndroidArchitecture { None = 0, ARMv7 = 1, ARM64 = 2, All = 3 }

    public enum BuildTargetGroup { Unknown = 0, Standalone = 1, Android = 13, iOS = 4 }

    public enum BuildTarget { StandaloneWindows64 = 19, StandaloneOSX = 2, StandaloneLinux64 = 24, Android = 13, iOS = 9 }

    [Flags]
    public enum BuildOptions { None = 0, Development = 1, AutoRunPlayer = 4 }

    public enum StandaloneBuildSubtarget { Default = 0, Server = 1, Player = 2 }

    /// <summary>
    /// UnityEditor.Build.NamedBuildTarget. Only the members the build script names; the real one is a
    /// struct with static readonly fields, and Server is the one a dedicated server build settles on.
    /// </summary>
    public struct NamedBuildTarget
    {
        public static readonly NamedBuildTarget Standalone = new NamedBuildTarget();
        public static readonly NamedBuildTarget Server = new NamedBuildTarget();
    }

    public struct BuildPlayerOptions
    {
        public string[] scenes;
        public string locationPathName;
        public BuildTarget target;
        public int subtarget;
        public BuildOptions options;
    }

    public static class BuildPipeline
    {
        public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options) { return new Build.Reporting.BuildReport(); }
    }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown, Succeeded, Failed, Cancelled }

    public struct BuildSummary
    {
        public BuildResult result;
        public ulong totalSize;
    }

    public class BuildReport
    {
        public BuildSummary summary { get; set; }
    }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene, DefaultGameObjects }
    public enum NewSceneMode { Single, Additive }

    public static class EditorSceneManager
    {
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup, NewSceneMode mode) { return new UnityEngine.SceneManagement.Scene(); }
        public static bool MarkSceneDirty(UnityEngine.SceneManagement.Scene scene) { return true; }
        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene, string path) { return true; }
        public static UnityEngine.SceneManagement.Scene OpenScene(string path) { return new UnityEngine.SceneManagement.Scene(); }
    }
}
