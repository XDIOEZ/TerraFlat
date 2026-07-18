using System.Collections.Generic;
using System.IO;
using FlatWorld.Networking.MirrorAdapter;
using kcp2k;
using Mirror;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Networking.Testing.Editor
{
    public static class NetworkTestSceneBuilder
    {
        public const string ScenePath = "Assets/3_Scenes/NetworkTest.unity";
        public const string PlayerPrefabPath = "Assets/2_Prefabs/NetworkingTest/NetworkTestPlayer.prefab";
        public const string BuildPath = "Builds/NetworkTest/FlatWorldNetworkTest.exe";

        [MenuItem("FlatWorld/Networking Test/Create or Update Test Scene")]
        public static void CreateOrUpdateTestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            CreateAssets(true);
            Debug.Log($"[NET_TEST] Test scene created: {ScenePath}");
        }

        [MenuItem("FlatWorld/Networking Test/Build Windows Test Player")]
        public static void BuildWindowsTestPlayer()
        {
            CreateAssets(false);

            string absoluteBuildPath = Path.GetFullPath(BuildPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteBuildPath));

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = absoluteBuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"Network test build failed: {report.summary.result}");

            Debug.Log($"[NET_TEST] Build succeeded: {BuildPath} ({report.summary.totalSize} bytes)");
        }

        private static void CreateAssets(bool openScene)
        {
            EnsureFolder("Assets/2_Prefabs", "NetworkingTest");

            GameObject playerPrefab = CreatePlayerPrefab();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
            CreateLight();
            CreateSpawnPoints();

            GameObject managerObject = new GameObject("NetworkManager");
            KcpTransport transport = managerObject.AddComponent<KcpTransport>();
            FlatWorldNetworkManager manager = managerObject.AddComponent<FlatWorldNetworkManager>();
            manager.transport = transport;
            manager.playerPrefab = playerPrefab;
            manager.autoCreatePlayer = true;
            manager.maxConnections = 8;
            manager.sendRate = 30;
            managerObject.AddComponent<NetworkTestHud>();
            managerObject.AddComponent<NetworkTestAutoLauncher>();
            managerObject.AddComponent<NetworkTestDiagnostics>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (openScene)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        private static GameObject CreatePlayerPrefab()
        {
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "NetworkTestPlayer";
            player.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);

            Collider collider = player.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            player.AddComponent<NetworkIdentity>();
            player.AddComponent<NetworkTestPlayer>();
            player.AddComponent<MirrorNetworkEntityContext>();
            NetworkTransformUnreliable networkTransform = player.AddComponent<NetworkTransformUnreliable>();
            networkTransform.target = player.transform;
            networkTransform.syncDirection = SyncDirection.ClientToServer;
            networkTransform.syncRotation = false;
            networkTransform.syncScale = false;
            networkTransform.interpolatePosition = true;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            Object.DestroyImmediate(player);
            return prefab;
        }

        private static void CreateCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 7f;
            camera.backgroundColor = new Color(0.06f, 0.08f, 0.12f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        }

        private static void CreateLight()
        {
            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
        }

        private static void CreateSpawnPoints()
        {
            Vector3[] positions =
            {
                new Vector3(-4f, -2f, 0f),
                new Vector3(4f, -2f, 0f),
                new Vector3(-4f, 2f, 0f),
                new Vector3(4f, 2f, 0f)
            };

            for (int i = 0; i < positions.Length; i++)
            {
                GameObject spawn = new GameObject($"SpawnPoint_{i + 1}");
                spawn.transform.position = positions[i];
                spawn.AddComponent<NetworkStartPosition>();
            }
        }

        private static void AddSceneToBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(item => item.path == ScenePath))
                return;

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
