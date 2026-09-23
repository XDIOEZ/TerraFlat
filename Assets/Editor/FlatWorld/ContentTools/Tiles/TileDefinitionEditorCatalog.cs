using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 编辑器使用的只读 JSON 地块目录与显式保存入口。旧 SO 只保留 ID，结构编辑器通过委托解析本目录；
/// 不创建 GameRes、不进入世界、不读取玩家存档，保存时仅更新 JSON 唯一真源。
/// </summary>
[InitializeOnLoad]
public static class TileDefinitionEditorCatalog
{
    #region 只读目录
    public const string Root = "Assets/StreamingAssets/GameConfig/Tiles";
    private static Dictionary<string, RuntimeTileDefinition> definitions;
    private static Dictionary<string, string> sourcePaths;
    private static Dictionary<string, TileBase> tiles;

    static TileDefinitionEditorCatalog()
    {
        Tile_Block.EditorDefinitionResolver = Get;
        EditorApplication.playModeStateChanged += _ => Invalidate();
    }

    public static void Invalidate()
    {
        definitions = null;
        sourcePaths = null;
        tiles = null;
    }

    public static IReadOnlyDictionary<string, RuntimeTileDefinition> Definitions
    {
        get { EnsureLoaded(); return definitions; }
    }

    public static RuntimeTileDefinition Get(string id)
    {
        EnsureLoaded();
        return definitions.TryGetValue(id, out var definition) ? definition :
            throw new InvalidDataException($"地块 {id} 不在 JSON 清单中：{Root}");
    }

    public static string GetSourcePath(string id)
    {
        EnsureLoaded();
        return sourcePaths.TryGetValue(id, out string path) ? path : null;
    }

    /// <summary>按 Addressables TileBase 标签解析资源名，包含已标记的文件夹资源。</summary>
    private static Dictionary<string, TileBase> LoadTiles()
    {
        var result = new Dictionary<string, TileBase>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) throw new InvalidDataException("缺少 Addressables Settings。");
        foreach (var group in settings.groups)
        {
            if (group == null) continue;
            foreach (var entry in group.entries)
            {
                if (!entry.labels.Contains("TileBase")) continue;
                if (AssetDatabase.IsValidFolder(entry.AssetPath))
                {
                    foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { entry.AssetPath }))
                        paths.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
                else paths.Add(entry.AssetPath);
            }
        }
        foreach (string path in paths)
        {
            TileBase tile = AssetDatabase.LoadAssetAtPath<TileBase>(path);
            if (tile == null) continue;
            if (!result.TryAdd(tile.name, tile)) throw new InvalidDataException($"TileBase 资源名重复：{tile.name}（{path}）");
        }
        return result;
    }

    private static void EnsureLoaded()
    {
        if (definitions != null) return;
        var loadedTiles = LoadTiles();
        var loadedDefinitions = new Dictionary<string, RuntimeTileDefinition>(StringComparer.OrdinalIgnoreCase);
        var loadedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var manifest = TileDefinitionCatalogLoader.DeserializeManifest(File.ReadAllText(Root + "/tile-manifest.json"));
        foreach (TileDefinitionPackageDto package in manifest.Packages)
        {
            if (!package.Enabled) continue;
            string path = StreamingAssetsTextLoader.CombinePath(Root, package.Path).Replace('\\', '/');
            foreach (TileDefinitionDto dto in TileDefinitionFactory.DeserializeCatalog(File.ReadAllText(path)).Tiles)
            {
                RuntimeTileDefinition definition = TileDefinitionFactory.Build(dto,
                    id => loadedTiles.TryGetValue(id, out var tile) ? tile : null);
                if (!loadedDefinitions.TryAdd(dto.Id, definition)) throw new InvalidDataException($"重复地块 {dto.Id}：{path}");
                loadedPaths.Add(dto.Id, path);
            }
        }
        TileDefinitionFactory.ValidateIdentities(loadedDefinitions.Values);
        tiles = loadedTiles;
        sourcePaths = loadedPaths;
        definitions = loadedDefinitions;
    }
    #endregion

    #region 显式保存
    /// <summary>资源装配工具更新单个地块的 JSON；新地块放入清单已声明的 buildings.json。</summary>
    public static void SaveDefinition(TileDefinitionDto dto)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出播放模式再编辑地块 JSON。");
        EnsureLoaded();
        Dictionary<string, TileBase> currentTiles = LoadTiles();
        RuntimeTileDefinition built = TileDefinitionFactory.Build(dto,
            id => currentTiles.TryGetValue(id, out var tile) ? tile : null);
        var candidate = new Dictionary<string, RuntimeTileDefinition>(definitions, StringComparer.OrdinalIgnoreCase) { [dto.Id] = built };
        if (definitions.TryGetValue(dto.Id, out var previous) && previous.RuntimeTileId != dto.RuntimeTileId)
            throw new InvalidDataException($"不能通过资源装配改变已发布地块 {dto.Id} 的数字 ID。");
        TileDefinitionFactory.ValidateIdentities(candidate.Values);
        string path = sourcePaths.TryGetValue(dto.Id, out string existing) ? existing : Root + "/buildings.json";
        JObject document = TileDefinitionJson.Parse(File.ReadAllText(path));
        var entries = (JArray)document["tiles"];
        JToken old = entries.FirstOrDefault(entry => string.Equals(entry.Value<string>("id"), dto.Id, StringComparison.OrdinalIgnoreCase));
        if (old != null) old.Replace(built.CopySource());
        else entries.Add(built.CopySource());
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, document.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
            File.Replace(temporary, path, null);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Invalidate();
        AssetDatabase.ImportAsset(path);
    }
    #endregion

    #region 静态诊断
    /// <summary>只在内存中检查 JSON 拒绝边界、工厂租约和克隆隔离，不创建世界或更改资源目录。</summary>
    public static int ValidateFactoryBoundaries()
    {
        var diagnosticSource = Get("Tile_Sand").CopySource();
        diagnosticSource["id"] = "flatworld.diagnostics:tile";
        diagnosticSource["behaviours"] = new JArray(new JObject
        {
            ["type"] = "universal",
            ["parameters"] = new JObject { ["buffInfo"] = new JArray() }
        });
        RuntimeTileDefinition diagnostic = BuildSource(diagnosticSource);
        int checks = 0;
        var invalid = diagnostic.CopySource();
        invalid["behaviours"][0]["type"] = "flatworld.diagnostics:missing";
        checks += ExpectRejected(() => BuildSource(invalid));
        var unknown = diagnostic.CopySource();
        unknown["behaviours"][0]["parameters"]["typo"] = 1;
        checks += ExpectRejected(() => BuildSource(unknown));
        var negative = diagnostic.CopySource();
        negative["data"]["parameters"]["demolitionTime"] = -1;
        checks += ExpectRejected(() => BuildSource(negative));
        var identity = diagnostic.CopySource();
        identity["runtimeTileId"] = -1;
        checks += ExpectRejected(() => BuildSource(identity));
        var position = diagnostic.CopySource();
        position["data"]["parameters"]["position"] = new JObject();
        checks += ExpectRejected(() => BuildSource(position));
        var penalty = diagnostic.CopySource();
        penalty["data"]["parameters"]["penalty"] = 40000;
        checks += ExpectRejected(() => BuildSource(penalty));
        var metadata = diagnostic.CopySource();
        metadata["$type"] = "NotAllowed";
        checks += ExpectRejected(() => BuildSource(metadata));
        checks += ExpectRejected(() => TileDefinitionJson.Parse("{\"id\":1,\"id\":2}"));
        var asset = diagnostic.CopySource();
        asset["tileAsset"] = "flatworld.diagnostics:missing";
        checks += ExpectRejected(() => BuildSource(asset));
        checks += ExpectRejected(() => TileDefinitionFactory.ValidateIdentities(new[] { diagnostic, diagnostic }));
        var other = diagnostic.CopySource();
        other["id"] = "flatworld.diagnostics:collision";
        RuntimeTileDefinition collision = BuildSource(other);
        checks += ExpectRejected(() => TileDefinitionFactory.ValidateIdentities(new[] { diagnostic, collision }));

        var custom = diagnostic.CopySource();
        custom["behaviours"][0]["type"] = "flatworld.diagnostics:empty";
        custom["behaviours"][0]["parameters"] = new JObject();
        using (TileBehaviourRegistry.RegisterBehaviour("flatworld.diagnostics:empty", _ => new Tile_Universal()))
        {
            if (BuildSource(custom).Behaviours[0] is not Tile_Universal)
                throw new InvalidDataException("地块自定义行为工厂未生效。");
            checks++;
        }
        checks += ExpectRejected(() => BuildSource(custom));
        TileData first = diagnostic.CreateTileData();
        TileData second = diagnostic.CreateTileData();
        first.Name = "mutated";
        if (second.Name == "mutated" || ReferenceEquals(first, second) ||
            diagnostic.TileDataTemplate.Name == "mutated")
            throw new InvalidDataException("地块 Clone 泄漏共享状态。");
        return checks + 1;
    }

    private static RuntimeTileDefinition BuildSource(JObject source) => TileDefinitionFactory.Build(
        TileDefinitionFactory.DeserializeDefinition(source), id => tiles.TryGetValue(id, out var tile) ? tile : null);

    /// <summary>预期错误必须来自配置校验；空引用等代码异常不能冒充校验成功。</summary>
    private static int ExpectRejected(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return 1; }
        catch (JsonException) { return 1; }
        throw new InvalidDataException("非法地块配置未被拒绝。");
    }

    [MenuItem("FlatWorld/诊断/检查地块 JSON 目录")]
    public static void ValidateCatalog()
    {
        Invalidate();
        EnsureLoaded();
        var errors = new List<string>();
        BuildingResourceCatalogValidator.ValidateTiles(new Dictionary<string, string>(), definitions, errors);
        foreach (RuntimeTileDefinition definition in definitions.Values)
        {
            TileData first = definition.CreateTileData();
            TileData second = definition.CreateTileData();
            if (ReferenceEquals(first, second) || ReferenceEquals(first, definition.TileDataTemplate))
                errors.Add($"地块 {definition.Id} 的 Clone 泄漏共享模板。");
            if (!ReferenceEquals(Get(definition.Id).Behaviours, definition.Behaviours))
                errors.Add($"地块 {definition.Id} 没有共享 Behaviour 集合。");
        }
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
        int boundaries = ValidateFactoryBoundaries();
        Debug.Log($"[TileDefinitionCatalog] 静态校验通过：{definitions.Count} 个 JSON 定义、{definitions.Values.Sum(d => d.Behaviours.Count)} 个共享行为、{boundaries} 项边界检查，资源引用与稳定编号一致。");
    }
    #endregion
}

/// <summary>配置或资源导入后废弃编辑器缓存，禁止运行时或编辑器继续读取旧定义。</summary>
public sealed class TileDefinitionCatalogPostprocessor : AssetPostprocessor
{
    #region 缓存失效
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] oldPaths)
    {
        if (imported.Concat(deleted).Concat(moved).Concat(oldPaths).Any(path =>
            path.StartsWith(TileDefinitionEditorCatalog.Root, StringComparison.Ordinal) ||
            path.StartsWith("Assets/7_Tiles/", StringComparison.Ordinal) ||
            path.StartsWith("Assets/AddressableAssetsData/", StringComparison.Ordinal)))
            TileDefinitionEditorCatalog.Invalidate();
    }
    #endregion
}

/// <summary>旧地块引用壳不再显示可编辑数值，直接引导到对应的 JSON 配置文件。</summary>
[CustomEditor(typeof(Tile_Block))]
public sealed class TileDefinitionReferenceEditor : Editor
{
    #region 引用面板
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("tileItemName"));
        serializedObject.ApplyModifiedProperties();
        EditorGUILayout.HelpBox("此资源仅保存地块 ID；数值、行为与外观资源键统一在 JSON 中配置。", MessageType.Info);
        if (GUILayout.Button("打开地块 JSON"))
        {
            string path = TileDefinitionEditorCatalog.GetSourcePath(((Tile_Block)target).DefinitionId);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<TextAsset>(path));
        }
    }
    #endregion
}
