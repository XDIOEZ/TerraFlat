// AI-Context: FlatWorld 全局 UI 主题迁移器；把运行时主题预览烘焙进 UI Prefab，禁止在这里添加业务事件或修改节点命名契约。

using System;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class FlatWorldUIThemeMigrator
{
    private const string PrefabRoot = "Assets/2_Prefabs/2-1_UI";
    private const string MigrationVersion = "2026.07.21.v4-structural";

    static FlatWorldUIThemeMigrator()
    {
        EditorApplication.delayCall += RunAutomaticMigration;
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
                if (UsesBespokeMenuArt(path))
                    continue;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (root.GetComponentInChildren<CanvasRenderer>(true) == null)
                        continue;

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
        Debug.Log($"[FlatWorld UI] 已统一 {styledCount} 个 UI Prefab；主界面、存档、新游戏保留专属美术。");
    }

    private static void RunAutomaticMigration()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            return;

        string projectKey = $"FlatWorld.UI.ThemeMigration.{Application.dataPath.GetHashCode()}.{MigrationVersion}";
        if (EditorPrefs.GetBool(projectKey, false))
            return;

        ApplyThemeToAllPrefabs();
        EditorPrefs.SetBool(projectKey, true);
    }

    private static bool UsesBespokeMenuArt(string path)
    {
        return path.EndsWith("UI_MainMenu.prefab", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith("UI_SaveSelectionPanel.prefab", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith("UI_NewGame.prefab", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith("UI_SaveSelectionButton.prefab", StringComparison.OrdinalIgnoreCase);
    }
}
