using MemoryPack;
using System;
using System.Collections.Generic;

/// <summary>
/// 食物模块的持久化载体。这里只保存基础食物数据和各观察者的通用状态负载，
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

    /// <summary>确保食物配置对象存在。</summary>
    public Food EnsureFoodData()
    {
        FoodData ??= new Food();
        return FoodData;
    }

    #endregion
}
