using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

#region JSON 定义

/// <summary>
/// 战利品表 JSON 根目录。表以稳定 ID 注册，Item/Actor 只保存引用 ID；每个条目独立判定概率并生成随机数量。
/// </summary>
[Serializable]
public sealed class LootTableCatalogDto
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion = 1;

    [JsonProperty("lootTables")]
    public List<LootTableDefinitionDto> LootTables = new();
}

/// <summary>一张可复用的战利品表定义。</summary>
[Serializable]
public sealed class LootTableDefinitionDto
{
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("entries")]
    public List<LootTableEntryDefinitionDto> Entries = new();
}

/// <summary>单项掉落规则；itemId 使用 ItemDefinition 的稳定 ID。</summary>
[Serializable]
public sealed class LootTableEntryDefinitionDto
{
    [JsonProperty("itemId")]
    public string ItemId;

    [JsonProperty("probability")]
    public float Probability = 1f;

    [JsonProperty("minAmount")]
    public int MinAmount = 1;

    [JsonProperty("maxAmount")]
    public int MaxAmount = 1;
}

#endregion

#region 运行时目录

/// <summary>校验后的不可变掉落条目。</summary>
public sealed class RuntimeLootTableEntry
{
    public string ItemId { get; }
    public float Probability { get; }
    public int MinAmount { get; }
    public int MaxAmount { get; }

    public RuntimeLootTableEntry(string itemId, float probability, int minAmount, int maxAmount)
    {
        ItemId = itemId;
        Probability = probability;
        MinAmount = minAmount;
        MaxAmount = maxAmount;
    }
}

/// <summary>
/// 校验后的不可变战利品表。运行时按需生成 DamageReceiver 参数，避免共享可变 LootEntry 实例。
/// </summary>
public sealed class RuntimeLootTable
{
    private readonly RuntimeLootTableEntry[] entries;

    public string Id { get; }
    public IReadOnlyList<RuntimeLootTableEntry> Entries => entries;

    public RuntimeLootTable(string id, RuntimeLootTableEntry[] entries)
    {
        Id = id;
        this.entries = entries ?? Array.Empty<RuntimeLootTableEntry>();
    }

    /// <summary>转换为 DamageReceiver.Data.LootTable 的严格 JSON 参数。</summary>
    public JArray CreateDamageReceiverEntries()
    {
        var result = new JArray();
        foreach (RuntimeLootTableEntry entry in entries)
        {
            result.Add(new JObject
            {
                ["LootPrefabName"] = entry.ItemId,
                ["DropChance"] = entry.Probability,
                ["MinAmount"] = entry.MinAmount,
                ["MaxAmount"] = entry.MaxAmount
            });
        }

        return result;
    }
}

#endregion

#region 加载器

/// <summary>
/// 从 StreamingAssets 加载本体战利品库；同步与异步入口共用同一套严格校验，支持编辑器与 Android/JAR 路径。
/// </summary>
public static class LootTableCatalogLoader
{
    public const int SupportedSchemaVersion = 1;
    public const string RelativeCatalogPath = "GameConfig/LootTables/loot-tables.json";

    public static string BuiltInCatalogPath =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeCatalogPath);

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        ObjectCreationHandling = ObjectCreationHandling.Replace
    };

    /// <summary>同步加载本体战利品库。</summary>
    public static IReadOnlyList<RuntimeLootTable> LoadBuiltIn()
    {
        return DeserializeCatalog(StreamingAssetsTextLoader.ReadAllText(BuiltInCatalogPath));
    }

    /// <summary>异步加载本体战利品库。</summary>
    public static IEnumerator LoadBuiltInAsync(
        Action<IReadOnlyList<RuntimeLootTable>> completed,
        Action<Exception> failed)
    {
        string json = null;
        Exception readError = null;
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(
            BuiltInCatalogPath,
            text => json = text,
            exception => readError = exception);

        if (readError != null)
        {
            failed?.Invoke(new IOException($"战利品库读取失败：{BuiltInCatalogPath}", readError));
            yield break;
        }

        try
        {
            completed?.Invoke(DeserializeCatalog(json));
        }
        catch (Exception exception)
        {
            failed?.Invoke(exception);
        }
    }

    /// <summary>解析并校验目录，任何无效配置都在资源初始化阶段直接失败。</summary>
    public static IReadOnlyList<RuntimeLootTable> DeserializeCatalog(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("战利品库为空");

        LootTableCatalogDto catalog;
        try
        {
            catalog = JsonConvert.DeserializeObject<LootTableCatalogDto>(json, SerializerSettings);
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("战利品库 JSON 无法解析", exception);
        }

        if (catalog == null)
            throw new InvalidDataException("战利品库根对象为空");
        if (catalog.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException($"不支持的战利品库 schemaVersion：{catalog.SchemaVersion}");
        if (catalog.LootTables == null || catalog.LootTables.Count == 0)
            throw new InvalidDataException("战利品库没有 lootTables");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<RuntimeLootTable>(catalog.LootTables.Count);
        foreach (LootTableDefinitionDto table in catalog.LootTables)
        {
            if (table == null || string.IsNullOrWhiteSpace(table.Id))
                throw new InvalidDataException("战利品库包含空表或空 ID");

            string tableId = table.Id.Trim();
            if (!ids.Add(tableId))
                throw new InvalidDataException($"战利品表 ID 重复：{tableId}");
            if (table.Entries == null || table.Entries.Count == 0)
                throw new InvalidDataException($"战利品表 {tableId} 没有 entries");

            var itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new RuntimeLootTableEntry[table.Entries.Count];
            for (int i = 0; i < table.Entries.Count; i++)
            {
                LootTableEntryDefinitionDto entry = table.Entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.ItemId))
                    throw new InvalidDataException($"战利品表 {tableId} 的条目 {i} 缺少 itemId");

                string itemId = entry.ItemId.Trim();
                if (!itemIds.Add(itemId))
                    throw new InvalidDataException($"战利品表 {tableId} 重复声明物品：{itemId}");
                if (entry.Probability < 0f || entry.Probability > 1f)
                    throw new InvalidDataException($"战利品表 {tableId}/{itemId} 的 probability 必须在 0 到 1 之间");
                if (entry.MinAmount < 1 || entry.MaxAmount < entry.MinAmount)
                    throw new InvalidDataException($"战利品表 {tableId}/{itemId} 的数量范围无效");

                entries[i] = new RuntimeLootTableEntry(
                    itemId,
                    entry.Probability,
                    entry.MinAmount,
                    entry.MaxAmount);
            }

            result.Add(new RuntimeLootTable(tableId, entries));
        }

        return result;
    }
}

#endregion
