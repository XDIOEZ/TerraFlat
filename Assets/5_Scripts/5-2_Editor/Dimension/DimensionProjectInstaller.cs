#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class DimensionProjectInstaller
{
    private const string CatalogDirectory = "Assets/Resources/Config";
    private const string CatalogPath = CatalogDirectory + "/DimensionCatalog_Default.asset";

    [MenuItem("FlatWorld/Dimension/Install Default Catalog")]
    public static void InstallDefaultCatalog()
    {
        Directory.CreateDirectory(CatalogDirectory);
        DimensionCatalogSO catalog = AssetDatabase.LoadAssetAtPath<DimensionCatalogSO>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<DimensionCatalogSO>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.ResetToDefaults();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[DimensionProjectInstaller] 默认维度目录已写入：{CatalogPath}");
    }
}
#endif
