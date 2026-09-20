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
        var manifest = TileDefinitionJson.Read<TileDefinitionManifestDto>(TileDefinitionJson.Parse(json));
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
            StreamingAssetsTextLoader.CombinePath(BuiltInRoot, path);
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !ids.Add(package.Id) || !paths.Add(path))
                throw new InvalidDataException($"地块分包 ID/路径重复或不是 JSON：{package.Id} / {path}");
        }
        return manifest;
    }
    #endregion

    #region 资源加载阶段
    public static IEnumerator LoadBuiltInAsync(GameRes resources, Action<int> completed, Action<Exception> failed)
    {
        if (resources == null) { failed?.Invoke(new ArgumentNullException(nameof(resources))); yield break; }
        string text = null;
        Exception readError = null;
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(BuiltInManifestPath, value => text = value, error => readError = error);
        if (readError != null) { failed?.Invoke(readError); yield break; }
        TileDefinitionManifestDto manifest;
        try { manifest = DeserializeManifest(text); }
        catch (Exception error) { failed?.Invoke(error); yield break; }

        var definitions = new List<RuntimeTileDefinition>();
        foreach (TileDefinitionPackageDto package in manifest.Packages)
        {
            if (!package.Enabled) continue;
            text = null;
            readError = null;
            yield return StreamingAssetsTextLoader.ReadAllTextAsync(
                StreamingAssetsTextLoader.CombinePath(BuiltInRoot, package.Path), value => text = value, error => readError = error);
            if (readError != null) { failed?.Invoke(readError); yield break; }
            try
            {
                foreach (TileDefinitionDto dto in TileDefinitionFactory.DeserializeCatalog(text).Tiles)
                    definitions.Add(TileDefinitionFactory.Build(dto, id => resources.tileBaseDict.TryGetValue(id, out var tile) ? tile : null));
            }
            catch (Exception error)
            {
                failed?.Invoke(new InvalidDataException($"地块分包 {package.Id}（{package.Path}）无效：{error.Message}", error));
                yield break;
            }
        }
        try
        {
            if (definitions.Count == 0) throw new InvalidDataException("地块清单没有加载任何定义。");
            TileDefinitionFactory.ValidateIdentities(definitions);
            resources.ReplaceTileDefinitions(definitions);
            Debug.Log($"[TileDefinitionCatalog] 已从 JSON 加载 {definitions.Count} 种地块；Behaviour 按定义共享。");
            completed?.Invoke(definitions.Count);
        }
        catch (Exception error) { failed?.Invoke(error); }
    }
    #endregion
}
