using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

#region 矿物力学参考值

/// <summary>矿石与矿脉共用的静态参考值，等级越高表示越耐划伤或越不易断裂。</summary>
public sealed class MineralPropertyReference
{
    public string Id { get; }
    public string ReferenceMaterial { get; }
    public int Hardness { get; }
    public int Toughness { get; }
    public IReadOnlyList<string> ItemIds { get; }

    internal MineralPropertyReference(string id, string referenceMaterial, int hardness, int toughness, string[] itemIds)
    {
        Id = id;
        ReferenceMaterial = referenceMaterial;
        Hardness = hardness;
        Toughness = toughness;
        ItemIds = Array.AsReadOnly(itemIds);
    }
}

/// <summary>独立的矿物参考目录，后续系统按物品 ID 查询，不参与现有采矿与伤害结算。</summary>
public sealed partial class MineralPropertyCatalog
{
    public const string RelativePath = "GameConfig/Items/mineral-properties.json";
    private readonly Dictionary<string, MineralPropertyReference> propertiesByItemId;
    public IReadOnlyList<MineralPropertyReference> Minerals { get; }

    private MineralPropertyCatalog(List<MineralPropertyReference> minerals,
        Dictionary<string, MineralPropertyReference> propertiesByItemId)
    {
        Minerals = minerals.AsReadOnly();
        this.propertiesByItemId = propertiesByItemId;
    }

    public bool TryGetByItemId(string itemId, out MineralPropertyReference properties)
    {
        properties = null;
        return !string.IsNullOrWhiteSpace(itemId) && propertiesByItemId.TryGetValue(itemId, out properties);
    }

    public static MineralPropertyCatalog LoadBuiltIn()
        => Deserialize(StreamingAssetsTextLoader.ReadAllText(
            StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativePath)));

    // 移动端与 WebGL 使用现有跨平台异步读取入口。
    public static IEnumerator LoadBuiltInAsync(Action<MineralPropertyCatalog> completed, Action<Exception> failed)
    {
        string json = null;
        Exception error = null;
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(
            StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativePath),
            text => json = text, exception => error = exception);
        MineralPropertyCatalog catalog = null;
        if (error == null)
        {
            try { catalog = Deserialize(json); }
            catch (Exception exception) { error = exception; }
        }
        if (error != null) failed?.Invoke(error);
        else completed?.Invoke(catalog);
    }

    public static MineralPropertyCatalog Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("矿物参考目录 JSON 不能为空。");
        CatalogDto source;
        try
        {
            source = JsonConfigurationCache.Deserialize<CatalogDto>(json, new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                ObjectCreationHandling = ObjectCreationHandling.Replace
            });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("矿物参考目录 JSON 格式无效。", exception);
        }

        if (source == null || source.SchemaVersion != 1 || source.Minerals == null || source.Minerals.Count == 0)
            throw new InvalidDataException("矿物参考目录必须使用 schemaVersion=1 并提供 minerals。");

        var minerals = new List<MineralPropertyReference>(source.Minerals.Count);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byItemId = new Dictionary<string, MineralPropertyReference>(StringComparer.OrdinalIgnoreCase);
        foreach (MineralDto entry in source.Minerals)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id.Trim()) ||
                string.IsNullOrWhiteSpace(entry.ReferenceMaterial) || entry.ItemIds == null || entry.ItemIds.Length == 0)
                throw new InvalidDataException("矿物参考项必须提供唯一 id、referenceMaterial 和非空 itemIds。");
            if (entry.Hardness < 1 || entry.Hardness > 10 || entry.Toughness < 1 || entry.Toughness > 10)
                throw new InvalidDataException($"矿物 {entry.Id} 的 hardness 和 toughness 必须是 1～10 的整数。");

            var itemIds = new string[entry.ItemIds.Length];
            for (int index = 0; index < itemIds.Length; index++)
            {
                itemIds[index] = entry.ItemIds[index]?.Trim();
                if (string.IsNullOrWhiteSpace(itemIds[index]))
                    throw new InvalidDataException($"矿物 {entry.Id} 存在空物品 ID。");
            }

            var properties = new MineralPropertyReference(entry.Id.Trim(), entry.ReferenceMaterial.Trim(),
                entry.Hardness, entry.Toughness, itemIds);
            foreach (string itemId in itemIds)
                if (!byItemId.TryAdd(itemId, properties))
                    throw new InvalidDataException($"物品 {itemId} 重复绑定了矿物参考值。");
            minerals.Add(properties);
        }
        return new MineralPropertyCatalog(minerals, byItemId);
    }

    [MemoryPack.MemoryPackable]
    [Serializable]
    private sealed partial class CatalogDto
    {
        [JsonProperty("schemaVersion")] public int SchemaVersion;
        [JsonProperty("notes")] public string Notes;
        [JsonProperty("sources")] public string[] Sources;
        [JsonProperty("minerals")] public List<MineralDto> Minerals;
    }

    [MemoryPack.MemoryPackable]
    [Serializable]
    private sealed partial class MineralDto
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("referenceMaterial")] public string ReferenceMaterial;
        [JsonProperty("hardness")] public int Hardness;
        [JsonProperty("toughness")] public int Toughness;
        [JsonProperty("itemIds")] public string[] ItemIds;
    }
}

#endregion
