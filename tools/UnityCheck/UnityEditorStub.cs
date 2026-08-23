// -----------------------------------------------------------------------------------------------
// COMPILE-CHECK STUB - see UnityEngineStub.cs. Covers the UnityEditor surface the project's editor
// scripts use, so the one-click setup and build menu are type-checked too.
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
    }

    public enum BuildTarget { StandaloneWindows64 = 19, StandaloneOSX = 2, StandaloneLinux64 = 24 }

    [Flags]
    public enum BuildOptions { None = 0, Development = 1, AutoRunPlayer = 4 }

    public enum StandaloneBuildSubtarget { Default = 0, Server = 1, Player = 2 }

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
