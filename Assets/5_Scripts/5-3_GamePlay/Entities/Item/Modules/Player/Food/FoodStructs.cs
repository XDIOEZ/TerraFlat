using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>一次完整食用的结果。</summary>
public readonly struct FoodConsumeResult
{
    public FoodConsumeResult(
        Item consumer,
        Item consumedItem,
        FoodConsumeKind kind,
        float consumedWater,
        float actualWaterGain,
        FoodConsumeSnapshot snapshot = default)
    {
        Consumer = consumer;
        ConsumedItem = consumedItem;
        Kind = kind;
        ConsumedWater = consumedWater;
        ActualWaterGain = actualWaterGain;
        Snapshot = snapshot;
    }

    public Item Consumer { get; }
    public Item ConsumedItem { get; }
    public FoodConsumeKind Kind { get; }
    public float ConsumedWater { get; }
    public float ActualWaterGain { get; }
    public FoodConsumeSnapshot Snapshot { get; }
    public string ConsumedItemId => Snapshot.ItemId;
    public float SpoilageRemainingSeconds => Snapshot.SpoilageRemainingSeconds;
    public float SpoilageIntervalSeconds => Snapshot.SpoilageIntervalSeconds;
    public bool IsDrink => Kind == FoodConsumeKind.Drink;
}

/// <summary>在库存扣除或替换前冻结被吃物品的身份与腐败计时。</summary>
public readonly struct FoodConsumeSnapshot
{
    public FoodConsumeSnapshot(string itemId, float spoilageRemainingSeconds, float spoilageIntervalSeconds)
    {
        ItemId = itemId ?? string.Empty;
        SpoilageRemainingSeconds = spoilageRemainingSeconds;
        SpoilageIntervalSeconds = spoilageIntervalSeconds;
    }

    public string ItemId { get; }
    public float SpoilageRemainingSeconds { get; }
    public float SpoilageIntervalSeconds { get; }

    #region 快照捕获

    public static FoodConsumeSnapshot Capture(IFoodRuntimeContext food)
    {
        string itemId = food?.ItemData?.IDName ?? string.Empty;
        ModData_FoodData persistentData = food?.PersistentData;
        if (persistentData?.FindSpoilageState() == null)
            return new FoodConsumeSnapshot(itemId, 0f, 0f);

        FoodSpoilageObserverData spoilage = FoodSpoilageObserverData.Load(persistentData);
        if (!spoilage.EnableSpoilage || !spoilage.HasValidInterval ||
            !float.IsFinite(spoilage.SpoilageElapsedSeconds))
        {
            return new FoodConsumeSnapshot(itemId, 0f, 0f);
        }

        return new FoodConsumeSnapshot(itemId, spoilage.RemainingSeconds, spoilage.SpoilageIntervalSeconds);
    }

    #endregion
}

/// <summary>一次食用行为的上下文。</summary>
public readonly struct FoodUseContext
{
    public FoodUseContext(IFoodRuntimeContext food, IFoodRuntimeContext consumer)
    {
        Food = food;
        Consumer = consumer;
    }

    public IFoodRuntimeContext Food { get; }
    public IFoodRuntimeContext Consumer { get; }
}

/// <summary>食物 Tick 的上下文。</summary>
public readonly struct FoodTickContext
{
    public FoodTickContext(IFoodRuntimeContext food, float deltaTime)
    {
        Food = food;
        DeltaTime = deltaTime;
    }

    public IFoodRuntimeContext Food { get; }
    public float DeltaTime { get; }
}

/// <summary>食物状态刷新上下文。</summary>
public readonly struct FoodStateChangedContext
{
    public FoodStateChangedContext(IFoodRuntimeContext food)
    {
        Food = food;
    }

    public IFoodRuntimeContext Food { get; }
}

/// <summary>
/// 食物生命模块的数据。它只保存配置，具体回血和营养不足伤害由 FoodHealthModule 执行。
/// </summary>
public partial class Mod_Food
{
    [MemoryPackable]
    [System.Serializable]
    public partial class FoodHealthState
    {
        public bool Enabled = true;
        public float HealSpeed = 0f;

        [Tooltip("大于 0 时按间隔一次性回血；为 0 时使用 HealSpeed 连续回血")]
        public float HealInterval = 0f;

        [Tooltip("离散回血每次恢复的生命值")]
        public float HealAmount = 0f;

        [Tooltip("口渴状态每次扣除的生命值（每 5 秒触发一次）")]
        public float WaterSelfHurt = 1f;

        [Tooltip("蛋白质不足时每秒扣除的生命值")]
        public float ProteinSelfHurt = 1f;

        public float VitaminSelfHurt = 1f;

        [Min(0f)]
        [Tooltip("玩家开始回血所需的最低蛋白质数值；玩家蛋白质必须高于此值")]
        public float PlayerProteinHealThreshold = 0f;

        [Tooltip("非玩家实体使用的蛋白质最低比例")]
        public float HealNeedRatio = 0.6f;
    }

    #region 生命模块数据

    [FoldoutGroup("运行时")]
    [LabelText("生命联动状态")]
    public FoodHealthState HealthState = new FoodHealthState();

    #endregion
}
