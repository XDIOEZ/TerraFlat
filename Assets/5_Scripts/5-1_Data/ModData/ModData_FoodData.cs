using MemoryPack;
using System;
using System.Collections.Generic;

/// <summary>
/// 食物模块的持久化载体。这里只保存基础食物数据和各观察者的状态，
/// 不持有库存、槽位、物品工厂或任何运行时游戏对象引用。
/// </summary>
[Serializable]
[MemoryPackable]
public partial class ModData_FoodData : ModuleData
{
    #region 持久化字段

    /// <summary>食物的基础营养和静态面板数据。</summary>
    public Food FoodData = new Food();

    /// <summary>观察者键到自身数据负载的通用容器，具体字段由观察者自行定义。</summary>
    public List<FoodMechanicStateData> MechanicStates = new List<FoodMechanicStateData>();

    #endregion

    #region 数据访问

    public const string SpoilageStateKey = "food.spoilage";
    public const string MeltingStateKey = "food.melting";

    /// <summary>确保食物配置对象存在。</summary>
    public Food EnsureFoodData()
    {
        FoodData ??= new Food();
        return FoodData;
    }

    public FoodMechanicStateData FindSpoilageState() => FindTimerState(SpoilageStateKey);
    public FoodMechanicStateData FindMeltingState() => FindTimerState(MeltingStateKey);

    // 保留观察者列表的实时结构，计时字段本身使用强类型数据。
    private FoodMechanicStateData FindTimerState(string key)
    {
        List<FoodMechanicStateData> states = MechanicStates;
        if (states != null)
            for (int i = 0; i < states.Count; i++)
                if (states[i] != null && string.Equals(states[i].StateKey, key, StringComparison.Ordinal))
                    return states[i];
        return null;
    }

    #endregion
}
