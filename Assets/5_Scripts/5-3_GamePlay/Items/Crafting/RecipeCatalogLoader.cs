using System;
using System.Collections;
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

    #region 玩家 JSON 与二进制配置包

    private static BinaryContentCache CreateBinaryCache(string root, string manifestPath)
        => BinaryContentCache.ForBuiltIn<RecipeBinaryCatalog>("recipes", root, manifestPath, 1);

    private static RecipeBinaryCatalog ReadLocalCatalog(BinaryContentCache cache, string root, string manifestPath)
    {
        RecipeManifestDto manifest = DeserializeManifest(cache.ReadText(manifestPath));
        ValidateManifest(manifest);
        var result = new RecipeBinaryCatalog { Manifest = manifest };
        foreach (RecipePackageDto package in manifest.Packages.Where(value => value.Enabled))
            result.Catalogs.Add(RecipeRuntimeFactory.Deserialize(cache.ReadText(ResolvePackagePath(root, package.Path))));
        return result;
    }

    private static List<(RecipePackageDto Package, List<RuntimeRecipe> Recipes, List<string> Warnings)> PreparePackages(
        RecipeBinaryCatalog catalog, Func<string, bool> itemExists)
    {
        ValidateManifest(catalog?.Manifest);
        RecipePackageDto[] enabled = catalog.Manifest.Packages.Where(value => value.Enabled).ToArray();
        if (catalog.Catalogs == null || enabled.Length != catalog.Catalogs.Count)
            throw new InvalidDataException("配方二进制包与 JSON 清单的启用分包不一致");
        var results = new List<(RecipePackageDto, List<RuntimeRecipe>, List<string>)>(enabled.Length);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < enabled.Length; index++)
        {
            List<RuntimeRecipe> recipes = RecipeRuntimeFactory.BuildCatalog(catalog.Catalogs[index], itemExists,
                out List<string> warnings);
            foreach (RuntimeRecipe recipe in recipes)
                if (!ids.Add(recipe.Id)) throw new InvalidDataException($"跨分包存在重复配方 ID：{recipe.Id}");
            results.Add((enabled[index], recipes, warnings));
        }
        return results;
    }

    #endregion

    public static int LoadBuiltIn(GameRes gameRes)
    {
        if (gameRes == null)
            throw new ArgumentNullException(nameof(gameRes));

        string recipeRoot = StreamingAssetsTextLoader.CombinePath(
            Application.streamingAssetsPath,
            RelativeRecipeRoot);
        string manifestPath = StreamingAssetsTextLoader.CombinePath(recipeRoot, ManifestFileName);
        BinaryContentCache cache = CreateBinaryCache(recipeRoot, manifestPath);
        bool hit = cache.TryRead(out RecipeBinaryCatalog catalog);
        if (!hit) catalog = ReadLocalCatalog(cache, recipeRoot, manifestPath);
        var prepared = PreparePackages(catalog, gameRes.AllPrefabs.ContainsKey);
        if (!hit) cache.Store(catalog);
        int count = 0;
        foreach (var package in prepared)
        {
            foreach (string warning in package.Warnings) Debug.LogWarning($"[RecipeCatalog:{package.Package.Id}] {warning}");
            foreach (RuntimeRecipe recipe in package.Recipes) { gameRes.RegisterRecipe(recipe, true); count++; }
        }
        cache.Report("配方", count);
        Debug.Log($"[RecipeCatalog] 已从 {prepared.Count} 个业务分包加载 {count} 条配方：{manifestPath}");
        return count;
    }

    /// <summary>跨平台协程入口；Android/WebGL 使用 UnityWebRequest 读取包内文件。</summary>
    public static IEnumerator LoadBuiltInAsync(
        GameRes gameRes,
        Action<int> onCompleted,
        Action<Exception> onFailed)
    {
        if (gameRes == null)
        {
            onFailed?.Invoke(new ArgumentNullException(nameof(gameRes)));
            yield break;
        }

        string recipeRoot;
        string manifestPath;
        try
        {
            recipeRoot = StreamingAssetsTextLoader.CombinePath(
                Application.streamingAssetsPath,
                RelativeRecipeRoot);
            manifestPath = StreamingAssetsTextLoader.CombinePath(recipeRoot, ManifestFileName);
        }
        catch (Exception exception)
        {
            onFailed?.Invoke(exception);
            yield break;
        }

        BinaryContentCache cache = CreateBinaryCache(recipeRoot, manifestPath);
        RecipeBinaryCatalog binary = null;
        yield return cache.TryReadAsync<RecipeBinaryCatalog>(value => binary = value);
        bool hit = binary != null;
        Exception readError = null;
        if (!hit && !StreamingAssetsTextLoader.RequiresWebRequest(recipeRoot))
        {
            string manifestJson = null;
            yield return cache.ReadTextAsync(manifestPath, value => manifestJson = value, exception => readError = exception);
            if (readError != null) { onFailed?.Invoke(readError); yield break; }
            RecipeManifestDto manifest;
            string[] paths;
            try
            {
                manifest = DeserializeManifest(manifestJson);
                ValidateManifest(manifest);
                // 配置路径在主线程统一校验，后台只读取已确认的文件。
                paths = manifest.Packages.Where(value => value.Enabled)
                    .Select(package => ResolvePackagePath(recipeRoot, package.Path)).ToArray();
            }
            catch (Exception exception) { onFailed?.Invoke(exception); yield break; }

            // 首次启动在后台合并读取分包 JSON，之后只恢复二进制 DTO。
            yield return StreamingAssetsTextLoader.RunPureDataAsync(
                () =>
                {
                    var result = new RecipeBinaryCatalog { Manifest = manifest };
                    foreach (string path in paths)
                        result.Catalogs.Add(RecipeRuntimeFactory.Deserialize(cache.ReadText(path)));
                    return result;
                },
                value => binary = value, exception => readError = exception);
        }
        else if (!hit)
        {
            string manifestJson = null;
            yield return cache.ReadTextAsync(manifestPath, value => manifestJson = value, exception => readError = exception);
            if (readError != null) { onFailed?.Invoke(readError); yield break; }
            RecipeManifestDto manifest;
            try { manifest = DeserializeManifest(manifestJson); ValidateManifest(manifest); }
            catch (Exception exception) { onFailed?.Invoke(exception); yield break; }
            binary = new RecipeBinaryCatalog { Manifest = manifest };
            foreach (RecipePackageDto package in manifest.Packages.Where(value => value.Enabled))
            {
                string packagePath;
                try { packagePath = ResolvePackagePath(recipeRoot, package.Path); }
                catch (Exception exception) { onFailed?.Invoke(exception); yield break; }
                string json = null;
                yield return cache.ReadTextAsync(packagePath, value => json = value, exception => readError = exception);
                if (readError != null) { onFailed?.Invoke(readError); yield break; }
                RecipeCatalogDto parsed = null;
                yield return StreamingAssetsTextLoader.RunPureDataAsync(() => RecipeRuntimeFactory.Deserialize(json),
                    value => parsed = value, exception => readError = exception);
                if (readError != null) { onFailed?.Invoke(readError); yield break; }
                binary.Catalogs.Add(parsed);
            }
        }
        if (readError != null) { onFailed?.Invoke(readError); yield break; }

        // 物品引用与规则每轮按最新目录重新校验，不缓存运行时对象。
        var knownItems = new HashSet<string>(gameRes.AllPrefabs.Keys, gameRes.AllPrefabs.Comparer);
        List<(RecipePackageDto Package, List<RuntimeRecipe> Recipes, List<string> Warnings)> preparedPackages = null;
        yield return StreamingAssetsTextLoader.RunPureDataAsync(
            () => PreparePackages(binary, knownItems.Contains),
            value => preparedPackages = value, exception => readError = exception);
        if (readError != null) { onFailed?.Invoke(readError); yield break; }
        if (!hit) yield return cache.StoreAsync(binary);

        int count = 0;
        foreach (var package in preparedPackages)
        {
            foreach (string warning in package.Warnings)
            {
                Debug.LogWarning($"[RecipeCatalog:{package.Package.Id}] {warning}");
                if (gameRes.ShouldYieldResourceWork()) yield return null;
            }
            foreach (RuntimeRecipe recipe in package.Recipes)
            {
                try { gameRes.RegisterRecipe(recipe, true); count++; }
                catch (Exception exception) { onFailed?.Invoke(exception); yield break; }
                if (gameRes.ShouldYieldResourceWork()) yield return null;
            }
        }

        try
        {
            cache.Report("配方", count);
            Debug.Log($"[RecipeCatalog] 已从 {preparedPackages.Count} 个业务分包加载 {count} 条配方：{manifestPath}");
            onCompleted?.Invoke(count);
        }
        catch (Exception exception)
        {
            onFailed?.Invoke(exception);
        }
    }

    public static string Serialize(RecipeCatalogDto catalog)
    {
        return JsonConvert.SerializeObject(catalog, Formatting.Indented);
    }

    public static RecipeManifestDto DeserializeManifest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("配方分包清单为空");
        return JsonConfigurationCache.Deserialize<RecipeManifestDto>(json);
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
        return StreamingAssetsTextLoader.ResolveCatalogPackagePathWithinRoot(recipeRoot, relativePath, "配方", "Recipes");
    }
}

/// <summary>首次运行从 JSON 生成；玩家修改清单或分包后由来源哈希触发重建。</summary>
[MemoryPack.MemoryPackable]
public sealed partial class RecipeBinaryCatalog
{
    public RecipeManifestDto Manifest;
    public List<RecipeCatalogDto> Catalogs = new();
}
