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
            result.Catalogs.Add(RecipeRuntimeFactory.DeserializeWithoutDocumentCache(cache.ReadText(ResolvePackagePath(root, package.Path))));
        return result;
    }

    private static List<(RecipePackageDto Package, List<RuntimeRecipe> Recipes, List<string> Warnings)> PreparePackages(
        RecipeBinaryCatalog catalog, Func<string, bool> itemExists)
    {
        if (BuiltInContentBinaryCatalog.TryRead("compiled:recipes", 1, out CompiledRecipePackage[] baked))
        {
            return baked.Select(package => (package.Package, package.Recipes,
                ValidateCompiledRecipeItemReferences(package.Recipes, itemExists))).ToList();
        }
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
        BuiltInContentBinaryCatalog.Record("compiled:recipes", 1, results.Select(package =>
            new CompiledRecipePackage { Package = package.Item1, Recipes = package.Item2 }).ToArray());
        return results;
    }

    /// <summary>构建时编译配方结构，当前物品目录的引用检查留在资源会话。</summary>
    [BuiltInContentBake]
    public static void BakeBuiltInConfiguration()
    {
        string root = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeRecipeRoot);
        string manifest = StreamingAssetsTextLoader.CombinePath(root, ManifestFileName);
        BinaryContentCache cache = CreateBinaryCache(root, manifest);
        if (!cache.TryRead(out RecipeBinaryCatalog catalog))
        {
            catalog = ReadLocalCatalog(cache, root, manifest);
            cache.Store(catalog);
        }
        PreparePackages(catalog, _ => true);
    }

    private static List<string> ValidateCompiledRecipeItemReferences(IEnumerable<RuntimeRecipe> recipes, Func<string, bool> itemExists)
    {
        var warnings = new List<string>();
        if (itemExists == null) return warnings;
        foreach (RuntimeRecipe recipe in recipes)
        {
            foreach (RuntimeRecipeIngredient input in recipe.inputs.RowItems_List)
                if (input.matchMode == MatchMode.ExactItem && !itemExists(input.ItemName))
                    warnings.Add($"配方 {recipe.Id} 输入引用的物品尚未注册：{input.ItemName}");
            foreach (RuntimeRecipeResult output in recipe.outputs.results)
                if (!string.IsNullOrEmpty(output.ItemName) && !itemExists(output.ItemName))
                    warnings.Add($"配方 {recipe.Id} 输出引用的物品尚未注册：{output.ItemName}");
        }
        return warnings;
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
        => LoadBuiltInAsync(gameRes, onCompleted, onFailed, null);

    internal sealed class RecipeCatalogPreparation
    {
        public BinaryContentCache Cache;
        public RecipeBinaryCatalog Catalog;
        public bool Hit;
        public bool CacheWriteAttempted;
        public string ManifestPath;
        public double InputMilliseconds;
    }

    internal static IEnumerator LoadBuiltInAsync(GameRes gameRes, Action<int> onCompleted,
        Action<Exception> onFailed, RecipeCatalogPreparation prepared)
    {
        if (gameRes == null)
        {
            onFailed?.Invoke(new ArgumentNullException(nameof(gameRes)));
            yield break;
        }

        Exception readError = null;
        if (prepared == null)
            yield return LoadBuiltInCatalogInputAsync(value => prepared = value, exception => readError = exception);
        if (readError != null) { onFailed?.Invoke(readError); yield break; }
        BinaryContentCache cache = prepared.Cache;
        RecipeBinaryCatalog binary = prepared.Catalog;

        // 物品引用与规则每轮按最新目录重新校验，不缓存运行时对象。
        var knownItems = new HashSet<string>(gameRes.AllPrefabs.Keys, gameRes.AllPrefabs.Comparer);
        List<(RecipePackageDto Package, List<RuntimeRecipe> Recipes, List<string> Warnings)> preparedPackages = null;
        yield return StreamingAssetsTextLoader.RunPureDataAsync(
            () => PreparePackages(binary, knownItems.Contains),
            value => preparedPackages = value, exception => readError = exception);
        if (readError != null) { onFailed?.Invoke(readError); yield break; }
        if (!prepared.Hit && !prepared.CacheWriteAttempted) yield return cache.StoreAsync(binary);

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
            cache.Report("配方", count, prepared.InputMilliseconds);
            Debug.Log($"[RecipeCatalog] 已从 {preparedPackages.Count} 个业务分包加载 {count} 条配方：{prepared.ManifestPath}");
            onCompleted?.Invoke(count);
        }
        catch (Exception exception)
        {
            onFailed?.Invoke(exception);
        }
    }

    /// <summary>预读配方纯数据，物品引用和目录注册仍在正式阶段处理。</summary>
    internal static IEnumerator LoadBuiltInCatalogInputAsync(Action<RecipeCatalogPreparation> completed,
        Action<Exception> failed)
    {
        string root = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeRecipeRoot);
        string manifest = StreamingAssetsTextLoader.CombinePath(root, ManifestFileName);
        if (StreamingAssetsTextLoader.RequiresWebRequest(root))
            return LoadPackagedCatalogInputAsync(completed, failed);
        BinaryContentCache cache = CreateBinaryCache(root, manifest);
        return StreamingAssetsTextLoader.BeginPureDataAsync(
            () => LoadLocalCatalogInput(cache, root, manifest), completed, failed);
    }

    // 本地读盘、来源校验和解析在同一私有任务完成，物品引用与注册仍在正式阶段执行。
    internal static RecipeCatalogPreparation LoadLocalCatalogInput(BinaryContentCache cache, string root, string manifestPath)
    {
        bool hit = cache.TryRead(out RecipeBinaryCatalog binary);
        if (!hit)
        {
            RecipeManifestDto manifest = DeserializeManifest(cache.ReadText(manifestPath));
            ValidateManifest(manifest);
            binary = new RecipeBinaryCatalog { Manifest = manifest };
            var paths = new List<string>();
            foreach (RecipePackageDto package in manifest.Packages)
                if (package.Enabled) paths.Add(StreamingAssetsTextLoader.CombinePath(root, package.Path));
            foreach (string path in paths)
                binary.Catalogs.Add(RecipeRuntimeFactory.DeserializeWithoutDocumentCache(cache.ReadText(path)));
        }
        if (!hit) cache.Store(binary);
        return new RecipeCatalogPreparation
        { Cache = cache, Catalog = binary, Hit = hit, CacheWriteAttempted = true, ManifestPath = manifestPath, InputMilliseconds = cache.ElapsedMilliseconds };
    }

    private static IEnumerator LoadPackagedCatalogInputAsync(Action<RecipeCatalogPreparation> completed,
        Action<Exception> failed)
    {
        string recipeRoot, manifestPath;
        try
        {
            recipeRoot = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeRecipeRoot);
            manifestPath = StreamingAssetsTextLoader.CombinePath(recipeRoot, ManifestFileName);
        }
        catch (Exception exception) { failed?.Invoke(exception); yield break; }
        BinaryContentCache cache = CreateBinaryCache(recipeRoot, manifestPath);
        RecipeBinaryCatalog binary = null;
        yield return cache.TryReadAsync<RecipeBinaryCatalog>(value => binary = value);
        bool hit = binary != null;
        Exception readError = null;
        if (!hit)
        {
            string manifestJson = null;
            yield return cache.ReadTextAsync(manifestPath, value => manifestJson = value, exception => readError = exception);
            if (readError != null) { failed?.Invoke(readError); yield break; }
            RecipeManifestDto manifest;
            string[] paths;
            try
            {
                manifest = DeserializeManifest(manifestJson);
                ValidateManifest(manifest);
                paths = manifest.Packages.Where(value => value.Enabled)
                    .Select(package => ResolvePackagePath(recipeRoot, package.Path)).ToArray();
            }
            catch (Exception exception) { failed?.Invoke(exception); yield break; }
            binary = new RecipeBinaryCatalog { Manifest = manifest };
            foreach (string path in paths)
            {
                string json = null;
                yield return cache.ReadTextAsync(path, value => json = value, exception => readError = exception);
                if (readError != null) { failed?.Invoke(readError); yield break; }
                RecipeCatalogDto parsed = null;
                yield return StreamingAssetsTextLoader.RunPureDataAsync(() => RecipeRuntimeFactory.DeserializeWithoutDocumentCache(json),
                    value => parsed = value, exception => readError = exception);
                if (readError != null) { failed?.Invoke(readError); yield break; }
                binary.Catalogs.Add(parsed);
            }
        }
        if (readError != null) { failed?.Invoke(readError); yield break; }
        completed?.Invoke(new RecipeCatalogPreparation
            { Cache = cache, Catalog = binary, Hit = hit, ManifestPath = manifestPath, InputMilliseconds = cache.ElapsedMilliseconds });
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

/// <summary>配方结构在构建时完成解析和规范化，启动只检查当前资源引用。</summary>
[MemoryPack.MemoryPackable]
public sealed partial class CompiledRecipePackage
{
    public RecipePackageDto Package;
    public List<RuntimeRecipe> Recipes;
}
