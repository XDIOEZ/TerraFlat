using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 按清单跨平台读取本体地块 JSON，先完整构建和校验，再一次发布到 GameRes。
/// 复用 StreamingAssetsTextLoader，兼容 Android APK 内资源；不再加载 TileBlock SO 标签。
/// </summary>
public static class TileDefinitionCatalogLoader
{
    #region 路径与清单
    public const string RelativeRoot = "GameConfig/Tiles";
    public const string ManifestFileName = "tile-manifest.json";
    public static string BuiltInRoot => StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeRoot);
    public static string BuiltInManifestPath => StreamingAssetsTextLoader.CombinePath(BuiltInRoot, ManifestFileName);

    public static TileDefinitionManifestDto DeserializeManifest(string json)
    {
        return JsonConfigurationCache.Parse(json, () => DeserializeManifestUncached(json));
    }

    private static BinaryContentCache CreateBinaryCache()
        => BinaryContentCache.ForBuiltIn<List<TileDefinitionDto>>("tile-definitions", BuiltInRoot, BuiltInManifestPath, 1);

    private static TileDefinitionManifestDto DeserializeManifestUncached(string json, string root = null)
    {
        var manifest = TileDefinitionJson.Read<TileDefinitionManifestDto>(TileDefinitionJson.Parse(json));
        ValidateManifest(manifest, root ?? BuiltInRoot);
        return manifest;
    }

    private static void ValidateManifest(TileDefinitionManifestDto manifest, string root)
    {
        if (manifest.SchemaVersion != TileDefinitionFactory.SupportedSchemaVersion || manifest.Packages == null || manifest.Packages.Count == 0)
            throw new InvalidDataException("地块清单版本无效或 packages 为空。");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TileDefinitionPackageDto package in manifest.Packages)
        {
            if (package == null) throw new InvalidDataException("地块清单包含空分包。");
            TileDefinitionFactory.ValidateId(package.Id, "地块分包 id");
            if (string.IsNullOrWhiteSpace(package.Path)) throw new InvalidDataException($"地块分包 {package.Id} 缺少 path。");
            string path = package.Path.Replace('\\', '/');
            StreamingAssetsTextLoader.CombinePath(root, path);
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !ids.Add(package.Id) || !paths.Add(path))
                throw new InvalidDataException($"地块分包 ID/路径重复或不是 JSON：{package.Id} / {path}");
        }
    }
    #endregion

    #region 资源加载阶段

    /// <summary>构建期只收集纯地块定义，不创建运行时 Tile 或资源句柄。</summary>
    [BuiltInContentBake]
    public static void BakeBuiltInConfiguration()
        => LoadLocalCatalogInput(CreateBinaryCache(), BuiltInRoot, BuiltInManifestPath);

    internal sealed class TileCatalogPreparation
    {
        internal BinaryContentCache Cache;
        internal List<TileDefinitionDto> Definitions;
    }

    internal static IEnumerator LoadBuiltInCatalogInputAsync(Action<TileCatalogPreparation> completed, Action<Exception> failed)
    {
        BinaryContentCache cache = CreateBinaryCache();
        string root = BuiltInRoot, manifest = BuiltInManifestPath;
        return StreamingAssetsTextLoader.RequiresWebRequest(root)
            ? LoadPackagedCatalogInputAsync(cache, completed, failed)
            : StreamingAssetsTextLoader.BeginPureDataAsync(() => LoadLocalCatalogInput(cache, root, manifest), completed, failed);
    }

    internal static TileCatalogPreparation LoadLocalCatalogInput(BinaryContentCache cache, string root, string manifestPath)
    {
        if (!cache.TryRead(out List<TileDefinitionDto> values))
        {
            TileDefinitionManifestDto manifest = DeserializeManifestUncached(cache.ReadText(manifestPath), root);
            values = new List<TileDefinitionDto>();
            foreach (TileDefinitionPackageDto package in manifest.Packages)
            {
                if (!package.Enabled) continue;
                string path = StreamingAssetsTextLoader.CombinePath(root, package.Path);
                try { values.AddRange(TileDefinitionFactory.DeserializeCatalogUncached(cache.ReadText(path)).Tiles); }
                catch (Exception error) { throw new InvalidDataException($"地块分包 {package.Id}（{package.Path}）无效：{error.Message}", error); }
            }
            if (values.Count == 0) throw new InvalidDataException("地块清单没有加载任何定义。");
            cache.Store(values);
        }
        return new TileCatalogPreparation { Cache = cache, Definitions = values };
    }

    public static IEnumerator LoadBuiltInAsync(GameRes resources, Action<int> completed, Action<Exception> failed)
        => LoadBuiltInPreparedAsync(resources, completed, failed, null);

    private static IEnumerator LoadPackagedCatalogInputAsync(BinaryContentCache cache,
        Action<TileCatalogPreparation> completed, Action<Exception> failed)
    {
        List<TileDefinitionDto> tileDtos = null;
        yield return cache.TryReadAsync<List<TileDefinitionDto>>(value => tileDtos = value);

        Exception readError = null;
        if (tileDtos == null)
        {
            string text = null;
            readError = null;
            yield return cache.ReadTextAsync(BuiltInManifestPath, value => text = value, error => readError = error);
            if (readError != null) { failed?.Invoke(readError); yield break; }

            TileDefinitionManifestDto manifest = null;
            string root = BuiltInRoot;
            yield return StreamingAssetsTextLoader.RunPureDataAsync(
                () => DeserializeManifestUncached(text, root), value => manifest = value, error => readError = error);
            if (readError != null) { failed?.Invoke(readError); yield break; }

            tileDtos = new List<TileDefinitionDto>();
            foreach (TileDefinitionPackageDto package in manifest.Packages)
            {
                if (!package.Enabled) continue;
                text = null;
                readError = null;
                string path = StreamingAssetsTextLoader.CombinePath(BuiltInRoot, package.Path);
                yield return cache.ReadTextAsync(path, value => text = value, error => readError = error);
                if (readError != null) { failed?.Invoke(readError); yield break; }

                TileDefinitionCatalogDto catalog = null;
                yield return StreamingAssetsTextLoader.RunPureDataAsync(
                    () => TileDefinitionFactory.DeserializeCatalogUncached(text),
                    value => catalog = value, error => readError = error);
                if (readError != null)
                {
                    failed?.Invoke(new InvalidDataException(
                        $"地块分包 {package.Id}（{package.Path}）无效：{readError.Message}", readError));
                    yield break;
                }
                tileDtos.AddRange(catalog.Tiles);
            }

            if (tileDtos.Count == 0) { failed?.Invoke(new InvalidDataException("地块清单没有加载任何定义。")); yield break; }
            yield return cache.StoreAsync(tileDtos);
        }

        completed(new TileCatalogPreparation { Cache = cache, Definitions = tileDtos });
    }

    internal static IEnumerator LoadBuiltInPreparedAsync(GameRes resources, Action<int> completed,
        Action<Exception> failed, TileCatalogPreparation prepared)
    {
        if (resources == null) { failed?.Invoke(new ArgumentNullException(nameof(resources))); yield break; }
        Exception readError = null;
        if (prepared == null)
            yield return LoadBuiltInCatalogInputAsync(value => prepared = value, error => readError = error);
        if (readError != null) { failed?.Invoke(readError); yield break; }
        List<TileDefinitionDto> tileDtos = prepared.Definitions;
        BinaryContentCache cache = prepared.Cache;

        var definitions = new List<RuntimeTileDefinition>(tileDtos.Count);
        try
        {
            if (tileDtos.Count == 0) throw new InvalidDataException("地块清单没有加载任何定义。");
            foreach (TileDefinitionDto dto in tileDtos)
                definitions.Add(TileDefinitionFactory.Build(dto,
                    id => resources.tileBaseDict.TryGetValue(id, out var tile) ? tile : null));
            TileDefinitionFactory.ValidateIdentities(definitions);
            resources.ReplaceTileDefinitions(definitions);
            Debug.Log($"[TileDefinitionCatalog] 已从 JSON 加载 {definitions.Count} 种地块；Behaviour 按定义共享。");
            cache.Report("地块定义", definitions.Count);
            completed?.Invoke(definitions.Count);
        }
        catch (Exception error) { failed?.Invoke(error); }
    }
    #endregion
}
