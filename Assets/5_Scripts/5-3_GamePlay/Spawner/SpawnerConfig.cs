using UnityEngine;
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;

public enum SpawnerScheduleMode
{
    TimedWindows = 0,
    DayMilestoneGrowth = 1
}

/// <summary>
/// 怪物生成系统配置类 - 包含所有生成相关的常数和配置
/// </summary>
[CreateAssetMenu(fileName = "SpawnerConfig", menuName = "FlatWorld/SpawnerConfig")]
public class SpawnerConfig : ScriptableObject
{
#region 嵌套类型

    /// <summary>
    /// 单个怪物生成项配置
    /// </summary>
    [Serializable]
    public class SpawnEntry
    {
        [LabelText("怪物预制体名称")]
        [Tooltip("生成时使用的预制体/物品标识，需与项目里的名称完全一致")]
        public string PrefabName = "Chicken"; // 生成的怪物预制体名称，需与物品/预制体标识一致

        [LabelText("生成概率")]
        [Tooltip("该怪物在随机抽取中的权重，建议所有条目总和不超过 1")]
        [Range(0f, 1f)]
        public float Probability = 0.5f; // 该怪物的生成概率，建议所有项总和不超过 1
    }

#endregion

#region 生成概率配置

    /// <summary>
    /// 怪物生成权重列表
    /// 可在检查器中直接增删条目、修改怪物名称和概率
    /// </summary>
    [LabelText("怪物生成列表")]
    [TableList(AlwaysExpanded = true, ShowIndexLabels = true)]
    [Tooltip("可直接在检查器中添加、删除和调整每种怪物的生成权重")]
    public List<SpawnEntry> SpawnEntries = new List<SpawnEntry>
    {
        new SpawnEntry { PrefabName = "Chicken", Probability = 0.5f },
        new SpawnEntry { PrefabName = "WildBoar", Probability = 0.2f }
    }; // 生成表（支持在检查器动态配置）

#endregion

#region 时间配置

    [LabelText("持久化标识")]
    [Tooltip("用于存档中区分不同生成配置。留空时使用资源名称。")]
    public string PersistentId;

    [LabelText("调度模式")]
    public SpawnerScheduleMode ScheduleMode = SpawnerScheduleMode.TimedWindows;

    [LabelText("仅在全局无光时生成")]
    [Tooltip("启用后，只在昼夜系统的全局光照为0时处理生成。")]
    public bool RequireGlobalDarkness;

    /// <summary>
    /// 生成触发时间 - 白天12点（游戏秒数）
    /// 一天时长1440秒，12点对应 12/24 * 1440 = 720秒
    /// </summary>
    [LabelText("生成触发时间")]
    [Tooltip("一天中触发怪物生成的时间点，单位为游戏秒，默认是中午12点")]
    public float SpawnTriggerTime = 720f; // 每天触发生成的时间点（游戏秒）

    /// <summary>
    /// 生成检查的时间容差范围（秒）
    /// 用于防止浮点精度导致的重复触发或遗漏
    /// </summary>
    [LabelText("时间容差")]
    [Tooltip("允许的触发误差范围，避免浮点误差导致漏刷或重复刷怪")]
    public float SpawnTimeTolerance = 1f; // 触发窗口容差

    [LabelText("每日生成次数")]
    [Tooltip("每天均匀分布多少个生成窗口。1 表示每天一次，2 表示刷新频率翻倍")]
    [MinValue(1)]
    public int SpawnsPerDay = 1;

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

    [LabelText("总体生成概率")]
    [Tooltip("每天触发时间点发生生成的总体概率（0-1），例如0.8表示80%概率生成")]
    [Range(0f, 1f)]
    public float SpawnChance = 1f; // 触发当天实际生成的概率

    [LabelText("生成间隔天数")]
    [Tooltip("两次成功生成之间至少间隔多少个游戏日")]
    public int DaysBetweenSpawns = 1; // 两次成功生成之间的最小间隔，单位：游戏日

    [ShowIf(nameof(ScheduleMode), SpawnerScheduleMode.DayMilestoneGrowth)]
    [LabelText("每多少天增加一次")]
    [MinValue(1)]
    public int GrowthIntervalDays = 3;

    [ShowIf(nameof(ScheduleMode), SpawnerScheduleMode.DayMilestoneGrowth)]
    [LabelText("累计生成上限")]
    [MinValue(1)]
    public int MaxLifetimeSpawnCount = 64;

    [ShowIf(nameof(ScheduleMode), SpawnerScheduleMode.DayMilestoneGrowth)]
    [LabelText("分帧生成间隔")]
    [MinValue(0f)]
    public float AsyncSpawnInterval;

    [LabelText("必须生成在完全黑暗地块")]
    public bool RequireCompletelyDarkTile = true;

    #endregion



#region 辅助方法

    /// <summary>
    /// 根据随机值判断生成的怪物类型
    /// </summary>
    /// <param name="randomValue">0-1 之间的随机值</param>
    /// <returns>生成的怪物类型（如果都不符合返回 null）</returns>
    public string DetermineSpawnType(float randomValue)
    {
        float cumulative = 0f;

        for (int i = 0; i < SpawnEntries.Count; i++)
        {
            SpawnEntry entry = SpawnEntries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.PrefabName) || entry.Probability <= 0f)
            {
                continue;
            }

            cumulative += entry.Probability;
            if (randomValue < cumulative)
            {
                return entry.PrefabName;
            }
        }

        // 其他情况不生成
        return null;
    }

#endregion
}
