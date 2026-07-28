using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 从 StreamingAssets 配方清单读取业务分包，并统一注册到 GameRes。
/// </summary>
public static class RecipeCatalogLoader
{
    public const string RelativeRecipeRoot = "GameConfig/Recipes";
    public const string ManifestFileName = "recipe-manifest.json";
    public const string RelativeManifestPath = RelativeRecipeRoot + "/" + ManifestFileName;

    public static int LoadBuiltIn(GameRes gameRes)
    {
        if (gameRes == null)
            throw new ArgumentNullException(nameof(gameRes));

        string recipeRoot = Path.Combine(Application.streamingAssetsPath, RelativeRecipeRoot);
        string manifestPath = Path.Combine(recipeRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"找不到配方分包清单：{manifestPath}", manifestPath);

        RecipeManifestDto manifest = DeserializeManifest(File.ReadAllText(manifestPath));
        ValidateManifest(manifest);

        var recipeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loadedRecipes = new List<RuntimeRecipe>();
        int loadedPackageCount = 0;
        foreach (RecipePackageDto package in manifest.Packages.Where(package => package.Enabled))
        {
            string packagePath = ResolvePackagePath(recipeRoot, package.Path);
            if (!File.Exists(packagePath))
                throw new FileNotFoundException($"配方分包 {package.Id} 不存在：{packagePath}", packagePath);

            RecipeCatalogDto catalog = RecipeRuntimeFactory.Deserialize(File.ReadAllText(packagePath));
            List<RuntimeRecipe> recipes = RecipeRuntimeFactory.BuildCatalog(
                catalog,
                itemId => gameRes.AllPrefabs.ContainsKey(itemId),
                out List<string> warnings);

            foreach (string warning in warnings)
                Debug.LogWarning($"[RecipeCatalog:{package.Id}] {warning}");
            foreach (RuntimeRecipe recipe in recipes)
            {
                if (!recipeIds.Add(recipe.Id))
                    throw new InvalidDataException($"跨分包存在重复配方 ID：{recipe.Id}");
                loadedRecipes.Add(recipe);
            }

            loadedPackageCount++;
        }

        foreach (RuntimeRecipe recipe in loadedRecipes)
            gameRes.RegisterRecipe(recipe, true);

        Debug.Log($"[RecipeCatalog] 已从 {loadedPackageCount} 个业务分包加载 {loadedRecipes.Count} 条配方：{manifestPath}");
        return loadedRecipes.Count;
    }

    public static string Serialize(RecipeCatalogDto catalog)
    {
        return JsonConvert.SerializeObject(catalog, Formatting.Indented);
    }

    public static RecipeManifestDto DeserializeManifest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("配方分包清单为空");
        return JsonConvert.DeserializeObject<RecipeManifestDto>(json);
    }

    public static string SerializeManifest(RecipeManifestDto manifest)
    {
        return JsonConvert.SerializeObject(manifest, Formatting.Indented);
    }

    public static void ValidateManifest(RecipeManifestDto manifest)
    {
        if (manifest == null)
            throw new InvalidDataException("配方分包清单根对象为空");
        if (manifest.SchemaVersion != RecipeRuntimeFactory.SupportedSchemaVersion)
            throw new InvalidDataException($"不支持的配方清单 schemaVersion：{manifest.SchemaVersion}");

        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RecipePackageDto package in manifest.Packages ?? Enumerable.Empty<RecipePackageDto>())
        {
            if (package == null)
                throw new InvalidDataException("配方清单包含空分包定义");
            if (string.IsNullOrWhiteSpace(package.Id))
                throw new InvalidDataException("配方清单包含空分包 ID");
            if (string.IsNullOrWhiteSpace(package.Path))
                throw new InvalidDataException($"配方分包 {package.Id} 缺少 path");
            if (!packageIds.Add(package.Id.Trim()))
                throw new InvalidDataException($"配方清单包含重复分包 ID：{package.Id}");
            if (!packagePaths.Add(package.Path.Trim().Replace('\\', '/')))
                throw new InvalidDataException($"配方清单包含重复文件路径：{package.Path}");
        }
    }

    public static string ResolvePackagePath(string recipeRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(recipeRoot))
            throw new ArgumentException("配方根目录不能为空", nameof(recipeRoot));
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"配方分包路径无效：{relativePath}");

        string normalizedRoot = Path.GetFullPath(recipeRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"配方分包路径越出 Recipes 目录：{relativePath}");
        return fullPath;
    }
}
