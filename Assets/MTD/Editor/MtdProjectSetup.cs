using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace MTD.Editor
{
    // Builds the parts of the project that are assets, not code, so they can be recreated exactly instead of being
    // put together by hand: the MTD scene, the build settings and the player settings.
    public static class MtdProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/MTD.unity";
        const string tmpEssentialsPackage = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";

        [MenuItem("MTD/Setup/1. Import TMP Essential Resources")]
        public static void ImportTmpEssentials()
        {
            UnityEditor.AssetPackage.Package.Import(tmpEssentialsPackage, false);
        }

        [MenuItem("MTD/Setup/2. Build Configuration Menu Prefab")]
        public static void BuildConfigurationMenuPrefab()
        {
            ConfigurationMenuBuilder.Build();
        }

        [MenuItem("MTD/Setup/3. Create MTD Scene")]
        public static void CreateScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Every MTD UI is a Screen Space canvas, so this camera only clears the screen to black behind it.
            GameObject cameraObject = new GameObject("Camera") { tag = "MainCamera" };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 0;
            camera.orthographic = true;

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();

            GameObject app = new GameObject("MTD");
            app.AddComponent<MtdApp>();

            GameObject menuPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ConfigurationMenuBuilder.PrefabPath);
            if (menuPrefab != null)
            {
                PrefabUtility.InstantiatePrefab(menuPrefab, scene);
            }
            else
            {
                Debug.LogWarning($"MTD: {ConfigurationMenuBuilder.PrefabPath} does not exist yet, so the scene has no Configuration Menu. Run setup step 2 first.");
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"MTD: created {ScenePath} and made it the only scene in the build.");
        }

        [MenuItem("MTD/Setup/4. Apply Player Settings")]
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "TCS JohnHuxley";
            PlayerSettings.productName = "DDS Multi-Table Display";
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
            Debug.Log("MTD: player settings applied.");
        }
    }
}
