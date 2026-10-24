using UnityEngine;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Sirenix.OdinInspector;

public enum SpawnerEcologyGroup
{
    Animals = 0,
    CommonEnemies = 1,
    NightEnemies = 2
}

/// <summary>树木生态扩展配置；默认关闭，不改变其他物种的生成和后端。</summary>
[MemoryPack.MemoryPackable]
[JsonObject(ItemRequired = Required.Always)]
[Serializable]
public sealed partial class SpawnerTreeHabitat
{
    public bool Enabled;
    public int TreesPerActor = 4;
    public float SpawnRadius = 3f;
}

#region 出生批次数量定义

/// <summary>同一轮出生数量的相对权重，未配置时沿用固定数量。</summary>
[MemoryPack.MemoryPackable]
[JsonObject(ItemRequired = Required.Always)]
[Serializable]
public sealed partial class SpawnerBatchSizeWeight
{
    public int Count;
    public float Weight;
}

#endregion

/// <summary>
/// 生成规则的世界运行时快照；只由已校验的 JSON 定义创建，不作为编辑器资源维护。
/// </summary>
public partial class SpawnerConfig : ScriptableObject
{
    public SpawnerTreeHabitat TreeHabitat = new();
    public bool WaterOnly;
#region 嵌套类型

    /// <summary>
    /// 单个怪物生成项配置
    /// </summary>
    [Serializable]
    public class SpawnEntry
    {
        [LabelText("怪物预制体名称")]
        [Tooltip("生成时使用的预制体/物品标识，需与项目里的名称完全一致")]
        public string PrefabName; // 生成的怪物预制体名称，需与物品/预制体标识一致

        [LabelText("AI 运行后端")]
        [Tooltip("普通动物使用 GameObject；尸潮、虫灾等超大规模单位显式选择 Entities。")]
        public AiRuntimeBackendKind RuntimeBackend = AiRuntimeBackendKind.GameObject;

        [LabelText("生成权重")]
        [Tooltip("参与归一化抽取的相对权重，不要求总和为 1")]
        [MinValue(0f)]
        public float Probability = 0.5f;

        [LabelText("独立生成概率"), Range(0f, 1f)]
        [Tooltip("抽中该物种且地点有效后再判定；未通过会消耗本次机会，不重抽或替换成其它物种。")]
        public float SpawnChance = 1f;

        /// <summary>独立概率不参与物种权重归一化，默认全通过且无需消耗额外随机数。</summary>
        public bool PassesSpawnChance(float sample)
            => SpawnChance >= 1f || (SpawnChance > 0f && sample >= 0f && sample < SpawnChance);

        [LabelText("生态成本")]
        [Tooltip("每生成一只该生物消耗的生态预算")]
        [MinValue(1)]
        public int EcologyCost = 1;

        [LabelText("物种存活上限")]
        [Tooltip("当前世界中该物种允许存活的最大数量；0 表示仅受生成组上限控制")]
        [MinValue(0)]
        public int SpeciesAliveLimit;

        [LabelText("出生初始化")]
        public SpawnerSpawnInitialization Initialization = new();
    }

    [MemoryPack.MemoryPackable]
    [JsonObject(ItemRequired = Required.Always)]
    [Serializable]
    public partial class SpawnerSpawnInitialization
    {
        [LabelText("营养初始化")]
        public SpawnerNutritionInitialization Nutrition = new();
    }

    [MemoryPack.MemoryPackable]
    [JsonObject(ItemRequired = Required.Always)]
    [Serializable]
    public partial class SpawnerNutritionInitialization
    {
        [LabelText("启用")]
        public bool Enabled;

        [LabelText("初始饱食度最小值")]
        [Range(0f, 1f)]
        public float MinFoodRate = 1f;

        [LabelText("初始饱食度最大值")]
        [Range(0f, 1f)]
        public float MaxFoodRate = 1f;
    }

#endregion

#region 生成概率配置

    /// <summary>
    /// 怪物生成权重列表，由 JSON 规则在世界进入时构建。
    /// </summary>
    [LabelText("怪物生成列表")]
    [TableList(AlwaysExpanded = true, ShowIndexLabels = true)]
    [Tooltip("通过独立的生物生成 JSON 配置每种怪物的生成权重")]
    public List<SpawnEntry> SpawnEntries = new();

#endregion

#region 规则身份与光照条件

    [LabelText("持久化标识")]
    [Tooltip("用于存档中区分不同生成配置。留空时使用资源名称。")]
    public string PersistentId;

    [LabelText("生态生成组")]
    public SpawnerEcologyGroup EcologyGroup = SpawnerEcologyGroup.Animals;

    [LabelText("仅在全局无光时生成")]
    [Tooltip("启用后，只在昼夜系统的全局光照为0时处理生成。")]
    public bool RequireGlobalDarkness;

#endregion

#region 视野和距离配置

    /// <summary>
    /// 最小生成距离 - 距玩家多近不生成（米）
    /// 防止在玩家附近直接刷怪
    /// </summary>
    [LabelText("最小生成距离")]
    [Tooltip("距离玩家太近时不允许生成，防止怪物贴脸刷出")]
    public float MinSpawnDistance = 15f; // 最小生成距离

    /// <summary>
    /// 最大生成距离 - 距玩家多远范围内生成（米）
    /// </summary>
    [LabelText("最大生成距离")]
    [Tooltip("怪物最多会在玩家周围这个距离内生成")]
    public float MaxSpawnDistance = 50f; // 最大生成距离

    [LabelText("玩家可视排斥距离")]
    [Tooltip("候选点距离任意玩家小于该值时不允许生成")]
    [MinValue(0f)]
    public float PlayerVisibilityExclusionDistance = 18f;

    [LabelText("玩家周边统计半径")]
    [Tooltip("计算玩家周边种群上限时使用的半径")]
    [MinValue(1f)]
    public float PlayerPopulationRadius = 60f;

    [LabelText("每位玩家周边上限")]
    [Tooltip("任意玩家统计半径内属于该生成组的生物上限；0 表示不单独限制")]
    [MinValue(0)]
    public int PerPlayerAliveLimit;

    /// <summary>
    /// 生成位置搜索重试次数
    /// 如果找不到有效生成位置，最多重试多少次
    /// </summary>
    [LabelText("搜索重试次数")]
    [Tooltip("在有效区块中寻找生成位置时的最大重试次数")]
    public int SpawnSearchRetryCount = 5; // 生成位置搜索重试次数

    #endregion

    #region 额外配置

    [LabelText("生成数量")]
    [Tooltip("每次触发生成时生成的怪物数量")]
    public int SpawnCount = 1; // 每次触发生成的怪物数量，默认1

    [LabelText("批次数量权重")]
    public List<SpawnerBatchSizeWeight> SpawnCountWeights = new();

    [LabelText("同批出生半径"), MinValue(0f)]
    [Tooltip("大于零时整批围绕首个成功出生点生成，零表示独立选点。")]
    public float BatchSpawnRadius;

    [LabelText("总体生成概率")]
    [Tooltip("统一倒计时抽中本规则后的一次概率；失败消费本轮机会，不重新抽取物种")]
    [Range(0f, 1f)]
    public float SpawnChance = 1f; // 抽中规则后实际生成的概率

    [LabelText("生成组存活上限")]
    [Tooltip("当前世界中该配置生成组允许存活的最大数量")]
    [MinValue(1)]
    public int GroupAliveLimit = 12;

    [LabelText("基础生态预算")]
    [Tooltip("进入世界时及首次迁移时使用的组预算上限")]
    [MinValue(1)]
    public int MaxEcologyBudget = 12;

    [LabelText("每日预算恢复")]
    [Tooltip("跨过一个游戏日时恢复的生态预算")]
    [MinValue(0)]
    public int DailyBudgetRecovery = 4;

    [LabelText("分帧生成间隔")]
    [MinValue(0f)]
    public float AsyncSpawnInterval;

    [LabelText("忽略生态预算")]
    [Tooltip("仅免除生态预算消耗，仍参与世界统一倒计时；种群上限由独立开关控制。")]
    public bool IgnoreEcologyBudget;

    [LabelText("忽略种群数量上限")]
    [Tooltip("启用后不再用物种、生成组、玩家周边或全局数量限制生成和回收；仍保留生成计划、概率、生态预算与远距离回收。")]
    public bool IgnorePopulationLimits;

    [LabelText("必须生成在完全黑暗地块")]
    public bool RequireCompletelyDarkTile = true;

    [LabelText("最大允许地块光照")]
    [Tooltip("候选地块光照高于该值时拒绝生成；1 表示不限制局部光照")]
    [Range(0f, 1f)]
    public float MaxAllowedTileLight = 1f;

    [LabelText("允许的生物群系")]
    [Tooltip("填写 BiomeName 或资源名；为空时允许所有群系")]
    public List<string> AllowedBiomeNames = new();

    [LabelText("允许出生的地表 Tile ID")]
    [Tooltip("为空时不限制；填写后只允许在这些权威地表 Tile 上生成。")]
    public List<int> AllowedGroundTileIds = new();

    [LabelText("远距离休眠距离")]
    [Tooltip("生物距离全部玩家均超过该值时序列化休眠；0 表示保持装载")]
    [MinValue(0f)]
    public float RecycleDistance = 110f;

    [LabelText("远距离休眠宽限")]
    [Tooltip("持续远离全部玩家达到该真实秒数后才休眠")]
    [MinValue(0f)]
    public float RecycleGraceSeconds = 20f;

    #endregion

#region 辅助方法

    // 显式数量权重描述实际批次大小，难度只改变这类批次的到期频率。
    public int DetermineSpawnCount(float sample)
    {
        if (SpawnCountWeights == null || SpawnCountWeights.Count == 0)
            return SpawnCount;

        double totalWeight = 0d;
        foreach (SpawnerBatchSizeWeight option in SpawnCountWeights)
            totalWeight += option.Weight;
        double target = Mathf.Clamp01(sample) * totalWeight;
        double cumulative = 0d;
        foreach (SpawnerBatchSizeWeight option in SpawnCountWeights)
        {
            cumulative += option.Weight;
            if (target < cumulative)
                return option.Count;
        }
        return SpawnCountWeights[SpawnCountWeights.Count - 1].Count;
    }

    /// <summary>
    /// 根据随机值判断生成的怪物类型
    /// </summary>
    /// <param name="randomValue">0-1 之间的随机值</param>
    /// <returns>生成的怪物类型（如果都不符合返回 null）</returns>
    public string DetermineSpawnType(float randomValue)
    {
        SpawnEntry entry = DetermineSpawnEntry(randomValue);
        return entry?.PrefabName;
    }

    /// <summary>
    /// 按有效条目的相对权重归一化抽取。
    /// </summary>
    public SpawnEntry DetermineSpawnEntry(float randomValue)
    {
        if (SpawnEntries == null || SpawnEntries.Count == 0)
            return null;

        float totalWeight = 0f;
        for (int i = 0; i < SpawnEntries.Count; i++)
        {
            SpawnEntry entry = SpawnEntries[i];
            if (entry != null && !string.IsNullOrWhiteSpace(entry.PrefabName) && entry.Probability > 0f)
                totalWeight += entry.Probability;
        }

        if (totalWeight <= 0f)
            return null;

        float targetWeight = Mathf.Clamp01(randomValue) * totalWeight;
        float cumulative = 0f;
        SpawnEntry lastValidEntry = null;

        for (int i = 0; i < SpawnEntries.Count; i++)
        {
            SpawnEntry entry = SpawnEntries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.PrefabName) || entry.Probability <= 0f)
            {
                continue;
            }

            lastValidEntry = entry;
            cumulative += entry.Probability;
            if (targetWeight < cumulative)
            {
                return entry;
            }
        }

        return lastValidEntry;
    }

#endregion
}
