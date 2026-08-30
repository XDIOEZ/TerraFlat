using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// JSON 配方目录；只保存可序列化数据，不包含 Unity 对象引用或行为实现。
/// </summary>
[Serializable]
public sealed class RecipeCatalogDto
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion = 1;

    [JsonProperty("recipes")]
    public List<RecipeDto> Recipes = new List<RecipeDto>();
}

/// <summary>
/// 配方分包清单；按顺序加载启用的业务配方文件。
/// </summary>
[Serializable]
public sealed class RecipeManifestDto
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion = 1;

    [JsonProperty("packages")]
    public List<RecipePackageDto> Packages = new List<RecipePackageDto>();
}

[Serializable]
public sealed class RecipePackageDto
{
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("path")]
    public string Path;

    [JsonProperty("enabled")]
    public bool Enabled = true;
}

/// <summary>
/// 单条 JSON 配方定义。
/// </summary>
[Serializable]
public sealed class RecipeDto
{
    /// <summary>仅用于编辑器按业务分包聚合与维护，不写入单个配方包 JSON。</summary>
    [JsonIgnore]
    public string Package = "crafting/survival";

    [JsonProperty("id")]
    public string Id;

    [JsonProperty("displayName")]
    public string DisplayName;

    [JsonProperty("recipeType")]
    public string RecipeType = "crafting";

    [JsonProperty("inputRule")]
    public string InputRule = "unordered";

    [JsonProperty("gridWidth")]
    public int GridWidth;

    [JsonProperty("gridHeight")]
    public int GridHeight;

    [JsonProperty("allowMirror")]
    public bool AllowMirror;

    [JsonProperty("temperature")]
    public float Temperature;

    [JsonProperty("maxTemperature")]
    public float MaxTemperature = 2000f;

    [JsonProperty("inputs")]
    public List<RecipeIngredientDto> Inputs = new List<RecipeIngredientDto>();

    [JsonProperty("outputs")]
    public List<RecipeOutputDto> Outputs = new List<RecipeOutputDto>();

    [JsonProperty("actions")]
    public List<RecipeActionDto> Actions = new List<RecipeActionDto>();
}

[Serializable]
public sealed class RecipeIngredientDto
{
    [JsonProperty("slot")]
    public int Slot;

    [JsonProperty("match")]
    public string Match = "exact_item";

    [JsonProperty("itemId")]
    public string ItemId;

    [JsonProperty("tag")]
    public string Tag;

    [JsonProperty("amount")]
    public int Amount;
}

[Serializable]
public sealed class RecipeOutputDto
{
    [JsonProperty("itemId")]
    public string ItemId;

    [JsonProperty("amount")]
    public int Amount = 1;
}

[Serializable]
public sealed class RecipeActionDto
{
    [JsonProperty("type")]
    public string Type;

    [JsonProperty("targetRole")]
    public string TargetRole;

    [JsonProperty("value")]
    public float Value;

    [JsonProperty("slotIndex")]
    public int SlotIndex = -1;
}
