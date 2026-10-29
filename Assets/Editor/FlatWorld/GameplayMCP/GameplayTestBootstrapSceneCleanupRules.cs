using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace FlatWorld.GameplayMCP
{
    internal static class GameplayTestBootstrapSceneCleanupRules
    {
        #region 临时测试场景清理采样
        [InitializeOnLoadMethod]
        private static void Register() => GameplayTestSystemProbe.Register("testing", "bootstrap_scene_cleanup", Capture);

        private static JObject Capture(JObject _)
        {
            string token = Guid.NewGuid().ToString("D");
            string openedPath = "Assets/InitTestScene" + token + ".unity";
            string orphanPath = "Assets/InitTestScene" + Guid.NewGuid().ToString("D") + ".unity";
            string lookalikePath = "Assets/InitTestScene" + token.Replace("-", string.Empty) + ".unity";
            string nestedPath = "Assets/3_Scenes/Development/InitTestScene" + token + ".unity";
            Scene originalScene = SceneManager.GetActiveScene();
            Scene fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                // 自有空场景实际落盘，验证删除、meta 收尾及已打开场景保留。
                bool saved = EditorSceneManager.SaveScene(fixture, openedPath) &&
                    EditorSceneManager.SaveScene(fixture, orphanPath, true) &&
                    EditorSceneManager.SaveScene(fixture, lookalikePath, true) &&
                    EditorSceneManager.SaveScene(fixture, nestedPath, true);
                SceneManager.SetActiveScene(originalScene);
                bool hadMeta = File.Exists(orphanPath + ".meta");
                bool cleaned = GameplayTestPipelineCoordinator.TryCleanupUnopenedTestRunnerBootstrapScenes(out int deleted);
                bool repeated = GameplayTestPipelineCoordinator.TryCleanupUnopenedTestRunnerBootstrapScenes(out int deletedAgain);
                bool invalidRejected = true;
                foreach (string path in new[] { null, string.Empty, "Assets/InitTestScene.unity", lookalikePath,
                    nestedPath, openedPath + ".meta", "Assets/InitTestScene" + new string('x', 36) + ".unity" })
                    invalidRejected &= !GameplayTestPipelineCoordinator.IsTestRunnerBootstrapScene(path);
                return new JObject
                {
                    ["saved"] = saved, ["hadMeta"] = hadMeta, ["cleaned"] = cleaned, ["deleted"] = deleted,
                    ["orphanDeleted"] = !File.Exists(orphanPath) && string.IsNullOrEmpty(
                        AssetDatabase.AssetPathToGUID(orphanPath, AssetPathToGUIDOptions.OnlyExistingAssets)),
                    ["orphanMetaDeleted"] = !File.Exists(orphanPath + ".meta"),
                    ["openedPreserved"] = File.Exists(openedPath) && fixture.isLoaded,
                    ["lookalikePreserved"] = File.Exists(lookalikePath), ["nestedPreserved"] = File.Exists(nestedPath),
                    ["activeScenePreserved"] = SceneManager.GetActiveScene() == originalScene,
                    ["repeatedNoChange"] = repeated && deletedAgain == 0,
                    ["recognized"] = GameplayTestPipelineCoordinator.IsTestRunnerBootstrapScene(openedPath),
                    ["invalidRejected"] = invalidRejected
                };
            }
            finally
            {
                EditorSceneManager.CloseScene(fixture, true);
                SceneManager.SetActiveScene(originalScene);
                foreach (string path in new[] { openedPath, orphanPath, lookalikePath, nestedPath })
                    if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            }
        }
        #endregion
    }
}
