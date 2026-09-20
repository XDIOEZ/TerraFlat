using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>地块 JSON 清单；仅加载显式列出的分包，不扫描目录，schemaVersion 固定为 1。</summary>
public sealed class TileDefinitionManifestDto
{
    #region 清单
    [JsonProperty("schemaVersion", Required = Required.Always)] public int SchemaVersion;
    [JsonProperty("packages", Required = Required.Always)] public List<TileDefinitionPackageDto> Packages;
    #endregion
}

/// <summary>一个本体地块分包的稳定身份和相对路径。</summary>
public sealed class TileDefinitionPackageDto
{
    #region 分包
    [JsonProperty("id", Required = Required.Always)] public string Id;
    [JsonProperty("path", Required = Required.Always)] public string Path;
    [JsonProperty("enabled")] public bool Enabled = true;
    #endregion
}

/// <summary>本体地块分包；MOD definitionFiles 中的 tiles 数组复用同一 TileDefinitionDto。</summary>
public sealed class TileDefinitionCatalogDto
{
    #region 目录
    [JsonProperty("schemaVersion", Required = Required.Always)] public int SchemaVersion;
    [JsonProperty("tiles", Required = Required.Always)] public List<TileDefinitionDto> Tiles;
    #endregion
}

/// <summary>
/// 地块静态定义。runtimeTileId 是写入世界格子的稳定整数，0 仅用于旧地图专用定义；
/// MOD 新地块使用不小于 1000000 的显式编号，不允许按加载顺序或运行时哈希分配。
/// tileAsset 是已注册 TileBase 的资源键，不是 Unity 对象、文件路径或程序集类型名。
/// </summary>
public sealed class TileDefinitionDto
{
    #region 配置
    [JsonProperty("id", Required = Required.Always)] public string Id;
    [JsonProperty("runtimeTileId")] public int RuntimeTileId;
    [JsonProperty("displayName")] public string DisplayName;
    [JsonProperty("tileAsset", Required = Required.Always)] public string TileAsset;
    [JsonProperty("data", Required = Required.Always)] public TileComponentDefinitionDto Data;
    [JsonProperty("behaviours")] public List<TileComponentDefinitionDto> Behaviours = new();
    [JsonProperty("damageProfile")] public TileBuildingDamageProfile DamageProfile = new();
    [JsonProperty("groundPlacement")] public GroundTilePlacementRule GroundPlacement;
    #endregion
}

/// <summary>稳定类型 ID 加参数；type 仅在显式注册表中解析，绝不解释为 CLR 类型名。</summary>
public sealed class TileComponentDefinitionDto
{
    #region 组件
    [JsonProperty("type", Required = Required.Always)] public string Type;
    [JsonProperty("parameters")] public JObject Parameters = new();
    #endregion
}
