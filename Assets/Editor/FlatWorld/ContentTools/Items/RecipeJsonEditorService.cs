#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

/// <summary>
/// 编辑器侧 JSON 配方维护入口；直接修改清单声明的业务分包，避免重新引入 Excel 双重真源。
/// 所有写入均先完成结构校验，并通过临时文件覆盖目标 JSON。
/// </summary>
public static class RecipeJsonEditorService
{
    #region 常量

    private const string RecipeRootAssetPath = "Assets/StreamingAssets/GameConfig/Recipes";
    private const string ManifestAssetPath = RecipeRootAssetPath + "/recipe-manifest.json";

    #endregion

    #region 公共入口

    /// <summary>在所有配方分包中替换产物物品 ID。</summary>
    public static int RelinkOutputItemId(string oldItemId, string newItemId)
    {
        if (string.IsNullOrWhiteSpace(oldItemId) || string.IsNullOrWhiteSpace(newItemId))
            return 0;

        string recipeRoot = ToAbsolutePath(RecipeRootAssetPath);
        RecipeManifestDto manifest = ReadManifest();
        int changed = 0;

        foreach (RecipePackageDto package in manifest.Packages ?? new List<RecipePackageDto>())
        {
            string packagePath = RecipeCatalogLoader.ResolvePackagePath(recipeRoot, package.Path);
            if (!File.Exists(packagePath))
                throw new FileNotFoundException($"找不到配方分包：{package.Path}", packagePath);

            RecipeCatalogDto catalog = RecipeRuntimeFactory.Deserialize(File.ReadAllText(packagePath));
            int packageChanges = RelinkCatalogOutputs(catalog, oldItemId, newItemId);
            if (packageChanges <= 0)
                continue;

            ValidateCatalog(catalog, package.Id);
            WriteTextAtomic(packagePath, RecipeCatalogLoader.Serialize(catalog));
            changed += packageChanges;
        }

        if (changed > 0)
            AssetDatabase.Refresh();
        return changed;
    }

    #endregion

    #region JSON 读写与校验

    /// <summary>读取并校验配方分包清单。</summary>
    private static RecipeManifestDto ReadManifest()
    {
        string manifestPath = ToAbsolutePath(ManifestAssetPath);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"找不到配方分包清单：{ManifestAssetPath}", manifestPath);

        RecipeManifestDto manifest = RecipeCatalogLoader.DeserializeManifest(File.ReadAllText(manifestPath));
        RecipeCatalogLoader.ValidateManifest(manifest);
        return manifest;
    }

    /// <summary>替换单个分包中的匹配产物 ID。</summary>
    private static int RelinkCatalogOutputs(RecipeCatalogDto catalog, string oldItemId, string newItemId)
    {
        int changed = 0;
        foreach (RecipeDto recipe in catalog?.Recipes ?? new List<RecipeDto>())
        {
            foreach (RecipeOutputDto output in recipe.Outputs ?? new List<RecipeOutputDto>())
            {
                if (!string.Equals(output.ItemId, oldItemId, StringComparison.Ordinal))
                    continue;

                output.ItemId = newItemId;
                changed++;
            }
        }

        return changed;
    }

    /// <summary>按运行时规则校验待写入的配方分包。</summary>
    private static void ValidateCatalog(RecipeCatalogDto catalog, string packageId)
    {
        if (catalog == null)
            throw new InvalidDataException($"配方分包 {packageId} 根对象为空。");
        if (catalog.SchemaVersion != RecipeRuntimeFactory.SupportedSchemaVersion)
            throw new InvalidDataException($"配方分包 {packageId} 的 schemaVersion 不受支持：{catalog.SchemaVersion}");

        RecipeRuntimeFactory.BuildCatalog(catalog, _ => true, out List<string> warnings);
        if (warnings.Count > 0)
            throw new InvalidDataException($"配方分包 {packageId} 校验产生警告：{string.Join("；", warnings)}");
    }

    /// <summary>通过临时文件安全覆盖 JSON。</summary>
    private static void WriteTextAtomic(string path, string content)
    {
        string temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            File.Copy(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    /// <summary>将 Unity 资源路径转换为绝对路径。</summary>
    private static string ToAbsolutePath(string assetPath)
    {
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
    }

    #endregion
}

#endif
