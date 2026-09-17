#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FlatWorld.EditorTools
{
    /// <summary>
    /// 控制 AIECS 运行时批次是否参与 SceneView 渲染。
    /// GameView 和正式构建仍通过相机原有 CullingMask 渲染；默认只从 SceneView 排除，避免 Play Mode 压测时同一批动态 Mesh 被编辑器重复绘制。
    /// </summary>
    [InitializeOnLoad]
    internal static class AiecsSceneViewVisibility
    {
        private const string RuntimeLayerName = "AIECSRuntime";
        private const string PreferenceKey = "FlatWorld.AIECS.ShowRuntimeBatchesInSceneView";
        private const string MenuPath = "Tools/AIECS/SceneView 显示运行时批次";

        static AiecsSceneViewVisibility()
        {
            // 只在域重载后应用一次，不订阅 EditorApplication.update / hierarchyChanged 热路径。
            EditorApplication.delayCall += ApplyPreference;
        }

        [MenuItem(MenuPath, priority = 1950)]
        private static void ToggleSceneViewVisibility()
        {
            bool visible = !EditorPrefs.GetBool(PreferenceKey, false);
            EditorPrefs.SetBool(PreferenceKey, visible);
            ApplyPreference();
            SceneView.RepaintAll();
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateSceneViewVisibility()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(PreferenceKey, false));
            return true;
        }

        /// <summary>按本机偏好一次性更新 SceneView 图层遮罩；默认隐藏 AIECS 压测批次。</summary>
        private static void ApplyPreference()
        {
            int layer = LayerMask.NameToLayer(RuntimeLayerName);
            if (layer < 0)
            {
                Debug.LogError($"[AIECS] SceneView 优化要求项目存在 Layer：{RuntimeLayerName}");
                return;
            }

            int layerBit = 1 << layer;
            bool visible = EditorPrefs.GetBool(PreferenceKey, false);
            Tools.visibleLayers = visible
                ? Tools.visibleLayers | layerBit
                : Tools.visibleLayers & ~layerBit;
        }
    }
}
#endif
