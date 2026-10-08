using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>资源会话级流体目录，本体和 MOD 使用相同 DTO、校验与引用检查。</summary>
public sealed class FluidCatalog
{
    #region 稳定身份目录
    private readonly Dictionary<string, FluidDefinition> definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FluidDefinition> byLiquid = new(StringComparer.OrdinalIgnoreCase);
    private static FluidCatalog current = new();
    public static FluidCatalog Default => current;
    public IReadOnlyDictionary<string, FluidDefinition> Definitions => new ReadOnlyDictionary<string, FluidDefinition>(definitions);
    public bool TryGet(string id, out FluidDefinition definition)
    { definition = null; return !string.IsNullOrWhiteSpace(id) && definitions.TryGetValue(id.Trim(), out definition); }
    public FluidDefinition Find(string id) => TryGet(id, out var result) ? result :
        throw new InvalidDataException($"流体定义不存在：{id}");
    public bool TryFromLiquidId(string liquidId, out FluidDefinition definition)
    { definition = null; return !string.IsNullOrWhiteSpace(liquidId) && byLiquid.TryGetValue(liquidId.Trim(), out definition); }
    public void Register(IEnumerable<FluidDefinition> values, string ownerModId = null, Func<string, bool> liquidExists = null)
    {
        var candidates = new List<FluidDefinition>(values ?? throw new ArgumentNullException(nameof(values)));
        var ids = new HashSet<string>(definitions.Keys, StringComparer.OrdinalIgnoreCase);
        var liquids = new HashSet<string>(byLiquid.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var value in candidates)
        {
            if (value == null || !ids.Add(value.Id)) throw new InvalidDataException($"流体身份冲突：{value?.Id}");
            if (!string.IsNullOrEmpty(ownerModId) && !value.Id.StartsWith(ownerModId + ":", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"MOD {ownerModId} 的流体必须使用自己的命名空间：{value.Id}");
            if (value.LiquidId == null) continue;
            if (!liquids.Add(value.LiquidId)) throw new InvalidDataException($"液体 {value.LiquidId} 重复映射到多个工业物质");
            if (liquidExists != null && !liquidExists(value.LiquidId))
                throw new InvalidDataException($"流体 {value.Id} 引用了未知 LiquidId：{value.LiquidId}");
        }
        foreach (var value in candidates)
        { definitions.Add(value.Id, value); if (value.LiquidId != null) byLiquid.Add(value.LiquidId, value); }
    }
    public void Unregister(FluidDefinition definition)
    {
        if (definition == null || !definitions.TryGetValue(definition.Id, out var currentValue) || !ReferenceEquals(currentValue, definition)) return;
        definitions.Remove(definition.Id);
        if (definition.LiquidId != null) byLiquid.Remove(definition.LiquidId);
    }
    public static void Replace(FluidCatalog value) => current = value ?? throw new ArgumentNullException(nameof(value));
    public static void Reset() => current = new FluidCatalog();
    internal static void ConfigureResourceReload(ResourceReloadContext context) =>
        context.Add(() => current, value => current = value, new FluidCatalog());
    #endregion
}

[Serializable]
public sealed class FluidContentCatalogDto
{
    [JsonProperty("schemaVersion", Required = Required.Always)] public int SchemaVersion;
    [JsonProperty("fluids", Required = Required.Always)] public List<FluidDefinitionDto> Fluids;
    [JsonProperty("atmospheres")] public List<AtmosphereDefinitionDto> Atmospheres = new();
}

/// <summary>纯解析不注册内容，全部构建成功后才发布本体目录。</summary>
public static class FluidCatalogLoader
{
    #region 本体目录加载
    public const string RelativePath = "GameConfig/Fluids/fluids.json";
    public static FluidContentCatalogDto Deserialize(string json)
    {
        var result = JsonConvert.DeserializeObject<FluidContentCatalogDto>(json, Settings);
        if (result == null || result.SchemaVersion != 1 || result.Fluids == null)
            throw new InvalidDataException("流体目录 schemaVersion 必须为 1 且具有 fluids 数组");
        return result;
    }
    public static readonly JsonSerializerSettings Settings = new()
    { MissingMemberHandling = MissingMemberHandling.Error, FloatParseHandling = FloatParseHandling.Decimal };
    public static FluidDefinitionDto DeserializeDefinition(JToken token) =>
        token?.ToObject<FluidDefinitionDto>(JsonSerializer.Create(Settings)) ?? throw new InvalidDataException("流体定义不能为空");
    public static IEnumerator LoadBuiltInAsync(GameRes resources, Action<int> completed, Action<Exception> failed)
    {
        string json = null; Exception error = null;
        string path = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativePath);
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(path, value => json = value, value => error = value);
        if (error != null) { failed?.Invoke(new IOException($"流体目录读取失败：{path}", error)); yield break; }
        try
        {
            FluidContentCatalogDto source = Deserialize(json);
            var catalog = new FluidCatalog(); var values = new List<FluidDefinition>();
            foreach (var dto in source.Fluids) values.Add(new FluidDefinition(dto));
            catalog.Register(values, null, id => resources != null && resources.TryGetLiquidDefinition(id, out _));
            foreach (var value in values)
                if (value.LiquidId != null && resources.TryGetLiquidDefinition(value.LiquidId, out var liquid) &&
                    value.LitersPerServing != (decimal)liquid.LitersPerServing)
                    throw new InvalidDataException($"流体 {value.Id} 与液体 {value.LiquidId} 的 litersPerServing 冲突");
            var atmosphereCatalog = new AtmosphereCatalog();
            atmosphereCatalog.Register(source.Atmospheres, catalog);
            FluidCatalog.Replace(catalog); AtmosphereCatalog.Replace(atmosphereCatalog);
            completed?.Invoke(values.Count);
        }
        catch (Exception exception) { failed?.Invoke(exception); }
    }
    #endregion
}
