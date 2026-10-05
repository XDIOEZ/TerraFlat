using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>世界生成预设；自然物、矿脉和河流配置由 JSON 补入快照。</summary>
[CreateAssetMenu(fileName = "ChunkGenerationProfile", menuName = "FlatWorld/World/Chunk Generation Profile")]
public sealed class ChunkGenerationProfileSO : ScriptableObject
{
    #region 地形参数

#pragma warning disable CS0649 // 这些字段由 Unity 序列化面板赋值。
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
#pragma warning restore CS0649

    [SerializeField, LabelText("配置标识")] private string profileId = "surface.default";
    [SerializeField, LabelText("生成签名")] private int generationSignature =
        DeterministicChunkGenerator.CurrentGenerationSignature;
    [SerializeField, LabelText("区块宽度"), Min(1)] private int chunkWidth = 100;
    [SerializeField, LabelText("区块高度"), Min(1)] private int chunkHeight = 100;
    [SerializeField, LabelText("数值参数列表")] private List<NumericParameter> numericParameters = new();
    [SerializeField, LabelText("文本参数列表")] private List<TextParameter> textParameters = new();
    [SerializeField, LabelText("生态全局倍率"), Min(0f)] private float ecologyGlobalMultiplier = 1f;
    [SerializeField, LabelText("生态物品规则 ID")] private List<string> ecologyRuleIds = new();
    [SerializeField, LabelText("洞穴矿脉规则 ID"), Tooltip("顺序代表稀有度优先级；最后通常为石矿。")]
    private List<string> caveResourceRuleIds = new();

    public string ProfileId => profileId;
    public int GenerationSignature => generationSignature;
    public int ChunkWidth => chunkWidth;
    public int ChunkHeight => chunkHeight;
    public float EcologyGlobalMultiplier => ecologyGlobalMultiplier;
    public IReadOnlyList<string> EcologyRuleIds => ecologyRuleIds;
    public IReadOnlyList<string> CaveResourceRuleIds => caveResourceRuleIds;

    #endregion

    #region 快照构建

    /// <summary>从已发布的 JSON 目录解析规则，复制成后台线程可安全读取的配置快照。</summary>
    public ChunkGenerationProfileSnapshot CreateSnapshot() =>
        CreateSnapshot(NaturalGenerationRuleCatalogService.RequireCatalog(),
            RiverGenerationConfigService.RequireCatalog());

    /// <summary>显式目录入口供编辑器预览使用；运行时由资源加载阶段提供目录。</summary>
    public ChunkGenerationProfileSnapshot CreateSnapshot(NaturalGenerationRuleCatalog rules,
        RiverGenerationConfigCatalog riverConfigs)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        if (riverConfigs == null) throw new ArgumentNullException(nameof(riverConfigs));
        var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < numericParameters.Count; i++)
        {
            NumericParameter parameter = numericParameters[i];
            if (string.IsNullOrWhiteSpace(parameter.Id))
                continue;
            if (!numbers.TryAdd(parameter.Id, parameter.Value))
                throw new InvalidOperationException($"Duplicate numeric generation parameter: {parameter.Id}");
        }

        Dictionary<string, string> texts = CreateTextParametersSnapshot();
        riverConfigs.ApplyTo(profileId, numbers, texts);
        AppendNaturalPlantableGroundTileIds(texts);

        var ecologySnapshots = new List<EcologySpawnRuleSnapshot>(ecologyRuleIds.Count);
        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ecologyRuleIds.Count; i++)
        {
            string ruleId = ecologyRuleIds[i];
            if (!ruleIds.Add(ruleId ?? string.Empty))
                throw new InvalidOperationException($"Profile {profileId} 包含重复的生态规则 ID：{ruleId}");
            ecologySnapshots.Add(rules.GetEcologyRule(ruleId));
        }

        var caveResourceSnapshots = new List<CaveResourceRuleSnapshot>(caveResourceRuleIds.Count);
        var caveRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < caveResourceRuleIds.Count; i++)
        {
            string ruleId = caveResourceRuleIds[i];
            if (!caveRuleIds.Add(ruleId ?? string.Empty))
                throw new InvalidOperationException($"Profile {profileId} 包含重复的矿脉规则 ID：{ruleId}");
            caveResourceSnapshots.Add(rules.GetCaveResourceRule(ruleId));
        }

        return new ChunkGenerationProfileSnapshot(
            profileId, generationSignature, chunkWidth, chunkHeight, numbers, texts,
            ecologyGlobalMultiplier, ecologySnapshots, caveResourceSnapshots);
    }

    /// <summary>把地块目录里的自然可种植能力冻结进纯生成快照，后台线程不读取 GameRes。</summary>
    private static void AppendNaturalPlantableGroundTileIds(Dictionary<string, string> texts)
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null)
            return;

        var tileIds = new SortedSet<int>();
        foreach (RuntimeTileDefinition definition in resources.TileBlockDict.Values)
            if (definition != null && definition.NaturalPlantable && definition.RuntimeTileId > 0)
                tileIds.Add(definition.RuntimeTileId);

        if (tileIds.Count > 0)
            texts["ecology.naturalPlantableGroundTileIds"] = string.Join(",", tileIds);
    }

    /// <summary>独立读取地块 ID 等文本参数，供编辑器校验使用且不依赖自然物目录。</summary>
    public Dictionary<string, string> CreateTextParametersSnapshot()
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < textParameters.Count; i++)
        {
            TextParameter parameter = textParameters[i];
            if (string.IsNullOrWhiteSpace(parameter.Id))
                continue;
            if (!texts.TryAdd(parameter.Id, parameter.Value ?? string.Empty))
                throw new InvalidOperationException($"Duplicate text generation parameter: {parameter.Id}");
        }
        return texts;
    }

    #endregion
}
