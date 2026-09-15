using System;
using FlatWorld.AIECS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace FlatWorld.AIECS.Editor
{
    /// <summary>创建可直接 Play 的正式 AIECS 开发入口；使用 Unity 序列化保存资产，复用现有动画目录和启动场景。</summary>
    public static class AiecsPlaygroundTools
    {
        public const string ScenePath = "Assets/3_Scenes/Development/AIECS实战入口.unity";
        public const string PrefabPath = "Assets/2_Prefabs/Development/AIECS实战开发入口.prefab";
        public const string CatalogPath = "Assets/6_Art/Generated/AIECS/生物动画目录.asset";

        /// <summary>用户显式打开开发入口；已有场景的未保存修改由 Unity 的标准场景保存流程处理。</summary>
        [MenuItem("FlatWorld/AIECS/打开实战开发入口")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play 后打开入口场景。");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateAssets(); EditorSceneManager.OpenScene(ScenePath);
        }

        /// <summary>仅创建缺失资产；不覆盖用户已经调整的场景、配置或当前打开场景。</summary>
        public static string CreateAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在编译结束后的编辑模式创建入口。");
            var catalog = AssetDatabase.LoadAssetAtPath<AiecsAnimationCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("缺少当前生物动画目录，请先使用 AIECS 动画导出入口。");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return ScenePath;
            EnsureFolder("Assets/2_Prefabs", "Development"); EnsureFolder("Assets/3_Scenes", "Development");
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (prefab == null)
                {
                    var host = new GameObject("AIECS实战开发入口"); SceneManager.MoveGameObjectToScene(host, scene);
                    var entry = host.AddComponent<AiecsPlayground>(); entry.Catalog = catalog; entry.LoadGameStartOnPlay = true;
                    prefab = PrefabUtility.SaveAsPrefabAsset(host, PrefabPath); UnityEngine.Object.DestroyImmediate(host);
                }
                PrefabUtility.InstantiatePrefab(prefab, scene);
                var cameraObject = new GameObject("入口相机"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 6f;
                camera.backgroundColor = Color.black; camera.clearFlags = CameraClearFlags.SolidColor; cameraObject.transform.position = new Vector3(0, 0, -10);
                var lightObject = new GameObject("入口全局光"); SceneManager.MoveGameObjectToScene(lightObject, scene);
                lightObject.AddComponent<Light2D>().lightType = Light2D.LightType.Global;
                EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            return ScenePath;
        }

        /// <summary>通过 AssetDatabase 创建稳定目录，让 Unity 生成正确 GUID。</summary>
        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
