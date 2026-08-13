using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public sealed class EcologySpawnRuleConfig
{
    [LabelText("规则标识")] public string RuleId = "ecology.rule";
    [LabelText("物品 ID")] public string ItemId;
    [LabelText("生成数量"), Min(1)] public int ItemCount = 1;
    [LabelText("基础概率"), Range(0f, 1f)] public float SpawnChance;
    [LabelText("概率倍率"), Min(0f)] public float SpawnChanceMultiplier = 1f;
    [LabelText("群系位掩码"), Tooltip("0 表示全部地表群系；1<<SurfaceBiomeKind 表示指定群系。")] public int BiomeMask;
    [LabelText("最低温度"), Range(0f, 1f)] public float MinTemperature;
    [LabelText("最高温度"), Range(0f, 1f)] public float MaxTemperature = 1f;
    [LabelText("最低降水"), Range(0f, 1f)] public float MinPrecipitation;
    [LabelText("最高降水"), Range(0f, 1f)] public float MaxPrecipitation = 1f;
    [LabelText("最低高度"), Range(0f, 1f)] public float MinHeight;
    [LabelText("最高高度"), Range(0f, 1f)] public float MaxHeight = 1f;
    [LabelText("最低河岸影响"), Range(0f, 1f), Tooltip("大于 0 时只在河流冲积影响带内生成。")]
    public float MinRiverFloodplainStrength;
    [LabelText("提供宿主标签")] public List<string> ProvidedTags = new();
    [LabelText("仅伴生物")] public bool CompanionOnly;
    [LabelText("宿主标签")] public string CompanionHostTag;
    [LabelText("伴生概率"), Range(0f, 1f)] public float CompanionSpawnChance;
    [LabelText("伴生固定偏移 X")] public float CompanionOffsetX;
    [LabelText("伴生固定偏移 Y")] public float CompanionOffsetY;
    [LabelText("伴生最小半径"), Min(0f)] public float CompanionMinRadius;
    [LabelText("伴生最大半径"), Min(0f)] public float CompanionMaxRadius;

    /// <summary>把 Unity 配置转换成后台线程可安全读取的纯数据。</summary>
    internal EcologySpawnRuleSnapshot CreateSnapshot()
    {
        return new EcologySpawnRuleSnapshot(
            RuleId,
            ItemId,
            ItemCount,
            SpawnChance,
            SpawnChanceMultiplier,
            BiomeMask,
            MinTemperature,
            MaxTemperature,
            MinPrecipitation,
            MaxPrecipitation,
            MinHeight,
            MaxHeight,
            ProvidedTags,
            CompanionOnly,
            CompanionHostTag,
            CompanionSpawnChance,
            CompanionOffsetX,
            CompanionOffsetY,
            CompanionMinRadius,
            CompanionMaxRadius,
            MinRiverFloodplainStrength);
    }
}

/// <summary>
/// 洞穴矿脉的 SO 配置。顺序代表旧版矿脉筛选优先级：稀有矿在前，石矿作为最后回退。
/// 只保存字符串和数字，生成线程可安全转换成纯数据快照。
/// </summary>
[Serializable]
public sealed class CaveResourceRuleConfig
{
    [LabelText("规则标识")] public string RuleId = "cave.resource";
    [LabelText("物品 ID")] public string ItemId;
    [LabelText("矿脉阈值"), Range(0f, 1f)] public float VeinThreshold;
    [LabelText("矿脉尺度"), Min(0.0001f)] public float VeinScale = 0.04f;
    [LabelText("噪声偏移")] public int NoiseOffset;

    /// <summary>把 Unity 配置转换成后台线程可安全读取的纯数据。</summary>
    internal CaveResourceRuleSnapshot CreateSnapshot()
    {
        return new CaveResourceRuleSnapshot(
            RuleId,
            ItemId,
            VeinThreshold,
            VeinScale,
            NoiseOffset);
    }
}

[CreateAssetMenu(fileName = "ChunkGenerationProfile", menuName = "FlatWorld/World/Chunk Generation Profile")]
public sealed class ChunkGenerationProfileSO : ScriptableObject
{
    [Serializable]
    private struct NumericParameter
    {
        [LabelText("参数标识")]
        public string Id;

        [LabelText("参数数值")]
        public double Value;
    }

    [Serializable]
    private struct TextParameter
    {
        [LabelText("参数标识")]
        public string Id;

        [LabelText("文本内容")]
        public string Value;
    }

    [SerializeField, LabelText("配置标识")] private string profileId = "surface.default";
    [SerializeField, LabelText("生成签名")] private int generationSignature =
        DeterministicChunkGenerator.CurrentGenerationSignature;
    [SerializeField, LabelText("区块宽度"), Min(1)] private int chunkWidth = 100;
    [SerializeField, LabelText("区块高度"), Min(1)] private int chunkHeight = 100;
    [SerializeField, LabelText("数值参数列表")] private List<NumericParameter> numericParameters = new();
    [SerializeField, LabelText("文本参数列表")] private List<TextParameter> textParameters = new();
    [SerializeField, LabelText("生态全局倍率"), Min(0f)] private float ecologyGlobalMultiplier = 1f;
    [SerializeField, LabelText("生态物品生成规则")] private List<EcologySpawnRuleConfig> ecologyRules = new();
    [SerializeField, LabelText("洞穴矿脉规则"), Tooltip("顺序即稀有度优先级；最后一条通常是石矿回退。")]
    private List<CaveResourceRuleConfig> caveResourceRules = new();

    public string ProfileId => profileId;
    public int GenerationSignature => generationSignature;
    public int ChunkWidth => chunkWidth;
    public int ChunkHeight => chunkHeight;
    public float EcologyGlobalMultiplier => ecologyGlobalMultiplier;
    public IReadOnlyList<EcologySpawnRuleConfig> EcologyRules => ecologyRules;
    public IReadOnlyList<CaveResourceRuleConfig> CaveResourceRules => caveResourceRules;

    /// <summary>把 Unity 资源中的参数复制成后台线程可以安全读取的配置快照。</summary>
    public ChunkGenerationProfileSnapshot CreateSnapshot()
    {
        numericParameters ??= new List<NumericParameter>();
        textParameters ??= new List<TextParameter>();
        ecologyRules ??= new List<EcologySpawnRuleConfig>();
        caveResourceRules ??= new List<CaveResourceRuleConfig>();
        var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < numericParameters.Count; i++)
        {
            NumericParameter parameter = numericParameters[i];
            if (string.IsNullOrWhiteSpace(parameter.Id))
                continue;
            if (!numbers.TryAdd(parameter.Id, parameter.Value))
                throw new InvalidOperationException($"Duplicate numeric generation parameter: {parameter.Id}");
        }

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < textParameters.Count; i++)
        {
            TextParameter parameter = textParameters[i];
            if (string.IsNullOrWhiteSpace(parameter.Id))
                continue;
            if (!texts.TryAdd(parameter.Id, parameter.Value ?? string.Empty))
                throw new InvalidOperationException($"Duplicate text generation parameter: {parameter.Id}");
        }

        var ecologySnapshots = new List<EcologySpawnRuleSnapshot>(ecologyRules.Count);
        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ecologyRules.Count; i++)
        {
            EcologySpawnRuleConfig rule = ecologyRules[i];
            if (rule == null || string.IsNullOrWhiteSpace(rule.ItemId))
                continue;
            EcologySpawnRuleSnapshot snapshot = rule.CreateSnapshot();
            if (!ruleIds.Add(snapshot.RuleId))
                throw new InvalidOperationException($"Duplicate ecology rule id: {snapshot.RuleId}");
            ecologySnapshots.Add(snapshot);
        }

        var caveResourceSnapshots = new List<CaveResourceRuleSnapshot>(caveResourceRules.Count);
        var caveRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < caveResourceRules.Count; i++)
        {
            CaveResourceRuleConfig rule = caveResourceRules[i];
            if (rule == null || string.IsNullOrWhiteSpace(rule.ItemId))
                continue;
            CaveResourceRuleSnapshot snapshot = rule.CreateSnapshot();
            if (!caveRuleIds.Add(snapshot.RuleId))
                throw new InvalidOperationException($"Duplicate cave resource rule id: {snapshot.RuleId}");
            caveResourceSnapshots.Add(snapshot);
        }

        return new ChunkGenerationProfileSnapshot(
            profileId, generationSignature, chunkWidth, chunkHeight, numbers, texts,
            ecologyGlobalMultiplier, ecologySnapshots, caveResourceSnapshots);
    }
}
