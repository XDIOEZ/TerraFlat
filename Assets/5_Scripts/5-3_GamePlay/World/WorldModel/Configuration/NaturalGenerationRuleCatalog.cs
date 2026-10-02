using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.WorldModel;
using Newtonsoft.Json;

/// <summary>自然物 JSON 仓库；规则 ID 是稳定引用，构建后的快照只读并可供后台生成使用。</summary>
public sealed class NaturalGenerationRuleCatalog
{
    #region 规则注册

    private readonly Dictionary<string, EcologySpawnRuleSnapshot> ecologyRules =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CaveResourceRuleSnapshot> caveResourceRules =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<EcologySpawnRuleSnapshot> EcologyRules => ecologyRules.Values;
    public IReadOnlyCollection<CaveResourceRuleSnapshot> CaveResourceRules => caveResourceRules.Values;

    /// <summary>登记一个分包；重复 ID 和无效数值直接阻止目录发布。</summary>
    internal void Register(NaturalGenerationRulePackage package, string source)
    {
        if (package == null || package.SchemaVersion != NaturalGenerationRuleCatalogLoader.SupportedSchemaVersion ||
            package.EcologyRules == null || package.CaveResourceRules == null)
            throw new InvalidDataException($"自然物分包 {source} 缺少规则列表或 schemaVersion 不受支持。");
        if (package.EcologyRules.Count == 0 && package.CaveResourceRules.Count == 0)
            throw new InvalidDataException($"自然物分包 {source} 为空。");

        foreach (NaturalEcologyRuleDefinition rule in package.EcologyRules)
        {
            ValidateEcologyRule(rule, source);
            EcologySpawnRuleSnapshot snapshot = rule.CreateSnapshot();
            if (!ecologyRules.TryAdd(snapshot.RuleId, snapshot))
                throw new InvalidDataException($"自然物规则 ID 重复：{snapshot.RuleId}（{source}）");
        }

        foreach (NaturalCaveResourceRuleDefinition rule in package.CaveResourceRules)
        {
            ValidateCaveRule(rule, source);
            CaveResourceRuleSnapshot snapshot = rule.CreateSnapshot();
            if (!caveResourceRules.TryAdd(snapshot.RuleId, snapshot))
                throw new InvalidDataException($"洞穴矿脉规则 ID 重复：{snapshot.RuleId}（{source}）");
        }
    }

    /// <summary>按 SO 中的 ID 查找生态规则；缺失即配置错误。</summary>
    public EcologySpawnRuleSnapshot GetEcologyRule(string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId) || !ecologyRules.TryGetValue(ruleId, out var rule))
            throw new InvalidDataException($"自然物 JSON 中找不到生态规则：{ruleId}");
        return rule;
    }

    /// <summary>按 SO 中的 ID 查找矿脉规则；返回顺序由 SO 决定。</summary>
    public CaveResourceRuleSnapshot GetCaveResourceRule(string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId) || !caveResourceRules.TryGetValue(ruleId, out var rule))
            throw new InvalidDataException($"自然物 JSON 中找不到洞穴矿脉规则：{ruleId}");
        return rule;
    }

    #endregion

    #region 数值校验

    private static void ValidateEcologyRule(NaturalEcologyRuleDefinition rule, string source)
    {
        if (rule == null || string.IsNullOrWhiteSpace(rule.RuleId) || string.IsNullOrWhiteSpace(rule.ItemId) ||
            rule.ItemCount < 1 || rule.BiomeMask < 0 || rule.ProvidedTags == null ||
            !Enum.IsDefined(typeof(EcologyDistributionMode), rule.DistributionMode) ||
            rule.PatchSpacing < 2 || !InRange(rule.PatchRadius, 0.5d, rule.PatchSpacing * 0.5d) ||
            !InRange(rule.SpawnChance, 0d, 1d) || !AtLeast(rule.SpawnChanceMultiplier, 0d) ||
            !InRange(rule.PatchChance, 0d, 1d) ||
            !OrderedUnitRange(rule.MinTemperature, rule.MaxTemperature) ||
            !OrderedUnitRange(rule.MinPrecipitation, rule.MaxPrecipitation) ||
            !OrderedUnitRange(rule.MinHeight, rule.MaxHeight) ||
            !OrderedUnitRange(rule.MinRiverFloodplainStrength, rule.MaxRiverFloodplainStrength) ||
            !Finite(rule.MinimumEnvironmentValue) ||
            !InRange(rule.CompanionSpawnChance, 0d, 1d) ||
            !Finite(rule.CompanionOffsetX) || !Finite(rule.CompanionOffsetY) ||
            !AtLeast(rule.CompanionMinRadius, 0d) ||
            !AtLeast(rule.CompanionMaxRadius, rule.CompanionMinRadius) ||
            (rule.CompanionOnly && string.IsNullOrWhiteSpace(rule.CompanionHostTag)) ||
            (!rule.CompanionOnly && !string.IsNullOrWhiteSpace(rule.RequiredChunkTag)) ||
            rule.RequiredTagChunkRadius < 0 || rule.RequiredTagChunkRadius > 1 ||
            (rule.RequiredTagChunkRadius > 0 && string.IsNullOrWhiteSpace(rule.RequiredChunkTag)))
            throw new InvalidDataException($"自然物规则数值或引用无效：{rule?.RuleId}（{source}）");

        foreach (string tag in rule.ProvidedTags)
            if (string.IsNullOrWhiteSpace(tag))
                throw new InvalidDataException($"自然物规则 {rule.RuleId} 含空标签（{source}）");
    }

    private static void ValidateCaveRule(NaturalCaveResourceRuleDefinition rule, string source)
    {
        if (rule == null || string.IsNullOrWhiteSpace(rule.RuleId) || string.IsNullOrWhiteSpace(rule.ItemId) ||
            !InRange(rule.VeinThreshold, 0d, 1d) || !AtLeast(rule.VeinScale, 0.0001d))
            throw new InvalidDataException($"洞穴矿脉规则无效：{rule?.RuleId}（{source}）");
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool InRange(double value, double min, double max) =>
        Finite(value) && value >= min && value <= max;
    private static bool AtLeast(double value, double min) => Finite(value) && value >= min;
    private static bool OrderedUnitRange(double min, double max) =>
        InRange(min, 0d, 1d) && InRange(max, min, 1d);

    #endregion
}

/// <summary>资源会话持有的自然物目录；F5 候选加载期间与当前世界隔离。</summary>
public static class NaturalGenerationRuleCatalogService
{
    #region 目录生命周期

    public static NaturalGenerationRuleCatalog Catalog { get; private set; }

    /// <summary>候选资源构建时使用独立目录，发布后替换当前目录。</summary>
    internal static void ConfigureResourceReload(ResourceReloadContext context) =>
        context.Add(() => Catalog, value => Catalog = value, (NaturalGenerationRuleCatalog)null);

    public static void ReplaceCatalog(NaturalGenerationRuleCatalog catalog) =>
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public static NaturalGenerationRuleCatalog RequireCatalog() =>
        Catalog ?? throw new InvalidOperationException("自然物 JSON 规则目录尚未加载。");

    public static void Reset() => Catalog = null;

    #endregion
}

/// <summary>每个 JSON 分包可定义多条生态规则和矿脉规则。</summary>
#pragma warning disable CS0649 // 以下字段由 JSON 反序列化赋值。
[JsonObject(ItemRequired = Required.Always)]
internal sealed class NaturalGenerationRulePackage
{
    public int SchemaVersion;
    public List<NaturalEcologyRuleDefinition> EcologyRules;
    public List<NaturalCaveResourceRuleDefinition> CaveResourceRules;
}

/// <summary>一条生态自然物规则的 JSON 数据，字段对应纯数据快照。</summary>
[JsonObject(ItemRequired = Required.Always)]
internal sealed class NaturalEcologyRuleDefinition
{
    public string RuleId;
    public string ItemId;
    public int ItemCount;
    public float SpawnChance;
    public float SpawnChanceMultiplier;
    public EcologyDistributionMode DistributionMode;
    public int PatchSpacing;
    public float PatchRadius;
    public float PatchChance;
    public int BiomeMask;
    public float MinTemperature;
    public float MaxTemperature;
    public float MinPrecipitation;
    public float MaxPrecipitation;
    public float MinHeight;
    public float MaxHeight;
    public float MinRiverFloodplainStrength;
    // 既有规则省略上限时仍允许全部河岸强度，新植物可显式排除湿地。
    [JsonProperty(Required = Required.DisallowNull)]
    public float MaxRiverFloodplainStrength = 1f;
    [JsonProperty(Required = Required.DisallowNull)]
    public string RequiredEnvironmentLayer = string.Empty;
    [JsonProperty(Required = Required.DisallowNull)]
    public float MinimumEnvironmentValue;
    public List<string> ProvidedTags;
    public bool CompanionOnly;
    public string CompanionHostTag;
    public string RequiredChunkTag;
    public int RequiredTagChunkRadius;
    public float CompanionSpawnChance;
    public float CompanionOffsetX;
    public float CompanionOffsetY;
    public float CompanionMinRadius;
    public float CompanionMaxRadius;

    /// <summary>完成严格校验后生成不可变规则。</summary>
    public EcologySpawnRuleSnapshot CreateSnapshot() => new(
        RuleId, ItemId, ItemCount, SpawnChance, SpawnChanceMultiplier, BiomeMask,
        MinTemperature, MaxTemperature, MinPrecipitation, MaxPrecipitation,
        MinHeight, MaxHeight, ProvidedTags, CompanionOnly, CompanionHostTag,
        RequiredChunkTag, CompanionSpawnChance, CompanionOffsetX, CompanionOffsetY,
        CompanionMinRadius, CompanionMaxRadius, MinRiverFloodplainStrength,
        DistributionMode, PatchSpacing, PatchRadius, PatchChance, RequiredTagChunkRadius,
        MaxRiverFloodplainStrength, RequiredEnvironmentLayer, MinimumEnvironmentValue);
}

/// <summary>一条洞穴矿脉规则的 JSON 数据；优先级由 SO 引用顺序决定。</summary>
[JsonObject(ItemRequired = Required.Always)]
internal sealed class NaturalCaveResourceRuleDefinition
{
    public string RuleId;
    public string ItemId;
    public float VeinThreshold;
    public float VeinScale;
    public int NoiseOffset;

    public CaveResourceRuleSnapshot CreateSnapshot() =>
        new(RuleId, ItemId, VeinThreshold, VeinScale, NoiseOffset);
}
#pragma warning restore CS0649
