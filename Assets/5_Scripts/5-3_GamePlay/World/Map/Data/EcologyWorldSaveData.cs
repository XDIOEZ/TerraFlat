using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using MemoryPack;

/// <summary>
/// 生态生成的世界级存档。
/// 配置只保存字符串和数值；自然物的基线由世界种子重新生成，存档只记录被删除的 GUID
/// 和发生过状态变化的 ItemData，避免区块保存体积随自然物数量线性膨胀。
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
[Serializable]
public partial class EcologyWorldSaveData
{
    #region 世界配置

    public const int CurrentDataVersion = 7;
    private const string RetiredWeedItemId = "Weed";
    private const string RetiredWeedRuleId = "surface.grassland.weed";

    [MemoryPackInclude, MemoryPackOrder(0)] public int DataVersion;
    [MemoryPackInclude, MemoryPackOrder(1)] public string ProfileId;
    [MemoryPackInclude, MemoryPackOrder(2)] public ulong ConfigurationFingerprint;
    [MemoryPackInclude, MemoryPackOrder(3)] public double GlobalMultiplier = 1d;
    [MemoryPackInclude, MemoryPackOrder(4)] public List<EcologyRuleSaveData> Rules = new();
    [MemoryPackInclude, MemoryPackOrder(5)] public List<EcologyChunkSaveData> Chunks = new();
    [MemoryPackInclude, MemoryPackOrder(6)] public List<IndependentNaturalEntitySaveData> IndependentEntities = new();
    // 冻结洞穴布局、矿脉和地表入口参数。
    [MemoryPackInclude, MemoryPackOrder(7)] public WorldGenerationProfileSaveData Generation = new();

    [MemoryPackIgnore]
    public bool HasConfiguration => DataVersion == CurrentDataVersion && Rules != null;
    [MemoryPackIgnore]
    public bool HasUnsupportedConfiguration => DataVersion > 0 &&
                                               DataVersion != CurrentDataVersion;
    [MemoryPackIgnore]
    public bool HasGenerationConfiguration => Generation != null && Generation.HasConfiguration;

    /// <summary>生态规则结构改变后直接拒绝旧存档，避免用缺失字段静默重排自然物。</summary>
    public void EnsureCompatibleVersion()
    {
        if (HasUnsupportedConfiguration)
        {
            throw new InvalidOperationException(
                $"生态存档版本不兼容：存档={DataVersion}，当前={CurrentDataVersion}。请创建新世界。");
        }

        if (HasGenerationConfiguration)
            Generation.RequireSupportedGenerationAlgorithmVersion();

        MigrateRetiredNaturalItemRules();
    }

    /// <summary>
    /// Weed 已从 Item 目录退休，普通草改由地表草层表现。
    /// 旧世界冻结的生态规则仍会保存这条生成规则；读取时显式移除它，
    /// 保留其它冻结规则与区块差量，避免旧档持续实例化已删除的 Item。
    /// </summary>
    private void MigrateRetiredNaturalItemRules()
    {
        if (DataVersion != CurrentDataVersion || Rules == null || Rules.Count == 0)
            return;

        int removed = Rules.RemoveAll(rule =>
            rule != null &&
            (string.Equals(rule.RuleId, RetiredWeedRuleId, StringComparison.Ordinal) ||
             string.Equals(rule.ItemId, RetiredWeedItemId, StringComparison.Ordinal)));
        if (removed > 0)
            ConfigurationFingerprint = 0UL;
    }

    /// <summary>首次进入世界时冻结当前 Profile 的生态配置。</summary>
    public void CaptureConfiguration(ChunkGenerationProfileSnapshot profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        DataVersion = CurrentDataVersion;
        ProfileId = profile.ProfileId ?? string.Empty;
        ConfigurationFingerprint = profile.EcologyFingerprint;
        GlobalMultiplier = profile.EcologyGlobalMultiplier;
        Rules ??= new List<EcologyRuleSaveData>();
        Rules.Clear();
        for (int i = 0; i < profile.EcologyRules.Count; i++)
            Rules.Add(EcologyRuleSaveData.FromSnapshot(profile.EcologyRules[i]));
        CaptureGenerationConfiguration(profile);
    }

    /// <summary>单独冻结完整 Profile，不覆盖已保存的生态规则。</summary>
    public void CaptureGenerationConfiguration(ChunkGenerationProfileSnapshot profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        Generation ??= new WorldGenerationProfileSaveData();
        Generation.Capture(profile);
    }

    /// <summary>丢弃冻结生成配置，但保留自然物删除、状态覆盖与恢复年份等区块差量。</summary>
    public void ClearFrozenConfiguration()
    {
        DataVersion = 0;
        ProfileId = string.Empty;
        ConfigurationFingerprint = 0UL;
        GlobalMultiplier = 1d;
        Rules = new List<EcologyRuleSaveData>();
        Generation = new WorldGenerationProfileSaveData();
    }

    /// <summary>仅在 Profile 标识匹配时恢复冻结参数，避免地表克隆数据误覆盖矿洞 Profile。</summary>
    public bool TryApplyGenerationConfiguration(ChunkGenerationProfileSnapshot profile,
        out ChunkGenerationProfileSnapshot restored)
    {
        restored = profile;
        if (profile == null || !HasGenerationConfiguration || !Generation.Matches(profile))
            return false;
        restored = Generation.Apply(profile);
        return true;
    }

    #endregion

    #region 区块差量

    /// <summary>从已冻结的生态配置恢复后台生成所需的纯规则快照。</summary>
    public IReadOnlyList<EcologySpawnRuleSnapshot> CreateRuleSnapshots()
    {
        if (Rules == null || Rules.Count == 0)
            return Array.Empty<EcologySpawnRuleSnapshot>();

        var snapshots = new List<EcologySpawnRuleSnapshot>(Rules.Count);
        for (int i = 0; i < Rules.Count; i++)
        {
            EcologyRuleSaveData rule = Rules[i];
            if (rule != null && !string.IsNullOrWhiteSpace(rule.RuleId) &&
                !string.IsNullOrWhiteSpace(rule.ItemId))
            {
                snapshots.Add(rule.ToSnapshot());
            }
        }
        return snapshots;
    }

    /// <summary>获取或创建一个区块的生态差量记录。</summary>
    public EcologyChunkSaveData GetOrCreateChunk(int chunkX, int chunkY)
    {
        Chunks ??= new List<EcologyChunkSaveData>();
        for (int i = 0; i < Chunks.Count; i++)
        {
            EcologyChunkSaveData chunk = Chunks[i];
            if (chunk != null && chunk.ChunkX == chunkX && chunk.ChunkY == chunkY)
                return chunk;
        }

        var created = new EcologyChunkSaveData
        {
            ChunkX = chunkX,
            ChunkY = chunkY
        };
        Chunks.Add(created);
        return created;
    }

    /// <summary>尝试读取指定区块的生态差量记录。</summary>
    public bool TryGetChunk(int chunkX, int chunkY, out EcologyChunkSaveData chunk)
    {
        chunk = null;
        if (Chunks == null)
            return false;

        for (int i = 0; i < Chunks.Count; i++)
        {
            EcologyChunkSaveData candidate = Chunks[i];
            if (candidate != null && candidate.ChunkX == chunkX && candidate.ChunkY == chunkY)
            {
                chunk = candidate;
                return true;
            }
        }
        return false;
    }

    /// <summary>判断自然物是否已被玩家采集或销毁。</summary>
    public bool IsRemoved(int chunkX, int chunkY, int guid)
    {
        return TryGetChunk(chunkX, chunkY, out EcologyChunkSaveData chunk) &&
               chunk.IsRemoved(guid);
    }

    /// <summary>读取自然物的状态覆盖。</summary>
    public bool TryGetChangedItem(int chunkX, int chunkY, int guid, out ItemData itemData)
    {
        itemData = null;
        return TryGetChunk(chunkX, chunkY, out EcologyChunkSaveData chunk) &&
               chunk.TryGetChangedItem(guid, out itemData);
    }

    /// <summary>记录自然物被删除，并清理同 GUID 的旧状态覆盖。</summary>
    public void MarkRemoved(int chunkX, int chunkY, int guid)
    {
        if (guid == 0)
            return;
        GetOrCreateChunk(chunkX, chunkY).MarkRemoved(guid);
    }

    /// <summary>记录自然物当前 ItemData，供卸载区块或保存前捕获。</summary>
    public void CaptureChangedItem(int chunkX, int chunkY, ItemData itemData)
    {
        if (itemData == null || itemData.Guid == 0)
            return;
        GetOrCreateChunk(chunkX, chunkY).CaptureChangedItem(itemData);
    }

    /// <summary>同一轮自然物快照只查找一次区块，批次结束后不保留易失效的索引。</summary>
    public void CaptureChangedItems(int chunkX, int chunkY, IReadOnlyList<ItemData> items)
    {
        if (items == null || items.Count == 0) return;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null || items[i].Guid == 0) continue;
            GetOrCreateChunk(chunkX, chunkY).CaptureChangedItems(items);
            return;
        }
    }

    #endregion
}

/// <summary>出生身份与当前位置分别保存，可动物自然资源不再被出生区块重复生成。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
[Serializable]
public partial class IndependentNaturalEntitySaveData
{
    #region 独立自然实体所有权
    [MemoryPackOrder(0)] public string SourceWorldKey;
    [MemoryPackOrder(1)] public int SourceChunkX;
    [MemoryPackOrder(2)] public int SourceChunkY;
    [MemoryPackOrder(3)] public int Guid;
    [MemoryPackIgnore] public ItemData Snapshot;
    [MemoryPackInclude, MemoryPackOrder(4)] private byte[] ItemSnapshotBytes
    {
        get => Snapshot == null ? null : ItemSnapshotSerialization.SerializePayload(ItemSnapshotSerialization.Capture(Snapshot));
        set => Snapshot = value == null ? null : MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(value).CreateColdData();
    }
    #endregion
}

/// <summary>
/// 生成 Profile 的通用持久化快照。
/// 该数据不含 Unity 资源引用，用于冻结地表入口、洞穴房间/隧道和矿脉参数。
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
[Serializable]
public partial class WorldGenerationProfileSaveData
{
    public const int CurrentDataVersion = 4;

    [MemoryPackOrder(0)] public int DataVersion;
    [MemoryPackOrder(1)] public string ProfileId;
    [MemoryPackOrder(2)] public ulong GenerationFingerprint;
    [MemoryPackOrder(3)] public List<WorldGenerationNumericParameterSaveData> NumericParameters = new();
    [MemoryPackOrder(4)] public List<WorldGenerationTextParameterSaveData> TextParameters = new();
    [MemoryPackOrder(5)] public List<CaveResourceRuleSaveData> CaveResourceRules = new();

    [MemoryPackIgnore]
    public bool HasConfiguration => DataVersion > 0 && NumericParameters != null &&
                                    TextParameters != null && CaveResourceRules != null;

    public int RequireSupportedGenerationAlgorithmVersion()
    {
        double version = WorldGenerationAlgorithmVersion.Current;
        if (NumericParameters != null)
            foreach (WorldGenerationNumericParameterSaveData parameter in NumericParameters)
                if (parameter != null && parameter.Id == WorldGenerationAlgorithmVersion.ParameterId)
                    version = parameter.Value;
        return WorldGenerationAlgorithmVersion.RequireSupported(version);
    }

    /// <summary>复制完整的数值、文本和矿脉规则；键按序保存便于审查与 JSON 转换。</summary>
    public void Capture(ChunkGenerationProfileSnapshot profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        DataVersion = CurrentDataVersion;
        ProfileId = profile.ProfileId ?? string.Empty;
        GenerationFingerprint = profile.GenerationFingerprint;
        NumericParameters ??= new List<WorldGenerationNumericParameterSaveData>();
        TextParameters ??= new List<WorldGenerationTextParameterSaveData>();
        CaveResourceRules ??= new List<CaveResourceRuleSaveData>();
        NumericParameters.Clear();
        TextParameters.Clear();
        CaveResourceRules.Clear();

        var numericKeys = new List<string>(profile.NumericParameters.Keys);
        numericKeys.Sort(StringComparer.Ordinal);
        for (int i = 0; i < numericKeys.Count; i++)
        {
            string id = numericKeys[i];
            NumericParameters.Add(new WorldGenerationNumericParameterSaveData
            {
                Id = id,
                Value = profile.NumericParameters[id]
            });
        }

        var textKeys = new List<string>(profile.TextParameters.Keys);
        textKeys.Sort(StringComparer.Ordinal);
        for (int i = 0; i < textKeys.Count; i++)
        {
            string id = textKeys[i];
            TextParameters.Add(new WorldGenerationTextParameterSaveData
            {
                Id = id,
                Value = profile.TextParameters[id] ?? string.Empty
            });
        }

        for (int i = 0; i < profile.CaveResourceRules.Count; i++)
            CaveResourceRules.Add(CaveResourceRuleSaveData.FromSnapshot(
                profile.CaveResourceRules[i]));
    }

    /// <summary>判断当前资源是否与冻结 Profile 对应，防止维度克隆时串用配置。</summary>
    public bool Matches(ChunkGenerationProfileSnapshot profile)
    {
        return profile != null && HasConfiguration &&
               string.Equals(ProfileId, profile.ProfileId, StringComparison.Ordinal);
    }

    /// <summary>构造后台生成器可直接使用的冻结 Profile。</summary>
    public ChunkGenerationProfileSnapshot Apply(ChunkGenerationProfileSnapshot profile)
    {
        if (!Matches(profile))
            return profile;

        var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < NumericParameters.Count; i++)
        {
            WorldGenerationNumericParameterSaveData parameter = NumericParameters[i];
            if (parameter != null && !string.IsNullOrWhiteSpace(parameter.Id))
                numbers[parameter.Id] = parameter.Value;
        }
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < TextParameters.Count; i++)
        {
            WorldGenerationTextParameterSaveData parameter = TextParameters[i];
            if (parameter != null && !string.IsNullOrWhiteSpace(parameter.Id))
                texts[parameter.Id] = parameter.Value ?? string.Empty;
        }
        var resources = new List<CaveResourceRuleSnapshot>(CaveResourceRules.Count);
        for (int i = 0; i < CaveResourceRules.Count; i++)
        {
            CaveResourceRuleSaveData rule = CaveResourceRules[i];
            if (rule != null && !string.IsNullOrWhiteSpace(rule.RuleId) &&
                !string.IsNullOrWhiteSpace(rule.ItemId))
            {
                resources.Add(rule.ToSnapshot());
            }
        }
        return profile.WithGenerationConfiguration(numbers, texts, resources);
    }
}

/// <summary>一个冻结数值生成参数。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
[Serializable]
public partial class WorldGenerationNumericParameterSaveData
{
    [MemoryPackOrder(0)] public string Id;
    [MemoryPackOrder(1)] public double Value;
}

/// <summary>一个冻结文本生成参数。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
[Serializable]
public partial class WorldGenerationTextParameterSaveData
{
    [MemoryPackOrder(0)] public string Id;
    [MemoryPackOrder(1)] public string Value;
}

/// <summary>洞穴矿脉规则的存档副本，顺序保持稀有度优先级。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
[Serializable]
public partial class CaveResourceRuleSaveData
{
    [MemoryPackOrder(0)] public string RuleId;
    [MemoryPackOrder(1)] public string ItemId;
    [MemoryPackOrder(2)] public double VeinThreshold;
    [MemoryPackOrder(3)] public double VeinScale;
    [MemoryPackOrder(4)] public int NoiseOffset;

    public static CaveResourceRuleSaveData FromSnapshot(CaveResourceRuleSnapshot snapshot)
    {
        if (snapshot == null)
            return null;
        return new CaveResourceRuleSaveData
        {
            RuleId = snapshot.RuleId,
            ItemId = snapshot.ItemId,
            VeinThreshold = snapshot.VeinThreshold,
            VeinScale = snapshot.VeinScale,
            NoiseOffset = snapshot.NoiseOffset
        };
    }

    public CaveResourceRuleSnapshot ToSnapshot()
    {
        return new CaveResourceRuleSnapshot(RuleId, ItemId, VeinThreshold, VeinScale,
            NoiseOffset);
    }
}

/// <summary>生态规则的 JSON/MemoryPack 存档副本，不引用 ScriptableObject 或 Prefab。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
[Serializable]
public partial class EcologyRuleSaveData
{
    #region 规则字段

    [MemoryPackOrder(0)] public string RuleId;
    [MemoryPackOrder(1)] public string ItemId;
    [MemoryPackOrder(2)] public int ItemCount = 1;
    [MemoryPackOrder(3)] public EcologyItemCountDistribution ItemCountDistribution;
    [MemoryPackOrder(4)] public int ItemCountMin = 1;
    [MemoryPackOrder(5)] public double SpawnChance;
    [MemoryPackOrder(6)] public double SpawnChanceMultiplier = 1d;
    [MemoryPackOrder(7)] public EcologyDistributionMode DistributionMode;
    [MemoryPackOrder(8)] public int PatchSpacing = 24;
    [MemoryPackOrder(9)] public double PatchRadius = 2.5d;
    [MemoryPackOrder(10)] public double PatchChance = 1d;
    [MemoryPackOrder(11)] public int BiomeMask;
    [MemoryPackOrder(12)] public double MinTemperature;
    [MemoryPackOrder(13)] public double MaxTemperature = 1d;
    [MemoryPackOrder(14)] public double MinPrecipitation;
    [MemoryPackOrder(15)] public double MaxPrecipitation = 1d;
    [MemoryPackOrder(16)] public double MinHeight;
    [MemoryPackOrder(17)] public double MaxHeight = 1d;
    [MemoryPackOrder(18)] public List<string> ProvidedTags = new();
    [MemoryPackOrder(19)] public bool CompanionOnly;
    [MemoryPackOrder(20)] public string CompanionHostTag;
    [MemoryPackOrder(21)] public double CompanionSpawnChance;
    [MemoryPackOrder(22)] public double CompanionOffsetX;
    [MemoryPackOrder(23)] public double CompanionOffsetY;
    [MemoryPackOrder(24)] public double CompanionMinRadius;
    [MemoryPackOrder(25)] public double CompanionMaxRadius;
    // 当前规则的河流泛滥平原限制。
    [MemoryPackOrder(26)] public double MinRiverFloodplainStrength;
    // 伴生物所需的自然物标签及区块搜索半径。
    [MemoryPackOrder(27)] public string RequiredChunkTag;
    [MemoryPackOrder(28)] public int RequiredTagChunkRadius;
    // 缺省与显式 0 必须分开，避免 MemoryPack 将未记录的上限读成禁止河岸。
    [MemoryPackOrder(29)] public double? MaxRiverFloodplainStrength;
    [MemoryPackOrder(30)] public string RequiredEnvironmentLayer;
    [MemoryPackOrder(31)] public double MinimumEnvironmentValue;
    [MemoryPackOrder(32)] public double? MaximumEnvironmentValue;
    [MemoryPackOrder(33)] public bool RequireNaturalPlantableGround;
    [MemoryPackOrder(34)] public int SharedRadius;
    [MemoryPackOrder(35)] public int FootprintWidth = 1;
    [MemoryPackOrder(36)] public int FootprintHeight = 1;

    #endregion

    #region 快照转换

    /// <summary>从后台规则快照创建可持久化副本。</summary>
    public static EcologyRuleSaveData FromSnapshot(EcologySpawnRuleSnapshot snapshot)
    {
        var data = new EcologyRuleSaveData
        {
            RuleId = snapshot.RuleId,
            ItemId = snapshot.ItemId,
            ItemCount = snapshot.ItemCount,
            ItemCountDistribution = snapshot.ItemCountDistribution,
            ItemCountMin = snapshot.ItemCountMin,
            SpawnChance = snapshot.SpawnChance,
            SpawnChanceMultiplier = snapshot.SpawnChanceMultiplier,
            DistributionMode = snapshot.DistributionMode,
            PatchSpacing = snapshot.PatchSpacing,
            PatchRadius = snapshot.PatchRadius,
            PatchChance = snapshot.PatchChance,
            BiomeMask = snapshot.BiomeMask,
            MinTemperature = snapshot.MinTemperature,
            MaxTemperature = snapshot.MaxTemperature,
            MinPrecipitation = snapshot.MinPrecipitation,
            MaxPrecipitation = snapshot.MaxPrecipitation,
            MinHeight = snapshot.MinHeight,
            MaxHeight = snapshot.MaxHeight,
            MinRiverFloodplainStrength = snapshot.MinRiverFloodplainStrength,
            MaxRiverFloodplainStrength = snapshot.MaxRiverFloodplainStrength,
            RequiredEnvironmentLayer = snapshot.RequiredEnvironmentLayer,
            MinimumEnvironmentValue = snapshot.MinimumEnvironmentValue,
            MaximumEnvironmentValue = snapshot.MaximumEnvironmentValue,
            RequireNaturalPlantableGround = snapshot.RequireNaturalPlantableGround,
            SharedRadius = snapshot.SharedRadius,
            FootprintWidth = snapshot.FootprintWidth,
            FootprintHeight = snapshot.FootprintHeight,
            CompanionOnly = snapshot.CompanionOnly,
            CompanionHostTag = snapshot.CompanionHostTag,
            RequiredChunkTag = snapshot.RequiredChunkTag,
            RequiredTagChunkRadius = snapshot.RequiredTagChunkRadius,
            CompanionSpawnChance = snapshot.CompanionSpawnChance,
            CompanionOffsetX = snapshot.CompanionOffsetX,
            CompanionOffsetY = snapshot.CompanionOffsetY,
            CompanionMinRadius = snapshot.CompanionMinRadius,
            CompanionMaxRadius = snapshot.CompanionMaxRadius,
            ProvidedTags = new List<string>()
        };
        for (int i = 0; i < snapshot.ProvidedTags.Count; i++)
            data.ProvidedTags.Add(snapshot.ProvidedTags[i]);
        return data;
    }

    /// <summary>恢复后台线程使用的规则快照。</summary>
    public EcologySpawnRuleSnapshot ToSnapshot()
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
            RequiredChunkTag,
            CompanionSpawnChance,
            CompanionOffsetX,
            CompanionOffsetY,
            CompanionMinRadius,
            CompanionMaxRadius,
            MinRiverFloodplainStrength,
            DistributionMode,
            PatchSpacing,
            PatchRadius,
            PatchChance,
            RequiredTagChunkRadius,
            MaxRiverFloodplainStrength ?? 1d,
            RequiredEnvironmentLayer,
            MinimumEnvironmentValue,
            ItemCountDistribution,
            ItemCountMin,
            MaximumEnvironmentValue ?? double.MaxValue,
            RequireNaturalPlantableGround, SharedRadius, FootprintWidth, FootprintHeight);
    }

    #endregion
}

/// <summary>单个区块的自然物差量，只保存删除 GUID 和变更后的 ItemData。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
[Serializable]
public partial class EcologyChunkSaveData
{
    #region 差量字段

    [MemoryPackOrder(0)] public int ChunkX;
    [MemoryPackOrder(1)] public int ChunkY;
    [MemoryPackOrder(2)] public List<int> RemovedGuids = new();
    [MemoryPackIgnore] public List<ItemData> ChangedItems = new();
    [MemoryPackInclude, MemoryPackOrder(3)] private List<byte[]> ChangedItemSnapshots
    {
        get
        {
            if (ChangedItems == null) return null;
            var snapshots = new List<byte[]>(ChangedItems.Count);
            foreach (ItemData item in ChangedItems)
                snapshots.Add(item == null ? null : ItemSnapshotSerialization.SerializePayload(ItemSnapshotSerialization.Capture(item)));
            return snapshots;
        }
        set
        {
            if (value == null) { ChangedItems = null; return; }
            ChangedItems = new List<ItemData>(value.Count);
            foreach (byte[] bytes in value)
                ChangedItems.Add(bytes == null ? null : MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(bytes).CreateColdData());
        }
    }
    [MemoryPackOrder(4)] public Dictionary<int, int> RenewalYears = new(); // 删除的自然资源最早恢复年份。

    #endregion

    #region 差量操作

    /// <summary>判断 GUID 是否在删除列表中。</summary>
    public bool IsRemoved(int guid)
    {
        return RemovedGuids != null && RemovedGuids.Contains(guid);
    }

    /// <summary>读取指定 GUID 的状态覆盖。</summary>
    public bool TryGetChangedItem(int guid, out ItemData itemData)
    {
        itemData = null;
        if (ChangedItems == null)
            return false;

        for (int i = 0; i < ChangedItems.Count; i++)
        {
            ItemData candidate = ChangedItems[i];
            if (candidate != null && candidate.Guid == guid)
            {
                itemData = candidate;
                return true;
            }
        }
        return false;
    }

    /// <summary>加入删除列表，并移除同 GUID 的状态覆盖。</summary>
    public void MarkRemoved(int guid)
    {
        RemovedGuids ??= new List<int>();
        if (!RemovedGuids.Contains(guid))
            RemovedGuids.Add(guid);

        RemoveChangedItem(guid);
    }

    /// <summary>加入或替换一条状态覆盖，并确保它不在删除列表中。</summary>
    public void CaptureChangedItem(ItemData itemData)
    {
        if (itemData == null || itemData.Guid == 0)
            return;

        RemovedGuids?.Remove(itemData.Guid);
        ChangedItems ??= new List<ItemData>();
        for (int i = 0; i < ChangedItems.Count; i++)
        {
            if (ChangedItems[i] != null && ChangedItems[i].Guid == itemData.Guid)
            {
                ChangedItems[i] = itemData;
                return;
            }
        }
        ChangedItems.Add(itemData);
    }

    /// <summary>一次建立 GUID 索引并依原顺序覆盖快照，避免每个自然物重新扫描整份区块差量。</summary>
    public void CaptureChangedItems(IReadOnlyList<ItemData> items)
    {
        if (items == null || items.Count == 0) return;
        int firstValid = 0;
        while (firstValid < items.Count && (items[firstValid] == null || items[firstValid].Guid == 0))
            firstValid++;
        if (firstValid == items.Count) return;
        ChangedItems ??= new List<ItemData>();
        var indices = new Dictionary<int, int>(ChangedItems.Count);
        for (int i = 0; i < ChangedItems.Count; i++)
        {
            ItemData existing = ChangedItems[i];
            // 和单条写入一样，只替换列表里同 GUID 的第一条，不调整原有顺序。
            if (existing != null && !indices.ContainsKey(existing.Guid)) indices.Add(existing.Guid, i);
        }
        for (int i = firstValid; i < items.Count; i++)
        {
            ItemData item = items[i];
            if (item == null || item.Guid == 0) continue;
            RemovedGuids?.Remove(item.Guid);
            if (indices.TryGetValue(item.Guid, out int index)) ChangedItems[index] = item;
            else
            {
                indices.Add(item.Guid, ChangedItems.Count);
                ChangedItems.Add(item);
            }
        }
    }

    private void RemoveChangedItem(int guid)
    {
        if (ChangedItems == null)
            return;
        for (int i = ChangedItems.Count - 1; i >= 0; i--)
        {
            if (ChangedItems[i] == null || ChangedItems[i].Guid == guid)
                ChangedItems.RemoveAt(i);
        }
    }

    #endregion
}
