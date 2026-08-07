using System;
using System.Collections.Generic;
using Newtonsoft.Json;

#region MOD 清单

[Serializable]
public sealed class ModManifest
{
    [JsonProperty("apiVersion")]
    public int ApiVersion = 1;

    [JsonProperty("id", Required = Required.Always)]
    public string Id;

    [JsonProperty("name")]
    public string Name;

    [JsonProperty("version", Required = Required.Always)]
    public string Version;

    [JsonProperty("description")]
    public string Description;

    [JsonProperty("author")]
    public string Author;

    [JsonProperty("minGameVersion")]
    public string MinGameVersion;

    [JsonProperty("maxGameVersion")]
    public string MaxGameVersion;

    [JsonProperty("loadOrder")]
    public int LoadOrder;

    [JsonProperty("dependencies")]
    public List<ModDependency> Dependencies = new();

    [JsonProperty("loadAfter")]
    public List<string> LoadAfter = new();

    [JsonProperty("loadBefore")]
    public List<string> LoadBefore = new();

    [JsonProperty("conflicts")]
    public List<string> Conflicts = new();

    [JsonProperty("definitionFiles")]
    public List<string> DefinitionFiles = new();

    [JsonProperty("patchFiles")]
    public List<string> PatchFiles = new();

    [JsonProperty("localizationFiles")]
    public List<string> LocalizationFiles = new();

    [JsonProperty("settingsFile")]
    public string SettingsFile;

    [JsonProperty("bundles")]
    public List<ModBundleDefinition> Bundles = new();

    [JsonProperty("entryLua")]
    public string EntryLua;

    [JsonProperty("contentHash")]
    public string ContentHash;
}

[Serializable]
public sealed class ModDependency
{
    [JsonProperty("id", Required = Required.Always)]
    public string Id;

    [JsonProperty("minVersion")]
    public string MinVersion;

    [JsonProperty("maxVersion")]
    public string MaxVersion;

    [JsonProperty("optional")]
    public bool Optional;
}

[Serializable]
public sealed class ModBundleDefinition
{
    [JsonProperty("id", Required = Required.Always)]
    public string Id;

    [JsonProperty("path", Required = Required.Always)]
    public string Path;

    [JsonProperty("platform")]
    public string Platform;
}

#endregion

#region MOD 内容定义

[Serializable]
public sealed class ModDefinitionDocument
{
    [JsonProperty("assets")]
    public List<ModAssetDefinition> Assets = new();

    [JsonProperty("items")]
    public List<ModItemDefinition> Items = new();

    [JsonProperty("recipes")]
    public List<RecipeDto> Recipes = new();

    [JsonProperty("buffs")]
    public List<BuffDefinitionDto> Buffs = new();
}

[Serializable]
public sealed class ModAssetDefinition
{
    [JsonProperty("id", Required = Required.Always)]
    public string Id;

    [JsonProperty("type", Required = Required.Always)]
    public string Type;

    [JsonProperty("bundle", Required = Required.Always)]
    public string Bundle;

    [JsonProperty("asset", Required = Required.Always)]
    public string Asset;
}

[Serializable]
public sealed class ModItemDefinition
{
    [JsonProperty("id", Required = Required.Always)]
    public string Id;

    [JsonProperty("abstract")]
    public bool Abstract;

    [JsonProperty("parent")]
    public string Parent;

    [JsonProperty("basePrefab")]
    public string BasePrefab;

    [JsonProperty("gameName")]
    public string GameName;

    [JsonProperty("description")]
    public string Description;

    [JsonProperty("labelKey")]
    public string LabelKey;

    [JsonProperty("descriptionKey")]
    public string DescriptionKey;

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

    [JsonProperty("modules")]
    public List<string> Modules = new();

    [JsonProperty("spriteBundle")]
    public string SpriteBundle;

    [JsonProperty("spriteAsset")]
    public string SpriteAsset;
}

#endregion

#region MOD Patch

[Serializable]
public sealed class ModPatchDocument
{
    [JsonProperty("patches")]
    public List<ModPatchOperation> Patches = new();
}

[Serializable]
public sealed class ModPatchOperation
{
    [JsonProperty("target", Required = Required.Always)]
    public string Target;

    [JsonProperty("operation", Required = Required.Always)]
    public string Operation;

    [JsonProperty("path", Required = Required.Always)]
    public string Path;

    [JsonProperty("value")]
    public Newtonsoft.Json.Linq.JToken Value;

    [JsonProperty("expect")]
    public Newtonsoft.Json.Linq.JToken Expect;

    [JsonProperty("optional")]
    public bool Optional;
}

#endregion

#region MOD 本地化与设置

[Serializable]
public sealed class ModLocalizationDocument
{
    [JsonProperty("language", Required = Required.Always)]
    public string Language;

    [JsonProperty("entries")]
    public Dictionary<string, string> Entries = new();
}

[Serializable]
public sealed class ModSettingsDocument
{
    [JsonProperty("settings")]
    public List<ModSettingDefinition> Settings = new();
}

[Serializable]
public sealed class ModSettingDefinition
{
    [JsonProperty("id", Required = Required.Always)]
    public string Id;

    [JsonProperty("type", Required = Required.Always)]
    public string Type;

    [JsonProperty("scope")]
    public string Scope = "client";

    [JsonProperty("default")]
    public Newtonsoft.Json.Linq.JToken DefaultValue;

    [JsonProperty("min")]
    public double? Minimum;

    [JsonProperty("max")]
    public double? Maximum;

    [JsonProperty("options")]
    public List<string> Options = new();

    [JsonProperty("labelKey")]
    public string LabelKey;

    [JsonProperty("descriptionKey")]
    public string DescriptionKey;

    [JsonProperty("restartRequired")]
    public bool RestartRequired;
}

#endregion

#region MOD 存档元数据

[MemoryPack.MemoryPackable]
[Serializable]
public partial class ModSaveRecord
{
    public string Id;
    public string Version;
    public string ContentHash;
    public int LoadIndex;
}

#endregion
