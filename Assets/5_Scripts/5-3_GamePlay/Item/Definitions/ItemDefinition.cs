using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>游戏本体 JSON 物品目录。</summary>
[Serializable]
public sealed class ItemDefinitionCatalogDto
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion = 1;

    [JsonProperty("items")]
    public List<ItemDefinitionDto> Items = new();
}

/// <summary>单个物品定义。parent 用于复用同类物品的公共配置。</summary>
[Serializable]
public sealed class ItemDefinitionDto
{
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("abstract")]
    public bool Abstract;

    [JsonProperty("parent")]
    public string Parent;

    [JsonProperty("shellPrefab")]
    public string ShellPrefab;

    /// <summary>仅供编辑器迁移/校验定位旧资源；运行时不会加载此 Prefab。</summary>
    [JsonProperty("sourcePrefab")]
    public string SourcePrefab;

    [JsonProperty("gameName")]
    public string GameName;

    [JsonProperty("description")]
    public string Description;

    [JsonProperty("durability")]
    public float? Durability;

    [JsonProperty("maxDurability")]
    public float? MaxDurability;

    [JsonProperty("amount")]
    public float? Amount;

    [JsonProperty("volume")]
    public float? Volume;

    [JsonProperty("canBePickedUp")]
    public bool? CanBePickedUp;

    [JsonProperty("tags")]
    public List<string> Tags;

    /// <summary>ItemData 中除公共快捷字段外的其余静态模板数据。</summary>
    [JsonProperty("itemData")]
    public JObject ItemData;

    [JsonProperty("visual")]
    public ItemVisualDefinitionDto Visual;

    /// <summary>稳定模块名 -> 模块定义。稳定名会直接成为 ModuleDataDic 的键。</summary>
    [JsonProperty("modules")]
    public Dictionary<string, ItemModuleDefinitionDto> Modules = new();
}

[Serializable]
public sealed class ItemVisualDefinitionDto
{
    [JsonProperty("rendererPath")]
    public string RendererPath;

    [JsonProperty("spriteAddress")]
    public string SpriteAddress;

    [JsonProperty("rendererLocalPosition")]
    public Vector3? RendererLocalPosition;

    [JsonProperty("rendererLocalEulerAngles")]
    public Vector3? RendererLocalEulerAngles;

    [JsonProperty("rendererLocalScale")]
    public Vector3? RendererLocalScale;

    [JsonProperty("color")]
    public Color? Color;

    [JsonProperty("flipX")]
    public bool? FlipX;

    [JsonProperty("flipY")]
    public bool? FlipY;

    [JsonProperty("sortingLayerName")]
    public string SortingLayerName;

    [JsonProperty("sortingOrder")]
    public int? SortingOrder;

    [JsonProperty("collider")]
    public ItemColliderDefinitionDto Collider;
}

[Serializable]
public sealed class ItemColliderDefinitionDto
{
    [JsonProperty("path")]
    public string Path;

    [JsonProperty("type")]
    public string Type;

    [JsonProperty("enabled")]
    public bool? Enabled;

    [JsonProperty("isTrigger")]
    public bool? IsTrigger;

    [JsonProperty("offset")]
    public Vector2? Offset;

    [JsonProperty("size")]
    public Vector2? Size;

    [JsonProperty("radius")]
    public float? Radius;

    [JsonProperty("direction")]
    public int? Direction;

    [JsonProperty("points")]
    public List<Vector2> Points;
}

[Serializable]
public sealed class ItemModuleDefinitionDto
{
    /// <summary>模块 Prefab ID；外壳已内置该模块时也使用同一个稳定 ID。</summary>
    [JsonProperty("prefab")]
    public string Prefab;

    /// <summary>玩法侧模块 ID；可与用于实例化的 Prefab 地址不同。</summary>
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("enabled")]
    public bool? Enabled;

    /// <summary>模块持久化数据；会写入克隆后的具体 ModuleData 子类。</summary>
    [JsonProperty("data")]
    public JObject Data;

    /// <summary>直接映射到 Module 的可序列化字段，支持 $transform 特殊块。</summary>
    [JsonProperty("parameters")]
    public JObject Parameters;
}

/// <summary>校验并解析后的不可变运行时物品定义。</summary>
public sealed class RuntimeItemDefinition
{
    private readonly ItemData templateData;
    private readonly Dictionary<string, string> moduleParameters;
    private readonly Dictionary<string, string> modulePrefabIds;

    public string Id { get; }
    public string ShellPrefabId { get; }
    public GameObject ShellPrefab { get; }
    public ItemVisualDefinitionDto Visual { get; }
    public string RendererPath => Visual?.RendererPath;
    public Sprite Sprite { get; }

    public RuntimeItemDefinition(
        string id,
        string shellPrefabId,
        GameObject shellPrefab,
        ItemData itemData,
        ItemVisualDefinitionDto visual,
        Sprite sprite,
        Dictionary<string, string> parameters,
        Dictionary<string, string> prefabIds)
    {
        Id = id;
        ShellPrefabId = shellPrefabId;
        ShellPrefab = shellPrefab;
        templateData = itemData;
        Visual = visual;
        Sprite = sprite;
        moduleParameters = parameters ?? new Dictionary<string, string>(StringComparer.Ordinal);
        modulePrefabIds = prefabIds ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public ItemData CreateItemData()
    {
        return FastCloner.FastCloner.DeepClone(templateData);
    }

    public bool TryGetModuleParameters(string stableModuleName, out string json)
    {
        return moduleParameters.TryGetValue(stableModuleName ?? string.Empty, out json);
    }

    public string GetModulePrefabId(string stableModuleName, string fallbackId)
    {
        return modulePrefabIds.TryGetValue(stableModuleName ?? string.Empty, out string prefabId) &&
               !string.IsNullOrWhiteSpace(prefabId)
            ? prefabId
            : fallbackId;
    }
}
