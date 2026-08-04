using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// JSON Buff 目录。DTO 只包含数据，不引用 Unity 对象或 C# 行为类型。
/// </summary>
[Serializable]
public sealed class BuffCatalogDto
{
    [JsonProperty("schemaVersion", Required = Required.Always)]
    public int SchemaVersion = 1;

    [JsonProperty("buffs", Required = Required.Always)]
    public List<BuffDefinitionDto> Buffs = new();
}

/// <summary>本体 Buff 分包清单；按声明顺序加载启用的 JSON 文件。</summary>
[Serializable]
public sealed class BuffManifestDto
{
    [JsonProperty("schemaVersion", Required = Required.Always)]
    public int SchemaVersion = 1;

    [JsonProperty("packages", Required = Required.Always)]
    public List<BuffPackageDto> Packages = new();
}

[Serializable]
public sealed class BuffPackageDto
{
    [JsonProperty("id", Required = Required.Always)]
    public string Id;

    [JsonProperty("path", Required = Required.Always)]
    public string Path;

    [JsonProperty("enabled")]
    public bool Enabled = true;
}

[Serializable]
public sealed class BuffDefinitionDto
{
    [JsonProperty("id", Required = Required.Always)]
    public string Id;

    [JsonProperty("displayName")]
    public string DisplayName;

    [JsonProperty("category")]
    public string Category = "general";

    [JsonProperty("description")]
    public string Description;

    [JsonProperty("labelKey")]
    public string LabelKey;

    [JsonProperty("descriptionKey")]
    public string DescriptionKey;

    [JsonProperty("durationSeconds")]
    public float? DurationSeconds;

    [JsonProperty("tickIntervalSeconds")]
    public float TickIntervalSeconds;

    [JsonProperty("stackMode")]
    public string StackMode = "ignore";

    [JsonProperty("drinkDurationExtensionSeconds")]
    public float DrinkDurationExtensionSeconds;

    [JsonProperty("effects", Required = Required.Always)]
    public List<BuffEffectDto> Effects = new();
}

[Serializable]
public sealed class BuffEffectDto
{
    [JsonProperty("phase", Required = Required.Always)]
    public string Phase;

    [JsonProperty("typeId", Required = Required.Always)]
    public string TypeId;

    [JsonProperty("targetId")]
    public string TargetId;

    [JsonProperty("requiredTag")]
    public string RequiredTag;

    [JsonProperty("value")]
    public float Value;

}
