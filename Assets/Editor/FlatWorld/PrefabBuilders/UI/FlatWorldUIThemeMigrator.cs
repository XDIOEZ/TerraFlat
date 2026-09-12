// AI-Context: FlatWorld 全局 UI 主题迁移器；把运行时主题预览烘焙进 UI Prefab，禁止在这里添加业务事件或修改节点命名契约。

using System;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class FlatWorldUIThemeMigrator
{
    private const string PrefabRoot = "Assets/2_Prefabs/2-1_UI";
    private const string MigrationVersion = "2026.09.11.v5.8-border-2px";

    static FlatWorldUIThemeMigrator()
    {
        // 程序集重载时 Unity 可能仍处于 compiling；用 update 等到编辑器真正空闲再迁移，
        // 避免一次 delayCall 因编译态直接返回后永久漏掉本次版本。
        EditorApplication.delayCall += RunAutomaticMigration;
        EditorApplication.update += RunAutomaticMigration;
    }

    [MenuItem("FlatWorld/UI/统一游戏内UI主题")]
    public static void ApplyThemeToAllPrefabs()
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
        Debug.Log($"[FlatWorld UI] 已将 {styledCount} 个 UI Prefab 统一为灰阶简约主题；主界面保留参考图背景与专属排版。");
    }

    private static void RunAutomaticMigration()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            return;

        EditorApplication.delayCall -= RunAutomaticMigration;
        EditorApplication.update -= RunAutomaticMigration;

        string projectKey = $"FlatWorld.UI.ThemeMigration.{Application.dataPath.GetHashCode()}.{MigrationVersion}";
        if (EditorPrefs.GetBool(projectKey, false))
            return;

        ApplyThemeToAllPrefabs();
        EditorPrefs.SetBool(projectKey, true);
    }

    private static bool PreservesReferenceMainMenu(string path)
    {
        return path.EndsWith("UI_MainMenu.prefab", StringComparison.OrdinalIgnoreCase);
    }
}
