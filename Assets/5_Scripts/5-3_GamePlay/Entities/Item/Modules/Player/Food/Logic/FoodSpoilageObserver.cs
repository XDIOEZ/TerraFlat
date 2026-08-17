using System;
using UnityEngine;

/// <summary>食物腐败观察者自己的配置与运行时状态，不进入 ModData_FoodData 的具体字段。</summary>
[Serializable]
public sealed class FoodSpoilageObserverData
{
    public bool EnableSpoilage = true;
    public float SpoilageElapsedSeconds;
    public float SpoilageIntervalSeconds = 1800f;
    public string SpoilageTargetItemID = "Meat_Rotten";

    /// <summary>从通用观察者负载读取腐败数据。</summary>
    public static FoodSpoilageObserverData Load(ModData_FoodData persistentData)
    {
        FoodMechanicStateData state = FoodObserverStateStore.Find(
            persistentData,
            FoodObserverStateStore.SpoilageStateKey);
        return new FoodSpoilageObserverData
        {
            EnableSpoilage = FoodObserverStateStore.ReadBool(state, "EnableSpoilage", true),
            SpoilageElapsedSeconds = FoodObserverStateStore.ReadFloat(state, "SpoilageElapsedSeconds", 0f),
            SpoilageIntervalSeconds = FoodObserverStateStore.ReadFloat(state, "SpoilageIntervalSeconds", 1800f),
            SpoilageTargetItemID = FoodObserverStateStore.ReadString(
                state,
                "SpoilageTargetItemID",
                "Meat_Rotten")
        };
    }

    /// <summary>把腐败数据写回观察者自己的通用负载。</summary>
    public void Save(ModData_FoodData persistentData)
    {
        FoodMechanicStateData state = FoodObserverStateStore.GetOrCreate(
            persistentData,
            FoodObserverStateStore.SpoilageStateKey);
        FoodObserverStateStore.WriteBool(state, "EnableSpoilage", EnableSpoilage);
        FoodObserverStateStore.WriteFloat(state, "SpoilageElapsedSeconds", SpoilageElapsedSeconds);
        FoodObserverStateStore.WriteFloat(state, "SpoilageIntervalSeconds", SpoilageIntervalSeconds);
        FoodObserverStateStore.WriteString(state, "SpoilageTargetItemID", SpoilageTargetItemID);
    }
}

/// <summary>
/// 监听库存中的食物数据 Tick，负责腐败计时和到期后的原槽位状态替换。
/// 观察者只处理规则判断，实际库存写入交给 IFoodItemOperationGateway 的默认实现。
/// </summary>
public sealed class FoodSpoilageModuleDataObserver : IModuleDataTickObserver
{
    /// <summary>只接收食物模块数据，避免观察者处理其他库存模块。</summary>
    public bool CanObserve(ModuleData moduleData)
    {
        return moduleData is ModData_FoodData;
    }

    /// <summary>每次库存 Tick 推进计时，到期后请求网关替换当前槽位物品。</summary>
    public void OnModuleDataTick(ModuleDataTickContext context)
    {
        // 没有开启腐败，或上下文不是有效的食物库存槽位时，不做任何处理。
        if (!(context.ModuleData is ModData_FoodData foodData))
            return;

        FoodSpoilageObserverData observerData = FoodSpoilageObserverData.Load(foodData);
        if (!observerData.EnableSpoilage)
            return;

        if (context.ItemData == null || context.Slot == null ||
            !ReferenceEquals(context.Slot.itemData, context.ItemData))
            return;

        // 配置异常时停止计时，避免无效数据导致物品立即替换或计时溢出。
        float intervalSeconds = observerData.SpoilageIntervalSeconds;
        if (intervalSeconds <= 0f || float.IsNaN(intervalSeconds) || float.IsInfinity(intervalSeconds))
        {
            Debug.LogWarning($"[FoodSpoilage] 腐败间隔无效，物品={context.ItemData.IDName}");
            return;
        }

        string targetItemID = observerData.SpoilageTargetItemID?.Trim();
        if (string.IsNullOrWhiteSpace(targetItemID))
        {
            Debug.LogWarning($"[FoodSpoilage] 腐败目标为空，物品={context.ItemData.IDName}");
            observerData.SpoilageElapsedSeconds = 0f;
            observerData.Save(foodData);
            return;
        }

        // 物品已经是目标状态时清空计时，防止重复触发替换。
        if (string.Equals(context.ItemData.IDName, targetItemID, System.StringComparison.OrdinalIgnoreCase))
        {
            observerData.SpoilageElapsedSeconds = 0f;
            observerData.Save(foodData);
            return;
        }

        // 使用游戏 Tick 累加运行时间，不能用现实时间直接替代。
        observerData.SpoilageElapsedSeconds += Mathf.Max(0f, context.DeltaTime);
        if (observerData.SpoilageElapsedSeconds < intervalSeconds)
        {
            observerData.Save(foodData);
            return;
        }

        observerData.SpoilageElapsedSeconds = 0f;
        observerData.Save(foodData);
        // 由库存操作网关负责保留槽位、数量、GUID 和 UI 等运行时状态。
        if (!InventoryFoodItemOperationGateway.TryReplaceSlot(
            context.InventoryData,
            context.Slot,
            context.SlotIndex,
            targetItemID,
            out string reason))
        {
            Debug.LogWarning($"[FoodSpoilage] 状态替换失败，物品={context.ItemData.IDName}，原因={reason}");
            return;
        }

        Debug.Log($"[FoodSpoilage] 状态替换完成，原物品={context.ItemData.IDName}，目标物品={targetItemID}");
    }
}
