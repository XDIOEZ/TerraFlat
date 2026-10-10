using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>河流生成参数目录；各 Profile 的参数在资源加载时合并进生成快照。</summary>
public sealed class RiverGenerationConfigCatalog
{
    #region 参数目录

    private readonly Dictionary<string, RiverGenerationProfileConfig> profiles =
        new(StringComparer.Ordinal);

    private static readonly string[] SurfaceNumericIds =
    {
        "river.enabled", "river.hydrologyRegionSize", "river.runoffCellSize", "river.maxTraceSteps",
        "river.infiltrationFloor", "river.runoffScale", "river.startFlow",
        "river.fullWidthFlow", "river.maxWidth", "river.meanderStrength", "river.meanderScale",
        "river.floodplainStartFlow",
        "river.floodplainMaxRadius", "river.floodplainMaxSlope",
        "river.alluvialTileThreshold", "river.bedCenterTileId", "river.bedEdgeTileId",
        "river.bedCenterStrengthThreshold", "river.bedEdgeDepositMinimumFlow",
        "river.bedEdgeDepositActivation", "river.bedEdgeDepositMinFraction",
        "river.bedEdgeDepositPeakFraction", "river.bedEdgeDepositMaxFraction",
        "river.depthMin", "river.depthMax", "river.maxCachedRegions"
    };

    private static readonly string[] CaveNumericIds =
    {
        "cave.river.enabled", "cave.river.connectionChance", "cave.river.halfWidth",
        "cave.river.minDepth", "cave.river.maxDepth"
    };

    internal RiverGenerationConfigCatalog(RiverGenerationConfigFile source)
    {
        if (source == null || source.SchemaVersion != RiverGenerationConfigLoader.SupportedSchemaVersion ||
            source.Profiles == null || source.Profiles.Count != 2)
            throw new InvalidDataException("河流生成 JSON 版本无效，或必须包含地表和洞穴两个 Profile。");

        foreach (RiverGenerationProfileConfig profile in source.Profiles)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.ProfileId) ||
                profile.NumericParameters == null || profile.TextParameters == null ||
                !profiles.TryAdd(profile.ProfileId, profile))
                throw new InvalidDataException($"河流生成 JSON 含空配置或重复 Profile：{profile?.ProfileId}");

            string[] requiredNumbers = profile.ProfileId switch
            {
                "surface.default" => SurfaceNumericIds,
                "cave.default" => CaveNumericIds,
                _ => throw new InvalidDataException($"未知的河流生成 Profile：{profile.ProfileId}")
            };
            var numbers = new HashSet<string>(StringComparer.Ordinal);
            foreach (RiverGenerationNumericParameter parameter in profile.NumericParameters)
            {
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Id) ||
                    double.IsNaN(parameter.Value) || double.IsInfinity(parameter.Value) ||
                    !numbers.Add(parameter.Id))
                    throw new InvalidDataException($"河流生成数值无效或重复：{profile.ProfileId}/{parameter?.Id}");
            }
            if (!numbers.SetEquals(requiredNumbers))
                throw new InvalidDataException($"河流生成数值参数缺失或多余：{profile.ProfileId}");

            // 正式水文只有统一入口，不接受切回旧算法的文本参数。
            if (profile.TextParameters.Count != 0)
                throw new InvalidDataException("河流生成配置不使用文本参数。");
        }
    }

    /// <summary>只允许 JSON 填入河流参数，发现 SO 中残留同名参数时直接报错。</summary>
    public void ApplyTo(string profileId, Dictionary<string, double> numbers,
        Dictionary<string, string> texts)
    {
        if (!profiles.TryGetValue(profileId, out RiverGenerationProfileConfig profile))
            throw new InvalidDataException($"河流生成 JSON 中找不到 Profile：{profileId}");
        foreach (RiverGenerationNumericParameter parameter in profile.NumericParameters)
            if (!numbers.TryAdd(parameter.Id, parameter.Value))
                throw new InvalidDataException($"河流参数仍同时存在于 SO 和 JSON：{parameter.Id}");
        foreach (RiverGenerationTextParameter parameter in profile.TextParameters)
            if (!texts.TryAdd(parameter.Id, parameter.Value))
                throw new InvalidDataException($"河流参数仍同时存在于 SO 和 JSON：{parameter.Id}");
    }

    #endregion
}

/// <summary>从 StreamingAssets 读取河流 JSON；移动平台使用异步读取。</summary>
public static class RiverGenerationConfigLoader
{
    #region 文件加载

    public const int SupportedSchemaVersion = 1;
    public const string RelativeConfigPath =
        "GameConfig/WorldGeneration/Hydrology/river-generation.json";

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        DateParseHandling = DateParseHandling.None
    };

    public static string BuiltInConfigPath =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeConfigPath);

    public static RiverGenerationConfigCatalog LoadBuiltIn() =>
        Deserialize(StreamingAssetsTextLoader.ReadAllText(BuiltInConfigPath));

    public static IEnumerator LoadBuiltInAsync(Action<RiverGenerationConfigCatalog> completed,
        Action<Exception> failed)
    {
        string json = null;
        Exception readError = null;
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(BuiltInConfigPath,
            value => json = value, error => readError = error);
        if (readError != null) { failed?.Invoke(readError); yield break; }
        try { completed?.Invoke(Deserialize(json)); }
        catch (Exception error) { failed?.Invoke(error); }
    }

    public static RiverGenerationConfigCatalog Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 32 * 1024)
            throw new InvalidDataException("河流生成 JSON 为空或超过大小限制。");
        try
        {
            RiverGenerationConfigFile source =
                JsonConvert.DeserializeObject<RiverGenerationConfigFile>(json, JsonSettings);
            return new RiverGenerationConfigCatalog(source);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("河流生成 JSON 格式错误：" + error.Message, error);
        }
    }

    #endregion
}

/// <summary>河流目录随资源会话发布，F5 候选加载失败时保留旧目录。</summary>
public static class RiverGenerationConfigService
{
    #region 资源会话

    private static RiverGenerationConfigCatalog catalog;

    public static void ReplaceCatalog(RiverGenerationConfigCatalog value) =>
        catalog = value ?? throw new ArgumentNullException(nameof(value));

    public static RiverGenerationConfigCatalog RequireCatalog() =>
        catalog ?? throw new InvalidOperationException("河流生成 JSON 尚未加载。");

    internal static void ConfigureResourceReload(ResourceReloadContext context) =>
        context.Add(() => catalog, value => catalog = value, (RiverGenerationConfigCatalog)null);

    public static void Reset() => catalog = null;

    #endregion
}

#region JSON 数据

#pragma warning disable CS0649 // 以下字段由 JSON 反序列化赋值。
[JsonObject(ItemRequired = Required.Always)]
internal sealed class RiverGenerationConfigFile
{
    [JsonProperty("schemaVersion")] public int SchemaVersion;
    [JsonProperty("profiles")] public List<RiverGenerationProfileConfig> Profiles;
}

[JsonObject(ItemRequired = Required.Always)]
internal sealed class RiverGenerationProfileConfig
{
    [JsonProperty("profileId")] public string ProfileId;
    [JsonProperty("numericParameters")] public List<RiverGenerationNumericParameter> NumericParameters;
    [JsonProperty("textParameters")] public List<RiverGenerationTextParameter> TextParameters;
}

[JsonObject(ItemRequired = Required.Always)]
internal sealed class RiverGenerationNumericParameter
{
    [JsonProperty("id")] public string Id;
    [JsonProperty("value")] public double Value;
}

[JsonObject(ItemRequired = Required.Always)]
internal sealed class RiverGenerationTextParameter
{
    [JsonProperty("id")] public string Id;
    [JsonProperty("value")] public string Value;
}
#pragma warning restore CS0649

#endregion
