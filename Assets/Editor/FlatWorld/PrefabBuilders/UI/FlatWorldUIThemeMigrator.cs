// AI-Context: FlatWorld 全局 UI 主题迁移器；把运行时主题预览烘焙进 UI Prefab，禁止在这里添加业务事件或修改节点命名契约。

using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// FlatWorld 全局 UI 主题迁移器。
/// 迁移只允许由开发者通过菜单显式执行，禁止在 Unity 启动、脚本重载或编译完成后自动批量保存 Prefab。
/// </summary>
public static class FlatWorldUIThemeMigrator
{
    private const string PrefabRoot = "Assets/2_Prefabs/2-1_UI";
    private const string MigrationVersion = "2026.09.11.v5.8-border-2px";

    /// <summary>执行当前版本迁移；同一项目、同一迁移版本默认只执行一次。</summary>
    [MenuItem("FlatWorld/UI/主题迁移/执行当前版本迁移")]
    public static void ApplyCurrentVersionMigration()
    {
        EnsureCanMigrate();

        string projectKey = GetMigrationProjectKey();
        if (EditorPrefs.GetBool(projectKey, false))
        {
            Debug.Log($"[FlatWorld UI] 当前主题迁移版本已执行：{MigrationVersion}。如需重新烘焙，请使用“强制重新应用统一主题”。");
            return;
        }

        int styledCount = ApplyThemeToAllPrefabsInternal();
        EditorPrefs.SetBool(projectKey, true);
        Debug.Log($"[FlatWorld UI] 已完成主题迁移 {MigrationVersion}，更新 {styledCount} 个 UI Prefab。");
    }

    /// <summary>忽略迁移版本记录，显式强制重新烘焙全部 UI Prefab。</summary>
    [MenuItem("FlatWorld/UI/主题迁移/强制重新应用统一主题")]
    public static void ApplyThemeToAllPrefabs()
    {
        EnsureCanMigrate();

        int styledCount = ApplyThemeToAllPrefabsInternal();
        EditorPrefs.SetBool(GetMigrationProjectKey(), true);
        Debug.Log($"[FlatWorld UI] 已强制将 {styledCount} 个 UI Prefab 重新应用灰阶简约主题；主界面保留参考图背景与专属排版。");
    }

    /// <summary>执行实际 Prefab 遍历与主题烘焙，不负责决定迁移触发时机。</summary>
    private static int ApplyThemeToAllPrefabsInternal()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
        int styledCount = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                bool preservesReferenceMainMenu = PreservesReferenceMainMenu(path);

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (root.GetComponentInChildren<CanvasRenderer>(true) == null)
                        continue;

                    // 主菜单保留专属排版和背景，但边框厚度仍服从全局 2px 规范。
                    FlatWorldUITheme.ApplyBorderThickness(root.transform);

                    if (preservesReferenceMainMenu)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        styledCount++;
                        continue;
                    }

                    // 行囊曾使用独立彩色槽位 Sprite 配置；全局灰阶主题下回归通用槽位，避免运行时再次覆盖统一样式。
                    InventorySlotVisualProfile inventoryProfile = root.GetComponent<InventorySlotVisualProfile>();
                    if (inventoryProfile != null)
                        UnityEngine.Object.DestroyImmediate(inventoryProfile, true);

                    FlatWorldUITheme.Apply(root.transform);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    styledCount++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.SaveAssets();
        return styledCount;
    }

    /// <summary>迁移只能在非 PlayMode、非编译阶段由开发者主动执行。</summary>
    private static void EnsureCanMigrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出 PlayMode，再执行 UI 主题迁移。");
        if (EditorApplication.isCompiling)
            throw new InvalidOperationException("Unity 正在编译脚本，请在编译完成后重新执行 UI 主题迁移。");
    }

    /// <summary>生成当前项目与迁移版本对应的本地执行记录键。</summary>
    private static string GetMigrationProjectKey()
    {
        return $"FlatWorld.UI.ThemeMigration.{Application.dataPath.GetHashCode()}.{MigrationVersion}";
    }

    /// <summary>判断是否为需要保留专属视觉排版的主菜单 Prefab。</summary>
    private static bool PreservesReferenceMainMenu(string path)
    {
        return path.EndsWith("UI_MainMenu.prefab", StringComparison.OrdinalIgnoreCase);
    }
}
