using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FullScreenMode = UnityEngine.FullScreenMode;

namespace Satisfying.Editor
{
    /// <summary>
    /// Makes a fresh clone playable without anyone clicking through the editor: it creates the boot
    /// scene, registers it for builds, and repairs the custom layers if the project settings were lost.
    /// Runs once on load and is also available from the menu.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Duel.unity";

        static readonly string[] RequiredLayers =
        {
            null, null, null, null, null, null, null, null,   // 0-7 are Unity's own
            "World", "Player", "PlayerProbe", "Viewmodel", "Fx"
        };

        static ProjectSetup()
        {
            // Deferred: the asset database is not ready during the static constructor.
            EditorApplication.delayCall += RunIfNeeded;
        }

        static void RunIfNeeded()
        {
            bool needsScene = !File.Exists(ScenePath);
            bool needsLayers = !LayersLookRight();
            if (!needsScene && !needsLayers) return;
            Run();
        }

        [MenuItem("Satisfying/Set up project", priority = 0)]
        public static void Run()
        {
            EnsureLayers();
            EnsureScene();
            EnsureBuildSettings();
            EnsureAlwaysIncludedShaders();

            // Without this the unfocused instance stalls, and you cannot playtest a duel on one machine.
            PlayerSettings.runInBackground = true;
            PlayerSettings.productName = "Satisfying";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;

            AssetDatabase.SaveAssets();
            Debug.Log("[satisfying] project set up: boot scene, layers and build settings are ready. Press play.");
        }

        // ------------------------------------------------------------------ layers
        static bool LayersLookRight()
        {
            for (int i = 0; i < RequiredLayers.Length; i++)
            {
                if (RequiredLayers[i] == null) continue;
                if (LayerMask.LayerToName(i) != RequiredLayers[i]) return false;
            }
            return true;
        }

        static void EnsureLayers()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("[satisfying] could not open TagManager.asset - add the layers by hand: " +
                                 "8 World, 9 Player, 10 PlayerProbe, 11 Viewmodel, 12 Fx");
                return;
            }

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null || !layers.isArray) return;

            for (int i = 0; i < RequiredLayers.Length && i < layers.arraySize; i++)
            {
                if (RequiredLayers[i] == null) continue;
                SerializedProperty entry = layers.GetArrayElementAtIndex(i);
                if (entry.stringValue == RequiredLayers[i]) continue;
                entry.stringValue = RequiredLayers[i];
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ scene
        static void EnsureScene()
        {
            if (File.Exists(ScenePath)) return;

            // Never replace a scene somebody is in the middle of editing.
            if (SceneManager.GetActiveScene().isDirty)
            {
                Debug.Log("[satisfying] the open scene has unsaved changes, so the boot scene was not created. " +
                          "Save it, then run Satisfying > Set up project.");
                return;
            }

            // Create the folder through the asset database, not System.IO, so Unity knows about it
            // before a scene is saved into it.
            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // The game builds itself at runtime, so the scene only needs the boot component.
            GameObject boot = new GameObject("Satisfying");
            boot.AddComponent<Game.GameBootstrap>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
        }

        static void EnsureBuildSettings()
        {
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            for (int i = 0; i < existing.Length; i++)
                if (existing[i].path == ScenePath) return;

            EditorBuildSettingsScene[] updated = new EditorBuildSettingsScene[existing.Length + 1];
            updated[0] = new EditorBuildSettingsScene(ScenePath, true);
            for (int i = 0; i < existing.Length; i++) updated[i + 1] = existing[i];
            EditorBuildSettings.scenes = updated;
        }

        /// <summary>
        /// Materials are created in code with Shader.Find, so the shader has to be forced into the build
        /// or every surface turns magenta in a player.
        /// </summary>
        static void EnsureAlwaysIncludedShaders()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets == null || assets.Length == 0) return;

            SerializedObject graphics = new SerializedObject(assets[0]);
            SerializedProperty included = graphics.FindProperty("m_AlwaysIncludedShaders");
            if (included == null || !included.isArray) return;

            string[] wanted = { "Standard", "Legacy Shaders/Diffuse" };
            for (int w = 0; w < wanted.Length; w++)
            {
                Shader shader = Shader.Find(wanted[w]);
                if (shader == null) continue;

                bool present = false;
                for (int i = 0; i < included.arraySize; i++)
                {
                    if (included.GetArrayElementAtIndex(i).objectReferenceValue != shader) continue;
                    present = true;
                    break;
                }
                if (present) continue;

                included.InsertArrayElementAtIndex(included.arraySize);
                included.GetArrayElementAtIndex(included.arraySize - 1).objectReferenceValue = shader;
            }
            graphics.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Satisfying/Open boot scene", priority = 1)]
        public static void OpenScene()
        {
            Run();
            EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
